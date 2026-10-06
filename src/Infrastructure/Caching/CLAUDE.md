# 02.Caching — Domain Brain

> Hybrid L1 (in-process) / L2 (Redis) caching on **ZiggyCreatures.FusionCache** — stampede protection, fail-safe,
> eager refresh, tags, factory-controlled caching, tenant-safe keys — plus Redis distributed locks and leases with
> fencing tokens, Redis hash storage and loss-tolerant Redis Pub/Sub. Application code depends only on the
> provider-neutral `SharedKernel.Caching.Abstractions`; a service that needs only L1 references no Redis package.
> This domain does **not** own durable messaging (`07.Messaging`), idempotency stores (`18.Idempotency`, which builds on
> `Redis.Core`) or health endpoints (`13.ServiceDefaults` maps the probes registered here). Philosophy:
> provider-neutral contracts, stampede-proof, an outage is never reported as contention. Consumers read each package's
> `README.md`; the living board is `state-map.md`.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Caching.Abstractions` | Abstractions | `ICacheService`, `ITenantCacheService`, `CacheLookup<T>`, `CacheFactoryContext`, `CachePolicy`, `CacheKeyFormat`, `ICacheKeyProvider`/`ITenantCacheKeyProvider`, `ICacheWarmupStrategy`, `ICachingBuilder`, `IDistributedLockService`/`IDistributedLock`, `DistributedLockOptions`, `DistributedLease`, `DistributedLockUnavailableException`. References only `SharedKernel.Execution` (for `TenantId`) and DI abstractions; no logging, options or hosting |
| `SharedKernel.Caching.FusionCache` | Adapter | The cache: `AddSharedKernelCaching`, tenant cache service, Brotli L2 compression, value encryption at rest, warmup, OTel meter/source, the `cache` readiness probe. Implementations (`FusionCacheService`, `CacheKeyProvider`, `TenantCacheService`, `EncryptedCacheService`, `BrotliCacheSerializer`, `CacheWarmupHostedService`) are internal |
| `SharedKernel.Caching.Redis.Core` | Adapter | The one shared `IConnectionMultiplexer`: `AddRedisConnection`, `EnsureRedisConnectionRegistered`, `RedisConnectionOptions`, the `redis` readiness probe. References no `SharedKernel.Caching` package |
| `SharedKernel.Caching.Redis` | Adapter | FusionCache L2 + backplane over the shared connection: `AddRedisL2`, `RedisL2Options`; internal `RedisDistributedCache`, `SharedConnectionMultiplexer`. Edge → `Redis.Core` |
| `SharedKernel.Caching.Redis.DistributedLocking` | Adapter | `AddRedisDistributedLocking()`; internal Lua-script `IDistributedLockService`. Edge → `Redis.Core` |
| `SharedKernel.Caching.Redis.HashStore` | Adapter | `IRedisHashService`, `ITypedHashStore<T>` (namespace `SharedKernel.Caching.Redis.HashStore`). Edge → `Redis.Core` |
| `SharedKernel.Caching.Redis.PubSub` | Adapter | `IRedisChannelService` (namespace `SharedKernel.Caching.Redis.PubSub`) — at-most-once, not durable. Edge → `Redis.Core` |

The four Redis role packages declare `<SharedKernelAllowedAdapterReferences>SharedKernel.Caching.Redis.Core</SharedKernelAllowedAdapterReferences>`.
All packages track public API (`PublicAPI.*.txt`). `consumer-verify/SharedKernel.Caching.ConsumerVerify` takes
`PackageReference`s to all seven packed packages and runs five hosts (L1-only, L1+L2, locking-only, hash-only,
pub/sub-only) against a Testcontainers Redis.

## Public Entry Points

**FusionCache** (namespace `SharedKernel.Caching.FusionCache.Extensions`)

- `services.AddSharedKernelCaching(configuration, configure?)` → `ICachingBuilder` — section `SharedKernel:Caching`
  (`CachingOptions`: `ServiceName` **required, no default**, `L1SizeLimit` entry count 10 000, `WaitForWarmup`,
  `DistributedCacheSoftTimeout`/`HardTimeout`, `FailSafeThrottleDuration`, code-only `SerializerContext`).
  `AddSharedKernelCaching(configure)` is the code-only overload. Registers `ICacheService`, one `CacheKeyProvider`
  behind both key-provider interfaces (each `TryAdd`), and the `cache` probe (`CacheReadinessProbeNames.Cache`).
- `.AddTenantCacheService()` — `ITenantCacheService`, the recommended entry point for tenant data.
- `.AddBrotliCompression(o => …)` — `CacheCompressionOptions` (`ThresholdBytes` 1024, `Level` Fastest); L2 only.
- `.AddCacheEncryption()` — decorates `ICacheService` with AES-256-GCM (needs `AddSharedKernelCryptography(configuration).AddSymmetricEncryption()`).
- `.AddCacheWarmup<TStrategy>()` — `ICacheWarmupStrategy` run by an `IHostedLifecycleService`.

**Redis.Core** (namespace `SharedKernel.Caching.Redis.Core.Extensions`)

- `services.AddRedisConnection(configuration, configure?)` — section `SharedKernel:Caching:Redis` (`RedisConnectionOptions`:
  `ConnectionString`, `ConnectTimeout`, `CommandTimeout`, `FailFastWhenDisconnected`, `Ssl`, code-only
  `ClientCertificates`/`CertificateValidation`); or `AddRedisConnection(o => …)`. Registers the `redis` probe
  (`RedisReadinessProbeNames.Connection`). Call **exactly once**.
- `EnsureRedisConnectionRegistered(caller)` — for role packages.

**Redis L2** — `.AddRedisL2(configure?)` / `.AddRedisL2(configuration, configure?)` on `ICachingBuilder`; section
`SharedKernel:Caching:Redis:L2` (`RedisL2Options`: `KeyPrefix`, `DistributedCacheCircuitBreakerDuration`,
`BackplaneCircuitBreakerDuration`, default 2 s each, `TimeSpan.Zero` = off).

**Locking** — `.AddRedisDistributedLocking()` on `ICachingBuilder` or `IServiceCollection`; inject `IDistributedLockService`:
`TryAcquireAsync(resource, DistributedLockOptions?)` (`Expiry` 30 s, `WaitTime` 0, `RetryInterval` 200 ms) and
`TryAcquireLeaseAsync(resource, duration)`.

**Hash store** — `services.AddRedisHashService()`, `services.AddTypedHashStore<T>(JsonTypeInfo<T>)`.

**Pub/Sub** — `services.AddRedisChannelService()`.

```csharp
services.AddRedisConnection(configuration);                      // once, before any other Redis registration
services.AddSharedKernelCaching(configuration)                   // SharedKernel:Caching (ServiceName required)
        .AddRedisL2(configuration)
        .AddTenantCacheService()
        .AddBrotliCompression()                                  // before encryption
        .AddCacheEncryption()                                    // last
        .AddRedisDistributedLocking();
services.AddHealthChecks().AddSharedKernelReadiness();           // host: maps "redis" and "cache"
```

## Rules & Invariants

### Cache contract

1. `ICacheService` is always FusionCache in production — never raw `IMemoryCache`/`IDistributedCache`.
2. Computed values go through `GetOrSetAsync` (factory runs once per key across concurrent callers). `TryGetAsync` then `SetAsync` is a bug.
3. A miss is `CacheLookup<T>.Miss`; a cached `null` or `0` is a **hit**. Never collapse them with `default`.
4. `CacheFactoryContext.SkipCaching()` writes nothing and returns the value **only to the caller whose factory ran** — a skipped value is never handed to concurrent waiters (each runs the factory itself). `Application.Pipeline.Caching` relies on this to keep a failed `Result` out of the cache; an `ICacheService` implementation must never broadcast a skipped value. `SetDurations` overrides one write.
5. `ExpireAsync` keeps the old value for fail-safe; `RemoveAsync` drops it (never served by fail-safe). `ClearAsync` is break-glass: every tenant, every instance, logged at Warning.
6. A factory using request-scoped services (scoped `DbContext`) must use `WithoutEagerRefresh()` and no factory timeouts — background refresh can outlive the request.
7. `CachePolicy` is an immutable validated record; every `With…` returns a new instance. `LocalOnly()` never touches Redis or the backplane.
8. Batch calls: one `CacheLookup` per **distinct** key; fan-out bounded at 16 (internal constant); batches never invoke factories. A failed `SetManyAsync` write may leave up to 16 in-flight writes completed.

### Keys and tenants

9. Keys `{service}:{entity}:{id}[:{seg}…]`; tenant keys `{service}:@{tenant}:{entity}:{id}`; tenant tags `@{tenant}:{tag}`; tenant-wide tag `@{tenant}`. Build them only through `ICacheKeyProvider`/`ITenantCacheKeyProvider`/`CacheKeyFormat`, never interpolation. Every caller part is escaped (`%`, `:`, `@`); the tenant is the `TenantId` lowercase GUID.
10. `CachingOptions.ServiceName` has no default and is validated (`CacheKeyFormat.IsValidServiceName`) on start — two services on one Redis cannot collide on a forgotten default. There is exactly one source of the service name.
11. Global tags may not start with `@`, so no global tag can match a tenant tag.
12. `ITenantCacheService`: explicit non-default `TenantId` on every call (`default` throws), `(entity, id)` never a pre-built key, never ambient (`IRequestContext`/`IHttpContextAccessor`). Every write uses `policy.ForTenant(tenantId)`; an already tenant-scoped policy throws. Cross-tenant invalidation must be impossible.
13. Version a changed cached shape by changing the key (`"invoice-v2"`); there is no policy-level versioning.

### Serialization, compression, encryption

14. STJ only. NativeAOT builds set `CachingOptions.SerializerContext`; the same options serve L2 and encryption plaintext. `AddRedisL2` uses `.WithRegisteredSerializer()` — never `.WithSystemTextJsonSerializer()` (it would overwrite the context).
15. `L1SizeLimit` and `SerializerContext` are read from `IOptions<CachingOptions>` when the cache is first built (the dedicated L1 `MemoryCache` and the FusionCache serializer), so both configuration binding and `configure` apply; changing them after the first resolution has no effect.
16. Brotli is opt-in and L2-only; payloads carry marker bytes `0x42 0x52`; unmarked payloads pass through. protobuf-net is prohibited.
17. Encryption decorates `ICacheService` (not the serializer) with **AAD = the cache key** — the serializer never sees the key. Decrypt failure → Warning (`Caching + 15`), best-effort evict, miss; `GetOrSetAsync` recomputes once through the wrapped service and never throws or returns wrong data.
18. `AddBrotliCompression()` must precede `AddCacheEncryption()` (the reverse throws); encryption then compresses before encrypting.

### Redis

19. `AddRedisConnection` is the **only** `IConnectionMultiplexer` registration and the only caller of `ConfigurationOptions.Parse`/`ConnectionMultiplexer.Connect`; a second call throws. Role packages call `EnsureRedisConnectionRegistered` first and never take a connection string.
20. `AbortOnConnectFail = false` always (an unreachable Redis logs `+103`, never fails startup); `FailFastWhenDisconnected` → `BacklogPolicy.FailFast`. Validator messages never echo the connection string.
21. TLS/mTLS goes through `ConfigurationOptions.SslClientAuthenticationOptions` (StackExchange.Redis has no `CertificateSelection`). Non-loopback without TLS is a Warning (`+102`), never an exception (mesh TLS is legitimate).
22. Never hand the shared multiplexer to something that may dispose it: `RedisDistributedCache` is passed via `WithDistributedCache(factory)` and **not** registered as `IDistributedCache`; the backplane gets `SharedConnectionMultiplexer` (no-op `Close`/`Dispose`). Do not reintroduce `Microsoft.Extensions.Caching.StackExchangeRedis`.
23. L2 entries are Redis strings at `{KeyPrefix}v2:{key}`, absolute expiry only (`SlidingExpiration` throws). A non-empty `KeyPrefix` is also the backplane channel prefix.
24. Cross-instance removal, expiry, tag eviction and clear need no extra wiring — the FusionCache backplane carries them. Do not build a cache-invalidation bus on Pub/Sub.
25. The only circuit breakers are FusionCache's L2/backplane breakers; no Polly anywhere in this domain. While the backplane breaker is open, evictions do not reach other instances.
26. Role packages never reference each other; `Redis` and `FusionCache` never reference each other.

### Locks

27. Three outcomes, never conflated: handle → acquired; `null` → contended; `DistributedLockUnavailableException` → store unreachable (thrown at once even with a `WaitTime`).
28. Acquisition and fencing are one Lua script: `SET sharedkernel:lock:{resource} NX PX` then `INCR sharedkernel:lock-fencing:{resource}` only on success. Locks and leases share key and counter; the fencing key never expires; `{resource}` hash tag keeps it Cluster-safe.
29. A lock is kept alive every `Expiry / 3` and reported lost (`IsHeld = false`, `LostToken` cancelled, Error `+307`) at `Expiry * 5 / 6` measured from the **send** time of the last successful acquire/extend — always before another owner can acquire. `LostToken` means lost *or* released.
30. The protected resource must reject a non-increasing fencing token; this domain issues tokens, never enforces them. Resource names are global — prefix with the service name.
31. No RedLock.net (`RedisTopologyRules.DistributedLockingNeverReferencesRedLock`).

### Hash store and Pub/Sub

32. Hash writes with `timeToLive` run the command and `PEXPIRE` in one Lua script (never separate calls, never `MULTI`); without it the existing expiry is kept. Keys are used as given — callers include service and tenant. No compression or encryption of fields.
33. Pub/Sub is at-most-once, cache-adjacent signalling only (SK0007 flags it in messaging code). `RedisChannel.Literal` only; one `ChannelMessageQueue` per subscription; handler awaited one message at a time; bad JSON logged `+501` and skipped. **Never resubscribe on reconnect** — StackExchange.Redis restores subscriptions; replaying doubled deliveries.

### General

34. Readiness is only the `redis` and `cache` probes (`IReadinessProbe`); the `cache` probe reports Degraded, never Unhealthy. Probe descriptions never contain secrets or exception messages.
35. No static mutable state (static `Meter`/`ActivitySource` excepted). Metric tag `cache.key_prefix` is `{service}:{entity}` — never an id or tenant. Span status carries the exception type name, never its message.

## Decisions

| Decision | Why |
| --- | --- |
| FusionCache over hand-rolled L1/L2 | Stampede protection, fail-safe, eager refresh, tags and backplane in one AOT-compatible library |
| Own `RedisDistributedCache` instead of `Microsoft.Extensions.Caching.StackExchangeRedis` | `RedisCache` disposed the shared multiplexer |
| Encryption as an `ICacheService` decorator, AAD = key | Binds ciphertext to its key; tags are unavailable at read time; `AsyncLocal` does not reach eager-refresh factories |
| Pub/Sub stays in `02.Caching`, not `07.Messaging` | Its contract is deliberately weaker; placing it in messaging would imply durability |
| Hash store and Pub/Sub have `IServiceCollection` registrations only | They are not part of the cache |
| Locks via Lua, not RedLock.net | RedLock cannot issue a fencing token in the acquisition step |
| `AddRedisConnection` throws on a second call | Conflicting connection settings must fail loudly, not "first caller wins" |
| Warmup with `WaitForWarmup` runs in `StartingAsync` | Completes before any `StartAsync`, so no traffic or readiness before warmup |
| No lock key prefix option, no lock metrics | Not needed; resource names already carry the service |

## Logging

`LoggingEventIdRanges.Caching` = **2000–2999**; every EventId is written `LoggingEventIdRanges.Caching + n` in a nested
`static partial class Log`.

| Sub-block | Package | Events |
| --- | --- | --- |
| +0 – +99 | `Caching.FusionCache` | +0..+7 warmup (`CacheWarmupHostedService`); +10 miss, +11 set, +12 factory invoked, +13 removed, +14 tag removed, +16 expired (Debug); +15 decrypt failed (Warning); +17 cleared (Warning) |
| +100 – +199 | `Caching.Redis.Core` | +100 connection restored, +101 connection failed, +102 non-loopback without TLS, +103 not reachable at startup |
| +200 – +299 | `Caching.Redis` | reserved, no logging |
| +300 – +399 | `Caching.Redis.DistributedLocking` | +300/+301 lock acquired/contended, +302/+303 lease acquired/contended, +304 released (Debug); +305 release failed, +306 extend failed (Warning); +307 lock lost (Error) |
| +400 – +499 | `Caching.Redis.HashStore` | reserved, no logging |
| +500 – +599 | `Caching.Redis.PubSub` | +500 handler failed (Error), +501 message not deserialized, +502 unsubscribe failed (Warning) |

`Caching.Abstractions` does not log. Logs carry `{KeyPrefix}`, never the full key; lock logs carry resource and token.

## Cross-Domain Couplings

- **01.Core** — `Execution` (`TenantId`), `Primitives` (`LoggingEventIdRanges`, `IReadinessProbe`), `Configuration` (`AddValidatedOptions`, `ISectionBoundOptions`), `Cryptography` (`ISymmetricEncryptionService`, `EncryptedPayload`) for encryption.
- **05.Application** — `SharedKernel.Application` references `Caching.Abstractions` for `ICacheableQuery`/`IInvalidatesCache`; `Application.Pipeline.Caching`'s `CachingBehavior` uses `GetOrSetAsync` + `SkipCaching()`.
- **13.ServiceDefaults** — `AddSharedKernelReadiness()` maps the `redis`/`cache` probes; `WithCachingTelemetry()` subscribes to the `SharedKernel.Caching` meter and activity source by name; `SharedKernel.MultiTenancy` uses `Caching.Abstractions` for `CachedTenantCatalog`.
- **18.Idempotency** — `Idempotency.Redis` builds on `Caching.Redis.Core` (declared edge).
- **19.Scheduling** — per-occurrence single execution through `IDistributedLockService.TryAcquireLeaseAsync`.
- **07.Messaging** — none, in either direction (`RedisTopologyRules.PubSubNeverReferencesMessaging`/`MessagingNeverReferencesCaching`).
- **00.Governance** — `RedisTopologyRules`, SK0007.

## Testing

- Unit lane: `Caching.Abstractions.Tests`, `Caching.FusionCache.Tests`. Integration lane (Testcontainers Redis): `Caching.Redis.Tests`, `.Redis.Core.Tests`, `.Redis.DistributedLocking.Tests`, `.Redis.HashStore.Tests`, `.Redis.PubSub.Tests`.
- Consumer fakes: `src/Infrastructure/Caching/SharedKernel.Caching.Testing` (`AddFakeCachingServices()`: `FakeCacheService`, `FakeTenantCacheService`, `FakeTenantCacheKeyProvider`, `FakeDistributedLockService`, `FakeCacheWarmupStrategy`) and `SharedKernel.Caching.Redis.Testing` (`AddFakeRedisServices()`: `FakeRedisHashService`, `FakeTypedHashStore`, `FakeRedisChannelService`).
- Redis tests always use `Testcontainers.Redis` at the central version shared with `SharedKernel.Testing.Internal` (a mismatched pin throws `MissingMethodException`). Every composition calls `AddRedisConnection` first; a test needing an `ICachingBuilder` without FusionCache uses a local `TestCachingBuilder`, never a sibling package.
- Must-cover behaviours: hit vs miss (cached `null`/`0`); stampede (factory once, also interleaved with batches); `SkipCaching`; expire vs remove under fail-safe; `ServiceName` validation; tenant isolation (keys, tag removal, `RemoveTenantAsync`); lock outcomes, fencing monotonicity and loss before expiry; encryption replay/tamper/cross-tenant; registration guards.
- `CrossInstanceTagInvalidationTests` uses two fully independent `ServiceProvider`s on one Redis — never two scopes of one container — with bounded polling, never a fixed delay.
- `IDistributedLockServiceContractTests` is an abstract contract base run against real Redis.
- `MeterListener`/`ActivityListener` accumulators must be thread-safe (`ConcurrentDictionary`/`ConcurrentBag`); OTel assertions are existence-style because instruments are static and shared.
- Lock expiry is a real Redis TTL: short durations plus margin; wait on `LostToken` with a timeout.

## Known Limitations

- Concurrent readers of one corrupt encrypted entry may each trigger a recompute (an extra factory run, never wrong data).
- While the backplane circuit breaker is open, other instances keep stale L1 entries until they expire.
- After a Redis primary failover a lock may be granted twice; fencing tokens are the only protection.
- `DistributedLease.ExpiresAt` is the acquiring process's view — approximate across machines.
- Pub/Sub on Redis Cluster reaches only the publishing node's subscribers; messages sent while a subscriber is disconnected are lost.
- Configuration-binding overloads are reflection-based; the delegate overloads avoid binding. The hash store and typed pub/sub take `JsonTypeInfo<T>`, but the packages make no AOT claim.
