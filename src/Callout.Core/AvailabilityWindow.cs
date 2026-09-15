namespace Callout.Core;

/// <summary>Inclusive local dates when the operator is physically in Marin.</summary>
public sealed record AvailabilityWindow
{
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }

    public AvailabilityWindow(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            throw new ArgumentException("An availability window cannot end before it starts.", nameof(endDate));

        StartDate = startDate;
        EndDate = endDate;
    }
}
