using Callout.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Callout.Web;

/// <summary>
/// Used only by migrations. Production migration credentials are supplied explicitly
/// through ConnectionStrings__MigrationConnection and never used by the web app.
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

        var connectionString = configuration.GetConnectionString("MigrationConnection")
            ?? configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Database=callout_dev;Username=callout_dev_app";
        }

        var options = new DbContextOptionsBuilder<CalloutDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CalloutDbContext(options);
    }
}
