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

    public DbSet<AdminSession> AdminSessions => Set<AdminSession>();
    public DbSet<UsedAdminCode> UsedAdminCodes => Set<UsedAdminCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminSession>().Property(s => s.CredentialVersion).HasMaxLength(64);
        modelBuilder.Entity<AdminSession>().HasIndex(s => s.ExpiresAtUtc);
        modelBuilder.Entity<UsedAdminCode>().Property(c => c.Id).HasMaxLength(160);
        modelBuilder.Entity<UsedAdminCode>().HasIndex(c => c.ExpiresAtUtc);
        var client = modelBuilder.Entity<Client>();
        client.Property(c => c.Name).HasMaxLength(120).IsRequired();
        client.Property(c => c.Phone).HasMaxLength(40).IsRequired();
        client.Property(c => c.Email).HasMaxLength(200).IsRequired();
        client.Property(c => c.Address).HasMaxLength(300).IsRequired();
        client.Property(c => c.Notes).HasMaxLength(1000);
        client.HasIndex(c => c.Email).IsUnique();

        var booking = modelBuilder.Entity<Booking>();
        booking.Property(b => b.SubmittedName).HasMaxLength(120).IsRequired();
        booking.Property(b => b.SubmittedPhone).HasMaxLength(40).IsRequired();
        booking.Property(b => b.SubmittedEmail).HasMaxLength(200).IsRequired();
        booking.Property(b => b.Description).HasMaxLength(2000).IsRequired();
        booking.Property(b => b.Address).HasMaxLength(300).IsRequired();
        booking.Property(b => b.PreferredAvailability).HasMaxLength(300);

        // Stored as text so the database stays readable and adding a status later
        // does not silently renumber the existing rows.
        booking.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);

        booking.HasOne(b => b.Client)
            .WithMany(c => c.Bookings)
            .HasForeignKey(b => b.ClientId);

        booking.HasIndex(b => b.CreatedAtUtc);
    }
}
