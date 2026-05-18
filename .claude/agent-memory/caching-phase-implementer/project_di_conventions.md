---
name: project-di-conventions
description: ICachingBuilder DI extension patterns and IConnectionMultiplexer registration rules established in 02.Caching
metadata:
  type: project
---

# DI Registration Conventions — 02.Caching

## ICachingBuilder pattern
- `AddSharedKernelCaching(this IServiceCollection)` returns `ICachingBuilder` (concrete: `CachingBuilder` in SharedKernel.Caching)
- All subsequent Redis registrations are extensions on `ICachingBuilder`, NOT on `IServiceCollection`
- Pattern: `AddRedisL2`, `AddRedisDistributedLocking` (exception — still on IServiceCollection), `AddRedisChannelService`, `AddRedisHashService`, `AddRedisCacheInvalidationBus`, `AddCacheInvalidationReceiver`

## IConnectionMultiplexer registration
- BOTH `AddRedisL2` AND `AddRedisDistributedLocking` call `TryAddSingleton<IConnectionMultiplexer>` — first caller wins (idempotent)
- `AddRedisHashService` guards on `IConnectionMultiplexer` presence: throws `InvalidOperationException` with exact message: `"AddRedisHashService requires AddRedisDistributedLocking or AddRedisL2 to be called first to register IConnectionMultiplexer."`
- `AddRedisCacheInvalidationBus` (Phase 12) guards on `IRedisChannelService` presence

## TryAddSingleton usage
- All service registrations use `TryAddSingleton` for idempotency — calling twice does not duplicate
- Exception: `AddRedisDistributedLocking` is on `IServiceCollection` (not `ICachingBuilder`) for standalone use without L1 cache

## Why AddRedisL2 registers IConnectionMultiplexer
- Phase 7 decision: AddRedisL2 needed to support downstream `AddRedisHashService` and `AddRedisChannelService` calls without requiring users to also call `AddRedisDistributedLocking`
