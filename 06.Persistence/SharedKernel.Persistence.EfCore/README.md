# SharedKernel.Persistence.EfCore

EF Core 10 implementation of `SharedKernel.Persistence.Abstractions` for Platform.SharedKernel microservices: `EfRepository<TAggregate,TId>` / `EfReadRepository<TAggregate,TId>`, `EfUnitOfWork` / `EfTransactionalUnitOfWork`, the platform's three save-changes interceptors (Audit, SoftDelete, Concurrency — deliberately **no** outbox interceptor, see below), `SpecificationEvaluator<T>`, `SharedKernelDbContext` / `TenantedDbContext`, field-level AES-256-GCM column encryption, and the `EfCorePersistenceBuilder` fluent DI entry point (`AddSharedKernelEfCore<TContext>`).

> **Outbox scope note:** the outbox pattern is owned entirely by `07.Messaging` via MassTransit's `UseEntityFrameworkOutbox`. No `OutboxMessage`/`IOutboxWriter`/`OutboxInterceptor` types exist in this package.

## Included types

- `SharedKernelDbContext` — abstract base; registers the three platform interceptors; `CurrentUserContext`/`RefreshUserContext` support DbContext pooling
- `TenantedDbContext` — multi-tenant base; installs an expression-tree global tenant query filter; `RefreshRequestContext` for pooling
- `EfRepository<TAggregate,TId>` / `EfReadRepository<TAggregate,TId>` — abstract bases consuming services extend per aggregate
- `EfUnitOfWork` / `EfTransactionalUnitOfWork` — the `IUnitOfWork.SaveChangesAsync` save boundary and explicit-transaction support
- `AuditInterceptor` / `SoftDeleteInterceptor` / `ConcurrencyInterceptor` — the platform three, always composed first
- `SpecificationEvaluator<T>` — criteria → keyset seek → includes → split-query → ordering → distinct → tracking → paging → projection, in that fixed order
- `EntityTypeConfigurationBase<TEntity,TId>`, `StronglyTypedIdValueConverter<TId,TValue>` — EF Core configuration building blocks
- `EncryptedValueConverter`, `.Encrypt()` extension, `EncryptionModelConvention` — transparent field-level AES-256-GCM column encryption
- `IRestorableRepository<TAggregate,TId>` / `EfRepository.RestoreAsync` — single-entity soft-delete restore (stages only, same save boundary as every other write)
- `IReadReplicaContextAccessor<TContext>` / `.WithReadReplica(...)` — opt-in read-replica routing for `IReadRepository`
- `[LoggerMessage]`-based structured logging (EventIds `6000-6010`) across `ConcurrencyInterceptor`, `MigrationAndSeedHostedService`, transient-retry diagnostics, and `EncryptionRotationService` — never a key byte or plaintext/ciphertext value
- `EfCorePersistenceBuilder<TContext>` — fluent DI builder (`AddSharedKernelEfCore<TContext>(...)`)

## Install

```xml
<ProjectReference Include="..\SharedKernel.Persistence.EfCore\SharedKernel.Persistence.EfCore.csproj" />
```

## Quick start — single-tenant service

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .Build();

services.AddScoped<IUserContext, OidcUserContext>();        // overrides the no-op placeholder
services.AddScoped<IRepository<Order, OrderId>, OrderEfRepository>();
services.AddScoped<IReadRepository<Order, OrderId>, OrderEfReadRepository>();
```

## Quick start — multi-tenant service

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithMultiTenancy()          // TContext must extend TenantedDbContext, or Build() throws
    .Build();

services.AddScoped<ITenantProvider, ClaimsTenantProvider>();
```

## DbContext pooling (opt-in, high-throughput services)

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithDbContextPooling(poolSize: 1024)
    .Build();
```

Consumer code injects `OrderDbContext` exactly as before — pooling and the per-lease user/tenant-context refresh are transparent. Cannot be combined with `.WithDbContextFactory()` or `.WithEncryption()` (both throw an actionable `InvalidOperationException` at `Build()` time).

## Field-level encryption

```csharp
services.AddSharedKernelCryptography(configuration);   // 01.Core/SharedKernel.Cryptography
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithEncryption(enc =>
    {
        enc.Enabled = true;
        enc.CurrentVersion = "v1";
        enc.Keys["v1"] = "<Base64-encoded 32-byte key>";
    })
    .Build();

// Inside IEntityTypeConfiguration<Customer>.Configure:
//   builder.Property(x => x.Email).HasMaxLength(255).Encrypt().IsRequired();
```

## Configuration-section binding

`.WithEncryption(...)` and `.WithServiceName(...)` also accept an `IConfiguration` overload — binds from `EncryptionOptions.SectionName`/`PersistenceServiceOptions.SectionName` (`"SharedKernel:Encryption"`/`"SharedKernel:Persistence"`) instead of a code-only `Action<T>`. Both overloads compose under normal `IOptions<T>` later-registration-wins semantics:

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithEncryption(configuration)     // binds configuration.GetSection(EncryptionOptions.SectionName)
    .WithServiceName(configuration)    // binds configuration.GetSection(PersistenceServiceOptions.SectionName)
    .Build();
```

```json
{
  "SharedKernel": {
    "Encryption": { "Enabled": true, "CurrentVersion": "v1", "Keys": { "v1": "<Base64-encoded 32-byte key>" } },
    "Persistence": { "ServiceName": "order-service" }
  }
}
```

## Soft-delete restore and command timeout (opt-in)

```csharp
// Single-entity restore — stages only; caller still calls SaveChangesAsync.
var order = await orderRepository.GetBySpecAsync(new ByIdSpecification<Order, OrderId>(orderId), ct);
if (order is not null)
{
    await orderRepository.RestoreAsync(order, ct);
    await unitOfWork.SaveChangesAsync(ct);
}

// Command timeout — provider-neutral (Microsoft.EntityFrameworkCore.Relational), not Npgsql-specific.
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithCommandTimeout(commandTimeoutSeconds: 30)
    .Build();
```

## Read-replica routing (opt-in)

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(primaryConnectionString))
    .WithReadReplica(options => options.UseNpgsql(replicaConnectionString))
    .Build();
```

`IReadRepository` reads are routed to the replica connection; `IRepository` writes always target the primary. **READ-AFTER-WRITE CONSISTENCY BECOMES THE CALLER'S RESPONSIBILITY ONCE ENABLED** — a handler that writes then immediately reads via `IReadRepository` in the same logical operation MAY OBSERVE STALE DATA under replication lag. A read issued inside an active transaction is NEVER routed to the replica, even when this is configured.

## Explicit transactions, bulk mutation, streaming, keyset pagination

See [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full `ITransactionalUnitOfWork`/`IBulkMutationRepository`/`IReadRepository.StreamAsync`/`ListKeysetAsync<TKey>` examples, the canonical `SpecificationEvaluator<T>` pipeline order, and every hard violation this package enforces.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts, implementation rules, and AOT posture.
