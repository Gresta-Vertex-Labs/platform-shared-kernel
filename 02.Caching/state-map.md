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

---

## Active Work

_Nothing in progress._

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
| `SharedKernel.Caching.Abstractions` | Phase 14 | `○` | `CachePolicy.NeverExpire` preset to be added; all prior contracts complete |
| `SharedKernel.Caching.FusionCache` | Phase 14 | `○` | Renamed from `SharedKernel.Caching`; namespace migration pending |
| `SharedKernel.Caching.Redis` | Phase 12 | `●` | Refactored to Abstractions; RedisChannelService, RedisHashService, RedisCacheInvalidationBus, CacheInvalidationReceiver — all implemented; 62 tests passing |

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
| F-01 | Rename folder `02.Caching/SharedKernel.Caching/` → `02.Caching/SharedKernel.Caching.FusionCache/` | SharedKernel.Caching.FusionCache | `○` |
| F-02 | Rename `.csproj` to `SharedKernel.Caching.FusionCache.csproj`; update `<PackageId>` to `SharedKernel.Caching.FusionCache`; update `<AssemblyName>` if explicitly set; update `<Description>` metadata | SharedKernel.Caching.FusionCache | `○` |
| F-03 | Update `InternalsVisibleTo` attribute to reference `SharedKernel.Caching.FusionCache.Tests` | SharedKernel.Caching.FusionCache | `○` |
| F-04 | Rename nested test project folder and `.csproj` to `SharedKernel.Caching.FusionCache.Tests` | SharedKernel.Caching.FusionCache.Tests | `○` |
| F-05 | Migrate all namespaces inside the package from `SharedKernel.Caching.*` to `SharedKernel.Caching.FusionCache.*` (implementation namespace only — `SharedKernel.Caching.Abstractions` namespace is unchanged) | SharedKernel.Caching.FusionCache | `○` |
| F-06 | Update `<ProjectReference>` in `SharedKernel.Caching.Redis.csproj` to point to `SharedKernel.Caching.FusionCache.csproj` | SharedKernel.Caching.Redis | `○` |
| F-07 | Update `<ProjectReference>` in `consumer-verify` project to reference renamed package | consumer-verify | `○` |
| F-08 | Update `Platform.SharedKernel.slnx` solution folder entry from `SharedKernel.Caching` to `SharedKernel.Caching.FusionCache` | Solution | `○` |
| F-09 | Add `CachePolicy.NeverExpire` static property to `CachePolicy` in `SharedKernel.Caching.Abstractions`; map to `TimeSpan.MaxValue` for both L1 and L2 durations; `FailSafeEnabled = true`; no `EagerRefreshThreshold`; full XML doc | SharedKernel.Caching.Abstractions | `○` |
| F-10 | Add test covering `CachePolicy.NeverExpire` property values (both durations `TimeSpan.MaxValue`, `FailSafeEnabled = true`, no `EagerRefreshThreshold`) | SharedKernel.Caching.FusionCache.Tests | `○` |
| F-11 | Verify all existing tests pass under the new package name — zero regressions | Both | `○` |

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

> Counts updated whenever a task state changes. Total tasks: 35 (existing phases) + 4 phases planned (P-005, P-006, P-007, P-012).

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
| `SK.02.FusionCacheRename` | Phase 14 (FusionCache Rename + NeverExpire) | 11 | 0 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-05-14] Sub state-map initialized — phase key registry, all 6 phases scaffolded at ○ (35 tasks total)
- [2026-05-15] D-01→D-06 → ● in SK.02.Design — all interface contracts, CachePolicy shape, DI signatures, and NuGet versions confirmed (state-map-phase)
- [2026-05-15] S-01→S-06 → ● in SK.02.Scaffold — NuGet refs, folder structure, solution registration, and xUnit test stubs all complete (state-map-phase)
- [2026-05-15] C-01→C-09 → ● in SK.02.Core — FusionCache L1/L2, RedLock, DI extensions, STJ context, L1 fallback all implemented (state-map-phase)
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
