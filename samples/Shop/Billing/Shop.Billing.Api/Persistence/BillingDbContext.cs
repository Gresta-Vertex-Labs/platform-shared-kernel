using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Migrations;
using Shop.Billing.Api.Payments;
using Shop.Billing.Api.Profiles;

namespace Shop.Billing.Api.Persistence;

/// <summary>
/// Billing's context: payments and merchant billing profiles (tenant data under row-level security), and MassTransit's
/// inbox and outbox.
/// </summary>
public sealed class BillingDbContext(
    DbContextOptions<BillingDbContext> options,
    PersistenceContextDependencies dependencies
) : TenantedDbContext(options, dependencies)
{
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<BillingProfile> Profiles => Set<BillingProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // MassTransit's outbox: an event commits with the payment that raised it. Its bookkeeping spans tenants (the
        // delivery service sends every tenant's messages), so it is not tenant data.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
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

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        // One payment per order and tenant: a retried charge finds the first instead of charging twice.
        builder.HasIndex(p => new { p.TenantId, p.OrderId }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.CustomerEmail });
        builder.Property(p => p.CustomerEmail).HasMaxLength(320);
        builder.Property(p => p.ProviderReference).HasMaxLength(64);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.InvoiceSigningKeyId).HasMaxLength(64);
    }
}

public sealed class BillingProfileConfiguration : IEntityTypeConfiguration<BillingProfile>
{
    public void Configure(EntityTypeBuilder<BillingProfile> builder)
    {
        builder.ToTable("billing_profiles");
        builder.HasIndex(p => p.TenantId).IsUnique();
        builder.Property(p => p.LegalName).HasMaxLength(200);
        builder.Property(p => p.Country).HasMaxLength(2);
        builder.Property(p => p.VatNumber).HasMaxLength(32);
        builder.Property(p => p.Bic).HasMaxLength(11);
        // An envelope: the IBAN encrypted under a fresh data key, wrapped by the Key Vault master key.
        builder.Property(p => p.IbanEnvelope).HasMaxLength(2048);
    }
}

/// <summary><c>dotnet ef</c> builds the context through this factory.</summary>
public sealed class BillingDbContextFactory()
    : PostgresDesignTimeDbContextFactory<BillingDbContext>(BillingDatabase.ConnectionName)
{
    protected override BillingDbContext Create(
        DbContextOptions<BillingDbContext> options,
        PersistenceContextDependencies dependencies
    ) => new(options, dependencies);

    protected override void ConfigurePersistence(
        EfCorePersistenceBuilder<BillingDbContext> persistence
    ) => BillingDatabase.Configure(persistence);
}

public static class BillingDatabase
{
    public const string ConnectionName = "billing";

    public static EfCorePersistenceBuilder<BillingDbContext> Configure(
        EfCorePersistenceBuilder<BillingDbContext> persistence
    ) => persistence.UseMultiTenancy(rowLevelSecurity: true);
}
