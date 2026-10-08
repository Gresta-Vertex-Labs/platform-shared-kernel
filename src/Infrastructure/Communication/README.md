<div align="center">

# SharedKernel Communication

**Outbound service-to-service calls: typed REST and gRPC clients configured from `appsettings.json`, resilient by
default, that carry the caller to the next service and return `Result` instead of throwing.**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](../../../LICENSE)
![Packages: 3](https://img.shields.io/badge/packages-3-informational)

<sub>📂 <code>src/Infrastructure/Communication</code> · domain <code>11.Communication</code> · <a href="../../../docs/packages.md">all packages by tier</a></sub>

</div>

A service calling another service needs the same things every time: an address that works on a laptop and in a
Kubernetes cluster; retries, timeouts and a circuit breaker that never repeat a side effect; credentials; a
correlation id so both sides' logs join up; the tenant and actor so the next service can authorise; and a failure that
arrives as the other service's own error. These packages do all of that once, at the composition root, so a typed
client method is one line.

## What this domain gives you

- **One registration, settings in configuration** — `AddSharedKernelCommunication(configuration)` plus one
  `AddRestClient`/`AddGrpcClient` per service; every client's address, timeouts, retries and credentials live under
  `SharedKernel:Communication:Clients:{name}` and are validated when the host starts.
- **Service discovery** through `Microsoft.Extensions.ServiceDiscovery` — configuration (Aspire), Kubernetes DNS or DNS
  SRV, round-robin per request.
- **Safe resilience** — Microsoft.Extensions.Http.Resilience for REST, gRPC's own retry policy for gRPC; a POST or
  PATCH is retried only with an `Idempotency-Key`.
- **Outbound credentials** — OAuth 2.0 client credentials, API key, your own `IAccessTokenProvider`, mutual TLS.
- **The caller travels** — correlation id, tenant, actor and client from `IRequestContextAccessor`, whether the call
  starts in an HTTP request, a message consumer, a workflow activity or a scheduled job.
- **Errors round-trip** — ProblemDetails and rich gRPC statuses become the called service's `Error`; a call that got
  no answer is `communication.unreachable`, `.timeout` or `.circuit_open`.

## Packages

| Package | Tier | When you need it |
| --- | --- | --- |
| [`SharedKernel.Communication`](SharedKernel.Communication/README.md) | Adapter | The shared base — service discovery, credentials, mTLS, `communication.*` codes. Comes with the two below |
| [`SharedKernel.Communication.Rest`](SharedKernel.Communication.Rest/README.md) | Adapter | You call another service over HTTP/JSON: `AddRestClient<IClient, Client>(name)`, `GetResultAsync`/`PostResultAsync` |
| [`SharedKernel.Communication.Grpc`](SharedKernel.Communication.Grpc/README.md) | Adapter | You call another service over gRPC: `AddGrpcClient<T>(name)`, `ToResultAsync()`, `google.type.Money` ↔ `Money` |

All three go into a service's **Infrastructure** project and reference nothing from ASP.NET Core. Test doubles live in
[`SharedKernel.Communication.Testing`](./SharedKernel.Communication.Testing/README.md)
(`StubHttpMessageHandler`, `GrpcCalls`, `TestServerCallContext`). Server-side conventions — the ProblemDetails and rich
statuses these clients read back — live in [`14.Presentation`](../../Hosting/Presentation/README.md).

## How a call travels

The call path in the Shop sample: Ordering reserves stock in Inventory over gRPC and charges the payment in Billing
over REST.

```mermaid
sequenceDiagram
    autonumber
    participant H as Ordering handler
    participant C as GrpcInventoryReservations / RestPayments
    participant P as Client pipeline
    participant I as Inventory / Billing
    H->>C: ReserveAsync(...) / ChargeAsync(...)
    C->>P: request
    Note over P: once per call — caller headers<br/>(X-Correlation-Id, X-Tenant-Id, x-sk-actor-*),<br/>Idempotency-Key on POST, gRPC deadline
    Note over P: per attempt — timeout, retry / breaker,<br/>API key, client certificate or bearer token, service discovery
    P->>I: gRPC ReserveStock · HTTP POST /payments
    Note over I: UseSharedKernelRequestContext()<br/>rebuilds the caller from the same headers
    I-->>P: 2xx body · ProblemDetails · rich status
    P-->>C: response
    C-->>H: Result<T> — the body, the callee's own Error,<br/>or communication.unreachable / timeout
```

## Get started

Reference the protocol package from your Infrastructure project:

```xml
<PackageReference Include="SharedKernel.Communication.Rest" />
```

Register the client and write it as one line per call:

```csharp
using SharedKernel.Communication;
using SharedKernel.Primitives.Results;

builder.Services.AddSharedKernelCommunication(builder.Configuration)
    .AddRestClient<IInventoryClient, InventoryClient>("inventory");

public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public Task<Result<InventoryReservation>> ReserveAsync(string sku, int quantity, CancellationToken ct) =>
        http.PostResultAsync<ReserveRequest, InventoryReservation>(
            "reservations", new ReserveRequest(sku, quantity), cancellationToken: ct);
}
```

```json
{
  "SharedKernel": {
    "Communication": {
      "Clients": {
        "inventory": { "BaseAddress": "http://inventory", "PropagateIdempotencyKey": true }
      }
    }
  },
  "Services": { "inventory": { "http": [ "http://localhost:5080" ] } }
}
```

For the caller to travel, the host opens a request context — `AddSharedKernelRequestContext()` and
`app.UseSharedKernelRequestContext()` from
[`SharedKernel.ServiceDefaults.Security`](../../Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security/README.md) — and
`builder.WithCommunicationTelemetry()` adds outbound spans and resilience metrics.

## The sample

In the Shop ([`samples/Shop`](../../../samples/Shop/README.md)), [Ordering](../../../samples/Shop/Ordering/) calls two
services, one per protocol:

- `inventory` (gRPC, `GrpcInventoryReservations`) reaches [Inventory](../../../samples/Shop/Inventory/) over mutual TLS
  with Ordering's client certificate; `billing` (REST, `RestPayments`) reaches [Billing](../../../samples/Shop/Billing/)
  with Ordering's API key. Both are registered in `Shop.Ordering.Infrastructure` and configured by the AppHost.
- Inventory and Billing rebuild the caller with `UseSharedKernelRequestContext()` and answer errors as rich statuses
  over gRPC and RFC 9457 problems over REST, which arrive in Ordering as the matching `Error` (a declined card as
  Billing's business-rule failure).
- `Shop.Ordering.Tests` (`GrpcInventoryReservationsTests`) covers the gRPC error mapping over the Communication fakes;
  the `Shop.E2E` flows cover the certificate allow-list, a rogue or missing certificate, the API key enforced and a
  declined card compensated.

## Guarantees

- **Settings are validated at startup.** A missing address, a total timeout shorter than an attempt, a breaker window
  too short for its timeout, a missing certificate file: the host does not start, and says why.
- **Retries never repeat a side effect.** POST and PATCH are retried or hedged only with an `Idempotency-Key`; gRPC
  retries only calls the service has not answered.
- **Every switch is real.** `MaxRetryAttempts = 0` means one attempt; `CircuitBreaker:Enabled = false` means no breaker.
- **Caller-supplied values win.** A header, metadata entry, `Authorization` or deadline you set is never overwritten.
- **No exceptions for a failed call.** Unreachable, timed out, circuit open, no token: `Error` values. Only the
  caller's own cancellation throws.
- **The correlation id is the caller's**, end to end — never `Activity.Id`.
- **Secrets stay out of logs.** Event ids 11000–11999 (base 11000–11099, Grpc 11100–11199, Rest 11200–11299); tokens
  and secrets are never logged.

---

For maintainers: rules in [`CLAUDE.md`](CLAUDE.md), phase history in [`state-map.md`](state-map.md).
