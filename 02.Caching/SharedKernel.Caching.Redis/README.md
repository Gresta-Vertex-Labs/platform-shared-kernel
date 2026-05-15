# SharedKernel.Caching.Redis

Redis L2 distributed backplane and RedLock distributed locking for
[`SharedKernel.Caching`](https://www.nuget.org/packages/SharedKernel.Caching).

Adds a Redis-backed `ICacheService` (FusionCache L2 via StackExchange.Redis) and
`IDistributedLockService` (RedLock.net) to the SharedKernel caching stack.

## Quick Start

### L1 + L2 (Redis distributed backplane)

```csharp
builder.Services
    .AddSharedKernelCaching()
    .AddRedisL2("localhost:6379");
```

No changes to service code — `ICacheService` automatically promotes reads/writes to Redis.

### Distributed Locking

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
            return; // not acquired within wait window — apply fallback

        await DoWorkAsync(invoiceId, ct);
    }
}
```

`AcquireAsync` returns `null` on timeout — it never throws for a contended lock.

## Configuration

### `AddRedisL2` (`SharedKernelCaching:Redis` section)

| Property | Default | Description |
|----------|---------|-------------|
| `ConnectionString` | _(required)_ | StackExchange.Redis connection string |
| `KeyPrefix` | `""` | Redis key prefix for all L2 entries |
| `ConnectTimeoutMs` | `5000` | Connection timeout in milliseconds |

### `AddRedisDistributedLocking` (`SharedKernelCaching:DistributedLock` section)

| Property | Default | Description |
|----------|---------|-------------|
| `ConnectionString` | _(required)_ | StackExchange.Redis connection string |
| `ConnectTimeoutMs` | `5000` | Connection timeout in milliseconds |

## Layering

```
SharedKernel.Caching.Redis  →  SharedKernel.Caching  →  SharedKernel.Core
```

Target framework: `net10.0`. AOT-compatible.

For full documentation see the [repository README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/README.md).
