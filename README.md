# Callout

![CI](https://github.com/Botaggg/pp/actions/workflows/ci.yml/badge.svg)

[Live request form](https://callout-marin-cfe8guhza9cnfsf0.northcentralus-01.azurewebsites.net/RequestForm)

Phase 0/1 verification and remaining release steps: [audit](docs/phase-0-1-audit.md).

Phase 2 is implemented: [offline availability engine and scheduling rules](docs/phase-2.md).

Booking and invoicing system for a solo mobile tech-help business. Clients submit a
request form, requests land in an admin queue, and the operator confirms a slot,
logs the job, and produces an invoice.

**Stack:** .NET 10, ASP.NET Core Razor Pages, EF Core on PostgreSQL, xUnit.

## Project structure

```
Callout.slnx
  src/Callout.Core            domain models + business rules, zero dependencies
  src/Callout.Infrastructure  EF Core (CalloutDbContext)
  src/Callout.Web             Razor Pages (public request form + admin queue)
  tests/Callout.Tests         xUnit tests
```

Dependencies point one direction only: `Core` references nothing,
`Infrastructure` to `Core`, `Web` to `Core` and `Infrastructure`, and `Tests` to
`Core` and `Web` for HTTP/database integration tests.

PostgreSQL is used in every environment. This replaces the original build plan's
SQLite development option, keeping local and production migrations identical.

## Configuration

Nothing secret lives in the repo. Use separate development credentials in user-secrets
and production credentials in App Service application settings. Never point local
development or tests at the production database.

```bash
# 1. Database (Postgres in every environment)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=...;Database=callout;Username=...;Password=...;SSL Mode=VerifyFull;Channel Binding=Require" \
  --project src/Callout.Web

# 2. Admin login. Generate the hash, then store the hash, never the password.
dotnet run --project src/Callout.Web -- hash-password

dotnet user-secrets set "Admin:Username" "gleb" --project src/Callout.Web
dotnet user-secrets set "Admin:PasswordHash" "<paste the hash>" --project src/Callout.Web
```

Production also requires `Admin__TotpSecret` and hashed recovery codes. Follow the
[security setup and recovery procedure](docs/security-hardening.md). Admin passwords
must have at least 16 characters; hashes use Identity V3 with at least 100,000 iterations.

## Database

The runtime database account cannot change the schema. Supply a separate migration
connection through `ConnectionStrings__MigrationConnection` in the migration
process environment, then run:

```bash
dotnet ef database update -p src/Callout.Infrastructure -s src/Callout.Web
```

Never save production migration credentials as the local application's default connection.

Migrations are scaffolded against a design-time factory, so adding one does not need
a live database:

```bash
dotnet ef migrations add <Name> -p src/Callout.Infrastructure -s src/Callout.Web
```

## Build, test, run

```bash
dotnet build
dotnet test
dotnet run --project src/Callout.Web
```

CI runs `restore`, `build`, and `test` on every push and pull request to `main`
(see [.github/workflows/ci.yml](.github/workflows/ci.yml)).

Run only the Phase 2 availability tests, without a database or network dependency:

```bash
dotnet test --no-build --no-restore --configuration Release --filter 'Phase=2'
```

Build once before using `--no-build`. The 38 availability tests use an in-memory
calendar and fixed clock. The live request form remains the Phase 1 flow until
Phase 3 adds the admin slot picker.

## HTTP and database tests

The integration suite exercises real Razor Pages requests, authentication,
antiforgery, rate limiting, and EF migrations against PostgreSQL 18. Point it at a
**local or disposable test server** whose user can create databases:

```bash
CALLOUT_TEST_CONNECTION_STRING='Host=localhost;Database=postgres;Username=postgres;Password=your-local-test-password' \
  dotnet test --configuration Release
```

Tests create a randomly named `callout_test_*` database, apply the checked-in
migrations, and remove that database when finished. They never read application
database credentials. Without this variable, the database tests are explicitly
skipped; a unit-only pass does not verify Phase 1.

Both CI and the Azure deployment workflow provide a temporary PostgreSQL service.
Deployment runs the tests before publishing only `Callout.Web`.

## Security

See [security hardening and operations](docs/security-hardening.md) for admin MFA, session revocation, database permissions and migration procedures.
