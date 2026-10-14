---
proven-by:
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderCommands.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/OrderEndpoints.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/OrderingInfrastructure.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Fulfilment/OrderFulfilment.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/Program.cs
  - samples/Shop/Ordering/Shop.Ordering.Tests/OrderingTests.cs
---
# Add idempotency

**Use when:** a command must run once even when the client retries or double-submits it, or a consumer must
process a redelivered message once.
**Not when:** the operation is a query (queries are already idempotent; caching is add-caching.md), or the
deduplication is a vendor's (a provider-side idempotency key, as SMS sending uses: add-notifications-webhooks.md).
A multi-step process that must resume after a crash is add-long-running-workflow.md.

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.Application` | `IIdempotentRequest` marker on the command |
| Application | `SharedKernel.Idempotency.Abstractions` | `IIdempotencyStore`, `IdempotencyPurpose` (when the Application project names them) |
| Infrastructure | `SharedKernel.Idempotency.Redis` | Store in Redis: `AddRedisIdempotency(…)` |
| Infrastructure | `SharedKernel.Idempotency.EfCore` | Store in PostgreSQL: `AddEfCoreIdempotency(…)` |
| Infrastructure | `SharedKernel.Caching.Redis.Core` | The shared Redis connection the Redis store uses |
| Infrastructure | `SharedKernel.Messaging.MassTransit` | `WithIdempotency()` on the bus builder, for consumers |
| Api/Worker | `SharedKernel.Application.Pipeline` | `app.WithIdempotency()` |
| Api/Worker | `SharedKernel.Presentation.WebApi` | Binds the `Idempotency-Key` header as `IdempotencyKey` |

A store serves one or both **purposes**: `Request` (commands through the pipeline) and `Message` (consumer
deduplication). Each purpose has exactly one store; the reference platform keeps requests in Redis and
messages in PostgreSQL next to the outbox.

## Steps

1. **Application.** The command implements `IIdempotentRequest`; its response must be a `Result`. The key
   comes from the client, carried on the command. Override `Fingerprint` only when the command's JSON is not
   stable across retries (the package README explains when).

```csharp
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;

[RequirePermission(OrderingPermissions.Place)]
public sealed record PlaceOrderCommand(
    string CustomerEmail,
    string ShippingAddress,
    string Currency,
    IReadOnlyList<PlaceOrderLine> Lines,
    string PaymentToken,
    string IdempotencyKey
) : ICommand<Guid>, IIdempotentRequest;
```

   A retry with the same key replays the first answer; the same key with a different command is refused. The
   key is reserved per tenant and caller, so it only has to be unique for its sender.

2. **Infrastructure.** Register one store per purpose.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Idempotency.EfCore.Extensions;
using SharedKernel.Idempotency.Redis.Extensions;
using SharedKernel.Persistence;

// Requests in Redis, over the one shared Redis connection.
services.AddRedisConnection(configuration);
services.AddRedisIdempotency(purposes => purposes.ForRequests());

// Messages in PostgreSQL. The store owns its context, so it gets its own data source.
var idempotencyDataSource = NpgsqlDataSource.Create(
    configuration.GetConnectionString("ordering")
        ?? throw new InvalidOperationException("ConnectionStrings:ordering is not configured.")
);
services.AddEfCoreIdempotency(
    db => db.UsePostgres(idempotencyDataSource),
    purposes => purposes.ForMessages()
);
```

   One store can serve both: `purposes => purposes.ForRequests().ForMessages()`. Dispose a data source you
   create yourself with the host (the reference platform registers a small `IAsyncDisposable` owner for it).

   **Consumers.** `WithIdempotency()` on the bus builder deduplicates every consumer through the `Message`
   store (the rest of the chain is in add-messaging.md):

```csharp
using SharedKernel.Messaging.MassTransit.Extensions;

services
    .AddSharedKernelMessaging(configuration)
    .UseRabbitMq(rabbitMqConnectionString)
    .WithRetry()
    .WithIdempotency()
    .WithInboundRequestContext()
    .AddConsumer<OrderPlacedConsumer>()
    .Build();
```

   Make the consumer's own side effect idempotent as well where it can be (the reference consumer starts a
   workflow with a business key, so a second start is refused and acknowledged).

3. **Api/Worker.** Opt into the behavior, and bind the header in the endpoint.

```csharp
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.Presentation.WebApi;

builder.Services.AddSharedKernelApplication(
    typeof(PlaceOrderCommand).Assembly,
    app => app.UseMediatR().WithIdempotency().WithTransactions()
);

orders.MapPost(
    "/",
    (PlaceOrderRequest body, IdempotencyKey key, ISender sender, CancellationToken ct) =>
        sender
            .Send(new PlaceOrderCommand(body.CustomerEmail, body.ShippingAddress, body.Currency,
                body.Lines, body.PaymentToken, key.Value), ct)
            .ToCreated(id => $"/orders/{id}", id => new OrderPlacedResponse(id))
);
```

   Endpoint modules are covered in add-http-endpoint.md.

4. **Configuration.** `SharedKernel:Idempotency:Redis`, `SharedKernel:Idempotency:EfCore`,
   `SharedKernel:Messaging:Idempotency`, and the Redis connection section of `SharedKernel.Caching.Redis.Core`.
   The keys are in each package README.

## Test it

- Command: `ApplicationPipelineTestHarness` (`SharedKernel.Application.Testing`) with
  `services.AddFakeApplicationBehaviorServices()`, which registers a `FakeIdempotencyStore` for the
  `Request` purpose, and `Configure(app => app.WithIdempotency())`. Send the same key twice and assert one
  stored aggregate, one published event, and the same returned value.
- A store on its own: `services.AddFakeIdempotencyStore(IdempotencyPurpose.Request)`
  (`SharedKernel.Idempotency.Testing`) follows the same reservation protocol as the Redis and EF Core stores.
- Consumer: call `Consume` twice with the same message and assert the side effect happened once.

## Check

- `SK0040`: `IIdempotentRequest` on a command whose response is not `Result`-shaped.
- The host fails at startup when `app.WithIdempotency()` or the bus's `WithIdempotency()` finds no store for
  its purpose (the bus checks at `Build()`, so register the store first); a second store for the same
  purpose throws at registration.
- An empty key fails the command with `idempotency.key_required`; a reused key with a different command
  fails with `idempotency.key_reused`.

## Read next

`SharedKernel.Idempotency.Abstractions`, `SharedKernel.Idempotency.Redis`, `SharedKernel.Idempotency.EfCore`,
`SharedKernel.Application.Pipeline`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Presentation.WebApi`
