using BillingApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Migrations;

namespace BillingApi.Infrastructure;

/// <summary>
/// The service's context. No configuration base class: ids, <c>Money</c>, audit/soft-delete/tenant columns,
/// <c>xmin</c> concurrency and snake_case all come from conventions. The configurations below only add what a
/// convention cannot know: lengths, indexes, the encrypted columns.
/// </summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
}

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(200);

        // Encrypted at rest (AES-256-GCM, per-tenant data key) and findable by value through a blind index that
        // ignores case and surrounding whitespace.
        builder.Property(c => c.Email).HasMaxLength(320)
            .Encrypt("billing.customer.email")
            .WithBlindIndex(BlindIndexNormalization.Trim | BlindIndexNormalization.CaseFold);

        builder.Property(c => c.TaxNumber).HasMaxLength(40).Encrypt("billing.customer.tax_number");
    }
}

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.Property(i => i.Number).HasMaxLength(32);
        builder.Property(i => i.TaxRateCode).HasMaxLength(16);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(i => new { i.TenantId, i.Number }).IsUnique();

        // The lines are part of the aggregate: always loaded with it (backed by the _lines field by convention).
        builder.HasMany(i => i.Lines).WithOne().IsRequired();
        builder.Navigation(i => i.Lines).AutoInclude();
    }
}

public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines");
        builder.Property(l => l.Description).HasMaxLength(200);
    }
}

public sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        builder.Property(t => t.Id).HasMaxLength(16);
        builder.Property(t => t.Description).HasMaxLength(100);
        builder.Property(t => t.Rate).HasPrecision(5, 4);
    }
}

/// <summary>
/// <c>dotnet ef migrations add</c> builds the context through this factory. It connects as the migration role
/// (<c>SharedKernel:Persistence:billing:MigrationConnectionString</c>, else <c>ConnectionStrings:billing</c>).
/// </summary>
public sealed class BillingDbContextFactory() : PostgresDesignTimeDbContextFactory<BillingDbContext>(BillingDatabase.ConnectionName)
{
    protected override BillingDbContext Create(DbContextOptions<BillingDbContext> options, PersistenceContextDependencies dependencies)
        => new(options, dependencies);

    // Same capabilities as Program.cs: encryption adds blind-index columns and widens encrypted ones.
    protected override void ConfigurePersistence(EfCorePersistenceBuilder<BillingDbContext> persistence)
        => BillingDatabase.Configure(persistence);
}

public static class BillingDatabase
{
    /// <summary>The connection name: <c>ConnectionStrings:billing</c> + <c>SharedKernel:Persistence:billing</c>.</summary>
    public const string ConnectionName = "billing";

    /// <summary>The persistence capabilities, used by both the service registration and the design-time factory.</summary>
    public static EfCorePersistenceBuilder<BillingDbContext> Configure(EfCorePersistenceBuilder<BillingDbContext> persistence) =>
        persistence
            .UseMultiTenancy(rowLevelSecurity: true)                           // tenant filter + write guard + RLS
            .UseAuditTrail()                                                   // IAuditTrailWriter, sealer, self-check
            .UseFieldEncryption(k => k.FromConfiguration().UseTenantDataKeys()); // AES-GCM columns, per-tenant keys
}
