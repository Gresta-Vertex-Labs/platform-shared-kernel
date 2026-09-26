# SharedKernel.Presentation.SignalR

`IHubFilter` implementations (request-context scope for every hub call, exception-to-`HubException`
mapping, per-connection invocation rate limiting), a tenant-scoped SignalR group naming convention,
conservative `HubOptions` defaults and a startup CORS diagnostic.

**Tier:** Host. It references `SharedKernel.Primitives`, `SharedKernel.Core` and
`SharedKernel.Execution` only — no Redis, no `SharedKernel.Presentation.WebApi`. The Redis
scale-out backplane is the separate
[`SharedKernel.Presentation.SignalR.Redis`](../SharedKernel.Presentation.SignalR.Redis/README.md)
package.

Like its `.WebApi` sibling, this package is framework-glue: it converts outcomes your hub methods
already produce (or throw) into safe, client-facing SignalR responses. It references no mediator,
persistence, messaging or caching package.

---

## Installation

```xml
<ItemGroup>
  <PackageReference Include="SharedKernel.Presentation.SignalR" />
  <!-- only for a hub host that runs more than one replica: -->
  <PackageReference Include="SharedKernel.Presentation.SignalR.Redis" />
</ItemGroup>
```

Versions come from your single `SharedKernelVersion`; every SharedKernel package ships with the
same version.

---

## Minimal setup — single replica, in-memory

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelRequestContext();   // SharedKernel.ServiceDefaults.Security
builder.Services.AddSharedKernelSignalR();

var app = builder.Build();

app.UseSharedKernelRequestContext();                 // first, as for every HTTP host
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<OrdersHub>("/hubs/orders");

app.Run();
```

`AddSharedKernelSignalR` registers SignalR plus three global hub filters — applied to **every** hub
in the service automatically, with no `[HubFilter]` attributes needed — and `IRequestContextAccessor`
(`SharedKernel.Execution`) when nothing else registered it:

- **`TenantContextHubFilter`** — at connect time takes the caller's `IRequestContext` (the ambient
  one from `IRequestContextAccessor.Current`, set by `UseSharedKernelRequestContext()` and
  `SharedKernel.MultiTenancy`'s tenant resolution, or else the `IRequestContext` registered in the
  connect request's services), keeps it for the life of the connection, and opens a
  `RequestContextScope` with it around the connect handler, every hub method and the disconnect
  handler. Hub code — and anything it calls, such as a repository filtering by tenant or an outbound
  REST call carrying the correlation id — reads the caller through `IRequestContextAccessor` /
  `IRequestContext`, exactly as on the HTTP path. It never rejects a connection with no tenant —
  that policy decision belongs to your Hub (via `[Authorize]` or an explicit check).
- **`HubExceptionMappingFilter`** — wraps every hub method invocation. Known
  `SharedKernelException` subtypes (`SharedKernel.Core`) are rethrown as a `HubException` carrying
  the `Error`'s message; any other exception is logged at `LogLevel.Error` (`EventId` 14100) and
  rethrown as a generic, redacted `HubException("An unexpected error occurred.")`. An
  already-thrown `HubException` passes through unchanged. No stack trace or internal type name
  ever crosses the hub boundary.
- **`HubInvocationRateLimitFilter`** — a no-op until `configureRateLimit` enables it (see below).

The filters are opt-out via the `configureHubOptions` callback if a consuming service needs
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

This is purely a `HubOptions` default-value change — it never alters the hub filters' behavior.

---

## Scale-out — Redis backplane

Omitting a backplane is correct for local dev and single-replica deployments. The moment a
hub-hosting service runs more than one replica behind a load balancer, add
[`SharedKernel.Presentation.SignalR.Redis`](../SharedKernel.Presentation.SignalR.Redis/README.md)
and chain `.WithRedisBackplane(connectionString)` onto `AddSharedKernelSignalR()`. This package has
no Redis dependency of its own.

---

## Tenant-scoped group broadcast

`HubGroupNaming` is the single source of truth for tenant-scoped group names — never format a
group name string inline elsewhere.

```csharp
public sealed class OrdersHub(IRequestContextAccessor requestContext) : Hub
{
    public override async Task OnConnectedAsync()
    {
        // TenantContextHubFilter opened the connection's RequestContextScope around this call.
        if (requestContext.Current?.TenantId is { } tenantId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroupNaming.TenantGroup(tenantId.Value));
        }

        await base.OnConnectedAsync();
    }
}

// Elsewhere — e.g. from an application-layer notification handler holding an
// IHubContext<OrdersHub> (composition root concern, not this package's):
await hubContext.Clients
    .Group(HubGroupNaming.TenantGroup(tenantId.Value))
    .SendAsync("OrderUpdated", orderId, cancellationToken);
```

`HubGroupNaming.TenantGroup(Guid)` always formats as `"tenant:{tenantId:D}"` — pass
`TenantId.Value` (`SharedKernel.Execution.Tenancy.TenantId`).

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

### Hub-filter composition rule

`HubExceptionMappingFilter` rethrows an already-thrown `HubException` unchanged (a
`catch (HubException) { throw; }` branch, checked first), so the rate-limit filter's specific message
reaches the client instead of the generic redacted one. Any hub filter you write that throws its own
`HubException` relies on the same rule — cover it with a test that runs the real two-filter pipeline.

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

**Diagnostic only.** No shared
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
| Caller (`IRequestContext`) scope for every hub call | `TenantContextHubFilter` |
| Exception-to-`HubException` redaction | `HubExceptionMappingFilter` |
| Tenant-scoped group naming | `HubGroupNaming.TenantGroup` |
| SignalR + global filter registration | `AddSharedKernelSignalR` |
| Conservative resource-exhaustion `HubOptions` defaults | `AddSharedKernelSignalR` (see "Resource-exhaustion defaults" above) |
| Per-connection invocation rate limit + argument validation | `HubInvocationRateLimitOptions` (via `AddSharedKernelSignalR`'s `configureRateLimit`) |
| Startup diagnostic for a mapped hub with no CORS policy | `SignalRCorsStartupDiagnostic` (registered automatically by `AddSharedKernelSignalR`) |
| Redis scale-out backplane | `WithRedisBackplane` — in `SharedKernel.Presentation.SignalR.Redis` |

See the [Configuration Reference](../CONFIGURATION.md) for every DI extension method's options and
defaults.

## Related packages

- [`SharedKernel.Presentation.SignalR.Redis`](../SharedKernel.Presentation.SignalR.Redis/README.md) — the Redis scale-out backplane.
- [`SharedKernel.Presentation.WebApi`](../SharedKernel.Presentation.WebApi/README.md) — `AddSharedKernelCors` and `CorsPolicyNames` for the hub's CORS policy.
- `SharedKernel.ServiceDefaults.Security` — `AddSharedKernelRequestContext()` / `UseSharedKernelRequestContext()`, the caller context the hub filter carries.
