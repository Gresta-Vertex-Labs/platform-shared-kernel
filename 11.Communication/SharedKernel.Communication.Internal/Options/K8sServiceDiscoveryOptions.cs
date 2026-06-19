using Microsoft.Extensions.Options;

namespace SharedKernel.Communication.Internal.Options;

/// <summary>
/// Configuration for the K8s DNS-based service endpoint resolver.
/// Bound via <see cref="IOptions{K8sServiceDiscoveryOptions}"/> in <c>AddK8sServiceDiscovery</c>.
/// </summary>
public sealed class K8sServiceDiscoveryOptions
{
    /// <summary>
    /// Kubernetes namespace used to build DNS names. Default: <c>"default"</c>.
    /// </summary>
    public string Namespace { get; set; } = "default";

    /// <summary>
    /// Cluster DNS domain suffix. Default: <c>"cluster.local"</c>.
    /// </summary>
    public string ClusterDomain { get; set; } = "cluster.local";

    /// <summary>
    /// Forces a specific URI scheme (<c>"http"</c> or <c>"https"</c>).
    /// Default: <c>null</c> — resolved as <c>"http"</c>.
    /// </summary>
    public string? SchemeOverride { get; set; }

    /// <summary>
    /// TTL in seconds for the in-memory endpoint cache inside <c>KubernetesServiceEndpointResolver</c>.
    /// Default: <c>30</c> seconds. Set to <c>0</c> to disable caching entirely.
    /// Negative values are rejected by <see cref="K8sServiceDiscoveryOptionsValidator"/>.
    /// </summary>
    public int EndpointCacheTtlSeconds { get; set; } = 30;
}

/// <summary>
/// Validates <see cref="K8sServiceDiscoveryOptions"/>.
/// Rejects empty <see cref="K8sServiceDiscoveryOptions.Namespace"/>,
/// empty <see cref="K8sServiceDiscoveryOptions.ClusterDomain"/>, or
/// negative <see cref="K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds"/>.
/// </summary>
internal sealed class K8sServiceDiscoveryOptionsValidator : IValidateOptions<K8sServiceDiscoveryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, K8sServiceDiscoveryOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Namespace))
            failures.Add($"{nameof(K8sServiceDiscoveryOptions.Namespace)} must not be empty.");

        if (string.IsNullOrWhiteSpace(options.ClusterDomain))
            failures.Add($"{nameof(K8sServiceDiscoveryOptions.ClusterDomain)} must not be empty.");

        if (options.EndpointCacheTtlSeconds < 0)
            failures.Add($"{nameof(K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds)} must not be negative. Use 0 to disable caching.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
