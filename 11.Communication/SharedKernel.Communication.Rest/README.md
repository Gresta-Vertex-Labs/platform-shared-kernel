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

Both paths map a non-2xx response to a `SharedKernel.Primitives.Error` via `ProblemDetailsDeserializer`
(`type` → `Error.Code`, `detail` ?? `title` → `Error.Message`). A 2xx response with an empty body, or
one that deserializes to `null`, fails with the `http.empty-body` code.

This is the client half of the platform's error round trip: a handler returns `Result`/`Result<T>`,
the HTTP boundary maps a failure to RFC 9457 ProblemDetails through `ResultHttpExtensions`
(`SharedKernel.Presentation.WebApi`), and `ReadResultAsync` maps it back to a `Result<T>` here. Never re-add a generic
`EnsureSuccessOrErrorAsync<T>` overload that promises a payload but does not deliver one — that shape
was retired for exactly that defect (P-361).

## Recipe: opt-in idempotency-key propagation

`StandardResilienceHandler`'s default `RetryCount = 3` means every typed client already silently
re-issues non-idempotent verbs (POST/PATCH/DELETE) on transient failure. `EnableIdempotencyKeyPropagation`
converts that existing hazard into an explicit, downstream-consumable guarantee — a stable
`x-idempotency-key` header attached once, before the first attempt, and preserved unchanged across
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

Disabled by default. A caller-supplied `x-idempotency-key` value is never overwritten.

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
