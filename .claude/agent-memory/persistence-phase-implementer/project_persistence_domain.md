---
name: project-persistence-domain
description: Key patterns, version pins, gotchas, and decisions established during 06.Persistence implementation sessions
metadata:
  type: project
---

## Phase completion status (as of 2026-07-31, WO-051 Docs phase closed)
- SK.06.Design: complete (84/84 tasks) — D-65..D-84 (WO-051 batches 1+2) done 2026-07-30
- SK.06.Core: complete (128/128 tasks) — C-98..C-128 verified/closed 2026-07-31
- SK.06.Tests: complete (97/97 tasks) — T-61..T-97 verified/closed 2026-07-31
- SK.06.Docs: complete (52/52 tasks) — DO-39..DO-52 verified/closed 2026-07-31 (see below)
- SK.06.Published: STILL PENDING (4/8 as of 2026-07-31) — P-05..P-08 (WO-051/P-324: wire
  `PackageReadmeFile`/`<None Include="README.md" Pack="true" PackagePath="\" />` into all four
  `.csproj` files so `dotnet pack` picks up the already-written READMEs; P-01..P-04 were already
  ● since WO-013). A FUTURE session must continue there — this is the LAST open phase for WO-051.
  Do not assume "Published" in root state-map means every sub-phase task is done — this domain
  repeatedly gets NEW WO's adding tasks to already-"complete" phase-key sections after
  reaching Published once (WO-013), and the root Domain Summary Board's "Current Phase"
  column just reflects whichever phase-key was MOST RECENTLY closed, not a monotonic
  milestone — it can and does go back to "Design"/"Core"/"Tests"/"Docs" when a new batch of
  tasks completes, even after the domain previously showed "Published".
- ALWAYS re-read 06.Persistence/state-map.md's own Overall Progress table before assuming a
  phase is done — don't trust the root's one-line domain summary alone.

## Fourth confirmed instance: Docs-phase "○ Pending" task rows whose content already exists (2026-07-31)
Closing DO-39..DO-52 (14 rows, all `○`): 12 of 14 were ALREADY fully correct — both the XML docs
on the real `.cs` source AND the corresponding `06.Persistence/CLAUDE.md` prose sections — from a
prior unclosed session (the arch-planner's WO-051 design pass had already written CLAUDE.md to
target-state, and a prior implementer session had already written matching XML docs but never
flipped the state-map). Only 2 of 14 tasks had genuine gaps, both subtle "cross-reference/table
completeness" gaps rather than missing content outright:
- DO-43: `SpecificationEvaluator.GetQuery`/`GetProjectedQuery` had no `<remarks>` on the METHOD
  itself cross-referencing `03.Domain`'s `ISpecification<T>.AsSplitQuery` Cartesian-product
  rationale — only the CLASS-level summary listed "AsSplitQuery" as a numbered pipeline step, with
  no explanation of why. Read `03.Domain/SharedKernel.Domain/Specifications/ISpecification.cs`'s own
  `AsSplitQuery` XML doc directly to get the exact rationale text before writing the cross-reference
  — never paraphrase a cross-domain rationale from memory.
- DO-44: `06.Persistence/CLAUDE.md`'s Observability "Traced operations" table listed `EfRepository`'s
  traced methods as only `AddAsync/UpdateAsync/DeleteAsync/*RangeAsync` — but the REAL source
  (`EfRepository.cs`) also traces `GetBySpecAsync` (the write-side tracked-fetch method) via the same
  `RepositoryTracing.ExecuteTracedAsync` wrapper; this had been omitted from the table since the
  method's own introduction. Also undocumented: `ExecuteUpdateAsync`/`ExecuteDeleteAsync`
  (`IBulkMutationRepository`) and `GetByIdsChunkedAsync` are NOT directly traced (bulk mutations
  bypass tracing consistent with their documented bypass of interceptors/domain events;
  `GetByIdsChunkedAsync` only produces indirect spans via its underlying `GetByIdsAsync` calls).
  **Lesson: when a "traced operations" or "covered surface" table exists in CLAUDE.md, always grep
  the actual source for the tracing/coverage call site (`RepositoryTracing.ExecuteTracedAsync` in
  this case) across EVERY method in the class, not just the ones the table already lists — tables
  like this drift silently when a method is added/traced without updating the enumeration.**

All four package `README.md` files (DO-50) already existed, fully written, matching the
`08.Storage`/`17.Workflows` quality bar (quick-start DI example + surface overview + link back to
CLAUDE.md) — no changes needed. Verified via `dotnet build` (0 errors, all warnings pre-existing/
unrelated) and `dotnet test .../SharedKernel.Persistence.EfCore.Tests/` (314/314) since only EfCore
source was touched (SpecificationEvaluator.cs, EfRepository.cs, EfReadRepository.cs — doc-only
edits). PostgreSQL/Dapper/Abstractions Testcontainers suites were not re-run since nothing there
changed this session — this is the correct call per the phase-implementer's own "run only test
projects with new/modified tests" rule, not a shortcut.

## Recurring pattern: "○ Pending" state-map task rows whose test code already exists (found AGAIN 2026-07-31, Tests phase)
Third confirmed instance of this pattern (see the Core-phase entry below for the first). When
closing SK.06.Tests's T-61..T-97 (37 rows, all `○`), 24 of them ALREADY had matching,
production-quality test files/methods on disk — written in a prior unclosed session, never
state-map-flipped. Before writing ANY new test for a `○` task, grep/Read the actual test
project directories first (file names/namespaces are usually self-describing, e.g.
`ConcurrencyIntegrationTests.cs`, `KeysetPaginationTests.cs`, `TransientFaultRetryIntegrationTests.cs`
already existed and covered T-61/T-63/T-65/T-67/T-69/T-73/T-75/T-76/T-80/T-81/T-83..T-91/T-95/T-96
verbatim). Only 9 of the 37 tasks were genuine gaps (T-62, T-64, T-66/T-68 additions, T-70,
T-72/T-74, T-77/T-78, T-79/T-82, T-92, T-93); 4 more (T-71/T-89/T-94/T-97) were pure
regression-proof tasks closed by the full green test run alone. Always run the FULL existing
test suite before writing anything — build the picture of what's covered from real file
contents, not from state-map `○` symbols alone.

## CRITICAL test-writing gotcha: SQLite AND Npgsql route store-generated-value writes through ExecuteReader, not ExecuteNonQuery (found 2026-07-31, T-79/T-93)
Any INSERT/UPDATE needing a `RETURNING` clause to read back a DB-generated value — e.g. any
`IHasConcurrency`/`xmin`-bound `RowVersion` entity (PostgreSQL), and empirically EVERY plain
SQLite INSERT in this codebase's fixtures — is executed via
`DbCommandInterceptor.ReaderExecuting(Async)`, never `NonQueryExecuting(Async)`. Two concrete
failures this caused this session, both silent (no compile error, no exception — just wrong
numbers or a fault that never fires):
1. **Command-COUNTING tests** (proving `AsSplitQuery` issues N statements, or
   `GetByIdsChunkedAsync` issues `ceil(N/chunkSize)` round trips) that seed fixture rows
   earlier in the SAME `DbCommandInterceptor`'s lifetime get their `ReaderCommandCount`
   inflated by every seed-phase INSERT (observed: expected 1, got 5; expected 3, got 10 — the
   delta was always exactly N, the seeded row count). Fix: snapshot a baseline count
   IMMEDIATELY AFTER seeding completes, assert only the DELTA the operation under test
   produces — never an absolute count when seeding happened earlier in the same interceptor's
   life.
2. **Fault-injection tests** (simulating a transient `TimeoutException` for retry-strategy
   proofs) that only override `NonQueryExecuting(Async)` can silently never fire — the target
   entity's INSERT went through the reader path instead, so `AttemptCount` stayed 0 the whole
   run yet the operation still succeeded (looks like a passing test, but proves nothing about
   retry behavior). Fix: override BOTH `NonQueryExecuting(Async)` AND `ReaderExecuting(Async)`
   in any interceptor that counts or fault-injects command executions, sharing one
   counter/trigger — never assume ExecuteNonQuery is used for writes in this domain.

## Test technique: PostgreSQL model-metadata assertions don't need a live Testcontainer
Accessing `DbContext.Model` triggers EF Core's full in-memory model build/finalization
(including every `IModelFinalizingConvention` pass, e.g. `XminConcurrencyTokenConvention`)
WITHOUT ever opening a database connection — only executing an actual query needs real
connectivity. `new DbContextOptionsBuilder<T>().UsePostgreSQL(anySyntacticallyValidConnStr)`
then `ctx.Model.FindEntityType(...).FindProperty(...)` is a fast, Testcontainer-free way to
assert `ColumnName`/`ColumnType`/`ValueGenerated` on a PostgreSQL-specific mapping (used for
`XminConcurrencyTokenConventionTests.cs`, reusing the `ConcurrencyTestDbContext`/
`ConcurrentPgAggregate` fixtures already defined in the Testcontainers-based
`ConcurrencyIntegrationTests.cs` in a different namespace — both are `public`, no duplication
needed).

## Test technique: decode-once/cache-hit proofs need no counting seam in production code
Reference-equality on a returned value is sufficient proof of exactly-once work when the
underlying operation always allocates a fresh object — `Convert.FromBase64String` always
returns a NEW `byte[]`, so a cache (e.g. `EncryptionKeyByteCache`) returning the SAME instance
across repeated calls for the same key is direct evidence the expensive decode ran only once.
No spy, wrapper, or counting seam needed. This domain's `InternalsVisibleTo` grant from
`SharedKernel.Persistence.EfCore` to its own `.Tests` project already makes internal classes
like `EncryptionKeyByteCache`/`EncryptionOptionsKeyProvider` directly constructible in tests.

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
