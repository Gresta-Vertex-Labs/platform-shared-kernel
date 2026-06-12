using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// Unit tests for <see cref="RedisConnectionHealthTracker"/>.
/// Covers RC-04: <see cref="ConnectionHealthState"/> transitions via internal event simulation.
/// </summary>
public sealed class RedisConnectionHealthTrackerTests
{
    [Fact]
    public void Constructor_NullMultiplexer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RedisConnectionHealthTracker(null!, NullLogger<RedisConnectionHealthTracker>.Instance));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var mux = Substitute.For<IConnectionMultiplexer>();

        Assert.Throws<ArgumentNullException>(() =>
            new RedisConnectionHealthTracker(mux, null!));
    }

    [Fact]
    public void ConnectionHealth_InitialState_IsConnected()
    {
        var mux = Substitute.For<IConnectionMultiplexer>();

        var tracker = new RedisConnectionHealthTracker(mux, NullLogger<RedisConnectionHealthTracker>.Instance);

        Assert.Equal(ConnectionHealthState.Connected, tracker.ConnectionHealth);
    }

    [Fact]
    public void OnConnectionFailed_MultiplexerDisconnected_TransitionsToReconnecting()
    {
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.IsConnected.Returns(false);

        var tracker = new RedisConnectionHealthTracker(mux, NullLogger<RedisConnectionHealthTracker>.Instance);

        tracker.OnConnectionFailed(mux, null!);

        Assert.Equal(ConnectionHealthState.Reconnecting, tracker.ConnectionHealth);
    }

    [Fact]
    public void OnConnectionFailed_MultiplexerStillConnected_RemainsConnected()
    {
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.IsConnected.Returns(true);

        var tracker = new RedisConnectionHealthTracker(mux, NullLogger<RedisConnectionHealthTracker>.Instance);

        tracker.OnConnectionFailed(mux, null!);

        Assert.Equal(ConnectionHealthState.Connected, tracker.ConnectionHealth);
    }

    [Fact]
    public void OnConnectionRestored_AfterFailure_TransitionsToConnected()
    {
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.IsConnected.Returns(false);

        var tracker = new RedisConnectionHealthTracker(mux, NullLogger<RedisConnectionHealthTracker>.Instance);

        tracker.OnConnectionFailed(mux, null!);
        Assert.Equal(ConnectionHealthState.Reconnecting, tracker.ConnectionHealth);

        tracker.OnConnectionRestored(mux, null!);

        Assert.Equal(ConnectionHealthState.Connected, tracker.ConnectionHealth);
    }

    [Fact]
    public void Constructor_SubscribesToConnectionLifecycleEvents()
    {
        var mux = Substitute.For<IConnectionMultiplexer>();

        _ = new RedisConnectionHealthTracker(mux, NullLogger<RedisConnectionHealthTracker>.Instance);

        // Verify both event accessors were subscribed to (add_ConnectionRestored / add_ConnectionFailed).
        mux.Received(1).ConnectionRestored += Arg.Any<EventHandler<ConnectionFailedEventArgs>>();
        mux.Received(1).ConnectionFailed += Arg.Any<EventHandler<ConnectionFailedEventArgs>>();
    }
}
