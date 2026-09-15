using System.Net;
using System.Text.RegularExpressions;
using Callout.Core;
using Microsoft.EntityFrameworkCore;

namespace Callout.Tests;

public class Phase01Tests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static Dictionary<string, string> ValidRequest() => new()
    {
        ["Input.Name"] = " Integration Test ",
        ["Input.Phone"] = "415-555-0100",
        ["Input.Email"] = $"TEST-{Guid.NewGuid():N}@example.invalid",
        ["Input.Address"] = " Test address ",
        ["Input.Needs"] = " Help setting up a printer ",
        ["Input.Availability"] = "",
        ["Input.Website"] = ""
    };

    private static async Task<HttpResponseMessage> PostForm(HttpClient browser, string path, Dictionary<string, string> fields)
    {
        var html = await browser.GetStringAsync(path.Split('?')[0]);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, "The form must contain an antiforgery token.");
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        return await browser.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    [PostgresFact]
    public async Task HomeUrl_ShowsRequestForm()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var response = await browser.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Input.Name", await response.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task ValidRequest_WithEmptyOptionalFields_PersistsAndConfirms()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var fields = ValidRequest();
        var response = await PostForm(browser, "/RequestForm", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Confirmation", response.Headers.Location?.OriginalString);
        await using var db = database.CreateContext();
        var booking = await db.Bookings.Include(b => b.Client).SingleAsync(b => b.Client.Email == fields["Input.Email"].ToLowerInvariant());
        Assert.Equal(BookingStatus.Requested, booking.Status);
        Assert.Equal("Integration Test", booking.Client.Name);
        Assert.Equal("Test address", booking.Address);
        Assert.Equal("Help setting up a printer", booking.Description);
        Assert.Equal(string.Empty, booking.PreferredAvailability);
        Assert.Equal(TimeSpan.Zero, booking.CreatedAtUtc.Offset);
        Assert.Null(booking.ScheduledStart);
        Assert.Null(booking.ScheduledEnd);
        Assert.Contains("Request Received!", await browser.GetStringAsync("/Confirmation"));
    }

    [PostgresFact]
    public async Task RepeatClient_ReusesNormalizedEmailAndPreservesBookingAddress()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var fields = ValidRequest();
        Assert.Equal(HttpStatusCode.Redirect, (await PostForm(browser, "/RequestForm", fields)).StatusCode);
        fields["Input.Email"] = fields["Input.Email"].ToLowerInvariant();
        fields["Input.Address"] = "New address";
        Assert.Equal(HttpStatusCode.Redirect, (await PostForm(browser, "/RequestForm", fields)).StatusCode);
        await using var db = database.CreateContext();
        var client = await db.Clients.Include(c => c.Bookings).SingleAsync(c => c.Email == fields["Input.Email"]);
        Assert.Equal(2, client.Bookings.Count);
        Assert.Equal("New address", client.Address);
        Assert.Contains(client.Bookings, b => b.Address == "Test address");
    }

    [PostgresFact]
    public async Task Honeypot_DiscardsSubmission()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var fields = ValidRequest();
        fields["Input.Website"] = "spam.example";
        var response = await PostForm(browser, "/RequestForm", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.Clients.AnyAsync(c => c.Email == fields["Input.Email"].ToLowerInvariant()));
    }

    [PostgresFact]
    public async Task InvalidInput_ShowsValidationAndDoesNotPersist()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var fields = ValidRequest();
        fields["Input.Name"] = " ";
        fields["Input.Needs"] = "x";
        var response = await PostForm(browser, "/RequestForm", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Please enter your name.", await response.Content.ReadAsStringAsync());
        await using var db = database.CreateContext();
        Assert.False(await db.Clients.AnyAsync(c => c.Email == fields["Input.Email"].ToLowerInvariant()));
    }

    [PostgresFact]
    public async Task MissingAntiforgeryToken_IsRejected()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/RequestForm", new FormUrlEncodedContent(ValidRequest()))).StatusCode);
    }

    [PostgresFact]
    public async Task ViewingForms_DoesNotConsumePostQuota()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/RequestForm")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Login")).StatusCode);
        }
    }

    [PostgresFact]
    public async Task TooManyRequestPosts_AreRejected()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var fields = ValidRequest();
        fields["Input.Website"] = "spam.example";
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Redirect, (await PostForm(browser, "/RequestForm", fields)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostForm(browser, "/RequestForm", fields)).StatusCode);
    }

    [PostgresFact]
    public async Task BlankLogin_IsRejectedWithoutServerError()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var response = await PostForm(browser, "/Login", new() { ["Username"] = "", ["Password"] = "" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid username or password.", await response.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task AdminLogin_ProtectsQueueAndLogoutRevokesAccess()
    {
        await using var factory = new CalloutFactory(database.ConnectionString);
        using var browser = factory.CreateBrowser();
        var denied = await browser.GetAsync("/AdminRequests");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/Login", denied.Headers.Location!.OriginalString);
        var rejected = await PostForm(browser, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = "incorrect" });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/AdminRequests")).StatusCode);
        var fields = ValidRequest();
        Assert.Equal(HttpStatusCode.Redirect, (await PostForm(browser, "/RequestForm", fields)).StatusCode);
        var login = await PostForm(browser, "/Login", new() { ["Username"] = CalloutFactory.Username, ["Password"] = CalloutFactory.Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var cookie = string.Join(";", login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie);
        var queue = await browser.GetStringAsync("/AdminRequests");
        Assert.Contains(fields["Input.Email"].ToLowerInvariant(), queue);
        Assert.Equal(HttpStatusCode.Redirect, (await PostForm(browser, "/AdminRequests?handler=Logout", new())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/AdminRequests")).StatusCode);
    }

    [PostgresFact]
    public async Task Migration_MatchesModelAndIsIdempotent()
    {
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("20260915011613_InitialCreate", await db.Database.GetAppliedMigrationsAsync());
    }
}
