using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Redis.Core.Diagnostics;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Core.Extensions;

/// <summary>
/// Opens the shared <see cref="IConnectionMultiplexer"/> and logs its connection events. A kernel type so the
/// logger category is <c>ILogger&lt;RedisConnectionFactory&gt;</c>.
/// </summary>
internal sealed class RedisConnectionFactory(IOptions<RedisConnectionOptions> options, ILogger<RedisConnectionFactory> logger)
{
    public IConnectionMultiplexer Connect()
    {
        var configurationOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options.Value);

        RedisConnectionCoreExtensions.WarnIfNonLoopbackWithoutTls(logger, configurationOptions);

        var multiplexer = ConnectionMultiplexer.Connect(configurationOptions);
        multiplexer.ConnectionFailed += (_, e) => RedisCoreLog.ConnectionFailed(logger, e.FailureType, e.Exception);
        multiplexer.ConnectionRestored += (_, _) => RedisCoreLog.ConnectionRestored(logger);

        if (!multiplexer.IsConnected)
            RedisCoreLog.NotConnectedAtStartup(logger);

        return multiplexer;
    }
}
