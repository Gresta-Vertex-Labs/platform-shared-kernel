---
proven-by:
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderingPorts.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Billing/RestPayments.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Inventory/GrpcInventoryReservations.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/OrderingInfrastructure.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Shop.Ordering.Infrastructure.csproj
  - samples/Shop/Ordering/Shop.Ordering.Api/Program.cs
  - samples/Shop/Ordering/Shop.Ordering.Tests/OrderingTests.cs
  - samples/Shop/Shop.Contracts/Shop.Contracts.csproj
unproven:
  - Unit-testing a REST client with StubHttpMessageHandler: the reference platform proves its REST client end to end only; the Communication.Testing README shows the stub
---
# Call another service

**Use when:** a handler needs data or an action from another service over REST or gRPC and must get the answer back
as a `Result<T>`, with the caller's tenant and correlation id carried along.
**Not when:** the other service should react to something that already happened (add-messaging.md); the call is one
step of a multi-step process that must survive crashes or compensate (add-long-running-workflow.md); you are
exposing your own endpoint (add-http-endpoint.md); you are sending a webhook to an external subscriber
(add-notifications-webhooks.md); you are publishing your own contracts and client, or consuming another service's
events (add-service-contracts.md).

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.Application` | The port the handler calls returns `Result`/`Result<T>` (nothing transport-specific) |
| Infrastructure | `SharedKernel.Communication` | `AddSharedKernelCommunication(configuration)`: discovery, outbound credentials, mTLS, caller propagation |
| Infrastructure | `SharedKernel.Communication.Rest` | `AddRestClient<IClient, Client>("name")`, `GetResultAsync`/`PostResultAsync`/`SendResultAsync` |
| Infrastructure | `SharedKernel.Communication.Grpc` | `AddGrpcClient<TClient>("name")`, `call.ToResultAsync()` |
| Api/Worker | `SharedKernel.ServiceDefaults` | `WithCommunicationTelemetry()` for the outbound HTTP/gRPC instrumentation |
| Tests | `SharedKernel.Communication.Testing` | `GrpcCalls` fake unary calls, `StubHttpMessageHandler` |

Take `.Rest`, `.Grpc` or both. The gRPC client classes come from the other service's `.proto` files (`Grpc.Tools`,
`<Protobuf … GrpcServices="Client" />`) in a contracts project, never from the other service's own projects.

## Steps

1. **Application: a port that speaks `Result`.** The handler depends on an interface; the Infrastructure class
   behind it owns the transport.

   ```csharp
   using SharedKernel.Primitives.Results;

   public interface IPayments
   {
       Task<Result<Guid>> ChargeAsync(PaymentRequest request, CancellationToken ct);
       Task<Result> RefundAsync(OrderId orderId, CancellationToken ct);
   }
   ```

2. **Infrastructure: a typed REST client.** The implementation takes the `HttpClient` the kernel configured for its
   name. The `…ResultAsync` extensions return the other service's own `Error` from its problem response, or a coded
   failure when it did not answer; they never throw for an HTTP failure. Use the overloads that take a
   source-generated `JsonTypeInfo`.

   ```csharp
   using SharedKernel.Communication;
   using SharedKernel.Primitives.Results;

   public sealed class RestPayments(HttpClient http) : IPayments
   {
       public const string ClientName = "billing";

       public async Task<Result<Guid>> ChargeAsync(PaymentRequest request, CancellationToken ct)
       {
           var charged = await http.PostResultAsync(
               "payments",
               new ChargeRequest(request.OrderId.Value, request.Amount, request.Currency,
                   request.CustomerEmail, request.PaymentToken),
               BillingJsonContext.Default.ChargeRequest,
               BillingJsonContext.Default.PaymentView,
               ct);
           return charged.IsFailure
               ? Result<Guid>.Failure(charged.Error)
               : Result<Guid>.Success(charged.Value.PaymentId);
       }

       public async Task<Result> RefundAsync(OrderId orderId, CancellationToken ct)
       {
           using var request = new HttpRequestMessage(
               HttpMethod.Post, $"payments/by-order/{orderId.Value:D}/refund");
           return await http.SendResultAsync(request, ct);
       }
   }
   ```

   `GetResultAsync<T>(uri, typeInfo, ct)`, `PutResultAsync` and `DeleteResultAsync` follow the same shape.

3. **Infrastructure: a gRPC client.** Inject the generated client; `ToResultAsync` maps a non-OK status back to the
   `Error` the other service returned, so its business-rule failure arrives here as a business-rule failure.

   ```csharp
   using SharedKernel.Communication;
   using SharedKernel.Primitives.Results;

   public sealed class GrpcInventoryReservations(InventoryService.InventoryServiceClient client)
       : IInventoryReservations
   {
       public const string ClientName = "inventory";

       public async Task<Result<int>> ReleaseAsync(OrderId orderId, CancellationToken ct)
       {
           var reply = await client
               .ReleaseStockAsync(
                   new ReleaseStockRequest { OrderId = orderId.Value.ToString("D") },
                   cancellationToken: ct)
               .ToResultAsync(ct);
           return reply.IsFailure
               ? Result<int>.Failure(reply.Error)
               : Result<int>.Success(reply.Value.ReleasedLines);
       }
   }
   ```

4. **Infrastructure: register both clients on one builder.** The name is the configuration key of the client.

   ```csharp
   using SharedKernel.Communication;

   services
       .AddSharedKernelCommunication(configuration)
       .AddGrpcClient<InventoryService.InventoryServiceClient>(GrpcInventoryReservations.ClientName)
       .AddRestClient<IPayments, RestPayments>(RestPayments.ClientName);
   services.AddScoped<IInventoryReservations, GrpcInventoryReservations>();
   ```

   The caller's tenant and correlation id go on every request and call automatically; do not add those headers
   yourself. Credentials (API key, client credentials, mutual TLS) are chosen per client in configuration.

5. **Api/Worker: telemetry.** After `builder.AddServiceDefaults()`, call `builder.WithCommunicationTelemetry()`
   (namespace `SharedKernel.ServiceDefaults.Telemetry`).

6. **Configuration:** `SharedKernel:Communication`, one entry per client under `SharedKernel:Communication:Clients`
   named like the client (`billing`, `inventory`). Addresses, timeouts, retries and authentication keys are in the
   `SharedKernel.Communication` README.

## Test it

- gRPC: substitute the generated client and return `GrpcCalls.Success(reply)` or
  `GrpcCalls.Failure<TReply>(error)` (namespace `SharedKernel.Testing.Communication`); assert the port returns the
  same `Error.Type` and `Error.Code`.

  ```csharp
  using SharedKernel.Primitives.Errors;
  using SharedKernel.Testing.Communication;

  client
      .ReserveStockAsync(Arg.Any<ReserveStockRequest>(), Arg.Any<Metadata>(),
          Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
      .Returns(GrpcCalls.Failure<ReserveStockReply>(
          Error.BusinessRule("inventory.insufficient_stock", "Not enough.")));
  ```

- REST: route the named client to a `StubHttpMessageHandler` with `services.UseStubHttpMessageHandler(name, stub)`;
  the reference platform does not do this, so follow the `SharedKernel.Communication.Testing` README.
- Handlers: fake the port, not the transport.

## Check

- `SK0013` flags an explicit constructor that takes a raw `HttpClient`; a typed client registered with
  `AddRestClient` takes it through its primary constructor, as in step 2.
- `SK0036` flags hand-built `RpcException`/`Status`; return a `Result`.
- `SK0030` flags a `Result` from `…ResultAsync` or `ToResultAsync` whose outcome nobody reads.
- `SK0022` flags a raw header name; caller propagation is already done for you.
- Host start fails when a registered client's options under `SharedKernel:Communication:Clients` are invalid.

## Read next

`SharedKernel.Communication`, `SharedKernel.Communication.Rest`, `SharedKernel.Communication.Grpc`,
`SharedKernel.Communication.Testing`.
