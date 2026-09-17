using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Redis.Core.Health;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Primitives.Logging;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Core.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods that register the Redis connection shared by every
/// <c>SharedKernel.Caching.Redis</c> package.
/// </summary>
public static partial class RedisConnectionCoreExtensions
{
    private const string LoggerCategoryName = "SharedKernel.Caching.Redis.Core.RedisConnection";

    /// <summary>
    /// Registers the shared <see cref="IConnectionMultiplexer"/> and <see cref="IRedisConnectionProbe"/>, with
    /// <see cref="RedisConnectionOptions"/> bound from the <c>SharedKernel:Caching:Redis</c> section and
    /// validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root that contains the section.</param>
    /// <param name="configure">
    /// Optional changes applied after binding, for settings configuration cannot carry such as
    /// <see cref="RedisConnectionOptions.ClientCertificates"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The Redis connection is already registered.</exception>
    /// <remarks>
    /// Call it once, before <c>AddRedisL2</c>, <c>AddRedisDistributedLocking</c>, <c>AddRedisHashService</c> or
    /// <c>AddRedisChannelService</c>. The connection is opened when it is first resolved; a server that is
    /// unreachable at that moment does not fail startup, and the connection keeps retrying in the background.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisConnection(builder.Configuration);
    /// builder.Services.AddSharedKernelCaching(builder.Configuration).AddRedisL2();
    /// </code>
    /// </example>
    public static IServiceCollection AddRedisConnection(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<RedisConnectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureNotRegistered(services);

        services.AddValidatedOptions<RedisConnectionOptions, RedisConnectionOptionsValidator>(configuration, validateDataAnnotations: true);
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return AddCore(services);
    }

    /// <summary>
    /// Registers the shared <see cref="IConnectionMultiplexer"/> and <see cref="IRedisConnectionProbe"/>, with
    /// <see cref="RedisConnectionOptions"/> set in code and validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the options; must at least set <see cref="RedisConnectionOptions.ConnectionString"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The Redis connection is already registered.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisConnection(o =>
    /// {
    ///     o.ConnectionString = builder.Configuration.GetConnectionString("redis")!;
    ///     o.Ssl = true;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddRedisConnection(
        this IServiceCollection services,
        Action<RedisConnectionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNotRegistered(services);

        services.AddOptions<RedisConnectionOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RedisConnectionOptions>, RedisConnectionOptionsValidator>());

        return AddCore(services);
    }

    /// <summary>
    /// Throws when <c>AddRedisConnection</c> has not been called, naming the registration that needs it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="caller">The registration method that requires the connection, used in the message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The Redis connection is not registered.</exception>
    /// <remarks>For packages built on the shared connection; application code does not need it.</remarks>
    public static void EnsureRedisConnectionRegistered(this IServiceCollection services, string caller)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);

        if (!IsRegistered(services))
        {
            throw new InvalidOperationException(
                $"{caller} uses the shared Redis connection. Call services.AddRedisConnection(...) before {caller}.");
        }
    }

    private static IServiceCollection AddCore(IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisConnectionOptions>>().Value;
            var configurationOptions = BuildConfigurationOptions(options);
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategoryName);

            WarnIfNonLoopbackWithoutTls(logger, configurationOptions);

            var multiplexer = ConnectionMultiplexer.Connect(configurationOptions);
            multiplexer.ConnectionFailed += (_, e) => Log.ConnectionFailed(logger, e.EndPoint?.ToString() ?? "(unknown)", e.FailureType);
            multiplexer.ConnectionRestored += (_, e) => Log.ConnectionRestored(logger, e.EndPoint?.ToString() ?? "(unknown)");

            if (!multiplexer.IsConnected)
                Log.NotConnectedAtStartup(logger);

            return multiplexer;
        });

        services.AddSingleton<IRedisConnectionProbe, RedisConnectionProbe>();

        return services;
    }

    private static bool IsRegistered(IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(IRedisConnectionProbe));

    private static void EnsureNotRegistered(IServiceCollection services)
    {
        if (IsRegistered(services))
        {
            throw new InvalidOperationException(
                "The Redis connection is already registered. Call AddRedisConnection once; every Redis package shares that connection.");
        }
    }

    internal static ConfigurationOptions BuildConfigurationOptions(RedisConnectionOptions options)
    {
        var configurationOptions = ConfigurationOptions.Parse(options.ConnectionString);
        configurationOptions.ConnectTimeout = (int)options.ConnectTimeout.TotalMilliseconds;
        configurationOptions.SyncTimeout = (int)options.CommandTimeout.TotalMilliseconds;
        configurationOptions.AsyncTimeout = (int)options.CommandTimeout.TotalMilliseconds;
        configurationOptions.AbortOnConnectFail = false;
        configurationOptions.BacklogPolicy = options.FailFastWhenDisconnected ? BacklogPolicy.FailFast : BacklogPolicy.Default;

        // Ssl = true turns TLS on; it never turns off TLS that the connection string enables.
        if (options.Ssl)
            configurationOptions.Ssl = true;

        var clientCertificates = options.ClientCertificates;
        var certificateValidation = options.CertificateValidation;

        if (clientCertificates is { Count: > 0 } || certificateValidation is not null)
        {
            configurationOptions.SslClientAuthenticationOptions = _ =>
            {
                var sslOptions = new SslClientAuthenticationOptions();

                if (clientCertificates is { Count: > 0 })
                    sslOptions.ClientCertificates = clientCertificates;

                if (certificateValidation is not null)
                {
                    sslOptions.RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
                        certificate is not null
                        && certificateValidation(
                            certificate as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()),
                            chain,
                            errors);
                }

                return sslOptions;
            };
        }

        return configurationOptions;
    }

    internal static void WarnIfNonLoopbackWithoutTls(ILogger logger, ConfigurationOptions configurationOptions)
    {
        if (configurationOptions.Ssl)
            return;

        var nonLoopback = configurationOptions.EndPoints.FirstOrDefault(endPoint => !IsLoopback(endPoint));
        if (nonLoopback is not null)
            Log.NonLoopbackWithoutTls(logger, nonLoopback.ToString() ?? "(unknown)");
    }

    internal static bool IsLoopback(EndPoint endPoint) => endPoint switch
    {
        IPEndPoint ip => IPAddress.IsLoopback(ip.Address),
        DnsEndPoint dns => string.Equals(dns.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(dns.Host, out var address) && IPAddress.IsLoopback(address)),
        _ => false,
    };

    private static partial class Log
    {
        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 100, Level = LogLevel.Information,
            Message = "Redis connection to {EndPoint} restored")]
        internal static partial void ConnectionRestored(ILogger logger, string endPoint);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 101, Level = LogLevel.Warning,
            Message = "Redis connection to {EndPoint} failed ({FailureType}); reconnecting in the background")]
        internal static partial void ConnectionFailed(ILogger logger, string endPoint, ConnectionFailureType failureType);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 102, Level = LogLevel.Warning,
            Message = "Redis endpoint {EndPoint} is not a loopback address and TLS is off. Traffic is unencrypted unless "
                + "TLS is terminated in front of Redis; set RedisConnectionOptions.Ssl to true to encrypt it.")]
        internal static partial void NonLoopbackWithoutTls(ILogger logger, string endPoint);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 103, Level = LogLevel.Warning,
            Message = "Redis is not reachable at startup; the connection keeps retrying in the background")]
        internal static partial void NotConnectedAtStartup(ILogger logger);
    }
}
