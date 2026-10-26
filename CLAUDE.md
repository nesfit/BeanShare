# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build (use the .slnf — it excludes MAUI, which needs mobile workloads; CI builds this)
dotnet build BeanShare.slnf

# All tests
dotnet test BeanShare.slnf

# Single test project / single test
dotnet test tests/BeanShare.Tests.Unit
dotnet test tests/BeanShare.Tests.Unit --filter "FullyQualifiedName~SomeTestName"

# Local infrastructure: PostgreSQL (5432), Keycloak (8080)
docker compose up -d

# Run the apps (API on http://localhost:5247, web on http://localhost:5126)
cd src/Presentation/BeanShare.Api && dotnet run
cd src/Presentation/BeanShare.BlazorWeb && dotnet run

# EF Core migrations (DesignTimeDbContextFactory reads BEANSHARE_CONNECTION_STRING,
# defaults to localhost beanshare_dev)
dotnet ef migrations add <Name> --project src/Infrastructure/BeanShare.Infrastructure.Persistence
```

- Integration tests (`tests/BeanShare.Tests.Integration`, including the E2E scenario) start their own PostgreSQL through Testcontainers, so they need a running Docker; the compose file's `beanshare-postgres-test` (5433) is not used by them. The Docker-free gate is `dotnet build BeanShare.slnf` plus `BeanShare.Application.Tests`, `BeanShare.Tests.Unit` and `BeanShare.Infrastructure.Tests` without its `DbContextTests` and `Repositories` classes (see TEST.md).
- Migrations are applied automatically at app startup (`DatabaseInitializationExtensions`); there is no separate migration step for local dev.
- On first run the API seeds demo data. Demo users all use password `password123` (e.g. `john.smith@beanshare.dev` is admin). Keycloak admin: admin/admin.
- Nix: `nix develop` for the dev shell; `nix run .#updateDeps` refreshes `nix/deps.json` after changing NuGet packages (CI also auto-commits this on main); `nix run .#dev-container -- up` runs the NixOS container image.
- Commits follow Conventional Commits (`feat:`, `fix:`, …) — semantic-release derives versions from them (`.releaserc.json`).

## Architecture

Clean Architecture on .NET 10 with two independent presentation heads over a shared core. NuGet versions are centralized in `Directory.Packages.props`; common project settings (net10.0, nullable) in `Directory.Build.props`.

**Dependency flow:** Domain ← Application ← Infrastructure ← Presentation.

- **`src/Core/BeanShare.Domain`** — aggregates (Space, CoffeeStock, Settlement), entities, value objects (Money, Currency, Weight), domain events, specifications, and domain services (e.g. weighted-average costing policy). No external dependencies.
- **`src/Core/BeanShare.Application`** — CQRS via MediatR. Each feature lives in `Features/<Feature>/{Commands,Queries,Dtos}` with handler + FluentValidation validator side by side. `Abstractions/` holds repository/service interfaces (incl. `IUserContext`) implemented by Infrastructure.
- **`src/Infrastructure/`** — four projects: `Persistence` (EF Core + Npgsql: DbContext, repositories, migrations, seeders, `DomainEventDispatcher`, UnitOfWork), `Identity` (auth), `Communication` (email), and `Infrastructure` (exchange rates, PDF/Excel documents via QuestPDF/ClosedXML).
- **`src/Presentation/`** — the two heads plus shared code:
  - **`BeanShare.BlazorWeb`** (Blazor Server) does **not** call the API over HTTP — it references Application/Infrastructure directly and components `@inject IScopedSender Mediator` to dispatch commands/queries in-process. Auth is Keycloak OIDC + cookies.
    - **Web rule:** pages send only through `IScopedSender`, which runs every operation in its own DI scope (a fresh `DbContext`), so a stale tracked entity never leaks from one click into the next. Do not hold entities across operations. Never register anything as `IMediator`, `ISender` or `IPublisher`: MediatR 12 forwards them to each other, and the recursion hangs (`ScopedSenderRegistrationTests` guards this).
    - Because each operation has its own scope, notification handlers cannot reach circuit-scoped services: there is no real-time push to the page from inside a command. The notification handlers only write `Notification` rows.
  - **`BeanShare.Api`** (FastEndpoints) serves the MAUI mobile app and external clients, with JWT auth. Endpoints mirror the feature folders and use **`BeanShare.Contracts`** request/response DTOs (shared with MAUI).
  - **`BeanShare.SharedUi`** — Razor components and services (QR code scanning/generation, theming) shared between BlazorWeb and MAUI; keep it free of server-only dependencies.
  - **`BeanShare.Maui`** — mobile app; in `BeanShare.sln` but excluded from `BeanShare.slnf`, so normal builds don't need MAUI workloads. See TEST.md for Android build specifics.

**Practical consequence:** a new feature typically touches the Application feature folder (command/query + handler + validator), and then *both* heads — a FastEndpoints endpoint + Contracts DTOs for the API, and the Blazor page/component for the web. Web-only changes don't need the API side.

**Seeding is environment-aware** (`Infrastructure.Persistence/Seeds`): development runs all seeders (users, spaces, stock, consumption, billing periods); production runs only `GlobalPresetSeeder` (marked `IsEssential`).

**Mock mode:** `UseMockAuthentication` + `UseInMemoryDatabase` (appsettings) run the API without Keycloak and PostgreSQL. The API accepts `UseMockAuthentication` only in the Development or Testing environment, or with `AllowMockAuthentication=true` (`MockAuthenticationGuard`); it refuses to start otherwise. EF InMemory is not atomic: a command that ends in 409 can still have persisted part of its changes in mock mode.

## Coffee stock is tracked per bag (lot)

Stock is **per physical bag**, not per product. Each `Purchase` inside the `CoffeeStock` aggregate is a
bag/lot with its own `RemainingGrams`, `Label` ("#N" per product), and lifecycle `Status`
(`Sealed → Open → Empty`). Key rules future work must respect:

- **One cup = one bag, no spill.** `CoffeeStock.ConsumeStock(product, grams, clock, targetLotId?)` draws
  entirely from a single lot — `targetLotId` if given, else the product's **open** bag, else auto-opens the
  **oldest** non-empty bag. If that bag lacks the grams it throws `InsufficientStockException` (the caller
  logs less / opens the next bag); it never spreads a cup across bags.
- **Per-cup cost is stamped at log time.** `ConsumptionEntry` stores the drawn bag's cost
  (`CostAmount`/`CostCurrency`/`UnitCostPerGram`). Settlement/analytics prefer this stamped cost and fall
  back to the weighted-average estimate only for legacy/unattributed (null-cost) cups.
- **`StockLevel` is a derived read-only projection** (per product, computed from a product's lots) — it is
  **not** persisted (the `StockLevels` table was dropped). It keeps `IsCurrentlyUsed => IsOpen` /
  `IsArchived => false` aliases for backward compatibility.
- **Bag operations:** `AddStockPurchaseCommand` takes `BagCount` (N identical lots — each lot gets its own
  cloned value objects; sharing one EF-owned instance across lots persists nulls). `OpenBagCommand` marks
  the in-use bag. `MarkBagEmptyCommand` empties a bag and **phantom-fills** the untracked remainder as
  consumption entries billed to that bag's drinkers at its own €/g (replaces the old `SplitRemainingStock`;
  legacy split/currently-used/archive endpoints remain as shims). Those entries are stamped `IsWriteOff`
  and the bag records `WrittenOffGrams`. `DiscardBagCommand` (`POST /stock/bags/{lotId}/discard`) is the
  no-bill counterpart for stale/spilled/never-real coffee: the remainder goes to `Purchase.DiscardedGrams`
  (so `RemainingGrams = Quantity − Consumed − Discarded − Voided`, and `StockLevel.CurrentStock` likewise), nobody
  is billed, and the ledger shows it as a `Kind = "Discard"` outflow attributed to nobody (`UserName "—"`,
  cost = sunk value) so the product balance still reaches zero. Both are reversible: `UndoBagEmptyCommand`
  (`POST /stock/bags/{lotId}/undo-empty`, admin only) deletes exactly the write-off cups (none for a
  discard) and puts the grams back (bag returns to Sealed if nothing was ever poured from it, else Open)
  within `Purchase.WriteOffUndoWindow` (24 h) and only while none of those cups sits in a Closed/Settled
  period. `StockPurchaseDto.CanUndoEmpty`/`UndoEmptyUntil` drive the "Undo empty" button in
  `StockBagsGrid`, which keeps such a bag listed for admins during the window. `DeletePurchase` stays
  reserved for untouched bags (nothing consumed) — a record entered by mistake; the grid offers it only then.
- **Write-off shares** can be deleted but not edited (an edit would re-draw the share as an ordinary cup).
  Deleting one restores its grams through `Restore(…, writeOff: true)`, which also lowers `WrittenOffGrams`,
  so a later "Undo empty" restores only what is still written off.
- **Phantom rows are voided, never deleted.** Deleting a write-off share returns its grams to its bag, and
  deleting a bag-less split row returns them product-wide into the bag in use; either way the phantom is billed
  again later. `VoidPhantomRowsCommand` (admin, all or nothing, audited by log) removes write-off shares and
  bag-less rows instead: a share's grams move to `Purchase.VoidedGrams` (`VoidWriteOff`, clamped to the
  consumed grams), so `RemainingGrams` and `Status` stay, no bag revives, and "Undo empty" never brings them
  back. A bag-less row touches no bag. `GetPhantomCandidatesQuery` lists write-off and split-like events since
  the ledger start (optionally every bag-less row). The stock ledger shows voided grams as unbilled discards.
- **Grid order:** `StockBagsGrid` lists products with an open bag first, and within a product the open bag
  first, then sealed bags by label, then a just-emptied (undoable) bag.
- **QR → bag:** `ActiveQrCode`, `RecordConsumptionCommand`, and the Create/Reassign QR commands carry an
  optional `StockLotId`; a scan logs against the bound bag, falling back to the open bag if it is empty/gone.
- **Manual log picks a bag too, not a product.** `GetBagSuggestionsQuery` ranks non-empty lots via
  `BagSuggestionPolicy` (Domain/Services): open bags first, then a histogram over the space's last 20 cups
  with the caller's own cups weighted ×2, tie-broken by last use → most recently opened → oldest purchase.
  The head of the list is `IsSuggested` and both heads' pickers (`ConsumptionForm`,
  `QuickConsumptionModal`) preselect it and submit its `StockLotId`; they fall back to the old
  product-level dropdown when no suggestions are supplied.
- **Ledger:** `GetSpaceLedgerQuery` is the unified, filterable/sortable/paged view of every purchase (+) and
  cup (−) with a per-product running balance and totals (web: the Ledger tab; API: `GET /stock/ledger`).
  This stock **Ledger** is a different thing from the money ledger below.
- **Running balance:** `GetUnsettledSummaryQuery(spaceId?)` shows each member's running balance as of now —
  carried in + balance entries + credit − cost (+ rounding and merge transfer), computed by the same calculator
  as a settlement (`LedgerSnapshotService`). "Unsettled" cups are those whose `BillingPeriodId` is null *or*
  points at a live period that is not `Settled`, so back-dated cups are never lost. Members see only their own
  balance; space admins also get the per-member breakdown. Passing no `spaceId` covers every space the caller
  is in — that is what the `/spaces` dashboard renders. Web: `UnsettledSummaryCard` on the dashboard and the
  Consumption tab; API: `GET /spaces/{id}/unsettled` and `GET /me/unsettled`.

## Money ledger (running balances, treasurer)

Money is a running balance per member and space (feature folder `Features/Balances`; web: the Balances tab).
A member's **Net** is positive when the space owes the member.

- **Inputs:** cups (cost to the drinker, the same amount as **credit** to the bag's payer, `Purchase.EffectivePayer`
  = `PaidByUserId ?? PurchasedBy`), two-party **balance entries** (`BalanceEntry`, kinds Opening and Correction,
  member vs counterparty, default counterparty the treasurer), and the **carry** from the previous settlement.
  One calculator does it all (`SettlementLedgerCalculator`, pure): generation, the settlement preview, the
  running balance and the Balances page go through `LedgerSnapshotService`, so ΣNet = 0 exactly.
- **Pricing:** a cup uses its stamped cost; a cup without one (legacy, or before bags existed) is a fallback cup
  priced at Σ cost / Σ grams of the space-currency bags bought by the period's end, and that credit goes pro rata
  to those bags' payers.
- **Treasurer:** an active admin (never a placeholder), set with `LedgerStartDate` in the ledger settings. Every
  payment goes through the treasurer (the star plan: debtors pay the treasurer, the treasurer pays creditors), and
  only the treasurer's line carries the rounding residual; every breakdown shows it.
- **Settlements** (`Settlement.Model = Ledger`): one line per member with cost, credit, carried in, adjustments,
  rounding and merge transfer. A line is confirmed (or resolves itself at 0); open lines are **carried** into the
  next settlement, which supersedes the previous one. A carried line is confirmed in the settlement it was
  carried into (`LINE_CARRIED`). There are no partial payments: a partial payment or a mistaken confirmation is
  fixed with a dated Correction entry against the treasurer.
- **Ordering:** settlements go in date order (`SETTLEMENT_ORDER`); a new live period cannot start on or before
  the last settled End. Imported historical periods never take part in ordering.
- **`LedgerStartDate` (C):** cups before C are never billed by the ledger; C must be ≤ today and after every
  imported historical period, and it is locked after the first ledger settlement (`LEDGER_START_LOCKED`).
  Entries included in a settlement are locked (`ENTRY_LOCKED`).
- **Legacy settlements** (made before the ledger) keep their old model and their frozen "Paid"; their open lines
  are not part of the running balance.
- **Concurrency:** version tokens on `Space.LedgerVersion`, `Settlement`, `BillingPeriod`, `Purchase`,
  `BalanceEntry` and `ConsumptionEntry`. Anything that changes a ledger input bumps `Space.LedgerVersion`, so a
  racing generation or confirmation gets `CONCURRENCY_CONFLICT` (409 in the API; the web reloads).
- **Settlement emails** go to every line with a deliverable address and something to read or do: consumption in
  the settlement, a non-zero Net, or (the treasurer) at least one transfer. Placeholders without an email are
  skipped. Every recipient gets the full PDF, and the wording follows the line's state ("you paid … confirmed").
  A superseded settlement can still be exported but not mailed.
- **Cutover tools (Balances → Cutover from Excel):** a bag bought before C can be marked **Paid in Excel**
  (`Purchase.ExcludedFromLedgerCredit`): it earns no BeanShare credit, it is left out of the fallback price, and a
  cup stamped to it is priced as a fallback cup. While a bag is flagged, C cannot move onto or before it or be
  cleared, and its date cannot move into the ledger (`BAG_PAID_IN_EXCEL`). The settlement preview flags
  `FallbackRateMissing` when bag-less cups have nothing to be priced by. Cup history keeps the stamped price of a
  cup on a Paid-in-Excel bag, while the settlement bills it at the average price. Missing old cups are entered
  as Correction entries, never as back-logged cups (a back-logged cup draws today's bag).
- **Treat the migrations as forward-only:** their `Down` would drop the ledger and history data; going back means
  restoring a backup. Deploy the API and the web together.

## Placeholder members

- An admin can add a **placeholder**: a member without an account (`User.IsPlaceholder`), with an email or with a
  sentinel address (`placeholder-…@placeholder.invalid`, shown as empty). One email belongs to one user, so a
  placeholder's email can exist in only one space (`EMAIL_IN_USE`). The paste previews can create placeholders
  for unknown members. Placeholders get no notifications, cannot be admins or the treasurer, and cannot act:
  `CurrentUserBehavior` (first in the pipeline) refuses every command and `IAuthorize` request from an unknown,
  inactive or placeholder identity (`UNAUTHENTICATED`), read uncached through `GetAccessStatusAsync`.
- **Linking:** sync matches a provider subject first, then an `email` claim whose `email_verified` is true
  (`Authentication:RequireVerifiedEmailForLinking`, default true; placeholders always need it). A verified match
  on an unclaimed placeholder is **pending**: the person sees the link page and accepts (the placeholder becomes
  their account, same user id) or declines (the placeholder gives up the email and the person gets a new account).
  The web strips any `beanshare_link_pending` claim that comes from the identity provider before sync decides;
  only sync adds it. The API answers a pending sync with 409 and an unverified one with 403.
  - In the API, `OidcClaimsTransformation` replaces `sub` with the database user id and keeps the provider's
    subject in `beanshare_provider_sub` (issuer `BeanShare`); sync reads it through `OidcClaims.ProviderSubjectOf`.
    An email match links only an account that has no provider link yet, and sentinel addresses never match.
- **Merge:** an admin can merge a placeholder into a member with an account. No entry is written: every later
  calculation **sweeps** the placeholder's Net to the target (`MergeTransfer` on both lines), whatever still
  reaches it. The merged placeholder stays a hidden member; nothing new can name it (`MEMBER_MERGED`), its lines
  cannot be confirmed (`LINE_MERGED`), its history moves to the target, and emptied bags it paid for name the
  target.

## Excel history import

`Features/History`: an admin pastes the old sheet as cups per period and member
(`period;start;end;member;cups`) and purchases (`period;date;paidBy;description;grams;cost`, date and payer
optional). Each period becomes a **historical** billing period (`IsHistorical`, Settled, no settlement) with a
`HistoryImport` holding the counts and purchases. History is statistics only: it never touches money, ordering
guards and the settled watermark ignore it, every historical End must be before C, and deleting an import removes
only its periods. Pastes (balances and history) are capped at 250,000 characters (`PasteLimits`); a text that
reaches the cap is refused.

## Docs

- `TEST.md` — local setup, demo credentials, MAUI/Android build, troubleshooting.
- `PRODUCTION-DEPLOYMENT.md` — production deployment (NixOS modules `services.beanshare-blazorweb` / `services.beanshare-api`, `nix/nixosModules/`), the money-ledger upgrade and the cutover runbook from the Excel sheet.
