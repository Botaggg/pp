using Callout.Infrastructure;
using Callout.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

// Utility mode: dotnet run --project src/Callout.Web -- hash-password
// Prints a PasswordHasher hash to paste into user-secrets or App Service settings.
// The plaintext password is never stored anywhere.
if (args.Length > 0 && args[0] == "hash-password")
{
    Console.Write("Password: ");
    var plaintext = Console.ReadLine() ?? string.Empty;
    Console.WriteLine();
    Console.WriteLine(new PasswordHasher<object>().HashPassword(null!, plaintext));
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Postgres everywhere. Local: user-secrets. Production: App Service application settings.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not set. Locally, run: " +
        "dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"<neon connection string>\" " +
        "--project src/Callout.Web");
}

builder.Services.AddDbContext<CalloutDbContext>(options => options.UseNpgsql(connectionString));

// App Service terminates TLS and proxies, so the real client IP and scheme arrive in
// headers. Without this every visitor shares one rate-limit partition.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Fixed-window rate limiters, keyed by client IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("request-form", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));

    static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
});

// Cookie auth with a single admin account.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "callout.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });
builder.Services.AddAuthorization();

// Admin credentials from config. The hash is stored, never the password.
var admin = builder.Configuration.GetSection("Admin").Get<AdminOptions>() ?? new AdminOptions();
if (string.IsNullOrWhiteSpace(admin.Username) || string.IsNullOrWhiteSpace(admin.PasswordHash))
{
    throw new InvalidOperationException(
        "Admin:Username and Admin:PasswordHash must be configured. Generate a hash with " +
        "`dotnet run --project src/Callout.Web -- hash-password`, then store it with user-secrets " +
        "locally or in App Service application settings in production.");
}

builder.Services.AddSingleton(new AdminCredentials
{
    Username = admin.Username,
    PasswordHash = admin.PasswordHash
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
