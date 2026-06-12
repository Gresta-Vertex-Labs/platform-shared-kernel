using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Caching.Redis.Core;

/// <summary>
/// Canonical configuration for a StackExchange.Redis <c>IConnectionMultiplexer</c> registered via
/// <see cref="Extensions.RedisConnectionCoreExtensions.AddRedisConnection"/>.
/// </summary>
/// <remarks>
/// This is the single, shared shape for Redis connection configuration across
/// <c>SharedKernel.Caching.Redis</c>, <c>SharedKernel.Caching.Redis.DistributedLocking</c>,
/// <c>SharedKernel.Caching.Redis.HashStore</c>, and <c>SharedKernel.Caching.Redis.PubSub</c>.
/// Consuming packages must bind to this type rather than declaring local copies.
/// </remarks>
public sealed class RedisConnectionOptions
{
    /// <summary>
    /// StackExchange.Redis connection string (e.g., <c>"localhost:6379"</c>).
    /// Required; must not be null or whitespace.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Connection timeout in milliseconds for the Redis multiplexer.
    /// Defaults to <c>5000</c> ms.
    /// </summary>
    [Range(100, 60_000, ErrorMessage = "ConnectTimeoutMs must be between 100 ms and 60 000 ms.")]
    public int ConnectTimeoutMs { get; set; } = 5_000;
}
