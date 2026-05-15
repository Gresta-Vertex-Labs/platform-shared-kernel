using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// Configuration options for the Redis L2 distributed backplane.
/// Bound to <c>SharedKernelCaching:Redis</c> section in application configuration.
/// </summary>
public sealed class RedisL2Options
{
    /// <summary>
    /// The configuration section name used when binding these options from
    /// <c>IConfiguration</c>.
    /// </summary>
    public const string SectionName = "SharedKernelCaching:Redis";

    /// <summary>
    /// StackExchange.Redis connection string.
    /// Required; must not be null or whitespace.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Optional Redis key prefix applied to all cache entries written to L2.
    /// Useful when multiple services share the same Redis instance.
    /// Defaults to an empty string (no prefix).
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Connection timeout in milliseconds for the Redis multiplexer.
    /// Defaults to <c>5000</c> ms.
    /// </summary>
    [Range(100, 60_000, ErrorMessage = "ConnectTimeout must be between 100 ms and 60 000 ms.")]
    public int ConnectTimeoutMs { get; set; } = 5_000;
}
