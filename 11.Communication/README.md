# 11.Communication

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](../LICENSE)
![Packages: 3](https://img.shields.io/badge/packages-3-informational)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-informational)

> **Outbound service-to-service calls for .NET services — typed REST and gRPC clients configured from
> `appsettings.json`, resilient by default, that carry the caller to the next service and return `Result` instead of
> throwing.**

A service calling another service needs the same things every time: an address that works on a laptop and in a
Kubernetes cluster; retries, timeouts and a circuit breaker that agree with each other and never repeat a side effect;
credentials; a correlation id so both sides' logs join up; the tenant and actor so the next service can authorise; and
a failure that arrives as the other service's own error. These packages do all of that once, at the composition root,
so a typed client method is one line.

```csharp
builder.Services.AddSharedKernelCommunication(builder.Configuration)
    .AddRestClient<IInventoryClient, InventoryClient>("inventory")
    .AddGrpcClient<Pricing.PricingClient>("pricing");
```

```json
"SharedKernel": {
  "Communication": {
    "Clients": {
      "inventory": { "BaseAddress": "http://inventory", "PropagateIdempotencyKey": true },
      "pricing": { "Address": "http://_grpc.pricing", "Deadline": "00:00:05" }
    }
  }
}
```

```csharp
public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public Task<Result<StockLevel>> GetStockAsync(string sku, CancellationToken ct) =>
        http.GetResultAsync($"stock/{Uri.EscapeDataString(sku)}", InventoryJson.Default.StockLevel, ct);
}

Result<Quote> quote = await pricing.GetQuoteAsync(request, cancellationToken: ct).ToResultAsync(ct);
```

## Packages

| Package | Use it for | Entry point |
| --- | --- | --- |
| [`SharedKernel.Communication`](SharedKernel.Communication/README.md) | The shared base: settings per client, service discovery (`Microsoft.Extensions.ServiceDiscovery`), outbound authentication (client credentials, API key, your own token provider), mutual TLS, the `communication.*` error codes | `AddSharedKernelCommunication(configuration)` |
| [`SharedKernel.Communication.Rest`](SharedKernel.Communication.Rest/README.md) | Typed `HttpClient`s: Microsoft.Extensions.Http.Resilience (retry, timeouts, circuit breaker or hedging), retries that never repeat a POST without an `Idempotency-Key`, ProblemDetails → `Result<T>` | `.AddRestClient<TClient, TImplementation>(name)` |
| [`SharedKernel.Communication.Grpc`](SharedKernel.Communication.Grpc/README.md) | Typed gRPC clients: deadline, gRPC retry policy, keepalive, round-robin across pods, rich status → `Result<T>`, `google.type.Money` | `.AddGrpcClient<TClient>(name)` |

A service references `.Rest` and/or `.Grpc`; the base comes with them. All three are **Adapter** tier — Foundation
packages, the declared `.Rest`/`.Grpc` → `SharedKernel.Communication` edge, and `.Grpc` → `SharedKernel.Domain`
(Model) for `Money` — and nothing from ASP.NET Core.

Related packages outside this folder:

| Package | Adds |
| --- | --- |
| [`SharedKernel.ServiceDefaults.Security`](../13.ServiceDefaults/SharedKernel.ServiceDefaults.Security/README.md) | `app.UseSharedKernelRequestContext()` — opens the inbound request's caller scope that these clients forward |
| [`SharedKernel.ServiceDefaults`](../13.ServiceDefaults/SharedKernel.ServiceDefaults/README.md) | `WithCommunicationTelemetry()` — outbound gRPC spans and resilience metrics |
| [`SharedKernel.Presentation.WebApi`](../14.Presentation/SharedKernel.Presentation.WebApi/README.md) / [`.Grpc`](../14.Presentation/SharedKernel.Presentation.Grpc/README.md) | The other side: the ProblemDetails and rich statuses these clients read back, and `[RequireIdempotencyKey]` |
| [`SharedKernel.Communication.Testing`](../16.Testing/SharedKernel.Communication.Testing/README.md) | `StubHttpMessageHandler`, `GrpcCalls`, `TestServerCallContext` — test projects only |

A worked example of two services talking over both protocols is [`samples/CheckoutApi`](../samples/CheckoutApi/) →
[`samples/InventoryApi`](../samples/InventoryApi/).

## One call, end to end

```text
application code ── inventory.ReserveAsync(...)
        │
        ▼  once per call
caller headers        X-Correlation-Id · X-Tenant-Id · x-sk-actor-id · x-sk-actor-kind · x-sk-client-id
Idempotency-Key       POST/PATCH, when PropagateIdempotencyKey — the same on every retry
your handlers
        │
        ▼  per attempt
resilience            attempt and total timeouts · retries (idempotent methods only) or hedging · circuit breaker
credential            Bearer token (client credentials / your provider) or API key
service discovery     http://inventory → an endpoint, round-robin
        │
        ▼
next service ── UseSharedKernelRequestContext() rebuilds the caller from the same headers
        │
        ▼
Result<T>             the body, the service's own Error, or communication.unreachable / timeout / circuit_open
```

The caller comes from the ambient `IRequestContextAccessor`, not `HttpContext`, so a call made from a message consumer,
a Temporal activity or a scheduled job carries its caller exactly like one made from an HTTP request. The correlation
id is always the caller's, never `Activity.Id`. gRPC does the same with metadata and an interceptor.

## What you can rely on

- **Settings are validated at startup.** A client without an address, a total timeout shorter than an attempt, a
  circuit-breaker window too short for its timeout, a missing certificate file: the host does not start, and says why.
- **Retries never repeat a side effect.** POST and PATCH are retried (or hedged) only with an `Idempotency-Key`.
- **Every switch is real.** `MaxRetryAttempts = 0` means one attempt; `CircuitBreaker:Enabled = false` means no breaker.
- **Caller-supplied values win.** A header, metadata entry, `Authorization` or deadline you set is never overwritten.
- **Propagation never fails a call.** Header and metadata writing is best-effort.
- **Errors round-trip.** ProblemDetails and rich statuses become the server's `Error` — code, `ErrorType`, field errors.
- **No exceptions for a failed call.** Unreachable, timed out, circuit open, no token: `Error` values. Only the
  caller's own cancellation throws.
- **Structured logs.** Event ids 11000–11999 (base 11000–11099, `.Grpc` 11100–11199, `.Rest` 11200–11299); tokens and
  secrets are never logged.

Maintainer rules live in [`CLAUDE.md`](CLAUDE.md).
