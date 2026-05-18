using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// The logical name of the owning service. Used as the first segment of every cache key
    /// produced by <c>ICacheKeyProvider</c> in the format <c>{service}:{entity}:{id}</c>.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"app"</c>. Must be explicitly set to a meaningful service name in production
    /// to avoid key collisions between services sharing a Redis backplane.
    /// Validation fails if this is null or whitespace.
    /// </remarks>
    public string ServiceName { get; set; } = "app";
}

/// <summary>
/// Validates <see cref="CachingOptions"/> beyond what data annotations can express.
/// Registered automatically by <c>AddSharedKernelCaching</c>.
/// </summary>
internal sealed class CachingOptionsValidator : IValidateOptions<CachingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CachingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceName))
            return ValidateOptionsResult.Fail("CachingOptions.ServiceName must not be null or whitespace.");

        return ValidateOptionsResult.Success;
    }
}
