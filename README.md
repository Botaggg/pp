# Callout

![CI](https://github.com/Botaggg/pp/actions/workflows/ci.yml/badge.svg)

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
`Infrastructure` to `Core`, `Web` to `Infrastructure`, `Tests` to `Core`.

## Configuration

Nothing secret lives in the repo. Both values below come from user-secrets locally
and from App Service application settings in production.

```bash
# 1. Database (Postgres in every environment)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=...;Database=callout;Username=...;Password=...;SSL Mode=Require" \
  --project src/Callout.Web

# 2. Admin login. Generate the hash, then store the hash, never the password.
dotnet run --project src/Callout.Web -- hash-password

dotnet user-secrets set "Admin:Username" "gleb" --project src/Callout.Web
dotnet user-secrets set "Admin:PasswordHash" "<paste the hash>" --project src/Callout.Web
```

## Database

```bash
dotnet ef database update -p src/Callout.Infrastructure -s src/Callout.Web
```

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
