# Phase 2: offline availability engine

## Delivered

- `Interval`: validated half-open time ranges, normalized to UTC.
- `IBusyCalendar`: the calendar-read contract in Core.
- `AvailabilityWindow`: an inclusive range of local dates when the operator is in Marin.
- `SlotGenerationOptions`: configurable scheduling rules.
- `SlotGenerator`: generates ordered, unique candidate slots.
- `FakeBusyCalendar`: an in-memory adapter in Infrastructure for tests and demos.
- 38 Phase 2 tests, passing without a database, web server, or network call.

Core still has no external dependencies. The fake calendar is not registered in
the production web app. This phase computes candidates; it does not confirm a
booking, reserve a slot, or write to Google Calendar.

## Default rules

| Setting | Default |
| --- | --- |
| Local timezone | America/Los_Angeles (Pacific) |
| Working hours | 9:00am to 7:00pm, on each supplied available date |
| Appointment duration | 60 minutes |
| Candidate start spacing | 30 minutes, anchored to opening time |
| Travel buffer | 30 minutes before and after every busy interval |
| Minimum notice | 24 elapsed hours, with exactly 24 hours allowed |

An empty calendar produces 19 candidate slots per available day, from 9:00am to
10:00am through 6:00pm to 7:00pm. Overlapping candidate slots are alternatives,
not simultaneous confirmed bookings.

The daily template applies to every supplied date, including weekends. Omit a
date from the availability windows when the operator is not working or not in
Marin. Overnight working hours are not supported by this phase.

## Pipeline

1. Intersect Marin availability windows with the requested inclusive search dates.
2. Deduplicate dates and resolve the daily working hours in Pacific time.
3. Ask the calendar for all overlapping busy events, extending the query by the
   travel buffer on both ends so nearby events outside working hours are included.
4. Expand every busy event by its travel buffer and merge overlapping or touching
   blocks. This subtracts busy time and travel time together.
5. Walk the half-hour grid anchored to opening time. Reject any candidate that
   overlaps a blocked interval or extends past closing.
6. Reject starts earlier than the captured current instant plus minimum notice.
7. Return sorted UTC intervals. Calendar errors propagate rather than silently
   treating a failed calendar lookup as free time.

For example, a busy event from 10:00am to 11:00am blocks 9:30am to 11:30am with
the default travel buffer. The first available one-hour candidate starts at
11:30am. A candidate ending exactly at the start of a blocked interval is allowed.

## Time and daylight saving

Dates and opening/closing times are local. Returned intervals and calendar query
bounds are UTC. Durations, grid steps, and minimum notice use elapsed time.

- Spring forward: nonexistent local clock times are not offered. A slot can run
  from 1:30am to 3:30am while lasting exactly one hour.
- Fall back: repeated clock times are distinct UTC instants. Both 1:30am starts
  can be represented without duplicate instants.
- An ambiguous opening uses its first occurrence; an ambiguous closing uses its
  last occurrence. The working interval is continuous between those instants.
- If a configured boundary falls in a missing clock period, it advances to the
  next existing local minute.

Injecting `TimeProvider` makes notice-boundary tests deterministic. The engine
reads it once per generation request.

## Usage

```csharp
using Callout.Core;
using Callout.Infrastructure.Calendars;

var options = new SlotGenerationOptions();
var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, options.TimeZone);
var date = DateOnly.FromDateTime(localNow.DateTime).AddDays(2);

// Supply known busy Interval values here for offline demos.
var calendar = new FakeBusyCalendar();
var generator = new SlotGenerator(calendar, options);
var slots = await generator.GenerateAsync(
    [new AvailabilityWindow(date, date)], date, date);

foreach (var slot in slots)
{
    Console.WriteLine($"{slot.Start:O} to {slot.End:O}");
}
```

`IBusyCalendar` implementations must return events that overlap the query even
when they started before it. The fake preserves complete event boundaries and
copies its input collection. Phase 6 can supply a real calendar adapter through
the same interface.

## Verification

After restoring/building dependencies once, run the Phase 2 suite offline:

```bash
dotnet test --no-build --no-restore --configuration Release --filter 'Phase=2'
```

Verified result: **38 passed, 0 failed, 0 skipped**. Coverage includes overlapping,
nested and duplicate busy blocks; adjacent-day events; swallowed gaps; off-grid
event ends; absent and overlapping Marin windows; search-range clipping; UTC
normalization; exact notice boundaries; configurable rules; DST changes;
cancellation; calendar errors; and seeded comparisons against a direct
candidate-by-candidate overlap check.

## Next phase

Phase 3 adds legal booking-status transitions and the admin booking detail page
with a slot picker and confirmation. Availability windows are currently domain
values passed to the engine. Persisting/managing them can be added when wiring
that admin flow. This phase requires no database migration or production data
changes.
