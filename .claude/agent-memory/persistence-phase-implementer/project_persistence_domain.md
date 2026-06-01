---
name: project-persistence-domain
description: Key patterns, version pins, gotchas, and decisions established during 06.Persistence implementation sessions
metadata:
  type: project
---

## Phase completion status
- SK.06.Design: complete (14/14 tasks)
- SK.06.Scaffold: complete (5/5 tasks)
- SK.06.Core: not started
- SK.06.Tests: not started

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
