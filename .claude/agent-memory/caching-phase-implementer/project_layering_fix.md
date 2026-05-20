---
name: layering-fix-patterns
description: CachingCoreOptions pattern for sharing options across sibling provider packages without cross-package references
metadata:
  type: project
---

## CachingCoreOptions pattern (Phase 17)

When a property must be accessible to two sibling provider packages (e.g. FusionCache and Redis), place it in a new `sealed class` in `SharedKernel.Caching.Abstractions` rather than having one provider reference the other.

**Why:** `SharedKernel.Caching.Redis` previously referenced `SharedKernel.Caching.FusionCache` solely to read `CachingOptions.ServiceName`. This forced FusionCache as a transitive dependency on any service using only Redis capabilities.

**How to apply:**
- Create the options class in Abstractions with no `IOptions<T>` usage in the class itself (plain POCO — `IOptions<T>` comes transitively via `Microsoft.Extensions.DependencyInjection.Abstractions`)
- Register it in `AddSharedKernelCaching` via `services.Configure<CachingCoreOptions>(o => o.ServiceName = tempOptions.ServiceName)` — copies value from `CachingOptions` to keep both in sync
- Consuming classes in Redis resolve `IOptions<CachingCoreOptions>`, not `IOptions<CachingOptions>`
- No new NuGet reference needed in Abstractions csproj — `Microsoft.Extensions.Options` is already transitive

## Sibling package rule

`SharedKernel.Caching.FusionCache` and `SharedKernel.Caching.Redis` are sibling packages at the same layer. After Phase 17, neither references the other. Both reference only `SharedKernel.Caching.Abstractions`.

## Test file update pattern

When a test directly constructs `CacheInvalidationReceiver` or `RedisCacheInvalidationBus` (bypassing DI), switch `AddOptions<CachingOptions>` to `AddOptions<CachingCoreOptions>` and `IOptions<CachingOptions>` to `IOptions<CachingCoreOptions>`. Keep `using SharedKernel.Caching.FusionCache.Extensions` in test files that call `AddSharedKernelCaching` — tests act as composition roots.
