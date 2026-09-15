namespace Callout.Core;

public class Booking
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTimeOffset ScheduledStart { get; set; }
    public BookingStatus Status { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
}
