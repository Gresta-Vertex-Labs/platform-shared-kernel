# SharedKernel.Presentation.SignalR

SignalR on the platform's error contract. One call sets up a hub host that presents errors the way
`SharedKernel.Presentation.WebApi` presents them over HTTP, enforces the WebApi authorization requirements on hubs
and hub methods, and limits how often a connection may invoke hub methods.

It builds on `SharedKernel.Presentation.WebApi` (error presentation, authorization, correlation id) and has no
third-party dependencies.

---

## Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddSharedKernelWebApi();   // error presentation, authorization, correlation id, CORS
builder.AddSharedKernelSignalR();  // SignalR, the hub filters, the authorization policies

var app = builder.Build();

app.UseSharedKernelWebApi();       // correlation id, routing, CORS, authentication, authorization
app.MapHub<OrdersHub>("/hubs/orders");

app.Run();
```

`AddSharedKernelSignalR()` calls `AddSignalR()` and returns SignalR's own `ISignalRServerBuilder`, so protocols and
a scale-out backplane are added the usual way — for example `.AddStackExchangeRedis(connectionString)` from
`Microsoft.AspNetCore.SignalR.StackExchangeRedis`.

Settings bind from `SharedKernel:Presentation:SignalR` and are validated when the host starts; the `configure`
callback runs after binding:

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

The hub filters are global and wrap the filters registered after this call: call it before adding hub filters of
your own. Calling it twice registers everything once and applies each `configure`.

---

## Errors

Every error of a hub method becomes a `HubException` whose message is `{code}: {message}`:

| The hub method… | The error |
| --- | --- |
| returns a failed `Result` or `Result<T>`, or throws a `SharedKernelException` | its `Error.Code` and client message: translated into the connection's culture, and for a server error (`Unexpected`, `Unavailable`, `Timeout`) replaced by a generic sentence outside Development — exactly like an HTTP problem response |
| throws a `ValidationException` with several errors | `validation.failed: {n} validation errors occurred.` |
| throws any other exception | `unexpected.exception: An unexpected error occurred.` (in Development, the exception's message) |
| throws a `HubException` itself | passes unchanged |

Server errors are logged at Error, client errors at Debug.

### What a client receives

SignalR puts its own sentence in front of the message, so a client never receives the bare `{code}: {message}`:

| Case | Error text the client receives |
| --- | --- |
| An invocation fails; also a stream whose hub method fails before it returns the stream | `An unexpected error occurred invoking '{method}' on the server. HubException: {code}: {message}` |
| A stream fails after it started | `An error occurred on the server while streaming results.` — no code |
| Authorization refuses a hub method | `Failed to invoke '{method}' because user is unauthorized` — no code |

`{method}` is the hub method's declared name (its `[HubMethodName]`, if it has one). Splitting the text at the first
`": "` would cut SignalR's sentence; read it with `HubErrorMessage.TryParse`, which also reads the bare server-side
text and returns `false` for every text without a code:

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

---

## Result hub methods

```csharp
public sealed class OrdersHub(IOrderService orders) : Hub
{
    public Task<Result<OrderDto>> GetOrder(Guid id) => orders.GetAsync(id);   // success: the value
    public Task<Result> Cancel(Guid id) => orders.CancelAsync(id);             // success: nothing
}
```

A failure reaches the client as the coded error; a success returns the value (nothing for `Result`). A `Result` is
read only as the hub method's own return value: inside a stream item or a collection it cannot be serialized.

### Streams

SignalR streams only a hub method **declared** to return `IAsyncEnumerable<T>` or `ChannelReader<T>` (optionally
inside `Task` or `ValueTask`). A method returning `Result<IAsyncEnumerable<T>>` or `Result<ChannelReader<T>>` is an
ordinary invocation that cannot stream: its failure is the coded error, and its success is refused as
`unexpected.exception` — logged at Error (EventId 14107) with the fix, which the Development message shows too —
instead of closing the connection.

To stream with a failure that can happen before the first item, declare the stream type and throw the failure
before returning the stream, with `SharedKernel.Core`'s `GetValueOrThrow()`:

```csharp
public IAsyncEnumerable<OrderDto> Orders(Guid customerId) =>
    orders.Stream(customerId).GetValueOrThrow();   // orders.Stream returns Result<IAsyncEnumerable<OrderDto>>
```

A `StreamAsync` / `stream()` caller then gets the coded error or the items. An exception thrown while the stream is
read — after the hub method returned it — reaches the client without a code, as
`An error occurred on the server while streaming results.`: the error mapping wraps the hub method, not the reading
of its stream. SignalR's `HubOptions.EnableDetailedErrors` appends that exception's type name and message,
unredacted — keep it off outside Development.

---

## Authorization

The WebApi requirement attributes are `[Authorize]` attributes, so they work on hubs natively:

```csharp
[RequirePermission("orders.read")]      // on the hub: checked when the connection opens
public sealed class OrdersHub : Hub
{
    [RequireFreshAuthentication(300)]   // on a hub method: checked by SignalR at every invocation
    public Task<Result> Refund(Guid id) => ...;
}

app.MapHub<SupportHub>("/hubs/support").RequirePermission("support.chat");
```

- On a hub class or a `MapHub<T>()` endpoint they guard the connection, which is refused with 401 or 403.
- On a hub method SignalR checks them itself, before any hub filter runs. A refused invocation fails with SignalR's
  own `Failed to invoke '{method}' because user is unauthorized`: it has no code, never reaches the method, and does
  not count against the rate limit. When a client must tell refusals apart, refuse at connection level, or return
  `Error.Forbidden(...)` from the method.
- A hub method's requirements are checked against the principal the connection was opened with (`Context.User`),
  at every invocation. That principal is never refreshed while the connection stays open, so a step-up lasts only
  as long as it allows: `[RequireFreshAuthentication]` compares its authentication time with the current time and
  lapses on an open connection once the maximum age has passed; `[RequireAuthenticationMethod]` without a maximum
  age keeps passing for as long as the connection stays open, and a maximum age for the method, where the attribute
  takes one, makes that step-up lapse on an open connection too.
  `MapHub<T>(path, options => options.CloseOnAuthenticationExpiration = true)` closes a connection when its
  authentication expires.

The host needs `UseAuthentication()` and `UseAuthorization()`; `UseSharedKernelWebApi()` does both.

---

## Invocation rate limit

Off by default. With `InvocationRateLimit:PermitLimit` set, each connection gets a token bucket of that many
invocations, refilled at that many per `Window` (default one second): a connection may burst up to the limit and
then sustain it. A refused invocation never reaches the hub method, fails with
`rate_limit.exceeded: Too many requests.` and is logged at Warning (EventId 14101). All buckets live in one
partitioned limiter keyed by connection id, with one replenishment timer for the whole server.

---

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
  `null`, never `Guid.Empty`, when there is no tenant, so tenantless connections never share a group.
- `HubGroupNaming.TenantGroup(tenantId)` is `tenant:{tenantId:D}` and throws for `Guid.Empty`. Build every group name
  there, never inline.
- `Context.GetCorrelationId()` is the correlation id `UseSharedKernelWebApi()` resolved for the request that opened
  the connection, or `null` without that middleware.

---

## CORS

Hubs are ordinary endpoints. The CORS policy of `SharedKernel:Presentation:WebApi:Cors`, applied by
`UseSharedKernelWebApi()`, covers negotiate and the HTTP transports; and when origins are configured, a WebSocket
from an origin outside the list is refused with 403, since browsers apply no CORS to WebSockets.
