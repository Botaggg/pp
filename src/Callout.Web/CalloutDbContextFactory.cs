using Callout.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Callout.Web;

/// <summary>
/// Used only by `dotnet ef` at design time. It reads the same configuration the app
/// does, so migrations run against the real database, and falls back to a local
/// placeholder so `migrations add` still works on a fresh clone with no secrets set.
/// </summary>
public class CalloutDbContextFactory : IDesignTimeDbContextFactory<CalloutDbContext>
{
    public CalloutDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(typeof(CalloutDbContextFactory).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Database=callout;Username=postgres;Password=postgres";
        }

        var options = new DbContextOptionsBuilder<CalloutDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CalloutDbContext(options);
    }
}
