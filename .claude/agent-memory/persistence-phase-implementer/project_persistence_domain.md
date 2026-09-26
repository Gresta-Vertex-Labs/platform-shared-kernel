---
name: project-persistence-domain
description: Key patterns, version pins, gotchas, and decisions established during 06.Persistence implementation sessions
metadata:
  type: project
---

> WO-086 (2026-09): `IRequestContext`/`IUnitOfWork`/`IAuditTrailWriter` are now `SharedKernel.Execution` (Foundation); `ITenantProvider`, `IUserContext` use in persistence, the P-078 Security.Abstractions exception and the 06→05 grant were deleted; `PostgreSqlContainerFixture` now lives in non-packable `SharedKernel.Testing.Internal`, `TestRequestContext` in core `SharedKernel.Testing`. Sections above "Domain architecture key facts" are history.

## SK.06.Docs fully closed 2026-08-04 (64/64) — WO-053 DO-53..DO-64, closing the domain out end to end
Sixth+ confirmed instance of "state-map ○ but content already exists": all six CLAUDE.md narrative
sections these 12 tasks required (Structured Logging, Soft-Delete Restore, Read-Replica Routing,
pgvector query-ergonomics subsection, Traced-operations table, Test Rules `PostgreSqlContainerFixture`
bullet) were ALREADY written to target-state by an earlier design-confirmation pass — zero CLAUDE.md
content edits needed. The genuine gaps were narrower than the task list implied and lived in the
`.cs` XML docs and the package READMEs, not CLAUDE.md:
1. **The IN-CAPS read-after-write consistency caveat existed in only ONE of the three places DO-61
   explicitly named.** `EfCorePersistenceBuilder.WithReadReplica`'s XML doc had it; `IReadReplicaContextAccessor<TContext>`'s
   interface-level remarks and `EfReadRepository`'s `replicaAccessor` constructor-parameter doc did
   not. When a task enumerates several specific doc locations for the identical caveat, verify EACH
   one individually — a caveat present at the "obvious" top-level entry point does not imply it
   propagated to the lower-level implementation-detail types a careful reader would also land on.
2. **Package READMEs lagged CLAUDE.md's own DI Registration examples by a whole batch of features.**
   `SharedKernel.Persistence.EfCore/README.md` had no config-binding, soft-delete-restore,
   command-timeout, or read-replica-routing sections at all, despite CLAUDE.md's DI Registration
   block already carrying all four as of the Design-phase pass; `SharedKernel.Persistence.PostgreSQL/README.md`
   had zero query-side pgvector content (mapping-only, matching the OLD pre-P-339 state) even though
   `VectorOrderingExpressions.ByDistance` had shipped and been fully documented in CLAUDE.md two
   sessions earlier. **Lesson: never assume a package README tracks CLAUDE.md 1:1 just because both
   are "Docs" deliverables — grep the README directly for the new symbol names (`WithReadReplica`,
   `VectorOrderingExpressions`, etc.) before marking a README-touching Docs task done.**
3. Also added a small, previously-missing "no secret logged" remark to `AdvisoryLockAcquired`/
   `AdvisoryLockReleased`'s `[LoggerMessage]` XML docs in `PersistenceLog.cs` (DO-55's literal wording
   named "advisory-lock logs" explicitly alongside encryption-rotation logs, even though neither log
   actually carries anything sensitive — added anyway to satisfy the literal acceptance criterion).

**state-map-phase mechanics nuance worth remembering for future sessions on THIS domain (or any
domain with a similar sub-phase-completion-order quirk):** `06.Persistence`'s own `SK.06.Published`
phase key had already reached 8/8 `●` back on 2026-07-31 — independently of, and BEFORE, `SK.06.Docs`
finishing — because `06.Persistence/state-map.md`'s own "Published" phase key only tracks four narrow
NuGet-packaging tasks (P-05..P-08), not "every other phase is also done." So when `SK.06.Docs` finally
reached 64/64 in THIS session, ALL SIX phase keys (Design/Scaffold/Core/Tests/Docs/Published) became
`●` simultaneously as a side effect, even though the `state-map-phase` skill's literal S8 instruction
("phase → the root phase name from the Phase Key Registry row" for the JUST-completed key) would say
to set the root Domain Summary Board's Current Phase to "Docs," not "Published." Resolved by setting
Current Phase to "Published" anyway (matching the pattern every other FULLY-closed domain in the root
board uses — 03/04/08/09/10/12/17 all show `Published` once all six sub-phases are `●`, never
whichever phase happened to close last) and additionally running the S8b-style domain-wide Phase
Backlog bulk-close (six WO-053 entries P-333/334/336/337/338/339, all still `◐ Dispatched` despite
their underlying work being 100% shipped and tested) even though the raw call parameter was
`phase_key: SK.06.Docs`, not `SK.06.Published`. **The literal skill instructions do not anticipate a
domain whose own sub-map lets "Published" tasks finish out of order ahead of "Docs"/"Tests" — when
this happens, check ALL SIX phase-key rows in the sub-map's own Overall Progress table before writing
the root propagation, not just the one phase_key named in the call.**

Full suites re-verified green after the doc-only source edits: `EfCore.Tests` 358/358, `PostgreSQL.Tests`
53/53 (real Docker). Zero production behavior changed this session — every edit was either an XML
doc comment or a README section.

## SK.06.Tests fully closed 2026-08-04 (121/121) — WO-053 T-98..T-105/T-110..T-121, closing out the Tests-phase gap left by the Core-phase session below
Verified each of the 20 `○` tasks file-by-file against the 92 tests the prior Core-phase session had
already written, per the standing "code may already exist, verify before writing" pattern. 10/20 were
already fully/functionally satisfied (T-102/103/110/111/115 exactly; T-98/99/101 functionally covered
but strengthened in place with the exact-count/`RowsInBatch`/`BatchNumber`/full-negative-scan
assertions the task text specifically demanded, since `LoggerAssertions.ShouldHaveLogged` alone never
proves "exactly once" — only `ShouldHaveLoggedCount` does). 10/20 were genuine gaps requiring new
tests/files: T-100, T-104/105, T-112, T-113, T-114/116, T-117, T-118/119/120, T-121.

**GATING genuine production defect found and fixed while writing T-118/T-119 (`VectorOrderingExpressions.ByDistance`, `SharedKernel.Persistence.PostgreSQL`) — the single most important finding of this session.**
This WO-053 Core-phase feature had NEVER been executed against a real database before this session
(the Core-phase's own tests were pure expression-tree-shape assertions, zero Testcontainer). Passing
`queryVector` as `Expression.Constant(queryVector, typeof(Vector))` throws a genuine
`Npgsql.PostgresException` ("42601: syntax error at or near '['") the moment the query actually runs —
confirmed via `query.ToQueryString()` diagnostic: the generated SQL was
`ORDER BY p.embedding <=> [1,0,0]` — the vector rendered via `Vector.ToString()`, completely UNQUOTED,
no cast. Root cause: EF Core's query pipeline treats an already-bare `ConstantExpression` as an
already-evaluated INLINE SQL literal; `Pgvector.EntityFrameworkCore`'s distance-function SQL
translator does not attach a `vector` `RelationalTypeMapping` to an inline constant the way it does for
a genuine ADO.NET parameter (confirmed working for INSERT/UPDATE, which route through the parameter
path). **The fix, and the reusable technique for any future hand-built `Expression` tree in this
codebase that needs a runtime value to become a genuine SQL query parameter rather than an inline
literal:** wrap the value in a private single-property holder class and access it via
`Expression.Property(Expression.Constant(holder), nameof(holder.Value))` — this exact shape
(`MemberExpression` over a `ConstantExpression` holding a small instance) is what the C# compiler
itself emits for a captured local variable inside an ordinary LINQ lambda closure, and EF Core's own
parameter-extraction visitor (`ParameterExtractingExpressionVisitor`) specifically recognizes and
promotes THAT shape to a real ADO.NET parameter — a bare `Expression.Constant(value, type)` built
directly (not via this closure-mimicking wrapper) does NOT get this treatment, no matter how correctly
its `Type` is set. Confirmed via the generated SQL changing from `<=> [1,0,0]` to `<=> @Value`. Zero
change to the method's public signature or its zero-reflection `MethodInfo`-capture technique.

**Second, independent genuine defect — this one in TEST SETUP, not production code, but equally
non-obvious and worth remembering for ANY future pgvector/Npgsql test in this domain:**
`CREATE EXTENSION IF NOT EXISTS vector` must run on a THROWAWAY connection/data source BEFORE the
EF-Core-managed Npgsql connection pool for the SAME database opens its own first connection. Npgsql
resolves the `vector` type's OID once per data-source/pool lifetime, at first connection open. Issuing
the `CREATE EXTENSION` command AS that pool's own first command (e.g. via
`ctx.Database.ExecuteSqlRawAsync(...)` on a context whose options already point at the target
database) opens that connection before the extension exists, permanently poisoning the pool's cached
type mapping for its ENTIRE remaining lifetime — every subsequent `Pgvector.Vector`-typed parameter
write then throws `System.NotSupportedException: Cannot resolve 'vector' to a fully qualified datatype
name`, even though the extension now genuinely exists in the database. `PostgreSQLIntegrationTests`
(the existing T-38 raw-ADO.NET pgvector test) never hit this because it never uses a `Vector`-typed
ADO.NET parameter at all — it casts a string literal via `'[1,2,3]'::vector` instead. Fix: run
`CREATE EXTENSION` on a separate `new NpgsqlDataSourceBuilder(connectionString).Build()` connection
FIRST, then construct the EF Core `DbContextOptions`/`DbContext`.

**New reusable test technique — proving an `internal` `[LoggerMessage]` emitter's behavior from a
SIBLING package's test project with NO `InternalsVisibleTo` grant (T-100):** `PersistenceRetryDiagnosticListener`
is `internal` to `SharedKernel.Persistence.EfCore`, which only grants `InternalsVisibleTo` to
`SharedKernel.Persistence.EfCore.Tests` — but T-100 needs a REAL PostgreSQL Testcontainer, which only
lives in `SharedKernel.Persistence.PostgreSQL.Tests`. You cannot write
`new InMemoryLogger<PersistenceRetryDiagnosticListener>()` there (compile error — inaccessible type).
Solution requiring NO `InternalsVisibleTo` widening and NO reference to the internal type at all:
register `16.Testing`'s `services.AddInMemoryLoggerFactory()` (wires `ILoggerFactory` →
`InMemoryLoggerFactory` PLUS the open-generic BCL `Logger<>` → `ILogger<>` mapping — the exact
mechanism `AddLogging()` uses internally) into the SAME `ServiceCollection` as
`AddSharedKernelEfCore<TContext>(...).WithTransientFaultRetry().Build()`; resolve the listener purely
through ITS PUBLIC `IHostedService` SURFACE (`provider.GetServices<IHostedService>()`, calling
`StartAsync`/`StopAsync` manually since no full generic `IHost` is built) — the concrete type name is
never needed; read its captured log records back via
`((InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>()).GetLogger(categoryName)`,
where `categoryName` is the internal type's OWN `.FullName` written as a plain STRING LITERAL
(`"SharedKernel.Persistence.EfCore.Diagnostics.PersistenceRetryDiagnosticListener"`) — a logging
category name is public, observable information (it appears in real production log output), so naming
it as a string carries none of the C# accessibility restriction that naming the `Type` directly would.
For a PUBLIC type needing the identical proof in the same test (e.g. `EfUnitOfWork`'s `6008`
exhaustion log), just use `typeof(EfUnitOfWork).FullName!` directly — no need for the string-literal
workaround there. Apply this pattern to any FUTURE cross-package internal-`[LoggerMessage]`-emitter
test in this domain.

**Full suites green:** `SharedKernel.Persistence.EfCore.Tests` 358/358 (was 353), `SharedKernel.Persistence.PostgreSQL.Tests`
53/53 (was 41, +12 new: 3 retry-logging, 2 command-timeout, 2 read-replica two-container, 1
reference-equality, 3 vector-correctness/composability, 1 vector-behavioral-proxy — plus several
existing methods gained assertions without new `[Fact]`s). Both suites run against a real Docker
daemon. This closes every task in `SK.06.Tests` — only `SK.06.Docs` (52/64, `◐`) remains open in this
domain's own state-map.

## SK.06.Core fully closed 2026-08-03 (145/145) — WO-053 C-129..C-145, same session as Scaffold close
Implemented and tested all 17 remaining Core tasks in one session, immediately after the Scaffold
close documented below. Production code for most tasks had ALREADY been written by a prior
(uncommitted-to-state-map) session — this session's actual work was: verify each file against
`06.Persistence/CLAUDE.md`'s already-reconciled target-state docs (zero drift found — the
Design-confirmation pass a few sessions earlier had already corrected the two genuine spec/reality
mismatches, `ConcurrencyInterceptor.TryTranslate` non-static + `Pgvector.EntityFrameworkCore.
VectorDbFunctionsExtensions` naming), then write ~92 new tests across 8 new test files + additions
to 2 existing files, run the full EfCore (353/353) and PostgreSQL (41/41, real Docker) suites, fix
one genuine test-writing-time bug (below), then close the state-map loop end to end (sub-map task
rows → sub-map Overall Progress → sub-map changelog → root Domain Summary Board → root Overall
Progress counts → root changelog — the full `state-map-phase` Sub-map-mode promotion chain).

**New genuine bug found and fixed, NOT foreseeable from design docs alone**: the new
`PersistenceRetryDiagnosticListener` (C-131) subscribes to `System.Diagnostics.DiagnosticListener
.AllListeners` — a single PROCESS-WIDE static, with no per-`DbConnection`/per-test correlation field
available on the diagnostic payload (`ExecutionStrategyEventData`) the way `Activity.ParentId`
provides for tracing spans (see the pre-existing "Cross-test ActivitySource contamination" entry
below — this is the SAME class of hazard, one layer lower in the BCL, but with NO equivalent fix
available). `PersistenceRetryDiagnosticListenerTests` (proving the listener logs N times for N
observed retries) and `RetryExhaustionLoggingTests` (proving `EfUnitOfWork`/`EfTransactionalUnitOfWork`
log on retry-exhaustion) both force genuine EF Core retries via the identical
`AlwaysRetryStrategyFactory`/`FaultInjectingInterceptor` fixture technique — when xUnit ran them
concurrently (different test CLASSES, xUnit's default), retry events from EITHER test's `DbContext`
were visible to BOTH tests' listener instances, inflating counts (observed: expected 2, got 3).
**Fix, and the pattern to reuse for any FUTURE test class that forces genuine retries**: tag every
such class into one shared xUnit collection — `[CollectionDefinition("RetryDiagnostics")]` on one
marker class (public sealed, no body needed beyond the attribute) plus `[Collection("RetryDiagnostics")]`
on each test class — forcing xUnit to run them sequentially relative to EACH OTHER (they still run
in parallel with every other, non-retry-forcing test class). Documented as a new `06.Persistence/
CLAUDE.md` Test Rules bullet via `sync-brain`, placed directly after the existing structured-logging
Test Rules bullet.

**Two reusable FluentAssertions/testing-infra gotchas surfaced while writing C-144/C-145's pure
expression-tree-shape tests** (`VectorOrderingExpressionsTests.cs`, `SharedKernel.Persistence
.PostgreSQL.Tests` — the first tests in that project needing NO Testcontainer at all, since
`Pgvector.EntityFrameworkCore`'s distance methods are EF-Core query-translation placeholders never
meaningfully invoked client-side, so only the built `Expression` tree's SHAPE is assertable):
1. `Expression.Call(...)`/`Expression.Property(...)` etc. return BCL-INTERNAL derived subtypes
   (`MethodCallExpression2`, `PropertyExpression`) rather than the public base type
   (`MethodCallExpression`, `MemberExpression`) directly. FluentAssertions' `.Should().BeOfType<T>()`
   checks EXACT type equality and fails against these — use `.Should().BeAssignableTo<T>()` instead
   (or a plain C# `(T)expr`/`is T` cast/check, which works fine against internal subtypes since they
   genuinely inherit from the public base). This will bite ANY future expression-tree-shape test in
   this domain (e.g. a future `KeysetSpecification` seek-predicate shape test) — always reach for
   `BeAssignableTo<T>()` first when asserting on a node produced by the `Expression` factory methods,
   never `BeOfType<T>()`.
2. `SharedKernel.Testing.Logging.LogRecord`'s real property names are `.LogLevel` (not `.Level`) and
   `.State` (an `IReadOnlyList<KeyValuePair<string,object?>>?`, not `.Properties`) — confirmed by
   reading the real type directly after two build failures from guessing the wrong names. Always
   `Read` `16.Testing/SharedKernel.Testing/Logging/LogRecord.cs` before writing a raw `record.Xxx`
   property access in a new logging test — `TryGetProperty(name, out value)` is the sanctioned way
   to read a structured property; `.LogLevel`/`.EventId`/`.Message`/`.Exception`/`.Scopes` are the
   only other public members.

Phase completion status (this session): SK.06.Design ●(103/103), SK.06.Scaffold ●(18/18),
SK.06.Core ●(145/145, NEW this session), SK.06.Tests ◐(101/121 — WO-053 added T-98..T-121 which
this session did NOT specifically target/close), SK.06.Docs ◐(52/64 — WO-053 added DO-53..DO-64,
also not targeted this session), SK.06.Published ●(8/8). A future session should re-check whether
the 92 tests THIS session wrote for C-129..C-145 happen to already satisfy some of T-98..T-121's
task text (likely, given the close 1:1 correspondence between WO-053's Core and Tests task lists)
before writing anything new — grep this session's new test file names against the Tests-phase task
descriptions first, per the now-well-established "code may already exist, verify before writing"
pattern documented throughout this file.

## SK.06.Scaffold fully closed 2026-08-03 (18/18) — S-17 + T-106..T-109 done same session
S-17 (WO-053/P-336) was left `◐` by a prior session pending T-106..T-108 (a separate Tests-phase
gate). The next Scaffold-phase dispatch explicitly authorized doing that migration work now to
close S-17 properly, rather than waiting for a formal Tests-phase dispatch — did the full
migration, then flipped T-106..T-109 too (with an annotation, since their own task text's "all
four classes switch" premise was wrong — see below). Two genuine, non-obvious technical findings
surfaced only by actually running the tests, not foreseeable from the design phase alone:

1. **`Database.EnsureCreatedAsync()` is coarse-grained — checks "does this DB have ANY tables,"
   not "does it have THIS model's tables."** Sharing one literal database (e.g. 16.Testing's
   `PostgreSqlContainerFixture`'s fixed `sharedkernel_test`) across multiple xUnit test classes
   with DIFFERENT `DbContext` models means only the FIRST model's `EnsureCreatedAsync()` call
   actually creates its schema — every subsequent, differently-shaped model's call silently
   no-ops (`HasTables() == true` already) and its tables are NEVER created → "relation does not
   exist" at query time. **Fix:** each class targets its own uniquely-named database within the
   ONE shared container via `new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
   { Database = "some_unique_name" }.ConnectionString` — `EnsureCreatedAsync()` can create a
   brand-new named database from scratch (Npgsql's create-database path connects to the built-in
   "postgres" administrative database every vanilla PostgreSQL image always provisions). This
   preserves genuine per-class schema isolation while still eliminating redundant container
   starts (the actual point of Testcontainers fixture consolidation is fewer containers, not one
   shared database). Applied: `sk_persistence_concurrency`/`sk_persistence_keyset`/
   `sk_persistence_transient_retry` in `SharedKernel.Persistence.PostgreSQL.Tests`.

2. **`DROP TABLE` order becomes load-bearing once a database persists across xUnit test METHODS**
   (not just across classes). A class's own `IAsyncLifetime.InitializeAsync` schema-setup script
   that drops a parent table before its FK-dependent children only "worked" because each `[Fact]`
   previously got a brand-new container (a fresh class instance = fresh container = no pre-existing
   tables to conflict with). Once the container/database persists across test methods via
   `IClassFixture<PostgreSqlContainerFixture>` (one container shared across every `[Fact]` in the
   class, while a FRESH test-class instance — and therefore a fresh `InitializeAsync()` call —
   still runs per test method), the SECOND test method's script fails with Npgsql
   `PostgresException` `2BP01` ("cannot drop table ... because other objects depend on it").
   **Fix:** always drop child (FK-holding) tables before parent tables in any schema-setup script
   that may run more than once against a persistent database. Found/fixed in
   `DapperReadServiceIntegrationTests.cs` (`dapper_payment`/`dapper_order` before `dapper_customer`).

3. **A real, PERMANENT constraint, not a temporary gap:** `PostgreSQLIntegrationTests` cannot join
   the shared `16.Testing` fixture — its pgvector round-trip test needs the `pgvector/pgvector:pg16`
   image (the extension binary is absent from a vanilla PostgreSQL image), and the shared fixture
   is pinned to plain `postgres:16.4`. It keeps its own dedicated container permanently. This means
   `SharedKernel.Persistence.PostgreSQL.Tests.csproj` genuinely still needs its direct
   `Testcontainers.PostgreSql` `PackageReference` (only `SharedKernel.Persistence.Dapper.Tests.csproj`'s
   reference was removable, since its one Postgres-touching class fully migrated). When a design
   doc says "all N classes migrate uniformly," verify per-class technical constraints before
   assuming that's achievable — it wasn't, here.

`xUnit` mechanics worth remembering: `IClassFixture<T>` instantiates the fixture ONCE per test
CLASS (shared across every `[Fact]` method in it) but xUnit still constructs a NEW instance of the
TEST CLASS itself per test method — so a class's own `IAsyncLifetime.InitializeAsync`/`DisposeAsync`
(for schema setup, not container lifecycle) still runs once per test method even after migrating
the container to a class fixture. This is why finding #2 above was previously invisible: before
migration, "once per test method" coincided with "once per fresh container," masking the ordering bug.

## Phase completion status (as of 2026-07-31, WO-051 FULLY CLOSED — all 6 phases ● end to end)
> STALE as of 2026-08-03: WO-053 (P-333/334/336/337/338/339) added new tasks across Design/Scaffold/
> Core/Tests/Docs after this snapshot — Core is closed again (see the top-of-file entry), but
> Tests/Docs are `◐` again with new WO-053 task IDs (T-98..T-121, DO-53..DO-64). "ZERO pending
> phases" below is no longer accurate. Kept for history per this file's own established convention.
- SK.06.Design: complete (84/84 tasks) — D-65..D-84 (WO-051 batches 1+2) done 2026-07-30
- SK.06.Core: complete (128/128 tasks) — C-98..C-128 verified/closed 2026-07-31
- SK.06.Tests: complete (97/97 tasks) — T-61..T-97 verified/closed 2026-07-31
- SK.06.Docs: complete (52/52 tasks) — DO-39..DO-52 verified/closed 2026-07-31
- SK.06.Published: complete (8/8) — P-05..P-08 closed 2026-07-31 (see entry below). 06.Persistence
  now has ZERO pending phases/tasks anywhere in its own state-map. Root Domain Summary Board row 06
  promoted to Published/●.
- Root Phase Backlog: ALL WO-051 06.Persistence entries (P-315..P-325, 11 total including P-324)
  closed to ● Complete 2026-07-31 — see "WO-051 backlog sweep" entry below. Only P-326
  (13.ServiceDefaults) and P-327 (00.Governance) remain open — different domains, not this
  agent's jurisdiction, but now unblocked since their dependencies (P-319, P-316) are shipped.
- Do not assume "Published" in root state-map means every sub-phase task is done — this domain
  repeatedly gets NEW WO's adding tasks to already-"complete" phase-key sections after
  reaching Published once (WO-013), and the root Domain Summary Board's "Current Phase"
  column just reflects whichever phase-key was MOST RECENTLY closed, not a monotonic
  milestone — it can and does go back to "Design"/"Core"/"Tests"/"Docs" when a new batch of
  tasks completes, even after the domain previously showed "Published".
- ALWAYS re-read 06.Persistence/state-map.md's own Overall Progress table before assuming a
  phase is done — don't trust the root's one-line domain summary alone.

## P-05..P-08 (2026-07-31): code already done, only state-map lagged — 4th+ instance of the pattern
All four `.csproj` files already had `<PackageReadmeFile>README.md</PackageReadmeFile>` AND
`<None Include="README.md" Pack="true" PackagePath="\" />` wired in from a prior unclosed session,
and all four `README.md` files already existed, fully written. Verified (not trusted) via real
`dotnet pack --configuration Release -o <scratch-dir>` runs for all four projects — all four
produced clean `.nupkg`+`.snupkg` with ZERO `NU5039`/`NU5128` warnings (only pre-existing unrelated
CS1574/CS1734 XML-doc-cref warnings appeared). Additionally confirmed via `unzip -l <nupkg>` that
README.md is physically present at the package root in all four generated packages — don't just
trust that pack succeeded silently, actually inspect package contents when a task's acceptance
criterion is "X is in the package." No source files needed changing this session — zero git diff.
Always `rm -rf` any scratch pack-output directory created inside the repo tree afterward (or better,
pack to the harness scratchpad dir, not a repo-local folder) so `git status` stays clean.

## CRITICAL: root Phase Backlog entries can lag their sub-map phase-key completion by many sessions
When a domain's WO-specific phases (e.g. WO-051's P-315..P-325 for 06.Persistence) get folded
directly into the six standard lifecycle phase-keys (Design/Scaffold/Core/Tests/Docs/Published)
rather than getting their own dedicated phase-key in the sub state-map, the `state-map-phase`
skill's S8a step (which closes individual root Phase Backlog P-NNN entries) never fires for them —
S8a only fires for phase keys whose "Maps to Root Phase" is itself a P-NNN token or has a
"Root Backlog ID" column, and explicitly SKIPS when the phase is one of the six standard lifecycle
names (Case 3). The ONLY point these get swept closed is S8b, which fires exactly once, the moment
the domain's Published phase-key completes. This means dozens of individually-dispatched root
Phase Backlog entries can sit at "◐ Dispatched" for many sessions after their actual work shipped —
found 11 such entries here (P-315 through P-325) still showing "◐ Dispatched" despite every one of
them having fully shipped, tested, documented code with explicit `WO-051/P-3xx` annotations in both
production source comments AND `06.Persistence/CLAUDE.md` prose. **Do not blindly apply S8b's
literal instruction ("close every entry whose Domain matches") without verification** — a domain's
Phase Backlog can legitimately contain genuinely-unfinished, unrelated future work for the same
domain (this was NOT the case here, but could be). The correct process: read each backlog entry's
full acceptance criteria, then grep the ACTUAL shipped `.cs`/test files for the concrete artifacts
each criterion names (class names, test file names, doc annotations) — never trust README.md prose
alone as proof (README prose can also be aspirational/stale, though in this case it lined up).
Evidence that clinched it here: production code comments literally embedding `(WO-051/P-320)`,
`(CORRECTED, WO-051/P-325)`, `(WO-051/P-316)` etc. directly at the fix site — when a codebase
uses this self-annotating convention, grep for `WO-0NN/P-NNN` directly; it's the fastest, most
reliable verification signal available. Only close backlog entries for the SAME domain the
completing phase belongs to — cross-domain follow-on entries in the same WO (here: P-326 for
13.ServiceDefaults, P-327 for 00.Governance) must be left untouched even though their dependency
phase IDs are now satisfied — they belong to a different domain's jurisdiction and a different
agent's session.

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

## Domain architecture key facts (current as of WO-086, 2026-09)
- No outbox types anywhere in 06.Persistence — the outbox is `07.Messaging`'s satellite `SharedKernel.Messaging.MassTransit.EfCore`
- One save interceptor (`PersistenceSaveChangesInterceptor`) plus `DomainClockMaterializationInterceptor`; the former Audit/SoftDelete/Concurrency interceptors are gone (P-558)
- Tenant and actor come from `IRequestContext` (`SharedKernel.Execution.Context`, `TenantId?`, null fails closed); `IUnitOfWork` from `SharedKernel.Execution.Transactions`, `IAuditTrailWriter` from `SharedKernel.Execution.Auditing` — all Foundation tier
- No package here references `12.Security`, `SharedKernel.Application`, `SharedKernel.Application.Pipeline` or MediatR (the P-078 `Security.Abstractions` exception and the 06→05 grant are both gone)
- Tiers: `.Abstractions` = Abstractions tier; `.Npgsql`/`.EfCore`/`.Dapper`/`.EfCore.Auditing`/`.EfCore.Encryption` = Adapter tier with declared edges EfCore,Dapper→Npgsql and Auditing,Encryption→EfCore (SKTIER errors enforce it; root CLAUDE.md "Tiers & Dependency Rules")
- Readiness: `IReadinessProbe` names `"field-encryption"` and `"audit-sealing"`, mapped by `AddSharedKernelReadiness()`; `ServiceDefaults.Persistence` keeps `AddDatabaseReadinessCheck<T>`, `AddDapperDatabaseReadinessCheck` and `AddPersistenceStartupReadinessCheck` (`AddFieldEncryptionReadinessCheck`/`AddAuditSealingReadinessCheck` were deleted)
- `AddSharedKernelPostgres<TContext>` is the sole DI entry point for EfCore wiring
- Paging (Skip/Take) is ALWAYS the last operation in SpecificationEvaluator pipeline
- Audit/soft-delete stamping writes through `ChangeTracker.Entry(entity).CurrentValues[name]` — never direct property setters

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

## P-557/W4 (2026-09-19) — Npgsql/PostgreSQL/Dapper gold-standard wave: key reusable findings

Note: as of P-557/W1-W4 the domain was restructured into 7 packages (Abstractions, EfCore,
EfCore.Auditing, EfCore.Encryption, Npgsql, PostgreSQL, Dapper) — the "6 project" solution-file
listing above and much of this file's earlier history predates that split. Treat entries below this
point as the current architecture; treat entries above as historical record of an earlier shape.

**PostgreSQL superusers bypass row-level security unconditionally, even under `FORCE ROW LEVEL
SECURITY`.** Testcontainers' PostgreSQL image's own `initdb`-created user IS a superuser. Any test
proving RLS actually filters rows MUST connect through a separately-created, genuinely unprivileged
role (`CREATE ROLE x LOGIN PASSWORD '...'; GRANT SELECT ON table TO x;`) — connecting as the fixture's
default user will silently see every row regardless of the policy, looking like "RLS doesn't work"
when actually the test setup never engaged it. Pattern used:
`06.Persistence/SharedKernel.Persistence.Dapper/SharedKernel.Persistence.Dapper.Tests/Integration/TenantSafeDapperReadServiceIntegrationTests.cs`.

**Dapper's default column-to-property mapper does NOT strip underscores.** `tenant_id` does not
auto-bind to a `TenantId` property (unlike EF Core's naming conventions). Every raw-SQL SELECT that
needs a snake_case-to-PascalCase mapping must alias explicitly: `tenant_id AS "TenantId"`. Found by a
real test returning `Guid.Empty` for every row despite correct seed data.

**`Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<T>` has ONLY `Expression<Func<...>>`
-typed constructors, never plain `Func<...>` delegates** (confirmed via reflection against the real
EF Core 10.0.5 assembly). The lambdas passed to it compile as expression trees, so C# pattern-matching
(`is`/`is not`) directly inside those lambda BODIES throws CS8122 ("Expression tree cannot contain an
'is' pattern-matching operator") — extract any null-check logic into a plain helper METHOD and call it
from the lambda instead (a `MethodCallExpression` referencing a real method is legal inside an
expression tree; the `is` check just can't be inlined there).

**The real EF Core Npgsql provider convention-level API for the pgvector extension is
`NpgsqlModelBuilderExtensions.HasPostgresExtension(this IConventionModelBuilder, string name, bool
fromDataAnnotation = false)`** — call it directly as `modelBuilder.HasPostgresExtension("vector")`
inside an `IModelFinalizingConvention.ProcessModelFinalizing`. Do NOT reach for
`IConventionModel.GetOrAddPostgresExtension` — that method exists too but takes a MANDATORY 3rd
`version` string argument (no default), a trap if you assume a 2-arg `(schema, name)` shape from a
design doc's sketch. Confirmed via a throwaway reflection probe project (`dotnet run` against a
scratch csproj referencing the real NuGet package) rather than guessed — grepping the installed `.dll`
for method-name STRINGS only confirms the method exists, not its parameter list; when unsure, write
and run a tiny reflection probe against the real package before committing to a signature.

**`Dapper.SqlMapper.QueryUnbufferedAsync<T>` has NO `CommandDefinition`/`CancellationToken`-accepting
overload** (Dapper 2.1.79, confirmed via reflection) — only `(DbConnection, string sql, object?
param, DbTransaction?, int? commandTimeout, CommandType?)`. A wrapper can only honor a
`CancellationToken` by checking `ct.ThrowIfCancellationRequested()` per yielded row.

**`SmartEnum<TEnum,TValue>`'s own base type ALREADY force-runs the derived type's static constructor
once per closed generic type** (a `_forceEnumStaticConstructor` field initializer calling
`RuntimeHelpers.RunClassConstructor` at the BASE type's own static init) so `TryFromValue`/`FromValue`/
`List` never observe an empty registration list. Any consumer code (e.g. a Dapper
`SmartEnumTypeHandler`) that ALSO calls `RunClassConstructor` before every `Parse` is genuinely
redundant, safe to delete — confirmed by reading `01.Core/SharedKernel.Primitives/Enums/SmartEnum.cs`'s
own XML doc remarks on that field, not by inference.

**`StronglyTypedId<TValue>`'s conversion operator is `explicit`, not `implicit`** (03.Domain's
"identifiers convert only explicitly" rule). Old XML doc comments elsewhere claiming "implicit
operator" are stale prose, not a real bug: a C# cast expression invokes a user-defined conversion
operator whether declared `implicit` or `explicit`. Grep any future "implicit operator" claim in this
domain's docs against the real 03.Domain source before trusting it.

**A `public sealed class`'s public constructor cannot declare a parameter of a LESS-accessible type**
(CS0051). Fix: declare the public constructor parameter as the PUBLIC interface the internal type
implements, then cast down at internal call sites within the same assembly that need the interface's
missing setter/extra members.

**CPM (Directory.Packages.props) can carry a genuinely-broken version pin that no existing project
graph happened to surface.** `Npgsql` was pinned at `10.0.2`, but `Npgsql.EntityFrameworkCore.PostgreSQL`
`10.0.2` (also pinned) actually requires `Npgsql >= 10.0.3` — silent until a new project referenced
BOTH packages directly for the first time, which then failed `NU1605` (warning-as-error). Fixed by
bumping the CPM pin to `10.0.3` (already in the local NuGet cache) — a legitimate, minimal,
version-correctness-only fix, not scope creep, when a new legitimate reference pattern is the first to
expose a pre-existing CPM mismatch.

**PostgreSQL's `SHOW` output unit is inconsistent across GUCs** — `lock_timeout` set to `2500` (ms)
reports back as `"2500ms"`, while `statement_timeout` set to a round number of seconds reports as
`"Ns"`. Do not assume a consistent unit string; confirm empirically per-GUC when asserting server-side
effectiveness in a test.

**EF Core's `ctx.Model` (the "read-optimized" runtime model) strips check-constraint metadata** —
`entityType.GetCheckConstraints()` throws `InvalidOperationException` directing you to
`ctx.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model` instead. Keys/
indexes/FKs are fine on the runtime model; check constraints specifically are not.

**EF Core's default table-naming convention prefers the `DbSet<T>` PROPERTY name over the full CLR
type name when a DbSet property exists.** A test trying to force 63-byte identifier truncation via an
extremely long CLASS name will silently fail if that entity also has a short-named `DbSet<T>`
property — force the scenario with an explicit `.ToTable("...")` call instead.

## P-557/W7 (2026-09-19/20) — Hardening sweep: a whitespace-collapse regex disaster and recovery, plus a real pack-check methodology

**CRITICAL, highest-value lesson of this session: never run a "collapse space before punctuation"
regex (`[ \t]+([.,;:)])` -> `$1`, meant to clean up leftover double-spaces after deleting inline
work-order-tag citations) across `.cs` files without treating it as a genuine code-mutation, not a
prose-cleanup.** It silently corrupted SIX independently-discovered shapes, spread across the entire
354-file scope, each requiring its own detection regex and its own repair rule (never share a repair
script across shapes — each was verified against real UNTOUCHED precedent elsewhere in the 20-domain
repo before trusting a fix):
1. Leading-punctuation continuation lines (`.Method()`/`)`/`;`/`,` at column 0, indentation eaten) —
   fix: restore from the anchor line's indent + 4 (fluent-chain root is always less-indented).
2. Leading-colon continuation lines (`: base(...)`, `: IInterface`, ternary `:` branch on its own
   line) — TWO different rules depending on the anchor: same-as-anchor when the anchor is itself a
   ternary `?`-line (already the one-level-deeper continuation), anchor+4 otherwise (ctor signature,
   class declaration).
3. `.NET`-in-prose (space before a proper noun eaten: `word.NET` from `word .NET`) — distinguish from
   legitimate zero-space compounds (`ADO.NET`/`ASP.NET`) by checking the 3 characters before `.NET`.
4. Orphaned-sentence-period doc-comment lines (`/// The default.\n///.\n/// </param>` — a trailing
   `///.` line on its own with nothing else) — the space before the period on the PREVIOUS line was
   eaten and the period got pushed onto its own continuation line.
5. Same-line primary-constructor/base-list/named-ctor-initializer colon (`class Foo(x): IBar`,
   `Ctor(x): base(y)` — established house style ALWAYS has a space before this `:`, confirmed via
   dozens of untouched precedents (`class Foo(x) : IBar`) before writing the fix). Two DIFFERENT
   regexes needed: one for `(class|record|struct) Name(...):`, one for the more general
   `identifier(...): (base|this)(`, since the first requires the type keyword and the second doesn't.
6. Single-line ternary colon (`cond ? true: false` missing the space before `:`, space after intact)
   — BY FAR the most pervasive shape, ~55 instances. Cannot be safely regex-fixed in bulk: a bare
   `identifier:` after a `?` on the same line is indistinguishable by pattern alone from a correctly
   un-spaced named-argument (`Foo(commandTimeout: x)`) or tuple-element-name (`(Property: p, ...)`)
   colon, which must NEVER gain a space. Fixed via individually-vetted exact-substring
   (file, find, replace) triples applied by a tiny C# script that verifies each substring occurs
   EXACTLY once (or an explicitly-allowed N times for a genuinely duplicated line) before writing —
   zero risk of an unintended match, at the cost of being fully manual to assemble. A SEVENTH shape
   (a fluent-chain continuation INSIDE an XML doc `<code>` sample, `///.Build()` instead of
   `///     .Build()`) was found only later, while investigating pack-time warnings — the `///` marker
   absorbing the leading dot meant the ORIGINAL leading-dot detector (which required the dot at column
   0) never matched it.

**Detection technique that generalizes to any future corruption hunt in this repo:** for a suspected
corrupted shape, grep the SAME pattern across the ~15 domains OUTSIDE the touched scope first. Zero
hits outside + many hits inside == very high confidence the pattern is corruption, not house style.
Used this to settle every one of the 7 shapes above before writing a single repair rule, and it also
positively CONFIRMED house-style conventions this repo had never written down (e.g. "a wrapped
constructor initializer/base-list is always anchor-indent+4", "a same-line primary-ctor base-list
always has a space before the colon").

**PublicApiAnalyzer's RS0026/RS0027 (multiple public overloads with optional parameters) are INVISIBLE
to `dotnet build`, even `dotnet build --no-incremental`, once MSBuild's cache considers a project
up-to-date at its CURRENT assembly version — they only reliably surface on a genuine recompile at a
DIFFERENT version, e.g. `dotnet pack -p:MinVerVersionOverride=<new-version>`.** Spent much of this
session believing "zero RS0026 in the whole domain" based on repeated full-solution `dotnet build`
checks, only to find 9 real occurrences across 6 files the moment packing forced a real recompile.
**Lesson for any future pack-check or public-API-review task: never trust a `dotnet build`-based
"zero warnings" claim for PublicApiAnalyzer diagnostics — force a real recompile via pack (or
`--no-incremental` PLUS a version bump) before concluding the check passed.** All 9 occurrences here
were genuinely safe (overloads disambiguated by required-parameter type/count/generic-arity, never
actually ambiguous to a caller) and were suppressed with a paired `#pragma warning disable/restore
RS0026` (or `RS0027` for the one `HasJsonbColumn` case, a related-but-distinct rule) plus a written,
per-site justification — never a blanket suppression, never a redesign of an already-good,
already-tested, already-documented public overload set just to silence an overly-conservative
analyzer rule.

**This repo's real pack-check mechanism is already built, not a throwaway scratch folder:**
`Directory.Build.props` sets `PackageOutputPath` to the repo-root `nupkgs/` for every project, and
root `NuGet.Config` routes every `SharedKernel.*` `PackageReference` (no inline version — Central
Package Management via `Directory.Packages.props`'s `SharedKernelPackageVersion` property) to that
folder as a local feed, `nuget.org` for everything else. To pack-check a domain that has NEVER been
published before (06.Persistence's case — zero prior `PackageVersion` entries existed for any of its
7 IDs in `Directory.Packages.props`, a gap this session had to fill), pack the FULL transitive
`SharedKernel.*` dependency closure at ONE shared explicit version
(`-p:MinVerVersionOverride=<same-version-for-everything>`) — packing at floating/independent MinVer
heights reproduces the exact "dependency stamped at a height that was never published" hazard
`.github/workflows/publish-package.yml`'s own dependency-gate step exists to catch. `SKPKG003`
(`Directory.Build.targets`, `BeforeTargets="Pack"`) is a repo-authored guard requiring every packable
project to have an adjacent `README.md` — like RS0026 above, it NEVER fires on `dotnet build`, only on
`dotnet pack`. Found three 06.Persistence packages (`.EfCore.Auditing`, `.EfCore.Encryption`,
`.Npgsql`) with no README at all this way — a real, would-have-blocked-a-real-publish defect, invisible
to every earlier full-solution build check this session ran.

**A consumer-verify project (packages resolved via `PackageReference`, never `ProjectReference`,
pointed at the local feed) is worth writing even pre-first-publish — it caught real, previously-
undocumented hard DI dependencies that reading the source/docs alone had missed.**
`EfAuditTrailWriter`'s constructor takes MANDATORY (non-nullable, no default) `IDbConnectionFactory`
and `IAmbientDbTransaction` parameters, but `WithAuditTrail()`'s own XML doc `REQUIRES` list framed
`AddSharedKernelNpgsql()` as a soft, gracefully-degrading nice-to-have ("without it, falls back to
retry alone" — true only for the ONE genuinely-optional `IAdvisoryTransactionLock? = null` parameter)
and never mentioned `EfCorePersistenceBuilder.WithTransactionalUnitOfWork()` at all, despite it being
the ONLY registration source for the mandatory `IAmbientDbTransaction`. Reading a constructor's actual
nullability directly (not the prose describing it) is what surfaces this class of doc/reality drift —
confirmed the fix was accurate by re-reading `EfAuditTrailWriter`'s real constructor signature before
touching the XML doc, never trusting the OLD prose as a starting point.

**Central Package Management (`Directory.Packages.props`) needs a `PackageVersion` entry for every
`SharedKernel.*` id a `ConsumerVerify`/other pure-`PackageReference` project will reference — a
domain's own production `.csproj` files use `ProjectReference` and never need one, so this gap is
invisible until the FIRST `PackageReference`-based consumer of that domain is written.** Add
alphabetically, matching the existing `Version="$(SharedKernelPackageVersion)"` pattern exactly.

**`dotnet pack`'s own compiler pass can surface entirely different warnings than the identical
project's `dotnet build` pass moments earlier — always grep pack output separately, never assume a
green build implies a clean pack.** Applies beyond RS0026/RS0027/SKPKG003 above; treat every
`dotnet pack` invocation on a not-recently-packed project as capable of surfacing genuinely new
information, not merely re-confirming the build.
