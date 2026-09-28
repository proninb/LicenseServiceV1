using Microsoft.EntityFrameworkCore;

namespace LicenseService.Data;

public sealed class LicenseDbContext(
    DbContextOptions<LicenseDbContext> options)
    : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    public DbSet<ServerInstance> ServerInstances => Set<ServerInstance>();

    public DbSet<LeaseRecord> Leases => Set<LeaseRecord>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(value => value.Id);

            entity.Property(value => value.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(value => value.EntraTenantId)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(value => value.EntraClientId)
                .HasMaxLength(128)
                .IsRequired();

            entity.HasIndex(
                    value => new
                    {
                        value.EntraTenantId,
                        value.EntraClientId,
                    })
                .IsUnique();
        });

        modelBuilder.Entity<Entitlement>(entity =>
        {
            entity.HasKey(value => value.Id);

            entity.Property(value => value.Product)
                .HasMaxLength(64)
                .IsRequired();

            entity.HasOne(value => value.Customer)
                .WithMany(value => value.Entitlements)
                .HasForeignKey(value => value.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(
                    value => new
                    {
                        value.CustomerId,
                        value.Product,
                    })
                .IsUnique();
        });

        modelBuilder.Entity<ServerInstance>(entity =>
        {
            entity.HasKey(value => value.Id);

            entity.Property(value => value.Id)
                .HasMaxLength(128);

            entity.HasOne(value => value.Customer)
                .WithMany()
                .HasForeignKey(value => value.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(
                value => value.CustomerId);
        });

        modelBuilder.Entity<LeaseRecord>(entity =>
        {
            entity.HasKey(value => value.Id);

            entity.Property(value => value.ServerInstanceId)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(value => value.Product)
                .HasMaxLength(64)
                .IsRequired();

            entity.HasIndex(
                value => new
                {
                    value.CustomerId,
                    value.ExpiresAtUtc,
                    value.ReleasedAtUtc,
                });

            entity.HasIndex(
                value => new
                {
                    value.ServerInstanceId,
                    value.ExpiresAtUtc,
                    value.ReleasedAtUtc,
                });
        });
    }
}
