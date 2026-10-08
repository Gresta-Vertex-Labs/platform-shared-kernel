<div align="center">

# CheckoutApi

**Calling another service: typed REST and gRPC clients configured from `appsettings.json`, whose failures come back as `Result`, not exceptions.**

<sub>📂 <code>samples/CheckoutApi</code> · <a href="../README.md">all samples</a> · calls <a href="../InventoryApi/">InventoryApi</a> · needs no infrastructure</sub>

</div>

## What it shows

- **Two clients in one chain.** `AddRestClient<IInventoryClient, InventoryClient>("inventory")` and
  `AddGrpcClient<Inventory.InventoryClient>("inventory-grpc")`; addresses, deadlines and authentication in configuration.
- **Service discovery.** Addresses name a service (`http://inventory`, `http://_grpc.inventory`), resolved through the
  `Services` section locally and through DNS in Kubernetes.
- **Safe retries.** An `Idempotency-Key` on every POST, so the reservation can be retried; a replayed key reserves once.
- **The other service's errors as your own.** InventoryApi's 404, 409 and field errors reach CheckoutApi's caller
  unchanged; InventoryApi down is a 503 `communication.unreachable`.
- **The caller carried across**, and `google.type.Money` mapped to the kernel's `Money`.

**Packages it uses:**
[SharedKernel.Communication.Rest](../../src/Infrastructure/Communication/SharedKernel.Communication.Rest/README.md) ·
[SharedKernel.Communication.Grpc](../../src/Infrastructure/Communication/SharedKernel.Communication.Grpc/README.md) ·
[SharedKernel.Application.Pipeline](../../src/Application/SharedKernel.Application.Pipeline/README.md) ·
[SharedKernel.Application.Mediator.MediatR](../../src/Application/SharedKernel.Application.Mediator.MediatR/README.md) ·
[SharedKernel.ServiceDefaults](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults/README.md) ·
[SharedKernel.ServiceDefaults.Security](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security/README.md) ·
[SharedKernel.Presentation.WebApi](../../src/Hosting/Presentation/SharedKernel.Presentation.WebApi/README.md)

Capabilities: [Communication](../../src/Infrastructure/Communication/README.md) · [Application](../../src/Application/README.md) ·
[Service defaults](../../src/Hosting/ServiceDefaults/README.md) · [Presentation](../../src/Hosting/Presentation/README.md)

## The registration

CheckoutApi calls [InventoryApi](../InventoryApi/) over gRPC for the price and over REST for the reservation:

```csharp
builder.Services.AddSharedKernelCommunication(builder.Configuration)
    .AddRestClient<IInventoryClient, InventoryClient>("inventory")
    .AddGrpcClient<Inventory.InventoryClient>("inventory-grpc");
```

Everything else is configuration (`appsettings.json`):

```json
"SharedKernel": {
  "Communication": {
    "Clients": {
      "inventory": {
        "BaseAddress": "http://inventory",
        "AttemptTimeout": "00:00:05",
        "PropagateIdempotencyKey": true,
        "Authentication": { "Mode": "ApiKey" }
      },
      "inventory-grpc": {
        "Address": "http://_grpc.inventory",
        "Deadline": "00:00:05",
        "Authentication": { "Mode": "ApiKey" }
      }
    }
  }
},
"Services": {
  "inventory": { "http": [ "http://localhost:5080" ], "grpc": [ "http://localhost:5081" ] }
}
```

- The addresses name a service, not a machine. `http://inventory` and `http://_grpc.inventory` (the endpoint named
  `grpc`) resolve through the `Services` section here; in Kubernetes, drop the section and they resolve through DNS.
- The API key (`Authentication:ApiKey:Value`) is not in `appsettings.json`: Development reads it from
  `appsettings.Development.json`, anything else from a secret store. Without it the host does not start.
- `PropagateIdempotencyKey` puts an `Idempotency-Key` on every POST, which is what lets the reservation be retried.

## The code

The typed client is one line per call (`Clients/InventoryClient.cs`):

```csharp
public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public Task<Result<InventoryReservation>> ReserveAsync(string sku, int quantity, CancellationToken ct) =>
        http.PostResultAsync<ReserveRequest, InventoryReservation>("reservations", new ReserveRequest(sku, quantity), cancellationToken: ct);
}
```

The handler uses both protocols and returns their failures as its own (`Features/Checkout/PlaceOrder.cs`):

```csharp
Result<StockReply> reply = await stock.GetStockAsync(new GetStockRequest { Sku = sku }, cancellationToken: ct).ToResultAsync(ct);
ValidationResult<Money> price = reply.Value.UnitPrice.ToMoney();   // google.type.Money → Money
Result<InventoryReservation> reservation = await inventory.ReserveAsync(sku, quantity, ct);
```

The gRPC client is generated from InventoryApi's own `Protos/inventory.proto` (`GrpcServices="Client"`,
`Access="Internal"`).

## Endpoints

| Endpoint | |
| --- | --- |
| `POST /checkout` `{ "sku", "quantity" }` | 200 `{ reservationId, sku, quantity, total, currency }`; 404 `inventory.sku_not_found` (from InventoryApi's gRPC status), 409 `inventory.insufficient_stock` and 400 with the `quantity` field error (from its ProblemDetails); 503 `communication.unreachable` when InventoryApi is down |
| `GET /quotes/{sku}` | The price over gRPC |
| `GET /health/live`, `/health/ready` | |

## Running it

Pack the kernel first (see [the samples guide](../README.md#building-and-running-them)), then start both services:

```bash
dotnet run --project samples/InventoryApi -p:SharedKernelPackageVersion=$V --environment Development
dotnet run --project samples/CheckoutApi -p:SharedKernelPackageVersion=$V --environment Development
curl -X POST http://localhost:5090/checkout -H "Content-Type: application/json" -d '{"sku":"sku-1","quantity":2}'
```

## Tests

`CheckoutApi.Tests` starts InventoryApi on two free loopback ports (REST over HTTP/1.1, gRPC over HTTP/2) and
CheckoutApi in memory, pointed at them through its `Services` section. No Docker. They prove: a checkout prices over
gRPC and reserves over REST; the caller's correlation id and an idempotency key reach InventoryApi; a replayed key
reserves once; InventoryApi's 404, 409 and field errors come back unchanged; a wrong API key is refused; InventoryApi
down is a 503.
