using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Callout.Core;
using Callout.Infrastructure;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.RateLimiting;

namespace Callout.Web.Models;

[EnableRateLimiting("request-form")]
public class RequestFormModel : PageModel
{
    private readonly CalloutDbContext _context;

    public RequestFormModel(CalloutDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public RequestViewModel Input { get; set; } = new();

    public string Message { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Honeypot check: If the hidden field is filled, it's likely a bot
        if (!string.IsNullOrEmpty(Input.Honeypot))
        {
            return Page();
        }

        var newBooking = new Booking
        {
            Description = Input.Needs,
            Address = Input.Address,
            ScheduledStart = DateTimeOffset.Now, // Default for now
            Status = BookingStatus.Requested
        };

        // Create client first to get an ID
        var newClient = new Client
        {
            Name = Input.Name,
            Phone = Input.Phone,
            Email = Input.Email,
            Address = Input.Address,
            Notes = Input.Availability
        };

        _context.Clients.Add(newClient);
        await _context.SaveChangesAsync();

        newBooking.ClientId = newClient.Id;
        newBooking.Client = newClient;

        _context.Bookings.Add(newBooking);
        await _context.SaveChangesAsync();

        return RedirectToPage("Confirmation");
    }
}
