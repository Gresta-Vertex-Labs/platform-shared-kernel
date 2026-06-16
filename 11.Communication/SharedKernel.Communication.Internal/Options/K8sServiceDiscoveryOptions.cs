using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Communication.Internal.Options;

/// <summary>
/// Configuration for the <c>KubernetesServiceEndpointResolver</c>.
/// Bound via <c>IOptions&lt;K8sServiceDiscoveryOptions&gt;</c> in <c>AddK8sServiceDiscovery</c>.
/// </summary>
public sealed class K8sServiceDiscoveryOptions
{
    /// <summary>
    /// Kubernetes namespace used to build DNS names. Default: <c>"default"</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Namespace { get; set; } = "default";

    /// <summary>
    /// Cluster DNS domain suffix. Default: <c>"cluster.local"</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ClusterDomain { get; set; } = "cluster.local";

    /// <summary>
    /// Forces a specific URI scheme (<c>"http"</c> or <c>"https"</c>).
    /// Default: <c>null</c> — resolved as <c>"http"</c>.
    /// </summary>
    public string? SchemeOverride { get; set; }
}
