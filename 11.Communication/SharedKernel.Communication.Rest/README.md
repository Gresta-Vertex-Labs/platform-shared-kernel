# SharedKernel.Communication.Rest

Typed `HttpClient` factory for Platform.SharedKernel microservices with Polly v8 resilience
(`StandardResilienceHandler` — retry, circuit breaker, timeout), `CorrelationIdDelegatingHandler`,
`TenantIdDelegatingHandler`, an opt-in `IdempotencyKeyDelegatingHandler`, ProblemDetails
deserialization to `Error`, and the fluent `IRestCommunicationBuilder` DI entry point. Every typed
client registered through this package carries the platform's resilience and propagation defaults —
no raw `HttpClient` injection, ever.

## Install

```bash
dotnet add package SharedKernel.Communication.Rest
```

```xml
<PackageReference Include="SharedKernel.Communication.Rest" Version="1.0.0" />
```

## Usage

```csharp
services
    .AddSharedKernelRestCommunication()
    .AddRestClient<OrderServiceClient>("order-service", options =>
    {
        options.BaseAddress = "http://order-service";
        options.TimeoutSeconds = 15;
        options.Resilience.RetryCount = 3;
        options.Resilience.CircuitBreakerEnabled = true;
    });

// Inject OrderServiceClient — a typed HttpClient with StandardResilienceHandler +
// CorrelationIdDelegatingHandler + TenantIdDelegatingHandler already wired, in that pipeline order.
public sealed class OrderServiceClient(HttpClient httpClient)
{
    public async Task<Result<OrderDto>> GetOrderAsync(Guid orderId, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync($"/api/orders/{orderId}", ct);
        return await response.ReadResultAsync(OrderJsonContext.Default.OrderDto, ct);
    }
}
```

`AddRestClient<TClient>` throws `OptionsValidationException` synchronously — at the call site, not
deferred to the first HTTP request — when `RestClientOptions`/`RestResilienceOptions` hold an invalid
value (`TimeoutSeconds <= 0`, `RetryCount <= 0`, a negative `TotalTimeoutBufferSec`, etc.).

`BaseAddress` may be omitted when an `IServiceEndpointResolver` is registered (see
[`SharedKernel.Communication.Internal`](https://www.nuget.org/packages/SharedKernel.Communication.Internal)) —
resolution then happens per request, at the point a call is issued, never hardcoded inside a typed
client method.

## Recipe: reading a status-only outcome vs. a deserialized payload

`HttpResponseMessageExtensions` offers two distinct extension methods — pick the one that matches
what the call site actually needs:

```csharp
// Status-check only — never deserializes the response body, even on 2xx.
Result result = await httpClient
    .PostAsync("/api/orders", content, ct)
    .Result.EnsureSuccessOrErrorAsync(ct);

// Deserialized payload via a source-generated JsonTypeInfo<T>.
Result<OrderDto> order = await httpClient
    .GetAsync($"/api/orders/{orderId}", ct)
    .Result.ReadResultAsync(OrderJsonContext.Default.OrderDto, ct);

// Reflection-based overload, when no JsonTypeInfo<T> is available.
Result<OrderDto> order2 = await httpClient
    .GetAsync($"/api/orders/{orderId}", ct)
    .Result.ReadResultAsync<OrderDto>(options: null, ct);
```

Both paths map a non-2xx response to a `SharedKernel.Primitives.Error` through `ProblemDetailsDeserializer`,
which reads the wire shape `SharedKernel.Presentation.WebApi` writes:

- **Code:** `errorCode`. A problem without one gets `"http.{status}"`, the code the server itself gives a
  response the framework produced. The code is never `title`: since P-562 that is the status reason phrase
  (`"Not Found"`), and from a service outside the platform it is free text, which your service would otherwise
  adopt as its own error code and pass on to its own callers. Nor is it `type`, a URI such as
  `"https://tools.ietf.org/html/rfc9110#section-15.5.5"`.
- **Message:** `detail`, else `"HTTP {status} error"`.
- **`ErrorType`:** from the response status, via `HttpStatusErrorTypeMap`: 400, 413, 415, 428 → Validation,
  401 → Unauthorized, 403 → Forbidden, 404 → NotFound, 409, 412 → Conflict, 422 → BusinessRule,
  429, 503 → Unavailable, 504 → Timeout, everything else → Unexpected. The map is the reverse of
  `SharedKernel.Presentation.WebApi`'s `ErrorTypeStatusCodeMap.Resolve`, plus the statuses an HTTP boundary
  answers outside it (a failed `If-Match`, a payload or media-type rejection, rate limiting). It is duplicated
  here rather than shared, since `11.Communication` may never reference `14.Presentation`. A downstream outage
  therefore comes back as `Unavailable` or `Timeout` rather than as an `Unexpected` fault, with a ProblemDetails
  body or without one.
- **Field errors:** read only from a 400 or a 422. 400 is the platform's validation status, and 422 is the one
  many other frameworks use for the same failure. There the `errors` extension (keyed by field path, or by code
  for an error that names no field, each value an array of messages) is rebuilt entry by entry and returned as
  one aggregate via `Error.Validation(IReadOnlyList<Error>)`. That is the shape `ValidationException` and
  `Error.Details` produce on the server, so no field is lost. A 422 with field errors is therefore `Validation`,
  and a 422 without them `BusinessRule`. The parallel `errorCodes` extension supplies each entry's real code,
  index by index; when it names a code different from the key, the key is kept as the field path in
  `MessageArguments[ErrorArgumentNames.PropertyPath]`. Without `errorCodes`, each key is taken as the code.
- **Field errors on any other status** are ignored, and the error keeps its status's category. `Error.Details`
  exists only on the validation aggregate, and an `errors` map on a 401, a 409 or a 503 must not turn an
  authentication failure, a conflict or a retryable outage into a validation failure.

A response without a usable body still takes its `ErrorType` from the status through the same
`HttpStatusErrorTypeMap`, with the code `"http.{status}"` and the status line as the message. That covers a
non-JSON body, an empty body, and a body with none of the members above, such as
`{"title":"Not Found","status":404}`: typically a gateway, load balancer or proxy answering with its own HTML
page for a service that is down or slow. A bodiless 429 or 503 is `Unavailable`, a 504 `Timeout`, a 500 or 502
`Unexpected` and a 404 `NotFound`, never an unclassified, status-blind fallback. A 2xx response with an empty
body, or one that deserializes to `null`, fails with the `http.empty-body` code.

This is the client half of the platform's error round trip: a handler returns `Result`/`Result<T>`,
the HTTP boundary maps a failure to RFC 9457 ProblemDetails through `ResultHttpExtensions`
(`SharedKernel.Presentation.WebApi`), and `ReadResultAsync` maps it back to a `Result<T>` here. Never re-add a generic
`EnsureSuccessOrErrorAsync<T>` overload that promises a payload but does not deliver one — that shape
was retired for exactly that defect (P-361).

## Recipe: opt-in idempotency-key propagation

`StandardResilienceHandler`'s default `RetryCount = 3` means every typed client already silently
re-issues non-idempotent verbs (POST/PATCH/DELETE) on transient failure. `EnableIdempotencyKeyPropagation`
converts that existing hazard into an explicit, downstream-consumable guarantee — a stable
`Idempotency-Key` header (`WellKnownHeaders.IdempotencyKey`, the name `SharedKernel.Presentation.WebApi`'s
`[RequireIdempotencyKey]` reads) attached once, before the first attempt, and preserved unchanged across
every Polly-driven retry of the same logical call:

```csharp
services
    .AddSharedKernelRestCommunication()
    .AddRestClient<IPaymentServiceClient>("payment-service", options =>
    {
        options.BaseAddress = "http://payment-service";
        options.EnableIdempotencyKeyPropagation = true;
    });
```

Disabled by default. A caller-supplied `Idempotency-Key` value is never overwritten.

## Layering

```text
SharedKernel.Communication.Rest  →  SharedKernel.Primitives (01.Core),
                                     SharedKernel.Security.Abstractions (12.Security),
                                     Microsoft.Extensions.Http, Microsoft.Extensions.Http.Resilience
```

Target framework: `net10.0`. Never references `02.Caching`, `05.Application`, `06.Persistence`, or
`07.Messaging` — outbound REST communication is the only concern this package owns.

For full documentation see
[`11.Communication/CLAUDE.md`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/11.Communication/CLAUDE.md).
