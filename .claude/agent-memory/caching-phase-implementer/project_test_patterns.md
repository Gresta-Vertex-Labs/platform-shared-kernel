---
name: project-test-patterns
description: Testcontainers setup, TestCachingBuilder helper, namespace migration lessons for 02.Caching Redis tests
metadata:
  type: project
---

# Test Patterns — 02.Caching Redis

## Testcontainers Redis image
- Always use `redis:7-alpine` via `RedisBuilder().WithImage("redis:7-alpine").Build()`
- Testcontainers.Redis package version: 4.4.0
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
