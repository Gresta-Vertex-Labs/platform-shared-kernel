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
}

/// <summary>
/// Validates <see cref="K8sServiceDiscoveryOptions"/>.
/// Rejects empty <see cref="K8sServiceDiscoveryOptions.Namespace"/> or
/// <see cref="K8sServiceDiscoveryOptions.ClusterDomain"/>.
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

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
