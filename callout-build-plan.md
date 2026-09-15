# Callout build plan

Callout handles requests, scheduling and invoicing for a solo mobile tech-help business in Marin County.

**Repository:** [Botaggg/Database-callout](https://github.com/Botaggg/Database-callout)

**Current stack:** .NET 10, ASP.NET Core Razor Pages, EF Core, PostgreSQL and xUnit.
Google Calendar and PDF invoicing are planned integrations.

## Architecture

| Project | Responsibility | Dependencies |
| --- | --- | --- |
| Callout.Core | Domain models and scheduling rules | None |
| Callout.Infrastructure | Database and calendar adapters | Core |
| Callout.Web | Public request form and admin pages | Core, Infrastructure |
| Callout.Tests | Domain tests and HTTP/database integration tests | Core, Web; Infrastructure through Web |

Razor Pages keeps the application in one language and one deployment. Core has no
external dependencies so scheduling and billing rules can run without a database
or network. `IBusyCalendar` separates slot generation from calendar providers.

Use PostgreSQL in every environment and store timestamps in UTC. Convert dates and
times to Pacific time for scheduling and display. Keep credentials outside source
control, use separate development and production databases, and apply migrations
with a separate account from the web application's runtime account.

## Phase 0: Deployment and continuous integration

**Status: technically complete.** [Verification](docs/phase-0-1-audit.md)

- .NET 10 solution with Core, Infrastructure, Web and Tests projects.
- GitHub CI restores locked dependencies, builds and tests each change.
- Azure App Service hosts the public application over HTTPS.
- Deployment tests the application before publishing the web project.

Acceptance: a pushed revision passes CI, deploys successfully and serves the live
request form. Security analysis also runs on pushes and pull requests.

## Phase 1: Public requests and admin queue

**Status: technically complete; real-customer rollout remains unverified.**
[Verification](docs/phase-0-1-audit.md)

- Client and Booking models with an EF Core migration applied to PostgreSQL.
- Public form for name, phone, email, address, needs and rough availability.
- Honeypot, validation, antiforgery protection and POST rate limits.
- Confirmation after a valid request is stored.
- Protected admin queue with single-operator cookie authentication.
- Production MFA, server-side session revocation and limited database permissions.
- Submitted contact details remain attached to each booking without allowing an
  anonymous visitor to overwrite an existing client's profile.

Technical acceptance: a labelled test request reaches production and appears in
the authenticated queue. Business acceptance: an actual client submits a request
and it appears in the production database. Test data does not satisfy the latter.

## Phase 2: Offline availability engine

**Status: implemented and verified.** [Scheduling rules and tests](docs/phase-2.md)

- Validated UTC `Interval` values and the `IBusyCalendar` contract in Core.
- `FakeBusyCalendar` supplies in-memory busy intervals for tests and demos.
- Inclusive `AvailabilityWindow` date ranges describe when the operator is in Marin.
- `SlotGenerator` applies the availability pipeline:
  1. Intersect availability windows with the requested dates.
  2. Apply Pacific working hours, 9am to 7pm by default.
  3. Read overlapping busy intervals, including nearby events outside working hours.
  4. Expand busy time by 30 minutes of travel time on each side and merge blocks.
  5. Exclude blocked time.
  6. Produce one-hour candidates on a 30-minute grid.
  7. Exclude starts less than 24 elapsed hours away.

Acceptance: deterministic tests pass without a database, web server or network
call. Coverage includes overlapping blocks, swallowed gaps, absent availability,
notice boundaries, daylight saving, cancellation and bounded input sizes.

Candidate slots are alternatives. This phase does not reserve appointments or
write to a calendar. The live form remains the Phase 1 request flow.

## Phase 3: Booking status and admin confirmation

**Status: planned.**

- Centralize legal transitions: Requested -> Confirmed -> Completed -> Invoiced -> Paid.
- Allow cancellation from Requested or Confirmed; reject illegal transitions.
- Add an authenticated booking detail page and slot picker.
- Persist/manage availability windows and supply known busy time to the engine.
- Recheck availability at confirmation and protect against conflicting bookings.
- Save scheduled start and end times in UTC.

Acceptance: an operator can confirm a request through the admin flow, with tests
for valid transitions, illegal transitions and conflicting confirmations.

## Phase 4: Time entries and billing

**Status: planned.**

- Add booking time entries with start, end and notes.
- Calculate billable minutes with a pure function:

```text
billable_minutes = max(minimum_minutes, round_up_to_15(actual_minutes))
amount = billable_minutes / 60 * hourly_rate
```

Keep the $30 minimum and $30 hourly rate independently configurable. Test 1, 14,
15, 16, 59, 60, 61, 75 and 76 minutes, plus invalid durations.

Acceptance: recorded time produces the expected charge for a completed booking.

## Phase 5: Invoices and PDF

**Status: planned.**

- Add Invoice and InvoiceLine models.
- Generate invoice lines from completed booking time entries.
- Produce a downloadable PDF containing business details, client details, lines
  and totals. Evaluate QuestPDF licensing before adopting it.
- Keep delivery manual for the first release.

Acceptance: an operator can download an accurate invoice for a completed job.

## Phase 6: Google Calendar integration

**Status: planned.**

- Configure Google Calendar access and store refresh credentials outside the repo.
- Implement `GoogleBusyCalendar` through `IBusyCalendar` using FreeBusy queries.
- Create calendar events when bookings are confirmed and save their event IDs.
- Update or delete the corresponding events on reschedule or cancellation.
- Surface provider errors and prevent retries from creating duplicate events.

Acceptance: live calendar conflicts remove candidates and confirmed appointments
synchronize correctly. Keep the offline scheduling suite independent of OAuth.

## Phase 7: Release documentation and operating metrics

**Status: planned.**

- Add screenshots of the public form and completed admin scheduling flow.
- Keep setup instructions, architecture decisions and test commands current.
- Track actual bookings, clients, hours and invoices without including test data.
- Review backup restoration, dependency alerts and access controls.

## Scope and delivery rules

Each phase should leave a working, deployable application. Use small changes with
accurate commit messages and verification records. Keep incomplete capabilities
labelled as planned.

Deposits, online payments, SMS reminders and a client account portal are outside
v1. Calendar integration and invoice styling can follow the core request and
scheduling workflows without blocking them.
