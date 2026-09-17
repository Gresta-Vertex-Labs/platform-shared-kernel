# SharedKernel.Caching.FusionCache

The FusionCache implementation of `ICacheService`, `ITenantCacheService` and the cache key providers from [`SharedKernel.Caching.Abstractions`](https://www.nuget.org/packages/SharedKernel.Caching.Abstractions).

It provides an in-process memory cache with stampede protection, fail-safe, eager refresh, tags, and factory-controlled caching. Add `SharedKernel.Caching.Redis` (`AddRedisL2`) for the Redis distributed layer and backplane.

## Install

```text
dotnet add package SharedKernel.Caching.FusionCache
```

## Registration

```csharp
builder.Services
    .AddSharedKernelCaching(o => o.ServiceName = "orders")  // required, validated at startup
    .AddTenantCacheService();                               // optional: ITenantCacheService
```

`ServiceName` prefixes every key and has no default, so two services sharing a Redis instance can never collide on a forgotten default. It must be 1 to 64 lowercase ASCII letters, digits, `.`, `_` or `-`, starting with a letter or digit.

| Registered | Implementation |
| --- | --- |
| `ICacheService` | FusionCache-backed service |
| `ICacheKeyProvider`, `ITenantCacheKeyProvider` | One key provider using `CachingOptions.ServiceName` |
| `ITenantCacheService` | Via `AddTenantCacheService()` |

For usage, policies, tenant isolation and the key format, see the [`SharedKernel.Caching.Abstractions` README](https://www.nuget.org/packages/SharedKernel.Caching.Abstractions).

## Options (`CachingOptions`)

| Property | Default | Description |
| --- | --- | --- |
| `ServiceName` | _(required)_ | Key prefix; see above |
| `L1SizeLimit` | `10000` | Maximum number of memory-cache entries |
| `CacheName` | `"default"` | FusionCache instance name |
| `WaitForWarmup` | `false` | Hold readiness until every `ICacheWarmupStrategy` has run |
| `SerializerContext` | `null` | Source-generated `JsonSerializerContext` for distributed entries; required for NativeAOT |

## Optional features

| Call | Effect |
| --- | --- |
| `AddBrotliCompression(o => …)` | Compresses distributed entries above a size threshold |
| `AddCacheEncryption()` | AES-GCM encrypts every value, bound to its key; requires `ISymmetricEncryptionService` from `SharedKernel.Cryptography`. Call after `AddBrotliCompression` |
| `AddCacheWarmup<TStrategy>()` | Runs an `ICacheWarmupStrategy` at startup |

## Telemetry

The meter and activity source are both named `SharedKernel.Caching`:

- **Metrics:** `cache.hits`, `cache.misses`, `cache.factory.duration`, `cache.errors`, `cache.evictions`.
- **Spans:** `cache.get`, `cache.set`, `cache.get_or_set`, tagged with the key prefix only, never the id.

## Layering

```text
SharedKernel.Caching.FusionCache  →  SharedKernel.Caching.Abstractions, SharedKernel.Primitives, SharedKernel.Cryptography
```

Target framework: `net10.0`.
