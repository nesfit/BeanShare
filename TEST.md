# BeanShare Local Testing Guide

How to run BeanShare locally for development and testing.

---

## Prerequisites

| Software | Version | Purpose |
|----------|---------|---------|
| [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0+ | Application runtime |
| [Docker Desktop](https://www.docker.com/products/docker-desktop) | Latest | PostgreSQL & Keycloak containers |

**For MAUI Android testing** (optional):
- Android SDK (API Level 21+)
- JDK 17+ (OpenJDK recommended)
- Android Emulator or physical device

---

## 1. Start Infrastructure

```bash
cd BeanShare
docker compose up -d
```

This starts three containers:

| Container | Port | Purpose |
|-----------|------|---------|
| `beanshare-postgres-dev` | 5432 | PostgreSQL database |
| `beanshare-keycloak-dev` | 8080 | Keycloak authentication |
| `beanshare-postgres-test` | 5433 | Spare PostgreSQL (the integration tests do not use it; they start their own through Testcontainers) |

Keycloak auto-imports the `beanshare` realm on startup with:
- 3 OAuth clients (`beanshare-api`, `beanshare-web`, `beanshare-mobile`)
- 8 demo users (all with password `password123`)

Verify all services are healthy:

```bash
docker compose ps
```

## 2. Run the API

```bash
cd src/Presentation/BeanShare.Api
dotnet run
```

The API starts on **http://localhost:5247**. On first run it automatically creates the database schema and seeds demo data (users, spaces, coffee stock, consumption entries, billing periods, and global presets).

Swagger UI: http://localhost:5247/swagger

## 3. Run the Web Application

```bash
cd src/Presentation/BeanShare.BlazorWeb
dotnet run
```

The web app starts on **http://localhost:5126**. Click **Login** to authenticate via Keycloak.

## 4. Demo Credentials

All demo users use password `password123`:

| User | Email | Role |
|------|-------|------|
| John Smith | john.smith@beanshare.dev | Admin (Engineering Team, Executive Lounge) |
| Sarah Johnson | sarah.johnson@beanshare.com | Member (Engineering Team, Marketing Office) |
| Mike Wilson | mike.wilson@beanshare.com | Member (Engineering Team, Remote Workers Hub) |
| Emma Davis | emma.davis@beanshare.com | Member (Engineering Team, Startup Garage) |
| Test User | test@beanshare.com | Member (Engineering Team, Remote Workers Hub) |

Keycloak Admin Console: http://localhost:8080 (admin / admin)

---

## Database Seeding

The application uses environment-aware seeding. In development, all seeders run automatically on first startup:

| Seeder | Description |
|--------|-------------|
| `GlobalPresetSeeder` | 12 coffee recipe presets (Espresso, Cappuccino, Pour Over, etc.) |
| `UserSeeder` | 8 demo users |
| `SpaceSeeder` | 5 spaces (Engineering Team, Marketing Office, Remote Workers Hub, Startup Garage, Executive Lounge) |
| `CoffeeStockSeeder` | Coffee stock purchases across all spaces |
| `ConsumptionSeeder` | Consumption entries with realistic date distribution |
| `BillingPeriodSeeder` | Billing periods in various states (open, closed, settled) |

In production, only the `GlobalPresetSeeder` runs (marked as `IsEssential`). See [PRODUCTION-DEPLOYMENT.md](../PRODUCTION-DEPLOYMENT.md) for details.

---

## MAUI Mobile App (Android Emulator)

### Build and Install

```bash
cd src/Presentation/BeanShare.Maui

JAVA_HOME=/path/to/jdk ANDROID_HOME=/path/to/sdk \
  dotnet build -f net10.0-android -c Debug -p:EmbedAssembliesIntoApk=true
```

Install the APK on the emulator:

```bash
adb install -r bin/Debug/net10.0-android/com.companyname.beanshare.maui-Signed.apk
```

### Emulator Notes

- The Android emulator reaches the host machine's `localhost` via `10.0.2.2`. The MAUI app handles this mapping automatically in `MauiProgram.cs` (API URL) and `AuthenticationService.cs` (Keycloak URL).
- `EmbedAssembliesIntoApk=true` is required — Fast Deployment breaks the Blazor WebView communication.
- After reinstalling the APK, clear app data to remove stale tokens: `adb shell pm clear com.companyname.beanshare.maui`

### Windows Build

```bash
cd src/Presentation/BeanShare.Maui
dotnet build -f net10.0-windows10.0.19041.0 -c Release
```

---

## Running Tests

```bash
# Docker-free gate (what every commit must pass): build, then the three Docker-free suites
nix develop -c dotnet build BeanShare.slnf
nix develop -c dotnet test tests/BeanShare.Application.Tests
nix develop -c dotnet test tests/BeanShare.Tests.Unit
nix develop -c dotnet test tests/BeanShare.Infrastructure.Tests \
  --filter "FullyQualifiedName!~BeanShare.Infrastructure.Tests.DbContextTests&FullyQualifiedName!~BeanShare.Infrastructure.Tests.Repositories"

# Integration tests: they start PostgreSQL through Testcontainers (postgres:15-alpine), so Docker must run
nix develop -c dotnet test tests/BeanShare.Tests.Integration

# The opening-balance E2E scenario only (Docker)
nix develop -c dotnet test tests/BeanShare.Tests.Integration \
  --filter "FullyQualifiedName~OpeningBalanceE2ETests" -nodeReuse:false -p:UseSharedCompilation=false
```

- The Docker-free Infrastructure filter keeps the InMemory classes (`InMemory/*`: concurrency races with a save
  barrier, which assert only the outcome, because EF InMemory is not atomic) and the pure classes (`Cutover/*`,
  `Identity/*`, `Services/*`). The PostgreSQL variants of the races, which also assert the end state, live in
  `BeanShare.Tests.Integration/Concurrency`.
- Five Integration classes need no container and can run anywhere: `ResultMappingTests`, `PlaceholderLinkEndpointTests`,
  `AvatarUploadAccessTests`, `MockAuthenticationGuardTests` and `OpeningBalanceScenarioTests`. The gate runs them in one
  process with
  `--filter "FullyQualifiedName~MockAuthenticationGuardTests|FullyQualifiedName~PlaceholderLinkEndpointTests|FullyQualifiedName~ResultMappingTests|FullyQualifiedName~OpeningBalanceScenarioTests|FullyQualifiedName~AvatarUploadAccessTests"`.
- Under the Integration mock authentication, `IUserContext` follows the `X-Test-UserId` header, while
  `ICurrentPrincipalAccessor` returns the `MockAuthenticationHandler` principal (user `2222…`). A test of a
  principal-based path, such as the pending placeholder link, must build its own principal (the E2E calls
  `IUserSynchronizationService` directly with one).
- Every command and authorized request needs an existing, active, non-placeholder user row. The fixtures seed
  users `11111111-1111-1111-1111-111111111111` (`test@coffeespace.local`) and
  `22222222-2222-2222-2222-222222222222` (`second@test.com`); call `EnsureUserAsync(id, email)` for
  any other `X-Test-UserId`.
- Endpoints with a request DTO expect a JSON body even when every field comes from the route; send `{}` (or the
  route values) rather than no body, or FastEndpoints answers 415.

---

## Troubleshooting

### Keycloak shows "unhealthy" in Docker

The healthcheck uses a bash TCP probe. If Keycloak is accessible at http://localhost:8080 but Docker shows unhealthy, restart it:
```bash
docker compose restart beanshare-keycloak
```

### API returns 401 for all requests

- Verify Keycloak is running and the realm exists
- Check that `Keycloak:Authority` URL is correct and reachable from the API
- Ensure the Keycloak signing keys haven't changed (restart API after Keycloak recreate)

### BlazorWeb login redirects to error page

- Verify `Keycloak:ClientSecret` matches the secret in Keycloak Admin Console
- Check that redirect URIs in Keycloak client include `http://localhost:5126/*`
- Ensure the Keycloak realm name matches the Authority URL path

### MAUI app can't reach API on Android emulator

The Android emulator uses `10.0.2.2` to reach the host machine's `localhost`. The MAUI app handles this automatically, but ensure the API is listening on `localhost:5247`.

### Currency conversion

Without an `OpenExchangeRates:AppId`, the API uses built-in constant exchange rates (approximate early-2026 values). For live rates, get a free key from https://openexchangerates.org/signup/free.
