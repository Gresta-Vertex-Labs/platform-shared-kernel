using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Migrations;
using Shop.Ordering.Domain;

namespace Shop.Ordering.Infrastructure.Persistence;

/// <summary>
/// Ordering's context: orders and their lines (tenant data under row-level security), the users' authenticator
/// enrollments (not tenant data), and MassTransit's inbox and outbox, so an integration event commits with the change
/// that raised it.
/// </summary>
public sealed class OrderingDbContext(
    DbContextOptions<OrderingDbContext> options,
    PersistenceContextDependencies dependencies
) : TenantedDbContext(options, dependencies)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<TotpEnrollment> TotpEnrollments => Set<TotpEnrollment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        // MassTransit's delivery bookkeeping spans tenants: the outbox delivers every tenant's messages.
        modelBuilder
            .Entity<MassTransit.EntityFrameworkCoreIntegration.InboxState>()
            .IsTenantShared();
        modelBuilder
            .Entity<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>()
            .IsTenantShared();
        modelBuilder
            .Entity<MassTransit.EntityFrameworkCoreIntegration.OutboxState>()
            .IsTenantShared();
    }
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // Personal data: encrypted at rest; the email is also findable through a blind index.
        builder
            .Property(o => o.CustomerEmail)
            .HasMaxLength(320)
            .Encrypt("ordering.order.customer_email")
            .WithBlindIndex(BlindIndexNormalization.Trim | BlindIndexNormalization.CaseFold);
        builder
            .Property(o => o.ShippingAddress)
            .HasMaxLength(500)
            .Encrypt("ordering.order.shipping_address");
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.RejectionReason).HasMaxLength(200);
        builder.Ignore(o => o.Total);

        builder.HasMany(o => o.Lines).WithOne().IsRequired();
        builder.Navigation(o => o.Lines).AutoInclude();
    }
}

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");
        builder.Property(l => l.Sku).HasMaxLength(32);
        builder.Ignore(l => l.Total);
    }
}

public sealed class TotpEnrollmentConfiguration : IEntityTypeConfiguration<TotpEnrollment>
{
    public void Configure(EntityTypeBuilder<TotpEnrollment> builder)
    {
        // An identity's secret, not tenant data.
        builder.IsTenantShared();
        builder.Property(t => t.SubjectId).HasMaxLength(256);
        builder.HasIndex(t => t.SubjectId).IsUnique();
        builder.Property(t => t.SecretBase32).HasMaxLength(64).Encrypt("ordering.totp.secret");
    }
}

/// <summary><c>dotnet ef</c> builds the context through this factory, with the host's capabilities.</summary>
public sealed class OrderingDbContextFactory()
    : PostgresDesignTimeDbContextFactory<OrderingDbContext>(OrderingDatabase.ConnectionName)
{
    protected override OrderingDbContext Create(
        DbContextOptions<OrderingDbContext> options,
        PersistenceContextDependencies dependencies
    ) => new(options, dependencies);

    protected override void ConfigurePersistence(
        EfCorePersistenceBuilder<OrderingDbContext> persistence
    ) => OrderingDatabase.Configure(persistence);
}

/// <summary>The ordering database: its connection name and persistence capabilities.</summary>
public static class OrderingDatabase
{
    public const string ConnectionName = "ordering";

    /// <summary>The audit sealer's data source: it writes the ledger's chain links as its own database role.</summary>
    public const string AuditSealer = "audit-sealer";

    /// <summary>Where the audit sealer's connection settings live.</summary>
    public const string AuditSealerSection = "SharedKernel:Persistence:" + AuditSealer;

    /// <summary>Row-level security, the signed audit ledger, and field encryption with keys from configuration.</summary>
    public static EfCorePersistenceBuilder<OrderingDbContext> Configure(
        EfCorePersistenceBuilder<OrderingDbContext> persistence
    ) =>
        persistence
            .UseMultiTenancy(rowLevelSecurity: true)
            .UseAuditTrail()
            .UseFieldEncryption(keys => keys.FromConfiguration());
}
