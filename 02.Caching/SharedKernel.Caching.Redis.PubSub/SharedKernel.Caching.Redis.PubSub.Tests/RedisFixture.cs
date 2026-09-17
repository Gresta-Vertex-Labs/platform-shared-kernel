using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.PubSub.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests;

/// <summary>Typed message used by the JSON publish and subscribe tests.</summary>
internal sealed record TestMessage(string Kind, int Sequence);

[JsonSerializable(typeof(TestMessage))]
internal sealed partial class TestJsonContext : JsonSerializerContext;

/// <summary>
/// One Redis container shared by every test in the <c>"Redis"</c> collection, with a channel service whose logs are
/// captured. Tests isolate themselves by using a unique channel name.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    private ServiceProvider? _provider;

    /// <summary>Gets the connection string of the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    internal CapturingLoggerProvider Logs { get; } = new();

    internal IRedisChannelService ChannelService => _provider!.GetRequiredService<IRedisChannelService>();

    /// <summary>Gets a database for admin commands such as <c>PUBSUB NUMSUB</c> and <c>CLIENT LIST</c>.</summary>
    public IDatabase Database => _provider!.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _provider = BuildProvider(logs: Logs);
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        await _container.DisposeAsync();
    }

    /// <summary>Builds a separate host with its own Redis connection to the container.</summary>
    internal ServiceProvider BuildProvider(string? connectionStringSuffix = null, CapturingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            if (logs is not null)
                builder.AddProvider(logs);
        });
        services
            .AddRedisConnection(o => o.ConnectionString = ConnectionString + connectionStringSuffix)
            .AddRedisChannelService();
        return services.BuildServiceProvider();
    }

    /// <summary>Returns the number of subscribers the server counts for <paramref name="channel"/>.</summary>
    public async Task<long> CountSubscribersAsync(string channel)
    {
        var reply = (RedisResult[])(await Database.ExecuteAsync("PUBSUB", "NUMSUB", channel))!;
        return (long)reply[1];
    }

    /// <summary>Returns a channel name no other test uses.</summary>
    public static string NewChannel(string prefix) => $"test:channel:{prefix}:{Guid.NewGuid():N}";
}

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>;

/// <summary>A log entry captured by <see cref="CapturingLoggerProvider"/>.</summary>
internal sealed record CapturedLog(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);

/// <summary>Records every log entry written through the loggers it creates.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => _entries.ToArray();

    /// <summary>Returns the entries whose <c>Channel</c> property equals <paramref name="channel"/>.</summary>
    public IReadOnlyList<CapturedLog> ForChannel(string channel) =>
        Entries.Where(e => e.Properties.TryGetValue("Channel", out var value) && Equals(value, channel)).ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, object?>();

            entries.Enqueue(new CapturedLog(category, logLevel, eventId, formatter(state, exception), exception, properties));
        }
    }
}
