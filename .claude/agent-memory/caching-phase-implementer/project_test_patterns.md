---
name: project-test-patterns
description: Testcontainers setup, TestCachingBuilder helper, namespace migration lessons for 02.Caching Redis tests
metadata:
  type: project
---

> WO-086 (2026-09): the shared Redis Testcontainers fixture is `16.Testing/SharedKernel.Testing.Internal`'s `RedisContainerFixture` (`redis:7.4`); caching fakes are in `SharedKernel.Caching.Testing`/`SharedKernel.Caching.Redis.Testing`. `SharedKernel.Caching` (now `.FusionCache`) and `SharedKernel.Caching.Redis.Abstractions` no longer exist, and `IRedisL2BatchService` was deleted (Phase 40); the reconnect and batch patterns below are phase history — verify against source before reuse.

# Test Patterns — 02.Caching Redis

## Testcontainers Redis image
- Always use `redis:7-alpine` via `RedisBuilder().WithImage("redis:7-alpine").Build()`
- Testcontainers.Redis package version: **4.13.0** (bumped from a stale 4.4.0 in Phase 39/WO-050 —
  see [[project_wo050_gold_standard]] for the `MissingMethodException` regression this caused
  across all four Redis-container `.Tests` projects and why 4.13.0 is now mandatory, matching
  `16.Testing`'s own floor)
- Tests use `IAsyncLifetime` pattern with `InitializeAsync` / `DisposeAsync`

## TestCachingBuilder helper
- `TestCachingBuilder` class lives in the test project (`SharedKernel.Caching.Redis.Tests`)
- Implements `ICachingBuilder` wrapping an `IServiceCollection`
- Used to drive `ICachingBuilder` extension methods (`AddRedisChannelService`, `AddRedisHashService`) without a full FusionCache setup
- Pattern: `var builder = new TestCachingBuilder(services); builder.AddRedisChannelService();`

## Test project references
- `SharedKernel.Caching.Redis.Tests.csproj` references both:
  - `SharedKernel.Caching.Redis.csproj` (primary)
  - `SharedKernel.Caching.csproj` (for L1FallbackTests and RedisL2IntegrationTests that use AddSharedKernelCaching, CachePolicy)
  - `StackExchange.Redis 2.13.1` (for IConnectionMultiplexer assertions)

## Namespace migration lesson (Phase 6-7)
- After Phase 5, `IDistributedLockService` moved from `SharedKernel.Caching.Redis.Abstractions` to `SharedKernel.Caching.Abstractions`
- After Phase 6, `CachePolicy` no longer in `SharedKernel.Caching.Policies` — it's in `SharedKernel.Caching.Abstractions`
- Existing test files had stale `using SharedKernel.Caching.Policies` and `using SharedKernel.Caching.Redis.Abstractions` — must be removed when working on Phase 7+

## STJ source-gen context for hash tests
- Test models need their own `[JsonSerializable(typeof(T))]` context
- Pattern: `internal sealed partial class TestJsonContext : JsonSerializerContext { }` decorated with `[JsonSerializable]` attributes
- Access via `TestJsonContext.Default.TestPayload`

## Pub/Sub timing in tests
- After `SubscribeAsync`, add `await Task.Delay(100)` before publishing to allow subscription to establish
- After `UnsubscribeAsync`, add `await Task.Delay(200)` for propagation, then wait `await Task.Delay(300)` before asserting no more messages

## L2 key format — verified in Phase 20

- `Microsoft.Extensions.Caching.StackExchangeRedis` v10+ stores keys as: `{InstanceName}v2:{user-key}`
- The `v2:` schema-version separator is injected by the library AFTER `InstanceName` (= `KeyPrefix`)
- When `KeyPrefix` is empty: effective key is `v2:{user-key}`
- Use `db.KeyExistsAsync(key)` NOT `db.StringGetAsync(key)` — entries are stored as Redis Hash type, not strings
- Diagnostic pattern: create a test that `Assert.True(false, listOfAllKeys.ToString())` to discover actual key names
- After `SetAsync`, add `await Task.Delay(500)` to allow FusionCache async L2 write to propagate

## L1SizeLimit eviction tests — Phase 20 pattern

- Use `CachePolicy.For(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10))` — NOT NeverExpire (NeverExpire bypasses eviction)
- After bulk inserts, add `await Task.Delay(100)` to allow lazy eviction to trigger
- MemoryCache eviction is lazy — count checks must allow for `liveCount <= limit + 1` tolerance
- Wire: `.WithMemoryCache(_ => new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit }))` + `.WithDefaultEntryOptions(o => o.Size = 1)`
- Also set `Size = 1` in `BuildEntryOptions` — per-call options override defaults

## Batch operations pipeline tests — Phase 22 pattern

- `IRedisL2BatchService` is `internal` — access from tests via `InternalsVisibleTo` using `AssemblyAttribute` item group in `.csproj` (not `[assembly:]` attribute in source)
- Pipeline verification: use separate admin-mode `IConnectionMultiplexer` (`ConfigurationOptions.AllowAdmin = true`) for `server.InfoAsync("stats")` — the regular multiplexer does not allow INFO command
- `GetTotalCommandsProcessedAsync`: reads `total_commands_processed` from `server.InfoAsync("stats")` response
- Pipeline delta assertion: `delta <= keyCount + 2` (N GETs + 2 INFO calls) — Redis counts each pipelined GET individually server-side; the pipeline saves network round-trips, not server-side command count
- Admin multiplexer disposal: `_adminMultiplexer` is disposed in `DisposeAsync` before `_provider`
- `IDatabase.CreateBatch()` + queue tasks + `batch.Execute()` is the correct StackExchange.Redis pipeline pattern

## Channel reconnect tests — Phase 26 pattern

- **Docker `PauseAsync` does not trigger SE.Redis `ConnectionFailed`** — SIGSTOP blocks the container but SE.Redis does not detect disconnect fast enough for tests. Do NOT use pause/unpause.
- **`InternalsVisibleTo` pattern for event simulation**: make `OnConnectionRestored` and `OnConnectionFailed` `internal` methods; the test project calls them directly via positional args: `service.OnConnectionFailed(mux, null!)` / `service.OnConnectionRestored(mux, null!)`. Named `args:` parameter syntax causes CS1739 — use positional syntax.
- **`ChannelMessageQueue` is sealed** — cannot be NSubstitute mocked. Return `Task.FromResult<ChannelMessageQueue>(null!)` in the `SubscribeAsync` substitute callback. `RedisChannelService` never uses the returned queue object.
- **`NativeCommandError` / `EndPointCollection.EmptyArray` don't exist** on `IConnectionMultiplexer` NSubstitute substitute — the event args `ConnectionFailedEventArgs` parameter is passed as `null!` (safe since the event handler only reads `_multiplexer.IsConnected`, not the args).
- **Live reconnect test**: use `CLIENT KILL ID {id}` Redis command to forcibly close subscriber connections. Parse `CLIENT LIST` output for lines with `flags=S` or `flags=PS`, extract `id=` field, then `db.ExecuteAsync("CLIENT", "KILL", "ID", id)`.
- **`inReplay` flag pattern**: when a test needs a mock subscriber to throw only during replay (not initial subscribe), use a `bool inReplay = false` closure variable; flip to `true` after initial subscriptions complete.
- **Connection string for reconnect tests**: use `abortConnect=false,connectRetry=10` (not `reconnectRetryPolicy` — that keyword is unsupported and throws `ArgumentException`).
- **`SubscriptionCount` internal property**: `RedisChannelService` exposes `internal int SubscriptionCount` for test assertions about registry state.

## Assert.Throws exact-type gotcha — Phase 32 pattern

- xUnit's `Assert.Throws<TException>` requires an **exact type match** — a derived
  exception does NOT satisfy a base-type assertion.
- `ArgumentException.ThrowIfNullOrWhiteSpace(value)` throws `ArgumentNullException`
  (a subclass of `ArgumentException`) when `value` is `null`, but plain
  `ArgumentException` when `value` is empty/whitespace-but-non-null.
- When testing this BCL guard: assert `Assert.Throws<ArgumentNullException>(...)`
  for the `null` case and `Assert.Throws<ArgumentException>(...)` for the
  whitespace case — two separate `[Fact]`s, two different expected types.

## New-package test relocation — Phase 34 pattern

- When a capability is extracted into a brand-new package (e.g.
  `SharedKernel.Caching.Redis.DistributedLocking`), relocate the relevant test files
  as **pure namespace-only renames**: only the `namespace` declaration and `using`
  statements pointing at relocated extension/option types change. Test method names,
  bodies, assertions, and pre-existing `[Obsolete]`-API call sites (CS0618 warnings)
  must NOT change — preserves "pure move" semantics required by the phase spec.
- `[Collection("Redis")]` works in a brand-new test project **without** an explicit
  `[CollectionDefinition("Redis")]` class — xUnit creates an implicit named collection
  per assembly. No need to copy a `CollectionDefinition` file when relocating tests.
- Reflection-based interface contract tests (e.g.
  `IDistributedLockServiceContractTests`) have zero Redis-specific dependencies — they
  only need `SharedKernel.Caching.Abstractions` + `Xunit`, so they relocate cleanly
  into any new package's test project with just a namespace change.
- Verify old test project cleanup: after `git rm` of relocated files, check that any
  now-empty subdirectories (e.g. `Abstractions/`) are gone too — `git rm` removes
  empty dirs automatically but worth a sanity check.

## FakeCacheService in 16.Testing — Phase 22 pattern (location updated for WO-086)

- `FakeCacheService` moved from concept to implementation in Phase 22 (previously missing from 16.Testing)
- Lives at `16.Testing/SharedKernel.Caching.Testing/FakeCacheService.cs` (moved out of `SharedKernel.Testing` by WO-086)
- Uses `ConcurrentDictionary<string, object?>` for thread-safe store
- Tag tracking: `ConcurrentDictionary<string, HashSet<string>>` keyed by cache key → set of tags
- `GetManyAsync`: returns a dictionary with an entry for every requested key; missing keys → default(T?)
- `SetManyAsync`: loops `_store[key] = value` for all entries; updates tag registry per-key
- `Count` property and `Clear()` method exposed for test assertions
- `16.Testing/SharedKernel.Caching.Testing.csproj` references `SharedKernel.Caching.Abstractions`; the core `SharedKernel.Testing` references Foundation + Model only
