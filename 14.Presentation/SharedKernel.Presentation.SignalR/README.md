# SharedKernel.Presentation.SignalR

> **SignalR on the platform's error contract: hub errors carry the error code and the same message an HTTP problem
> would, hub methods may return `Result`, and the WebApi authorization attributes guard hubs and hub methods.**

One call sets up a hub host that presents errors the way
[`SharedKernel.Presentation.WebApi`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/14.Presentation/SharedKernel.Presentation.WebApi)
presents them over HTTP, enforces its authorization requirements, and limits how often a connection may invoke hub
methods. It builds on the WebApi core (error presentation, authorization, correlation ids) and has no third-party
dependencies.

## Install

```shell
dotnet add package SharedKernel.Presentation.SignalR
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Dependencies | `SharedKernel.Presentation.WebApi`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Configuration`, `SharedKernel.Security.Abstractions`, the ASP.NET Core shared framework |
| Third-party packages | none |

## Setup

```csharp
using SharedKernel.Presentation.SignalR;
using SharedKernel.Presentation.WebApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedKernelWebApi();    // error presentation, authorization, correlation ids, CORS
builder.AddSharedKernelSignalR()    // SignalR, the hub filters and the authorization policies
    .AddStackExchangeRedis("redis:6379");   // optional scale-out, the usual way

var app = builder.Build();

app.UseSharedKernelWebApi();        // correlation ids, CORS, authentication, authorization
app.MapHub<OrdersHub>("/hubs/orders", options => options.CloseOnAuthenticationExpiration = true)
    .RequirePermission("orders.read");

app.Run();
```

- `AddSharedKernelSignalR()` calls `AddSignalR()`, `AddSharedKernelAuthorization()` and adds two global hub filters:
  the error mapping, and inside it the invocation rate limit. It returns SignalR's own `ISignalRServerBuilder`, for
  protocols or a backplane (`AddStackExchangeRedis` comes from `Microsoft.AspNetCore.SignalR.StackExchangeRedis`).
- It binds `SharedKernel:Presentation:SignalR` and validates it when the host starts; the `configure` callback runs
  after binding. It is idempotent.
- The filters are global and wrap filters registered after this call: call it before adding hub filters of your own.
  They wrap hub method invocations, not `OnConnectedAsync` or `OnDisconnectedAsync`.
- SignalR's own settings (message size, keep-alive, timeouts) stay on `HubOptions`, with the framework's defaults.

## Errors

Every error of a hub method invocation becomes a `HubException` whose message is `{code}: {message}`, presented by the
same `ErrorPresentation` HTTP uses:

| The hub method… | Error text |
| --- | --- |
| returns a failed `Result` or `Result<T>`, or throws a `SharedKernelException` | `{Error.Code}: {client message}`: translated into the connection's culture, and for a server error (`Unexpected`, `Unavailable`, `Timeout`) replaced by a generic sentence outside Development, as in an HTTP problem |
| throws a `ValidationException` | one error: its own code and message; several: `validation.failed: {n} validation errors occurred.` |
| throws a `TimeoutException`, or an `OperationCanceledException` while the connection is open | `timeout.default: The operation did not complete in time.`, what HTTP answers with 504 |
| is refused by the invocation rate limit | `rate_limit.exceeded: Too many requests.` |
| throws any other exception | `unexpected.exception: An unexpected error occurred.` (in Development, the exception's message) |
| throws a `HubException` itself | passes unchanged |
| is cancelled because the connection closed | rethrown, logged at Debug: no client is left to answer |

Server errors are logged at Error, client errors at Debug. The inner exception stays on the server.

### What a client receives

SignalR puts its own sentence in front of the message, so a client never receives the bare `{code}: {message}`:

| Case | Text the client receives |
| --- | --- |
| An invocation fails; also a stream whose hub method fails before it returns the stream | `An unexpected error occurred invoking '{method}' on the server. HubException: {code}: {message}` |
| A stream fails after it started | `An error occurred on the server while streaming results.`, no code |
| Authorization refuses a hub method | `Failed to invoke '{method}' because user is unauthorized`, no code |

`{method}` is the hub method's declared name (its `[HubMethodName]`, if it has one). The sentence is SignalR's even
for an expected failure such as `order.not_found`. Splitting the text at the first `": "` cuts SignalR's sentence;
read it with `HubErrorMessage.TryParse`, which also reads the bare server-side text and returns `false` for every text
without a code:

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

A browser client reads the same text with the same pattern:

```js
const coded = /(?:^| HubException: )(?<code>[^\s:]+): (?<message>[\s\S]*)$/;

try {
  await connection.invoke("PlaceOrder", order);
} catch (error) {
  const match = coded.exec(error.message);
  if (match?.groups.code === "order.not_found") {
    showNotFound(match.groups.message);
  }
}
```

`HubErrorMessage` is in this package, which needs the ASP.NET Core shared framework; a client that cannot reference
it uses the pattern above.

## Result hub methods

```csharp
[RequirePermission("orders.read")]                  // checked when the connection opens: 401 or 403
public sealed class OrdersHub(IOrderService orders) : Hub
{
    // A failure reaches the client as "{code}: {message}"; a success returns the order.
    public async Task<Result<OrderResponse>> GetOrder(Guid id) =>
        (await orders.GetAsync(id, Context.ConnectionAborted)).Map(OrderResponse.From);

    [RequirePermission("orders.refund")]
    [RequireAuthenticationMethod("otp", MaxAgeSeconds = 300)]   // checked at every invocation
    public Task<Result> Refund(Guid id) => orders.RefundAsync(id, Context.ConnectionAborted);
}
```

A failure reaches the client as the coded error; a success returns the value (nothing for `Result`). `Map` and
`GetValueOrThrow` are `SharedKernel.Core`'s (`SharedKernel.Core.Extensions`).

- A hub returning `Result` needs `AddSharedKernelSignalR()`: without the filter, SignalR serializes the `Result` itself
  and the connection fails. A `Result` is read only as the hub method's own return value; inside a stream item or a
  collection it cannot be serialized.
- The typed style works on any SignalR host and shows typed-client generators the real payload: declare the value
  type and end with `GetValueOrThrow()`, whose `SharedKernelException` the filter maps to the same coded error.

  ```csharp
  public async Task<OrderResponse> GetOrder(Guid id) =>
      OrderResponse.From(await orders.GetAsync(id, Context.ConnectionAborted).GetValueOrThrow());
  ```

### Streams

SignalR streams only a hub method **declared** to return `IAsyncEnumerable<T>` or `ChannelReader<T>` (optionally
inside `Task` or `ValueTask`). A method returning `Result<IAsyncEnumerable<T>>` is an ordinary invocation that cannot
stream: its failure is the coded error, and its success is refused as `unexpected.exception`, logged at Error (14107)
with the fix, instead of closing the connection.

To stream with a failure that can happen before the first item, declare the stream type and throw the failure before
returning the stream:

```csharp
public IAsyncEnumerable<OrderResponse> Orders(Guid customerId) =>
    streams.Stream(customerId).GetValueOrThrow();   // Stream returns Result<IAsyncEnumerable<OrderResponse>>
```

A `StreamAsync` or `stream()` caller then gets the coded error or the items. An exception thrown while the stream is
read, after the hub method returned it, reaches the client without a code: the error mapping wraps the hub method,
not the reading of its stream. SignalR's `HubOptions.EnableDetailedErrors` appends that exception's type and message,
unredacted; keep it off outside Development.

## Authorization

The WebApi requirement attributes are `[Authorize]` attributes, so SignalR enforces them natively:

- On a hub class or a `MapHub<T>()` endpoint they guard the connection, which is refused with 401 or 403 and the
  platform's problem body.
- On a hub method SignalR checks them before any hub filter runs. A refused invocation fails with SignalR's own
  `Failed to invoke '{method}' because user is unauthorized`: it has no code, never reaches the method, and does not
  count against the rate limit. When a client must tell refusals apart, refuse at connection level, or return
  `Error.Forbidden(…)` from the method.
- The host needs `UseAuthentication()` and `UseAuthorization()`; `UseSharedKernelWebApi()` adds both.

A hub method's requirements are checked at every invocation against the principal the connection opened with
(`Context.User`), which is never refreshed while the connection stays open:

| Requirement on a hub method | On a connection that stays open |
| --- | --- |
| `[RequireFreshAuthentication(300)]` | Lapses 300 seconds after the sign-in |
| `[RequireAuthenticationMethod("otp")]` | Keeps passing for as long as the connection is open |
| `[RequireAuthenticationMethod("otp", MaxAgeSeconds = 300)]` | Lapses 300 seconds after the method was verified (the step-up's `amr_time`, else the sign-in) |

Put step-up requirements on hub methods with a maximum age; on the hub class or `MapHub<T>()` they are checked once,
when the connection opens. Keep the maximum age no longer than the step-up's own window
(`TotpStepUpOptions.FreshnessWindow`). SignalR's `CloseOnAuthenticationExpiration` on `MapHub` closes a connection
when its authentication expires.

## Invocation rate limit

Off by default. With `InvocationRateLimit:PermitLimit` set, each connection gets a token bucket of that many
invocations, refilled at that many per `Window` (one second by default): a connection may burst up to the limit and
then sustain it. One connection exhausting its bucket never slows another.

- A refused invocation never reaches the hub method and fails with `rate_limit.exceeded: Too many requests.`, logged
  at Warning (14101).
- All buckets live in one partitioned limiter keyed by connection id, with one replenishment timer for the whole
  server; a closed connection's bucket is dropped once it has refilled.

```json
{
  "SharedKernel": {
    "Presentation": {
      "SignalR": {
        "InvocationRateLimit": { "PermitLimit": 20, "Window": "00:00:10" }
      }
    }
  }
}
```

The keys and rules are in
[CONFIGURATION.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/CONFIGURATION.md#sharedkernelpresentationsignalr).

## Tenant and correlation id

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

- `Context.GetTenantId()` asks the registered `ITenantProvider` about the caller that opened the connection. It is
  `null`, never `Guid.Empty`, without a provider or a tenant, so tenantless connections never share a group.
- `HubGroupNaming.TenantGroup(tenantId)` is `tenant:{tenantId:D}` and throws for `Guid.Empty`. Build every group name
  there, never inline.
- `Context.GetCorrelationId()` is the correlation id `UseSharedKernelWebApi()` resolved for the request that opened
  the connection, or `null` without that middleware.

## CORS

Hubs are ordinary endpoints. The CORS policy of `SharedKernel:Presentation:WebApi:Cors`, applied by
`UseSharedKernelWebApi()`, covers negotiate and the HTTP transports. Browsers apply no CORS to WebSockets, so when
origins are configured a WebSocket from an origin outside the list is refused with 403 `forbidden.origin_not_allowed`.

## Logging

EventIds 14100–14199.

| EventId | Level | Event |
| --- | --- | --- |
| 14100 | Error | An exception that is not a `SharedKernelException` or a timeout, with the exception |
| 14101 | Warning | The invocation rate limit refused a hub method invocation |
| 14103 | Error | A hub method failed with a server error (`Unexpected`, `Unavailable`, `Timeout`) |
| 14104 | Debug | A hub method was rejected with a client error |
| 14106 | Debug | The connection closed before the hub method completed |
| 14107 | Error | A hub method returned a stream inside a `Result` |

14102 and 14105 belonged to removed types and are not reused.

## Pitfalls

- **Parsing the error text yourself.** Use `HubErrorMessage.TryParse` or the documented pattern.
- **Hub methods returning `Result` on a plain SignalR host.** The connection fails; call `AddSharedKernelSignalR()`, or
  use the typed style.
- **Step-up on the hub class.** It is checked once, at connect; put it on the hub method with a maximum age.
- **Own hub filters registered before `AddSharedKernelSignalR()`.** They run outside the error mapping, so their
  exceptions reach clients uncoded.
- **`EnableDetailedErrors` in production.** It sends exception details of failed streams to clients.

## Not in this package

| Looking for | Use instead |
| --- | --- |
| `WithRedisBackplane` | `AddSharedKernelSignalR().AddStackExchangeRedis(…)` from `Microsoft.AspNetCore.SignalR.StackExchangeRedis` |
| `TenantContextHubFilter` and its `Context.Items` keys | `Context.GetTenantId()` |
| `SignalRCorsStartupDiagnostic` | The WebApi CORS settings and its WebSocket origin check |
| `HubOptions` pins (message size, parallel invocations, timeouts) | SignalR's defaults, adjusted on `HubOptions` |
| `MaxStringArgumentLength`, `ArgumentValidators` | Validation in the hub method or the application layer |
| `AddSharedKernelSignalR(IServiceCollection, …)` | `builder.AddSharedKernelSignalR(…)` on the host builder |
