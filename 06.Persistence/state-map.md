# 06.Persistence — State Map

> **What this file is:** Phase and task tracker for all work within `06.Persistence`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.06.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
| --- | --- | --- |
| `SK.06.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.06.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.06.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.06.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.06.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.06.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement IRepository<TAggregate,TId> | SK.06.Core | SharedKernel.Persistence.Abstractions | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.06.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Persistence.Abstractions` | Core | `●` | Zero ORM dependencies; references Primitives + Domain; no outbox types (MassTransit EF outbox is 07.Messaging) |
| `SharedKernel.Persistence.EfCore` | Core | `●` | EF Core 10.x; references Abstractions + Domain; three interceptors (Audit, SoftDelete, Concurrency) — no OutboxInterceptor |
| `SharedKernel.Persistence.PostgreSQL` | — | `○` | Npgsql + pgvector + JSONB; references EfCore |
| `SharedKernel.Persistence.Dapper` | — | `○` | Read-side micro-ORM; references Abstractions + Dapper |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.06.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Error`, `IClock`) | Available |
| `SK.06.Scaffold` | `01.Core` | `SharedKernel.Core` ProjectReference (`SharedKernelException`, `DomainException`) | Available |
| `SK.06.Scaffold` | `03.Domain` | `SharedKernel.Domain` ProjectReference (`IAggregateRoot<TId>`, `IHasDomainEvents`, `ISpecification<T>`, audit interfaces) | Available |
| `SK.06.Core` | `12.Security` | `IUserContext` interface — injected as scoped DI dependency into `AuditInterceptor` and `SoftDeleteInterceptor`; no direct project reference to `12.Security` packages | Pending (12.Security) |

---

## Phase: Design <!-- phase-key: SK.06.Design -->

> Finalize all interface shapes, interceptor contracts, specification evaluator behavior, and DI extension signatures before any implementation begins.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| D-01 | Design `StronglyTypedIdValueConverter<TId, TValue>` — implicit-operator-based EF value converter with no reflection; companion `StronglyTypedIdValueConverterSelector` for `ModelConfigurationBuilder` auto-registration | WO-008 (P-033) | `SharedKernel.Persistence.EfCore` | `●` |
| D-02 | Design `ValueObjectOwnershipConvention` — `IEntityTypeConfiguration` model-building convention that auto-applies `.OwnsOne()` for `IValueObject` properties; nested value object handling; document `OwnsMany` limitation | WO-008 (P-033) | `SharedKernel.Persistence.EfCore` | `●` |
| D-03 | Design `AuditSaveChangesInterceptor` contract — `ISaveChangesInterceptor` populating `IHasCreatedAudit` on Added entries and `IHasAudit` on Modified entries; `IUserContext` scoped DI injection pattern; `IClock` injection; `FallbackUserIdentifier` fallback strategy | WO-008 (P-033) | `SharedKernel.Persistence.EfCore` | `●` |
| D-04 | Design soft-delete query filter convention — auto-applies `e => !e.IsDeleted` global query filter for `ISoftDeletable` entities; `IgnoreSoftDeleteFilter()` extension on `IQueryable<T>` for admin/audit paths | WO-008 (P-033) | `SharedKernel.Persistence.EfCore` | `●` |
| D-05 | Design concurrency token convention — auto-applies `.IsRowVersion()` (SQL Server) or `.UseXminAsConcurrencyToken()` (PostgreSQL/Npgsql) based on provider; unknown provider falls back to `.IsConcurrencyToken()` with startup warning | WO-008 (P-033) | `SharedKernel.Persistence.EfCore` | `●` |
| D-06 | Design tenant query filter convention — `AddTenantIsolationFilter()` opt-in extension; global query filter `e => e.TenantId == currentTenantId` for `IHasTenant` entities; `ITenantProvider` sourced from DI; not applied by default | WO-008 (P-033) | `SharedKernel.Persistence.EfCore` | `●` |
| D-07 | Design `IRepository<TAggregate, TId>` and `IReadRepository<TAggregate, TId>` interface contracts — write-side vs read-side separation; no `IQueryable` exposure; specification-driven read surface; generic constraints `where TAggregate : IAggregateRoot<TId> where TId : notnull` | WO-013 (P-066) | `SharedKernel.Persistence.Abstractions` | `●` |
| D-08 | Design `IUnitOfWork`, `IDbConnectionFactory`, and `ISpecificationEvaluator<T>` interface contracts — single-method save boundary; open-connection factory for Dapper; evaluator contract for alternative implementations (e.g., Cosmos DB) | WO-013 (P-066) | `SharedKernel.Persistence.Abstractions` | `●` |
| D-09 | Design `SharedKernelDbContext` abstract base — three-interceptor constructor registration (Audit, SoftDelete, Concurrency; no OutboxInterceptor); `ApplyConfigurationsFromAssembly` in `OnModelCreating`; `SaveChangesAsync` override semantics | WO-013 (P-067) | `SharedKernel.Persistence.EfCore` | `●` |
| D-10 | Design `EntityTypeConfigurationBase<TEntity, TId>` — auto-apply primary key, concurrency token, soft-delete filter, audit columns, tenant column; `base.Configure(builder)` call contract for downstream configurations | WO-013 (P-067) | `SharedKernel.Persistence.EfCore` | `●` |
| D-11 | Design `EfRepository<TAggregate, TId>` and `EfReadRepository<TAggregate, TId>` abstract base classes — write-only vs read-only surface; `ISpecificationEvaluator<T>` composition; `AsNoTracking` application | WO-013 (P-069) | `SharedKernel.Persistence.EfCore` | `●` |
| D-12 | Design `SpecificationEvaluator<T>` evaluation order — strict pipeline: Criteria → Includes → OrderBy/ThenBys → Distinct → AsNoTracking → Skip/Take; paging-last invariant | WO-013 (P-069) | `SharedKernel.Persistence.EfCore` | `●` |
| D-13 | Design `TenantedDbContext` and `ICurrentTenantService` — runtime-captured (not startup-snapshot) global query filter; `TenantedRepository<TAggregate, TId>` with `GetByIdForTenantAsync` bypass method | WO-013 (P-070) | `SharedKernel.Persistence.EfCore` | `●` |
| D-14 | Design `EfCorePersistenceBuilder` fluent DI API — `AddSharedKernelEfCore<TContext>` entry point; `.WithMultiTenancy()` opt-in; `.Build()` registration shape; startup guard for TContext/TenantedDbContext mismatch; no-op `IUserContext` placeholder strategy | WO-013 (P-073) | `SharedKernel.Persistence.EfCore` | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.06.Scaffold -->

> Wire up `.csproj` NuGet references, intra-domain project references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| S-01 | Scaffold `SharedKernel.Persistence.Abstractions.csproj` — `net10.0`, ImplicitUsings, Nullable enabled; project references to `SharedKernel.Primitives` and `SharedKernel.Domain`; zero ORM NuGet dependencies; package metadata (id, description, version `1.0.0`) | WO-013 (P-065) | `SharedKernel.Persistence.Abstractions` | `●` |
| S-02 | Scaffold `SharedKernel.Persistence.EfCore.csproj` — `net10.0`, ImplicitUsings, Nullable enabled; project references to `SharedKernel.Persistence.Abstractions` and `SharedKernel.Domain`; NuGet: `Microsoft.EntityFrameworkCore` 10.x, `Microsoft.EntityFrameworkCore.Relational` 10.x, `Microsoft.Extensions.DependencyInjection.Abstractions`; package metadata | WO-013 (P-065) | `SharedKernel.Persistence.EfCore` | `●` |
| S-03 | Scaffold `SharedKernel.Persistence.Abstractions.Tests.csproj` — `net10.0`; project reference to `SharedKernel.Persistence.Abstractions` and `SharedKernel.Testing`; xUnit, xunit.runner.visualstudio, coverlet.collector packages | WO-013 (P-065) | `SharedKernel.Persistence.Abstractions` | `●` |
| S-04 | Scaffold `SharedKernel.Persistence.EfCore.Tests.csproj` — `net10.0`; project reference to `SharedKernel.Persistence.EfCore` and `SharedKernel.Testing`; xUnit, xunit.runner.visualstudio, coverlet.collector, `Microsoft.EntityFrameworkCore.Sqlite` packages | WO-013 (P-065) | `SharedKernel.Persistence.EfCore` | `●` |
| S-05 | Register all four projects in `Platform.SharedKernel.slnx` under the `06.Persistence` solution folder; verify `dotnet build` passes for both production projects from a clean state | WO-013 (P-065) | `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.EfCore` | `●` |

---

## Phase: Core <!-- phase-key: SK.06.Core -->

> Full implementation of all interfaces, base classes, interceptors, evaluators, type converters, and DI registrations.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| C-01 | Implement `IRepository<TAggregate, TId>` in `Repositories/` — write-only surface (`GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`); generic constraints `where TAggregate : IAggregateRoot<TId> where TId : notnull`; no `IQueryable<TAggregate>` member | WO-013 (P-066) | `SharedKernel.Persistence.Abstractions` | `●` |
| C-02 | Implement `IReadRepository<TAggregate, TId>` in `Repositories/` — read-only surface (`GetByIdAsync`, `GetBySpecAsync`, `ListAsync`, `CountAsync`, `AnyAsync`); all methods accept `ISpecification<TAggregate>`; same generic constraints as C-01 | WO-013 (P-066) | `SharedKernel.Persistence.Abstractions` | `●` |
| C-03 | Implement `IUnitOfWork` in `UnitOfWork/` — single method `SaveChangesAsync(CancellationToken ct) → Task<int>`; no provider-specific concepts | WO-013 (P-066) | `SharedKernel.Persistence.Abstractions` | `●` |
| C-04 | Implement `IDbConnectionFactory` in `Connections/` — single method `CreateConnectionAsync(CancellationToken ct) → Task<IDbConnection>`; returns open connection; caller disposes | WO-013 (P-066) | `SharedKernel.Persistence.Abstractions` | `●` |
| C-05 | Implement `ISpecificationEvaluator<T>` in `Specifications/` — single method `GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec) → IQueryable<T>`; lives in Abstractions for alternative evaluator implementations | WO-013 (P-066) | `SharedKernel.Persistence.Abstractions` | `●` |
| C-06 | Implement `SharedKernelDbContext` abstract base in `Context/` — constructor registers exactly three scoped interceptors (`AuditInterceptor`, `SoftDeleteInterceptor`, `ConcurrencyInterceptor`); `OnModelCreating` calls `modelBuilder.ApplyConfigurationsFromAssembly` for the calling assembly; `SaveChangesAsync` override delegates to interceptor pipeline; no `OutboxInterceptor` registration | WO-013 (P-067) | `SharedKernel.Persistence.EfCore` | `●` |
| C-07 | Implement `EntityTypeConfigurationBase<TEntity, TId>` in `Configurations/` — applies primary key on `TId`; concurrency token (`.IsRowVersion()`) for `IHasConcurrency`; global query filter `e => !e.IsDeleted` for `ISoftDeletable`; owned audit columns (`CreatedBy`, `CreatedOn`) for `IHasCreatedAudit`; additionally `ModifiedBy`, `ModifiedOn` for `IHasAudit`; `TenantId` column + index for `IHasTenant` | WO-013 (P-067) | `SharedKernel.Persistence.EfCore` | `●` |
| C-08 | Implement `StronglyTypedIdValueConverter<TStronglyTypedId, TValue>` sealed class in `Conversions/` — extends `ValueConverter<TStronglyTypedId, TValue>`; uses `implicit operator TValue` for to-provider direction; no `Activator.CreateInstance`, no reflection; companion `ModelConfigurationBuilder` extension for auto-registration across all `IStronglyTypedId<TValue>` types | WO-013 (P-067) | `SharedKernel.Persistence.EfCore` | `●` |
| C-09 | Implement `AuditInterceptor` sealed class in `Interceptors/` — fires `SavingChangesAsync`/`SavingChanges`; sets `CreatedBy`/`CreatedOn` on Added `IHasCreatedAudit` entities; sets `ModifiedBy`/`ModifiedOn` on Modified `IHasAudit` entities; uses `ChangeTracker.Entry(entity).CurrentValues[propertyName]` — never direct property setter; injected `IUserContext` (scoped DI); injected `IClock` | WO-013 (P-068) | `SharedKernel.Persistence.EfCore` | `●` |
| C-10 | Implement `SoftDeleteInterceptor` sealed class in `Interceptors/` — fires `SavingChangesAsync`/`SavingChanges`; converts EF entity state `Deleted → Modified` for `ISoftDeletable` entities; sets `IsDeleted = true`, `DeletedOn`, `DeletedBy` via EF ChangeTracker; non-soft-deletable entities pass through without modification; injected `IUserContext` and `IClock` | WO-013 (P-068) | `SharedKernel.Persistence.EfCore` | `●` |
| C-11 | Implement `ConcurrencyInterceptor` sealed class in `Interceptors/` — fires `SaveChangesFailedAsync`/`SaveChangesFailed`; catches `DbUpdateConcurrencyException` for `IHasConcurrency` entities; rethrows as typed `ConcurrencyException` carrying `Error.Conflict(...)`; non-concurrency exceptions propagate unchanged; no retry logic | WO-013 (P-068) | `SharedKernel.Persistence.EfCore` | `●` |
| C-12 | Implement `EfRepository<TAggregate, TId>` abstract class in `Repositories/` — implements `IRepository<TAggregate, TId>`; backed by `DbContext.Set<TAggregate>()`; implements `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`; never exposes `IQueryable<TAggregate>`; accepts `SharedKernelDbContext` via constructor | WO-013 (P-069) | `SharedKernel.Persistence.EfCore` | `●` |
| C-13 | Implement `EfReadRepository<TAggregate, TId>` abstract class in `Repositories/` — implements `IReadRepository<TAggregate, TId>`; uses `ISpecificationEvaluator<TAggregate>` internally; implements all five read methods; applies `AsNoTracking()` when `spec.AsNoTracking == true`; accepts `SharedKernelDbContext` and `ISpecificationEvaluator<TAggregate>` via constructor | WO-013 (P-069) | `SharedKernel.Persistence.EfCore` | `●` |
| C-14 | Implement `EfUnitOfWork` sealed class in `UnitOfWork/` — implements `IUnitOfWork`; delegates `SaveChangesAsync` to `SharedKernelDbContext.SaveChangesAsync`; no additional logic; sole permitted save boundary | WO-013 (P-069) | `SharedKernel.Persistence.EfCore` | `●` |
| C-15 | Implement `SpecificationEvaluator<T>` sealed class in `Specifications/` — implements `ISpecificationEvaluator<T>`; applies operations in strict order: Criteria → Includes → OrderBy/OrderByDescending → ThenBys (only when primary sort set) → Distinct → AsNoTracking → Skip/Take (paging always last); null Criteria matches all entities | WO-013 (P-069) | `SharedKernel.Persistence.EfCore` | `●` |
| C-16 | Implement `ICurrentTenantService` interface in `MultiTenancy/` — exposes `TenantId` as `Guid?`; defined in `SharedKernel.Persistence.EfCore` (not Abstractions) as it is an EfCore-layer DbContext concern; concrete implementation provided by `13.ServiceDefaults.MultiTenancy` | WO-013 (P-070) | `SharedKernel.Persistence.EfCore` | `●` |
| C-17 | Implement `TenantedDbContext` abstract class in `MultiTenancy/` — extends `SharedKernelDbContext`; overrides `OnModelCreating` to install runtime-captured (not startup-snapshot) global query filter `e => e.TenantId == currentTenantService.TenantId` on all `IHasTenant` entities; accepts `ICurrentTenantService` and `DbContextOptions` via constructor | WO-013 (P-070) | `SharedKernel.Persistence.EfCore` | `●` |
| C-18 | Implement `TenantedRepository<TAggregate, TId>` abstract class in `MultiTenancy/` — extends `EfRepository<TAggregate, TId>`; adds `GetByIdForTenantAsync(TId id, Guid tenantId, CancellationToken ct) → Task<TAggregate?>` for cross-tenant admin operations; standard `GetByIdAsync` routes through global tenant filter automatically | WO-013 (P-070) | `SharedKernel.Persistence.EfCore` | `●` |
| C-19 | Implement `EfCorePersistenceBuilder` fluent builder in `Extensions/` — `AddSharedKernelEfCore<TContext>(IServiceCollection, Action<DbContextOptionsBuilder>)` returns builder; `.WithMultiTenancy()` registers no-op `ICurrentTenantService` placeholder; `.Build()` registers `TContext` as `DbContext`, `IUnitOfWork → EfUnitOfWork`, `ISpecificationEvaluator<T> → SpecificationEvaluator<T>` (singleton), and all three interceptors as scoped | WO-013 (P-073) | `SharedKernel.Persistence.EfCore` | `●` |
| C-20 | Implement startup guard in `EfCorePersistenceBuilder.Build()` — throws `InvalidOperationException` at startup if `.WithMultiTenancy()` was called but `TContext` does not extend `TenantedDbContext`; error message must be actionable | WO-013 (P-073) | `SharedKernel.Persistence.EfCore` | `●` |
| C-21 | Implement no-op `IUserContext` placeholder registration in `EfCorePersistenceBuilder.Build()` — registers scoped no-op returning `"system"` only when no `IUserContext` is already registered; consuming services override by registering their own implementation; no outbox, Dapper, or PostgreSQL wiring in this builder | WO-013 (P-073) | `SharedKernel.Persistence.EfCore` | `●` |

---

## Phase: Tests <!-- phase-key: SK.06.Tests -->

> Unit and integration test coverage for all packages. Integration tests use SQLite (EfCore) or Testcontainers PostgreSQL (PostgreSQL, Dapper). No mocked database connections.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| T-01 | Abstractions contract shape tests — verify all expected methods present on `IRepository`, `IReadRepository`, `IUnitOfWork`, `IDbConnectionFactory`, `ISpecificationEvaluator`; assert no outbox types (`OutboxMessage`, `IOutboxWriter`) exist in this package | WO-013 (P-074) | `SharedKernel.Persistence.Abstractions` | `●` |
| T-02 | `AuditInterceptor` SQLite integration tests — Added entity: `CreatedBy`/`CreatedOn` set; Modified entity: `ModifiedBy`/`ModifiedOn` set; Deleted non-soft-deletable entity: no audit mutation; all field writes via EF ChangeTracker verified (not direct property) | WO-013 (P-074) | `SharedKernel.Persistence.EfCore` | `●` |
| T-03 | `SoftDeleteInterceptor` SQLite integration tests — Deleted `ISoftDeletable` entity: state changed to Modified; `IsDeleted = true`; `DeletedOn` and `DeletedBy` set; soft-deleted entity invisible via global filter after reload; non-soft-deletable entity passes through untouched | WO-013 (P-074) | `SharedKernel.Persistence.EfCore` | `●` |
| T-04 | `ConcurrencyInterceptor` tests — `DbUpdateConcurrencyException` on `IHasConcurrency` entity rethrown as `ConcurrencyException` with `Error.Conflict` payload; non-concurrency exceptions propagate unchanged without wrapping | WO-013 (P-074) | `SharedKernel.Persistence.EfCore` | `●` |
| T-05 | `SpecificationEvaluator<T>` unit tests — all six operations (criteria, includes, orderby, thenbys, distinct, asnotracking) verified independently; paging-last order proven (Skip/Take appear after OrderBy in generated query expression tree); null Criteria matches all entities; ThenBys ignored when no primary sort | WO-013 (P-074) | `SharedKernel.Persistence.EfCore` | `●` |
| T-06 | `EfRepository`/`EfReadRepository` SQLite round-trip tests — write entity via `EfRepository.AddAsync`, read back via `EfReadRepository.GetBySpecAsync`; `ListAsync` with `PagedSpecification` returns correct page; `CountAsync` and `AnyAsync` return correct values | WO-013 (P-074) | `SharedKernel.Persistence.EfCore` | `●` |
| T-07 | `StronglyTypedIdValueConverter` round-trip test — write entity with strongly-typed ID via EF Core, read back, assert `TId` value equals original; uses SQLite provider; no reflection path exercised | WO-013 (P-074) | `SharedKernel.Persistence.EfCore` | `●` |
| T-08 | `TenantedDbContext` isolation tests — global query filter isolates records by `TenantId`; query with no current tenant (`TenantId == null`) returns zero rows; `GetByIdForTenantAsync` bypasses filter and returns entity by explicit tenant scope | WO-013 (P-074) | `SharedKernel.Persistence.EfCore` | `●` |
| T-09 | `EfCorePersistenceBuilder` smoke tests — `.Build()` with `.WithMultiTenancy()` on a non-`TenantedDbContext` throws `InvalidOperationException` at startup; no real database required for this test | WO-013 (P-074) | `SharedKernel.Persistence.EfCore` | `●` |
| T-10 | EF Core domain primitive convention tests (P-033) — `ValueObjectOwnershipConvention` auto-applies `.OwnsOne()` for `IValueObject` properties; soft-delete convention auto-applies global filter; concurrency token convention applies correct strategy per provider; tenant filter convention is opt-in only | WO-008 (P-033) | `SharedKernel.Persistence.EfCore` | `●` |

---

## Phase: Docs <!-- phase-key: SK.06.Docs -->

> XML doc comments on all public APIs, README with usage examples.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| DO-01 | XML doc comments on all public types in `SharedKernel.Persistence.Abstractions` — `IRepository`, `IReadRepository`, `IUnitOfWork`, `IDbConnectionFactory`, `ISpecificationEvaluator`; include remarks on `IQueryable` prohibition and save-boundary rule | WO-013 (P-066) | `SharedKernel.Persistence.Abstractions` | `●` |
| DO-02 | XML doc comments on `SharedKernelDbContext`, `EntityTypeConfigurationBase`, `StronglyTypedIdValueConverter`, and `EfUnitOfWork`; include remarks on interceptor registration, `base.Configure(builder)` contract, AOT safety | WO-013 (P-067) | `SharedKernel.Persistence.EfCore` | `●` |
| DO-03 | XML doc comments on all three interceptors (`AuditInterceptor`, `SoftDeleteInterceptor`, `ConcurrencyInterceptor`), `EfRepository`, `EfReadRepository`, `SpecificationEvaluator`, `TenantedDbContext`, `TenantedRepository`; include remarks on ChangeTracker-only mutation, soft-delete state transition, no-retry concurrency policy | WO-013 (P-068, P-069, P-070) | `SharedKernel.Persistence.EfCore` | `●` |
| DO-04 | XML doc comments on `EfCorePersistenceBuilder` and all DI extension methods; `ValueObjectOwnershipConvention` doc must explicitly document `OwnsMany` limitation; `IgnoreSoftDeleteFilter()` extension doc must warn about admin-panel-only usage | WO-008 / WO-013 (P-033, P-073) | `SharedKernel.Persistence.EfCore` | `●` |

---

## Phase: Published <!-- phase-key: SK.06.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| P-01 | Finalize NuGet packaging metadata for `SharedKernel.Persistence.Abstractions` — verify `PackageId`, `Description`, `Version`, `Authors`, `RepositoryUrl`; run `dotnet pack`; verify package contains only interface types and no ORM binaries | WO-013 | `SharedKernel.Persistence.Abstractions` | `●` |
| P-02 | Finalize NuGet packaging metadata for `SharedKernel.Persistence.EfCore` — verify `PackageId`, `Description`, `Version`, `Authors`, `RepositoryUrl`; run `dotnet pack`; verify package dependencies list `Microsoft.EntityFrameworkCore` but not `Npgsql` | WO-013 | `SharedKernel.Persistence.EfCore` | `●` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | --- | --- | --- | --- |
| `SK.06.Design` | Design | 14 | 14 | 0 | `●` |
| `SK.06.Scaffold` | Scaffold | 5 | 5 | 0 | `●` |
| `SK.06.Core` | Core | 21 | 21 | 0 | `●` |
| `SK.06.Tests` | Tests | 10 | 10 | 0 | `●` |
| `SK.06.Docs` | Docs | 4 | 4 | 0 | `●` |
| `SK.06.Published` | Published | 2 | 2 | 0 | `●` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-01] Sub state-map initialized — phase key registry, 6 phases scaffolded at ○, no tasks yet
- [2026-06-01] Added 56 tasks across all 6 phases — WO-008 (P-033) design+test tasks D-01..D-06, T-10; WO-013 (P-065..P-070, P-073, P-074) design D-07..D-14, scaffold S-01..S-05, core C-01..C-21, tests T-01..T-09, docs DO-01..DO-04, published P-01..P-02; outbox types removed from Abstractions and EfCore scope (MassTransit EF outbox is 07.Messaging boundary)
- [2026-06-01] SK.06.Design complete — all 14 design tasks D-01..D-14 marked ●; all interface shapes, interceptor contracts, evaluator pipeline, DI builder signatures, and multi-tenancy patterns confirmed against CLAUDE.md
- [2026-06-01] SK.06.Design → ● (14/14) — promotion condition met; propagating to root state-map (state-map-phase)
- [2026-06-01] S-01..S-05 → ● in SK.06.Scaffold — csproj files, test projects, solution registration, build verified green (state-map-phase)
- [2026-06-01] C-01..C-21 → ● in SK.06.Core — all interfaces, base classes, interceptors, evaluator, converters, multi-tenancy, DI builder implemented; 62 tests green (state-map-phase)
- [2026-06-01] T-01..T-10 → ● in SK.06.Tests — 71 tests green across Abstractions (20) and EfCore (51); ValueObjectOwnershipConvention implemented (state-map-phase)
- [2026-06-01] DO-01..DO-04 → ● in SK.06.Docs — XML doc comments on all public APIs; IgnoreSoftDeleteFilter() extension added with admin-only warning (state-map-phase)
- [2026-06-01] P-01, P-02 → ● in SK.06.Published — NuGet metadata verified; dotnet pack succeeded; Abstractions has no ORM deps; EfCore lists EF not Npgsql (state-map-phase)
