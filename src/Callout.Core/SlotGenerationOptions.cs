namespace Callout.Core;

public sealed record SlotGenerationOptions
{
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    public TimeOnly WorkingDayStart { get; init; } = new(9, 0);
    public TimeOnly WorkingDayEnd { get; init; } = new(19, 0);
    public TimeSpan SlotDuration { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan SlotStep { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan TravelBuffer { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan MinimumNotice { get; init; } = TimeSpan.FromHours(24);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(TimeZone);
        if (WorkingDayEnd <= WorkingDayStart)
            throw new ArgumentException("Working hours must start and end on the same day, with end after start.");
        if (SlotDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SlotDuration));
        if (SlotStep <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SlotStep));
        if (TravelBuffer < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(TravelBuffer));
        if (MinimumNotice < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MinimumNotice));
    }
}
