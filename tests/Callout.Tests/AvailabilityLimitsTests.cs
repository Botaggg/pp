using Callout.Core;
using Callout.Infrastructure.Calendars;

namespace Callout.Tests;

[Trait("Phase", "2")]
public class AvailabilityLimitsTests
{
    [Fact]
    public async Task AvailabilitySearchRejectsExcessiveWork()
    {
        var date = new DateOnly(2026, 9, 20);
        var generator = new SlotGenerator(new FakeBusyCalendar());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => generator.GenerateAsync([], date, date.AddDays(366)));
        await Assert.ThrowsAsync<ArgumentException>(() => generator.GenerateAsync(
            Enumerable.Repeat(new AvailabilityWindow(date, date), 367), date, date));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlotGenerator(
            new FakeBusyCalendar(), new() { SlotStep = TimeSpan.FromTicks(1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlotGenerator(
            new FakeBusyCalendar(), new() { TravelBuffer = TimeSpan.MaxValue }));
    }

    [Fact]
    public async Task MaximumSearchRangeIncludesAll366Days()
    {
        var date = new DateOnly(2026, 9, 20);
        var last = date.AddDays(365);
        var clock = new CountingClock(new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var slots = await new SlotGenerator(new FakeBusyCalendar(), clock: clock)
            .GenerateAsync([new(date, last)], date, last);
        Assert.Equal(366 * 19, slots.Count);
        Assert.Equal(slots.Count, slots.Distinct().Count());
        Assert.Equal(slots.OrderBy(s => s.Start), slots);
    }

    [Theory]
    [InlineData(10000)]
    [InlineData(10001)]
    public async Task CalendarEventLimitAcceptsBoundaryAndRejectsOverflow(int count)
    {
        var date = new DateOnly(2026, 9, 20);
        var block = new Interval(new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero),
            new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));
        var calendar = new CallbackCalendar(() => Enumerable.Repeat(block, count).ToArray());
        var generator = new SlotGenerator(calendar,
            clock: new CountingClock(new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        var result = () => generator.GenerateAsync([new(date, date)], date, date);
        if (count == 10000) Assert.Empty(await result());
        else await Assert.ThrowsAsync<InvalidOperationException>(result);
    }

    [Fact]
    public async Task CancellationDuringCalendarLookupPreventsReturningSlots()
    {
        var date = new DateOnly(2026, 9, 20);
        using var cancellation = new CancellationTokenSource();
        var calendar = new CallbackCalendar(() => { cancellation.Cancel(); return []; });
        var generator = new SlotGenerator(calendar,
            clock: new CountingClock(new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            generator.GenerateAsync([new(date, date)], date, date, cancellation.Token));
    }

    [Fact]
    public async Task NoticeBoundaryUsesOneClockReadingForAllDates()
    {
        var date = new DateOnly(2026, 9, 20);
        var clock = new CountingClock(new(2026, 9, 19, 16, 0, 0, TimeSpan.Zero));
        var slots = await new SlotGenerator(new FakeBusyCalendar(), clock: clock)
            .GenerateAsync([new(date, date.AddDays(1))], date, date.AddDays(1));
        Assert.Equal(1, clock.Reads);
        Assert.Equal(38, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 16, 0, 0, TimeSpan.Zero), slots[0].Start);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9999)]
    public async Task UnsupportedBoundaryYearsAreRejectedBeforeCalendarLookup(int year)
    {
        var date = new DateOnly(year, 6, 1);
        var calendar = new CallbackCalendar(() => throw new InvalidOperationException("Must not query the calendar."));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new SlotGenerator(calendar).GenerateAsync([new(date, date)], date, date));
    }

    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() => now.AddDays(Reads++);
    }

    private sealed class CallbackCalendar(Func<IReadOnlyList<Interval>> result) : IBusyCalendar
    {
        public Task<IReadOnlyList<Interval>> GetBusyAsync(
            DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            Task.FromResult(result());
    }
}
