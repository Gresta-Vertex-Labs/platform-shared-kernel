---
name: project_efcorepersistencebuilder_extensions
description: EfCorePersistenceBuilder new fluent methods — WithDbContextFactory, AddInterceptor, WithCompiledModel (P-106)
metadata:
  type: project
---

**P-106 Caps 2, 3, 4 (WO-018, 2026-06-03):** Three new optional fluent methods added to `EfCorePersistenceBuilder<TContext>`.

**`.WithDbContextFactory()`**
- Calls `services.AddDbContextFactory<TContext>(configureDb)` in `Build()`.
- Required for background services, hosted workers, Hangfire jobs, Temporal activities — any code that runs outside an HTTP request scope and cannot consume a scoped `DbContext`.
- Factory-created contexts receive `NoOpUserContext` (UserId = `Guid.Empty`) → audit fields default to `"system"` unless a singleton `IUserContext` is registered.
- Anti-pattern to avoid: singleton scoped context shared across background threads (EF Core is not thread-safe per context instance).

**`.AddInterceptor<TInterceptor>()`**
- Registers `TInterceptor` as scoped `ISaveChangesInterceptor`.
- Multiple calls accumulate; all fire after the platform three (Audit, SoftDelete, Concurrency) in registration order.
- Platform interceptors always fire first — this ordering is non-negotiable.
- `SharedKernelDbContext` constructor accepts `IEnumerable<ISaveChangesInterceptor> additionalInterceptors` (empty by default — backward compatible).

**`.WithCompiledModel(IModel compiledModel)`**
- Wraps `configureDb` to also call `optionsBuilder.UseModel(compiledModel)`.
- Produced by `dotnet ef dbcontext optimize`.
- When used: `ValueObjectOwnershipBuilder.Apply` and runtime model-building scans do NOT run — all mappings must be in the compiled model.
- Pure pass-through: builder does not validate the model.

**How to apply:** All three methods are optional. Omit them for services that do not need background DbContext, custom interceptors, or compiled models. All return `EfCorePersistenceBuilder<TContext>` for fluent chaining.
