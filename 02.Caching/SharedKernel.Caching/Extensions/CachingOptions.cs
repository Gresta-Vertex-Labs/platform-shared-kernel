using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Caching.Extensions;

/// <summary>
/// Configuration options for the SharedKernel caching infrastructure.
/// Bound to <c>SharedKernelCaching</c> section in application configuration.
/// </summary>
public sealed class CachingOptions
{
    /// <summary>
    /// The configuration section name used when binding these options from
    /// <c>IConfiguration</c>.
    /// </summary>
    public const string SectionName = "SharedKernelCaching";

    /// <summary>
    /// Maximum number of items the L1 in-process memory cache may hold.
    /// Defaults to <c>10 000</c>. Must be a positive integer.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "L1SizeLimit must be at least 1.")]
    public int L1SizeLimit { get; set; } = 10_000;

    /// <summary>
    /// Name of the FusionCache instance. Used to differentiate multiple cache instances in the
    /// same DI container. Defaults to <c>"default"</c>.
    /// </summary>
    [Required]
    public string CacheName { get; set; } = "default";
}
