# 02.Caching — State Map

> **What this file is:** Phase and task tracker for all work within `02.Caching`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.02.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
|-----------|-------------------|-------------------|
| `SK.02.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.02.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.02.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.02.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.02.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.02.Published` | Published | All tasks in Phase: Published are `●` |
| `SK.02.Abstractions` | Phase 5 (Abstractions Package) | All tasks in Phase: Abstractions are `●` |
| `SK.02.CachingRefactor` | Phase 6 (Caching Refactor) | All tasks in Phase: CachingRefactor are `●` |
| `SK.02.RedisRefactor` | Phase 7 (Redis Refactor) | All tasks in Phase: RedisRefactor are `●` |
| `SK.02.InvalidationBus` | Phase 12 (Invalidation Bus) | All tasks in Phase: InvalidationBus are `●` |
| `SK.02.FusionCacheRename` | Phase 14 (FusionCache Rename + NeverExpire) | All tasks in Phase: FusionCacheRename are `●` |
| `SK.02.AotHardening` | Phase 15 (AOT Hardening + ITypedHashStore) | All tasks in Phase: AotHardening are `●` |
| `SK.02.BrotliCompression` | Phase 16 (Brotli L2 Compression) | All tasks in Phase: BrotliCompression are `●` |
| `SK.02.LayeringFix` | Phase 17 (Redis→FusionCache Layering Violation Fix) | All tasks in Phase: LayeringFix are `●` |
| `SK.02.AotSerializerFix` | Phase 18 (AddRedisL2 Silent Serializer Override Fix) | All tasks in Phase: AotSerializerFix are `●` |
| `SK.02.DiErgonomics` | Phase 19 (DI Ergonomics Hardening) | All tasks in Phase: DiErgonomics are `●` |
| `SK.02.L1SizeLimit` | Phase 20 (Wire L1SizeLimit + Verify L2 KeyPrefix) | All tasks in Phase: L1SizeLimit are `●` |
| `SK.02.ValueTaskFactory` | Phase 21 (GetOrSetAsync ValueTask Factory Delegate) | All tasks in Phase: ValueTaskFactory are `●` |
| `SK.02.BatchOperations` | Phase 22 (Batch Get and Set Operations) | All tasks in Phase: BatchOperations are `●` |
| `SK.02.RenewableLock` | Phase 23 (IRenewableLock Heartbeat and Renewal) | All tasks in Phase: RenewableLock are `●` |
| `SK.02.SlidingExpiration` | Phase 24 (Sliding Expiration in CachePolicy) | All tasks in Phase: SlidingExpiration are `●` |
| `SK.02.KeyVersioning` | Phase 25 (Cache Key Versioning Strategy) | All tasks in Phase: KeyVersioning are `●` |
| `SK.02.ChannelReconnect` | Phase 26 (RedisChannelService Reconnect Resilience) | All tasks in Phase: ChannelReconnect are `●` |
| `SK.02.CachingCoreOptionsDi` | Phase 27 (CachingCoreOptions Standalone DI Registration) | All tasks in Phase: CachingCoreOptionsDi are `●` |
| `SK.02.CacheWarmup` | Phase 28 (ICacheWarmupStrategy and Startup Runner) | All tasks in Phase: CacheWarmup are `●` |
| `SK.02.TenantCacheKey` | Phase 29 (Multi-Tenant Cache Key Isolation) | All tasks in Phase: TenantCacheKey are `●` |
| `SK.02.RedisCircuitBreaker` | Phase 30 (Polly v8 Circuit Breaker for Redis L2) | All tasks in Phase: RedisCircuitBreaker are `●` |
| `SK.02.OtelMeters` | Phase 31 (ICacheService OTel Meters) | All tasks in Phase: OtelMeters are `●` |
| `SK.02.RedisConnectionCore` | Phase 32 (Redis Connection Core Extraction) | All tasks in Phase: RedisConnectionCore are `●` |
| `SK.02.RedisL2Refactor` | Phase 33 (Redis L2 Backplane Package Refactor) | All tasks in Phase: RedisL2Refactor are `●` |
| `SK.02.RedisLockingExtraction` | Phase 34 (Redis Distributed Locking Package Extraction) | All tasks in Phase: RedisLockingExtraction are `●` |
| `SK.02.RedisHashExtraction` | Phase 35 (Redis Hash Store Package Extraction) | All tasks in Phase: RedisHashExtraction are `●` |
| `SK.02.RedisPubSubExtraction` | Phase 36 (Redis Pub/Sub and Invalidation Package Extraction) | All tasks in Phase: RedisPubSubExtraction are `●` |

---

## Active Work

_Nothing in progress — all 36 phases complete (WO-006 + WO-007 + WO-023). WO-023 (Redis package split, Phases 32–36) is fully complete. No further phases planned._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Define ICacheService contract | SK.02.Design | SharedKernel.Caching | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| C-04 Redis L2 backplane | SK.02.Core | Waiting for StackExchange.Redis AOT verdict |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
|---------|--------------|:-----:|-------|
| `SharedKernel.Caching.Abstractions` | Phase 31 (complete) | `●` | All 31 phases of WO-006/WO-007 complete; no contract changes planned in WO-023 |
| `SharedKernel.Caching.FusionCache` | Phase 31 (complete) | `●` | All 31 phases of WO-006/WO-007 complete; no changes planned in WO-023 |
| `SharedKernel.Caching.Redis` | Phase 33 (complete) | `●` | `AddRedisL2` now sources `IConnectionMultiplexer` and the optional circuit breaker `ResiliencePipeline` from `.Redis.Core` (Ph.32) via `AddRedisConnection`/`AddRedisCircuitBreaker`; `RedisL2Options.CircuitBreaker` retyped to `RedisCircuitBreakerOptions` (Core); 154 tests passing. RedLock/hash/pub-sub types still physically reside here pending Phases 34-36 |
| `SharedKernel.Caching.Redis.Core` | Phase 32 (complete) | `●` | New package created — `AddRedisConnection`/`AddRedisCircuitBreaker`, `RedisConnectionHealthTracker`, `RedisConnectionOptions`, `RedisCircuitBreakerOptions`; 33 tests passing; dependency root for Phases 33-36 |
| `SharedKernel.Caching.Redis.DistributedLocking` | Phase 34 (complete) | `●` | New package — extracted from `SharedKernel.Caching.Redis`: `RedLockDistributedLockService`, `RedLockRenewableLock` + `KeepAliveAsync`, `RedisLockOptions`, `AddRedisDistributedLocking`; sources `IConnectionMultiplexer` via `AddRedisConnection` from `.Redis.Core`; 41 tests passing |
| `SharedKernel.Caching.Redis.HashStore` | Phase 35 (pending) | `○` | New package — extracted from `SharedKernel.Caching.Redis`: `IRedisHashService`, `ITypedHashStore<T>` |
| `SharedKernel.Caching.Redis.PubSub` | Phase 36 (pending) | `○` | New package — extracted from `SharedKernel.Caching.Redis`: `IRedisChannelService`, `ICacheInvalidationBus`, receiver |

---

## Phase: Design <!-- phase-key: SK.02.Design -->

> Finalize all interface contracts, policy shapes, and DI extension signatures before any implementation begins.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | Define `ICacheService` method signatures | SharedKernel.Caching | `●` |
| D-02 | Define `CachePolicy` record shape and factory methods | SharedKernel.Caching | `●` |
| D-03 | Define `IDistributedLockService` method signatures | SharedKernel.Caching.Redis | `●` |
| D-04 | Define DI extension signatures (`AddSharedKernelCaching`, `AddRedisL2`, `AddRedisDistributedLocking`) | Both | `●` |
| D-05 | Confirm FusionCache NuGet version and AOT compatibility | SharedKernel.Caching | `●` |
| D-06 | Confirm RedLock.net NuGet version and AOT status | SharedKernel.Caching.Redis | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.02.Scaffold -->

> Wire up .csproj NuGet references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Add FusionCache + STJ NuGet refs to `SharedKernel.Caching.csproj` | SharedKernel.Caching | `●` |
| S-02 | Add StackExchange.Redis + FusionCache.Backplane.Redis + RedLock.net refs to `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | `●` |
| S-03 | Add `SharedKernel.Caching` project reference to `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | `●` |
| S-04 | Create folder structure (`Abstractions/`, `Policies/`, `Extensions/`) inside each project | Both | `●` |
| S-05 | Register both projects in `Platform.SharedKernel.slnx` under solution folder `02.Caching` | Both | `●` |
| S-06 | Stub empty `.Tests` projects with xUnit package reference | Both | `●` |

---

## Phase: Core <!-- phase-key: SK.02.Core -->

> Full implementation of all interfaces, FusionCache wiring, Redis L2 backplane, RedLock, and DI registration.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `ICacheService` backed by FusionCache L1 | SharedKernel.Caching | `●` |
| C-02 | Implement `CachePolicy` sealed record with factory methods and tag support | SharedKernel.Caching | `●` |
| C-03 | Implement `AddSharedKernelCaching` DI extension | SharedKernel.Caching | `●` |
| C-04 | Implement Redis L2 backplane wiring (FusionCache + StackExchange.Redis) | SharedKernel.Caching.Redis | `●` |
| C-05 | Implement `AddRedisL2` DI extension (optional, additive) | SharedKernel.Caching.Redis | `●` |
| C-06 | Implement `IDistributedLockService` via RedLock.net | SharedKernel.Caching.Redis | `●` |
| C-07 | Implement `AddRedisDistributedLocking` DI extension | SharedKernel.Caching.Redis | `●` |
| C-08 | Add STJ source-gen context base class and serialization wiring | SharedKernel.Caching | `●` |
| C-09 | Verify L1-only silent fallback when Redis is not registered | Both | `●` |

---

## Phase: Tests <!-- phase-key: SK.02.Tests -->

> Unit and integration test coverage. Redis tests must use Testcontainers.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Unit: `ICacheService` get, set, remove, tag-eviction | SharedKernel.Caching.Tests | `●` |
| T-02 | Unit: `CachePolicy` factory methods and immutability | SharedKernel.Caching.Tests | `●` |
| T-03 | Stampede: parallel `GetOrSetAsync` — factory called exactly once | SharedKernel.Caching.Tests | `●` |
| T-04 | Integration: Redis L2 round-trip via Testcontainers | SharedKernel.Caching.Redis.Tests | `●` |
| T-05 | Integration: RedLock acquire success, timeout (returns null), release, expiry | SharedKernel.Caching.Redis.Tests | `●` |
| T-06 | Integration: L1-only fallback when Redis container is stopped mid-run | SharedKernel.Caching.Redis.Tests | `●` |

---

## Phase: Docs <!-- phase-key: SK.02.Docs -->

> XML doc comments on all public APIs, README with usage examples, configuration reference.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | XML doc all public interfaces and types | Both | `●` |
| DO-02 | Write `02.Caching/README.md` with usage examples | Both | `●` |
| DO-03 | Document `CachePolicy` presets and tag-based eviction pattern | SharedKernel.Caching | `●` |
| DO-04 | Document DI registration options with annotated code samples | Both | `●` |

---

## Phase: Published <!-- phase-key: SK.02.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Add NuGet metadata to both `.csproj` files (authors, description, version, license) | Both | `●` |
| P-02 | Pack and publish `SharedKernel.Caching` to feed | SharedKernel.Caching | `●` |
| P-03 | Pack and publish `SharedKernel.Caching.Redis` to feed | SharedKernel.Caching.Redis | `●` |
| P-04 | Verify dependency graph in a consumer test project | Both | `●` |

---

## Phase: Abstractions <!-- phase-key: SK.02.Abstractions -->

> Introduce a zero-infra `SharedKernel.Caching.Abstractions` package owning all caching contracts with zero infrastructure NuGet dependencies.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| A-01 | Create `SharedKernel.Caching.Abstractions.csproj` (`net10.0`; only `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.1`) | SharedKernel.Caching.Abstractions | `●` |
| A-02 | Implement `ICacheService.cs` — five-method cache service contract | SharedKernel.Caching.Abstractions | `●` |
| A-03 | Implement `CachePolicy.cs` — sealed immutable record with `Default`, `For`, `WithTags`, `WithEagerRefresh` factory methods | SharedKernel.Caching.Abstractions | `●` |
| A-04 | Implement `ICacheKeyProvider.cs` — key construction contract with `{service}:{entity}:{id}` format documented | SharedKernel.Caching.Abstractions | `●` |
| A-05 | Implement `IDistributedLockService.cs` — distributed lock contract migrated from `SharedKernel.Caching.Redis` | SharedKernel.Caching.Abstractions | `●` |
| A-06 | Implement `IRedisChannelService.cs` — ephemeral Pub/Sub fanout contract; scope constraint in XML doc | SharedKernel.Caching.Abstractions | `●` |
| A-07 | Implement `IRedisHashService.cs` — hash field-value contract with `JsonTypeInfo<T>` typed methods | SharedKernel.Caching.Abstractions | `●` |
| A-08 | Implement `ICacheInvalidationBus.cs` — cross-service invalidation contract; no-guarantee XML doc | SharedKernel.Caching.Abstractions | `●` |
| A-09 | Implement `CacheInvalidationMessage.cs` — sealed record + `CacheInvalidationType` enum + STJ source-gen context | SharedKernel.Caching.Abstractions | `●` |
| A-10 | Implement `ICachingBuilder.cs` — DI extension chaining interface | SharedKernel.Caching.Abstractions | `●` |
| A-11 | Register project in `Platform.SharedKernel.slnx` under `02.Caching` solution folder | Solution | `●` |

---

### Goal

Introduce a new `SharedKernel.Caching.Abstractions` package that owns every caching contract with zero infrastructure NuGet dependencies. This allows `05.Application` (and any other layer that must stay infrastructure-free) to depend solely on caching abstractions without transitively pulling in FusionCache or StackExchange.Redis. It also establishes a canonical home for contracts that currently lack one (`ICacheKeyProvider`, `IRedisChannelService`, `IRedisHashService`, `ICacheInvalidationBus`) and separates the builder interface so `SharedKernel.Caching.Redis` can extend it without depending on the FusionCache package.

### Scope

- **Package(s) affected:** New `SharedKernel.Caching.Abstractions` (create); `SharedKernel.Caching` (source removal, phase P-006); `SharedKernel.Caching.Redis` (source removal, phase P-007)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Abstractions/SharedKernel.Caching.Abstractions.csproj`
  - `02.Caching/SharedKernel.Caching.Abstractions/ICacheService.cs`
  - `02.Caching/SharedKernel.Caching.Abstractions/CachePolicy.cs`
  - `02.Caching/SharedKernel.Caching.Abstractions/ICacheKeyProvider.cs`
  - `02.Caching/SharedKernel.Caching.Abstractions/IDistributedLockService.cs`
  - `02.Caching/SharedKernel.Caching.Abstractions/IRedisChannelService.cs`
  - `02.Caching/SharedKernel.Caching.Abstractions/IRedisHashService.cs`
  - `02.Caching/SharedKernel.Caching.Abstractions/ICacheInvalidationBus.cs`
  - `02.Caching/SharedKernel.Caching.Abstractions/CacheInvalidationMessage.cs`
  - `02.Caching/SharedKernel.Caching.Abstractions/ICachingBuilder.cs`
- **Modified files:** `Platform.SharedKernel.slnx` (register new project under `02.Caching` solution folder)
- **Deleted files:** None in this phase — removal from `SharedKernel.Caching` and `SharedKernel.Caching.Redis` is deferred to P-006 and P-007 respectively

### Implementation Rules

1. The `.csproj` must reference only `Microsoft.Extensions.DependencyInjection.Abstractions` (10.0.1) for `IServiceCollection` used by `ICachingBuilder`; no FusionCache, no StackExchange.Redis, no RedLock.net, no MassTransit.
2. `ICacheService` and `CachePolicy` are migrated verbatim — zero behavioral changes, only the declaring assembly changes.
3. `ICacheKeyProvider` exposes a single method: `string BuildKey(string entity, string id, params string[] extraSegments)`. The format contract is `{service}:{entity}:{id}[:{extraSegment}...]`. XML doc must name the format explicitly.
4. `IDistributedLockService` is migrated verbatim from `SharedKernel.Caching.Redis` — zero behavioral changes.
5. `IRedisChannelService` XML doc must contain the phrase "ephemeral, non-durable" and explicitly state that durable/ordered messaging belongs in `07.Messaging`. Signatures: `PublishAsync(string channel, string message, CancellationToken ct)`, `SubscribeAsync(string channel, Func<string, ValueTask> handler, CancellationToken ct)`, `UnsubscribeAsync(string channel, CancellationToken ct)`.
6. `IRedisHashService` typed methods must accept `JsonTypeInfo<T>` as a parameter — no reflection-based serialization. Signatures: `GetFieldAsync<T>(string key, string field, JsonTypeInfo<T> typeInfo, CancellationToken ct)`, `SetFieldAsync<T>(string key, string field, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct)`, `GetAllFieldsAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken ct)` returns `IReadOnlyDictionary<string, T>`, `DeleteFieldAsync(string key, string field, CancellationToken ct)`, `IncrementFieldAsync(string key, string field, long delta, CancellationToken ct)`.
7. `ICacheInvalidationBus` carries a mandatory XML doc stating no delivery guarantees and explicitly defers durable delivery to `07.Messaging`. Methods: `PublishKeyInvalidationAsync(string[] keys, CancellationToken ct)`, `PublishTagInvalidationAsync(string[] tags, CancellationToken ct)`, `PublishBroadcastInvalidationAsync(CancellationToken ct)`, `PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct)`.
8. `CacheInvalidationMessage` is a sealed record. It must carry a `[JsonSerializable]`-compatible STJ source-generated context declared inside the same file (`CacheInvalidationMessageJsonContext`). Fields: `SourceService` (string), `InvalidationType` (`CacheInvalidationType` enum: `Key`, `Tag`, `All`), `Keys` (string[]?), `Tags` (string[]?), `CorrelationId` (string), `TimestampUtc` (DateTimeOffset). All fields are init-only. `CorrelationId` defaults to `Activity.Current?.Id ?? Guid.NewGuid().ToString("N")` — the using for `System.Diagnostics` is required.
9. `ICachingBuilder` exposes `IServiceCollection Services { get; }` — identical to what was previously in `SharedKernel.Caching.Extensions`. No additional members.
10. All public types must have XML doc comments. No internal implementation code in this package — abstractions only.
11. Every interface must be `public` and reside in the `SharedKernel.Caching.Abstractions` namespace. `CachePolicy`, `CacheInvalidationMessage`, `CacheInvalidationType`, and `CachingBuilder` (if any) follow the same namespace rule.

### File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `SharedKernel.Caching.Abstractions.csproj` | SharedKernel.Caching.Abstractions | Create | Project definition; targets `net10.0`; refs `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.1` only |
| `ICacheService.cs` | SharedKernel.Caching.Abstractions | Create | Five-method cache service contract |
| `CachePolicy.cs` | SharedKernel.Caching.Abstractions | Create | Sealed immutable record with `Default`, `For`, `WithTags`, `WithEagerRefresh` factory methods |
| `ICacheKeyProvider.cs` | SharedKernel.Caching.Abstractions | Create | Key construction contract with `{service}:{entity}:{id}` format documented |
| `IDistributedLockService.cs` | SharedKernel.Caching.Abstractions | Create | Distributed lock contract migrated from `SharedKernel.Caching.Redis` |
| `IRedisChannelService.cs` | SharedKernel.Caching.Abstractions | Create | Redis Pub/Sub ephemeral fanout contract; scope constraint in XML doc |
| `IRedisHashService.cs` | SharedKernel.Caching.Abstractions | Create | Redis Hash structured field-value contract; typed via `JsonTypeInfo<T>` |
| `ICacheInvalidationBus.cs` | SharedKernel.Caching.Abstractions | Create | Cross-service invalidation broadcast contract; no-guarantee XML doc |
| `CacheInvalidationMessage.cs` | SharedKernel.Caching.Abstractions | Create | Wire payload sealed record + `CacheInvalidationType` enum + STJ source-gen context |
| `ICachingBuilder.cs` | SharedKernel.Caching.Abstractions | Create | Builder interface for DI extension chaining |
| `Platform.SharedKernel.slnx` | Solution | Modify | Register `SharedKernel.Caching.Abstractions` under `02.Caching` solution folder |

### Acceptance Criteria

- [ ] New project `SharedKernel.Caching.Abstractions` exists in `02.Caching/SharedKernel.Caching.Abstractions/`
- [ ] Project targets `net10.0` with zero NuGet infrastructure references
- [ ] `ICacheService` contract present with all five methods
- [ ] `CachePolicy` sealed record present with all factory methods
- [ ] `ICacheKeyProvider` interface present with format contract documented
- [ ] `IDistributedLockService` interface present
- [ ] `IRedisChannelService` interface present; XML doc states ephemeral/non-durable scope
- [ ] `IRedisHashService` interface present with `JsonTypeInfo<T>` typed methods
- [ ] `ICacheInvalidationBus` interface present with four methods; XML doc states no delivery guarantees
- [ ] `CacheInvalidationMessage` sealed record present with six fields; includes `CacheInvalidationMessageJsonContext` STJ source-gen context
- [ ] `ICachingBuilder` interface present
- [ ] All public types have XML doc comments
- [ ] Project registered in `Platform.SharedKernel.slnx` under `02.Caching`
- [ ] Package is AOT-safe: no reflection, STJ source-gen only

### Dependencies

- Requires Phase N-x to be complete: No — this phase has no predecessor within the domain
- Unblocks: P-006 (Caching refactor), P-007 (Redis refactor), P-012 (invalidation bus implementation)

### Redis / FusionCache Version Pins

- StackExchange.Redis: N/A (no reference in this package)
- FusionCache: N/A (no reference in this package)
- .NET: `net10.0`
- `Microsoft.Extensions.DependencyInjection.Abstractions`: 10.0.1

---

## Phase: CachingRefactor <!-- phase-key: SK.02.CachingRefactor -->

> Convert `SharedKernel.Caching` from a contract-defining package into a pure FusionCache provider — sources all contracts from `SharedKernel.Caching.Abstractions` and adds `CacheKeyProvider`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| R6-01 | Add `<ProjectReference>` to `SharedKernel.Caching.Abstractions` in `.csproj` | SharedKernel.Caching | `●` |
| R6-02 | Delete `ICacheService.cs` — sourced from abstractions | SharedKernel.Caching | `●` |
| R6-03 | Delete `CachePolicy.cs` — sourced from abstractions | SharedKernel.Caching | `●` |
| R6-04 | Delete `ICachingBuilder.cs` — sourced from abstractions | SharedKernel.Caching | `●` |
| R6-05 | Add `ServiceName` property + non-null/non-whitespace validation to `CachingOptions.cs` | SharedKernel.Caching | `●` |
| R6-06 | Create `CacheKeyProvider.cs` implementing `ICacheKeyProvider` with `{service}:{entity}:{id}` format | SharedKernel.Caching | `●` |
| R6-07 | Register `CacheKeyProvider` as `ICacheKeyProvider` singleton in `AddSharedKernelCaching` DI extension | SharedKernel.Caching | `●` |
| R6-08 | Update `using` in `FusionCacheService.cs` to reference abstractions namespace | SharedKernel.Caching | `●` |

---

### Goal

Convert `SharedKernel.Caching` from a contract-defining package into a pure FusionCache provider implementation package. It sources all interface definitions from `SharedKernel.Caching.Abstractions`, removes its own inline declarations, and adds a `CacheKeyProvider` that implements the `ICacheKeyProvider` contract with the platform-standard `{service}:{entity}:{id}` key format driven by a new `ServiceName` property on `CachingOptions`. The DI extension signatures remain entirely unchanged — this is a non-breaking refactor.

### Scope

- **Package(s) affected:** `SharedKernel.Caching` (modify)
- **New files:**
  - `02.Caching/SharedKernel.Caching/CacheKeyProvider.cs` — default `ICacheKeyProvider` implementation
- **Modified files:**
  - `SharedKernel.Caching.csproj` — add project reference to `SharedKernel.Caching.Abstractions`
  - `ICacheService.cs` (in `SharedKernel.Caching`) — delete (sourced from abstractions)
  - `CachePolicy.cs` (in `SharedKernel.Caching`) — delete (sourced from abstractions)
  - `ICachingBuilder.cs` (in `SharedKernel.Caching`) — delete (sourced from abstractions)
  - `CachingOptions.cs` — add `ServiceName` property (string, default `"app"`, validated non-null/non-whitespace)
  - DI extension class — register `CacheKeyProvider` as `ICacheKeyProvider` singleton
- **Deleted files:** Inline `ICacheService.cs`, `CachePolicy.cs`, `ICachingBuilder.cs` within the `SharedKernel.Caching` project folder

### Implementation Rules

1. The `csproj` must add `<ProjectReference>` to `SharedKernel.Caching.Abstractions` and remove no existing references — FusionCache packages remain.
2. `FusionCacheService` must implement `ICacheService` from the abstractions namespace — the implementation class itself stays in `SharedKernel.Caching`.
3. `CacheKeyProvider` produces keys via the formula `$"{options.ServiceName}:{entity}:{id}"`. When `extraSegments` are provided, each is appended with `:` separator. No casing normalisation is applied by default — the caller controls casing.
4. `CachingOptions.ServiceName` is validated by `IValidateOptions<CachingOptions>` — a validation class already used for other options in this package. If null or whitespace, validation must fail with message `"CachingOptions.ServiceName must not be null or whitespace."`.
5. `AddSharedKernelCaching` registers `CacheKeyProvider` as `IServiceCollection.AddSingleton<ICacheKeyProvider, CacheKeyProvider>()`. The binding is overridable — if the consuming service calls `services.AddSingleton<ICacheKeyProvider, CustomKeyProvider>()` after `AddSharedKernelCaching`, the last registration wins (standard DI behavior, no special handling needed).
6. `CacheJsonSerializerContext` (STJ base) remains in this package — it is an implementation detail, not a contract.
7. All `using` directives in files that previously imported the local `ICacheService`/`CachePolicy` namespace must be updated to import `SharedKernel.Caching.Abstractions`.
8. No behavioral changes to `FusionCacheService`, `AddSharedKernelCaching`, or `AddRedisL2` extension methods.

### File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `SharedKernel.Caching.csproj` | SharedKernel.Caching | Modify | Add project ref to `SharedKernel.Caching.Abstractions` |
| `ICacheService.cs` | SharedKernel.Caching | Delete | Sourced from abstractions |
| `CachePolicy.cs` | SharedKernel.Caching | Delete | Sourced from abstractions |
| `ICachingBuilder.cs` | SharedKernel.Caching | Delete | Sourced from abstractions |
| `CachingOptions.cs` | SharedKernel.Caching | Modify | Add `ServiceName` property + validation |
| `CacheKeyProvider.cs` | SharedKernel.Caching | Create | `ICacheKeyProvider` implementation using `{service}:{entity}:{id}` format |
| DI extension class | SharedKernel.Caching | Modify | Register `CacheKeyProvider` as `ICacheKeyProvider` singleton |
| `FusionCacheService.cs` | SharedKernel.Caching | Modify | Update `using` to reference abstractions namespace |

### Acceptance Criteria

- [ ] `SharedKernel.Caching.csproj` has project reference to `SharedKernel.Caching.Abstractions`
- [ ] `ICacheService`, `CachePolicy`, `ICachingBuilder` removed from this package (sourced from abstractions)
- [ ] `FusionCacheService` still implements `ICacheService` — zero behavioral changes
- [ ] `CacheKeyProvider` implements `ICacheKeyProvider` using `{service}:{entity}:{id}` format
- [ ] `CachingOptions` has `ServiceName` property with non-null/non-whitespace validation
- [ ] `AddSharedKernelCaching` registers `CacheKeyProvider` as the default `ICacheKeyProvider` singleton
- [ ] All existing tests continue to pass — no regressions
- [ ] New tests cover `CacheKeyProvider` key format and `ServiceName` configuration
- [ ] All public types carry XML doc comments

### Dependencies

- Requires Phase 5 (P-005) to be complete: Yes — `SharedKernel.Caching.Abstractions` must exist and be registered before this project can reference it
- Unblocks: No direct phase dependency, but completes the `SharedKernel.Caching` side of the three-package split

### Redis / FusionCache Version Pins

- ZiggyCreatures.FusionCache: 2.6.0 (unchanged)
- ZiggyCreatures.FusionCache.Serialization.SystemTextJson: 2.6.0 (unchanged)
- .NET: `net10.0`

---

## Phase: RedisRefactor <!-- phase-key: SK.02.RedisRefactor -->

> Convert `SharedKernel.Caching.Redis` into a pure Redis provider — sources `IDistributedLockService` from abstractions and adds `RedisChannelService` and `RedisHashService` sharing the singleton `IConnectionMultiplexer`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| R7-01 | Add `<ProjectReference>` to `SharedKernel.Caching.Abstractions`; swap/remove direct `SharedKernel.Caching` reference | SharedKernel.Caching.Redis | `●` |
| R7-02 | Delete `IDistributedLockService.cs` — sourced from abstractions | SharedKernel.Caching.Redis | `●` |
| R7-03 | Update `using` in `RedLockDistributedLockService.cs` to reference abstractions namespace | SharedKernel.Caching.Redis | `●` |
| R7-04 | Create `RedisChannelService.cs` implementing `IRedisChannelService` with Literal channels and exception-safe handler dispatch | SharedKernel.Caching.Redis | `●` |
| R7-05 | Create `RedisHashService.cs` implementing `IRedisHashService` with `JsonTypeInfo<T>` serialization and shared `IDatabase` | SharedKernel.Caching.Redis | `●` |
| R7-06 | Create `Extensions/RedisChannelServiceExtensions.cs` — `AddRedisChannelService(this ICachingBuilder)` | SharedKernel.Caching.Redis | `●` |
| R7-07 | Create `Extensions/RedisHashServiceExtensions.cs` — `AddRedisHashService(this ICachingBuilder)` with multiplexer guard | SharedKernel.Caching.Redis | `●` |

---

### Goal

Convert `SharedKernel.Caching.Redis` from a partial contract definer into a pure Redis provider implementation package. It sources `IDistributedLockService` from `SharedKernel.Caching.Abstractions` and adds two new Redis-backed services: `RedisChannelService` (Pub/Sub fanout for cache-adjacent signaling) and `RedisHashService` (structured field-value projection alongside cache). All three services share the single `IConnectionMultiplexer` singleton to avoid Redis connection proliferation. AOT safety is maintained throughout via STJ `JsonTypeInfo<T>` parameterisation.

### Scope

- **Package(s) affected:** `SharedKernel.Caching.Redis` (modify)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Redis/RedisChannelService.cs`
  - `02.Caching/SharedKernel.Caching.Redis/RedisHashService.cs`
  - `02.Caching/SharedKernel.Caching.Redis/Extensions/RedisChannelServiceExtensions.cs`
  - `02.Caching/SharedKernel.Caching.Redis/Extensions/RedisHashServiceExtensions.cs`
- **Modified files:**
  - `SharedKernel.Caching.Redis.csproj` — replace project reference from `SharedKernel.Caching` to `SharedKernel.Caching.Abstractions` (retain if `ICachingBuilder` extension still needed; see rule 2)
  - `IDistributedLockService.cs` (in `SharedKernel.Caching.Redis`) — delete (sourced from abstractions)
  - `RedLockDistributedLockService.cs` — update `using` to reference abstractions namespace
- **Deleted files:** Inline `IDistributedLockService.cs` within the `SharedKernel.Caching.Redis` project folder

### Implementation Rules

1. The `csproj` must add `<ProjectReference>` to `SharedKernel.Caching.Abstractions`. The existing `<ProjectReference>` to `SharedKernel.Caching` may be retained only if `ICachingBuilder` extension methods are defined in this package and the builder type is still resolved from `SharedKernel.Caching` transitively; otherwise remove it and reference abstractions directly.
2. `RedLockDistributedLockService` must implement `IDistributedLockService` from the abstractions namespace — no behavioral change.
3. `RedisChannelService` must use `RedisChannel.Literal(channelName)` when constructing channel names — never use `RedisChannel.Pattern` for this service.
4. `RedisChannelService` maintains an internal `ConcurrentDictionary<string, Action<RedisChannel, RedisValue>>` subscription registry. `SubscribeAsync` wraps the `Func<string, ValueTask>` handler in a try/catch that logs all exceptions via `ILogger<RedisChannelService>` and never propagates. `UnsubscribeAsync` calls `ISubscriber.UnsubscribeAsync` and removes the handler from the dictionary.
5. `RedisChannelService` is registered as `IRedisChannelService` singleton.
6. `RedisHashService` receives `IConnectionMultiplexer` via constructor injection — it must not create a new connection or call `ConnectionMultiplexer.Connect`. It calls `multiplexer.GetDatabase()` once and caches the `IDatabase` reference.
7. All typed `RedisHashService` methods accept `JsonTypeInfo<T>` as a parameter — no `typeof(T)` reflection, no `JsonSerializer.Serialize<T>(value)` without `typeInfo`. String overloads for plain string field values are accepted alongside typed overloads.
8. `AddRedisHashService` must verify that `IConnectionMultiplexer` is already registered in the service collection before adding `RedisHashService`. If not registered, throw `InvalidOperationException` with message: `"AddRedisHashService requires AddRedisDistributedLocking or AddRedisL2 to be called first to register IConnectionMultiplexer."`.
9. Both `AddRedisChannelService` and `AddRedisHashService` are extension methods on `ICachingBuilder` (consistent with the existing `AddRedisL2` and `AddRedisDistributedLocking` pattern) — not on `IServiceCollection` directly.
10. All handler exceptions in `RedisChannelService` are caught, logged as `LogLevel.Error` with structured properties for `ChannelName` and the exception, and swallowed — the subscriber thread must never observe an unhandled exception.

### File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | Modify | Add/swap project ref to `SharedKernel.Caching.Abstractions` |
| `IDistributedLockService.cs` | SharedKernel.Caching.Redis | Delete | Sourced from abstractions |
| `RedLockDistributedLockService.cs` | SharedKernel.Caching.Redis | Modify | Update `using` to abstractions namespace |
| `RedisChannelService.cs` | SharedKernel.Caching.Redis | Create | `IRedisChannelService` impl; Literal channels; exception-safe handler dispatch |
| `RedisHashService.cs` | SharedKernel.Caching.Redis | Create | `IRedisHashService` impl; typed via `JsonTypeInfo<T>`; shared `IDatabase` from multiplexer |
| `Extensions/RedisChannelServiceExtensions.cs` | SharedKernel.Caching.Redis | Create | `AddRedisChannelService(this ICachingBuilder)` DI extension |
| `Extensions/RedisHashServiceExtensions.cs` | SharedKernel.Caching.Redis | Create | `AddRedisHashService(this ICachingBuilder)` DI extension with guard |

### Acceptance Criteria

- [ ] `SharedKernel.Caching.Redis.csproj` references `SharedKernel.Caching.Abstractions`
- [ ] `IDistributedLockService` removed from this package (sourced from abstractions)
- [ ] `RedisChannelService` implements `IRedisChannelService`; all handler exceptions caught and logged; never propagated
- [ ] `RedisHashService` implements `IRedisHashService` with `JsonTypeInfo<T>` typed serialization
- [ ] Both services share the singleton `IConnectionMultiplexer` — no new connections created
- [ ] `AddRedisChannelService` DI extension on `ICachingBuilder` registers channel service as singleton
- [ ] `AddRedisHashService` DI extension on `ICachingBuilder` throws `InvalidOperationException` with clear message if multiplexer not registered
- [ ] All existing tests pass — no regressions on RedLock and L2 backplane behavior
- [ ] New integration tests cover: Pub/Sub round-trip, unsubscribe stops delivery, all hash operations (set/get/get-all/delete/increment), shared multiplexer verification — all via Testcontainers
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe

### Dependencies

- Requires Phase 5 (P-005) to be complete: Yes — `SharedKernel.Caching.Abstractions` must exist
- Requires Phase 6 (P-006) to be complete: No — P-007 is independent of P-006 (both depend only on P-005)
- Unblocks: P-012 (invalidation bus uses `IRedisChannelService`)

### Redis / FusionCache Version Pins

- StackExchange.Redis: >= 2.13.1 (unchanged)
- ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis: 2.6.0 (unchanged)
- RedLock.net: 2.3.2 (unchanged)
- .NET: `net10.0`

---

## Phase: InvalidationBus <!-- phase-key: SK.02.InvalidationBus -->

> Close the cross-service L1 invalidation gap with a Redis-backed `ICacheInvalidationBus` publisher and opt-in `CacheInvalidationReceiver` background service.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| I-01 | Create `RedisCacheInvalidationBus.cs` implementing `ICacheInvalidationBus` via `IRedisChannelService`; enforce channel naming convention; STJ serialization | SharedKernel.Caching.Redis | `●` |
| I-02 | Create `CacheInvalidationReceiver.cs` `BackgroundService`; subscribe to own-service + broadcast channels; dispatch Key/Tag/All; OTel span; exception-safe | SharedKernel.Caching.Redis | `●` |
| I-03 | Create `Extensions/CacheInvalidationExtensions.cs` — `AddRedisCacheInvalidationBus` (with `IRedisChannelService` guard) and `AddCacheInvalidationReceiver` DI extensions | SharedKernel.Caching.Redis | `●` |

---

### Goal

Close the cross-service L1 invalidation gap: FusionCache's Redis backplane propagates invalidations across instances of the same service but has no mechanism to notify a different service to evict its own L1 entries. This phase adds a first-class `ICacheInvalidationBus` abstraction (contract already placed in `SharedKernel.Caching.Abstractions` by P-005) with a Redis-backed publisher (`RedisCacheInvalidationBus`) and an opt-in `CacheInvalidationReceiver` background service. The channel naming convention, wire payload, OTel tracing continuity, and no-delivery-guarantee contract are all standardised here to prevent teams from inventing divergent solutions.

### Scope

- **Package(s) affected:** `SharedKernel.Caching.Redis` (new files only)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Redis/RedisCacheInvalidationBus.cs`
  - `02.Caching/SharedKernel.Caching.Redis/CacheInvalidationReceiver.cs`
  - `02.Caching/SharedKernel.Caching.Redis/Extensions/CacheInvalidationExtensions.cs`
- **Modified files:** None — all new
- **Deleted files:** None

### Implementation Rules

1. `RedisCacheInvalidationBus` depends only on `IRedisChannelService` (from abstractions) and `CachingOptions` (for `ServiceName`). It must not take a direct `IConnectionMultiplexer` or `StackExchange.Redis` dependency — all channel I/O goes through `IRedisChannelService`.
2. Channel name convention (enforced by `RedisCacheInvalidationBus`):
   - Targeted: `sharedkernel:cache:invalidation:{service-name}` where `{service-name}` is `options.ServiceName.ToLowerInvariant().Replace(' ', '-')`
   - Broadcast: `sharedkernel:cache:invalidation:broadcast` (literal constant — no substitution)
3. `PublishKeyInvalidationAsync` constructs `CacheInvalidationMessage { InvalidationType = Key, Keys = keys, SourceService = ..., CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N"), TimestampUtc = DateTimeOffset.UtcNow }` and calls `PublishInvalidationAsync`.
4. `PublishTagInvalidationAsync` follows the same pattern with `InvalidationType = Tag`, `Tags = tags`.
5. `PublishBroadcastInvalidationAsync` uses `InvalidationType = All` and publishes on the broadcast channel.
6. `PublishInvalidationAsync` serializes `CacheInvalidationMessage` using `CacheInvalidationMessageJsonContext.Default.CacheInvalidationMessage` (the STJ source-gen context from abstractions) and publishes the resulting JSON string.
7. `CacheInvalidationReceiver` is a `BackgroundService`. `ExecuteAsync` calls `IRedisChannelService.SubscribeAsync` for both the own-service channel and the broadcast channel. `StopAsync` override must call `UnsubscribeAsync` for both channels before calling `base.StopAsync`.
8. Dispatch logic in `CacheInvalidationReceiver`:
   - `Key` — iterate `message.Keys` and call `ICacheService.RemoveAsync(key, ct)` for each
   - `Tag` — iterate `message.Tags` and call `ICacheService.RemoveByTagAsync(tag, ct)` for each
   - `All` — log a structured warning that full flush is best-effort via TTL expiry; do not attempt to enumerate all keys. Message: `"CacheInvalidationReceiver received broadcast All invalidation. Full L1 flush is not supported; entries will expire via TTL."`
9. OTel: for each received message, start an `ActivitySource` span named `"cache.invalidation.receive"`. Set `activity.SetTag("cache.invalidation.source", message.SourceService)`, `activity.SetTag("cache.invalidation.correlation_id", message.CorrelationId)`, `activity.SetTag("cache.invalidation.type", message.InvalidationType.ToString())`. If `message.CorrelationId` is a valid W3C trace-parent, use `ActivityContext.TryParse` to link the span to the parent trace.
10. All exceptions inside the receiver's message handler (deserialization errors, `ICacheService` errors) are caught, logged as `LogLevel.Error` with structured properties, and swallowed. Processing must continue for subsequent messages.
11. `AddRedisCacheInvalidationBus` is an extension on `ICachingBuilder`. It registers `RedisCacheInvalidationBus` as `ICacheInvalidationBus` singleton. It must check that `IRedisChannelService` is registered; if not, throw `InvalidOperationException`: `"AddRedisCacheInvalidationBus requires AddRedisChannelService to be called first."`.
12. `AddCacheInvalidationReceiver` is an extension on `ICachingBuilder`. It calls `services.AddHostedService<CacheInvalidationReceiver>()`. The receiver depends on `IRedisChannelService`, `ICacheService`, `CachingOptions`, and `ILogger<CacheInvalidationReceiver>` — all resolved from DI.
13. Both extensions are additive and opt-in — neither is called by default by `AddRedisL2` or `AddSharedKernelCaching`.

### File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `RedisCacheInvalidationBus.cs` | SharedKernel.Caching.Redis | Create | `ICacheInvalidationBus` publisher; channel naming; STJ serialization; OTel send-side tag |
| `CacheInvalidationReceiver.cs` | SharedKernel.Caching.Redis | Create | `BackgroundService` subscriber; dispatch to `ICacheService`; OTel receive-side span; exception-safe |
| `Extensions/CacheInvalidationExtensions.cs` | SharedKernel.Caching.Redis | Create | `AddRedisCacheInvalidationBus` and `AddCacheInvalidationReceiver` DI extensions |

### Acceptance Criteria

- [ ] `ICacheInvalidationBus` interface exists in `SharedKernel.Caching.Abstractions` (placed by P-005) — confirmed present before this phase begins
- [ ] `CacheInvalidationMessage` sealed record exists in `SharedKernel.Caching.Abstractions` with six fields and STJ source-gen context — confirmed present before this phase begins
- [ ] Channel naming enforced: `sharedkernel:cache:invalidation:{service-name}` for targeted, `sharedkernel:cache:invalidation:broadcast` for broadcast
- [ ] `RedisCacheInvalidationBus` implements `ICacheInvalidationBus` using only `IRedisChannelService` — no direct Redis client dependency
- [ ] `CacheInvalidationReceiver` is a `BackgroundService`; subscribes to own-service channel and broadcast channel on startup; unsubscribes gracefully on `StopAsync`
- [ ] Receiver dispatches `Key` invalidations via `ICacheService.RemoveAsync`, `Tag` via `ICacheService.RemoveByTagAsync`, `All` via structured warning log (no enumerate-all attempt)
- [ ] Receiver creates an OTel activity span per message with `SourceService`, `CorrelationId`, `InvalidationType` tags
- [ ] Receiver never propagates exceptions — all errors logged and processing continues
- [ ] `AddRedisCacheInvalidationBus` throws `InvalidOperationException` if `IRedisChannelService` not registered
- [ ] `AddCacheInvalidationReceiver` registers `CacheInvalidationReceiver` as a hosted service
- [ ] Both extensions are opt-in (not auto-registered)
- [ ] Integration tests: Key invalidation round-trip, Tag invalidation round-trip, broadcast warning logged, sender-offline no-crash, OTel correlation id present on span
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe

### Dependencies

- Requires Phase 5 (P-005) to be complete: Yes — `ICacheInvalidationBus` and `CacheInvalidationMessage` must exist in abstractions
- Requires Phase 7 (P-007) to be complete: Yes — `IRedisChannelService` implementation (`RedisChannelService`) must be registered before this phase's receiver can be wired
- Unblocks: Any domain that needs to send targeted cache invalidation signals to peer services

### Redis / FusionCache Version Pins

- StackExchange.Redis: >= 2.13.1 (via `IRedisChannelService` impl — no direct dep in this phase's new files)
- ZiggyCreatures.FusionCache: 2.6.0 (via `ICacheService` dep)
- .NET: `net10.0`
- `System.Diagnostics.DiagnosticSource`: included in `net10.0` BCL — no additional NuGet reference required

---

## Phase: FusionCacheRename <!-- phase-key: SK.02.FusionCacheRename -->

> Rename `SharedKernel.Caching` to `SharedKernel.Caching.FusionCache` for provider-name consistency, migrate all internal namespaces accordingly, and add a `CachePolicy.NeverExpire` preset to the abstractions package for truly static data.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| F-01 | Rename folder `02.Caching/SharedKernel.Caching/` → `02.Caching/SharedKernel.Caching.FusionCache/` | SharedKernel.Caching.FusionCache | `●` |
| F-02 | Rename `.csproj` to `SharedKernel.Caching.FusionCache.csproj`; update `<PackageId>` to `SharedKernel.Caching.FusionCache`; update `<AssemblyName>` if explicitly set; update `<Description>` metadata | SharedKernel.Caching.FusionCache | `●` |
| F-03 | Update `InternalsVisibleTo` attribute to reference `SharedKernel.Caching.FusionCache.Tests` | SharedKernel.Caching.FusionCache | `●` |
| F-04 | Rename nested test project folder and `.csproj` to `SharedKernel.Caching.FusionCache.Tests` | SharedKernel.Caching.FusionCache.Tests | `●` |
| F-05 | Migrate all namespaces inside the package from `SharedKernel.Caching.*` to `SharedKernel.Caching.FusionCache.*` (implementation namespace only — `SharedKernel.Caching.Abstractions` namespace is unchanged) | SharedKernel.Caching.FusionCache | `●` |
| F-06 | Update `<ProjectReference>` in `SharedKernel.Caching.Redis.csproj` to point to `SharedKernel.Caching.FusionCache.csproj` | SharedKernel.Caching.Redis | `●` |
| F-07 | Update `<ProjectReference>` in `consumer-verify` project to reference renamed package | consumer-verify | `—` |
| F-08 | Update `Platform.SharedKernel.slnx` solution folder entry from `SharedKernel.Caching` to `SharedKernel.Caching.FusionCache` | Solution | `●` |
| F-09 | Add `CachePolicy.NeverExpire` static property to `CachePolicy` in `SharedKernel.Caching.Abstractions`; map to `TimeSpan.MaxValue` for both L1 and L2 durations; `FailSafeEnabled = true`; no `EagerRefreshThreshold`; full XML doc | SharedKernel.Caching.Abstractions | `●` |
| F-10 | Add test covering `CachePolicy.NeverExpire` property values (both durations `TimeSpan.MaxValue`, `FailSafeEnabled = true`, no `EagerRefreshThreshold`) | SharedKernel.Caching.FusionCache.Tests | `●` |
| F-11 | Verify all existing tests pass under the new package name — zero regressions | Both | `●` |

---

### Goal

Align the FusionCache L1 provider package with the platform's `SharedKernel.{Capability}.{Provider}` naming convention by renaming `SharedKernel.Caching` to `SharedKernel.Caching.FusionCache`. The current generic name misleads developers into treating it as the abstraction layer; every other multi-provider domain (`.MassTransit`, `.EfCore`, `.Meilisearch`) uses provider-specific suffixes and caching must follow suit. The rename is zero-cost at v1.0.0 and becomes progressively more expensive as services onboard. A companion `CachePolicy.NeverExpire` preset is added to `SharedKernel.Caching.Abstractions` to give teams a discoverable, auditable way to declare truly static data caching intent, replacing ad-hoc large `TimeSpan` values.

### Scope

- **Packages affected:** `SharedKernel.Caching` (rename to `SharedKernel.Caching.FusionCache`); `SharedKernel.Caching.Abstractions` (additive change — `CachePolicy.NeverExpire`); `SharedKernel.Caching.Redis` (project reference update only); consumer-verify project (project reference update only); solution file
- **New files:** None
- **Modified files:**
  - `02.Caching/SharedKernel.Caching/SharedKernel.Caching.csproj` → renamed to `SharedKernel.Caching.FusionCache.csproj` (all metadata updated)
  - All `.cs` source files inside the renamed package — namespace declarations updated
  - `02.Caching/SharedKernel.Caching/SharedKernel.Caching.Tests/` → renamed to `SharedKernel.Caching.FusionCache.Tests/`
  - `SharedKernel.Caching.Redis.csproj` — `<ProjectReference>` path updated
  - consumer-verify `.csproj` — `<ProjectReference>` path updated
  - `Platform.SharedKernel.slnx` — solution folder entry updated
  - `02.Caching/SharedKernel.Caching.Abstractions/CachePolicy.cs` — `NeverExpire` static property added
- **Deleted files:** None — this is a rename, not a delete + create; git tracks the rename via `git mv`

### Implementation Rules

1. The folder and project rename must be performed with `git mv` to preserve history — do not delete and recreate.
2. The implementation namespace changes from `SharedKernel.Caching` to `SharedKernel.Caching.FusionCache` for all types that live inside the renamed package (`FusionCacheService`, `CacheKeyProvider`, `CachingOptions`, `CacheJsonSerializerContext`, DI extensions). The abstractions namespace `SharedKernel.Caching.Abstractions` is immutable — it must not change.
3. `CacheJsonSerializerContext` (STJ source-gen base) must update its `namespace` declaration to `SharedKernel.Caching.FusionCache` — it is an implementation detail of the FusionCache provider, not a contract.
4. The renamed `.csproj` must keep all existing NuGet references and project references unchanged except for the `<PackageId>`, `<AssemblyName>` (if present), and `<Description>` fields.
5. `CachePolicy.NeverExpire` must be a `static` read-only property on the `CachePolicy` sealed record in `SharedKernel.Caching.Abstractions`. It must return a new `CachePolicy` instance constructed with `L1Duration = TimeSpan.MaxValue`, `L2Duration = TimeSpan.MaxValue`, and `FailSafeEnabled = true`. No `EagerRefreshThreshold` must be set (it remains at its default `null`/absent value).
6. The XML doc on `CachePolicy.NeverExpire` must state:
   - Intended use case: truly static data (reference tables, feature flag snapshots, lookup codes).
   - Explicit invalidation requirement: cache invalidation must be managed explicitly via `ICacheService.RemoveAsync` or `ICacheInvalidationBus` because TTL-based expiry will not occur.
   - Warning: do not use for any data that can change without an explicit invalidation signal.
7. `CachePolicy.NeverExpire` must not set `EagerRefreshThreshold` — there is nothing to eagerly refresh when expiry is indefinite.
8. After the rename, `SharedKernel.Caching.Redis.csproj` must reference `SharedKernel.Caching.FusionCache.csproj` only if the Redis package previously had a direct `<ProjectReference>` to `SharedKernel.Caching`. If the Redis package now references only `SharedKernel.Caching.Abstractions`, no change is needed for the Redis project reference path — verify the actual state before applying.
9. The test for `CachePolicy.NeverExpire` must verify: `L1Duration == TimeSpan.MaxValue`, `L2Duration == TimeSpan.MaxValue`, `FailSafeEnabled == true`, `EagerRefreshThreshold == null` (or absent). It lives in `SharedKernel.Caching.FusionCache.Tests`.
10. `02.Caching/CLAUDE.md` package table must be updated: `SharedKernel.Caching` row replaced by `SharedKernel.Caching.FusionCache`. The root `CLAUDE.md` "What Goes Where" table and abstractions table must be updated by the implementer — the root file is outside this agent's jurisdiction and must be flagged to the root `arch-lead` for update.

### File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `SharedKernel.Caching/` (folder) | SharedKernel.Caching.FusionCache | Rename via `git mv` | Align folder name with new package identity |
| `SharedKernel.Caching.csproj` | SharedKernel.Caching.FusionCache | Rename + modify | Update `<PackageId>`, `<AssemblyName>`, `<Description>`, `InternalsVisibleTo` |
| `SharedKernel.Caching.Tests/` (folder) | SharedKernel.Caching.FusionCache.Tests | Rename via `git mv` | Align test folder name |
| `SharedKernel.Caching.Tests.csproj` | SharedKernel.Caching.FusionCache.Tests | Rename + modify | Update project reference to parent package |
| All `.cs` source files (package) | SharedKernel.Caching.FusionCache | Modify | Update `namespace` declarations from `SharedKernel.Caching` to `SharedKernel.Caching.FusionCache` |
| All `.cs` source files (tests) | SharedKernel.Caching.FusionCache.Tests | Modify | Update `using` and `namespace` declarations |
| `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | Modify (if applicable) | Update `<ProjectReference>` path to renamed package |
| consumer-verify `.csproj` | consumer-verify | Modify | Update `<ProjectReference>` path to renamed package |
| `Platform.SharedKernel.slnx` | Solution | Modify | Update solution folder entry to new project path |
| `CachePolicy.cs` | SharedKernel.Caching.Abstractions | Modify | Add `NeverExpire` static property with full XML doc |
| New test method in `CachePolicyTests.cs` | SharedKernel.Caching.FusionCache.Tests | Modify | Verify `NeverExpire` property values |

### Acceptance Criteria

- [ ] Folder `02.Caching/SharedKernel.Caching.FusionCache/` exists; `02.Caching/SharedKernel.Caching/` does not
- [ ] `SharedKernel.Caching.FusionCache.csproj` has `<PackageId>SharedKernel.Caching.FusionCache</PackageId>`
- [ ] `InternalsVisibleTo` updated to `SharedKernel.Caching.FusionCache.Tests`
- [ ] Nested test project folder and `.csproj` named `SharedKernel.Caching.FusionCache.Tests`
- [ ] All namespaces inside the renamed package are `SharedKernel.Caching.FusionCache.*`
- [ ] `SharedKernel.Caching.Abstractions` namespace unchanged throughout
- [ ] `SharedKernel.Caching.Redis.csproj` project reference path is correct for the renamed package (verify current state — may already reference Abstractions only)
- [ ] consumer-verify project reference path updated
- [ ] `Platform.SharedKernel.slnx` solution entry updated
- [ ] `CachePolicy.NeverExpire` static property present in `SharedKernel.Caching.Abstractions`; `L1Duration == TimeSpan.MaxValue`, `L2Duration == TimeSpan.MaxValue`, `FailSafeEnabled == true`, no `EagerRefreshThreshold`
- [ ] XML doc on `CachePolicy.NeverExpire` states: static-data intent, explicit invalidation requirement via `ICacheService.RemoveAsync` or `ICacheInvalidationBus`, warning against use for mutable data
- [ ] Test covering all four `NeverExpire` property assertions exists and passes
- [ ] All existing 66 Caching tests + 62 Redis tests pass with zero regressions
- [ ] `02.Caching/CLAUDE.md` package table reflects `SharedKernel.Caching.FusionCache`

### Dependencies

- Requires Phase: None — no prior incomplete phase; all phases 5/6/7/12 are complete
- Unblocks: Future phases that reference the FusionCache provider by correct name; root `CLAUDE.md` "What Goes Where" table update (root agent scope — flag to `arch-lead`)

### Redis / FusionCache Version Pins

- ZiggyCreatures.FusionCache: 2.6.0 (unchanged)
- ZiggyCreatures.FusionCache.Serialization.SystemTextJson: 2.6.0 (unchanged)
- StackExchange.Redis: 2.13.1 (unchanged — Redis package untouched except project ref path)
- RedLock.net: 2.3.2 (unchanged)
- Microsoft.Extensions.DependencyInjection.Abstractions: 10.0.1 (unchanged)
- .NET: `net10.0`

---

## Phase: AotHardening <!-- phase-key: SK.02.AotHardening -->

> Fix the FusionCache STJ serializer AOT gap and introduce `ITypedHashStore<T>` — a typed generic service that eliminates per-call `JsonTypeInfo<T>` passing while maintaining full NativeAOT safety.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| H-01 | Add `SerializerContext` property (`JsonSerializerContext?`) to `CachingOptions` | SharedKernel.Caching.FusionCache | `●` |
| H-02 | Wire `SerializerContext` into `WithSystemTextJsonSerializer()` in `AddSharedKernelCaching` — combine with `CacheInvalidationMessageJsonContext.Default` via `JsonTypeInfoResolver.Combine` when set | SharedKernel.Caching.FusionCache | `●` |
| H-03 | Add `ITypedHashStore<T>` to `SharedKernel.Caching.Abstractions` — five methods mirroring `IRedisHashService` without `JsonTypeInfo<T>` parameter; full XML doc stating the AOT registration contract | SharedKernel.Caching.Abstractions | `●` |
| H-04 | Create `TypedHashStore<T>.cs` in `SharedKernel.Caching.Redis` — internal sealed class implementing `ITypedHashStore<T>`; captures `JsonTypeInfo<T>` in constructor; delegates all operations to `IRedisHashService` | SharedKernel.Caching.Redis | `●` |
| H-05 | Add `AddTypedHashStore<T>(JsonTypeInfo<T> typeInfo)` extension on `ICachingBuilder` in `RedisHashServiceExtensions.cs` — registers `TypedHashStore<T>` as `ITypedHashStore<T>` singleton; guard: `IRedisHashService` must already be registered | SharedKernel.Caching.Redis | `●` |
| H-06 | Update `CacheJsonSerializerContext` XML doc to document the new `SerializerContext` startup registration pattern | SharedKernel.Caching.FusionCache | `●` |
| H-07 | Integration tests: `ITypedHashStore<T>` set/get/get-all/delete/increment round-trip via Testcontainers | SharedKernel.Caching.Redis.Tests | `●` |
| H-08 | Unit test: `AddSharedKernelCaching` with `SerializerContext` set — verify FusionCache STJ serializer options include the provided context type info resolver | SharedKernel.Caching.FusionCache.Tests | `●` |

---

### Goal

Fix a critical NativeAOT gap: `AddSharedKernelCaching` currently calls `.WithSystemTextJsonSerializer()` without a user-provided `JsonSerializerContext`, causing FusionCache's L2 serializer to fall back to reflection-based STJ for all cached types. This silently breaks NativeAOT for every microservice using L2 Redis. The fix adds `SerializerContext` to `CachingOptions` and plumbs it through to FusionCache's STJ serializer at startup.

Simultaneously, introduce `ITypedHashStore<T>` — the .NET 10 gold standard for AOT-safe typed hash access. By capturing `JsonTypeInfo<T>` once at DI registration (analogous to typed `HttpClient`), consuming services inject `ITypedHashStore<OrderDto>` directly and call `GetFieldAsync(key, field)` with no per-call type passing. `IRedisHashService` with explicit `JsonTypeInfo<T>` parameters is retained as the low-level primitive for generic infrastructure code.

### Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (add `ITypedHashStore<T>`); `SharedKernel.Caching.FusionCache` (fix STJ serializer); `SharedKernel.Caching.Redis` (add `TypedHashStore<T>` + DI extension)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Abstractions/ITypedHashStore.cs`
  - `02.Caching/SharedKernel.Caching.Redis/TypedHashStore.cs`
- **Modified files:**
  - `CachingOptions.cs` — add `SerializerContext` property
  - `CachingServiceCollectionExtensions.cs` — wire `SerializerContext` into serializer registration
  - `RedisHashServiceExtensions.cs` — add `AddTypedHashStore<T>` extension
  - `CacheJsonSerializerContext.cs` — update XML doc

### Implementation Rules

1. `CachingOptions.SerializerContext` is `JsonSerializerContext?` — nullable, no validation failure if absent. When absent, FusionCache's STJ serializer uses default options (reflection-based). Warn via XML doc that AOT builds require this to be set.
2. When `SerializerContext` is set, construct `JsonSerializerOptions` with `TypeInfoResolver = JsonTypeInfoResolver.Combine(options.SerializerContext, CacheInvalidationMessageJsonContext.Default)` and pass to `WithSystemTextJsonSerializer(jsonOptions)`.
3. `ITypedHashStore<T>` is in namespace `SharedKernel.Caching.Abstractions`. It is public, generic, and contains exactly five methods: `GetFieldAsync`, `SetFieldAsync`, `GetAllFieldsAsync`, `DeleteFieldAsync`, `IncrementFieldAsync` — signatures identical to `IRedisHashService` equivalents minus the `JsonTypeInfo<T>` parameter.
4. `TypedHashStore<T>` is `internal sealed class` in `SharedKernel.Caching.Redis`. Constructor: `TypedHashStore(IRedisHashService hashService, JsonTypeInfo<T> typeInfo)`. Each method calls through to `IRedisHashService` passing the captured `typeInfo`.
5. `AddTypedHashStore<T>` must guard: if `IRedisHashService` is not registered, throw `InvalidOperationException` with message `"AddTypedHashStore<T> requires AddRedisHashService to be called first."`. Registration: `services.AddSingleton<ITypedHashStore<T>>(sp => new TypedHashStore<T>(sp.GetRequiredService<IRedisHashService>(), typeInfo))`.
6. `IRedisHashService` is NOT removed or deprecated — it remains the low-level primitive. `ITypedHashStore<T>` is an ergonomic overlay.
7. No changes to `ICacheService` or any other contract — this phase is additive only.
8. All new public types carry XML doc comments.

### File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `ITypedHashStore.cs` | SharedKernel.Caching.Abstractions | Create | Typed generic hash store abstraction — no `JsonTypeInfo<T>` per call |
| `TypedHashStore.cs` | SharedKernel.Caching.Redis | Create | Internal implementation capturing `JsonTypeInfo<T>` at construction |
| `CachingOptions.cs` | SharedKernel.Caching.FusionCache | Modify | Add `SerializerContext` property |
| `CachingServiceCollectionExtensions.cs` | SharedKernel.Caching.FusionCache | Modify | Wire `SerializerContext` into STJ serializer registration |
| `RedisHashServiceExtensions.cs` | SharedKernel.Caching.Redis | Modify | Add `AddTypedHashStore<T>` extension |
| `CacheJsonSerializerContext.cs` | SharedKernel.Caching.FusionCache | Modify | Update XML doc for new pattern |

### Acceptance Criteria

- [ ] `CachingOptions` has `SerializerContext` property accepting `JsonSerializerContext?`
- [ ] `AddSharedKernelCaching` passes a combined `JsonSerializerOptions` to `WithSystemTextJsonSerializer` when `SerializerContext` is set
- [ ] `ITypedHashStore<T>` interface present in `SharedKernel.Caching.Abstractions` with five methods and full XML doc
- [ ] `TypedHashStore<T>` implements `ITypedHashStore<T>`; captures `JsonTypeInfo<T>` at construction; delegates to `IRedisHashService`
- [ ] `AddTypedHashStore<T>` extension on `ICachingBuilder` registers `ITypedHashStore<T>` as singleton; guards on `IRedisHashService` presence
- [ ] All existing 70 FusionCache + 62 Redis tests continue to pass — zero regressions
- [ ] New integration tests cover `ITypedHashStore<T>` five-method round-trip via Testcontainers
- [ ] Unit test verifies `SerializerContext` is threaded through to FusionCache STJ serializer options

### Dependencies

- Requires: No prior incomplete phase
- Unblocks: Any microservice wishing to use NativeAOT with L2 caching; Phase 16 (Brotli) which builds on the same FusionCache serializer extension point

### Declined Items

- **protobuf-net serialization** — DECLINED. Not NativeAOT-compatible (`RuntimeTypeModel` uses `IL.Emit`). Requires `[ProtoContract]`/`[ProtoMember]` on every DTO — invasive, violates clean arch principle that domain/app DTOs must not reference infrastructure packages. Breaks existing Redis wire format on upgrade/rollback. Space savings from Brotli compression (Phase 16) achieve equivalent payload reduction without these drawbacks.

---

## Phase: BrotliCompression <!-- phase-key: SK.02.BrotliCompression -->

> Add opt-in Brotli compression to the L2 Redis distributed cache path — BCL-only, zero additional NuGet dependencies, configurable threshold, backward-compatible magic byte detection.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| B-01 | Add `CompressionOptions` nested class to `CachingOptions` — `Enabled` (bool, default `false`), `L2ThresholdBytes` (int, default `1024`), `Level` (`CompressionLevel`, default `Fastest`) | SharedKernel.Caching.FusionCache | `●` |
| B-02 | Create `BrotliCacheSerializer.cs` in `SharedKernel.Caching.FusionCache/Serialization/` — implements `IFusionCacheSerializer`; delegates to base STJ serializer; compresses payloads above threshold using `BrotliEncoder`; prepends magic byte `0xBR` (hex `0x42 0x52`) for detection on read | SharedKernel.Caching.FusionCache | `●` |
| B-03 | Add `AddBrotliCompression(Action<CompressionOptions>? configure = null)` extension on `ICachingBuilder` in new `Extensions/BrotliCompressionExtensions.cs` — replaces registered `IFusionCacheSerializer` with `BrotliCacheSerializer`; validates `L2ThresholdBytes > 0` | SharedKernel.Caching.FusionCache | `●` |
| B-04 | Unit tests: compressed round-trip (payload above threshold), passthrough (below threshold), magic-byte detection (decompressor handles both variants transparently), `CompressionLevel` configuration | SharedKernel.Caching.FusionCache.Tests | `●` |
| B-05 | Update `02.Caching/CLAUDE.md` package table and DI registration shape for new compression extension | Both | `●` |

---

### Goal

Enable opt-in Brotli compression for the L2 Redis distributed cache path. Values serialized to L2 above a configurable byte threshold are compressed with `BrotliEncoder` before being written to Redis; on read, the magic-byte prefix determines whether decompression is needed (transparent to callers). L1 in-process memory is never affected. No new NuGet dependencies — `System.IO.Compression.Brotli` is in the .NET 10 BCL.

### Scope

- **Package(s) affected:** `SharedKernel.Caching.FusionCache` only
- **New files:**
  - `02.Caching/SharedKernel.Caching.FusionCache/Serialization/BrotliCacheSerializer.cs`
  - `02.Caching/SharedKernel.Caching.FusionCache/Extensions/BrotliCompressionExtensions.cs`
- **Modified files:**
  - `CachingOptions.cs` — add `CompressionOptions` nested class
  - `02.Caching/CLAUDE.md` — update DI shape and implementation rules
- **Deleted files:** None

### Implementation Rules

1. `BrotliCacheSerializer` wraps an existing `IFusionCacheSerializer` (the STJ serializer registered by `AddSharedKernelCaching`). It must not re-implement STJ serialization — it decorates.
2. Magic byte prefix: the first 2 bytes of a compressed payload are `0x42 0x52` ("BR" in ASCII). Uncompressed payloads start with any other byte. `DeserializeAsync` reads the first 2 bytes to detect compression; if not present, it delegates directly to the base serializer without copying. This ensures backward compatibility: values written before compression was enabled are still readable.
3. `BrotliEncoder.TryCompress` is used (not `BrotliStream`) to avoid `MemoryStream` allocation on the hot path. Use `ArrayPool<byte>.Shared` for the output buffer — rent a buffer sized at `BrotliEncoder.GetMaxCompressedLength(inputLength) + 2`, write magic bytes + compressed payload, then copy the used portion to a final `byte[]` and return the rented buffer.
4. Compression is only applied when `payload.Length >= CompressionOptions.L2ThresholdBytes`. Below threshold, the raw STJ `byte[]` is passed through unchanged (no magic byte prepended).
5. `AddBrotliCompression` resolves the already-registered `IFusionCacheSerializer` from the service collection (the one added by `WithSystemTextJsonSerializer`), wraps it in `BrotliCacheSerializer`, and re-registers it via `services.Replace(ServiceDescriptor.Singleton<IFusionCacheSerializer>(sp => new BrotliCacheSerializer(sp.GetRequiredService<IFusionCacheSerializer>(), compressionOptions)))`. Note: this depends on `WithSystemTextJsonSerializer` registering `IFusionCacheSerializer` as a singleton — verify this is the case at implementation time.
6. `CompressionOptions.Level` maps directly to `System.IO.Compression.CompressionLevel`. Default is `Fastest` — for a cache the round-trip latency cost matters more than compression ratio.
7. `CompressionOptions.L2ThresholdBytes` must be validated: `> 0`, error message `"CompressionOptions.L2ThresholdBytes must be greater than zero."`.
8. No changes to `ICacheService`, `IRedisHashService`, `ITypedHashStore<T>`, or any abstraction — this is entirely an implementation detail of the FusionCache provider.
9. All public types carry XML doc comments.

### File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Serialization/BrotliCacheSerializer.cs` | SharedKernel.Caching.FusionCache | Create | `IFusionCacheSerializer` decorator with Brotli compress/decompress + magic byte detection |
| `Extensions/BrotliCompressionExtensions.cs` | SharedKernel.Caching.FusionCache | Create | `AddBrotliCompression` extension on `ICachingBuilder` |
| `CachingOptions.cs` | SharedKernel.Caching.FusionCache | Modify | Add `CompressionOptions` nested class |
| `CLAUDE.md` | 02.Caching | Modify | DI shape + compression rules |

### Acceptance Criteria

- [ ] `CachingOptions` has `CompressionOptions` nested class with `Enabled`, `L2ThresholdBytes`, `Level`
- [ ] `BrotliCacheSerializer` implements `IFusionCacheSerializer` as a decorator over the base STJ serializer
- [ ] Payloads at or above `L2ThresholdBytes` are compressed; below threshold pass through unchanged
- [ ] Magic bytes `0x42 0x52` detected on read; both compressed and uncompressed payloads deserialize correctly
- [ ] `ArrayPool<byte>.Shared` used for compression output buffer — no `MemoryStream` allocation on hot path
- [ ] `AddBrotliCompression` extension on `ICachingBuilder` registers the decorator
- [ ] All existing 70 FusionCache tests continue to pass — compression is opt-in and off by default
- [ ] Four new unit tests: compressed round-trip, passthrough, mixed (both variants in same cache), level configuration
- [ ] Zero new NuGet package references added to any `.csproj`

### Dependencies

- Requires Phase 15 (H-01/H-02) — the `SerializerContext` fix makes the FusionCache serializer extension point well-understood; Brotli wraps the same serializer
- Unblocks: Any service caching large payloads (binary assets refs, report snapshots, bulk lookup tables)

### Not In Scope

- `IRedisHashService` Brotli compression — hash fields are naturally small; callers who need per-field compression can compress values before calling `SetFieldAsync`
- `BrotliStream` streaming path — requires FusionCache `PipeWriter` serializer API (not yet in FC 2.6.0)
- `ArrayPool<byte>` optimization for `IRedisHashService` string path — micro-optimization deferred to a future perf phase
- Memory-mapped / zero-copy Redis writes — out of scope for this phase

---

## Phase: LayeringFix <!-- phase-key: SK.02.LayeringFix -->

> Resolve the sibling-package layering violation where `SharedKernel.Caching.Redis` depends directly on `SharedKernel.Caching.FusionCache` solely to access `CachingOptions.ServiceName`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| L-01 | Add `CachingCoreOptions` sealed class to `SharedKernel.Caching.Abstractions` — `ServiceName` string property, default `"app"`, namespace `SharedKernel.Caching.Abstractions` | SharedKernel.Caching.Abstractions | `●` |
| L-02 | Add `Microsoft.Extensions.Options` reference to `SharedKernel.Caching.Abstractions.csproj` if required for `IOptions<T>` usage (verify minimum — `Microsoft.Extensions.DependencyInjection.Abstractions` may already pull it transitively) | SharedKernel.Caching.Abstractions | `●` |
| L-03 | Update `AddSharedKernelCaching` in `SharedKernel.Caching.FusionCache` to register `CachingCoreOptions` as a configured singleton, copying `ServiceName` from `CachingOptions` so both types are in sync | SharedKernel.Caching.FusionCache | `●` |
| L-04 | Update `RedisCacheInvalidationBus` to depend on `IOptions<CachingCoreOptions>` (from Abstractions) instead of `IOptions<CachingOptions>` (from FusionCache) | SharedKernel.Caching.Redis | `●` |
| L-05 | Update `CacheInvalidationReceiver` to depend on `IOptions<CachingCoreOptions>` (from Abstractions) instead of `IOptions<CachingOptions>` (from FusionCache) | SharedKernel.Caching.Redis | `●` |
| L-06 | Remove `<ProjectReference>` to `SharedKernel.Caching.FusionCache` from `SharedKernel.Caching.Redis.csproj`; confirm only `SharedKernel.Caching.Abstractions` reference remains | SharedKernel.Caching.Redis | `●` |
| L-07 | Verify all 91 FusionCache + 74 Redis tests pass with zero regressions | Both | `●` |
| L-08 | Verify a composition root that references only `SharedKernel.Caching.Redis` + `SharedKernel.Caching.Abstractions` compiles without any FusionCache transitive dependency | Both | `●` |

---

### Ph17 — Goal

Eliminate the sibling-package layering violation: `SharedKernel.Caching.Redis` depends on `SharedKernel.Caching.FusionCache` only to access `CachingOptions.ServiceName` for channel name construction. Placing the shared property `ServiceName` in a new `CachingCoreOptions` class in `SharedKernel.Caching.Abstractions` — where it logically belongs — removes the transitive FusionCache dependency from the Redis package. A service that needs only Redis capabilities (channel service, hash service, distributed locking, invalidation bus) without L1 FusionCache will no longer be forced to pull in the FusionCache provider.

### Ph17 — Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (additive — new class); `SharedKernel.Caching.FusionCache` (register `CachingCoreOptions`); `SharedKernel.Caching.Redis` (remove FusionCache ref, update two classes)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Abstractions/CachingCoreOptions.cs`
- **Modified files:**
  - `SharedKernel.Caching.Abstractions.csproj` — possible `Microsoft.Extensions.Options` reference addition
  - `CachingServiceCollectionExtensions.cs` (FusionCache) — register `CachingCoreOptions`
  - `RedisCacheInvalidationBus.cs` — switch from `IOptions<CachingOptions>` to `IOptions<CachingCoreOptions>`
  - `CacheInvalidationReceiver.cs` — switch from `IOptions<CachingOptions>` to `IOptions<CachingCoreOptions>`
  - `SharedKernel.Caching.Redis.csproj` — remove `ProjectReference` to FusionCache
- **Deleted files:** None

### Ph17 — Implementation Rules

1. `CachingCoreOptions` is a `sealed class` in namespace `SharedKernel.Caching.Abstractions`. It carries exactly one property for now: `ServiceName` (string, default `"app"`). XML doc must state it is the authoritative source of shared options consumed by all caching provider packages.
2. `SharedKernel.Caching.Abstractions.csproj` must remain zero-infrastructure. `Microsoft.Extensions.Options` ships as part of `Microsoft.Extensions.DependencyInjection.Abstractions` transitive graph — confirm before adding a redundant reference. If `IOptions<T>` resolution is needed only at consumption sites (FusionCache, Redis), the class itself needs no special NuGet reference.
3. `AddSharedKernelCaching` in FusionCache must register `CachingCoreOptions` using `services.Configure<CachingCoreOptions>(o => o.ServiceName = cachingOptions.ServiceName)` after binding `CachingOptions`. Both options types must reflect the same `ServiceName` at startup.
4. `CachingOptions.ServiceName` in FusionCache retains its existing validation (non-null/non-whitespace). `CachingCoreOptions.ServiceName` does not duplicate the validation — it trusts the value copied from `CachingOptions`.
5. `RedisCacheInvalidationBus` and `CacheInvalidationReceiver` must resolve `IOptions<CachingCoreOptions>` from Abstractions namespace only — no `using` import of the FusionCache namespace.
6. After the fix, `SharedKernel.Caching.Redis.csproj` must have no `ProjectReference` to `SharedKernel.Caching.FusionCache`. If FusionCache NuGet packages (ZiggyCreatures.*) were pulled transitively through the project reference, they must remain as explicit `PackageReference` entries.
7. The channel naming convention (`sharedkernel:cache:invalidation:{service-name}`) is unchanged.
8. This is a non-breaking restructure — no interface changes, no behavioral changes, no new public APIs beyond `CachingCoreOptions`.

### Ph17 — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `CachingCoreOptions.cs` | SharedKernel.Caching.Abstractions | Create | Shared options class with `ServiceName`; authoritative across all provider packages |
| `SharedKernel.Caching.Abstractions.csproj` | SharedKernel.Caching.Abstractions | Modify if needed | Verify `Microsoft.Extensions.Options` availability |
| `CachingServiceCollectionExtensions.cs` | SharedKernel.Caching.FusionCache | Modify | Register `CachingCoreOptions` in sync with `CachingOptions.ServiceName` |
| `RedisCacheInvalidationBus.cs` | SharedKernel.Caching.Redis | Modify | Switch to `IOptions<CachingCoreOptions>` |
| `CacheInvalidationReceiver.cs` | SharedKernel.Caching.Redis | Modify | Switch to `IOptions<CachingCoreOptions>` |
| `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | Modify | Remove FusionCache project reference |

### Ph17 — Acceptance Criteria

- [ ] `CachingCoreOptions` class exists in `SharedKernel.Caching.Abstractions` with at minimum a `ServiceName` property (string, default `"app"`)
- [ ] `SharedKernel.Caching.Redis.csproj` no longer has a `ProjectReference` to `SharedKernel.Caching.FusionCache`
- [ ] `RedisCacheInvalidationBus` depends on `IOptions<CachingCoreOptions>` from Abstractions, not `IOptions<CachingOptions>` from FusionCache
- [ ] `CacheInvalidationReceiver` depends on `IOptions<CachingCoreOptions>` from Abstractions, not `IOptions<CachingOptions>` from FusionCache
- [ ] `AddSharedKernelCaching` registers `CachingCoreOptions` as a configured singleton — `ServiceName` kept in sync with `CachingOptions`
- [ ] `CachingOptions.ServiceName` in FusionCache retains its validation and default
- [ ] A service referencing only `SharedKernel.Caching.Redis` + `SharedKernel.Caching.Abstractions` can call `AddRedisChannelService`, `AddRedisHashService`, `AddRedisCacheInvalidationBus` without pulling in FusionCache
- [ ] All 91 FusionCache + 74 Redis tests pass — zero regressions

### Ph17 — Dependencies

- Requires prior incomplete phase: None
- Unblocks: Phase 19 (DI Ergonomics) which also touches `ICachingBuilder` extension signatures in `SharedKernel.Caching.Redis`

### Ph17 — Version Pins

- StackExchange.Redis: >= 2.13.1 (unchanged)
- ZiggyCreatures.FusionCache: 2.6.0 (unchanged)
- Microsoft.Extensions.DependencyInjection.Abstractions: 10.0.1 (unchanged)
- .NET: `net10.0`

---

## Phase: AotSerializerFix <!-- phase-key: SK.02.AotSerializerFix -->

> Fix the silent NativeAOT correctness bug where `AddRedisL2` overwrites the user-configured STJ serializer with a reflection-based default, destroying the `SerializerContext` set by `AddSharedKernelCaching`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| AS-01 | Remove `.WithSystemTextJsonSerializer()` call from `AddRedisL2` in `RedisServiceCollectionExtensions.cs` | SharedKernel.Caching.Redis | `●` |
| AS-02 | Replace the removed call with `.WithRegisteredSerializer()` (or rely on FusionCache's automatic DI-registered serializer pickup — verify FusionCache 2.6.0 behavior) so the DI-registered `IFusionCacheSerializer` is used for L2 | SharedKernel.Caching.Redis | `●` |
| AS-03 | Confirm `AddRedisL2` retains `.WithRegisteredDistributedCache()` and `.WithStackExchangeRedisBackplane()` — no other FusionCache builder calls | SharedKernel.Caching.Redis | `●` |
| AS-04 | Write regression test: after `AddSharedKernelCaching(o => o.SerializerContext = ctx)` followed by `AddRedisL2(...)`, verify the DI-resolved `IFusionCacheSerializer` is a `FusionCacheSystemTextJsonSerializer` (or `BrotliCacheSerializer` wrapping one) whose `JsonSerializerOptions.TypeInfoResolver` includes `ctx` | SharedKernel.Caching.FusionCache.Tests or SharedKernel.Caching.Redis.Tests | `●` |
| AS-05 | Verify all 91 FusionCache + 74 Redis tests pass — zero regressions | Both | `●` |

---

### Ph18 — Goal

Correct a silent high-severity NativeAOT bug: `AddRedisL2` calls `.WithSystemTextJsonSerializer()` on the FusionCache builder after `AddSharedKernelCaching` has already registered an AOT-safe STJ serializer (potentially configured with a user-supplied `SerializerContext`). The second call silently replaces the options-aware registration with a reflection-based default, making NativeAOT builds fail at runtime with cryptic serialization errors that never surface during non-AOT development. The fix removes all serializer registration from `AddRedisL2` — FusionCache will pick up the DI-registered `IFusionCacheSerializer` automatically via `WithRegisteredSerializer()`.

### Ph18 — Scope

- **Package(s) affected:** `SharedKernel.Caching.Redis` (remove serializer call); `SharedKernel.Caching.FusionCache.Tests` or `SharedKernel.Caching.Redis.Tests` (new regression test)
- **New files:** None (test added to existing test class or new test class)
- **Modified files:**
  - `RedisServiceCollectionExtensions.cs` — remove `.WithSystemTextJsonSerializer()`, add `.WithRegisteredSerializer()` if needed
- **Deleted files:** None

### Ph18 — Implementation Rules

1. `AddRedisL2` must not call `.WithSystemTextJsonSerializer()` in any form — not with options, not without options.
2. FusionCache 2.6.0 supports `WithRegisteredSerializer()` to pick up an `IFusionCacheSerializer` from DI. If this method is available, use it explicitly. If FusionCache's builder automatically resolves from DI when no serializer is registered (verify in 2.6.0 docs/source), an explicit call may be unnecessary — document the decision.
3. `AddRedisL2` is only permitted to call: `.WithRegisteredDistributedCache()`, `.WithStackExchangeRedisBackplane()`, and `.WithRegisteredSerializer()` (if required). No other FusionCache builder methods belong in this extension.
4. The regression test must resolve `IFusionCacheSerializer` from the built `IServiceProvider` directly and inspect its type and configuration — do not rely on cache behavior tests alone, as those will pass even with a reflection-based serializer in non-AOT mode.
5. If `BrotliCacheSerializer` is registered via `AddBrotliCompression`, the resolved `IFusionCacheSerializer` will be a `BrotliCacheSerializer` wrapping the STJ serializer. The test must handle this case: unwrap one level and assert the inner serializer carries the correct `JsonSerializerOptions`.
6. The regression test lives in `SharedKernel.Caching.FusionCache.Tests` if it tests only DI wiring (no Redis needed), or in `SharedKernel.Caching.Redis.Tests` if it requires `AddRedisL2` to be called with a real or mocked `IDistributedCache`. Prefer the FusionCache test project for a pure DI wiring test.

### Ph18 — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `RedisServiceCollectionExtensions.cs` | SharedKernel.Caching.Redis | Modify | Remove `.WithSystemTextJsonSerializer()` call; add `.WithRegisteredSerializer()` if required |
| New test class or method | SharedKernel.Caching.FusionCache.Tests | Create/Modify | Regression: `SerializerContext` survives `AddRedisL2` call |

### Ph18 — Acceptance Criteria

- [ ] `AddRedisL2` no longer calls `.WithSystemTextJsonSerializer()` in any form
- [ ] `AddRedisL2` calls `.WithRegisteredDistributedCache()` and `.WithStackExchangeRedisBackplane()` as before; calls `.WithRegisteredSerializer()` if required by FusionCache 2.6.0
- [ ] After `AddSharedKernelCaching(o => o.SerializerContext = ctx)` + `AddRedisL2(...)`, the DI-resolved `IFusionCacheSerializer` includes `ctx` in its type info resolver chain
- [ ] The regression test covers both the no-Brotli and Brotli-wrapped serializer cases
- [ ] All 91 FusionCache + 74 Redis tests pass — zero regressions

### Ph18 — Dependencies

- Requires prior incomplete phase: None (independent fix)
- Unblocks: NativeAOT-safe L2 caching for all microservices; any phase that involves L2 serialization guarantees

### Ph18 — Version Pins

- ZiggyCreatures.FusionCache: 2.6.0 (unchanged)
- ZiggyCreatures.FusionCache.Serialization.SystemTextJson: 2.6.0 (unchanged)
- StackExchange.Redis: 2.13.1 (unchanged)
- .NET: `net10.0`

---

## Phase: DiErgonomics <!-- phase-key: SK.02.DiErgonomics -->

> Harden DI registration ergonomics: move `AddRedisDistributedLocking` to `ICachingBuilder`, add startup guards to `AddRedisChannelService` and `AddCacheInvalidationReceiver`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DE-01 | Add `AddRedisDistributedLocking(this ICachingBuilder builder, string connectionString, ...)` extension on `ICachingBuilder` in `RedisDistributedLockingExtensions.cs` | SharedKernel.Caching.Redis | `●` |
| DE-02 | Mark the existing `AddRedisDistributedLocking(this IServiceCollection services, ...)` overload `[Obsolete]`; have it delegate to the new `ICachingBuilder` extension to avoid a hard break for existing callers | SharedKernel.Caching.Redis | `●` |
| DE-03 | Add `IConnectionMultiplexer` registration guard to `AddRedisChannelService` — throw `InvalidOperationException` with message `"AddRedisChannelService requires AddRedisL2 or AddRedisDistributedLocking to be called first to register IConnectionMultiplexer."` if multiplexer not registered | SharedKernel.Caching.Redis | `●` |
| DE-04 | Add `IRedisChannelService` and `ICacheService` registration guards to `AddCacheInvalidationReceiver` — throw `InvalidOperationException` with clear message if either is absent | SharedKernel.Caching.Redis | `●` |
| DE-05 | Unit tests: each guard path throws the correct `InvalidOperationException` with the expected message; `[Obsolete]` overload still delegates correctly | SharedKernel.Caching.Redis.Tests | `●` |
| DE-06 | Verify all 91 FusionCache + 74 Redis tests pass — zero regressions | Both | `●` |

---

### Ph19 — Goal

Eliminate Day-1 DI configuration errors by surfacing them at startup rather than at the first request. Three defects are addressed in one cohesive phase: (1) `AddRedisDistributedLocking` currently extends `IServiceCollection` rather than `ICachingBuilder`, breaking the fluent chain; (2) `AddRedisChannelService` does not guard that `IConnectionMultiplexer` is already registered; (3) `AddCacheInvalidationReceiver` does not guard that its two hard dependencies (`IRedisChannelService`, `ICacheService`) are registered. All three fixes are additive or shim-compatible — no behavioral changes to registered services, no interface changes.

### Ph19 — Scope

- **Package(s) affected:** `SharedKernel.Caching.Redis` (modify two DI extension files)
- **New files:** None
- **Modified files:**
  - `Extensions/RedisDistributedLockingExtensions.cs` — new `ICachingBuilder` overload; `[Obsolete]` shim on existing `IServiceCollection` overload
  - `Extensions/RedisChannelServiceExtensions.cs` — add multiplexer guard
  - `Extensions/CacheInvalidationExtensions.cs` — add `IRedisChannelService` + `ICacheService` guards to `AddCacheInvalidationReceiver`
- **Deleted files:** None

### Ph19 — Implementation Rules

1. `AddRedisDistributedLocking(this ICachingBuilder builder, string connectionString, ...)` is the new canonical overload. Implementation is identical to the existing one — it registers `IConnectionMultiplexer` (via `TryAddSingleton`) and `IDistributedLockService`. The `ICachingBuilder` overload returns `ICachingBuilder` to maintain the fluent chain.
2. The `[Obsolete]` shim on `IServiceCollection` must carry the message: `"Use AddRedisDistributedLocking on ICachingBuilder instead. This overload will be removed in a future version."` and `error: false` (warning only, not compile error).
3. The `IServiceCollection` shim delegates to the `ICachingBuilder` extension by constructing a temporary `CachingBuilder` wrapper: `return new CachingBuilder(services).AddRedisDistributedLocking(connectionString, ...).Services;`. `CachingBuilder` is the internal implementation of `ICachingBuilder` — if it is `internal`, the shim must be in the same assembly.
4. `AddRedisChannelService` guard: check `builder.Services.Any(sd => sd.ServiceType == typeof(IConnectionMultiplexer))`. Throw before registering `RedisChannelService`.
5. `AddCacheInvalidationReceiver` guards: check for `IRedisChannelService` and `ICacheService` separately. Throw separate exceptions with distinct messages if either is missing:
   - `"AddCacheInvalidationReceiver requires AddRedisChannelService to be called first."` if `IRedisChannelService` absent.
   - `"AddCacheInvalidationReceiver requires AddSharedKernelCaching to be called first to register ICacheService."` if `ICacheService` absent.
6. `AddRedisCacheInvalidationBus` already guards for `IRedisChannelService` (Phase 12 rule 11) — do not duplicate this guard. Only `AddCacheInvalidationReceiver` needs the new guards.
7. Guard checks use `services.Any(...)` — read the registration state at extension call time. This is the same pattern as `AddRedisHashService`.
8. All XML doc on the new `ICachingBuilder` overload states the preferred calling pattern showing the full fluent chain.

### Ph19 — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Extensions/RedisDistributedLockingExtensions.cs` | SharedKernel.Caching.Redis | Modify | Add `ICachingBuilder` overload; mark `IServiceCollection` overload `[Obsolete]` |
| `Extensions/RedisChannelServiceExtensions.cs` | SharedKernel.Caching.Redis | Modify | Add `IConnectionMultiplexer` guard |
| `Extensions/CacheInvalidationExtensions.cs` | SharedKernel.Caching.Redis | Modify | Add `IRedisChannelService` + `ICacheService` guards to `AddCacheInvalidationReceiver` |
| New/existing test class | SharedKernel.Caching.Redis.Tests | Create/Modify | Guard path unit tests; obsolete shim delegation test |

### Ph19 — Acceptance Criteria

- [ ] `AddRedisDistributedLocking(this ICachingBuilder builder, ...)` extension exists; returns `ICachingBuilder` for fluent chaining
- [ ] The old `IServiceCollection` overload is `[Obsolete]` with warning message and delegates to the new one
- [ ] `AddRedisChannelService` throws `InvalidOperationException` with the correct message when `IConnectionMultiplexer` is not registered
- [ ] `AddCacheInvalidationReceiver` throws `InvalidOperationException` with correct messages when `IRedisChannelService` or `ICacheService` is absent
- [ ] Unit tests cover all three guard paths and the obsolete shim delegation
- [ ] All 91 FusionCache + 74 Redis tests pass — zero regressions
- [ ] Full fluent chain `services.AddSharedKernelCaching(...).AddRedisL2(...).AddRedisDistributedLocking(...)` compiles and registers correctly

### Ph19 — Dependencies

- Requires Phase 17 (SK.02.LayeringFix) to be complete: Yes — after Phase 17 the Redis package no longer references FusionCache, so `ICachingBuilder` extensions in Redis must not accidentally re-introduce that dependency
- Unblocks: Clean fluent DI registration patterns for all downstream microservices

### Ph19 — Version Pins

- StackExchange.Redis: >= 2.13.1 (unchanged)
- RedLock.net: 2.3.2 (unchanged)
- .NET: `net10.0`

---

## Phase: L1SizeLimit <!-- phase-key: SK.02.L1SizeLimit -->

> Wire `CachingOptions.L1SizeLimit` into FusionCache's in-process MemoryCache so the option is no longer dead configuration; verify and document L2 key prefix behavior with a direct Redis key inspection test.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| SL-01 | Wire `CachingOptions.L1SizeLimit` into FusionCache's `MemoryCacheOptions.SizeLimit` in `AddSharedKernelCaching` | SharedKernel.Caching.FusionCache | `●` |
| SL-02 | Ensure entry `Size = 1` is set in `FusionCacheEntryOptions` defaults so every L1 entry counts against `SizeLimit` — document that `L1SizeLimit` is a count (entries), not bytes | SharedKernel.Caching.FusionCache | `●` |
| SL-03 | Unit test: set `L1SizeLimit = 100`, insert 101 unique entries, verify the cache size does not exceed 101 and that a `GetAsync` for the overflow entry returns null or evicts the oldest | SharedKernel.Caching.FusionCache.Tests | `●` |
| SL-04 | Integration test: set `KeyPrefix = "myservice"`, write a value through `ICacheService.SetAsync`, read the raw Redis key via `IConnectionMultiplexer.GetDatabase().KeysAsync("myservice*")` or `StringGetAsync` to confirm prefix is applied | SharedKernel.Caching.Redis.Tests | `●` |
| SL-05 | Update XML doc on `AddRedisL2` to state the effective L2 key format: `{KeyPrefix}{FusionCacheInstanceName}:{cache-name}:{user-key}` (verify actual format and correct if different) | SharedKernel.Caching.Redis | `●` |
| SL-06 | Fix L2 key prefix wiring if the integration test reveals the prefix is not applied or is double-applied | SharedKernel.Caching.Redis | `●` |
| SL-07 | Verify all 91 FusionCache + 74 Redis tests pass | Both | `●` |

---

### Ph20 — Goal

Two connected dead-configuration and correctness issues are addressed together. `CachingOptions.L1SizeLimit` is a visible option that services set expecting to bound their L1 memory footprint — but `AddSharedKernelCaching` never reads it, so it has zero effect. In K8s environments with tight pod memory limits, unbounded L1 caches cause OOMKilled events. `L1SizeLimit` must be wired to FusionCache's underlying `MemoryCacheOptions.SizeLimit` with entry sizes set to 1 (count-based). Separately, the L2 `KeyPrefix` passed through `RedisL2Options.KeyPrefix` to `StackExchange.Redis` `IDistributedCache` `InstanceName` has never been integration-tested — a key prefix mismatch between services sharing a Redis instance causes silent cross-service key collision on L2.

### Ph20 — Scope

- **Package(s) affected:** `SharedKernel.Caching.FusionCache` (L1SizeLimit wiring + entry size default); `SharedKernel.Caching.Redis` (XML doc update, possible prefix wiring fix)
- **New files:** None
- **Modified files:**
  - `CachingServiceCollectionExtensions.cs` — wire `L1SizeLimit` into `MemoryCacheOptions`
  - `FusionCacheService.cs` or entry options factory — set default entry `Size = 1`
  - `RedisServiceCollectionExtensions.cs` — update XML doc; fix prefix wiring if SL-06 triggered
- **Deleted files:** None

### Ph20 — Implementation Rules

1. `L1SizeLimit` is wired via FusionCache's `.WithOptions(o => o.SizeLimit = cachingOptions.L1SizeLimit)` on the `IFusionCacheBuilder`. Alternatively, if FusionCache configures the underlying `IMemoryCache` through a named options pattern, use `services.Configure<MemoryCacheOptions>(fusionCacheName, o => o.SizeLimit = ...)`. Verify the correct API in FusionCache 2.6.0.
2. For `SizeLimit` to take effect, every cache entry must have a `Size` set. FusionCache's default entry options must set `Size = 1`. If FusionCache provides `WithDefaultEntryOptions(o => o.Size = 1)`, use it. If not, the `FusionCacheService` implementation must set `Size = 1` in the `FusionCacheEntryOptions` object passed per operation. Document the chosen approach.
3. `L1SizeLimit` is a count (number of entries), not a byte limit. This must be stated explicitly in the XML doc on `CachingOptions.L1SizeLimit` and in the `AddSharedKernelCaching` XML doc.
4. The `L2SizeLimit` unit test must use `CachePolicy.Default` or `CachePolicy.For(TimeSpan, TimeSpan)` — not `NeverExpire` — so entries are eligible for eviction.
5. The L2 key prefix integration test must use `IConnectionMultiplexer` directly (not `IDistributedCache`) to inspect the raw Redis keyspace. Use `IDatabase.KeysAsync("myservice*")` or construct the expected full key and call `StringGetAsync`. The test must fail if no key with the expected prefix is found.
6. If the prefix integration test reveals double-prefixing (e.g., FusionCache also applying a prefix), the wiring fix must ensure only one prefix is applied. Document the final effective key format in XML doc and in `CLAUDE.md`.
7. No changes to any interface contract — this phase is implementation-only.

### Ph20 — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `CachingServiceCollectionExtensions.cs` | SharedKernel.Caching.FusionCache | Modify | Wire `L1SizeLimit` into FusionCache memory cache options |
| `FusionCacheService.cs` or entry options factory | SharedKernel.Caching.FusionCache | Modify | Set default entry `Size = 1` for count-based SizeLimit tracking |
| `RedisServiceCollectionExtensions.cs` | SharedKernel.Caching.Redis | Modify | Update XML doc on `AddRedisL2` with effective L2 key format; fix prefix if needed |
| New test method | SharedKernel.Caching.FusionCache.Tests | Create | L1SizeLimit eviction unit test |
| New test method | SharedKernel.Caching.Redis.Tests | Create | L2 key prefix inspection integration test |

### Ph20 — Acceptance Criteria

- [ ] `AddSharedKernelCaching` passes `CachingOptions.L1SizeLimit` to FusionCache's memory cache configuration
- [ ] FusionCache entry defaults set `Size = 1` so count-based eviction works
- [ ] `CachingOptions.L1SizeLimit` XML doc states it is an entry count limit, not bytes
- [ ] Unit test: `L1SizeLimit = 100` with 101 entries causes at least one eviction (cache does not exceed 101 entries)
- [ ] Integration test: key written through `ICacheService` with `KeyPrefix = "myservice"` produces a raw Redis key containing `"myservice"` prefix
- [ ] XML doc on `AddRedisL2` states the effective L2 key format explicitly
- [ ] All 91 FusionCache + 74 Redis tests pass

### Ph20 — Dependencies

- Requires prior incomplete phase: None (independent of Phases 17–19)
- Unblocks: Services that set `L1SizeLimit` for memory safety in K8s; services that rely on L2 key isolation via prefix

### Ph20 — Version Pins

- ZiggyCreatures.FusionCache: 2.6.0 (unchanged — verify `WithOptions` or named `MemoryCacheOptions` API)
- StackExchange.Redis: 2.13.1 (unchanged)
- .NET: `net10.0`

---

## Phase: ValueTaskFactory <!-- phase-key: SK.02.ValueTaskFactory -->

> Upgrade `ICacheService.GetOrSetAsync` factory delegate from `Task<T>` to `ValueTask<T>` for .NET 10 alignment; add a nullable overload for caching absent (null) results.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| VT-01 | Change `ICacheService.GetOrSetAsync<T>` factory parameter from `Func<CancellationToken, Task<T>>` to `Func<CancellationToken, ValueTask<T>>`; update XML doc with migration note | SharedKernel.Caching.Abstractions | `●` |
| VT-02 | Add nullable overload `GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T?>> factory, CachePolicy policy, CancellationToken ct) → ValueTask<T?>` to `ICacheService`; XML doc must explain the negative-result caching use case | SharedKernel.Caching.Abstractions | `●` |
| VT-03 | Update `FusionCacheService.GetOrSetAsync<T>` to adapt `ValueTask<T>` factory to FusionCache's `Task<T>` API via `async token => await factory(token)` | SharedKernel.Caching.FusionCache | `●` |
| VT-04 | Implement nullable overload in `FusionCacheService` using FusionCache's `MaybeValue<T>` support so null results are cached correctly — factory called once on cache miss, not on every request | SharedKernel.Caching.FusionCache | `●` |
| VT-05 | Update all existing tests in `SharedKernel.Caching.FusionCache.Tests` that use `Task<T>` factory delegates to `ValueTask<T>` | SharedKernel.Caching.FusionCache.Tests | `●` |
| VT-06 | New test: nullable factory returning null — verify factory is called once on first miss, not on second request (null is cached) | SharedKernel.Caching.FusionCache.Tests | `●` |
| VT-07 | New test: nullable factory returning a value — round-trip returns the value, factory called once | SharedKernel.Caching.FusionCache.Tests | `●` |
| VT-08 | Verify all 91 FusionCache + 74 Redis tests pass after migration | Both | `●` |

---

### Ph21 — Goal

Align `ICacheService.GetOrSetAsync` with the .NET 10 idiomatic async primitive. Factory callers whose data-access layers return `ValueTask<T>` currently must call `.AsTask()` (allocates a wrapper `Task`) or unwrap via `await` (allocates a state machine) on every cache miss. Since `GetOrSetAsync` is the primary cache API and the factory is invoked on every miss, this allocation is recurring and real. The fix changes the factory parameter to `Func<CancellationToken, ValueTask<T>>` and adapts it inside `FusionCacheService`. A companion nullable overload `GetOrSetAsync<T?>` fills the negative-result caching gap: services that need to cache "entity not found" results currently have no mechanism to do so, causing per-request factory stampedes for missing keys.

This is a **breaking change** to `ICacheService`. It is cheaper at v1.0.0 (before any services onboard at scale) than at any later version. All existing factory delegate callers must update `Task<T>` to `ValueTask<T>` — documented in XML.

### Ph21 — Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (interface change + new overload); `SharedKernel.Caching.FusionCache` (implementation update + nullable impl); `SharedKernel.Caching.FusionCache.Tests` (test migration + new tests)
- **New files:** None
- **Modified files:**
  - `ICacheService.cs` — factory delegate type change + nullable overload
  - `FusionCacheService.cs` — update existing method + add nullable method
  - Existing test files in `SharedKernel.Caching.FusionCache.Tests/` — `Task<T>` → `ValueTask<T>` in factory delegates
- **Deleted files:** None

### Ph21 — Implementation Rules

1. `ICacheService.GetOrSetAsync<T>` factory parameter changes from `Func<CancellationToken, Task<T>>` to `Func<CancellationToken, ValueTask<T>>`. Return type remains `ValueTask<T>` (non-nullable). **This is a breaking change** — state it in the XML doc migration note: "If you have an existing `Task<T>` factory, wrap it: `async ct => await existingFactory(ct)`."
2. The nullable overload signature: `ValueTask<T?> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T?>> factory, CachePolicy policy, CancellationToken ct)`. The return type is `ValueTask<T?>` (nullable T). XML doc must explain: this overload caches null as a valid result; use it when a missing entity should not cause repeated factory invocations.
3. In `FusionCacheService`, adapt `ValueTask<T>` factory to FusionCache's `Task<T>` factory API using: `async (ctx, token) => await factory(token)` (FusionCache 2.x factory receives `FusionCacheFactoryExecutionContext<T>` and `CancellationToken`). Verify the exact FusionCache 2.6.0 factory delegate signature.
4. For the nullable overload, use FusionCache's `MaybeValue<T>` support: the factory returns `MaybeValue<T>.None()` when the result is null, causing FusionCache to cache the absence. Verify `MaybeValue<T>` API in FusionCache 2.6.0.
5. The adapter `async ct => await factory(ct)` introduces one state machine allocation per cache miss. This is the correct trade-off: it occurs only on actual misses (not on every call) and eliminates the per-call `.AsTask()` allocation overhead for the common case.
6. `FusionCacheService` must not use `.AsTask()` anywhere — always use the `async/await` adapter pattern.
7. All existing test factory delegates using `Task.FromResult(...)` must change to `ValueTask.FromResult(...)`.
8. The negative-result test must use `GetOrSetAsync<string?>` with a factory that returns `ValueTask<string?>.FromResult((string?)null)`. After the first call, a second call must not invoke the factory again — verified by a counter or mock.
9. `CachePolicy.NeverExpire` must not be used for the nullable null-caching test — use a short TTL so test isolation is preserved.
10. No changes to `IRedisHashService`, `ITypedHashStore<T>`, `IDistributedLockService`, or any other interface.

### Ph21 — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `ICacheService.cs` | SharedKernel.Caching.Abstractions | Modify | Change factory type; add nullable overload; migration XML doc |
| `FusionCacheService.cs` | SharedKernel.Caching.FusionCache | Modify | Adapt `ValueTask<T>` factory; implement nullable overload via `MaybeValue<T>` |
| Existing test files | SharedKernel.Caching.FusionCache.Tests | Modify | Update `Task<T>` factories to `ValueTask<T>` |
| New test methods | SharedKernel.Caching.FusionCache.Tests | Create | Null cached correctly; value cached correctly; factory call count verified |

### Ph21 — Acceptance Criteria

- [ ] `ICacheService.GetOrSetAsync<T>` factory parameter is `Func<CancellationToken, ValueTask<T>>`
- [ ] XML doc migration note is present: explains how to migrate existing `Task<T>` factory callers
- [ ] Nullable overload `GetOrSetAsync<T?>(string, Func<CancellationToken, ValueTask<T?>>, CachePolicy, CancellationToken) → ValueTask<T?>` exists on `ICacheService`
- [ ] `FusionCacheService` adapts `ValueTask<T>` → FusionCache `Task<T>` via `async/await` (no `.AsTask()`)
- [ ] Nullable null result is cached: factory called once on first miss, not on second request
- [ ] All existing tests updated to `ValueTask<T>` factory delegates — no `Task.FromResult` in factory positions
- [ ] All 91 FusionCache + 74 Redis tests pass after migration

### Ph21 — Dependencies

- Requires prior incomplete phase: None (independent — but review after Phase 18 is complete since Phase 18 touches `FusionCacheService` serializer path)
- Unblocks: .NET 10 idiomatic async patterns for all downstream cache consumers; negative-result caching scenarios

### Ph21 — Version Pins

- ZiggyCreatures.FusionCache: 2.6.0 (verify `MaybeValue<T>` API, factory delegate signature)
- ZiggyCreatures.FusionCache.Serialization.SystemTextJson: 2.6.0 (unchanged)
- .NET: `net10.0`

---

## Phase: BatchOperations <!-- phase-key: SK.02.BatchOperations -->

> Extend `ICacheService` with `GetManyAsync` and `SetManyAsync` batch methods, with an optional Redis pipeline optimization internal helper in `SharedKernel.Caching.Redis`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| BA-01 | Add `GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct) → ValueTask<IReadOnlyDictionary<string, T?>>` to `ICacheService`; XML doc states per-key null-on-miss behavior and that every requested key has an entry in the returned dictionary | SharedKernel.Caching.Abstractions | `●` |
| BA-02 | Add `SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct) → ValueTask` to `ICacheService`; XML doc states single policy applies to all entries in the batch | SharedKernel.Caching.Abstractions | `●` |
| BA-03 | Implement `GetManyAsync<T>` in `FusionCacheService` via `TryGetAsync` loop; always returns a dictionary entry per input key (null entry on miss) | SharedKernel.Caching.FusionCache | `●` |
| BA-04 | Implement `SetManyAsync<T>` in `FusionCacheService` via `SetAsync` loop applying the supplied `CachePolicy` to each entry | SharedKernel.Caching.FusionCache | `●` |
| BA-05 | Create `IRedisL2BatchService` internal helper in `SharedKernel.Caching.Redis` that batches `IDatabase.StringGetAsync` calls into a single Redis pipeline round-trip for `GetManyAsync` L2 path | SharedKernel.Caching.Redis | `●` |
| BA-06 | Update `FakeCacheService` in `16.Testing` to implement `GetManyAsync` and `SetManyAsync` | SharedKernel.Testing | `●` |
| BA-07 | Unit tests: batch get with mixed hits/misses, batch set then get-many, empty key list returns empty dictionary | SharedKernel.Caching.FusionCache.Tests | `●` |
| BA-08 | Integration tests: `GetManyAsync` with L2 active uses a single Redis pipeline round-trip (verified via Redis command count) | SharedKernel.Caching.Redis.Tests | `●` |

---

### Ph21 (P-021) — Goal

Services that fetch lists of entities by ID call `GetAsync` in a loop today, each call triggering a separate L2 Redis round-trip. A 50-item fetch becomes 50 sequential or parallel Redis calls where a single Redis pipeline would suffice. Beyond latency, each loop call resets the FusionCache stampede-protection context. `GetManyAsync` and `SetManyAsync` provide a first-class batch path. The FusionCache implementation uses a loop (FusionCache has no native batch API) but the Redis L2 path uses an internal pipeline helper to collapse multiple L2 lookups into a single round-trip.

### Ph21 (P-021) — Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (two new methods); `SharedKernel.Caching.FusionCache` (implementations); `SharedKernel.Caching.Redis` (internal `IRedisL2BatchService` helper); `16.Testing` (`FakeCacheService` update)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Redis/RedisL2BatchService.cs` (internal)
- **Modified files:**
  - `ICacheService.cs` — two new batch method signatures
  - `FusionCacheService.cs` — implement `GetManyAsync`, `SetManyAsync`
  - `16.Testing/SharedKernel.Testing/FakeCacheService.cs` — implement new methods

### Ph21 (P-021) — Implementation Rules

1. `GetManyAsync<T>` signature: `ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct)`. Every input key must appear in the returned dictionary. Keys not present in cache map to `null`. An empty key enumerable returns an empty dictionary.
2. `SetManyAsync<T>` signature: `ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct)`. The same `CachePolicy` is applied to all entries — there is no per-key policy in a batch call. XML doc must state this constraint explicitly.
3. `FusionCacheService.GetManyAsync` iterates keys and calls `TryGetAsync` per key — there is no FusionCache native batch API and this is the correct approach given FusionCache's stampede-protection model per key.
4. `IRedisL2BatchService` is `internal` to `SharedKernel.Caching.Redis` — it must not be registered in DI as a public service and must not leak into `ICacheService` or any abstraction.
5. The Redis pipeline optimization is an implementation detail of `SharedKernel.Caching.Redis`; calling code using `ICacheService.GetManyAsync` is not aware of it. FusionCache wires the pipeline lookup through its L2 entry resolution path.
6. `FakeCacheService.GetManyAsync` must iterate the in-memory dictionary and return a result entry per input key (null on miss). `FakeCacheService.SetManyAsync` must apply the same logic as `SetAsync` per entry.
7. Both new methods must carry full XML doc: batch semantics, per-key null-on-miss, the single-policy constraint for `SetManyAsync`, and a note that the FusionCache implementation loops internally.
8. The interface addition is backward-compatible at the abstraction level; any other implementations (test doubles, mocks) must add the two methods.

### Ph21 (P-021) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `ICacheService.cs` | SharedKernel.Caching.Abstractions | Modify | Add `GetManyAsync<T>` and `SetManyAsync<T>` with XML doc |
| `FusionCacheService.cs` | SharedKernel.Caching.FusionCache | Modify | Implement both batch methods via per-key loop |
| `RedisL2BatchService.cs` | SharedKernel.Caching.Redis | Create | Internal pipeline helper batching `StringGetAsync` calls |
| `FakeCacheService.cs` | SharedKernel.Testing | Modify | Implement `GetManyAsync` and `SetManyAsync` on the test double |

### Ph21 (P-021) — Acceptance Criteria

- [ ] `ICacheService` gains `GetManyAsync<T>` and `SetManyAsync<T>` with the signatures above
- [ ] `FusionCacheService` implements both; `GetManyAsync` returns a dictionary with one entry per input key; missing keys map to `null`
- [ ] `SetManyAsync` applies a single `CachePolicy` to all entries
- [ ] Unit tests: batch get with mixed hits/misses, batch set then get-many, empty key list returns empty dictionary
- [ ] Integration tests: `GetManyAsync` with L2 active uses a single Redis pipeline round-trip
- [ ] `FakeCacheService` in `16.Testing` implements the new methods
- [ ] All existing tests continue to pass
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe

### Ph21 (P-021) — Dependencies

- Requires prior incomplete phase: None
- Unblocks: Any service building list-fetch patterns over `ICacheService`

### Ph21 (P-021) — Version Pins

- ZiggyCreatures.FusionCache: 2.6.0 (unchanged)
- StackExchange.Redis: 2.13.1 (unchanged)
- .NET: `net10.0`

---

## Phase: RenewableLock <!-- phase-key: SK.02.RenewableLock -->

> Add `IRenewableLock` interface and `AcquireRenewableAsync` to `IDistributedLockService` so long-running operations can extend a distributed lock without silent expiry.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| RL-01 | Add `IRenewableLock` interface to `SharedKernel.Caching.Abstractions`: `RenewAsync(CancellationToken ct) → ValueTask<bool>`, `IsAcquired` (bool), extends `IAsyncDisposable` | SharedKernel.Caching.Abstractions | `●` |
| RL-02 | Add `AcquireRenewableAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry, CancellationToken ct) → ValueTask<IRenewableLock?>` to `IDistributedLockService` | SharedKernel.Caching.Abstractions | `●` |
| RL-03 | Create `RedLockRenewableLock.cs` in `SharedKernel.Caching.Redis` implementing `IRenewableLock`; `RenewAsync` uses RedLock.net `ExtendAsync` if available or re-acquires before expiry; returns `false` without throwing when lock is lost | SharedKernel.Caching.Redis | `●` |
| RL-04 | Implement `AcquireRenewableAsync` on `RedLockDistributedLockService` returning a `RedLockRenewableLock` or `null` | SharedKernel.Caching.Redis | `●` |
| RL-05 | Add `KeepAliveAsync(IRenewableLock lock, TimeSpan renewalInterval, CancellationToken ct) → Task` static extension method in `SharedKernel.Caching.Redis`; loops calling `RenewAsync` until cancellation or `IsAcquired` is `false` | SharedKernel.Caching.Redis | `●` |
| RL-06 | Update `FakeDistributedLockService` in `16.Testing` to implement `AcquireRenewableAsync` with a fake `IRenewableLock` that tracks renewal call count | SharedKernel.Testing | `●` |
| RL-07 | Integration tests: renewal succeeds before expiry, renewal returns `false` after expiry, `KeepAliveAsync` prevents lock loss across a simulated slow operation | SharedKernel.Caching.Redis.Tests | `●` |

---

### Ph22 (P-022) — Goal

The current `IDistributedLockService.AcquireAsync` returns a fixed-expiry lock with no renewal mechanism. Long-running critical sections that exceed the initial TTL silently lose the lock — another node may acquire it, leading to concurrent execution. `IRenewableLock` standardizes lock heartbeat across all services, eliminating ad-hoc RedLock renewal code and the unsafe pattern of setting excessively long initial TTLs.

### Ph22 (P-022) — Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (two additions); `SharedKernel.Caching.Redis` (two new types + extension); `16.Testing` (`FakeDistributedLockService` update)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Abstractions/IRenewableLock.cs`
  - `02.Caching/SharedKernel.Caching.Redis/RedLockRenewableLock.cs`
  - `02.Caching/SharedKernel.Caching.Redis/Extensions/RenewableLockExtensions.cs`
- **Modified files:**
  - `IDistributedLockService.cs` — add `AcquireRenewableAsync`
  - `RedLockDistributedLockService.cs` — implement `AcquireRenewableAsync`
  - `16.Testing/SharedKernel.Testing/FakeDistributedLockService.cs` — implement new method

### Ph22 (P-022) — Implementation Rules

1. `IRenewableLock` extends `IAsyncDisposable`. `RenewAsync` returns `ValueTask<bool>`: `true` if renewal succeeded (lock still held), `false` if the lock was lost (expired before renewal, or Redis unavailable). `RenewAsync` must never throw for a lost lock — it absorbs failures and returns `false`.
2. `IsAcquired` is `bool` — `true` while the lock is currently held; transitions to `false` when the lock is released or lost. Must remain `false` after `DisposeAsync`.
3. `AcquireRenewableAsync` returns `null` if the lock could not be acquired within `wait`. This matches the semantics of `AcquireAsync` for consistency.
4. `RedLockRenewableLock` uses RedLock.net `ExtendAsync` if the version exposes it. If `ExtendAsync` is unavailable, implement renewal by re-acquiring the lock on the same resource before the current expiry; if re-acquisition fails, set `IsAcquired = false` and return `false` from `RenewAsync`.
5. `KeepAliveAsync` is a static extension method, not an instance method on `IRenewableLock`. It takes `IRenewableLock lock, TimeSpan renewalInterval, CancellationToken ct`. It loops: waits `renewalInterval`, calls `lock.RenewAsync(ct)`, exits the loop if renewal returns `false` or `ct` is cancelled. Returns a `Task` the caller can `await` to observe completion.
6. The Redis `csproj` must not gain any new NuGet references for this phase — RedLock.net is already referenced.
7. All public types carry XML doc comments. `KeepAliveAsync` XML doc must state the caller is responsible for cancellation; it does not own the `IRenewableLock`.
8. `FakeRenewableLock` in Testing tracks a `RenewalCount` property; `IsAcquired` defaults to `true` until disposed.

### Ph22 (P-022) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `IRenewableLock.cs` | SharedKernel.Caching.Abstractions | Create | Renewable lock contract: `RenewAsync`, `IsAcquired`, `IAsyncDisposable` |
| `IDistributedLockService.cs` | SharedKernel.Caching.Abstractions | Modify | Add `AcquireRenewableAsync` method |
| `RedLockRenewableLock.cs` | SharedKernel.Caching.Redis | Create | `IRenewableLock` implementation via RedLock.net |
| `RedLockDistributedLockService.cs` | SharedKernel.Caching.Redis | Modify | Implement `AcquireRenewableAsync` |
| `Extensions/RenewableLockExtensions.cs` | SharedKernel.Caching.Redis | Create | `KeepAliveAsync` static extension |
| `FakeDistributedLockService.cs` | SharedKernel.Testing | Modify | Implement `AcquireRenewableAsync` + `FakeRenewableLock` |

### Ph22 (P-022) — Acceptance Criteria

- [ ] `IRenewableLock` interface exists in `SharedKernel.Caching.Abstractions` with `RenewAsync`, `IsAcquired`, and `IAsyncDisposable`
- [ ] `IDistributedLockService` gains `AcquireRenewableAsync` returning `ValueTask<IRenewableLock?>`
- [ ] `RedLockRenewableLock` implements `IRenewableLock`; `RenewAsync` returns `false` without throwing when lock is lost
- [ ] `KeepAliveAsync` static extension method exists for background renewal
- [ ] `FakeDistributedLockService` implements `AcquireRenewableAsync` with a fake `IRenewableLock` that tracks renewal calls
- [ ] Integration tests: renewal succeeds before expiry, renewal returns `false` after expiry, `KeepAliveAsync` prevents lock loss
- [ ] All existing `AcquireAsync` tests continue to pass
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe; no new NuGet references

### Ph22 (P-022) — Dependencies

- Requires prior incomplete phase: None
- Unblocks: Any saga/workflow using distributed critical sections longer than the lock TTL

### Ph22 (P-022) — Version Pins

- RedLock.net: 2.3.2 (unchanged — verify `ExtendAsync` availability at implementation time)
- StackExchange.Redis: 2.13.1 (unchanged)
- .NET: `net10.0`

---

## Phase: SlidingExpiration <!-- phase-key: SK.02.SlidingExpiration -->

> Add `SlidingWindow` property and `CachePolicy.Sliding` factory method to `CachePolicy` for idle-expiry use cases; map to FusionCache mechanism in `BuildEntryOptions`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| SE-01 | Add `SlidingWindow` property (`TimeSpan?`, nullable, default `null`) to `CachePolicy` in `SharedKernel.Caching.Abstractions` | SharedKernel.Caching.Abstractions | `●` |
| SE-02 | Add `CachePolicy.Sliding(TimeSpan window)` factory method; returns a policy with `SlidingWindow = window` and absolute durations matching `CachePolicy.Default` | SharedKernel.Caching.Abstractions | `●` |
| SE-03 | Map `SlidingWindow` in `FusionCacheService.BuildEntryOptions`; use FusionCache sliding mechanism if available, otherwise approximate via short `L1Duration` + aggressive `EagerRefreshThreshold`; document approximation in XML doc if exact sliding is not available | SharedKernel.Caching.FusionCache | `●` |
| SE-04 | Add validation guard: `CachePolicy.NeverExpire` and `SlidingWindow` set together is invalid; throw `InvalidOperationException` in `BuildEntryOptions` or add a check in `CachePolicy` construction | SharedKernel.Caching.FusionCache | `●` |
| SE-05 | Unit tests: `Sliding` factory method properties correct, sliding-expired entry not returned after idle period, absolute max TTL still applies when both `L1Duration` and `SlidingWindow` are set | SharedKernel.Caching.FusionCache.Tests | `●` |
| SE-06 | All existing `CachePolicy` tests continue to pass | SharedKernel.Caching.FusionCache.Tests | `●` |

---

### Ph23 (P-023) — Goal

Shopping cart data, user sessions, and partial workflow state need idle-expiry: entries that expire only when genuinely not accessed, not after a fixed wall-clock duration. Teams currently model this as short absolute TTL with `SetAsync` on every access — expensive and inconsistent. `CachePolicy.Sliding` standardizes the pattern, prevents `NeverExpire` + sliding combination bugs, and lets the FusionCache implementation apply the most efficient available mechanism.

### Ph23 (P-023) — Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (two additions to `CachePolicy`); `SharedKernel.Caching.FusionCache` (`BuildEntryOptions` mapping + validation guard)
- **New files:** None
- **Modified files:**
  - `CachePolicy.cs` — `SlidingWindow` property, `Sliding` factory
  - `FusionCacheService.cs` — `BuildEntryOptions` mapping + guard

### Ph23 (P-023) — Implementation Rules

1. `CachePolicy.SlidingWindow` is `TimeSpan?` — nullable. Default is `null` (no sliding expiry). When null, behavior is unchanged (pure absolute TTL). This is a backward-compatible, purely additive change.
2. `CachePolicy.Sliding(TimeSpan window)` is a static factory method. It constructs a `CachePolicy` with `SlidingWindow = window` and absolute durations equal to `CachePolicy.Default.L1Duration` and `CachePolicy.Default.L2Duration`. The `SlidingWindow` acts as the idle TTL; the absolute durations act as the ceiling TTL.
3. The `CachePolicy` record must remain immutable — `SlidingWindow` is an `init`-only property consistent with the existing record shape.
4. The guard against `NeverExpire + SlidingWindow` combination: if both `L1Duration == TimeSpan.MaxValue` and `SlidingWindow != null`, throw `InvalidOperationException("CachePolicy.Sliding is incompatible with CachePolicy.NeverExpire.")` in `FusionCacheService.BuildEntryOptions`. Do not add the guard to `CachePolicy` construction in Abstractions — the abstraction package must remain zero-infrastructure and free of runtime validation logic beyond what the record compiler enforces.
5. FusionCache 2.6.0 mapping: FusionCache does not have a native "sliding expiry" concept for L2 Redis, but its `MemoryCache` L1 supports sliding via the `SlidingExpiration` property on `MemoryCacheEntryOptions`. Map `SlidingWindow` to L1's `SlidingExpiration` when set. For L2, the `L2Duration` acts as the absolute ceiling and L2 entries do not slide. Document this L1-only sliding behavior in the XML doc on `CachePolicy.Sliding`.
6. The `CachePolicy.WithTags` and other fluent methods must be compatible with `SlidingWindow` — chaining `CachePolicy.Sliding(window).WithTags("x")` must produce the correct combined policy.
7. All public types carry XML doc comments.

### Ph23 (P-023) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `CachePolicy.cs` | SharedKernel.Caching.Abstractions | Modify | Add `SlidingWindow` property + `Sliding` factory |
| `FusionCacheService.cs` | SharedKernel.Caching.FusionCache | Modify | Map `SlidingWindow` to L1 sliding; add NeverExpire+Sliding guard |

### Ph23 (P-023) — Acceptance Criteria

- [ ] `CachePolicy` gains `SlidingWindow` property (`TimeSpan?`, nullable)
- [ ] `CachePolicy.Sliding(TimeSpan window)` factory method exists
- [ ] `FusionCacheService.BuildEntryOptions` maps `SlidingWindow` to L1 `SlidingExpiration`; limitation (L2 does not slide) documented in XML
- [ ] Validation guard prevents `NeverExpire + SlidingWindow` combination
- [ ] Unit tests: `Sliding` factory correctness, idle expiry behavior, absolute ceiling behavior
- [ ] All existing `CachePolicy` tests pass
- [ ] All public types carry XML doc comments

### Ph23 (P-023) — Dependencies

- Requires prior incomplete phase: None
- Unblocks: Session-adjacent and cart data caching patterns

### Ph23 (P-023) — Version Pins

- ZiggyCreatures.FusionCache: 2.6.0 (L1 `SlidingExpiration` via MemoryCache — standard .NET `IMemoryCache` API)
- .NET: `net10.0`

---

## Phase: KeyVersioning <!-- phase-key: SK.02.KeyVersioning -->

> Add `KeyVersion` property and `WithVersion` fluent method to `CachePolicy`; update `ICacheKeyProvider.BuildKey` to append `:v{version}` suffix when version is non-zero.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| KV-01 | Add `KeyVersion` property (`int`, default `0`) to `CachePolicy` in `SharedKernel.Caching.Abstractions` | SharedKernel.Caching.Abstractions | `●` |
| KV-02 | Add `WithVersion(int version)` fluent method to `CachePolicy`; chains correctly alongside `WithTags` | SharedKernel.Caching.Abstractions | `●` |
| KV-03 | Update `ICacheKeyProvider.BuildKey` XML doc to state that callers should pass the `CachePolicy.KeyVersion` value when non-zero; format: `{service}:{entity}:{id}[:{extraSegment}...]:v{version}` | SharedKernel.Caching.Abstractions | `●` |
| KV-04 | Update `CacheKeyProvider` in `SharedKernel.Caching.FusionCache` to accept an `int version` parameter in `BuildKey` (or a `CachePolicy` overload); append `:v{version}` suffix when `version > 0` | SharedKernel.Caching.FusionCache | `●` |
| KV-05 | Update `ICacheKeyProvider` to add overload `BuildKey(string entity, string id, int version, params string[] extraSegments)` — `ICacheService` method signatures do NOT change | SharedKernel.Caching.Abstractions | `●` |
| KV-06 | Add XML doc to `CacheKeyProvider` describing the deployment workflow: increment version → deploy → old key expires via TTL → no cache flush required | SharedKernel.Caching.FusionCache | `●` |
| KV-07 | Unit tests: `BuildKey` with version 0 produces no suffix (backward-compatible), `BuildKey` with version 3 produces `:v3` suffix, `WithVersion(3).WithTags("x")` chains correctly | SharedKernel.Caching.FusionCache.Tests | `●` |
| KV-08 | All existing `CacheKeyProvider` and `CachePolicyTests` pass — no regressions | Both | `●` |

---

### Ph24 (P-024) — Goal

Silent deserialization failures after DTO schema changes cause post-deployment incidents. Teams flush Redis (unsafe — thundering herd) or add ad-hoc `_v2` key suffixes (inconsistent). `KeyVersion` makes schema evolution an explicit, zero-downtime, zero-flush operation: increment the version alongside the DTO change, deploy, and old entries expire naturally via TTL.

### Ph24 (P-024) — Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (`CachePolicy` + `ICacheKeyProvider`); `SharedKernel.Caching.FusionCache` (`CacheKeyProvider` implementation)
- **New files:** None
- **Modified files:**
  - `CachePolicy.cs` — `KeyVersion` property + `WithVersion` method
  - `ICacheKeyProvider.cs` — new `BuildKey` overload with version parameter
  - `CacheKeyProvider.cs` — version suffix implementation

### Ph24 (P-024) — Implementation Rules

1. `CachePolicy.KeyVersion` is `int`, default `0`. Version `0` means no suffix — identical key format to today. This is backward-compatible: callers who do not call `WithVersion` are unaffected.
2. `WithVersion(int version)` is a fluent method on `CachePolicy` returning a new `CachePolicy` instance with `KeyVersion = version`. It chains with `WithTags`, `WithEagerRefresh`, and `SlidingWindow`.
3. `ICacheKeyProvider` gains an overload: `BuildKey(string entity, string id, int version, params string[] extraSegments)`. The existing `BuildKey(string entity, string id, params string[] extraSegments)` signature is unchanged and defaults to version `0` behavior (no suffix). The interface must not add a default method implementation — provide the overload as a new member.
4. `CacheKeyProvider.BuildKey` with version: format is `{service}:{entity}:{id}[:{extraSegment}...]:v{version}` when `version > 0`. When `version == 0`, the output is identical to today's format (no `:v0` suffix appended).
5. `ICacheService.GetAsync`, `SetAsync`, `GetOrSetAsync`, `RemoveAsync` signatures do NOT change. Key versioning is the caller's responsibility via `ICacheKeyProvider.BuildKey` — the version is baked into the key string passed to these methods, not managed by `ICacheService` itself.
6. The new `ICacheKeyProvider` overload is placed in the same interface file. Since `SharedKernel.Caching.Abstractions` has zero infrastructure dependencies, no default implementation is possible — `CacheKeyProvider` in FusionCache must implement both overloads.
7. All public types carry XML doc comments. The `BuildKey` version overload XML doc must describe the deployment workflow explicitly.

### Ph24 (P-024) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `CachePolicy.cs` | SharedKernel.Caching.Abstractions | Modify | Add `KeyVersion` int property + `WithVersion(int)` fluent method |
| `ICacheKeyProvider.cs` | SharedKernel.Caching.Abstractions | Modify | Add `BuildKey` overload with `int version` parameter |
| `CacheKeyProvider.cs` | SharedKernel.Caching.FusionCache | Modify | Implement version-suffix logic; `v{version}` appended when `version > 0` |

### Ph24 (P-024) — Acceptance Criteria

- [ ] `CachePolicy.KeyVersion` property (`int`, default `0`) and `WithVersion(int)` fluent method exist
- [ ] `ICacheKeyProvider` has `BuildKey` overload accepting `int version`
- [ ] `CacheKeyProvider` appends `:v{version}` when `version > 0`; version `0` produces no change to today's format
- [ ] `ICacheService` method signatures unchanged
- [ ] Unit tests: version 0 format unchanged, version 3 produces `:v3`, `WithVersion` + `WithTags` chain correctly
- [ ] All existing tests pass
- [ ] XML doc on versioned `BuildKey` describes the deployment workflow
- [ ] All public types carry XML doc comments

### Ph24 (P-024) — Dependencies

- Requires prior incomplete phase: None
- Unblocks: Safe DTO schema evolution in cache-heavy services

### Ph24 (P-024) — Version Pins

- ZiggyCreatures.FusionCache: 2.6.0 (unchanged)
- .NET: `net10.0`

---

## Phase: ChannelReconnect <!-- phase-key: SK.02.ChannelReconnect -->

> Add reconnect resilience to `RedisChannelService`: resubscribe to all registered channels on `ConnectionRestored` event; expose `ConnectionHealthState` property.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| CR-01 | Subscribe `RedisChannelService` to `IConnectionMultiplexer.ConnectionRestored` event in constructor | SharedKernel.Caching.Redis | `●` |
| CR-02 | On `ConnectionRestored`, resubscribe all channels in the internal subscription registry atomically (lock registry during replay); exceptions during resubscription logged at `LogLevel.Error` and do not propagate | SharedKernel.Caching.Redis | `●` |
| CR-03 | Add `ConnectionHealthState` enum (`Connected`, `Reconnecting`, `Disconnected`) and property to `RedisChannelService` (and expose on `IRedisChannelService` interface); update the enum to `SharedKernel.Caching.Abstractions` | SharedKernel.Caching.Abstractions | `●` |
| CR-04 | Subscribe to `IConnectionMultiplexer.ConnectionFailed` to transition state to `Reconnecting` or `Disconnected`; `ConnectionRestored` transitions back to `Connected` | SharedKernel.Caching.Redis | `●` |
| CR-05 | Integration test: drop and restore Redis container connection; verify messages are delivered again after reconnect; verify resubscription count equals the pre-disconnect subscription count | SharedKernel.Caching.Redis.Tests | `●` |
| CR-06 | All existing `RedisChannelServiceIntegrationTests` continue to pass | SharedKernel.Caching.Redis.Tests | `●` |

---

### Ph25 (P-025) — Goal

Redis rolling upgrades, pod restarts, and network blips trigger connection drops. `CacheInvalidationReceiver` subscriptions managed through `IRedisChannelService` are silently lost — L1 cache becomes stale indefinitely with no indication. Reconnect-triggered resubscription is the standard Redis client pattern and makes the invalidation bus reliable across transient Redis interruptions.

### Ph25 (P-025) — Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (`ConnectionHealthState` enum + property on `IRedisChannelService`); `SharedKernel.Caching.Redis` (`RedisChannelService` reconnect logic)
- **New files:** None
- **Modified files:**
  - `IRedisChannelService.cs` — add `ConnectionHealthState ConnectionHealth { get; }`
  - `ConnectionHealthState.cs` (new file in Abstractions) — enum `Connected | Reconnecting | Disconnected`
  - `RedisChannelService.cs` — reconnect logic, `ConnectionHealthState` transitions

### Ph25 (P-025) — Implementation Rules

1. `RedisChannelService` constructor subscribes to both `IConnectionMultiplexer.ConnectionRestored` and `IConnectionMultiplexer.ConnectionFailed` using the StackExchange.Redis event API. These are thread-safe event registrations.
2. On `ConnectionRestored`: acquire a lock over the internal subscription registry (use `lock` statement or `SemaphoreSlim(1,1)` — consistent with existing registry locking if any); iterate all registered channel-handler pairs; call `ISubscriber.SubscribeAsync` for each; transition `ConnectionHealth` to `Connected`. Any exception per channel is caught, logged at `LogLevel.Error`, and does not abort remaining channels.
3. On `ConnectionFailed`: transition `ConnectionHealth` to `Reconnecting` if the multiplexer is attempting to reconnect, or `Disconnected` if not. Use `IConnectionMultiplexer.IsConnected` to determine final state after the event fires.
4. `ConnectionHealthState` enum is defined in `SharedKernel.Caching.Abstractions` (zero infrastructure deps — it is a plain enum). `IRedisChannelService` exposes `ConnectionHealthState ConnectionHealth { get; }`.
5. `RedisChannelService` implements `ConnectionHealth` as a non-locking volatile read on an `_connectionHealth` field. Writes use an `Interlocked.Exchange` equivalent or `volatile` field — AOT-safe.
6. The resubscription must replay the full registry atomically: hold the registry lock for the entire replay, not per-channel. This prevents a concurrent `SubscribeAsync` from adding a new channel to the registry mid-replay and being resubscribed prematurely with a stale handler.
7. All new public types carry XML doc comments. `IRedisChannelService.ConnectionHealth` XML doc states: "reflects the current StackExchange.Redis connection state; intended for health check consumption."

### Ph25 (P-025) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `ConnectionHealthState.cs` | SharedKernel.Caching.Abstractions | Create | `Connected`, `Reconnecting`, `Disconnected` enum |
| `IRedisChannelService.cs` | SharedKernel.Caching.Abstractions | Modify | Add `ConnectionHealth` property |
| `RedisChannelService.cs` | SharedKernel.Caching.Redis | Modify | Subscribe to multiplexer events; reconnect resubscription; health state transitions |

### Ph25 (P-025) — Acceptance Criteria

- [ ] `RedisChannelService` subscribes to `IConnectionMultiplexer.ConnectionRestored` on construction
- [ ] On connection restoration, all channels in the registry are resubscribed atomically; failures logged and do not propagate
- [ ] `ConnectionHealthState` enum exists in `SharedKernel.Caching.Abstractions`
- [ ] `IRedisChannelService.ConnectionHealth` property exposes the current state
- [ ] Integration test: drop and restore Redis; verify messages delivered after reconnect
- [ ] All existing `RedisChannelService` tests continue to pass
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe

### Ph25 (P-025) — Dependencies

- Requires prior incomplete phase: None
- Unblocks: Reliable `CacheInvalidationReceiver` across Redis outages; health check integration for `ConnectionHealth`

### Ph25 (P-025) — Version Pins

- StackExchange.Redis: 2.13.1 (unchanged — `ConnectionRestored`/`ConnectionFailed` events exist in this version)
- .NET: `net10.0`

---

## Phase: CachingCoreOptionsDi <!-- phase-key: SK.02.CachingCoreOptionsDi -->

> Add standalone `AddCachingCoreOptions` DI extension in `SharedKernel.Caching.Abstractions` so Redis-only consumers (background workers) can configure `CachingCoreOptions.ServiceName` without depending on `SharedKernel.Caching.FusionCache`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| CO-01 | Add `AddCachingCoreOptions(this IServiceCollection services, Action<CachingCoreOptions> configure) → IServiceCollection` extension in `SharedKernel.Caching.Abstractions` | SharedKernel.Caching.Abstractions | `●` |
| CO-02 | Verify `Microsoft.Extensions.Options` is available in `SharedKernel.Caching.Abstractions.csproj` (either direct or transitively via `Microsoft.Extensions.DependencyInjection.Abstractions`); add explicit reference only if required | SharedKernel.Caching.Abstractions | `●` |
| CO-03 | Update `AddRedisCacheInvalidationBus` to log `LogLevel.Warning` at startup if `CachingCoreOptions.ServiceName` equals the default `"app"` | SharedKernel.Caching.Redis | `●` |
| CO-04 | Integration test: `CachingCoreOptions.ServiceName` resolves correctly when set via `AddCachingCoreOptions` without `AddSharedKernelCaching` | SharedKernel.Caching.Redis.Tests | `●` |
| CO-05 | All existing tests continue to pass | Both | `●` |

---

### Ph26 (P-026) — Goal

A background worker using only Redis coordination (distributed locking, channel service, invalidation bus) cannot set `CachingCoreOptions.ServiceName` without a transitive dependency on `SharedKernel.Caching.FusionCache` (which registers the options via `AddSharedKernelCaching`). This violates the plug-in principle. `AddCachingCoreOptions` in Abstractions gives Redis-only consumers a clean, zero-infrastructure entry point.

### Ph26 (P-026) — Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (new extension); `SharedKernel.Caching.Redis` (startup warning)
- **New files:** None (extension added to existing or new `CachingCoreOptionsExtensions.cs` in Abstractions)
- **Modified files:**
  - New `Extensions/CachingCoreOptionsExtensions.cs` in `SharedKernel.Caching.Abstractions`
  - `Extensions/CacheInvalidationExtensions.cs` in `SharedKernel.Caching.Redis` — add startup warning

### Ph26 (P-026) — Implementation Rules

1. `AddCachingCoreOptions` extension is on `IServiceCollection` (not `ICachingBuilder`) — it is the entry point for services that do not call `AddSharedKernelCaching`. It uses `services.Configure<CachingCoreOptions>(configure)` which requires `Microsoft.Extensions.Options`.
2. The extension must have zero dependencies on FusionCache, StackExchange.Redis, or any provider package. It imports only `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Options` namespaces.
3. `SharedKernel.Caching.Abstractions.csproj` currently references only `Microsoft.Extensions.DependencyInjection.Abstractions`. `Microsoft.Extensions.Options` may already be available transitively — verify at implementation time. If not, add `Microsoft.Extensions.Options` as an explicit `PackageReference` at version `10.0.0` or the version floor dictated by `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.1`.
4. `AddSharedKernelCaching` (in FusionCache) must continue to register `CachingCoreOptions` — its behavior is unchanged. A service calling both `AddCachingCoreOptions` and `AddSharedKernelCaching` is not an error; `Configure<T>` is additive and both configurations apply (last-writer-wins for the same property).
5. The startup warning in `AddRedisCacheInvalidationBus`: after all DI registrations complete, use an `IStartupFilter` or inline check using `IServiceProvider.GetRequiredService<IOptions<CachingCoreOptions>>().Value.ServiceName == "app"`. Because DI registration happens at build time, not runtime, the warning must be emitted using `ILogger` resolution from a startup hook (e.g., register a `IHostedService` singleton that logs the warning on `StartAsync`), or document in XML doc that the check happens at startup. Prefer the simplest correct approach.
6. The warning message: `"CachingCoreOptions.ServiceName has not been configured (still set to default 'app'). All cache invalidation channels and distributed lock resources will use the 'app' namespace. Call AddCachingCoreOptions or AddSharedKernelCaching to set the service name."`.

### Ph26 (P-026) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Extensions/CachingCoreOptionsExtensions.cs` | SharedKernel.Caching.Abstractions | Create | `AddCachingCoreOptions` DI extension |
| `SharedKernel.Caching.Abstractions.csproj` | SharedKernel.Caching.Abstractions | Modify if needed | Add `Microsoft.Extensions.Options` if not transitively available |
| `Extensions/CacheInvalidationExtensions.cs` | SharedKernel.Caching.Redis | Modify | Add startup warning for default `ServiceName` |

### Ph26 (P-026) — Acceptance Criteria

- [ ] `AddCachingCoreOptions(this IServiceCollection, Action<CachingCoreOptions>)` exists in `SharedKernel.Caching.Abstractions`
- [ ] Extension has zero dependency on FusionCache, StackExchange.Redis, or any provider
- [ ] A service can call `services.AddCachingCoreOptions(o => o.ServiceName = "worker").AddRedisDistributedLocking(connStr)` with no other caching registration
- [ ] `AddRedisCacheInvalidationBus` logs `LogLevel.Warning` if `ServiceName` is still `"app"` at startup
- [ ] Integration test: `ServiceName` resolves correctly when set via `AddCachingCoreOptions` alone
- [ ] All existing tests continue to pass
- [ ] All public types carry XML doc comments

### Ph26 (P-026) — Dependencies

- Requires prior incomplete phase: None — `CachingCoreOptions` exists in Abstractions since Phase 17
- Unblocks: Redis-only consumer services (background workers using distributed locking or Pub/Sub without L1 FusionCache)

### Ph26 (P-026) — Version Pins

- Microsoft.Extensions.Options: 10.0.x (verify exact floor from DI.Abstractions transitive graph)
- .NET: `net10.0`

---

## Phase: CacheWarmup <!-- phase-key: SK.02.CacheWarmup -->

> Add `ICacheWarmupStrategy` contract in Abstractions and `CacheWarmupHostedService` runner in FusionCache to prime L1 before traffic arrives; integrate with host readiness.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| CW-01 | Add `ICacheWarmupStrategy` to `SharedKernel.Caching.Abstractions`: `Name` (string), `Order` (int), `WarmupAsync(ICacheService cache, CancellationToken ct)` | SharedKernel.Caching.Abstractions | `●` |
| CW-02 | Create `CacheWarmupHostedService` `BackgroundService` in `SharedKernel.Caching.FusionCache`: resolves all `ICacheWarmupStrategy` from DI, orders by `Order`, executes sequentially; logs start/completion/duration at `Information`; catches and logs per-strategy exceptions at `Error` without aborting | SharedKernel.Caching.FusionCache | `●` |
| CW-03 | Add `WaitForWarmup` opt-in to `CachingOptions` (bool, default `false`); when `true`, `CacheWarmupHostedService` integrates with `IHostedLifecycle.StartedAsync` to delay readiness signal until warmup completes | SharedKernel.Caching.FusionCache | `●` |
| CW-04 | Add `AddCacheWarmup<TStrategy>(this ICachingBuilder builder)` extension where `TStrategy : ICacheWarmupStrategy`; registers `TStrategy` as `ICacheWarmupStrategy` singleton and registers `CacheWarmupHostedService` once (idempotent — use `TryAddEnumerable`) | SharedKernel.Caching.FusionCache | `●` |
| CW-05 | Unit tests: multiple strategies execute in correct `Order`; failed strategy does not abort others; timing logged per strategy | SharedKernel.Caching.FusionCache.Tests | `●` |
| CW-06 | All public types carry XML doc comments | Both | `●` |

---

### Ph27 (P-027) — Goal

Every K8s pod deployment starts with a cold L1 cache. The first wave of requests hits the factory (database, downstream APIs) for every key — a thundering herd that overwhelms dependencies post-deployment. `ICacheWarmupStrategy` gives teams a consistent, testable, ordering-aware warmup pattern that integrates with Kubernetes readiness probes to ensure pods do not receive traffic until the cache is primed.

### Ph27 (P-027) — Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (new interface); `SharedKernel.Caching.FusionCache` (hosted service, DI extension, `CachingOptions` extension)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Abstractions/ICacheWarmupStrategy.cs`
  - `02.Caching/SharedKernel.Caching.FusionCache/CacheWarmupHostedService.cs`
  - `02.Caching/SharedKernel.Caching.FusionCache/Extensions/CacheWarmupExtensions.cs`
- **Modified files:**
  - `CachingOptions.cs` — add `WaitForWarmup` property

### Ph27 (P-027) — Implementation Rules

1. `ICacheWarmupStrategy` is in `SharedKernel.Caching.Abstractions`. It has exactly three members: `string Name { get; }`, `int Order { get; }`, `ValueTask WarmupAsync(ICacheService cache, CancellationToken ct)`. Using `ValueTask` (not `Task`) is consistent with the rest of the domain.
2. `CacheWarmupHostedService` is a `BackgroundService`. In `ExecuteAsync`, it resolves `IEnumerable<ICacheWarmupStrategy>` from `IServiceProvider`, orders by `Order` ascending, then loops executing each. Per-strategy: start an `ILogger.Information` log, execute `WarmupAsync`, log completion with elapsed ms; catch all exceptions, log at `Error` with strategy `Name`, and continue to the next strategy.
3. `WaitForWarmup = true` behavior: `CacheWarmupHostedService` uses `IHostApplicationLifetime.WaitForApplicationStarted` semantics. If `Microsoft.Extensions.Hosting.Abstractions` `IHostedLifecycle` is available in .NET 10, implement `IHostedLifecycle.StartedAsync` (called before readiness) instead of `IHostedService.StartAsync`. Document the exact API used at implementation time — verify .NET 10 `IHostedLifecycle` availability.
4. `AddCacheWarmup<TStrategy>` adds `TStrategy` to `IServiceCollection` as `ICacheWarmupStrategy` using `TryAddEnumerable` to support multiple registrations. It adds `CacheWarmupHostedService` using `services.AddHostedService<CacheWarmupHostedService>()` wrapped in a guard so the hosted service is only registered once even when `AddCacheWarmup` is called multiple times.
5. `AddCacheWarmup<TStrategy>` is on `ICachingBuilder` — consistent with all other extensions in this domain.
6. A failed warmup strategy must not crash the pod — the `BackgroundService` must catch `Exception`, log it, and proceed. This is critical for startup safety.
7. All public types carry XML doc comments.

### Ph27 (P-027) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `ICacheWarmupStrategy.cs` | SharedKernel.Caching.Abstractions | Create | Warmup contract: `Name`, `Order`, `WarmupAsync` |
| `CacheWarmupHostedService.cs` | SharedKernel.Caching.FusionCache | Create | Ordered execution runner; per-strategy error isolation; readiness integration |
| `Extensions/CacheWarmupExtensions.cs` | SharedKernel.Caching.FusionCache | Create | `AddCacheWarmup<TStrategy>` DI extension on `ICachingBuilder` |
| `CachingOptions.cs` | SharedKernel.Caching.FusionCache | Modify | Add `WaitForWarmup` bool property (default `false`) |

### Ph27 (P-027) — Acceptance Criteria

- [ ] `ICacheWarmupStrategy` interface exists in `SharedKernel.Caching.Abstractions`
- [ ] `CacheWarmupHostedService` executes strategies in `Order` order; failed strategy logged and does not abort others
- [ ] `AddCacheWarmup<TStrategy>` extension registers strategy + hosted service (idempotent)
- [ ] `WaitForWarmup = true` delays readiness until warmup completes
- [ ] Unit tests: ordering correct, failure isolation correct, timing logged
- [ ] All existing tests continue to pass
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe

### Ph27 (P-027) — Dependencies

- Requires prior incomplete phase: None
- Unblocks: K8s-safe rolling deployments with pre-traffic cache warming

### Ph27 (P-027) — Version Pins

- ZiggyCreatures.FusionCache: 2.6.0 (unchanged)
- Microsoft.Extensions.Hosting.Abstractions: included in `net10.0` target — no new NuGet ref
- .NET: `net10.0`

---

## Phase: TenantCacheKey <!-- phase-key: SK.02.TenantCacheKey -->

> Add `ITenantCacheKeyProvider` to Abstractions and `TenantCacheKeyProvider` implementation to FusionCache for per-tenant key namespacing in multi-tenant SaaS services.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| TK-01 | Add `ITenantCacheKeyProvider` to `SharedKernel.Caching.Abstractions`: extends `ICacheKeyProvider`; adds `BuildTenantKey(string tenantId, string entity, string id, params string[] extraSegments) → string`; key format: `{service}:{tenant}:{entity}:{id}[:{extra}...]` | SharedKernel.Caching.Abstractions | `●` |
| TK-02 | Create `TenantCacheKeyProvider.cs` in `SharedKernel.Caching.FusionCache` implementing `ITenantCacheKeyProvider`; takes `IOptions<CachingCoreOptions>` for service name | SharedKernel.Caching.FusionCache | `●` |
| TK-03 | Add `AddTenantCacheKeyProvider(this ICachingBuilder builder)` extension in FusionCache; registers `TenantCacheKeyProvider` as `ITenantCacheKeyProvider` singleton; does not replace the existing `ICacheKeyProvider` registration | SharedKernel.Caching.FusionCache | `●` |
| TK-04 | Verify `ITenantCacheKeyProvider` has zero dependency on `12.Security` or `IHttpContextAccessor`; `tenantId` is always an explicit parameter | SharedKernel.Caching.Abstractions | `●` |
| TK-05 | Add `FakeTenantCacheKeyProvider` to `16.Testing` or extend `AddFakeCachingServices()` to include the fake | SharedKernel.Testing | `●` |
| TK-06 | Unit tests: `BuildTenantKey` format correct, two different tenant IDs produce different keys for same entity+id, format consistent with `BuildKey` (same entity+id produces correct base format minus tenant segment) | SharedKernel.Caching.FusionCache.Tests | `●` |

---

### Ph28 (P-028) — Goal

SaaS services serving multiple tenants must namespace cache keys per tenant to prevent cross-tenant data leakage. Teams handle this manually with inconsistent key prefix patterns today. `ITenantCacheKeyProvider` standardizes the format, documents the contract, and makes the explicit `tenantId` parameter (not resolved from HTTP context) a first-class constraint — keeping the method pure and testable without request context.

### Ph28 (P-028) — Scope

- **Package(s) affected:** `SharedKernel.Caching.Abstractions` (new interface); `SharedKernel.Caching.FusionCache` (implementation + extension); `16.Testing` (fake)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Abstractions/ITenantCacheKeyProvider.cs`
  - `02.Caching/SharedKernel.Caching.FusionCache/TenantCacheKeyProvider.cs`
- **Modified files:**
  - FusionCache DI extensions file (or new `Extensions/TenantCacheKeyExtensions.cs`)
  - `16.Testing/SharedKernel.Testing/FakeTenantCacheKeyProvider.cs` (new)

### Ph28 (P-028) — Implementation Rules

1. `ITenantCacheKeyProvider` extends `ICacheKeyProvider` — it inherits `BuildKey`. It adds exactly one method: `string BuildTenantKey(string tenantId, string entity, string id, params string[] extraSegments)`. Format contract: `{service}:{tenant}:{entity}:{id}[:{extraSegment}...]`. XML doc must state the format contract explicitly and state that `tenantId` is always supplied by the caller — not resolved from ambient context.
2. `ITenantCacheKeyProvider` has zero dependency on `12.Security`, `IHttpContextAccessor`, or any HTTP or security abstraction. The Abstractions package must remain free of such references. This constraint is enforced by the package's zero-infrastructure-dependency rule.
3. `TenantCacheKeyProvider` in FusionCache takes `IOptions<CachingCoreOptions>` in its constructor (from Abstractions — no FusionCache-specific options needed for key construction). It derives service name from `CachingCoreOptions.ServiceName`.
4. `AddTenantCacheKeyProvider` registers `TenantCacheKeyProvider` as `ITenantCacheKeyProvider` singleton. It does NOT replace the existing `ICacheKeyProvider` singleton (which was registered by `AddSharedKernelCaching`). Both registrations coexist — callers inject `ITenantCacheKeyProvider` explicitly.
5. `FakeTenantCacheKeyProvider` in Testing: implements `ITenantCacheKeyProvider`; `BuildTenantKey` produces `"test-service:{tenant}:{entity}:{id}[:{extra}...]"`; `BuildKey` produces `"test-service:{entity}:{id}[:{extra}...]"` (consistent with `FakeCacheKeyProvider`).
6. The FusionCache package already depends on `CachingCoreOptions` (Abstractions) — `TenantCacheKeyProvider` leverages this existing dependency.

### Ph28 (P-028) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `ITenantCacheKeyProvider.cs` | SharedKernel.Caching.Abstractions | Create | Tenant key contract; format `{service}:{tenant}:{entity}:{id}` |
| `TenantCacheKeyProvider.cs` | SharedKernel.Caching.FusionCache | Create | Implementation using `CachingCoreOptions.ServiceName` |
| `Extensions/TenantCacheKeyExtensions.cs` | SharedKernel.Caching.FusionCache | Create | `AddTenantCacheKeyProvider` DI extension on `ICachingBuilder` |
| `FakeTenantCacheKeyProvider.cs` | SharedKernel.Testing | Create | Test double for `ITenantCacheKeyProvider` |

### Ph28 (P-028) — Acceptance Criteria

- [ ] `ITenantCacheKeyProvider` extends `ICacheKeyProvider`; `BuildTenantKey` format contract is `{service}:{tenant}:{entity}:{id}[:{extra}...]`
- [ ] `TenantCacheKeyProvider` implements `ITenantCacheKeyProvider` in FusionCache
- [ ] `AddTenantCacheKeyProvider` registers as singleton; does not replace `ICacheKeyProvider`
- [ ] Zero dependency on `12.Security` or `IHttpContextAccessor`
- [ ] Unit tests: format correctness, tenant isolation (different tenant IDs → different keys)
- [ ] `FakeTenantCacheKeyProvider` in `16.Testing`
- [ ] All public types carry XML doc comments

### Ph28 (P-028) — Dependencies

- Requires prior incomplete phase: None
- Unblocks: Multi-tenant SaaS cache key safety for all downstream services

### Ph28 (P-028) — Version Pins

- ZiggyCreatures.FusionCache: 2.6.0 (unchanged)
- .NET: `net10.0`

---

## Phase: RedisCircuitBreaker <!-- phase-key: SK.02.RedisCircuitBreaker -->

> Add opt-in Polly v8 circuit breaker to `AddRedisL2` so Redis connection failures short-circuit immediately to FusionCache fail-safe, eliminating timeout accumulation during outages.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| RCB-01 | Add `CircuitBreaker` nested options class to `RedisL2Options`: `Enabled` (bool, default `false`), `FailureThreshold` (int, default `5`), `SamplingDuration` (TimeSpan, default `10s`), `BreakDuration` (TimeSpan, default `30s`), `MinimumThroughput` (int, default `3`) | SharedKernel.Caching.Redis | `●` |
| RCB-02 | Add `Polly.Core` v8 `PackageReference` to `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | `●` |
| RCB-03 | When `CircuitBreaker.Enabled = true`, `AddRedisL2` registers a Polly v8 `ResiliencePipeline<RedisValue>` singleton wrapping Redis operations with a `CircuitBreakerStrategy`; pipeline is available from DI for `RedisChannelService` and `RedisHashService` | SharedKernel.Caching.Redis | `●` |
| RCB-04 | When the circuit is open, Redis operations short-circuit immediately (no timeout wait); FusionCache fail-safe serves L1 | SharedKernel.Caching.Redis | `●` |
| RCB-05 | Unit tests: circuit opens after `FailureThreshold` consecutive failures, open circuit short-circuits immediately, circuit closes after `BreakDuration` | SharedKernel.Caching.Redis.Tests | `●` |
| RCB-06 | `CircuitBreaker.Enabled = false` (the default) produces zero behavioral change — all existing tests pass | SharedKernel.Caching.Redis.Tests | `●` |

---

### Ph29 (P-029) — Goal

FusionCache's fail-safe correctly serves stale L1 data during Redis outages, but it does not prevent the latency overhead of repeated failed Redis connection attempts. A Polly v8 circuit breaker complements fail-safe: when Redis is clearly down, the circuit opens and L2 calls short-circuit to `null` (triggering fail-safe from L1 immediately, with zero Redis wait time). Pods continue serving at full speed from L1 while Redis recovers.

### Ph29 (P-029) — Scope

- **Package(s) affected:** `SharedKernel.Caching.Redis` (new options, Polly wiring)
- **New files:** None
- **Modified files:**
  - `RedisL2Options.cs` (or `RedisServiceCollectionExtensions.cs` if options are inline) — add `CircuitBreaker` nested class
  - `RedisServiceCollectionExtensions.cs` — conditional Polly pipeline registration in `AddRedisL2`
  - `SharedKernel.Caching.Redis.csproj` — add `Polly.Core` reference

### Ph29 (P-029) — Implementation Rules

1. `CircuitBreaker` is a nested class (not record) on `RedisL2Options`. All five properties are settable via the standard options configuration pattern. Default `Enabled = false` is the critical constraint — all existing behavior must be preserved when disabled.
2. `Polly.Core` v8 is the only new NuGet dependency. `Microsoft.Extensions.Http.Resilience` is NOT added — it is an HTTP-specific package. Use `Polly.Core` (version `>= 8.0.0`) directly with the `ResiliencePipelineBuilder` API.
3. The circuit breaker uses Polly v8's `AddCircuitBreaker(CircuitBreakerStrategyOptions)` on `ResiliencePipelineBuilder`. Map `RedisL2Options.CircuitBreaker.*` to the corresponding `CircuitBreakerStrategyOptions` properties.
4. The `ResiliencePipeline` is registered as a singleton `IServiceCollection.AddSingleton<ResiliencePipeline>`. It is not registered under a named key — only one circuit breaker pipeline exists per service. `RedisChannelService` and `RedisHashService` can inject it optionally via `IServiceProvider.GetService<ResiliencePipeline>()` — if not registered (disabled), they proceed without Polly.
5. FusionCache's Redis backplane does not directly use the Polly pipeline — FusionCache's own fail-safe handles L2 unavailability. The Polly pipeline wraps `IDatabase` operations in `RedisHashService` and `ISubscriber` operations in `RedisChannelService` only.
6. The pipeline is only registered when `CircuitBreaker.Enabled = true`. The `AddRedisL2` extension checks the option before registering anything Polly-related.
7. `Polly.Core` 8.x is AOT-compatible — verify this on the exact version chosen at implementation time. If AOT compatibility is not confirmed, do not add it — flag in the XML doc and plan a future phase.
8. All new public types carry XML doc comments.

### Ph29 (P-029) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `RedisL2Options.cs` | SharedKernel.Caching.Redis | Modify | Add `CircuitBreaker` nested options class |
| `RedisServiceCollectionExtensions.cs` | SharedKernel.Caching.Redis | Modify | Conditional Polly pipeline registration |
| `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | Modify | Add `Polly.Core` v8 package reference |

### Ph29 (P-029) — Acceptance Criteria

- [ ] `RedisL2Options` gains `CircuitBreaker` nested class with five properties
- [ ] `AddRedisL2` registers Polly `ResiliencePipeline` singleton when `CircuitBreaker.Enabled = true`
- [ ] Open circuit short-circuits immediately; FusionCache fail-safe takes over
- [ ] `Polly.Core` v8 added to `SharedKernel.Caching.Redis.csproj`
- [ ] Unit tests: opens after threshold, short-circuits on open, closes after break duration
- [ ] `Enabled = false` produces zero behavioral change — all existing tests pass
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe

### Ph29 (P-029) — Dependencies

- Requires prior incomplete phase: None
- Unblocks: Graceful-degradation patterns for high-traffic services; complements FusionCache fail-safe

### Ph29 (P-029) — Version Pins

- Polly.Core: >= 8.0.0 (new dependency — verify exact version and AOT status at implementation time)
- StackExchange.Redis: 2.13.1 (unchanged)
- ZiggyCreatures.FusionCache: 2.6.0 (unchanged)
- .NET: `net10.0`

---

## Phase: OtelMeters <!-- phase-key: SK.02.OtelMeters -->

> Add OTel `System.Diagnostics.Metrics` instruments to `FusionCacheService`: `cache.hits`, `cache.misses`, `cache.factory.duration`, `cache.errors`, `cache.evictions`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| OM-01 | Create a static `Meter` named `"SharedKernel.Caching"` (version `"1.0"`) as a static field in `FusionCacheService`; no new NuGet dependencies | SharedKernel.Caching.FusionCache | `●` |
| OM-02 | Add `Counter<long> _cacheHits` instrument; increment on every `GetAsync` or `GetOrSetAsync` that returns a cached value without invoking the factory; tags: `cache.key_prefix`, `cache.level` (`"l1"` or `"l2"`) | SharedKernel.Caching.FusionCache | `●` |
| OM-03 | Add `Counter<long> _cacheMisses` instrument; increment on every `GetAsync` returning `null` and on every `GetOrSetAsync` factory invocation; tags: `cache.key_prefix` | SharedKernel.Caching.FusionCache | `●` |
| OM-04 | Add `Histogram<double> _factoryDuration` instrument (milliseconds); records factory elapsed time on cache miss; tags: `cache.key_prefix` | SharedKernel.Caching.FusionCache | `●` |
| OM-05 | Add `Counter<long> _cacheErrors` instrument; increments on factory or `SetAsync` exceptions; tags: `cache.error_type` | SharedKernel.Caching.FusionCache | `●` |
| OM-06 | Add `Counter<long> _cacheEvictions` instrument; subscribe to FusionCache `Events.Memory.Eviction` event to increment; tags: `cache.eviction_reason` | SharedKernel.Caching.FusionCache | `●` |
| OM-07 | Use FusionCache events API (`IFusionCache.Events.Memory.Hit`, `Events.Memory.Miss`) where available to populate hits/misses — prefer events over call-site duplication | SharedKernel.Caching.FusionCache | `●` |
| OM-08 | Unit tests: verify each counter and histogram increments under the correct conditions using `MeterListener` | SharedKernel.Caching.FusionCache.Tests | `●` |
| OM-09 | All existing tests continue to pass — meter recording is additive | SharedKernel.Caching.FusionCache.Tests | `●` |

---

### Ph30 (P-030) — Goal

Cache efficiency is operationally critical — a hit rate drop from 95% to 70% means a 5x increase in database load — but without meters this is invisible until latency rises. `System.Diagnostics.Metrics` instruments (BCL, AOT-safe, no new NuGet) emit counters and histograms that wire directly into Prometheus/OTel exporters. SREs get `cache.hits / (cache.hits + cache.misses)` hit-rate out of the box; factory duration regressions and L1 eviction pressure become observable.

### Ph30 (P-030) — Scope

- **Package(s) affected:** `SharedKernel.Caching.FusionCache` only (BCL instruments, no new deps)
- **New files:** None
- **Modified files:**
  - `FusionCacheService.cs` — add `Meter`, five instruments, event subscriptions

### Ph30 (P-030) — Implementation Rules

1. `Meter` is created as `static readonly Meter _meter = new("SharedKernel.Caching", "1.0")`. Static `Meter` is AOT-safe and the BCL pattern — no DI registration required.
2. Instruments are static readonly fields on `FusionCacheService`. The `Meter` and instruments are initialized once; all threads share them (thread-safe by design in `System.Diagnostics.Metrics`).
3. Tag values for `cache.key_prefix`: extract the `{service}:{entity}` portion from the key string (split on `:`, take first two segments). Do not include the full key (contains `{id}` which is high-cardinality and would blow up Prometheus cardinality). This is a planning rule — implementers must follow it strictly.
4. Use FusionCache's `Events` API (`IFusionCache.Events.Memory.Hit`, `Events.Memory.Miss`, `Events.Memory.Eviction`) to subscribe to events in the constructor. If the events API does not provide the information needed (e.g., cache level), instrument call sites directly. Prefer event subscription to avoid duplicating call-site logic.
5. `cache.factory.duration` records milliseconds as `double`. Use `Stopwatch` (BCL, AOT-safe) to measure factory elapsed time. Do not use `DateTime.UtcNow` subtraction.
6. No new NuGet dependencies. `System.Diagnostics.Metrics` is in the BCL for `net10.0`. `System.Diagnostics.DiagnosticSource` (already referenced for OTel tracing in invalidation receiver) is not required for metrics — metrics is a separate BCL API.
7. The `Meter` name `"SharedKernel.Caching"` is consistent with the OTel `ActivitySource` name convention used by `CacheInvalidationReceiver` (`"SharedKernel.Caching.Invalidation"`). Both use the `SharedKernel.Caching.*` namespace.
8. Instruments impose negligible overhead when no listener is attached (BCL guarantee). Do not add `Enabled` guards around instrument creation or recording — the BCL handles the no-listener fast path.
9. All public types carry XML doc comments. The `Meter` field XML doc states: "Consumers attach a `MeterListener` or configure an OTel metrics exporter to receive these metrics."

### Ph30 (P-030) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `FusionCacheService.cs` | SharedKernel.Caching.FusionCache | Modify | Add static `Meter`, five instruments, FusionCache event subscriptions |

### Ph30 (P-030) — Acceptance Criteria

- [ ] `FusionCacheService` creates `Meter` named `"SharedKernel.Caching"` with version `"1.0"`
- [ ] `cache.hits`, `cache.misses`, `cache.factory.duration`, `cache.errors`, `cache.evictions` instruments exist with correct tag sets
- [ ] `cache.key_prefix` tag uses `{service}:{entity}` (not full high-cardinality key)
- [ ] FusionCache events API used where available for hit/miss/eviction
- [ ] Unit tests verify each counter and histogram via `MeterListener`
- [ ] All existing tests continue to pass
- [ ] No new NuGet dependencies
- [ ] All public types carry XML doc comments
- [ ] Package remains AOT-safe

### Ph30 (P-030) — Dependencies

- Requires prior incomplete phase: None
- Unblocks: Grafana/Prometheus cache efficiency dashboards; SRE observability on cache hit rate, factory duration, eviction pressure

### Ph30 (P-030) — Version Pins

- `System.Diagnostics.Metrics`: BCL in `net10.0` — no NuGet reference
- ZiggyCreatures.FusionCache: 2.6.0 (verify `Events.Memory.*` API at implementation time)
- .NET: `net10.0`

---

## Phase: RedisConnectionCore <!-- phase-key: SK.02.RedisConnectionCore -->

> Extract a new foundational `SharedKernel.Caching.Redis.Core` package owning `IConnectionMultiplexer` registration, `ConnectionHealthState` tracking, and a reusable Polly v8 `ResiliencePipeline` factory — the shared dependency root for L2, distributed locking, hash store, and pub/sub packages.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| RC-01 | Create `SharedKernel.Caching.Redis.Core.csproj` (`net10.0`; refs `SharedKernel.Caching.Abstractions`, `StackExchange.Redis`, `Polly.Core`, `Microsoft.Extensions.DependencyInjection.Abstractions`) | SharedKernel.Caching.Redis.Core | `●` |
| RC-02 | Create `RedisConnectionOptions.cs` — `ConnectionString`, `ConnectTimeoutMs` (default `5000`), validated via `[Required]`/`[Range]` | SharedKernel.Caching.Redis.Core | `●` |
| RC-03 | Create `RedisCircuitBreakerOptions.cs` — generalized from `RedisL2Options.CircuitBreakerOptions`: `Enabled`, `FailureThreshold`, `SamplingDuration`, `BreakDuration`, `MinimumThroughput` | SharedKernel.Caching.Redis.Core | `●` |
| RC-04 | Create `RedisConnectionHealthTracker.cs` — wraps `IConnectionMultiplexer.ConnectionRestored`/`ConnectionFailed`, exposes `ConnectionHealthState ConnectionHealth`, `internal` event handlers with `InternalsVisibleTo` for test simulation | SharedKernel.Caching.Redis.Core | `●` |
| RC-05 | Create `Extensions/RedisConnectionCoreExtensions.cs` — `AddRedisConnection(this IServiceCollection, string connectionString, Action<RedisConnectionOptions>? configure = null)`; `TryAddSingleton<IConnectionMultiplexer>` (first caller wins); `TryAddSingleton<RedisConnectionHealthTracker>` | SharedKernel.Caching.Redis.Core | `●` |
| RC-06 | Create `Extensions/RedisCircuitBreakerExtensions.cs` — `AddRedisCircuitBreaker(this IServiceCollection, Action<RedisCircuitBreakerOptions>? configure = null)`; registers `ResiliencePipeline` singleton only when `Enabled = true`, using the FailureRatio=1.0/MinimumThroughput pattern from Phase 30 | SharedKernel.Caching.Redis.Core | `●` |
| RC-07 | Register project in `Platform.SharedKernel.slnx` under `02.Caching` solution folder | Solution | `●` |
| RC-08 | Stub `SharedKernel.Caching.Redis.Core.Tests` nested test project (xUnit, references `16.Testing`) | SharedKernel.Caching.Redis.Core.Tests | `●` |
| RC-09 | Re-partition relevant subset of the existing 154 Redis tests: connection registration (`TryAddSingleton` first-caller-wins), `ConnectionHealthState` transitions, circuit breaker pipeline construction (enabled/disabled) | SharedKernel.Caching.Redis.Core.Tests | `●` |

---

### Ph32 (P-140) — Goal

`SharedKernel.Caching.Redis` today bundles four infrastructure roles — L2 cache backplane, distributed locking, hash storage, pub/sub — all silently sharing one `IConnectionMultiplexer` via `TryAddSingleton` ordering, plus duplicated/scoped Polly circuit breaker plumbing (`RedisL2Options.CircuitBreakerOptions`) and connection-health tracking embedded inside `RedisChannelService`. This phase extracts those three cross-cutting concerns — connection lifecycle, health monitoring, and resilience — into a new standalone `SharedKernel.Caching.Redis.Core` package. It becomes the dependency root for Phases 33–36 (L2 refactor, distributed locking, hash store, pub/sub extraction), each of which sources `IConnectionMultiplexer` and the optional `ResiliencePipeline` from this package instead of registering or assuming a multiplexer independently.

This package knows about Redis connections and resilience only — zero references to FusionCache, RedLock.net, or any capability-specific (hash/channel/lock) type.

### Ph32 (P-140) — Scope

- **Package(s) affected:** New `SharedKernel.Caching.Redis.Core` (create)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Redis.Core/SharedKernel.Caching.Redis.Core.csproj`
  - `02.Caching/SharedKernel.Caching.Redis.Core/RedisConnectionOptions.cs`
  - `02.Caching/SharedKernel.Caching.Redis.Core/RedisCircuitBreakerOptions.cs`
  - `02.Caching/SharedKernel.Caching.Redis.Core/RedisConnectionHealthTracker.cs`
  - `02.Caching/SharedKernel.Caching.Redis.Core/Extensions/RedisConnectionCoreExtensions.cs`
  - `02.Caching/SharedKernel.Caching.Redis.Core/Extensions/RedisCircuitBreakerExtensions.cs`
  - `02.Caching/SharedKernel.Caching.Redis.Core/SharedKernel.Caching.Redis.Core.Tests/` (nested test project)
- **Modified files:** `Platform.SharedKernel.slnx` (register new project under `02.Caching`)
- **Deleted files:** None in this phase — `SharedKernel.Caching.Redis` is not yet modified to consume this package; that begins in Phase 33. This phase only creates the new package and its own tests in isolation.

### Ph32 (P-140) — Implementation Rules

1. The `.csproj` references `SharedKernel.Caching.Abstractions` (for `ConnectionHealthState`, which already exists there per Phase 26 — do **not** redeclare it), `StackExchange.Redis` (>= 2.13.1), `Polly.Core` (8.5.2), and `Microsoft.Extensions.DependencyInjection.Abstractions` (10.0.1). No FusionCache packages, no RedLock.net, no `Microsoft.Extensions.Hosting.Abstractions`.
2. `ConnectionHealthState` enum remains defined in `SharedKernel.Caching.Abstractions` (Phase 26) — it is **not** duplicated here. `RedisConnectionHealthTracker` consumes the existing enum.
3. `AddRedisConnection(this IServiceCollection, string connectionString, Action<RedisConnectionOptions>? configure = null)` is the single DI entry point. It:
   - Parses `ConfigurationOptions` from `connectionString`, applies `ConnectTimeoutMs` and `AbortOnConnectFail = false`.
   - Calls `services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configOptions))` — preserves the existing "first caller wins" semantics verbatim from `AddRedisL2`/`AddRedisDistributedLocking`.
   - Calls `services.TryAddSingleton<RedisConnectionHealthTracker>()`.
4. `RedisConnectionHealthTracker` constructor takes `IConnectionMultiplexer` and `ILogger<RedisConnectionHealthTracker>`. It subscribes to `ConnectionRestored`/`ConnectionFailed` in its constructor (mirrors the Phase 26 `RedisChannelService` pattern: `volatile int _connectionHealth` cast to/from `ConnectionHealthState`, `internal` `OnConnectionRestored`/`OnConnectionFailed` handlers exposed via `InternalsVisibleTo` for test simulation). It exposes `ConnectionHealthState ConnectionHealth { get; }`.
5. `RedisConnectionHealthTracker` does **not** itself perform any resubscription/replay logic — that is specific to `RedisChannelService` (pub/sub) and remains in the Phase 36 package. This tracker is a passive health-state observer that any dependent package can inject for health-check reporting (e.g., via `IHealthCheck`).
6. `RedisCircuitBreakerOptions` is a direct generalization of `RedisL2Options.CircuitBreakerOptions` (Phase 30) — same five properties (`Enabled`, `FailureThreshold`, `SamplingDuration`, `BreakDuration`, `MinimumThroughput`), same defaults, same `[Range]` validation attributes. It is a top-level public class (not nested) so hash store, channel service, and distributed locking packages can each bind their own instance independently.
7. `AddRedisCircuitBreaker(this IServiceCollection, Action<RedisCircuitBreakerOptions>? configure = null)` registers `ResiliencePipeline` as singleton **only** when `Enabled = true`, using the exact FailureRatio=1.0/MinimumThroughput mapping pattern established in Phase 30 (`FailureRatio = 1.0`, `MinimumThroughput = FailureThreshold`, `BreakDuration` minimum 500ms enforced by Polly itself). This is registered via `TryAddSingleton` so a service that calls it multiple times (once per capability package, e.g., hash store and pub/sub each calling it defensively) does not throw or duplicate.
8. No service in this package depends on `ICachingBuilder` — `AddRedisConnection` and `AddRedisCircuitBreaker` are plain `IServiceCollection` extensions. `ICachingBuilder`-based fluent chaining is the responsibility of consuming packages (L2, locking, hash, pub/sub) which wrap these calls.
9. All public types carry XML doc comments. `RedisConnectionOptions` and `RedisCircuitBreakerOptions` XML docs must state they are the canonical shapes — Phases 33–36 must bind to these types, not redeclare local copies.
10. This package introduces **no breaking change** to `SharedKernel.Caching.Redis` in this phase — it exists standalone and is wired into the slimmed `SharedKernel.Caching.Redis` only in Phase 33.

### Ph32 (P-140) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `SharedKernel.Caching.Redis.Core.csproj` | SharedKernel.Caching.Redis.Core | Create | Project definition; `net10.0`; refs Abstractions + StackExchange.Redis + Polly.Core |
| `RedisConnectionOptions.cs` | SharedKernel.Caching.Redis.Core | Create | `ConnectionString`, `ConnectTimeoutMs` — canonical connection options shape |
| `RedisCircuitBreakerOptions.cs` | SharedKernel.Caching.Redis.Core | Create | Generalized circuit breaker options (from Ph.30 `RedisL2Options.CircuitBreakerOptions`) |
| `RedisConnectionHealthTracker.cs` | SharedKernel.Caching.Redis.Core | Create | `ConnectionHealthState` tracker; reconnect/failure event wiring (generalized from Ph.26 `RedisChannelService`) |
| `Extensions/RedisConnectionCoreExtensions.cs` | SharedKernel.Caching.Redis.Core | Create | `AddRedisConnection` — `TryAddSingleton<IConnectionMultiplexer>` + health tracker registration |
| `Extensions/RedisCircuitBreakerExtensions.cs` | SharedKernel.Caching.Redis.Core | Create | `AddRedisCircuitBreaker` — conditional `ResiliencePipeline` singleton |
| `SharedKernel.Caching.Redis.Core.Tests/` | SharedKernel.Caching.Redis.Core.Tests | Create | Nested xUnit test project |
| `Platform.SharedKernel.slnx` | Solution | Modify | Register `SharedKernel.Caching.Redis.Core` under `02.Caching` |

### Ph32 (P-140) — Acceptance Criteria

- [ ] New project `SharedKernel.Caching.Redis.Core` exists, targets `net10.0`, references only `SharedKernel.Caching.Abstractions`, `StackExchange.Redis`, `Polly.Core`, `Microsoft.Extensions.DependencyInjection.Abstractions`
- [ ] `AddRedisConnection` registers `IConnectionMultiplexer` via `TryAddSingleton` — first caller wins, identical semantics to current `AddRedisL2`/`AddRedisDistributedLocking`
- [ ] `RedisConnectionHealthTracker` exposes `ConnectionHealthState ConnectionHealth`; wired to `ConnectionRestored`/`ConnectionFailed`; `internal` handlers exposed via `InternalsVisibleTo`
- [ ] `ConnectionHealthState` enum is NOT duplicated — sourced from `SharedKernel.Caching.Abstractions` (Phase 26)
- [ ] `RedisCircuitBreakerOptions` matches the five-property shape of Phase 30's `RedisL2Options.CircuitBreakerOptions`
- [ ] `AddRedisCircuitBreaker` registers `ResiliencePipeline` singleton only when `Enabled = true`, using FailureRatio=1.0/MinimumThroughput pattern
- [ ] No FusionCache, RedLock.net, or capability-specific (hash/channel/lock) types present in this package
- [ ] Project registered in `Platform.SharedKernel.slnx` under `02.Caching`
- [ ] New tests cover: connection registration first-caller-wins, health state transitions (Connected/Reconnecting/Disconnected), circuit breaker pipeline construction (enabled returns non-null, disabled returns null)
- [ ] All public types carry XML doc comments
- [ ] `dotnet build` clean; no new compile warnings

### Ph32 (P-140) — Dependencies

- Requires prior incomplete phase: None — all phases 1–31 complete
- Unblocks: Phase 33 (L2 refactor), Phase 34 (distributed locking extraction), Phase 35 (hash store extraction), Phase 36 (pub/sub extraction) — all four source `IConnectionMultiplexer` and `ResiliencePipeline` from this package

### Ph32 (P-140) — Redis / FusionCache Version Pins

- StackExchange.Redis: >= 2.13.1
- Polly.Core: 8.5.2 (unchanged from Phase 30)
- ZiggyCreatures.FusionCache: N/A (no reference in this package)
- Microsoft.Extensions.DependencyInjection.Abstractions: 10.0.1
- .NET: `net10.0`

---

## Phase: RedisL2Refactor <!-- phase-key: SK.02.RedisL2Refactor -->

> Slim `SharedKernel.Caching.Redis` to contain only the FusionCache L2 distributed backplane (`AddRedisL2`, `RedisL2Options`, Brotli compression) — sourcing `IConnectionMultiplexer` and the circuit breaker pipeline from `SharedKernel.Caching.Redis.Core` (Phase 32) instead of self-registering.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| RL2-01 | Add `<ProjectReference>` to `SharedKernel.Caching.Redis.Core.csproj` | SharedKernel.Caching.Redis | `●` |
| RL2-02 | Update `AddRedisL2` to call `services.AddRedisConnection(connectionString, ...)` (Phase 32) instead of inline `TryAddSingleton<IConnectionMultiplexer>` + `ConnectionMultiplexer.Connect` | SharedKernel.Caching.Redis | `●` |
| RL2-03 | Update `AddRedisL2` to call `services.AddRedisCircuitBreaker(...)` (Phase 32) instead of the inline `RedisL2Options.CircuitBreakerOptions` Polly registration; remove `RedisL2Options.CircuitBreaker` nested class — replaced by `RedisCircuitBreakerOptions` from Core | SharedKernel.Caching.Redis | `●` |
| RL2-04 | Remove `Polly.Core` direct `PackageReference` from `SharedKernel.Caching.Redis.csproj` (now transitive via `SharedKernel.Caching.Redis.Core`) — verify transitive resolution is sufficient or keep as direct ref if `RedisHashService`/`RedisChannelService` types still compile in this assembly during transition | SharedKernel.Caching.Redis | `●` |
| RL2-05 | Verify `services.AddSharedKernelCaching(...).AddRedisL2(connectionString)` DI usage shape is unchanged for consumers — no signature changes to `AddRedisL2` or `RedisL2Options` (minus the removed `CircuitBreaker` nested class, replaced by a top-level `CircuitBreaker` property of type `RedisCircuitBreakerOptions` from Core for source-compat) | SharedKernel.Caching.Redis | `●` |
| RL2-06 | Confirm `RedisChannelService.cs`, `RedisHashService.cs`, `TypedHashStore.cs`, `RedisCacheInvalidationBus.cs`, `CacheInvalidationReceiver.cs`, `RedLockDistributedLockService.cs`, `RedLockRenewableLock.cs`, `Batch/*`, and their corresponding `Extensions/*` files remain present and compiling in this package for this phase (their relocation is Phases 34–36 — not removed yet) | SharedKernel.Caching.Redis | `●` |
| RL2-07 | Re-run full Redis test suite against the now-Core-backed `AddRedisL2`; verify zero regressions | SharedKernel.Caching.Redis.Tests | `●` |
| RL2-08 | Update `02.Caching/CLAUDE.md` package table: `SharedKernel.Caching.Redis` row updated to reference `SharedKernel.Caching.Redis.Core` as a dependency | SharedKernel.Caching.Redis | `●` |

---

### Ph33 (P-141) — Goal

This phase performs the minimal "wire the L2 path through Core" step: `AddRedisL2` stops self-registering `IConnectionMultiplexer` and the circuit breaker pipeline, sourcing both from `SharedKernel.Caching.Redis.Core` (Phase 32) instead. The public DI surface (`AddRedisL2(...)`, `AddBrotliCompression(...)`) remains functionally equivalent — `services.AddSharedKernelCaching(...).AddRedisL2(connectionString)` continues to work unchanged from the consumer's perspective.

**Important scope clarification:** this phase does **not** yet remove distributed locking, hash store, or pub/sub types from `SharedKernel.Caching.Redis` — that physical relocation happens in Phases 34, 35, and 36 respectively (each of which depends on Phase 32, not on this phase). This phase's job is narrowly: make `AddRedisL2` itself Core-backed, and prepare the package's dependency graph (add `ProjectReference` to Core) so that Phases 34–36 can each independently move their slice out without `AddRedisL2` regressing. After Phases 34–36 land, `SharedKernel.Caching.Redis` will contain only the L2 backplane — but that end-state is reached cumulatively, not solely by this phase.

### Ph33 (P-141) — Scope

- **Package(s) affected:** `SharedKernel.Caching.Redis` (modify)
- **New files:** None
- **Modified files:**
  - `SharedKernel.Caching.Redis.csproj` — add `<ProjectReference>` to `SharedKernel.Caching.Redis.Core.csproj`; re-evaluate `Polly.Core` direct reference
  - `Extensions/RedisServiceCollectionExtensions.cs` — `AddRedisL2` sources `IConnectionMultiplexer` and `ResiliencePipeline` from Core extensions
  - `Extensions/RedisL2Options.cs` — `CircuitBreaker` nested class replaced by a top-level `RedisCircuitBreakerOptions` (from Core) property; `RedisL2Options` itself retains `ConnectionString`, `KeyPrefix`, `ConnectTimeoutMs`
  - `02.Caching/CLAUDE.md` — package table updated
- **Deleted files:** None in this phase

### Ph33 (P-141) — Implementation Rules

1. `AddRedisL2` calls `services.AddRedisConnection(connectionString, o => { o.ConnectTimeoutMs = options.ConnectTimeoutMs; })` (Phase 32) in place of the inline `ConfigurationOptions.Parse` + `TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(...))` block. The resulting `IConnectionMultiplexer` registration is identical in shape (`TryAddSingleton`, first caller wins) — only the registration call site moves.
2. `AddRedisL2` calls `services.AddRedisCircuitBreaker(o => { /* map from RedisL2Options.CircuitBreaker */ })` (Phase 32) in place of the inline `ResiliencePipelineBuilder().AddCircuitBreaker(...)` registration. The `FailureRatio = 1.0` / `MinimumThroughput = FailureThreshold` mapping logic moves to Core verbatim — `AddRedisL2` only forwards configuration values.
3. `RedisL2Options.CircuitBreaker` changes type from the locally-nested `RedisL2Options.CircuitBreakerOptions` class to `SharedKernel.Caching.Redis.Core.RedisCircuitBreakerOptions` (a top-level class). The property name `CircuitBreaker` and all five sub-property names (`Enabled`, `FailureThreshold`, `SamplingDuration`, `BreakDuration`, `MinimumThroughput`) are preserved exactly — this is a type-relocation, not a rename, so `options.CircuitBreaker.Enabled = true` continues to compile unchanged for consumers who configure `AddRedisL2(connectionString, o => o.CircuitBreaker.Enabled = true)`.
4. `services.AddStackExchangeRedisCache(...)` and the `AddFusionCache().WithRegisteredDistributedCache().WithRegisteredSerializer().WithStackExchangeRedisBackplane(...)` chain are **unchanged** — these are the genuinely L2-specific FusionCache wiring calls and remain in `AddRedisL2` verbatim.
5. The Phase 18 AOT rule is unaffected: `AddRedisL2` continues to use `.WithRegisteredSerializer()`, never `.WithSystemTextJsonSerializer()`.
6. Brotli compression (`AddBrotliCompression`, `BrotliCacheSerializer`) lives in `SharedKernel.Caching.FusionCache` (per Phase 16/B-01..B-05) — it is **not** moved by this phase. The acceptance criterion "Brotli compression rules continue to function" refers to the existing cross-package wiring (`AddRedisL2` + `AddBrotliCompression` chain) continuing to work after `AddRedisL2`'s internals change.
7. If `Polly.Core` is no longer directly used by any type compiled into `SharedKernel.Caching.Redis.csproj` after this phase (i.e., `AddRedisL2` no longer references `Polly.CircuitBreaker` types directly because `AddRedisCircuitBreaker` encapsulates them), remove the direct `PackageReference Include="Polly.Core"` — it remains available transitively via `SharedKernel.Caching.Redis.Core`. However, `RedisHashService` and `RedisChannelService` (still in this package until Phases 35/36) reference `Polly.ResiliencePipeline` directly via constructor injection — **keep** the direct `PackageReference` until those types are relocated. Verify the actual compile dependency before removing; do not remove speculatively.
8. All public types carry XML doc comments. `RedisL2Options.CircuitBreaker` XML doc must note the type now originates from `SharedKernel.Caching.Redis.Core`.

### Ph33 (P-141) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | Modify | Add `ProjectReference` to `SharedKernel.Caching.Redis.Core`; re-evaluate `Polly.Core` direct ref |
| `Extensions/RedisServiceCollectionExtensions.cs` | SharedKernel.Caching.Redis | Modify | `AddRedisL2` sources multiplexer + circuit breaker pipeline from Core |
| `Extensions/RedisL2Options.cs` | SharedKernel.Caching.Redis | Modify | `CircuitBreaker` property retyped to `RedisCircuitBreakerOptions` (Core); nested `CircuitBreakerOptions` class removed |
| `02.Caching/CLAUDE.md` | — | Modify | Package table: `SharedKernel.Caching.Redis` now depends on `SharedKernel.Caching.Redis.Core` |

### Ph33 (P-141) — Acceptance Criteria

- [ ] `SharedKernel.Caching.Redis.csproj` references `SharedKernel.Caching.Redis.Core`
- [ ] `AddRedisL2` sources `IConnectionMultiplexer` via `services.AddRedisConnection(...)` (Phase 32) — no duplicate inline registration
- [ ] `AddRedisL2` sources the circuit breaker `ResiliencePipeline` via `services.AddRedisCircuitBreaker(...)` (Phase 32) — `RedisL2Options.CircuitBreaker` is `RedisCircuitBreakerOptions` from Core
- [ ] `services.AddSharedKernelCaching(...).AddRedisL2(connectionString)` DI usage shape unchanged — including `o => o.CircuitBreaker.Enabled = true` configuration syntax
- [ ] Brotli compression chain (`AddRedisL2(...).AddBrotliCompression(...)`) continues to function — covered by existing tests
- [ ] No duplicate `IConnectionMultiplexer` registration when both `AddRedisL2` and Core's `AddRedisConnection` are reachable in the same container
- [ ] Distributed locking, hash store, and pub/sub types remain present in this package for this phase (relocation deferred to Phases 34–36)
- [ ] Relevant subset of the 154 Redis tests (L2 round-trip, circuit breaker open/close/short-circuit, Brotli compression) passing against the Core-backed `AddRedisL2`
- [ ] `02.Caching/CLAUDE.md` package table updated
- [ ] `dotnet build` clean; no new compile warnings

### Ph33 (P-141) — Dependencies

- Requires Phase 32 (P-140) to be complete: Yes — `SharedKernel.Caching.Redis.Core` must exist with `AddRedisConnection` and `AddRedisCircuitBreaker`
- Unblocks: Phases 34, 35, 36 can proceed independently once Core-backed `AddRedisL2` confirms the dependency graph pattern (`ProjectReference` to Core + sourcing multiplexer/pipeline from Core extensions) works end-to-end

### Ph33 (P-141) — Redis / FusionCache Version Pins

- StackExchange.Redis: >= 2.13.1 (unchanged)
- ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis: 2.6.0 (unchanged)
- Polly.Core: 8.5.2 (now sourced transitively via Core; direct ref retained pending Phase 35/36)
- .NET: `net10.0`

---

## Phase: RedisLockingExtraction <!-- phase-key: SK.02.RedisLockingExtraction -->

> Extract `IDistributedLockService`/`IRenewableLock` RedLock.net implementation (`RedLockDistributedLockService`, `RedLockRenewableLock`, `KeepAliveAsync`, `AddRedisDistributedLocking`) into a new standalone `SharedKernel.Caching.Redis.DistributedLocking` package depending on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` (Phase 32) + RedLock.net.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| RDL-01 | Create `SharedKernel.Caching.Redis.DistributedLocking.csproj` (`net10.0`; refs `SharedKernel.Caching.Abstractions`, `SharedKernel.Caching.Redis.Core`, `RedLock.net` 2.3.2) | SharedKernel.Caching.Redis.DistributedLocking | `●` |
| RDL-02 | Move `Implementations/RedLockDistributedLockService.cs` from `SharedKernel.Caching.Redis` → new package, namespace updated to `SharedKernel.Caching.Redis.DistributedLocking.Implementations` | SharedKernel.Caching.Redis.DistributedLocking | `●` |
| RDL-03 | Move `RedLockRenewableLock.cs` from `SharedKernel.Caching.Redis` → new package; `KeepAliveAsync` static extension moves with it via `Extensions/RenewableLockExtensions.cs` | SharedKernel.Caching.Redis.DistributedLocking | `●` |
| RDL-04 | Move `Extensions/RedisDistributedLockingExtensions.cs` (`AddRedisDistributedLocking` on `ICachingBuilder` + `[Obsolete]` `IServiceCollection` shim) and `Extensions/RedisLockOptions.cs` → new package; `AddRedisDistributedLocking` now calls `services.AddRedisConnection(...)` (Phase 32) instead of inline multiplexer registration | SharedKernel.Caching.Redis.DistributedLocking | `●` |
| RDL-05 | Move/recreate `RedisCachingBuilder` internal helper (used by the `[Obsolete]` `IServiceCollection` shim) into the new package — do not duplicate; if `SharedKernel.Caching.Redis` still needs an internal `ICachingBuilder` shim for its own `[Obsolete]` overloads, each package owns its own minimal copy (small, internal, no shared dependency) | SharedKernel.Caching.Redis.DistributedLocking | `●` |
| RDL-06 | Remove `RedLockDistributedLockService.cs`, `RedLockRenewableLock.cs`, `Extensions/RedisDistributedLockingExtensions.cs`, `Extensions/RedisLockOptions.cs`, `Extensions/RenewableLockExtensions.cs` from `SharedKernel.Caching.Redis` | SharedKernel.Caching.Redis | `●` |
| RDL-07 | Remove `RedLock.net` `PackageReference` from `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | `●` |
| RDL-08 | Register new project in `Platform.SharedKernel.slnx` under `02.Caching`; create nested `SharedKernel.Caching.Redis.DistributedLocking.Tests` | Solution / SharedKernel.Caching.Redis.DistributedLocking.Tests | `●` |
| RDL-09 | Relocate RedLock-related tests from `SharedKernel.Caching.Redis.Tests` (`RedLockIntegrationTests.cs`, `RenewableLockIntegrationTests.cs`, `FakeRenewableLockTests.cs`, `IDistributedLockServiceContractTests.cs`) to `SharedKernel.Caching.Redis.DistributedLocking.Tests` | SharedKernel.Caching.Redis.DistributedLocking.Tests | `●` |
| RDL-10 | Update `02.Caching/CLAUDE.md` package table with the new package's role and references | — | `●` |

---

### Ph34 (P-142) — Goal

Distributed locking is an infrastructure coordination primitive, not a caching primitive — its presence in `SharedKernel.Caching.Redis` is a historical artifact of "it also uses Redis." This phase extracts `IDistributedLockService`/`IRenewableLock` RedLock.net implementation into `SharedKernel.Caching.Redis.DistributedLocking`, depending on `SharedKernel.Caching.Abstractions` (interfaces unchanged) and `SharedKernel.Caching.Redis.Core` (Phase 32) for `IConnectionMultiplexer` and connection health. A microservice that needs only distributed locks (e.g., a Hangfire job coordinator or leader-election worker) references `Abstractions` + `Redis.Core` + `Redis.DistributedLocking` — zero transitive dependency on FusionCache, hash-store types, or pub/sub types.

The existing `AddRedisDistributedLocking(connectionString)` fluent registration shape (canonical `ICachingBuilder` overload + `[Obsolete]` `IServiceCollection` shim, per Phase 19) is preserved exactly.

### Ph34 (P-142) — Scope

- **Package(s) affected:** New `SharedKernel.Caching.Redis.DistributedLocking` (create); `SharedKernel.Caching.Redis` (remove relocated files)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Redis.DistributedLocking/SharedKernel.Caching.Redis.DistributedLocking.csproj`
  - `02.Caching/SharedKernel.Caching.Redis.DistributedLocking/Implementations/RedLockDistributedLockService.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.DistributedLocking/RedLockRenewableLock.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.DistributedLocking/Extensions/RedisDistributedLockingExtensions.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.DistributedLocking/Extensions/RedisLockOptions.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.DistributedLocking/Extensions/RenewableLockExtensions.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.DistributedLocking/SharedKernel.Caching.Redis.DistributedLocking.Tests/` (nested test project)
- **Modified files:**
  - `SharedKernel.Caching.Redis.csproj` — remove `RedLock.net` package reference
  - `02.Caching/CLAUDE.md` — package table updated
- **Deleted files (relocated, not deleted from solution):**
  - `02.Caching/SharedKernel.Caching.Redis/Implementations/RedLockDistributedLockService.cs`
  - `02.Caching/SharedKernel.Caching.Redis/RedLockRenewableLock.cs`
  - `02.Caching/SharedKernel.Caching.Redis/Extensions/RedisDistributedLockingExtensions.cs`
  - `02.Caching/SharedKernel.Caching.Redis/Extensions/RedisLockOptions.cs`
  - `02.Caching/SharedKernel.Caching.Redis/Extensions/RenewableLockExtensions.cs`
  - `02.Caching/SharedKernel.Caching.Redis/SharedKernel.Caching.Redis.Tests/RedLockIntegrationTests.cs`
  - `02.Caching/SharedKernel.Caching.Redis/SharedKernel.Caching.Redis.Tests/RenewableLockIntegrationTests.cs`
  - `02.Caching/SharedKernel.Caching.Redis/SharedKernel.Caching.Redis.Tests/FakeRenewableLockTests.cs`
  - `02.Caching/SharedKernel.Caching.Redis/SharedKernel.Caching.Redis.Tests/Abstractions/IDistributedLockServiceContractTests.cs`

### Ph34 (P-142) — Implementation Rules

1. `IDistributedLockService` and `IRenewableLock` interface contracts in `SharedKernel.Caching.Abstractions` are **unchanged** — no breaking change to the abstraction. Only the implementation (`RedLockDistributedLockService`, `RedLockRenewableLock`) and DI extension move.
2. `RedLockDistributedLockService` and `RedLockRenewableLock` namespaces change from `SharedKernel.Caching.Redis.*` to `SharedKernel.Caching.Redis.DistributedLocking.*`. No behavioral changes.
3. `AddRedisDistributedLocking(this ICachingBuilder, string connectionString, Action<RedisLockOptions>? configure = null)` is preserved verbatim in signature and behavior, with one internal change: it calls `services.AddRedisConnection(connectionString, o => o.ConnectTimeoutMs = options.ConnectTimeoutMs)` (Phase 32) instead of its own inline `ConfigurationOptions.Parse` + `TryAddSingleton<IConnectionMultiplexer>` block. The `TryAddSingleton` "first caller wins" guarantee is preserved — whichever of `AddRedisL2`, `AddRedisDistributedLocking`, `AddRedisHashService`, or `AddRedisChannelService` (Phases 33/35/36) runs first wins the multiplexer registration via Core.
4. `RedLockFactory` registration (`services.TryAddSingleton(sp => RedLockFactory.Create(...))`) and `IDistributedLockFactory` exposure remain unchanged — these are RedLock.net-specific and stay in this package.
5. The `[Obsolete]` `AddRedisDistributedLocking(this IServiceCollection, ...)` shim (Phase 19) is preserved. It needs an internal `ICachingBuilder` implementation to delegate to the canonical overload — this package defines its own minimal `internal sealed class RedisLockCachingBuilder(IServiceCollection services) : ICachingBuilder` (do not attempt to share `RedisCachingBuilder` across packages via a project reference — each extraction package owns a tiny private copy of this trivial adapter; this avoids introducing a dependency purely for a 3-line internal type).
6. **Renewable lock re-acquisition behavior is preserved exactly** (Phase 23 rule): RedLock.net 2.3.2 has no public `ExtendAsync`; `RedLockRenewableLock` uses dispose-then-recreate (`CreateLockAsync` on the same resource after disposing the old lock). `KeepAliveAsync` remains a static extension method on `IRenewableLock`, now declared in `SharedKernel.Caching.Redis.DistributedLocking.Extensions`.
7. `IRenewableLock.RenewAsync` returns `false` for a lost lock and never throws (Phase 23 rule, unchanged). `IsAcquired` transitions to `false` after `DisposeAsync` and after a failed renewal.
8. `FakeDistributedLockService` and `FakeRenewableLock` (in `16.Testing`, Phase 23) are **not** moved — `16.Testing` is outside this domain's jurisdiction and those types reference only `SharedKernel.Caching.Abstractions`, which is unaffected.
9. All public types carry XML doc comments. The package XML doc / `<Description>` must state: "Distributed mutual-exclusion locking over Redis via RedLock.net. Depends on SharedKernel.Caching.Redis.Core for connection management — does not transitively reference SharedKernel.Caching.Redis (L2), hash store, or pub/sub packages."

### Ph34 (P-142) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `SharedKernel.Caching.Redis.DistributedLocking.csproj` | SharedKernel.Caching.Redis.DistributedLocking | Create | Project definition; refs Abstractions + Redis.Core + RedLock.net |
| `Implementations/RedLockDistributedLockService.cs` | SharedKernel.Caching.Redis.DistributedLocking | Create (relocated) | `IDistributedLockService` impl via RedLock.net |
| `RedLockRenewableLock.cs` | SharedKernel.Caching.Redis.DistributedLocking | Create (relocated) | `IRenewableLock` impl; re-acquisition strategy |
| `Extensions/RedisDistributedLockingExtensions.cs` | SharedKernel.Caching.Redis.DistributedLocking | Create (relocated) | `AddRedisDistributedLocking` (canonical + `[Obsolete]` shim); sources multiplexer from Core |
| `Extensions/RedisLockOptions.cs` | SharedKernel.Caching.Redis.DistributedLocking | Create (relocated) | Lock options (`ConnectionString`, `ConnectTimeoutMs`, lock-specific settings) |
| `Extensions/RenewableLockExtensions.cs` | SharedKernel.Caching.Redis.DistributedLocking | Create (relocated) | `KeepAliveAsync` static extension |
| `SharedKernel.Caching.Redis.DistributedLocking.Tests/` | SharedKernel.Caching.Redis.DistributedLocking.Tests | Create | Relocated RedLock + renewable lock tests |
| `SharedKernel.Caching.Redis/Implementations/RedLockDistributedLockService.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis/RedLockRenewableLock.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis/Extensions/RedisDistributedLockingExtensions.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis/Extensions/RedisLockOptions.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis/Extensions/RenewableLockExtensions.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | Modify | Remove `RedLock.net` package reference |
| `02.Caching/CLAUDE.md` | — | Modify | Package table: add `SharedKernel.Caching.Redis.DistributedLocking` row |

### Ph34 (P-142) — Acceptance Criteria

- [ ] New package `SharedKernel.Caching.Redis.DistributedLocking` contains `RedLockDistributedLockService`, `RedLockRenewableLock`, `KeepAliveAsync`, `RedisLockOptions`, `AddRedisDistributedLocking`
- [ ] Package depends on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` + `RedLock.net` — no reference to `SharedKernel.Caching.Redis` (L2), hash store, or pub/sub packages
- [ ] `IDistributedLockService` and `IRenewableLock` contracts in `SharedKernel.Caching.Abstractions` unchanged
- [ ] `AddRedisDistributedLocking(connectionString)` fluent registration shape preserved (canonical `ICachingBuilder` overload + `[Obsolete]` `IServiceCollection` shim); `IConnectionMultiplexer` sourced from Phase 32's `AddRedisConnection`
- [ ] Renewable lock re-acquisition behavior (no public `ExtendAsync` in RedLock.net 2.3.2) and `KeepAliveAsync` background renewal preserved with existing test coverage
- [ ] `RedLock.net` package reference removed from `SharedKernel.Caching.Redis.csproj`
- [ ] Relevant subset of the 154 Redis tests (acquire/timeout/release/expiry/renewal) relocated and passing against the new package
- [ ] `02.Caching/CLAUDE.md` package table updated with the new package's role and references
- [ ] `dotnet build` clean; no new compile warnings

### Ph34 (P-142) — Dependencies

- Requires Phase 32 (P-140) to be complete: Yes — `SharedKernel.Caching.Redis.Core` must provide `AddRedisConnection`
- Requires Phase 33 (P-141) to be complete: No — independent of the L2 refactor; both depend only on Phase 32
- Unblocks: Microservices needing only distributed locking (Hangfire coordinators, leader election workers) can take a minimal `Abstractions` + `Redis.Core` + `Redis.DistributedLocking` dependency

### Ph34 (P-142) — Redis / FusionCache Version Pins

- RedLock.net: 2.3.2 (unchanged — no public `ExtendAsync`, re-acquisition strategy retained)
- StackExchange.Redis: >= 2.13.1 (via Core)
- ZiggyCreatures.FusionCache: N/A (no reference in this package)
- .NET: `net10.0`

---

## Phase: RedisHashExtraction <!-- phase-key: SK.02.RedisHashExtraction -->

> Extract `IRedisHashService`/`ITypedHashStore<T>` (`RedisHashService`, `TypedHashStore<T>`, `AddRedisHashService`, `AddTypedHashStore<T>`) into a new standalone `SharedKernel.Caching.Redis.HashStore` package depending on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` (Phase 32).

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| RHS-01 | Create `SharedKernel.Caching.Redis.HashStore.csproj` (`net10.0`; refs `SharedKernel.Caching.Abstractions`, `SharedKernel.Caching.Redis.Core`) | SharedKernel.Caching.Redis.HashStore | `●` |
| RHS-02 | Move `RedisHashService.cs` from `SharedKernel.Caching.Redis` → new package, namespace updated to `SharedKernel.Caching.Redis.HashStore` | SharedKernel.Caching.Redis.HashStore | `●` |
| RHS-03 | Move `TypedHashStore.cs` (internal sealed) → new package | SharedKernel.Caching.Redis.HashStore | `●` |
| RHS-04 | Move `Extensions/RedisHashServiceExtensions.cs` (`AddRedisHashService`, `AddTypedHashStore<T>`) → new package; startup guard now validates against Phase 32's `IConnectionMultiplexer` registration (registered by `AddRedisConnection` from any of L2/locking/hash/pubsub) | SharedKernel.Caching.Redis.HashStore | `●` |
| RHS-05 | Update `AddRedisHashService` to resolve the optional `ResiliencePipeline` via `sp.GetService<ResiliencePipeline>()` — sourced from Phase 32's `AddRedisCircuitBreaker` (registered by whichever package calls it) | SharedKernel.Caching.Redis.HashStore | `●` |
| RHS-06 | Remove `RedisHashService.cs`, `TypedHashStore.cs`, `Extensions/RedisHashServiceExtensions.cs` from `SharedKernel.Caching.Redis` | SharedKernel.Caching.Redis | `●` |
| RHS-07 | Register new project in `Platform.SharedKernel.slnx` under `02.Caching`; create nested `SharedKernel.Caching.Redis.HashStore.Tests` | Solution / SharedKernel.Caching.Redis.HashStore.Tests | `●` |
| RHS-08 | Relocate hash-related tests (`RedisHashServiceIntegrationTests.cs`, `TypedHashStoreIntegrationTests.cs`) to `SharedKernel.Caching.Redis.HashStore.Tests`; preserve `InternalsVisibleTo` for `TypedHashStore<T>` access | SharedKernel.Caching.Redis.HashStore.Tests | `●` |
| RHS-09 | Update `02.Caching/CLAUDE.md` package table with the new package's role and references | — | `●` |

---

### Ph35 (P-143) — Goal

`IRedisHashService` and `ITypedHashStore<T>` are a general-purpose structured-storage primitive over Redis Hashes (sessions, configuration snapshots, counters) — conceptually closer to a lightweight key-value/document store than to "caching." Today, any service using `ITypedHashStore<OrderDto>` for session storage transitively pulls in FusionCache, RedLock.net, and pub/sub code via `SharedKernel.Caching.Redis`. This phase extracts hash-store types into `SharedKernel.Caching.Redis.HashStore`, giving hash-store consumers (often BFF/session-management services) a minimal, purpose-named dependency: `Abstractions` + `Redis.Core` + `Redis.HashStore`.

### Ph35 (P-143) — Scope

- **Package(s) affected:** New `SharedKernel.Caching.Redis.HashStore` (create); `SharedKernel.Caching.Redis` (remove relocated files)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Redis.HashStore/SharedKernel.Caching.Redis.HashStore.csproj`
  - `02.Caching/SharedKernel.Caching.Redis.HashStore/RedisHashService.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.HashStore/TypedHashStore.cs` (relocated, internal sealed)
  - `02.Caching/SharedKernel.Caching.Redis.HashStore/Extensions/RedisHashServiceExtensions.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.HashStore/SharedKernel.Caching.Redis.HashStore.Tests/` (nested test project)
- **Modified files:**
  - `02.Caching/CLAUDE.md` — package table updated
- **Deleted files (relocated, not deleted from solution):**
  - `02.Caching/SharedKernel.Caching.Redis/RedisHashService.cs`
  - `02.Caching/SharedKernel.Caching.Redis/TypedHashStore.cs`
  - `02.Caching/SharedKernel.Caching.Redis/Extensions/RedisHashServiceExtensions.cs`
  - `02.Caching/SharedKernel.Caching.Redis/SharedKernel.Caching.Redis.Tests/RedisHashServiceIntegrationTests.cs`
  - `02.Caching/SharedKernel.Caching.Redis/SharedKernel.Caching.Redis.Tests/TypedHashStoreIntegrationTests.cs`

### Ph35 (P-143) — Implementation Rules

1. `IRedisHashService` and `ITypedHashStore<T>` interface contracts in `SharedKernel.Caching.Abstractions` are **unchanged** — no breaking change. Only `RedisHashService` and `TypedHashStore<T>` implementations and the DI extension move.
2. `RedisHashService` namespace changes from `SharedKernel.Caching.Redis` to `SharedKernel.Caching.Redis.HashStore`. Constructor signature (`IConnectionMultiplexer multiplexer, ResiliencePipeline? pipeline = null`) is unchanged — `IDatabase` is still obtained once via `multiplexer.GetDatabase()` and cached (Phase 7 rule).
3. `TypedHashStore<T>` remains `internal sealed` — it is `internal` to `SharedKernel.Caching.Redis.HashStore` now (not `SharedKernel.Caching.Redis`). The `InternalsVisibleTo` attribute in the new `.csproj` targets `SharedKernel.Caching.Redis.HashStore.Tests`.
4. `AddRedisHashService(this ICachingBuilder)` startup guard changes from checking `services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer))` registered by `AddRedisL2`/`AddRedisDistributedLocking` to checking the same `IConnectionMultiplexer` type — now potentially registered by Phase 32's `AddRedisConnection` (called from any of L2, locking, hash, or pub/sub packages). The guard message updates to: `"AddRedisHashService requires AddRedisConnection (directly, or transitively via AddRedisL2 / AddRedisDistributedLocking / AddRedisChannelService) to be called first to register IConnectionMultiplexer."` The underlying check (`services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer))`) is unchanged — only the error message and conceptual framing change, since the registration source is now centralized in Core.
5. All typed methods continue to use `JsonTypeInfo<T>` — no `typeof(T)` reflection introduced (Phase 15/AOT rule, unchanged).
6. The optional Polly circuit breaker pipeline continues to be resolved via `sp.GetService<ResiliencePipeline>()` (optional, not required) — now the pipeline may have been registered by Phase 32's `AddRedisCircuitBreaker`, called by whichever capability package's extension method invoked it. `RedisHashService` has zero knowledge of which package registered the pipeline — it only resolves the type.
7. `AddTypedHashStore<T>(JsonTypeInfo<T> typeInfo)` extension preserves its guard: throws `InvalidOperationException("AddTypedHashStore<T> requires AddRedisHashService to be called first.")` if `IRedisHashService` is not registered (Phase 15 rule, unchanged).
8. All public types carry XML doc comments. Package `<Description>` must state: "Structured Redis Hash storage (IRedisHashService, ITypedHashStore<T>). Depends on SharedKernel.Caching.Redis.Core for connection management — does not transitively reference SharedKernel.Caching.Redis (L2), distributed locking, or pub/sub packages."

### Ph35 (P-143) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `SharedKernel.Caching.Redis.HashStore.csproj` | SharedKernel.Caching.Redis.HashStore | Create | Project definition; refs Abstractions + Redis.Core |
| `RedisHashService.cs` | SharedKernel.Caching.Redis.HashStore | Create (relocated) | `IRedisHashService` impl; `JsonTypeInfo<T>` typed; shared `IDatabase` |
| `TypedHashStore.cs` | SharedKernel.Caching.Redis.HashStore | Create (relocated) | `ITypedHashStore<T>` impl; internal sealed |
| `Extensions/RedisHashServiceExtensions.cs` | SharedKernel.Caching.Redis.HashStore | Create (relocated) | `AddRedisHashService`, `AddTypedHashStore<T>`; guard updated for Core registration |
| `SharedKernel.Caching.Redis.HashStore.Tests/` | SharedKernel.Caching.Redis.HashStore.Tests | Create | Relocated hash store tests |
| `SharedKernel.Caching.Redis/RedisHashService.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis/TypedHashStore.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis/Extensions/RedisHashServiceExtensions.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `02.Caching/CLAUDE.md` | — | Modify | Package table: add `SharedKernel.Caching.Redis.HashStore` row |

### Ph35 (P-143) — Acceptance Criteria

- [ ] New package `SharedKernel.Caching.Redis.HashStore` contains `RedisHashService`, `TypedHashStore<T>` (internal sealed), `AddRedisHashService`, `AddTypedHashStore<T>`
- [ ] Package depends on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` — no reference to `SharedKernel.Caching.Redis` (L2), distributed locking, or pub/sub packages
- [ ] `IRedisHashService` and `ITypedHashStore<T>` contracts in `SharedKernel.Caching.Abstractions` unchanged
- [ ] `AddRedisHashService()` / `AddTypedHashStore(JsonTypeInfo<T>)` fluent registration shapes preserved; startup guard validates against `IConnectionMultiplexer` (sourced via Phase 32)
- [ ] All typed methods continue to use `JsonTypeInfo<T>` — no `typeof(T)` reflection introduced
- [ ] Optional Polly circuit breaker pipeline continues to be resolved via `sp.GetService<ResiliencePipeline>()` (optional, sourced from Phase 32)
- [ ] Relevant subset of the 154 Redis tests (set/get/get-all/delete/increment, typed store round-trip, shared multiplexer) relocated and passing against the new package
- [ ] `02.Caching/CLAUDE.md` package table updated with the new package's role and references
- [ ] `dotnet build` clean; no new compile warnings

### Ph35 (P-143) — Dependencies

- Requires Phase 32 (P-140) to be complete: Yes — `SharedKernel.Caching.Redis.Core` must provide `IConnectionMultiplexer` registration and optional `ResiliencePipeline`
- Requires Phase 33 (P-141) / Phase 34 (P-142) to be complete: No — independent; all three (33/34/35) depend only on Phase 32
- Unblocks: Session-management/BFF services needing only structured Redis Hash storage can take a minimal `Abstractions` + `Redis.Core` + `Redis.HashStore` dependency

### Ph35 (P-143) — Redis / FusionCache Version Pins

- StackExchange.Redis: >= 2.13.1 (via Core)
- Polly.Core: 8.5.2 (optional, via Core, when circuit breaker enabled)
- ZiggyCreatures.FusionCache: N/A (no reference in this package)
- .NET: `net10.0`

---

## Phase: RedisPubSubExtraction <!-- phase-key: SK.02.RedisPubSubExtraction -->

> Extract `IRedisChannelService`/`ICacheInvalidationBus` (`RedisChannelService`, `RedisCacheInvalidationBus`, `CacheInvalidationReceiver`) into a new standalone `SharedKernel.Caching.Redis.PubSub` package depending on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` (Phase 32). Stays within `02.Caching` — not moved to `07.Messaging`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| RPS-01 | Create `SharedKernel.Caching.Redis.PubSub.csproj` (`net10.0`; refs `SharedKernel.Caching.Abstractions`, `SharedKernel.Caching.Redis.Core`, `Microsoft.Extensions.Hosting.Abstractions` for `CacheInvalidationReceiver`) | SharedKernel.Caching.Redis.PubSub | `●` |
| RPS-02 | Move `RedisChannelService.cs` from `SharedKernel.Caching.Redis` → new package, namespace updated to `SharedKernel.Caching.Redis.PubSub`; reconnect/resubscribe replay logic (Phase 26 `Dictionary` + `_registryLock`, `volatile int _connectionHealth`) relocated intact, but `ConnectionRestored`/`ConnectionFailed` subscription now composes with (or supersedes) Phase 32's `RedisConnectionHealthTracker` — see Implementation Rule 3 | SharedKernel.Caching.Redis.PubSub | `●` |
| RPS-03 | Move `RedisCacheInvalidationBus.cs` and `CacheInvalidationReceiver.cs` → new package | SharedKernel.Caching.Redis.PubSub | `●` |
| RPS-04 | Move `Extensions/RedisChannelServiceExtensions.cs` and `Extensions/CacheInvalidationExtensions.cs` (`AddRedisChannelService`, `AddRedisCacheInvalidationBus`, `AddCacheInvalidationReceiver`) → new package; startup guards updated to validate against Phase 32's `IConnectionMultiplexer` | SharedKernel.Caching.Redis.PubSub | `●` |
| RPS-05 | Remove `RedisChannelService.cs`, `RedisCacheInvalidationBus.cs`, `CacheInvalidationReceiver.cs`, `Extensions/RedisChannelServiceExtensions.cs`, `Extensions/CacheInvalidationExtensions.cs` from `SharedKernel.Caching.Redis` | SharedKernel.Caching.Redis | `●` |
| RPS-06 | Remove `Microsoft.Extensions.Hosting.Abstractions` `PackageReference` from `SharedKernel.Caching.Redis.csproj` if no longer used (verify — `CacheInvalidationReceiver` was the only `BackgroundService` consumer) | SharedKernel.Caching.Redis | `●` |
| RPS-07 | Register new project in `Platform.SharedKernel.slnx` under `02.Caching`; create nested `SharedKernel.Caching.Redis.PubSub.Tests` | Solution / SharedKernel.Caching.Redis.PubSub.Tests | `●` |
| RPS-08 | Relocate pub/sub + invalidation tests (`RedisChannelServiceIntegrationTests.cs`, `ChannelReconnectIntegrationTests.cs`, `CacheInvalidationIntegrationTests.cs`, `CachingCoreOptionsDiTests.cs` if pub/sub-specific) to `SharedKernel.Caching.Redis.PubSub.Tests` | SharedKernel.Caching.Redis.PubSub.Tests | `●` |
| RPS-09 | Update `02.Caching/CLAUDE.md` package table with the new package's role and references; add explicit statement that this package stays in `02.Caching` and why (07.Messaging durability contrast) | — | `●` |

---

### Ph36 (P-144) — Goal

`IRedisChannelService` and `ICacheInvalidationBus` carry an explicit, deliberate at-most-once / no-delivery-guarantee contract — the architectural opposite of `07.Messaging`'s durable, outbox-backed, retryable contract. This phase extracts these types into `SharedKernel.Caching.Redis.PubSub`, depending on `SharedKernel.Caching.Abstractions` (interfaces, `CacheInvalidationMessage`, `CachingCoreOptions`, `ConnectionHealthState` — all unchanged) and `SharedKernel.Caching.Redis.Core` (Phase 32) for `IConnectionMultiplexer`, connection health, and circuit breaker pipeline.

**This package remains within `02.Caching` — it is NOT moved to `07.Messaging`.** Moving these types into `07.Messaging` would (a) violate the layering rule that `07.Messaging` may not reference `02.Caching` types (`CacheInvalidationMessage`, `CachingCoreOptions`), and (b) create a foreseeable trap where developers assume `07.Messaging`-housed abstractions inherit its delivery guarantees. The existing XML-doc boundary ("ephemeral, no delivery guarantees, not a substitute for `07.Messaging`") is preserved and reinforced by the package name itself.

### Ph36 (P-144) — Scope

- **Package(s) affected:** New `SharedKernel.Caching.Redis.PubSub` (create); `SharedKernel.Caching.Redis` (remove relocated files)
- **New files:**
  - `02.Caching/SharedKernel.Caching.Redis.PubSub/SharedKernel.Caching.Redis.PubSub.csproj`
  - `02.Caching/SharedKernel.Caching.Redis.PubSub/RedisChannelService.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.PubSub/RedisCacheInvalidationBus.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.PubSub/CacheInvalidationReceiver.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.PubSub/Extensions/RedisChannelServiceExtensions.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.PubSub/Extensions/CacheInvalidationExtensions.cs` (relocated)
  - `02.Caching/SharedKernel.Caching.Redis.PubSub/SharedKernel.Caching.Redis.PubSub.Tests/` (nested test project)
- **Modified files:**
  - `SharedKernel.Caching.Redis.csproj` — remove `Microsoft.Extensions.Hosting.Abstractions` reference if unused; remove direct `Polly.Core` reference if unused (final cleanup — by this phase, `RedisHashService`/`RedisChannelService` are both relocated, so `SharedKernel.Caching.Redis` should have no remaining direct `Polly.ResiliencePipeline` consumer)
  - `02.Caching/CLAUDE.md` — package table updated; explicit "stays in 02.Caching" rationale added
- **Deleted files (relocated, not deleted from solution):**
  - `02.Caching/SharedKernel.Caching.Redis/RedisChannelService.cs`
  - `02.Caching/SharedKernel.Caching.Redis/RedisCacheInvalidationBus.cs`
  - `02.Caching/SharedKernel.Caching.Redis/CacheInvalidationReceiver.cs`
  - `02.Caching/SharedKernel.Caching.Redis/Extensions/RedisChannelServiceExtensions.cs`
  - `02.Caching/SharedKernel.Caching.Redis/Extensions/CacheInvalidationExtensions.cs`
  - Corresponding test files in `SharedKernel.Caching.Redis.Tests`: `RedisChannelServiceIntegrationTests.cs`, `ChannelReconnectIntegrationTests.cs`, `CacheInvalidationIntegrationTests.cs`

### Ph36 (P-144) — Implementation Rules

1. `IRedisChannelService`, `ICacheInvalidationBus`, `CacheInvalidationMessage`, `CacheInvalidationMessageJsonContext`, `ConnectionHealthState`, and `CachingCoreOptions` remain in `SharedKernel.Caching.Abstractions` — **unchanged**, no breaking change. Only `RedisChannelService`, `RedisCacheInvalidationBus`, `CacheInvalidationReceiver`, and their DI extensions move.
2. Namespaces for the three relocated implementation types change from `SharedKernel.Caching.Redis` to `SharedKernel.Caching.Redis.PubSub`.
3. **Connection health composition:** `RedisChannelService` (Phase 26) has its own `volatile int _connectionHealth` field and `ConnectionRestored`/`ConnectionFailed` subscriptions for resubscription replay — this logic is **relocated intact**, not replaced by Phase 32's `RedisConnectionHealthTracker`. `RedisChannelService.ConnectionHealth` continues to be the source of truth for `IRedisChannelService.ConnectionHealth` (the public contract). `RedisConnectionHealthTracker` (Phase 32, Core) is a separate, simpler tracker available for health-check use by packages that don't need resubscription replay (e.g., hash store, distributed locking). Both may coexist in the same DI container — they subscribe to the same multiplexer's events independently and do not conflict (StackExchange.Redis supports multiple subscribers to the same event). Do not attempt to unify them in this phase — that is a future consideration, not a Phase 36 requirement.
4. Channel naming convention is **unchanged**: targeted `sharedkernel:cache:invalidation:{service-name}` (where `{service-name}` = `options.ServiceName.ToLowerInvariant().Replace(' ', '-')` from `CachingCoreOptions`), broadcast `sharedkernel:cache:invalidation:broadcast` (literal constant).
5. `AddRedisChannelService(this ICachingBuilder)` startup guard updates from checking `IConnectionMultiplexer` registered by `AddRedisL2`/`AddRedisDistributedLocking` to the same type-presence check, with an updated error message reflecting that `AddRedisConnection` (Phase 32, directly or transitively via any capability package) is the source. `AddCacheInvalidationReceiver` guards (`IRedisChannelService` registered via `AddRedisChannelService`; `ICacheService` registered via `AddSharedKernelCaching`) are unchanged (Phase 19 rules).
6. `RedisChannelService` continues to use `RedisChannel.Literal(channelName)` exclusively — never `RedisChannel.Pattern` (Phase 7 rule, unchanged). All handler exceptions caught and logged at `LogLevel.Error`, never propagated (Phase 7 rule, unchanged).
7. `CacheInvalidationReceiver` remains a `BackgroundService` registered via `AddHostedService`; `StopAsync` calls `UnsubscribeAsync` for both channels before `base.StopAsync` (Phase 12 rule, unchanged). OTel span `"cache.invalidation.receive"` with tags `cache.invalidation.source`, `cache.invalidation.correlation_id`, `cache.invalidation.type` (Phase 12 rule, unchanged).
8. The XML-doc boundary statement — "ephemeral, no delivery guarantees, not a substitute for `07.Messaging`" — is preserved verbatim on `IRedisChannelService` and `ICacheInvalidationBus` in `SharedKernel.Caching.Abstractions` (these types are not moved, so the doc comments are untouched by definition; this rule exists to explicitly confirm no accidental doc-comment loss during any incidental abstractions edits in this phase).
9. The optional Polly circuit breaker pipeline (`sp.GetService<ResiliencePipeline>()`) continues to be resolved optionally in `RedisChannelService`, sourced from Phase 32's `AddRedisCircuitBreaker` (Phase 30 rule, unchanged).
10. After this phase, `SharedKernel.Caching.Redis` contains **only**: `AddRedisL2`, `RedisL2Options`, FusionCache Redis backplane wiring, and (in `SharedKernel.Caching.FusionCache`, unaffected) Brotli compression. Verify and remove any now-unused `PackageReference` entries (`Microsoft.Extensions.Hosting.Abstractions`, `Polly.Core` if no longer directly consumed) from `SharedKernel.Caching.Redis.csproj`.
11. All public types carry XML doc comments. Package `<Description>` must state: "Ephemeral Redis Pub/Sub signaling and cache invalidation (IRedisChannelService, ICacheInvalidationBus, CacheInvalidationReceiver). At-most-once delivery, no durability guarantees — not a substitute for SharedKernel.Messaging.* (07.Messaging). Depends on SharedKernel.Caching.Redis.Core for connection management — does not transitively reference SharedKernel.Caching.Redis (L2), distributed locking, or hash store packages."

### Ph36 (P-144) — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `SharedKernel.Caching.Redis.PubSub.csproj` | SharedKernel.Caching.Redis.PubSub | Create | Project definition; refs Abstractions + Redis.Core + Hosting.Abstractions |
| `RedisChannelService.cs` | SharedKernel.Caching.Redis.PubSub | Create (relocated) | `IRedisChannelService` impl; reconnect replay intact |
| `RedisCacheInvalidationBus.cs` | SharedKernel.Caching.Redis.PubSub | Create (relocated) | `ICacheInvalidationBus` publisher; channel naming |
| `CacheInvalidationReceiver.cs` | SharedKernel.Caching.Redis.PubSub | Create (relocated) | `BackgroundService` subscriber; OTel span |
| `Extensions/RedisChannelServiceExtensions.cs` | SharedKernel.Caching.Redis.PubSub | Create (relocated) | `AddRedisChannelService`; guard updated for Core registration |
| `Extensions/CacheInvalidationExtensions.cs` | SharedKernel.Caching.Redis.PubSub | Create (relocated) | `AddRedisCacheInvalidationBus`, `AddCacheInvalidationReceiver` |
| `SharedKernel.Caching.Redis.PubSub.Tests/` | SharedKernel.Caching.Redis.PubSub.Tests | Create | Relocated pub/sub + invalidation tests |
| `SharedKernel.Caching.Redis/RedisChannelService.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis/RedisCacheInvalidationBus.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis/CacheInvalidationReceiver.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis/Extensions/RedisChannelServiceExtensions.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis/Extensions/CacheInvalidationExtensions.cs` | SharedKernel.Caching.Redis | Delete (relocated) | — |
| `SharedKernel.Caching.Redis.csproj` | SharedKernel.Caching.Redis | Modify | Remove unused `Hosting.Abstractions`/`Polly.Core` refs if confirmed unused |
| `02.Caching/CLAUDE.md` | — | Modify | Package table: add `SharedKernel.Caching.Redis.PubSub` row; "stays in 02.Caching" rationale |

### Ph36 (P-144) — Acceptance Criteria

- [ ] New package `SharedKernel.Caching.Redis.PubSub` contains `RedisChannelService`, `RedisCacheInvalidationBus`, `CacheInvalidationReceiver`
- [ ] `IRedisChannelService`, `ICacheInvalidationBus`, `CacheInvalidationMessage`, `CacheInvalidationMessageJsonContext`, `ConnectionHealthState`, `CachingCoreOptions` remain in `SharedKernel.Caching.Abstractions` — unchanged
- [ ] Package depends on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` — no reference to `SharedKernel.Caching.Redis` (L2), distributed locking, or hash store packages, and no reference to any `07.Messaging` package
- [ ] `AddRedisChannelService()`, `AddRedisCacheInvalidationBus()`, `AddCacheInvalidationReceiver()` fluent shapes preserved; startup guards updated to validate against Phase 32's `IConnectionMultiplexer`
- [ ] Connection reconnect/resubscribe replay logic (Phase 26 `Dictionary` + lock registry, `ConnectionHealthState` transitions) relocated intact
- [ ] Channel naming convention (`sharedkernel:cache:invalidation:{service-name}` / `:broadcast`) unchanged
- [ ] XML doc boundary statement ("ephemeral, no delivery guarantees, not a substitute for 07.Messaging") preserved on `IRedisChannelService` and `ICacheInvalidationBus`
- [ ] Relevant subset of the 154 Redis tests (pub/sub round-trip, unsubscribe, reconnect/resubscribe, invalidation receiver: key/tag/broadcast/offline) relocated and passing against the new package
- [ ] `02.Caching/CLAUDE.md` package table updated with the new package's role and references; explicit statement that this package stays in `02.Caching` and why (linking the rationale to the `07.Messaging` durability contrast)
- [ ] After this phase, `SharedKernel.Caching.Redis` contains only L2 backplane code (`AddRedisL2`, `RedisL2Options`, FusionCache Redis backplane wiring) — verify via file listing
- [ ] `dotnet build` clean; no new compile warnings

### Ph36 (P-144) — Dependencies

- Requires Phase 32 (P-140) to be complete: Yes — `SharedKernel.Caching.Redis.Core` must provide `IConnectionMultiplexer` registration, `RedisConnectionHealthTracker`, and optional `ResiliencePipeline`
- Requires Phase 33 (P-141) / Phase 34 (P-142) / Phase 35 (P-143) to be complete: No — independent; all four extraction phases (33/34/35/36) depend only on Phase 32, but Phase 36 is logically the "last" extraction since it leaves `SharedKernel.Caching.Redis` in its final L2-only state
- Unblocks: Lightweight cross-instance signaling consumers (no FusionCache, no RedLock, no hash store) can take a minimal `Abstractions` + `Redis.Core` + `Redis.PubSub` dependency; completes the WO-023 package topology

### Ph36 (P-144) — Redis / FusionCache Version Pins

- StackExchange.Redis: >= 2.13.1 (via Core)
- Microsoft.Extensions.Hosting.Abstractions: 10.0.0 (for `CacheInvalidationReceiver` `BackgroundService`)
- Polly.Core: 8.5.2 (optional, via Core, when circuit breaker enabled)
- ZiggyCreatures.FusionCache: N/A (no reference in this package)
- `System.Diagnostics.DiagnosticSource`: BCL in `net10.0` — no additional NuGet reference
- .NET: `net10.0`

---

## Cross-Domain Dependencies

_No active cross-domain dependencies._

<!--
Format when active:
| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
| SK.02.Core | 01.Core | IClock type finalized | ◐ In progress |
-->

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | State |
|-----------|-------|:-----:|:------:|:-----:|
| `SK.02.Design` | Design | 6 | 6 | `●` |
| `SK.02.Scaffold` | Scaffold | 6 | 6 | `●` |
| `SK.02.Core` | Core | 9 | 9 | `●` |
| `SK.02.Tests` | Tests | 6 | 6 | `●` |
| `SK.02.Docs` | Docs | 4 | 4 | `●` |
| `SK.02.Published` | Published | 4 | 4 | `●` |
| `SK.02.Abstractions` | Phase 5 (Abstractions Package) | 11 | 11 | `●` |
| `SK.02.CachingRefactor` | Phase 6 (Caching Refactor + CacheKeyProvider) | 8 | 8 | `●` |
| `SK.02.RedisRefactor` | Phase 7 (Redis Refactor + Channel + Hash) | 7 | 7 | `●` |
| `SK.02.InvalidationBus` | Phase 12 (Invalidation Bus) | 3 | 3 | `●` |
| `SK.02.FusionCacheRename` | Phase 14 (FusionCache Rename + NeverExpire) | 11 | 11 | `●` |
| `SK.02.AotHardening` | Phase 15 (AOT Hardening + ITypedHashStore) | 8 | 8 | `●` |
| `SK.02.BrotliCompression` | Phase 16 (Brotli L2 Compression) | 5 | 5 | `●` |
| `SK.02.LayeringFix` | Phase 17 (Redis→FusionCache Layering Fix) | 8 | 8 | `●` |
| `SK.02.AotSerializerFix` | Phase 18 (AddRedisL2 Serializer Override Fix) | 5 | 5 | `●` |
| `SK.02.DiErgonomics` | Phase 19 (DI Ergonomics Hardening) | 6 | 6 | `●` |
| `SK.02.L1SizeLimit` | Phase 20 (L1SizeLimit Wiring + L2 KeyPrefix) | 7 | 7 | `●` |
| `SK.02.ValueTaskFactory` | Phase 21 (GetOrSetAsync ValueTask Factory) | 8 | 8 | `●` |
| `SK.02.BatchOperations` | Phase 22 (Batch Get and Set) | 8 | 8 | `●` |
| `SK.02.RenewableLock` | Phase 23 (IRenewableLock Heartbeat) | 7 | 7 | `●` |
| `SK.02.SlidingExpiration` | Phase 24 (Sliding Expiration) | 6 | 6 | `●` |
| `SK.02.KeyVersioning` | Phase 25 (Cache Key Versioning) | 8 | 8 | `●` |
| `SK.02.ChannelReconnect` | Phase 26 (Channel Reconnect Resilience) | 6 | 6 | `●` |
| `SK.02.CachingCoreOptionsDi` | Phase 27 (CachingCoreOptions Standalone DI) | 5 | 5 | `●` |
| `SK.02.CacheWarmup` | Phase 28 (Cache Warmup Strategy) | 6 | 6 | `●` |
| `SK.02.TenantCacheKey` | Phase 29 (Multi-Tenant Cache Key) | 6 | 6 | `●` |
| `SK.02.RedisCircuitBreaker` | Phase 30 (Redis Circuit Breaker) | 6 | 6 | `●` |
| `SK.02.OtelMeters` | Phase 31 (OTel Metrics) | 9 | 9 | `●` |
| `SK.02.RedisConnectionCore` | Phase 32 (Redis Connection Core Extraction) | 9 | 9 | `●` |
| `SK.02.RedisL2Refactor` | Phase 33 (Redis L2 Backplane Package Refactor) | 8 | 8 | `●` |
| `SK.02.RedisLockingExtraction` | Phase 34 (Redis Distributed Locking Package Extraction) | 10 | 10 | `●` |
| `SK.02.RedisHashExtraction` | Phase 35 (Redis Hash Store Package Extraction) | 9 | 9 | `●` |
| `SK.02.RedisPubSubExtraction` | Phase 36 (Redis Pub/Sub and Invalidation Package Extraction) | 9 | 9 | `●` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-05-14] Sub state-map initialized — phase key registry, all 6 phases scaffolded at ○ (35 tasks total)
- [2026-05-15] D-01→D-06 → ● in SK.02.Design — all interface contracts, CachePolicy shape, DI signatures, and NuGet versions confirmed (state-map-phase)
- [2026-05-15] S-01→S-06 → ● in SK.02.Scaffold — NuGet refs, folder structure, solution registration, and xUnit test stubs all complete (state-map-phase)
- [2026-05-15] C-01→C-09 → ● in SK.02.Core — FusionCache L1/L2, RedLock, DI extensions, STJ context, L1 fallback all implemented (state-map-phase)
- [2026-06-11] RDL-01→RDL-10 → ● in SK.02.RedisLockingExtraction — SharedKernel.Caching.Redis.DistributedLocking package created; RedLock.net distributed locking extracted from SharedKernel.Caching.Redis (state-map-phase)
- [2026-05-15] T-01→T-06 → ● in SK.02.Tests — unit + integration tests all passing (80 tests, 0 failed) (state-map-phase)
- [2026-05-15] DO-01→DO-04 → ● in SK.02.Docs — XML docs verified, README with usage examples and config reference written (state-map-phase)
- [2026-05-15] P-01→P-04 → ● in SK.02.Published — NuGet metadata added, packages packed to local feed, consumer dependency graph verified (state-map-phase)
- [2026-05-18] Phase 5, 6, 7, 12 planned — abstractions package split, CacheKeyProvider, RedisChannelService, RedisHashService, invalidation bus (WO-003)
- [2026-05-18] Phase 5 → ● — SharedKernel.Caching.Abstractions created: 11 deliverables complete, 0 infra NuGet refs, build clean (state-map-phase)
- [2026-05-18] Phase 6 → ● — SharedKernel.Caching refactored to abstractions; CacheKeyProvider + ServiceName validation added; 66 tests passing (state-map-phase)
- [2026-05-18] Phase 7 → ● — SharedKernel.Caching.Redis refactored to abstractions; RedisChannelService + RedisHashService added; 51 tests passing (state-map-phase)
- [2026-05-18] Phase 12 → ● — RedisCacheInvalidationBus, CacheInvalidationReceiver, CacheInvalidationExtensions implemented; [JsonConstructor] added to CacheInvalidationMessage primary ctor; 62 Redis tests + 66 Caching tests passing (caching-phase-implementer)
- [2026-05-18] State-map structural refactor — Phases 5/6/7/12 migrated to standard format: SK.02.* phase keys added to registry, section headers converted to `## Phase: Name <!-- phase-key: SK.02.Key -->`, task tables added (all ●), Overall Progress phase-key column filled, Package Board Redis row updated, root P-006/P-007 closed to ●
- [2026-05-18] Phase 14 planned (WO-004) — SharedKernel.Caching renamed to SharedKernel.Caching.FusionCache; CachePolicy.NeverExpire preset added to Abstractions (caching-arch-planner)
- [2026-05-20] Phase 14 → ● — SharedKernel.Caching renamed to SharedKernel.Caching.FusionCache; namespaces migrated; Redis/test refs updated; CachePolicy.NeverExpire added; 70 FusionCache + 62 Redis tests passing (caching-phase-implementer)
- [2026-05-20] Phases 15 + 16 planned (WO-005) — Phase 15: fix FusionCache STJ AOT gap + ITypedHashStore; Phase 16: Brotli L2 compression; protobuf-net declined (not AOT-compatible, invasive); BrotliStream memory-bypass deferred (arch-lead)
- [2026-05-20] H-01→H-08 → ● in SK.02.AotHardening — SerializerContext option wired; ITypedHashStore<T> + TypedHashStore<T> + AddTypedHashStore<T> implemented; 77 FusionCache + 74 Redis tests passing (state-map-phase)
- [2026-05-20] B-01→B-05 → ● in SK.02.BrotliCompression — BrotliCacheSerializer decorator + AddBrotliCompression extension; 91 FusionCache + 74 Redis tests passing (state-map-phase)
- [2026-05-20] Phases 17–21 planned (WO-006) — Ph17: Redis→FusionCache layering fix via CachingCoreOptions; Ph18: AddRedisL2 silent serializer override AOT fix; Ph19: DI ergonomics (ICachingBuilder overload, startup guards); Ph20: L1SizeLimit wiring + L2 KeyPrefix verification; Ph21: GetOrSetAsync ValueTask factory + nullable overload (caching-arch-planner)
- [2026-05-20] L-01→L-08 → ● in SK.02.LayeringFix — CachingCoreOptions added to Abstractions; Redis ProjectRef to FusionCache removed; 91+74 tests passing (state-map-phase)
- [2026-05-20] AS-01→AS-05 → ● in SK.02.AotSerializerFix — AddRedisL2 no longer calls WithSystemTextJsonSerializer; uses WithRegisteredSerializer; regression tests added; 95+74 tests passing (state-map-phase)
- [2026-05-20] SL-01→SL-07 → ● in SK.02.L1SizeLimit — L1SizeLimit wired via WithMemoryCache(SizeLimit)+Size=1; L2 key format verified as {KeyPrefix}v2:{user-key}; 95+93 tests passing (state-map-phase)
- [2026-05-20] DE-01→DE-06 → ● in SK.02.DiErgonomics — ICachingBuilder overload for AddRedisDistributedLocking; IServiceCollection overload marked [Obsolete]; guards on AddRedisChannelService + AddCacheInvalidationReceiver; 95+89 tests passing (state-map-phase)
- [2026-05-20] VT-01→VT-08 → ● in SK.02.ValueTaskFactory — factory migrated to ValueTask{T}; negative-result caching via GetOrSetAsync{T?}; 102 FusionCache + 93 Redis tests passing (state-map-phase)
- [2026-05-21] Phases 22–31 planned (WO-007) — Ph22: batch GetManyAsync/SetManyAsync + Redis pipeline helper; Ph23: IRenewableLock heartbeat + KeepAliveAsync; Ph24: CachePolicy.Sliding + SlidingWindow; Ph25: CachePolicy.KeyVersion + ICacheKeyProvider version overload; Ph26: RedisChannelService reconnect resilience + ConnectionHealthState; Ph27: AddCachingCoreOptions standalone DI in Abstractions; Ph28: ICacheWarmupStrategy + CacheWarmupHostedService; Ph29: ITenantCacheKeyProvider multi-tenant key isolation; Ph30: Polly v8 circuit breaker opt-in for Redis L2; Ph31: OTel System.Diagnostics.Metrics instruments on FusionCacheService (caching-arch-planner)
- [2026-05-21] BA-01→BA-08 → ● in SK.02.BatchOperations — GetManyAsync/SetManyAsync on ICacheService; FusionCacheService loop impl; IRedisL2BatchService pipeline helper; FakeCacheService updated; 114 FusionCache + 99 Redis tests passing (state-map-phase)
- [2026-05-21] RL-01→RL-07 → ● in SK.02.RenewableLock — IRenewableLock + AcquireRenewableAsync + RedLockRenewableLock + KeepAliveAsync extension + FakeDistributedLockService/FakeRenewableLock in Testing; 114 FusionCache + 125 Redis tests passing (state-map-phase)
- [2026-05-21] SE-01→SE-06 → ● in SK.02.SlidingExpiration — CachePolicy.SlidingWindow + Sliding() factory; BuildEntryOptions approximation via SetMemoryCacheDuration; NeverExpire guard; 134 FusionCache tests passing (state-map-phase)
- [2026-05-21] KV-01→KV-08 → ● in SK.02.KeyVersioning — CachePolicy.KeyVersion + WithVersion(int); ICacheKeyProvider versioned overload; CacheKeyProvider :v{n} suffix logic; 160 FusionCache + 125 Redis tests passing (state-map-phase)
- [2026-05-21] CR-01→CR-06 → ● in SK.02.ChannelReconnect — ConnectionHealthState enum; IRedisChannelService.ConnectionHealth; ConnectionRestored replay; ConnectionFailed transition; 160 FusionCache + 132 Redis tests passing (state-map-phase)
- [2026-05-21] CO-01→CO-05 → ● in SK.02.CachingCoreOptionsDi — AddCachingCoreOptions on IServiceCollection in Abstractions; Microsoft.Extensions.Options explicit ref added; RedisCacheInvalidationBus warns when ServiceName is default "app"; 160 FusionCache + 142 Redis tests passing (state-map-phase)
- [2026-05-21] CW-01→CW-06 → ● in SK.02.CacheWarmup — ICacheWarmupStrategy in Abstractions; CacheWarmupHostedService (IHostedLifecycleService); WaitForWarmup option; AddCacheWarmup<T> extension; 172 FusionCache + 142 Redis tests passing (state-map-phase)
- [2026-05-22] TK-01→TK-06 → ● in SK.02.TenantCacheKey — ITenantCacheKeyProvider in Abstractions; TenantCacheKeyProvider + AddTenantCacheKeyProvider in FusionCache; FakeTenantCacheKeyProvider in Testing; 196 FusionCache tests passing (state-map-phase)
- [2026-05-22] RCB-01→RCB-06 → ● in SK.02.RedisCircuitBreaker — Polly.Core 8.5.2 added; CircuitBreakerOptions nested class; ResiliencePipeline singleton when Enabled=true; 196 FusionCache + 154 Redis tests passing (state-map-phase)
- [2026-05-22] OM-01→OM-09 → ● in SK.02.OtelMeters — static Meter+5 instruments; FusionCache events for hit/miss/eviction; factory Stopwatch; 209 FusionCache tests passing (state-map-phase)
- [2026-06-11] Phases 32–36 planned (WO-023) — split SharedKernel.Caching.Redis into 5 packages: Ph32 SharedKernel.Caching.Redis.Core (IConnectionMultiplexer registration, RedisConnectionHealthTracker, AddRedisCircuitBreaker — dependency root); Ph33 SharedKernel.Caching.Redis slimmed to L2-only (AddRedisL2 sources multiplexer/pipeline from Core); Ph34 SharedKernel.Caching.Redis.DistributedLocking (RedLock.net extraction); Ph35 SharedKernel.Caching.Redis.HashStore (IRedisHashService/ITypedHashStore<T> extraction); Ph36 SharedKernel.Caching.Redis.PubSub (IRedisChannelService/ICacheInvalidationBus extraction, stays in 02.Caching not 07.Messaging). All Abstractions contracts unchanged; all existing AddRedis* fluent shapes preserved (caching-arch-planner)
- [2026-06-11] RC-01→RC-09 → ● in SK.02.RedisConnectionCore — SharedKernel.Caching.Redis.Core created (RedisConnectionOptions, RedisCircuitBreakerOptions, RedisConnectionHealthTracker, AddRedisConnection, AddRedisCircuitBreaker); registered in slnx; 33 tests passing (state-map-phase)
- [2026-06-11] RL2-01→RL2-08 → ● in SK.02.RedisL2Refactor — SharedKernel.Caching.Redis adds ProjectReference to .Redis.Core; AddRedisL2 sources IConnectionMultiplexer via AddRedisConnection and circuit breaker via AddRedisCircuitBreaker; RedisL2Options.CircuitBreaker retyped to RedisCircuitBreakerOptions (Core, source-compat); Polly.Core direct ref retained (RedisHashService/RedisChannelService still inject ResiliencePipeline); 154 Redis + 33 Redis.Core tests passing (state-map-phase)
- [2026-06-12] RHS-01→RHS-09 → ● in SK.02.RedisHashExtraction — SharedKernel.Caching.Redis.HashStore package created; RedisHashService/TypedHashStore<T>/AddRedisHashService/AddTypedHashStore<T> extracted from SharedKernel.Caching.Redis (pure namespace rename, no Obsolete shim needed); startup guard message updated to reference AddRedisConnection; ResiliencePipeline resolution unchanged; 68 Redis + 41 Redis.DistributedLocking + 30 Redis.HashStore + 33 Redis.Core tests passing (state-map-phase)
- [2026-06-12] RPS-01→RPS-09 → ● in SK.02.RedisPubSubExtraction — SharedKernel.Caching.Redis.PubSub package created; RedisChannelService/RedisCacheInvalidationBus/CacheInvalidationReceiver/AddRedisChannelService/AddRedisCacheInvalidationBus/AddCacheInvalidationReceiver extracted from SharedKernel.Caching.Redis (pure namespace rename, Phase 26 reconnect logic relocated intact); SharedKernel.Caching.Redis slimmed to L2-only end state (Hosting.Abstractions + Polly.Core PackageReferences removed); 02.Caching/CLAUDE.md updated; 28 Redis + 41 Redis.DistributedLocking + 30 Redis.HashStore + 33 Redis.Core + 41 Redis.PubSub tests passing — WO-023 (Phases 32-36) fully complete (state-map-phase)
