using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.StronglyTypedIds;

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
}
