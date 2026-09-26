# SharedKernel.Messaging.MassTransit.EfCore

MassTransit's EF Core transactional outbox for `SharedKernel.Messaging.MassTransit`, over the consuming service's own `DbContext`. Reference it only in a service that uses the outbox; the core package does not reference EF Core.

```csharp
builder.Services
    .AddSharedKernelMessaging(builder.Configuration)
    .UseRabbitMq(connectionString)
    .WithEntityFrameworkOutbox<OrdersDbContext>(o => o.QueryDelay = TimeSpan.FromSeconds(1))
    .Build();
```

The `DbContext` must map the outbox tables (`modelBuilder.AddInboxStateEntity()`, `AddOutboxMessageEntity()`, `AddOutboxStateEntity()`), and the service owns the migration. Delivery is at-least-once, so consumers must be idempotent.

The delivery service locks outbox rows with SQL that depends on the database, chosen by `OutboxOptions.Database`: `PostgreSql` (the default), `SqlServer`, `MySql` or `Sqlite`. Set it when the `DbContext` is not on PostgreSQL. MassTransit's own default is SQL Server syntax, which PostgreSQL rejects on every poll, so nothing would be delivered.

MassTransit is pinned to 8.5.x, the last Apache-2.0 release.
