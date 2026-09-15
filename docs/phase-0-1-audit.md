# Phase 0 and Phase 1 audit

Verified September 15, 2026 against source, GitHub Actions, Azure App Service,
and the connected Neon production database.

## Verdict

**Phase 0 and the Phase 1 technical requirements are complete and verified in
production.** Commit `e4020a8` fixes the request flow, home route, rate limiting,
and blank-login handling. Both GitHub workflows passed and Azure deployed the
fixes. A labelled test request passed through the live public form, Neon, and
the authenticated admin queue. Admin login and logout were verified live.

The original plan also calls for a real client to use the form. That customer
rollout milestone remains separate: the verification record is explicitly test
data, and no client was contacted as part of this work.

## Phase 0

| Requirement | Evidence | Status |
| --- | --- | --- |
| .NET 10 and layered solution | SDK 10.0.401; Core, Infrastructure, Web, Tests build successfully | Pass |
| Domain has no external dependencies | Core project has no package or project references | Pass |
| GitHub repository and CI badge | `Botaggg/pp`; README badge and CI workflow present | Pass |
| Green CI after a push | [CI run for e4020a8](https://github.com/Botaggg/pp/actions/runs/35009071105) | Pass for deployed revision |
| Azure deployment | [Deployment run for e4020a8](https://github.com/Botaggg/pp/actions/runs/35009071055); App Service running on .NET 10 with HTTPS required | Pass for deployed revision |
| Public URL | `/` and `/RequestForm` both return 200 with the corrected form | Pass |

The Azure subscription is an enabled Azure for Students subscription. App Service
plan `ASP-calloutrg-9ba5` uses the Free/F1 tier.

## Phase 1

| Requirement | Evidence | Status |
| --- | --- | --- |
| Client and Booking models | Domain classes, relationship, field limits, requested status | Pass |
| Real PostgreSQL database | Neon project `callout`, production branch, database `neondb` | Pass |
| Initial migration applied | `20260915011613_InitialCreate`, EF 10.0.12, in production migration history | Pass |
| Correct schema | `Clients`, `Bookings`, foreign key, unique email index, booking date index | Pass |
| UTC timestamp storage | All stored timestamps use PostgreSQL `timestamp with time zone` | Pass |
| Public request fields | Name, phone, email, address, needs, availability | Present |
| Form accepts valid submissions | Empty honeypot and availability no longer get implicit required validation | Pass, live and local |
| Spam rejection | Filled honeypot redirects to confirmation without inserting data | Pass, live and local |
| Fixed-window POST limits | 10 request POSTs per 10 minutes, 10 login POSTs per 15 minutes | Pass locally; page views also verified live |
| Confirmation | Valid form writes client and booking, then redirects to confirmation | Pass, live and local PostgreSQL |
| Single-admin cookie authentication | Framework PasswordHasher, configured credentials, secure HTTP-only production cookie | Pass, live and local |
| Protected admin queue | Anonymous access redirects to login; authenticated access shows the persisted request; logout revokes access | Pass, live and local |
| Production persistence | Labelled verification client 1 and booking 1 saved through the live form | Pass |
| Real client usage | No actual customer submission was performed or claimed | Customer rollout step |

Production connection and admin configuration are present and match the local
configured values. The original admin hash was a truncated placeholder, so a
strong password and valid framework hash were generated and configured. Only the
hash is stored in Azure and application user-secrets. The login is saved in the
owner-only local file `~/.config/callout/admin-credentials.txt`, outside the
repository. Secret values are not included in this report.

Production now contains one labelled verification client and booking. It was
submitted through the public HTTP form, not inserted directly with SQL. Read-only
Neon queries independently confirmed:

- Client ID: `1`; booking ID: `1`; status: `Requested`.
- Created at: `2026-09-15T18:44:22.382Z`.
- Blank availability persisted as an empty string.
- Scheduled start and end remain null.
- Honeypot and invalid submissions created no client rows.
- The authenticated admin queue displays the test email and booking address.

The record says `[TEST] Phase 0/1 verification` and `TEST ONLY - no service visit`.
It is not a customer request or an appointment.

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
- GitHub CI and the test-gated Azure deployment both succeeded for `e4020a8`.
- Live HTTP checks passed for valid submission, confirmation, optional fields,
  validation, honeypot rejection, antiforgery, repeated page views, incorrect and
  blank login, valid admin login, secure HTTP-only cookie, queue visibility, and
  logout. Neon independently confirmed the persisted record and rejected inputs.
- PostgreSQL 18 was installed locally for verification. The temporary test server
  was stopped afterward; no background service was enabled.

## Customer rollout

The app is ready for a real client to submit a request at the
[live form](https://callout-marin-cfe8guhza9cnfsf0.northcentralus-01.azurewebsites.net/).
Using it with an actual customer satisfies the original plan's real-client
milestone. No additional Phase 0/1 feature implementation is required.

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
