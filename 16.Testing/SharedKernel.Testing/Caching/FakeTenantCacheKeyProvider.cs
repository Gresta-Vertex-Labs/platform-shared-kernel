using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// Fake <see cref="ITenantCacheKeyProvider"/> that produces the real <see cref="CacheKeyFormat"/>
/// keys for a fixed service name, so assertions match production keys exactly.
/// </summary>
public sealed class FakeTenantCacheKeyProvider : ITenantCacheKeyProvider
{
    /// <summary>The service name used by the parameterless constructor.</summary>
    public const string DefaultServiceName = "test-svc";

    /// <summary>Creates a provider for <see cref="DefaultServiceName"/>.</summary>
    public FakeTenantCacheKeyProvider()
        : this(DefaultServiceName)
    {
    }

    /// <summary>Creates a provider for <paramref name="serviceName"/>.</summary>
    /// <param name="serviceName">A valid service name; see <see cref="CacheKeyFormat.IsValidServiceName"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="serviceName"/> is invalid.</exception>
    public FakeTenantCacheKeyProvider(string serviceName)
    {
        if (!CacheKeyFormat.IsValidServiceName(serviceName))
            throw new ArgumentException("The service name is not a valid cache key prefix.", nameof(serviceName));

        ServiceName = serviceName;
    }

    /// <summary>Gets the service name prefixed to every key.</summary>
    public string ServiceName { get; }

    /// <inheritdoc />
    public string BuildKey(string entity, string id, params string[] segments) =>
        CacheKeyFormat.BuildKey(ServiceName, entity, id, segments);

    /// <inheritdoc />
    public string BuildTenantKey(string tenantId, string entity, string id, params string[] segments) =>
        CacheKeyFormat.BuildTenantKey(ServiceName, tenantId, entity, id, segments);
}
