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

# Local infrastructure: PostgreSQL (5432), Keycloak (8080), test PostgreSQL (5433)
docker compose up -d

# Run the apps (API on http://localhost:5247, web on http://localhost:5126)
cd src/Presentation/BeanShare.Api && dotnet run
cd src/Presentation/BeanShare.BlazorWeb && dotnet run

# EF Core migrations (DesignTimeDbContextFactory reads BEANSHARE_CONNECTION_STRING,
# defaults to localhost beanshare_dev)
dotnet ef migrations add <Name> --project src/Infrastructure/BeanShare.Infrastructure.Persistence
```

- Integration tests (`tests/BeanShare.Tests.Integration`) require the `beanshare-postgres-test` container on port 5433 from `docker compose up -d`.
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
  - **`BeanShare.BlazorWeb`** (Blazor Server) does **not** call the API over HTTP — it references Application/Infrastructure directly and components `@inject IMediator` to dispatch commands/queries in-process. Auth is Keycloak OIDC + cookies.
  - **`BeanShare.Api`** (FastEndpoints) serves the MAUI mobile app and external clients, with JWT auth. Endpoints mirror the feature folders and use **`BeanShare.Contracts`** request/response DTOs (shared with MAUI).
  - **`BeanShare.SharedUi`** — Razor components and services (QR code scanning/generation, theming) shared between BlazorWeb and MAUI; keep it free of server-only dependencies.
  - **`BeanShare.Maui`** — mobile app; in `BeanShare.sln` but excluded from `BeanShare.slnf`, so normal builds don't need MAUI workloads. See TEST.md for Android build specifics.

**Practical consequence:** a new feature typically touches the Application feature folder (command/query + handler + validator), and then *both* heads — a FastEndpoints endpoint + Contracts DTOs for the API, and the Blazor page/component for the web. Web-only changes don't need the API side.

**Seeding is environment-aware** (`Infrastructure.Persistence/Seeds`): development runs all seeders (users, spaces, stock, consumption, billing periods); production runs only `GlobalPresetSeeder` (marked `IsEssential`).

**Mock modes:** `UseMockServices` / `UseMockAuthentication` (appsettings) run without PostgreSQL/Keycloak; `UseInMemoryDatabase` swaps the EF provider.

## Docs

- `TEST.md` — local setup, demo credentials, MAUI/Android build, troubleshooting.
- `PRODUCTION-DEPLOYMENT.md` — production deployment (NixOS modules `services.beanshare-blazorweb` / `services.beanshare-api`, `nix/nixosModules/`).
