# SharedKernel.ServiceDefaults.Security

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **One request context for every layer that asks who is calling, and the HTTP middleware that fills it: the caller,
> the tenant and one correlation id — from the first middleware to the last log line and every outbound call.**

| You get | So that |
| --- | --- |
| `AddSharedKernelRequestContext()` | `IRequestContext` (`SharedKernel.Execution`) is built over `12.Security`'s `IUserContext` — one contract for every layer |
| `app.UseSharedKernelRequestContext()` | Each request runs in a `RequestContextScope`: handlers, repositories, audit, messages and outbound calls see one caller |
| A validated `X-Correlation-Id` | Kept end to end when valid, replaced when missing or malformed, echoed on every response — errors included |
| Inbound W3C baggage refused | A caller cannot plant a tenant or user id that downstream services and log queries would trust |
| REST, gRPC and SignalR alike | gRPC runs through the HTTP pipeline, and the SignalR hub filter reopens the connection's scope per invocation |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.ServiceDefaults.Security" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** / **Worker** project |
| Depends on | `SharedKernel.ServiceDefaults`, `SharedKernel.Execution`, `SharedKernel.Security.Abstractions` |
| Namespaces | `SharedKernel.ServiceDefaults.Security` |

## Quick start

```csharp
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.Security;

builder.AddServiceDefaults();
builder.Services.AddOidcAuthentication(builder.Configuration);   // any 12.Security scheme that registers IUserContext
builder.Services.AddSharedKernelRequestContext();
builder.AddSharedKernelWebApi();

var app = builder.Build();

app.UseSharedKernelRequestContext();                       // 1. first: baggage refused, correlation id, the scope
app.UseSharedKernelWebApi(pipeline => pipeline             // 2. headers, exception handler, routing, CORS,
    .BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()));   // authentication, tenant, authorization
app.MapEndpoints();                                        // 3. endpoints
```

Without `SharedKernel.Presentation.WebApi` the order is `UseSharedKernelRequestContext()`, `UseExceptionHandler()`,
`UseAuthentication()`, the optional `TenantResolutionMiddleware`, `UseAuthorization()`, then the endpoints.

Read the caller anywhere through `IRequestContext` (injected) or `IRequestContextAccessor.Current` (in a singleton):

```csharp
using SharedKernel.Execution.Context;

public sealed class PlaceOrderHandler(IRequestContext caller) : ICommandHandler<PlaceOrder, OrderId>
{
    public Task<Result<OrderId>> Handle(PlaceOrder command, CancellationToken ct)
    {
        TenantId? tenant = caller.TenantId;           // null fails closed in persistence
        string? correlationId = caller.CorrelationId; // the request's X-Correlation-Id
        ...
    }
}
```

## How it works

- **Why it goes first.** Placed before the exception handler, the correlation id is in the log context of every later
  middleware, in the `correlationId` of every problem response, and in the `X-Correlation-Id` response header
  (written through `Response.OnStarting`, error responses included).
- **Safe before `UseAuthentication()`.** The caller is read lazily, the first time something asks — after
  authentication has run. Nothing between this middleware and `UseAuthentication()` should read the caller.
- **Tenant resolution refines, never replaces.** `TenantResolutionMiddleware` opens an inner scope that changes only
  the tenant; the caller and correlation id stay this middleware's.
- **Correlation id:**

  | Inbound `X-Correlation-Id` | Result |
  | --- | --- |
  | Absent or whitespace | A new id, `CorrelationIds.New()` (EventId 13006) |
  | Accepted by `CorrelationIds.IsValid` (≤ 128 characters of `[A-Za-z0-9-_:.]`) | Kept unchanged |
  | Anything else | A new id; only the rejected value's **length** is logged (EventId 13007), never its content |

  The id is set as `Activity` baggage (`WellKnownBaggageKeys.CorrelationId`) for log enrichment and forwarded by every
  outbound adapter through `RequestContextPropagation` (REST, gRPC, the message bus, Temporal).
- **Inbound baggage.** `AddSharedKernelRequestContext()` decorates the `DistributedContextPropagator` ASP.NET Core
  hosting reads requests with, so hosting takes no baggage from the request (trace context is read as before).
  `UseSharedKernelRequestContext()` removes any item still on the request's `Activity` before adding the correlation
  id, so the only baggage left is the platform's own.
- **A context that outlives its request.** A SignalR connection keeps the context of the request that opened it. When
  the request ends, the middleware resolves the caller if nothing has yet, so the context keeps answering from the
  authenticated caller, never from disposed request services.
- **The mapping:**

  | `IRequestContext` | From |
  | --- | --- |
  | `IsAuthenticated` | `IUserContext.IsAuthenticated` |
  | `UserId` | `SubjectId`, else `ClientId`; `null` when unauthenticated |
  | `TenantId` | `IUserContext.TenantId`, unless `TenantResolutionMiddleware` resolved another; `null` fails closed |
  | `ActorKind` | `IUserContext.ActorKind` when authenticated; `ActorKind.Anonymous` otherwise — never `System` |
  | `ClientId`, `SessionId` | `IUserContext` |
  | `CorrelationId` | The HTTP request's id |
  | `HasPermissionAsync` | `IUserContext.HasPermission` (ordinal) |

  `IRequestContext` is registered **transient**: it returns `RequestContextScope.Current` when an inbound adapter
  opened a scope (this middleware, the SignalR hub filter, the MassTransit consume filter, the Temporal activity
  interceptor, the scheduler's job runner). The registration uses `Add`, so it replaces the fail-closed
  `AnonymousRequestContext` default `SharedKernel.Persistence.EfCore` registers, whatever the call order.
- **Who reads it:** `05.Application`'s `[RequirePermission]` check and cache-key scope, `18.Idempotency`'s key scope,
  `06.Persistence`'s audit columns, tenant filters and row-level security, `11.Communication`/`07.Messaging`/
  `17.Workflows` propagation, and `14.Presentation`'s problem `correlationId` and gRPC `ErrorInfo`.

## Configuration

Set in code on `AddSharedKernelRequestContext(o => …)` (`RequestContextOptions`); nothing is bound from configuration.

| Option | Type | Default | Meaning |
| --- | --- | --- | --- |
| `TrustInboundBaggage` | `bool` | `false` | Keep the caller's W3C baggage. Only behind a gateway that removes or rewrites caller-supplied baggage |

`SharedKernel.ServiceDefaults`' telemetry keeps the caller's baggage out of OpenTelemetry's `Baggage.Current`
whatever this setting says.

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `IServiceCollection.AddSharedKernelRequestContext(Action<RequestContextOptions>?)` | `IRequestContext` (transient), `IRequestContextAccessor`, the baggage-refusing propagator. Safe to call more than once |
| `IApplicationBuilder.UseSharedKernelRequestContext()` | The request-context middleware; throws `InvalidOperationException` at startup when `AddSharedKernelRequestContext()` was not called |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 13006 | Debug | Created a correlation id for an inbound request that carried none |
| 13007 | Warning | Rejected a caller-supplied correlation id of length `{CorrelationIdLength}`; created a new one |

## Testing

- Unit tests of code that reads the caller: `TestRequestContext` from
  [`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Testing/README.md)
  (`TestRequestContext.ForTenant(tenantId)`, `.ForUser(...)`, `.System(...)`, `.Anonymous()`), registered with
  `services.AddTestRequestContext(context)` from `SharedKernel.Persistence.Testing` or opened with
  `RequestContextScope.Begin(context)`.
- Host tests through the real middleware: replace the user with
  [`SharedKernel.Security.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Security.Testing/README.md)'s
  `FakeUserContext` (`services.AddSingleton<IUserContext>(new FakeUserContext { TenantId = tenantId })`) in a
  `WebApplicationFactory<Program>`, then assert the `X-Correlation-Id` response header.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Put `UseSharedKernelRequestContext()` after `UseExceptionHandler()` or `UseSharedKernelWebApi()` | Make it the first middleware | Problem responses and error logs would lack the correlation id |
| Read `IRequestContext` in middleware before `UseAuthentication()` | Read it after authentication | The caller would be fixed as anonymous |
| Read the tenant from `IUserContext` in application code | Read `IRequestContext.TenantId` | Only the request context carries the tenant `TenantResolutionMiddleware` resolved |
| Turn on `TrustInboundBaggage` on an internet-facing service | Leave it off unless a gateway strips caller baggage | Baggage is caller-controlled and travels downstream |
| Use `Activity.Id` as a correlation id | Use `IRequestContext.CorrelationId` | The correlation id keeps its original value end to end |

## Design decisions

**Why transient?** A transient `IRequestContext` always answers from the innermost scope — the tenant scope
`TenantResolutionMiddleware` opens, a SignalR invocation's scope — rather than a value captured when a scoped service
was first built.

**Why an unauthenticated caller is `Anonymous`, not `System`?** The audit trail and the cross-tenant-scope log must
tell an anonymous request from the platform's own background work, which runs under a `SystemRequestContext` with an
explicit permission set.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[ServiceDefaults domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/13.ServiceDefaults/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
