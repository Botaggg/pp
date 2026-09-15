namespace Callout.Core;

public interface IBusyCalendar
{
    /// <summary>
    /// Return every busy interval overlapping [from, to), including events that
    /// start before from. Inputs are UTC. Implementations must surface failures.
    /// </summary>
    Task<IReadOnlyList<Interval>> GetBusyAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
