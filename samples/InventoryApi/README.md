# InventoryApi

The service [CheckoutApi](../CheckoutApi//) calls: stock, prices and reservations over REST and over gRPC, behind an
API key. It is the other half of the `11.Communication` sample — the side whose answers a client reads back.

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
