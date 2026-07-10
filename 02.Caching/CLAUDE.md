# 02.Caching — Domain Brain

## What This Domain Is

The caching capability domain. Provides a hybrid L1 (in-process memory) / L2 (Redis distributed) cache abstraction powered by **ZiggyCreatures.FusionCache** with built-in stampede protection, background refresh, and fail-safe. The Redis provider lives in a separate package so microservices that only need L1 can stay Redis-free. A dedicated abstractions package (zero infrastructure dependencies) allows layers such as `05.Application` to depend on caching contracts without transitively pulling in FusionCache or StackExchange.Redis.

Philosophy: **Fail-silent by default. Stampede-proof. AOT-compatible.**

---

## Current Phase

**Phases 1–37 complete (WO-006 + WO-007 + WO-023 + WO-041 logging retrofit).**

Last completed: Phase 36 (RedisPubSubExtraction) — ephemeral Redis Pub/Sub signaling and cache invalidation (`RedisChannelService`, `RedisCacheInvalidationBus`, `CacheInvalidationReceiver`, `AddRedisChannelService`, `AddRedisCacheInvalidationBus`, `AddCacheInvalidationReceiver`) extracted from `SharedKernel.Caching.Redis` into new standalone package `SharedKernel.Caching.Redis.PubSub`, depending only on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core`. The Phase 26 reconnect/resubscribe replay logic (`Dictionary<string, SubscriptionEntry>` + `_registryLock`, `volatile int _connectionHealth`, `OnConnectionRestored`/`OnConnectionFailed`) relocated **intact** with `RedisChannelService` — it coexists independently with `.Redis.Core`'s passive `RedisConnectionHealthTracker`. `AddRedisChannelService` startup guard message updated to reference `AddRedisConnection` (directly, or transitively via `AddRedisL2` / `AddRedisDistributedLocking` / `AddRedisHashService`). `SharedKernel.Caching.Redis` is now slimmed to its end state: `AddRedisL2`, `RedisL2Options`, `IRedisL2BatchService` (internal), and the FusionCache Redis backplane wiring only — `Microsoft.Extensions.Hosting.Abstractions` and `Polly.Core` PackageReferences removed (Polly resilience is still consumed transitively via `.Redis.Core`). 28 Redis + 41 Redis.DistributedLocking + 30 Redis.HashStore + 33 Redis.Core + 41 Redis.PubSub tests passing.

**End state achieved (WO-023)**: `SharedKernel.Caching.Redis` is split into five packages — `.Redis.Core` (Phase 32, connection/health/resilience root), `.Redis` (Phases 33–35, L2 FusionCache backplane only), `.Redis.DistributedLocking` (Phase 34, RedLock), `.Redis.HashStore` (Phase 35, structured hash storage), `.Redis.PubSub` (Phase 36, cache invalidation signaling). Sibling packages (`.Redis`, `.DistributedLocking`, `.HashStore`, `.PubSub`) never reference each other — only `.Redis.Core` + `Abstractions`. All `Add*` fluent DI shapes and `SharedKernel.Caching.Abstractions` contracts are unchanged throughout WO-023 — this was a pure package-topology refactor.

**Phase 37 (WO-041, P-252) — Logging Retrofit — complete.** Every production log statement in `SharedKernel.Caching.FusionCache`, `SharedKernel.Caching.Redis.Core`, `SharedKernel.Caching.Redis.DistributedLocking`, and `SharedKernel.Caching.Redis.PubSub` now uses the platform's `[LoggerMessage]` source-generated pattern, and every `EventId` in this domain is renumbered into this domain's own `LoggingEventIdRanges.Caching` (2000-2999) block via `LoggingEventIdRanges.Caching + {offset}` compile-time constant expressions. This closed two confirmed pre-existing defects: `SharedKernel.Caching.Redis.Core`'s `RedisConnectionHealthTracker` and `SharedKernel.Caching.Redis.PubSub`'s `CacheInvalidationReceiver` had independently chosen `EventId` 4001/4002 (a live collision across two packages that run in the same process), and `FusionCacheService`/`RedisChannelService` had squatted on `01.Core`'s and `03.Domain`'s reserved `EventId` blocks respectively. See the "Logging (EventId sub-blocks)" section below for the full per-package allocation.

---

## Packages

> **WO-023 (Phases 32–36) split `SharedKernel.Caching.Redis` into five packages — complete as of Phase 36.** The table below reflects the final topology.

| Package | Role | NuGet / Project References |
| ------- | ---- | -------------------------- |
| `SharedKernel.Caching.Abstractions` | Zero-infra contracts: `ICacheService`, `CachePolicy` (incl. `NeverExpire`), `ICacheKeyProvider`, `IDistributedLockService`, `IRenewableLock`, `IRedisChannelService`, `IRedisHashService`, `ITypedHashStore<T>`, `ICacheInvalidationBus`, `CacheInvalidationMessage`, `ConnectionHealthState`, `ICachingBuilder`, `CachingCoreOptions`, `ICacheWarmupStrategy`, `ITenantCacheKeyProvider` | `Microsoft.Extensions.DependencyInjection.Abstractions` only |
| `SharedKernel.Caching.FusionCache` | FusionCache L1 provider implementation: `FusionCacheService`, `CacheKeyProvider`, `BrotliCacheSerializer` (opt-in), STJ context base, `AddSharedKernelCaching` DI extension. **Previously named `SharedKernel.Caching` — renamed in Phase 14.** | `SharedKernel.Caching.Abstractions`, FusionCache packages, `01.Core` |
| `SharedKernel.Caching.Redis.Core` *(implemented, Phase 32)* | Foundational connection layer: `IConnectionMultiplexer` registration (`AddRedisConnection`, `TryAddSingleton`, first-caller-wins), `RedisConnectionHealthTracker` (`ConnectionHealthState` tracking), `RedisCircuitBreakerOptions` + `AddRedisCircuitBreaker` (Polly v8 `ResiliencePipeline` factory). Zero references to FusionCache, RedLock.net, or capability-specific types. **Dependency root for `.Redis`, `.DistributedLocking`, `.HashStore`, `.PubSub`.** | `SharedKernel.Caching.Abstractions`, StackExchange.Redis, Polly.Core, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `SharedKernel.Caching.Redis` *(slimmed, Phases 33–36 — end state)* | Redis L2 FusionCache distributed backplane only: `AddRedisL2`, `RedisL2Options`, `IRedisL2BatchService` (internal batch pipeline helper, Phase 22). `AddRedisL2` sources `IConnectionMultiplexer` via `services.AddRedisConnection(...)` and the optional circuit breaker `ResiliencePipeline` via `services.AddRedisCircuitBreaker(...)`, both from `.Redis.Core` (Phase 33). `RedisL2Options.CircuitBreaker` is the top-level `RedisCircuitBreakerOptions` type from `.Redis.Core`. Brotli compression itself lives in `.FusionCache` (Phase 16) — this package's role is `.WithRegisteredSerializer()` wiring (Phase 18 AOT rule) so the Brotli-wrapped serializer is picked up. RedLock-based distributed locking was removed in Phase 34, `RedisHashService`/`TypedHashStore<T>` were removed in Phase 35, and `RedisChannelService`/`RedisCacheInvalidationBus`/`CacheInvalidationReceiver` were removed in Phase 36 — `RedLock.net`, `Microsoft.Extensions.Hosting.Abstractions`, and `Polly.Core` PackageReferences dropped (Polly resilience is consumed transitively via `.Redis.Core`). **Must not reference `SharedKernel.Caching.FusionCache` (fixed in Ph17).** | `SharedKernel.Caching.Abstractions`, `SharedKernel.Caching.Redis.Core`, Microsoft.Extensions.Caching.StackExchangeRedis, ZiggyCreatures.FusionCache, ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis |
| `SharedKernel.Caching.Redis.DistributedLocking` *(implemented, Phase 34)* | RedLock.net distributed locking: `RedLockDistributedLockService` (`IDistributedLockService`), `RedLockRenewableLock` + `KeepAliveAsync` extension (`IRenewableLock`), `RedisLockOptions`, `AddRedisDistributedLocking` (canonical `ICachingBuilder` overload + `[Obsolete]` `IServiceCollection` shim with its own private `RedisLockCachingBuilder` adapter). Sources `IConnectionMultiplexer` via `services.AddRedisConnection(...)` from `.Redis.Core`. | `SharedKernel.Caching.Abstractions`, `SharedKernel.Caching.Redis.Core`, RedLock.net |
| `SharedKernel.Caching.Redis.HashStore` *(implemented, Phase 35)* | Structured Redis Hash storage: `RedisHashService` (`IRedisHashService`), `TypedHashStore<T>` (internal sealed, `ITypedHashStore<T>`), `AddRedisHashService`, `AddTypedHashStore<T>`. `AddRedisHashService` sources `IConnectionMultiplexer` via `services.AddRedisConnection(...)` (guard on `AddRedisConnection` having been called) and the optional circuit breaker `ResiliencePipeline` via `sp.GetService<ResiliencePipeline>()`, both from `.Redis.Core`. | `SharedKernel.Caching.Abstractions`, `SharedKernel.Caching.Redis.Core` |
| `SharedKernel.Caching.Redis.PubSub` *(implemented, Phase 36)* | Ephemeral Redis Pub/Sub signaling and cache invalidation: `RedisChannelService` (`IRedisChannelService`, with Phase 26 reconnect/resubscribe replay relocated intact), `RedisCacheInvalidationBus` (`ICacheInvalidationBus`), `CacheInvalidationReceiver` (`BackgroundService`). `AddRedisChannelService` sources `IConnectionMultiplexer` via the `AddRedisConnection` guard and the optional circuit breaker `ResiliencePipeline` via `sp.GetService<ResiliencePipeline>()`, both from `.Redis.Core`. **Stays in `02.Caching` — see "Why pub/sub stays in 02.Caching" below.** | `SharedKernel.Caching.Abstractions`, `SharedKernel.Caching.Redis.Core`, Microsoft.Extensions.Hosting.Abstractions |

All packages target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

**Microservices must reference `SharedKernel.Caching.Abstractions` for DI contracts.** They reference concrete provider packages only at the composition root (startup project) — and only the ones they need:

- L1-only: `SharedKernel.Caching.FusionCache`
- L1 + L2 cache: `+ SharedKernel.Caching.Redis` (which transitively brings `.Redis.Core`)
- Distributed locking only (no cache): `SharedKernel.Caching.Redis.Core` + `SharedKernel.Caching.Redis.DistributedLocking`
- Structured hash storage only (e.g., session store): `SharedKernel.Caching.Redis.Core` + `SharedKernel.Caching.Redis.HashStore`
- Cache invalidation signaling only: `SharedKernel.Caching.Redis.Core` + `SharedKernel.Caching.Redis.PubSub`

**Layering rules:**

- (Phase 17) `SharedKernel.Caching.Redis` and `SharedKernel.Caching.FusionCache` are sibling provider packages at the same layer. They must never reference each other. Both depend only on `SharedKernel.Caching.Abstractions` (`.Redis` also depends on `.Redis.Core`).
- (Phase 32) `SharedKernel.Caching.Redis.Core` has zero references to FusionCache, RedLock.net, or any capability-specific (hash/channel/lock) type — it knows about Redis connections and resilience only.
- (Phase 32) `SharedKernel.Caching.Redis`, `.DistributedLocking`, `.HashStore`, and `.PubSub` are sibling packages that all depend on `.Redis.Core` but **must never reference each other**. A consumer can take any subset of these four without pulling in the others.

### Why pub/sub stays in `02.Caching` (not `07.Messaging`)

`IRedisChannelService` and `ICacheInvalidationBus` (and their Phase 36 home, `SharedKernel.Caching.Redis.PubSub`) carry a **deliberately weaker contract** than `07.Messaging`:

- **At-most-once, no durability.** Redis Pub/Sub messages are not persisted — an offline subscriber misses the message permanently. There is no outbox, no retry, no dead-letter queue.
- **No ordering or delivery guarantees across restarts.** `07.Messaging` (`SharedKernel.Messaging.Abstractions` / `.MassTransit`) provides outbox-backed, retryable, ordered delivery via RabbitMQ/ASB.
- **Purpose-bound to cache coherence.** These types exist solely to propagate cache invalidation signals and lightweight ephemeral broadcasts between instances of the *same* cache topology — not general inter-service event communication.

Moving these types into `07.Messaging` would create two problems: (1) it would violate the layering rule that `07.Messaging` may reference `01–04` only — `CacheInvalidationMessage` and `CachingCoreOptions` are `02.Caching` types, and `07.Messaging` must not depend on `02.Caching`; and (2) developers would reasonably (but incorrectly) assume anything housed in `07.Messaging` inherits its durability guarantees, creating a latent reliability trap. Keeping `IRedisChannelService`/`ICacheInvalidationBus` in `02.Caching` (now `SharedKernel.Caching.Redis.PubSub`) — with the XML-doc boundary statement preserved verbatim — keeps the weaker contract visually and architecturally distinct from `07.Messaging`'s guarantees.

---

## Technology Stack

| Concern | Technology | Confirmed Version | Owning Package |
| ------- | ---------- | ----------------- | --------------- |
| L1 cache (in-process) | `ZiggyCreatures.FusionCache` | 2.6.0 | `.FusionCache` |
| L2 cache (distributed) | `ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis` | 2.6.0 | `.Redis` |
| STJ serialization for FusionCache | `ZiggyCreatures.FusionCache.Serialization.SystemTextJson` | 2.6.0 | `.FusionCache` |
| Redis connection + resilience core | `StackExchange.Redis` | 2.13.1 | `.Redis.Core` (Phase 32) |
| Distributed locking | `RedLock.net` (over Redis) | 2.3.2 | `.Redis.DistributedLocking` (implemented, Phase 34) |
| DI abstractions | `Microsoft.Extensions.DependencyInjection.Abstractions` | **10.0.1** (not 10.0.0 — FusionCache transitive floor) | all packages |
| OTel tracing (invalidation receiver) | `System.Diagnostics.DiagnosticSource` | BCL in `net10.0` — no additional NuGet reference | `.Redis.PubSub` (Phase 36) |
| Redis circuit breaker (opt-in) | `Polly.Core` | 8.5.2 | `.Redis.Core` (Phase 32) |
| Background hosting (invalidation receiver) | `Microsoft.Extensions.Hosting.Abstractions` | 10.0.0 | `.Redis.PubSub` (Phase 36) |

> **Note:** "Owning Package" reflects the **final, achieved topology** as of Phase 36. `StackExchange.Redis` and `Polly.Core`'s *registration call sites* (`AddRedisConnection`, `AddRedisCircuitBreaker`) live in `.Redis.Core`; `RedLock.net` is confined to `.Redis.DistributedLocking` (Phase 34); `RedisHashService`/`TypedHashStore<T>` live in `.Redis.HashStore` (Phase 35); `RedisChannelService`/`RedisCacheInvalidationBus`/`CacheInvalidationReceiver` and their `Microsoft.Extensions.Hosting.Abstractions` dependency live in `.Redis.PubSub` (Phase 36). `SharedKernel.Caching.Redis` no longer carries direct `PackageReference`s to `StackExchange.Redis`, `Polly.Core`, `RedLock.net`, or `Microsoft.Extensions.Hosting.Abstractions` — its only Redis-related dependencies are transitive, via `.Redis.Core` and the FusionCache Redis backplane packages.

---

## Interface Contracts

### `SharedKernel.Caching.Abstractions` — full public surface

```text
ICacheService
    GetAsync<T>(string key, CancellationToken ct)                                          → ValueTask<T?>
    SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct)            → ValueTask
    GetOrSetAsync<T>(string key, Func<CancellationToken,ValueTask<T>> factory,
                     CachePolicy policy, CancellationToken ct)                             → ValueTask<T>
    RemoveAsync(string key, CancellationToken ct)                                          → ValueTask
    RemoveByTagAsync(string tag, CancellationToken ct)                                     → ValueTask

    NOTE (Phase 21 — breaking change): factory delegate changed from Task<T> to ValueTask<T>.
    C# constraint: a separate nullable overload is not possible because Func<CT,ValueTask<T>> and
    Func<CT,ValueTask<T?>> are the same CLR type. Negative-result caching is achieved by calling
    GetOrSetAsync<T?> (e.g. GetOrSetAsync<string?>) — the same single generic method handles
    both nullable and non-nullable cases. FusionCache caches null as a genuine entry when T is
    nullable. Migration: Task<T> callers wrap with async ct => await oldFactory(ct).

CachePolicy  (sealed immutable record)
    .Default                              → 5 min L1, 30 min L2, fail-safe on
    .NeverExpire                          → TimeSpan.MaxValue L1 + L2, fail-safe on, no EagerRefreshThreshold
                                            Use only for truly static data; invalidation must be explicit via
                                            ICacheService.RemoveAsync or ICacheInvalidationBus.
    .For(TimeSpan l1, TimeSpan l2)
    .WithTags(params string[] tags)
    .WithEagerRefresh(double threshold)   → default 0.9 (90 % of TTL)

CachingCoreOptions  (sealed class — added Phase 17)
    ServiceName  string  (default "app")
    Authoritative source of ServiceName for all provider packages.
    Registered in DI by AddSharedKernelCaching; consumed by RedisCacheInvalidationBus
    and CacheInvalidationReceiver so the Redis package has no dependency on FusionCache.

ICacheKeyProvider
    BuildKey(string entity, string id, params string[] extraSegments) → string
    // Format contract: {service}:{entity}:{id}[:{extraSegment}...]

IDistributedLockService
    AcquireAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry, CancellationToken ct)
    → IAsyncDisposable?   (null = lock not acquired within wait; callers decide fallback)

IRedisChannelService                      [ephemeral / non-durable — see rule below]
    PublishAsync(string channel, string message, CancellationToken ct)   → ValueTask
    SubscribeAsync(string channel, Func<string, ValueTask> handler, CancellationToken ct) → ValueTask
    UnsubscribeAsync(string channel, CancellationToken ct)               → ValueTask

IRedisHashService
    GetFieldAsync<T>(string key, string field, JsonTypeInfo<T> typeInfo, CancellationToken ct)        → ValueTask<T?>
    SetFieldAsync<T>(string key, string field, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct) → ValueTask
    GetAllFieldsAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken ct) → ValueTask<IReadOnlyDictionary<string, T>>
    DeleteFieldAsync(string key, string field, CancellationToken ct)                  → ValueTask
    IncrementFieldAsync(string key, string field, long delta, CancellationToken ct)   → ValueTask<long>

ICacheInvalidationBus                     [no delivery guarantees — see rule below]
    PublishKeyInvalidationAsync(string[] keys, CancellationToken ct)     → ValueTask
    PublishTagInvalidationAsync(string[] tags, CancellationToken ct)     → ValueTask
    PublishBroadcastInvalidationAsync(CancellationToken ct)              → ValueTask
    PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct) → ValueTask

CacheInvalidationMessage  (sealed record)
    SourceService    string
    InvalidationType CacheInvalidationType  (Key | Tag | All)
    Keys             string[]?
    Tags             string[]?
    CorrelationId    string   (defaults to Activity.Current?.Id ?? Guid.NewGuid().ToString("N"))
    TimestampUtc     DateTimeOffset

ITypedHashStore<T>                        [AOT-safe typed wrapper — no per-call JsonTypeInfo<T>]
    GetFieldAsync(string key, string field, CancellationToken ct)                           → ValueTask<T?>
    SetFieldAsync(string key, string field, T value, CancellationToken ct)                  → ValueTask
    GetAllFieldsAsync(string key, CancellationToken ct)          → ValueTask<IReadOnlyDictionary<string, T>>
    DeleteFieldAsync(string key, string field, CancellationToken ct)                        → ValueTask
    IncrementFieldAsync(string key, string field, long delta, CancellationToken ct)         → ValueTask<long>

ICachingBuilder
    Services  IServiceCollection { get; }

    -- Planned additions (WO-007) --

ICacheService  [additions in Phase 22]
    GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct)
    → ValueTask<IReadOnlyDictionary<string, T?>>
    // Every requested key has an entry; missing keys map to null.

    SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct)
    → ValueTask
    // Single CachePolicy applies to all entries in the batch.

IDistributedLockService  [addition in Phase 23]
    AcquireRenewableAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry,
                          CancellationToken ct)                → ValueTask<IRenewableLock?>

IRenewableLock  [new in Phase 23 — SharedKernel.Caching.Abstractions]
    RenewAsync(CancellationToken ct)                           → ValueTask<bool>
    // true = lock still held; false = lock lost (never throws for lost lock)
    IsAcquired                                                  bool { get; }
    // Extends IAsyncDisposable.

CachePolicy  [additions in Phases 24 and 25]
    .Sliding(TimeSpan window)         → CachePolicy with SlidingWindow set
    .WithVersion(int version)         → CachePolicy with KeyVersion set
    SlidingWindow                     TimeSpan?  (null = absolute TTL only)
    KeyVersion                        int        (0 = no suffix, backward-compat)

    NOTE (Phase 24): SlidingWindow maps to L1 SlidingExpiration only — L2 does not slide.
    NOTE (Phase 24): NeverExpire + SlidingWindow combination is invalid (guard in BuildEntryOptions).
    NOTE (Phase 25): KeyVersion 0 produces the existing key format with no change.
                     KeyVersion > 0 appends :v{version} suffix: {service}:{entity}:{id}:v{version}

ICacheKeyProvider  [addition in Phase 25]
    BuildKey(string entity, string id, int version, params string[] extraSegments) → string
    // When version > 0 appends :v{version}. Version 0 = original format unchanged.

ConnectionHealthState  [new enum in Phase 26 — SharedKernel.Caching.Abstractions]
    Connected | Reconnecting | Disconnected

IRedisChannelService  [addition in Phase 26]
    ConnectionHealth                  ConnectionHealthState { get; }

ICacheWarmupStrategy  [new in Phase 28 — SharedKernel.Caching.Abstractions]
    Name                              string { get; }
    Order                             int { get; }
    WarmupAsync(ICacheService cache, CancellationToken ct) → ValueTask

ITenantCacheKeyProvider  [new in Phase 29 — SharedKernel.Caching.Abstractions]
    // Extends ICacheKeyProvider.
    BuildTenantKey(string tenantId, string entity, string id, params string[] extraSegments) → string
    // Format: {service}:{tenant}:{entity}:{id}[:{extra}...]
    // tenantId is always an explicit parameter — never resolved from ambient context.
    // Zero dependency on 12.Security or IHttpContextAccessor.
```

### `SharedKernel.Caching.Redis.Core` — public surface (implemented, Phase 32)

> These types are **not** part of `SharedKernel.Caching.Abstractions` — they are concrete connection/resilience primitives shared by `.Redis`, `.DistributedLocking`, `.HashStore`, and `.PubSub`. No `Abstractions` contract changes in WO-023.

```text
RedisConnectionOptions  [sealed class — Phase 32]
    ConnectionString    string  ([Required])
    ConnectTimeoutMs     int     ([Range(100,60000)], default 5000)

RedisCircuitBreakerOptions  [sealed class — Phase 32; generalized from Ph.30 RedisL2Options.CircuitBreakerOptions]
    Enabled             bool          (default false)
    FailureThreshold    int           ([Range(1,int.MaxValue)], default 5)
    SamplingDuration    TimeSpan      (default 10s)
    BreakDuration       TimeSpan      (default 30s)
    MinimumThroughput   int           ([Range(1,int.MaxValue)], default 3)

RedisConnectionHealthTracker  [sealed class — Phase 32]
    ConnectionHealth    ConnectionHealthState { get; }
    // Wraps IConnectionMultiplexer.ConnectionRestored / ConnectionFailed.
    // internal OnConnectionRestored / OnConnectionFailed exposed via InternalsVisibleTo for test simulation.
    // Passive health observer only — no resubscription/replay logic (that remains in
    // RedisChannelService / .Redis.PubSub, Phase 36).

AddRedisConnection(this IServiceCollection, string connectionString,
                    Action<RedisConnectionOptions>? configure = null)  → IServiceCollection
    // TryAddSingleton<IConnectionMultiplexer> (first caller wins) + TryAddSingleton<RedisConnectionHealthTracker>

AddRedisCircuitBreaker(this IServiceCollection, Action<RedisCircuitBreakerOptions>? configure = null)
    → IServiceCollection
    // TryAddSingleton<ResiliencePipeline> only when Enabled = true (FailureRatio=1.0 / MinimumThroughput pattern)
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
- `ServiceName` is sourced from `IOptions<CachingCoreOptions>` (Abstractions) — not from `CachingOptions` (FusionCache). This is enforced after Phase 17.

### RedisChannelService rules

- Always use `RedisChannel.Literal(channelName)` — never `RedisChannel.Pattern`.
- All handler exceptions must be caught and logged at `LogLevel.Error`. They must never propagate to the Redis subscriber thread.

### FusionCache STJ serializer AOT rule

- `CachingOptions.SerializerContext` must be set to the microservice's source-generated `JsonSerializerContext` in any NativeAOT build.
- When set, `AddSharedKernelCaching` passes `JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine(SerializerContext, CacheInvalidationMessageJsonContext.Default) }` to `WithSystemTextJsonSerializer()`.
- When absent, FusionCache falls back to reflection-based STJ — acceptable for non-AOT builds, breaks NativeAOT.
- `AddRedisL2` must **not** call `.WithSystemTextJsonSerializer()` — this silently overwrites the options-aware registration with a reflection-based default (fixed in Phase 18). `AddRedisL2` uses `.WithRegisteredSerializer()` so FusionCache picks up the DI-registered `IFusionCacheSerializer`.
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

### DI startup guard rules (Phase 19)

- `AddRedisChannelService` throws `InvalidOperationException` if `IConnectionMultiplexer` is not registered: `"AddRedisChannelService requires AddRedisL2 or AddRedisDistributedLocking to be called first to register IConnectionMultiplexer."`
- `AddCacheInvalidationReceiver` throws `InvalidOperationException` if `IRedisChannelService` is not registered: `"AddCacheInvalidationReceiver requires AddRedisChannelService to be called first."`
- `AddCacheInvalidationReceiver` throws `InvalidOperationException` if `ICacheService` is not registered: `"AddCacheInvalidationReceiver requires AddSharedKernelCaching to be called first to register ICacheService."`
- `AddRedisDistributedLocking` canonical overload is on `ICachingBuilder` (returns `ICachingBuilder` for fluent chaining). The `IServiceCollection` overload is `[Obsolete]` and delegates to the builder overload.
- Guard checks use `services.Any(sd => sd.ServiceType == typeof(...))` at registration time.

### L1SizeLimit rule (Phase 20)

- `CachingOptions.L1SizeLimit` is wired to FusionCache's `MemoryCacheOptions.SizeLimit` via `.WithMemoryCache(_ => new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit }))`. Every cache entry has `Size = 1` in both the default entry options and `BuildEntryOptions`, so `L1SizeLimit` is an **entry count** limit, not a byte limit.
- Default is 10,000 entries. Services in K8s with strict memory limits must set this explicitly.
- **Verified L2 key format (Phase 20):** `{KeyPrefix}v2:{user-key}`. The `v2:` schema-version separator is injected by `Microsoft.Extensions.Caching.StackExchangeRedis` (v10+) between the `InstanceName` (= `KeyPrefix`) and the user key. It is not added by FusionCache or SharedKernel. When `KeyPrefix` is empty the key is `v2:{user-key}`. This behavior is confirmed by integration test via raw `KeyExistsAsync` inspection against a live Redis container.

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
- Both `AddRedisL2` and `AddRedisDistributedLocking` register `IConnectionMultiplexer` as a singleton (via `TryAddSingleton` — first caller wins). Either call satisfies the guard in `AddRedisHashService` and `AddRedisChannelService`.

### Batch operations rules (Phase 22)

- `GetManyAsync<T>` always returns a dictionary entry for every requested key — missing keys map to `null`. An empty input enumerable returns an empty dictionary.
- `SetManyAsync<T>` applies a single `CachePolicy` to all entries — there is no per-key policy in a batch call.
- `IRedisL2BatchService` is `internal` to `SharedKernel.Caching.Redis` — it must never be registered in DI as a public service or leak into abstractions.
- The FusionCache implementation loops over keys (FusionCache has no native batch API) — this is correct given FusionCache's stampede-protection model per key.
- `FakeCacheService` in `16.Testing` must implement both batch methods.

### Renewable lock rules (Phase 23)

- `IRenewableLock.RenewAsync` returns `false` for a lost lock — it must never throw.
- `IRenewableLock.IsAcquired` transitions to `false` after `DisposeAsync` and after renewal returns `false`.
- `KeepAliveAsync` is a static extension method on `IRenewableLock` in `SharedKernel.Caching.Redis`; the caller owns cancellation.
- `AcquireRenewableAsync` returns `null` if the lock cannot be acquired within `wait` — consistent with `AcquireAsync` semantics.
- RedLock.net 2.3.2 has **no public `ExtendAsync`**. `RedLockRenewableLock` uses re-acquisition: dispose the old lock first, then `CreateLockAsync` on the same resource. This has a brief unprotected window but is the only option on a single-node Redis without a public extend API. Do not search for `ExtendAsync` on `IRedLock` — it does not exist in the public surface.

### Sliding expiration rules (Phase 24)

- `CachePolicy.SlidingWindow` maps to L1 `SlidingExpiration` only — L2 in Redis does not support sliding expiry; L2 uses the absolute `L2Duration` as a ceiling.
- `CachePolicy.NeverExpire` and a non-null `SlidingWindow` together must throw `InvalidOperationException("CachePolicy.Sliding is incompatible with CachePolicy.NeverExpire.")` in `FusionCacheService.BuildEntryOptions`.
- The guard lives in `BuildEntryOptions` (FusionCache package), not in `CachePolicy` construction (Abstractions) — Abstractions must remain free of runtime validation logic.

### Key versioning rules (Phase 25)

- `CachePolicy.KeyVersion` default is `0`. Version `0` produces no suffix change — identical key format to today. This is backward-compatible.
- Version suffix format: `:v{version}` appended after all other segments. Example: `{service}:{entity}:{id}:v3`.
- `ICacheService` method signatures do NOT change — key versioning is the caller's responsibility via `ICacheKeyProvider.BuildKey`. The version is baked into the key string.
- `ICacheKeyProvider` gains a new `BuildKey` overload with `int version` parameter. Both overloads must be implemented by `CacheKeyProvider`.

### Channel reconnect rules (Phase 26)

- `RedisChannelService` subscribes to `IConnectionMultiplexer.ConnectionRestored` and `ConnectionFailed` in its constructor.
- On `ConnectionRestored`: acquire the registry lock atomically for the full replay loop; resubscribe all channels; log per-channel failures at `Error` without aborting remaining channels; transition `ConnectionHealth` to `Connected` before the replay.
- On `ConnectionFailed`: check `IConnectionMultiplexer.IsConnected`; set `Connected` if still connected, `Reconnecting` if not.
- `ConnectionHealthState` enum lives in `SharedKernel.Caching.Abstractions` — it is a plain enum with no infrastructure dependencies.
- `IRedisChannelService.ConnectionHealth` property is intended for health check consumption; stated in XML doc.
- **Registry implementation**: `Dictionary<string, SubscriptionEntry>` + `object _registryLock` (replaces `ConcurrentDictionary`). The lock is held for the entire replay loop — prevents a concurrent `SubscribeAsync` from inserting a channel mid-replay. `SubscribeAsync` and `UnsubscribeAsync` both acquire `_registryLock`.
- `_connectionHealth` is a `volatile int` field (cast to/from `ConnectionHealthState`) — AOT-safe, single-writer event callbacks, reads without a lock.
- `OnConnectionRestored` and `OnConnectionFailed` are `internal` — exposed via `InternalsVisibleTo` so test projects can simulate events without a live multiplexer.

### CachingCoreOptions standalone DI rules (Phase 27)

- `AddCachingCoreOptions(this IServiceCollection, Action<CachingCoreOptions>)` lives in `SharedKernel.Caching.Abstractions`.
- It has zero dependency on FusionCache, StackExchange.Redis, or any provider package.
- `AddSharedKernelCaching` behavior is unchanged — services calling it do not need `AddCachingCoreOptions`.
- `AddRedisCacheInvalidationBus` logs `LogLevel.Warning` at startup if `ServiceName` is still the default `"app"`.

### Cache warmup rules (Phase 28)

- `ICacheWarmupStrategy.WarmupAsync` uses `ValueTask` — consistent with the rest of the domain.
- `CacheWarmupHostedService` catches per-strategy exceptions, logs at `Error`, and continues — a failed strategy must never crash the pod.
- `AddCacheWarmup<TStrategy>` uses `TryAddEnumerable` for strategy registration (supports multiple strategies) and guards `CacheWarmupHostedService` registration to be idempotent.
- `WaitForWarmup = true` in `CachingOptions` delays readiness via `IHostedLifecycle` (verify .NET 10 API at implementation time).

### Multi-tenant cache key rules (Phase 29)

- `ITenantCacheKeyProvider.BuildTenantKey` key format: `{service}:{tenant}:{entity}:{id}[:{extra}...]`.
- `tenantId` is always an explicit parameter — `ITenantCacheKeyProvider` never resolves tenant from HTTP context or ambient state.
- `ITenantCacheKeyProvider` has zero dependency on `12.Security` or `IHttpContextAccessor`.
- `AddTenantCacheKeyProvider` does NOT replace the existing `ICacheKeyProvider` registration.

### Polly circuit breaker rules (Phase 30)

- `RedisL2Options.CircuitBreaker.Enabled` defaults to `false` — all existing behavior is preserved when disabled.
- Use `Polly.Core` v8 only — do not add `Microsoft.Extensions.Http.Resilience`. Confirmed AOT-compatible at version 8.5.2.
- The circuit breaker `ResiliencePipeline` is registered as a singleton only when `Enabled = true`.
- FusionCache's own fail-safe is not replaced — the Polly circuit breaker is complementary (short-circuits before the timeout accumulates).
- **Count-based semantics via ratio API:** Polly v8 uses ratio-based circuit breaking (`FailureRatio` 0.0–1.0 + `MinimumThroughput`). To emulate count-based behaviour: set `FailureRatio = 1.0` and `MinimumThroughput = CircuitBreaker.FailureThreshold`. This means "all calls in the window must fail AND the threshold count must be reached."
- **`BreakDuration` minimum:** Polly v8 enforces a minimum `BreakDuration` of `500ms`. The default of 30 s is safe; test code using shorter durations must use `TimeSpan.FromMilliseconds(500)` as the minimum.
- **Optional DI injection:** `ResiliencePipeline` is resolved via `sp.GetService<ResiliencePipeline>()` (not `GetRequiredService`) in `RedisHashService` and `RedisChannelService` factory registrations. When not registered (disabled), the value is `null` and services operate without Polly overhead.
- The FusionCache Redis backplane does not use the circuit breaker — FusionCache's own fail-safe covers L2 unavailability at that level.

### OTel metrics rules (Phase 31)

- `Meter` name is `"SharedKernel.Caching"` (version `"1.0"`). Static readonly field — created once, AOT-safe.
- `cache.key_prefix` tag value is `{service}:{entity}` (first two key segments only) — never the full key with `{id}` (high-cardinality).
- No new NuGet dependencies — `System.Diagnostics.Metrics` is in the BCL for `net10.0`.
- Use FusionCache events API (`IFusionCache.Events.Memory.*`) for hit/miss/eviction where available — prefer events over call-site instrumentation to avoid duplication.
- Do not add `Enabled` guards around instrument recording — the BCL handles the no-listener fast path internally.

### Redis Connection Core rules (Phase 32)

- `SharedKernel.Caching.Redis.Core` is the **single registration point** for `IConnectionMultiplexer`. `AddRedisConnection(this IServiceCollection, connectionString, configure?)` calls `services.TryAddSingleton<IConnectionMultiplexer>(...)` — first caller wins, identical semantics to the pre-Phase-32 inline registrations in `AddRedisL2` and `AddRedisDistributedLocking`.
- `RedisConnectionHealthTracker` is a **passive** health observer (`ConnectionHealthState` via `ConnectionRestored`/`ConnectionFailed`). It performs no resubscription or replay logic — that remains specific to `RedisChannelService` (`.Redis.PubSub`, Phase 36). Both may independently subscribe to the same multiplexer's events; StackExchange.Redis supports multiple subscribers without conflict.
- `ConnectionHealthState` is **not** duplicated in `.Redis.Core` — it is sourced from `SharedKernel.Caching.Abstractions` (Phase 26), where it already lives.
- `RedisCircuitBreakerOptions` is the generalized, top-level form of Phase 30's `RedisL2Options.CircuitBreakerOptions` — same five properties, same defaults, same `[Range]` validation. `AddRedisCircuitBreaker(this IServiceCollection, configure?)` registers `ResiliencePipeline` via `TryAddSingleton` only when `Enabled = true`, using the FailureRatio=1.0/MinimumThroughput pattern (Phase 30, unchanged).
- `.Redis.Core` extensions (`AddRedisConnection`, `AddRedisCircuitBreaker`) are plain `IServiceCollection` extensions — not `ICachingBuilder`. Fluent `ICachingBuilder` chaining is the responsibility of the consuming packages (`.Redis`, `.DistributedLocking`, `.HashStore`, `.PubSub`), each of which wraps these calls internally.
- `.Redis.Core` has zero references to FusionCache, RedLock.net, or any capability-specific (hash/channel/lock) type.

### Redis distributed locking extraction rules (Phase 34)

- `SharedKernel.Caching.Redis.DistributedLocking` depends only on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` — no reference to `SharedKernel.Caching.Redis`, `.HashStore`, or `.PubSub`.
- `AddRedisDistributedLocking(this ICachingBuilder, string connectionString, Action<RedisLockOptions>? configure = null)` is the canonical signature, unchanged from pre-extraction. Internally it calls `services.AddRedisConnection(connectionString, o => o.ConnectTimeoutMs = options.ConnectTimeoutMs)` to source `IConnectionMultiplexer` from `.Redis.Core` (`TryAddSingleton` — first-caller-wins).
- The `[Obsolete]` `AddRedisDistributedLocking(this IServiceCollection, ...)` shim is preserved and delegates to the canonical `ICachingBuilder` overload via a private `internal sealed class RedisLockCachingBuilder(IServiceCollection services) : ICachingBuilder` adapter — this adapter is **this package's own copy**, not shared with `.Redis` or any sibling package.
- `RedLockFactory` registration and `IDistributedLockFactory` exposure are unchanged and stay in this package.
- Renewable lock re-acquisition strategy (dispose-then-recreate, RedLock.net 2.3.2 has no public `ExtendAsync`) is preserved exactly — see Phase 23 rule above, which still applies verbatim to `RedLockRenewableLock` in its new location.
- `KeepAliveAsync` remains a static extension method on `IRenewableLock`, now in `SharedKernel.Caching.Redis.DistributedLocking.Extensions`.
- `FakeDistributedLockService`/`FakeRenewableLock` remain in `16.Testing` — they were not moved and are referenced by this package's test project via `ProjectReference`.

### Redis Hash Store extraction rules (Phase 35)

- `SharedKernel.Caching.Redis.HashStore` depends only on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` — no reference to `SharedKernel.Caching.Redis`, `.DistributedLocking`, or `.PubSub`.
- `RedisHashService` (`internal sealed`, `IRedisHashService`) and `TypedHashStore<T>` (`internal sealed`, `ITypedHashStore<T>`) are pure namespace-rename relocations from `SharedKernel.Caching.Redis` — no behavioral changes to either type's method bodies.
- `AddRedisHashService(this ICachingBuilder)` is the canonical signature, unchanged from pre-extraction (no connection-string parameter — it never owned multiplexer registration). Its startup guard now reads: `"AddRedisHashService requires AddRedisConnection (directly, or transitively via AddRedisL2 / AddRedisDistributedLocking / AddRedisChannelService) to be called first to register IConnectionMultiplexer."`
- `AddRedisHashService` resolves the optional `ResiliencePipeline` via `sp.GetService<ResiliencePipeline>()` (Phase 30 pattern, unchanged) — now sourced from `.Redis.Core`'s `AddRedisCircuitBreaker` rather than from `AddRedisL2`'s inline registration.
- `AddTypedHashStore<T>(this ICachingBuilder, JsonTypeInfo<T> typeInfo)` is unchanged — still guards on `IRedisHashService` being registered first (`"AddTypedHashStore<T> requires AddRedisHashService to be called first."`).
- No `[Obsolete]` shim was needed — `AddRedisHashService`/`AddTypedHashStore<T>` were already `ICachingBuilder`-only before this phase (added in Phase 15/19, post-dating the `IServiceCollection`-era APIs that needed shims in Phase 34).
- **Hidden cross-package test coverage**: extraction phases must grep the *donor* package's test projects for references to the extension methods being moved, not just the File-Level Plan's listed test files — `.Redis.Tests`'s `RedisDiRegistrationTests.cs` and `CircuitBreakerTests.cs` both contained `AddRedisHashService`-related tests that were not in the Phase 35 File-Level Plan. These were removed from `.Redis.Tests` and their coverage (DI registration, startup guard message, `ResiliencePipeline` injection) was recreated in the new package's `RedisHashServiceDiTests.cs`.

### Redis Pub/Sub extraction rules (Phase 36)

- `SharedKernel.Caching.Redis.PubSub` depends only on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` — no reference to `SharedKernel.Caching.Redis`, `.DistributedLocking`, or `.HashStore`.
- `RedisChannelService` (`internal sealed`, `IRedisChannelService`), `RedisCacheInvalidationBus` (`internal sealed`, `ICacheInvalidationBus`), and `CacheInvalidationReceiver` (`BackgroundService`) are pure namespace-rename relocations from `SharedKernel.Caching.Redis` — no behavioral changes to any type's method bodies.
- The Phase 26 reconnect/resubscribe replay logic (`Dictionary<string, SubscriptionEntry>` + `_registryLock`, `volatile int _connectionHealth`, `OnConnectionRestored`/`OnConnectionFailed`) relocates **intact** with `RedisChannelService` — it is not refactored to depend on `.Redis.Core`'s `RedisConnectionHealthTracker`. The two health-tracking mechanisms coexist independently (see Phase 32 rule above).
- `AddRedisChannelService(this ICachingBuilder)`, `AddRedisCacheInvalidationBus(this ICachingBuilder)`, and `AddCacheInvalidationReceiver(this ICachingBuilder)` are the canonical signatures, unchanged from pre-extraction. `AddRedisChannelService`'s startup guard now reads: `"AddRedisChannelService requires AddRedisConnection (directly, or transitively via AddRedisL2 / AddRedisDistributedLocking / AddRedisHashService) to be called first to register IConnectionMultiplexer."`
- `AddRedisChannelService` resolves the optional `ResiliencePipeline` via `sp.GetService<ResiliencePipeline>()` (Phase 30 pattern, unchanged) — now sourced from `.Redis.Core`'s `AddRedisCircuitBreaker` rather than from `AddRedisL2`'s inline registration.
- No `[Obsolete]` shim was needed — `AddRedisChannelService`/`AddRedisCacheInvalidationBus`/`AddCacheInvalidationReceiver` were already `ICachingBuilder`-only before this phase.
- **Hidden cross-package test coverage** (3rd occurrence of this pattern): `.Redis.Tests`'s `CachingCoreOptionsDiTests.cs` (both CO-04 generic `AddCachingCoreOptions` tests and CO-03 `RedisCacheInvalidationBus`-internal-type-dependent tests), `DI/DiErgonomicsGuardTests.cs` (pub/sub DI guard tests), and `DI/RedisDiRegistrationTests.cs` (`AddRedisChannelService` registration tests) were full-file relocations not called out in the File-Level Plan, plus one test in `CircuitBreakerTests.cs` (`AddRedisChannelService_WithCircuitBreakerEnabled_InjectsResiliencePipeline`). These were removed from `.Redis.Tests`; the `CircuitBreakerTests.cs` coverage was recreated as `RedisChannelServiceDiTests.cs` in the new package, modeled on Phase 35's `RedisHashServiceDiTests.cs`.
- **DI integration tests that previously called `AddSharedKernelCaching`/`AddRedisL2`** (which would violate the sibling-package no-cross-reference rule) were rewritten to use `AddCachingCoreOptions` (from `SharedKernel.Caching.Abstractions`, zero FusionCache/Redis dependency) + `AddRedisConnection` (from `.Redis.Core`) + `services.AddSingleton<ICacheService, FakeCacheService>()` (`SharedKernel.Testing.Caching`, satisfies the `AddCacheInvalidationReceiver` `ICacheService` guard) + `TestCachingBuilder` wrapping `services` for the fluent `ICachingBuilder` chain. This is the established pattern for any future PubSub.Tests DI integration test that needs an `ICacheService`.

### Redis package extraction rules (Phases 33–36)

- **Source of truth for `IConnectionMultiplexer` and `ResiliencePipeline`:** every `Add*` extension that previously self-registered `IConnectionMultiplexer` (`AddRedisL2`, `AddRedisDistributedLocking`) now calls `services.AddRedisConnection(connectionString, ...)` (Phase 32) internally. Extensions that don't take a connection string (`AddRedisHashService`, `AddRedisChannelService`) retain their existing `services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer))` startup guard, with the error message updated to point at `AddRedisConnection` (directly, or transitively via `AddRedisL2` / `AddRedisDistributedLocking` / `AddRedisChannelService`) as the registration source.
- **No new `IConnectionMultiplexer` registrations** are permitted outside `.Redis.Core`'s `AddRedisConnection`. Any `Add*` extension in `.Redis`, `.DistributedLocking`, `.HashStore`, or `.PubSub` that needs a multiplexer must call `AddRedisConnection` (idempotent via `TryAddSingleton`) — never `ConnectionMultiplexer.Connect(...)` directly.
- **Sibling packages never reference each other.** `.Redis`, `.DistributedLocking`, `.HashStore`, and `.PubSub` each depend only on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core`. A consumer can take any subset without transitively pulling in the others (e.g., a session-store service takes `.HashStore` without `.DistributedLocking` or FusionCache).
- **All public DI extension signatures are preserved exactly.** `AddRedisL2(connectionString, configure?)`, `AddRedisDistributedLocking(connectionString, configure?)`, `AddRedisHashService()`, `AddTypedHashStore<T>(JsonTypeInfo<T>)`, `AddRedisChannelService()`, `AddRedisCacheInvalidationBus()`, `AddCacheInvalidationReceiver()` — no signature changes, no new required parameters. `RedisL2Options.CircuitBreaker` changes type from a locally-nested class to the top-level `RedisCircuitBreakerOptions` (Phase 32) — a type relocation, not a rename; `o.CircuitBreaker.Enabled = true` continues to compile.
- **`[Obsolete]` `IServiceCollection` shims**: each extraction package that retains an `[Obsolete]` `IServiceCollection`-overload shim (e.g., `.DistributedLocking` for `AddRedisDistributedLocking`) defines its **own** small `internal sealed class ...CachingBuilder(IServiceCollection services) : ICachingBuilder` adapter. Do not share this adapter across packages via a project reference — each copy is ~3 lines and the duplication avoids an otherwise-unjustified inter-package dependency.
- **`SharedKernel.Caching.Abstractions` contracts are unchanged** across Phases 33–36: `ICacheService`, `CachePolicy`, `ICacheKeyProvider`, `IDistributedLockService`, `IRenewableLock`, `IRedisChannelService`, `IRedisHashService`, `ITypedHashStore<T>`, `ICacheInvalidationBus`, `CacheInvalidationMessage`, `ConnectionHealthState`, `CachingCoreOptions`, `ICachingBuilder` all remain as documented above — only their implementations and DI registration call sites move.
- **End state (achieved, Phase 36):** `SharedKernel.Caching.Redis` contains only `AddRedisL2`, `RedisL2Options`, `IRedisL2BatchService` (internal), and the FusionCache Redis backplane wiring (`AddStackExchangeRedisCache` + `AddFusionCache().WithRegisteredDistributedCache().WithRegisteredSerializer().WithStackExchangeRedisBackplane(...)`).
- **Reconnect/resubscribe replay logic** (Phase 26: `Dictionary<string, SubscriptionEntry>` + `_registryLock`, `volatile int _connectionHealth`, `OnConnectionRestored`/`OnConnectionFailed`) relocated **intact** with `RedisChannelService` into `.Redis.PubSub` (Phase 36) — it was not refactored to depend on `.Redis.Core`'s `RedisConnectionHealthTracker`. The two health-tracking mechanisms coexist independently (see Phase 32 rule above).

### AOT compatibility

- FusionCache is AOT-compatible as of v1.x — verify release notes on every upgrade.
- StackExchange.Redis is AOT-compatible — avoid dynamic configuration patterns. `SharedKernel.Caching.Redis.Core` (Phase 32) is the sole owner of `ConfigurationOptions.Parse` / `ConnectionMultiplexer.Connect` — no dynamic configuration patterns introduced there.
- RedLock.net — verify AOT status on each major upgrade and wrap if needed. Confined to `SharedKernel.Caching.Redis.DistributedLocking` (implemented, Phase 34) — AOT risk does not propagate to `.Redis`, `.HashStore`, or `.PubSub`.
- `IRedisHashService` typed methods use `JsonTypeInfo<T>` parameter to remain AOT-safe.
- `CacheInvalidationMessage` uses a source-generated `JsonSerializerContext` — no reflection.
- `AddRedisL2` must not re-register the STJ serializer — doing so silently breaks NativeAOT by overwriting the user-configured `SerializerContext` (Phase 18 fix).

### Logging (EventId sub-blocks — Phase 37, WO-041, P-252)

`02.Caching` reserves `LoggingEventIdRanges.Caching` (2000-2999, from `SharedKernel.Primitives` — `01.Core` P-249) as its platform-wide `EventId` block. This domain has the most logging call sites of any capability domain and, before Phase 37, the only confirmed live cross-package `EventId` collision in the platform (`Redis.Core` vs. `Redis.PubSub`, both at 4001/4002) plus two confirmed cross-domain squats (`FusionCache` on `01.Core`'s 1000-1999 block, `Redis.PubSub`'s `RedisChannelService` on `03.Domain`'s 3000-3999 block). The table below is this domain's **authoritative** 100-wide-per-package sub-block allocation, assigned in Packages-table declaration order — it supersedes `01.Core/CLAUDE.md`'s illustrative worked example for this domain, which predates this design and omitted `SharedKernel.Caching.FusionCache` entirely.

| Package | Sub-block | Status |
| ------- | --------- | ------ |
| `SharedKernel.Caching.Abstractions` | — (no sub-block) | Zero infrastructure dependencies — can never carry a logging call site |
| `SharedKernel.Caching.FusionCache` | `LoggingEventIdRanges.Caching + 0` .. `+ 99` | `CacheWarmupHostedService` (`+0`..`+7`), `FusionCacheService` (`+10`..`+14`) |
| `SharedKernel.Caching.Redis.Core` | `LoggingEventIdRanges.Caching + 100` .. `+ 199` | `RedisConnectionHealthTracker` (`+100`, `+101`) |
| `SharedKernel.Caching.Redis` (L2) | `LoggingEventIdRanges.Caching + 200` .. `+ 299` | Reserved — no logging call sites today |
| `SharedKernel.Caching.Redis.DistributedLocking` | `LoggingEventIdRanges.Caching + 300` .. `+ 399` | `RedLockDistributedLockService` (`+300`..`+306`), `RedLockRenewableLock` (`+307`..`+312`) |
| `SharedKernel.Caching.Redis.HashStore` | `LoggingEventIdRanges.Caching + 400` .. `+ 499` | Reserved — no logging call sites today |
| `SharedKernel.Caching.Redis.PubSub` | `LoggingEventIdRanges.Caching + 500` .. `+ 599` | `RedisChannelService` (`+500`..`+504`), `CacheInvalidationReceiver` (`+505`..`+509`), `RedisCacheInvalidationBus` (`+510`) |

- Every `[LoggerMessage(EventId = ...)]` value in this domain must be written as `LoggingEventIdRanges.Caching + {offset}` — never a bare literal integer. `LoggingEventIdRanges.Caching` is `const int`, so the sum is itself a valid compile-time constant expression for the attribute argument.
- All four logging-bearing packages (`FusionCache`, `Redis.Core`, `Redis.DistributedLocking`, `Redis.PubSub`) take a `<ProjectReference>` to `SharedKernel.Primitives` solely to consume this constant — layering-legal per the root `CLAUDE.md` (`02.Caching` may reference `01.Core`).
- `SharedKernel.Caching.Redis` (L2) and `SharedKernel.Caching.Redis.HashStore` reserve a sub-block each (`+200..+299`, `+400..+499`) despite carrying zero logging call sites today, so the first future log statement in either package has a pre-assigned, collision-free home.
- No direct `ILogger.LogInformation`/`LogWarning`/`LogError`/`LogDebug`/`LogCritical`/`LogTrace` extension-method call and no hand-written `LoggerMessage.Define`/`LoggerMessage.DefineScope` delegate is permitted anywhere in this domain's production source — every log statement is authored via the `[LoggerMessage]` source-generated partial-method pattern, mirroring the existing `private static partial class Log` nested-class convention already used by `FusionCacheService`, `RedisConnectionHealthTracker`, `RedLockDistributedLockService`, `RedLockRenewableLock`, `RedisChannelService`, and `CacheInvalidationReceiver`.
- Message template placeholders are PascalCase named properties matching the call's named arguments — never positional placeholders, never string-interpolated into the template (root convention, unchanged).
- This is enforced mechanically by `00.Governance`'s SK0020 (`DirectILoggerExtensionMethodUsage`), SK0021 (`HandWrittenLoggerMessageDefineDelegate`), and `LoggingEventIdIntegrityAssertion` once P-250 ships.

---

## DI Registration (current shape)

> All examples below remain valid throughout WO-023 (Phases 32–36) — no `Add*` signatures change. The new Phase 32 entry points (`AddRedisConnection`, `AddRedisCircuitBreaker`) are additive, low-level, `IServiceCollection`-based primitives consumed internally by `AddRedisL2` / `AddRedisDistributedLocking` / `AddRedisHashService` / `AddRedisChannelService` — most consumers never call them directly.

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

// Distributed locking — fluent chain (Phase 19: canonical form)
services.AddSharedKernelCaching(options => { })
        .AddRedisL2(connectionString)
        .AddRedisDistributedLocking(connectionString);

// Distributed locking — standalone (IServiceCollection overload, Obsolete after Phase 19)
services.AddRedisDistributedLocking(connectionString);

// Redis Pub/Sub channel service (requires IConnectionMultiplexer — guard added Ph19)
services.AddSharedKernelCaching(options => { })
        .AddRedisL2(connectionString)
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

// Redis-only consumer (no FusionCache) — Phase 27; AddRedisDistributedLocking from
// SharedKernel.Caching.Redis.Core + SharedKernel.Caching.Redis.DistributedLocking only (Phase 34)
services.AddCachingCoreOptions(o => o.ServiceName = "worker-service")
        .AddRedisDistributedLocking(connectionString);
// Note: AddCachingCoreOptions is on IServiceCollection (not ICachingBuilder) — it is the
// standalone entry point for services that do not call AddSharedKernelCaching.
// AddRedisDistributedLocking sources IConnectionMultiplexer via AddRedisConnection internally
// (TryAddSingleton — first caller wins).

// Renewable lock (Phase 23) — fluent chain
services.AddSharedKernelCaching(options => { })
        .AddRedisDistributedLocking(connectionString);
// Inject: IDistributedLockService → call AcquireRenewableAsync → IRenewableLock
// Background renewal: await KeepAliveAsync(lock, renewalInterval, ct);

// Cache warmup (Phase 28)
services.AddSharedKernelCaching(options => { options.WaitForWarmup = true; })
        .AddCacheWarmup<MyProductCatalogWarmup>()
        .AddCacheWarmup<MyUserPreferencesWarmup>();
// Multiple strategies registered; executed in Order ascending at startup.

// Multi-tenant key provider (Phase 29)
services.AddSharedKernelCaching(options => { options.ServiceName = "my-service"; })
        .AddTenantCacheKeyProvider();
// Inject: ITenantCacheKeyProvider → call BuildTenantKey(tenantId, entity, id)

// Redis L2 with circuit breaker (Phase 30; RedisL2Options.CircuitBreaker is RedisCircuitBreakerOptions from .Redis.Core as of Phase 32/33)
services.AddSharedKernelCaching(options => { })
        .AddRedisL2(connectionString, o => {
            o.CircuitBreaker.Enabled = true;
            o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        });

// Standalone connection core — minimal dependency for a service that only needs
// IConnectionMultiplexer + health tracking + optional circuit breaker, with no
// FusionCache, no RedLock, no hash store, no pub/sub (Phase 32).
services.AddRedisConnection(connectionString, o => { o.ConnectTimeoutMs = 5000; });
services.AddRedisCircuitBreaker(o => {
    o.Enabled = true;
    o.FailureThreshold = 5;
    o.BreakDuration = TimeSpan.FromSeconds(30);
});
// Inject: IConnectionMultiplexer, RedisConnectionHealthTracker, ResiliencePipeline (when enabled)

// Hash store — session storage (Phase 35). AddRedisHashService requires an ICachingBuilder
// (from AddSharedKernelCaching) and IConnectionMultiplexer (from AddRedisConnection, or any
// Add* that registers it transitively, e.g. AddRedisL2 / AddRedisDistributedLocking / AddRedisChannelService).
services.AddRedisConnection(connectionString); // registers IConnectionMultiplexer via .Redis.Core (TryAddSingleton)

services.AddSharedKernelCaching(options => {
             options.ServiceName = "session-service";
             options.SerializerContext = MyAppSerializerContext.Default;
         })
        .AddRedisHashService()
        .AddTypedHashStore(MyAppSerializerContext.Default.SessionDto);
// Inject: IRedisHashService, ITypedHashStore<SessionDto>

// Pub/Sub + invalidation only — lightweight signaling, no FusionCache, no RedLock, no hash store (Phase 36)
services.AddCachingCoreOptions(o => o.ServiceName = "my-service")
        .Services
        .AddRedisConnection(connectionString); // registers IConnectionMultiplexer via .Redis.Core (TryAddSingleton)

services.AddSingleton<ICacheService, MyCacheServiceImplementation>(); // required by AddCacheInvalidationReceiver guard

var pubSubBuilder = new MyCachingBuilder(services); // any ICachingBuilder wrapping `services`
pubSubBuilder.AddRedisChannelService()
             .AddRedisCacheInvalidationBus()
             .AddCacheInvalidationReceiver();
// Inject: IRedisChannelService, ICacheInvalidationBus; CacheInvalidationReceiver runs as a BackgroundService
```

---

## Test Rules

- Unit tests → `SharedKernel.Caching.FusionCache/SharedKernel.Caching.FusionCache.Tests/`
- **WO-023 (Phases 32–36) test locations** — each extracted package owns its nested `.Tests` project; tests are partitioned by capability, not duplicated:
  - `SharedKernel.Caching.Redis.Core/SharedKernel.Caching.Redis.Core.Tests/` — connection registration (first-caller-wins), `ConnectionHealthState` transitions via `RedisConnectionHealthTracker`, circuit breaker pipeline construction (enabled/disabled)
  - `SharedKernel.Caching.Redis/SharedKernel.Caching.Redis.Tests/` — L2 round-trip, batch operations, Brotli compression, `SerializerContext`/AOT regression, L1 fallback, L1SizeLimit, L2 KeyPrefix, sliding expiration, key versioning
  - `SharedKernel.Caching.Redis.DistributedLocking/SharedKernel.Caching.Redis.DistributedLocking.Tests/` *(implemented, Phase 34)* — RedLock acquire/timeout/release/expiry, renewable lock renewal/`KeepAliveAsync`, `IDistributedLockService` contract tests, `FakeRenewableLock`/`FakeDistributedLockService` unit tests, DI registration sanity
  - `SharedKernel.Caching.Redis.HashStore/SharedKernel.Caching.Redis.HashStore.Tests/` — `RedisHashService` set/get/get-all/delete/increment, `ITypedHashStore<T>` round-trip
  - `SharedKernel.Caching.Redis.PubSub/SharedKernel.Caching.Redis.PubSub.Tests/` — Pub/Sub round-trip, unsubscribe, channel reconnect/resubscribe, cache invalidation (key/tag/broadcast/offline), `CachingCoreOptions` DI (pub/sub-facing), DI registration sanity and optional circuit-breaker `ResiliencePipeline` injection for `AddRedisChannelService`
- Redis integration tests **must** use Testcontainers (`16.Testing/SharedKernel.Testing`) — no external Redis dependency. This applies to every `.Tests` project listed above.
- Stampede protection must be covered: parallel `GetOrSetAsync` calls verifying factory is invoked exactly once.
- RedLock tests must cover: acquire success, acquire timeout (returns `null`), release on dispose, lock expiry.
- L1-only fallback must be covered: stop the Redis container and assert cache degrades gracefully.
- `RedisChannelService` tests: Pub/Sub round-trip, unsubscribe stops delivery.
- `RedisHashService` tests: set/get field, get-all fields, delete field, increment field, shared multiplexer (no duplicate connection).
- `CacheInvalidationReceiver` tests: Key invalidation, Tag invalidation, broadcast warning logged, sender-offline no-crash, OTel `CorrelationId` present on span.
- `ITypedHashStore<T>` tests: five-method round-trip via Testcontainers (set/get/get-all/delete/increment).
- `BrotliCacheSerializer` unit tests: compressed round-trip (above threshold), passthrough (below threshold), mixed reads (both compressed and uncompressed values readable), compression level configuration.
- DI guard tests (Phase 19): each guard path throws `InvalidOperationException` with the correct message; `[Obsolete]` `AddRedisDistributedLocking(IServiceCollection)` shim delegates correctly.
- `SerializerContext` regression test (Phase 18): after `AddSharedKernelCaching(o => o.SerializerContext = ctx)` + `AddRedisL2(...)`, the DI-resolved `IFusionCacheSerializer` includes `ctx` in its type info resolver chain.
- L1SizeLimit eviction test (Phase 20): `L1SizeLimit = 100` with 101 entries causes at least one eviction.
- L2 KeyPrefix integration test (Phase 20): raw Redis key contains the configured prefix.
- Nullable factory tests (Phase 21): null result is cached (factory called once on first miss, not on second); non-null factory round-trip works correctly.
- Batch operations tests (Phase 22): mixed hits/misses, batch set then get-many, empty key list → empty dictionary; integration: single Redis pipeline round-trip verified by command count.
- Renewable lock tests (Phase 23): renewal succeeds before expiry, returns `false` after expiry without throwing, `KeepAliveAsync` prevents lock loss; `FakeRenewableLock` tracks `RenewalCount`.
- Sliding expiration tests (Phase 24): `Sliding` factory properties correct, idle-expired entry not returned, absolute ceiling applies; `NeverExpire + SlidingWindow` guard throws.
- Key versioning tests (Phase 25): version 0 = no suffix change, version 3 = `:v3` suffix, `WithVersion` + `WithTags` chain correctly.
- Channel reconnect test (Phase 26): drop and restore Redis container; messages delivered after reconnect; resubscription count equals pre-disconnect count.
- CachingCoreOptions DI test (Phase 27): `ServiceName` resolves correctly via `AddCachingCoreOptions` alone; warning logged when `ServiceName` is still `"app"`.
- Cache warmup tests (Phase 28): ordering correct, failed strategy does not abort others, timing logged per strategy.
- Tenant key tests (Phase 29): `BuildTenantKey` format, tenant isolation (different tenant IDs → different keys), format consistent with `BuildKey`.
- Circuit breaker tests (Phase 30): circuit opens after threshold, short-circuits on open, closes after break duration; `Enabled = false` → zero behavioral change.
- OTel metrics tests (Phase 31): each counter and histogram verified via `MeterListener` under correct conditions.
- Connection core tests (Phase 32, new): `AddRedisConnection` registers `IConnectionMultiplexer` exactly once when called from multiple `Add*` extensions in the same container (first-caller-wins); `RedisConnectionHealthTracker.ConnectionHealth` transitions correctly via `internal` `OnConnectionRestored`/`OnConnectionFailed` simulation; `AddRedisCircuitBreaker` returns a non-null `ResiliencePipeline` when `Enabled = true` and registers nothing when `false`.
- Redis package extraction regression tests (Phases 33–36, relocated not new): every test listed above for L2/locking/hash/pub-sub must continue to pass unchanged after relocation to its new package — relocation is a pure move (namespace + project updates only), so test *behavior* and *assertions* must not change. A relocated test that requires behavioral changes to pass indicates a relocation error, not an expected update.
- Cross-package DI composition test (Phases 34–36, new): a container that calls only `AddRedisConnection` + one of `AddRedisDistributedLocking` / `AddRedisHashService` / `AddRedisChannelService` (without `AddRedisL2`) resolves successfully — proves the four extraction packages are independently consumable per the "sibling packages never reference each other" rule.

---

## Changelog

> Maintained by the caching domain agent. One line per significant change.

- [2026-05-14] Domain brain initialized — packages, interfaces, rules, AOT notes
- [2026-05-18] Refreshed for WO-003 — three-package split (Abstractions/Caching/Redis), ICacheKeyProvider, IRedisChannelService, IRedisHashService, ICacheInvalidationBus, CacheInvalidationMessage, channel naming convention, invalidation receiver rules, updated DI registration shape (Phases 5, 6, 7, 12)
- [2026-05-18] Phase 7 complete — Redis package now refs Abstractions directly; AddRedisL2 registers IConnectionMultiplexer; FusionCache pkgs explicit in Redis csproj (agent)
- [2026-05-18] Phase 14 planned (WO-004) — SharedKernel.Caching renamed to SharedKernel.Caching.FusionCache; namespace migration to SharedKernel.Caching.FusionCache.*; CachePolicy.NeverExpire preset added; CLAUDE.md package table and test rules updated (caching-arch-planner)
- [2026-05-20] Phases 15 + 16 planned (WO-005) — Phase 15: fix FusionCache STJ AOT gap (SerializerContext option) + ITypedHashStore typed hash store; Phase 16: opt-in Brotli L2 compression (magic-byte, ArrayPool, threshold); protobuf-net declined (AOT-incompatible); package table, interface contracts, rules, DI shape updated (arch-lead)
- [2026-05-20] B-01 to B-05 complete in SK.02.BrotliCompression — CompressionOptions nested class added to CachingOptions; BrotliCacheSerializer decorator (ArrayPool, magic bytes 0x42 0x52, BrotliStream decompress); AddBrotliCompression extension on ICachingBuilder; AddSharedKernelCaching updated to register IFusionCacheSerializer in DI via WithRegisteredSerializer(); 91 FusionCache + 74 Redis tests passing (caching-phase-implementer)
- [2026-05-20] Phases 17–21 planned (WO-006) — Ph17: CachingCoreOptions in Abstractions resolves Redis→FusionCache layering violation; Ph18: AddRedisL2 serializer override AOT fix; Ph19: DI ergonomics (ICachingBuilder overload for AddRedisDistributedLocking, startup guards); Ph20: L1SizeLimit wiring + L2 KeyPrefix integration test; Ph21: GetOrSetAsync ValueTask factory + nullable overload (breaking change at v1.0.0); package table, interface contracts, rules, DI shape, test rules updated (caching-arch-planner)
- [2026-05-20] Phase 17 implemented — CachingCoreOptions added to Abstractions; Redis ProjectRef to FusionCache removed; sibling layering rule enforced; 91+74 tests passing (caching-phase-implementer)
- [2026-05-20] Phase 20 implemented — L1SizeLimit wired via WithMemoryCache(SizeLimit)+Size=1 per entry; L2 key format verified as {KeyPrefix}v2:{user-key}; v2: injected by Microsoft.Extensions.Caching.StackExchangeRedis v10+; 95+93 tests passing (caching-phase-implementer)
- [2026-05-20] Phase 21 implemented — GetOrSetAsync factory migrated from Task{T} to ValueTask{T}; C# constraint prevents a separate nullable overload (Func{CT,ValueTask{T}} and Func{CT,ValueTask{T?}} are identical at CLR level); negative-result caching via GetOrSetAsync{T?} confirmed working (FusionCache caches null as genuine entry); 102 FusionCache + 93 Redis tests passing (caching-phase-implementer)
- [2026-05-21] Phases 22–31 planned (WO-007) — batch operations (GetManyAsync/SetManyAsync), IRenewableLock heartbeat, CachePolicy.Sliding + SlidingWindow, CachePolicy.KeyVersion + BuildKey version overload, RedisChannelService reconnect resilience + ConnectionHealthState, AddCachingCoreOptions standalone DI, ICacheWarmupStrategy + CacheWarmupHostedService, ITenantCacheKeyProvider multi-tenant key isolation, Polly v8 circuit breaker opt-in for Redis L2, OTel System.Diagnostics.Metrics on FusionCacheService; new interface contracts, rules, DI patterns, test rules updated (caching-arch-planner)
- [2026-05-21] Phase 22 implemented — GetManyAsync/SetManyAsync added to ICacheService; FusionCacheService loop impl; IRedisL2BatchService pipeline helper (internal, InternalsVisibleTo test); FakeCacheService updated; 114 FusionCache + 99 Redis tests passing (caching-phase-implementer)
- [2026-05-21] Phase 23 implemented — IRenewableLock + AcquireRenewableAsync + RedLockRenewableLock (re-acquisition strategy; RedLock.net 2.3.2 has no public ExtendAsync); KeepAliveAsync extension; FakeDistributedLockService + FakeRenewableLock in 16.Testing; 114 FusionCache + 125 Redis tests passing (caching-phase-implementer)
- [2026-05-21] Phase 24 implemented — CachePolicy.SlidingWindow + Sliding() factory; FusionCacheService BuildEntryOptions SlidingExpiration wiring; NeverExpire+Sliding guard; 134 FusionCache + 125 Redis tests passing (caching-phase-implementer)
- [2026-05-21] Phase 25 implemented — CachePolicy.KeyVersion + WithVersion(int); ICacheKeyProvider versioned BuildKey overload; CacheKeyProvider :v{n} suffix; 160 FusionCache + 125 Redis tests passing (caching-phase-implementer)
- [2026-05-21] Phase 26 implemented — ConnectionHealthState enum; IRedisChannelService.ConnectionHealth; RedisChannelService reconnect replay (Dictionary+lock, volatile int health); OnConnectionRestored/Failed internal for testability; 160 FusionCache + 132 Redis tests passing (caching-phase-implementer)
- [2026-05-22] Phase 27 implemented — AddCachingCoreOptions on IServiceCollection in Abstractions; zero FusionCache/Redis dependency; AddSharedKernelCaching unchanged; 172 FusionCache + 142 Redis tests passing (caching-phase-implementer)
- [2026-05-22] Phase 28 implemented — ICacheWarmupStrategy in Abstractions; CacheWarmupHostedService (IHostedLifecycleService.StartedAsync); WaitForWarmup option; AddCacheWarmup extension; 172 FusionCache + 142 Redis tests passing (caching-phase-implementer)
- [2026-05-22] Phase 29 implemented — ITenantCacheKeyProvider in Abstractions; TenantCacheKeyProvider (IOptions`CachingCoreOptions`, internal sealed) + AddTenantCacheKeyProvider in FusionCache; FakeTenantCacheKeyProvider in 16.Testing; 196 FusionCache + 142 Redis tests passing (caching-phase-implementer)
- [2026-05-22] Phase 30 implemented — Polly.Core 8.5.2 added to Redis package; CircuitBreakerOptions nested class in RedisL2Options; ResiliencePipeline singleton registered only when Enabled=true; FailureRatio=1.0+MinimumThroughput pattern for count semantics; BreakDuration minimum 500ms; factory DI for optional pipeline injection; 196 FusionCache + 154 Redis tests passing (sync-brain)
- [2026-06-11] Phases 32–36 planned (WO-023) — split SharedKernel.Caching.Redis into 5 packages: Ph32 SharedKernel.Caching.Redis.Core (new dependency root: AddRedisConnection, RedisConnectionHealthTracker, RedisCircuitBreakerOptions + AddRedisCircuitBreaker — generalized from Ph.30); Ph33 SharedKernel.Caching.Redis slimmed to L2-only (AddRedisL2 sources multiplexer/pipeline from Core via new ProjectReference); Ph34 SharedKernel.Caching.Redis.DistributedLocking (RedLockDistributedLockService/RedLockRenewableLock/AddRedisDistributedLocking extracted); Ph35 SharedKernel.Caching.Redis.HashStore (RedisHashService/TypedHashStore<T>/AddRedisHashService/AddTypedHashStore<T> extracted); Ph36 SharedKernel.Caching.Redis.PubSub (RedisChannelService/RedisCacheInvalidationBus/CacheInvalidationReceiver extracted, stays in 02.Caching not 07.Messaging — durability-contrast rationale documented). SharedKernel.Caching.Abstractions contracts unchanged; all AddRedis* fluent DI shapes preserved; package table, technology stack, interface contracts (.Redis.Core surface), implementation rules, DI registration examples, and test rules updated for the 7-package target topology (caching-arch-planner)
- [2026-06-11] Phase 32 implemented — SharedKernel.Caching.Redis.Core package created (RedisConnectionOptions, RedisCircuitBreakerOptions, RedisConnectionHealthTracker, AddRedisConnection, AddRedisCircuitBreaker); zero references to FusionCache/RedLock.net; 33 tests passing (caching-phase-implementer)
- [2026-06-11] Phase 33 implemented — SharedKernel.Caching.Redis.csproj adds ProjectReference to .Redis.Core; AddRedisL2 sources IConnectionMultiplexer via AddRedisConnection and the optional circuit breaker via AddRedisCircuitBreaker; RedisL2Options.CircuitBreaker retyped from a locally-nested class to the top-level RedisCircuitBreakerOptions (Core) — type relocation, source-compat, o.CircuitBreaker.Enabled = true unchanged; Polly.Core/StackExchange.Redis/RedLock.net/Hosting.Abstractions direct references retained (RedisHashService/RedisChannelService/RedLock locking still physically reside in .Redis pending Phases 34-36); 154 Redis + 33 Redis.Core tests passing (caching-phase-implementer)
- [2026-06-11] Phase 34 implemented — SharedKernel.Caching.Redis.DistributedLocking package created; RedLockDistributedLockService, RedLockRenewableLock + KeepAliveAsync extension, RedisLockOptions, AddRedisDistributedLocking (canonical ICachingBuilder overload + Obsolete IServiceCollection shim with its own RedisLockCachingBuilder adapter) relocated from SharedKernel.Caching.Redis; AddRedisDistributedLocking now sources IConnectionMultiplexer via AddRedisConnection from .Redis.Core; RedLock.net PackageReference removed from SharedKernel.Caching.Redis.csproj; FakeDistributedLockService/FakeRenewableLock remain in 16.Testing (not moved); 96 Redis + 41 Redis.DistributedLocking + 33 Redis.Core tests passing (caching-phase-implementer)
- [2026-06-12] Phase 35 implemented — SharedKernel.Caching.Redis.HashStore package created; RedisHashService (IRedisHashService) and TypedHashStore<T> (internal sealed, ITypedHashStore<T>) relocated from SharedKernel.Caching.Redis via pure namespace rename; AddRedisHashService/AddTypedHashStore<T> relocated, no Obsolete shim needed (already ICachingBuilder-only); AddRedisHashService startup guard message updated to reference AddRedisConnection (directly, or transitively via AddRedisL2/AddRedisDistributedLocking/AddRedisChannelService); optional ResiliencePipeline continues to resolve via sp.GetService<ResiliencePipeline>(), now sourced from .Redis.Core's AddRedisCircuitBreaker; two hidden AddRedisHashService-related test files in .Redis.Tests (RedisDiRegistrationTests.cs, CircuitBreakerTests.cs) outside the File-Level Plan were trimmed and their coverage recreated in the new package's RedisHashServiceDiTests.cs; 68 Redis + 41 Redis.DistributedLocking + 30 Redis.HashStore + 33 Redis.Core tests passing (caching-phase-implementer)
- [2026-06-12] Phase 36 implemented — SharedKernel.Caching.Redis.PubSub package created; RedisChannelService (IRedisChannelService, with Phase 26 reconnect/resubscribe replay relocated intact), RedisCacheInvalidationBus (ICacheInvalidationBus), CacheInvalidationReceiver (BackgroundService) relocated from SharedKernel.Caching.Redis via pure namespace rename; AddRedisChannelService/AddRedisCacheInvalidationBus/AddCacheInvalidationReceiver relocated, no Obsolete shim needed (already ICachingBuilder-only); AddRedisChannelService startup guard message updated to reference AddRedisConnection (directly, or transitively via AddRedisL2/AddRedisDistributedLocking/AddRedisHashService); optional ResiliencePipeline continues to resolve via sp.GetService (ResiliencePipeline), now sourced from .Redis.Core's AddRedisCircuitBreaker; SharedKernel.Caching.Redis.csproj slimmed to its WO-023 end state — Microsoft.Extensions.Hosting.Abstractions and Polly.Core PackageReferences removed (Polly resilience consumed transitively via .Redis.Core); three hidden cross-package test coverage findings in .Redis.Tests outside the File-Level Plan (CachingCoreOptionsDiTests.cs full file, DI/DiErgonomicsGuardTests.cs full file, DI/RedisDiRegistrationTests.cs full file, plus one test in CircuitBreakerTests.cs) were relocated/recreated — the CircuitBreakerTests.cs coverage recreated as RedisChannelServiceDiTests.cs modeled on Phase 35's RedisHashServiceDiTests.cs; 28 Redis + 41 Redis.DistributedLocking + 30 Redis.HashStore + 33 Redis.Core + 41 Redis.PubSub tests passing — WO-023 (Redis package split, Phases 32-36) now fully complete (caching-phase-implementer)
- [2026-07-09] Phase 37 planned (WO-041, P-252) — logging retrofit to the platform `[LoggerMessage]` standard, dispatched from the root logging-standard work order (P-249 EventId registry design in `01.Core`, P-250 SK0020/SK0021 analyzer + `LoggingEventIdIntegrityAssertion` design in `00.Governance`). Audited all six packages: `FusionCache`'s `CacheWarmupHostedService` uses 8 direct `ILogger` calls and `FusionCacheService` squats on `01.Core`'s reserved `EventId` block (1001-1005); `Redis.PubSub`'s `RedisChannelService` squats on `03.Domain`'s reserved block (3001-3005) and `RedisCacheInvalidationBus` uses a hand-written `LoggerMessage.Define<string>` delegate; `Redis.Core`'s `RedisConnectionHealthTracker` and `Redis.PubSub`'s `CacheInvalidationReceiver` collide with each other at 4001/4002 (the exact collision that motivated `01.Core` P-249); `Redis` (L2) and `Redis.HashStore` carry zero logging call sites and are unaffected. Designed and documented this domain's authoritative 100-wide-per-package `EventId` sub-block allocation inside `LoggingEventIdRanges.Caching` (2000-2999), in Packages-table declaration order — superseding `01.Core/CLAUDE.md`'s illustrative worked example, which omitted `FusionCache`. New "Logging (EventId sub-blocks)" subsection added under Implementation Rules; "Current Phase" section updated. Execution is blocked until `01.Core` ships `SharedKernel.Primitives.Logging.LoggingEventIdRanges` (P-249's C-42 task, `○` Pending as of this planning pass) — all four affected packages need a new `<ProjectReference>` to `SharedKernel.Primitives` to consume it. 12 tasks (LR-01→LR-12) added to `02.Caching/state-map.md` under `SK.02.LoggingRetrofit` (caching-arch-planner, WO-041)
- [2026-07-10] Phase 37 (WO-041, P-252) executed — `LoggingEventIdRanges` blocker confirmed cleared (`01.Core` P-249 shipped); added `<ProjectReference>` to `SharedKernel.Primitives` in `FusionCache`, `Redis.Core`, `Redis.DistributedLocking`, `Redis.PubSub` csproj files. `CacheWarmupHostedService`'s 8 direct `ILogger` calls converted to a new nested `private static partial class Log` (`LoggingEventIdRanges.Caching + 0`..`+7`; the outer class had to become `partial` for the source generator to attach) and its own `FusionCacheService.Log` renumbered from literal `1001-1005` to `+10`..`+14`. `RedisConnectionHealthTracker.Log` renumbered `4001/4002` → `+100`/`+101`. `RedLockDistributedLockService.Log` (`2001-2007` → `+300`..`+306`) and `RedLockRenewableLock.Log` (`2010-2015` → `+307`..`+312`) renumbered. `RedisChannelService.Log` (`3001-3005` → `+500`..`+504`) and `CacheInvalidationReceiver.Log` (`4001-4005` → `+505`..`+509`) renumbered. `RedisCacheInvalidationBus`'s hand-written `LoggerMessage.Define<string>` static delegate replaced with a `[LoggerMessage]`-attributed `DefaultServiceNameWarning` method on a new nested `Log` class (`+510`) — the class itself had to become `partial`. Zero message-text, level, or structured-property changes — `EventId` values and authoring mechanism only. All six test suites regression-run standalone (pre-existing, unrelated `NU1605` package-downgrade restore errors from `SharedKernel.Testing`'s transitive dependency floor block solution-wide `dotnet build`/`dotnet test`, confirmed via `git stash` reproduction on unmodified code — worked around per-project with `-p:NoWarn=NU1605` for verification only, no csproj changes made to suppress it): FusionCache 209/209, Redis.Core 33/33, Redis.DistributedLocking 41/41, Redis.PubSub 41/41, Redis (L2) 28/28, Redis.HashStore 30/30 — zero behavioral change confirmed. "Current Phase" and "Logging (EventId sub-blocks)" sections updated to reflect completion (caching-phase-implementer)
