# SharedKernel.Messaging.MassTransit.EfCore

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
[![MassTransit 8.5](https://img.shields.io/badge/MassTransit-8.5.x%20(Apache--2.0)-512BD4)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/README.md#why-masstransit-85-and-not-9x)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **MassTransit's EF Core transactional outbox for
> [`SharedKernel.Messaging.MassTransit`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit/README.md),
> over your service's own `DbContext`: messages published inside a unit of work are written in the same
> transaction as your data and delivered after the commit.**

No lost event when the process dies between the commit and the publish, and no phantom event when the transaction
rolls back. The core package does not reference EF Core; reference this package only in the startup project of a
service that uses the outbox.

## Install

```xml
<PackageReference Include="SharedKernel.Messaging.MassTransit" />
<PackageReference Include="SharedKernel.Messaging.MassTransit.EfCore" />
<!-- plus one transport: .RabbitMq or .AzureServiceBus -->
```

Versions come from your single `SharedKernelVersion`. **Adapter** tier; references the MassTransit core (a declared
Adapter → Adapter edge), `MassTransit.EntityFrameworkCore` 8.5.x (not `MassTransit.EntityFrameworkCoreIntegration`)
and `Microsoft.EntityFrameworkCore`. It does **not** reference `06.Persistence`: your `DbContext` arrives as a
generic type parameter, and no outbox type is defined anywhere in the persistence packages.

## Registration

`WithEntityFrameworkOutbox<TDbContext>` is an extension method on `MessagingBusBuilder`, declared in the builder's
own namespace (`SharedKernel.Messaging.MassTransit.Extensions`), so the chain needs no extra `using`. It is built on
the core's `ConfigureMassTransit(...)` extension point and combines with either transport. Call it once.

```csharp
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!)
    .WithEntityFrameworkOutbox<OrdersDbContext>(o => o.QueryDelay = TimeSpan.FromSeconds(1))
    .WithRetry()
    .AddConsumer<OrderPlacedConsumer>()
    .Build();
```

Map MassTransit's outbox entities in the context and add a migration — the service owns it, and the tables must
exist before the bus starts:

```csharp
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
```

```bash
dotnet ef migrations add AddMassTransitOutbox
```

Then publish as usual through `IEventPublisher`/`IMessageBus` inside the unit of work; the message is written to
the outbox during `SaveChangesAsync` and delivered by MassTransit's background worker after the commit.

## Options

`OutboxOptions` (namespace `SharedKernel.Messaging.MassTransit.Options`):

| Option | Default | Notes |
| --- | --- | --- |
| `BatchSize` | `100` | Messages delivered per cycle of the bus outbox |
| `QueryDelay` | 1 s | Polling interval for undelivered rows |
| `DuplicateDetectionWindow` | 30 min | MassTransit's inbox deduplication window |
| `Database` | `PostgreSql` | Selects the SQL the delivery service locks outbox rows with: `PostgreSql`, `SqlServer`, `MySql` or `Sqlite`. Set it when the `DbContext` is not on PostgreSQL |

## Behaviour to know

- **Delivery is at-least-once.** A message can be delivered again after a crash between delivery and the outbox
  update, so consumers must be idempotent — enable `WithIdempotency()` on the consuming side.
- **The lock SQL follows `OutboxOptions.Database`.** MassTransit's own default is SQL Server syntax
  (`SELECT TOP 1 … WITH (UPDLOCK, ROWLOCK, READPAST)`), which PostgreSQL rejects on every poll, so nothing would be
  delivered; the extension therefore selects PostgreSQL unless told otherwise.
- MassTransit is pinned to 8.5.x, the last Apache-2.0 release.
- The outbox is MassTransit's; this package only wires it to the platform builder. Never define an outbox table,
  writer or interceptor of your own alongside it.

## Testing

`SharedKernel.Messaging.MassTransit.EfCore.Tests` asserts the registration and that an outbox row is written during
`SaveChangesAsync`, using SQLite with a kept-open `SqliteConnection("Data Source=:memory:")`.
`SharedKernel.Messaging.MassTransit.EfCore.Integration.Tests` (Integration lane, Docker) proves end-to-end delivery on
PostgreSQL with the default options.

## Related packages

| Package | Why |
| --- | --- |
| [`SharedKernel.Messaging.MassTransit`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit/README.md) | The bus this outbox plugs into |
| [`SharedKernel.Messaging.MassTransit.RabbitMq`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit.RabbitMq/README.md) / [`.AzureServiceBus`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/SharedKernel.Messaging.MassTransit.AzureServiceBus/README.md) | The transport the outbox delivers to |
| [`SharedKernel.Idempotency.Redis` / `.EfCore`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/18.Idempotency/SharedKernel.Idempotency.Abstractions/README.md) | The store consumer-side `WithIdempotency()` needs to make at-least-once delivery safe |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[domain overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/07.Messaging/README.md).
