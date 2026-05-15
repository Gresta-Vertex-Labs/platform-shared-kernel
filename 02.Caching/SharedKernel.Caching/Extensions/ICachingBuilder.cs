using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Caching.Extensions;

/// <summary>
/// A builder that allows chaining additional caching registrations after
/// <c>AddSharedKernelCaching</c> has been called.
/// </summary>
/// <remarks>
/// This interface is intentionally minimal. The Redis L2 extension method
/// <c>AddRedisL2</c> is defined in <c>SharedKernel.Caching.Redis</c> as an extension
/// on this interface, keeping the Redis dependency isolated to the Redis package.
/// </remarks>
public interface ICachingBuilder
{
    /// <summary>Gets the underlying service collection.</summary>
    IServiceCollection Services { get; }
}
