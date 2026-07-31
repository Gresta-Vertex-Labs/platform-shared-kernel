# SharedKernel.Persistence.PostgreSQL

PostgreSQL-specific EF Core conventions for Platform.SharedKernel microservices, built on `SharedKernel.Persistence.EfCore`: `SnakeCaseNamingConvention`, the genuine PostgreSQL optimistic-concurrency mechanism (`XminConcurrencyTokenConvention` binding `IHasConcurrency` to the real `xmin` system column), JSONB column support, pgvector column support, `NpgsqlConnectionFactory`, and the `UsePostgreSQL()` / `AddSharedKernelPostgreSQL()` DI extensions.

## Included types

- `UsePostgreSQL(DbContextOptionsBuilder, connectionString, ...)` — one call wires the Npgsql provider, snake_case naming, the `xmin` concurrency-token convention, and (optionally) `EnableRetryOnFailure`
- `SnakeCaseNamingConvention` — converts table/column/index/constraint names to snake_case automatically
- `XminConcurrencyTokenConvention` / `XminRowVersionValueConverter` — the working `IHasConcurrency`/`FullAuditableAggregateRoot<TId>` concurrency mechanism (`.IsRowVersion()` alone is non-functional on a plain PostgreSQL `bytea` column)
- `HasJsonbColumn` / `JsonbColumnAttribute` — JSONB column mapping via Npgsql's native JSON support
- `HasVectorColumn` / `VectorColumnAttribute` — pgvector-typed column mapping (`Pgvector.EntityFrameworkCore`)
- `NpgsqlConnectionFactory` — `IDbConnectionFactory` implementation shared with the Dapper package
- `AddSharedKernelPostgreSQL(connectionString)` — registers the shared `NpgsqlDataSource` and `IDbConnectionFactory`

## Install

```xml
<ProjectReference Include="..\SharedKernel.Persistence.PostgreSQL\SharedKernel.Persistence.PostgreSQL.csproj" />
```

## Quick start

```csharp
// EF Core context — one call configures the provider, naming convention, and concurrency token.
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UsePostgreSQL(connectionString))
    .Build();

// Shared NpgsqlDataSource + IDbConnectionFactory (consumed by SharedKernel.Persistence.Dapper
// in the same service, and by any readiness probe using IDbConnectionFactory.CheckReadinessAsync).
services.AddSharedKernelPostgreSQL(connectionString);
```

## Transient-fault retry (opt-in)

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options =>
        options.UsePostgreSQL(connectionString, maxRetryCount: 6))   // the only legal EnableRetryOnFailure call site
    .WithTransientFaultRetry(maxRetryCount: 6)
    .WithTransactionalUnitOfWork()
    .Build();
```

Held-open explicit transactions must migrate to `ITransactionalUnitOfWork.ExecuteInTransactionAsync` once retry is enabled — `BeginTransactionAsync()` throws an actionable `InvalidOperationException` under a configured retrying execution strategy.

## JSONB and pgvector columns

```csharp
public sealed class ProductConfig : EntityTypeConfigurationBase<Product, ProductId>
{
    public override void Configure(EntityTypeBuilder<Product> builder)
    {
        base.Configure(builder);
        builder.HasJsonbColumn(p => p.Attributes);
        builder.HasVectorColumn(p => p.Embedding, dimensions: 1536);
    }
}
```

Call `EnsureVectorExtension()` in a migration or at startup before the first vector column is used.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts, concurrency-token rationale, and implementation rules.
