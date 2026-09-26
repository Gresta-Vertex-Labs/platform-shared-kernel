using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Idempotency.Abstractions;

/// <summary>Registers and resolves <see cref="IIdempotencyStore"/> implementations keyed by <see cref="IdempotencyPurpose"/>.</summary>
public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TStore"/> as the <see cref="IIdempotencyStore"/> for
    /// <paramref name="purpose"/>, keyed by the purpose.
    /// </summary>
    /// <typeparam name="TStore">The store implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="purpose">The purpose the store serves.</param>
    /// <param name="lifetime">
    /// The store's lifetime: scoped by default, as the Redis and EF Core stores are; singleton for a store that
    /// keeps its own state in memory.
    /// </param>
    /// <returns>The same <paramref name="services"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="purpose"/> is not a defined value.</exception>
    /// <exception cref="InvalidOperationException">A store is already registered for <paramref name="purpose"/>.</exception>
    /// <remarks>
    /// One store per purpose: a second registration for the same purpose is a configuration error, not an override,
    /// because the first would silently stop guarding anything.
    /// </remarks>
    public static IServiceCollection AddIdempotencyStore<TStore>(
        this IServiceCollection services,
        IdempotencyPurpose purpose,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TStore : class, IIdempotencyStore
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown idempotency purpose.");

        if (services.HasIdempotencyStore(purpose))
        {
            throw new InvalidOperationException(
                $"An {nameof(IIdempotencyStore)} is already registered for {nameof(IdempotencyPurpose)}.{purpose}. " +
                "Register one store per purpose.");
        }

        services.Add(new ServiceDescriptor(typeof(IIdempotencyStore), purpose, typeof(TStore), lifetime));
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TStore"/> for every purpose <paramref name="purposes"/> selects.
    /// </summary>
    /// <typeparam name="TStore">The store implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="purposes">Selects the purposes, for example <c>p =&gt; p.ForRequests().ForMessages()</c>.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No purpose was selected, or a store is already registered for a selected purpose.
    /// </exception>
    public static IServiceCollection AddIdempotencyStore<TStore>(
        this IServiceCollection services,
        Action<IdempotencyPurposeSelection> purposes)
        where TStore : class, IIdempotencyStore
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var purpose in SelectPurposes(purposes))
            services.AddIdempotencyStore<TStore>(purpose);

        return services;
    }

    /// <summary>Whether an <see cref="IIdempotencyStore"/> is registered for <paramref name="purpose"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="purpose">The purpose to check.</param>
    /// <returns><see langword="true"/> if a keyed store is registered for the purpose.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static bool HasIdempotencyStore(this IServiceCollection services, IdempotencyPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Any(d =>
            d.ServiceType == typeof(IIdempotencyStore)
            && d.IsKeyedService
            && d.ServiceKey is IdempotencyPurpose key
            && key == purpose);
    }

    /// <summary>Resolves the <see cref="IIdempotencyStore"/> registered for <paramref name="purpose"/>.</summary>
    /// <param name="services">The service provider (normally a scope's).</param>
    /// <param name="purpose">The purpose.</param>
    /// <returns>The store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No store is registered for <paramref name="purpose"/>.</exception>
    public static IIdempotencyStore GetRequiredIdempotencyStore(this IServiceProvider services, IdempotencyPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredKeyedService<IIdempotencyStore>(purpose);
    }

    /// <summary>Evaluates a purpose selection, requiring at least one purpose.</summary>
    /// <param name="purposes">The selection delegate.</param>
    /// <returns>The selected purposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="purposes"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No purpose was selected.</exception>
    public static IReadOnlyList<IdempotencyPurpose> SelectPurposes(Action<IdempotencyPurposeSelection> purposes)
    {
        ArgumentNullException.ThrowIfNull(purposes);
        var selection = new IdempotencyPurposeSelection();
        purposes(selection);

        if (selection.Purposes.Count == 0)
        {
            throw new InvalidOperationException(
                "Select at least one idempotency purpose, for example p => p.ForRequests().ForMessages().");
        }

        return selection.Purposes;
    }
}
