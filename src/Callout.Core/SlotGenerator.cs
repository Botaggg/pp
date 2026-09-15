namespace Callout.Core;

/// <summary>Computes candidate slots without storing bookings or changing a calendar.</summary>
public sealed class SlotGenerator
{
    private readonly IBusyCalendar _calendar;
    private readonly SlotGenerationOptions _options;
    private readonly TimeProvider _clock;

    public SlotGenerator(IBusyCalendar calendar, SlotGenerationOptions? options = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        _calendar = calendar;
        _options = options ?? new SlotGenerationOptions();
        _options.Validate();
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Search an inclusive range of local dates, intersected with the supplied
    /// availability windows. Results are unique, ordered UTC intervals.
    /// </summary>
    public async Task<IReadOnlyList<Interval>> GenerateAsync(
        IEnumerable<AvailabilityWindow> availabilityWindows,
        DateOnly fromDate,
        DateOnly throughDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(availabilityWindows);
        if (throughDate < fromDate)
            throw new ArgumentException("Search end date must be on or after its start date.", nameof(throughDate));
        cancellationToken.ThrowIfCancellationRequested();

        // Read the clock once so all slots use exactly the same notice boundary.
        var earliestStart = _clock.GetUtcNow() + _options.MinimumNotice;
        var dates = new SortedSet<DateOnly>();
        foreach (var window in availabilityWindows)
        {
            ArgumentNullException.ThrowIfNull(window);
            var first = window.StartDate > fromDate ? window.StartDate : fromDate;
            var last = window.EndDate < throughDate ? window.EndDate : throughDate;
            for (var date = first; date <= last; date = date.AddDays(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                dates.Add(date);
                if (date == last) break;
            }
        }

        var workingIntervals = new List<Interval>();
        foreach (var date in dates)
        {
            var start = ResolveBoundary(date, _options.WorkingDayStart, isClosing: false);
            var end = ResolveBoundary(date, _options.WorkingDayEnd, isClosing: true);
            if (end > start && end - start >= _options.SlotDuration && end - earliestStart >= _options.SlotDuration)
                workingIntervals.Add(new Interval(start, end));
        }
        if (workingIntervals.Count == 0) return Array.Empty<Interval>();

        // Include adjacent events whose travel buffer can reach into working hours.
        var busy = await _calendar.GetBusyAsync(
            workingIntervals[0].Start - _options.TravelBuffer,
            workingIntervals[^1].End + _options.TravelBuffer,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var blocked = Merge(busy.Select(interval => new Interval(
            interval.Start - _options.TravelBuffer, interval.End + _options.TravelBuffer)));

        var slots = new List<Interval>();
        var blockedIndex = 0;
        foreach (var working in workingIntervals)
        {
            // Keep the grid anchored to opening time, even when an event ends
            // off the grid. UTC stepping preserves real elapsed durations at DST.
            for (var start = working.Start; start <= working.End - _options.SlotDuration; start += _options.SlotStep)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (start < earliestStart) continue;
                var slot = new Interval(start, start + _options.SlotDuration);
                while (blockedIndex < blocked.Count && blocked[blockedIndex].End <= slot.Start)
                    blockedIndex++;
                if (blockedIndex == blocked.Count || !slot.Overlaps(blocked[blockedIndex]))
                    slots.Add(slot);
            }
        }
        return slots.AsReadOnly();
    }

    private DateTimeOffset ResolveBoundary(DateOnly date, TimeOnly time, bool isClosing)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // If a clock change removes the configured opening/closing time, use
        // the next existing local minute. No nonexistent instant is invented.
        while (_options.TimeZone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (_options.TimeZone.IsAmbiguousTime(local))
        {
            var offsets = _options.TimeZone.GetAmbiguousTimeOffsets(local);
            // Open on the first occurrence, close on the last occurrence.
            return new DateTimeOffset(local, isClosing ? offsets.Min() : offsets.Max()).ToUniversalTime();
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _options.TimeZone));
    }

    private static List<Interval> Merge(IEnumerable<Interval> intervals)
    {
        var merged = new List<Interval>();
        foreach (var current in intervals.OrderBy(interval => interval.Start).ThenBy(interval => interval.End))
        {
            if (merged.Count == 0 || current.Start > merged[^1].End)
                merged.Add(current);
            else if (current.End > merged[^1].End)
                merged[^1] = new Interval(merged[^1].Start, current.End);
        }
        return merged;
    }
}
