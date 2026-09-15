using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Callout.Web.Pages;

[EnableRateLimiting("login")]
public class LoginModel : PageModel
{
    private readonly AdminCredentials _admin;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(AdminCredentials admin, ILogger<LoginModel> logger)
    {
        _admin = admin;
        _logger = logger;
    }

    [BindProperty]
    public string? Username { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    public string? Error { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Always run the hash verification, even when the username is wrong, so the
        // response time does not reveal whether the username exists.
        var hasher = new PasswordHasher<object>();
        var passwordOk = hasher.VerifyHashedPassword(null!, _admin.PasswordHash, Password ?? string.Empty)
            != PasswordVerificationResult.Failed;
        var usernameOk = string.Equals(Username, _admin.Username, StringComparison.Ordinal);

        if (!passwordOk || !usernameOk)
        {
            _logger.LogWarning("Failed admin login attempt.");
            Error = "Invalid username or password.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, _admin.Username),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return RedirectToPage("/AdminRequests");
    }
}
