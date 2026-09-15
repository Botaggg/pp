using Callout.Infrastructure;
using Callout.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Callout.Tests;

// Never use application credentials. Tests create and remove only their own database.
public sealed class PostgresDatabase : IAsyncLifetime
{
    private readonly string? _adminConnection = Environment.GetEnvironmentVariable("CALLOUT_TEST_CONNECTION_STRING");
    private readonly string _databaseName = "callout_test_" + Guid.NewGuid().ToString("N");
    private bool _created;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_adminConnection)) return;
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        _created = true;
        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _databaseName }.ConnectionString;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public CalloutDbContext CreateContext() => new(
        new DbContextOptionsBuilder<CalloutDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task DisposeAsync()
    {
        if (!_created) return;
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CALLOUT_TEST_CONNECTION_STRING")))
            Skip = "Set CALLOUT_TEST_CONNECTION_STRING to a disposable PostgreSQL server to run HTTP/database tests.";
    }
}

public sealed class CalloutFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string Username = "test-admin";
    public const string Password = "Only-for-local-integration-tests-123!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Admin:Username", Username);
        builder.UseSetting("Admin:TotpSecret", "");
        builder.UseSetting("Admin:PasswordHash", new PasswordHasher<object>().HashPassword(null!, Password));
    }

    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}
