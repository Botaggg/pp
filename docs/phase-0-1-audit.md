# Phase 0 and Phase 1 audit

Verified September 15, 2026 against source, GitHub Actions, Azure App Service,
and the connected Neon production database.

## Verdict

**The deployed version does not complete Phase 1.** Its honeypot is implicitly
required by ASP.NET Core, while its handler rejects populated honeypots. A normal
request cannot be saved. Fixes are implemented and tested locally, but production
acceptance remains pending deployment and a real client submission.

## Phase 0

| Requirement | Evidence | Status |
| --- | --- | --- |
| .NET 10 and layered solution | SDK 10.0.401; Core, Infrastructure, Web, Tests build successfully | Pass |
| Domain has no external dependencies | Core project has no package or project references | Pass |
| GitHub repository and CI badge | `Botaggg/pp`; README badge and CI workflow present | Pass |
| Green CI after a push | [CI run for 65d9036](https://github.com/Botaggg/pp/actions/runs/34925969933) | Pass for deployed revision |
| Azure deployment | [Deployment run for 65d9036](https://github.com/Botaggg/pp/actions/runs/34925969994); App Service running on .NET 10 with HTTPS required | Pass for deployed revision |
| Public URL | `/RequestForm` returns 200; `/` returns 404 on the deployed version | Home route fixed locally |

The Azure subscription is an enabled Azure for Students subscription.

## Phase 1

| Requirement | Evidence | Status |
| --- | --- | --- |
| Client and Booking models | Domain classes, relationship, field limits, requested status | Pass |
| Real PostgreSQL database | Neon project `callout`, production branch, database `neondb` | Pass |
| Initial migration applied | `20260915011613_InitialCreate`, EF 10.0.12, in production migration history | Pass |
| Correct schema | `Clients`, `Bookings`, foreign key, unique email index, booking date index | Pass |
| UTC timestamp storage | All stored timestamps use PostgreSQL `timestamp with time zone` | Pass |
| Public request fields | Name, phone, email, address, needs, availability | Present |
| Form accepts valid submissions | Empty honeypot and availability no longer get implicit required validation | Fixed and tested locally |
| Spam rejection | Filled honeypot redirects to confirmation without inserting data | Verified locally |
| Fixed-window POST limits | 10 request POSTs per 10 minutes, 10 login POSTs per 15 minutes | GET quota bug fixed locally |
| Confirmation | Valid form writes client and booking, then redirects to confirmation | Verified locally with PostgreSQL |
| Single-admin cookie authentication | Framework PasswordHasher, configured credentials, secure HTTP-only production cookie | Verified locally; blank-login error fixed |
| Protected admin queue | Anonymous access redirects to login; authenticated access shows the persisted request; logout revokes access | Verified locally |
| Real production submissions | Production contained 0 clients and 0 bookings at audit time | Pending |

Production connection and admin configuration are present and match the local
configured values. Secret values were not included in this report. Production
queries inspected schema and aggregate counts; they did not create test bookings.

PostgreSQL in every environment is the implemented replacement for the original
SQLite development plan. One provider and migration set now cover local tests and
production.

## Changes made

- Made the honeypot and rough availability nullable and safely stored blank
  availability as an empty string.
- Added the request form at `/`, retaining `/RequestForm`.
- Excluded page views from both POST rate-limit quotas.
- Handled empty login credentials as failed authentication instead of a server error.
- Displayed each booking's address in the admin queue.
- Added HTTP/PostgreSQL integration tests and PostgreSQL services in both workflows.
- Made deployment run tests before publishing only the web project.
- Corrected documentation that prematurely marked Phase 1 complete and claimed
  `IBusyCalendar` already existed.

## Validation

- Baseline: 7 failing and 6 passing tests exposed the submission, home route,
  rate-limit, and blank-login problems.
- After fixes: **13 passed, 0 failed, 0 skipped**, Release configuration.
- A new isolated PostgreSQL 18 database received the checked-in migration. Tests
  verified persistence, repeat-client matching, booking-address preservation,
  UTC values, validation, honeypot rejection, antiforgery, request throttling,
  login, protected queue access, logout, and migration/model consistency.
- Test databases are created with unique names and removed after execution.
- These changes have not yet run on GitHub Actions or been deployed to Azure.
- PostgreSQL 18 was installed locally for verification. The temporary test server
  was stopped afterward; no background service was enabled.

## Remaining acceptance steps

1. Publish the reviewed changes through the tested deployment workflow.
2. Verify `/` and `/RequestForm` load on the deployed revision.
3. Submit a clearly labelled verification request and confirm its client and
   booking records in Neon and in the authenticated admin queue.
4. Have a real client submit a request. This is the original Phase 1 acceptance
   criterion and cannot be replaced by an automated test.

## Follow-up considerations

- Local application user-secrets currently target the production database. Use a
  separate development database for normal development, as the tests already do.
- Admin timestamps currently use the server's local timezone. Configure Pacific
  display time before adding scheduled appointments.
- Concurrent first requests using the same email can race on the unique client
  index. Add conflict recovery before treating concurrent submissions as supported.
- Forwarded headers currently trust all proxies. Confirm the Azure ingress trust
  boundary before exposing this app behind any additional proxy or direct endpoint.

The existing untracked `MyWebApp`, `ScriptProject`, and `publish.zip` files were
left intact. The checkout was fast-forwarded to the already-published deployment
workflow commit before making these fixes.
