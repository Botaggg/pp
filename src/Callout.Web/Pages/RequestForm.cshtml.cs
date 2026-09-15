using Callout.Core;
using Callout.Infrastructure;
using Callout.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    public async Task<IActionResult> OnPostAsync([FromServices] PostAdmissionLimits admission)
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

        if (!admission.TryAcquireRequest()) return StatusCode(StatusCodes.Status429TooManyRequests);

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

        var booking = new Booking
        {
            Client = client,
            SubmittedName = Input.Name.Trim(),
            SubmittedPhone = Input.Phone.Trim(),
            SubmittedEmail = email,
            Description = Input.Needs.Trim(),
            Address = Input.Address.Trim(),
            PreferredAvailability = Input.Availability?.Trim() ?? string.Empty,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Status = BookingStatus.Requested
        };
        _context.Bookings.Add(booking);

        // One save, so a client is never written without its booking.
        try
        {
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Clients_Email" })
        {
            // A concurrent request created this email first. The failed save was atomic.
            // Reuse its identity without accepting anonymous changes to its profile.
            _context.ChangeTracker.Clear();
            booking.Id = 0;
            booking.Client = await _context.Clients.SingleAsync(c => c.Email == email, HttpContext.RequestAborted);
            booking.ClientId = booking.Client.Id;
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
        }

        return RedirectToPage("Confirmation");
    }
}
