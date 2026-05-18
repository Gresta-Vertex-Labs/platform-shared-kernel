---
name: project-caching-domain
description: Three-package split architecture, confirmed NuGet versions, and key implementation decisions for 02.Caching
metadata:
  type: project
---

# SharedKernel Caching Domain — Architecture & Versions

**Three-package split (established Phase 5–7):**
- `SharedKernel.Caching.Abstractions` — zero-infra contracts only; refs `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.1`
- `SharedKernel.Caching` — FusionCache L1 provider; refs Abstractions + FusionCache packages + 01.Core
- `SharedKernel.Caching.Redis` — Redis L2, RedLock, RedisChannelService, RedisHashService; refs Abstractions directly (NOT SharedKernel.Caching)

**Confirmed NuGet versions:**
- ZiggyCreatures.FusionCache: 2.6.0
- ZiggyCreatures.FusionCache.Serialization.SystemTextJson: 2.6.0
- ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis: 2.6.0
- StackExchange.Redis: 2.13.1
- RedLock.net: 2.3.2
- Microsoft.Extensions.DependencyInjection.Abstractions: 10.0.1
- Microsoft.Extensions.Caching.StackExchangeRedis: 10.0.0

**SharedKernel.Caching.Redis.csproj** explicitly includes FusionCache packages (ZiggyCreatures.FusionCache + Serialization.SystemTextJson + Backplane) because it no longer references SharedKernel.Caching transitively.

**Why:** Redis package must be independently deployable without pulling FusionCache via SharedKernel.Caching. FusionCache references are needed for AddFusionCache() and WithSystemTextJsonSerializer() in AddRedisL2.

**How to apply:** When adding features to SharedKernel.Caching.Redis that need FusionCache APIs, confirm the package has its own explicit FusionCache references.
