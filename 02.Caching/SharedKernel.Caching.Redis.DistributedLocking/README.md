# SharedKernel.Caching.Redis.DistributedLocking

Redis implementation of `IDistributedLockService` from `SharedKernel.Caching.Abstractions`:

- **Locks** kept alive until released, and reported when lost.
- **Leases** that expire on their own.
- **Fencing tokens** issued in the same atomic server-side step as each acquisition.

It runs on a single Redis primary or on Redis Cluster. It depends only on `SharedKernel.Caching.Abstractions` and `SharedKernel.Caching.Redis.Core`.

## Install

```text
dotnet add package SharedKernel.Caching.Redis.DistributedLocking
```

## Registration

```csharp
// Lock-only host
services.AddRedisDistributedLocking("localhost:6379");

// Alongside the cache
services.AddSharedKernelCaching(o => o.ServiceName = "billing")
        .AddRedisL2("localhost:6379")
        .AddRedisDistributedLocking("localhost:6379");
```

Every Redis package shares one `IConnectionMultiplexer`, whichever registers it first. `TimeProvider.System` is registered unless a `TimeProvider` already is.

## Usage

```csharp
public sealed class InvoiceSettlement(IDistributedLockService locks, IInvoiceStore invoices)
{
    public async Task SettleAsync(long invoiceId, CancellationToken ct)
    {
        await using IDistributedLock? handle = await locks.TryAcquireAsync(
            $"billing:invoice:{invoiceId}",
            new DistributedLockOptions { Expiry = TimeSpan.FromSeconds(30), WaitTime = TimeSpan.FromSeconds(5) },
            ct);

        if (handle is null)
            return; // another holder kept the lock for the whole wait time

        using var work = CancellationTokenSource.CreateLinkedTokenSource(ct, handle.LostToken);
        await invoices.SettleAsync(invoiceId, handle.FencingToken, work.Token);
    }
}
```

A lease claims a resource once and simply expires:

```csharp
DistributedLease? lease = await locks.TryAcquireLeaseAsync($"reports:nightly:{runDate:yyyy-MM-dd}", TimeSpan.FromHours(1), ct);
if (lease is null)
    return; // another replica claimed this run
```

## How it works

| Operation | Redis |
| --- | --- |
| Acquire (lock or lease) | One Lua script: `SET sharedkernel:lock:{resource} <owner> NX PX <expiry>` and, only if that succeeded, `INCR sharedkernel:lock-fencing:{resource}` |
| Keep alive (locks) | Every third of the expiry, `PEXPIRE` the key only if it still holds this owner |
| Release (locks) | `DEL` the key only if it still holds this owner |

- **Atomic fencing tokens.** A token can never be issued to a holder that did not acquire.
- **Exclusive.** Locks and leases on the same resource exclude each other.
- **Monotonic.** They share one fencing counter.
- **Loss detection.** A lock is reported lost, through `IsHeld` = `false` and a cancelled `LostToken`, when an extension finds another owner. It is also reported lost when extensions keep failing past the expiry.
- **Outages.** When Redis cannot be reached, acquisition throws `DistributedLockUnavailableException` instead of returning `null`.

**Failover caveat.** After a primary failover, a replica that had not yet received a lock key can grant the lock again. Fencing tokens are what protect the resource in that window, so check them in the protected write path.

## Configuration

| `RedisLockOptions` property | Default | Description |
| --- | --- | --- |
| `ConnectionString` | _(required)_ | StackExchange.Redis connection string |
| `ConnectTimeoutMs` | `5000` | Connection timeout in milliseconds |

## Layering

```text
SharedKernel.Caching.Redis.DistributedLocking  →  SharedKernel.Caching.Abstractions, SharedKernel.Caching.Redis.Core
```

Target framework: `net10.0`.
