using Callout.Core;
using Callout.Infrastructure.Calendars;

namespace Callout.Tests;

[Trait("Phase", "2")]
public class SlotGeneratorTests
{
    private static readonly DateOnly Day = new(2026, 9, 20);
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int hour, int minute = 0) =>
        new DateTimeOffset(2026, 9, 20, hour, minute, 0, TimeSpan.FromHours(-7)).ToUniversalTime();

    private static Interval Busy(int startHour, int startMinute, int endHour, int endMinute) =>
        new(At(startHour, startMinute), At(endHour, endMinute));

    private static Task<IReadOnlyList<Interval>> Generate(
        Interval[]? busy = null, SlotGenerationOptions? options = null, DateTimeOffset? now = null) =>
        new SlotGenerator(new FakeBusyCalendar(busy), options, new FixedClock(now ?? Now))
            .GenerateAsync([new(Day, Day)], Day, Day);

    [Fact]
    public async Task EmptyCalendar_ProducesNineteenOneHourSlotsOnHalfHourGrid()
    {
        var slots = await Generate();
        Assert.Equal(19, slots.Count);
        Assert.Equal(At(9), slots[0].Start);
        Assert.Equal(At(19), slots[^1].End);
        for (var i = 0; i < slots.Count; i++)
        {
            Assert.Equal(At(9).AddMinutes(i * 30), slots[i].Start);
            Assert.Equal(TimeSpan.FromHours(1), slots[i].Duration);
            Assert.Equal(TimeSpan.Zero, slots[i].Start.Offset);
            Assert.Equal(TimeSpan.Zero, slots[i].End.Offset);
        }
    }

    [Fact]
    public async Task NoMarinWindow_DoesNotQueryCalendar()
    {
        var calendar = new RecordingCalendar();
        var slots = await new SlotGenerator(calendar, clock: new FixedClock(Now))
            .GenerateAsync([], Day, Day);
        Assert.Empty(slots);
        Assert.Empty(calendar.Queries);
    }

    [Fact]
    public async Task WindowsAreInclusiveClippedToSearchAndDeduplicated()
    {
        var slots = await new SlotGenerator(new FakeBusyCalendar(), clock: new FixedClock(Now))
            .GenerateAsync([new(Day.AddDays(-2), Day), new(Day, Day.AddDays(1)), new(Day, Day)], Day, Day);
        Assert.Equal(19, slots.Count);
        Assert.Equal(slots.Count, slots.Distinct().Count());
    }

    [Fact]
    public async Task DatesOutsideMarinWindowsHaveNoSlots()
    {
        var slots = await new SlotGenerator(new FakeBusyCalendar(), clock: new FixedClock(Now))
            .GenerateAsync([new(Day, Day), new(Day.AddDays(2), Day.AddDays(2))], Day, Day.AddDays(2));
        Assert.Equal(38, slots.Count);
        Assert.DoesNotContain(slots, slot => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(slot.Start, Pacific).DateTime) == Day.AddDays(1));
        Assert.Equal(slots.OrderBy(slot => slot.Start), slots);
    }

    [Fact]
    public async Task PastWindowsDoNotQueryCalendar()
    {
        var calendar = new RecordingCalendar();
        var slots = await new SlotGenerator(calendar, clock: new FixedClock(At(20)))
            .GenerateAsync([new(Day, Day)], Day, Day);
        Assert.Empty(slots);
        Assert.Empty(calendar.Queries);
    }

    [Fact]
    public async Task BusyTimeAndBothTravelBuffersAreExcluded()
    {
        var slots = await Generate([Busy(10, 0, 11, 0)]);
        Assert.Equal(14, slots.Count);
        Assert.Equal(At(11, 30), slots[0].Start);
        Assert.All(slots, slot => Assert.False(slot.Overlaps(Busy(9, 30, 11, 30))));
    }

    [Fact]
    public async Task OverlappingNestedAndDuplicateBusyBlocksBehaveLikeTheirUnion()
    {
        var blocks = new[] { Busy(10, 30, 12, 0), Busy(10, 0, 11, 0), Busy(10, 0, 11, 0), Busy(10, 45, 11, 15) };
        var expected = await Generate([Busy(10, 0, 12, 0)]);
        Assert.Equal(expected, await Generate(blocks));
        Assert.Equal(expected, await Generate(blocks.Reverse().ToArray()));
    }

    [Fact]
    public async Task TravelBuffersCanSwallowAnEntireGap()
    {
        var slots = await Generate([Busy(10, 0, 11, 0), Busy(12, 0, 13, 0)]);
        Assert.Equal(At(13, 30), slots[0].Start);
    }

    [Fact]
    public async Task TouchingBusyBoundariesAreAvailableWhenBufferIsZero()
    {
        var slots = await Generate([Busy(11, 0, 12, 0)], new() { TravelBuffer = TimeSpan.Zero });
        Assert.Contains(new Interval(At(10), At(11)), slots);
        Assert.Contains(new Interval(At(12), At(13)), slots);
        Assert.DoesNotContain(slots, slot => slot.Overlaps(Busy(11, 0, 12, 0)));
    }

    [Fact]
    public async Task OffGridBusyEndDoesNotShiftSlotGrid()
    {
        var slots = await Generate([Busy(10, 10, 10, 40)], new() { TravelBuffer = TimeSpan.Zero });
        Assert.Contains(new Interval(At(11), At(12)), slots);
        Assert.DoesNotContain(slots, slot => slot.Start == At(10, 40));
        Assert.All(slots, slot => Assert.Equal(0, slot.Start.Minute % 30));
    }

    [Fact]
    public async Task AdjacentEventsOutsideWorkingHoursStillApplyTravelBuffers()
    {
        var calendar = new RecordingCalendar([Busy(8, 15, 8, 45), Busy(19, 15, 19, 45)]);
        var slots = await new SlotGenerator(calendar, clock: new FixedClock(Now))
            .GenerateAsync([new(Day, Day)], Day, Day);
        Assert.Equal(new Interval(At(8, 30), At(19, 30)), Assert.Single(calendar.Queries));
        Assert.Equal(At(9, 30), slots[0].Start);
        Assert.Equal(At(18, 30), slots[^1].End);
    }

    [Fact]
    public async Task AllDayOrMultiDayBusyBlockLeavesNoSlots()
    {
        Assert.Empty(await Generate([new Interval(At(0).AddDays(-1), At(23).AddDays(1))]));
    }

    [Fact]
    public async Task TwentyHourNoticeIsRejectedAndExactlyTwentyFourHoursIsAllowed()
    {
        var slots = await Generate(now: At(13).AddDays(-1));
        Assert.DoesNotContain(slots, slot => slot.Start == At(9));
        Assert.Equal(At(13), slots[0].Start);
        Assert.All(slots, slot => Assert.True(slot.Start >= At(13)));
    }

    [Fact]
    public async Task NoticeBoundaryDoesNotRoundDownToAnEarlierSlot()
    {
        var slots = await Generate(now: At(13).AddDays(-1).AddTicks(1));
        Assert.Equal(At(13, 30), slots[0].Start);
    }

    [Fact]
    public async Task ConfigurableHoursDurationAndStepOnlyReturnFullSlots()
    {
        var slots = await Generate(options: new()
        {
            WorkingDayStart = new(10, 15), WorkingDayEnd = new(12, 0),
            SlotDuration = TimeSpan.FromMinutes(45), SlotStep = TimeSpan.FromMinutes(15)
        });
        Assert.Equal(5, slots.Count);
        Assert.Equal(At(10, 15), slots[0].Start);
        Assert.Equal(At(12), slots[^1].End);
        Assert.All(slots, slot => Assert.Equal(TimeSpan.FromMinutes(45), slot.Duration));
    }

    [Fact]
    public async Task GapShorterThanSlotProducesNothing()
    {
        Assert.Empty(await Generate(options: new() { WorkingDayStart = new(9, 0), WorkingDayEnd = new(9, 45) }));
    }

    [Fact]
    public async Task SpringForwardSlotsUseActualElapsedTimeAndNeverInventTwoAm()
    {
        var date = new DateOnly(2026, 3, 8);
        var slots = await new SlotGenerator(new FakeBusyCalendar(), new()
        {
            WorkingDayStart = new(0, 0), WorkingDayEnd = new(4, 0)
        }, new FixedClock(new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)))
            .GenerateAsync([new(date, date)], date, date);
        Assert.Equal(5, slots.Count);
        var crossing = Assert.Single(slots, slot => TimeZoneInfo.ConvertTime(slot.Start, Pacific).TimeOfDay == TimeSpan.FromMinutes(90));
        Assert.Equal(TimeSpan.FromHours(1), crossing.Duration);
        Assert.Equal(TimeSpan.FromMinutes(210), TimeZoneInfo.ConvertTime(crossing.End, Pacific).TimeOfDay);
        Assert.DoesNotContain(slots, slot => TimeZoneInfo.ConvertTime(slot.Start, Pacific).Hour == 2);
    }

    [Fact]
    public async Task FallBackKeepsBothOccurrencesOfOneThirtyAsDistinctInstants()
    {
        var date = new DateOnly(2026, 11, 1);
        var slots = await new SlotGenerator(new FakeBusyCalendar(), new()
        {
            WorkingDayStart = new(0, 0), WorkingDayEnd = new(4, 0)
        }, new FixedClock(new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)))
            .GenerateAsync([new(date, date)], date, date);
        Assert.Equal(9, slots.Count);
        var repeated = slots.Where(slot => TimeZoneInfo.ConvertTime(slot.Start, Pacific).TimeOfDay == TimeSpan.FromMinutes(90)).ToArray();
        Assert.Equal(2, repeated.Length);
        Assert.Equal(TimeSpan.FromHours(1), repeated[1].Start - repeated[0].Start);
        Assert.Equal(TimeSpan.FromMinutes(90), TimeZoneInfo.ConvertTime(repeated[0].End, Pacific).TimeOfDay);
        Assert.Equal(slots.Count, slots.Distinct().Count());
        Assert.All(slots, slot => Assert.Equal(TimeSpan.FromHours(1), slot.Duration));
    }

    [Fact]
    public async Task WorkingHoursStayLocalAcrossDstChangeBetweenDates()
    {
        var first = new DateOnly(2026, 10, 31);
        var last = first.AddDays(1);
        var slots = await new SlotGenerator(new FakeBusyCalendar(), clock: new FixedClock(new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)))
            .GenerateAsync([new(first, last)], first, last);
        Assert.Equal(38, slots.Count);
        Assert.Equal(16, slots[0].Start.Hour);
        Assert.Equal(17, slots[19].Start.Hour);
        Assert.Equal(9, TimeZoneInfo.ConvertTime(slots[19].Start, Pacific).Hour);
    }

    [Fact]
    public async Task NonexistentOpeningAdvancesToNextExistingLocalMinute()
    {
        var date = new DateOnly(2026, 3, 8);
        var slots = await new SlotGenerator(new FakeBusyCalendar(), new()
        {
            WorkingDayStart = new(2, 30), WorkingDayEnd = new(4, 0)
        }, new FixedClock(new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)))
            .GenerateAsync([new(date, date)], date, date);
        Assert.Equal(new TimeSpan(3, 0, 0), TimeZoneInfo.ConvertTime(Assert.Single(slots).Start, Pacific).TimeOfDay);
    }

    [Fact]
    public async Task AmbiguousOpeningAndClosingUseFirstAndLastOccurrences()
    {
        var date = new DateOnly(2026, 11, 1);
        var slots = await new SlotGenerator(new FakeBusyCalendar(), new()
        {
            WorkingDayStart = new(1, 0), WorkingDayEnd = new(1, 30)
        }, new FixedClock(new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)))
            .GenerateAsync([new(date, date)], date, date);
        Assert.Equal(2, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero), slots[0].Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero), slots[^1].End);
    }

    [Fact]
    public async Task NoticeUsesElapsedHoursAcrossDstRatherThanCalendarDays()
    {
        var date = new DateOnly(2026, 3, 8);
        var now = new DateTimeOffset(2026, 3, 7, 9, 0, 0, TimeSpan.FromHours(-8));
        var slots = await new SlotGenerator(new FakeBusyCalendar(), clock: new FixedClock(now))
            .GenerateAsync([new(date, date)], date, date);
        Assert.Equal(10, TimeZoneInfo.ConvertTime(slots[0].Start, Pacific).Hour);
        Assert.Equal(TimeSpan.FromHours(24), slots[0].Start - now);
    }

    [Fact]
    public async Task BusyEventCanBlockOnlyOneOccurrenceOfRepeatedLocalHour()
    {
        var date = new DateOnly(2026, 11, 1);
        var busy = new Interval(new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero), new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        var slots = await new SlotGenerator(new FakeBusyCalendar([busy]), new()
        {
            WorkingDayStart = new(0, 0), WorkingDayEnd = new(4, 0), TravelBuffer = TimeSpan.Zero
        }, new FixedClock(new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)))
            .GenerateAsync([new(date, date)], date, date);
        Assert.DoesNotContain(slots, slot => slot.Start == busy.Start);
        Assert.Contains(slots, slot => slot.Start == busy.End);
    }

    [Fact]
    public async Task CalendarFailurePropagatesRatherThanAdvertisingFalseAvailability()
    {
        await Assert.ThrowsAsync<IOException>(() => new SlotGenerator(new FailedCalendar(), clock: new FixedClock(Now))
            .GenerateAsync([new(Day, Day)], Day, Day));
    }

    [Fact]
    public async Task CancellationBeforeWorkDoesNotCallCalendar()
    {
        var calendar = new RecordingCalendar();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SlotGenerator(calendar)
            .GenerateAsync([new(Day, Day)], Day, Day, new CancellationToken(true)));
        Assert.Empty(calendar.Queries);
    }

    [Fact]
    public async Task ManyUnsortedBusyIntervalsNeverProduceAnOverlappingOrOffGridSlot()
    {
        var random = new Random(27);
        for (var sample = 0; sample < 30; sample++)
        {
            var busy = Enumerable.Range(0, 5).Select(_ =>
            {
                var start = At(7).AddMinutes(random.Next(840));
                return new Interval(start, start.AddMinutes(random.Next(1, 90)));
            }).ToArray();
            var slots = await Generate(busy);
            var expected = Enumerable.Range(0, 19).Select(i => new Interval(At(9).AddMinutes(i * 30), At(10).AddMinutes(i * 30)))
                .Where(slot => busy.All(b => !slot.Overlaps(new(b.Start.AddMinutes(-30), b.End.AddMinutes(30)))))
                .ToArray();
            Assert.Equal(expected, slots);
        }
    }

    [Fact]
    public async Task ReversedSearchRangeIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new SlotGenerator(new FakeBusyCalendar())
            .GenerateAsync([], Day.AddDays(1), Day));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }

    private sealed class RecordingCalendar(Interval[]? busy = null) : IBusyCalendar
    {
        public List<Interval> Queries { get; } = [];
        public Task<IReadOnlyList<Interval>> GetBusyAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            Queries.Add(new(from, to));
            return new FakeBusyCalendar(busy).GetBusyAsync(from, to, cancellationToken);
        }
    }

    private sealed class FailedCalendar : IBusyCalendar
    {
        public Task<IReadOnlyList<Interval>> GetBusyAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            throw new IOException("Calendar unavailable.");
    }
}
