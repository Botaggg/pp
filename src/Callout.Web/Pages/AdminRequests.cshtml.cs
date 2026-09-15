using Callout.Core;
using Callout.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Callout.Web.Pages;

[Authorize]
public class AdminRequestsModel : PageModel
{
    private readonly CalloutDbContext _context;

    public AdminRequestsModel(CalloutDbContext context)
    {
        _context = context;
    }

    public List<Booking> Bookings { get; set; } = new();

    public async Task OnGetAsync()
    {
        Bookings = await _context.Bookings
            .AsNoTracking()
            .Include(b => b.Client)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Login");
    }
}
