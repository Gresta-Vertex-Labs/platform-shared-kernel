using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;

public sealed record EncCustomerId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static EncCustomerId New() => new(Guid.NewGuid());
}

/// <summary>
/// Owned entity type (same-table <c>OwnsOne</c>), deliberately WITHOUT an encrypted property of its own — see
/// <c>EncryptionOwnedTypeShadowKeyGuardTests</c> for why a same-table owned type's shadow primary key rules out
/// encrypting one of its OWN properties (it can still coexist on an entity that also has encrypted properties,
/// which this type proves).
/// </summary>
public sealed class EncShippingAddress
{
    public string Line1 { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public EncShippingAddress(string line1, string city)
    {
        Line1 = line1;
        City = city;
    }

    private EncShippingAddress() { } // ORM path

    public void ChangeLine1(string line1) => Line1 = line1;
}

/// <summary>EF Core 10 complex-type (value-object) property, with an encrypted sub-property — no separate table, no primary key of its own; its columns live on the owning entity's own table.</summary>
public sealed class EncBillingAddress
{
    public string Line1 { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public EncBillingAddress(string line1, string city)
    {
        Line1 = line1;
        City = city;
    }

    private EncBillingAddress() { } // ORM path
}

/// <summary>
/// Test aggregate: tenant + audit + soft-delete + xmin concurrency, with three encrypted scalar properties
/// exercising the plain, blind-indexed, and per-tenant-key shapes, plus one nullable encrypted property, a
/// complex-type (EF Core 10 value-object) encrypted property, and a same-table owned entity type WITHOUT an
/// encrypted property (its shadow primary key rules that out — see <see cref="EncShippingAddress"/>'s remarks).
/// </summary>
public sealed class EncCustomer : TenantedFullAuditableAggregateRoot<EncCustomerId>
{
    public string Email { get; private set; } = string.Empty;

    public string Ssn { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    public EncShippingAddress ShippingAddress { get; private set; } = null!;

    public EncBillingAddress BillingAddress { get; private set; } = null!;

    public EncCustomer(
        EncCustomerId id,
        Guid tenantId,
        IClock clock,
        string email,
        string ssn,
        string? note = null,
        EncShippingAddress? shippingAddress = null,
        EncBillingAddress? billingAddress = null)
            : base(id, tenantId, clock)
    {
        Email = email;
        Ssn = ssn;
        Note = note;
        ShippingAddress = shippingAddress ?? new EncShippingAddress("1 Ship St", "Springfield");
        BillingAddress = billingAddress ?? new EncBillingAddress("2 Bill Ave", "Shelbyville");
    }

    private EncCustomer() { } // ORM path

    protected override void OnDelete() { }

    public void ChangeEmail(string email) => Email = email;

    public void ChangeNote(string? note) => Note = note;

    public void ChangeShippingAddressLine1(string line1) => ShippingAddress.ChangeLine1(line1);
}

public sealed class EncCustomerConfig : EntityTypeConfigurationBase<EncCustomer, EncCustomerId>
{
    public override void Configure(EntityTypeBuilder<EncCustomer> builder)
    {
        base.Configure(builder);

        builder.Property(x => x.Email)
            .HasMaxLength(1024)
                .IsRequired()
                    .Encrypt("customer.email")
                        .WithBlindIndex(static s => s.Trim().ToLowerInvariant());

        builder.Property(x => x.Ssn)
            .HasMaxLength(1024)
                .IsRequired()
                    .Encrypt("customer.ssn", perTenantKey: true);

        builder.Property(x => x.Note)
            .HasMaxLength(2048)
                .Encrypt("customer.note");

        builder.OwnsOne(x => x.ShippingAddress, a =>
        {
            // EF Core 10's owned-same-table PK-sharing convention names the dependent's shadow FK/PK after the
            // principal CLR type rather than reusing the principal's own key property name when that key is
            // strongly-typed-id-converted — pin it explicitly so the owned type's key is genuinely EncCustomerId.
            a.Property<EncCustomerId>("Id");
            a.HasKey("Id");
            // Explicit column names: ShippingAddress and BillingAddress are both same-table (OwnsOne/ComplexProperty
            // flatten onto EncCustomer's own row) and share property names (Line1/City) — without a distinct
            // column name each pair collides on one physical column and EF throws at SaveChanges.
            a.Property(addr => addr.Line1).HasColumnName("shipping_line1").HasMaxLength(256).IsRequired();
            a.Property(addr => addr.City).HasColumnName("shipping_city").HasMaxLength(128).IsRequired();
        });

        builder.ComplexProperty(x => x.BillingAddress, a =>
        {
            a.Property(addr => addr.Line1).HasColumnName("billing_line1").HasMaxLength(256).IsRequired().Encrypt("customer.billing_address.line1");
            a.Property(addr => addr.City).HasColumnName("billing_city").HasMaxLength(128).IsRequired();
        });
    }
}
