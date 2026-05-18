using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// A builder returned by <c>AddSharedKernelCaching</c> that allows chaining additional
/// caching registrations onto the service collection.
/// </summary>
/// <remarks>
/// Extension methods on this interface (e.g., <c>AddRedisL2</c>, <c>AddRedisChannelService</c>,
/// <c>AddRedisHashService</c>, <c>AddRedisCacheInvalidationBus</c>) are defined in their
/// respective provider packages, keeping infrastructure dependencies isolated from the
/// abstractions package.
/// </remarks>
public interface ICachingBuilder
{
    /// <summary>Gets the underlying service collection.</summary>
    IServiceCollection Services { get; }
}
