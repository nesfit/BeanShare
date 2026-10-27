# BeanShare Production Deployment Guide

## Architecture Overview

BeanShare consists of three services:

| Service | Description | Port |
|---------|-------------|------|
| **BeanShare.BlazorWeb** | Blazor Server web application | 5126 (dev) / 8080 (internal prod) |
| **BeanShare.Api** | REST API (FastEndpoints) | 5247 (dev and internal prod) |
| **PostgreSQL** | Database (v15+) | 5432 |

An external **OpenID Connect provider** is required for authentication. BeanShare works with any standards-compliant OIDC provider — Keycloak, Auth0, Azure AD/Entra ID, Google Identity Platform, Okta, and others.

```
┌─────────────────────────────────────────────────────────────────┐
│                         PRODUCTION                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │  OIDC Provider│    │  PostgreSQL  │    │  BeanShare   │       │
│  │  (Keycloak,  │    │  (Database)  │    │  Blazor Web  │       │
│  │  Auth0, etc.)│    │  Port 5432   │    │  Port 443    │       │
│  └──────────────┘    └──────────────┘    └──────────────┘       │
│         │                   │                   │                │
│         └───────────────────┴───────────────────┘                │
│                         │                                        │
│                  ┌──────────────┐                                │
│                  │   NGINX /    │                                │
│                  │   Traefik    │                                │
│                  └──────────────┘                                │
│                         │                                        │
└─────────────────────────┴────────────────────────────────────────┘
                          │
                    HTTPS (443)
                          │
                      Internet
```

---

## Prerequisites

- Docker & Docker Compose
- .NET 10 SDK (or use containerized build)
- PostgreSQL 15+
- An OIDC provider with admin access to create clients
- Reverse proxy (Nginx, Traefik, or cloud load balancer)
- SSL certificates (Let's Encrypt or commercial)

---

## Step 1: Configure the OIDC Provider

BeanShare requires **three OIDC client registrations** in your identity provider. The exact steps vary by provider, but the requirements are the same.

### Client 1: Web Application (Confidential)

| Setting | Value |
|---------|-------|
| Client ID | `beanshare-web` (configurable) |
| Client Type | Confidential (server-side) |
| Grant Type | Authorization Code |
| Redirect URI | `https://YOUR_WEB_DOMAIN/signin-oidc` |
| Post-Logout URI | `https://YOUR_WEB_DOMAIN/signout-callback-oidc` |
| Scopes | `openid`, `profile`, `email` |

### Client 2: API (Resource Server)

| Setting | Value |
|---------|-------|
| Client ID | `beanshare-api` (configurable) |
| Client Type | Confidential or Bearer-only |
| Purpose | Validates JWT access tokens |

### Client 3: Mobile Application (Public)

| Setting | Value |
|---------|-------|
| Client ID | `beanshare-mobile` (configurable) |
| Client Type | Public (no client secret) |
| Grant Type | Authorization Code with PKCE |
| Redirect URI | `beanshare://callback` |
| Scopes | `openid`, `profile`, `email` |

### Required Claims

BeanShare reads the following claims from ID tokens and access tokens:

| Claim | Purpose |
|-------|---------|
| `sub` | User identifier (required) |
| `email` | User email |
| `email_verified` | Whether the provider verified the email. Matching a login to an existing user or a placeholder by email needs it (see `Authentication__RequireVerifiedEmailForLinking`) |
| `name` | Display name (falls back to `preferred_username`) |
| `preferred_username` | Username |
| `picture` | Avatar URL (optional) |

### Role Claims

BeanShare supports optional role-based access (e.g., system admin). Roles can be provided via any of these formats:

- Standard `role` claim
- `realm_access.roles` JSON array (Keycloak convention)
- Standard `ClaimTypes.Role`

### Provider-Specific Setup

<details>
<summary><b>Keycloak</b></summary>

Deploy Keycloak with a persistent database backend (not the dev H2 mode):

```bash
docker run -d \
  --name keycloak \
  -e KC_DB=postgres \
  -e KC_DB_URL=jdbc:postgresql://DB_HOST:5432/keycloak \
  -e KC_DB_USERNAME=keycloak \
  -e KC_DB_PASSWORD=<KC_DB_PASSWORD> \
  -e KC_HOSTNAME=auth.yourdomain.com \
  -e KEYCLOAK_ADMIN=admin \
  -e KEYCLOAK_ADMIN_PASSWORD=<ADMIN_PASSWORD> \
  -p 8080:8080 \
  quay.io/keycloak/keycloak:23.0 start
```

After Keycloak starts:

1. Open the Admin Console at `https://auth.yourdomain.com/admin`
2. Create a new realm named `beanshare`
3. Import `scripts/keycloak-realm.json` via Realm Settings > Partial Import
4. Update client redirect URIs to match your production domain:
   - Web sign-in callback: `https://beanshare.example.com/signin-oidc`
   - Web sign-out callback: `https://beanshare.example.com/signout-callback-oidc`
   - Web home URL: `https://beanshare.example.com/`
5. Change client secrets for `beanshare-web` and `beanshare-api`
6. Delete demo users imported from the realm JSON (development only)
7. Create a real admin user and assign the `admin` realm role

Authority URL format: `https://your-keycloak.example.com/realms/beanshare`

Keycloak stores roles in `realm_access.roles` as a JSON object — BeanShare parses this automatically.

</details>

<details>
<summary><b>Auth0</b></summary>

1. Create a "Regular Web Application" for the web client
2. Create an "API" for the API audience
3. Create a "Native" application for mobile with PKCE
4. Authority URL: `https://YOUR_TENANT.auth0.com/`
5. Use Auth0 Rules/Actions to add roles to the `role` claim in tokens

</details>

<details>
<summary><b>Azure AD / Entra ID</b></summary>

1. Register three applications in Azure AD
2. Configure redirect URIs for each
3. Authority URL: `https://login.microsoftonline.com/YOUR_TENANT_ID/v2.0`
4. Use Azure AD App Roles assigned to users
5. Set `MapInboundClaims = false` (already configured in BeanShare)

</details>

<details>
<summary><b>External SSO via Keycloak (SAML/OIDC brokering)</b></summary>

Keycloak can act as a broker for external identity providers — useful for integrating with university or corporate SSO systems.

1. In the Keycloak Admin Console, navigate to `Identity Providers > Add provider`
2. Select SAML v2.0 or OpenID Connect v1.0
3. Configure the IdP's metadata URL or endpoint URLs
4. Set up attribute mapping (email, name → Keycloak user fields)
5. Provide your SP metadata to the external IdP administrator:
   - SAML SP descriptor: `https://auth.yourdomain.com/realms/beanshare/protocol/saml/descriptor`
   - OIDC redirect URI: `https://auth.yourdomain.com/realms/beanshare/broker/{alias}/endpoint`

Refer to the [Keycloak Identity Broker documentation](https://www.keycloak.org/docs/latest/server_admin/#_identity_broker) for details.

</details>

---

## Step 2: Configure the Database

```sql
CREATE DATABASE beanshare;
CREATE USER beanshare WITH PASSWORD '<STRONG_PASSWORD>';
GRANT ALL PRIVILEGES ON DATABASE beanshare TO beanshare;
```

The application applies EF Core migrations automatically on first startup. The database user needs privileges to create and alter schema objects.

Connection string format:
```
Host=db.example.com;Port=5432;Database=beanshare;Username=beanshare;Password=your-secure-password;SSL Mode=Require
```

---

## Step 3: Set Environment Variables

Both applications read configuration from environment variables using the ASP.NET Core double-underscore convention (`Oidc__Authority` maps to `Oidc:Authority` in config).
When the web app runs behind nginx, preserve the original host and port so ASP.NET Core generates the correct `redirect_uri` values for `/signin-oidc` and `/signout-callback-oidc`.

### Required Variables

| Variable | Service | Description |
|----------|---------|-------------|
| `ConnectionStrings__DefaultConnection` | API, Web | PostgreSQL connection string |
| `UseOidc` | API, Web | Must be `true` |
| `Oidc__Authority` | API, Web | OIDC provider URL |
| `Oidc__ClientId` | Web | Web client ID (default: `beanshare-web`) |
| `Oidc__ClientSecret` | Web | Web client secret |
| `Oidc__Audience` | API | API audience (default: `beanshare-api`) |
| `QrCodeBaseUrl` | Web | Optional public URL for QR code deep links; defaults to the current web origin |

### Optional Variables

| Variable | Service | Description | Default |
|----------|---------|-------------|---------|
| `Oidc__RegistrationEndpoint` | Web | Optional self-service registration endpoint override | Derived from `Oidc__Authority` |
| `Oidc__IdentityProviderHintParam` | Web | Optional authorize parameter name used to force an upstream IdP | Provider-specific |
| `OpenExchangeRates__AppId` | API | API key for currency conversion | (disabled) |
| `Email__Enabled` | API | Enable email notifications | `false` |
| `Email__SmtpHost` | API | SMTP server | `smtp.gmail.com` |
| `Email__SmtpPort` | API | SMTP port | `587` |
| `Email__SmtpUsername` | API | SMTP username | - |
| `Email__SmtpPassword` | API | SMTP password | - |
| `IncludeDemoData` | API, Web | Seed demo spaces/users/coffee data on startup | `false` |
| `UploadsRootPath` | API, Web | Shared filesystem path for uploaded avatars | OS temp directory |
| `Authentication__RequireVerifiedEmailForLinking` | API, Web | Match a login to an existing account by email only when the provider marks it verified. Placeholders always need a verified email, whatever this says | `true` |
| `TZ` | Web | Time zone of the web service. On a UTC host set `Europe/Prague`, so "today" in the dialogs and every local time shown is Prague time | host time zone |

**Never set `UseMockAuthentication` in production.** It trusts a request header for the user id. The API refuses to start with it outside the Development and Testing environments, unless `AllowMockAuthentication=true` is set as well; that is meant only for a local dry run on a copy of the database (see the cutover runbook).

---

## Step 4: Deploy

### Option A: Docker Compose (Recommended for Self-Hosting)

1. Build the Docker images:

```bash
docker build -f deploy/Dockerfile.api -t beanshare-api .
docker build -f deploy/Dockerfile.web -t beanshare-web .
```

2. Create a `docker-compose.prod.yml`:

```yaml
services:
  postgres:
    image: postgres:15-alpine
    environment:
      POSTGRES_USER: beanshare
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: beanshare
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U beanshare"]
      interval: 10s
      timeout: 5s
      retries: 5

  api:
    image: beanshare-api
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ConnectionStrings__DefaultConnection: Host=postgres;Port=5432;Database=beanshare;Username=beanshare;Password=${POSTGRES_PASSWORD}
      UseOidc: "true"
      Oidc__Authority: ${OIDC_AUTHORITY}
      Oidc__Audience: beanshare-api
      ASPNETCORE_ENVIRONMENT: Production

  web:
    image: beanshare-web
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ConnectionStrings__DefaultConnection: Host=postgres;Port=5432;Database=beanshare;Username=beanshare;Password=${POSTGRES_PASSWORD}
      UseOidc: "true"
      Oidc__Authority: ${OIDC_AUTHORITY}
      Oidc__ClientId: beanshare-web
      Oidc__ClientSecret: ${OIDC_WEB_SECRET}
      ASPNETCORE_ENVIRONMENT: Production
    ports:
      - "8080:8080"

volumes:
  pgdata:
```

3. Start:

```bash
docker compose -f docker-compose.prod.yml up -d
```

### Option B: Direct Deployment (VPS / Bare Metal)

```bash
dotnet publish src/Presentation/BeanShare.Api -c Release -o /opt/beanshare/api
dotnet publish src/Presentation/BeanShare.BlazorWeb -c Release -o /opt/beanshare/web
```

Set environment variables in the service definition or via `/etc/environment`, then run:

```bash
cd /opt/beanshare/api && dotnet BeanShare.Api.dll
cd /opt/beanshare/web && dotnet BeanShare.BlazorWeb.dll
```

### Option C: Cloud Platforms (fly.io, Railway, Render, etc.)

The Dockerfiles work on any container platform. See `deploy/DEPLOYMENT.md` for a complete fly.io example.

---

## Step 5: Reverse Proxy (Nginx)

Blazor Server uses WebSockets, so the web upstream needs the `Upgrade` and `Connection` headers. A same-origin reverse proxy keeps the browser on one public host while routing `/api/` to the API service.

```nginx
server {
    listen 443 ssl;
    server_name yourdomain.com;

    ssl_certificate /etc/letsencrypt/live/yourdomain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/yourdomain.com/privkey.pem;

    location / {
        proxy_pass http://localhost:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /api/ {
        proxy_pass http://localhost:5247;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /swagger {
        proxy_pass http://localhost:5247;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

Equivalent NixOS module setup:

```nix
{
  imports = [
    self.nixosModules.beanshareCommon
    self.nixosModules.api
    self.nixosModules.beanshare
  ];

  services.beanshare = {
    database = {
      password = "use-environmentFile-in-production";
      postgresql.dataDir = "/mnt/db/data/postgresql/18";
    };

    storage.uploadsRootPath = "/mnt/db/data/beanshare/uploads";
  };

  services.beanshare-api = {
    enable = true;
    listenAddress = "127.0.0.1";
    port = 5247;
    oidc = {
      enable = true;
      authority = "https://auth.example.com/realms/beanshare";
      audience = "beanshare-api";
    };
  };

  services.beanshare-blazorweb = {
    enable = true;
    listenAddress = "127.0.0.1";
    port = 8080;
    oidc = {
      enable = true;
      authority = "https://auth.example.com/realms/beanshare";
      clientId = "beanshare-web";
      clientSecret = "use-environmentFile-in-production";
      registrationEndpoint = "https://auth.example.com/realms/beanshare/protocol/openid-connect/registrations";
      identityProviderHintParam = "kc_idp_hint";
    };

    nginx = {
      enable = true;
      domain = "beanshare.example.com";
      proxyApi.enable = true;
    };
  };
}
```

---

## Upgrading to the Money Ledger

The release with running balances, placeholders and the history import changes the schema (migrations
`AddBalanceLedger` and `AddHistoryAndPlaceholders`). Treat them as **forward-only**: their `Down` would drop
the new columns and tables, and with them every balance entry, ledger line field and imported history. An older
build cannot run on the new schema; to go back, restore the backup from step 1.

1. **Back up the database** (`pg_dump --format=custom`) before deploying.
2. **Deploy the API and the web together.** Both apply pending migrations at startup and share the schema.
3. At startup the ledger back-fill runs once and is idempotent: cups that legacy settlements (or Settled periods
   without a settlement) already billed are stamped to those periods, and the "Paid" of legacy settlement lines
   is frozen. Check the startup log for both migrations and the back-fill counts.
4. Nothing is billed by the new ledger until a space has a treasurer and a ledger start date. Spaces that keep
   using the spreadsheet until a planned cutover follow the runbook below.

### API changes for clients

The response DTOs only gain trailing optional fields (for example the ledger fields of settlement lines,
`IsPlaceholder` on members and `IsHistorical` on billing periods). On `/unsettled`, `Balance` and `MyBalance` now
mean the running balance. These status codes change:

| Endpoint | Before | Now |
|---|---|---|
| Every command endpoint (each POST, PUT and DELETE that runs a command) | Access failures mostly 400; a few endpoints already answered 403 | 401 `UNAUTHENTICATED` when the caller has no active, non-placeholder user row; 403 `NOT_MEMBER`, `NOT_ADMIN`, `FORBIDDEN` or `INSUFFICIENT_PRIVILEGES`; 409 `CONCURRENCY_CONFLICT` when the data changed meanwhile (retry after reloading). Each comes with the code in the body, and a 401 also with the `WWW-Authenticate: Bearer` challenge. Every other failure keeps the endpoint's previous status and body |
| `POST /api/billing-periods/{id}/settlement` (generate) | Every failure 400, with only the message in `detail` | Ledger failures (`SETTLEMENT_ORDER`, `TREASURER_REQUIRED`, …) stay 400. The message is now in `Detail` (capital D, like the rest of the API's JSON), and the code is added in `Errors[].Reason` |
| `PUT /api/settlements/{id}/confirm-payment/{memberUserId}` | Every failure 400, with only the message in `detail` | Failures that are not about access (`PAYMENT_ALREADY_CONFIRMED`, `LINE_CARRIED`, `LINE_MERGED`, …) stay 400. The message is now in `Detail`, and the code is added in `Errors[].Reason` |
| `POST /api/users/sync` | 500 on a sync failure | 403 `EMAIL_NOT_VERIFIED` or `SYNC_FAILED`, 409 `PLACEHOLDER_LINK_PENDING`; the 200 response is unchanged |
| `POST /api/users/sync/placeholder-link` (new) | — | `{ "accept": true \| false }` → 200 with the outcome; 404 when nothing is pending; 409 `CONCURRENCY_CONFLICT`; 403 `SYNC_FAILED` |
| Placeholder create/edit (new) | — | 409 `EMAIL_IN_USE` for an address another user already has |
| `DELETE /api/consumptions/{id}`, `PUT /api/consumptions/{id}` | A cup before the ledger start could be deleted, or re-dated past it | 400 `BEFORE_LEDGER_START` for a cup before the ledger start, and for a re-date across it in either direction |
| `POST /api/me/avatar` | Any signed-in caller could upload | 401 `UNAUTHENTICATED`, with the code and the Bearer challenge, for an unknown, deactivated or placeholder account, or a token without a usable user id; the web profile page refuses such an account too, before any file is written |
| `GET /api/spaces/{SpaceId}/consumption` (recent cups), and the `Consumption` part of `GET /api/spaces/{SpaceId}/dashboard` | `CanEdit` offered both Edit and Delete | New `CanDelete` offers Delete. `CanEdit` offers Edit only and is now false for a write-off share, which can only be deleted |

Queries (GET) keep their own failure shapes. A body "with the code" is the shared problem-details shape
(`application/problem+json`): each entry of `Errors` carries an error code in `Reason`. `Detail`, the first error's
message, comes only with the command endpoints' 401, 403 and 409 answers, every failure of generate-settlement and
confirm-payment, and the endpoints that answer through the shared status mapping (for example promote/demote member,
the balance, history and placeholder endpoints). Most other business failures, such as an endpoint's own 400 or 404
(the cups' `BEFORE_LEDGER_START` included) and FastEndpoints' own validation and binding 400s, carry the code without
`Detail`. These keep their earlier bodies, which carry no code: the billing-period create, open, close and reopen
endpoints (`POST /api/spaces/{SpaceId}/billing-periods`, `PUT /api/billing-periods/{id}/open`, `/close`, `/reopen`)
answer ASP.NET problem details with the message in lowercase `detail`; purchase update and delete
(`PUT` and `DELETE /api/spaces/{SpaceId}/stock/purchases/{id}`) and `POST /api/settlements/{id}/send-emails` answer
the message as a bare JSON string; and the profile, avatar and notification endpoints keep their own response bodies.
The 401 answers listed here also carry the Bearer challenge.

**Deploy the API before the MAUI app.** A new MAUI build shows Delete only where `CanDelete` is true; an older API does
not send it, so the app would hide Delete on every cup. An older MAUI build against the new API still shows Delete
under `CanEdit`, so it hides Delete on write-off shares only.

---

## Cutover from the Excel Sheet (Runbook)

This runbook moves a space that settled money in a spreadsheet to BeanShare's money ledger. Production already
holds real bags and cups, and no settlements.

### The facts

- BeanShare has been used since about May 2026, and tally paper #3 was kept in parallel with it.
- **C**, the ledger start date, is the first day of tally paper #3, which is the BeanShare start. Read the exact
  date from paper 3 and check it against the earliest live cup in BeanShare.
- The **final Excel round** covers papers 1 + 2, from 11.08.2025 to C − 1.
- **Paper 3 is never part of Excel money.** BeanShare bills its cups, because they are dated on or after C.

### What the stock data looks like

- **Phantom grams.** BeanShare never saw the grams drunk before C. On 2026-07-24 the lot back-fill poured each
  product's logged grams into its bags oldest first, each bag to its full size. So the pre-C grams surface as a
  phantom in **later** bags of the same product. That phantom was then billed by a later bag's "Mark empty" (a
  write-off), or before 07-24 by a product-level split; or it still shows in a bag; or it was discarded.
- **A product's phantom = Σ g_pre over its bags opened before C + Σ Q (full size) over its Paid-in-Excel bags**,
  whichever bags BeanShare later showed or wrote those grams off on. A Paid-in-Excel bag is the product's oldest,
  so the back-fill or a later draw usually emptied it, and all of its grams surface elsewhere.
- **Cups with no bag.** Cups from C to 2026-07-24 have no bag.
- **The simple route.** The treasurer is the sole bag payer, so attributing those cups to bags would only change
  the per-cup price, never who is credited. BeanShare prices bag-less cups at the average of the bags **not**
  marked Paid in Excel. Phantom billing is voided before the first settlement, and the Excel shares are computed
  by hand.

### Dry run on a copy of production first

Run the whole cutover — steps 2–11 and the branch steps — on a restored copy of the production database. Touch
production only when the copy's result matches the Excel expectation.

- **R1, copy.** On the production host, record the server version with `SELECT version();` and take a dump with
  the read-only access: `pg_dump --format=custom --no-owner --file beanshare-<date>.dump <database>`. Copy it over
  SSH to the operator's machine. It holds personal data: keep it local, and delete it at R7.
- **R2, restore.** Start a **separate throwaway** PostgreSQL of production's major version — not the development
  database from `docker compose`, and not a fixed version (a dump from a newer `pg_dump` cannot be read by an
  older `pg_restore`):
  ```bash
  docker run --rm -d --name bs-dryrun -e POSTGRES_PASSWORD=<pw> -p 5434:5432 postgres:<major>
  docker exec bs-dryrun createdb -U postgres beanshare_dryrun
  docker exec -i bs-dryrun pg_restore -U postgres -d beanshare_dryrun --no-owner --no-privileges < beanshare-<date>.dump
  ```
- **R3, run the new API against it.** Use the new build with these settings:
  - `ConnectionStrings__DefaultConnection=Host=localhost;Port=5434;Database=beanshare_dryrun;Username=postgres;Password=<pw>`
  - `ASPNETCORE_ENVIRONMENT=Production` and `IncludeDemoData=false`, so no demo data is seeded
  - `UseOidc=false`, `UseMockAuthentication=true` and `AllowMockAuthentication=true` (the API refuses mock
    authentication in Production without the explicit allow)
  - Startup applies both migrations and the back-fills to the copy, which also tests them on real data; check the
    log for both migrations and the back-fill counts.
  - Every request carries `X-Test-UserId: <the treasurer's UserId in the copy>`. Mock authentication is for this
    local copy only; never on production.
  - Do not call "send emails".
  - The web UI is optional; it needs an OIDC login whose email matches an admin in the copy.
- **R4, rehearse.** Through the API, do steps 2–11 (settings, dates, flags, voids, Corrections, placeholders,
  opening paste, history paste) and the branch steps. Then preview and generate the first settlement on the copy.
- **R5, compare.** The copy must satisfy all of these; anything over a few cents is investigated:
  - For every person, the copy's first-settlement Net (or the running balance) equals the Excel-computed
    expectation, in Net's sign (positive = the space owes the person): **expected Net = the final Excel round's
    balance (positive = owed to them) + the credit for their bags' coffee drunk since C − their cups since C ×
    the expected price.**
  - ΣNet = 0, and the treasurer's total matches.
  - Step 8 holds for every product.
  - The settlement preview's `FallbackRateMissing` is false.
  - The grams voided per product equal that product's phantom (see "What the stock data looks like"), apart from
    the partly phantom event.
- **R6, record and repeat.** Record the inputs that produced the match: C, the flags, the voided row ids, the
  Corrections and the paste texts. If anything was corrected, restore a fresh copy and repeat from R2, because a dry
  run must succeed from a clean restore. Production then replays exactly these inputs, after a fresh backup. Do it
  soon after the dump, with no stock edits in between; otherwise take a new dump and recheck the lists.
- **R7, clean up.** `docker stop bs-dryrun` (the container is removed) and delete the dump.

### Steps common to both branches

1. Back up the database, then deploy the API and the web together.
2. As admin, open **Balances → Ledger settings**. Set the treasurer (a space admin) and C, which must be ≤ today.
   The confirm dialog shows how many existing cups before C will never be billed. With C at the BeanShare start
   this should be about 0, and any such cup must be on paper 2.
3. **Read-only production check.** Before anything is changed, collect:
   - the production PostgreSQL major version (`SELECT version();`), for the dry run;
   - the live periods that start before C, in any state; these choose branch A or B below;
   - every live period in state Closed, whatever its dates, because step 6 reopens them;
   - the web host's time zone. If it is UTC, set `TZ=Europe/Prague` for the web service, so that "today" in the
     dialogs and every local time shown is Prague time. No code change is needed;
   - every purchase, per product, whether bought before or after C: size, cost, payer, status and counters;
   - the real purchase date of each bag that was on the shelf at C. Shelf bags were entered with their entry time
     as the purchase date;
   - every write-off row since C, per product: per-bag shares (`IsWriteOff`), and product-level split rows. Split
     rows have no bag and are not `IsWriteOff`; they show up as one row per member with the same timestamp, made
     by the old "split remaining stock" before 07-24. **Balances → Cutover from Excel → Phantom rows** lists both
     kinds, grouped by event;
   - the number and grams of bag-less cups on or after C, per product;
   - the identity setup: the identity provider and its brokers; whether self-registration is open; whether email
     verification is required; and whether any broker has "Trust Email" on.
     - If any source can assert `email_verified` without verifying, the identity provider's admin fixes that first.
     - Until then, step 9 creates placeholders without an email, and the admin links them by merge.
4. **Fix purchase dates.** Where a bag's purchase date is its entry time rather than the real date, correct it in
   the stock grid (edit purchase). The flag (step 5) and the average price both read this date.
5. **Classify every bag bought before C**, using paper 2, the shelf and the purchase list:

   | Class | Final Excel round | BeanShare |
   |---|---|---|
   | Finished before C | Its full price, as an Excel purchase | Mark it **Paid in Excel** (Balances → Cutover from Excel → Bags paid in Excel). It gets no BeanShare credit: it is out of the average price, and any cup stamped to it is priced at the average price. |
   | Opened before C, with coffee left at C | **Excel share = K × g_pre / Q**, computed by hand, clamped to [0, K] (K = its price, Q = its size). g_pre comes from the paper-2 cups of that product since the day the bag was opened, or from its weighed remainder at C. Never use BeanShare's counters. | Unchanged, and in the average price. Its phantom shows up in a later bag (step 6). |
   | Unopened at C | Nothing | Unchanged |

   While a bag is flagged, C cannot be moved onto or before its purchase date or cleared, and its date cannot be
   moved to C or later; unflag it first, and correct the opening entry that holds its price.
6. **Remove phantom billing.**
   - **First, reopen every Closed live period** from step 3 that holds such rows. None has a settlement, so Reopen
     is allowed. Close them again after step 8.
   - Then, for each product, take its phantom from "What the stock data looks like" and walk the write-offs since C
     from step 3, oldest first. **Void** them in **Balances → Cutover from Excel → Phantom rows**:
     - **An event that is entirely phantom:** void all its rows, whether per-bag shares or product-level split rows.
       Nothing returns to stock, and no bag revives. Voiding cannot be undone, and "Undo empty" never brings voided
       grams back.
     - **The one event that is only partly phantom:** void it too. Then re-bill its real part with Correction
       entries: each of its drinkers −(their share of the real grams × the price per gram), counterparty = the
       payer (the treasurer). The price is that bag's for a share, and the product's for a split row, as in step 7.
     - Never **delete** these rows instead: deleting a share returns its grams to its bag, and deleting a split row
       returns them into the bag in use, so the phantom is billed again later.
   - **Phantom that still shows** (never written off):
     - On a bag that is physically empty: **Discard** it. This is unbilled. Do not press "Undo empty" afterwards.
     - On a bag still in use: write down the remaining phantom **per product**, not per bag. If that bag is later
       marked empty, void its write-off and re-bill the real part ("Afterwards"). If it instead empties by cups,
       BeanShare keeps drawing until it reaches 0 and the phantom moves on to the product's **next** bag: void up to
       the recorded grams from the product's next write-offs, on whichever bag they land.
   - **Fallback, only if a settlement already billed the rows:** refund the phantom part with Corrections: the
     drinker +X, counterparty = the payer.
     - X = grams × the bag's price, for a share on a bag that is not Paid in Excel.
     - X = grams × the average price of the settlement that billed it (Σ cost / Σ grams over the unflagged
       space-currency bags bought by its period's End), for a split row or a share on a Paid-in-Excel bag.
7. **Missing paper-3 cups.** Compare paper 3 per person with BeanShare's cups from C on.
   - **A kept write-off, or a step-6 re-bill, already billed the grams:** add nothing to the total. If that
     write-off billed another drinker, move the amount with one Correction: the billed drinker +X, counterparty =
     the paper-3 drinker.
   - **Otherwise:** add a Correction entry: the drinker −(grams × that product's price per gram), counterparty =
     the treasurer, dated the paper-3 day, note "paper 3".
   - Do not back-log a cup: it would draw today's bag.
8. **Per-product check.** For each product, in grams: Σ Q of its bags = its phantom + its BeanShare cups ≥ C after
   step 6 (kept write-offs included) + the grams re-billed by step-6 Corrections + **real** discards + what is still
   on the shelf. Real discards are spills and stale coffee only: not step 6's phantom discards, and **not voided
   grams, which the bag counters and the stock ledger also show as discarded**. A gap bigger than a few cups means
   unlogged cups or a missed phantom; investigate it, never book it.
9. **Add placeholders** (Members → Add placeholder), either directly or from the paste previews ("create unknown
   members as placeholders").
   - Before a person's first login, fill in their email as their identity provider knows it, unless step 3 found
     the identity provider untrustworthy.
   - The safe default stays: `Authentication__RequireVerifiedEmailForLinking` is on, and an unverified person sees
     "verify your email". The admin can instead clear the placeholder's email, let the person sign in (this
     creates a new account), and then **merge** the placeholder into it (Members → Merge).
10. **Bulk-paste the opening balances** (Balances → Paste) from the final Excel round, which includes the step-5
    Excel shares and the full price of every Paid-in-Excel bag. Use counterparty = treasurer, effective date = C,
    note "Excel cutoff". The preview shows the entry sum and the treasurer's resulting total. Reconcile it against
    the sheet before saving (the known gap of +3 957.91 from the final-round analysis).
11. **Paste the history** (Billing periods → Import history) converted from the spreadsheet, up to and including
    the final round (papers 1 + 2) as the last historical period. Every historical End must be before C, and paper
    3 is not history. Overlap with live periods is allowed. Check the preview totals against the sheet.

### Worked example

The treasurer T paid three bags of one product, each 1000 g for 600.00 (0.60 per g).

- **X**, bought 03-15, was finished before C and is Paid in Excel.
- **Y**, bought 04-15, was opened on paper 2: 250 g were drunk before C and 750 g after.
- **Z**, bought 06-01, gave 250 g before 07-24 and 750 g after.
- In BeanShare: the 1000 g logged before 07-24 have no bag, and the back-fill counted them all on X. After 07-24
  the draws opened Y and stamped Z's 750 g to it, so Y showed 250 g and Z 1000 g. In August "Mark empty" wrote off
  both bags: 1250 g.

| Step | Action | Money |
|---|---|---|
| 5 | X Paid in Excel; Y opened before C, g_pre = 250 | Excel: X 600.00 + Y 600.00 × 250 / 1000 = 150.00, so **750.00** to T |
| 6 | Phantom = 250 + 1000 = 1250 g = all the write-offs: **void** them. Y and Z stay Empty. | Nothing is billed for them |
| 8 | 3000 = 1250 (phantom) + 1750 (cups ≥ C) + 0 (re-billed) + 0 (real discards; the 1250 voided grams are not counted) + 0 (shelf) | — |
| First settlement | 1000 g without a bag at the average price (Y + Z: 1200.00 / 2000 g = 0.60) = 600.00; 750 g stamped to Y = 450.00 | The drinkers pay **1050.00** for 1750 g. T is credited 1050.00, so T gets 750.00 + 1050.00 = **1800.00** = X + Y + Z. |

`CutoverRunbookTests` replays this example (and its variants) on the real back-fill, stock and calculator.

### Choosing the branch

The first list from step 3 decides. No live period starts before C → branch A; some do → branch B.

**Branch A — no live period starts before C**

12. Create the first period starting at C. The first settlement includes the opening balances.

**Branch B — live periods starting before C exist**

Order matters. The history import may overlap live periods that already exist, but a live period created
afterwards is still checked against history. So leave the pre-C live periods as they are, and create every new
live period at C or later.

12. Delete Draft periods that start before C (they would otherwise block later settlements).
13. Settle the pre-C Open/Closed periods in date order. Close the Open ones first.
    - A period that ends before C settles **empty**, because its cups are before C and the entries are dated C. It
      completes immediately.
    - A period that spans C becomes the first real settlement: its cups on or after C are billed, and the opening
      entries (dated C) are included.
14. Create later periods as usual. A period cannot start on or before the last settled End.

### Afterwards

- There are no partial payments. A partial payment, or a mistaken confirmation, is fixed with a dated Correction
  entry against the treasurer.
- If BeanShare later bills more phantom grams (for example a later "Mark empty" of a bag that was in use at the
  cutover, or the next bag of that product), void that write-off before the next generation while its period is
  open, up to the product's recorded remaining phantom, and re-bill its real part, as in step 6. Once it is
  settled, refund it with the step-6 fallback formula.
- Cup history keeps the stamped price of a cup on a Paid-in-Excel bag, while the settlement bills it at the
  average price.
- Settlement emails reach only members with something to read or do (consumption, a non-zero Net, or the
  treasurer with transfers). Everyone can still see the full settlement on the web.

---

## Database Seeding

On first startup, the application seeds **essential data only** in production:

- 12 global coffee presets (Espresso, Cappuccino, Pour Over, AeroPress, etc.)

No demo users, spaces, or fake consumption data are created unless `IncludeDemoData=true` is set. The first user to log in with the `admin` role in the OIDC provider is automatically synced to the database as an admin via `UserSynchronizationService`.

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| OIDC redirect loop | Incorrect redirect URI in provider | Verify redirect URI matches exactly, including trailing slash |
| 401 on all API calls | Token issuer mismatch | Check `Oidc__Authority` matches the `iss` claim in issued tokens |
| Database connection refused | Wrong connection string or firewall | Verify host, port, and credentials |
| "State mismatch" error | Clock skew between servers | Ensure NTP is configured; check `ClockSkew` setting |
| Roles not working | Claims not mapped | Check that your OIDC provider includes roles in the token |
| QR codes link to wrong URL | Wrong public origin detected | Set `QrCodeBaseUrl` explicitly to the public URL of your web application |

---

## Mobile Application (Android)

The MAUI Android app is a client-side application — it does not run on a server. Configuration is embedded in `appsettings.json` at build time and must be set before building the APK.

### Configuration

Edit `src/Presentation/BeanShare.Maui/appsettings.json`:

```json
{
  "ApiBaseUrl": "https://api.yourdomain.com",
  "UseOidc": true,
  "Oidc": {
    "Authority": "https://auth.yourdomain.com/realms/beanshare",
    "ClientId": "beanshare-mobile",
    "RedirectUri": "beanshare://callback"
  }
}
```

`beanshare://callback` must match the redirect URI registered in your OIDC provider (Client 3 above).

### Build Requirements

- .NET 10 SDK with MAUI workload (`dotnet workload install maui-android`)
- Android SDK (API 35+)
- Java JDK 21

### Building the APK

```bash
dotnet build src/Presentation/BeanShare.Maui/BeanShare.Maui.csproj \
  -f net10.0-android \
  -c Release \
  -p:EmbedAssembliesIntoApk=true
```

The signed APK is output to `bin/Release/net10.0-android/com.companyname.beanshare.maui-Signed.apk`.

### Installation

Sideload via adb (USB debugging must be enabled on the device):

```bash
adb install -r com.companyname.beanshare.maui-Signed.apk
```

For distribution, the APK can be published to Google Play or distributed as a direct download. The app uses the `beanshare://` custom URL scheme for the OIDC callback, which is handled by `WebAuthenticatorCallbackActivity` and requires no additional server configuration.

---
*Author: Oliver Golec*
