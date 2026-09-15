using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Callout.Web.Pages;

[EnableRateLimiting("login")]
public class LoginModel : PageModel
{
    private readonly AdminCredentials _admin;
    private readonly AdminSessionSecurity _sessions;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(AdminCredentials admin, ILogger<LoginModel> logger, AdminSessionSecurity sessions)
    {
        _admin = admin;
        _sessions = sessions;
        _logger = logger;
    }

    [BindProperty, Required, StringLength(120)]
    public string? Username { get; set; }

    [BindProperty, Required, StringLength(1024)]
    public string? Password { get; set; }

    [BindProperty, StringLength(80)]
    public string? Code { get; set; }
    public bool RequiresCode => !string.IsNullOrEmpty(_admin.TotpSecret);

    public string? Error { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync([FromServices] PostAdmissionLimits admission)
    {
        if (!ModelState.IsValid)
        {
            Error = "Invalid username or password.";
            return Page();
        }
        if (!admission.TryAcquireLogin()) return StatusCode(StatusCodes.Status429TooManyRequests);
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

        if (!await _sessions.SignInAsync(HttpContext, Code))
        {
            _logger.LogWarning("Failed admin second-factor verification.");
            Error = "Invalid or already used sign-in code.";
            return Page();
        }
        return RedirectToPage("/AdminRequests");
    }
}
