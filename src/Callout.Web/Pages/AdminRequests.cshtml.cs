using Callout.Core;
using Callout.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Callout.Web.Pages;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminRequestsModel : PageModel
{
    private readonly CalloutDbContext _context;

    public AdminRequestsModel(CalloutDbContext context)
    {
        _context = context;
    }

    public List<Booking> Bookings { get; set; } = new();

    public const int PageSize = 50;
    public int PageNumber { get; private set; }
    public bool HasNextPage { get; private set; }

    public async Task OnGetAsync(int pageNumber = 1)
    {
        PageNumber = Math.Clamp(pageNumber, 1, 100000);
        Bookings = await _context.Bookings
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.Id)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize + 1)
            .ToListAsync(HttpContext.RequestAborted);
        HasNextPage = Bookings.Count > PageSize;
        if (HasNextPage) Bookings.RemoveAt(PageSize);
    }

    public async Task<IActionResult> OnPostLogoutAsync([FromServices] AdminSessionSecurity sessions)
    {
        await sessions.RevokeAsync(User, HttpContext.RequestAborted);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Login");
    }
}
