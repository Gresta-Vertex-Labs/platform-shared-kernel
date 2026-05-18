---
name: project-caching-state
description: Three-package split for 02.Caching, planned phases (5/6/7/12), interface canonical locations, and channel naming convention
metadata:
  type: project
---

## Three-Package Split (after WO-004 / Phase 14)

| Package | Role |
|---------|------|
| `SharedKernel.Caching.Abstractions` | Zero-infra contracts only — no FusionCache, no StackExchange.Redis |
| `SharedKernel.Caching.FusionCache` | FusionCache L1 provider + CacheKeyProvider implementation. **Previously `SharedKernel.Caching` — renamed in Phase 14 (WO-004).** |
| `SharedKernel.Caching.Redis` | Redis L2, RedLock, RedisChannelService, RedisHashService, RedisCacheInvalidationBus, CacheInvalidationReceiver |

**Why:** `05.Application` needs `ICacheService` for `CachingBehavior` but must not transitively pull in FusionCache. The abstractions package resolves this. The rename aligns with the platform `SharedKernel.{Capability}.{Provider}` convention (same as `.MassTransit`, `.EfCore`, `.Meilisearch`).

**How to apply:** Any new interface for this domain goes in `SharedKernel.Caching.Abstractions` unless it is implementation-specific (e.g., a FusionCache-specific helper stays in `SharedKernel.Caching.FusionCache`).

## Interface Canonical Locations (post P-005)

- `ICacheService` — `SharedKernel.Caching.Abstractions`
- `CachePolicy` — `SharedKernel.Caching.Abstractions`
- `ICacheKeyProvider` — `SharedKernel.Caching.Abstractions`
- `IDistributedLockService` — `SharedKernel.Caching.Abstractions` (migrated from `SharedKernel.Caching.Redis`)
- `IRedisChannelService` — `SharedKernel.Caching.Abstractions`
- `IRedisHashService` — `SharedKernel.Caching.Abstractions`
- `ICacheInvalidationBus` — `SharedKernel.Caching.Abstractions`
- `CacheInvalidationMessage` + `CacheInvalidationType` — `SharedKernel.Caching.Abstractions`
- `ICachingBuilder` — `SharedKernel.Caching.Abstractions`
- `CacheKeyProvider` — `SharedKernel.Caching` (implementation)
- `FusionCacheService` — `SharedKernel.Caching` (implementation)
- `RedLockDistributedLockService` — `SharedKernel.Caching.Redis`
- `RedisChannelService` — `SharedKernel.Caching.Redis`
- `RedisHashService` — `SharedKernel.Caching.Redis`
- `RedisCacheInvalidationBus` — `SharedKernel.Caching.Redis`
- `CacheInvalidationReceiver` — `SharedKernel.Caching.Redis`

## Phase Dependency Chain

P-005 (Abstractions) → P-006 (Caching refactor) — independent of P-007
P-005 (Abstractions) → P-007 (Redis refactor + Channel + Hash)
P-005 + P-007 → P-012 (Invalidation Bus)

## Key Design Decisions

- `IRedisHashService` typed methods take `JsonTypeInfo<T>` parameter — no reflection, AOT-safe.
- `IRedisChannelService` uses `RedisChannel.Literal` only — never pattern matching.
- `CacheInvalidationMessage.CorrelationId` defaults to `Activity.Current?.Id ?? Guid.NewGuid().ToString("N")`.
- `AddRedisHashService` throws `InvalidOperationException` if `IConnectionMultiplexer` not already registered.
- `AddRedisCacheInvalidationBus` throws `InvalidOperationException` if `IRedisChannelService` not registered.
- Broadcast invalidation (`All`) logs a structured warning and relies on TTL — no enumerate-all-keys attempt.
- All three Redis services share the single `IConnectionMultiplexer` singleton — no connection proliferation.

## Invalidation Channel Naming Convention

- Targeted: `sharedkernel:cache:invalidation:{service-name}` (lowercase, spaces → hyphens)
- Broadcast: `sharedkernel:cache:invalidation:broadcast` (literal constant)

## CachingOptions.ServiceName

New required field. Defaults to `"app"`. Validated non-null/non-whitespace. Used by `CacheKeyProvider` and `RedisCacheInvalidationBus`.

## NuGet Version Pins

- ZiggyCreatures.FusionCache: 2.6.0
- ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis: 2.6.0
- ZiggyCreatures.FusionCache.Serialization.SystemTextJson: 2.6.0
- StackExchange.Redis: 2.13.1
- RedLock.net: 2.3.2
- Microsoft.Extensions.DependencyInjection.Abstractions: 10.0.1
