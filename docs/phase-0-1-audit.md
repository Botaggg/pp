# Phase 0 and Phase 1 verification

Rechecked September 15, 2026 against `main` at `29edade`, the current source,
a fresh PostgreSQL 18 test database and GitHub Actions. This recheck adds tests
and documentation only; production application behavior is unchanged.

## Verdict

Phase 0 and the Phase 1 technical implementation are complete. The original
Phase 1 acceptance criterion also requires a real customer submission. The
verified production request is labelled as test data, so customer rollout must
remain open. No customer was contacted during this verification.

Phase 2 is already implemented. Its independent offline suite passes, and the
full HTTP/database suite shows no regression in the request flow.

## Phase 0

| Requirement | Evidence | Result |
| --- | --- | --- |
| .NET 10 layered solution | SDK 10.0.401; Core, Infrastructure, Web and Tests build | Pass |
| Core has no external dependencies | Core project has no package or project references | Pass |
| Repository and CI badge | Botaggg/Database-callout; README badge uses current repository name | Pass |
| CI on main | [CI run for 29edade](https://github.com/Botaggg/Database-callout/actions/runs/35016068359) | Pass |
| Test-gated Azure deployment | [Deployment for 29edade](https://github.com/Botaggg/Database-callout/actions/runs/35016068335) | Pass |
| Security analysis | [Security run for 29edade](https://github.com/Botaggg/Database-callout/actions/runs/35016068388) | Pass |

These workflow results apply to the deployed baseline. Results for subsequent
commits must be checked separately.

## Phase 1

| Requirement | Verification | Result |
| --- | --- | --- |
| Client and Booking models | Required fields, relationship, unique email and booking-date indexes | Pass |
| PostgreSQL migrations | Fresh database receives both checked-in migrations; model matches schema; reapplying is safe | Pass |
| UTC timestamp storage | Database tests verify UTC values; migration columns use timestamp with time zone | Pass |
| Public request fields | Name, phone, email, address, needs and optional rough availability | Pass |
| Valid submissions | Blank optional fields accepted; client and booking stored; confirmation redirect returned | Pass |
| Invalid submissions | Validation, honeypot and missing antiforgery token cannot create requests | Pass |
| POST rate limits | Per-client and aggregate budgets tested; reads and rejected traffic do not spend unrelated capacity | Pass |
| Admin authentication | Framework password hashing, role checks, secure HTTP-only cookies and production MFA | Pass |
| Protected queue | Anonymous access redirects; authenticated operator sees requests; logout revokes copied cookies | Pass |
| Repeat and concurrent clients | Email reuse preserves original client profile and each booking's submitted contact details | Pass |
| Pacific display and pagination | Queue converts UTC to Pacific; page tests prevent missing or repeated requests | Pass |
| Real customer rollout | No verified real-customer submission | Open |

## Local verification

- Locked dependency restore completed.
- Release build completed with zero warnings and zero errors.
- Full suite: **74 passed, 0 failed, 0 skipped** against a new PostgreSQL 18
  cluster bound to loopback on port 55433.
- Tests created randomly named databases, applied migrations and removed those
  databases after execution. No application or production database credentials
  were used for these tests.
- Offline Phase 2 suite: **46 passed, 0 failed, 0 skipped** with the test database
  connection variable unset and with build/restore disabled.
- The availability input-limit test now lives outside the PostgreSQL fixture and
  participates in the Phase 2 filter. Additional checks cover maximum search
  duration, calendar event count, cancellation, clock consistency and date limits.

The first local test attempt used a role absent from the existing development
cluster. Creating a separate test cluster resolved the environment problem; no
application change was needed.

## Production verification record

The original labelled request was submitted through the live public form on
September 15, 2026 at 18:44 UTC and independently verified in Neon and the admin
queue. Its client and booking IDs are both 1, its status is Requested, and its
scheduled start/end are null. It is not an appointment or a customer request.

The current live recheck confirmed that `/`, `/RequestForm`, `/Confirmation`,
`/Login` and the local stylesheet return HTTP 200. Both request routes contain
all expected fields and antiforgery tokens. Login offers MFA, anonymous admin
access redirects to login, and responses carry the strict Content Security Policy.
A fresh read-only Neon query confirmed both applied migrations and the original
labelled test request still present with Requested status. A second request is
not labelled as test data; the owner confirmed it was another test. Neither
request satisfies the real-customer milestone.

A new authenticated production login could not be completed because macOS
Keychain was waiting for access to the saved password. The successful live MFA
and logout verification below is historical; the current local regression tests
passed for both behaviors.

The latest successful deployment is `29edade`. Earlier live verification covered
MFA login, logout revocation, antiforgery and browser security headers, documented
in [security operations](security-hardening.md).

## Changes since the earlier audit

The prior follow-up items have been implemented: local development uses a
separate database, the queue displays Pacific time, concurrent email submissions
recover from unique-index conflicts, and forwarded headers trust only configured
proxies. Runtime and migration database permissions are separated. Production
requires MFA and server-side session validation.

The original PostgreSQL-in-all-environments decision remains in effect. There is
one database provider and one migration history for local tests and production.

## Remaining milestone

A real client needs to submit the [live request form](https://callout-marin-cfe8guhza9cnfsf0.northcentralus-01.azurewebsites.net/)
and have their request appear in the production queue. This is an operational
milestone, not a missing Phase 0/1 feature, and does not prevent work on the offline
Phase 2 engine.
