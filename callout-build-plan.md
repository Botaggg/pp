# Callout: Complete Build Plan

Booking and invoicing system for a solo mobile tech-help business.

**Repo:** `github.com/Botaggg/pp`
**Stack:** .NET 10, ASP.NET Core Razor Pages, EF Core, PostgreSQL in all environments, Google Calendar API, QuestPDF, xUnit
**Timeline:** 8 weekends, each ending in something shippable

---

## Part 1: What this is and why it exists

### The problem being solved

You run a mobile tech-help business in Marin County at $30/hr with a $30 minimum. Right now the workflow is: someone texts you, you check your calendar by hand, you text back a time, you remember roughly how long the job took, and you tell them what they owe. That works at your current volume and breaks as soon as it grows.

Callout automates that loop.

### The loop

1. A client fills out a public request form on your site
2. The request appears in your admin queue
3. You open it and see a list of slots the app computed from your actual calendar, your travel buffers, and the dates you're physically in Marin
4. You pick one and confirm. The app writes the event to your Google Calendar with a reminder
5. After the job you log your start and end time
6. The app applies your billing rule and produces an invoice PDF you download and email

### Why you are building it

This is the important part, and it changes the priorities.

Callout is useful to your business, but that is not the main reason to build it. You are a first-year CS student applying for software internships with an empty GitHub. Your business experience is real and unusual, but a hiring manager screening for a dev role cannot verify a single line of code from it.

Callout converts your business from a soft credential into a technical one. After it exists, your pitch is "I run an IT business and I built the software that runs it, here is the live link and here is the source." That is a genuinely strong position for a first-year, because it proves three things most student projects prove none of:

- You can ship, not just code. It is deployed and publicly reachable.
- You solve real problems. Actual people submit actual requests to it.
- You can structure a codebase. Tests, CI, layered projects, no spaghetti.

**The consequence for the plan:** visibility beats completeness. A rough version live in three weeks is worth more than a polished one in February, because summer 2027 internship applications are open now and many close in November and December.

---

## Part 2: How it is architected

### Project layout

```
Callout.sln
  src/Callout.Core            domain models + business rules, ZERO dependencies
  src/Callout.Infrastructure  EF Core, Google Calendar client
  src/Callout.Web             Razor Pages
  tests/Callout.Tests
```

Dependency arrows point one direction only:

```
Core            (references nothing)
Infrastructure  → Core
Web             → Core, Infrastructure
Tests           → Core, Infrastructure
```

**Why this split matters.** Core having zero dependencies means your billing rule and your slot generator can be tested without a database, without a network call, and without starting the web app. Tests run in milliseconds. It is also the first structural thing an engineer notices when they open your repo, and it gives you a concrete answer when someone asks how you organized the project.

**Why Razor Pages and not an API plus a React frontend.** You write C#. You do not write JavaScript and you do not claim to. Razor Pages lets you build the entire thing in one language with server-rendered HTML. A separate frontend would add a second language, a build pipeline, and a deployment target, all for an app with one admin user. Choosing the simpler tool deliberately is a better answer in an interview than reaching for a stack you can't defend.

### Data model

| Model | Fields |
|---|---|
| **Client** | name, phone, email, address, notes |
| **Booking** | client, description, status, scheduled start/end, address, google event id |
| **TimeEntry** | booking, start, end, notes |
| **Invoice** | client, issue date, status, lines |
| **InvoiceLine** | description, billable minutes, rate, amount |
| **AvailabilityWindow** | date range when you're actually in Marin |

`AvailabilityWindow` exists because you live in Merced and your clients are in Marin. No amount of free calendar time on a Tuesday matters if you are 120 miles away. This is the constraint that makes the availability problem genuinely interesting rather than a generic calendar lookup.

### The three rules worth testing

**Billing rule.** Pure function, no dependencies:

```
billable = max(MINIMUM_MINUTES, ceilTo15(actualMinutes))
amount   = billable / 60 * HOURLY_RATE
```

Your $30 minimum happens to equal exactly one hour at your $30 rate. Write them as two separate constants anyway. If you raise your rate to $35 later and they share one value, your minimum silently changes too.

Boundary tests: 1, 14, 15, 16, 59, 60, 61, 75, 76 minutes.

**Availability pipeline.** Seven steps:

1. Start from `AvailabilityWindow` records (which dates you're in Marin)
2. Apply a daily working-hours template (say 9am to 7pm)
3. Query the calendar for busy intervals
4. Subtract those busy blocks
5. Subtract a travel buffer on each side of every existing booking
6. Slice the remainder into one-hour slots at 30-minute granularity
7. Drop anything less than 24 hours in the future

Edge cases to test: overlapping busy blocks, a travel buffer that swallows an entire gap, a day with no availability window, a slot straddling the DST boundary, a request made 20 hours out.

**Status machine.**

```
Requested → Confirmed → Completed → Invoiced → Paid
Cancelled reachable from anything before Completed
```

One method owns every transition and throws on illegal moves. Pages call it, pages never check status themselves. Scattered `if (booking.Status == ...)` checks across six Razor pages is exactly how you end up invoicing a cancelled job.

### Two decisions that save you weeks

**1. Store every timestamp in UTC from the very first migration.** Convert only at display time. Retrofitting this is genuinely painful, and if you don't, daylight saving in November will quietly move your bookings by an hour and you will not notice until a client is standing on a porch alone.

**2. Put the calendar behind an interface.** Define this in Core:

```csharp
public interface IBusyCalendar
{
    Task<IReadOnlyList<Interval>> GetBusyAsync(
        DateTimeOffset from, DateTimeOffset to);
}
```

Core says what it needs. Infrastructure supplies it later. You build and fully test the entire availability algorithm against a `FakeBusyCalendar` returning hardcoded intervals, then swap in the real Google implementation in Phase 6 with no other code changes.

This is not architecture for its own sake. Google OAuth is the single most likely thing in this project to eat an entire weekend and leave you with nothing to show. The interface means that even in the worst case, the most interesting code you will write still exists and is still tested.

---

## Part 3: The build, phase by phase

### Phase 0: Empty app, live, with CI

**Weekend 1.**

1. Install the .NET 10 SDK
2. `dotnet new sln -n Callout`
3. Create the four projects and wire the references as shown above
4. `dotnet new gitignore`, initial commit, push to `github.com/Botaggg/pp`
5. Add `.github/workflows/ci.yml` that runs restore, build, and test on every push. Put the badge in the README
6. Create an Azure for Students account with your ucmerced.edu address. This gives you credit with no credit card
7. Deploy the blank Web project to App Service free tier

**Why deploy before writing any features.** Student projects die at deployment because it gets saved for last, at the point where the app is complex enough that you cannot tell whether a failure is your code or your config. Deploy an empty page and every later phase is just a push.

**Why CI now, before there are any tests to run.** It costs an hour and it is one of the few things a screening engineer actually looks for in a student repo. Added later means never added.

**Done when:** you push a whitespace change, a green check appears on GitHub, and the live URL loads.

---

### Phase 1: Public request form, real database, real submissions

**Weekend 2.**

1. Add `Client` and `Booking` to Core. Nothing else yet
2. Add `CalloutDbContext` in Infrastructure. Use PostgreSQL locally and in production so one migration set covers both (updated implementation decision)
3. Sign up for Neon Postgres free tier. It does not expire
4. First migration, verified on local PostgreSQL and applied to Neon production
5. Build the public request form: name, phone, email, address, what they need, rough availability
6. Add a hidden honeypot field. Real users never fill it, bots always do. Reject any submission where it has a value
7. Add ASP.NET Core's built-in fixed-window rate limiter on the POST endpoint
8. Confirmation page after submit
9. Cookie authentication with one admin account, using the framework's `PasswordHasher`, credentials from config
10. Behind the login, a bare list of incoming requests

**Why the public form before the admin panel.** The public form is the only page a recruiter can see without your password. An admin login they cannot get past is an unclickable link. It is also the only page a real client touches, so real usage starts the day this ships.

**Do not put SQLite on App Service free tier.** The filesystem gets wiped on restart and your data disappears silently. This is why the provider swap is in the plan from the beginning rather than bolted on later.

**On auth.** One admin account, cookie auth, framework `PasswordHasher`, no user management. If asked in an interview, say exactly that: single operator, standard framework primitives, no custom crypto, smallest surface that does the job.

**Done when:** you text the link to a client, they submit, and it is in your production database.

---

### Phase 2: The availability engine, fully offline

**Weekend 3.**

1. Define `IBusyCalendar` and an `Interval` record in Core
2. Write `FakeBusyCalendar` returning hardcoded intervals
3. Add `AvailabilityWindow` to the model
4. Write `SlotGenerator` implementing the seven-step pipeline
5. Test it hard, with the edge cases listed in Part 2

**Why this is the crown jewel.** Everything else in this project is CRUD that any bootcamp graduate can write. This is the one piece with real algorithmic content, real edge cases, and a real constraint that comes from your actual life. It is what you talk about when someone asks what was interesting about the project.

**Done when:** all tests green, with no network and no database touched.

### >>> STOP HERE AND APPLY <<<

Three weekends in, you have: a deployed app, a public link recruiters can click, real client submissions, tested non-trivial code, green CI, and a non-empty GitHub.

Update the resume, LinkedIn (`linkedin.com/in/hlibku`), and Handshake now. Add GitHub back to all three, since it is no longer empty.

Resume entry, two lines, placed directly under your IT Support Consultant role:

> **Callout** — booking and invoicing web app for my IT consulting business. C#, ASP.NET Core, EF Core, Postgres, Google Calendar API. Live link, source on GitHub.
> Computes bookable slots from live calendar data, travel buffers, and geographic availability windows; automated quoting and invoicing across [N] clients and [N] jobs.

Then keep building while your applications are out.

---

### Phase 3: Status machine and admin confirm

**Weekend 4.**

1. Write the transition method in Core. Illegal moves throw
2. Test every legal transition and a representative set of illegal ones
3. Build the admin booking detail page: request details, slots rendered from Phase 2, a picker
4. Confirming writes scheduled start and end and moves status to Confirmed

**Done when:** you run one of this week's real jobs end to end through the flow.

---

### Phase 4: Time entries and billing

**Weekend 5.**

1. Add `TimeEntry` to the model
2. Write the billing rule as a pure static function in Core
3. Boundary tests
4. Admin page to log start and end on a completed booking

**Why bother testing something this small.** Because it is a pure function with no dependencies, which makes it the clearest possible demonstration that you know what is worth testing and how to choose boundary cases. Six or seven tests cover it completely. Cheap signal.

**Done when:** you log time on a real completed job and the number matches what you would have charged by hand.

---

### Phase 5: Invoices and PDF

**Weekend 6.**

1. Add `Invoice` and `InvoiceLine`
2. Generate an invoice from a completed booking's time entries
3. QuestPDF template with your name, the client, line items, total
4. Download endpoint. You email it yourself

**Why this comes late despite being the highest business value.** A PDF generator is a library call. It is the lowest technical signal in the whole project. Clients can wait one more week; recruiters were never going to weight it.

---

### Phase 6: Real Google Calendar

**Weekend 7.**

1. Set up a Google Cloud project, enable the Calendar API, configure the OAuth consent screen
2. Run the consent flow once manually and capture the refresh token
3. Store it in user-secrets locally and App Service application settings in production. **Never in the repo**, not even in an appsettings file you intend to gitignore
4. Write `GoogleBusyCalendar : IBusyCalendar` backed by a FreeBusy query
5. Swap it in for the fake. The entire Phase 2 pipeline now runs against your real schedule with no other changes
6. Write side: on confirm, create the event with a reminder and store the returned event id on the Booking. On reschedule, patch it. On cancel, delete it

**Step 5 is the payoff for the interface.** One line of DI registration changes and the whole thing goes live. That is the story you tell about why you structured it that way.

**If OAuth stalls, stop and ship.** Everything before this works. A booking app where you enter your own busy times is a complete product. A half-finished OAuth flow is nothing.

---

### Phase 7: Presentation

**Weekend 8.**

Start the README in Phase 0, finish it here:

- One sentence on what it is and who uses it
- Live link at the very top
- Screenshot or short GIF of the request form and the slot picker
- A short "decisions" section: why Razor Pages, why Core has no dependencies, why the calendar sits behind an interface
- How to run locally in three commands
- Seed data so the live link is not an empty table

Then instrument it. Count bookings taken, clients onboarded, invoices generated, hours logged. Put the real numbers in the resume bullet. Numbers are what turn "I built a booking app" into something a recruiter can weigh.

---

## Part 4: Rules that apply throughout

**Commit like a human.** Small commits spread across weeks, real messages. Three commits all dated the same night reads like a tutorial followed or a plan handed to an AI. Yours will not be, so do not let it look like it.

**Every phase ends shippable.** If midterms eat you at Phase 3, you still have a deployed app with real submissions and tested code. That is a real portfolio piece, not an abandoned one.

**Secrets never touch the repo.** user-secrets locally, App Service configuration in production. Ever.

**UTC everywhere in storage.** Convert at display only.

---

## Part 5: Where you will get stuck, and what to cut

**The three predictable time sinks:**

1. **Google OAuth consent and refresh token storage.** Mitigated by the interface, which is why Phase 6 is last.
2. **Timezones.** Mitigated by storing UTC from the first migration.
3. **Scope creep.** Mitigated by the list below.

**Cut in this order when time runs out:** travel buffer, invoice PDF styling, the reschedule path on calendar write, availability windows (hardcode "I'm in Marin these weekends"). Do not cut the tests or the README.

**Out of scope for v1 no matter how tempting:** deposits, SMS reminders, a client login portal, online payments. Every one of these is a whole project. None of them make the resume bullet better.

---

## Part 6: Interview prep

Have real answers ready for these. They are what you will actually be asked.

- **Why Razor Pages instead of an API plus a frontend?** One language, one deployment, one admin user. Deliberate simplicity.
- **Why does Core have no dependencies?** So the rules are testable without a database or a network. Fast tests, clear boundaries.
- **Why is the calendar behind an interface?** Dependency inversion, with a concrete reason: I built and tested the whole availability algorithm before I had OAuth working.
- **What happens if two clients request the same slot at once?** Know your answer. The honest one is that at your volume it does not happen yet and you would handle it with a uniqueness constraint plus a re-check at confirm time.
- **What broke that you did not expect?** This one you cannot prepare, which is the best reason to start building now rather than planning further.

---

## The immediate next action

Install the .NET 10 SDK. That is it. Everything in Phase 0 follows from there and it is one evening's work to have a non-empty GitHub.
