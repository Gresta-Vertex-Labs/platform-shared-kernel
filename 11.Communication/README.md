# 11.Communication

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](../LICENSE)
![Packages: 3](https://img.shields.io/badge/packages-3-informational)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-informational)

> **Outbound service-to-service calls for .NET services — typed REST and gRPC clients that are resilient by default
> and carry the caller to the next service, from any entry point.**

A service calling another service needs the same things every time: retries, a circuit breaker and timeouts that
agree with each other; a correlation id so the two sides' logs join up; the tenant and the calling actor so the
next service can authorise and filter; and an address that works inside a Kubernetes cluster. These packages wire
all of that once, at the composition root, so a typed client method is just the call.

```csharp
builder.Services.AddK8sServiceDiscovery(o => o.Namespace = "production");

builder.Services.AddSharedKernelRestCommunication()
    .AddRestClient<InventoryClient>("inventory-service", o => o.EnableIdempotencyKeyPropagation = true);

builder.Services.AddSharedKernelGrpcCommunication()
    .AddGrpcClient<Pricing.PricingClient>(configure: o => o.DeadlineSeconds = 5);
```

```csharp
public sealed class InventoryClient(HttpClient http)
{
    public async Task<Result<StockLevel>> GetStockAsync(string sku, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/stock/{sku}", ct);
        return await response.ReadResultAsync(InventoryJson.Default.StockLevel, ct);   // ProblemDetails → Error
    }
}
```

## Packages

| Package | Use it for | Entry point |
| --- | --- | --- |
| [`SharedKernel.Communication.Rest`](SharedKernel.Communication.Rest/README.md) | Typed `HttpClient`s with Polly v8 `StandardResilienceHandler`, caller propagation, opt-in `Idempotency-Key`, ProblemDetails → `Result<T>` | `AddSharedKernelRestCommunication().AddRestClient<TClient>(name, …)` |
| [`SharedKernel.Communication.Grpc`](SharedKernel.Communication.Grpc/README.md) | Typed gRPC clients with trace and caller metadata, an enforced per-call deadline, `Money`/`Timestamp` conversions | `AddSharedKernelGrpcCommunication().AddGrpcClient<TClient>(…)` |
| [`SharedKernel.Communication.Internal`](SharedKernel.Communication.Internal/README.md) | In-cluster service discovery (DNS SRV + A-record, cached, never throws) and a static map for dev/test | `AddK8sServiceDiscovery()` / `AddStaticServiceDiscovery(…)` |

All three are **Adapter** tier: they reference Foundation packages (`SharedKernel.Primitives`,
`SharedKernel.Execution`) plus the declared `.Rest`/`.Grpc` → `.Internal` edge, and nothing from ASP.NET Core.

Related packages outside this folder:

| Package | Adds |
| --- | --- |
| [`SharedKernel.ServiceDefaults.Security`](../13.ServiceDefaults/SharedKernel.ServiceDefaults.Security/README.md) | `app.UseSharedKernelRequestContext()` — opens the inbound request's caller scope that these clients forward |
| [`SharedKernel.ServiceDefaults`](../13.ServiceDefaults/SharedKernel.ServiceDefaults/README.md) | `WithCommunicationTelemetry()` — gRPC client tracing and Polly resilience metrics |
| [`SharedKernel.Presentation.GraphQL`](../14.Presentation/SharedKernel.Presentation.GraphQL/README.md) | HotChocolate server conventions (formerly `SharedKernel.Communication.GraphQL`; serving an API is inbound) |
| [`SharedKernel.Presentation.Grpc`](../14.Presentation/SharedKernel.Presentation.Grpc/README.md) | Server-side gRPC conventions |
| [`SharedKernel.Communication.Testing`](../16.Testing/SharedKernel.Communication.Testing/README.md) | `MockServiceEndpointResolver`, `TestServerCallContext` — test projects only |

## The caller travels with every call

```text
inbound HTTP / gRPC / message / workflow activity / scheduled job
        │  opens a RequestContextScope (tenant, actor, client, correlation id)
        ▼
application code ── InventoryClient.GetStockAsync(...)
        ▼
RequestContextDelegatingHandler / TenantIdInterceptor + CorrelationTracingInterceptor
        │  IRequestContextAccessor.Current → RequestContextPropagation.WriteHeaders
        │  X-Correlation-Id · X-Tenant-Id · x-sk-actor-id · x-sk-actor-kind · x-sk-client-id
        ▼
StandardResilienceHandler (retry · circuit breaker · timeout) — same headers on every retry
        ▼
next service ── UseSharedKernelRequestContext() rebuilds the caller from the same headers
```

Because the caller comes from the ambient `IRequestContextAccessor` rather than `HttpContext`, a REST or gRPC call
made from a message consumer, a Temporal activity or a scheduled job carries its caller exactly like one made from
an HTTP request. The correlation id is always the caller's, never `Activity.Id`.

## What you can rely on

- **Resilience on every REST client.** `StandardResilienceHandler` is always attached; timeouts, retry count and
  circuit-breaker settings are validated when the client is registered, not on the first call.
- **Caller-supplied values win.** A header, metadata entry or deadline you set yourself is never overwritten.
- **Propagation never fails a call.** Header and metadata injection is best-effort.
- **Idempotent retries when you ask for them.** `EnableIdempotencyKeyPropagation` sets one `Idempotency-Key` that
  survives every retry of a logical call.
- **Errors round-trip.** A ProblemDetails response becomes an `Error` with the server's code, `ErrorType` and
  per-field validation details.
- **Discovery never throws.** An unresolvable name returns the Kubernetes convention address and lets the transport
  report the failure.
- **Structured logs.** Event ids 11000–11999 (`.Grpc` 11100–11199, `.Internal` 11300–11399).

Maintainer rules live in [`CLAUDE.md`](CLAUDE.md).
