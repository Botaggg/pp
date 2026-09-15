using System.Net;
using System.Text.RegularExpressions;
using Callout.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Callout.Tests;

// Regression coverage for the vulnerabilities reproduced during the security audit.
[Trait("Security", "true")]
public class SecurityTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static Dictionary<string, string> Request(string email, string name = "Original owner") => new()
    {
        ["Input.Name"] = name, ["Input.Phone"] = "415-555-0100", ["Input.Email"] = email,
        ["Input.Address"] = "Original address", ["Input.Needs"] = "Local security audit only",
        ["Input.Availability"] = "", ["Input.Website"] = ""
    };

    private static async Task<HttpResponseMessage> Post(HttpClient browser, string path, Dictionary<string, string> fields)
    {
        var body = await browser.GetStringAsync(path.Split('?')[0]);
        var token = Regex.Match(body, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);
        return await browser.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    [PostgresFact]
    public async Task AnonymousVisitorCannotOverwriteExistingClient()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var original = factory.CreateBrowser();
        using var stranger = factory.CreateBrowser();
        var email = "audit-victim@example.invalid";
        Assert.Equal(HttpStatusCode.Redirect, (await Post(original, "/", Request(email))).StatusCode);
        var forged = Request(email, "Unverified replacement");
        forged["Input.Phone"] = "415-555-0199";
        forged["Input.Address"] = "Attacker supplied address";
        Assert.Equal(HttpStatusCode.Redirect, (await Post(stranger, "/", forged)).StatusCode);
        await using var db = database.CreateContext();
        var client = await db.Clients.Include(c => c.Bookings).SingleAsync(c => c.Email == email);
        Assert.Equal("Original owner", client.Name);
        Assert.Equal("415-555-0100", client.Phone);
        Assert.Equal("Original address", client.Address);
        Assert.Equal(2, client.Bookings.Count);
        Assert.Contains(client.Bookings, b => b.Address == "Original address" && b.SubmittedName == "Original owner");
    }

    [PostgresFact]
    public async Task UntrustedForwardedHeaderCannotBypassLoginThrottle()
    {
        await using var baseFactory = new CalloutFactory(database.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter>(new UntrustedPeer())));
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await Post(browser, "/Login", new() { ["Username"] = "wrong", ["Password"] = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(browser, "/Login", new() { ["Username"] = "wrong", ["Password"] = "wrong" })).StatusCode);
        browser.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.222");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(browser, "/Login", new() { ["Username"] = "wrong", ["Password"] = "wrong" })).StatusCode);
    }

    [PostgresFact]
    public async Task CopiedSessionIsRejectedAfterLogout()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var login = await Post(browser, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("callout.auth=")).Split(';')[0];
        using var copiedSession = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        copiedSession.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(browser, "/AdminRequests?handler=Logout", new())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await copiedSession.GetAsync("/AdminRequests")).StatusCode);
        factory.Services.GetRequiredService<AdminCredentials>().PasswordHash = new PasswordHasher<object>().HashPassword(null!, "new-local-audit-password");
        var oldPasswordLogin = await Post(browser, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password });
        Assert.Contains("Invalid username or password", await oldPasswordLogin.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await copiedSession.GetAsync("/AdminRequests")).StatusCode);
    }

    [PostgresFact]
    public async Task UnrelatedPostsCannotBlockSubmissionOrLogout()
    {
        await using var baseFactory = new CalloutFactory(database.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter>(new SeparateTestPeers())));
        using var visitor = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var admin = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        admin.DefaultRequestHeaders.Add("X-Test-Peer", "other");
        var login = await Post(admin, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("callout.auth=")).Split(';')[0];

        // Unmatched, read-only, unauthorized, and invalid-CSRF requests must not
        // consume the shared admission budget for actual form handlers.
        string[] paths = ["/not-a-page", "/Confirmation", "/AdminRequests?handler=Logout", "/RequestForm"];
        for (var i = 0; i < 120; i++)
            using (await visitor.PostAsync(paths[i % paths.Length], new FormUrlEncodedContent([]))) { }

        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, "/", Request($"quota-{Guid.NewGuid():N}@example.invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, "/AdminRequests?handler=Logout", new())).StatusCode);
        using var replay = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Redirect, (await replay.GetAsync("/AdminRequests")).StatusCode);
    }

    [PostgresFact]
    public async Task RejectedRequestPostsCannotSpendAnotherClientsAdmissionBudget()
    {
        await using var baseFactory = new CalloutFactory(database.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter>(new SeparateTestPeers())));
        using var visitor = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var other = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        other.DefaultRequestHeaders.Add("X-Test-Peer", "other");
        for (var i = 0; i < 110; i++)
        {
            using var response = await Post(visitor, i % 2 == 0 ? "/" : "/RequestForm", Request($"limited-{Guid.NewGuid():N}@example.invalid"));
            Assert.Equal(i < 10 ? HttpStatusCode.Redirect : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Redirect, (await Post(other, "/RequestForm", Request($"other-{Guid.NewGuid():N}@example.invalid"))).StatusCode);
    }

    [PostgresFact]
    public async Task RejectedLoginPostsCannotSpendAnotherClientsAdmissionBudget()
    {
        await using var baseFactory = new CalloutFactory(database.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter>(new SeparateTestPeers())));
        using var visitor = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var other = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        other.DefaultRequestHeaders.Add("X-Test-Peer", "other");
        for (var i = 0; i < 40; i++)
        {
            using var response = await Post(visitor, i % 2 == 0 ? "/Login" : "/login/", new() { ["Username"] = "wrong", ["Password"] = "wrong" });
            Assert.Equal(i < 10 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Redirect, (await Post(other, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/AdminRequests")).StatusCode);
    }

    [PostgresFact]
    public async Task InvalidCsrfCannotConsumeAggregateCapacityAcrossClients()
    {
        await using var baseFactory = new CalloutFactory(database.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter>(new SeparateTestPeers())));
        for (var i = 0; i < 110; i++)
        {
            using var visitor = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
            visitor.DefaultRequestHeaders.Add("X-Test-Peer", (i + 1).ToString());
            Assert.Equal(HttpStatusCode.BadRequest, (await visitor.PostAsync("/RequestForm", new FormUrlEncodedContent(Request("csrf@example.invalid")))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await visitor.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["Username"] = "wrong", ["Password"] = "wrong" }))).StatusCode);
        }
        using var other = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        other.DefaultRequestHeaders.Add("X-Test-Peer", "200");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(other, "/", Request($"csrf-control-{Guid.NewGuid():N}@example.invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(other, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password })).StatusCode);
    }

    [PostgresFact]
    public async Task AggregateSubmissionLimitIsPreservedWithoutBlockingLogout()
    {
        await using var baseFactory = new CalloutFactory(database.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter>(new SeparateTestPeers())));
        using var admin = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        admin.DefaultRequestHeaders.Add("X-Test-Peer", "200");
        var login = await Post(admin, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("callout.auth=")).Split(';')[0];
        for (var i = 0; i < 101; i++)
        {
            using var visitor = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
            visitor.DefaultRequestHeaders.Add("X-Test-Peer", (i / 10 + 1).ToString());
            using var response = await Post(visitor, i % 2 == 0 ? "/" : "/RequestForm", Request($"aggregate-{Guid.NewGuid():N}@example.invalid"));
            Assert.Equal(i < 100 ? HttpStatusCode.Redirect : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, "/AdminRequests?handler=Logout", new())).StatusCode);
        using var replay = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Redirect, (await replay.GetAsync("/AdminRequests")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password })).StatusCode);
    }

    [PostgresFact]
    public async Task AggregateLoginLimitIsPreservedWithoutBlockingSubmission()
    {
        await using var baseFactory = new CalloutFactory(database.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter>(new SeparateTestPeers())));
        for (var i = 0; i < 31; i++)
        {
            using var visitor = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
            visitor.DefaultRequestHeaders.Add("X-Test-Peer", (i / 10 + 1).ToString());
            using var response = await Post(visitor, i % 2 == 0 ? "/Login" : "/login/", new() { ["Username"] = "wrong", ["Password"] = "wrong" });
            Assert.Equal(i < 30 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        using var other = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        other.DefaultRequestHeaders.Add("X-Test-Peer", "200");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(other, "/RequestForm", Request($"login-capacity-{Guid.NewGuid():N}@example.invalid"))).StatusCode);
    }

    [PostgresFact]
    public async Task Verified_RazorEscapesClientHtmlAndIgnoresPostedBookingState()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var fields = Request("audit-encoding@example.invalid", "<img src=x onerror=alert(1)>");
        fields["Input.Status"] = "Paid";
        fields["Input.ClientId"] = "999";
        Assert.Equal(HttpStatusCode.Redirect, (await Post(browser, "/", fields)).StatusCode);
        await Post(browser, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password });
        var body = await browser.GetStringAsync("/AdminRequests");
        Assert.Contains("&lt;img", body);
        Assert.DoesNotContain("<img src=x", body);
        await using var db = database.CreateContext();
        var booking = await db.Bookings.SingleAsync(b => b.Client.Email == fields["Input.Email"]);
        Assert.Equal(Callout.Core.BookingStatus.Requested, booking.Status);
        Assert.NotEqual(999, booking.ClientId);
        var noCsrf = await browser.PostAsync("/AdminRequests?handler=Logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [PostgresFact]
    public async Task PasswordRotationAndAbsoluteExpiryInvalidateExistingCookies()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(browser, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/AdminRequests")).StatusCode);
        factory.Services.GetRequiredService<AdminCredentials>().PasswordHash = new PasswordHasher<object>().HashPassword(null!, "rotated-password");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/AdminRequests")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(browser, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = "rotated-password" })).StatusCode);
        await using var db = database.CreateContext();
        await db.AdminSessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/AdminRequests")).StatusCode);
    }

    [PostgresFact]
    public async Task AuthenticatorIsRequiredAndCodesCannotBeReplayed()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var admin = factory.Services.GetRequiredService<AdminCredentials>();
        admin.TotpSecret = OtpNet.Base32Encoding.ToString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
        var fields = new Dictionary<string, string> { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password };
        Assert.Equal(HttpStatusCode.OK, (await Post(browser, "/Login", fields)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/AdminRequests")).StatusCode);
        fields["Code"] = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(admin.TotpSecret)).ComputeTotp();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(browser, "/Login", fields)).StatusCode);
        using var other = factory.CreateBrowser();
        Assert.Equal(HttpStatusCode.OK, (await Post(other, "/Login", fields)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await other.GetAsync("/AdminRequests")).StatusCode);
        var recovery = Guid.NewGuid().ToString("N").ToUpperInvariant();
        admin.RecoveryCodeHashes = [Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(recovery)))];
        fields["Code"] = recovery;
        Assert.Equal(HttpStatusCode.Redirect, (await Post(other, "/Login", fields)).StatusCode);
        using var third = factory.CreateBrowser();
        Assert.Equal(HttpStatusCode.OK, (await Post(third, "/Login", fields)).StatusCode);
    }

    [PostgresFact]
    public async Task SecurityHeadersAndRequestSizeLimitAreEnforced()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var response = await browser.GetAsync("/Login");
        Assert.Contains("script-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.DoesNotContain("<script", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await browser.PostAsync("/", new StringContent(new string('x', 17000)))).StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentFirstRequestsCreateOneClientAndKeepEverySubmission()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        var email = $"concurrent-{Guid.NewGuid():N}@example.invalid";
        var browsers = Enumerable.Range(0, 5).Select(_ => factory.CreateBrowser()).ToArray();
        try
        {
            var responses = await Task.WhenAll(browsers.Select((b, i) => Post(b, "/", Request(email, "Visitor " + i))));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
            await using var db = database.CreateContext();
            Assert.Equal(1, await db.Clients.CountAsync(c => c.Email == email));
            Assert.Equal(5, await db.Bookings.CountAsync(b => b.SubmittedEmail == email));
            Assert.Equal(5, await db.Bookings.Where(b => b.SubmittedEmail == email).Select(b => b.SubmittedName).Distinct().CountAsync());
        }
        finally { foreach (var b in browsers) b.Dispose(); }
    }

    [PostgresFact]
    public async Task AdminQueuePaginatesWithoutDroppingOrRepeatingRequests()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        await using var db = database.CreateContext();
        var client = new Callout.Core.Client { Name = "Pagination", Email = $"pages-{Guid.NewGuid():N}@example.invalid", Phone = "415-555-0100", Address = "Test" };
        db.Clients.Add(client);
        var time = DateTimeOffset.UtcNow.AddDays(1);
        for (int i = 0; i < 51; i++) db.Bookings.Add(new Callout.Core.Booking
        {
            Client = client, SubmittedName = $"page-marker-{i:D3}", SubmittedPhone = client.Phone, SubmittedEmail = client.Email,
            Description = "Pagination test", Address = "Test", CreatedAtUtc = time.AddSeconds(i)
        });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(browser, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password })).StatusCode);
        var first = await browser.GetStringAsync("/AdminRequests");
        Assert.Equal(50, Regex.Matches(first, "page-marker-").Count);
        Assert.Contains("page-marker-050", first);
        Assert.DoesNotContain("page-marker-000", first);
        var second = await browser.GetStringAsync("/AdminRequests?pageNumber=2");
        Assert.Contains("page-marker-000", second);
        Assert.DoesNotContain("page-marker-001", second);
        // Avoid affecting other tests' first-page expectations.
        await db.Bookings.Where(b => b.ClientId == client.Id).ExecuteDeleteAsync();
        await db.Clients.Where(c => c.Id == client.Id).ExecuteDeleteAsync();
    }

    // This header is interpreted only by the test host, never by application code.
    private sealed class SeparateTestPeers : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, invokeNext) =>
            {
                var peer = context.Request.Headers["X-Test-Peer"].ToString();
                context.Connection.RemoteIpAddress = IPAddress.Parse(
                    int.TryParse(peer, out var number) && number is > 0 and < 255
                        ? $"198.51.100.{number}"
                        : peer == "other" ? "203.0.113.20" : "203.0.113.10");
                await invokeNext();
            });
            next(app);
        };
    }

    private sealed class UntrustedPeer : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, invokeNext) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
                await invokeNext();
            });
            next(app);
        };
    }
}
