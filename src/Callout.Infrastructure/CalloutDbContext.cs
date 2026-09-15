using Callout.Core;
using Microsoft.EntityFrameworkCore;

namespace Callout.Infrastructure;

public class CalloutDbContext : DbContext
{
    public CalloutDbContext(DbContextOptions<CalloutDbContext> options) : base(options)
    {
    }

    public DbSet<Booking> Bookings { get; set; } = null!;
    public DbSet<Client> Clients { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Client)
            .WithMany(c => c.Bookings)
            .HasForeignKey(b => b.ClientId);
    }
}
