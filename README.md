# BeanShare

BeanShare is a Clean Architecture information system for managing office coffee consumption, built with .NET 10, Blazor Server, and .NET MAUI. It tracks purchases, logs user consumption, generates settlements, and provides analytics with PostgreSQL and OpenID Connect support.

## Features

- **Multi-User Spaces**: Create and manage multiple coffee-sharing spaces
- **Stock Management**: Track coffee purchases and inventory levels
- **Consumption Tracking**: Log individual coffee consumption with presets
- **Running Balances**: Every member has a running balance (cups at their bag's price, credit for the bags they paid for, balance entries, and what was carried from the last settlement)
- **Treasurer Settlements**: Settlements go through one treasurer; open lines carry into the next settlement, and corrections are balance entries
- **Placeholder Members**: Members without an account, who can later link their login or be merged into another member
- **Excel History**: Import the old spreadsheet's periods, cups and purchases as statistics, and cut over from it to BeanShare
- **Multi-Currency Support**: Handle purchases and settlements in different currencies with automatic exchange rate updates
- **Authentication**: Support for Keycloak OIDC, JWT tokens, and OAuth providers
- **Cross-Platform**: Web application (Blazor Server) and mobile app (.NET MAUI)
- **Document Export**: Generate settlement reports in PDF and Excel formats
- **Email Notifications**: Send settlement notifications to space members

## Architecture

BeanShare follows Clean Architecture principles with clear separation of concerns:

```
BeanShare/
├── src/
│   ├── Core/
│   │   ├── BeanShare.Domain/           # Entities, Value Objects, Domain Services
│   │   └── BeanShare.Application/      # Use Cases, DTOs, Interfaces
│   ├── Infrastructure/
│   │   ├── BeanShare.Infrastructure/   # Exchange Rates, Documents
│   │   ├── BeanShare.Infrastructure.Persistence/  # EF Core, Repositories
│   │   ├── BeanShare.Infrastructure.Identity/     # Authentication
│   │   └── BeanShare.Infrastructure.Communication/ # Email Service
│   └── Presentation/
│       ├── BeanShare.Api/              # FastEndpoints API
│       ├── BeanShare.BlazorWeb/        # Blazor Server Web App
│       ├── BeanShare.Maui/             # Mobile App
│       └── BeanShare.SharedUi/         # Shared Razor Components
└── tests/
    ├── BeanShare.Application.Tests/    # Application Layer Tests
    ├── BeanShare.Infrastructure.Tests/ # Infrastructure Tests
    ├── BeanShare.Tests.Unit/           # Domain Unit Tests
    └── BeanShare.Tests.Integration/    # Integration Tests
```

## Quick Start

### Prerequisites

- .NET 10.0 SDK
- PostgreSQL 14+
- (Optional) Keycloak for OIDC authentication
- (Optional) OpenExchangeRates API key for currency conversion

### Local Development Setup

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd BeanShare
   ```

2. **Configure environment variables**
   ```bash
   cp .env.example .env
   # Edit .env with your local configuration
   ```

3. **Set up the database**
   ```bash
   # Create PostgreSQL database
   createdb beanshare

   # Update connection string in .env
   ConnectionStrings__DefaultConnection="Host=localhost;Database=beanshare;Username=postgres;Password=yourpassword"
   ```

4. **Run the API**
   ```bash
   cd src/Presentation/BeanShare.Api
   dotnet run
   ```

5. **Run the Web Application**
   ```bash
   cd src/Presentation/BeanShare.BlazorWeb
   dotnet run
   ```

6. **Access the application**
   - Web App: http://localhost:5126
   - API: http://localhost:5247
   - Swagger: http://localhost:5247/swagger

### Using Mock Services (No Database Required)

For quick testing of the API without PostgreSQL or Keycloak:

```json
{
  "UseInMemoryDatabase": true,
  "UseMockAuthentication": true
}
```

Add these to `appsettings.Development.json` and run the API. Mock authentication trusts the `X-Test-UserId` header, so the API accepts it only in the Development or Testing environment (or with `AllowMockAuthentication=true`). The in-memory database is not transactional: after a 409 a command may have saved part of its changes.

### Nix / NixOS

Development with Nix:

```bash
nix develop          # enter dev shell (.NET 10, git)
nix run .#updateDeps # refresh NuGet lockfile (nix/deps.json)
cp .env.oidc.local.example .env.oidc.local # optional: enable local OIDC for dev-container
nix run .#dev-container -- up # build/load/run dev container in Docker
```

Run the development container for the `services.beanshare-blazorweb` NixOS module in Docker:

```bash
# build and start
nix run .#dev-container -- up

# open a shell in the running container
nix run .#dev-container -- exec

# connect over SSH (nixos@localhost:2222, password: nixos)
nix run .#dev-container -- ssh

# inspect and stop it
nix run .#dev-container -- status
nix run .#dev-container -- down
```

This workflow:
- builds a NixOS container image via `dockerTools` (no project Dockerfile)
- enables `services.beanshare-blazorweb`, `services.beanshare-api`, and nginx inside the container
- loads and starts it under Docker with systemd
- exposes SSH on `localhost:2222` and a single HTTP endpoint on `0.0.0.0:5000`
- serves the Blazor web app on `http://localhost:5000/` and the API on `http://localhost:5000/api/`
- mounts `.env.oidc.local` into the container when present, so local OIDC secrets stay out of git
- supports `up`, `exec`, `down`, `ps`, `status`, and `ssh` subcommands via `dev-container`

When deploying the NixOS modules directly, import `self.nixosModules.beanshareCommon` alongside the web and API modules. It provides shared defaults for:
- `services.beanshare.database.*` for the common PostgreSQL connection and local data directory
- `services.beanshare.storage.uploadsRootPath` for the shared avatar upload filesystem path

## Documentation

- **[Deployment Guide](DEPLOYMENT.md)** - Full deployment guide with infrastructure setup
- **[Configuration Guide](CONFIGURATION.md)** - Complete guide to all configuration options
- **[Production Deployment Checklist](PRODUCTION_DEPLOYMENT_CHECKLIST.md)** - Step-by-step deployment checklist
- **[CSS Architecture](CSS_ARCHITECTURE.md)** - UI styling and theming guide
- **[Architecture](ARCHITECTURE.md)** - System architecture and design decisions

## Configuration

BeanShare uses environment variables for sensitive configuration. See [CONFIGURATION.md](CONFIGURATION.md) for detailed information.

### Required Configuration

- `ConnectionStrings__DefaultConnection` - PostgreSQL connection string
- `Jwt__Secret` - JWT signing secret (minimum 32 characters)
- `OpenExchangeRates__AppId` - API key from openexchangerates.org

### Optional Configuration

- Keycloak OIDC settings
- Email (SMTP) settings
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)

### Setup

```bash
# 1. Start PostgreSQL + Keycloak
docker compose up -d

# 2. Run the API (auto-creates database and seeds demo data)
cd src/Presentation/BeanShare.Api
dotnet run

# 3. In another terminal, run the web app
cd src/Presentation/BeanShare.BlazorWeb
dotnet run
```

### Access

- **Web App**: http://localhost:5126
- **API / Swagger**: http://localhost:5247/swagger
- **Keycloak Admin**: http://localhost:8080 (admin / admin)

Login with any demo user (password: `password123`):
- `john.smith@beanshare.dev` (Admin)
- `sarah.johnson@beanshare.com` (Member)
- `test@beanshare.com` (Member)

See [PRODUCTION-DEPLOYMENT.md](../PRODUCTION-DEPLOYMENT.md) for production deployment and [TEST.md](TEST.md) for local setup.

## Documentation

- **[Local Testing Guide](TEST.md)** - Development setup & local testing
- **[Production Deployment](../PRODUCTION-DEPLOYMENT.md)** - Production deployment guide
- **[Configuration Guide](CONFIGURATION.md)** - All configuration options
- **[Architecture](ARCHITECTURE.md)** - System architecture and design decisions
- **[API Documentation](API_DOCUMENTATION.md)** - REST API reference
- **[User Guide](USER_GUIDE.md)** - End-user documentation
- **[CSS Architecture](CSS_ARCHITECTURE.md)** - UI styling and theming

## Configuration

See [CONFIGURATION.md](CONFIGURATION.md) for all options and [PRODUCTION-DEPLOYMENT.md](../PRODUCTION-DEPLOYMENT.md) for production setup.

### Required for Production

- PostgreSQL connection string
- Keycloak realm URL and client secrets
- JWT signing secret (minimum 32 characters)

### Optional

- [OpenExchangeRates](https://openexchangerates.org/signup/free) API key (currency conversion)
- SMTP credentials (email notifications)
- OAuth providers (Google, Facebook)

## Authentication Options

BeanShare supports multiple authentication methods:

1. **Keycloak OIDC** (Recommended for production)
   - Enterprise-grade authentication
   - OAuth 2.0 / OpenID Connect
   - Single sign-on support
   - User federation

2. **OAuth Providers**
   - Google OAuth
   - Facebook OAuth

See [CONFIGURATION.md](CONFIGURATION.md) for setup instructions.

## Technology Stack

### Backend
- **.NET 10** - Application framework
- **Entity Framework Core** - ORM
- **PostgreSQL** - Database
- **FastEndpoints** - API framework
- **MediatR** - CQRS pattern
- **Mapster** - Object mapping

### Frontend
- **Blazor Server** - Web application framework
- **.NET MAUI** - Mobile application framework
- **Bootstrap 5** - UI framework
- **Font Awesome** - Icons

### External Services
- **Keycloak** - Authentication (optional)
- **OpenExchangeRates** - Currency conversion
- **SMTP** - Email notifications

### Development Tools
- **xUnit** - Testing framework
- **FluentAssertions** - Test assertions
- **Moq** - Mocking library
- **QuestPDF** - PDF generation
- **ClosedXML** - Excel generation

## Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/BeanShare.Application.Tests

# Integration and E2E tests start PostgreSQL through Testcontainers and need Docker
dotnet test tests/BeanShare.Tests.Integration

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Building for Production

```bash
# Build API
cd src/Presentation/BeanShare.Api
dotnet publish -c Release -o ./publish

# Build Web App
cd src/Presentation/BeanShare.BlazorWeb
dotnet publish -c Release -o ./publish

# Build MAUI (Windows)
cd src/Presentation/BeanShare.Maui
dotnet publish -f net10.0-windows10.0.19041.0 -c Release

# Build MAUI (Android)
dotnet publish -f net10.0-android -c Release
```

See [PRODUCTION_DEPLOYMENT_CHECKLIST.md](PRODUCTION_DEPLOYMENT_CHECKLIST.md) for complete deployment guide.
See [TEST.md](TEST.md) for local setup and [PRODUCTION-DEPLOYMENT.md](../PRODUCTION-DEPLOYMENT.md) for production deployment.

## Project Structure

### Core Layer

**BeanShare.Domain** - Enterprise business rules
- `Aggregates/` - Domain aggregates (Space, CoffeeStock, Settlement)
- `Entities/` - Domain entities
- `ValueObjects/` - Value objects (Money, Currency, Weight)
- `Services/` - Domain services (CostingPolicy)
- `Specifications/` - Query specifications

**BeanShare.Application** - Application business rules
- `Features/` - Use cases organized by feature (CQRS pattern)
- `Abstractions/` - Repository and service interfaces
- `Services/` - Application services
- `Constants/` - Application constants
- `Common/` - Shared DTOs and results

### Infrastructure Layer

**BeanShare.Infrastructure** - External services implementation
- `Services/` - Currency exchange, document generation
- `DependencyInjection.cs` - Service registration

**BeanShare.Infrastructure.Persistence** - Data access
- `Configurations/` - EF Core entity configurations
- `Repositories/` - Repository implementations
- `Seeds/` - Database seeding

**BeanShare.Infrastructure.Identity** - Authentication
- JWT token generation
- Password hashing
- OAuth integration

**BeanShare.Infrastructure.Communication** - Email service
- SMTP email sending
- Email templates

### Presentation Layer

**BeanShare.Api** - REST API
- `Endpoints/` - FastEndpoints definitions
- `Filters/` - Request/response filters
- `Middleware/` - Custom middleware

**BeanShare.BlazorWeb** - Web application
- `Components/` - Razor components
- `Endpoints/` - Account endpoints (login/register)
- `Services/` - UI services

**BeanShare.Maui** - Mobile application
- `Components/` - Mobile-specific components
- `Services/` - Platform-specific services
- `Platforms/` - Platform-specific code

**BeanShare.SharedUi** - Shared components
- Reusable Razor components
- Shared CSS and assets

## Contributing

1. Follow Clean Architecture principles
2. Write unit tests for new features
3. Update documentation as needed
4. Follow C# coding conventions
5. Use meaningful commit messages

## License

This project is licensed under the [GNU Affero General Public License v3.0](LICENSE).

Authored by Oliver Golec as a diploma thesis at the Faculty of Information Technology, Brno University of Technology (VUT FIT), under the supervision of Ing. Jan Pluskal, Ph.D.

For issues and questions, create an issue in the repository.
