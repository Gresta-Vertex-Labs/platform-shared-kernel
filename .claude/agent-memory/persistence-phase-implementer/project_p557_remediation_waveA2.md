---
name: project-p557-remediation-waveA2
description: P-557 remediation Wave A2 (tenant isolation, the rest) — H1-H3/C4-C5/M-item fixes, key technical findings and multi-agent coordination lessons
metadata:
  type: project
---

> WO-086 (2026-09): the 06-layering lock `SharedKernelLayeringRules.PersistenceNeverReferencesApplicationOrSecurity` was deleted — tiers (SKTIER errors, `DependencyGraphRulesTests`) replace it; `05.Application.Behaviors` is now `SharedKernel.Application.Pipeline`; `TenantSafeDapperCommandService` was removed by P-558 (`IDbSessionFactory`).

## Context
Wave A2 picked up where `[[project-p557-remediation-waveA]]`-shaped work (remediation Wave A part 1,
logged separately) left off: H1 test coverage, H2 (RLS scope-transition correctness), H3
(`DapperCommandService` had no tenant protection), C5 (bulk `ExecuteUpdateAsync` into encrypted
columns), several M items, and a missing 06-layering architecture test. Ran concurrently with two
other agents (Auditing/Wave B, Encryption) sharing the same repo and Docker daemon — see "Multi-agent
coordination" below for what that actually cost in practice.

## H1 — tenant write guard: production fix was ALREADY DONE, only tests were missing
By the time this session started, `TenantedDbContext.ApplyTenantFilters` already marked `TenantId` an
EF Core concurrency token (`ApplyTenantConcurrencyToken`) — the fix for "a detached stub claiming the
attacker's own tenant id but the victim's primary key" was already shipped and well-documented in code
comments, even though `p557-remA-log.md` (written earlier in the same overall effort) still listed H1
as open. **Lesson reinforced: trust the code over a session log — a log is a narrative snapshot, the
code is ground truth, and multiple sessions/agents can land fixes between when a log was written and
when it's read.** Only the INVERTED-shape tests (attacker tenant + victim PK, for both update and
delete) were missing — added to both the SQLite suite (`TenantWriteGuardInterceptorTests`, using
`TenantedTestAggregate` — no `IHasConcurrency`) and the Postgres suite (`TenantIsolationPostgresTests`,
using `PgOrderAggregate`).

**Real gotcha found only by running the Postgres test:** `PgOrderAggregate` is
`TenantedFullAuditableAggregateRoot`, so it ALSO implements `IHasConcurrency` (real xmin RowVersion).
`ConcurrencyInterceptor.TryTranslate` checks `IHasConcurrency` BEFORE `IHasTenant`, and a
`DbUpdateConcurrencyException` carries no way to tell which column(s) in the compound WHERE clause
(`TenantId`, `RowVersion`, or both) caused the zero-row match — so the inverted-attack scenario
surfaces as `ConflictException`, never `ForbiddenException`, for any aggregate that also carries a
concurrency token. This is NOT fixable post-hoc (the DB doesn't tell you which column(s) mismatched),
and trying to special-case "claimed tenant != current tenant" doesn't help either — in the attack
shape, the stub's claimed tenant DELIBERATELY equals the current context's tenant (that's what makes
it pass the in-memory guard). **Accepted as correct, documented behavior**: both exception types mean
"write rejected, victim row untouched" — the SQLite suite (no RowVersion) proves `ForbiddenException`
specifically; the Postgres suite (`PgOrderAggregate`, has RowVersion) proves `ConflictException` for
the identical attack shape, with the ambiguity explained in a code comment at the assertion site.

## H2 — RLS cross-tenant escape outliving its scope
Root cause: `RowLevelSecurityConnectionInterceptor` binds `app.tenant_id`/`app.cross_tenant`
SESSION-scoped, ONCE, at `ConnectionOpened`. For the common one-statement-per-connection-lease shape
this self-heals every time EF closes and reopens the connection — the review's own note that "the
existing test only passes because EF closes and reopens the connection between two untransacted
queries" is exactly right. It stops being exact the moment a connection's lease spans more than one
statement under an EXPLICIT transaction.

**Fix: a NEW `RowLevelSecurityCommandInterceptor : DbCommandInterceptor`** (all six
Reader/NonQuery/Scalar × sync/async overrides — this domain's own memory already flags that SQLite AND
Npgsql route store-generated-value writes through the READER path, not NonQuery, so all three command
kinds genuinely need coverage) that re-binds via `ITenantSessionBinder.BindAsync` (transaction-scoped,
`SET LOCAL`-equivalent, self-resetting) immediately before EVERY command that runs inside an explicit
transaction (`command.Transaction is not null`). Outside an explicit transaction, this interceptor is
a deliberate no-op — the connection-scoped bind/reopen cycle is ALREADY exact there, and rebinding
every untransacted statement would just be redundant round trips. Registered alongside (never instead
of) the existing connection interceptor via the SAME `RowLevelSecurityOptionsContributor`/
`.WithRowLevelSecurity()` wiring.

Proved against real Postgres with the exact three scenarios the review named: scope entered AFTER a
transaction already opened, scope exited while the transaction stays open (next read on the SAME
transaction is isolated again), and an ENLISTED Dapper write after scope exit (see H3 below — this one
needed BOTH the EF-side command interceptor AND `TenantSafeDapperCommandService`'s own per-statement
rebind to be provably correct, since Dapper never goes through EF's command pipeline).

**Escape-token hardening (secondary H2 finding):** `current_setting('app.cross_tenant', true) = 'on'`
is a literal ANY session can write via an ordinary, unprivileged `SELECT set_config(...)` call — no
`ICrossTenantScope.Enter()` needed. Comparing two session-writable GUCs to each other (a "nonce" +
"expected nonce" scheme) provides ZERO security, since an attacker with arbitrary-SQL execution rights
controls both sides of that comparison. The only sound fix is a genuine, unpublished, per-deployment
secret baked into BOTH the RLS policy's SQL text (at migration-authoring time) and the runtime binder
(via configuration) — analogous to how `01.Core.Cryptography`'s peppers/keys are provisioned.
Implemented as an OPTIONAL `NpgsqlPersistenceOptions.CrossTenantEscapeToken` (validated `[MinLength(32)]`
when set) threaded into `NpgsqlTenantSessionBinder`'s constructor and
`EnableTenantRowLevelSecurity(...)`'s new `crossTenantEscapeToken` parameter — defaults to the legacy
`"on"` literal when omitted, so existing deployments/tests are unaffected, but a real per-deployment
secret closes the gap once configured. `RowLevelSecurityMigrationBuilderExtensions`' XML doc, which
previously overstated exclusivity ("set only by ITenantSessionBinder"), now says plainly that nothing
at the DB level enforces that.

## H3 — DapperCommandService had zero tenant protection; TenantSafeDapperCommandService added
Mirrors `TenantSafeDapperReadService` exactly (fail-closed `RequireTenant()`, same rejected-call log
event `DapperPersistenceLog.TenantSafeCallRejectedNoTenant` — reused, not duplicated, since its
message text was already channel-agnostic). Key design point: for the AMBIENT-TRANSACTION path (when
enlisted in `ITransactionalUnitOfWork`), it rebinds `ITenantSessionBinder.BindAsync` on the SHARED
connection/transaction immediately before its OWN statement, live, every call — this is what makes the
"enlisted Dapper statement after scope exit" scenario correct even though Dapper statements never flow
through EF's command interceptor pipeline. `DapperCommandService` (unprotected, unchanged) got an XML
doc addition documenting the asymmetry as deliberate (single-tenant/DB-enforced-only use) rather than
an oversight, pointing to the new sibling for tenant-scoped tables.

## C4 (handed over mid-session from the Encryption wave) — EncryptAnnotationRegisteredGuardConvention missed complex-type properties
`EntityTypeConfigurationBase`/model-finalizing guard conventions in this domain use a
well-established TWO-LOOP pattern (`entityType.GetProperties()` for direct properties +
`entityType.GetComplexProperties()` → `.ComplexType.GetProperties()` for EF Core 10 complex-type
sub-properties) — `EncryptionModelConvention` (owned by the Encryption package) already had both
loops; `EncryptAnnotationRegisteredGuardConvention` (owned by this package, EfCore) only had the
first, so a model whose ONLY encrypted property lived inside a `ComplexProperty` passed the "was
`.WithEncryption()` ever called" guard silently — the exact fail-open shape the guard exists to catch,
one level deeper. Fixed by adding the identical second loop. **Test-writing gotcha hit while proving
this**: FOUR near-identical `DbContext` subclasses were needed (direct+not-applied, direct+applied,
complex+not-applied, complex+applied), never ONE class toggled by an instance-level constructor flag —
EF Core's default model cache key is derived from the CONTEXT TYPE (+ options), not instance state, so
two instances of the SAME context type sharing a test process silently share whichever one built the
model FIRST. Confirmed by writing it wrong first: every "should throw" case reported "no exception was
thrown" because it silently reused the OTHER scenario's cached, throw-free model.

## C5 — bulk ExecuteUpdateAsync could write plaintext into an .Encrypt(...) column
`BulkSpecificationGuard.ValidateSetters` gained an `IEntityType? entityType` parameter; for each
resolved (fail-closed-checked) property name it now also checks
`entityType?.FindProperty(name)?.FindAnnotation(PersistenceModelAnnotationNames.Encrypt) is not null`
and rejects with `UnsupportedSpecificationException`. `EfRepository.ExecuteUpdateAsync` passes
`DbContext.Model.FindEntityType(typeof(TAggregate))`. Reads the SAME bare annotation-name constant
(`SharedKernel.Persistence.EfCore.Extensibility.PersistenceModelAnnotationNames.Encrypt`) the
Encryption package's real `.Encrypt(...)` extension sets — this constant is deliberately declared in
the LOWER package (EfCore) precisely so a check like this never needs a reference to the Encryption
package. Test fixture applied the SAME raw annotation directly (`HasAnnotation(...Encrypt, "purpose")`)
without referencing the Encryption package at all — but had to ALSO set `EncryptApplied` (else it trips
the UNRELATED C4 guard from the same session, a different check).

## M — Build() idempotency guard and multi-context last-wins guard: also already fixed, only tests missing
Same pattern as H1 — `EfCorePersistenceBuilder.Build()` already had both guards (checking for an
existing keyed `IDbContextFactory<TContext>` registration under `InnerFactoryKey`, and checking for an
existing unkeyed `SharedKernelDbContext` registration from a DIFFERENT `TContext`), with detailed
comments explaining the stack-overflow/last-wins failure modes they prevent. Added the tests the
production code had no coverage for.

## M — scoped assembly scan dropped navigation-only children
`SharedKernelDbContext.OnModelCreating`'s `ExposedEntityTypes` used to be a reflection walk over the
CONTEXT TYPE's own `DbSet<T>` properties only — a child entity reachable ONLY via a navigation (no
`DbSet<T>` of its own) was in the EF model (auto-discovered) but its OWN `IEntityTypeConfiguration<T>`
was filtered out by the scoped-scan predicate, silently losing column config AND any `.Encrypt(...)`
annotation. **Fix, verified empirically (not assumed): read `modelBuilder.Model.GetEntityTypes()`
directly, INSIDE `OnModelCreating`, instead of reflecting over CLR DbSet properties.** EF Core's own
`DbSet<T>` auto-discovery + the convention pipeline that follows a root type's navigations both run
BEFORE `OnModelCreating`'s body starts, so by the time this predicate runs, the model already contains
every navigation-reachable type — using EF's own authoritative discovery instead of reinventing which
navigations it would traverse (collections vs. references, owned vs. not, ignored properties, etc.).
Confirmed via a real test with a `ScopeParentWithChild`/`ScopeNavigationChild` fixture (child has NO
DbSet, only a dedicated `IEntityTypeConfiguration<T>`) — the child's config (MaxLength 789) genuinely
applies. The PRE-EXISTING cross-context-bleed test (`ScopeWidget`/`ScopeGadget`, no navigation between
them) still passes unchanged — confirms the fix doesn't regress the original W7 bleed fix, since an
UNRELATED type never gets added to the model by navigation discovery in the first place.
`AdditionalConfiguredEntityTypes` is now a true last-resort manual escape hatch, not the only way to
reach a non-DbSet type.

## M — ICrossTenantScope diagnostics
Deliberately METRICS-ONLY, no `ILogger`: `SharedKernel.Persistence.Abstractions`'s OWN `PersistenceLog.cs`
(EfCore package) doc comment explicitly records the design decision that Abstractions is "a pure
interface library with no DI-resolved ILogger consumer" and reserves no EventId sub-block — adding
`[LoggerMessage]` there would contradict an already-shipped, explicit design note. `CrossTenantScope`
is also a process-wide SINGLETON, so it can never safely constructor-capture a scoped
`ICurrentActorContext` (the exact captive-dependency class of bug this whole refactor wave spent 7
waves fixing) — the actor is threaded through explicitly instead: `Enter(string? actorId = null)`,
recorded on a BCL `System.Diagnostics.Metrics.Meter` counter
(`persistence.cross_tenant_scope_entries`, tag `persistence.actor_id`) under the meter name
`"SharedKernel.Persistence"` — the SAME name EfCore's `PersistenceMeter` already uses, so BOTH are
picked up by the ONE existing `13.ServiceDefaults.WithPersistenceTelemetry` `AddMeter(...)` call with
zero further coordination (a lower layer can't reference the higher layer's name CONSTANT, so the
literal is deliberately duplicated, not shared). Every `Enter()` call is counted, including nested/
re-entrant ones — each call site is an independent, attributable decision to bypass isolation. Also
fixed `ICrossTenantScope`'s XML doc, which literally still said "no enforcement yet" / "a later phase
(W2) wires this" despite enforcement having shipped several waves earlier — a stale doc directly
contradicting shipped behavior in a public API doc comment.

## Architecture test: 06-layering lock (SharedKernelLayeringRules.PersistenceNeverReferencesApplicationOrSecurity)
Two independent mechanisms, deliberately — a NetArchTest IL-level rule alone can't cheaply cover every
TEST project without an unprecedented pile of test-project `ProjectReference`s on the governance
project (the established pattern in `SharedKernel.ArchitectureTests.Tests.csproj` only ever references
PRODUCTION assemblies for "real assembly" checks):
1. `SharedKernelLayeringRules.PersistenceNeverReferencesApplicationOrSecurity(Assembly)` — the standard
   `NotHaveDependencyOn("SharedKernel.Application")`/`NotHaveDependencyOn("SharedKernel.Security")`
   shape, proven against all 7 real, compiled 06.Persistence PRODUCTION assemblies via `[Theory]`.
2. A Roslyn SOURCE-TREE scan (`PersistenceSourceTree_NoProductionOrTestFileReferencesApplicationOrSecurity`)
   walking every `.cs` file physically under `06.Persistence` (production AND test, found by walking up
   from `AppContext.BaseDirectory` to `Platform.SharedKernel.slnx`), checking every `NameSyntax` node's
   own text for the forbidden prefixes. `NameSyntax` nodes never include comment trivia, so this
   correctly ignores doc comments that DISCUSS the removed dependencies (several exist, deliberately)
   while catching a real `using` directive (reached through its `Name` child, same as any other
   qualified reference — no special-casing needed).

**This test found FIVE genuine, previously-unnoticed dead `using SharedKernel.Security.Abstractions;`
directives** (`EfCorePersistenceBuilderNewFeaturesTests.cs`,
`XminConcurrencyTokenConventionTests.cs`, `ConcurrencyIntegrationTests.cs`,
`ReadReplicaRoutingIntegrationTests.cs`, `TransientFaultRetryIntegrationTests.cs`) — leftovers from
before the W1 refactor removed the real Security dependency, never cleaned up, with ZERO actual type
usage in any of them. A plain `grep -rl` for the same strings ALSO found these files, but a first pass
mis-triaged them as "doc-comment noise" by verifying only 4 of the 5 files individually and assuming
the rest matched the same pattern — the lesson: when spot-checking grep hits for "is this real or just
prose," verify EVERY file individually, never extrapolate from a subset, especially right before
relying on the conclusion to design a test.

## Cross-domain "note only" items — DO NOT re-attempt without re-verifying current file state
- `EfAuditTrailWriter.cs:195` (Auditing package, not owned): required a ONE-TOKEN mechanical fix
  (`cancellationToken` positional argument → `cancellationToken: cancellationToken` named) purely
  because `IAdvisoryTransactionLock.AcquireAsync` gained a new optional `TimeSpan? timeout` parameter
  BEFORE `cancellationToken`. Made this exact edit (zero semantic change, pure argument-binding fix)
  since leaving it broken would fail the mandated "full solution green" check and the fix carries no
  design judgment. Flagged explicitly in the handback report regardless.

## Multi-agent coordination reality (three persistence-phase-implementer-shaped agents sharing one repo)
- `git status`/build failures from files you don't own are NORMAL mid-session noise, not your bug —
  confirmed transient `05.Application.Behaviors` / `EfCore.Encryption` / `EfCore.Auditing` build
  breaks during this session were from OTHER agents' concurrent in-flight edits, gone by the time of
  the final full-solution build.
- Coordinator relays cross-agent findings live (mid-task messages naming specific files/line numbers
  in packages you own but weren't originally scoped to touch) — treat these as authoritative, additional
  task items, not noise, and re-verify the named file/line yourself before trusting the description
  (in this session, one relayed finding's suggested fix location was one function; the actual fix
  applied was structurally the same but independently re-derived from reading the real code).
- **`| tail -N` on a background `dotnet test`/`dotnet build` command hides the REAL exit code** — in a
  bash pipe, `cmd1 | tail -N`'s reported exit status is `tail`'s, not `cmd1`'s, so a background task
  can report "exited with code 0" even when the underlying build/test genuinely failed, if the failure
  happened to still let a fixed number of trailing lines print. Always redirect long-running background
  build/test commands to a real log file (`> file.log 2>&1; echo EXIT=$?`) and grep the file afterward,
  never pipe straight to `tail` when the actual exit code matters for a go/no-go decision.
- Regenerating `PublicAPI.Unshipped.txt` entries by hand is genuinely error-prone for anything beyond a
  trivial signature (nullability annotations on generic type arguments — `object` vs `object!` inside
  `InterceptionResult<T>` — caused two real RS0016 round-trips in this session). Always let a real
  `dotnet build` of the SPECIFIC project be the source of truth for the exact required text, never trust
  a hand-typed line to be right on the first try.

## Follow-up item after first handback: startup guard for retry-vs-transactional-UoW (received, resolved, second handback)
- Coordinator can and does send a further small item immediately after a `SubagentHandback` fires,
  in the same turn's tool-result batch — explicitly offering "say so and I'll take this one myself" as
  an out. Read the item fully before deciding: if it's small, clearly inside your ownership, and you
  still have full context loaded, finishing it yourself is usually cheaper than a hand-off round trip.
- **Pattern worth reusing: when a `Build()`-time eager check cannot see far enough (crosses a package
  boundary this package must not reference), add a SECOND check to the existing conditionally-registered
  `IHostedService` startup validator instead of inventing a new hosted service or trying to probe a
  throwaway `ServiceProvider`/`DbContextOptions` inside `Build()` itself.** Concretely:
  `EfCorePersistenceExtensions.Build()` already had an eager check —
  `if (_transientFaultRetryOptions is not null && _transactionalUnitOfWorkEnabled) throw ...` — but
  `_transientFaultRetryOptions` is set ONLY by `.WithTransientFaultRetry()` (a pure discoverability
  flag, decoupled BY DESIGN from whether `UsePostgreSQL(..., maxRetryCount)` was actually called inside
  the `configureDb` delegate — `TransientFaultRetryOptions`'s own doc says so explicitly). So a consumer
  enabling Npgsql-native retry directly, without ever calling `.WithTransientFaultRetry()`, slipped past
  that check. Rather than trying to probe `configureDb`'s effect on execution-strategy state inside
  `Build()` (would need a throwaway root `ServiceProvider`, risking eager construction of whatever
  singletons the CONSUMER already registered before calling `.Build()` — a real, if narrow, side-effect
  risk), the fix extends `Diagnostics/PersistenceContextWiringValidator<TContext>` (already an
  `IHostedService`, already conditionally registered by `Build()`, already constructs one real
  `TContext` via `IDbContextFactory<TContext>` inside a proper DI scope to verify interceptor
  attachment) with one more check reading `context.Database.CreateExecutionStrategy().RetriesOnFailure`
  — the EXACT SAME live, provider-neutral signal `EfTransactionalUnitOfWork.BeginTransactionAsync`
  itself already checks at runtime, so there is no duplicated/driftable logic and no need to reference
  Npgsql to detect its retry configuration. Constructing a `DbContext` to read its execution strategy
  needs no database connection (confirmed by this same validator's pre-existing doc comment for its
  original check) — this is a real, general pattern for this domain: EF Core's
  `DatabaseFacade.CreateExecutionStrategy()`/`IExecutionStrategy.RetriesOnFailure` is provider-neutral
  even though ENABLING a retrying strategy (`EnableRetryOnFailure`) is provider-specific, so READING
  live execution-strategy state is always a legal, `06.Persistence.EfCore`-safe way to detect retry
  configuration this package could never see through its own `configureDb` delegate.
- `Build()`'s `hasCapabilitiesToVerify` gate (deciding whether the wiring-validator hosted service is
  registered at all) must independently include EVERY capability the validator checks — it did not
  include `_transactionalUnitOfWorkEnabled` before this fix, so a bare `.WithTransactionalUnitOfWork()`
  call with no other opt-in capability skipped registering the validator entirely, a second instance of
  the same underlying gap. When adding a new check to this validator, always check whether its OWN
  enabling flag also needs adding to that gate expression, not just to the validator's own logic.
- Testing a "retrying execution strategy" scenario needs NO Postgres/Npgsql at all:
  `.ReplaceService<IExecutionStrategyFactory, TImplementation>()` is a provider-neutral EF Core API
  (works identically on SQLite), and this codebase already has a ready-made fixture,
  `AlwaysRetryStrategyFactory`/`AlwaysRetryStrategy` (`SharedKernel.Persistence.EfCore.Tests.Diagnostics`
  namespace, defined for `PersistenceRetryDiagnosticListenerTests`, `internal` but reachable from any
  test in the same assembly via `using SharedKernel.Persistence.EfCore.Tests.Diagnostics;` — see
  `RetryExhaustionLoggingTests.cs` for the established reuse pattern). If the test only needs
  `RetriesOnFailure == true` as a property read (no actual retry LOOP needs to execute — i.e. no
  `FaultInjectingInterceptor` / no real `SaveChangesAsync` against a failing operation), it does NOT
  need to join the shared `"RetryDiagnostics"` xUnit collection those fault-injecting tests use to avoid
  cross-contaminating `PersistenceRetryDiagnosticListener`'s process-wide observation — that serialization
  requirement only applies when genuine retry attempts actually run.
- FluentAssertions gotcha (re-confirmed, cost one build round-trip): `act.Should().ThrowAsync<T>()
  .WithMessage(...)` returns `Task<ExceptionAssertions<T>>`, NOT `ExceptionAssertions<T>` — `.Which`
  must be accessed AFTER awaiting the whole chain (`(await act.Should().ThrowAsync<T>()
  .WithMessage(...)).Which...`), unlike the synchronous `.Throw<T>()` form used elsewhere in the same
  file, where `.WithMessage(...).Which...` chains directly with no `await` in between. Easy to typo by
  copy-pasting the sync pattern into an async test.
