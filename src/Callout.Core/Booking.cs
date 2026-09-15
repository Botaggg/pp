namespace Callout.Core;

public class Booking
{
    public int Id { get; set; }

    // Submitted contact details belong to this request, not the matching client profile.
    public string SubmittedName { get; set; } = string.Empty;
    public string SubmittedPhone { get; set; } = string.Empty;
    public string SubmittedEmail { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    /// <summary>What the client said about when they are free. Free text, not a commitment.</summary>
    public string PreferredAvailability { get; set; } = string.Empty;

    /// <summary>When the request came in. Stored in UTC, converted only at display time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Null until the booking is confirmed against a real slot.</summary>
    public DateTimeOffset? ScheduledStart { get; set; }

    /// <summary>Null until the booking is confirmed against a real slot.</summary>
    public DateTimeOffset? ScheduledEnd { get; set; }

    public BookingStatus Status { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;
}
