namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Represents the current connection health of a Redis channel service instance.
/// </summary>
/// <remarks>
/// Intended for consumption by health checks and observability tooling. Transitions are
/// driven by the underlying <c>IConnectionMultiplexer</c> lifecycle events
/// (<c>ConnectionRestored</c> / <c>ConnectionFailed</c>).
/// </remarks>
public enum ConnectionHealthState
{
    /// <summary>
    /// The Redis connection is established and operational.
    /// </summary>
    Connected = 0,

    /// <summary>
    /// The Redis connection was lost and the multiplexer is actively attempting to reconnect.
    /// Subscriptions will be automatically reinstated once <see cref="Connected"/> is reached.
    /// </summary>
    Reconnecting = 1,

    /// <summary>
    /// The Redis connection is not available and no reconnection attempt is in progress.
    /// </summary>
    Disconnected = 2,
}
