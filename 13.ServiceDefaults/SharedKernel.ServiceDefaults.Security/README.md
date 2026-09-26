# SharedKernel.ServiceDefaults.Security

**One request context for every layer that asks who is calling, and the HTTP adapter that fills it.**

- `AddSharedKernelRequestContext()` registers `IRequestContext` (`SharedKernel.Execution`) over `12.Security`'s
  `IUserContext`, plus `IRequestContextAccessor`.
- `app.UseSharedKernelRequestContext()` is the HTTP inbound adapter: it owns the request's correlation id and runs the
  rest of the request inside a `RequestContextScope`, so handlers, repositories, audit records, outbound REST and gRPC
  calls, published messages and log lines all see one caller and one correlation id.

**Tier: Host.** References `SharedKernel.ServiceDefaults`, `SharedKernel.Execution` and
`SharedKernel.Security.Abstractions`.

## Install

```xml
<PackageReference Include="SharedKernel.ServiceDefaults.Security" />
```

## Usage

```csharp
using SharedKernel.ServiceDefaults.Security;

builder.AddServiceDefaults();
builder.Services.AddOidcAuthentication(builder.Configuration);   // any 12.Security scheme that registers IUserContext
builder.Services.AddSharedKernelRequestContext();

var app = builder.Build();

app.UseSharedKernelRequestContext();          // 1. first: correlation id + the request's context scope
app.UseSharedKernelSecurityHeaders();         // 2. SharedKernel.Presentation.WebApi
app.UseExceptionHandler();                    // 3.
app.UseAuthentication();                      // 4.
app.UseMiddleware<TenantResolutionMiddleware>(); // 5. optional, SharedKernel.MultiTenancy
app.UseAuthorization();                       // 6.

app.MapControllers();                         // 7. endpoints
```

This is the order `samples/OrderApi` compiles and tests. `UseSharedKernelRequestContext()` throws
`InvalidOperationException` at startup when `AddSharedKernelRequestContext()` was not called.

Read the caller anywhere through `IRequestContext` (injected) or `IRequestContextAccessor.Current` (for code that is
not resolved per request):

```csharp
public sealed class PlaceOrderHandler(IRequestContext caller) : ICommandHandler<PlaceOrder, OrderId>
{
    public Task<Result<OrderId>> Handle(PlaceOrder command, CancellationToken ct)
    {
        TenantId? tenant = caller.TenantId;           // null fails closed in persistence
        string? correlationId = caller.CorrelationId; // the request's X-Correlation-Id
        // ...
    }
}
```

## Why it goes first

- **Correlation id on every log line and every response.** Placed before `UseExceptionHandler()`, the id is in the
  log context of the exception handler and every later middleware, and the `X-Correlation-Id` response header is
  written through `Response.OnStarting` — error responses included.
- **Safe before `UseAuthentication()`.** The caller is read lazily, the first time something asks, which is after
  authentication has run. (The scoped `IUserContext` snapshots `HttpContext.User` when first created, so reading it any
  earlier would fix the caller as anonymous.) Nothing between this middleware and `UseAuthentication()` should read
  the caller.
- **Tenant resolution refines, never replaces.** `TenantResolutionMiddleware` opens an inner scope that changes only
  the tenant; the caller and correlation id stay this middleware's.

## Correlation id

| Inbound `X-Correlation-Id` | Result |
| --- | --- |
| absent or whitespace | a new id, `CorrelationIds.New()` (EventId `13006`, Debug) |
| accepted by `CorrelationIds.IsValid` (≤ 128 characters of `[A-Za-z0-9-_:.]`) | kept unchanged |
| anything else | a new id; only the rejected value's **length** is logged (EventId `13007`, Warning) — never its content |

The resolved id is set as `Activity` baggage (`WellKnownBaggageKeys.CorrelationId`) for log enrichment and is
forwarded by every outbound adapter through `RequestContextPropagation`: REST, gRPC, the message bus and Temporal. A
rejected value is never logged, echoed or put in baggage.

## The `IRequestContext` mapping

| `IRequestContext` | From |
| --- | --- |
| `IsAuthenticated` | `IUserContext.IsAuthenticated` |
| `UserId` | `SubjectId`, else `ClientId`; `null` when unauthenticated |
| `TenantId` | `IUserContext.TenantId` (`TenantId?`) — the tenant the credential asserts, unless `TenantResolutionMiddleware` resolved another; `null` fails closed |
| `ActorKind` | `IUserContext.ActorKind` when authenticated; `ActorKind.Anonymous` otherwise |
| `ClientId`, `SessionId` | `IUserContext` |
| `CorrelationId` | the HTTP request's id (from the scope this middleware opened) |
| `HasPermissionAsync` | `IUserContext.HasPermission` (ordinal) |

`IRequestContext` is registered **transient**: it returns `RequestContextScope.Current` when an inbound adapter opened
a scope (this middleware, the gRPC server interceptor, the MassTransit consume filter, the Temporal activity
interceptor, the scheduler's job runner), and otherwise the scope's `SecurityRequestContext`. The registration uses
`Add`, so it replaces the fail-closed `AnonymousRequestContext` default that `SharedKernel.Persistence.EfCore`
registers, whatever the call order.

An unauthenticated caller is `ActorKind.Anonymous`, never `System`: the audit trail and the cross-tenant-scope log can
tell an anonymous request from the platform's own background work. Background work runs under a
`SystemRequestContext` (authenticated, `ActorKind.System`, an explicit permission set) that its adapter opens.

## Who reads it

- `SharedKernel.Application.Pipeline`: `AuthorizationBehavior` (permissions); `SharedKernel.Application.Pipeline.Caching`:
  the caching behaviors (tenant/user key scope).
- `18.Idempotency`: the tenant scope of an idempotency key.
- `06.Persistence`: audit columns, tenant filters and row-level security, the audit ledger.
- `11.Communication`, `07.Messaging`, `17.Workflows`: outbound propagation of tenant, actor and correlation id.

## Related packages

- [`SharedKernel.MultiTenancy`](../SharedKernel.MultiTenancy/README.md) — header and directory tenant resolution
- [`SharedKernel.ServiceDefaults`](../SharedKernel.ServiceDefaults/README.md) — the composition base
- `SharedKernel.Execution` (`01.Core`) — `IRequestContext`, `RequestContextScope`, `CorrelationIds`, `TenantId`
