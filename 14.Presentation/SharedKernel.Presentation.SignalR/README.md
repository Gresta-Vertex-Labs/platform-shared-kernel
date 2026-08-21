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

### Resource-exhaustion defaults

`AddSharedKernelSignalR` also sets explicit, conservative defaults on four `HubOptions` members that
control per-connection resource consumption on a long-lived WebSocket/SSE surface — pinned even
where a value matches SignalR's own current framework default, so the platform's posture is
documented and stable across future SignalR version bumps rather than implicit:

| `HubOptions` member | Platform default | Why |
| --- | --- | --- |
| `MaximumReceiveMessageSize` | `32 * 1024` (32 KB) | Caps the size of a single inbound message from a client. With no ceiling, one misbehaving or hostile client can send an arbitrarily large message and consume disproportionate memory/CPU per connection — a real resource-exhaustion vector this domain previously left entirely to whatever SignalR's own current default happened to be. |
| `MaximumParallelInvocationsPerClient` | `1` | Caps how many hub method invocations from the *same* client SignalR will run concurrently. Prevents one client from starving the server by firing many concurrent long-running invocations down a single connection. |
| `ClientTimeoutInterval` | `30` seconds | How long the server waits for a client keep-alive before considering the connection dead and reclaiming its resources. |
| `KeepAliveInterval` | `15` seconds | How often the server pings a connected client to keep the connection alive and detect drops promptly. |

These defaults are applied **before** your `configureHubOptions` callback runs, so every one of them
remains fully overridable (raise or lower) with no signature change:

```csharp
builder.Services.AddSharedKernelSignalR(options =>
{
    // Override the platform default for a hub that legitimately needs larger messages.
    options.MaximumReceiveMessageSize = 128 * 1024;
});
```

This is purely a `HubOptions` default-value change — it never alters
`TenantContextHubFilter`/`HubExceptionMappingFilter`/`WithRedisBackplane` behavior.

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

## Hub invocation rate limiting & argument validation

`AddSharedKernelSignalR`'s `configureRateLimit` parameter adds a per-connection invocation rate limit
and argument-payload shape check on top of the connection-level `HubOptions` defaults above. Those
defaults cap resource use *per connection*; they do nothing to stop a single connection from firing
an unbounded number of hub-method invocations. Real-time fintech workloads (live trading updates,
payment status streams) are exactly the ones most likely to expose a hub method to high-frequency
invocation.

```csharp
builder.Services.AddSharedKernelSignalR(configureRateLimit: o =>
{
    o.PermitLimit = 20;
    o.Window = TimeSpan.FromSeconds(10);      // 20 invocations per 10-second window, per connection
    o.MaxStringArgumentLength = 4 * 1024;      // reject an oversized string argument before dispatch
});
```

`HubInvocationRateLimitFilter` is always registered (like every other filter in this package), but
every check inside `HubInvocationRateLimitOptions` defaults to disabled (`PermitLimit`/
`MaxStringArgumentLength` both `null`, `ArgumentValidators` empty) — omitting `configureRateLimit`
entirely is a genuine no-op, zero behavior change. Built on `System.Threading.RateLimiting`'s
`TokenBucketRateLimiter`, created lazily per connection and disposed on disconnect — one client
exceeding its limit never throttles any other connection on the same hub.

A rejected invocation throws a `HubException` carrying a specific, caller-safe message (e.g. `"Too
many requests. Please slow down."`) — **before** the target hub method body ever executes.

### Hub-filter composition rule this depends on

`HubExceptionMappingFilter`'s existing catch-all had no branch recognizing an already-thrown
`HubException` as terminal before this capability shipped — without one, a `HubException` raised by
`HubInvocationRateLimitFilter` (or any other filter) would fall into the "unknown exception" branch
and be silently re-wrapped into the generic redacted `"An unexpected error occurred."` message,
discarding the specific rate-limit text this filter exists to surface. `HubExceptionMappingFilter`
now has a `catch (HubException) { throw; }` branch, checked **first**, so an already-well-formed
`HubException` always passes through unchanged.

> **Standing rule for any future hub filter you write that throws its own `HubException`:** never
> assume `HubExceptionMappingFilter`'s existing catch-all already preserves it — verify explicitly
> (as this capability's own tests do, with a real two-filter pipeline proving the specific message
> survives). This branch happened to already exist in this package (present since the very first
> WO-031 build-out), but that was confirmed only by checking `git log`, not assumed from reading the
> code once.

---

## SignalR + CORS

A service correctly applying deny-by-default CORS (`SharedKernel.Presentation.WebApi`'s
`AddSharedKernelCors`) to its REST endpoints can still forget that a mapped SignalR hub is a
**separate** ASP.NET Core endpoint requiring its own explicit CORS policy attachment — a
well-documented real-world SignalR gotcha. This package never takes a `ProjectReference` on
`SharedKernel.Presentation.WebApi` to close that gap (the two packages remain deliberately
independent API surfaces — a pure real-time host must not be forced to pull in
`Asp.Versioning`/`Microsoft.AspNetCore.OpenApi`/`Scalar.AspNetCore` transitively just to get a CORS
integration point). Instead, `AddSharedKernelSignalR` registers a startup-time **diagnostic**
(`SignalRCorsStartupDiagnostic`) that scans every mapped endpoint once the host has started and logs
a `Warning` (`EventId` 14102) for any SignalR hub with no CORS policy attached — it never throws and
never blocks startup.

Worked example — a hub correctly wired for a credentialed cross-origin browser client, composing
`SharedKernel.Presentation.WebApi`'s named policy **by reference/documentation only, with zero code
coupling**:

```csharp
// Composition root — both packages present, referenced by name only:
builder.Services.AddSharedKernelCors(o =>
{
    o.AllowedOrigins.Add("https://app.example.com");
    o.AllowCredentials = true;
});
builder.Services.AddSharedKernelSignalR();

var app = builder.Build();

app.UseCors(CorsPolicyNames.Default);   // SharedKernel.Presentation.WebApi's own named policy

app.MapHub<OrdersHub>("/hubs/orders")
   .RequireCors(CorsPolicyNames.Default);   // <-- required; a hub is its own endpoint, CORS isn't inherited
```

Omitting `.RequireCors(...)` on a mapped hub does **not** fail the request outright — it triggers the
startup `Warning` above so the gap is visible in logs/telemetry rather than silently causing
inaccessible or (worse) accidentally-permissive hub connections discovered only in production.

**Which shape ultimately shipped (T-66):** diagnostic-only, confirmed sufficient — no shared
CORS-integration point (e.g. a negotiate-endpoint origin-policy bridge) was built or is needed. The
real SignalR CORS-decision marker is `Microsoft.AspNetCore.Cors.Infrastructure.ICorsMetadata`
(confirmed via reflection against the installed assemblies — **not** `ICorsPolicyMetadata`, a
narrower interface `RequireCors`'s underlying `EnableCorsAttribute` does not implement in this
ASP.NET Core version); each hub's `/negotiate` companion endpoint is skipped during the scan since it
always carries identical CORS metadata to its primary hub endpoint.

---

## What you get out of the box

| Concern | Type |
| --- | --- |
| Tenant attachment at connect time | `TenantContextHubFilter` |
| Exception-to-`HubException` redaction | `HubExceptionMappingFilter` |
| Tenant-scoped group naming | `HubGroupNaming.TenantGroup` |
| SignalR + global filter registration | `AddSharedKernelSignalR` |
| Conservative resource-exhaustion `HubOptions` defaults | `AddSharedKernelSignalR` (see "Resource-exhaustion defaults" above) |
| Per-connection invocation rate limit + argument validation | `HubInvocationRateLimitOptions` (via `AddSharedKernelSignalR`'s `configureRateLimit`) |
| Startup diagnostic for a mapped hub with no CORS policy | `SignalRCorsStartupDiagnostic` (registered automatically by `AddSharedKernelSignalR`) |
| Redis scale-out backplane | `WithRedisBackplane` |

See the [Configuration Reference](../CONFIGURATION.md) for every DI extension method's options and
defaults.
