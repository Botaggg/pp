# Security hardening

## Controls implemented

- Anonymous requests preserve submitted name, phone, email and address on the booking. Matching an email never authorizes changes to the saved client. Existing booking contacts were backfilled from the best available client record; changes made before this migration cannot be reconstructed.
- Concurrent first requests for an email are recovered after PostgreSQL's unique constraint rejects one insertion. Every accepted request retains its own details.
- Admin access requires the Admin role, the configured password and an authenticator code in Production. The authenticator secret contains 160 random bits. Codes use the Otp.NET implementation of RFC 6238 with a 30-second step and one adjacent step on either side for clock drift. Every accepted time step and recovery code is consumed atomically in PostgreSQL.
- Cookies expire after 30 minutes without renewal. Sessions have an independent eight-hour absolute limit. Each authenticated request checks the server record and credential version. Logout revokes the server record; password or authenticator rotation invalidates existing sessions. Existing pre-migration cookies have no session record and are rejected.
- Admin pages return at most 50 requests per page and identify visitor-supplied contact details as unverified.
- Forms have antiforgery protection, field validation, a 16 KiB request limit and rate limits. Login has an additional aggregate limit of 30 POSTs per 15 minutes; other POSTs have an aggregate limit of 100 per 15 minutes. Limits are per application process. The current single-instance deployment is the supported configuration. Multiple instances require a shared limiter or an ingress enforcement service.
- Forwarded headers only trust loopback defaults and explicitly configured proxy IPs. There is no trust-all configuration. On Azure, `Hosting__HttpsTerminated=true` describes the platform's HTTPS-only ingress. Arbitrary forwarded headers cannot change that deployment setting. Automatic unrestricted forwarding must remain disabled. Unrecognized proxy addresses fall back to shared throttling, which favors security but can reject legitimate requests under load.
- The browser receives local CSS only, no external executable scripts. Content Security Policy blocks scripts, frames, external styles, object embedding and external form submissions. Responses also include nosniff, no-referrer and restricted browser permissions.
- Production database connections require `SSL Mode=VerifyFull` and use channel binding. Runtime role `callout_web` can read/insert clients and bookings, use their ID sequences, and maintain session/code records. It cannot edit clients, delete bookings, create schema objects, create roles, or bypass row security. Migration credentials are separate.
- Local development uses an empty local PostgreSQL database and different admin credentials, never production defaults.
- Availability searches are limited to 366 days and 366 windows, a minimum one-minute step and at most 10,000 returned calendar intervals. Time-zone and buffer boundaries are validated.
- NuGet dependencies use committed lock files and restore audits fail builds on known vulnerability advisories or audit-service errors. GitHub Actions are pinned to commit IDs. Dependabot checks NuGet and actions weekly. CodeQL runs on pushes, pull requests and a weekly schedule.

## Operations

### Admin authenticator

Use the private setup file supplied during deployment to add a time-based account to an authenticator. Never put the setup key or recovery codes into this repository. A recovery code also requires the admin password and can be used once. Store recovery codes separately from the authenticator. The existing production password was saved and verified in macOS Keychain under `Callout production admin`.

For administrative recovery, an Azure administrator can replace `Admin__TotpSecret` and `Admin__RecoveryCodeHashes__0` through `__7` in App Service settings. Generate a new random secret and random 128-bit recovery codes; store only uppercase SHA-256 hashes of the uppercase recovery codes. Restarting with the new secret invalidates old sessions. Do not disable MFA to recover access. An authenticator device must be enrolled by its owner.

### Database migrations

Migrations are explicit deployment work, never web-server startup work. Supply the owner connection only through `ConnectionStrings__MigrationConnection` to a migration command. Test against an isolated database first, then apply the additive migration before releasing the corresponding app version. Normal runtime settings must continue to use `callout_web`. A rollback must preserve the added history and session tables; do not run the destructive migration `Down` against live data.

Local development is `callout_dev` on `127.0.0.1:55432`. Start its existing isolated PostgreSQL cluster when needed:

```sh
/opt/homebrew/opt/postgresql@18/bin/pg_ctl -D /tmp/callout-phase01-pg/data -l /tmp/callout-phase01-pg/server.log -o '-h 127.0.0.1 -p 55432 -k /tmp/callout-phase01-pg' start
```

This development cluster contains no production copy. Its `/tmp` location is disposable. Recreate and migrate it if the OS removes it. Tests create and delete only their own random databases using `CALLOUT_TEST_CONNECTION_STRING`.

### Ongoing responsibilities

Review dependency and CodeQL alerts, keep Azure and GitHub administrator accounts protected by MFA, periodically test restoring database backups, and update protections as new endpoints or integrations are added. The public form accepts unverified requests; verify the requester before granting access to existing customer information or making sensitive changes. This hardening closes the audited paths and adds regression checks; it does not guarantee freedom from future vulnerabilities, phishing, provider outages, or denial of service.
