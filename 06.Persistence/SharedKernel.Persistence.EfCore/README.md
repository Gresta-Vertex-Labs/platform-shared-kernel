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
- `EfAuditTrailWriter` / `EfAuditQueryService` / `EfCoreAuditActorContext` / `AuditRecordImmutabilityInterceptor` / `.WithAuditTrail()` — opt-in, append-only, hash-chained audit trail implementing `SharedKernel.Persistence.Abstractions`'s `IAuditTrailWriter`/`IAuditQueryService`
- `CurrencyValueConverter` / `MoneyValueConverter` / `.OwnsMoney(...)` / `ConfigureMoney()` — `03.Domain`'s `Money`/`Currency` value objects mapped to a single packed column
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

**(P-498/WO-081) `.WithEncryption()` now builds its own persistence-scoped `ISymmetricEncryptionService` internally — `AddSharedKernelCryptography()` is NOT required for this config-backed default path.** It never resolves the ambient, unkeyed `IEncryptionKeyProvider`/`ISymmetricEncryptionService` slot, so an unrelated general-purpose `AddSharedKernelCryptography()` call (or a KMS-key-provider registration made for other purposes, e.g. `13.ServiceDefaults`'s `AddSharedKernelKeyVaultKeyProvider`) elsewhere in the same container can never silently win or lose this package's own encryption wiring.

```csharp
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
//
// Rename-safe AAD binding — supply BEFORE ever encrypting a row you anticipate renaming the
// underlying table/column for:
//   builder.Property(x => x.Ssn).HasMaxLength(20).Encrypt(associatedDataOverride: "Customer.Ssn").IsRequired();
```

### KMS-backed field-level encryption (opt-in, `.WithExternalEncryptionKeyProvider<TProvider>()`)

Directs the SAME field-level encryption pipeline at a KMS/HSM-backed `IEncryptionKeyProvider` (e.g. `SharedKernel.Cryptography.KeyVault.Azure`'s `AzureKeyVaultEncryptionKeyProvider`) instead of the config-backed default — the ONLY sanctioned way to do so; hand-wiring a raw KMS provider directly would either block a thread per encrypted-column read/write (EF Core's `ValueConverter` has no async path) or throw `NotSupportedException` outright once `01.Core`'s `ISynchronousEncryptionKeyProvider` capability gate is in effect.

```csharp
// The consumer registers TProvider itself — e.g. via 13.ServiceDefaults' AddSharedKernelKeyVaultKeyProvider,
// or any other unkeyed registration. This package resolves it by type, never assumes a specific source.
services.AddSharedKernelKeyVaultKeyProvider(configuration);   // registers AzureKeyVaultEncryptionKeyProvider

services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithEncryption(enc => enc.Enabled = true)                          // MUST come first
    .WithExternalEncryptionKeyProvider<AzureKeyVaultEncryptionKeyProvider>()
    .Build();
```

This additionally registers a startup readiness gate (`EncryptionKeyPreWarmingHostedService`) that warms the current key once, at boot, before the host accepts traffic — never a per-request blocking KMS call — and a fifth interceptor (`EncryptionKeyPreWarmingInterceptor`) that keeps the warm cache current across both writes (`SavingChangesAsync`) and reads (`ReaderExecutingAsync`, EF Core's genuine async pre-materialization hook — the piece that actually protects a query-only/read-replica service, which a write-only pre-warm hook would leave completely uncovered).

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

## Mapping `Money` (opt-in, requires `ConfigureMoney()`)

`03.Domain`'s `Money`/`Currency` value objects map to a **single packed `"{amount}:{currencyCode}"` string column** (`HasMaxLength(40)`), not two independently-queryable columns — `Amount` and `Currency` are **NOT filterable or aggregatable in SQL** through this mapping (no `WHERE Currency = 'USD'`, no `ORDER BY Amount`, no `SUM(Amount)`). A service that needs that must map its own separate scalar `decimal`/`string` shadow columns instead. See [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full D-105/D-106 rationale (a genuine two-column owned-type mapping is unreachable through any public EF Core 10 API).

`ConfigureMoney()` **must** be called from `ConfigureConventions()` before `.OwnsMoney(...)` is used anywhere in the model — omitting it fails model building, because EF Core's automatic navigation discovery walks `Money` (and transitively `Currency`) as candidate entity types before `OnModelCreating` ever runs:

```csharp
using SharedKernel.Persistence.EfCore.Conversions;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options, /* ... */)
    : SharedKernelDbContext(options, /* ... */)
{
    // Required once per DbContext — registers the Currency/Money conversions globally, before
    // OnModelCreating's automatic navigation discovery ever walks a Money-typed property.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureMoney();
        base.ConfigureConventions(configurationBuilder);
    }
}

// Applied automatically by SharedKernelDbContext.OnModelCreating via
// ModelBuilder.ApplyConfigurationsFromAssembly — no manual registration needed.
internal sealed class OrderEntityConfiguration : EntityTypeConfigurationBase<Order, OrderId>
{
    public override void Configure(EntityTypeBuilder<Order> builder)
    {
        base.Configure(builder);

        builder.OwnsMoney(x => x.Total);                                        // packed column "Total"
        builder.OwnsMoney(x => x.ShippingFee, columnName: "shipping_fee_amount");
    }
}
```

A `Money` property is deliberately excluded from `ValueObjectOwnershipBuilder`'s generic auto-owned scan — it must always be configured explicitly via `.OwnsMoney(...)`.

## Append-only audit trail (opt-in)

`.WithAuditTrail()` registers an append-only, hash-chained audit trail implementing `SharedKernel.Persistence.Abstractions`'s `IAuditTrailWriter`/`IAuditQueryService` — distinct from `AuditInterceptor` above, which only stamps mutable `CreatedBy`/`ModifiedBy`/`ModifiedOn` columns that the next edit overwrites. Omitting `.WithAuditTrail()` leaves `IAuditTrailWriter`/`IAuditQueryService` unregistered and no `AuditRecord` table in the model.

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UseNpgsql(connectionString))
    .WithAuditTrail()   // registers EfAuditTrailWriter, EfAuditQueryService, AuditRecordImmutabilityInterceptor,
                         // AuditTrailFeatureMarker (singleton), and a default IAuditActorContext bridging the
                         // already-registered IUserContext/ITenantProvider
    .Build();

// AuditTrailFeatureMarker? must be declared on the DbContext's OWN constructor and forwarded to base(...) —
// the same pattern .WithEncryption() already requires for ISymmetricEncryptionService?/IEncryptionKeyProvider?.
// A raw bool flag cannot do this: DI cannot auto-resolve a primitive constructor parameter, only a registered type.
public sealed class OrderDbContext : SharedKernelDbContext
{
    public OrderDbContext(
        DbContextOptions<OrderDbContext> options,
        AuditInterceptor audit, SoftDeleteInterceptor softDelete, ConcurrencyInterceptor concurrency,
        IEnumerable<ISaveChangesInterceptor>? additionalInterceptors,
        AuditTrailFeatureMarker? auditTrailMarker)   // <-- required for AuditRecord to join this context's model
        : base(options, audit, softDelete, concurrency, additionalInterceptors, auditTrailMarker: auditTrailMarker)
    { }
}

var record = await auditTrailWriter.RecordAsync(new AuditEntry
{
    Action = "CustomerLimitChanged",
    ResourceType = nameof(Customer),
    ResourceId = customer.Id.ToString(),
    BeforeSnapshot = JsonSerializer.Serialize(beforeState),
    AfterSnapshot = JsonSerializer.Serialize(afterState),
    ApprovalId = approvalRecord?.Id.ToString(),
}, ct);
```

`AuditRecordImmutabilityInterceptor` (the fourth, opt-in-only interceptor `.WithAuditTrail()` registers) throws if any `AuditRecord` entry is `Modified` or `Deleted` — the load-bearing structural guarantee. **THIS IS AN APPLICATION-LEVEL GUARD ONLY — IT CANNOT STOP A DBA-LEVEL OR DIRECT-SQL MUTATION.** For real defense-in-depth, ALSO ISSUE A DATABASE-LEVEL `REVOKE UPDATE, DELETE` GRANT ON THE UNDERLYING `AuditRecord` TABLE FOR THE APPLICATION'S DATABASE ROLE. `IAuditQueryService.VerifyChainIntegrityAsync` detects a tampered record's hash mismatch after the fact — it proves tampering occurred, it does not prevent it.

## Explicit transactions, bulk mutation, streaming, keyset pagination

See [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full `ITransactionalUnitOfWork`/`IBulkMutationRepository`/`IReadRepository.StreamAsync`/`ListKeysetAsync<TKey>` examples, the canonical `SpecificationEvaluator<T>` pipeline order, and every hard violation this package enforces.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts, implementation rules, and AOT posture.
