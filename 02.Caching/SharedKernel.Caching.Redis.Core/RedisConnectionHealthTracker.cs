using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Core;

/// <summary>
/// Passive observer of <see cref="IConnectionMultiplexer"/> connection lifecycle events,
/// exposing the current <see cref="ConnectionHealthState"/> for health checks and
/// observability tooling.
/// </summary>
/// <remarks>
/// <para>
/// This tracker performs no resubscription or replay logic — it is a pure health-state
/// observer. Capability-specific reconnect handling (e.g., Redis Pub/Sub channel
/// resubscription) remains the responsibility of the owning package
/// (<c>SharedKernel.Caching.Redis.PubSub</c>).
/// </para>
/// <para>
/// StackExchange.Redis supports multiple independent subscribers to
/// <see cref="IConnectionMultiplexer.ConnectionRestored"/> and
/// <see cref="IConnectionMultiplexer.ConnectionFailed"/> — this tracker can coexist with any
/// other subscriber on the same multiplexer without conflict.
/// </para>
/// </remarks>
public sealed partial class RedisConnectionHealthTracker
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisConnectionHealthTracker> _logger;

    // Volatile field so reads do not require a lock. Interlocked is not needed because
    // ConnectionHealthState fits within a single word and volatile guarantees visibility.
    private volatile int _connectionHealth = (int)ConnectionHealthState.Connected;

    /// <summary>
    /// Initialises a new <see cref="RedisConnectionHealthTracker"/> for the supplied
    /// multiplexer and subscribes to its connection lifecycle events.
    /// </summary>
    /// <param name="multiplexer">The shared Redis connection multiplexer.</param>
    /// <param name="logger">Logger for structured health-transition recording.</param>
    public RedisConnectionHealthTracker(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisConnectionHealthTracker> logger)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(logger);

        _multiplexer = multiplexer;
        _logger = logger;

        _multiplexer.ConnectionRestored += OnConnectionRestored;
        _multiplexer.ConnectionFailed += OnConnectionFailed;
    }

    /// <summary>
    /// The current connection health, derived from <see cref="IConnectionMultiplexer"/>
    /// connection lifecycle events.
    /// </summary>
    public ConnectionHealthState ConnectionHealth => (ConnectionHealthState)_connectionHealth;

    // ─── Multiplexer event handlers ─────────────────────────────────────────────

    // Internal visibility allows the test project to simulate events without a live multiplexer.
    internal void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        _connectionHealth = (int)ConnectionHealthState.Connected;
        Log.ConnectionRestored(_logger);
    }

    // Internal visibility allows the test project to simulate events without a live multiplexer.
    internal void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        var newState = _multiplexer.IsConnected
            ? ConnectionHealthState.Connected
            : ConnectionHealthState.Reconnecting;

        _connectionHealth = (int)newState;
        Log.ConnectionFailed(_logger, newState);
    }

    // ─── Logging ─────────────────────────────────────────────────────────────────

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 4001,
            Level = LogLevel.Information,
            Message = "Redis connection restored; connection health is now Connected")]
        internal static partial void ConnectionRestored(ILogger logger);

        [LoggerMessage(
            EventId = 4002,
            Level = LogLevel.Warning,
            Message = "Redis connection failed; transitioning health state to {NewState}")]
        internal static partial void ConnectionFailed(ILogger logger, ConnectionHealthState newState);
    }
}
