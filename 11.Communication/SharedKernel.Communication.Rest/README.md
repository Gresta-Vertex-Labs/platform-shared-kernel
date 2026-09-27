# SharedKernel.Communication.Rest

Typed REST clients for services on SharedKernel. One line registers a client; its address, timeouts, retries and
credentials live in configuration; every call carries the caller; and a typed client's methods return `Result` —
the same `Error` the other service returned, or one for a service that did not answer — instead of throwing.

```csharp
builder.Services.AddSharedKernelCommunication(builder.Configuration)
    .AddRestClient<IInventoryClient, InventoryClient>("inventory");
```

```json
"SharedKernel": {
  "Communication": {
    "Clients": {
      "inventory": {
        "BaseAddress": "http://inventory",
        "PropagateIdempotencyKey": true,
        "Authentication": { "Mode": "ApiKey", "ApiKey": { "Value": "(from a secret store)" } }
      }
    }
  }
}
```

```csharp
public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public Task<Result<StockLevel>> GetStockAsync(string sku, CancellationToken ct) =>
        http.GetResultAsync($"stock/{Uri.EscapeDataString(sku)}", InventoryJson.Default.StockLevel, ct);

    public Task<Result<Reservation>> ReserveAsync(ReserveRequest request, CancellationToken ct) =>
        http.PostResultAsync("reservations", request, InventoryJson.Default.ReserveRequest, InventoryJson.Default.Reservation, ct);
}
```

Service discovery, authentication (client credentials, API key, your own token provider), mutual TLS and the error
codes are shared with gRPC clients and described in [`SharedKernel.Communication`](../SharedKernel.Communication/README.md).

## Registering a client

| Call | Registers |
| --- | --- |
| `AddRestClient<TClient, TImplementation>(name)` | The interface the application injects, implemented by a class that takes `HttpClient` in its constructor. |
| `AddRestClient<TClient>(name)` | A class, injected as itself. An interface without an implementation is refused. |

The optional second argument adjusts the client:

```csharp
.AddRestClient<IInventoryClient, InventoryClient>("inventory", client => client
    .Configure(o => o.AttemptTimeout = TimeSpan.FromSeconds(3))   // after configuration binding
    .UseHedging()                                                // instead of retries
    .UseAccessTokenProvider<ManagedIdentityTokenProvider>())     // your own credential
```

`client.HttpClientBuilder` is the underlying `IHttpClientBuilder`, for a handler of your own.

## Settings

`SharedKernel:Communication:Clients:{name}`, validated when the host starts (`RestClientOptions`):

| Setting | Default | |
| --- | --- | --- |
| `BaseAddress` | — (required) | `http://inventory`, `https+http://inventory`, `https://api.example.com/v2/`. End a base path with `/`. |
| `AttemptTimeout` | 10 s | One attempt. |
| `TotalTimeout` | 30 s | The whole call, retries and their delays included; at least `AttemptTimeout`. |
| `Retry:MaxRetryAttempts` | 3 | Retries after the first attempt; `0` turns retries off. |
| `Retry:BaseDelay` | 500 ms | Doubles each retry, with jitter; a `Retry-After` from the service wins. |
| `Retry:RetryNonIdempotentMethods` | `false` | Retry POST and PATCH without an `Idempotency-Key`. |
| `CircuitBreaker:Enabled` | `true` | `false` really turns it off. |
| `CircuitBreaker:FailureRatio` / `MinimumThroughput` | 0.1 / 100 | It opens when this share of at least this many calls failed… |
| `CircuitBreaker:SamplingDuration` / `BreakDuration` | 30 s / 5 s | …within this window (at least twice `AttemptTimeout`), and stays open this long. |
| `Hedging:MaxHedgedAttempts` / `Delay` | 1 / 2 s | For a client registered with `UseHedging()`. |
| `PropagateIdempotencyKey` | `false` | Every POST and PATCH gets an `Idempotency-Key`. |
| `Authentication`, `Tls` | none | See [`SharedKernel.Communication`](../SharedKernel.Communication/README.md). |

The defaults are Microsoft.Extensions.Http.Resilience's standard pipeline (retries on a lost connection, a timeout,
408, 429 and 5xx).

## Retries never repeat a side effect

GET, HEAD, OPTIONS, PUT and DELETE are idempotent by definition (RFC 9110) and are retried. **POST and PATCH are
not** — a retry after a lost response would place the order twice — unless the request carries an `Idempotency-Key`:

- With `PropagateIdempotencyKey`, every POST and PATCH gets a new key per call, set before the first attempt and
  repeated by every retry, so the called service recognises the repeat. The header is `Idempotency-Key`, the one
  `14.Presentation`'s `[RequireIdempotencyKey]`/`IdempotencyKey` parameter reads. Turning it on lets those methods be retried.
- A key the request already has is kept. Set one yourself when the call must be recognised across your own re-runs
  (a redelivered message, a restarted job), derived from what identifies the operation.
- Hedging follows the same rule: a POST or PATCH without a key is sent once, never in parallel.

## Results, not exceptions

`HttpClientResultExtensions` — `GetResultAsync`, `PostResultAsync`, `PutResultAsync`, `DeleteResultAsync`,
`SendResultAsync` — each with a source-generated overload (`JsonTypeInfo<T>`, trimming- and AOT-safe) and a
reflection one (`JsonSerializerOptions.Web`: camelCase, case-insensitive):

| Outcome | Result |
| --- | --- |
| 2xx | Success; the body read as `T` |
| 2xx without a body / not valid JSON for `T` | `communication.empty_body` / `communication.invalid_body` |
| A platform ProblemDetails | The service's `Error`: `errorCode` → `Code`, `detail` → `Message`, the status → `ErrorType` |
| 400 or 422 with `errors` | `Error.Validation` of the field errors (the `errorCodes` map gives each its code) |
| A body without a code (a gateway's HTML 503) | `http.{status}` with the status's `ErrorType`: a 503 or 429 is `Unavailable`, a 504 `Timeout` |
| No response | `communication.unreachable`, `communication.timeout`, `communication.circuit_open`, `communication.access_token_unavailable` |
| The caller's own cancellation | Thrown (`OperationCanceledException`) |

Status → `ErrorType` is the reverse of `14.Presentation`'s map: 400 Validation, 401 Unauthorized, 403 Forbidden,
404 NotFound, 409 and 412 Conflict, 413/415/428 Validation, 422 BusinessRule, 429 and 503 Unavailable, 504 Timeout,
anything else Unexpected. The code is never `title` (a reason phrase) or `type` (a URI).

`HttpResponseMessageResultExtensions` — `ToResultAsync()` and `ReadResultAsync<T>()` — read a response you sent yourself.

## What every call carries

Outermost first: the caller's correlation id, tenant, actor and client (`WellKnownHeaders`, from
`IRequestContextAccessor` — the same on every retry; a request's own header wins); the `Idempotency-Key`; your
handlers; then, per attempt, the resilience pipeline, the credential, and service discovery choosing the endpoint.

## Testing

`SharedKernel.Communication.Testing`:

```csharp
var stub = new StubHttpMessageHandler()
    .RespondJson(HttpMethod.Get, "/stock/sku-1", new StockLevel("sku-1", 3))
    .RespondProblem(HttpMethod.Post, "/reservations", HttpStatusCode.Conflict, "inventory.insufficient_stock");
services.UseStubHttpMessageHandler("inventory", stub);   // after AddRestClient
```

The client's whole pipeline runs; `stub.Requests` records what it sent.

## Logging

EventIds 11200–11299 are reserved for this package; it logs nothing of its own. Outbound HTTP spans and resilience
metrics come from `13.ServiceDefaults`' `WithCommunicationTelemetry()`.
