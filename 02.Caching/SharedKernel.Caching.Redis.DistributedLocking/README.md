# SharedKernel.Caching.Redis.DistributedLocking

Distributed mutual-exclusion locking over Redis via RedLock.net: `IDistributedLockService`
(acquire/release) and `IRenewableLock` (heartbeat-renewable locks for long-running work). Depends
only on `SharedKernel.Caching.Abstractions` + `SharedKernel.Caching.Redis.Core` — does not
transitively reference `SharedKernel.Caching.Redis` (L2), the hash store, or the pub/sub package.

## Install

```
dotnet add package SharedKernel.Caching.Redis.DistributedLocking
```

```xml
<PackageReference Include="SharedKernel.Caching.Redis.DistributedLocking" Version="1.0.0" />
```

## Usage

```csharp
services.AddRedisDistributedLocking("localhost:6379");
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

### Renewable lock (heartbeat)

```csharp
await using var renewable = await locks.AcquireRenewableAsync(
    resource : $"invoice:{invoiceId}",
    expiry   : TimeSpan.FromSeconds(30),
    wait     : TimeSpan.FromSeconds(5),
    retry    : TimeSpan.FromMilliseconds(200),
    ct       : ct);

if (renewable is not null)
{
    await renewable.KeepAliveAsync(TimeSpan.FromSeconds(10), ct); // background renewal loop
}
```

## Layering

```
SharedKernel.Caching.Redis.DistributedLocking  →  SharedKernel.Caching.Redis.Core  →  SharedKernel.Caching.Abstractions
```

Target framework: `net10.0`. AOT-compatible.

For full documentation see the [repository README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/02.Caching/README.md).
