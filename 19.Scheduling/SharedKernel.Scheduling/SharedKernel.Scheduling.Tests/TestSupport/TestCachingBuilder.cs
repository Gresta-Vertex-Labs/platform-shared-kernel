using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Scheduling.Tests.TestSupport;

/// <summary>
/// Minimal <see cref="ICachingBuilder"/> so the Redis lock registration extension can be driven
/// without a full cache setup.
/// </summary>
internal sealed class TestCachingBuilder(IServiceCollection services) : ICachingBuilder
{
    /// <inheritdoc />
    public IServiceCollection Services { get; } = services;
}
