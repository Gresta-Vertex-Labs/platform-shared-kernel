using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Domain.Monetary;

namespace SharedKernel.Persistence.EfCore.Conversions;

/// <summary>
/// Extension methods on <see cref="ModelConfigurationBuilder"/> for auto-registering
/// strongly-typed ID value converters.
/// </summary>
public static class ModelConfigurationBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="StronglyTypedIdValueConverter{TStronglyTypedId,TValue}"/> for the
    /// specified <typeparamref name="TStronglyTypedId"/> type so that EF Core automatically
    /// maps this ID type in all entity configurations without per-aggregate manual registration.
    /// </summary>
    /// <typeparam name="TStronglyTypedId">
    /// The strongly-typed ID record extending <see cref="StronglyTypedId{TValue}"/>.
    /// </typeparam>
    /// <typeparam name="TValue">The underlying primitive value type.</typeparam>
    /// <param name="configurationBuilder">
    /// The <see cref="ModelConfigurationBuilder"/> from <c>ConfigureConventions</c>.
    /// </param>
    /// <returns>The same builder for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    /// {
    /// configurationBuilder.ConfigureStronglyTypedId&lt;OrderId, Guid&gt;();
    /// configurationBuilder.ConfigureStronglyTypedId&lt;CustomerId, Guid&gt;();
    /// }
    /// </code>
    /// </example>
    public static ModelConfigurationBuilder ConfigureStronglyTypedId<TStronglyTypedId, TValue>(
        this ModelConfigurationBuilder configurationBuilder)
        where TStronglyTypedId : StronglyTypedId<TValue>
        where TValue : notnull
    {
        configurationBuilder
            .Properties<TStronglyTypedId>()
            .HaveConversion<StronglyTypedIdValueConverter<TStronglyTypedId, TValue>>();

        return configurationBuilder;
    }

    /// <summary>
    /// Pre-declares <see cref="Money"/> as an EF Core 10 complex type, and registers
    /// <see cref="CurrencyValueConverter"/> globally for any standalone (not <see cref="Money"/>-nested)
    /// <see cref="Currency"/> property, so both map correctly without per-property registration.
    /// </summary>
    /// <param name="configurationBuilder">
    /// The <see cref="ModelConfigurationBuilder"/> from <c>ConfigureConventions</c>.
    /// </param>
    /// <returns>The same builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// This call is REQUIRED — not merely a convenience — for any <c>DbContext</c> that maps
    /// a <see cref="Money"/>-typed property via <see cref="MoneyEntityTypeBuilderExtensions.Money{TEntity}"/>.
    /// </para>
    /// <para>
    /// <strong>Why this is mandatory:</strong> EF Core's automatic navigation/entity-type discovery
    /// walks every DbSet-reachable entity type's CLR properties as soon as the context's model starts
    /// building — BEFORE <c>OnModelCreating</c>'s body (and therefore before any per-property
    /// <c>.ComplexProperty(...)</c> call inside an
    /// <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}"/>) ever runs. A
    /// <see cref="Money"/>-typed property EF has not yet been told is complex is, at that point,
    /// indistinguishable from a genuine navigation, and model finalization fails with "No suitable
    /// constructor was found for the type 'Money'" (empirically confirmed against EF Core 10.0.5 while
    /// building this convention). <c>configurationBuilder.ComplexProperties&lt;Money&gt;()</c> — which
    /// runs before any entity-type/navigation discovery — settles the question early enough that
    /// <see cref="Money"/> is never considered a navigation candidate in the first place, exactly
    /// mirroring why <see cref="ConfigureStronglyTypedId{TStronglyTypedId,TValue}"/> (also a
    /// <c>ConfigureConventions</c>-time, not <c>OnModelCreating</c>-time, registration) never exhibits
    /// this problem. <see cref="Money.Currency"/> needs no equivalent early registration of its own:
    /// once <see cref="Money"/> itself is settled as complex, EF never walks into its members during
    /// the early phase at all, and <see cref="MoneyEntityTypeBuilderExtensions.Money{TEntity}"/> maps
    /// <see cref="Money.Currency"/> to a scalar column itself, per property, via
    /// <see cref="CurrencyValueConverter"/>.
    /// </para>
    /// <para>
    /// <strong>The global <see cref="Currency"/> conversion is a separate, independent concern:</strong>
    /// it exists only for a service that also has a <see cref="Currency"/> property that is
    /// <em>not</em> nested inside a <see cref="Money"/> value (e.g. a "preferred currency" field).
    /// <see cref="Currency"/> derives from <see cref="SharedKernel.Domain.ValueObjects.SingleValueObject{TValue}"/>,
    /// so <see cref="Conventions.ValueObjectOwnershipBuilder"/> never auto-configures it either — without
    /// this registration, a standalone <see cref="Currency"/> property would need its own explicit
    /// <c>.HasConversion&lt;CurrencyValueConverter&gt;()</c> call.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    /// {
    /// configurationBuilder.ConfigureMoney();
    /// base.ConfigureConventions(configurationBuilder);
    /// }
    /// </code>
    /// </example>
    public static ModelConfigurationBuilder ConfigureMoney(
        this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ComplexProperties<Money>();

        configurationBuilder
            .Properties<Currency>()
            .HaveConversion<CurrencyValueConverter>();

        return configurationBuilder;
    }
}
