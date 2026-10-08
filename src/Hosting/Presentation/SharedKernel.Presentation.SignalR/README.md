# SharedKernel.Presentation.SignalR

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **SignalR on the platform's error contract: hub errors carry the error code and the same message an HTTP problem
> would, hub methods may return `Result`, the shared authorization attributes guard hubs and hub methods, and every
> hub method runs in the caller's request context.** Use it for real-time hubs in a host that already runs
> `SharedKernel.Presentation.WebApi`; plain request/response endpoints stay in WebApi.

| You get | So that |
| --- | --- |
| `builder.AddSharedKernelSignalR()` | One call sets up SignalR with the platform's hub filters and authorization |
| Coded hub errors (`{code}: {message}`) + `HubErrorMessage.TryParse` | Clients branch on `order.not_found` exactly as over HTTP, with server errors redacted outside Development |
| Hub methods returning `Result` / `Result<T>` | A hub method is one line over `ISender`, like an endpoint |
| The connection's `IRequestContext` reopened around every invocation | Handlers, repositories, outbound calls and logs see the caller, tenant and correlation id |
| `Context.GetTenantId()`, `HubGroupNaming.TenantGroup(tenantId)` | Tenant broadcasts use one group-name format and never mix tenants |
| An optional per-connection invocation rate limit | One chatty connection cannot flood the server |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Presentation.SignalR" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** / **Worker** project |
| Depends on | [`SharedKernel.Presentation.WebApi`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/SharedKernel.Presentation.WebApi/README.md), `SharedKernel.Presentation.Core`, `SharedKernel.Core`, `SharedKernel.Configuration`, `SharedKernel.Execution`; no third-party packages |
| Namespaces | `SharedKernel.Presentation.SignalR` |

## Quick start

```csharp
using SharedKernel.Presentation.Authorization;
using SharedKernel.Presentation.SignalR;
using SharedKernel.Presentation.WebApi;
using SharedKernel.ServiceDefaults.Security;

builder.Services.AddSharedKernelRequestContext();   // the caller, tenant and correlation id of each connection
builder.AddSharedKernelWebApi();                    // error presentation, authorization, CORS
builder.AddSharedKernelSignalR();                   // SignalR, the hub filters, the authorization policies

var app = builder.Build();
app.UseSharedKernelRequestContext();                // first
app.UseSharedKernelWebApi();                        // CORS, authentication, authorization
app.MapHub<OrdersHub>("/hubs/orders", o => o.CloseOnAuthenticationExpiration = true)
    .RequireEndpointPermission("orders.read");
app.Run();
```

```csharp
using Microsoft.AspNetCore.SignalR;
using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;             // Map
using SharedKernel.Presentation.Authorization;
using SharedKernel.Primitives.Results;

[RequireEndpointPermission("orders.read")]      // checked when the connection opens: 401 or 403
public sealed class OrdersHub(ISender sender) : Hub
{
    public async Task<Result<OrderResponse>> GetOrder(Guid id) =>
        (await sender.Send(new GetOrderQuery(id), Context.ConnectionAborted)).Map(OrderResponse.From);

    [RequireAuthenticationMethod("otp", MaxAgeSeconds = 300)]   // checked at every invocation
    public Task<Result> Refund(Guid id) => sender.Send(new RefundOrder(id), Context.ConnectionAborted);
}
```

## How it works

- `AddSharedKernelSignalR()` calls `AddSignalR()`, registers the shared authorization policies (with WebApi's problem
  body for a refused negotiate request) and adds three global hub filters: the request context outermost, then the
  error mapping, then the invocation rate limit. It binds and validates `SharedKernel:Presentation:SignalR`, is
  idempotent, and returns SignalR's `ISignalRServerBuilder` (for protocols or a backplane). Filters wrap hub method
  invocations; the request-context filter also wraps `OnConnectedAsync`/`OnDisconnectedAsync`.
- **Errors.** Every failed invocation becomes a `HubException` with message `{code}: {message}`, presented by the same
  rules as an HTTP problem:

  | The hub method… | Error text |
  | --- | --- |
  | Returns a failed `Result`, or throws a `SharedKernelException` | `{Error.Code}: {client message}` — translated; generic outside Development for `Unexpected`/`Unavailable`/`Timeout` |
  | Throws a `ValidationException` | One error: its code and message; several: `validation.failed: {n} validation errors occurred.` |
  | Throws a `TimeoutException`, or is cancelled while the connection is open | `timeout.default: The operation did not complete in time.` |
  | Is refused by the invocation rate limit | `rate_limit.exceeded: Too many requests.` |
  | Throws anything else | `unexpected.exception: An unexpected error occurred.` (the exception's message in Development) |
  | Throws a `HubException` itself | Unchanged |
  | Is cancelled because the connection closed | Rethrown, logged at Debug |

  SignalR prefixes its own sentence (`An unexpected error occurred invoking '{method}' on the server. HubException: …`),
  so read the text with `HubErrorMessage.TryParse`. A stream that fails after it started, and a hub method refused by
  authorization, reach the client without a code.
- **Request context.** A hub invocation does not run in the flow of the request that opened the connection. The
  outermost filter captures that request's `IRequestContext` when the connection opens and reopens it around connect,
  every hub method and disconnect. Like `Context.User`, it is never refreshed while the connection stays open.
- **Authorization.** On a hub class or `MapHub<T>()` the attributes guard the connection (401/403 with the platform's
  problem). On a hub method SignalR checks them before any filter, against the principal the connection opened with:
  `[RequireFreshAuthentication(300)]` lapses 300 s after sign-in; `[RequireAuthenticationMethod("otp")]` keeps passing;
  with `MaxAgeSeconds` it lapses that long after the method was verified.
- **Rate limit.** With `PermitLimit` set, each connection gets a token bucket of that many invocations, refilled at that
  many per `Window`; all buckets live in one partitioned limiter keyed by connection id.
- **CORS.** `SharedKernel:Presentation:WebApi:Cors` covers negotiate and the HTTP transports. Browsers apply no CORS to
  WebSockets, so with origins configured a WebSocket from an unlisted origin is refused with 403
  `forbidden.origin_not_allowed`.

## Recipes

### 1. Read a coded error on the client

```csharp
try
{
    await connection.InvokeAsync("PlaceOrder", order);
}
catch (HubException exception) when (HubErrorMessage.TryParse(exception.Message, out var code, out var message))
{
    // code: "order.not_found", message: "Order 42 was not found."
}
```

A browser uses the same pattern:

```js
const coded = /(?:^| HubException: )(?<code>[^\s:]+): (?<message>[\s\S]*)$/;
try { await connection.invoke("PlaceOrder", order); }
catch (error) { const match = coded.exec(error.message); /* match?.groups.code */ }
```

### 2. Broadcast to one tenant

```csharp
public override async Task OnConnectedAsync()
{
    if (Context.GetTenantId() is { } tenantId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroupNaming.TenantGroup(tenantId));
    }
    await base.OnConnectedAsync();
}

// Elsewhere, through IHubContext<OrdersHub>:
await hubContext.Clients.Group(HubGroupNaming.TenantGroup(tenantId)).SendAsync("OrderUpdated", orderId);
```

`TenantGroup` is `tenant:{tenantId:D}` and throws for an empty id; tenantless connections never share a group.

### 3. Stream with an early failure

SignalR streams only a method **declared** to return `IAsyncEnumerable<T>` or `ChannelReader<T>`. Throw the failure
before returning the stream:

```csharp
public IAsyncEnumerable<OrderResponse> Orders(Guid customerId) =>
    streams.Stream(customerId).GetValueOrThrow();   // Stream returns Result<IAsyncEnumerable<OrderResponse>>
```

A method returning `Result<IAsyncEnumerable<T>>` cannot stream: its success is refused as `unexpected.exception`
(EventId 14107).

### 4. Scale out

```csharp
builder.AddSharedKernelSignalR().AddStackExchangeRedis("redis:6379");   // Microsoft.AspNetCore.SignalR.StackExchangeRedis
```

## Configuration

Section `SharedKernel:Presentation:SignalR` (`SharedKernelSignalROptions`), validated at host start; `configure` runs
after binding. See [CONFIGURATION.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/CONFIGURATION.md#sharedkernelpresentationsignalr).

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Presentation:SignalR:InvocationRateLimit:PermitLimit` | `int?` | `null` (off) | Invocations per connection per `Window`, and the burst size |
| `SharedKernel:Presentation:SignalR:InvocationRateLimit:Window` | `TimeSpan` | `00:00:01` | The refill period |

SignalR's own settings (message size, keep-alive, timeouts) stay on `HubOptions`, with the framework's defaults.

## Reference

| Member | Purpose |
| --- | --- |
| `IHostApplicationBuilder.AddSharedKernelSignalR(Action<SharedKernelSignalROptions>?)` | SignalR + filters + authorization; returns `ISignalRServerBuilder` |
| `HubErrorMessage.TryParse(string?, out string? code, out string? message)` | Reads a coded error, bare or with SignalR's prefix |
| `HubCallerContext.GetTenantId()` → `TenantId?` | The connection's tenant |
| `HubCallerContext.GetCorrelationId()` → `string?` | The correlation id of the request that opened the connection |
| `HubGroupNaming.TenantGroup(TenantId or Guid)` | The tenant group name |
| `SharedKernelSignalROptions`, `SignalRInvocationRateLimitOptions` | The options above |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 14100 | Error | An exception that is not a `SharedKernelException` or a timeout |
| 14101 | Warning | The invocation rate limit refused an invocation |
| 14103 | Error | A hub method failed with a server error |
| 14104 | Debug | A hub method was rejected with a client error |
| 14106 | Debug | The connection closed before the hub method completed |
| 14107 | Error | A hub method returned a stream inside a `Result` |

## Testing

Run the hub in a `WebApplicationFactory<Program>` host and connect with `HubConnectionBuilder` over the test server's
handler (`WithUrl(url, o => o.HttpMessageHandlerFactory = _ => server.CreateHandler())`); assert coded failures with
`HubErrorMessage.TryParse`. Give the connection a caller with
[`SharedKernel.Security.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Security/SharedKernel.Security.Testing/README.md)'s
`FakeUserContext`. Hub methods called directly (no filters) return the `Result` itself — assert it.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Split the error text at the first `": "` | Use `HubErrorMessage.TryParse` or the documented pattern | SignalR puts its own sentence in front |
| Return `Result` from a hub on a plain SignalR host | Call `AddSharedKernelSignalR()`, or return the value and end with `GetValueOrThrow()` | SignalR cannot serialize `Result`; the connection fails |
| Put step-up on the hub class | Put it on the hub method with a maximum age | Class-level requirements are checked once, at connect |
| Register your own hub filters before `AddSharedKernelSignalR()` | Add them after | They would run outside the error mapping and reach clients uncoded |
| Turn on `HubOptions.EnableDetailedErrors` outside Development | Leave it off | It sends unredacted exception details of failed streams |
| Build group names inline | Use `HubGroupNaming.TenantGroup` | One format, and an empty tenant is refused |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Presentation packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
