using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Core.Diagnostics;

/// <summary>
/// Log statements of the shared Redis connection. They carry the failure type and the exception, never an endpoint,
/// host or connection string.
/// </summary>
internal static partial class RedisCoreLog
{
    [LoggerMessage(
        EventId = RedisCoreLoggingEventIds.ConnectionRestored,
        Level = LogLevel.Information,
        Message = "The Redis connection was restored.")]
    public static partial void ConnectionRestored(ILogger logger);

    [LoggerMessage(
        EventId = RedisCoreLoggingEventIds.ConnectionFailed,
        Level = LogLevel.Warning,
        Message = "The Redis connection failed with failure type {FailureType}, and it reconnects in the background.")]
    public static partial void ConnectionFailed(ILogger logger, ConnectionFailureType failureType, Exception? exception);

    [LoggerMessage(
        EventId = RedisCoreLoggingEventIds.NonLoopbackWithoutTls,
        Level = LogLevel.Warning,
        Message = "A non-loopback Redis endpoint is configured without TLS, so traffic is unencrypted unless TLS is terminated in front of Redis; set RedisConnectionOptions.Ssl to true to encrypt it.")]
    public static partial void NonLoopbackWithoutTls(ILogger logger);

    [LoggerMessage(
        EventId = RedisCoreLoggingEventIds.NotConnectedAtStartup,
        Level = LogLevel.Warning,
        Message = "Redis is not reachable at startup, and the connection keeps retrying in the background.")]
    public static partial void NotConnectedAtStartup(ILogger logger);

    [LoggerMessage(
        EventId = RedisCoreLoggingEventIds.ProbeFailed,
        Level = LogLevel.Debug,
        Message = "The Redis readiness probe failed with {ErrorType}.")]
    public static partial void ProbeFailed(ILogger logger, string errorType, Exception exception);
}
