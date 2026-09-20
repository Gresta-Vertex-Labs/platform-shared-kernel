# SharedKernel.Persistence.PostgreSQL

PostgreSQL-specific EF Core conventions for Platform.SharedKernel microservices, built on `SharedKernel.Persistence.EfCore`: `SnakeCaseNamingConvention`, the genuine PostgreSQL optimistic-concurrency mechanism (`XminConcurrencyTokenConvention` binding `IHasConcurrency` to the real `xmin` system column), JSONB column support, pgvector column support, `NpgsqlConnectionFactory`, and the `UsePostgreSQL()` / `AddSharedKernelPostgreSQL()` DI extensions.

## Included types

- `UsePostgreSQL(DbContextOptionsBuilder, connectionString, ...)` — one call wires the Npgsql provider, snake_case naming, the `xmin` concurrency-token convention, and (optionally) `EnableRetryOnFailure`
- `SnakeCaseNamingConvention` — converts table/column/index/constraint names to snake_case automatically
- `XminConcurrencyTokenConvention` / `XminRowVersionValueConverter` — the working `IHasConcurrency`/`FullAuditableAggregateRoot<TId>` concurrency mechanism (`.IsRowVersion()` alone is non-functional on a plain PostgreSQL `bytea` column)
- `HasJsonbColumn` — JSONB column mapping (reflection-based `JsonSerializerOptions` overload, or a source-generated `JsonTypeInfo<T>` overload for an AOT/trim-friendly path), with a structural `ValueComparer` so an in-place mutation of the deserialized object graph is detected as a change
- `HasVectorColumn` / `HasHalfVectorColumn` — pgvector-typed (`Pgvector.Vector`) and reduced-precision `halfvec` column mapping; `HasVectorIndex` — HNSW/IVFFlat approximate-nearest-neighbor index creation with the operator class matching a `VectorDistanceMetric`
- `VectorDistanceMetric` / `VectorOrderingExpressions.ByDistance<TAggregate>(...)` — server-side nearest-neighbor `ORDER BY` expression builder for `Pgvector.Vector`-typed columns (cosine/L2/L1/inner-product, zero runtime reflection)
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
    .Build();
```

**`.WithTransientFaultRetry(...)` and `.WithTransactionalUnitOfWork()` cannot be combined on the same builder** — `Build()` throws immediately. A retrying execution strategy can replay work, but not work spanning a transaction the caller began itself (`BeginTransactionAsync`), so the combination would make every explicit transaction fail at runtime instead. Enable retry only for a service that has no need for `ITransactionalUnitOfWork`.

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

pgvector support is opt-in at the data-source level — pass `useVector: true` to `UsePostgreSQL(...)`, which both enables Npgsql's `vector` CLR-type mapping and registers the `CREATE EXTENSION IF NOT EXISTS vector` model annotation automatically. There is no separate `EnsureVectorExtension()` call to make. Calling `HasVectorColumn` without `useVector: true` produces a column EF Core cannot translate a `Pgvector.Vector`-typed parameter for.

```csharp
services
    .AddSharedKernelEfCore<OrderDbContext>(options => options.UsePostgreSQL(connectionString, useVector: true))
    .Build();
```

## pgvector nearest-neighbor queries (Quick-Start)

`HasVectorColumn` maps the column type only. `VectorOrderingExpressions.ByDistance<TAggregate>(...)` is the first query-side ergonomics for actually finding the nearest rows to a query vector — compose it into your own `Specification<T>` subclass alongside the usual `Criteria`/paging; zero `SpecificationEvaluator<T>` changes are needed:

```csharp
public sealed class NearestProductsSpecification : Specification<Product>
{
    public NearestProductsSpecification(Vector queryEmbedding, int topK)
    {
        AddCriteria(p => p.IsActive);
        ApplyOrderBy(VectorOrderingExpressions.ByDistance<Product>(
            p => p.Embedding, queryEmbedding, VectorDistanceMetric.Cosine));
        ApplyPaging(skip: 0, take: topK);
    }
}

var nearest = await productReadRepository.ListAsync(
    new NearestProductsSpecification(queryVector, topK: 10), ct);
```

`VectorDistanceMetric` supports `Cosine`, `L2` (Euclidean), `L1`, and `InnerProduct` — a deliberate narrowing of the six distance/similarity functions `Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions` exposes (`HammingDistance`/`JaccardDistance` apply only to `bit`-vector columns and stay out of scope). `HasVectorIndex` accepts the same metric to pick the matching pgvector operator class for an HNSW/IVFFlat index. Scoped to `Pgvector.Vector`-typed properties only — `HasVectorColumn` is not generic over an arbitrary property type; EF Core's own relational model validator rejects a `float[]` property mapped to a `vector(n)` column outright, so there never was a working `float[]` path to preserve.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts, concurrency-token rationale, and implementation rules.
