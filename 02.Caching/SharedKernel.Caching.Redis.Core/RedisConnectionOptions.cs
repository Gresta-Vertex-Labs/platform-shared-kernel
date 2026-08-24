using System.ComponentModel.DataAnnotations;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SharedKernel.Caching.Redis.Core;

/// <summary>
/// Canonical configuration for a StackExchange.Redis <c>IConnectionMultiplexer</c> registered via
/// <see cref="Extensions.RedisConnectionCoreExtensions.AddRedisConnection"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the single, shared shape for Redis connection configuration across
/// <c>SharedKernel.Caching.Redis</c>, <c>SharedKernel.Caching.Redis.DistributedLocking</c>,
/// <c>SharedKernel.Caching.Redis.HashStore</c>, and <c>SharedKernel.Caching.Redis.PubSub</c>.
/// Consuming packages must bind to this type rather than declaring local copies.
/// </para>
/// <para>
/// This type is registered via <c>services.AddOptions&lt;RedisConnectionOptions&gt;().Configure(...)
/// .ValidateDataAnnotations().ValidateOnStart()</c> — the <see cref="RequiredAttribute"/> and
/// <see cref="RangeAttribute"/> below are genuinely enforced at host-startup time (Phase 45,
/// WO-065/P-436), not merely decorative.
/// </para>
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

    /// <summary>
    /// Whether to negotiate TLS for the connection to Redis. Defaults to <see langword="false"/> —
    /// today's exact plaintext behavior — for full backward compatibility with a bare
    /// connection-string caller.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/> and the configured endpoint is not loopback, a one-time
    /// <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/> is logged (never a thrown
    /// exception — sidecar/mesh-terminated TLS, e.g. a service-mesh mTLS proxy in front of Redis, is
    /// a legitimate production topology this domain must not falsely flag as broken). See
    /// "Fail-fast validation and TLS/mTLS surface" in <c>02.Caching/CLAUDE.md</c>.
    /// </remarks>
    public bool Ssl { get; set; }

    /// <summary>
    /// Client certificates to present during TLS negotiation with Redis, for mutual-TLS (mTLS)
    /// deployments where the Redis server requires client authentication.
    /// <see langword="null"/> by default — no client certificate is presented.
    /// </summary>
    public X509Certificate2Collection? ClientCertificates { get; set; }

    /// <summary>
    /// Optional custom server-certificate validation callback, invoked during TLS negotiation with
    /// Redis. <see langword="null"/> by default — StackExchange.Redis's own default certificate
    /// validation applies. Mirrors the synchronous
    /// <c>Func&lt;X509Certificate2, X509Chain?, SslPolicyErrors, bool&gt;</c> shape ASP.NET Core
    /// Kestrel's own <c>ClientCertificateValidation</c> callback uses, for consistency across the
    /// platform's TLS-configuration surfaces.
    /// </summary>
    public Func<X509Certificate2, X509Chain?, SslPolicyErrors, bool>? CertificateValidation { get; set; }
}
