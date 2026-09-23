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

Both paths map a non-2xx response to a `SharedKernel.Primitives.Error` via `ProblemDetailsDeserializer`,
mirroring the real wire shape `SharedKernel.Presentation.WebApi` produces: `errorCode` (falling back to
`title`) → `Error.Code` — never `type`, which is an RFC 9457 status URI such as
`"https://httpstatuses.io/404"`, not a machine code — and `detail` → `Error.Message`. The response's
HTTP status maps back to an `ErrorType` (400, 413, 415, 428 → Validation, 401 → Unauthorized,
403 → Forbidden, 404 → NotFound, 409, 412 → Conflict, 422 → BusinessRule, 429, 503 → Unavailable,
504 → Timeout, everything else → Unexpected) via `HttpStatusErrorTypeMap`, the reverse of
`SharedKernel.Presentation.WebApi`'s `ErrorTypeStatusCodeMap.Resolve` plus the statuses an HTTP
boundary answers outside it (a failed `If-Match`, a payload or media-type rejection, rate limiting) —
duplicated here rather than shared, since `11.Communication` may never reference `14.Presentation`.
A downstream outage answered with a ProblemDetails body therefore comes back as `Unavailable` or
`Timeout` rather than as an `Unexpected` fault. When the body carries the `errors` extension (a multi-field
validation failure: keyed by field path, or by code for an error that names no field, each value an
array of messages), every entry is rebuilt as its own `Error` and returned as one aggregate via
`Error.Validation(IReadOnlyList<Error>)` — the same shape `ValidationException`/`Error.Details`
produce on the server, round-tripping without losing any field. The parallel `errorCodes` extension
supplies each entry's real code, index by index; when it names a code different from the key, the key
is kept as the field path in `MessageArguments[ErrorArgumentNames.PropertyPath]`. A body from an
older server without `errorCodes` is read as before, each key taken as the code. A non-JSON body, an
empty body, or a body with none of these recognizable members still yields
an `Error.Unexpected` carrying the response status in its code/message (`"http.{status}"`) — never an
unclassified, status-blind fallback. A 2xx response with an empty body, or one that deserializes to
`null`, fails with the `http.empty-body` code.

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
