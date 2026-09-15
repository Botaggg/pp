using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Callout.Infrastructure;

/// <summary>
/// Used only by `dotnet ef` at design time so migrations can be scaffolded without
/// a live database or a real connection string. Never used at runtime.
/// </summary>
public class CalloutDbContextFactory : IDesignTimeDbContextFactory<CalloutDbContext>
{
    public CalloutDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CALLOUT_CONNECTION")
            ?? "Host=localhost;Database=callout;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<CalloutDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CalloutDbContext(options);
    }
}
