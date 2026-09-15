using Callout.Core;
using Callout.Infrastructure.Calendars;

namespace Callout.Tests;

[Trait("Phase", "2")]
public class AvailabilityModelTests
{
    [Fact]
    public void IntervalsNormalizeOffsetsAndDoNotOverlapAtTouchingBoundaries()
    {
        var start = new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.FromHours(-7));
        var interval = new Interval(start, start.AddHours(1));
        Assert.Equal(16, interval.Start.Hour);
        Assert.Equal(TimeSpan.Zero, interval.Start.Offset);
        Assert.False(interval.Overlaps(new(interval.End, interval.End.AddHours(1))));
        Assert.True(interval.Overlaps(new(start.AddMinutes(30), start.AddHours(2))));
        Assert.Equal(new Interval(start.ToUniversalTime(), start.AddHours(1).ToUniversalTime()), interval);
    }

    [Fact]
    public void EmptyAndReversedIntervalsAndWindowsAreRejected()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => new Interval(now, now));
        Assert.Throws<ArgumentException>(() => new Interval(now, now.AddMinutes(-1)));
        Assert.Throws<ArgumentException>(() => new AvailabilityWindow(new(2026, 9, 20), new(2026, 9, 19)));
    }

    [Theory]
    [InlineData("zero duration")]
    [InlineData("negative duration")]
    [InlineData("zero step")]
    [InlineData("negative step")]
    [InlineData("negative travel")]
    [InlineData("negative notice")]
    [InlineData("overnight hours")]
    [InlineData("equal hours")]
    public void InvalidOptionsAreRejectedBeforeGeneration(string setting)
    {
        var options = setting switch
        {
            "zero duration" => new SlotGenerationOptions { SlotDuration = TimeSpan.Zero },
            "negative duration" => new() { SlotDuration = TimeSpan.FromMinutes(-1) },
            "zero step" => new() { SlotStep = TimeSpan.Zero },
            "negative step" => new() { SlotStep = TimeSpan.FromMinutes(-1) },
            "negative travel" => new() { TravelBuffer = TimeSpan.FromMinutes(-1) },
            "negative notice" => new() { MinimumNotice = TimeSpan.FromMinutes(-1) },
            "overnight hours" => new() { WorkingDayStart = new(19, 0), WorkingDayEnd = new(9, 0) },
            _ => new() { WorkingDayEnd = new(9, 0) }
        };
        Assert.ThrowsAny<ArgumentException>(() => new SlotGenerator(new FakeBusyCalendar(), options));
    }

    [Fact]
    public async Task FakeCalendarFindsSpanningEventsAndDoesNotLeakItsInputCollection()
    {
        var start = new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);
        var spanning = new Interval(start.AddDays(-1), start.AddDays(1));
        var intervals = new List<Interval> { spanning, new(start.AddHours(1), start.AddHours(2)) };
        var calendar = new FakeBusyCalendar(intervals);
        intervals.Clear();
        Assert.Equal(spanning, Assert.Single(await calendar.GetBusyAsync(start, start.AddHours(1))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => calendar.GetBusyAsync(start, start.AddHours(1), new CancellationToken(true)));
    }
}
