---
name: project_dapper_package
description: SharedKernel.Persistence.Dapper package — StronglyTypedIdTypeHandler, SmartEnumTypeHandler, DapperReadService (P-109)
metadata:
  type: project
---

**P-109 (WO-018, 2026-06-03):** `SharedKernel.Persistence.Dapper` package fully planned. References `SharedKernel.Persistence.Abstractions` + `SharedKernel.Persistence.PostgreSQL` (for `NpgsqlConnectionFactory`) + `Dapper`. Does NOT reference `SharedKernel.Persistence.EfCore`.

**Key types:**

- `StronglyTypedIdTypeHandler<TStronglyTypedId, TValue>` — abstract, extends `SqlMapper.TypeHandler<TStronglyTypedId>`; `SetValue` uses `implicit operator TValue` (static method call — no reflection); `Parse` uses `implicit operator TStronglyTypedId` or factory on `StronglyTypedId<TValue>`; consuming services write a one-line concrete subclass per ID type.
- `SmartEnumTypeHandler<TEnum, TValue>` — abstract, extends `SqlMapper.TypeHandler<TEnum>` where `TEnum : SmartEnum<TEnum, TValue>`; `Parse` calls `SmartEnum<TEnum,TValue>.TryFromValue` (no reflection); throws on unknown value.
- `DapperTypeHandlers` — static class; `Register()` is idempotent (guarded by `private static bool _registered`); registers platform-wide handlers (e.g., `DateTimeOffset` for `timestamptz`); service-specific handlers registered by consumers in their composition root.
- `DapperReadService` — abstract base; constructor accepts `IDbConnectionFactory`; three protected methods (`QueryAsync<TResult>`, `QuerySingleOrDefaultAsync<TResult>`, `ExecuteAsync`) each open-and-dispose connection per call via `await using`; parameterized queries only — string interpolation in SQL is a hard violation (SQL injection risk).
- `AddSharedKernelDapper(IServiceCollection)` — calls `DapperTypeHandlers.Register()`; does NOT register `IDbConnectionFactory` (that is `AddSharedKernelPostgreSQL`'s responsibility); returns `IServiceCollection`.

**AOT note:** Dapper uses reflection for parameter binding and result mapping. All Dapper code is behind `DapperReadService` so the AOT boundary is contained to that class. See [[project_postgresql_package]] for connection factory placement.

**Tests:** All require real PostgreSQL Testcontainer.
