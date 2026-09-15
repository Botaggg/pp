using Callout.Core;
using Callout.Infrastructure;
using Callout.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Callout.Web.Pages;

[EnableRateLimiting("request-form")]
public class RequestFormModel : PageModel
{
    private readonly CalloutDbContext _context;
    private readonly ILogger<RequestFormModel> _logger;

    public RequestFormModel(CalloutDbContext context, ILogger<RequestFormModel> logger)
    {
        _context = context;
        _logger = logger;
    }

    [BindProperty]
    public RequestViewModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Honeypot. Real users never see this field, so anything in it is a bot.
        // Redirect as if it worked so the bot gets no signal that it was caught.
        if (!string.IsNullOrWhiteSpace(Input.Website))
        {
            _logger.LogInformation("Honeypot triggered, submission discarded.");
            return RedirectToPage("Confirmation");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim().ToLowerInvariant();

        // Match repeat clients on email instead of creating a duplicate row every time.
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Email == email);
        if (client is null)
        {
            client = new Client
            {
                Name = Input.Name.Trim(),
                Phone = Input.Phone.Trim(),
                Email = email,
                Address = Input.Address.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            _context.Clients.Add(client);
        }
        else
        {
            // Keep the latest contact details they gave us.
            client.Name = Input.Name.Trim();
            client.Phone = Input.Phone.Trim();
            client.Address = Input.Address.Trim();
        }

        var booking = new Booking
        {
            Client = client,
            Description = Input.Needs.Trim(),
            Address = Input.Address.Trim(),
            PreferredAvailability = Input.Availability?.Trim() ?? string.Empty,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Status = BookingStatus.Requested
        };
        _context.Bookings.Add(booking);

        // One save, so a client is never written without its booking.
        await _context.SaveChangesAsync();

        return RedirectToPage("Confirmation");
    }
}
