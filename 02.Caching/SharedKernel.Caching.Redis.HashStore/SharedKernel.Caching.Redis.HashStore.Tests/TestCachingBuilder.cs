using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Redis.HashStore.Tests;

/// <summary>
/// Minimal <see cref="ICachingBuilder"/> implementation used in tests to drive
/// <c>ICachingBuilder</c> extension methods without requiring a full FusionCache setup.
/// </summary>
internal sealed class TestCachingBuilder : ICachingBuilder
{
    internal TestCachingBuilder(IServiceCollection services) => Services = services;

    /// <inheritdoc />
    public IServiceCollection Services { get; }
}
