# 02.Caching — Domain Brain

## What This Domain Is

The caching capability domain. A hybrid L1 (in-process memory) / L2 (Redis distributed) cache powered by **ZiggyCreatures.FusionCache**, with stampede protection, fail-safe, eager refresh, tags and factory-controlled caching, plus Redis distributed locks and leases with fencing tokens. The Redis packages are separate, so a service that needs only L1 references no Redis package. `SharedKernel.Caching.Abstractions` is provider-neutral (Abstractions tier): packages such as `SharedKernel.Application` and `SharedKernel.Application.Pipeline.Caching` depend on the contracts without pulling in FusionCache or StackExchange.Redis.

Philosophy: **Provider-neutral contracts. Stampede-proof. Outages are never reported as contention.**

---

## Current State

All seven packages are implemented and released with the repo-wide release train (one MinVer version for every package; see root `PLATFORM.md`). The contract, key format and Redis topology below are final; phase-by-phase history lives in `02.Caching/state-map.md`.

- **P-547** redesigned `SharedKernel.Caching.Abstractions` (`CacheLookup<T>`, `CacheFactoryContext`, validated `CachePolicy`, `CacheKeyFormat`, locks with fencing tokens and leases, `DistributedLockUnavailableException`).
- **P-548** hardened `SharedKernel.Caching.FusionCache` (required `ServiceName`, warmup in `StartingAsync`, tenant-free metric tags).
- **P-550** hardened the five Redis packages: one shared connection from `AddRedisConnection`, no connection strings anywhere else, FusionCache circuit breakers for L2, no Polly, no RedLock.net.
- **WO-086** (P-563, P-565, P-569, P-571): every tenant parameter is `SharedKernel.Execution.Tenancy.TenantId` (never `default`, formatted as a lowercase GUID); readiness is `SharedKernel.Primitives.Health.IReadinessProbe` — `redis` from `AddRedisConnection`, `cache` from `AddSharedKernelCaching` — and `IRedisConnectionProbe`/`RedisConnectionHealth` and the `SharedKernel.ServiceDefaults.Caching`/`.Caching.Redis` packages are gone; the test fakes split into `SharedKernel.Caching.Testing` (`AddFakeCachingServices()`) and `SharedKernel.Caching.Redis.Testing` (`AddFakeRedisServices()`).

---

## Packages

| Package | Role | NuGet / Project References |
| ------- | ---- | -------------------------- |
| `SharedKernel.Caching.Abstractions` | Provider-neutral contracts: `ICacheService`, `ITenantCacheService`, `CacheLookup<T>`, `CacheFactoryContext`, `CachePolicy`, `CacheKeyFormat`, `ICacheKeyProvider`, `ITenantCacheKeyProvider`, `ICacheWarmupStrategy`, `ICachingBuilder`, `IDistributedLockService`, `IDistributedLock`, `DistributedLockOptions`, `DistributedLease`, `DistributedLockUnavailableException`. Public API tracked (RS0016/RS0017 and friends as errors). No logging, no options, no hosting. **Abstractions tier** | `SharedKernel.Execution` (for `TenantId`) and `Microsoft.Extensions.DependencyInjection.Abstractions` only (plus the private `Microsoft.CodeAnalysis.PublicApiAnalyzers`) |
| `SharedKernel.Caching.FusionCache` | `FusionCacheService` (`ICacheService`), `CacheKeyProvider` (both key interfaces), `TenantCacheService`, `EncryptedCacheService`, `BrotliCacheSerializer`, `CacheWarmupHostedService`, `CachingOptions` + `CachingOptionsValidator`, internal `CacheReadinessProbe` (the `cache` readiness probe, name in `CacheReadinessProbeNames.Cache`); `AddSharedKernelCaching`, `AddTenantCacheService`, `AddBrotliCompression`, `AddCacheEncryption`, `AddCacheWarmup<TStrategy>` | `Abstractions`, `SharedKernel.Primitives`, `SharedKernel.Cryptography`, ZiggyCreatures.FusionCache (+ `.Serialization.SystemTextJson`), `Microsoft.Extensions.Options.DataAnnotations`, `Microsoft.Extensions.Hosting.Abstractions` |
| `SharedKernel.Caching.Redis.Core` | The one shared connection: `AddRedisConnection` (configuration or code; throws when called twice), `EnsureRedisConnectionRegistered`, `RedisConnectionOptions` (section-bound, validated), `RedisReadinessProbeNames.Connection` (`"redis"`); internal `RedisConnectionOptionsValidator`, `RedisConnectionProbe` (the `redis` `IReadinessProbe`). **References no `SharedKernel.Caching` package** | `SharedKernel.Configuration`, `SharedKernel.Primitives`, StackExchange.Redis, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options.DataAnnotations` |
| `SharedKernel.Caching.Redis` | L2 distributed cache and FusionCache backplane over the shared connection: `AddRedisL2` (two overloads), `RedisL2Options` (section-bound, validated); internal `RedisL2OptionsValidator`, `RedisDistributedCache` (one Redis string per entry, handed to FusionCache only), `SharedConnectionMultiplexer` (backplane wrapper that ignores `Close`/`Dispose`) | `Abstractions`, `Redis.Core`, `SharedKernel.Configuration`, ZiggyCreatures.FusionCache, ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis. **Must not reference `.FusionCache` or `Microsoft.Extensions.Caching.StackExchangeRedis`** |
| `SharedKernel.Caching.Redis.DistributedLocking` | `AddRedisDistributedLocking()` on `ICachingBuilder` and on `IServiceCollection`; internal `RedisDistributedLockService` (`IDistributedLockService`), `RedisDistributedLock` (`IDistributedLock`), `RedisLockScripts`. No options type | `Abstractions`, `Redis.Core`, `SharedKernel.Primitives`. No RedLock.net (locked by `00.Governance`) |
| `SharedKernel.Caching.Redis.HashStore` | `IRedisHashService`, `ITypedHashStore<T>` (namespace `SharedKernel.Caching.Redis.HashStore`); `AddRedisHashService()`, `AddTypedHashStore<T>(JsonTypeInfo<T>)` on `IServiceCollection`; internal `RedisHashService`, `TypedHashStore<T>` | `Abstractions` (for `CacheLookup<T>`), `Redis.Core` |
| `SharedKernel.Caching.Redis.PubSub` | `IRedisChannelService` (namespace `SharedKernel.Caching.Redis.PubSub`); `AddRedisChannelService()` on `IServiceCollection`; internal `RedisChannelService`, `RedisChannelSubscription` | `Redis.Core`, `SharedKernel.Primitives`. No `Abstractions` reference, no hosting package |

All packages target `net10.0`. `.Abstractions` is Abstractions tier; the other six are Adapter tier. The four Redis capability packages declare `<SharedKernelAllowedAdapterReferences>SharedKernel.Caching.Redis.Core</SharedKernelAllowedAdapterReferences>` (SKTIER002 rejects any other adapter-to-adapter edge). Test projects are nested inside each project folder.

**NuGet packaging:** all 7 packages carry the full metadata bar (`PackageId`/`Authors`/`Company`/`Product`/`Description`/`PackageTags`/`PackageLicenseExpression`/`PackageReadmeFile`/`RepositoryType`/`RepositoryUrl`/`PackageProjectUrl`/`Copyright`/`GenerateDocumentationFile`/`IncludeSymbols`/`SymbolPackageFormat`) and a packed `README.md`. Versions come from the repo-wide MinVer tag (root `PLATFORM.md`), never a `<Version>` element.

**`02.Caching/consumer-verify`:** `SharedKernel.Caching.ConsumerVerify.csproj` takes a `PackageReference` (not `ProjectReference` — proving the *packed* artifact resolves is the point) to all 7 `PackageId`s, plus `Microsoft.Extensions.Hosting` and `Testcontainers.Redis`. `Program.cs` runs five surfaces, each through a real `Host.CreateApplicationBuilder()` → `IHost.StartAsync()`, against one shared Testcontainers Redis:

| Surface | DI composition | Contract(s) resolved | Real-Redis proof |
| ------- | --------------- | --------------------- | ----------------- |
| 1. L1-only | `AddSharedKernelCaching(o => o.ServiceName = "consumer-verify-l1")` | `ICacheService`, `ICacheKeyProvider` | none needed — `GetOrSetAsync` round-trip in-process |
| 2. L1+L2 | `AddRedisConnection(o => o.ConnectionString = cs)` + `AddSharedKernelCaching(...).AddRedisL2()` | `ICacheService` | raw `KeyExistsAsync("v2:{key}")` (a Redis string; FusionCache adds `v2:`) after a `GetOrSetAsync` write |
| 3. Locking-only (no FusionCache) | `AddRedisConnection(...).AddRedisDistributedLocking()` | `IDistributedLockService` | `TryAcquireAsync` returns a held lock with a positive fencing token; a contended attempt returns `null`; disposing releases and cancels `LostToken`; the next acquisition has a greater token; a second lease on the same resource returns `null` |
| 4. Hash-store-only | `AddRedisConnection(...).AddRedisHashService().AddTypedHashStore(...)` | `IRedisHashService`, `ITypedHashStore<string>` | `SetFieldAsync`/`GetFieldAsync`/`IncrementFieldAsync` round-trip |
| 5. Pub/Sub-only | `AddRedisConnection(...).AddRedisChannelService()` | `IRedisChannelService` | `SubscribeAsync`/`PublishAsync` round-trip (bounded 10 s wait), then disposing the subscription |

Surface 2 also resolves the `redis` readiness probe (`GetRequiredReadinessProbe(RedisReadinessProbeNames.Connection)`) and expects it healthy. Each surface is run independently; the process exits `1` if any failed.

**Microservices reference `SharedKernel.Caching.Abstractions` for contracts** and provider packages only at the composition root. Every Redis composition starts with one `AddRedisConnection`:

- L1-only: `SharedKernel.Caching.FusionCache`
- L1 + L2 cache: `+ SharedKernel.Caching.Redis` (brings `.Redis.Core`)
- Distributed locking only (no cache): `SharedKernel.Caching.Redis.DistributedLocking` (brings `.Redis.Core`)
- Structured hash storage only: `SharedKernel.Caching.Redis.HashStore`
- Ephemeral signaling only: `SharedKernel.Caching.Redis.PubSub`

**Dependency rules** (tiers are build-enforced by `eng/SharedKernelTiers.targets`; these topology rules are locked by `00.Governance`'s `RedisTopologyRules`):

- `SharedKernel.Caching.Abstractions` references only `SharedKernel.Execution` and `Microsoft.Extensions.DependencyInjection.Abstractions`, and declares no provider-named type.
- `SharedKernel.Caching.Redis` and `SharedKernel.Caching.FusionCache` never reference each other.
- `SharedKernel.Caching.Redis.Core` references no capability package and no `SharedKernel.Caching` package; it knows the Redis connection, its options and its probe only.
- `.Redis`, `.DistributedLocking`, `.HashStore` and `.PubSub` depend on `.Redis.Core` (the first three also on `Abstractions`) but **never on each other**. A consumer can take any subset.

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
| L2 distributed cache | Internal `RedisDistributedCache` (`IDistributedCache` over `StackExchange.Redis` strings; `Microsoft.Extensions.Caching.StackExchangeRedis` removed in P-550) | — | `.Redis` |
| STJ serialization for FusionCache | `ZiggyCreatures.FusionCache.Serialization.SystemTextJson` | 2.6.0 | `.FusionCache` |
| Redis client | `StackExchange.Redis` | 2.13.1 | `.Redis.Core` (used by every Redis package) |
| Distributed locking | Server-side Lua scripts over `StackExchange.Redis` (`IDatabase.ScriptEvaluateAsync`) | — | `.Redis.DistributedLocking` |
| DI abstractions | `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.11 | all packages |
| Public API tracking | `Microsoft.CodeAnalysis.PublicApiAnalyzers` | 5.6.0 | all seven packages |
| OTel tracing and metrics | `System.Diagnostics` `ActivitySource`/`Meter` (BCL) | — | `.FusionCache` |
| L2 and backplane circuit breakers | FusionCache `DistributedCacheCircuitBreakerDuration`/`BackplaneCircuitBreakerDuration` | 2.6.0 | `.Redis` (options), `.FusionCache` (runtime) |
| Section-bound options (`AddValidatedOptions`, `ISectionBoundOptions`) | `01.Core/SharedKernel.Configuration` | ProjectReference | `.FusionCache`, `.Redis.Core`, `.Redis` |
| Options validation | `Microsoft.Extensions.Options.DataAnnotations` | 10.0.11 | `.FusionCache`, `.Redis.Core` |
| Hosting (warmup hosted service) | `Microsoft.Extensions.Hosting.Abstractions` | 10.0.11 | `.FusionCache` |
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
    ForTenant(TenantId tenantId)                // tags → @{tenant}:{tag}, plus tenant-wide tag @{tenant};
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
    // tenantId is a TenantId (SharedKernel.Execution.Tenancy), written as its lowercase GUID (never escaped,
    // never contains ':' or '@'); default(TenantId) throws ArgumentException.
    // every other caller-supplied part is escaped: no two inputs share a key or tag, a global key never
    // equals a tenant key, one tenant's tag never matches another tenant's entries

ICacheKeyProvider
    BuildKey(string entity, string id, params string[] segments) → string
ITenantCacheKeyProvider : ICacheKeyProvider
    BuildTenantKey(TenantId tenantId, string entity, string id, params string[] segments) → string
    // tenantId is always explicit — never ambient; no IRequestContext, no IHttpContextAccessor

ITenantCacheService   (every method: explicit, non-defaulted TenantId tenantId; (entity, id), never a pre-built key)
    TryGetAsync<T>(tenantId, entity, id, ct)                                   → ValueTask<CacheLookup<T>>
    GetOrSetAsync<T>(tenantId, entity, id, Func<CancellationToken, ValueTask<T>>, CachePolicy, ct) → ValueTask<T>
    GetOrSetAsync<T>(tenantId, entity, id, Func<CacheFactoryContext, CancellationToken, ValueTask<T>>, CachePolicy, ct)
    SetAsync<T>(tenantId, entity, id, T value, CachePolicy, ct)               → ValueTask
    RemoveAsync(tenantId, entity, id, ct) / ExpireAsync(tenantId, entity, id, ct) → ValueTask
    RemoveByTagAsync(TenantId tenantId, string tag, CancellationToken ct)     → ValueTask   // tag unscoped, as passed to WithTags
    RemoveTenantAsync(TenantId tenantId, CancellationToken ct)                → ValueTask   // every entry of the tenant
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

Authoritative surface for each Redis package: its `PublicAPI.Unshipped.txt`. Usage: its `README.md`.

```text
RedisConnectionOptions : ISectionBoundOptions  (sealed class, namespace SharedKernel.Caching.Redis.Core)
    static SectionName          "SharedKernel:Caching:Redis"
    ConnectionString            string       [Required]; must parse and name >= 1 endpoint (validator; message never echoes it)
    ConnectTimeout              TimeSpan     5 s;  100 ms – 1 min
    CommandTimeout              TimeSpan     5 s;  100 ms – 1 min  (SyncTimeout and AsyncTimeout)
    FailFastWhenDisconnected    bool         true  (BacklogPolicy.FailFast; false → BacklogPolicy.Default)
    Ssl                         bool         false (true forces TLS on; never turns off TLS the string enables)
    ClientCertificates          X509Certificate2Collection?                              code only
    CertificateValidation       Func<X509Certificate2, X509Chain?, SslPolicyErrors, bool>? code only
    // AbortOnConnectFail is always false. Timeouts on this type override those in the string.

AddRedisConnection(this IServiceCollection, IConfiguration, Action<RedisConnectionOptions>? configure = null) → IServiceCollection
    // AddValidatedOptions<RedisConnectionOptions, RedisConnectionOptionsValidator>(configuration, validateDataAnnotations: true),
    // then services.Configure(configure) (runs after binding)
AddRedisConnection(this IServiceCollection, Action<RedisConnectionOptions> configure) → IServiceCollection
    // AddOptions().Configure(configure).ValidateDataAnnotations().ValidateOnStart() + TryAddEnumerable(validator)
    // Both: throw InvalidOperationException when already registered (detected by the RedisConnectionProbe
    // IReadinessProbe descriptor); AddSingleton<IConnectionMultiplexer>(factory) + AddReadinessProbe<RedisConnectionProbe>().
    // Factory: BuildConfigurationOptions → WarnIfNonLoopbackWithoutTls (+102) → ConnectionMultiplexer.Connect →
    // ConnectionFailed (+101) / ConnectionRestored (+100) handlers → NotConnectedAtStartup (+103) when !IsConnected.
EnsureRedisConnectionRegistered(this IServiceCollection, string caller) → void
    // For sibling packages; throws InvalidOperationException naming the caller when AddRedisConnection is missing.

RedisReadinessProbeNames  (static class, namespace SharedKernel.Caching.Redis.Core.Health)
    const Connection = "redis"
RedisConnectionProbe  (internal sealed : SharedKernel.Primitives.Health.IReadinessProbe, Name "redis")
    // resolves the multiplexer lazily (a host constructs every probe to read its name);
    // !IsConnected → ReadinessReport.Unhealthy("Not connected to Redis.") without a PING; PING latency → Healthy;
    // PING RedisException/TimeoutException → Unhealthy "Redis did not answer PING ({type name})"; only cancellation throws.
    // The description never contains the connection string or an exception message. No IHealthCheck here:
    // the host's services.AddHealthChecks().AddSharedKernelReadiness() (SharedKernel.ServiceDefaults) reports every probe.
```

### `SharedKernel.Caching.Redis.PubSub` / `.Redis.HashStore` — provider contracts

```text
IRedisChannelService  (namespace SharedKernel.Caching.Redis.PubSub)   [at most once, not durable]
    PublishAsync(string channel, string message, CancellationToken ct = default)                 → ValueTask<long>
    PublishAsync<T>(string channel, T message, JsonTypeInfo<T> typeInfo, CancellationToken ct)    → ValueTask<long>
        // receiver count; on Cluster only the publishing node's subscribers
    SubscribeAsync(string channel, Func<string, CancellationToken, ValueTask> handler, CancellationToken ct) → ValueTask<IAsyncDisposable>
    SubscribeAsync<T>(string channel, JsonTypeInfo<T> typeInfo, Func<T, CancellationToken, ValueTask> handler, CancellationToken ct)
                                                                                                  → ValueTask<IAsyncDisposable>
        // each call = own ChannelMessageQueue (RedisChannel.Literal); handler awaited one message at a time in delivery order;
        // handler token cancelled on dispose; dispose unsubscribes only that queue, waits for a running handler unless
        // called from inside it (AsyncLocal), idempotent; bad JSON logged (+501) and skipped;
        // JSON literal null reaches the handler as null for reference/nullable T (documented, not rejected)
AddRedisChannelService(this IServiceCollection) → IServiceCollection        // TryAddSingleton; requires AddRedisConnection

IRedisHashService  (namespace SharedKernel.Caching.Redis.HashStore)
    GetFieldAsync<T>(key, field, JsonTypeInfo<T>, ct)                         → ValueTask<CacheLookup<T>>
    GetFieldsAsync<T>(key, IEnumerable<string> fields, JsonTypeInfo<T>, ct)   → ValueTask<IReadOnlyDictionary<string, T>>  // existing only; duplicates once; empty → no call
    GetAllFieldsAsync<T>(key, JsonTypeInfo<T>, ct)                            → ValueTask<IReadOnlyDictionary<string, T>>
    SetFieldAsync<T>(key, field, T value, JsonTypeInfo<T>, TimeSpan? timeToLive = null, ct) → ValueTask
    SetFieldsAsync<T>(key, IReadOnlyDictionary<string, T> values, JsonTypeInfo<T>, TimeSpan? timeToLive = null, ct) → ValueTask  // empty → ArgumentException
    IncrementFieldAsync(key, field, long delta = 1, TimeSpan? timeToLive = null, ct) → ValueTask<long>
    DeleteFieldAsync(key, field, ct) → ValueTask<bool>
    DeleteAsync(key, ct)             → ValueTask<bool>
    ExpireAsync(key, TimeSpan? timeToLive, ct) → ValueTask<bool>             // null → PERSIST
    // key: null/whitespace → ArgumentException; field: null/empty → ArgumentException; timeToLive <= 0 → ArgumentOutOfRangeException;
    // with timeToLive the write/increment and PEXPIRE run in one Lua script; without it the existing expiry is kept;
    // cancellation checked before sending only

ITypedHashStore<T>  (namespace SharedKernel.Caching.Redis.HashStore)   — the same nine members without typeInfo
AddRedisHashService(this IServiceCollection) → IServiceCollection         // TryAddSingleton; requires AddRedisConnection
AddTypedHashStore<T>(this IServiceCollection, JsonTypeInfo<T>) → IServiceCollection
    // calls AddRedisHashService; throws InvalidOperationException when ITypedHashStore<T> is already registered
```

### `SharedKernel.Caching.Redis.DistributedLocking` — public surface

```text
AddRedisDistributedLocking(this ICachingBuilder)     → ICachingBuilder
AddRedisDistributedLocking(this IServiceCollection)  → IServiceCollection
    // Both call one private Register: EnsureRedisConnectionRegistered, TryAddSingleton(TimeProvider.System),
    // TryAddSingleton<IDistributedLockService, RedisDistributedLockService>. Idempotent. No options type:
    // per-acquisition settings are DistributedLockOptions from Abstractions; connection settings come from AddRedisConnection.
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
    // ITenantCacheKeyProvider (each TryAdd — a consumer may override either); AddReadinessProbe<CacheReadinessProbe>()
    // — the "cache" probe reads a synthetic key through ICacheService (2 s timeout); a failure is Degraded, never
    // Unhealthy, because L1 and fail-safe may still serve.

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
AddRedisL2(this ICachingBuilder, Action<RedisL2Options>? configure = null)                   → ICachingBuilder
    // AddOptions<RedisL2Options>().Configure(configure?).ValidateOnStart() + TryAddEnumerable(RedisL2OptionsValidator)
AddRedisL2(this ICachingBuilder, IConfiguration, Action<RedisL2Options>? configure = null)    → ICachingBuilder
    // AddValidatedOptions<RedisL2Options, RedisL2OptionsValidator>(configuration) (section optional), then Configure(configure)
    // Both: EnsureRedisConnectionRegistered; a second call throws InvalidOperationException (private RedisL2Marker);
    // FusionCacheOptions.DistributedCacheCircuitBreakerDuration / BackplaneCircuitBreakerDuration from RedisL2Options;
    // FusionCacheOptions.BackplaneChannelPrefix = KeyPrefix when KeyPrefix is non-empty;
    // AddFusionCache().WithRegisteredOptions()
    //   .WithDistributedCache(sp => new RedisDistributedCache(shared multiplexer, KeyPrefix, TimeProvider ?? System))
    //   .WithRegisteredSerializer()
    //   .WithBackplane(sp => new RedisBackplane(ConnectionMultiplexerFactory = new SharedConnectionMultiplexer(shared))).
    // Registers NO IDistributedCache: a service that needs one for something else registers its own.

RedisL2Options : ISectionBoundOptions  (sealed class, namespace SharedKernel.Caching.Redis.Extensions)
    static SectionName                        "SharedKernel:Caching:Redis:L2"
    KeyPrefix                                 string    ""; not null; <= 64 chars. Stored key {KeyPrefix}{cache key};
                                                        also the backplane channel prefix when non-empty
    DistributedCacheCircuitBreakerDuration    TimeSpan  2 s; 0 (off) – 10 min
    BackplaneCircuitBreakerDuration           TimeSpan  2 s; 0 (off) – 10 min
```

---

## Implementation Rules

### Core cache rules

- `ICacheService` is **always** backed by FusionCache in production — never raw `IMemoryCache` or `IDistributedCache`.
- `CachePolicy` is a sealed, immutable record. Every `With…` method returns a new instance and validates its arguments; equality compares every setting and every tag in order.
- Use `GetOrSetAsync` for stampede protection. `TryGetAsync` followed by `SetAsync` is a bug wherever the value is computed.
- A miss is `CacheLookup<T>.Miss`; a cached `null` or `0` is a hit. Never collapse the two with `default`.
- `SkipCaching()` returns the value to every waiting caller and writes nothing. It is how a failed `Result` avoids being cached while keeping stampede protection (`SharedKernel.Application.Pipeline.Caching`'s `CachingBehavior` does exactly this). `SetDurations` overrides both durations for that one write.
- `ExpireAsync` versus `RemoveAsync`: expire keeps the old value available to fail-safe if recomputing fails; remove drops it (a removed value is never served by fail-safe — `RemoveAsync_FailSafePolicy_FactoryFailureCannotServeRemovedValue`).
- `ClearAsync` calls FusionCache `ClearAsync(allowFailSafe: false)` and logs a `Warning`. It removes every tenant's entries on every instance; use `ITenantCacheService.RemoveTenantAsync` for one tenant.
- Background work (eager refresh, a soft factory timeout) can run the factory after the caller's request ended. A factory that uses request-scoped services (a scoped `DbContext`) must run with `WithoutEagerRefresh()` and no factory timeouts.
- `CachePolicy.NeverExpire` is for reference data that changes only with an explicit `RemoveAsync`/`ExpireAsync`/tag removal. It carries no eager refresh.
- `LocalOnly()` maps to `SkipDistributedCacheRead`/`SkipDistributedCacheWrite`/`SkipBackplaneNotifications`: the entry never reaches Redis and never tells other instances to evict.
- The Redis L2 layer is opt-in. Without `AddRedisL2`, `ICacheService` operates L1-only.
- No static mutable state anywhere in this domain (the static `Meter`/`ActivitySource` instruments are the documented exception).
- **Cross-instance propagation needs no extra wiring.** With `AddRedisL2`, `RemoveAsync`, `ExpireAsync`, `RemoveByTagAsync`/`RemoveByTagsAsync` and `ClearAsync` reach every independently-constructed instance sharing the Redis server. `RemoveByTagAsync` is not a per-key operation: FusionCache writes a "clear before this timestamp" marker to a reserved tag key (`{KeyPrefix}__fc:t:{tag}`) and publishes that write on the same backplane channel as any other `Set`/`Remove` (channel prefix = `KeyPrefix` when set, otherwise FusionCache's default). Every entry carries its creation `Timestamp`; on read, an entry older than its tag's marker is treated as expired. `SkipBackplaneNotifications` (off unless `LocalOnly()`) is the only knob that disables this. Confirmed by Redis `MONITOR` inspection and `CrossInstanceTagInvalidationTests`.

### Key naming rule

- Keys: `{service}:{entity}:{id}[:{segment}...]`; tenant keys: `{service}:@{tenant}:{entity}:{id}[...]`; tenant tags: `@{tenant}:{tag}`; tenant-wide tag: `@{tenant}`. `{tenant}` is the `TenantId` as a lowercase GUID; every other caller-supplied part is escaped by `CacheKeyFormat.Escape` (`%`, `:`, `@`).
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
- Every `SubscribeAsync` call subscribes its own `ChannelMessageQueue` (`ISubscriber.SubscribeAsync(channel)`) and starts one `RedisChannelSubscription` read loop. Never register handlers through `ISubscriber.Subscribe(channel, Action)`: that shared callback list is what let a second subscriber keep the first alive and let unsubscribe remove the wrong one.
- The loop awaits the handler for one message before reading the next. Handler exceptions are caught and logged at `Error` (`+500`); an `OperationCanceledException` while the subscription's token is cancelled ends the loop silently.
- Typed subscriptions catch only `JsonException` from deserialization, log `+501` and skip the message.
- `DisposeAsync`: idempotent (`Interlocked`); `queue.UnsubscribeAsync()` (a `RedisException`/`TimeoutException` is logged at `+502` and ignored); cancel the handler token; await the loop unless the `AsyncLocal` running-handler marker is this subscription (disposal from inside the handler).
- Publish and subscribe check the caller's token before sending; nothing else is cancellable once sent.

### FusionCache STJ serializer AOT rule

- `CachingOptions.SerializerContext` must be set to the microservice's source-generated `JsonSerializerContext` in any NativeAOT build.
- When set, `AddSharedKernelCaching` builds `JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine(SerializerContext, EncryptedCacheEntryJsonContext.Default) }` for FusionCache's STJ serializer and for `CacheSerializationOptions`.
- When absent, FusionCache uses reflection-based STJ (`CacheSerializationOptions` falls back to `JsonSerializerDefaults.Web`) — acceptable for non-AOT builds, breaks NativeAOT.
- `AddSharedKernelCaching` reads `L1SizeLimit` and `SerializerContext` by invoking `configure` on a temporary `CachingOptions` at registration time; values bound later from configuration do not affect them.
- `AddRedisL2` must **not** call `.WithSystemTextJsonSerializer()` — it would overwrite the options-aware registration with a reflection-based default. It uses `.WithRegisteredSerializer()`.

### ITypedHashStore rules

- `ITypedHashStore<T>` is the preferred API for type-specific Redis hash operations in application code. `IRedisHashService` with explicit `JsonTypeInfo<T>` is the low-level primitive — both are valid.
- Register one `ITypedHashStore<T>` per DTO type via `AddTypedHashStore<T>(JsonTypeInfo<T>)`. It calls `AddRedisHashService()` itself (so it also requires `AddRedisConnection`) and throws `InvalidOperationException` when a store for `T` is already registered — never a silent duplicate.
- `TypedHashStore<T>` is `internal sealed` and only forwards to `IRedisHashService` with its fixed `JsonTypeInfo<T>`.

### RedisHashService rules

- All typed methods accept `JsonTypeInfo<T>` — no `typeof(T)` reflection.
- `IDatabase` comes from `IConnectionMultiplexer.GetDatabase()` per call (cheap; the multiplexer caches it).
- Reads return `CacheLookup<T>` (single field) or only the existing fields (multi-field) — never `default(T)` for a missing field.
- A write or increment with `timeToLive` runs the hash command and `PEXPIRE` in one Lua script. Never a separate expire call (the hash would exist without an expiry if the process died in between), and never `MULTI`/`EXEC` (Redis does not roll back, so a failed `HSET` would still set the expiry on a key of another type). Without `timeToLive`, no expiry command is sent, so the existing expiry is kept.
- `ExpireAsync(key, null)` is `KeyPersistAsync`.
- Argument validation (key null/whitespace, field null/empty, `timeToLive <= 0`, empty `SetFieldsAsync`) runs before the cancellation check and before any command.
- Do not compress or encrypt hash fields; callers who need it transform values before writing.

### DI startup guard rules

- Every Redis registration except `AddRedisConnection` calls `services.EnsureRedisConnectionRegistered(nameof(Method))` first, which throws `InvalidOperationException("{caller} uses the shared Redis connection. Call services.AddRedisConnection(...) before {caller}.")`. Registration order is therefore enforced at registration time, not at resolution.
- `AddRedisConnection` detects an existing registration by its `RedisConnectionProbe` `IReadinessProbe` descriptor and throws `InvalidOperationException` on a second call.
- `AddRedisL2` throws on a second call (private `RedisL2Marker`); `AddRedisDistributedLocking`, `AddRedisHashService` and `AddRedisChannelService` are idempotent (`TryAdd`); `AddTypedHashStore<T>` throws on a duplicate `T`.
- `AddCacheEncryption` and `AddBrotliCompression` guards: see "Cache-value encryption rules" and "Brotli compression rules".

### L1SizeLimit rule

- `CachingOptions.L1SizeLimit` is wired to a dedicated `MemoryCache(new MemoryCacheOptions { SizeLimit = L1SizeLimit })` via `.WithMemoryCache(...)`. Every entry has `Size = 1` (default entry options and `BuildEntryOptions`), so it is an **entry count**, not bytes. Default 10,000; set it explicitly under strict K8s memory limits.
- **L2 key format:** `{KeyPrefix}v2:{cache key}`. The distributed cache writes `{KeyPrefix}{key it is given}`; FusionCache adds `v2:` to every key it passes (`DistributedCacheKeyModifierMode.Prefix`, its default). An expiry under 1 ms deletes instead of sending an invalid `SETEX 0`, and TTLs use the system clock, as FusionCache does.
- `RedisDistributedCache` stores every L2 entry as one Redis **string** (`SET key value PX ttl`); raw reads use `IDatabase.StringGetAsync(key)`. The stored JSON is FusionCache's distributed-entry envelope (`Value`/`Timestamp`/`LogicalExpirationTimestamp`/`Tags`/`Metadata`); the cached value is nested in `Value`.
- **Expiry:** absolute only. `AbsoluteExpirationRelativeToNow` or `AbsoluteExpiration` (against `TimeProvider`) becomes the key TTL; no expiry means no TTL; a TTL of zero or less deletes the key. `SlidingExpiration` throws `NotSupportedException` (FusionCache never sets it); `Refresh`/`RefreshAsync` do nothing.

### L2 connection ownership rules

- **Never hand the shared multiplexer to a component that may close or dispose it.** `Microsoft.Extensions.Caching.StackExchangeRedis`'s `RedisCache` closed its connection on dispose (reaching the shared one through `IDatabase.Multiplexer`), which is why it was replaced; do not reintroduce it.
- `RedisDistributedCache` is passed to FusionCache through `WithDistributedCache(factory)` and is **not** registered as `IDistributedCache`. Keep it that way: a registered `IDistributedCache` would be resolved and disposed by other components.
- The FusionCache `RedisBackplane` disposes its connection when it unsubscribes, so it receives `SharedConnectionMultiplexer`, a pass-through wrapper whose `Close`/`CloseAsync`/`Dispose`/`DisposeAsync` do nothing. The DI container disposes the real multiplexer once.
- A non-empty `KeyPrefix` also sets `FusionCacheOptions.BackplaneChannelPrefix`, so deployments sharing one Redis neither share stored entries nor receive each other's backplane notifications.

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

- `RedisChannelService`, `RedisHashService`, `RedisDistributedLockService`, the internal L2 `RedisDistributedCache` and the FusionCache backplane all use the single `IConnectionMultiplexer` registered by `AddRedisConnection`. The distributed cache receives it directly; the backplane receives it through `ConnectionMultiplexerFactory`, wrapped in `SharedConnectionMultiplexer`. Neither is ever given a connection string. No package creates another connection.

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
- **Keep-alive and loss.** `RedisDistributedLock` extends every `Expiry / 3` on a `PeriodicTimer(TimeProvider)` with a compare-owner `PEXPIRE` script. The lock is marked lost (`IsHeld = false`, `LostToken` cancelled, `Error` log `Caching + 307`, keep-alive stopped) when an extension finds another owner, or when a one-shot loss timer (`TimeProvider.CreateTimer`) fires `Expiry * 5 / 6` after the **send** timestamp of the acquisition or of the last successful extension. Because the key cannot expire earlier than `Expiry` after that send time, loss is always reported at least `Expiry / 6` before another owner can acquire. Never measure the deadline from the reply time or let it reach `Expiry`: the pre-P-550 "past the expiry" rule let a second replica acquire while `IsHeld` was still `true`. Store failures during keep-alive are logged (`Caching + 306`) and retried on the next tick.
- **Release.** `DisposeAsync` is idempotent: it disposes the loss timer (waiting for a running callback), stops and awaits the keep-alive, runs a compare-owner `DEL` only while the state is still held (a lost lock is left to expire; a store failure is logged at `Caching + 305` and the key expires on its own), then cancels `LostToken`. `LostToken` means "lost or released".
- **Owner ids** are fresh `Guid`s per acquisition (`lease:` prefix for leases). Retry waits use `Task.Delay(delay, TimeProvider, ct)`; the retry interval is capped at the remaining wait time.
- **Lease expiry** (`DistributedLease.ExpiresAt`) is `TimeProvider.GetUtcNow()` at request time plus the duration — approximate across machines.
- **Failover caveat:** after a primary failover, a replica that had not received the lock key can grant it again. Fencing tokens are the protection in that window, so the protected write path must check them.
- RedLock.net must not come back: it cannot issue a token in the acquisition step. `00.Governance`'s `RedisTopologyRules.DistributedLockingNeverReferencesRedLock` locks this.

### Channel reconnect rules

- **Do not resubscribe.** StackExchange.Redis restores every subscription, including every `ChannelMessageQueue`, after a reconnect. `RedisChannelService` does not handle `ConnectionRestored`/`ConnectionFailed`, keeps no channel registry and exposes no health state. Replaying subscriptions on `ConnectionRestored` doubled every delivery after each reconnect (verified against a real Redis in P-550).
- Messages published while the subscriber connection is down are lost: at-most-once is the documented contract, not a defect to fix here.
- Connection health for every Redis package is the `redis` readiness probe registered by `.Redis.Core`'s `AddRedisConnection`; no capability package adds its own.

### Cache warmup rules

- `ICacheWarmupStrategy.WarmupAsync` uses `ValueTask`.
- `CacheWarmupHostedService` (internal, `IHostedLifecycleService`) runs strategies in ascending `Order`, catches per-strategy exceptions, logs at `Error`, and continues — a failed strategy never crashes the pod.
- `AddCacheWarmup<TStrategy>` uses `TryAddEnumerable` for the strategy and for the hosted service (idempotent).
- `CachingOptions.WaitForWarmup = true` runs warmup inside `StartingAsync`, which the host completes for every lifecycle service before any `StartAsync` — including the web server's — so no traffic or readiness arrives until warmup finishes (P-548; the former `StartedAsync` wait ran after the server was already listening). Otherwise `StartAsync` starts warmup in the background and `StopAsync` cancels and awaits it. Cancellation propagates; strategy failures do not.

### Tenant cache service rules

> **`ITenantCacheService` is the recommended entry point for tenant data.** `ITenantCacheKeyProvider` remains available for callers that need raw tenant keys. `ICacheService` stays the entry point for genuinely global data.

- Every method takes an explicit, non-defaulted `TenantId tenantId` and `(entity, id)` — never a pre-built key, never resolved from ambient context (no `IRequestContext`, no `IHttpContextAccessor`). A caller holding `IRequestContext.TenantId` (`TenantId?`) decides what a missing tenant means before calling; `default(TenantId)` throws `ArgumentException`.
- `TenantCacheService` builds the key with `ITenantCacheKeyProvider.BuildTenantKey` and never formats keys itself.
- **Tag scoping is mandatory:** every write passes `policy.ForTenant(tenantId)`, so tags become `@{tenant}:{tag}` and every entry also carries the tenant-wide tag `@{tenant}`. `RemoveByTagAsync(tenantId, tag)` removes `BuildTenantTag(tenantId, tag)`; `RemoveTenantAsync(tenantId)` removes `BuildTenantWideTag(tenantId)`. Because every part is escaped and global tags cannot start with `@`, two tenants sharing a tag name — or a global tag named like a tenant tag — can never cross-invalidate (a cross-tenant **invalidation** vector, worse than a read leak).
- A policy that is already tenant-scoped throws `InvalidOperationException`: pass the unscoped policy.
- `AddTenantCacheService()` only registers `ITenantCacheService`; the key providers come from `AddSharedKernelCaching()`.

### L2 circuit breaker rules

- The only circuit breakers in this domain are FusionCache's `DistributedCacheCircuitBreakerDuration` and `BackplaneCircuitBreakerDuration`, set by `AddRedisL2` from `RedisL2Options` (default 2 s each; `TimeSpan.Zero` turns one off; validator caps at 10 min). They wrap every distributed-cache and backplane operation, which the removed Polly pipeline never did.
- No Polly dependency anywhere in `02.Caching`. The hash store, pub/sub and locks rely on the connection's `FailFastWhenDisconnected` to fail immediately during an outage, and surface the Redis exception (locks wrap it in `DistributedLockUnavailableException`).
- While the backplane breaker is open, removals and expirations are not sent to other instances; they keep their memory entries until those expire. Document this wherever short `L1Duration` matters.

### OTel metrics rules

- `Meter("SharedKernel.Caching", <assembly informational version>)`, static readonly on `FusionCacheService`. The name is fixed: `SharedKernel.ServiceDefaults`' `WithCachingTelemetry` subscribes to it.
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
- `SharedKernel.ServiceDefaults`' `WithCachingTelemetry` registers both the meter and the source by name.

### Redis Connection Core rules

- `SharedKernel.Caching.Redis.Core` is the **single registration point** for `IConnectionMultiplexer`, and `AddRedisConnection` may be called **once** (a second call throws). No "first caller wins": conflicting settings must fail loudly.
- It is the only package that calls `ConfigurationOptions.Parse` or `ConnectionMultiplexer.Connect`.
- `AbortOnConnectFail = false` always: an unreachable server at first resolution logs `+103` and keeps reconnecting; it never fails startup. Readiness reports the outage through the `redis` readiness probe.
- `FailFastWhenDisconnected` (default `true`) maps to `BacklogPolicy.FailFast`, so commands fail at once during an outage and the cache breaker/fail-safe take over; `false` maps to `BacklogPolicy.Default`.
- `ConnectTimeout`/`CommandTimeout` always overwrite the string's `ConnectTimeout`/`SyncTimeout`/`AsyncTimeout`.
- `.Redis.Core` references no `SharedKernel.Caching` package, and the abstractions package declares no connection type.
- `AddRedisConnection` is a plain `IServiceCollection` extension; fluent `ICachingBuilder` chaining belongs to the consuming packages.
- The probe never opens its own connection, never throws for a Redis failure (only for cancellation) and never puts the connection string or an exception message in `Description`.

#### Fail-fast validation and TLS/mTLS surface

- Configuration overload: `AddValidatedOptions<RedisConnectionOptions, RedisConnectionOptionsValidator>(configuration, validateDataAnnotations: true)` binds `SharedKernel:Caching:Redis`, then `services.Configure(configure)` runs after binding (for `ClientCertificates`/`CertificateValidation`). Code overload: `AddOptions<RedisConnectionOptions>().Configure(configure).ValidateDataAnnotations().ValidateOnStart()` plus the validator. The multiplexer factory resolves the validated `IOptions<RedisConnectionOptions>.Value`, so every setting is read lazily, on first resolution.
- `RedisConnectionOptionsValidator` reports a connection string that does not parse (catching `ArgumentException`/`RedisConnectionException`) or names no endpoint with a fixed message — never the parser's message, which can echo the password.
- `Ssl`, `ClientCertificates` and `CertificateValidation` compose into the built `ConfigurationOptions` before `ConnectionMultiplexer.Connect(...)`. `Ssl = true` sets `ConfigurationOptions.Ssl`; `Ssl = false` leaves TLS enabled by the string untouched.
- **StackExchange.Redis 2.13.1 has no `ConfigurationOptions.CertificateSelection`/`CertificateValidation`.** TLS/mTLS goes through `ConfigurationOptions.SslClientAuthenticationOptions` (`Func<string, SslClientAuthenticationOptions>`): `ClientCertificates` → `SslClientAuthenticationOptions.ClientCertificates`; `CertificateValidation` is bridged into `RemoteCertificateValidationCallback` (converting `X509Certificate?` to `X509Certificate2`, rejecting `null`). The delegate is set only when one of the two is supplied; a bare `Ssl = true` keeps StackExchange.Redis's default TLS behaviour.
- A `[LoggerMessage]` `Warning` (`Caching + 102`) — **never an exception**, since sidecar/mesh-terminated TLS is legitimate — fires once, from the multiplexer factory, when TLS is off after composition and an endpoint is non-loopback (`IPAddress.IsLoopback`, or a `DnsEndPoint` named `localhost`). The check stops at the first such endpoint.
- `BuildConfigurationOptions`/`WarnIfNonLoopbackWithoutTls`/`IsLoopback` are `internal` for testability without a live Redis.
- TLS protects values in transit; `AddCacheEncryption` protects them at rest. Use both for sensitive data.

### Redis distributed locking package rules

- `SharedKernel.Caching.Redis.DistributedLocking` depends only on `Abstractions` + `Redis.Core` (+ `SharedKernel.Primitives` for EventIds).
- `AddRedisDistributedLocking()` exists on `ICachingBuilder` and on `IServiceCollection`; both call one private `Register` and register exactly the same services. Neither is obsolete; there is no adapter builder class and no options type.
- `Register` calls `EnsureRedisConnectionRegistered` and registers `TimeProvider.System` only if no `TimeProvider` is registered (tests inject a fake).
- `RedisDistributedLockService`, `RedisDistributedLock` and `RedisLockScripts` are `internal`.
- Lock keys are `sharedkernel:lock:{resource}` and `sharedkernel:lock-fencing:{resource}`, with the resource used as given. No key prefix option, no expiring fencing counters, no lock metrics (considered in P-550, not selected).

### Redis Hash Store package rules

- `SharedKernel.Caching.Redis.HashStore` depends only on `Abstractions` (for `CacheLookup<T>`) + `Redis.Core`. No logging.
- `IRedisHashService`/`ITypedHashStore<T>` are declared here, in namespace `SharedKernel.Caching.Redis.HashStore` (P-547).
- `AddRedisHashService()` and `AddTypedHashStore<T>()` are `IServiceCollection` extensions only; there is no `ICachingBuilder` overload, because hash storage is not part of the cache.
- Keys are used exactly as given: no prefix, no tenant scoping. Callers include service and tenant.

### Redis Pub/Sub package rules

- `SharedKernel.Caching.Redis.PubSub` depends only on `Redis.Core` (+ `SharedKernel.Primitives`). It has no hosting dependency: it ships no background service.
- `IRedisChannelService` is declared here, in namespace `SharedKernel.Caching.Redis.PubSub` (P-547).
- `AddRedisChannelService()` is the only registration, an `IServiceCollection` extension; there is no `ICachingBuilder` overload.
- There is no cache-invalidation bus or receiver. Cache invalidation across instances is the FusionCache backplane's job; do not rebuild one on this package.

### Redis package topology rules

- **No new `IConnectionMultiplexer` registrations** outside `.Redis.Core`'s `AddRedisConnection`. Capability packages call `EnsureRedisConnectionRegistered` and resolve the multiplexer — never `AddRedisConnection` on the caller's behalf, never a connection-string parameter, never `ConnectionMultiplexer.Connect(...)`.
- **Sibling packages never reference each other.** `.Redis`, `.DistributedLocking`, `.HashStore` and `.PubSub` depend only on `Redis.Core` (and `Abstractions` where they need its types).
- `SharedKernel.Caching.Redis` contains only `AddRedisL2`, `RedisL2Options`, its validator and the FusionCache distributed-cache/backplane wiring.
- Locked by `00.Governance`'s `RedisTopologyRules` (`RedisCoreNeverReferencesCapabilityPackages`, `CapabilityPackagesNeverReferenceEachOther`, `PubSubNeverReferencesMessaging`, `MessagingNeverReferencesCaching`, `CachingAbstractionsHasNoInfrastructureDependencies`, `CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions`, `CachingAbstractionsDeclaresNoProviderSpecificTypes`, `DistributedLockingNeverReferencesRedLock`).

### AOT compatibility

- FusionCache is AOT-compatible — verify release notes on every upgrade.
- `.Redis.Core` is the sole owner of `ConfigurationOptions.Parse`/`ConnectionMultiplexer.Connect`.
- Configuration binding (`AddRedisConnection(IConfiguration)`, `AddRedisL2(IConfiguration)`) is reflection-based, like every `AddValidatedOptions` caller; the delegate overloads avoid binding.
- The lock implementation is plain Lua over `IDatabase.ScriptEvaluateAsync` — no third-party locking library.
- `IRedisHashService` and the typed `IRedisChannelService` overloads take `JsonTypeInfo<T>`. READMEs do not claim AOT compatibility for these packages.
- `AddRedisL2` must not re-register the STJ serializer (it would overwrite the user's `SerializerContext`).

### Logging (EventId sub-blocks)

`02.Caching` owns `LoggingEventIdRanges.Caching` (2000–2999, `SharedKernel.Primitives`). Sub-blocks, 100 per package in Packages-table order:

| Package | Sub-block | Events |
| ------- | --------- | ------ |
| `SharedKernel.Caching.Abstractions` | — | No logging (no logging dependency, locked by the reference allow-list) |
| `SharedKernel.Caching.FusionCache` | `Caching + 0` .. `+ 99` | `CacheWarmupHostedService` `+0`..`+7`; `FusionCacheService` `+10` miss, `+11` set, `+12` factory invoked, `+13` removed, `+14` tag removed, `+16` expired (Debug), `+17` cleared (Warning); `EncryptedCacheService` `+15` decrypt failed (Warning) |
| `SharedKernel.Caching.Redis.Core` | `Caching + 100` .. `+ 199` | `RedisConnectionCoreExtensions` (category `SharedKernel.Caching.Redis.Core.RedisConnection`) `+100` connection restored (Information), `+101` connection failed (Warning), `+102` non-loopback endpoint without TLS (Warning), `+103` not reachable at startup (Warning) |
| `SharedKernel.Caching.Redis` (L2) | `Caching + 200` .. `+ 299` | Reserved — no logging today |
| `SharedKernel.Caching.Redis.DistributedLocking` | `Caching + 300` .. `+ 399` | `RedisDistributedLockService` `+300` lock acquired, `+301` lock contended, `+302` lease acquired, `+303` lease contended (Debug); `RedisDistributedLock` `+304` released (Debug), `+305` release failed (Warning), `+306` extend failed (Warning), `+307` lock lost (Error) |
| `SharedKernel.Caching.Redis.HashStore` | `Caching + 400` .. `+ 499` | Reserved — no logging today |
| `SharedKernel.Caching.Redis.PubSub` | `Caching + 500` .. `+ 599` | `RedisChannelSubscription.Log` (category `SharedKernel.Caching.Redis.PubSub.RedisChannelService`) `+500` handler failed (Error), `+501` message not deserialized (Warning), `+502` unsubscribe failed (Warning) |

- Every `EventId` is written `LoggingEventIdRanges.Caching + {offset}` — never a bare literal.
- The four logging packages (`FusionCache`, `Redis.Core`, `Redis.DistributedLocking`, `Redis.PubSub`) reference `SharedKernel.Primitives` for the constant.
- Every log statement uses the `[LoggerMessage]` source-generated pattern in a nested `static partial class Log`; no direct `ILogger.LogX` call and no `LoggerMessage.Define` (SK0020/SK0021, `LoggingEventIdIntegrityAssertion`).
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

// Every Redis composition: the shared connection, exactly once, before any other Redis registration
services.AddRedisConnection(configuration);                    // SharedKernel:Caching:Redis
// or code only:
services.AddRedisConnection(o => { o.ConnectionString = connectionString; o.Ssl = true; });

// L1 + L2 (Redis distributed layer + backplane) — no connection string
services.AddSharedKernelCaching(o =>
         {
             o.ServiceName = "my-service";
             o.SerializerContext = MyAppSerializerContext.Default;
         })
        .AddRedisL2(o => o.KeyPrefix = "prod:");               // or .AddRedisL2(configuration) for SharedKernel:Caching:Redis:L2

// Tenant-scoped cache — the recommended entry point for tenant data
services.AddSharedKernelCaching(o => o.ServiceName = "my-service")
        .AddTenantCacheService();
// Inject: ITenantCacheService → GetOrSetAsync(tenantId /* TenantId */, entity, id, factory, policy, ct)
//         ITenantCacheKeyProvider / ICacheKeyProvider are registered by AddSharedKernelCaching

// Brotli compression for large L2 payloads
services.AddSharedKernelCaching(o => o.ServiceName = "my-service")
        .AddRedisL2()
        .AddBrotliCompression(o => o.ThresholdBytes = 2048);

// Cache-value encryption at rest — cryptography and the cache first; Brotli before encryption
services.AddSingleton<IEncryptionKeyProvider>(keyProvider);    // any provider, KMS-backed included
services.AddSharedKernelCryptography(configuration)
        .AddSymmetricEncryption();                             // 01.Core — ISymmetricEncryptionService
services.AddSharedKernelCaching(o => o.ServiceName = "my-service")
        .AddRedisL2()
        .AddBrotliCompression(o => o.ThresholdBytes = 2048)  // compression first
        .AddCacheEncryption();                                 // encryption last

// Distributed locking alongside the cache
services.AddSharedKernelCaching(o => o.ServiceName = "billing")
        .AddRedisL2()
        .AddRedisDistributedLocking();

// Lock-only host — no FusionCache, no ICachingBuilder
services.AddRedisConnection(configuration).AddRedisDistributedLocking();
// await using IDistributedLock? handle = await locks.TryAcquireAsync(resource,
//     new DistributedLockOptions { Expiry = TimeSpan.FromSeconds(30), WaitTime = TimeSpan.FromSeconds(5) }, ct);
// DistributedLease? lease = await locks.TryAcquireLeaseAsync(resource, TimeSpan.FromMinutes(5), ct);
// Both return null only on contention; an unreachable store throws DistributedLockUnavailableException.

// Redis Pub/Sub channel service — IServiceCollection only
services.AddRedisConnection(configuration).AddRedisChannelService();
// await using IAsyncDisposable sub = await channels.SubscribeAsync(channel, (msg, token) => HandleAsync(msg, token), ct);

// Redis hash service — typed per DTO, IServiceCollection only
services.AddRedisConnection(configuration)
        .AddRedisHashService()
        .AddTypedHashStore(MyAppSerializerContext.Default.OrderDto)
        .AddTypedHashStore(MyAppSerializerContext.Default.CustomerDto);
// Inject: IRedisHashService, ITypedHashStore<OrderDto>, ITypedHashStore<CustomerDto>

// Cache warmup
services.AddSharedKernelCaching(o => { o.ServiceName = "my-service"; o.WaitForWarmup = true; })
        .AddCacheWarmup<MyProductCatalogWarmup>()
        .AddCacheWarmup<MyUserPreferencesWarmup>();

// L2 circuit breakers (defaults 2 s; TimeSpan.Zero = off)
services.AddSharedKernelCaching(o => o.ServiceName = "my-service")
        .AddRedisL2(o =>
        {
            o.DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(5);
            o.BackplaneCircuitBreakerDuration = TimeSpan.FromSeconds(5);
        });

// Redis connection with mutual TLS — composes into ConfigurationOptions.SslClientAuthenticationOptions
services.AddRedisConnection(configuration, o =>
{
    o.Ssl = true;
    o.ClientCertificates = [clientCertificate];
    o.CertificateValidation = (cert, chain, errors) => ValidateAgainstPrivateRoot(cert, chain, errors);
});

// Readiness: AddRedisConnection registers the "redis" IReadinessProbe, AddSharedKernelCaching the "cache" one;
// the host reports them with services.AddHealthChecks().AddSharedKernelReadiness() (SharedKernel.ServiceDefaults).
```

---

## Test Rules

- Every package owns a nested `.Tests` project; tests are partitioned by capability, not duplicated:
  - `SharedKernel.Caching.Abstractions.Tests` (78) — `CachePolicyTests` (presets, validation, `ForTenant`, equality), `CacheKeyFormatTests` (escaping, service-name rule, no-collision properties: a global key never equals a tenant key, a separator inside a part never collides, tenant tags never collide), `CacheLookupTests`, `CacheFactoryContextTests`, `DistributedLockContractTests` (options, lease, exception). No provider reference.
  - `SharedKernel.Caching.FusionCache.Tests` (198) — `FusionCacheServiceTests`, `Policies/BuildEntryOptionsTests` (every `CachePolicy` setting → `FusionCacheEntryOptions`, `ApplyFactoryDecision`), `BatchOperationsTests`, `NullableFactoryTests`, `TenantCacheServiceTests`, `CacheKeyProviderTests`, `DI/CachingDiRegistrationTests`, `Encryption/EncryptedCacheServiceTests`, `Serialization/*`, `SerializerContextTests`, `L1SizeLimitTests`, `CacheWarmupTests`, `OtelMetricsTests`, `OtelTracingTests`.
  - `SharedKernel.Caching.Redis.Tests` — L2 round-trip and stampede protection over the shared connection, batch integration, FusionCache circuit breakers (options mapped onto `FusionCacheOptions`, L1 service while Redis is down), L1 fallback, `KeyPrefix` (stored as `{KeyPrefix}{key}` Redis strings; two deployments with different prefixes on one Redis neither share entries nor receive each other's notifications), no `IDistributedCache` registered, the shared connection still usable by locks/hash/pub-sub after the cache and backplane are disposed, sliding expiration rejected, `RedisL2Options` binding/validation, `Integration/CacheEncryptionAtRestTests`, `Integration/CrossInstanceTagInvalidationTests`.
  - `SharedKernel.Caching.Redis.DistributedLocking.Tests` — `Abstractions/IDistributedLockServiceContractTests` (abstract contract base, run against real Redis by `RedisDistributedLockServiceContractTests`), `RedisDistributedLockTests` (raw key/TTL, keep-alive, loss, owner-safe release, fencing counter), `LockStoreUnavailableTests`, `DI/RedisDistributedLockingExtensionsTests`.
  - `SharedKernel.Caching.Redis.HashStore.Tests` — `RedisHashService` and `ITypedHashStore<T>` integration (lookups, multi-field, TTL, delete, expire), DI guards.
  - `SharedKernel.Caching.Redis.PubSub.Tests` — publish/subscribe round-trip, independent subscriptions, disposal, ordering, handler failures, bad JSON, reconnect, DI guard.
  - `SharedKernel.Caching.Redis.Core.Tests` — registration (once only, order guard), options binding and validation, TLS composition, loopback detection, probe.
- Redis integration tests **must** use Testcontainers (`Testcontainers.Redis`), never an external Redis. `Testcontainers.Redis` stays on the same version as `16.Testing/SharedKernel.Testing.Internal` (central package version, 4.13.0); a lower direct pin resolves a mismatched `DotNet.Testcontainers` assembly and throws `MissingMethodException` at container start.
- Every Redis test composition calls `AddRedisConnection` first, exactly as consumers do; no test registers its own `IConnectionMultiplexer` for a package under test. A test that needs an `ICachingBuilder` without `.FusionCache` uses its own small `TestCachingBuilder` — never a reference to a sibling provider package.
- Tests for removed types (`RedisCircuitBreakerOptions`, `RedisConnectionHealthTracker`, `ConnectionHealthState`, `ResiliencePipeline` injection, channel resubscription) are deleted, not adapted.
- **Hit versus miss** must be covered: a cached `null` and a cached `0` are hits (`TryGetAsync_DistinguishesMiss_FromCachedNull_AndCachedZero`, `NullableFactory_*`).
- **Stampede protection** must be covered: parallel `GetOrSetAsync` calls invoke the factory exactly once (L1 and with Redis L2), including when interleaved with batch calls.
- **Factory context** must be covered: `SkipCaching` returns the value and stores nothing; skipping for some calls still caches the others; `SetDurations` overrides expire independently; a throwing factory caches nothing and propagates.
- **Expire versus remove under fail-safe:** after `ExpireAsync`, a failing factory serves the old value; after `RemoveAsync`, it cannot.
- **Service name validation:** an unset or invalid `ServiceName` fails options resolution, key-provider resolution and host start; a valid one passes.
- **Tenant isolation** must be covered at both layers: same `(entity, id)` for two tenants never collides; tenant A's tag removal never evicts tenant B's entries; a global `RemoveByTagAsync` with the same tag name — or a tag named like a tenant tag — never evicts tenant entries; `RemoveTenantAsync` removes only that tenant, including entries written through `GetOrSetAsync`; an already tenant-scoped policy throws.
- **Locks** must cover: free resource → held handle with a positive token; contended → `null` (with zero wait immediately, with a shorter wait after waiting, with a longer wait acquiring after release); parallel attempts → exactly one winner (locks and leases); tokens strictly increase across locks and leases and a stale holder is rejected by a guard that accepted a newer token; contended attempts do not advance the counter; counters are independent per resource; dispose is idempotent, deletes only its own key and keeps the counter; a deleted or foreign-owned key is reported lost; a held lock outlives its expiry while kept alive; an unreachable store throws `DistributedLockUnavailableException` for locks, leases and waiting attempts; a held lock whose extensions keep failing is reported lost at 5/6 of its expiry measured from the last successful send, before the key can expire (fake `TimeProvider` for the deadline, a real store outage for the end-to-end case); a lost lock's dispose deletes nothing; cancellation throws.
- **L1-only fallback:** the cache works with no Redis configured.
- **Batch operations:** mixed hits/misses; set-many then get-many; empty key list → empty dictionary; duplicate keys → one lookup per distinct key; single policy (tag) applies to every entry; 200 keys under bounded concurrency lose or duplicate nothing; a 50-key batch against real Redis completes in ≤ 75% of 50 sequential `TryGetAsync` calls (CI-tolerant margin).
- **Cache encryption** (`EncryptedCacheServiceTests`, 27): round-trip; a payload replayed under a different key fails authentication, is a miss and is evicted; tampered ciphertext and a non-payload value are misses and are evicted; `TryGetManyAsync` maps a decrypt failure and an absent key to misses while a valid key decrypts; a corrupted entry in `GetOrSetAsync` is evicted and recomputed once through the inner service (also with a real FusionCache); `SkipCaching` passes through; the stored entry is `EncryptedPayload.ToBytes()` bound to the key; every member round-trips with an async-only key provider; the wrapped factory re-invoked from an unrelated execution context still encrypts correctly; compress-then-encrypt produces shorter ciphertext; `AddBrotliCompression().AddCacheEncryption()` unwraps the serializer and round-trips, the reverse order throws; each registration guard throws; with `AddTenantCacheService()`, a cross-tenant replay for the same `(entity, id)` fails. `CacheEncryptionAtRestTests` proves a raw Redis read (`StringGetAsync(key)`) contains neither the plaintext nor the DTO property names, and the envelope's `Value` is Base64 that parses with `EncryptedPayload.TryParse`.
- **Cross-instance propagation** (`CrossInstanceTagInvalidationTests`): two fully independent `ServiceProvider`s (own multiplexer, own L1, own `IFusionCache`) share one Testcontainers Redis and the same `ServiceName` — never two scopes of one container, which could pass with the guarantee broken. `RemoveByTagAsync`, `RemoveAsync` and `ExpireAsync` on instance A make instance B recompute; a `LocalOnly()` entry on A is neither written to Redis nor visible on B. Assertions bounded-poll (100 ms interval, 5 s timeout), never a fixed `Task.Delay`.
- **Lock expiry is a real Redis TTL**, so expiry tests wait real time: short durations plus a small margin. Loss-detection tests wait on `LostToken` with a timeout (`Task.Delay(timeout, handle.LostToken)`), never an unbounded wait.
- **OTel tests:** `OtelMetricsTests` verifies each instrument via `MeterListener`; `OtelTracingTests` verifies span name, `ActivityKind.Client`, `cache.key_prefix`/`cache.outcome` and the source's name/version. Assertions are existence-style (`Assert.Contains`, `>= 1`), because other test classes run concurrently against the same static instruments.
- **Listener-callback accumulators must be thread-safe.** `MeterListener`/`ActivityListener` callbacks run on whatever thread records the measurement or stops the activity, so two concurrent test classes can call one test's callback from two threads. A plain `Dictionary` read-then-write threw `InvalidOperationException` from unrelated test classes (~25% of runs) until switched to `ConcurrentDictionary.AddOrUpdate`; spans are collected in a `ConcurrentBag<Activity>`. Use `ConcurrentDictionary`/`ConcurrentBag`/`ConcurrentQueue` in any listener-based test.
- **L2 circuit breakers:** both durations reach `FusionCacheOptions`; `TimeSpan.Zero` is accepted; negative or over 10 minutes fails validation; with Redis unreachable, cache calls complete from memory without waiting per call.
- **Redis connection hardening** (`RedisConnectionValidationTests`/`RedisTlsConfigurationTests`): invalid options (missing, unparsable or endpoint-less connection string; timeouts outside 100 ms – 1 min) throw `OptionsValidationException` via `IStartupValidator.Validate()` and on first `IOptions<T>.Value` access, with no connection-string text in the message; configuration binding from `SharedKernel:Caching:Redis` and the post-bind `configure`; timeouts, fail-fast and `Ssl` compose into `ConfigurationOptions`; client certificates and validation compose into `SslClientAuthenticationOptions`; the non-loopback warning fires exactly once and never for loopback or TLS.
- **Registration guards:** a second `AddRedisConnection` throws; every capability registration throws without it; `AddRedisL2` twice throws; `AddTypedHashStore<T>` twice for one `T` throws; idempotent registrations do not duplicate descriptors.
- **Readiness probes:** `redis` is healthy with latency against a live container and unhealthy (not thrown) when disconnected or when `PING` fails; `cache` is degraded (not unhealthy) when the read fails; descriptions carry no secrets; cancellation throws.
- **Pub/Sub subscriptions:** two subscriptions on one channel both receive; disposing one leaves the other receiving; per-subscription order is delivery order and handlers never overlap; a throwing handler does not stop later messages; invalid JSON is skipped; disposal from inside the handler completes; after forcibly killing the subscriber connection on a live container, each subscription receives each later message **exactly once** (no duplicate delivery).
- **Hash store:** a missing field is a miss while a stored `0` is a hit; `GetFieldsAsync` omits missing fields; a write with `timeToLive` leaves a key TTL and a write without it keeps the existing TTL; `IncrementFieldAsync` with TTL; `DeleteAsync`/`ExpireAsync(null)` results; argument validation.
- **Cross-package composition:** a container calling only `AddRedisConnection` + one of `AddRedisDistributedLocking`/`AddRedisHashService`/`AddRedisChannelService` resolves (proven end to end by `consumer-verify` surfaces 3–5).
- **Cache warmup:** ascending order, a failed strategy does not abort the others, `WaitForWarmup` finishes warmup in `StartingAsync` before any hosted service starts, background warmup is cancelled on stop, registration is idempotent.

---

## Changelog

History of this domain (WO-003 through WO-086) is recorded in `02.Caching/state-map.md`; the root `CLAUDE.changelog.md` carries the platform-wide entries.
