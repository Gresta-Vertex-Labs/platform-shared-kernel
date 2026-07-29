# SharedKernel.Caching.Abstractions

Zero-infrastructure caching abstractions for Platform.SharedKernel. Contains `ICacheService`,
`CachePolicy`, `ICacheKeyProvider`, `IDistributedLockService`/`IRenewableLock`, `IRedisChannelService`,
`IRedisHashService`/`ITypedHashStore<T>`, `ICacheInvalidationBus`, `CacheInvalidationMessage`,
`ConnectionHealthState`, `CachingCoreOptions`, `ICacheWarmupStrategy`, `ITenantCacheKeyProvider`, and
`ICachingBuilder` — the contracts every provider package
([`SharedKernel.Caching.FusionCache`](https://www.nuget.org/packages/SharedKernel.Caching.FusionCache),
`SharedKernel.Caching.Redis*`) implements. No FusionCache, StackExchange.Redis, or RedLock.net
transitive dependency, so `05.Application` and other upstream layers can depend on caching contracts
without pulling in an infrastructure package.

## Install

```
dotnet add package SharedKernel.Caching.Abstractions
```

```xml
<PackageReference Include="SharedKernel.Caching.Abstractions" Version="1.0.0" />
```

## Usage

Application code depends on `ICacheService`/`CachePolicy` only — never a concrete provider type:

```csharp
public sealed class ProductService(ICacheService cache, IProductRepository repository)
{
    public ValueTask<Product?> GetAsync(Guid id, CancellationToken ct) =>
        cache.GetOrSetAsync(
            key     : $"products:{id}",
            factory : token => repository.FindAsync(id, token),
            policy  : CachePolicy.Default,
            ct      : ct);
}
```

This package ships **no DI registration of its own** — a microservice's composition root wires a
concrete provider that satisfies these contracts, e.g.
[`SharedKernel.Caching.FusionCache`](https://www.nuget.org/packages/SharedKernel.Caching.FusionCache)'s
`AddSharedKernelCaching()`.

## Layering

```
SharedKernel.Caching.Abstractions  →  (no infrastructure dependencies)
```

Target framework: `net10.0`. AOT-compatible.

For full documentation see the [repository README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/README.md).
