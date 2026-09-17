# 02.Caching — Domain Brain

## What This Domain Is

The caching capability domain. A hybrid L1 (in-process memory) / L2 (Redis distributed) cache powered by **ZiggyCreatures.FusionCache**, with stampede protection, fail-safe, eager refresh, tags and factory-controlled caching, plus Redis distributed locks and leases with fencing tokens. The Redis packages are separate, so a service that needs only L1 references no Redis package. `SharedKernel.Caching.Abstractions` is provider-neutral: layers such as `05.Application` depend on the contracts without pulling in FusionCache or StackExchange.Redis.

Philosophy: **Provider-neutral contracts. Stampede-proof. Outages are never reported as contention.**

---

## Current Phase

**P-547 — pre-first-publish redesign of `SharedKernel.Caching.Abstractions` (BREAKING API and behaviour), implemented in source.** The root phase stays `◐` until the packages are published. What changed, in one place:

- `ICacheService`: `TryGetAsync`/`TryGetManyAsync` return `CacheLookup<T>` (a cached `null`/`0` is a hit); a `GetOrSetAsync` overload whose factory receives a `CacheFactoryContext` (`SkipCaching`, `SetDurations`); new `ExpireAsync`, `RemoveByTagsAsync`, `ClearAsync`.
- `CachePolicy`: tags are an `IReadOnlyList<string>`, validated; `ForTenant`; fail-safe maximum, factory timeouts, jitter, `LocalOnly`. `Sliding` and key versioning are gone.
- `CacheKeyFormat`: one escaped, collision-free key and tag format with an `@{tenant}` marker. `CachingOptions.ServiceName` has no default and is validated at startup.
- Locks: `IDistributedLockService.TryAcquireAsync` → `IDistributedLock` (fencing token, `IsHeld`, `LostToken`, kept alive until disposed); `TryAcquireLeaseAsync` → `DistributedLease`; an unreachable store throws `DistributedLockUnavailableException`. RedLock.net is replaced by atomic Lua scripts.
- Removed: the Pub/Sub cache-invalidation bus and receiver (the FusionCache backplane already propagates removals), `CachingCoreOptions`. Provider contracts moved to their providers (`IRedisChannelService` → `.Redis.PubSub`, `IRedisHashService`/`ITypedHashStore<T>` → `.Redis.HashStore`, `ConnectionHealthState` → `.Redis.Core`).
- The abstractions package now tracks its public API (`PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`) and has its own test project.

Test runs after the redesign: Abstractions 78, FusionCache 198, Redis (L2) 34, Redis.DistributedLocking 47, Redis.PubSub 18, Redis.HashStore 30, Redis.Core 55 — all passing. Consumers migrated in the same pass: `05.Application.Behaviors.Caching` (24), `13.ServiceDefaults` MultiTenancy (85) and ServiceDefaults.Caching (9), `16.Testing` SelfTests (1399), `19.Scheduling` (39), `00.Governance` ArchitectureTests (323) and Analyzers (331).

Phase-by-phase history (WO-003 through WO-081) lives in `02.Caching/state-map.md` and the Changelog below; it describes types that no longer exist.

---

## Packages

| Package | Role | NuGet / Project References |
| ------- | ---- | -------------------------- |
| `SharedKernel.Caching.Abstractions` | Provider-neutral contracts: `ICacheService`, `ITenantCacheService`, `CacheLookup<T>`, `CacheFactoryContext`, `CachePolicy`, `CacheKeyFormat`, `ICacheKeyProvider`, `ITenantCacheKeyProvider`, `ICacheWarmupStrategy`, `ICachingBuilder`, `IDistributedLockService`, `IDistributedLock`, `DistributedLockOptions`, `DistributedLease`, `DistributedLockUnavailableException`. Public API tracked (RS0016/RS0017 and friends as errors). No logging, no options, no hosting | `Microsoft.Extensions.DependencyInjection.Abstractions` only (plus the private `Microsoft.CodeAnalysis.PublicApiAnalyzers`) |
| `SharedKernel.Caching.FusionCache` | `FusionCacheService` (`ICacheService`), `CacheKeyProvider` (both key interfaces), `TenantCacheService`, `EncryptedCacheService`, `BrotliCacheSerializer`, `CacheWarmupHostedService`, `CachingOptions` + `CachingOptionsValidator`; `AddSharedKernelCaching`, `AddTenantCacheService`, `AddBrotliCompression`, `AddCacheEncryption`, `AddCacheWarmup<TStrategy>` | `Abstractions`, `SharedKernel.Primitives`, `SharedKernel.Cryptography`, ZiggyCreatures.FusionCache (+ `.Serialization.SystemTextJson`), `Microsoft.Extensions.Options.DataAnnotations`, `Microsoft.Extensions.Hosting.Abstractions` |
| `SharedKernel.Caching.Redis.Core` | Shared connection layer: `AddRedisConnection` (one `IConnectionMultiplexer`, first caller wins), `RedisConnectionOptions` (validated, TLS/mTLS), `RedisConnectionHealthTracker`, `ConnectionHealthState`, `RedisCircuitBreakerOptions` + `AddRedisCircuitBreaker`. **References no `SharedKernel.Caching` package** | `SharedKernel.Primitives`, StackExchange.Redis, Polly.Core, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options.DataAnnotations`, `Microsoft.Extensions.Hosting.Abstractions` |
| `SharedKernel.Caching.Redis` | L2 distributed cache and FusionCache backplane only: `AddRedisL2`, `RedisL2Options` | `Abstractions`, `Redis.Core`, `Microsoft.Extensions.Caching.StackExchangeRedis`, ZiggyCreatures.FusionCache, ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis. **Must not reference `.FusionCache`** |
| `SharedKernel.Caching.Redis.DistributedLocking` | `RedisDistributedLockService` (`IDistributedLockService`), `RedisDistributedLock` (`IDistributedLock`), `RedisLockScripts` (all internal); `AddRedisDistributedLocking` on `ICachingBuilder` and on `IServiceCollection`; `RedisLockOptions` | `Abstractions`, `Redis.Core`, `SharedKernel.Primitives`. No RedLock.net (locked by `00.Governance`) |
| `SharedKernel.Caching.Redis.HashStore` | `IRedisHashService`, `ITypedHashStore<T>` (namespace `SharedKernel.Caching.Redis.HashStore`), `RedisHashService`, `TypedHashStore<T>`; `AddRedisHashService`, `AddTypedHashStore<T>` | `Abstractions` (for `ICachingBuilder`), `Redis.Core` |
| `SharedKernel.Caching.Redis.PubSub` | `IRedisChannelService` (namespace `SharedKernel.Caching.Redis.PubSub`), `RedisChannelService`; `AddRedisChannelService` | `Abstractions` (for `ICachingBuilder`), `Redis.Core`, `SharedKernel.Primitives`. No hosting package |

All packages target `net10.0`. Test projects are nested inside each project folder.

**NuGet packaging:** all 7 packages carry the full metadata bar (`PackageId`/`Authors`/`Company`/`Product`/`Description`/`PackageTags`/`PackageLicenseExpression`/`PackageReadmeFile`/`RepositoryType`/`RepositoryUrl`/`PackageProjectUrl`/`Copyright`/`GenerateDocumentationFile`/`IncludeSymbols`/`SymbolPackageFormat`) and a packed `README.md`. Versions come from the repo-wide MinVer tag (root `PLATFORM.md`), never a `<Version>` element.

**`02.Caching/consumer-verify`:** `SharedKernel.Caching.ConsumerVerify.csproj` takes a `PackageReference` (not `ProjectReference` — proving the *packed* artifact resolves is the point) to all 7 `PackageId`s, plus `Microsoft.Extensions.Hosting` and `Testcontainers.Redis`. `Program.cs` runs five surfaces, each through a real `Host.CreateApplicationBuilder()` → `IHost.StartAsync()`, against one shared Testcontainers Redis:

| Surface | DI composition | Contract(s) resolved | Real-Redis proof |
| ------- | --------------- | --------------------- | ----------------- |
| 1. L1-only | `AddSharedKernelCaching(o => o.ServiceName = "consumer-verify-l1")` | `ICacheService`, `ICacheKeyProvider` | none needed — `GetOrSetAsync` round-trip in-process |
| 2. L1+L2 | `AddSharedKernelCaching(...).AddRedisL2(connectionString)` | `ICacheService` | raw `KeyExistsAsync("v2:{key}")` after a `GetOrSetAsync` write |
| 3. Locking-only (no FusionCache) | `services.AddRedisDistributedLocking(connectionString)` | `IDistributedLockService` | `TryAcquireAsync` returns a held lock with a positive fencing token; a contended attempt returns `null`; disposing releases and cancels `LostToken`; the next acquisition has a greater token; a second lease on the same resource returns `null` |
| 4. Hash-store-only | `AddRedisConnection(connectionString)` + a local `ICachingBuilder` `.AddRedisHashService().AddTypedHashStore(...)` | `IRedisHashService`, `ITypedHashStore<string>` | `SetFieldAsync`/`GetFieldAsync`/`IncrementFieldAsync` round-trip |
| 5. Pub/Sub-only | `AddRedisConnection(connectionString)` + a local `ICachingBuilder` `.AddRedisChannelService()` | `IRedisChannelService` | `SubscribeAsync`/`PublishAsync` round-trip (bounded 10 s wait), then `UnsubscribeAsync` |

Each surface is run independently; the process exits `1` if any failed.

**Microservices reference `SharedKernel.Caching.Abstractions` for contracts** and provider packages only at the composition root:

- L1-only: `SharedKernel.Caching.FusionCache`
- L1 + L2 cache: `+ SharedKernel.Caching.Redis` (brings `.Redis.Core`)
- Distributed locking only (no cache): `SharedKernel.Caching.Redis.DistributedLocking` (brings `.Redis.Core`)
- Structured hash storage only: `SharedKernel.Caching.Redis.HashStore`
- Ephemeral signaling only: `SharedKernel.Caching.Redis.PubSub`

**Layering rules:**

- `SharedKernel.Caching.Abstractions` references only `Microsoft.Extensions.DependencyInjection.Abstractions` and declares no provider-named type. `00.Governance` locks both (`RedisTopologyRules`).
- `SharedKernel.Caching.Redis` and `SharedKernel.Caching.FusionCache` never reference each other.
- `SharedKernel.Caching.Redis.Core` references no capability package and no `SharedKernel.Caching` package; it knows Redis connections and resilience only.
- `.Redis`, `.DistributedLocking`, `.HashStore` and `.PubSub` depend on `.Redis.Core` (and `Abstractions`) but **never on each other**. A consumer can take any subset.

### Why pub/sub stays in `02.Caching` (not `07.Messaging`)

`IRedisChannelService` (`SharedKernel.Caching.Redis.PubSub`) carries a **deliberately weaker contract** than `07.Messaging`:

- **At-most-once, no durability.** An offline subscriber misses the message permanently. No outbox, no retry, no dead-letter queue.
- **No ordering or delivery guarantees across restarts.** `07.Messaging` provides outbox-backed, retryable, ordered delivery via RabbitMQ/ASB.
- **Cache-adjacent only.** Lightweight, loss-tolerant signals between instances. Cache invalidation itself does not use it: FusionCache's Redis backplane (`AddRedisL2`) already carries removals, expirations and tag evictions to every instance.

Moving it into `07.Messaging` would let developers assume durability it does not have. `07.Messaging` never references a caching package and no caching package references messaging (root hard rule, locked by `RedisTopologyRules.PubSubNeverReferencesMessaging`/`MessagingNeverReferencesCaching`). The at-most-once boundary statement stays in `IRedisChannelService`'s XML doc.

---

## Technology Stack

| Concern | Technology | Confirmed Version | Owning Package |
| ------- | ---------- | ----------------- | --------------- |
| L1 cache (in-process) | `ZiggyCreatures.FusionCache` | 2.6.0 | `.FusionCache` |
| L2 backplane | `ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis` | 2.6.0 | `.Redis` |
| L2 distributed cache | `Microsoft.Extensions.Caching.StackExchangeRedis` | 10.0.0 | `.Redis` |
| STJ serialization for FusionCache | `ZiggyCreatures.FusionCache.Serialization.SystemTextJson` | 2.6.0 | `.FusionCache` |
| Redis client | `StackExchange.Redis` | 2.13.1 | `.Redis.Core` (used by every Redis package) |
| Distributed locking | Server-side Lua scripts over `StackExchange.Redis` (`IDatabase.ScriptEvaluateAsync`) | — | `.Redis.DistributedLocking` |
| DI abstractions | `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.11 | all packages |
| Public API tracking | `Microsoft.CodeAnalysis.PublicApiAnalyzers` | 5.6.0 | `Abstractions` |
| OTel tracing and metrics | `System.Diagnostics` `ActivitySource`/`Meter` (BCL) | — | `.FusionCache` |
| Redis circuit breaker (opt-in) | `Polly.Core` | 8.7.0 | `.Redis.Core`, `.HashStore`, `.PubSub` |
| Options validation | `Microsoft.Extensions.Options.DataAnnotations` | 10.0.11 | `.FusionCache`, `.Redis.Core` |
| Hosting (`ValidateOnStart` pairing, warmup hosted service) | `Microsoft.Extensions.Hosting.Abstractions` | 10.0.11 | `.FusionCache`, `.Redis.Core` |
| Cache-value encryption at rest (opt-in) | `01.Core/SharedKernel.Cryptography` `ISymmetricEncryptionService` (AES-256-GCM, required AAD, async-only) | ProjectReference | `.FusionCache` |

---

## Interface Contracts

### `SharedKernel.Caching.Abstractions` — full public surface (P-547)

Authoritative surface: `SharedKernel.Caching.Abstractions/PublicAPI.Unshipped.txt`. Usage: that package's `README.md`.

```text
ICacheService
    TryGetAsync<T>(string key, CancellationToken ct)                                   → ValueTask<CacheLookup<T>>
    TryGetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct)
                                                          → ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>>
        // one lookup per DISTINCT requested key
    GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
                     CachePolicy policy, CancellationToken ct)                         → ValueTask<T>
    GetOrSetAsync<T>(string key, Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
                     CachePolicy policy, CancellationToken ct)                         → ValueTask<T>
        // factory runs once per key across concurrent callers; a nullable T caches "not found"
    SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct)         → ValueTask
    SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct) → ValueTask
    RemoveAsync(string key, CancellationToken ct)                                      → ValueTask
    ExpireAsync(string key, CancellationToken ct)                                      → ValueTask
        // next read recomputes; fail-safe may still serve the old value if recomputing fails
    RemoveByTagAsync(string tag, CancellationToken ct)                                 → ValueTask
    RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct)                  → ValueTask
    ClearAsync(CancellationToken ct)                                                   → ValueTask
        // every entry, all tenants, every instance — break-glass

CacheLookup<T>  (readonly struct, IEquatable)
    IsHit                bool
    Value                T        (throws InvalidOperationException on a miss)
    TryGetValue(out T)   bool
    GetValueOrDefault(T fallback) → T
    static Miss (== default), static Hit(T value)
    // a cached null or 0 is a HIT

CacheFactoryContext  (sealed class)
    ctor(string key, CachePolicy policy)
    Key, Policy
    SkipCaching()                              // value still returned to every waiting caller; nothing written;
                                               // an existing entry is left untouched
    IsCachingSkipped                           bool
    SetDurations(TimeSpan l1, TimeSpan l2)     // same validation as CachePolicy.For
    L1DurationOverride / L2DurationOverride    TimeSpan?

CachePolicy  (sealed immutable record; value equality incl. tags in order)
    L1Duration  (5 min)   L2Duration (30 min)   — TimeSpan.MaxValue = never expires by time
    Tags                  IReadOnlyList<string> (default empty)
    IsFailSafeEnabled     (true)   FailSafeMaxDuration  TimeSpan?
    FactorySoftTimeout    TimeSpan?  FactoryHardTimeout TimeSpan?
    EagerRefreshThreshold double? (0.9)
    JitterMaxDuration     TimeSpan?
    IsLocalOnly           (false)  IsTenantScoped (false)
    static Default                              // 5 min / 30 min, fail-safe on, eager refresh 0.9
    static NeverExpire                          // MaxValue L1 + L2, no eager refresh
    static For(TimeSpan l1, TimeSpan l2)        // both > 0, l1 <= l2
    static For(TimeSpan duration)
    WithTags(params string[] tags)              // non-empty; no null/whitespace; no leading '@'; copied
    ForTenant(string tenantId)                  // tags → @{tenant}:{tag}, plus tenant-wide tag @{tenant};
                                                // throws if already tenant-scoped; WithTags after it throws
    WithFailSafe(TimeSpan max) / WithoutFailSafe()   // WithoutFailSafe also clears the soft timeout
    WithFactoryTimeouts(TimeSpan? soft, TimeSpan? hard) // soft needs fail-safe; soft < hard
    WithEagerRefresh(double threshold = 0.9) / WithoutEagerRefresh()   // threshold in (0, 1)
    WithJitter(TimeSpan max)
    LocalOnly()                                 // never L2, no backplane notification

CacheKeyFormat  (static class)
    Separator ':'   TenantMarker '@'
    IsValidServiceName(string?)  → bool         // 1–64 of [a-z0-9._-], starts with [a-z0-9]
    Escape(string part)          → string       // '%'→%25, ':'→%3A, '@'→%40; null/whitespace throws
    BuildKey(service, entity, id, params ReadOnlySpan<string> segments)            → {service}:{entity}:{id}[:{segment}...]
    BuildTenantKey(service, tenantId, entity, id, params ReadOnlySpan<string> segs) → {service}:@{tenant}:{entity}:{id}[...]
    BuildTenantTag(tenantId, tag)  → @{tenant}:{tag}
    BuildTenantWideTag(tenantId)   → @{tenant}
    // every caller-supplied part escaped: no two inputs share a key or tag, a global key never
    // equals a tenant key, one tenant's tag never matches another tenant's entries

ICacheKeyProvider
    BuildKey(string entity, string id, params string[] segments) → string
ITenantCacheKeyProvider : ICacheKeyProvider
    BuildTenantKey(string tenantId, string entity, string id, params string[] segments) → string
    // tenantId is always explicit — never ambient; zero dependency on 12.Security or IHttpContextAccessor

ITenantCacheService   (every method: explicit, non-defaulted tenantId; (entity, id), never a pre-built key)
    TryGetAsync<T>(tenantId, entity, id, ct)                                   → ValueTask<CacheLookup<T>>
    GetOrSetAsync<T>(tenantId, entity, id, Func<CancellationToken, ValueTask<T>>, CachePolicy, ct) → ValueTask<T>
    GetOrSetAsync<T>(tenantId, entity, id, Func<CacheFactoryContext, CancellationToken, ValueTask<T>>, CachePolicy, ct)
    SetAsync<T>(tenantId, entity, id, T value, CachePolicy, ct)               → ValueTask
    RemoveAsync(tenantId, entity, id, ct) / ExpireAsync(tenantId, entity, id, ct) → ValueTask
    RemoveByTagAsync(string tenantId, string tag, CancellationToken ct)       → ValueTask   // tag unscoped, as passed to WithTags
    RemoveTenantAsync(string tenantId, CancellationToken ct)                  → ValueTask   // every entry of the tenant
    // policies passed in must NOT already be tenant-scoped

ICacheWarmupStrategy
    Name string, Order int, WarmupAsync(ICacheService cache, CancellationToken ct) → ValueTask

ICachingBuilder
    Services  IServiceCollection { get; }

IDistributedLockService
    TryAcquireAsync(string resource, DistributedLockOptions? options = null, CancellationToken ct)
                                                                → ValueTask<IDistributedLock?>
    TryAcquireLeaseAsync(string resource, TimeSpan duration, CancellationToken ct)
                                                                → ValueTask<DistributedLease?>   // single attempt
    // null ONLY when another holder has the resource; store unreachable → DistributedLockUnavailableException;
    // cancelled → OperationCanceledException

IDistributedLock : IAsyncDisposable
    Resource string, FencingToken long, IsHeld bool, LostToken CancellationToken
    // kept alive by the provider until disposed; release is idempotent; LostToken is cancelled
    // when the lock is lost OR released

DistributedLockOptions  (sealed record, validated init)
    Expiry (30 s, > 0), WaitTime (0, >= 0), RetryInterval (200 ms, > 0); static Default

DistributedLease  (sealed record)
    ctor(string resource, long fencingToken, DateTimeOffset expiresAt)   // token > 0
    Resource, FencingToken, ExpiresAt   // ExpiresAt as observed by the acquiring process — approximate
    // never released or extended; simply expires

DistributedLockUnavailableException : Exception
    static ForResource(string resource, Exception? inner = null); Resource string?
```

**Fencing contract (stated in the XML docs):** a holder can lose a lock without knowing it. Every lock or lease gets a token strictly greater than every earlier lock or lease on the same resource (gaps are normal). The **protected resource's own write path** must reject a token that is not greater than the last one it accepted — this domain provides the token, never the enforcement. Resource names are global across services; prefix them with the service name unless services must contend.

### `SharedKernel.Caching.Redis.Core` — public surface

```text
ConnectionHealthState  (enum, namespace SharedKernel.Caching.Redis.Core)
    Connected | Reconnecting | Disconnected

RedisConnectionOptions  (sealed class)
    ConnectionString         string                                                        ([Required])
    ConnectTimeoutMs         int                                                           ([Range(100,60000)], default 5000)
    Ssl                      bool                                                          (default false)
    ClientCertificates       X509Certificate2Collection?                                   (default null)
    CertificateValidation    Func<X509Certificate2, X509Chain?, SslPolicyErrors, bool>?     (default null)
    // Registered via AddOptions<RedisConnectionOptions>().Configure(...).ValidateDataAnnotations()
    // .ValidateOnStart(). Ssl/ClientCertificates/CertificateValidation compose into
    // ConfigurationOptions.Ssl and ConfigurationOptions.SslClientAuthenticationOptions before
    // ConnectionMultiplexer.Connect(...). All default to plaintext behaviour.

RedisCircuitBreakerOptions  (sealed class)
    Enabled (false), FailureThreshold ([Range(1,int.MaxValue)], 5), SamplingDuration (10 s),
    BreakDuration (30 s), MinimumThroughput ([Range(1,int.MaxValue)], 3)

RedisConnectionHealthTracker  (sealed class)
    ConnectionHealth    ConnectionHealthState { get; }
    // Wraps IConnectionMultiplexer.ConnectionRestored / ConnectionFailed. internal OnConnectionRestored /
    // OnConnectionFailed exposed via InternalsVisibleTo for tests. Passive observer — no resubscription.

AddRedisConnection(this IServiceCollection, string connectionString,
                    Action<RedisConnectionOptions>? configure = null)  → IServiceCollection
    // TryAddSingleton<IConnectionMultiplexer> (first caller wins) + TryAddSingleton<RedisConnectionHealthTracker>.
    // The multiplexer factory logs a one-time Warning (LoggingEventIdRanges.Caching + 102) for a
    // non-loopback endpoint with Ssl = false — never a thrown exception.

AddRedisCircuitBreaker(this IServiceCollection, Action<RedisCircuitBreakerOptions>? configure = null)
    → IServiceCollection
    // TryAddSingleton<ResiliencePipeline> only when Enabled = true (FailureRatio=1.0 / MinimumThroughput pattern)
```

### `SharedKernel.Caching.Redis.PubSub` / `.Redis.HashStore` — provider contracts

```text
IRedisChannelService  (namespace SharedKernel.Caching.Redis.PubSub)   [ephemeral / at-most-once]
    ConnectionHealth                                                     ConnectionHealthState { get; }
    PublishAsync(string channel, string message, CancellationToken ct)   → ValueTask
    SubscribeAsync(string channel, Func<string, ValueTask> handler, CancellationToken ct) → ValueTask
    UnsubscribeAsync(string channel, CancellationToken ct)               → ValueTask

IRedisHashService  (namespace SharedKernel.Caching.Redis.HashStore)
    GetFieldAsync<T>(string key, string field, JsonTypeInfo<T> typeInfo, CancellationToken ct)        → ValueTask<T?>
    SetFieldAsync<T>(string key, string field, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct) → ValueTask
    GetAllFieldsAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken ct) → ValueTask<IReadOnlyDictionary<string, T>>
    DeleteFieldAsync(string key, string field, CancellationToken ct)                  → ValueTask
    IncrementFieldAsync(string key, string field, long delta, CancellationToken ct)   → ValueTask<long>

ITypedHashStore<T>  (namespace SharedKernel.Caching.Redis.HashStore)   [no per-call JsonTypeInfo<T>]
    GetFieldAsync / SetFieldAsync / GetAllFieldsAsync / DeleteFieldAsync / IncrementFieldAsync (same shapes, no typeInfo)
```

### `SharedKernel.Caching.Redis.DistributedLocking` — public surface

```text
AddRedisDistributedLocking(this ICachingBuilder, string connectionString,
                           Action<RedisLockOptions>? configure = null)      → ICachingBuilder
AddRedisDistributedLocking(this IServiceCollection, string connectionString,
                           Action<RedisLockOptions>? configure = null)      → IServiceCollection
    // Both overloads are supported (neither is obsolete) and register the same services:
    // AddRedisConnection(connectionString, ConnectTimeoutMs), TryAddSingleton(TimeProvider.System),
    // TryAddSingleton<IDistributedLockService, RedisDistributedLockService>. The IServiceCollection
    // overload is the lock-only host entry point (no FusionCache, no ICachingBuilder needed).

RedisLockOptions  (sealed class)
    SectionName = "SharedKernelCaching:DistributedLock"
    ConnectionString  string  ([Required])
    ConnectTimeoutMs  int     ([Range(100, 60000)], default 5000)
```

### `SharedKernel.Caching.FusionCache` — public surface

```text
AddSharedKernelCaching(this IServiceCollection, IConfiguration, Action<CachingOptions>? configure = null) → ICachingBuilder
    // AddValidatedOptions<CachingOptions, CachingOptionsValidator>(configuration, validateDataAnnotations: true)
    // (section SharedKernel:Caching, ValidateOnStart), then Configure(configure) so the delegate runs after binding.
AddSharedKernelCaching(this IServiceCollection, Action<CachingOptions> configure) → ICachingBuilder
    // configure is required. AddOptions().Configure().ValidateDataAnnotations().ValidateOnStart() +
    // TryAddEnumerable(IValidateOptions<CachingOptions>, CachingOptionsValidator) — TryAddEnumerable, not
    // TryAddSingleton, because ValidateDataAnnotations already registered a validator of that service type.
    // Both overloads then register, reading IOptions<CachingOptions> only inside factories (never at registration):
    // CacheSerializationOptions (one JsonSerializerOptions: General defaults, or Combine(SerializerContext,
    // EncryptedCacheEntryJsonContext)); FusionCacheSystemTextJsonSerializer and IFusionCacheSerializer over it;
    // FusionCache with its own MemoryCache (SizeLimit = L1SizeLimit) and WithPostSetup applying ApplyDefaults
    // (Size = 1, DistributedCacheSoft/HardTimeout, FailSafeThrottleDuration) to IFusionCache.DefaultEntryOptions;
    // TryAddSingleton<ICacheService, FusionCacheService>; one CacheKeyProvider behind ICacheKeyProvider AND
    // ITenantCacheKeyProvider (each TryAdd — a consumer may override either).

CachingOptions  (sealed class : ISectionBoundOptions)
    static SectionName => "SharedKernel:Caching"
    ServiceName                 string     (NO default — empty; must satisfy CacheKeyFormat.IsValidServiceName)
    L1SizeLimit                 int        ([Range(1, int.MaxValue)], 10_000 — an entry count)
    WaitForWarmup               bool       (false)
    DistributedCacheSoftTimeout TimeSpan?  (positive; < hard when both set)
    DistributedCacheHardTimeout TimeSpan?  (positive)
    FailSafeThrottleDuration    TimeSpan?  (positive; provider default 30 s)
    SerializerContext           JsonSerializerContext?  (code only)

AddTenantCacheService(this ICachingBuilder)  → ICachingBuilder
    // TryAddSingleton<ITenantCacheService, TenantCacheService>; the key providers come from AddSharedKernelCaching.

TenantCacheService  (internal sealed : ITenantCacheService)
    // key = ITenantCacheKeyProvider.BuildTenantKey(tenantId, entity, id); write policy = policy.ForTenant(tenantId);
    // RemoveByTagAsync → ICacheService.RemoveByTagAsync(CacheKeyFormat.BuildTenantTag(tenantId, tag));
    // RemoveTenantAsync → ICacheService.RemoveByTagAsync(CacheKeyFormat.BuildTenantWideTag(tenantId)).

CacheCompressionOptions  (public sealed class) { ThresholdBytes (1024, > 0), Level (Fastest) }
AddBrotliCompression(this ICachingBuilder, Action<CacheCompressionOptions>? configure) → ICachingBuilder
    // Replaces IFusionCacheSerializer with BrotliCacheSerializer (Inner = the STJ serializer, Options = the settings).
    // Throws InvalidOperationException if CacheEncryptionMarker is already registered (must precede
    // AddCacheEncryption); ArgumentException for ThresholdBytes <= 0.

AddCacheEncryption(this ICachingBuilder)  → ICachingBuilder
    // Guards (InvalidOperationException): ISymmetricEncryptionService registered (names
    // AddSharedKernelCryptography(configuration).AddSymmetricEncryption()); ICacheService registered;
    // IFusionCacheSerializer registered.
    // Unwraps a BrotliCacheSerializer back to its Inner and passes its CacheCompressionOptions to
    // EncryptedCacheService; replaces ICacheService with EncryptedCacheService wrapping the previous
    // registration; registers the internal CacheEncryptionMarker.

EncryptedCacheService  (internal sealed partial : ICacheService)
    // AAD = Encoding.UTF8.GetBytes(key). Stores byte[] = EncryptedPayload.ToBytes() in the wrapped ICacheService.
    // Value → JSON (CacheSerializationOptions.Value) → optional Brotli (BrotliPayloadCodec, configured threshold
    // and level) → EncryptAsync. RemoveAsync/ExpireAsync/RemoveByTagAsync/RemoveByTagsAsync/ClearAsync pass through.

BrotliCacheSerializer / BrotliPayloadCodec (internal static)   // marker bytes 0x42 0x52 ("BR"), shared codec
CacheSerializationOptions (internal sealed) { Value JsonSerializerOptions }
EncryptedCacheEntryJsonContext (internal sealed partial JsonSerializerContext, [JsonSerializable(typeof(byte[]))])
CacheEncryptionMarker (internal sealed)   // registration-time ordering marker only
AddCacheWarmup<TStrategy>(this ICachingBuilder) → ICachingBuilder
    // TryAddEnumerable for the strategy and for the internal CacheWarmupHostedService (IHostedLifecycleService).
```

### `SharedKernel.Caching.Redis` — public surface

```text
AddRedisL2(this ICachingBuilder, string connectionString, Action<RedisL2Options>? configure = null) → ICachingBuilder
    // AddRedisConnection(connectionString, ConnectTimeoutMs); AddStackExchangeRedisCache (InstanceName = KeyPrefix);
    // AddFusionCache().WithRegisteredDistributedCache().WithRegisteredSerializer().WithStackExchangeRedisBackplane(...);
    // AddRedisCircuitBreaker(...) from RedisL2Options.CircuitBreaker.

RedisL2Options  (sealed class)
    SectionName = "SharedKernelCaching:Redis"
    ConnectionString string, KeyPrefix string (""), ConnectTimeoutMs int (5000),
    CircuitBreaker RedisCircuitBreakerOptions { get; }
```

---

## Implementation Rules

### Core cache rules

- `ICacheService` is **always** backed by FusionCache in production — never raw `IMemoryCache` or `IDistributedCache`.
- `CachePolicy` is a sealed, immutable record. Every `With…` method returns a new instance and validates its arguments; equality compares every setting and every tag in order.
- Use `GetOrSetAsync` for stampede protection. `TryGetAsync` followed by `SetAsync` is a bug wherever the value is computed.
- A miss is `CacheLookup<T>.Miss`; a cached `null` or `0` is a hit. Never collapse the two with `default`.
- `SkipCaching()` returns the value to every waiting caller and writes nothing. It is how a failed `Result` avoids being cached while keeping stampede protection (`05.Application`'s `CachingBehavior` does exactly this). `SetDurations` overrides both durations for that one write.
- `ExpireAsync` versus `RemoveAsync`: expire keeps the old value available to fail-safe if recomputing fails; remove drops it (a removed value is never served by fail-safe — `RemoveAsync_FailSafePolicy_FactoryFailureCannotServeRemovedValue`).
- `ClearAsync` calls FusionCache `ClearAsync(allowFailSafe: false)` and logs a `Warning`. It removes every tenant's entries on every instance; use `ITenantCacheService.RemoveTenantAsync` for one tenant.
- Background work (eager refresh, a soft factory timeout) can run the factory after the caller's request ended. A factory that uses request-scoped services (a scoped `DbContext`) must run with `WithoutEagerRefresh()` and no factory timeouts.
- `CachePolicy.NeverExpire` is for reference data that changes only with an explicit `RemoveAsync`/`ExpireAsync`/tag removal. It carries no eager refresh.
- `LocalOnly()` maps to `SkipDistributedCacheRead`/`SkipDistributedCacheWrite`/`SkipBackplaneNotifications`: the entry never reaches Redis and never tells other instances to evict.
- The Redis L2 layer is opt-in. Without `AddRedisL2`, `ICacheService` operates L1-only.
- No static mutable state anywhere in this domain (the static `Meter`/`ActivitySource` instruments are the documented exception).
- **Cross-instance propagation needs no extra wiring.** With `AddRedisL2`, `RemoveAsync`, `ExpireAsync`, `RemoveByTagAsync`/`RemoveByTagsAsync` and `ClearAsync` reach every independently-constructed instance sharing the Redis connection string. `RemoveByTagAsync` is not a per-key operation: FusionCache writes a "clear before this timestamp" marker to a reserved tag key (`{KeyPrefix}v2:__fc:t:{tag}`) and publishes that write on the same backplane channel (`FusionCache.Backplane:v2` for the default cache) as any other `Set`/`Remove`. Every entry carries its creation `Timestamp`; on read, an entry older than its tag's marker is treated as expired. `SkipBackplaneNotifications` (off unless `LocalOnly()`) is the only knob that disables this. Confirmed by Redis `MONITOR` inspection and `CrossInstanceTagInvalidationTests`.

### Key naming rule

- Keys: `{service}:{entity}:{id}[:{segment}...]`; tenant keys: `{service}:@{tenant}:{entity}:{id}[...]`; tenant tags: `@{tenant}:{tag}`; tenant-wide tag: `@{tenant}`. Every caller-supplied part is escaped by `CacheKeyFormat.Escape` (`%`, `:`, `@`).
- `CachingOptions.ServiceName` has **no default**. `CachingOptionsValidator` rejects any value failing `CacheKeyFormat.IsValidServiceName`, and it runs on first `IOptions<CachingOptions>.Value` access and at host start (`ValidateOnStart`). Two services sharing a Redis instance therefore cannot collide on a forgotten default.
- There is one key provider: the internal `CacheKeyProvider` implements both `ICacheKeyProvider` and `ITenantCacheKeyProvider` and reads the single validated `CachingOptions.ServiceName`. There is no second source of the service name anywhere in this domain.
- Build keys through `ICacheKeyProvider`/`ITenantCacheKeyProvider`, never by string interpolation. To change a cached type's shape across a deployment, change the key (`BuildKey("invoice-v2", id)`); there is no policy-level versioning.
- Global tags may not start with `@` (`WithTags` throws), so no global tag can match a tenant tag.

### IRedisChannelService scope constraint

- `IRedisChannelService` is scoped to **cache-adjacent, loss-tolerant ephemeral signaling only**.
- It must never be used for durable, ordered, or guaranteed-delivery messaging — that is `07.Messaging`'s domain. `00.Governance`'s SK0007 flags it in command/event-named classes, including namespace-qualified and `global::` references.
- This constraint is stated in the XML doc on the interface.

### RedisChannelService rules

- Always use `RedisChannel.Literal(channelName)` — never `RedisChannel.Pattern`.
- All handler exceptions are caught and logged at `Error`. They never propagate to the Redis subscriber thread.

### FusionCache STJ serializer AOT rule

- `CachingOptions.SerializerContext` must be set to the microservice's source-generated `JsonSerializerContext` in any NativeAOT build.
- When set, `AddSharedKernelCaching` builds `JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine(SerializerContext, EncryptedCacheEntryJsonContext.Default) }` for FusionCache's STJ serializer and for `CacheSerializationOptions`.
- When absent, FusionCache uses reflection-based STJ (`CacheSerializationOptions` falls back to `JsonSerializerDefaults.Web`) — acceptable for non-AOT builds, breaks NativeAOT.
- `AddSharedKernelCaching` reads `L1SizeLimit` and `SerializerContext` by invoking `configure` on a temporary `CachingOptions` at registration time; values bound later from configuration do not affect them.
- `AddRedisL2` must **not** call `.WithSystemTextJsonSerializer()` — it would overwrite the options-aware registration with a reflection-based default. It uses `.WithRegisteredSerializer()`.

### ITypedHashStore rules

- `ITypedHashStore<T>` is the preferred API for type-specific Redis hash operations in application code. `IRedisHashService` with explicit `JsonTypeInfo<T>` is the low-level primitive — both are valid.
- Register one `ITypedHashStore<T>` per DTO type via `AddTypedHashStore<T>(JsonTypeInfo<T>)`.
- `AddTypedHashStore<T>` throws `InvalidOperationException("AddTypedHashStore<T> requires AddRedisHashService to be called first.")` if `IRedisHashService` is not registered.
- `TypedHashStore<T>` is `internal sealed`.

### RedisHashService rules

- All typed methods accept `JsonTypeInfo<T>` — no `typeof(T)` reflection.
- `IDatabase` is obtained once from `IConnectionMultiplexer.GetDatabase()` and cached.
- Do not compress hash fields; callers who need it compress values before `SetFieldAsync`.

### DI startup guard rules

- Guards use `services.Any(sd => sd.ServiceType == typeof(...))` at registration time and throw `InvalidOperationException`.
- `AddRedisChannelService`: `"AddRedisChannelService requires AddRedisConnection (directly, or transitively via AddRedisL2 / AddRedisDistributedLocking / AddRedisHashService) to be called first to register IConnectionMultiplexer."`
- `AddRedisHashService`: `"AddRedisHashService requires AddRedisConnection (directly, or transitively via AddRedisL2 / AddRedisDistributedLocking / AddRedisChannelService) to be called first to register IConnectionMultiplexer."`
- `AddCacheEncryption` and `AddBrotliCompression` guards: see "Cache-value encryption rules" and "Brotli compression rules".
- `AddRedisL2` and `AddRedisDistributedLocking` take a connection string and register the multiplexer themselves, so they need no guard.

### L1SizeLimit rule

- `CachingOptions.L1SizeLimit` is wired to a dedicated `MemoryCache(new MemoryCacheOptions { SizeLimit = L1SizeLimit })` via `.WithMemoryCache(...)`. Every entry has `Size = 1` (default entry options and `BuildEntryOptions`), so it is an **entry count**, not bytes. Default 10,000; set it explicitly under strict K8s memory limits.
- **L2 key format:** `{KeyPrefix}v2:{user-key}`. The `v2:` separator is injected by `Microsoft.Extensions.Caching.StackExchangeRedis` (v10+) between `InstanceName` (= `KeyPrefix`) and the key. Verified by raw `KeyExistsAsync` against a live Redis container.
- `Microsoft.Extensions.Caching.StackExchangeRedis` stores every L2 entry as a Redis **Hash** (`absexp`/`sldexp`/`data`), never a plain string: raw reads use `IDatabase.HashGetAsync(key, "data")`. The stored JSON is FusionCache's distributed-entry envelope (`Value`/`Timestamp`/`LogicalExpirationTimestamp`/`Tags`/`Metadata`); the cached value is nested in `Value`.

### Brotli compression rules

- Compression is opt-in and L2-only. L1 values are never compressed.
- `AddBrotliCompression()` wraps the registered `IFusionCacheSerializer` with `BrotliCacheSerializer`.
- Compressed payloads carry magic bytes `0x42 0x52` ("BR"); payloads without the prefix pass to the inner serializer unchanged.
- `BrotliEncoder` (not `BrotliStream`) with `ArrayPool<byte>.Shared` for compression — no `MemoryStream` on the hot path. The mechanics live in the internal `BrotliPayloadCodec`, shared with `EncryptedCacheService`.
- Default threshold 1,024 bytes; default level `CompressionLevel.Fastest`.
- protobuf-net is prohibited as a cache serializer: not NativeAOT-compatible, requires `[ProtoContract]` on DTOs, breaks the Redis wire format across deployments.

### Cache-value encryption rules

> **Why a service decorator, not a serializer decorator — do not revert.** `IFusionCacheSerializer` members receive only the value, never the cache key or tags, so a serializer-level decorator cannot bind ciphertext to its key. AAD from `typeof(T)` alone lets a ciphertext written under key `A` decrypt under key `B`; an `AsyncLocal` key channel is not guaranteed to flow into FusionCache's background eager-refresh factory calls. `EncryptedCacheService` decorates `ICacheService`, where the key is an explicit parameter on every member.

- Opt-in via `AddCacheEncryption(this ICachingBuilder)`; disabled by default.
- **AAD is the cache key only** (`Encoding.UTF8.GetBytes(key)`). Tags are unavailable at `TryGetAsync(key)` time, so a tag-inclusive AAD could not be reproduced on decrypt.
- Every member uses `ISymmetricEncryptionService.EncryptAsync`/`DecryptAsync` (async-only), so any `IEncryptionKeyProvider`, KMS-backed included, works.
- **Stored shape:** a `byte[]` per key — `EncryptedPayload.ToBytes()`, `01.Core`'s versioned storage format — written to L2 as a Base64 string (covered by `EncryptedCacheEntryJsonContext`).
- Applies to both L1 and L2, since it sits above `ICacheService`.
- **Decrypt failure** (tamper, wrong key, AAD mismatch — indistinguishable — or a stored value that fails `EncryptedPayload.TryParse`) is logged at `Warning` (`LoggingEventIdRanges.Caching + 15`) and the entry is best-effort evicted (`RemoveAsync`, non-cancellation failures swallowed):
  - `TryGetAsync` returns a miss; `TryGetManyAsync` returns a miss for that key only.
  - `GetOrSetAsync` evicts and recomputes **once through the wrapped service**, so the recompute keeps stampede protection. If the recomputed entry still does not decrypt (another writer keeps storing entries this process cannot read), it logs again and returns a fresh factory value without caching it. It never throws for a corrupt entry and never returns wrong data.
- **Known limitation (accepted):** concurrent readers that all observe the same corrupt entry each evict it and each start the recompute through the wrapped service. Stampede protection collapses calls that overlap, but a reader arriving after one recompute has already stored the fresh entry can trigger one extra recompute. The cost is an extra factory run, never wrong data.
- Built only on `01.Core/SharedKernel.Cryptography` (AES-256-GCM). No other cryptographic primitive or library.
- **Composition order is structurally enforced:** `AddBrotliCompression()` must precede `AddCacheEncryption()` (it throws when the internal `CacheEncryptionMarker` is already registered). When the registered serializer is a `BrotliCacheSerializer`, `AddCacheEncryption()` unwraps it to `.Inner` and passes its `CacheCompressionOptions`; `EncryptedCacheService` then compresses before encrypting and decompresses after decrypting, because compression after encryption is useless. Without encryption, `BrotliCacheSerializer` is untouched.
- `EncryptedCacheService` honours the configured `ThresholdBytes` and `Level`: payloads below the threshold are encrypted uncompressed and carry no marker, so decryption decompresses only marked payloads.
- Entries in an older stored shape fail to parse or decrypt and are handled as decrypt failures (a cold-cache wave on rollout, not data loss).
- Not a substitute for TLS on the Redis connection: encryption protects the value at rest, TLS in transit. Use both for sensitive data.
- Composes with `AddTenantCacheService()` in either order: the tenant-scoped key `TenantCacheService` builds is exactly the AAD, so a payload replayed across tenants for the same `(entity, id)` fails to decrypt.

### STJ serialization rule

- All serialization uses STJ; source-generated contexts for AOT builds.
- A service that trims or publishes as NativeAOT sets `CachingOptions.SerializerContext` (code only) to a context covering every cached type; the same `JsonSerializerOptions` instance serves the distributed serializer and the encryption plaintext.
- `EncryptedCacheEntryJsonContext` covers the stored `byte[]` entry.

### Shared multiplexer rule

- `RedisChannelService`, `RedisHashService`, `RedisDistributedLockService` and the L2 backplane share the single `IConnectionMultiplexer` registered by `AddRedisConnection` (`TryAddSingleton`, first caller wins). No package creates another connection. (The FusionCache backplane and `AddStackExchangeRedisCache` connect with the same connection string through their own libraries.)

### Batch operations rules

- `TryGetManyAsync<T>` returns one `CacheLookup<T>` per **distinct** requested key (duplicates collapse; null/whitespace keys throw `ArgumentException`). An empty input returns an empty dictionary.
- `SetManyAsync<T>` applies one `CachePolicy` to all entries; entry options and tags are computed once before the fan-out.
- **Bounded concurrent execution:** both fan out per-key L2 round-trips via `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 16` (`FusionCacheService.MaxBatchConcurrency`, a fixed internal constant, not a `CachingOptions` knob). `TryGetManyAsync`'s accumulator is a `ConcurrentDictionary` — a correctness requirement.
- **Accepted trade-off:** when one `SetManyAsync` write fails, up to 16 other writes may already be in flight and complete. Callers must not rely on iteration order for fail-fast semantics.
- `EncryptedCacheService.TryGetManyAsync` delegates the batch read to the wrapped service and decrypts sequentially; `SetManyAsync` encrypts sequentially, then delegates one batch write.
- Batch operations never invoke factories, so they cannot interfere with per-key stampede protection (proven by an interleaving regression test).

### Distributed locking rules

- **Two primitives, different lifetimes.** A lock guards a critical section and is kept alive until disposed — always `await using`. A lease claims a resource for a fixed duration and is never released or extended; use it for "this occurrence runs once across replicas" (`19.Scheduling`'s per-occurrence lease).
- **Three outcomes, never conflated.** A lock or lease → acquired. `null` → another holder has the resource (for a lock: for the whole `WaitTime`). `DistributedLockUnavailableException` → the store was unreachable (`RedisException` or `TimeoutException`, wrapped with `ForResource`). An outage must never be reported as contention; with a `WaitTime`, a store failure throws at once instead of waiting.
- **Atomic acquisition and fencing.** One Lua script (`RedisLockScripts.Acquire`): `SET sharedkernel:lock:{resource} <owner> NX PX <ms>` and, only if that succeeded, `INCR sharedkernel:lock-fencing:{resource}`, returning the token (0 when contended). A token can never be issued to a holder that did not acquire, and a contended attempt never advances the counter. Locks and leases share the lock key and the counter, so they exclude each other and their tokens are mutually monotonic. The fencing key never expires. Both keys use the `{resource}` hash tag, so the script is valid on Redis Cluster.
- **Keep-alive and loss.** `RedisDistributedLock` extends every `Expiry / 3` on a `PeriodicTimer(TimeProvider)` with a compare-owner `PEXPIRE` script. The lock is marked lost (`IsHeld = false`, `LostToken` cancelled, `Error` log `Caching + 307`) when an extension finds another owner, or when extensions keep failing until `Expiry` has passed since the last success. Store failures during keep-alive are logged (`Caching + 306`) and retried until then.
- **Release.** `DisposeAsync` is idempotent: it stops the keep-alive, runs a compare-owner `DEL` (never deletes another owner's key; a store failure is logged at `Caching + 305` and the key expires on its own), then cancels `LostToken`. `LostToken` means "lost or released".
- **Owner ids** are fresh `Guid`s per acquisition (`lease:` prefix for leases). Retry waits use `Task.Delay(delay, TimeProvider, ct)`; the retry interval is capped at the remaining wait time.
- **Lease expiry** (`DistributedLease.ExpiresAt`) is `TimeProvider.GetUtcNow()` at request time plus the duration — approximate across machines.
- **Failover caveat:** after a primary failover, a replica that had not received the lock key can grant it again. Fencing tokens are the protection in that window, so the protected write path must check them.
- RedLock.net must not come back: it cannot issue a token in the acquisition step. `00.Governance`'s `RedisTopologyRules.DistributedLockingNeverReferencesRedLock` locks this.

### Channel reconnect rules

- `RedisChannelService` subscribes to `IConnectionMultiplexer.ConnectionRestored` and `ConnectionFailed` in its constructor.
- On `ConnectionRestored`: take the registry lock for the whole replay loop; resubscribe every channel; log per-channel failures at `Error` without aborting the rest; set `ConnectionHealth` to `Connected` before the replay.
- On `ConnectionFailed`: `Connected` if `IConnectionMultiplexer.IsConnected`, otherwise `Reconnecting`.
- `ConnectionHealthState` lives in `SharedKernel.Caching.Redis.Core` (P-547); `IRedisChannelService.ConnectionHealth` is for health checks.
- **Registry:** `Dictionary<string, SubscriptionEntry>` + `_registryLock`, held for the entire replay so a concurrent `SubscribeAsync` cannot insert mid-replay. `SubscribeAsync`/`UnsubscribeAsync` take the same lock.
- `_connectionHealth` is a `volatile int` cast to/from `ConnectionHealthState`.
- `OnConnectionRestored`/`OnConnectionFailed` are `internal` (`InternalsVisibleTo`) so tests simulate events without a live multiplexer.
- This replay logic is independent of `.Redis.Core`'s passive `RedisConnectionHealthTracker`; both may subscribe to the same multiplexer's events.

### Cache warmup rules

- `ICacheWarmupStrategy.WarmupAsync` uses `ValueTask`.
- `CacheWarmupHostedService` (internal, `IHostedLifecycleService`) runs strategies in ascending `Order`, catches per-strategy exceptions, logs at `Error`, and continues — a failed strategy never crashes the pod.
- `AddCacheWarmup<TStrategy>` uses `TryAddEnumerable` for the strategy and for the hosted service (idempotent).
- `CachingOptions.WaitForWarmup = true` runs warmup inside `StartingAsync`, which the host completes for every lifecycle service before any `StartAsync` — including the web server's — so no traffic or readiness arrives until warmup finishes (P-548; the former `StartedAsync` wait ran after the server was already listening). Otherwise `StartAsync` starts warmup in the background and `StopAsync` cancels and awaits it. Cancellation propagates; strategy failures do not.

### Tenant cache service rules

> **`ITenantCacheService` is the recommended entry point for tenant data.** `ITenantCacheKeyProvider` remains available for callers that need raw tenant keys. `ICacheService` stays the entry point for genuinely global data.

- Every method takes an explicit, non-defaulted `tenantId` and `(entity, id)` — never a pre-built key, never resolved from ambient context. Zero dependency on `12.Security` or `IHttpContextAccessor`.
- `TenantCacheService` builds the key with `ITenantCacheKeyProvider.BuildTenantKey` and never formats keys itself.
- **Tag scoping is mandatory:** every write passes `policy.ForTenant(tenantId)`, so tags become `@{tenant}:{tag}` and every entry also carries the tenant-wide tag `@{tenant}`. `RemoveByTagAsync(tenantId, tag)` removes `BuildTenantTag(tenantId, tag)`; `RemoveTenantAsync(tenantId)` removes `BuildTenantWideTag(tenantId)`. Because every part is escaped and global tags cannot start with `@`, two tenants sharing a tag name — or a global tag named like a tenant tag — can never cross-invalidate (a cross-tenant **invalidation** vector, worse than a read leak).
- A policy that is already tenant-scoped throws `InvalidOperationException`: pass the unscoped policy.
- `AddTenantCacheService()` only registers `ITenantCacheService`; the key providers come from `AddSharedKernelCaching()`.

### Polly circuit breaker rules

- `RedisCircuitBreakerOptions.Enabled` defaults to `false` — no Polly types registered, behaviour unchanged.
- `Polly.Core` v8 only — do not add `Microsoft.Extensions.Http.Resilience`.
- The `ResiliencePipeline` singleton is registered only when `Enabled = true`.
- Complementary to FusionCache fail-safe, not a replacement.
- **Count-based semantics via the ratio API:** `FailureRatio = 1.0` and `MinimumThroughput = FailureThreshold`.
- **`BreakDuration` minimum:** Polly v8 enforces 500 ms; tests must not go below it.
- **Optional injection:** `RedisHashService` and `RedisChannelService` resolve `sp.GetService<ResiliencePipeline>()`; `null` means no Polly overhead.
- The FusionCache backplane and `RedisDistributedLockService` do not use the circuit breaker.

### OTel metrics rules

- `Meter("SharedKernel.Caching", <assembly informational version>)`, static readonly on `FusionCacheService`. The name is fixed: `13.ServiceDefaults`' `WithCachingTelemetry` subscribes to it.
- Instruments: `cache.hits`, `cache.misses`, `cache.factory.duration` (ms), `cache.errors`, `cache.evictions`.
- `cache.key_prefix` is `{service}:{entity}` — for a tenant key `{service}:@{tenant}:{entity}:{id}` the tenant segment is dropped (`ExtractKeyPrefix`). Never an id, never a tenant (P-548; tenant ids previously leaked into every metric series).
- Hits come from `Events.Memory.Hit` (`cache.level = l1`) and `Events.Distributed.Hit` (`l2`); evictions from `Events.Memory.Eviction`. Misses are recorded only at the call sites — a `TryGetAsync`/`TryGetManyAsync` miss or a `GetOrSetAsync` factory run — because a memory miss the distributed layer answers is not a miss.
- Logs use `{KeyPrefix}` placeholders, never the full key; tenant tags are logged through `DescribeTag` as `@tenant:{tag}`.
- No `Enabled` guards around recording; no new NuGet dependency.

### OTel tracing rules

- `ActivitySource("SharedKernel.Caching", <assembly informational version>)` — same scope name/version as the `Meter` — static readonly on `FusionCacheService`.
- Spans (`ActivityKind.Client`): `cache.get` (`TryGetAsync`), `cache.set` (`SetAsync`), `cache.get_or_set` (`GetOrSetAsync`). Batch, removal, expire and clear calls have no span.
- Spans start **after** argument validation.
- Tags: `cache.key_prefix` on all three; `cache.outcome` (`"hit"`/`"miss"`) on `cache.get` and `cache.get_or_set` (the latter from a `factoryInvoked` flag set inside the factory).
- On an exception in the factory or `SetAsync`, `activity?.SetStatus(ActivityStatusCode.Error, <exception type name>)` (never the message, which can carry data) before rethrowing, alongside `cache.errors`.
- `13.ServiceDefaults`' `WithCachingTelemetry` registers both the meter and the source by name.

### Redis Connection Core rules

- `SharedKernel.Caching.Redis.Core` is the **single registration point** for `IConnectionMultiplexer`: `AddRedisConnection` → `TryAddSingleton<IConnectionMultiplexer>`, first caller wins.
- `RedisConnectionHealthTracker` is a passive observer; it performs no resubscription.
- `ConnectionHealthState` is declared here (P-547) — `.Redis.Core` references no `SharedKernel.Caching` package, and the abstractions package declares no connection type.
- `AddRedisConnection`/`AddRedisCircuitBreaker` are plain `IServiceCollection` extensions; fluent `ICachingBuilder` chaining belongs to the consuming packages.

#### Fail-fast validation and TLS/mTLS surface

- `AddRedisConnection` wires `RedisConnectionOptions` through `AddOptions<RedisConnectionOptions>().Configure(...).ValidateDataAnnotations().ValidateOnStart()` directly in its method body (visible to `00.Governance`'s `AssertMethodBodyInvokesMethod`). The multiplexer factory resolves the validated `IOptions<RedisConnectionOptions>.Value`. The configure delegate therefore runs lazily, on first options access.
- `Ssl`, `ClientCertificates` and `CertificateValidation` compose into the built `ConfigurationOptions` before `ConnectionMultiplexer.Connect(...)`.
- **StackExchange.Redis 2.13.1 has no `ConfigurationOptions.CertificateSelection`/`CertificateValidation`.** TLS/mTLS goes through `ConfigurationOptions.SslClientAuthenticationOptions` (`Func<string, SslClientAuthenticationOptions>`): `ClientCertificates` → `SslClientAuthenticationOptions.ClientCertificates`; `CertificateValidation` is bridged into `RemoteCertificateValidationCallback` (converting `X509Certificate?` to `X509Certificate2`, rejecting `null`). The delegate is set only when one of the two is supplied; a bare `Ssl = true` keeps StackExchange.Redis's default TLS behaviour.
- A `[LoggerMessage]` `Warning` (`Caching + 102`) — **never an exception**, since sidecar/mesh-terminated TLS is legitimate — fires once per registration, from the multiplexer factory, when an endpoint is non-loopback and `Ssl = false`. The check stops at the first such endpoint.
- `BuildConfigurationOptions`/`WarnIfNonLoopbackWithoutTls`/`IsLoopback` are `internal` for testability without a live Redis.
- TLS protects values in transit; `AddCacheEncryption` protects them at rest. Use both for sensitive data.

### Redis distributed locking package rules

- `SharedKernel.Caching.Redis.DistributedLocking` depends only on `Abstractions` + `Redis.Core` (+ `SharedKernel.Primitives` for EventIds).
- `AddRedisDistributedLocking` exists on `ICachingBuilder` and on `IServiceCollection`; both call one private `Register` and register exactly the same services (`BothOverloads_RegisterTheSameServices`). Neither is obsolete; there is no adapter builder class.
- It sources `IConnectionMultiplexer` via `AddRedisConnection(connectionString, ConnectTimeoutMs)` and registers `TimeProvider.System` only if no `TimeProvider` is registered (tests inject a fake).
- `RedisDistributedLockService`, `RedisDistributedLock` and `RedisLockScripts` are `internal`.

### Redis Hash Store package rules

- `SharedKernel.Caching.Redis.HashStore` depends only on `Abstractions` + `Redis.Core`.
- `IRedisHashService`/`ITypedHashStore<T>` are declared here, in namespace `SharedKernel.Caching.Redis.HashStore` (P-547).
- `AddRedisHashService(this ICachingBuilder)` takes no connection string; it guards on `IConnectionMultiplexer` and resolves the optional `ResiliencePipeline`.

### Redis Pub/Sub package rules

- `SharedKernel.Caching.Redis.PubSub` depends only on `Abstractions` + `Redis.Core` (+ `SharedKernel.Primitives`). It has no hosting dependency: it ships no background service.
- `IRedisChannelService` is declared here, in namespace `SharedKernel.Caching.Redis.PubSub` (P-547).
- `AddRedisChannelService(this ICachingBuilder)` is the only registration; it guards on `IConnectionMultiplexer` and resolves the optional `ResiliencePipeline`.
- There is no cache-invalidation bus or receiver. Cache invalidation across instances is the FusionCache backplane's job; do not rebuild one on this package.

### Redis package topology rules

- **No new `IConnectionMultiplexer` registrations** outside `.Redis.Core`'s `AddRedisConnection`. Every `Add*` that needs a multiplexer calls it (idempotent) — never `ConnectionMultiplexer.Connect(...)` directly.
- **Sibling packages never reference each other.** `.Redis`, `.DistributedLocking`, `.HashStore` and `.PubSub` depend only on `Abstractions` + `Redis.Core`.
- `SharedKernel.Caching.Redis` contains only `AddRedisL2`, `RedisL2Options` and the FusionCache backplane wiring.
- Locked by `00.Governance`'s `RedisTopologyRules` (`RedisCoreNeverReferencesCapabilityPackages`, `CapabilityPackagesNeverReferenceEachOther`, `PubSubNeverReferencesMessaging`, `MessagingNeverReferencesCaching`, `CachingAbstractionsHasNoInfrastructureDependencies`, `CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions`, `CachingAbstractionsDeclaresNoProviderSpecificTypes`, `DistributedLockingNeverReferencesRedLock`).

### AOT compatibility

- FusionCache is AOT-compatible — verify release notes on every upgrade.
- StackExchange.Redis is AOT-compatible; `.Redis.Core` is the sole owner of `ConfigurationOptions.Parse`/`ConnectionMultiplexer.Connect`.
- The lock implementation is plain Lua over `IDatabase.ScriptEvaluateAsync` — no third-party locking library.
- `IRedisHashService` typed methods take `JsonTypeInfo<T>`.
- `AddRedisL2` must not re-register the STJ serializer (it would overwrite the user's `SerializerContext`).

### Logging (EventId sub-blocks)

`02.Caching` owns `LoggingEventIdRanges.Caching` (2000–2999, `SharedKernel.Primitives`). Sub-blocks, 100 per package in Packages-table order:

| Package | Sub-block | Events |
| ------- | --------- | ------ |
| `SharedKernel.Caching.Abstractions` | — | No logging (no logging dependency, locked by the reference allow-list) |
| `SharedKernel.Caching.FusionCache` | `Caching + 0` .. `+ 99` | `CacheWarmupHostedService` `+0`..`+7`; `FusionCacheService` `+10` miss, `+11` set, `+12` factory invoked, `+13` removed, `+14` tag removed, `+16` expired (Debug), `+17` cleared (Warning); `EncryptedCacheService` `+15` decrypt failed (Warning) |
| `SharedKernel.Caching.Redis.Core` | `Caching + 100` .. `+ 199` | `RedisConnectionHealthTracker` `+100` restored, `+101` failed; `+102` non-loopback endpoint without TLS (Warning) |
| `SharedKernel.Caching.Redis` (L2) | `Caching + 200` .. `+ 299` | Reserved — no logging today |
| `SharedKernel.Caching.Redis.DistributedLocking` | `Caching + 300` .. `+ 399` | `RedisDistributedLockService` `+300` lock acquired, `+301` lock contended, `+302` lease acquired, `+303` lease contended (Debug); `RedisDistributedLock` `+304` released (Debug), `+305` release failed (Warning), `+306` extend failed (Warning), `+307` lock lost (Error) |
| `SharedKernel.Caching.Redis.HashStore` | `Caching + 400` .. `+ 499` | Reserved — no logging today |
| `SharedKernel.Caching.Redis.PubSub` | `Caching + 500` .. `+ 599` | `RedisChannelService` `+500` handler exception, `+501` connection restored, `+502` channel resubscribed, `+503` resubscription failed, `+504` connection failed |

- Every `EventId` is written `LoggingEventIdRanges.Caching + {offset}` — never a bare literal.
- The four logging packages (`FusionCache`, `Redis.Core`, `Redis.DistributedLocking`, `Redis.PubSub`) reference `SharedKernel.Primitives` for the constant.
- Every log statement uses the `[LoggerMessage]` source-generated pattern in a nested `private static partial class Log`; no direct `ILogger.LogX` call and no `LoggerMessage.Define` (SK0020/SK0021, `LoggingEventIdIntegrityAssertion`).
- Placeholders are PascalCase named properties. Lock logs carry the resource name and fencing token, never secrets.

---

## DI Registration (current shape)

```csharp
// L1-only (non-AOT) — ServiceName is required and validated at startup
services.AddSharedKernelCaching(o => o.ServiceName = "my-service");

// L1-only (NativeAOT — source-generated context for all cached types)
services.AddSharedKernelCaching(o =>
{
    o.ServiceName = "my-service";
    o.SerializerContext = MyAppSerializerContext.Default;
});

// L1 + L2 (Redis distributed layer + backplane)
services.AddSharedKernelCaching(o =>
         {
             o.ServiceName = "my-service";
             o.SerializerContext = MyAppSerializerContext.Default;
         })
        .AddRedisL2(connectionString, o => o.KeyPrefix = "prod:");

// Tenant-scoped cache — the recommended entry point for tenant data
services.AddSharedKernelCaching(o => o.ServiceName = "my-service")
        .AddTenantCacheService();
// Inject: ITenantCacheService → GetOrSetAsync(tenantId, entity, id, factory, policy, ct)
//         ITenantCacheKeyProvider / ICacheKeyProvider are registered by AddSharedKernelCaching

// Brotli compression for large L2 payloads
services.AddSharedKernelCaching(o => o.ServiceName = "my-service")
        .AddRedisL2(connectionString)
        .AddBrotliCompression(o => o.ThresholdBytes = 2048);

// Cache-value encryption at rest — cryptography and the cache first; Brotli before encryption
services.AddSingleton<IEncryptionKeyProvider>(keyProvider);    // any provider, KMS-backed included
services.AddSharedKernelCryptography(configuration)
        .AddSymmetricEncryption();                             // 01.Core — ISymmetricEncryptionService
services.AddSharedKernelCaching(o => o.ServiceName = "my-service")
        .AddRedisL2(connectionString)
        .AddBrotliCompression(o => o.ThresholdBytes = 2048)  // compression first
        .AddCacheEncryption();                                 // encryption last

// Distributed locking alongside the cache
services.AddSharedKernelCaching(o => o.ServiceName = "billing")
        .AddRedisL2(connectionString)
        .AddRedisDistributedLocking(connectionString);

// Lock-only host — no FusionCache, no ICachingBuilder
services.AddRedisDistributedLocking(connectionString, o => o.ConnectTimeoutMs = 3000);
// await using IDistributedLock? handle = await locks.TryAcquireAsync(resource,
//     new DistributedLockOptions { Expiry = TimeSpan.FromSeconds(30), WaitTime = TimeSpan.FromSeconds(5) }, ct);
// DistributedLease? lease = await locks.TryAcquireLeaseAsync(resource, TimeSpan.FromMinutes(5), ct);
// Both return null only on contention; an unreachable store throws DistributedLockUnavailableException.

// Redis Pub/Sub channel service (requires IConnectionMultiplexer)
services.AddSharedKernelCaching(o => o.ServiceName = "my-service")
        .AddRedisL2(connectionString)
        .AddRedisChannelService();

// Redis hash service — typed per DTO (AOT-safe)
services.AddSharedKernelCaching(o =>
         {
             o.ServiceName = "session-service";
             o.SerializerContext = MyAppSerializerContext.Default;
         })
        .AddRedisL2(connectionString)
        .AddRedisHashService()
        .AddTypedHashStore(MyAppSerializerContext.Default.OrderDto)
        .AddTypedHashStore(MyAppSerializerContext.Default.CustomerDto);
// Inject: IRedisHashService, ITypedHashStore<OrderDto>, ITypedHashStore<CustomerDto>

// Hash store or pub/sub without FusionCache: register the connection, then use any ICachingBuilder
services.AddRedisConnection(connectionString);
var cachingBuilder = new MyCachingBuilder(services);   // a local ICachingBuilder wrapping `services`
cachingBuilder.AddRedisHashService().AddTypedHashStore(MyAppSerializerContext.Default.SessionDto);
cachingBuilder.AddRedisChannelService();

// Cache warmup
services.AddSharedKernelCaching(o => { o.ServiceName = "my-service"; o.WaitForWarmup = true; })
        .AddCacheWarmup<MyProductCatalogWarmup>()
        .AddCacheWarmup<MyUserPreferencesWarmup>();

// Redis L2 with circuit breaker
services.AddSharedKernelCaching(o => o.ServiceName = "my-service")
        .AddRedisL2(connectionString, o =>
        {
            o.CircuitBreaker.Enabled = true;
            o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        });

// Standalone connection core
services.AddRedisConnection(connectionString, o => o.ConnectTimeoutMs = 5000);
services.AddRedisCircuitBreaker(o => { o.Enabled = true; o.FailureThreshold = 5; });
// Inject: IConnectionMultiplexer, RedisConnectionHealthTracker, ResiliencePipeline (when enabled)

// Redis connection with TLS — composes into ConfigurationOptions.SslClientAuthenticationOptions
services.AddRedisConnection(connectionString, o =>
{
    o.Ssl = true;
    o.CertificateValidation = (cert, chain, errors) => errors == SslPolicyErrors.None;
});
```

---

## Test Rules

- Every package owns a nested `.Tests` project; tests are partitioned by capability, not duplicated:
  - `SharedKernel.Caching.Abstractions.Tests` (78) — `CachePolicyTests` (presets, validation, `ForTenant`, equality), `CacheKeyFormatTests` (escaping, service-name rule, no-collision properties: a global key never equals a tenant key, a separator inside a part never collides, tenant tags never collide), `CacheLookupTests`, `CacheFactoryContextTests`, `DistributedLockContractTests` (options, lease, exception). No provider reference.
  - `SharedKernel.Caching.FusionCache.Tests` (198) — `FusionCacheServiceTests`, `Policies/BuildEntryOptionsTests` (every `CachePolicy` setting → `FusionCacheEntryOptions`, `ApplyFactoryDecision`), `BatchOperationsTests`, `NullableFactoryTests`, `TenantCacheServiceTests`, `CacheKeyProviderTests`, `DI/CachingDiRegistrationTests`, `Encryption/EncryptedCacheServiceTests`, `Serialization/*`, `SerializerContextTests`, `L1SizeLimitTests`, `CacheWarmupTests`, `OtelMetricsTests`, `OtelTracingTests`.
  - `SharedKernel.Caching.Redis.Tests` (34) — L2 round-trip and stampede protection, batch integration, circuit breaker, L1 fallback, `KeyPrefix`, `Integration/CacheEncryptionAtRestTests`, `Integration/CrossInstanceTagInvalidationTests`.
  - `SharedKernel.Caching.Redis.DistributedLocking.Tests` (47) — `Abstractions/IDistributedLockServiceContractTests` (abstract contract base, run against real Redis by `RedisDistributedLockServiceContractTests`), `RedisDistributedLockTests` (raw key/TTL, keep-alive, loss, owner-safe release, fencing counter), `LockStoreUnavailableTests`, `DI/RedisDistributedLockingExtensionsTests`.
  - `SharedKernel.Caching.Redis.HashStore.Tests` (30) — `RedisHashService` and `ITypedHashStore<T>` integration, DI guard and `ResiliencePipeline` injection.
  - `SharedKernel.Caching.Redis.PubSub.Tests` (18) — publish/subscribe round-trip, unsubscribe, handler exceptions swallowed, reconnect/resubscribe, DI guard and `ResiliencePipeline` injection.
  - `SharedKernel.Caching.Redis.Core.Tests` (55) — connection registration (first caller wins), health transitions via internal event handlers, circuit breaker, options validation, TLS composition.
- Redis integration tests **must** use Testcontainers (`16.Testing/SharedKernel.Testing`), never an external Redis. `Testcontainers.Redis` stays on the same version as `16.Testing` (4.13.0); a lower direct pin resolves a mismatched `DotNet.Testcontainers` assembly and throws `MissingMethodException` at container start.
- A test project that needs an `ICachingBuilder` without `.FusionCache` uses its own small `TestCachingBuilder` — never a reference to a sibling provider package.
- **Hit versus miss** must be covered: a cached `null` and a cached `0` are hits (`TryGetAsync_DistinguishesMiss_FromCachedNull_AndCachedZero`, `NullableFactory_*`).
- **Stampede protection** must be covered: parallel `GetOrSetAsync` calls invoke the factory exactly once (L1 and with Redis L2), including when interleaved with batch calls.
- **Factory context** must be covered: `SkipCaching` returns the value and stores nothing; skipping for some calls still caches the others; `SetDurations` overrides expire independently; a throwing factory caches nothing and propagates.
- **Expire versus remove under fail-safe:** after `ExpireAsync`, a failing factory serves the old value; after `RemoveAsync`, it cannot.
- **Service name validation:** an unset or invalid `ServiceName` fails options resolution, key-provider resolution and host start; a valid one passes.
- **Tenant isolation** must be covered at both layers: same `(entity, id)` for two tenants never collides; tenant A's tag removal never evicts tenant B's entries; a global `RemoveByTagAsync` with the same tag name — or a tag named like a tenant tag — never evicts tenant entries; `RemoveTenantAsync` removes only that tenant, including entries written through `GetOrSetAsync`; an already tenant-scoped policy throws.
- **Locks** must cover: free resource → held handle with a positive token; contended → `null` (with zero wait immediately, with a shorter wait after waiting, with a longer wait acquiring after release); parallel attempts → exactly one winner (locks and leases); tokens strictly increase across locks and leases and a stale holder is rejected by a guard that accepted a newer token; contended attempts do not advance the counter; counters are independent per resource; dispose is idempotent, deletes only its own key and keeps the counter; a deleted or foreign-owned key is reported lost; a held lock outlives its expiry while kept alive; an unreachable store throws `DistributedLockUnavailableException` for locks, leases and waiting attempts, and a held lock whose store stays down past its expiry is reported lost; cancellation throws.
- **L1-only fallback:** the cache works with no Redis configured.
- **Batch operations:** mixed hits/misses; set-many then get-many; empty key list → empty dictionary; duplicate keys → one lookup per distinct key; single policy (tag) applies to every entry; 200 keys under bounded concurrency lose or duplicate nothing; a 50-key batch against real Redis completes in ≤ 75% of 50 sequential `TryGetAsync` calls (CI-tolerant margin).
- **Cache encryption** (`EncryptedCacheServiceTests`, 27): round-trip; a payload replayed under a different key fails authentication, is a miss and is evicted; tampered ciphertext and a non-payload value are misses and are evicted; `TryGetManyAsync` maps a decrypt failure and an absent key to misses while a valid key decrypts; a corrupted entry in `GetOrSetAsync` is evicted and recomputed once through the inner service (also with a real FusionCache); `SkipCaching` passes through; the stored entry is `EncryptedPayload.ToBytes()` bound to the key; every member round-trips with an async-only key provider; the wrapped factory re-invoked from an unrelated execution context still encrypts correctly; compress-then-encrypt produces shorter ciphertext; `AddBrotliCompression().AddCacheEncryption()` unwraps the serializer and round-trips, the reverse order throws; each registration guard throws; with `AddTenantCacheService()`, a cross-tenant replay for the same `(entity, id)` fails. `CacheEncryptionAtRestTests` proves a raw Redis read (`HashGetAsync(key, "data")`) contains neither the plaintext nor the DTO property names, and the envelope's `Value` is Base64 that parses with `EncryptedPayload.TryParse`.
- **Cross-instance propagation** (`CrossInstanceTagInvalidationTests`): two fully independent `ServiceProvider`s (own multiplexer, own L1, own `IFusionCache`) share one Testcontainers Redis and the same `ServiceName` — never two scopes of one container, which could pass with the guarantee broken. `RemoveByTagAsync`, `RemoveAsync` and `ExpireAsync` on instance A make instance B recompute; a `LocalOnly()` entry on A is neither written to Redis nor visible on B. Assertions bounded-poll (100 ms interval, 5 s timeout), never a fixed `Task.Delay`.
- **Lock expiry is a real Redis TTL**, so expiry tests wait real time: short durations plus a small margin. Loss-detection tests wait on `LostToken` with a timeout (`Task.Delay(timeout, handle.LostToken)`), never an unbounded wait.
- **OTel tests:** `OtelMetricsTests` verifies each instrument via `MeterListener`; `OtelTracingTests` verifies span name, `ActivityKind.Client`, `cache.key_prefix`/`cache.outcome` and the source's name/version. Assertions are existence-style (`Assert.Contains`, `>= 1`), because other test classes run concurrently against the same static instruments.
- **Listener-callback accumulators must be thread-safe.** `MeterListener`/`ActivityListener` callbacks run on whatever thread records the measurement or stops the activity, so two concurrent test classes can call one test's callback from two threads. A plain `Dictionary` read-then-write threw `InvalidOperationException` from unrelated test classes (~25% of runs) until switched to `ConcurrentDictionary.AddOrUpdate`; spans are collected in a `ConcurrentBag<Activity>`. Use `ConcurrentDictionary`/`ConcurrentBag`/`ConcurrentQueue` in any listener-based test.
- **Circuit breaker tests:** opens after the threshold, short-circuits when open, closes after the break duration; `Enabled = false` registers nothing.
- **Redis connection hardening** (`RedisConnectionValidationTests`/`RedisTlsConfigurationTests`): invalid options throw `OptionsValidationException` via `IStartupValidator.Validate()` and on first `IOptions<T>.Value` access; TLS settings compose into `SslClientAuthenticationOptions`; the non-loopback warning fires exactly once and never for loopback or `Ssl = true`.
- **Channel reconnect:** forcibly kill the subscriber connection on a live container; messages are delivered after reconnect; the resubscription count equals the pre-disconnect count; a partial resubscription failure still resubscribes the remaining channels.
- **Cross-package composition:** a container calling only `AddRedisConnection` + one of `AddRedisDistributedLocking`/`AddRedisHashService`/`AddRedisChannelService` resolves (proven end to end by `consumer-verify` surfaces 3–5).
- **Cache warmup:** ascending order, a failed strategy does not abort the others, `WaitForWarmup` finishes warmup in `StartingAsync` before any hosted service starts, background warmup is cancelled on stop, registration is idempotent.

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
- [2026-07-29] Phases 38–41 planned (WO-050) — four independent phases dispatched from a direct root architecture review (P-301–P-304), none blocking on each other. Ph38 (NuGetPackagingParity): confirmed via direct csproj inspection that `Abstractions` carries only minimal NuGet metadata (no license/repo/copyright/`PackageReadmeFile` block, plus a mangled em-dash encoding defect) while `.Redis.Core`/`.DistributedLocking`/`.HashStore`/`.PubSub` already carry full `08.Storage`-bar metadata but no `README.md` of the six that lack one, and only `.FusionCache`/`.Redis` (L2) ship a README today; also confirmed `consumer-verify` does not compile — it imports namespaces retired at the Phase 14 rename/WO-023 split and references the retired pre-rename `PackageId`s, using `BuildServiceProvider()` only. Full 5-README + full-metadata + 5-surface `IHost.StartAsync()`-based `consumer-verify` rebuild designed (12 tasks NP-01→NP-12), deliberately keeping `PackageReference`-against-local-nupkg-feed rather than `08.Storage`'s `ProjectReference` pattern, since proving the packed artifact resolves is the whole point. Ph39 (CrossInstanceTagInvalidation): designed a genuine two-independent-DI-container test (own multiplexer/L1/`IFusionCache` each, same `ServiceName`, one shared Testcontainers Redis) to prove `RemoveByTagAsync` propagates cross-pod — outcome (already-correct vs. needs-a-wiring-fix) deliberately left open for the implementer to determine (10 tasks CTI-01→CTI-10). Ph40 (BatchOperationsParallelization): grep-confirmed the Phase 22 `IRedisL2BatchService`/`RedisL2BatchService` pipeline helper has zero DI registration and zero production caller — dead code, and architecturally unreachable from `FusionCacheService` regardless under the `.FusionCache`/`.Redis` sibling non-reference rule (Phase 17) plus its own `internal` visibility; designed its retirement paired with a bounded `Parallel.ForEachAsync` (`MaxDegreeOfParallelism = 16`) rewrite of `GetManyAsync`/`SetManyAsync` over a `ConcurrentDictionary` accumulator (10 tasks BP-01→BP-10). Ph41 (OtelTracingSpans): designed a companion `ActivitySource("SharedKernel.Caching", "1.0")` alongside the existing Phase 31 `Meter`, producing `cache.get`/`cache.set`/`cache.get_or_set` spans tagged `cache.key_prefix` + `cache.outcome`, zero new NuGet dependency (9 tasks OT-01→OT-09). "Current Phase", core cache rules, batch-operations rules, and OTel-metrics-adjacent sections all gained forward-looking "(Phase N, planned)" annotations; full task breakdowns, implementation rules, file-level plans, and acceptance criteria recorded in `02.Caching/state-map.md` (caching-arch-planner, WO-050)
- [2026-07-29] Phase 38 (WO-050, P-301, NuGetPackagingParity) implemented — all 12 tasks (NP-01→NP-12) complete. `SharedKernel.Caching.Abstractions.csproj` brought to full metadata parity (`Company`/`Product`/`PackageLicenseExpression=MIT`/`PackageReadmeFile`/`RepositoryType`/`RepositoryUrl`/`PackageProjectUrl`/`Copyright`/`GenerateDocumentationFile`/`IncludeSymbols`/`SymbolPackageFormat=snupkg` added) and its two mangled `â€”` inline-comment encoding defects fixed to a real em dash. Five `README.md` files authored (`Abstractions`, `.Redis.Core`, `.Redis.DistributedLocking`, `.Redis.HashStore`, `.Redis.PubSub`), each wired via `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="\" />` in their respective csproj (the latter four's metadata block was otherwise already complete from Phases 32-36). All 7 packages packed clean to the repo-root `./nupkgs` local feed — zero `NU5039`/`NU5128` warnings; this refreshed two pre-existing stale artifacts in that feed (`SharedKernel.Caching.1.0.0.nupkg`, the retired pre-rename `PackageId` with content dating to before Phase 14, and `SharedKernel.Caching.Redis.1.0.0.nupkg`, stale content dating to before the WO-023 split) — **discovered mid-implementation that the local NuGet global-packages cache (`~/.nuget/packages`) had these same-version packages cached from that earlier era, so a same-version re-pack was silently ignored by `dotnet restore` until the eight `sharedkernel.caching*` folders were manually cleared from the cache** — a NuGet same-version-immutability gotcha worth knowing for any future re-pack of an already-cached version, not something `dotnet pack`/`NuGet.Config` can prevent on their own. `consumer-verify` fully rewritten: `SharedKernel.Caching.ConsumerVerify.csproj` now references all 7 current `PackageId`s directly (at `1.0.0`, against the local feed — `PackageReference`, never `ProjectReference`, a deliberate divergence from `08.Storage`'s pattern) plus `Microsoft.Extensions.Hosting` `10.0.9` and `Testcontainers.Redis` `4.13.0`; `Program.cs` rewritten from scratch as 5 `Surface_*` functions (L1-only, L1+L2, locking-only, hash-store-only, pub/sub-only) against current namespaces, each composing DI through a real `Host.CreateApplicationBuilder()` → `IHost.StartAsync()`, sharing one Testcontainers Redis container started once and disposed at the end of the run, with a `RunSurfaceAsync`/`Verify` fail-fast-with-label harness so one surface's failure never blocks the other four from running and reporting. Ran end-to-end against a real Docker daemon: all 5 surfaces PASS, process exits 0. `state-map.md`'s `SK.02.Published` phase gained a forward-pointer note to Phase 38 as its 7-package packaging successor. "Current Phase", the `Packages` section's NuGet-packaging-status paragraph, and a new consumer-verify 5-surface composition-matrix subsection all updated to reflect completion (caching-phase-implementer)
- [2026-07-29] Phase 40 (WO-050, P-303, BatchOperationsParallelization) implemented — all 10 tasks (BP-01→BP-10) complete. `FusionCacheService.GetManyAsync<T>`/`SetManyAsync<T>` rewritten from a strictly sequential `foreach (var key in keys) { await ... }` loop to a bounded `System.Threading.Tasks.Parallel.ForEachAsync` fan-out (new `private const int MaxBatchConcurrency = 16` field, deliberately not exposed via `CachingOptions`); `GetManyAsync`'s accumulator switched from `Dictionary<string, T?>` to `System.Collections.Concurrent.ConcurrentDictionary<string, T?>` (a correctness fix — the sequential code was only thread-safe because it was single-threaded). `SharedKernel.Caching.Redis/Batch/IRedisL2BatchService.cs` and `Batch/RedisL2BatchService.cs` deleted outright (confirmed dead: zero DI registration, zero production caller since Phase 22, architecturally unreachable from `.FusionCache` under the Phase 17 sibling non-reference rule); the now-empty `Batch/` folder and the `RedisL2BatchService`-only `InternalsVisibleTo` grant in `SharedKernel.Caching.Redis.csproj` were removed in the same pass. `BatchOperationsIntegrationTests.cs`'s three BA-08 pipeline-verification tests (which directly exercised the deleted internal type via Redis `INFO stats total_commands_processed`) were removed along with the `_multiplexer`/`_adminMultiplexer` fields and `GetTotalCommandsProcessedAsync` helper that existed solely to support them; all pre-existing `GetManyAsync`/`SetManyAsync` functional-correctness tests were retained unchanged. Five new tests added: a stampede-protection-under-concurrency regression (`BatchOperationsTests.GetOrSetAsync_StampedeProtection_UnaffectedByInterleavedBatchOperations`, `.FusionCache.Tests`) proving 20 concurrent `GetOrSetAsync` calls on one key still invoke the factory exactly once when interleaved with an unrelated 20-entry batch call; a 200-key `ConcurrentDictionary`-safety test and a 50-key-vs-50-sequential-`GetAsync` wall-clock comparison (≤ 75% of baseline wall-clock time, a CI-tolerant margin) in `.Redis.Tests` against a real Testcontainers Redis; and one explicit Phase-22-contract regression pair (empty-input short-circuit + single-`CachePolicy`-for-all-entries, the latter deliberately batching 20 entries — beyond `MaxDegreeOfParallelism` — under one tag) added to **both** `.FusionCache.Tests` and `.Redis.Tests`, since both were already covered incidentally but the phase required explicit non-incidental proof. Full regression run, all green: FusionCache 212/212 (209 pre-existing + 3 new), Redis (L2) 29/29 (29 pre-existing − 3 removed BA-08 tests + 3 new). "Current Phase", "Batch operations rules" (renamed to note the Phase 40 execution-model correction, documenting the accepted partial-in-flight-on-failure trade-off), and Test Rules all updated to reflect completion (caching-phase-implementer)
- [2026-07-29] Phase 39 (WO-050, P-302, CrossInstanceTagInvalidation) implemented — all 10 tasks (CTI-01→CTI-10) complete. New `SharedKernel.Caching.Redis.Tests/Integration/CrossInstanceTagInvalidationTests.cs` builds two fully independent `ServiceCollection`/`ServiceProvider` pairs (never two scopes of one container) — each with its own `IConnectionMultiplexer` (via its own `AddRedisConnection` call inside `AddRedisL2`), its own L1 `MemoryCache`, and its own `IFusionCache` — both pointed at one shared Testcontainers Redis connection string and the same `CachingCoreOptions.ServiceName`. Seeds a tagged entry via instance A, reads it through on instance B (populating B's L1 without invoking the factory), tag-invalidates via instance A's `RemoveByTagAsync` only, then bounded-polls (100 ms interval, 5 s timeout — same tolerance as the Phase 23 renewable-lock precedent) instance B's `GetOrSetAsync` with a new distinguishable factory value until it is observed. **Ran against the current, unmodified implementation first, per the phase's explicit instruction not to assume an outcome — the guarantee already held; zero changes to `AddRedisL2`/`AddSharedKernelCaching`/`FusionCacheService` were needed.** Confirmed the carrying mechanism empirically, not by reading FusionCache source alone: attached `redis-cli monitor` to a fixed-port diagnostic Redis container (via a throwaway test, deleted after use) and observed that `RemoveByTagAsync` writes an ordinary `HMSET` to a reserved per-tag key (`{KeyPrefix}v2:__fc:t:{tag}`, value = a "clear before this timestamp" marker) and `PUBLISH`es that write to the same shared backplane channel (`FusionCache.Backplane:v2`, for the default/unnamed cache) as any other `Set` — i.e., tag invalidation rides the identical L2-write-plus-backplane-broadcast path `AddRedisL2` already wires for regular data, with no separate tagging-specific channel, option, or opt-in. **Discovered and fixed a genuine pre-existing regression blocking the new test from ever running**: all four Redis-container-based `.Tests` projects (`.Redis`, `.DistributedLocking`, `.HashStore`, `.PubSub`) carried a stale direct `<PackageReference Include="Testcontainers.Redis" Version="4.4.0" />` that silently conflicted with `16.Testing`'s `>= 4.13.0` floor (bumped there, and in Phase 38's `consumer-verify`, at some point after these four `.Tests.csproj` files were authored) — MSBuild resolved the lower direct reference into the test output alongside the higher transitive core `DotNet.Testcontainers` assembly, producing a `MissingMethodException` in `RedisConfiguration`'s constructor at container-startup time; reproduced identically on the pre-existing, untouched `RedisL2IntegrationTests` before any fix, confirming this was not caused by the new test. Fixed by bumping all four `.Tests.csproj` files to `Testcontainers.Redis` `4.13.0`, matching `16.Testing` and `consumer-verify`; zero production code touched by this fix. Full regression run after both fixes, all green: FusionCache 209/209 (unchanged, confirms Phase 6/7/22 single-instance `RemoveByTagAsync` tests unaffected), Redis.Core 33/33, Redis (L2) 29/29 (28 pre-existing + 1 new), Redis.DistributedLocking 41/41, Redis.HashStore 30/30, Redis.PubSub 41/41. "Current Phase", the "Core cache rules" bullet (replaced the "(Phase 39, planned)" placeholder with the proven guarantee + mechanism), and Test Rules (new package-list entry plus a dedicated cross-instance test-rule bullet with the timeout justification) all updated (caching-phase-implementer)
- [2026-07-29] Phase 41 (WO-050, P-304, OtelTracingSpans) implemented — all 9 tasks (OT-01→OT-09) complete, the last of the four WO-050 follow-up phases. Added `private static readonly ActivitySource _activitySource = new("SharedKernel.Caching", "1.0")` to `FusionCacheService` alongside `_meter` (same instrumentation-scope name/version, deliberately), with a combined XML doc `<remarks>`. `GetAsync<T>`/`SetAsync<T>`/`GetOrSetAsync<T>` each wrapped in a `cache.get`/`cache.set`/`cache.get_or_set` span (`ActivityKind.Client`), started after argument validation, tagged `cache.key_prefix` (via the existing `ExtractKeyPrefix`) and, for the two read paths, `cache.outcome` (`"hit"`/`"miss"` — for `GetAsync` from the existing `result.HasValue` check, for `GetOrSetAsync` from a new local `factoryInvoked` flag set inside the existing factory lambda alongside `Log.FactoryInvoked`). `SetAsync`/`GetOrSetAsync`'s existing `catch` blocks (which already increment `_cacheErrors`) now also call `activity?.SetStatus(ActivityStatusCode.Error, ex.Message)` before rethrowing. Zero new NuGet dependency — `ActivitySource`/`ActivityKind`/`ActivityStatusCode` are `System.Diagnostics` BCL, already imported for `Stopwatch`. New `OtelTracingTests.cs` (6 tests) mirrors `OtelMetricsTests.cs` with `ActivityListener` in place of `MeterListener` — first pass used `Assert.Single(activities, predicate)` and failed intermittently under xUnit's default cross-class test parallelism (a concurrently-running test class's own `GetOrSetAsync` calls landed in the same process-wide listener); fixed with existence-style `Assert.True(HasMatchingSpan(...))` assertions, mirroring `OtelMetricsTests.cs`'s own `>= 1` tolerance for the identical reason. Full regression green: FusionCache 218/218 (212 prior + 6 new), re-run 3× with no flakiness. "Current Phase", "OTel tracing rules" (promoted from "(Phase 41, planned)" to a shipped rule), Package Board, and Test Rules all updated to reflect completion (caching-phase-implementer)
- [2026-07-29] Brain sync pass (post-Phase 41) — added a "OTel tracing (cache read/write spans)" row to the Technology Stack table (`System.Diagnostics` `ActivitySource`/`Activity`, BCL, `.FusionCache`, Phase 41), closing the same documentation gap the sibling "OTel tracing (invalidation receiver)" row already fills for `.Redis.PubSub`; confirmed no other section (Packages, Interface Contracts, DI Registration) needs a Phase 41 update — no new package, interface, or DI registration signature was introduced. Confirmed no root `CLAUDE.md` change is warranted: Phase 41, like the other three WO-050 follow-up phases (38–40), is purely additive to an existing package with no new package/interface/DI convention/layering exception, consistent with those phases receiving no root changelog entry either. The `ActivityListener`-cross-class-parallelism test hazard was evaluated for a cross-domain note in `16.Testing`/`00.Governance` and judged already adequately covered by existing precedent (`OtelMetricsTests.cs`'s own `>= 1` tolerance, `16.Testing`'s existing `ActivityRecorder` helper) — no new brain file created for it (agent)
- [2026-07-29] Post-brain-sync regression discovery and fix — a 10-run repeat of the full `SharedKernel.Caching.FusionCache.Tests` suite surfaced an intermittent (~25%) `InvalidOperationException: Operations that change non-concurrent collections must have exclusive access`, thrown from unrelated test classes (`NullableFactoryTests` observed, but any class recording against the meter could trigger it). Root-caused via a git-stash A/B comparison to a genuine, previously-dormant thread-safety bug in `OtelMetricsTests.cs`'s pre-existing `BuildListener` helper: its accumulator was a plain `Dictionary<string, long>`/`Dictionary<string, double>` updated via a non-atomic read-then-write, and `MeterListener` measurement callbacks can run on whatever thread completes the recording — under xUnit's cross-class parallelism, two threads could call the same dictionary's `set_Item` concurrently. This bug pre-dates Phase 41; Phase 41's `OtelTracingTests.cs` added enough extra concurrent cache traffic against the same static meter to raise the collision probability into visibility (10/10 clean before `OtelTracingTests.cs` existed, ~25% flake after, 0/10 flakes after this fix). Fixed `OtelMetricsTests.cs` to use `ConcurrentDictionary<string, long>`/`ConcurrentDictionary<string, double>` with atomic `AddOrUpdate`; `OtelTracingTests.cs`'s own accumulator was written as a `ConcurrentBag<Activity>` from the start for the same reason, never a plain `List<Activity>`. New Test Rules bullet added documenting the general rule for future `MeterListener`/`ActivityListener` tests in this suite. This corrects the immediately preceding brain-sync entry's judgment that the parallelism hazard was "already adequately covered" — the existing `>= 1` tolerance covered cross-test *observation* contamination but not this separate concurrent-*write* corruption bug (caching-phase-implementer)
- [2026-08-24] Phases 42–45 planned (WO-065, P-433–P-436) — a second direct root-level gold-standard/big-fintech architecture review of this domain (after WO-050) dispatched four independent phases, none blocking on each other. Ph42 (CacheEncryptionAtRest): opt-in AES-GCM `CacheEncryptionSerializer` decorator over the FusionCache serialization pipeline, architecturally identical in shape to the shipped `BrotliCacheSerializer` (magic bytes `0x45 0x4E`/"EN", pass-through for unprefixed payloads), built entirely on `01.Core/SharedKernel.Cryptography`'s `ISymmetricEncryptionService` — closes this domain's gap against `07.Messaging` (P-346/WO-054) and `15.Integration` (P-427/WO-064), which already ship equivalent opt-in payload encryption; compose-then-encrypt ordering is enforced structurally (`AddBrotliCompression()` throws if called after `AddCacheEncryption()` has already wrapped the registered serializer), not left to call-order chance. Ph43 (LockFencingTokens): new `IFencedLock` interface plus an atomic per-resource Redis `INCR` fencing-token counter wired into `RedLockDistributedLockService`/`RedLockRenewableLock`, directly mitigating the "brief unprotected window" stale-owner hazard the Phase 23 renewal rule already names and accepts; `IRenewableLock` now extends `IFencedLock`, the plain `AcquireAsync` path exposes the token via an additive `is IFencedLock` cast rather than a signature change. Ph44 (TenantCacheService): new `ITenantCacheService` requiring a mandatory, non-defaulted `tenantId` on every method, composing the already-shipped `ITenantCacheKeyProvider.BuildTenantKey` (Phase 29) — brings this domain in line with the mandatory-tenant-scope-as-outermost-parameter convention `09.Search`/`10.Intelligence`/`17.Workflows` all independently converged on since Phase 29 predates that platform-wide pattern; design work surfaced and closed a genuine latent cross-tenant **invalidation** vector in `RemoveByTagAsync` (FusionCache tags live in a global namespace `ITenantCacheKeyProvider` never touches, so tags must be tenant-scoped as `{tenantId}:{tag}` on both `SetAsync` and `RemoveByTagAsync`). Ph45 (RedisTransportHardening): `RedisConnectionOptions`'s existing `[Required]`/`[Range]` `DataAnnotations` — confirmed decorative, never invoked through an `IOptions<T>`/`ValidateOnStart()` path — wired to genuinely fail fast at host startup (the identical defect class this review cycle has now found repeatedly elsewhere, most recently `13.ServiceDefaults` `TenantResolutionOptions`/WO-061/P-396); a first-class discoverable `Ssl`/`ClientCertificates`/`CertificateValidation` TLS/mTLS surface (today TLS is reachable only by hand-splicing `ssl=true` into a raw connection string, mTLS not reachable at all); a one-time non-loopback-without-TLS `Warning` (`LoggingEventIdRanges.Caching + 102`), never a hard failure since sidecar/mesh-terminated TLS is legitimate. No Packages-table topology change, no new package, no layering exception — every phase extends an already-shipped package's surface. Four new Implementation Rules subsections added ("Cache-value encryption rules", "Fencing token rules", "Tenant cache service rules", the Redis Connection Core "Fail-fast validation and TLS/mTLS surface" extension), all marked `(Phase NN, planned)`; Interface Contracts, Technology Stack, Packages table, and DI Registration examples all updated with forward-pointing `(planned, WO-065)` annotations; "Current Phase" section updated. Full task breakdowns, per-phase Goal/Scope/Implementation Rules/File-Level Plan/Acceptance Criteria/Dependencies/Version Pins recorded in `02.Caching/state-map.md` Phases 42–45 (caching-arch-planner, WO-065)
- [2026-08-24] Phase 42 (WO-065, P-433, CacheEncryptionAtRest) implemented end to end — all 9 tasks (CE-01→CE-09) complete. `CacheEncryptionSerializer` (`SharedKernel.Caching.FusionCache/Serialization/`) decorates `IFusionCacheSerializer` exactly like `BrotliCacheSerializer`, encrypting via the constructor-injected `ISymmetricEncryptionService` and packing `KeyId`/`Nonce`/`Tag`/`Ciphertext` behind magic bytes `0x45 0x4E` ("EN") using an explicit-length-prefixed frame (mirroring `07.Messaging.MassTransit`'s existing `EncryptedPayloadWireCodec` shape, since `EncryptedPayload` makes no fixed-size guarantee on `Nonce`/`Tag`); a corrupted/tampered ciphertext throws `CryptographicException`, a malformed frame throws `FormatException` — both loud, never silent. `AddCacheEncryption(this ICachingBuilder)` (`Extensions/CacheEncryptionCachingBuilderExtensions.cs`) genuinely wraps *whichever* `IFusionCacheSerializer` is currently registered — captured via a `ServiceDescriptor`-inspection helper (`ResolveExistingFactory`, handling the instance/factory/type cases) rather than resolving a fixed concrete type the way `AddBrotliCompression` does — so it correctly composes on top of an already-applied Brotli decorator; it throws `InvalidOperationException` naming `AddSharedKernelCryptography()` when `ISymmetricEncryptionService` is missing. The ordering guard (CE-03) is enforced via a new marker type, `CacheEncryptionOptions` (registered by `AddCacheEncryption`, checked by `AddBrotliCompression`) — not, as the phase's Implementation Rule #3 suggested, "a type check on the DI-resolved serializer instance": building a temporary `ServiceProvider` mid-registration to inspect a live instance was judged fragile (other prerequisites may not yet be resolvable) and unnecessary, since a marker-descriptor check is behaviorally equivalent and matches this domain's existing Phase 19 `services.Any(sd => sd.ServiceType == typeof(...))` guard idiom. `SharedKernel.Caching.FusionCache.csproj` gained a `<ProjectReference>` to `SharedKernel.Cryptography` (01.Core) — the domain's first cryptography dependency. All 9 unit/ordering tests (`CacheEncryptionSerializerTests.cs`) plus 2 Redis integration tests (`CacheEncryptionAtRestTests.cs`) pass; full regression 234/234 (`SharedKernel.Caching.FusionCache.Tests`) + 31/31 (`SharedKernel.Caching.Redis.Tests`). **Two genuine discoveries during implementation, both fixed:** (1) `SharedKernel.Caching.Redis.Tests.csproj`'s own `Microsoft.Extensions.DependencyInjection`/`Microsoft.Extensions.Logging`/`Polly.Core` `PackageReference` pins (`10.0.0`/`10.0.0`/`8.5.2`) were already stale against `SharedKernel.Testing`'s transitive floor (`10.0.9`/`10.0.9`/`8.7.0`) — the project failed to restore (`NU1605`) even before this phase's new test file existed (confirmed via an isolated repro with the new file temporarily removed); bumped all three pins to match, unblocking the whole test project, not just the new test. This is a project-local fix only — the identical stale-pin pattern was also found, but deliberately left untouched, in three sibling `.Tests.csproj` files (`Redis.Core`, `Redis.DistributedLocking`, `Redis.PubSub`) that this phase never otherwise touches; that is a pre-existing, broader maintenance gap out of this phase's scope, worth a future dedicated pass. (2) `Microsoft.Extensions.Caching.StackExchangeRedis` (10.x) stores every L2 entry as a Redis **Hash** (`absexp`/`sldexp`/`data` fields via its own Lua script), never a plain Redis string — the phase's own CE-07 wording ("raw `IDatabase.StringGetAsync` read") was corrected to `HashGetAsync(key, "data")` after a raw `StringGetAsync` call threw `WRONGTYPE`; the pre-existing `L2KeyPrefixIntegrationTests.cs` had never surfaced this because its own raw-Redis assertions only ever use `KeyExistsAsync`/`server.Keys(...)`, both type-agnostic. Both discoveries are now documented inline (Test Rules section) so a future Redis-raw-value test in this domain does not repeat either mistake. "Current Phase", Packages table, Technology Stack, Interface Contracts (split into a new shipped "cache-encryption additions (Phase 42, shipped)" block plus the still-planned Phase 44 block), "Cache-value encryption rules" (planned qualifier dropped), DI Registration example, and Test Rules all updated to reflect shipped status; Phases 43–45 (WO-065) remain `○` planned, untouched by this session (caching-phase-implementer)
- [2026-08-24] Phase 43 (WO-065, P-434, LockFencingTokens) implemented end to end — all 10 tasks (FT-01→FT-10) complete. `IFencedLock : IAsyncDisposable { long FencingToken { get; } }` added to `SharedKernel.Caching.Abstractions`, XML-documenting the standard "reject any write presenting a non-increasing token" usage contract; `IRenewableLock` now extends `IFencedLock` (adds `FencingToken`) rather than `IAsyncDisposable` directly. New internal `RedisFencingTokenSource` helper (`SharedKernel.Caching.Redis.DistributedLocking`) wraps an atomic `IDatabase.StringIncrementAsync` on `sharedkernel:lock:fencing:{resource}`, read/written through the same shared `IConnectionMultiplexer` this package already sources via `AddRedisConnection` — no second connection. `RedLockDistributedLockService` now takes `IConnectionMultiplexer` as a constructor dependency (resolved automatically from the existing `AddRedisConnection` registration — no DI wiring change needed in `AddRedisDistributedLocking`); the `INCR` fires only immediately after `redLock.IsAcquired` is confirmed true, in both `AcquireAsync` and `AcquireRenewableAsync` — never on a failed/contended attempt. `AcquireAsync`'s private `LockHandle` nested type now additionally implements `IFencedLock` (`AcquireAsync`'s own declared `IAsyncDisposable?` return type is unchanged — callers opt in via `is`/`as IFencedLock`). `RedLockRenewableLock` gained an `IConnectionMultiplexer` field and a settable `FencingToken` backing property; its `RenewAsync`'s dispose-then-recreate re-acquisition path issues a fresh `INCR` immediately after swapping in the new `IRedLock` handle, so the post-renewal token is always strictly greater than the pre-renewal token. Existing `[LoggerMessage]` templates (`LockAcquired`/`RenewableLockAcquired`/`LockRenewed`, same `EventId`s) extended with a `{FencingToken}` parameter for observability — no new `EventId`s needed. New `FencingTokenTests.cs` (9 tests) proves: the `AcquireAsync` handle implements `IFencedLock` with a positive token; sequential acquisitions after release strictly increase; a failed contended attempt never advances the counter (verified via exact `firstToken + 1` equality after the contended attempt); `AcquireRenewableAsync`'s handle exposes a positive token; `RenewAsync` (single and 3× consecutive) always strictly increases the token; a minimal "reject non-increasing token" guard function correctly accepts the post-renewal token and then rejects the stale pre-renewal token once presented afterward; and two different GUID-scoped resources both independently start at token `1` and never influence each other's sequence (proving per-resource, not global, scoping) — full regression 50/50 (41 pre-existing + 9 new). **Cross-domain blast radius (FT-02), resolved pragmatically:** `IRenewableLock` gaining `FencingToken` is a breaking change for `16.Testing`'s `FakeRenewableLock`, the only other implementer; per the phase's own explicit instruction this was **not** actioned as a full `16.Testing` phase, but since `SharedKernel.Caching.Redis.DistributedLocking.Tests` carries a direct `ProjectReference` to `SharedKernel.Testing`, leaving it unfixed would have made `SharedKernel.Testing.csproj` itself fail to compile — cascading into this package's own test build via the standard MSBuild `ProjectReference` transitive-build mechanism, blocking this phase's own required green test run. Applied the narrowest possible fix: `public long FencingToken { get; set; } = 1;` on `FakeRenewableLock`, no auto-increment on renewal, no new tests, no DI changes — a compile-shim only. FT-10 records the real follow-up (a behaviorally-faithful, auto-incrementing fake with its own test coverage) as a queued, separate `16.Testing`-domain phase. **One more pre-existing discovery, fixed the same way Phase 42 handled its own equivalent finding:** `SharedKernel.Caching.Redis.DistributedLocking.Tests.csproj`'s `Microsoft.Extensions.DependencyInjection`/`Microsoft.Extensions.Logging` pins (`10.0.0`/`10.0.0`) were already stale against `SharedKernel.Testing`'s transitive floor (`10.0.9`, established out-of-band by a `16.Testing` session between Phase 42 and this one) — the project failed to restore (`NU1605`) even before this phase's new test file existed; bumped both to `10.0.9`, matching the identical fix Phase 42 applied to `SharedKernel.Caching.Redis.Tests.csproj`. The same stale-pin pattern still exists, deliberately untouched, in `.Redis.Core.Tests`/`.Redis.HashStore.Tests`/`.Redis.PubSub.Tests` — out of this phase's scope, a growing candidate for a dedicated future pass. "Current Phase", Packages table, "Fencing token rules" (planned qualifier dropped, `16.Testing` follow-up note added), and Interface Contracts (Phase 43 block promoted out of "Planned additions (WO-065)") all updated to reflect shipped status; Phases 44–45 (WO-065) remain `○` planned, untouched by this session (caching-phase-implementer)
- [2026-08-24] Phase 44 (WO-065, P-435, TenantCacheService) implemented end to end — all 9 tasks (TC-01→TC-09) complete. `ITenantCacheService` added to `SharedKernel.Caching.Abstractions` (`GetAsync`/`SetAsync`/`GetOrSetAsync`/`RemoveAsync`/`RemoveByTagAsync`, every method taking a mandatory, non-defaulted `tenantId` first parameter and `(entity, id)` — never a pre-built key — matching `ITenantCacheKeyProvider.BuildTenantKey`'s own shape). `internal sealed TenantCacheService` (`SharedKernel.Caching.FusionCache/Implementations/`) composes `ICacheService` + `ITenantCacheKeyProvider`: every method builds the tenant-scoped key via `BuildTenantKey(tenantId, entity, id)` then delegates — zero duplicated key-formatting logic. **The subtle defect this phase was explicitly tasked not to introduce (TC-03) is closed:** `SetAsync` rewrites every tag on the supplied `CachePolicy` to `{tenantId}:{tag}` (via a private `ScopeTagsToTenant` helper that no-ops on the common untagged path — no unnecessary record copy) before delegating to `ICacheService.SetAsync`, and `RemoveByTagAsync` applies the identical `{tenantId}:{tag}` rewrite before calling `ICacheService.RemoveByTagAsync` — so two tenants both calling `.WithTags("orders")` can never cross-invalidate each other's entries. `AddTenantCacheService(this ICachingBuilder)` (`Extensions/TenantCacheServiceCachingBuilderExtensions.cs`) is additive: it `TryAddSingleton`s both `ITenantCacheKeyProvider` and `ITenantCacheService`, so it never overrides a prior standalone `AddTenantCacheKeyProvider()` call, and also works correctly stand-alone with no prior call at all — the two orderings produce an identical resulting registration. Zero dependency on `12.Security`/`IHttpContextAccessor` anywhere in the new files, confirmed by a targeted grep (TC-08) — the only matches are XML-doc prose stating the rule, not an actual reference. New `TenantCacheServiceTests.cs` (26 tests) proves: two different `tenantId` values for the same `(entity, id)` pair never collide on the same key (TC-05, both `SetAsync` and a same-tag-name `RemoveAsync`-isolation variant); cross-tenant tag isolation — tenant A's `WithTags("orders")`-tagged entry survives tenant B's `RemoveByTagAsync(tenantB, "orders")` call (TC-06), and a tenant's own `RemoveByTagAsync` still evicts all of its own tagged entries while leaving untagged ones alone; `AddTenantCacheService()` does not remove or override a prior standalone `AddTenantCacheKeyProvider()` registration, self-registers `ITenantCacheKeyProvider` when none exists yet, is idempotent, and registers both types as singletons (TC-07); plus round-trip (`GetAsync`/`SetAsync`/`GetOrSetAsync`/`RemoveAsync`) and argument-validation coverage. Full regression green: FusionCache 255/255 (229 prior + 26 new), 0 failed. "Current Phase", Packages table, Interface Contracts (Phase 44 block promoted out of "Planned additions (WO-065)" for both `SharedKernel.Caching.Abstractions` and `SharedKernel.Caching.FusionCache`), "Tenant cache service rules" (planned qualifier dropped), DI Registration example, and Test Rules all updated to reflect shipped status; Phase 45 (WO-065) remains `○` planned, untouched by this session (caching-phase-implementer)
- [2026-08-24] Phase 45 (WO-065, P-436, RedisTransportHardening) implemented end to end — all 10 tasks (TH-01→TH-10) complete. This is WO-065's final `02.Caching` phase — every phase key in this domain is now `●`. `AddRedisConnection` (`SharedKernel.Caching.Redis.Core`) now registers `RedisConnectionOptions` via `services.AddOptions<RedisConnectionOptions>().Configure(o => { o.ConnectionString = connectionString; configure?.Invoke(o); }).ValidateDataAnnotations().ValidateOnStart()`, a real, statically-visible call directly in the method body (not behind a helper or a conditional, for governance-rule visibility); the `IConnectionMultiplexer` singleton factory resolves `IOptions<RedisConnectionOptions>.Value` and builds `ConfigurationOptions` via a new internal `BuildConfigurationOptions` helper. `RedisConnectionOptions` gained `Ssl` (`bool`, default `false`), `ClientCertificates` (`X509Certificate2Collection?`), and `CertificateValidation` (`Func<X509Certificate2, X509Chain?, SslPolicyErrors, bool>?`, mirroring `13.ServiceDefaults`'s Kestrel `ClientCertificateValidation` callback shape). A one-time `[LoggerMessage]` `Warning` (`LoggingEventIdRanges.Caching + 102`, the next free offset in this package's `+100..+199` sub-block after `RedisConnectionHealthTracker`'s `+100`/`+101`) fires from inside the multiplexer factory when the resolved endpoint set contains a non-loopback host with `Ssl = false` — implemented via `FirstOrDefault`, so it fires once per registration even with multiple non-loopback endpoints configured, never once per endpoint. **Two corrections to the original plan, found by verifying against the real pinned StackExchange.Redis 2.13.1 API rather than trusting the phase's prose:** (1) `ConfigurationOptions.CertificateSelection`/`.CertificateValidation` do not exist in this version — that legacy StackExchange.Redis surface was replaced by `ConfigurationOptions.SslClientAuthenticationOptions` (`Func<string, System.Net.Security.SslClientAuthenticationOptions>`), confirmed via a scratch console app reflecting over the installed assembly; `BuildConfigurationOptions` composes `ClientCertificates`/`CertificateValidation` into that delegate instead, converting the BCL callback's `X509Certificate?` to `X509Certificate2` and rejecting outright (`false`) on a null certificate. (2) `ValidateOnStart()`/`IStartupValidator` resolve purely from `Microsoft.Extensions.Options` (transitively via `Microsoft.Extensions.Options.DataAnnotations`) in this platform's pinned `10.0.0` — confirmed via an isolated scratch project that compiled and ran `ValidateOnStart()` end-to-end with zero `Microsoft.Extensions.Hosting.Abstractions` reference at all; the real `IHostedService` that triggers `IStartupValidator.Validate()` at `IHost.StartAsync()` time is supplied by the Generic Host itself (`Microsoft.Extensions.Hosting`, a full package no library references), not by `.Redis.Core`. Both `PackageReference`s from the phase's Version Pins table were added anyway per the phase's explicit instruction (harmless, and matches this package's declared dependency intent) — flagged here for `00.Governance`'s P-437, whose planned `AssertMethodBodyInvokesMethod` check should target `Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions.ValidateOnStart` (the real declaring type, confirmed via reflection), not `Microsoft.Extensions.Options.OptionsBuilderExtensions` as the phase spec assumed. A genuine, pre-existing test regressed by this session's own change and fixed in place: `RedisConnectionCoreExtensionsTests.AddRedisConnection_WithConfigureDelegate_AppliesConnectTimeout` asserted the configure delegate ran synchronously inside `AddRedisConnection` itself — true before this phase, no longer true now that registration goes through `AddOptions<T>().Configure(...)` (lazy, invoked on first `IOptions<T>.Value` access) — rewritten to resolve `IOptions<RedisConnectionOptions>` from a built `ServiceProvider` instead of a side-channel capture; no production call site in `.Redis`/`.DistributedLocking`/`.HashStore`/`.PubSub` needed a change, since each already passes a self-contained configure delegate whose result is identical regardless of invocation timing. New `RedisConnectionValidationTests.cs` (5 tests, TH-06) proves an invalid `ConnectTimeoutMs`/`ConnectionString` throws `OptionsValidationException` both via `IStartupValidator.Validate()` and via first `IOptions<T>.Value` access, and that default/valid configurations pass. New `RedisTlsConfigurationTests.cs` (18 tests, TH-07/TH-08) proves `Ssl`/`ClientCertificates`/`CertificateValidation` composition (including the null-certificate-rejection edge case and the "no allocation when neither is set" backward-compatibility case) and the exactly-once, loopback-exempt, `Ssl`-exempt warning behavior, via a new internal-visibility convention on `BuildConfigurationOptions`/`WarnIfNonLoopbackWithoutTls`/`IsLoopback` (mirroring `RedisConnectionHealthTracker`'s existing internal-for-testability pattern) rather than resolving a real `IConnectionMultiplexer` against a live Redis instance, since this test project carries no Testcontainers dependency. One more pre-existing stale-pin discovery, fixed the same way Phases 42/43 handled their own: `SharedKernel.Caching.Redis.Core.Tests.csproj`'s `Microsoft.Extensions.DependencyInjection`/`Microsoft.Extensions.Logging`/`Polly.Core` pins (`10.0.0`/`10.0.0`/`8.5.2`) were stale against `SharedKernel.Testing`'s transitive floor (`10.0.9`/`10.0.9`/`8.7.0`) — bumped to match; `.Redis.HashStore.Tests`/`.Redis.PubSub.Tests` still carry the same stale pins, deliberately untouched, out of this phase's scope. Full regression green: `SharedKernel.Caching.Redis.Core.Tests` 55/55 (33 prior + 22 new); `.Redis`/`.DistributedLocking`/`.HashStore`/`.PubSub` all confirmed to still build clean against the unchanged `AddRedisConnection` public signature. "Current Phase", Packages table, Technology Stack, and the "Fail-fast validation and TLS/mTLS surface" subsection (promoted out of "planned") all updated to reflect shipped status (caching-phase-implementer)
- [2026-08-24] Brain sync pass (post-Phase 45) — Interface Contracts block for `SharedKernel.Caching.Redis.Core` corrected: `Ssl`/`ClientCertificates`/`CertificateValidation` merged into the main `RedisConnectionOptions` entry (dropped the separate "Planned additions (WO-065, Phase 45)" sub-block, which was the last of its kind — Phases 42–44's equivalent sub-blocks were already promoted in their own sessions), and the composition comment corrected from `.CertificateSelection`/`.CertificateValidation` to the real `ConfigurationOptions.SslClientAuthenticationOptions` surface. Test Rules' "Redis connection hardening tests" bullet promoted from `(Phase 45, planned)` to `(Phase 45, shipped)` with the real test file names. Two stale inline comments in the DI Registration example fixed in passing: "Distributed locking with fencing tokens (Phase 43, planned, WO-065)" and "Redis connection with TLS (Phase 45, planned, WO-065)" both still said "planned" despite Phase 43 having shipped in an earlier session — corrected to drop the stale qualifier, and the TLS example gained a one-line comment naming the real `SslClientAuthenticationOptions` composition target. No root `CLAUDE.md` change warranted — Phase 45 introduces no new package, no new cross-domain abstraction, and no layering change; it hardens an existing package's surface only (agent)
- [2026-09-08] Phase 46 (WO-081, P-497, `SK.02.CacheEncryptionAadBinding`, `○` Pending) planned against `01.Core`'s design-locked `SK.01.P491`/`SK.01.P492`. **Read the full reasoning in "Current Phase" and the new "Cache-value encryption rules" forward-pointer note before touching this feature again.** Headline finding: `IFusionCacheSerializer` never receives the cache key, so `CacheEncryptionSerializer` (Phase 42) cannot be async-ified in place to derive key-bound associated data — the phase input's own literal framing ("migrate CacheEncryptionSerializer to async contracts") does not survive contact with FusionCache's actual serializer contract, and this was verified against the interface shape rather than assumed. An `AsyncLocal` ambient-key workaround was evaluated and rejected (a background eager-refresh factory re-invocation is not verifiably tied to the original caller's execution context — a silent-wrong-AAD risk on a feature this domain already ships). Resolution: retire `CacheEncryptionSerializer`; add `EncryptedCacheService`, a decorator over `ICacheService` (the wrap-`ICacheService` DI shape `TenantCacheService`/Phase 44 already established, reused rather than reinvented) where the key is always an explicit parameter; AAD = UTF-8 bytes of the cache key only (tags excluded — not available on `GetAsync<T>`'s signature, so not reproducible at decrypt time); compression moves into the same decorator when both features are opted in (reusing, not reimplementing, `BrotliCacheSerializer`'s codec via a new extracted `BrotliPayloadCodec` + a new `.Inner` property mirroring `01.Core`'s own P-492 `CachedEncryptionKeyProvider.Inner` precedent), since encryption must see plaintext bytes before compression can safely run. `AddCacheEncryption()`'s public signature and the compress-then-encrypt ordering guarantee are unchanged; `AddBrotliCompression()` needs zero code change. Flagged, accepted consequence: the wire shape changes, so a rolling deploy causes previously-encrypted L2 entries to fail decryption and be silently treated as cache misses — a cold-cache-refill wave, not data loss. Hard sequencing dependency recorded: this phase's own tasks (13, `AA-01`→`AA-13`, all `○`) cannot be implemented until `01.Core` ships `SK.01.P491`'s/`SK.01.P492`'s own `C-*`/`P-*` tasks (currently design-locked only). "Current Phase", the `.FusionCache` Packages-table row, the Phase 42 Interface Contracts block, the "Cache-value encryption rules" section, and the FusionCache `EventId` sub-block table (`+15` reserved) all updated with forward-pointing notes — the actual rewrite of "Cache-value encryption rules" and the Interface Contracts block into their post-Phase-46 shape is deliberately deferred to the implementation pass, per this file's own "describe state after the phase, once it ships" convention; documenting a not-yet-real API now would mislead a reader into coding against it. No root `CLAUDE.md` change made or needed — no new package, no layering change; report any `.slnx`/root-`state-map.md` update needed for this phase separately, out of this agent's file-edit jurisdiction (caching-arch-planner, WO-081)
- [2026-09-08] Phase 46 (WO-081, P-497, `SK.02.CacheEncryptionAadBinding`) implemented end to end — all 13 tasks (`AA-01`→`AA-13`) complete, against `01.Core`'s now-shipped `SK.01.P491`/`SK.01.P492` (`ISymmetricEncryptionService`'s required `associatedData` parameter; `ISynchronousEncryptionKeyProvider`'s capability gate on the four sync members). `CacheEncryptionSerializer` deleted outright (`Serialization/CacheEncryptionSerializer.cs` + its test file); superseded by `EncryptedCacheService` (`Encryption/EncryptedCacheService.cs`, `internal sealed partial : ICacheService`) — every member calls `EncryptAsync`/`DecryptAsync` exclusively, AAD = `Encoding.UTF8.GetBytes(key)`, a decrypt failure (`Result.Failure`) is logged (`LoggingEventIdRanges.Caching + 15`, `Warning`) and treated as a cache miss with best-effort eviction. `GetOrSetAsync`'s wrapped `Func<CancellationToken, ValueTask<EncryptedPayload>>` factory closure captures only `key`/`factory` by value — no `AsyncLocal`, proven by AA-08's throw-on-sync-call test double driven through a captured-factory re-invocation from an unrelated `Task.Run` continuation. `BrotliCacheSerializer`'s compress/decompress mechanics extracted verbatim into a new `Serialization/BrotliPayloadCodec.cs` (shared, zero behavioral change to `BrotliCacheSerializer` itself) plus a new `.Inner` property; `EncryptedCacheService` calls `BrotliPayloadCodec.Compress(bytes, thresholdBytes: 0, CompressionLevel.Fastest)` — unconditional, since its constructor captures only a `bool compressionEnabled` (per the phase spec's own exhaustive constructor-parameter list), not the source `CompressionOptions` instance; the per-service `L2ThresholdBytes`/`Level` a caller configured via `AddBrotliCompression(o => ...)` deliberately do not carry over once compression moves to this layer — recorded as a deliberate simplification, not a defect. New `Serialization/CacheSerializationOptions.cs` (internal DI holder for the `JsonSerializerOptions` `AddSharedKernelCaching` already builds) and `Serialization/EncryptedPayloadJsonContext.cs` (STJ source-gen context for `EncryptedPayload`) let `EncryptedCacheService` reuse the identical, already-`SerializerContext`-combined options instance for its own T-to-plaintext-bytes step rather than re-deriving a second one — `CachingServiceCollectionExtensions.AddSharedKernelCaching` now also folds `EncryptedPayloadJsonContext.Default` into the existing `JsonTypeInfoResolver.Combine(...)` call and registers `CacheSerializationOptions` unconditionally (zero behavioral change for services that never call `AddCacheEncryption()`). `CacheEncryptionCachingBuilderExtensions.AddCacheEncryption` rewritten: gained a new `ICacheService`-registered guard (naming `AddSharedKernelCaching()`) alongside the unchanged `ISymmetricEncryptionService` guard; reuses the existing `ResolveExistingFactory` `ServiceDescriptor`-inspection helper (Phase 42) for **both** the `IFusionCacheSerializer` unwrap-if-Brotli replacement and the `ICacheService`-wrapping replacement — a deliberate, judged-acceptable divergence from the phase's literal "type-check the currently-registered `IFusionCacheSerializer`" instruction: rather than building a temporary mid-registration `ServiceProvider` (the exact pattern Phase 42's own memory/brain already flagged as fragile), the type check happens lazily inside each factory using the real, fully-built `IServiceProvider` at first resolution — `resolveExistingSerializer(sp)` is invoked independently from both factories, harmlessly re-constructing a second stateless `BrotliCacheSerializer` instance in the case where compression was applied (the actual registered `IFusionCacheSerializer` singleton is still exactly one instance — only the discarded-after-use unwrap-check instance is duplicated). `AddBrotliCompression()` itself required zero code change, confirming the phase's own Rule 9. New `Encryption/EncryptedCacheServiceTests.cs` (superseding the deleted `CacheEncryptionSerializerTests.cs`) covers round-trip, the AA-07 headline cross-key-replay-fails-authentication test (a raw `EncryptedPayload` moved byte-for-byte from one key to another), tamper detection, `GetManyAsync` per-key failure isolation, AA-08's async-only-compliance proof (a `ThrowOnSyncCallEncryptionService` wired through every `ICacheService` member) plus the eager-refresh-survives-a-different-execution-context proof, AA-09's ordering tests (a direct ciphertext-byte-length comparison between compression-enabled/disabled instances, replacing Phase 42's now-inapplicable magic-byte-prefix assertion, since the wire shape is a directly-stored JSON `EncryptedPayload` rather than an opaque prefixed blob), full `AddCacheEncryption()` DI-registration sanity (including the new missing-`ICacheService` guard, tested by constructing the `internal` `CachingBuilder` directly), and AA-11's tenant-composition proof (a payload replayed from one tenant's `ITenantCacheKeyProvider.BuildTenantKey`-scoped key to another tenant's key for the identical `(entity, id)` fails to decrypt). `SharedKernel.Caching.Redis.Tests/Integration/CacheEncryptionAtRestTests.cs` updated per AA-10: **discovered mid-implementation** that the raw Redis-stored JSON is FusionCache's own distributed-entry envelope (`Value`/`Timestamp`/`LogicalExpirationTimestamp`/`Tags`/`Metadata`) with the `EncryptedPayload` nested inside `Value`, not at the JSON root — a first attempt asserting on root-level `KeyId`/`Ciphertext` properties failed against the real shape (caught by actually running the Testcontainers-backed test, not by inspection alone); fixed by asserting `JsonDocument.Parse` succeeds (proves it's now well-formed JSON, not an opaque blob) plus a case-insensitive whole-payload substring search for `"Ciphertext"`/`"KeyId"`, since the nested envelope's own property-name casing depends on FusionCache's unmodified default `System.Text.Json` options, which this domain has never pinned and should not start asserting on here. Full regression green: `SharedKernel.Caching.FusionCache.Tests` 259/259, `SharedKernel.Caching.Redis.Tests` 31/31 (including both encryption-at-rest tests). Confirmed via `git status` that this session ran concurrently with four sibling WO-081 domain agents (`06.Persistence`, `07.Messaging`, `15.Integration`, `17.Workflows`) — a transient `SharedKernel.Persistence.EfCore` compile break (unrelated `IConventionEntityType` API errors, mid-fix by the `06.Persistence` sibling) blocked this project's own `dotnet build`/`dotnet test` runs for part of the session purely via the shared `16.Testing` transitive `ProjectReference` chain; resolved itself once the sibling agent's fix landed, with zero changes needed on this domain's side — confirming the phase brief's own "a red full-solution build is not your failure signal" guidance held in practice. "Current Phase", the `.FusionCache` Packages-table row, the Technology Stack row, the Interface Contracts block (fully rewritten for `EncryptedCacheService`/`BrotliPayloadCodec`/`CacheSerializationOptions`/`EncryptedPayloadJsonContext`), "Cache-value encryption rules" (fully rewritten, permanent structural-finding note added), the DI Registration example comment, the FusionCache `EventId` sub-block table row, and Test Rules all updated to reflect shipped status. Every `SK.02.CacheEncryptionAadBinding` task is now `●`; root `state-map.md`/root `CLAUDE.md` propagation is out of this agent's file-edit jurisdiction per the shared-file protocol — recorded here for whoever closes the root entry centrally (caching-phase-implementer)
