using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Primitives.Health;
using SharedKernel.Caching.Redis.Core.Diagnostics;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.Core.Health;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// The Redis connection readiness probe and the shared <see cref="IConnectionMultiplexer"/> against a real Redis
/// and against an endpoint nothing listens on.
/// </summary>
public sealed class RedisConnectionProbeTests : IAsyncLifetime
{
    private const string Password = "Pr0be-S3cret";

    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task ProbeAsync_ReachableRedis_IsHealthyWithLatency()
    {
        await using var provider = Build(o => o.ConnectionString = _container.GetConnectionString());
        var probe = provider.GetRequiredReadinessProbe(RedisReadinessProbeNames.Connection);

        var health = await probe.ProbeAsync();

        Assert.True(health.IsHealthy);
        Assert.NotNull(health.Latency);
        Assert.True(health.Latency >= TimeSpan.Zero);
        Assert.True(health.Latency < TimeSpan.FromSeconds(5));
        Assert.Null(health.Description);
    }

    [Fact]
    public async Task ProbeAsync_UsesTheSharedMultiplexer()
    {
        await using var provider = Build(o => o.ConnectionString = _container.GetConnectionString());

        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
        Assert.Same(multiplexer, provider.GetRequiredService<IConnectionMultiplexer>());

        var health = await provider.GetRequiredReadinessProbe(RedisReadinessProbeNames.Connection).ProbeAsync();

        Assert.True(health.IsHealthy);
        Assert.True(multiplexer.IsConnected);
    }

    [Fact]
    public async Task SharedMultiplexer_AppliesTheConfiguredTimeouts()
    {
        await using var provider = Build(o =>
        {
            o.ConnectionString = _container.GetConnectionString();
            o.ConnectTimeout = TimeSpan.FromMilliseconds(1_500);
            o.CommandTimeout = TimeSpan.FromMilliseconds(2_500);
        });

        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();

        Assert.Equal(2_500, multiplexer.TimeoutMilliseconds);
        await multiplexer.GetDatabase().StringSetAsync("probe:timeouts", "ok");
        Assert.Equal("ok", (string?)await multiplexer.GetDatabase().StringGetAsync("probe:timeouts"));
    }

    [Fact]
    public async Task ProbeAsync_UnreachableEndpoint_IsUnhealthy_WithoutThrowingOrLeakingTheConnectionString()
    {
        var connectionString = "localhost:1,password=" + Password;
        var loggerFactory = new InMemoryLoggerFactory();

        await using var provider = Build(
            o =>
            {
                o.ConnectionString = connectionString;
                o.ConnectTimeout = TimeSpan.FromMilliseconds(200);
                o.CommandTimeout = TimeSpan.FromMilliseconds(200);
            },
            loggerFactory);

        // Resolving does not throw: the connection keeps retrying in the background.
        var probe = provider.GetRequiredReadinessProbe(RedisReadinessProbeNames.Connection);
        var health = await probe.ProbeAsync();

        Assert.False(health.IsHealthy);
        Assert.Null(health.Latency);
        Assert.False(string.IsNullOrWhiteSpace(health.Description));
        Assert.DoesNotContain(Password, health.Description!, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost:1", health.Description!, StringComparison.Ordinal);

        var records = loggerFactory.Loggers.Values.SelectMany(l => l.Records).ToList();
        records.ShouldHaveLogged(new EventId(LoggingEventIdRanges.Caching + 103), LogLevel.Warning);
        Assert.All(records, r => Assert.DoesNotContain(Password, r.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeAsync_PingFails_IsUnhealthy_AndLogsTheExceptionAtDebug()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.IsConnected.Returns(true);
        var database = Substitute.For<IDatabase>();
        database.PingAsync(Arg.Any<CommandFlags>()).Returns(Task.FromException<TimeSpan>(new TimeoutException("ping timed out")));
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        await using var provider = new ServiceCollection().AddSingleton(multiplexer).BuildServiceProvider();
        var logger = new InMemoryLogger<RedisConnectionProbe>();
        var probe = new RedisConnectionProbe(provider, logger);

        var health = await probe.ProbeAsync();

        Assert.False(health.IsHealthy);
        var record = logger.Records.ShouldHaveLoggedWithException(new EventId(RedisCoreLoggingEventIds.ProbeFailed), typeof(TimeoutException));
        Assert.Equal(LogLevel.Debug, record.LogLevel);
        Assert.Equal(LoggingEventIdRanges.Caching + 104, record.EventId.Id);
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public void EventIds_StayInTheRedisCoreSubBlock_AndKeepTheirNumbers()
    {
        Assert.Equal(2100, RedisCoreLoggingEventIds.ConnectionRestored);
        Assert.Equal(2101, RedisCoreLoggingEventIds.ConnectionFailed);
        Assert.Equal(2102, RedisCoreLoggingEventIds.NonLoopbackWithoutTls);
        Assert.Equal(2103, RedisCoreLoggingEventIds.NotConnectedAtStartup);
        Assert.Equal(2104, RedisCoreLoggingEventIds.ProbeFailed);
        Assert.All(
            typeof(RedisCoreLoggingEventIds).GetFields().Where(f => f.IsLiteral && f.Name != nameof(RedisCoreLoggingEventIds.Base)),
            field => Assert.True(
                LoggingEventIdRanges.IsInSubBlock((int)field.GetRawConstantValue()!, LoggingEventIdRanges.Caching, 1),
                field.Name));
    }

    [Fact]
    public async Task Connection_LogsUnderTheKernelFactoryCategory_NotAHandTypedString()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        await using var provider = Build(
            o =>
            {
                o.ConnectionString = "localhost:1";
                o.ConnectTimeout = TimeSpan.FromMilliseconds(200);
            },
            loggerFactory);

        _ = provider.GetRequiredService<IConnectionMultiplexer>();

        loggerFactory.ShouldHaveCategory(typeof(RedisConnectionFactory));
        Assert.DoesNotContain("SharedKernel.Caching.Redis.Core.RedisConnection", loggerFactory.Loggers.Keys);
    }

    [Fact]
    public async Task ProbeAsync_UnreachableEndpoint_RepeatedCalls_StayUnhealthy()
    {
        await using var provider = Build(o =>
        {
            o.ConnectionString = "localhost:1";
            o.ConnectTimeout = TimeSpan.FromMilliseconds(200);
            o.CommandTimeout = TimeSpan.FromMilliseconds(200);
        });
        var probe = provider.GetRequiredReadinessProbe(RedisReadinessProbeNames.Connection);

        for (var i = 0; i < 3; i++)
            Assert.False((await probe.ProbeAsync()).IsHealthy);
    }

    [Fact]
    public async Task ProbeAsync_CanceledToken_Throws()
    {
        await using var provider = Build(o => o.ConnectionString = _container.GetConnectionString());
        var probe = provider.GetRequiredReadinessProbe(RedisReadinessProbeNames.Connection);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.ProbeAsync(new CancellationToken(canceled: true)));
    }


    [Fact]
    public async Task ConnectionFailed_ConnectionDrops_LogsWarningWithTheConnectionException_AndStartupDoesNotThrow()
    {
        await using var container = new RedisBuilder("redis:7-alpine").Build();
        await container.StartAsync();
        var endpoint = container.GetConnectionString().Split(',')[0];
        var loggerFactory = new InMemoryLoggerFactory();

        await using var provider = Build(o => o.ConnectionString = container.GetConnectionString(), loggerFactory);
        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
        Assert.True(multiplexer.IsConnected);

        // The handler is attached after the initial connect, so the event comes from an established connection dropping.
        await container.StopAsync();

        var eventId = new EventId(LoggingEventIdRanges.Caching + 101);
        LogRecord? failed = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (failed is null && DateTime.UtcNow < deadline)
        {
            failed = loggerFactory.Loggers.Values.SelectMany(l => l.Records).FirstOrDefault(r => r.EventId == eventId);
            if (failed is null)
                await Task.Delay(50);
        }

        Assert.NotNull(failed);
        Assert.Equal(LogLevel.Warning, failed.LogLevel);
        Assert.NotNull(failed.Exception);

        var records = loggerFactory.Loggers.Values.SelectMany(l => l.Records).ToList();
        records.ShouldHaveLoggedWithException(eventId, typeof(Exception));
        records.ShouldHaveLoggedTemplate(eventId, "The Redis connection failed with failure type {FailureType}, and it reconnects in the background.");

        // No endpoint, host or port reaches the text (L5).
        Assert.DoesNotContain(endpoint, failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_UnreachableEndpoint_DoesNotThrow()
    {
        await using var provider = Build(o =>
        {
            o.ConnectionString = "localhost:1";
            o.ConnectTimeout = TimeSpan.FromMilliseconds(200);
        });

        Assert.NotNull(provider.GetRequiredService<IConnectionMultiplexer>());
    }

    private static ServiceProvider Build(Action<RedisConnectionOptions> configure, ILoggerFactory? loggerFactory = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (loggerFactory is not null)
            services.AddSingleton(loggerFactory);
        services.AddRedisConnection(configure);
        return services.BuildServiceProvider();
    }
}
