using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Testing.Logging;

/// <summary>
/// Dependency-injection registration extensions for the in-memory structured log capture double.
/// </summary>
public static class LoggingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryLoggerFactory"/> as a singleton <see cref="ILoggerFactory"/>,
    /// plus the real BCL open-generic <see cref="Logger{T}"/> adapter as <see cref="ILogger{TCategoryName}"/>
    /// — the exact mechanism <c>AddLogging()</c> itself uses internally — so any
    /// <c>ILogger&lt;THandler&gt;</c> constructor-injected in the container under test resolves
    /// correctly through the fake with zero additional SharedKernel code.
    /// </summary>
    /// <remarks>
    /// <see cref="InMemoryLoggerFactory"/> is registered as a singleton so captured records survive
    /// past the DI scope for post-action assertions — the same rationale documented for
    /// <c>AddInMemoryMessageBus</c>/<c>AddInMemoryEventPublisher</c>, and an intentional divergence
    /// from any narrower production <see cref="ILoggerFactory"/> lifetime a consuming service might
    /// otherwise expect.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    public static IServiceCollection AddInMemoryLoggerFactory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ILoggerFactory, InMemoryLoggerFactory>();
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));

        return services;
    }
}
