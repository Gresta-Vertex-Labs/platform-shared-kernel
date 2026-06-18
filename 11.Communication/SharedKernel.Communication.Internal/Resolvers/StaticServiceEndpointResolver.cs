namespace SharedKernel.Communication.Internal.Resolvers;

/// <summary>
/// Dev/test-only <see cref="IServiceEndpointResolver"/> backed by a pre-configured
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> of service-name-to-<see cref="Uri"/> mappings.
/// Returns the K8s convention URI for unknown service names — never throws.
/// </summary>
/// <remarks>
/// Registered as singleton via <c>AddStaticServiceDiscovery</c>, which logs
/// <c>LogLevel.Warning</c> at startup and throws <see cref="InvalidOperationException"/>
/// if <see cref="IServiceEndpointResolver"/> is already registered.
/// </remarks>
internal sealed class StaticServiceEndpointResolver(
    IReadOnlyDictionary<string, Uri> endpoints) : IServiceEndpointResolver
{
    private const string DefaultNamespace = "default";
    private const string DefaultClusterDomain = "cluster.local";

    /// <inheritdoc />
    public ValueTask<Uri> ResolveAsync(string serviceName, CancellationToken ct)
    {
        if (endpoints.TryGetValue(serviceName, out var uri))
            return ValueTask.FromResult(uri);

        return ValueTask.FromResult(BuildFallbackUri(serviceName));
    }

    /// <summary>Builds the K8s DNS-convention URI for an unmapped service name. Never throws.</summary>
    private static Uri BuildFallbackUri(string serviceName) =>
        new($"http://{serviceName}.{DefaultNamespace}.svc.{DefaultClusterDomain}");
}
