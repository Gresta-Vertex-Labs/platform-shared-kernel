using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Idempotency.Abstractions;

namespace SharedKernel.Testing.Idempotency;

/// <summary>Registers <see cref="FakeIdempotencyStore"/> in place of a real idempotency store.</summary>
public static class FakeIdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers one singleton <see cref="FakeIdempotencyStore"/> as the keyed <see cref="IIdempotencyStore"/> for each of
    /// <paramref name="purposes"/> (both when none is given), replacing any store already registered for them.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="purposes">The purposes to fake; both <see cref="IdempotencyPurpose"/> values when empty.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    /// <remarks>Resolve <see cref="FakeIdempotencyStore"/> itself to assert on <see cref="FakeIdempotencyStore.Calls"/>.</remarks>
    public static IServiceCollection AddFakeIdempotencyStore(
        this IServiceCollection services,
        params IdempotencyPurpose[] purposes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(purposes);

        services.TryAddSingleton<FakeIdempotencyStore>();

        IdempotencyPurpose[] selected = purposes.Length == 0
            ? [IdempotencyPurpose.Request, IdempotencyPurpose.Message]
            : purposes;

        foreach (var purpose in selected.Distinct())
        {
            services.RemoveAllKeyed<IIdempotencyStore>(purpose);
            services.AddKeyedSingleton<IIdempotencyStore>(
                purpose,
                (sp, _) => sp.GetRequiredService<FakeIdempotencyStore>());
        }

        return services;
    }
}
