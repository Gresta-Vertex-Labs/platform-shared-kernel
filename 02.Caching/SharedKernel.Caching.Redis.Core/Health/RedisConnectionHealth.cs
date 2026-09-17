namespace SharedKernel.Caching.Redis.Core.Health;

/// <summary>The result of <see cref="IRedisConnectionProbe.ProbeAsync"/>.</summary>
/// <param name="IsHealthy">Whether Redis answered the <c>PING</c>.</param>
/// <param name="Latency">The round-trip time of the <c>PING</c>, or <see langword="null"/> when it failed.</param>
/// <param name="Description">
/// Why the check failed, or <see langword="null"/> when healthy. Health endpoints may expose it, so it never
/// contains the connection string or an exception message.
/// </param>
public sealed record RedisConnectionHealth(bool IsHealthy, TimeSpan? Latency, string? Description);
