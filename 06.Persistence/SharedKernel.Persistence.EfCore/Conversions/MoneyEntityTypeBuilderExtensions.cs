using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Monetary;

namespace SharedKernel.Persistence.EfCore.Conversions;

/// <summary>
/// Extension methods on <see cref="EntityTypeBuilder{TEntity}"/> for configuring a
/// <see cref="Money"/>-typed property.
/// </summary>
public static class MoneyEntityTypeBuilderExtensions
{
    /// <summary>
    /// Maximum column length for the packed <c>"{amount}:{currencyCode}"</c> representation:
    /// <see cref="decimal.MinValue"/>'s longest possible rendering (31 characters, including the
    /// sign) plus the <c>:</c> separator plus a 3-character ISO 4217 code, rounded up for
    /// headroom.
    /// </summary>
    private const int PackedColumnMaxLength = 40;

    /// <summary>
    /// Configures a <see cref="Money"/>-typed property identified by <paramref name="propertyExpression"/>
    /// as a single packed <see langword="string"/> column.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <param name="builder">The <see cref="EntityTypeBuilder{TEntity}"/> being configured.</param>
    /// <param name="propertyExpression">A property-access expression selecting the <see cref="Money"/> property.</param>
    /// <param name="columnName">
    /// Optional override for the generated column name. Defaults to the property name (subject to
    /// whatever naming convention — e.g. snake_case — the consuming <c>DbContext</c> applies).
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// WO-066/P-440/D-105/D-106/D-107. A <see cref="Money"/> property MUST always be configured
    /// explicitly via this method — it is deliberately excluded from
    /// <see cref="Conventions.ValueObjectOwnershipBuilder"/>'s generic
    /// <see cref="SharedKernel.Domain.Abstractions.IValueObject"/> scan (D-107), which would
    /// otherwise silently auto-own it with no column mapping at all (EF Core cannot bind
    /// <see cref="Money"/>'s private, three-argument constructor automatically — see
    /// <see cref="MoneyValueConverter"/>'s remarks for the full investigation).
    /// </para>
    /// <para>
    /// <strong>Single-column mapping (D-106 fallback, not D-105's original two-column design):</strong>
    /// this configures ONE <see langword="string"/> column via <see cref="MoneyValueConverter"/>,
    /// not two independently queryable <c>Amount</c>/<c>Currency"</c> columns. Consult
    /// <see cref="MoneyValueConverter"/>'s remarks for the full rationale (every EF Core 10 path
    /// to a genuine two-column owned-type mapping requires either reflecting into EF Core's own
    /// internal metadata, or an interceptor-registration mechanism that does not compose with
    /// per-property, <c>OnModelCreating</c>-time configuration). A service that genuinely needs
    /// <c>WHERE Currency = 'USD' AND Amount &gt; ...</c>-style SQL filtering should maintain its
    /// own separate, independently-mapped shadow columns alongside this one.
    /// </para>
    /// <para>
    /// <strong>REQUIRES <see cref="ModelConfigurationBuilderExtensions.ConfigureMoney"/> to have been
    /// called from the owning <c>DbContext</c>'s <c>ConfigureConventions</c> override.</strong> This
    /// method only customises the column (name, max length) of an ALREADY globally-converted
    /// <see cref="Money"/> property — it does not itself register <see cref="MoneyValueConverter"/>.
    /// Calling <c>OwnsMoney(...)</c> without first calling <c>ConfigureMoney()</c> causes model
    /// building to fail: EF Core's automatic navigation discovery walks <see cref="Money"/> (and
    /// transitively <see cref="Currency"/>) as candidate entity types before this,
    /// <c>OnModelCreating</c>-time, call ever runs. See <see cref="ModelConfigurationBuilderExtensions.ConfigureMoney"/>'s
    /// remarks for the full empirical investigation.
    /// </para>
    /// </remarks>
    public static EntityTypeBuilder<TEntity> OwnsMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Money>> propertyExpression,
        string? columnName = null)
        where TEntity : class
    {
        var propertyBuilder = builder
            .Property(propertyExpression)
            .HasMaxLength(PackedColumnMaxLength);

        if (columnName is not null)
        {
            propertyBuilder.HasColumnName(columnName);
        }

        return builder;
    }
}
