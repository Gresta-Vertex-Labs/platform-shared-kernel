<div align="center">

# InventoryApi

**The service CheckoutApi calls: stock, prices and reservations over REST and gRPC on two ports, behind an API key.**

<sub>📂 <code>samples/InventoryApi</code> · <a href="../README.md">all samples</a> · called by <a href="../CheckoutApi/">CheckoutApi</a> · needs no infrastructure</sub>

</div>

## What it shows

- **One service, two protocols.** REST over HTTP/1.1 on 5080 and gRPC over HTTP/2 on 5081 — in Kubernetes, the
  service's two named ports `http` and `grpc`.
- **Errors in each protocol's own shape.** RFC 9457 problems on REST (`AddSharedKernelWebApi`), gRPC rich statuses with
  `ErrorInfo.reason` on gRPC (`AddSharedKernelGrpc`).
- **API-key authentication**, compared in fixed time, with the health endpoints left anonymous.
- **Idempotent writes.** An `Idempotency-Key` read with an `IdempotencyKey?` parameter returns the first reservation on
  a repeat.
- **The caller's context arriving**: the reservation records the correlation id of the checkout that made it.

**Packages it uses:**
[SharedKernel.Presentation.WebApi](../../src/Hosting/Presentation/SharedKernel.Presentation.WebApi/README.md) ·
[SharedKernel.Presentation.Grpc](../../src/Hosting/Presentation/SharedKernel.Presentation.Grpc/README.md) ·
[SharedKernel.Security.ApiKey](../../src/Hosting/Security/SharedKernel.Security.ApiKey/README.md) ·
[SharedKernel.Communication.Grpc](../../src/Infrastructure/Communication/SharedKernel.Communication.Grpc/README.md) ·
[SharedKernel.Application.Pipeline](../../src/Application/SharedKernel.Application.Pipeline/README.md) ·
[SharedKernel.Application.Mediator.MediatR](../../src/Application/SharedKernel.Application.Mediator.MediatR/README.md) ·
[SharedKernel.ServiceDefaults](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults/README.md) ·
[SharedKernel.ServiceDefaults.Security](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security/README.md)

Capabilities: [Presentation](../../src/Hosting/Presentation/README.md) · [Security](../../src/Hosting/Security/README.md) ·
[Communication](../../src/Infrastructure/Communication/README.md) · [Application](../../src/Application/README.md) ·
[Service defaults](../../src/Hosting/ServiceDefaults/README.md)

## The ports

It is the other half of the [CheckoutApi](../CheckoutApi/) sample — the side whose answers a client reads back.

| Port | Protocol | Serves |
| --- | --- | --- |
| 5080 | HTTP/1.1 | REST: the endpoints below, errors as RFC 9457 problems (`AddSharedKernelWebApi`) |
| 5081 | HTTP/2 | gRPC: `inventory.v1.Inventory` (`Protos/inventory.proto`), errors as rich statuses (`AddSharedKernelGrpc`) |

Two ports because gRPC over plain HTTP needs an HTTP/2-only endpoint, which a REST client on HTTP/1.1 cannot use. In
Kubernetes they are the service's two named ports, `http` and `grpc` — which is what `http://_grpc.inventory` in
CheckoutApi's configuration picks.

## Endpoints

| Endpoint | |
| --- | --- |
| `GET /stock/{sku}` | `{ sku, available, unitPrice, currency }`; 404 `inventory.sku_not_found` |
| `POST /reservations` `{ "sku", "quantity" }` | 201 with a `Location`; 400 for a non-positive quantity (field error `quantity`); 409 `inventory.insufficient_stock`. An `Idempotency-Key` header (optional, read with an `IdempotencyKey?` parameter) makes a repeat return the first reservation |
| `GET /reservations/{id}` | The reservation, with the caller's correlation id and the idempotency key it arrived with |
| gRPC `GetStock` | The stock and the unit price as `google.type.Money` (`MoneyProtoExtensions.ToMoneyProto()`); `NOT_FOUND` with `ErrorInfo.reason` `inventory.sku_not_found` |
| `GET /health/live`, `/health/ready` | Anonymous |

Every other endpoint needs `X-Api-Key` (`SharedKernel.Security.ApiKey` with a validator over `Inventory:ApiKey`,
compared in fixed time). The caller's headers — correlation id, tenant, actor — become the request's
`IRequestContext` through `UseSharedKernelRequestContext()`, so a reservation records the correlation id of the
checkout that made it.

The inventory is in memory (`InventoryStore`): `sku-1` has 10 at 19.99 EUR, `sku-2` none at 5.00 EUR.

## Run it

Pack the kernel first (see [the samples guide](../README.md#building-and-running-them)), then:

```bash
dotnet run --project samples/InventoryApi -p:SharedKernelPackageVersion=$V --environment Development
curl http://localhost:5080/health/ready
```

Drive it through [CheckoutApi](../CheckoutApi/#running-it), whose tests also start it on two loopback ports.
