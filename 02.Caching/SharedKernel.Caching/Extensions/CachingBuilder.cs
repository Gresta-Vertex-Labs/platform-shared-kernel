using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Caching.Extensions;

/// <summary>
/// Default implementation of <see cref="ICachingBuilder"/>.
/// Wraps the <see cref="IServiceCollection"/> and exposes it for chaining.
/// </summary>
internal sealed class CachingBuilder : ICachingBuilder
{
    internal CachingBuilder(IServiceCollection services) => Services = services;

    /// <inheritdoc />
    public IServiceCollection Services { get; }
}
