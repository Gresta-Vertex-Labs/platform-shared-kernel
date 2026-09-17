// consumer-verify — exercises 02.Caching's 7 published packages exactly as a downstream
// microservice would: real PackageReference against the local nupkgs feed (nuget.config's
// "local-shared-kernel" source), never ProjectReference — proving the PACKED artifacts actually
// resolve and compose, not merely that the source tree compiles. Five surfaces, each composed
// through a real IHost.StartAsync() (never BuildServiceProvider() alone):
//   1. L1-only            — AddSharedKernelCaching() resolves ICacheService/ICacheKeyProvider,
//                            zero Redis dependency
//   2. L1 + L2 (Redis)    — AddRedisConnection(...) + AddSharedKernelCaching().AddRedisL2() round-trips
//                            through a real Testcontainers Redis L2 (verified via a raw redis key read),
//                            and IRedisConnectionProbe reports the shared connection healthy
//   3. Locking-only       — AddRedisConnection(...) + AddRedisDistributedLocking() — no FusionCache; lock,
//                            contention, fencing tokens and a self-expiring lease against real Redis
//   4. Hash-store-only    — AddRedisConnection(...) + AddRedisHashService()/AddTypedHashStore<T>() —
//                            no FusionCache, no locking; lookups, counters and key expiry
//   5. Pub/Sub-only       — AddRedisConnection(...) + AddRedisChannelService(); text and typed
//                            publish/subscribe round-trips, then disposing the subscription
//
// Each Redis host registers its own connection with AddRedisConnection, the one registration every
// Redis package builds on. One ephemeral Redis container (Testcontainers.Redis) is started once and
// shared across surfaces 2-5, then disposed at the end of the run. A failure in one surface never
// prevents the others from running — each is reported independently, and the process exits 1 if any
// surface failed.

using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.Core.Health;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using SharedKernel.Caching.Redis.Extensions;
using SharedKernel.Caching.Redis.HashStore;
using SharedKernel.Caching.Redis.HashStore.Extensions;
using SharedKernel.Caching.Redis.PubSub;
using SharedKernel.Caching.Redis.PubSub.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;

var failures = new List<string>();

await using var redisContainer = new RedisBuilder("redis:7.4").Build();
await redisContainer.StartAsync();
var connectionString = redisContainer.GetConnectionString();

await RunSurfaceAsync("Surface 1 (L1-only)", Surface1_L1OnlyAsync, failures);
await RunSurfaceAsync("Surface 2 (L1+L2)", () => Surface2_L1PlusL2Async(connectionString), failures);
await RunSurfaceAsync("Surface 3 (locking-only)", () => Surface3_DistributedLockingOnlyAsync(connectionString), failures);
await RunSurfaceAsync("Surface 4 (hash-store-only)", () => Surface4_HashStoreOnlyAsync(connectionString), failures);
await RunSurfaceAsync("Surface 5 (pub/sub-only)", () => Surface5_PubSubOnlyAsync(connectionString), failures);

Console.WriteLine();
if (failures.Count > 0)
{
    Console.WriteLine($"FAILED — {failures.Count} of 5 surface(s) failed:");
    foreach (var failure in failures)
    {
        Console.WriteLine($"  - {failure}");
    }

    return 1;
}

Console.WriteLine("ALL 5 SURFACES VERIFIED — consumer-verify PASSED");
return 0;

// ── Harness plumbing ─────────────────────────────────────────────────────────

static async Task RunSurfaceAsync(string label, Func<Task> surface, List<string> failures)
{
    try
    {
        await surface();
    }
    catch (Exception ex)
    {
        failures.Add($"{label}: {ex.Message}");
        Console.WriteLine($"{label} FAIL: {ex}");
    }
}

static void Verify(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {label}");
    }
}

// ── Surface 1: L1-only — AddSharedKernelCaching() ────────────────────────────
static async Task Surface1_L1OnlyAsync()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddSharedKernelCaching(o => o.ServiceName = "consumer-verify-l1");

    using var host = builder.Build();
    await host.StartAsync();

    var cache = host.Services.GetRequiredService<ICacheService>();
    var keyProvider = host.Services.GetRequiredService<ICacheKeyProvider>();

    var key = keyProvider.BuildKey("surface", "l1");
    var value = await cache.GetOrSetAsync(
        key: key,
        factory: _ => new ValueTask<int>(42),
        policy: CachePolicy.Default);

    Verify(value == 42, "L1-only GetOrSetAsync round-trip returns the factory value");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 1 PASS: AddSharedKernelCaching() resolves ICacheService/ICacheKeyProvider L1-only, zero DI exceptions, GetOrSetAsync round-trips");
}

// ── Surface 2: L1 + L2 (Redis) — AddRedisConnection(...) + AddRedisL2() ──────
static async Task Surface2_L1PlusL2Async(string connectionString)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddRedisConnection(o => o.ConnectionString = connectionString);
    builder.Services
        .AddSharedKernelCaching(o => o.ServiceName = "consumer-verify-l2")
        .AddRedisL2();

    using var host = builder.Build();
    await host.StartAsync();

    var cache = host.Services.GetRequiredService<ICacheService>();
    var multiplexer = host.Services.GetRequiredService<IConnectionMultiplexer>();

    var health = await host.Services.GetRequiredService<IRedisConnectionProbe>().ProbeAsync();
    Verify(
        health is { IsHealthy: true, Latency: not null, Description: null },
        "IRedisConnectionProbe reports the shared connection healthy with a latency");

    var key = host.Services.GetRequiredService<ICacheKeyProvider>().BuildKey("surface", Guid.NewGuid().ToString("N"));
    var value = await cache.GetOrSetAsync(
        key: key,
        factory: _ => new ValueTask<string>("hello-l2"),
        policy: CachePolicy.Default);

    Verify(value == "hello-l2", "L1+L2 GetOrSetAsync round-trip returns the factory value");

    // Prove the write actually reached the real Redis L2 backplane, not merely L1 — per the
    // documented L2 key format ({KeyPrefix}v2:{user-key}; KeyPrefix is empty by default).
    var db = multiplexer.GetDatabase();
    var landedInRedis = await db.KeyExistsAsync($"v2:{key}");
    Verify(
        landedInRedis,
        "the cached entry is present in the real Redis L2 backplane under the documented v2: key format");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 2 PASS: AddRedisConnection(...) + AddSharedKernelCaching().AddRedisL2() resolve ICacheService and IRedisConnectionProbe, round-trip through a real Redis L2 backplane");
}

// ── Surface 3: locking-only — AddRedisConnection(...) + AddRedisDistributedLocking() ──
static async Task Surface3_DistributedLockingOnlyAsync(string connectionString)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services
        .AddRedisConnection(o => o.ConnectionString = connectionString)
        .AddRedisDistributedLocking();

    using var host = builder.Build();
    await host.StartAsync();

    var locks = host.Services.GetRequiredService<IDistributedLockService>();

    var resource = $"consumer-verify:lock:{Guid.NewGuid():N}";
    var handle = await locks.TryAcquireAsync(resource, new DistributedLockOptions { Expiry = TimeSpan.FromSeconds(30) });

    Verify(handle is { IsHeld: true, FencingToken: > 0 }, "TryAcquireAsync acquires a held lock with a fencing token over real Redis");

    var contended = await locks.TryAcquireAsync(resource);
    Verify(contended is null, "a contended lock on the same resource returns null instead of throwing");

    var firstToken = handle!.FencingToken;
    await handle.DisposeAsync();
    Verify(!handle.IsHeld && handle.LostToken.IsCancellationRequested, "disposing releases the lock and cancels LostToken");

    await using var second = await locks.TryAcquireAsync(resource);
    Verify(second is not null && second.FencingToken > firstToken, "a later acquisition gets a strictly greater fencing token");

    var leaseResource = $"consumer-verify:lease:{Guid.NewGuid():N}";
    var lease = await locks.TryAcquireLeaseAsync(leaseResource, TimeSpan.FromSeconds(30));
    var secondLease = await locks.TryAcquireLeaseAsync(leaseResource, TimeSpan.FromSeconds(30));
    Verify(lease is not null && secondLease is null, "a lease claims the resource once until it expires");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 3 PASS: AddRedisConnection(...) + AddRedisDistributedLocking() resolve IDistributedLockService (no FusionCache); lock, contention, fencing and lease verified against real Redis");
}

// ── Surface 4: hash-store-only — AddRedisConnection(...) + AddRedisHashService() ────────
static async Task Surface4_HashStoreOnlyAsync(string connectionString)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services
        .AddRedisConnection(o => o.ConnectionString = connectionString)
        .AddRedisHashService()
        .AddTypedHashStore(ConsumerVerifyJsonContext.Default.String);

    using var host = builder.Build();
    await host.StartAsync();

    var hashService = host.Services.GetRequiredService<IRedisHashService>();
    var typedStore = host.Services.GetRequiredService<ITypedHashStore<string>>();
    var db = host.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    var key = $"consumer-verify:hash:{Guid.NewGuid():N}";

    await typedStore.SetFieldAsync(key, "field1", "typed-value", TimeSpan.FromMinutes(5));
    var typedValue = await typedStore.GetFieldAsync(key, "field1");
    Verify(typedValue is { IsHit: true, Value: "typed-value" }, "ITypedHashStore<string> round-trips through a real Redis hash");
    Verify(await db.KeyTimeToLiveAsync(key) is { } ttl && ttl > TimeSpan.Zero, "a write with a time-to-live sets the expiry of the whole hash");

    await hashService.SetFieldAsync(key, "field2", "raw-value", ConsumerVerifyJsonContext.Default.String);
    var fields = await hashService.GetFieldsAsync(key, ["field2", "missing"], ConsumerVerifyJsonContext.Default.String);
    Verify(fields.Count == 1 && fields["field2"] == "raw-value", "IRedisHashService.GetFieldsAsync returns existing fields only");

    var miss = await hashService.GetFieldAsync(key, "missing", ConsumerVerifyJsonContext.Default.String);
    Verify(!miss.IsHit, "a missing field is a miss, not a default value");

    var incremented = await hashService.IncrementFieldAsync(key, "counter", 5);
    Verify(incremented == 5, "IRedisHashService.IncrementFieldAsync creates and increments a counter field");

    Verify(await hashService.ExpireAsync(key, null), "ExpireAsync(null) removes the expiry of an existing hash");
    Verify(await db.KeyTimeToLiveAsync(key) is null, "the hash no longer expires");
    Verify(await hashService.DeleteAsync(key) && !await hashService.DeleteAsync(key), "DeleteAsync reports whether the hash existed");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 4 PASS: AddRedisConnection(...) + AddRedisHashService()/AddTypedHashStore<T>() resolve IRedisHashService/ITypedHashStore<T> (no FusionCache, no locking); lookups, counters and expiry verified");
}

// ── Surface 5: pub/sub-only — AddRedisConnection(...) + AddRedisChannelService() ─────
static async Task Surface5_PubSubOnlyAsync(string connectionString)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services
        .AddRedisConnection(o => o.ConnectionString = connectionString)
        .AddRedisChannelService();

    using var host = builder.Build();
    await host.StartAsync();

    var channelService = host.Services.GetRequiredService<IRedisChannelService>();

    var channel = $"consumer-verify:channel:{Guid.NewGuid():N}";
    var typedChannel = $"{channel}:typed";
    var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var receivedTyped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

    var subscription = await channelService.SubscribeAsync(channel, (message, _) =>
    {
        received.TrySetResult(message);
        return ValueTask.CompletedTask;
    });

    await using var typedSubscription = await channelService.SubscribeAsync(
        typedChannel,
        ConsumerVerifyJsonContext.Default.String,
        (message, _) =>
        {
            receivedTyped.TrySetResult(message);
            return ValueTask.CompletedTask;
        });

    var receivers = await channelService.PublishAsync(channel, "hello-pubsub");
    Verify(receivers >= 1, "PublishAsync reports at least one receiver");
    await channelService.PublishAsync(typedChannel, "hello-typed", ConsumerVerifyJsonContext.Default.String);

    var both = Task.WhenAll(received.Task, receivedTyped.Task);
    var completed = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(10)));
    Verify(completed == both, "IRedisChannelService text and typed publish/subscribe round-trip over real Redis Pub/Sub within 10s");
    Verify(received.Task.Result == "hello-pubsub", "the received text message matches what was published");
    Verify(receivedTyped.Task.Result == "hello-typed", "the received typed message matches what was published");

    await subscription.DisposeAsync();
    var afterDispose = await channelService.PublishAsync(channel, "nobody-listens");
    Verify(afterDispose == 0, "after the only subscription is disposed, the channel has no receivers");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 5 PASS: AddRedisConnection(...) + AddRedisChannelService() resolve IRedisChannelService; text and typed publish/subscribe round-trip over real Redis, disposal unsubscribes");
}

// ── Supporting types ──────────────────────────────────────────────────────────

// Source-generated STJ context for the primitive type this harness stores in Redis hash fields and
// publishes on channels (the hash store and pub/sub take an explicit JsonTypeInfo<T> for AOT-safe,
// reflection-free serialization).
[JsonSerializable(typeof(string))]
internal sealed partial class ConsumerVerifyJsonContext : JsonSerializerContext
{
}
