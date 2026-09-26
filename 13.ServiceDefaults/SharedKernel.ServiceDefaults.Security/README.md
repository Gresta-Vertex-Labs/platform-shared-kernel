# SharedKernel.ServiceDefaults.Security

**One request context for every layer that asks who is calling, and the HTTP adapter that fills it.**

- `AddSharedKernelRequestContext()` registers `IRequestContext` (`SharedKernel.Execution`) over `12.Security`'s
  `IUserContext`, plus `IRequestContextAccessor`.
- `app.UseSharedKernelRequestContext()` is the HTTP inbound adapter: it refuses the caller's W3C baggage, owns the
  request's correlation id and runs the rest of the request inside a `RequestContextScope`, so handlers, repositories,
  audit records, outbound REST and gRPC calls, published messages and log lines all see one caller and one correlation
  id. REST, gRPC and SignalR share the pipeline, so all three get it; `SharedKernel.Presentation.SignalR` carries the
  connection's context into every hub invocation.

The registered `IRequestContext` is what `05.Application`'s pipeline checks `[RequirePermission]` against (always; the
host start fails without one when a use case declares a permission), what the caching behaviors scope keys by, and what
`06.Persistence` attributes audit columns, filters tenant rows and writes audit records with. There is no separate
persistence or pipeline bridge.

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
builder.Services.AddSharedKernelRequestContext();                // optional: o => o.TrustInboundBaggage = true
builder.AddSharedKernelWebApi();                                 // SharedKernel.Presentation.WebApi

var app = builder.Build();

app.UseSharedKernelRequestContext();          // 1. first: inbound baggage refused, correlation id, the request's scope
app.UseSharedKernelWebApi(pipeline => pipeline // 2. security headers, exception handler, routing, CORS,
    .BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>())); //    authentication, (tenant), authorization
app.MapEndpoints();                           // 3. endpoints
```

This is the canonical HTTP pipeline (P-579). Without `SharedKernel.Presentation.WebApi`, the order is
`UseSharedKernelRequestContext()`, `UseExceptionHandler()`, `UseAuthentication()`, the optional
`TenantResolutionMiddleware` (`SharedKernel.MultiTenancy`), `UseAuthorization()`, then the endpoints.
`UseSharedKernelRequestContext()` throws `InvalidOperationException` at startup when `AddSharedKernelRequestContext()`
was not called.

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

- **Correlation id on every log line and every response.** Placed before the exception handler (before
  `UseSharedKernelWebApi()`, which adds it), the id is in the log context of the exception handler and every later
  middleware, in the `correlationId` member of every problem response, and in the `X-Correlation-Id` response header,
  written through `Response.OnStarting` — error responses included.
- **Caller baggage refused before anything reads it.** See [Inbound baggage](#inbound-baggage).
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
rejected value is never logged, echoed or put in baggage. `SharedKernel.Presentation.WebApi`'s
`HttpContext.GetCorrelationId()`, the gRPC `ErrorInfo` and SignalR's `HubCallerContext.GetCorrelationId()` all read it
from the request's scope; the presentation packages resolve no correlation id of their own (P-579).

## Inbound baggage

W3C `baggage` travels onward with every outgoing call and is copied onto log records, so a caller who can set it could
plant a tenant or user id that downstream services and log queries trust. By default this package refuses it:

- `AddSharedKernelRequestContext()` decorates the `DistributedContextPropagator` ASP.NET Core hosting reads requests
  with, so hosting takes no baggage from the request (not even its own "Request starting" log record carries it);
  trace context is read as before, and outgoing propagation is unchanged.
- `UseSharedKernelRequestContext()` removes any baggage item still on the request's `Activity` **before** it adds the
  correlation id, so the only baggage left is the platform's own.

```csharp
// Only behind a gateway that removes or rewrites caller-supplied baggage:
builder.Services.AddSharedKernelRequestContext(options => options.TrustInboundBaggage = true);
```

This moved here from `SharedKernel.Presentation.WebApi` (`SharedKernelWebApiOptions.TrustInboundBaggage`, P-579): the
edge that owns the correlation id owns the refusal. `SharedKernel.ServiceDefaults`' telemetry separately keeps the
caller's baggage out of OpenTelemetry's own store (`Baggage.Current`), whatever this setting says.

## A context that outlives its request

A SignalR connection keeps the context of the request that opened it, and with long polling that request ends long
before the connection. When the request ends, the middleware resolves the caller if nothing has yet, so the context
keeps answering from the caller the request authenticated, never from the request's disposed services.

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
a scope (this middleware — for REST and gRPC calls alike, since gRPC runs through the HTTP pipeline — the SignalR hub
filter, which reopens the connection's scope around every invocation, the MassTransit consume filter, the Temporal
activity interceptor, the scheduler's job runner), and otherwise the scope's `SecurityRequestContext`. The registration uses
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
- `14.Presentation`: the `correlationId` of problem responses and gRPC `ErrorInfo`, SignalR's `GetTenantId()` and
  `GetCorrelationId()`, and what a gRPC or hub method reads through `IRequestContext`.

## Related packages

- [`SharedKernel.MultiTenancy`](../SharedKernel.MultiTenancy/README.md) — header and directory tenant resolution
- [`SharedKernel.ServiceDefaults`](../SharedKernel.ServiceDefaults/README.md) — the composition base
- `SharedKernel.Execution` (`01.Core`) — `IRequestContext`, `RequestContextScope`, `CorrelationIds`, `TenantId`
