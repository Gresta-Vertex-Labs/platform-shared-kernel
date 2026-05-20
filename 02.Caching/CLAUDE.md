# 02.Caching — Domain Brain

## What This Domain Is

The caching capability domain. Provides a hybrid L1 (in-process memory) / L2 (Redis distributed) cache abstraction powered by **ZiggyCreatures.FusionCache** with built-in stampede protection, background refresh, and fail-safe. The Redis provider lives in a separate package so microservices that only need L1 can stay Redis-free. A dedicated abstractions package (zero infrastructure dependencies) allows layers such as `05.Application` to depend on caching contracts without transitively pulling in FusionCache or StackExchange.Redis.

Philosophy: **Fail-silent by default. Stampede-proof. AOT-compatible.**

---

## Current Phase

**Phase 16 complete (WO-005)** — All planned phases complete. Phase 15 (AOT Hardening + ITypedHashStore) and Phase 16 (Brotli L2 Compression) both delivered. `BrotliCacheSerializer` is implemented as an opt-in decorator over the STJ serializer; activated via `AddBrotliCompression()` on `ICachingBuilder`.

---

## Packages

| Package | Role | NuGet / Project References |
|---------|------|---------------------------|
| `SharedKernel.Caching.Abstractions` | Zero-infra contracts: `ICacheService`, `CachePolicy` (incl. `NeverExpire`), `ICacheKeyProvider`, `IDistributedLockService`, `IRedisChannelService`, `IRedisHashService`, `ITypedHashStore<T>`, `ICacheInvalidationBus`, `CacheInvalidationMessage`, `ICachingBuilder` | `Microsoft.Extensions.DependencyInjection.Abstractions` only |
| `SharedKernel.Caching.FusionCache` | FusionCache L1 provider implementation: `FusionCacheService`, `CacheKeyProvider`, `BrotliCacheSerializer` (opt-in), STJ context base, `AddSharedKernelCaching` DI extension. **Previously named `SharedKernel.Caching` — renamed in Phase 14.** | `SharedKernel.Caching.Abstractions`, FusionCache packages, `01.Core` |
| `SharedKernel.Caching.Redis` | Redis L2 distributed provider, RedLock distributed locking, `RedisChannelService`, `RedisHashService`, `TypedHashStore<T>`, `RedisCacheInvalidationBus`, `CacheInvalidationReceiver` | `SharedKernel.Caching.Abstractions`, StackExchange.Redis, RedLock.net, ZiggyCreatures.FusionCache, ZiggyCreatures.FusionCache.Serialization.SystemTextJson, ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis |

All packages target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

**Microservices must reference `SharedKernel.Caching.Abstractions` for DI contracts. They reference `SharedKernel.Caching.FusionCache` and/or `SharedKernel.Caching.Redis` only at the composition root (startup project).**

---

## Technology Stack

| Concern | Technology | Confirmed Version |
|---------|------------|------------------|
| L1 cache (in-process) | `ZiggyCreatures.FusionCache` | 2.6.0 |
| L2 cache (distributed) | `ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis` | 2.6.0 |
| STJ serialization for FusionCache | `ZiggyCreatures.FusionCache.Serialization.SystemTextJson` | 2.6.0 |
| Distributed locking | `RedLock.net` (over Redis) | 2.3.2 |
| Redis client | `StackExchange.Redis` | 2.13.1 |
| DI abstractions | `Microsoft.Extensions.DependencyInjection.Abstractions` | **10.0.1** (not 10.0.0 — FusionCache transitive floor) |
| OTel tracing (invalidation receiver) | `System.Diagnostics.DiagnosticSource` | BCL in `net10.0` — no additional NuGet reference |

---

## Interface Contracts

### `SharedKernel.Caching.Abstractions` — full public surface

```
ICacheService
    GetAsync<T>(string key, CancellationToken ct)                                        → T?
    SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct)          → void
    GetOrSetAsync<T>(string key, Func<CancellationToken,Task<T>> factory, CachePolicy)  → T
    RemoveAsync(string key, CancellationToken ct)                                        → void
    RemoveByTagAsync(string tag, CancellationToken ct)                                   → void

CachePolicy  (sealed immutable record)
    .Default                              → 5 min L1, 30 min L2, fail-safe on
    .NeverExpire                          → TimeSpan.MaxValue L1 + L2, fail-safe on, no EagerRefreshThreshold
                                            Use only for truly static data; invalidation must be explicit via
                                            ICacheService.RemoveAsync or ICacheInvalidationBus.
    .For(TimeSpan l1, TimeSpan l2)
    .WithTags(params string[] tags)
    .WithEagerRefresh(double threshold)   → default 0.9 (90 % of TTL)

ICacheKeyProvider
    BuildKey(string entity, string id, params string[] extraSegments) → string
    // Format contract: {service}:{entity}:{id}[:{extraSegment}...]

IDistributedLockService
    AcquireAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry, CancellationToken ct)
    → IAsyncDisposable?   (null = lock not acquired within wait; callers decide fallback)

IRedisChannelService                      [ephemeral / non-durable — see rule below]
    PublishAsync(string channel, string message, CancellationToken ct)   → void
    SubscribeAsync(string channel, Func<string, ValueTask> handler, CancellationToken ct) → void
    UnsubscribeAsync(string channel, CancellationToken ct)               → void

IRedisHashService
    GetFieldAsync<T>(string key, string field, JsonTypeInfo<T> typeInfo, CancellationToken ct)        → T?
    SetFieldAsync<T>(string key, string field, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct) → void
    GetAllFieldsAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken ct) → IReadOnlyDictionary<string, T>
    DeleteFieldAsync(string key, string field, CancellationToken ct)                  → void
    IncrementFieldAsync(string key, string field, long delta, CancellationToken ct)   → long

ICacheInvalidationBus                     [no delivery guarantees — see rule below]
    PublishKeyInvalidationAsync(string[] keys, CancellationToken ct)     → void
    PublishTagInvalidationAsync(string[] tags, CancellationToken ct)     → void
    PublishBroadcastInvalidationAsync(CancellationToken ct)              → void
    PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct) → void

CacheInvalidationMessage  (sealed record)
    SourceService    string
    InvalidationType CacheInvalidationType  (Key | Tag | All)
    Keys             string[]?
    Tags             string[]?
    CorrelationId    string   (defaults to Activity.Current?.Id ?? Guid.NewGuid().ToString("N"))
    TimestampUtc     DateTimeOffset

ITypedHashStore<T>                        [AOT-safe typed wrapper — no per-call JsonTypeInfo<T>]
    GetFieldAsync(string key, string field, CancellationToken ct)                           → T?
    SetFieldAsync(string key, string field, T value, CancellationToken ct)                  → void
    GetAllFieldsAsync(string key, CancellationToken ct)          → IReadOnlyDictionary<string, T>
    DeleteFieldAsync(string key, string field, CancellationToken ct)                        → void
    IncrementFieldAsync(string key, string field, long delta, CancellationToken ct)         → long

ICachingBuilder
    Services  IServiceCollection { get; }
```

---

## Implementation Rules

### Core cache rules
- `ICacheService` is **always** backed by FusionCache — never raw `IMemoryCache` or `IDistributedCache`.
- `CachePolicy` is a sealed, immutable record — no subclassing, no mutation after construction.
- Callers **must** use `GetOrSetAsync` for stampede protection. `GetAsync` + `SetAsync` in sequence is a bug.
- The Redis L2 backplane is opt-in. When `AddRedisL2` is not called, `ICacheService` operates L1-only silently.
- `IDistributedLockService.AcquireAsync` returns `null` on timeout — it must never throw for a contended lock.
- No static mutable state anywhere in this domain.
- `CachePolicy.NeverExpire` is for **truly static data only** (reference tables, feature flag snapshots, lookup codes). Any data that can change without notice must never use this preset. Callers using `NeverExpire` must explicitly invalidate via `ICacheService.RemoveAsync` or `ICacheInvalidationBus` — TTL-based expiry will not occur.
- `CachePolicy.NeverExpire` must not set `EagerRefreshThreshold` — eager refresh is meaningless when no expiry is configured.

### Key naming rule
- `CacheKeyProvider` produces keys in the format `{service}:{entity}:{id}[:{extraSegment}...]`.
- `CachingOptions.ServiceName` defaults to `"app"` but must be explicitly set in production — validation fails on null/whitespace.
- Callers must use `ICacheKeyProvider` rather than constructing key strings inline.

### IRedisChannelService scope constraint
- `IRedisChannelService` is scoped to **cache-adjacent ephemeral signaling only**: cache invalidation signals, lightweight broadcast.
- It must never be used for durable, ordered, or guaranteed-delivery messaging — that is `07.Messaging`'s domain.
- This constraint must be stated in the XML doc on the interface.

### ICacheInvalidationBus contract
- Invalidation messages are ephemeral (Redis Pub/Sub). If a subscriber is offline when a message is published, it will not receive the invalidation and must rely on TTL expiry.
- `ICacheInvalidationBus` is not a substitute for `07.Messaging`. This must be stated in the XML doc.
- `PublishBroadcastInvalidationAsync` is a break-glass operation — the receiver logs a structured warning and relies on TTL rather than attempting to enumerate all keys.

### Invalidation channel naming convention (enforced by `RedisCacheInvalidationBus`)
- Targeted: `sharedkernel:cache:invalidation:{service-name}` where `{service-name}` is `options.ServiceName.ToLowerInvariant().Replace(' ', '-')`
- Broadcast: `sharedkernel:cache:invalidation:broadcast` (literal constant)
- Receivers subscribe to both their own named channel and the broadcast channel.

### RedisChannelService rules
- Always use `RedisChannel.Literal(channelName)` — never `RedisChannel.Pattern`.
- All handler exceptions must be caught and logged at `LogLevel.Error`. They must never propagate to the Redis subscriber thread.

### FusionCache STJ serializer AOT rule

- `CachingOptions.SerializerContext` must be set to the microservice's source-generated `JsonSerializerContext` in any NativeAOT build.
- When set, `AddSharedKernelCaching` passes `JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine(SerializerContext, CacheInvalidationMessageJsonContext.Default) }` to `WithSystemTextJsonSerializer()`.
- When absent, FusionCache falls back to reflection-based STJ — acceptable for non-AOT builds, breaks NativeAOT.
- Example: `services.AddSharedKernelCaching(o => o.SerializerContext = MyAppSerializerContext.Default);`

### ITypedHashStore rules

- `ITypedHashStore<T>` is the preferred API for type-specific Redis hash operations in application code. `IRedisHashService` with explicit `JsonTypeInfo<T>` params is the low-level primitive for generic infrastructure code — both are valid and neither is deprecated.
- Register one `ITypedHashStore<T>` per DTO type at startup via `AddTypedHashStore<T>(JsonTypeInfo<T>)`. Multiple types may be registered independently.
- `AddTypedHashStore<T>` throws `InvalidOperationException` if `IRedisHashService` is not already registered — call `AddRedisHashService` first.
- `TypedHashStore<T>` is `internal sealed` — consumers depend only on `ITypedHashStore<T>`.

### RedisHashService rules

- All typed methods accept `JsonTypeInfo<T>` — no `typeof(T)` reflection anywhere.
- `IDatabase` reference is obtained once from `IConnectionMultiplexer.GetDatabase()` and cached.
- `AddRedisHashService` throws `InvalidOperationException` if `IConnectionMultiplexer` is not already registered.

### Brotli compression rules

- Compression is opt-in and L2-only. L1 in-process values are never compressed.
- `AddBrotliCompression()` on `ICachingBuilder` wraps the registered `IFusionCacheSerializer` with `BrotliCacheSerializer`.
- `BrotliCacheSerializer` prepends magic bytes `0x42 0x52` ("BR") to compressed payloads. On read it detects the prefix and decompresses; payloads without the prefix are forwarded to the base STJ serializer unchanged (backward-compatible).
- `BrotliEncoder` (not `BrotliStream`) is used for compression — `ArrayPool<byte>.Shared` for the output buffer, no `MemoryStream` allocation on the hot path.
- Default threshold: 1 024 bytes. Values smaller than this threshold are stored uncompressed regardless of `Enabled`. Default level: `CompressionLevel.Fastest`.
- Do not apply compression to `IRedisHashService` hash fields — they are naturally small. Callers who need per-field compression must compress values before passing to `SetFieldAsync`.
- protobuf-net is explicitly prohibited as a cache serializer: not NativeAOT-compatible, requires `[ProtoContract]` attribute on DTOs (violates clean architecture), breaks the Redis wire format across deployments.

### CacheInvalidationReceiver rules
- It is a `BackgroundService` — registered via `AddHostedService`.
- `StopAsync` must call `IRedisChannelService.UnsubscribeAsync` for both channels before `base.StopAsync`.
- All deserialization and `ICacheService` errors are caught, logged at `LogLevel.Error`, and swallowed.
- OTel span: `"cache.invalidation.receive"` with tags `cache.invalidation.source`, `cache.invalidation.correlation_id`, `cache.invalidation.type`. Attempt to link to parent trace via `ActivityContext.TryParse` on `CorrelationId`.

### STJ serialization rule
- All serialization uses STJ source-generated contexts — no reflection-based serializer.
- `CacheInvalidationMessage` includes its own `CacheInvalidationMessageJsonContext` source-gen context.
- `CacheJsonSerializerContext` (STJ base for FusionCache payloads) resides in `SharedKernel.Caching.FusionCache` under namespace `SharedKernel.Caching.FusionCache`.

### Shared multiplexer rule
- `RedisChannelService`, `RedisHashService`, `RedisCacheInvalidationBus`, and the existing L2 backplane all share the single `IConnectionMultiplexer` singleton. No new connections are created.
- Both `AddRedisL2` and `AddRedisDistributedLocking` register `IConnectionMultiplexer` as a singleton (via `TryAddSingleton` — first caller wins). Either call satisfies the guard in `AddRedisHashService`.

### AOT compatibility
- FusionCache is AOT-compatible as of v1.x — verify release notes on every upgrade.
- StackExchange.Redis is AOT-compatible — avoid dynamic configuration patterns.
- RedLock.net — verify AOT status on each major upgrade and wrap if needed.
- `IRedisHashService` typed methods use `JsonTypeInfo<T>` parameter to remain AOT-safe.
- `CacheInvalidationMessage` uses a source-generated `JsonSerializerContext` — no reflection.

---

## DI Registration (current + planned shape)

```csharp
// L1-only (non-AOT)
services.AddSharedKernelCaching(options => { options.ServiceName = "my-service"; });

// L1-only (NativeAOT — provide source-generated context for all cached types)
services.AddSharedKernelCaching(options => {
    options.ServiceName = "my-service";
    options.SerializerContext = MyAppSerializerContext.Default;
});

// L1 + L2 (Redis backplane, NativeAOT)
services.AddSharedKernelCaching(options => {
             options.ServiceName = "my-service";
             options.SerializerContext = MyAppSerializerContext.Default;
         })
        .AddRedisL2(connectionString, options => { });

// L1 + L2 + Brotli compression for large payloads (opt-in)
services.AddSharedKernelCaching(options => { options.SerializerContext = MyAppSerializerContext.Default; })
        .AddRedisL2(connectionString)
        .AddBrotliCompression(o => { o.L2ThresholdBytes = 2048; });

// Distributed locking (requires Redis, independent of L2 cache)
services.AddRedisDistributedLocking(connectionString);

// Redis Pub/Sub channel service
services.AddSharedKernelCaching(options => { })
        .AddRedisChannelService();

// Redis Hash service — low-level (JsonTypeInfo<T> per call)
services.AddSharedKernelCaching(options => { })
        .AddRedisL2(connectionString)
        .AddRedisHashService();

// Redis Hash service — typed per-DTO (no JsonTypeInfo<T> per call, AOT-safe)
services.AddSharedKernelCaching(options => { options.SerializerContext = MyAppSerializerContext.Default; })
        .AddRedisL2(connectionString)
        .AddRedisHashService()
        .AddTypedHashStore(MyAppSerializerContext.Default.OrderDto)
        .AddTypedHashStore(MyAppSerializerContext.Default.CustomerDto);
// Inject: ITypedHashStore<OrderDto>, ITypedHashStore<CustomerDto>

// Cross-service cache invalidation (full stack)
services.AddSharedKernelCaching(options => { options.ServiceName = "my-service"; })
        .AddRedisL2(connectionString)
        .AddRedisChannelService()
        .AddRedisCacheInvalidationBus()     // registers ICacheInvalidationBus publisher
        .AddCacheInvalidationReceiver();    // registers background service subscriber (opt-in)
```

---

## Test Rules

- Unit tests → `SharedKernel.Caching.FusionCache/SharedKernel.Caching.FusionCache.Tests/`
- Integration tests → `SharedKernel.Caching.Redis/SharedKernel.Caching.Redis.Tests/`
- Redis integration tests **must** use Testcontainers (`16.Testing/SharedKernel.Testing`) — no external Redis dependency.
- Stampede protection must be covered: parallel `GetOrSetAsync` calls verifying factory is invoked exactly once.
- RedLock tests must cover: acquire success, acquire timeout (returns `null`), release on dispose, lock expiry.
- L1-only fallback must be covered: stop the Redis container and assert cache degrades gracefully.
- `RedisChannelService` tests: Pub/Sub round-trip, unsubscribe stops delivery.
- `RedisHashService` tests: set/get field, get-all fields, delete field, increment field, shared multiplexer (no duplicate connection).
- `CacheInvalidationReceiver` tests: Key invalidation, Tag invalidation, broadcast warning logged, sender-offline no-crash, OTel `CorrelationId` present on span.
- `ITypedHashStore<T>` tests: five-method round-trip via Testcontainers (set/get/get-all/delete/increment).
- `BrotliCacheSerializer` unit tests: compressed round-trip (above threshold), passthrough (below threshold), mixed reads (both compressed and uncompressed values readable), compression level configuration.

---

## Changelog

> Maintained by the caching domain agent. One line per significant change.

- [2026-05-14] Domain brain initialized — packages, interfaces, rules, AOT notes
- [2026-05-18] Refreshed for WO-003 — three-package split (Abstractions/Caching/Redis), ICacheKeyProvider, IRedisChannelService, IRedisHashService, ICacheInvalidationBus, CacheInvalidationMessage, channel naming convention, invalidation receiver rules, updated DI registration shape (Phases 5, 6, 7, 12)
- [2026-05-18] Phase 7 complete — Redis package now refs Abstractions directly; AddRedisL2 registers IConnectionMultiplexer; FusionCache pkgs explicit in Redis csproj (agent)
- [2026-05-18] Phase 14 planned (WO-004) — SharedKernel.Caching renamed to SharedKernel.Caching.FusionCache; namespace migration to SharedKernel.Caching.FusionCache.*; CachePolicy.NeverExpire preset added; CLAUDE.md package table and test rules updated (caching-arch-planner)
- [2026-05-20] Phases 15 + 16 planned (WO-005) — Phase 15: fix FusionCache STJ AOT gap (SerializerContext option) + ITypedHashStore typed hash store; Phase 16: opt-in Brotli L2 compression (magic-byte, ArrayPool, threshold); protobuf-net declined (AOT-incompatible); package table, interface contracts, rules, DI shape updated (arch-lead)
- [2026-05-20] B-01 to B-05 complete in SK.02.BrotliCompression — CompressionOptions nested class added to CachingOptions; BrotliCacheSerializer decorator (ArrayPool, magic bytes 0x42 0x52, BrotliStream decompress); AddBrotliCompression extension on ICachingBuilder; AddSharedKernelCaching updated to register IFusionCacheSerializer in DI via WithRegisteredSerializer(); 91 FusionCache + 74 Redis tests passing (caching-phase-implementer)
