using System.Net;
using StackExchange.Redis;
using StackExchange.Redis.Maintenance;
using StackExchange.Redis.Profiling;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// The shared <see cref="IConnectionMultiplexer"/> handed to the distributed cache and the backplane, which ignores
/// <c>Close</c> and <c>Dispose</c>.
/// </summary>
/// <remarks>
/// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> closes and disposes the connection its factory returns
/// when it is disposed (and after errors when its force-reconnect switch is on), and the FusionCache Redis
/// backplane disposes its connection when it unsubscribes. Both assume they own the connection. The shared
/// connection belongs to the DI container, which disposes it once, after every Redis package is done with it.
/// </remarks>
internal sealed class SharedConnectionMultiplexer(IConnectionMultiplexer inner) : IConnectionMultiplexer
{
    public string ClientName => inner.ClientName;

    public string Configuration => inner.Configuration;

    public int TimeoutMilliseconds => inner.TimeoutMilliseconds;

    public long OperationCount => inner.OperationCount;

    [Obsolete("Not supported; if you require ordered pub/sub, please see ChannelMessageQueue.")]
    public bool PreserveAsyncOrder
    {
#pragma warning disable CS0618
        get => inner.PreserveAsyncOrder;
        set => inner.PreserveAsyncOrder = value;
#pragma warning restore CS0618
    }

    public bool IsConnected => inner.IsConnected;

    public bool IsConnecting => inner.IsConnecting;

    [Obsolete("Please use ConfigurationOptions.IncludeDetailInExceptions instead - this will be removed in 3.0.")]
    public bool IncludeDetailInExceptions
    {
#pragma warning disable CS0618
        get => inner.IncludeDetailInExceptions;
        set => inner.IncludeDetailInExceptions = value;
#pragma warning restore CS0618
    }

    public int StormLogThreshold
    {
        get => inner.StormLogThreshold;
        set => inner.StormLogThreshold = value;
    }

    public event EventHandler<RedisErrorEventArgs>? ErrorMessage
    {
        add => inner.ErrorMessage += value;
        remove => inner.ErrorMessage -= value;
    }

    public event EventHandler<ConnectionFailedEventArgs>? ConnectionFailed
    {
        add => inner.ConnectionFailed += value;
        remove => inner.ConnectionFailed -= value;
    }

    public event EventHandler<InternalErrorEventArgs>? InternalError
    {
        add => inner.InternalError += value;
        remove => inner.InternalError -= value;
    }

    public event EventHandler<ConnectionFailedEventArgs>? ConnectionRestored
    {
        add => inner.ConnectionRestored += value;
        remove => inner.ConnectionRestored -= value;
    }

    public event EventHandler<EndPointEventArgs>? ConfigurationChanged
    {
        add => inner.ConfigurationChanged += value;
        remove => inner.ConfigurationChanged -= value;
    }

    public event EventHandler<EndPointEventArgs>? ConfigurationChangedBroadcast
    {
        add => inner.ConfigurationChangedBroadcast += value;
        remove => inner.ConfigurationChangedBroadcast -= value;
    }

    public event EventHandler<ServerMaintenanceEvent>? ServerMaintenanceEvent
    {
        add => inner.ServerMaintenanceEvent += value;
        remove => inner.ServerMaintenanceEvent -= value;
    }

    public event EventHandler<HashSlotMovedEventArgs>? HashSlotMoved
    {
        add => inner.HashSlotMoved += value;
        remove => inner.HashSlotMoved -= value;
    }

    public void RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider) => inner.RegisterProfiler(profilingSessionProvider);

    public ServerCounters GetCounters() => inner.GetCounters();

    public EndPoint[] GetEndPoints(bool configuredOnly = false) => inner.GetEndPoints(configuredOnly);

    public void Wait(Task task) => inner.Wait(task);

    public T Wait<T>(Task<T> task) => inner.Wait(task);

    public void WaitAll(params Task[] tasks) => inner.WaitAll(tasks);

    public int HashSlot(RedisKey key) => inner.HashSlot(key);

    public ISubscriber GetSubscriber(object? asyncState = null) => inner.GetSubscriber(asyncState);

    public IDatabase GetDatabase(int db = -1, object? asyncState = null) => inner.GetDatabase(db, asyncState);

    public IServer GetServer(string host, int port, object? asyncState = null) => inner.GetServer(host, port, asyncState);

    public IServer GetServer(string hostAndPort, object? asyncState = null) => inner.GetServer(hostAndPort, asyncState);

    public IServer GetServer(IPAddress host, int port) => inner.GetServer(host, port);

    public IServer GetServer(EndPoint endpoint, object? asyncState = null) => inner.GetServer(endpoint, asyncState);

    public IServer GetServer(RedisKey key, object? asyncState = null, CommandFlags flags = CommandFlags.None) =>
        inner.GetServer(key, asyncState, flags);

    public IServer[] GetServers() => inner.GetServers();

    public Task<bool> ConfigureAsync(TextWriter? log = null) => inner.ConfigureAsync(log);

    public bool Configure(TextWriter? log = null) => inner.Configure(log);

    public string GetStatus() => inner.GetStatus();

    public void GetStatus(TextWriter log) => inner.GetStatus(log);

    public string? GetStormLog() => inner.GetStormLog();

    public void ResetStormLog() => inner.ResetStormLog();

    public long PublishReconfigure(CommandFlags flags = CommandFlags.None) => inner.PublishReconfigure(flags);

    public Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None) => inner.PublishReconfigureAsync(flags);

    public int GetHashSlot(RedisKey key) => inner.GetHashSlot(key);

    public void ExportConfiguration(Stream destination, ExportOptions options = ExportOptions.All) =>
        inner.ExportConfiguration(destination, options);

    public void AddLibraryNameSuffix(string suffix) => inner.AddLibraryNameSuffix(suffix);

    public override string ToString() => inner.ToString() ?? string.Empty;

    // The container owns the shared connection.
    public void Close(bool allowCommandsToComplete = true)
    {
    }

    public Task CloseAsync(bool allowCommandsToComplete = true) => Task.CompletedTask;

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
