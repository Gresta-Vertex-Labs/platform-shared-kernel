# SharedKernel.Caching

Hybrid L1/L2 cache abstraction for .NET 10 microservices powered by **ZiggyCreatures.FusionCache**.
Provides `ICacheService` with stampede protection, background eager-refresh, fail-safe stale serving,
and tag-based eviction. L1-only by default; add
[`SharedKernel.Caching.Redis`](https://www.nuget.org/packages/SharedKernel.Caching.Redis) to opt in
to a Redis distributed backplane.

## Quick Start

```csharp
// Program.cs — L1-only (in-process memory cache)
builder.Services.AddSharedKernelCaching();
```

```csharp
// Inject ICacheService — always use GetOrSetAsync for stampede protection.
public sealed class ProductService(ICacheService cache)
{
    public Task<Product?> GetAsync(Guid id, CancellationToken ct) =>
        cache.GetOrSetAsync(
            key     : $"products:{id}",
            factory : token => LoadFromDbAsync(id, token),
            policy  : CachePolicy.Default,
            ct      : ct);
}
```

## CachePolicy

`CachePolicy` is a sealed immutable record. Customise via fluent factory methods:

```csharp
var policy = CachePolicy
    .For(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(20))
    .WithTags("tenant:acme", "entity:product")
    .WithEagerRefresh(0.75);
```

| Preset | L1 TTL | L2 TTL | Fail-safe | Eager-refresh |
|--------|--------|--------|-----------|---------------|
| `CachePolicy.Default` | 5 min | 30 min | enabled | 0.9 |

## Tag-Based Eviction

```csharp
await cache.SetAsync(key, value, CachePolicy.Default.WithTags("tenant:acme"), ct);
await cache.RemoveByTagAsync("tenant:acme", ct);
```

## Configuration (`SharedKernelCaching` section)

| Property | Default | Description |
|----------|---------|-------------|
| `L1SizeLimit` | `10000` | Max items in the in-process cache |
| `CacheName` | `"default"` | FusionCache instance name |

## Layering

```
SharedKernel.Caching  →  SharedKernel.Core
```

Target framework: `net10.0`. AOT-compatible.

For full documentation see the [repository README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/README.md).
