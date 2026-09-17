using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.Core.Health;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// <see cref="IRedisConnectionProbe"/> and the shared <see cref="IConnectionMultiplexer"/> against a real Redis
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
        var probe = provider.GetRequiredService<IRedisConnectionProbe>();

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

        var health = await provider.GetRequiredService<IRedisConnectionProbe>().ProbeAsync();

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
        var probe = provider.GetRequiredService<IRedisConnectionProbe>();
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
    public async Task ProbeAsync_UnreachableEndpoint_RepeatedCalls_StayUnhealthy()
    {
        await using var provider = Build(o =>
        {
            o.ConnectionString = "localhost:1";
            o.ConnectTimeout = TimeSpan.FromMilliseconds(200);
            o.CommandTimeout = TimeSpan.FromMilliseconds(200);
        });
        var probe = provider.GetRequiredService<IRedisConnectionProbe>();

        for (var i = 0; i < 3; i++)
            Assert.False((await probe.ProbeAsync()).IsHealthy);
    }

    [Fact]
    public async Task ProbeAsync_CanceledToken_Throws()
    {
        await using var provider = Build(o => o.ConnectionString = _container.GetConnectionString());
        var probe = provider.GetRequiredService<IRedisConnectionProbe>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.ProbeAsync(new CancellationToken(canceled: true)));
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
