using Callout.Core;

namespace Callout.Tests;

public class BookingTests
{
    [Fact]
    public void NewBooking_StartsInRequestedState()
    {
        var booking = new Booking();

        Assert.Equal(BookingStatus.Requested, booking.Status);
    }

    [Fact]
    public void NewClient_HasNoBookings()
    {
        var client = new Client();

        Assert.Empty(client.Bookings);
    }
}
