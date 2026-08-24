using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Logging;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Core.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering the shared Redis
/// <see cref="IConnectionMultiplexer"/> and connection health tracking.
/// </summary>
public static partial class RedisConnectionCoreExtensions
{
    private const string LoggerCategoryName = "SharedKernel.Caching.Redis.Core.RedisConnectionCoreExtensions";

    /// <summary>
    /// Registers the shared <see cref="IConnectionMultiplexer"/> singleton and
    /// <see cref="RedisConnectionHealthTracker"/> for the supplied connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">
    /// A StackExchange.Redis connection string (e.g., <c>"localhost:6379"</c>).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="RedisConnectionOptions"/>. When
    /// <see langword="null"/> the defaults are used.
    /// </param>
    /// <returns>The same <paramref name="services"/> to allow further chaining.</returns>
    /// <remarks>
    /// <para>
    /// This is the single registration point for <see cref="IConnectionMultiplexer"/> across
    /// <c>SharedKernel.Caching.Redis</c>, <c>SharedKernel.Caching.Redis.DistributedLocking</c>,
    /// <c>SharedKernel.Caching.Redis.HashStore</c>, and <c>SharedKernel.Caching.Redis.PubSub</c>.
    /// </para>
    /// <para>
    /// The multiplexer is registered via <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection, Func{IServiceProvider, TService})"/> —
    /// first caller wins. Calling this method multiple times across different <c>Add*</c>
    /// extensions in the same container is safe and idempotent; the connection string and
    /// options supplied by the first caller take effect.
    /// </para>
    /// <para>
    /// <b>Fail-fast validation (Phase 45, WO-065/P-436):</b> <see cref="RedisConnectionOptions"/> is
    /// registered via <c>services.AddOptions&lt;RedisConnectionOptions&gt;().Configure(...)
    /// .ValidateDataAnnotations().ValidateOnStart()</c> — the existing <c>[Required]</c>/
    /// <c>[Range]</c> attributes on <see cref="RedisConnectionOptions.ConnectionString"/> and
    /// <see cref="RedisConnectionOptions.ConnectTimeoutMs"/> are genuinely enforced at
    /// host-startup time (<c>IHost.StartAsync()</c>/<c>RunAsync()</c>) via
    /// <c>ValidateOnStart()</c>'s <see cref="Microsoft.Extensions.Options.IStartupValidator"/>
    /// mechanism, surfacing a clear <see cref="OptionsValidationException"/> instead of an
    /// unvalidated bad value flowing silently into StackExchange.Redis and surfacing later as an
    /// opaque <c>RedisConnectionException</c> at first use.
    /// </para>
    /// <para>
    /// <b>TLS/mTLS (Phase 45, WO-065/P-436):</b> <see cref="RedisConnectionOptions.Ssl"/>,
    /// <see cref="RedisConnectionOptions.ClientCertificates"/>, and
    /// <see cref="RedisConnectionOptions.CertificateValidation"/> are composed into the
    /// <see cref="ConfigurationOptions"/> built by the <see cref="IConnectionMultiplexer"/>
    /// factory before <see cref="ConnectionMultiplexer.Connect(ConfigurationOptions, System.IO.TextWriter?)"/>
    /// runs. All three default to today's exact plaintext behavior — fully backward-compatible for
    /// a bare-connection-string caller. When the resolved endpoint set contains a non-loopback host
    /// and <see cref="RedisConnectionOptions.Ssl"/> is <see langword="false"/>, a one-time
    /// <see cref="LogLevel.Warning"/> is logged — never a thrown exception, since
    /// sidecar/mesh-terminated TLS is a legitimate production topology.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRedisConnection(
        this IServiceCollection services,
        string connectionString,
        Action<RedisConnectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services
            .AddOptions<RedisConnectionOptions>()
            .Configure(o =>
            {
                o.ConnectionString = connectionString;
                configure?.Invoke(o);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisConnectionOptions>>().Value;
            var configOptions = BuildConfigurationOptions(options);

            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategoryName);
            WarnIfNonLoopbackWithoutTls(logger, options, configOptions);

            return ConnectionMultiplexer.Connect(configOptions);
        });

        services.TryAddSingleton<RedisConnectionHealthTracker>();

        return services;
    }

    /// <summary>
    /// Builds the StackExchange.Redis <see cref="ConfigurationOptions"/> for the supplied
    /// <see cref="RedisConnectionOptions"/>, composing the TLS/mTLS surface (Phase 45).
    /// </summary>
    /// <remarks>Internal visibility allows the test project to exercise composition directly,
    /// without resolving a real <see cref="IConnectionMultiplexer"/> against a live Redis
    /// instance (mirrors <see cref="RedisConnectionHealthTracker"/>'s existing
    /// internal-for-testability convention).</remarks>
    internal static ConfigurationOptions BuildConfigurationOptions(RedisConnectionOptions options)
    {
        var configOptions = ConfigurationOptions.Parse(options.ConnectionString);
        configOptions.ConnectTimeout = options.ConnectTimeoutMs;
        configOptions.AbortOnConnectFail = false;
        configOptions.Ssl = options.Ssl;

        var clientCertificates = options.ClientCertificates;
        var certificateValidation = options.CertificateValidation;

        if (clientCertificates is { Count: > 0 } || certificateValidation is not null)
        {
            configOptions.SslClientAuthenticationOptions = _ =>
            {
                var sslOptions = new SslClientAuthenticationOptions();

                if (clientCertificates is { Count: > 0 })
                {
                    sslOptions.ClientCertificates = clientCertificates;
                }

                if (certificateValidation is not null)
                {
                    sslOptions.RemoteCertificateValidationCallback = (_, certificate, chain, sslPolicyErrors) =>
                        certificate is not null
                        && certificateValidation(new X509Certificate2(certificate), chain, sslPolicyErrors);
                }

                return sslOptions;
            };
        }

        return configOptions;
    }

    /// <summary>
    /// Logs a one-time <see cref="LogLevel.Warning"/> when the resolved endpoint set contains a
    /// non-loopback host and <see cref="RedisConnectionOptions.Ssl"/> is <see langword="false"/>.
    /// Never throws — sidecar/mesh-terminated TLS is a legitimate production topology.
    /// </summary>
    internal static void WarnIfNonLoopbackWithoutTls(
        ILogger logger,
        RedisConnectionOptions options,
        ConfigurationOptions configOptions)
    {
        if (options.Ssl)
        {
            return;
        }

        var nonLoopbackEndPoint = configOptions.EndPoints.FirstOrDefault(ep => !IsLoopback(ep));

        if (nonLoopbackEndPoint is not null)
        {
            Log.NonLoopbackWithoutTls(logger, nonLoopbackEndPoint.ToString() ?? "(unknown endpoint)");
        }
    }

    internal static bool IsLoopback(EndPoint endPoint) => endPoint switch
    {
        IPEndPoint ipEndPoint => IPAddress.IsLoopback(ipEndPoint.Address),
        DnsEndPoint dnsEndPoint => string.Equals(dnsEndPoint.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(dnsEndPoint.Host, out var address) && IPAddress.IsLoopback(address)),
        _ => false,
    };

    // ─── Logging ─────────────────────────────────────────────────────────────────

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Caching + 102,
            Level = LogLevel.Warning,
            Message = "Redis endpoint '{EndPoint}' is configured with Ssl = false and is not a loopback address. " +
                "If TLS is not terminated elsewhere in the network path (e.g. a service-mesh sidecar/mTLS proxy), " +
                "traffic to Redis is unencrypted. Set RedisConnectionOptions.Ssl = true to enable TLS on this connection.")]
        internal static partial void NonLoopbackWithoutTls(ILogger logger, string endPoint);
    }
}
