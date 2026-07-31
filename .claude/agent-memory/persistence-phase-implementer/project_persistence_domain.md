---
name: project-persistence-domain
description: Key patterns, version pins, gotchas, and decisions established during 06.Persistence implementation sessions
metadata:
  type: project
---

## Phase completion status (as of 2026-07-31, WO-051 Core phase closed)
- SK.06.Design: complete (84/84 tasks) — D-65..D-84 (WO-051 batches 1+2) done 2026-07-30
- SK.06.Core: complete (128/128 tasks) — C-98..C-128 verified/closed 2026-07-31 (see below)
- SK.06.Tests / Docs / Published: WO-051 added MORE tasks to these phases that are STILL
  PENDING (Tests 60/97, Docs 38/52, Published 4/8 as of 2026-07-31) — a FUTURE session must
  continue there. Do not assume "Published" in root state-map means every sub-phase task is
  done — this domain repeatedly gets NEW WO's adding tasks to already-"complete" phase-key
  sections after reaching Published once (WO-013), and the root Domain Summary Board's
  "Current Phase" column just reflects whichever phase-key was MOST RECENTLY closed, not a
  monotonic milestone — it can and does go back to "Design"/"Core" when a new batch of tasks
  completes, even after the domain previously showed "Published".
- ALWAYS re-read 06.Persistence/state-map.md's own Overall Progress table before assuming a
  phase is done — don't trust the root's one-line domain summary alone.

## CRITICAL: a "Core phase pending" state-map does not mean the code is unwritten (found 2026-07-31)
A prior session had fully implemented ALL of C-98..C-128 (WO-051 batches 1+2) — every file,
every design nuance, matching `06.Persistence/CLAUDE.md`'s target-state docs exactly — and
had even run the full test suite (376/376 green, recorded in a CLAUDE.md changelog entry
dated 2026-07-30) — but NEVER called `state-map-phase` to flip the Core task rows, so
`state-map.md` still showed `Core 97/128`. Lesson: when a phase's task list looks
"suspiciously already implemented," `git log --oneline -- 06.Persistence/` FIRST — commit
messages here are unusually descriptive (e.g. "feat(persistence): xmin concurrency token
convention for postgresql") and will immediately reveal whether the work already landed.
Don't assume; read every file the phase's tasks reference and diff against the CLAUDE.md
spec before writing anything new. In this case the actual work was: verify all 31 files,
run the 4 test projects (Abstractions 49, EfCore 294, PostgreSQL 18 Testcontainers, Dapper
15 Testcontainers = 376 total), fix one real defect found along the way (below), then just
close the state-map loop. Zero new production code was needed for C-98..C-128 itself.

## Genuine defect found during Core-phase verification: EfPersistenceTransaction duplication
`EfCorePersistenceEfCore/UnitOfWork/EfTransactionalUnitOfWork.cs`'s P-320
`BeginTransactionAsync`/`ExecuteInTransactionAsync` work had drifted into constructing a
SECOND, undocumented class `EfTransactionalPersistenceTransaction` (defined inline at the
bottom of that same file, with post-commit dispatch) instead of extending the already-shipped,
CLAUDE.md-documented `EfPersistenceTransaction` adapter
(`SharedKernel.Persistence.EfCore/UnitOfWork/EfPersistenceTransaction.cs`) — which was left in
place as dead, non-dispatching code with a doc comment saying "dispatch is NOT performed here."
`06.Persistence/CLAUDE.md`'s own prose already named `EfPersistenceTransaction` as the return
type of `BeginTransactionAsync` and referenced `EfPersistenceTransaction.CommitAsync` by name —
so the shipped code had silently diverged from its own correct, pre-existing documentation.
Fix: consolidate into the single `EfPersistenceTransaction` class (constructor takes the owning
`EfTransactionalUnitOfWork`, `CommitAsync` calls its `DispatchAndClearEventsAsync`), delete the
duplicate, update the one call site and one stale `<see cref>`. No CLAUDE.md content changed —
the doc was already right; only the code needed to catch up. **General lesson**: when a phase's
implementation is unusually large/multi-part (transient-fault retry + explicit transactions +
dispatch-deferral, in this case), grep for every class name CLAUDE.md documents and confirm each
is actually the one constructed at every call site — don't just confirm the documented class
*exists* somewhere in the file tree.

## Bash/perl pitfall: DO NOT use perl -i -pe line-anchored regex on this repo's state-map.md files
Attempted `perl -i -pe 's/^(\| C-(9[89]|...)\|.*)\| \`○\` \|\s*$/$1| \`●\` |/'` to bulk-flip 31
task rows from `○` to `●` in one shot. Result: **31 originally-separate lines got silently
merged into a single physical line** (file line count dropped from 587 to 556, confirmed via
`wc -l` before/after) — root cause never fully diagnosed (file is plain LF-terminated UTF-8
per `file` command, so it isn't a CRLF issue), but the corruption was real and would have gone
unnoticed without an explicit before/after `wc -l` check. **Always verify line count is
unchanged (`wc -l` before and after) whenever using any `sed`/`perl -i` regex substitution on
these state-map files** — or better, just use the `Edit` tool with the full row text as
`old_string` (verbose but zero ambiguity, guaranteed to only touch the intended line since the
match is an exact, non-regex string). Restored from a manual `cp` backup in the scratchpad dir
and redid all 31 flips via 31 individual `Edit` calls instead — safe, verified via `wc -l`
staying at 587 throughout.

## CRITICAL: EF Core DbContext pooling + OnConfiguring interceptor wiring (discovered WO-051/P-322)
`DbContextOptions.IsFrozen` is a PUBLIC property (confirmed via direct probe against the real
package, not assumed). `AddPooledDbContextFactory<TContext>(...)` FREEZES the options built by its
own `optionsAction` BEFORE any pooled `TContext` instance is ever constructed — `Options.IsFrozen`
is `true` inside `OnConfiguring` for EVERY pooled instance, including the very first. If
`OnConfiguring` tries to mutate the builder anyway (e.g. `AddInterceptors(...)`, which this
platform's `SharedKernelDbContext.OnConfiguring` unconditionally did before this fix), it does NOT
throw immediately at the mutation call site — it silently "succeeds" and only throws LATER, the
first time the context's internal services are actually built (`SaveChangesAsync`,
`EnsureCreatedAsync`, etc.), with `InvalidOperationException: 'OnConfiguring' cannot be used to
modify DbContextOptions when DbContext pooling is enabled`. This was caught ONLY by writing a real
end-to-end DI test (`AddSharedKernelEfCore<T>(...).WithDbContextPooling().Build()` +
`BuildServiceProvider()` + `CreateScope()` + `EnsureCreatedAsync()`), never by unit-testing the
builder's registration list alone.
**Fix pattern (reusable for any future domain adding pooled-DbContext support):**
1. Guard the base class's `OnConfiguring` mutation: `if (!optionsBuilder.Options.IsFrozen) { ...
   existing AddInterceptors/other mutation logic... }` — a no-op under pooling is correct here.
2. Pre-wire whatever `OnConfiguring` would have added, INSIDE the pool's own `optionsAction`,
   BEFORE the freeze — use the `AddPooledDbContextFactory<TContext>((sp, options) => {...},
   poolSize)` two-arg overload (NOT the simple `Action<DbContextOptionsBuilder>` overload) to get
   `IServiceProvider sp` access for resolving singleton dependencies (e.g. `IClock`,
   `IOptions<T>`) needed to construct the interceptors. A throwaway placeholder value is fine for
   any per-request-scoped constructor parameter (e.g. `IUserContext`) PROVIDED the interceptor
   itself has ALREADY been redesigned to read live per-request state off the current `DbContext`
   instance (`eventData.Context`) rather than its own captured field — otherwise the placeholder
   value would be baked in for the pool's entire lifetime, which is the exact hazard pooling
   support is supposed to fix in the first place.
3. `sp` inside this `optionsAction` is NOT a real per-request scope (EF Core resolves pool-miss
   construction dependencies from its own internal scope) — resolving genuinely scoped consumer
   services from it (e.g. a `.AddInterceptor<T>()` custom interceptor needing its own scoped
   dependency) will throw a normal "cannot resolve scoped service from root provider" DI error.
   Documented as an accepted, out-of-scope edge case rather than solved — only the platform's own
   three interceptors are guaranteed pool-safe by this pattern.

## Test-suite-wide EF Core gotcha: ManyServiceProvidersCreatedWarning (recurring, not "fixed once")
EF Core's internal `ServiceProviderCache` throws `InvalidOperationException` (wrapping
`ManyServiceProvidersCreatedWarning`) once more than ~20 DISTINCT internal service providers have
been built across the ENTIRE TEST PROCESS (not per test class) — and the exception surfaces on
whichever DbContext construction happens to be the Nth (>20) in that PARTICULAR run's execution
order, which varies between runs because xUnit parallelizes test classes. This makes it look like a
RANDOM, unrelated test is flaky, when the real cause is often a DIFFERENT file that never called
`.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))` on its own
`DbContextOptionsBuilder`. **Every single `DbContextOptionsBuilder`/`options.UseSqlite(...)` call
site across the WHOLE test project needs this suppression — not just newly-added ones.** Adding new
distinct DbContextOptions configurations (even fully-suppressed ones) still consumes cache slots and
can push the total over the threshold, causing a PRE-EXISTING, previously-dormant, unsuppressed call
site elsewhere in the suite to start failing intermittently. When this happens: `grep -rl
"UseSqlite(" *.Tests/ --include="*.cs"` then check each file for `ConfigureWarnings` — any file
missing it is a latent time bomb. Found and fixed 10 such files in `SharedKernel.Persistence.EfCore.Tests`
during WO-051 (including the most-reused fixture, `TestDbContextFactory.cs` — fixing that ALONE
would not have been sufficient, since structurally-different configurations across OTHER files each
contribute their own distinct cache entries).

## Cross-test ActivitySource contamination for exact-count assertions (discovered WO-051/P-319)
A shared static `ActivitySource` + xUnit's default cross-CLASS parallelism means an `ActivityListener`
filtering only on `OperationName` can observe a concurrently-running SIBLING test class's span if
both drive the same traced operation (e.g. two different test files both calling
`EfReadRepository.StreamAsync` on the same aggregate type). `02.Caching`'s own precedent
(`OtelTracingTests`/`OtelMetricsTests`) solves this with existence-style (`>= 1`) tolerance, which
works when the assertion is "a span with these characteristics exists." It does NOT work when the
assertion must be an EXACT count (e.g. proving `StreamAsync` produces exactly ONE span for a full
enumeration, not one per item — the actual invariant under test). Fix: start a local root `Activity`
via the plain `System.Diagnostics.Activity` API — `using var rootActivity = new
Activity("Test.Root").Start();` (no `ActivitySource`/listener registration needed for this alone to
work — `Activity.Start()` sets `Activity.Current` by itself) — BEFORE registering the
`ActivityListener`. Since `ActivitySource.StartActivity(name, kind)` with no explicit parent
defaults to `Activity.Current`, every span the code-under-test produces becomes a direct child of
`rootActivity`. Filter observed activities to `activity.ParentId == rootActivity.Id` to isolate
"spans this test's own call produced" from "spans any concurrently-running sibling test produced."

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
- CORRECTED (was stale): `SharedKernel.Persistence.EfCore` DOES take a direct `ProjectReference` to
  `SharedKernel.Security.Abstractions` (P-078, a deliberate, documented layering exception — it's a
  zero-dependency interface library) for `IUserContext`/`ITenantProvider`. No OTHER `12.Security.*`
  package may ever be referenced from this domain.
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
