using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Entities;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

// ---------------------------------------------------------------------------
// Real PostgreSQL verification suite. Shared domain/EF fixtures for
// every *PostgresTests class in this folder. One aggregate model, deliberately rich enough to prove
// every named Critical/High scenario against a REAL PostgreSQL instance (Testcontainers) rather than
// SQLite: tenant isolation (query/keyset/bulk), the explicit cross-tenant scope bypass, soft-delete
// cascade over BOTH a required owned-type collection (OwnsMany, its own table) and a same-table owned
// value object, domain-event dispatch from a hard-deleted aggregate, retry-safe
// ExecuteInTransactionAsync under a genuine transient-fault injection, pooled-factory tenant
// attachment from a background scope, and the aggregate-touch-root concurrency-conflict/classifier
// paths — all against real xmin-bound concurrency tokens and real Npgsql exceptions, which SQLite
// cannot provide (see SharedKernel.Persistence.EfCore.Tests.Interceptors' own SQLite-scoped
// disclaimers for why these specific scenarios were previously only proven there).
// ---------------------------------------------------------------------------

public sealed record PgOrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static PgOrderId New() => new(Guid.NewGuid());
}

public sealed record PgOrderTagId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static PgOrderTagId New() => new(Guid.NewGuid());
}

/// <summary>Domain event proving the "events from a hard-deleted aggregate" dispatch scenario.</summary>
public sealed record PgOrderHardDeletedEvent : DomainEvent;

/// <summary>
/// Same-table owned value object — proves VO columns survive a soft-delete untouched.
/// </summary>
/// <remarks>
/// Mapped via <c>OwnsOne</c>. EF Core 10's owned-type-same-table PK-sharing convention
/// names the dependent's shadow FK/PK after the principal CLR type ("PgOrderAggregateId") rather
/// than reusing the principal's own key property name ("Id") when the principal's key is a
/// strongly-typed-id-converted property — a real EF Core modeling interaction, not a
/// <c>06.Persistence</c> defect, worked around below by pinning the shadow property explicitly in
/// <see cref="PgOrderAggregateConfig"/>. Kept as a genuine <c>OwnsOne</c> (not flattened to scalar
/// columns) specifically so this suite also proves that interaction is handled correctly.
/// </remarks>
public sealed class PgAddress
{
    public string Street { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;

    public PgAddress(string street, string city)
    {
        Street = street;
        City = city;
    }

    private PgAddress() { } // ORM path
}

/// <summary>
/// Owned-collection line item — its own table (<c>OwnsMany</c>), required+cascade by EF Core default,
/// so soft-deleting the owning <see cref="PgOrderAggregate"/> exercises
/// <c>SoftDeleteInterceptor</c>'s cascade-rescue path, and editing ONE line's quantity (leaving the
/// root row itself untouched) exercises <see cref="AggregateRootTouchInterceptor"/>.
/// </summary>
public sealed class PgOrderLine
{
    public Guid LineId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public int Quantity { get; private set; }

    public PgOrderLine(string description, int quantity)
    {
        LineId = Guid.NewGuid();
        Description = description;
        Quantity = quantity;
    }

    private PgOrderLine() { } // ORM path

    public void ChangeQuantity(int quantity) => Quantity = quantity;
}

/// <summary>
/// Root aggregate for the whole Postgres suite — tenanted, soft-deletable, audited, and
/// concurrency-tokened (via <see cref="TenantedFullAuditableAggregateRoot{TId}"/>), with a same-table
/// owned VO (<see cref="Address"/>) and an owned-collection required child (<see cref="Lines"/>).
/// </summary>
public sealed class PgOrderAggregate : TenantedFullAuditableAggregateRoot<PgOrderId>
{
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public PgAddress Address { get; private set; } = null!;
    public List<PgOrderLine> Lines { get; private set; } = [];

    /// <summary>
    /// Required <see cref="Money"/> complex property — two independently queryable columns.
    /// Defaults to zero USD so every one of this fixture's pre-existing constructor call sites (none
    /// of which pass a total) keeps compiling; tests that care about the total call <see cref="SetTotal"/>.
    /// </summary>
    public Money Total { get; private set; } = Money.Zero(Currency.Usd);

    /// <summary>Optional <see cref="Money"/> complex property — proves nullable round-trip.</summary>
    public Money? DiscountTotal { get; private set; }

    public PgOrderAggregate(PgOrderId id, Guid tenantId, string name, string code, string street, string city, IClock clock)
        : base(id, tenantId, clock)
    {
        Name = name;
        Code = code;
        Address = new PgAddress(street, city);
    }

    private PgOrderAggregate() { } // ORM path

    protected override void OnDelete() { }

    public void AddLine(string description, int quantity) => Lines.Add(new PgOrderLine(description, quantity));

    public void ChangeLineQuantity(Guid lineId, int quantity) =>
        Lines.First(l => l.LineId == lineId).ChangeQuantity(quantity);

    public void Rename(string name) => Name = name;

    public void SetTotal(Money total) => Total = total;

    public void SetDiscountTotal(Money? discountTotal) => DiscountTotal = discountTotal;

    /// <summary>Raises the hard-delete-dispatch proof event.</summary>
    public void RaiseHardDeletedEvent() => RaiseDomainEvent(ts => new PgOrderHardDeletedEvent { OccurredOn = ts });
}

/// <summary>
/// Standalone, non-owned, non-tenanted child with a REQUIRED foreign key to
/// <see cref="PgOrderAggregate"/> — the FK-violation classification proof inserts one referencing a
/// non-existent order id.
/// </summary>
public sealed class PgOrderTag : Entity<PgOrderTagId>
{
    public PgOrderId OrderId { get; private set; } = null!;
    public string Label { get; private set; } = string.Empty;

    public PgOrderTag(PgOrderTagId id, PgOrderId orderId, string label) : base(id)
    {
        OrderId = orderId;
        Label = label;
    }

    private PgOrderTag() { } // ORM path
}

public sealed record PgHardDeleteId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static PgHardDeleteId New() => new(Guid.NewGuid());
}

/// <summary>Domain event proving the "events from a hard-deleted aggregate" dispatch scenario.</summary>
public sealed record PgHardDeleteAggregateRemovedEvent : DomainEvent;

/// <summary>
/// Tenanted and audited, but deliberately NOT <c>ISoftDeletable</c> — <c>Remove()</c> +
/// <c>SaveChangesAsync</c> issues a genuine physical <c>DELETE</c>, the only way to prove domain
/// events raised on an about-to-be-hard-deleted aggregate are still dispatched (pre-commit, before
/// the tracked entity's state — and its events — would otherwise be lost to the physical delete).
/// </summary>
public sealed class PgHardDeleteAggregate : TenantedAuditableAggregateRoot<PgHardDeleteId>
{
    public string Name { get; private set; } = string.Empty;

    public PgHardDeleteAggregate(PgHardDeleteId id, Guid tenantId, string name, IClock clock)
        : base(id, tenantId, clock)
    {
        Name = name;
    }

    private PgHardDeleteAggregate() { } // ORM path

    public void RaiseRemovedEvent() => RaiseDomainEvent(ts => new PgHardDeleteAggregateRemovedEvent { OccurredOn = ts });
}

public sealed class PgHardDeleteAggregateConfig : IEntityTypeConfiguration<PgHardDeleteAggregate>
{
    public void Configure(EntityTypeBuilder<PgHardDeleteAggregate> builder)
    {
        builder.HasKey("Id");
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

public sealed class PgOrderAggregateConfig : IEntityTypeConfiguration<PgOrderAggregate>
{
    public void Configure(EntityTypeBuilder<PgOrderAggregate> builder)
    {
        builder.HasKey("Id");

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Code).HasMaxLength(64).IsRequired();

        // Scoped per tenant — proves the unique-violation classification path without colliding
        // across the tenant-isolation tests' own generated codes.
        builder.HasIndex(nameof(IHasTenantForIndex.TenantId), nameof(PgOrderAggregate.Code)).IsUnique();

        builder.OwnsOne(e => e.Address, a =>
        {
            // Pin the shared-table shadow PK to the SAME property/column the owner's own "Id" maps
            // to — EF Core's default owned-same-table convention instead names it
            // "PgOrderAggregateId" (a DIFFERENT column) when the owner's key is a strongly-typed-id
            // value-converted property, which fails model validation ("both mapped to PK_orders but
            // with different columns"). Explicit here rather than relying on convention.
            a.Property<PgOrderId>("Id");
            a.HasKey("Id");

            a.Property(x => x.Street).HasMaxLength(200).IsRequired();
            a.Property(x => x.City).HasMaxLength(200).IsRequired();
        });

        builder.Money(e => e.Total, amountColumnName: "total_amount", currencyColumnName: "total_currency");
        builder.Money(
            e => e.DiscountTotal,
            required: false,
            amountColumnName: "discount_total_amount",
            currencyColumnName: "discount_total_currency");

        builder.OwnsMany(e => e.Lines, l =>
        {
            l.WithOwner().HasForeignKey("OrderId");
            l.HasKey("OrderId", nameof(PgOrderLine.LineId));
            l.Property(x => x.Description).HasMaxLength(200).IsRequired();
            l.Property(x => x.Quantity).IsRequired();
        });
    }

    // A pure marker so the HasIndex(...) call above doesn't need a raw "TenantId" string literal
    // divorced from the real interface — kept file-local since it exists only to name the constant.
    private static class IHasTenantForIndex
    {
        public const string TenantId = nameof(SharedKernel.Domain.Abstractions.IHasTenant.TenantId);
    }
}

public sealed class PgOrderTagConfig : IEntityTypeConfiguration<PgOrderTag>
{
    public void Configure(EntityTypeBuilder<PgOrderTag> builder)
    {
        builder.HasKey(nameof(Entity<PgOrderTagId>.Id));
        builder.Property(e => e.Label).HasMaxLength(200).IsRequired();

        // REQUIRED, RESTRICT (no cascade) — deliberately so an insert referencing a non-existent
        // PgOrderAggregate id fails with a real PostgreSQL foreign-key violation (SQLSTATE 23503)
        // rather than being silently accepted or cascaded around.
        builder.HasOne<PgOrderAggregate>()
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Postgres-backed multi-tenant test DbContext shared by every test class in this folder.</summary>
public sealed class PgTestDbContext : TenantedDbContext
{
    public DbSet<PgOrderAggregate> Orders => Set<PgOrderAggregate>();
    public DbSet<PgOrderTag> Tags => Set<PgOrderTag>();
    public DbSet<PgHardDeleteAggregate> HardDeleteAggregates => Set<PgHardDeleteAggregate>();

    public PgTestDbContext(
        DbContextOptions<PgTestDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Deliberately NOT calling base.OnModelCreating (the assembly-scan path) — this test
        // assembly has many unrelated IEntityTypeConfiguration<T> types; apply only this suite's own.
        modelBuilder.ApplyConfiguration(new PgOrderAggregateConfig());
        modelBuilder.ApplyConfiguration(new PgOrderTagConfig());
        modelBuilder.ApplyConfiguration(new PgHardDeleteAggregateConfig());
        ApplyTenantFilters(modelBuilder);
    }
}
