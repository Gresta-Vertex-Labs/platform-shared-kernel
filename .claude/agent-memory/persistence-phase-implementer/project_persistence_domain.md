---
name: project-persistence-domain
description: Key patterns, version pins, gotchas, and decisions established during 06.Persistence implementation sessions
metadata:
  type: project
---

## Phase completion status (as of 2026-06-16)
- SK.06.Design: complete (58/58 tasks)
- SK.06.Scaffold: complete (12/12 tasks)
- SK.06.Core: complete (92/92 tasks) — C-82..C-92 (WO-024) done 2026-06-16
- SK.06.Tests: complete (54/54 tasks) — T-40..T-54 (WO-019+WO-024) done 2026-06-16
- SK.06.Docs: complete (36/36 tasks) — DO-29..DO-36 (WO-019+WO-024) done 2026-06-16
- SK.06.Published: complete (4/4) — root state-map already shows domain 06 at Published/●
- ALL SUB-MAP PHASES NOW COMPLETE. Root state-map row for 06.Persistence is at Published(●).
  No further work remains in this domain unless a new phase is added.

## EF Core 10.0.5 API gotchas (verified by compiling against real package, 2026-06-15)
These corrected two designs originally written by persistence-arch-planner (WO-024) that referenced
EF Core 5-8 era APIs which DO NOT EXIST in EF Core 10.0.5:

1. **`DbContext.Set(Type entityType)` non-generic overload does NOT exist.** Only `Set<TEntity>()` and
   `Set<TEntity>(string name)` (both generic). Cannot use `typeof(DbContext).GetMethods()...MakeGenericMethod(clrType).Invoke(...)`
   as a "the non-generic overload" claim — there is no non-generic overload to begin with (this was
   actually still reflection, just disguised). Reflection-free alternative: closed-generic per-entity-type
   processor classes (`EncryptedEntityBatchProcessor<TEntity>`) registered in a `Dictionary<Type, IInterface>`
   built ONCE at startup via `Activator.CreateInstance(typeof(Processor<>).MakeGenericType(clrType))` —
   this startup-time exception is the same class as `ValueObjectOwnershipBuilder`'s model-scan exception.

2. **`EntityFrameworkQueryableExtensions.ToListAsync` has no non-generic `IQueryable` overload.**
   Only `ToListAsync<TSource>(IQueryable<TSource>, CancellationToken)`. Same implication as above —
   must operate on a closed generic `IQueryable<TEntity>`, not `IQueryable` / `IQueryable<object>`.

3. **`Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<T>` no longer exists** (was EF Core 5-8 API).
   `IQueryable<T>.ExecuteUpdate`/`ExecuteUpdateAsync` now take `Action<UpdateSettersBuilder<TSource>>`
   (a delegate, NOT `Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>>`). Usage:
   `query.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Prop, value), ct)`.
   `UpdateSettersBuilder<T>` lives in `Microsoft.EntityFrameworkCore.Query` — this is an EF Core type,
   so any interface exposing it (e.g. `IBulkMutationRepository<TAggregate,TId>.ExecuteUpdateAsync`) must
   live in `SharedKernel.Persistence.EfCore`, never `.Abstractions`.

4. **Confirmed working (no corrections needed):**
   - `((IInfrastructure<IServiceProvider>)dbContext).Instance` — resolves scoped `IServiceProvider`
     (from `Microsoft.EntityFrameworkCore.Infrastructure`)
   - `IQueryable<T>.AsNoTracking().AsAsyncEnumerable()` — streaming reads
   - `context.Database.CanConnectAsync(ct)`, `.ProviderName`, `.MigrateAsync(ct)`
   - `IQueryable<T>.ExecuteDeleteAsync(ct)` (no setter param needed, unaffected by SetPropertyCalls removal)

When implementing C-82..C-92 (Core phase), follow the corrected designs documented in
`06.Persistence/CLAUDE.md` under "EF Core 10 API correction (P-147...)" and the corrected
`IBulkMutationRepository` signature (P-148) — do not revert to the original WO-024 spec text.

## Confirmed package versions (as of Scaffold phase)
- `Microsoft.EntityFrameworkCore` 10.0.5
- `Microsoft.EntityFrameworkCore.Relational` 10.0.5
- `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.5 — must match EFCore transitive; pinning to 10.0.4 triggers NU1605 downgrade error
- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.5 (EfCore tests only)
- `xunit` 2.9.3
- `xunit.runner.visualstudio` 2.8.2
- `Microsoft.NET.Test.Sdk` 17.13.0
- `coverlet.collector` 6.0.4
- `FluentAssertions` 8.4.0
- `NSubstitute` 5.3.0 (EfCore tests only)

## Critical gotcha: IEntity<TId> is a marker interface
`IEntity<TId>` in `SharedKernel.Domain` has NO `Id` property — it is a zero-member marker interface.
`Id` is declared on `Entity<TId>` (the abstract base class).
In EF Core configurations, use the string literal `"Id"` for PK configuration, NOT `nameof(IEntity<TId>.Id)`.

**Why:** `nameof(IEntity<TId>.Id)` does not compile because `IEntity<TId>` has no `Id` member.

## Required: nested test folder exclusion in production csproj
Every production `.csproj` with a nested `*.Tests` subfolder must include:
```xml
<ItemGroup>
  <Compile Remove="*.Tests\**" />
  <EmbeddedResource Remove="*.Tests\**" />
  <None Remove="*.Tests\**" />
</ItemGroup>
```
Without this, the .NET SDK globs pick up test `.cs` files during production build → compile errors.

## Required: GlobalUsings.cs in every test project
`ImplicitUsings` does NOT auto-import xUnit attributes. Every test project needs:
```csharp
// GlobalUsings.cs
global using Xunit;
```

## Domain architecture key facts
- No outbox types anywhere in 06.Persistence — MassTransit's UseEntityFrameworkOutbox owns that at 07.Messaging
- SharedKernelDbContext registers exactly 3 interceptors: AuditInterceptor, SoftDeleteInterceptor, ConcurrencyInterceptor
- ICurrentTenantService is defined in SharedKernel.Persistence.EfCore (not Abstractions)
- IUserContext is NOT referenced via project reference — injected via DI; no reference to 12.Security packages
- EfCorePersistenceBuilder is the sole DI entry point for EfCore wiring
- Paging (Skip/Take) is ALWAYS the last operation in SpecificationEvaluator pipeline
- AuditInterceptor and SoftDeleteInterceptor must use ChangeTracker.Entry(entity).CurrentValues[name] — never direct property setters

## Solution file
All 6 persistence projects registered in Platform.SharedKernel.slnx under /06.Persistence/ folder:
- SharedKernel.Persistence.Abstractions
- SharedKernel.Persistence.Abstractions.Tests  (added in Scaffold phase)
- SharedKernel.Persistence.Dapper
- SharedKernel.Persistence.Dapper.Tests
- SharedKernel.Persistence.EfCore
- SharedKernel.Persistence.EfCore.Tests
- SharedKernel.Persistence.PostgreSQL
- SharedKernel.Persistence.PostgreSQL.Tests
