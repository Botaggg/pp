using Npgsql;
using System.Net;
using Microsoft.AspNetCore.Http.Features;
using OtpNet;
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
    string plaintext;
    if (Console.IsInputRedirected) plaintext = Console.ReadLine() ?? string.Empty;
    else
    {
        var buffer = new System.Text.StringBuilder();
        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && buffer.Length > 0) buffer.Length--;
            else if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
        plaintext = buffer.ToString();
    }
    if (plaintext.Length < 16) throw new ArgumentException("Use at least 16 characters for the admin password.");
    Console.WriteLine();
    Console.WriteLine(new PasswordHasher<object>().HashPassword(null!, plaintext));
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
    options.Conventions.AddPageRoute("/RequestForm", ""));

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

// Forwarded headers are accepted only from explicitly configured proxy addresses.
// Never trust arbitrary X-Forwarded-For values for login throttling.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var proxy in builder.Configuration.GetSection("TrustedProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});
if (builder.Configuration.GetValue<bool>("ForwardedHeaders_Enabled"))
    throw new InvalidOperationException("Unrestricted automatic forwarded headers must be disabled.");

var databaseSettings = new NpgsqlConnectionStringBuilder(connectionString);
if (builder.Environment.IsProduction() && databaseSettings.SslMode != SslMode.VerifyFull)
    throw new InvalidOperationException("Production database connections must use SSL Mode=VerifyFull.");
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueCountLimit = 20;
    options.KeyLengthLimit = 100;
    options.ValueLengthLimit = 4096;
    options.MultipartBodyLengthLimit = 16384;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 16384;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Fixed-window rate limiters, keyed by client IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // A second, shared limit cannot be bypassed by rotating or spoofing client IPs.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        !HttpMethods.IsPost(context.Request.Method) ? RateLimitPartition.GetNoLimiter("read") :
        RateLimitPartition.GetFixedWindowLimiter(string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/Login", StringComparison.OrdinalIgnoreCase) ? "login" : "forms",
            key => new FixedWindowRateLimiterOptions
            {
                PermitLimit = key == "login" ? 30 : 100,
                Window = TimeSpan.FromMinutes(15), QueueLimit = 0
            }));

    options.AddPolicy("request-form", context =>
        !HttpMethods.IsPost(context.Request.Method)
        ? RateLimitPartition.GetNoLimiter("read")
        : RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    options.AddPolicy("login", context =>
        !HttpMethods.IsPost(context.Request.Method)
        ? RateLimitPartition.GetNoLimiter("read")
        : RateLimitPartition.GetFixedWindowLimiter(
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
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.EventsType = typeof(AdminSessionSecurity);
        options.SlidingExpiration = true;
        options.Cookie.Name = "callout.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AdminSessionSecurity>();

// Admin credentials from config. The hash is stored, never the password.
var admin = builder.Configuration.GetSection("Admin").Get<AdminOptions>() ?? new AdminOptions();
if (string.IsNullOrWhiteSpace(admin.Username) || string.IsNullOrWhiteSpace(admin.PasswordHash))
{
    throw new InvalidOperationException(
        "Admin:Username and Admin:PasswordHash must be configured. Generate a hash with " +
        "`dotnet run --project src/Callout.Web -- hash-password`, then store it with user-secrets " +
        "locally or in App Service application settings in production.");
}

try
{
    var hash = Convert.FromBase64String(admin.PasswordHash);
    if (hash.Length != 61 || hash[0] != 1 ||
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(1, 4)) != 2 ||
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(5, 4)) is < 100000 or > 1000000 ||
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(9, 4)) != 16)
        throw new FormatException();
    _ = new PasswordHasher<object>().VerifyHashedPassword(null!, admin.PasswordHash, "configuration-validation");
}
catch (Exception ex) when (ex is FormatException or ArgumentException or IndexOutOfRangeException)
{
    throw new InvalidOperationException("Admin password hash must be a valid Identity V3 hash with at least 100000 iterations.");
}
if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(admin.TotpSecret))
    throw new InvalidOperationException("Production admin sign-in requires an authenticator secret.");
if (!string.IsNullOrEmpty(admin.TotpSecret) && Base32Encoding.ToBytes(admin.TotpSecret).Length < 20)
    throw new InvalidOperationException("Admin authenticator secret must contain at least 160 bits.");
if (admin.RecoveryCodeHashes.Any(h => h.Length != 64 || !h.All(char.IsAsciiHexDigit)))
    throw new InvalidOperationException("Recovery codes must be stored as SHA-256 hashes.");

builder.Services.AddSingleton(new AdminCredentials
{
    Username = admin.Username,
    PasswordHash = admin.PasswordHash,
    TotpSecret = admin.TotpSecret,
    RecoveryCodeHashes = admin.RecoveryCodeHashes
});

var app = builder.Build();

app.UseForwardedHeaders();

// App Service enforces HTTPS at its public ingress. A deployment-only setting
// describes that boundary; no visitor-supplied header can change the scheme.
if (builder.Configuration.GetValue<bool>("Hosting:HttpsTerminated"))
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")))
        throw new InvalidOperationException("HTTPS termination mode is only supported on Azure App Service.");
    app.Use((context, next) => { context.Request.Scheme = "https"; return next(context); });
}
app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'none'; style-src 'self'; img-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.CacheControl = "no-store";
    // Also enforce the bound under in-process hosts that do not use Kestrel.
    if (context.Request.ContentLength > 16384) { context.Response.StatusCode = 413; return; }
    await next(context);
});

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

// Expose the entry point for HTTP integration tests.
public partial class Program { }
