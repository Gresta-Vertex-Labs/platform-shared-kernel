using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// Configuration options for the RedLock.net distributed locking service.
/// Bound to <c>SharedKernelCaching:DistributedLock</c> section in application configuration.
/// </summary>
public sealed class RedisLockOptions
{
    /// <summary>
    /// The configuration section name used when binding these options from
    /// <c>IConfiguration</c>.
    /// </summary>
    public const string SectionName = "SharedKernelCaching:DistributedLock";

    /// <summary>
    /// StackExchange.Redis connection string for the locking Redis instance.
    /// Required; must not be null or whitespace.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Connection timeout in milliseconds for the Redis multiplexer used by RedLock.
    /// Defaults to <c>5000</c> ms.
    /// </summary>
    [Range(100, 60_000, ErrorMessage = "ConnectTimeout must be between 100 ms and 60 000 ms.")]
    public int ConnectTimeoutMs { get; set; } = 5_000;
}
