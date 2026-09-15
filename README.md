# Callout

![CI](https://github.com/Botaggg/pp/actions/workflows/ci.yml/badge.svg)

Booking and invoicing system for a solo mobile tech-help business. Clients submit a
request form, requests land in an admin queue, and the operator confirms a slot,
logs the job, and produces an invoice.

**Stack:** .NET 10, ASP.NET Core Razor Pages, EF Core (SQLite locally / Postgres in
production), xUnit.

## Project structure

```
Callout.slnx
  src/Callout.Core            domain models + business rules, zero dependencies
  src/Callout.Infrastructure  EF Core (CalloutDbContext)
  src/Callout.Web             Razor Pages (public request form + admin queue)
  tests/Callout.Tests         xUnit tests
```

Dependencies point one direction only: `Core` references nothing,
`Infrastructure` → `Core`, `Web` → `Infrastructure`, `Tests` → `Core`.

## Build & test

```bash
dotnet build
dotnet test
```

CI runs `restore`, `build`, and `test` on every push and pull request to `main`
(see [.github/workflows/ci.yml](.github/workflows/ci.yml)).

## Run locally

```bash
dotnet run --project src/Callout.Web
```

The app uses SQLite by default (`Data Source=callout.db`).
