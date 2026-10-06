using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Migrations;
using Shop.Catalog.Domain;

namespace Shop.Catalog.Infrastructure.Persistence;

/// <summary>
/// The catalog's context. Ids, <c>Money</c>, the tenant and audit columns, <c>xmin</c> and snake_case come from the
/// kernel's conventions; the configuration below adds only lengths and the per-tenant SKU index.
/// </summary>
public sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options,
    PersistenceContextDependencies dependencies
) : TenantedDbContext(options, dependencies)
{
    public DbSet<Product> Products => Set<Product>();
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Sku).HasMaxLength(32);
        builder.Property(p => p.Name).HasMaxLength(Product.MaxNameLength);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Brand).HasMaxLength(100);
        builder.Property(p => p.Category).HasMaxLength(100);
        builder.Property(p => p.ImageKey).HasMaxLength(200);
        builder.HasIndex(p => new { p.TenantId, p.Sku }).IsUnique();
    }
}

/// <summary><c>dotnet ef</c> builds the context through this factory, with the same capabilities as the host.</summary>
public sealed class CatalogDbContextFactory()
    : PostgresDesignTimeDbContextFactory<CatalogDbContext>(CatalogDatabase.ConnectionName)
{
    protected override CatalogDbContext Create(
        DbContextOptions<CatalogDbContext> options,
        PersistenceContextDependencies dependencies
    ) => new(options, dependencies);

    protected override void ConfigurePersistence(
        EfCorePersistenceBuilder<CatalogDbContext> persistence
    ) => CatalogDatabase.Configure(persistence);
}

/// <summary>The catalog database: its connection name and persistence capabilities.</summary>
public static class CatalogDatabase
{
    /// <summary><c>ConnectionStrings:catalog</c> and <c>SharedKernel:Persistence:catalog</c>.</summary>
    public const string ConnectionName = "catalog";

    /// <summary>The capabilities, shared by the host registration and the design-time factory.</summary>
    public static EfCorePersistenceBuilder<CatalogDbContext> Configure(
        EfCorePersistenceBuilder<CatalogDbContext> persistence
    ) => persistence.UseMultiTenancy(rowLevelSecurity: true);
}
