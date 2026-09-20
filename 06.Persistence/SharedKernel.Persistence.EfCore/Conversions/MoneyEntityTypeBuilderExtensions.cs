using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Monetary;

namespace SharedKernel.Persistence.EfCore.Conversions;

/// <summary>
/// Extension methods on <see cref="EntityTypeBuilder{TEntity}"/> for configuring a
/// <see cref="Money"/>-typed property as an EF Core 10 complex type with two independently
/// queryable columns.
/// </summary>
public static class MoneyEntityTypeBuilderExtensions
{
    /// <summary>The default column precision: 19 total digits.</summary>
    public const int DefaultPrecision = 19;

    /// <summary>The default column scale: 4 digits after the decimal point.</summary>
    public const int DefaultScale = 4;

    /// <summary>
    /// Configures a <see cref="Money"/>-typed property identified by <paramref name="propertyExpression"/>
    /// as a two-column complex type: <c>{property}_amount numeric(precision,scale)</c> and
    /// <c>{property}_currency char(3)</c> (exact column names follow whatever naming convention — e.g.
    /// snake_case — the consuming <c>DbContext</c> applies; these are the EF Core default shadow names
    /// before any such convention runs).
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <param name="builder">The <see cref="EntityTypeBuilder{TEntity}"/> being configured.</param>
    /// <param name="propertyExpression">
    /// A property-access expression selecting the <see cref="Money"/> property. Works identically whether
    /// the property is declared <c>Money</c> or the nullable-reference-annotated <c>Money?</c> — C#
    /// erases that annotation, so both compile to the same expression tree; pass <c>required: false</c>
    /// explicitly for the latter.
    /// </param>
    /// <param name="required">
    /// Whether the property must always hold a value. Pass <see langword="false"/> for an optional
    /// (<c>Money?</c>) property — EF Core 10 supports an optional complex property; a missing value
    /// stores every column of it as <see langword="null"/> and reads back as a <see langword="null"/>
    /// <see cref="Money"/> reference, never a half-populated instance.
    /// </param>
    /// <param name="precision">
    /// The amount column's total digit count. Defaults to <see cref="DefaultPrecision"/> (19).
    /// </param>
    /// <param name="scale">
    /// The amount column's digits after the decimal point. Defaults to <see cref="DefaultScale"/> (4) —
    /// enough headroom for every ISO 4217 minor unit in <c>CurrencyCatalog</c> (at most 3 digits) plus a
    /// safety margin; pass a higher <paramref name="scale"/> for a service that needs more.
    /// </param>
    /// <param name="amountColumnName">Optional override for the amount column's name.</param>
    /// <param name="currencyColumnName">Optional override for the currency code column's name.</param>
    /// <returns>The same <paramref name="builder"/>, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Two independently queryable columns, replacing the single packed-string column the
    /// platform shipped before its first publish while <c>Money</c> had no
    /// EF-bindable constructor. <c>Money</c> now has a private, persistence-only two-parameter
    /// constructor EF Core's complex-type materialization binds directly
    /// (<c>Amount</c>/<c>Currency</c> by name) — see <c>Money</c>'s remarks in <c>03.Domain</c>. A
    /// stored amount with more decimal places than its currency's minor unit allows fails loudly on
    /// read instead of being silently re-rounded.
    /// </para>
    /// <para>
    /// <strong>REQUIRES <see cref="ModelConfigurationBuilderExtensions.ConfigureMoney"/> to have been
    /// called from the owning <c>DbContext</c>'s <c>ConfigureConventions</c> override</strong> — it
    /// pre-declares <see cref="Money"/> as a complex type before EF Core's early navigation/entity-type
    /// discovery walks it (which runs before <c>OnModelCreating</c>'s body, and therefore before this
    /// method ever runs). Calling <c>.Money(...)</c> without first calling <c>ConfigureMoney()</c>
    /// causes model building to fail the same way it always has for any undeclared complex/owned type.
    /// </para>
    /// <para>
    /// A <see cref="Money"/> property MUST always be configured explicitly through this method — it is
    /// deliberately excluded from <see cref="Conventions.ValueObjectOwnershipBuilder"/>'s generic
    /// <see cref="SharedKernel.Domain.Abstractions.IValueObject"/> scan, which has no basis to guess a
    /// column's precision/scale.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Money(e =&gt; e.Price);
    /// builder.Money(e =&gt; e.Discount, required: false, precision: 19, scale: 2, amountColumnName: "discount_amount");
    /// </code>
    /// </example>
    public static EntityTypeBuilder<TEntity> Money<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Money?>> propertyExpression,
        bool required = true,
        int precision = DefaultPrecision,
        int scale = DefaultScale,
        string? amountColumnName = null,
        string? currencyColumnName = null)
        where TEntity : class
    {
        builder.ComplexProperty(propertyExpression, cp =>
        {
            cp.IsRequired(required);

            var amount = cp.Property(m => m.Amount).HasPrecision(precision, scale);
            if (amountColumnName is not null)
                amount.HasColumnName(amountColumnName);

            var currency = cp.Property(m => m.Currency)
                .HasConversion<CurrencyValueConverter>()
                .HasMaxLength(3)
                .IsFixedLength();
            if (currencyColumnName is not null)
                currency.HasColumnName(currencyColumnName);
        });

        return builder;
    }
}
