# SharedKernel.Presentation.SignalR

`IHubFilter` implementations (tenant context attachment, exception-to-`HubException` mapping), a
tenant-scoped SignalR group naming convention, and an opt-in Redis-backed scale-out backplane.

Like its `.WebApi` sibling, this package is framework-glue: it converts outcomes your hub methods
already produce (or throw) into safe, client-facing SignalR responses. It never references
`05.Application`, `06.Persistence`, `07.Messaging`, or `02.Caching.*` — see "Why the Redis
backplane is distinct from `02.Caching.Redis.PubSub`" in `14.Presentation/CLAUDE.md`.

---

## Installation

```xml
<ItemGroup>
  <PackageReference Include="SharedKernel.Presentation.SignalR" Version="x.y.z" />
</ItemGroup>
```

Brings in `Microsoft.AspNetCore.SignalR.StackExchangeRedis` as a transitive dependency (used only
when `WithRedisBackplane` is called — otherwise SignalR stays fully in-memory).

---

## Minimal setup — single replica, in-memory

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelSignalR();

var app = builder.Build();

app.MapHub<OrdersHub>("/hubs/orders");

app.Run();
```

`AddSharedKernelSignalR` registers SignalR plus two global hub filters — applied to **every** hub
in the service automatically, with no `[HubFilter]` attributes needed:

- **`TenantContextHubFilter`** — resolves `ITenantProvider` from the connecting client's
  `HttpContext` and stores the tenant ID in `Context.Items["TenantId"]` for the life of the
  connection. It never rejects a connection with no resolvable tenant — that policy decision
  belongs to your Hub (via `[Authorize]` or an explicit check inside the hub method).
- **`HubExceptionMappingFilter`** — wraps every hub method invocation. Known
  `SharedKernelException` subtypes (`01.Core`) are rethrown as a `HubException` carrying the
  `Error`'s message; any other exception is logged at `LogLevel.Error` and rethrown as a generic,
  redacted `HubException("An unexpected error occurred.")`. No stack trace or internal type name
  ever crosses the hub boundary.

Both filters are opt-out via the `configureHubOptions` callback if a consuming service needs
different behavior:

```csharp
builder.Services.AddSharedKernelSignalR(options =>
{
    // e.g. remove the platform's exception filter and add a service-specific one instead.
    options.HubFilters.RemoveAll(f => f.GetType() == typeof(HubExceptionMappingFilter));
});
```

---

## Scale-out setup — Redis backplane

Omitting `WithRedisBackplane` is correct for local dev and single-replica deployments. Add it the
moment a hub-hosting service runs more than one pod/instance behind a load balancer:

```csharp
builder.Services
    .AddSharedKernelSignalR()
    .WithRedisBackplane(builder.Configuration.GetConnectionString("SignalRBackplane")!);

// Optional: configure the underlying RedisOptions (e.g. channel prefix).
builder.Services
    .AddSharedKernelSignalR()
    .WithRedisBackplane(connectionString, redisOptions =>
    {
        redisOptions.Configuration.ChannelPrefix = RedisChannel.Literal("orders-svc");
    });
```

`WithRedisBackplane` is a thin pass-through over
`Microsoft.AspNetCore.SignalR.StackExchangeRedis`'s own `AddStackExchangeRedis` — it manages its
own `IConnectionMultiplexer` lifecycle internally and **never** shares a connection with
`02.Caching.Redis.Core`. A backplane outage and a cache-connection outage must never be conflated
in health checks or logs; that is intentional isolation, not an oversight.

---

## Tenant-scoped group broadcast

`HubGroupNaming` is the single source of truth for tenant-scoped group names — never format a
group name string inline elsewhere.

```csharp
public sealed class OrdersHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = (Guid)Context.Items[TenantContextHubFilter.ItemsKey]!;
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroupNaming.TenantGroup(tenantId));
        await base.OnConnectedAsync();
    }
}

// Elsewhere — e.g. from an application-layer notification handler holding an
// IHubContext<OrdersHub> (composition root concern, not this package's):
await hubContext.Clients
    .Group(HubGroupNaming.TenantGroup(tenantId))
    .SendAsync("OrderUpdated", orderId, cancellationToken);
```

`HubGroupNaming.TenantGroup(Guid)` always formats as `"tenant:{tenantId:D}"`.

---

## What you get out of the box

| Concern | Type |
| --- | --- |
| Tenant attachment at connect time | `TenantContextHubFilter` |
| Exception-to-`HubException` redaction | `HubExceptionMappingFilter` |
| Tenant-scoped group naming | `HubGroupNaming.TenantGroup` |
| SignalR + global filter registration | `AddSharedKernelSignalR` |
| Redis scale-out backplane | `WithRedisBackplane` |

See the [Configuration Reference](../CONFIGURATION.md) for every DI extension method's options and
defaults.
