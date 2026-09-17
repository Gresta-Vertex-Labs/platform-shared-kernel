# SharedKernel.Caching.Redis

The Redis distributed layer (L2) and backplane for
[`SharedKernel.Caching.FusionCache`](https://www.nuget.org/packages/SharedKernel.Caching.FusionCache).

Entries are written to Redis as well as to memory, so every instance of a service shares them.
The backplane carries removals, expirations, tag evictions and clears to every instance. For
distributed locks, use `SharedKernel.Caching.Redis.DistributedLocking`.

## Quick Start

### L1 + L2 (Redis distributed layer and backplane)

```csharp
builder.Services
    .AddSharedKernelCaching(o => o.ServiceName = "orders")
    .AddRedisL2("localhost:6379");
```

No changes to service code: `ICacheService` reads and writes through Redis automatically. Entries
created with `CachePolicy.LocalOnly()` stay in memory.

## Configuration

### `AddRedisL2` (`SharedKernelCaching:Redis` section)

| Property | Default | Description |
|----------|---------|-------------|
| `ConnectionString` | _(required)_ | StackExchange.Redis connection string |
| `KeyPrefix` | `""` | Redis key prefix for all L2 entries |
| `ConnectTimeoutMs` | `5000` | Connection timeout in milliseconds |

## Layering

```text
SharedKernel.Caching.Redis  →  SharedKernel.Caching.Abstractions, SharedKernel.Caching.Redis.Core
```

Target framework: `net10.0`.

For full documentation see the [repository README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/README.md).
