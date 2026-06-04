---
name: project_postgresql_package
description: SharedKernel.Persistence.PostgreSQL package — SnakeCaseNamingConvention, JSONB, pgvector, NpgsqlConnectionFactory (P-108)
metadata:
  type: project
---

**P-108 (WO-018, 2026-06-03):** `SharedKernel.Persistence.PostgreSQL` package fully planned. References `SharedKernel.Persistence.EfCore` + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x + `Pgvector.EntityFrameworkCore`. Does NOT reference Dapper.

**Key types:**

- `SnakeCaseNamingConvention` — `IModelFinalizingConvention`; converts all table/column/index/constraint names to `snake_case`; idempotent (already-snake-cased names pass through).
- `UsePostgreSQL(DbContextOptionsBuilder, string connectionString)` — configures Npgsql + `SnakeCaseNamingConvention` + vector support + JSONB defaults. Called inside the `configureDb` action passed to `AddSharedKernelEfCore<TContext>`.
- `HasJsonbColumn<TProperty>` on `EntityTypeBuilder<T>` — applies `HasColumnType("jsonb")`; STJ serialization configured globally by Npgsql, no per-column converters needed. Companion `[JsonbColumn]` attribute.
- `HasVectorColumn<TProperty>(propertyExpression, int dimensions)` on `EntityTypeBuilder<T>` — applies `vector({dimensions})` column type. Companion `[VectorColumn(int Dimensions)]` attribute.
- `AddSharedKernelPostgreSQL(IServiceCollection, string connectionString)` — registers `NpgsqlDataSource` as singleton + `IDbConnectionFactory → NpgsqlConnectionFactory` as scoped; shared pool used by both EF Core (via `UseNpgsql(dataSource)`) and Dapper.
- `NpgsqlConnectionFactory` — sealed, implements `IDbConnectionFactory`; backed by `NpgsqlDataSource`; `CreateConnectionAsync` returns open `NpgsqlConnection`; caller disposes.

**Placement of NpgsqlConnectionFactory:** Lives in PostgreSQL package (not Dapper). Dapper package references PostgreSQL package to consume it. See [[project_dapper_package]].

**Tests:** All require real PostgreSQL Testcontainer (no SQLite substitute for these tests).
