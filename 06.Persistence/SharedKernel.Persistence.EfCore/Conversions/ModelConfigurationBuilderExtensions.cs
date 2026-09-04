using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Domain.ValueObjects.Money;

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
    ///     configurationBuilder.ConfigureStronglyTypedId&lt;OrderId, Guid&gt;();
    ///     configurationBuilder.ConfigureStronglyTypedId&lt;CustomerId, Guid&gt;();
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
    /// Registers <see cref="CurrencyValueConverter"/> and <see cref="MoneyValueConverter"/> globally
    /// so that every <see cref="Currency"/>- and <see cref="Money"/>-typed property in the model is
    /// automatically mapped as a scalar column, without per-property configuration.
    /// </summary>
    /// <param name="configurationBuilder">
    /// The <see cref="ModelConfigurationBuilder"/> from <c>ConfigureConventions</c>.
    /// </param>
    /// <returns>The same builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// WO-066/P-440/D-106. This call is REQUIRED — not merely a convenience — for any
    /// <c>DbContext</c> that maps a <see cref="Money"/>- or <see cref="Currency"/>-typed property,
    /// including via <see cref="MoneyEntityTypeBuilderExtensions.OwnsMoney{TEntity}"/>.
    /// </para>
    /// <para>
    /// <strong>Why this is mandatory (not just per-property <c>.HasConversion(...)</c>):</strong>
    /// EF Core's automatic navigation/entity-type discovery walks every DbSet-reachable entity
    /// type's CLR properties as soon as the context's model starts building — BEFORE
    /// <c>OnModelCreating</c>'s body (and therefore before any per-property
    /// <c>.Property(...).HasConversion(...)</c> call inside an
    /// <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}"/>) ever runs. A
    /// <see cref="Money"/>-typed property with no GLOBALLY-registered conversion yet is, at that
    /// point, indistinguishable from a genuine navigation — EF auto-discovers <c>Money</c> as an
    /// owned/related entity type and recurses into ITS properties, discovering
    /// <see cref="Money.Currency"/> as a second, nested entity type. A later per-property
    /// <c>.HasConversion(...)</c> call successfully converts the OUTER property back to a scalar,
    /// but the transitively-discovered, now-orphaned <see cref="Currency"/> entity type can be left
    /// behind in the model, causing model finalization to fail with "No suitable constructor was
    /// found for the type 'Currency'" (empirically confirmed against EF Core 10 while building this
    /// converter). Calling <see cref="ConfigureMoney"/> from <c>ConfigureConventions</c> — which
    /// runs before any entity-type/navigation discovery — registers both conversions early enough
    /// that <see cref="Money"/>/<see cref="Currency"/> are never considered navigation candidates in
    /// the first place, exactly mirroring why <see cref="ConfigureStronglyTypedId{TStronglyTypedId,TValue}"/>
    /// (also a <c>ConfigureConventions</c>-time, not <c>OnModelCreating</c>-time, registration) never
    /// exhibits this problem.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    /// {
    ///     configurationBuilder.ConfigureMoney();
    ///     base.ConfigureConventions(configurationBuilder);
    /// }
    /// </code>
    /// </example>
    public static ModelConfigurationBuilder ConfigureMoney(
        this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<Currency>()
            .HaveConversion<CurrencyValueConverter>();

        configurationBuilder
            .Properties<Money>()
            .HaveConversion<MoneyValueConverter>();

        return configurationBuilder;
    }
}
