using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Callout.Core;
using Callout.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OtpNet;

namespace Callout.Web;

public sealed class AdminSessionSecurity(CalloutDbContext db, AdminCredentials admin, TimeProvider clock)
    : CookieAuthenticationEvents
{
    public static string Version(AdminCredentials credentials) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(credentials.Username + "\0" + credentials.PasswordHash + "\0" + credentials.TotpSecret + "\0" + string.Join(",", credentials.RecoveryCodeHashes))));

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var now = clock.GetUtcNow();
        var valid = Guid.TryParse(context.Principal?.FindFirstValue("session_id"), out var id)
            && await db.AdminSessions.AsNoTracking().AnyAsync(s => s.Id == id && s.RevokedAtUtc == null
                && s.ExpiresAtUtc > now && s.CredentialVersion == Version(admin), context.HttpContext.RequestAborted);
        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public async Task<bool> SignInAsync(HttpContext context, string? code)
    {
        var now = clock.GetUtcNow();
        var version = Version(admin);
        UsedAdminCode? used = null;
        if (!string.IsNullOrEmpty(admin.TotpSecret))
        {
            code = code?.Trim().Replace(" ", "", StringComparison.Ordinal) ?? "";
            var totp = new Totp(Base32Encoding.ToBytes(admin.TotpSecret));
            if (code.Length == 6 && code.All(char.IsAsciiDigit)
                && totp.VerifyTotp(now.UtcDateTime, code, out var step, new VerificationWindow(previous: 1, future: 1)))
                used = new UsedAdminCode { Id = version + ":totp:" + step, ExpiresAtUtc = now.AddMinutes(2) };
            else
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.ToUpperInvariant())));
                if (!admin.RecoveryCodeHashes.Contains(hash, StringComparer.Ordinal)) return false;
                // Recovery codes stay consumed even after password changes.
                used = new UsedAdminCode { Id = "recovery:" + hash, ExpiresAtUtc = DateTimeOffset.MaxValue.ToUniversalTime() };
            }
            if (await db.UsedAdminCodes.AnyAsync(c => c.Id == used.Id, context.RequestAborted)) return false;
            db.UsedAdminCodes.Add(used);
        }
        var session = new AdminSession { Id = Guid.NewGuid(), CredentialVersion = version, ExpiresAtUtc = now.AddHours(8) };
        db.AdminSessions.Add(session);
        try { await db.SaveChangesAsync(context.RequestAborted); }
        catch (DbUpdateException ex) when (used != null && ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.Name, admin.Username), new Claim(ClaimTypes.Role, "Admin"),
            new Claim("session_id", session.Id.ToString())], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        // Bounded housekeeping: expired sessions cannot authenticate even if cleanup fails later.
        await db.AdminSessions.Where(s => s.ExpiresAtUtc < now).ExecuteDeleteAsync(context.RequestAborted);
        await db.UsedAdminCodes.Where(c => c.ExpiresAtUtc < now).ExecuteDeleteAsync(context.RequestAborted);
        return true;
    }

    public async Task RevokeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(principal.FindFirstValue("session_id"), out var id))
            await db.AdminSessions.Where(s => s.Id == id).ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.RevokedAtUtc, clock.GetUtcNow()), cancellationToken);
    }
}
