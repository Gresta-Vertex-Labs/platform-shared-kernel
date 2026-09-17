using System.ComponentModel.DataAnnotations;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using SharedKernel.Configuration;

namespace SharedKernel.Caching.Redis.Core;

/// <summary>
/// Settings for the one Redis connection shared by every <c>SharedKernel.Caching.Redis</c> package, bound
/// from the <c>SharedKernel:Caching:Redis</c> configuration section or set in code.
/// </summary>
/// <remarks>
/// <para>
/// The distributed cache, its backplane, distributed locks, the hash store and pub/sub all use the
/// connection built from these settings, so TLS, timeouts and health apply to all of them. No other
/// registration takes a connection string.
/// </para>
/// <para>
/// Every setting is read when the connection is first resolved and validated at host startup.
/// <see cref="ClientCertificates"/> and <see cref="CertificateValidation"/> cannot come from configuration;
/// set them in the <c>configure</c> delegate.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // appsettings.json
/// // "SharedKernel": { "Caching": { "Redis": { "ConnectionString": "redis.internal:6380", "Ssl": true } } }
/// builder.Services.AddRedisConnection(builder.Configuration);
/// </code>
/// </example>
public sealed class RedisConnectionOptions : ISectionBoundOptions
{
    /// <summary>Gets the configuration section path: <c>SharedKernel:Caching:Redis</c>.</summary>
    public static string SectionName => "SharedKernel:Caching:Redis";

    /// <summary>
    /// Gets or sets the StackExchange.Redis connection string, for example
    /// <c>"redis.internal:6379,password=secret"</c>. Required.
    /// </summary>
    /// <remarks>
    /// Keep the password out of source control: bind it from a secret store. The timeouts on this type
    /// override those in the string; TLS is on when either <see cref="Ssl"/> or the string enables it.
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ConnectionString is required.")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets how long a connection attempt may take. Defaults to 5 seconds.</summary>
    /// <remarks>Between 100 milliseconds and 1 minute.</remarks>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets how long a command may wait for its reply. Defaults to 5 seconds.</summary>
    /// <remarks>Between 100 milliseconds and 1 minute. Applies to synchronous and asynchronous commands.</remarks>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets a value indicating whether a command fails at once while no connection is available,
    /// instead of waiting up to <see cref="CommandTimeout"/> for a reconnect. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Failing fast lets the cache serve fail-safe values, and callers see an outage immediately, during a
    /// Redis failover or network loss. Set it to <see langword="false"/> to ride out short reconnects instead.
    /// </remarks>
    public bool FailFastWhenDisconnected { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the connection uses TLS. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// When it is <see langword="false"/> and an endpoint is not a loopback address, a warning is logged
    /// once. It is not an error: TLS may be terminated by a service-mesh sidecar in front of Redis.
    /// </remarks>
    public bool Ssl { get; set; }

    /// <summary>
    /// Gets or sets client certificates presented during the TLS handshake, for Redis servers that require
    /// mutual TLS. <see langword="null"/> by default.
    /// </summary>
    public X509Certificate2Collection? ClientCertificates { get; set; }

    /// <summary>
    /// Gets or sets a callback that validates the Redis server certificate, for example to trust a private
    /// certificate authority. <see langword="null"/> by default, which uses the platform's validation.
    /// </summary>
    /// <remarks>Return <see langword="true"/> to accept the certificate.</remarks>
    public Func<X509Certificate2, X509Chain?, SslPolicyErrors, bool>? CertificateValidation { get; set; }
}
