# SharedKernel.Caching

Hybrid L1 / L2 caching for .NET 10 microservices backed by **ZiggyCreatures.FusionCache**.
Provides stampede protection, background eager-refresh, fail-safe stale serving, and an optional
Redis distributed backplane. A separate package (`SharedKernel.Caching.Redis`) adds Redis L2 and
RedLock-based distributed locking — services that only need in-process L1 cache stay Redis-free.

---

## Packages

| Package | Purpose |
|---------|---------|
| `SharedKernel.Caching` | Core abstraction (`ICacheService`, `CachePolicy`) + FusionCache L1 wiring |
| `SharedKernel.Caching.Redis` | Redis L2 backplane + `IDistributedLockService` via RedLock.net |

---

## Quick Start

### 1 — L1-Only (in-process memory cache)

```csharp
// Program.cs
builder.Services
    .AddSharedKernelCaching();
```

Inject `ICacheService` anywhere:

```csharp
public sealed class ProductService(ICacheService cache)
{
    public async Task<Product?> GetProductAsync(Guid id, CancellationToken ct)
    {
        // Always use GetOrSetAsync — it is stampede-proof.
        return await cache.GetOrSetAsync(
            key     : $"products:{id}",
            factory : async token => await LoadFromDbAsync(id, token),
            policy  : CachePolicy.Default,
            ct      : ct);
    }
}
```

### 2 — L1 + L2 (Redis distributed backplane)

```csharp
builder.Services
    .AddSharedKernelCaching()
    .AddRedisL2("localhost:6379");
```

No changes to service code — `ICacheService` automatically promotes reads to/from Redis when L2 is
registered.

### 3 — Distributed Locking (independent of L2 cache)

```csharp
builder.Services
    .AddRedisDistributedLocking("localhost:6379");
```

```csharp
public sealed class InvoiceProcessor(IDistributedLockService locks)
{
    public async Task ProcessAsync(long invoiceId, CancellationToken ct)
    {
        await using var handle = await locks.AcquireAsync(
            resource : $"invoice:{invoiceId}",
            expiry   : TimeSpan.FromSeconds(30),
            wait     : TimeSpan.FromSeconds(5),
            retry    : TimeSpan.FromMilliseconds(200),
            ct       : ct);

        if (handle is null)
        {
            // Lock not acquired within 5 s — apply your fallback strategy.
            return;
        }

        // Critical section — lock is held until DisposeAsync.
        await DoWorkAsync(invoiceId, ct);
    }
}
```

---

## `CachePolicy` Presets and Customisation

`CachePolicy` is a **sealed immutable record**. All customisation is done through factory methods
that return new instances — the original is never mutated.

### Built-in preset

| Preset | L1 TTL | L2 TTL | Fail-safe | Eager-refresh threshold |
|--------|--------|--------|-----------|------------------------|
| `CachePolicy.Default` | 5 min | 30 min | enabled | 0.9 (90 % of L1 TTL) |

### Factory methods

```csharp
// Custom TTLs — preserves default fail-safe and eager-refresh settings.
var shortLived = CachePolicy.For(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));

// Tags for group invalidation (see Tag-Based Eviction below).
var tagged = CachePolicy.Default.WithTags("tenant:acme", "entity:product");

// Custom eager-refresh threshold — background refresh starts at 80 % of L1 TTL.
var earlyRefresh = CachePolicy.Default.WithEagerRefresh(0.8);

// Disable eager refresh entirely.
var noEager = CachePolicy.Default.WithoutEagerRefresh();

// Disable fail-safe (not recommended for production — consider carefully).
var noFailSafe = CachePolicy.Default.WithFailSafeDisabled();

// Compose freely — each call returns a new instance.
var policy = CachePolicy
    .For(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(20))
    .WithTags("tenant:acme")
    .WithEagerRefresh(0.75);
```

### Eager-refresh behaviour

When `EagerRefreshThreshold` is set (default `0.9`), FusionCache triggers a background factory
call when the remaining TTL of an L1 entry drops below the threshold. The stale value continues
to be served without latency while the refresh runs in the background.

Example: with `L1Duration = 5 min` and `EagerRefreshThreshold = 0.9`, the background refresh
starts at 4 min 30 s (90 % elapsed). Callers never block on this refresh.

### Fail-safe behaviour

When `FailSafeEnabled = true` (default), FusionCache will serve a stale cache value if the
factory throws an exception or times out. This prevents total unavailability during transient
database or downstream service failures. The stale value is served with a short synthetic TTL so
the factory is retried shortly afterwards.

---

## Tag-Based Eviction

Tags allow invalidating groups of related cache entries with a single call. Assign tags at write
time via `CachePolicy.WithTags`; evict all entries for a tag via `RemoveByTagAsync`.

```csharp
// Write — assign tags to the entry.
await cache.SetAsync(
    key    : $"products:{product.Id}",
    value  : product,
    policy : CachePolicy.Default.WithTags("tenant:acme", "entity:product"),
    ct     : ct);

await cache.SetAsync(
    key    : $"inventory:{sku}",
    value  : inventory,
    policy : CachePolicy.Default.WithTags("tenant:acme"),
    ct     : ct);

// Evict — remove all entries tagged "tenant:acme" in one call.
await cache.RemoveByTagAsync("tenant:acme", ct);
```

Typical tag naming patterns:

| Pattern | Example | Use case |
|---------|---------|---------|
| Tenant scope | `tenant:{tenantId}` | Invalidate all entries for a tenant on tenant config change |
| Entity type | `entity:product` | Invalidate all cached products on a bulk import |
| Composite | `tenant:acme:entity:order` | Fine-grained combined invalidation |

> Tag-based eviction is propagated across nodes via the Redis backplane when L2 is registered.
> In L1-only mode, eviction is local to the current process only.

---

## DI Registration Reference

### `AddSharedKernelCaching` — L1-only baseline

```csharp
builder.Services
    .AddSharedKernelCaching(options =>
    {
        options.L1SizeLimit = 50_000;  // max items in the in-process cache (default: 10 000)
        options.CacheName   = "my-service"; // FusionCache instance name (default: "default")
    });
```

Configuration can also be bound from `appsettings.json` under the `SharedKernelCaching` section:

```json
{
  "SharedKernelCaching": {
    "L1SizeLimit": 50000,
    "CacheName": "my-service"
  }
}
```

Registered services:

| Service | Lifetime | Implementation |
|---------|----------|---------------|
| `ICacheService` | Singleton | `FusionCacheService` |
| `IFusionCache` | Singleton | FusionCache (internal) |

---

### `.AddRedisL2` — Redis distributed backplane (optional, additive)

Must be chained after `AddSharedKernelCaching`.

```csharp
builder.Services
    .AddSharedKernelCaching()
    .AddRedisL2(
        connectionString : "my-redis:6379,password=secret",
        configure        : options =>
        {
            options.KeyPrefix        = "my-service:";  // Redis key prefix (default: none)
            options.ConnectTimeoutMs = 3_000;           // connection timeout (default: 5 000 ms)
        });
```

Configuration can also be bound from `appsettings.json` under `SharedKernelCaching:Redis`:

```json
{
  "SharedKernelCaching": {
    "Redis": {
      "ConnectionString": "my-redis:6379",
      "KeyPrefix": "my-service:",
      "ConnectTimeoutMs": 3000
    }
  }
}
```

Additional registered services:

| Service | Lifetime | Implementation |
|---------|----------|---------------|
| `IDistributedCache` | Singleton | StackExchange.Redis |
| FusionCache backplane | Singleton | `StackExchangeRedisBackplane` |

---

### `AddRedisDistributedLocking` — distributed locking (independent)

Can be registered independently of `AddSharedKernelCaching` / `.AddRedisL2`.

```csharp
builder.Services
    .AddRedisDistributedLocking(
        connectionString : "my-redis:6379",
        configure        : options =>
        {
            options.ConnectTimeoutMs = 3_000; // connection timeout (default: 5 000 ms)
        });
```

Configuration binding from `SharedKernelCaching:DistributedLock`:

```json
{
  "SharedKernelCaching": {
    "DistributedLock": {
      "ConnectionString": "my-redis:6379",
      "ConnectTimeoutMs": 3000
    }
  }
}
```

Registered services:

| Service | Lifetime | Implementation |
|---------|----------|---------------|
| `IDistributedLockService` | Singleton | `RedLockDistributedLockService` |
| `RedLockFactory` | Singleton | RedLock.net |
| `IConnectionMultiplexer` | Singleton | StackExchange.Redis |

---

## Correct Usage Patterns

### Always use `GetOrSetAsync` — never `GetAsync` + `SetAsync`

```csharp
// CORRECT — stampede-proof; factory is guaranteed to run at most once per key.
var value = await cache.GetOrSetAsync("orders:42", LoadOrderAsync, CachePolicy.Default, ct);

// WRONG — race condition; factory may run concurrently on multiple nodes.
var value = await cache.GetAsync<Order>("orders:42", ct);
if (value is null)
{
    value = await LoadOrderAsync(ct);
    await cache.SetAsync("orders:42", value, CachePolicy.Default, ct);
}
```

### Namespace keys by service

This package does not add any key prefix. Each consuming service is responsible for namespacing
its own keys to prevent collisions across services sharing the same Redis instance.

```csharp
// Convention: "{service}:{entity}:{id}"
$"orders-svc:order:{orderId}"
$"inventory-svc:sku:{sku}"
```

### Handle `null` on `AcquireAsync`

`AcquireAsync` returns `null` when the lock is not acquired within the `wait` window. It never
throws for a contended lock. Always check the return value:

```csharp
await using var handle = await locks.AcquireAsync("resource", expiry, wait, retry, ct);
if (handle is null)
{
    // decide: skip, queue, return a conflict response, etc.
    return;
}
// critical section
```

---

## STJ Source-Generated Serialization

FusionCache uses System.Text.Json to serialize values for L2 Redis storage. For full AOT
compatibility, consuming services must provide a source-generated `JsonSerializerContext`:

```csharp
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(CustomerDto))]
internal sealed partial class MyCacheContext : JsonSerializerContext { }
```

Register it in the FusionCache STJ serializer setup if you override the default serializer
options; otherwise the defaults apply and reflection-based serialization is used for L2
(acceptable in non-AOT environments).

---

## Configuration Reference

### `CachingOptions` (`SharedKernelCaching` section)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `L1SizeLimit` | `int` | `10000` | Maximum items in the L1 in-process cache |
| `CacheName` | `string` | `"default"` | FusionCache instance name |

### `RedisL2Options` (`SharedKernelCaching:Redis` section)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectionString` | `string` | _(required)_ | StackExchange.Redis connection string |
| `KeyPrefix` | `string` | `""` | Redis key prefix for all L2 entries |
| `ConnectTimeoutMs` | `int` | `5000` | Redis connection timeout in milliseconds |

### `RedisLockOptions` (`SharedKernelCaching:DistributedLock` section)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ConnectionString` | `string` | _(required)_ | StackExchange.Redis connection string |
| `ConnectTimeoutMs` | `int` | `5000` | Redis connection timeout in milliseconds |

---

## Technology

| Component | Package | Version |
|-----------|---------|---------|
| L1 cache | `ZiggyCreatures.FusionCache` | 2.6.0 |
| L2 backplane | `ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis` | 2.6.0 |
| STJ serialization | `ZiggyCreatures.FusionCache.Serialization.SystemTextJson` | 2.6.0 |
| Distributed locking | `RedLock.net` | 2.3.2 |
| Redis client | `StackExchange.Redis` | 2.13.1 |

Both packages target `net10.0` and are AOT-compatible.

---

## Layering

```
SharedKernel.Caching.Redis  →  SharedKernel.Caching  →  SharedKernel.Core
```

Downstream packages may reference `SharedKernel.Caching` for the abstraction and inject
`SharedKernel.Caching.Redis` as the provider. Never reference the Redis package from domain or
application layers — depend only on `ICacheService` and `IDistributedLockService`.
