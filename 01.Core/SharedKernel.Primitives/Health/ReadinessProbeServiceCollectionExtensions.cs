using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharedKernel.Primitives.Health;

/// <summary>Registration and lookup of <see cref="IReadinessProbe"/> implementations.</summary>
/// <remarks>
/// Provider packages call these from their own registration methods, so a probe exists exactly when its
/// provider is registered. Every probe is a singleton: a host resolves them once, when it maps them to health
/// checks.
/// </remarks>
public static class ReadinessProbeServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TProbe"/> as a singleton <see cref="IReadinessProbe"/>. Registering the same
    /// type again is a no-op.
    /// </summary>
    /// <typeparam name="TProbe">The probe type. Use it for a provider with a single target.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddReadinessProbe<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProbe>(
        this IServiceCollection services)
        where TProbe : class, IReadinessProbe
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReadinessProbe, TProbe>());
        return services;
    }

    /// <summary>
    /// Registers one singleton <see cref="IReadinessProbe"/> built by <paramref name="factory"/>. Use it for a
    /// provider with several targets (named stores, indexes, collections), calling it once per target.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="factory">Builds the probe for one target.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Unlike <see cref="AddReadinessProbe{TProbe}(IServiceCollection)"/> this does not de-duplicate: call it
    /// once per target. Two probes with the same <see cref="IReadinessProbe.Name"/> are rejected by the host
    /// when it maps them.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddReadinessProbe(
        this IServiceCollection services,
        Func<IServiceProvider, IReadinessProbe> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        // Deliberately additive (one probe per target), so neither TryAdd form applies: TryAddEnumerable would
        // collapse every target of one probe type into the first, since it de-duplicates by implementation type.
        services.Add(ServiceDescriptor.Singleton(typeof(IReadinessProbe), factory));
        return services;
    }

    /// <summary>Returns the registered probe named <paramref name="name"/>.</summary>
    /// <param name="services">The service provider.</param>
    /// <param name="name">The probe's <see cref="IReadinessProbe.Name"/>.</param>
    /// <returns>The probe.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">No probe, or more than one probe, has that name.</exception>
    public static IReadinessProbe GetRequiredReadinessProbe(this IServiceProvider services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var matches = services.GetServices<IReadinessProbe>()
            .Where(p => string.Equals(p.Name, name, StringComparison.Ordinal))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No readiness probe named '{name}' is registered."),
            _ => throw new InvalidOperationException($"{matches.Count} readiness probes are named '{name}'; probe names must be unique."),
        };
    }
}
