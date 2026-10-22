---
proven-by:
  - samples/Shop/Shop.Contracts/Billing/BillingContracts.cs
  - samples/Shop/Shop.Contracts/Ordering/OrderEvents.cs
  - samples/Shop/Shop.Contracts/Shop.Contracts.csproj
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderingPorts.cs
  - samples/Shop/Ordering/Shop.Ordering.Application/Shop.Ordering.Application.csproj
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Billing/RestPayments.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Inventory/GrpcInventoryReservations.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/OrderingInfrastructure.cs
  - samples/Shop/Notify/Shop.Notify.Worker/Receipts.cs
  - samples/Shop/Notify/Shop.Notify.Worker/Program.cs
unproven:
  - Bumping an event to Version 2: every reference event is Version 1; the rule is stated in the SharedKernel.Contracts README
  - One contracts package per service, published to a feed: the reference platform shares a single contracts project and packs nothing
  - A typed client published for other services to reference: the reference platform has none, callers write their own class over the REST client package
  - Reading a paged result in a typed client: the reference platform has no paged client call; the SharedKernel.Contracts README shows the source-generated context
---
# Publish and consume a service's contracts

**Use when:** a service exposes something other services depend on (integration events, request and response
DTOs, error codes, gRPC services), or it calls another service and must keep that service's types and client out of
its Application logic.
**Not when:** you only need to call a service and already have its types (add-outbound-call.md); you are wiring the
bus itself, outbox or transport (add-messaging.md); you are exposing the endpoint behind the contract
(add-http-endpoint.md).

Recipes decide packages and APIs; the service's own project guidance (the instructions file its assistant reads)
decides folders and file placement. Names below are the reference platform's.

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Contracts project | `SharedKernel.Contracts` | `IIntegrationEvent`, `[IntegrationEvent]`, `EventEnvelope<T>`, `PagedList<T>` |
| Application | `SharedKernel.Application` | The port a handler calls; it returns `Result`/`Result<T>` |
| Application | `SharedKernel.Messaging.Abstractions` | `IEventPublisher`, when this service publishes the events |
| Infrastructure | `SharedKernel.Communication.Rest` | `AddRestClient<IClient, Client>("name")`, `…ResultAsync` verbs |
| Infrastructure | `SharedKernel.Communication.Grpc` | `AddGrpcClient<TClient>("name")`, `call.ToResultAsync()` |
| Infrastructure or Worker | `SharedKernel.Messaging.MassTransit` | `ConsumerBase<T>` for a foreign event |

A contracts project holds wire shapes only: no domain type, no logic, no reference to a service's own projects.
Application and Infrastructure may reference it; a service never references another service's projects.

## Steps

1. **Contracts project: events.** A `sealed record` implementing `IIntegrationEvent` with a wire name
   `{context}.{name}` (lowercase letters and digits, segments joined by `.`, `-` or `_`) and a version. The name is
   what brokers route on, so choose it once; carry primitives (`Guid`, `decimal` plus a currency code), never
   domain types.

   ```csharp
   using SharedKernel.Contracts.Events;

   [IntegrationEvent("billing.receipt-due", Version = 1)]
   public sealed record ReceiptDue(
       Guid EventId,
       DateTimeOffset OccurredOn,
       Guid PaymentId,
       Guid OrderId,
       string CustomerEmail,
       decimal Amount,
       string Currency) : IIntegrationEvent;
   ```

   Adding an optional member keeps the version. A breaking change is a new type with the **same name** and the next
   `Version`, published beside the old one until every consumer has moved. Never reuse a name and version.

2. **Contracts project: DTOs, error codes and the JSON context.** Request and response records, the error codes
   other services act on as constants, and one source-generated context listing the DTOs a client sends or reads.
   A client that reads a page lists `PagedList<T>` in the same context (SharedKernel.Contracts README).

   ```csharp
   using System.Text.Json;
   using System.Text.Json.Serialization;

   public sealed record ChargeRequest(Guid OrderId, decimal Amount, string Currency, string CustomerEmail, string PaymentToken);
   public sealed record PaymentView(Guid PaymentId, Guid OrderId, string Status, decimal Amount, string Currency);

   public static class BillingErrorCodes
   {
       public const string PaymentDeclined = "billing.payment_declined";
   }

   [JsonSerializable(typeof(ChargeRequest))]
   [JsonSerializable(typeof(PaymentView))]
   [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
   public sealed partial class BillingJsonContext : JsonSerializerContext;
   ```

   gRPC services are `.proto` files in the same project, compiled for both server and client
   (`<Protobuf Include="Protos\*.proto" GrpcServices="Both" />`). The contracts project references
   `SharedKernel.Contracts`, `Google.Protobuf`, `Grpc.Core.Api` and `Grpc.Tools`, and nothing else from the kernel.

3. **Publishing a client for others.** The kernel generates no client and ships no client base class. The owning
   service may publish the contracts project (and, if it wishes, a small class over the REST client package) for
   others to reference; a caller can equally write its own, as step 4 does. A published contracts package is
   referenced the same way as the shared project.

4. **Application: a port, in the caller's own words.** The handler depends on an interface that returns `Result`;
   it never sees `HttpClient`, a gRPC client or the other service's problem response. Application may reference the
   contracts project for event and DTO types, never a Communication package.

   ```csharp
   using SharedKernel.Primitives.Results;

   public interface IPayments
   {
       Task<Result<Guid>> ChargeAsync(PaymentRequest request, CancellationToken ct);
       Task<Result> RefundAsync(OrderId orderId, CancellationToken ct);
   }
   ```

5. **Infrastructure: the client and its registration.** The class behind the port takes the kernel-configured
   `HttpClient`, sends the contract DTOs with the context's `JsonTypeInfo` and returns the other service's own
   `Error`. A gRPC client is the same idea over the generated client and `ToResultAsync`
   (`GrpcInventoryReservations`, add-outbound-call.md). Register every client on one builder; the name is its
   configuration key.

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
   }

   services
       .AddSharedKernelCommunication(configuration)
       .AddRestClient<IPayments, RestPayments>(RestPayments.ClientName);
   ```

   The caller checks `Error.Code` against the constants from step 2 (`BillingErrorCodes.PaymentDeclined`).

6. **Infrastructure or Worker: consume a foreign event.** Reference the contracts project, derive from
   `ConsumerBase<EventEnvelope<TEvent>>` and register it on the bus. The consumer is an adapter, not a port: its
   work is done through the consuming service's own ports or senders.

   ```csharp
   using Microsoft.Extensions.Logging;
   using SharedKernel.Contracts.Events;
   using SharedKernel.Messaging.MassTransit.Consumers;
   using Shop.Contracts.Billing;

   public sealed class ReceiptDueConsumer(ILogger<ReceiptDueConsumer> logger)
       : ConsumerBase<EventEnvelope<ReceiptDue>>(logger)
   {
       protected override Task ConsumeAsync(EventEnvelope<ReceiptDue> envelope, CancellationToken ct)
       {
           ReceiptDue receipt = envelope.Data;
           // act on receipt.OrderId and receipt.Amount through the service's own ports
           return Task.CompletedTask;
       }
   }

   services.AddSharedKernelMessaging(configuration)
       .UseRabbitMq(connectionString)
       .WithInboundRequestContext()
       .AddConsumer<ReceiptDueConsumer>()
       .Build();
   ```

   Reading an envelope whose `type` is not the target event's name fails instead of yielding an empty object.

7. **Configuration:** `SharedKernel:Communication:Clients:{name}` for each client (address, timeouts, credentials);
   the bus section is in the `SharedKernel.Messaging.MassTransit` README.

## Test it

- Handlers: fake the port, not the transport.
- Clients and gRPC failures: `StubHttpMessageHandler` and `GrpcCalls` as in add-outbound-call.md.
- Consumers: add-messaging.md. Contract shapes: `SharedKernel.Testing` has envelope and page builders and assertions.

## Check

- A contracts project that references a service's Domain, Application or Infrastructure project is wrong: it must
  stay a leaf the services share.
- The Application project has no `SharedKernel.Communication*` package reference; the service's architecture tests
  flag the placement (write-tests.md).
- `SK0013` flags a typed client that takes `HttpClient` through an explicit constructor.

## Read next

`SharedKernel.Contracts`, `SharedKernel.Communication.Rest`, `SharedKernel.Communication.Grpc`,
`SharedKernel.Messaging.MassTransit`.
