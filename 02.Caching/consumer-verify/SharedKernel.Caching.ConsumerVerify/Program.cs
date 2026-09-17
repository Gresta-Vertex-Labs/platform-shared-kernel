// consumer-verify — exercises 02.Caching's 7 published packages exactly as a downstream
// microservice would: real PackageReference against the local nupkgs feed (nuget.config's
// "local-shared-kernel" source), never ProjectReference — proving the PACKED artifacts actually
// resolve and compose, not merely that the source tree compiles. Five surfaces, each composed
// through a real IHost.StartAsync() (never BuildServiceProvider() alone):
//   1. L1-only            — AddSharedKernelCaching() resolves ICacheService/ICacheKeyProvider,
//                            zero Redis dependency
//   2. L1 + L2 (Redis)    — AddSharedKernelCaching().AddRedisL2(...) round-trips through a real
//                            Testcontainers Redis L2 backplane (verified via a raw redis key read)
//   3. Locking-only       — services.AddRedisDistributedLocking(...) — no FusionCache; lock, contention,
//                            fencing tokens and a self-expiring lease against real Redis
//   4. Hash-store-only    — AddRedisConnection() + AddRedisHashService()/.AddTypedHashStore<T>() —
//                            no FusionCache, no locking
//   5. Pub/Sub-only       — AddRedisConnection() + AddRedisChannelService(), publish/subscribe round-trip
//
// One ephemeral Redis container (Testcontainers.Redis) is started once and shared across surfaces
// 2-5, then disposed at the end of the run. A failure in one surface never prevents the others from
// running — each is reported independently, and the process exits 1 if any surface failed.

using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Core.Extensions;
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

// ── Surface 2: L1 + L2 (Redis) — AddSharedKernelCaching().AddRedisL2(...) ────
static async Task Surface2_L1PlusL2Async(string connectionString)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services
        .AddSharedKernelCaching(o => o.ServiceName = "consumer-verify-l2")
        .AddRedisL2(connectionString);

    using var host = builder.Build();
    await host.StartAsync();

    var cache = host.Services.GetRequiredService<ICacheService>();
    var multiplexer = host.Services.GetRequiredService<IConnectionMultiplexer>();

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
        "Surface 2 PASS: AddSharedKernelCaching().AddRedisL2(...) resolves ICacheService, round-trips through a real Redis L2 backplane");
}

// ── Surface 3: locking-only — services.AddRedisDistributedLocking(...) ──────────
static async Task Surface3_DistributedLockingOnlyAsync(string connectionString)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddRedisDistributedLocking(connectionString);

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
        "Surface 3 PASS: services.AddRedisDistributedLocking(...) resolves IDistributedLockService (no FusionCache); lock, contention, fencing and lease verified against real Redis");
}

// ── Surface 4: hash-store-only — AddRedisConnection() + AddRedisHashService() ────────
static async Task Surface4_HashStoreOnlyAsync(string connectionString)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddRedisConnection(connectionString);

    var cachingBuilder = new ConsumerCachingBuilder(builder.Services);
    cachingBuilder
        .AddRedisHashService()
        .AddTypedHashStore(ConsumerVerifyJsonContext.Default.String);

    using var host = builder.Build();
    await host.StartAsync();

    var hashService = host.Services.GetRequiredService<IRedisHashService>();
    var typedStore = host.Services.GetRequiredService<ITypedHashStore<string>>();

    var key = $"consumer-verify:hash:{Guid.NewGuid():N}";

    await typedStore.SetFieldAsync(key, "field1", "typed-value");
    var typedValue = await typedStore.GetFieldAsync(key, "field1");
    Verify(typedValue == "typed-value", "ITypedHashStore<string> round-trips through a real Redis hash");

    await hashService.SetFieldAsync(key, "field2", "raw-value", ConsumerVerifyJsonContext.Default.String);
    var rawValue = await hashService.GetFieldAsync(key, "field2", ConsumerVerifyJsonContext.Default.String);
    Verify(rawValue == "raw-value", "IRedisHashService round-trips with an explicit JsonTypeInfo<T>");

    var incremented = await hashService.IncrementFieldAsync(key, "counter", 5);
    Verify(incremented == 5, "IRedisHashService.IncrementFieldAsync creates and increments a counter field");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 4 PASS: AddRedisConnection() + AddRedisHashService()/.AddTypedHashStore<T>() resolve IRedisHashService/ITypedHashStore<T> (no FusionCache, no locking)");
}

// ── Surface 5: pub/sub-only — AddRedisConnection() + AddRedisChannelService() ─────
static async Task Surface5_PubSubOnlyAsync(string connectionString)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddRedisConnection(connectionString);

    var cachingBuilder = new ConsumerCachingBuilder(builder.Services);
    cachingBuilder.AddRedisChannelService();

    using var host = builder.Build();
    await host.StartAsync();

    var channelService = host.Services.GetRequiredService<IRedisChannelService>();

    var channel = $"consumer-verify:channel:{Guid.NewGuid():N}";
    var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

    await channelService.SubscribeAsync(channel, message =>
    {
        received.TrySetResult(message);
        return default;
    });

    await channelService.PublishAsync(channel, "hello-pubsub");

    var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10)));
    Verify(completed == received.Task, "IRedisChannelService publish/subscribe round-trips over real Redis Pub/Sub within 10s");
    Verify(received.Task.Result == "hello-pubsub", "the received message matches what was published");

    await channelService.UnsubscribeAsync(channel);

    await host.StopAsync();
    Console.WriteLine(
        "Surface 5 PASS: AddRedisConnection() + AddRedisChannelService() resolve IRedisChannelService; publish/subscribe round-trips over real Redis");
}

// ── Supporting types ──────────────────────────────────────────────────────────

// Minimal ICachingBuilder implementation — a downstream consumer writes exactly this when composing
// a standalone Redis capability package without SharedKernel.Caching.FusionCache's own builder.
internal sealed class ConsumerCachingBuilder(IServiceCollection services) : ICachingBuilder
{
    public IServiceCollection Services { get; } = services;
}

// Source-generated STJ context for the small set of primitive types this harness stores in Redis
// hash fields (IRedisHashService.SetFieldAsync/GetFieldAsync require an explicit JsonTypeInfo<T>
// for AOT-safe, reflection-free serialization).
[JsonSerializable(typeof(string))]
internal sealed partial class ConsumerVerifyJsonContext : JsonSerializerContext
{
}
