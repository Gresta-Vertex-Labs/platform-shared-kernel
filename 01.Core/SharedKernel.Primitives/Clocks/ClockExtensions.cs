using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharedKernel.Primitives.Clocks;

/// <summary>
/// DI extension methods for registering the <see cref="IClock"/> abstraction.
/// </summary>
public static class ClockExtensions
{
    /// <summary>
    /// Registers <see cref="SystemClock"/> as the singleton implementation of <see cref="IClock"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// Use this method in all production hosts. In tests, register a fake clock that returns a
    /// fixed <see cref="DateTimeOffset"/> to keep time deterministic. Uses <c>TryAddSingleton</c>
    /// — calling this method more than once never double-registers, and a consumer-supplied
    /// <see cref="IClock"/> registration made <b>before</b> this call always wins over the
    /// platform default.
    /// </remarks>
    public static IServiceCollection AddClock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IClock, SystemClock>();
        return services;
    }
}
