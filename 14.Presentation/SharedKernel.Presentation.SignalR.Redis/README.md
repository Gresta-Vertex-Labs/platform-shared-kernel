# SharedKernel.Presentation.SignalR.Redis

The Redis scale-out backplane for
[`SharedKernel.Presentation.SignalR`](../SharedKernel.Presentation.SignalR/README.md): one extension
method, `WithRedisBackplane`, on the SignalR server builder.

**Tier:** Host. It references `SharedKernel.Presentation.SignalR` and
`Microsoft.AspNetCore.SignalR.StackExchangeRedis`, which pulls in `StackExchange.Redis`. Keeping it a
separate package means a single-replica SignalR host carries no Redis dependency at all.

Reference it only in a SignalR host that runs **more than one replica**. Without it SignalR stays in
memory, which is right for local development and a single replica; with several replicas, a message
sent through `IHubContext<THub>` on one pod would otherwise never reach clients connected to another.

---

## Installation

```xml
<ItemGroup>
  <PackageReference Include="SharedKernel.Presentation.SignalR" />
  <PackageReference Include="SharedKernel.Presentation.SignalR.Redis" />
</ItemGroup>
```

Versions come from your single `SharedKernelVersion`; every SharedKernel package ships with the
same version.

---

## Registration

`WithRedisBackplane` is declared in the `SharedKernel.Presentation.SignalR.Extensions` namespace — the
same one as `AddSharedKernelSignalR` — so the chain needs no extra `using`:

```csharp
using SharedKernel.Presentation.SignalR.Extensions;

builder.Services
    .AddSharedKernelSignalR()
    .WithRedisBackplane(builder.Configuration.GetConnectionString("signalr-redis")!);
```

Pass a callback to configure the underlying `RedisOptions`
(`Microsoft.AspNetCore.SignalR.StackExchangeRedis`), for example a channel prefix so several services
can share one Redis instance:

```csharp
builder.Services
    .AddSharedKernelSignalR()
    .WithRedisBackplane(connectionString, redis =>
    {
        redis.Configuration.ChannelPrefix = RedisChannel.Literal("orders-svc");
    });
```

| Parameter | Required | Default | Meaning |
| --- | --- | --- | --- |
| `connectionString` | yes | — | StackExchange.Redis connection string for the backplane. |
| `configure` | no | `null` | Customises `RedisOptions` (channel prefix, connection factory, `ConfigurationOptions`). |

---

## Behaviour

- **Thin pass-through.** `WithRedisBackplane(connectionString, configure)` calls
  `AddStackExchangeRedis(connectionString, configure)` and adds nothing else. It exists so a service
  has one platform-named method to find.
- **Its own connection.** The backplane manages its own `IConnectionMultiplexer` and never shares the
  connection `SharedKernel.Caching.Redis.Core`'s `AddRedisConnection` registers. A backplane outage is
  never reported as a cache outage, and the reverse.
- **Not `SharedKernel.Caching.Redis.PubSub`.** Both use Redis pub/sub, but the backplane fans hub
  messages out to connected clients across pods, while `.PubSub` carries loss-tolerant service
  signals. A service may use both; neither references the other.

---

## Related packages

- [`SharedKernel.Presentation.SignalR`](../SharedKernel.Presentation.SignalR/README.md) — hub filters, group naming and `AddSharedKernelSignalR`.
- [Configuration reference](../CONFIGURATION.md) — every option of both SignalR packages.
