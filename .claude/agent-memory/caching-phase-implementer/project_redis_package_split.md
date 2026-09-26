---
name: project-redis-package-split
description: WO-023 Redis package split (Phases 32-36, COMPLETE) — SharedKernel.Caching.Redis.Core foundational pattern, Phase 33 L2 consumption pattern, Phase 34/35/36 standalone-extraction pattern
metadata:
  type: project
---

> WO-086 (2026-09): fakes moved from `SharedKernel.Testing` to `16.Testing/SharedKernel.Caching.Testing` (`FakeCacheService`, `FakeDistributedLockService`); Redis Testcontainers fixtures live in `SharedKernel.Testing.Internal`; the packages are now Adapter tier with the declared edge `Redis.*`→`Redis.Core`. RedLock.net, `FakeRenewableLock`, `CachingCoreOptions`/`AddCachingCoreOptions` and the invalidation bus were deleted (P-547/P-550).

# WO-023 — Redis Package Split (Phases 32-36) — COMPLETE

`SharedKernel.Caching.Redis` was split into 5 packages. Phase 32 (complete,
2026-06-11) created the dependency root: `SharedKernel.Caching.Redis.Core`. Phase 33
(complete, 2026-06-11) refactored `AddRedisL2` to consume it. Phase 34 (complete,
2026-06-11) extracted `SharedKernel.Caching.Redis.DistributedLocking` as a brand-new
standalone sibling package. Phase 35 (complete, 2026-06-12) extracted
`SharedKernel.Caching.Redis.HashStore` the same way. Phase 36 (complete, 2026-06-12)
extracted `SharedKernel.Caching.Redis.PubSub` — final phase, WO-023 now fully done.
Final test counts: 28 Redis + 41 Redis.DistributedLocking + 30 Redis.HashStore + 33
Redis.Core + 41 Redis.PubSub. See [[project_caching_domain]] for the broader package
architecture. No further phases planned for this domain as of 2026-06-12.

## Phase 34 — established extraction pattern (apply same shape to 35-36)

Phase 34 (`SharedKernel.Caching.Redis.DistributedLocking`) is the template for "lift an
entire capability out of `.Redis` into its own package," as opposed to Phase 33's
"refactor an in-place capability to consume Core."

- New `.csproj` references only `SharedKernel.Caching.Abstractions` +
  `SharedKernel.Caching.Redis.Core` (+ the capability's own third-party package, e.g.
  `RedLock.net`). Never references `SharedKernel.Caching.Redis` itself.
- Relocated implementation files get a namespace rename from
  `SharedKernel.Caching.Redis.*` → `SharedKernel.Caching.Redis.{Role}.*` — pure rename,
  zero behavioral change. C# parent-namespace visibility means a type in
  `SharedKernel.Caching.Redis.DistributedLocking` (no suffix) is visible without
  `using` from `SharedKernel.Caching.Redis.DistributedLocking.Implementations` — used
  this to avoid an extra `using` for `RedLockRenewableLock` from
  `RedLockDistributedLockService`.
- The extension method that previously did inline `ConfigurationOptions.Parse` +
  `TryAddSingleton<IConnectionMultiplexer>` now calls
  `services.AddRedisConnection(connectionString, o => o.ConnectTimeoutMs = ...)` —
  same one-line replacement as Phase 33, just inside the newly-relocated extension.
- The `[Obsolete]` `IServiceCollection` shim's `internal sealed class
  ...CachingBuilder(IServiceCollection) : ICachingBuilder` adapter is recreated as the
  new package's OWN tiny copy (e.g. `RedisLockCachingBuilder`) — confirmed this is the
  right call, not shared via project reference.
- Old test files relocate as **pure namespace-only renames** — change only the
  `namespace` declaration and any `using SharedKernel.Caching.Redis.*Extensions;` to
  the new `.{Role}.*` namespace. Test logic, assertions, `[Collection("Redis")]`
  attributes, and even pre-existing `[Obsolete]`-API call sites (CS0618 warnings) are
  preserved verbatim — "pure move" semantics means the warning existed before and
  still exists after, by design.
- Test doubles in `16.Testing` (`FakeDistributedLockService`/`FakeRenewableLock`) are
  NOT moved — they reference only `Abstractions` and are referenced via
  `ProjectReference` from the new `.Tests` project same as before.
- After extraction, `SharedKernel.Caching.Redis.csproj` drops the capability's
  third-party `PackageReference` (`RedLock.net`) entirely — it has zero remaining
  references to that technology.

## Phase 35 — HashStore extraction (confirms Phase 34 pattern + one new finding)

`SharedKernel.Caching.Redis.HashStore` (`RedisHashService`, `TypedHashStore<T>`,
`AddRedisHashService`, `AddTypedHashStore<T>`) extracted cleanly following the Phase 34
template — confirms the template generalizes. Two notable points:

- **No `[Obsolete]` shim needed.** Unlike `AddRedisDistributedLocking` (Phase 34),
  `AddRedisHashService`/`AddTypedHashStore<T>` were already `ICachingBuilder`-only
  before this phase (added Phase 15/19, post-dating the `IServiceCollection`-era APIs
  that needed shims). Don't assume every extraction needs a shim — check the
  extension's existing signature first.
- **Hidden cross-package test coverage — grep before relocating, not just the
  File-Level Plan's listed files.** The donor package's test project
  (`SharedKernel.Caching.Redis.Tests`) had `AddRedisHashService`-related tests in TWO
  files that were NOT in the Phase 35 File-Level Plan: `RedisDiRegistrationTests.cs`
  (3 DI-registration tests) and `CircuitBreakerTests.cs` (1
  `ResiliencePipeline`-injection test). Both broke with CS1061 once the extension
  method moved packages. Fix pattern: (1) `dotnet build` the donor `.Tests` project
  after removing the relocated source files — compile errors surface every hidden
  reference; (2) remove the broken tests from the donor; (3) recreate equivalent
  coverage as a NEW file in the new package's `.Tests` project (here:
  `RedisHashServiceDiTests.cs`, 6 tests covering registration, the updated guard
  message, idempotency, and both with/without circuit-breaker `ResiliencePipeline`
  resolution). This is now a standing step for Phase 36: grep `.Redis.Tests` for
  `AddRedisChannelService`/`AddRedisCacheInvalidationBus`/`AddCacheInvalidationReceiver`
  usages outside the files the arch-lead's File-Level Plan lists.
- Test count math after extraction: donor tests minus relocated minus
  removed-hidden-coverage = new donor total (96 − 24 − 4 = 68 for `.Redis.Tests`);
  new package total = relocated + new DI tests (24 + 6 = 30 for `.HashStore.Tests`).
  Useful sanity check before reporting final numbers.

## Phase 36 — PubSub extraction (3rd generalization of Phase 34 pattern, final phase)

`SharedKernel.Caching.Redis.PubSub` (`RedisChannelService`, `RedisCacheInvalidationBus`,
`CacheInvalidationReceiver`, `AddRedisChannelService`, `AddRedisCacheInvalidationBus`,
`AddCacheInvalidationReceiver`) extracted cleanly following the Phase 34/35 template —
confirms the template generalizes a 3rd time. New findings:

- **`CachingCoreOptionsDiTests.cs` full-file relocation decision.** This donor file
  mixed two concerns: generic `AddCachingCoreOptions` tests (CO-04, applicable to any
  package) and `RedisCacheInvalidationBus`-internal-type-dependent tests (CO-03,
  pub/sub-specific). Decision: relocate the ENTIRE file to the new package rather than
  split it — avoids duplicating generic `AddCachingCoreOptions` coverage across
  packages, and the Test Rules section explicitly scoped "CachingCoreOptions DI
  (pub/sub-facing)" to PubSub.Tests.
- **Hidden cross-package test coverage — 3rd occurrence, now a STANDING step.** Beyond
  the File-Level Plan, `.Redis.Tests` contained: `CachingCoreOptionsDiTests.cs` (full
  file), `DI/DiErgonomicsGuardTests.cs` (full file, pub/sub guard tests),
  `DI/RedisDiRegistrationTests.cs` (full file, `AddRedisChannelService` registration
  tests), and one test in `CircuitBreakerTests.cs`
  (`AddRedisChannelService_WithCircuitBreakerEnabled_InjectsResiliencePipeline`). All
  four were removed from `.Redis.Tests`; the `CircuitBreakerTests.cs` coverage was
  recreated as `RedisChannelServiceDiTests.cs` (2 tests) modeled on Phase 35's
  `RedisHashServiceDiTests.cs`. **Always grep the donor `.Tests` project for the
  extension methods/internal types being moved — not just the File-Level Plan's listed
  files** — before declaring relocation complete.
- **`TestCachingBuilder` cross-namespace `using` requirement.** Files placed in a
  `...Tests.DI` sub-namespace need an explicit `using SharedKernel.Caching.Redis.PubSub.Tests;`
  to reference `TestCachingBuilder`, which lives in the parent `...Tests` namespace
  (not `...Tests.DI`). C# does not search parent namespaces for unqualified type
  references across files.
- **`FakeCacheService` substitution pattern for DI integration tests.** PubSub.Tests DI
  integration tests that previously called `AddSharedKernelCaching`/`AddRedisL2` (which
  would violate the sibling-no-cross-reference rule) were rewritten as:
  `services.AddCachingCoreOptions(o => o.ServiceName = "...")` (Abstractions, zero
  FusionCache/Redis deps) + `services.AddRedisConnection(connectionString)` (.Redis.Core,
  multiplexer) + `services.AddSingleton<ICacheService, FakeCacheService>()`
  (`SharedKernel.Testing.Caching`, satisfies `AddCacheInvalidationReceiver`'s
  `ICacheService` guard) + `new TestCachingBuilder(services)` for the fluent
  `ICachingBuilder` chain. This is the template for any future package needing
  `ICacheService` in a DI test without `.FusionCache`/`.Redis`.
- No `[Obsolete]` shim needed — `AddRedisChannelService`/`AddRedisCacheInvalidationBus`/
  `AddCacheInvalidationReceiver` were already `ICachingBuilder`-only before this phase.
- Test count math: donor tests minus relocated minus removed-hidden-coverage = new
  donor total (68 − 39 − 1 = 28 for `.Redis.Tests`); new package total = relocated +
  hidden-coverage-recreated (39 + 2 = 41 for `.PubSub.Tests`).

## Phase 33 — established consumption pattern (apply same shape to 34-36)

- `SharedKernel.Caching.Redis.csproj` adds a single `<ProjectReference>` to
  `..\SharedKernel.Caching.Redis.Core\SharedKernel.Caching.Redis.Core.csproj` —
  placed directly after the `Abstractions` ProjectReference, with a one-line XML
  comment explaining what it provides (`AddRedisConnection` / `AddRedisCircuitBreaker`).
- Inside the `Add*` extension method, replace inline
  `ConfigurationOptions.Parse(...)` + `services.TryAddSingleton<IConnectionMultiplexer>(_
  => ConnectionMultiplexer.Connect(configOptions))` with a single call:
  `services.AddRedisConnection(options.ConnectionString, coreOptions => {
  coreOptions.ConnectTimeoutMs = options.ConnectTimeoutMs; });`
- Replace the inline Polly `ResiliencePipelineBuilder().AddCircuitBreaker(...)` block
  with `services.AddRedisCircuitBreaker(cbOptions => { cbOptions.Enabled = ...;
  cbOptions.FailureThreshold = ...; /* map all 5 properties */ });` — map every
  property explicitly, do not pass the options object directly (different types).
- `using SharedKernel.Caching.Redis.Core.Extensions;` is the namespace for both
  extension methods; `using SharedKernel.Caching.Redis.Core;` for the
  `RedisCircuitBreakerOptions` type itself.
- **Polly.Core direct PackageReference: KEEP, do not remove.** Even though
  `AddRedisCircuitBreaker`'s registration call site moved to `.Redis.Core`,
  `RedisHashService`/`RedisChannelService` (not relocated until Phases 35/36) inject
  `Polly.ResiliencePipeline?` directly via `sp.GetService<ResiliencePipeline>()` and
  need the `Polly` namespace to compile in this assembly. Re-evaluate per-package:
  only drop `Polly.Core` once the last Polly-injecting type leaves the package
  (i.e., at Phase 36 for `.Redis` itself, if nothing else needs it by then).
- Test-side change: only the one explicit-construction site
  (`new RedisL2Options.CircuitBreakerOptions { ... }` → `new RedisCircuitBreakerOptions
  { ... }`) needs updating; all `opts.CircuitBreaker.X` / `o.CircuitBreaker.Enabled =
  true` property-access usages compile unchanged (source-compat type relocation).
  Add `using SharedKernel.Caching.Redis.Core;` to any test file referencing
  `RedisCircuitBreakerOptions` by name.
- No test csproj changes needed — `SharedKernel.Caching.Redis.Core` types reach the
  `.Tests` project transitively via the main project's new ProjectReference.

## SharedKernel.Caching.Redis.Core — established shape (Phase 32)

- `RedisConnectionOptions` — `ConnectionString` (`[Required]`), `ConnectTimeoutMs`
  (`[Range(100,60000)]`, default 5000). Canonical shape — Phases 33-36 bind to this,
  never redeclare.
- `RedisCircuitBreakerOptions` — top-level (not nested) generalization of Phase 30's
  `RedisL2Options.CircuitBreakerOptions`. Same 5 properties/defaults/`[Range]` rules.
- `RedisConnectionHealthTracker` — **passive observer only**. Subscribes to
  `ConnectionRestored`/`ConnectionFailed` in ctor, exposes `ConnectionHealthState
  ConnectionHealth { get; }` via `volatile int _connectionHealth`. `internal
  OnConnectionRestored`/`OnConnectionFailed` exposed via `InternalsVisibleTo` for test
  simulation. Does NOT do resubscription/replay — that stays in `RedisChannelService`
  (Phase 36) as its own independent mechanism. Both can coexist on the same
  multiplexer (SE.Redis supports multiple event subscribers).
- `AddRedisConnection(this IServiceCollection, connectionString, configure?)` and
  `AddRedisCircuitBreaker(this IServiceCollection, configure?)` are **plain
  `IServiceCollection` extensions, NOT `ICachingBuilder`**. This is a deliberate
  departure from the existing `ICachingBuilder`-fluent convention documented in
  [[project_di_conventions]] — Redis.Core sits below the builder abstraction;
  consuming packages (.Redis, .DistributedLocking, .HashStore, .PubSub) wrap these
  calls internally and expose their own `ICachingBuilder` fluent surface.
- `ConnectionHealthState` enum is sourced from `SharedKernel.Caching.Abstractions`
  (Phase 26) — never duplicate it in Redis.Core.

## Constraints for Phases 33-36 (from arch-lead spec, verify against current
02.Caching/CLAUDE.md before each phase — this is a snapshot)

- No new `IConnectionMultiplexer` registrations outside `.Redis.Core`'s
  `AddRedisConnection`. Every `Add*` that needs a multiplexer calls
  `AddRedisConnection` (idempotent via `TryAddSingleton`).
- Sibling packages (.Redis, .DistributedLocking, .HashStore, .PubSub) never
  reference each other — only `.Redis.Core` + `Abstractions`.
- All public `Add*` DI signatures preserved exactly. `RedisL2Options.CircuitBreaker`
  changes type to `RedisCircuitBreakerOptions` (Core) — type relocation, not rename;
  `o.CircuitBreaker.Enabled = true` still compiles.
- `[Obsolete]` `IServiceCollection` shims: each package defines its OWN small
  `internal sealed class ...CachingBuilder(IServiceCollection) : ICachingBuilder`
  adapter — do not share across packages via project reference (avoid unjustified
  inter-package dependency for ~3 lines of code).
- Phase 26 reconnect/resubscribe replay logic (`Dictionary<string,
  SubscriptionEntry>` + `_registryLock`, `volatile int _connectionHealth`,
  `OnConnectionRestored`/`OnConnectionFailed`) relocates **intact** with
  `RedisChannelService` to `.Redis.PubSub` (Phase 36) — NOT refactored to depend on
  `RedisConnectionHealthTracker`. Two independent health-tracking mechanisms coexist
  by design.
