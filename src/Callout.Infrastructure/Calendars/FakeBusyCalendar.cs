using Callout.Core;

namespace Callout.Infrastructure.Calendars;

/// <summary>In-memory calendar for tests and offline demos. It does not reflect a real calendar.</summary>
public sealed class FakeBusyCalendar : IBusyCalendar
{
    private readonly Interval[] _busy;

    public FakeBusyCalendar(IEnumerable<Interval>? busy = null)
    {
        _busy = busy?.ToArray() ?? [];
        if (_busy.Any(interval => interval is null))
            throw new ArgumentException("Busy intervals cannot contain null entries.", nameof(busy));
    }

    public Task<IReadOnlyList<Interval>> GetBusyAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = new Interval(from, to);
        IReadOnlyList<Interval> result = Array.AsReadOnly(_busy.Where(interval => interval.Overlaps(query)).ToArray());
        return Task.FromResult(result);
    }
}
