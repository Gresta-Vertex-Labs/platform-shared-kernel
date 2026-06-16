namespace SharedKernel.Communication.Internal.Resolvers;

/// <summary>
/// Dev/test-only <see cref="IServiceEndpointResolver"/> backed by a pre-configured
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> of service-name-to-<see cref="Uri"/> mappings.
/// Returns the K8s convention URI for unknown service names — never throws.
/// Registered as singleton via <c>AddStaticServiceDiscovery</c>, which logs
/// <c>LogLevel.Warning</c> at startup and throws <see cref="InvalidOperationException"/>
/// if <see cref="IServiceEndpointResolver"/> is already registered.
/// </summary>
internal sealed class StaticServiceEndpointResolver(
    IReadOnlyDictionary<string, Uri> endpoints) : IServiceEndpointResolver
{
    private readonly IReadOnlyDictionary<string, Uri> _endpoints = endpoints;
    private const string DefaultNamespace = "default";
    private const string DefaultClusterDomain = "cluster.local";

    /// <inheritdoc />
    public ValueTask<Uri> ResolveAsync(string serviceName, CancellationToken ct)
    {
        // TODO: implement
        throw new NotImplementedException();
    }

    private static Uri BuildFallbackUri(string serviceName) =>
        new($"http://{serviceName}.{DefaultNamespace}.svc.{DefaultClusterDomain}");
}
