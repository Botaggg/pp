namespace Callout.Core;

/// <summary>A half-open time range [Start, End), normalized to UTC.</summary>
public sealed record Interval
{
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public TimeSpan Duration => End - Start;

    public Interval(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
            throw new ArgumentException("An interval must end after it starts.", nameof(end));

        Start = start.ToUniversalTime();
        End = end.ToUniversalTime();
    }

    public bool Overlaps(Interval other) => Start < other.End && other.Start < End;
}
