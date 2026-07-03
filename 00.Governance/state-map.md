# 00.Governance — State Map

> **What this file is:** Phase and task tracker for all work within `00.Governance`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.00.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition | Root Backlog ID |
|-----------|-------------------|-------------------|-----------------|
| `SK.00.Design` | Design | All tasks in Phase: Design are `●` | — |
| `SK.00.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` | — |
| `SK.00.Core` | Core | All tasks in Phase: Core are `●` | — |
| `SK.00.Tests` | Tests | All tasks in Phase: Tests are `●` | — |
| `SK.00.Docs` | Docs | All tasks in Phase: Docs are `●` | — |
| `SK.00.Published` | Published | All tasks in Phase: Published are `●` | — |
| `SK.00.GuardPurity` | Guard Purity Enforcement | All tasks in Phase: Guard Purity Enforcement are `●` | — |
| `SK.00.CachingEnforcement` | Caching Abstractions Enforcement | All tasks in Phase: Caching Abstractions Enforcement are `●` | — |
| `SK.00.DomainLayerPurity` | Domain Layer Purity Enforcement | All tasks in Phase: Domain Layer Purity Enforcement are `●` | — |
| `SK.00.DomainGoldStandard` | Domain Gold-Standard Architecture Rules | All tasks in Phase: Domain Gold-Standard Architecture Rules are `●` | — |
| `SK.00.ContractsPurity` | Contracts Layer Purity Architecture Rules | All tasks in Phase: Contracts Layer Purity Architecture Rules are `●` | — |
| `SK.00.PersistenceEnforcement` | Persistence Architecture Enforcement | All tasks in Phase: Persistence Architecture Enforcement are `●` | — |
| `SK.00.PersistenceEnforcement2` | Persistence Architecture Rules Phase 2 — Interface Migration Enforcement | All tasks in Phase: Persistence Architecture Rules Phase 2 are `●` | — |
| `SK.00.PersistenceContractCompleteness` | Architecture Rule — IUserContext Audit String Adapter and Repository Contract Completeness | All tasks in Phase: IUserContext Audit String Adapter and Repository Contract Completeness are `●` | — |
| `SK.00.EfCorePackageHygiene` | Governance: Architecture Rules for EfCore Package Hygiene | All tasks in Phase: EfCore Package Hygiene Architecture Rules are `●` | — |
| `SK.00.TenantedDbContextGuard` | Governance: TenantedDbContext Tenant-Filter Guard Architecture Rule | All tasks in Phase: TenantedDbContext Tenant-Filter Guard are `●` | — |
| `SK.00.EncryptionPatternGuard` | Governance: Architecture Rules for DB Encryption Pattern Correctness | All tasks in Phase: EncryptionPatternGuard are `●` | — |
| `SK.00.MessagingArchRules` | Governance: Messaging Architecture Rules — No Raw IBus Injection, No IMessageBus Singleton, No Domain Messaging, No Hardcoded Queue URIs | All tasks in Phase: Messaging Architecture Rules are `●` | — |
| `SK.00.ExtendedMessagingArchRules` | Governance: Extended Messaging Architecture Rules — Fault Consumers, Scheduling, Singleton Guards | All tasks in Phase: Extended Messaging Architecture Rules are `●` | P-133 |
| `SK.00.RedisTopology` | Governance: Architecture Rules for Redis Package Topology | All tasks in Phase: Redis Package Topology Architecture Rules are `●` | P-145 |
| `SK.00.ReflectionGuard` | Governance: Architecture Rule Forbidding Reflection-Based Generic Method Invocation | All tasks in Phase: Reflection Guard Architecture Rules are `●` | P-153 |
| `SK.00.CommunicationArchRules` | Governance: Architecture Rules for Communication Layer | All tasks in Phase: Communication Layer Architecture Rules are `●` | P-159 |
| `SK.00.WO026CommunicationQuality` | Governance: Architecture Rules for WO-026 Communication Quality Improvements | All tasks in Phase: WO-026 Communication Quality are `●` | P-167 |
| `SK.00.HealthCheckConstantsGuard` | Governance: Architecture Rule Banning Bare Health-Check String Literals Where a Constants Class Exists | All tasks in Phase: Health-Check Constants Guard are `●` | P-178 |
| `SK.00.PresentationArchRules` | Governance: Architecture Rules Banning Hand-Rolled ProblemDetails and Inline Result-to-HTTP Branching | All tasks in Phase: Architecture Rules Banning Hand-Rolled ProblemDetails and Inline Result-to-HTTP Branching are `●` | P-199 |
| `SK.00.ApplicationPipelineArchRules` | Governance: Architecture Enforcement for the Extended Application Pipeline | All tasks in Phase: Architecture Enforcement for the Extended Application Pipeline are `●` | P-225 |
| `SK.00.CryptoDelegationAndUowSeamGuard` | Governance: Architecture Rules Locking the Cryptography Delegation and IUnitOfWork Bridge | All tasks in Phase: Architecture Rules Locking the Cryptography Delegation and IUnitOfWork Bridge are `●` | P-229 |
| `SK.00.MetricsOutcomeTagAndMisregistrationGuard` | Governance: Architecture Enforcement for WO-038 Application Audit Findings | All tasks in Phase SK.00.MetricsOutcomeTagAndMisregistrationGuard are `●` | P-235 |
| `SK.00.DomainEventDispatcherReflectionExemption` | Governance: Register MediatRDomainEventDispatcher's SK0012 Reflection Exemption | All tasks in Phase SK.00.DomainEventDispatcherReflectionExemption are `●` | P-240 |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement SK0001 DirectDateTimeUsageAnalyzer | SK.00.Core | SharedKernel.Analyzers | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| S-01 Retarget Analyzers to netstandard2.0 | SK.00.Scaffold | Confirm Roslyn version ships netstandard2.0 compatible ref assemblies |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
|---------|--------------|:-----:|-------|
| `SharedKernel.Analyzers` | — | `○` | Must target `netstandard2.0`, not `net10.0` |
| `SharedKernel.ArchitectureTests` | — | `○` | Test-only; `PrivateAssets="all"` |
| `SharedKernel.Benchmarks` | — | `○` | Dev-only; not published to production feed |
| `SharedKernel.Linter` | — | `○` | Content-only NuGet; no DLL output |

---

## Phase: Design <!-- phase-key: SK.00.Design -->

> Finalize all diagnostic rule IDs, analyzer trigger conditions, architecture-test contract shapes, benchmark config shape, and linter distribution strategy before any implementation begins.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | Define Roslyn diagnostic ID registry (SK0001–SK0005): rule name, category, default severity, trigger condition, compliant fix | SharedKernel.Analyzers | `●` |
| D-02 | Define SK0001 `DirectDateTimeUsage`: detect `DateTime.UtcNow`, `DateTime.Now`, `DateTimeOffset.UtcNow` outside `SharedKernel.Primitives` namespace | SharedKernel.Analyzers | `●` |
| D-03 | Define SK0002 `DirectMicrosoftFeatureManagerUsage`: detect direct reference to `Microsoft.FeatureManagement.IFeatureManager` in constructor params, field/property declarations | SharedKernel.Analyzers | `●` |
| D-04 | Define SK0003 `RawExceptionThrow`: detect `throw new Exception(...)` and `throw new ApplicationException(...)` without an `Error` payload argument | SharedKernel.Analyzers | `●` |
| D-05 | Define SK0004 `NullErrorReturn`: detect `return null` literal inside methods whose declared return type is `Error` or `Error?` | SharedKernel.Analyzers | `●` |
| D-06 | Define SK0005 `StringOnlyExceptionConstructor`: detect `new {SharedKernelException subclass}(string)` constructors that bypass the required `Error` payload | SharedKernel.Analyzers | `●` |
| D-07 | Define `ArchitectureRuleBase` abstract shape — assembly discovery pattern, FluentAssertions integration, `AssertRule` contract | SharedKernel.ArchitectureTests | `●` |
| D-08 | Define `SharedKernelLayeringRules` static class — one `IArchRule` factory per hard layering constraint from the root CLAUDE.md; include the three absolute-hard rules (Domain never references Persistence/Messaging, Application never references concrete infra) | SharedKernel.ArchitectureTests | `●` |
| D-09 | Define `SharedKernelBenchmarkConfig` shape — job selection (Job.Short), diagnosers (MemoryDiagnoser), exporter (MarkdownExporter deterministic), HardwareCounters disabled | SharedKernel.Benchmarks | `●` |
| D-10 | Define linter distribution strategy: which files ship as content, `.props`/`.targets` auto-import mechanism, `PrivateAssets="all"` enforcement, CSharpier version pinning | SharedKernel.Linter | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.00.Scaffold -->

> Wire up correct target frameworks, NuGet references, folder structures, and solution registration — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Retarget `SharedKernel.Analyzers.csproj` to `netstandard2.0`; add `Microsoft.CodeAnalysis.CSharp` NuGet ref; set `<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>` | SharedKernel.Analyzers | `●` |
| S-02 | Add `NetArchTest.eNt` and `FluentAssertions` NuGet refs to `SharedKernel.ArchitectureTests.csproj`; set `<IsPackable>true</IsPackable>` | SharedKernel.ArchitectureTests | `●` |
| S-03 | Add `BenchmarkDotNet` NuGet ref to `SharedKernel.Benchmarks.csproj`; set `<IsPackable>false</IsPackable>` (dev-only) | SharedKernel.Benchmarks | `●` |
| S-04 | Configure `SharedKernel.Linter.csproj` as content-only: `<IncludeBuildOutput>false</IncludeBuildOutput>`, `<ContentTargetFolders>content</ContentTargetFolders>`, `<NoWarn>NU5128</NoWarn>` | SharedKernel.Linter | `●` |
| S-05 | Create folder structure `Diagnostics/`, `CodeFixes/` in `SharedKernel.Analyzers/` | SharedKernel.Analyzers | `●` |
| S-06 | Create folder structure `Rules/`, `Helpers/` in `SharedKernel.ArchitectureTests/` | SharedKernel.ArchitectureTests | `●` |
| S-07 | Create folder structure `Configurations/`, `Baselines/` in `SharedKernel.Benchmarks/` | SharedKernel.Benchmarks | `●` |
| S-08 | Create content folders `content/`, `content/build/` in `SharedKernel.Linter/`; place empty `.editorconfig`, `.csharpierrc.json`, `.props`, `.targets` stubs | SharedKernel.Linter | `●` |
| S-09 | Create `SharedKernel.Analyzers.Tests/` with xUnit + `Microsoft.CodeAnalysis.CSharp.Testing.XUnit` NuGet refs | SharedKernel.Analyzers | `●` |
| S-10 | Register all five projects (`SharedKernel.Analyzers`, `SharedKernel.Analyzers.Tests`, `SharedKernel.ArchitectureTests`, `SharedKernel.Benchmarks`, `SharedKernel.Linter`) in `Platform.SharedKernel.slnx` under solution folder `00.Governance` | All | `●` |

---

## Phase: Core <!-- phase-key: SK.00.Core -->

> Full implementation of all analyzer rules, architecture-test base classes, benchmark config, and linter distribution content.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `AnalyzerBase` abstract class: category constants (`Usage`, `Design`), `CreateDescriptor` factory with `HelpLinkUri` wired to README anchor | SharedKernel.Analyzers | `●` |
| C-02 | Implement `SK0001 DirectDateTimeUsageAnalyzer` — `SyntaxKind.SimpleMemberAccessExpression` walker flagging `DateTime.UtcNow`, `DateTime.Now`, `DateTimeOffset.UtcNow`; suppress inside `SharedKernel.Primitives` namespace | SharedKernel.Analyzers | `●` |
| C-03 | Implement `SK0002 DirectMicrosoftFeatureManagerAnalyzer` — detect `Microsoft.FeatureManagement.IFeatureManager` symbol in constructor parameter types and field/property declarations | SharedKernel.Analyzers | `●` |
| C-04 | Implement `SK0003 RawExceptionAnalyzer` — detect `throw new Exception(...)` / `throw new ApplicationException(...)` by checking thrown type's base-type chain; suppress if constructor argument includes `Error` type | SharedKernel.Analyzers | `●` |
| C-05 | Implement `SK0004 NullErrorReturnAnalyzer` — detect `return null` literal in methods whose return type symbol is `Error` or `Error?` (nullable `Error`) | SharedKernel.Analyzers | `●` |
| C-06 | Implement `SK0005 StringOnlyExceptionConstructorAnalyzer` — detect `ObjectCreationExpression` for types derived from `SharedKernelException` where the first and only argument is a string literal | SharedKernel.Analyzers | `●` |
| C-07 | Implement `ArchitectureRuleBase` abstract class with `GetAssemblyTypes`, `ShouldNotReference`, and `AssertRule` using NetArchTest fluent API | SharedKernel.ArchitectureTests | `●` |
| C-08 | Implement `SharedKernelLayeringRules` static class: `CoreReferencesNothing`, `CachingReferencesOnlyCore`, `DomainReferencesOnlyCore`, `ContractsReferencesOnlyCoreAndDomain`, `DomainNeverReferencesPersistence`, `DomainNeverReferencesMessaging`, `ApplicationNeverReferencesConcreteInfrastructure`, `TestingNeverReferencedByProduction` | SharedKernel.ArchitectureTests | `●` |
| C-09 | Implement `SharedKernelBenchmarkConfig` : `ManualConfig` — `Job.Short`, `MemoryDiagnoser.Default`, `MarkdownExporter` with deterministic column order, HardwareCounters disabled | SharedKernel.Benchmarks | `●` |
| C-10 | Implement `[SharedKernelBenchmark]` attribute (shorthand for `[Config(typeof(SharedKernelBenchmarkConfig))]`) | SharedKernel.Benchmarks | `●` |
| C-11 | Author `.editorconfig`: indent_style=space, indent_size=4, charset=utf-8-bom, end_of_line=crlf, trim_trailing_whitespace=true; C#-specific overrides for using directives, namespace scope | SharedKernel.Linter | `●` |
| C-12 | Author `.csharpierrc.json`: printWidth=120, tabWidth=4, useTabs=false | SharedKernel.Linter | `●` |
| C-13 | Author `SharedKernel.Linter.props`: import `.editorconfig` via `<AdditionalFiles>`; define `<CSharpierVersion>` property | SharedKernel.Linter | `●` |
| C-14 | Author `SharedKernel.Linter.targets`: add `CSharpierCheck` build target that runs `dotnet csharpier --check` in CI (`$(ContinuousIntegrationBuild)` guard) | SharedKernel.Linter | `●` |

---

## Phase: Tests <!-- phase-key: SK.00.Tests -->

> Analyzer unit tests using `Microsoft.CodeAnalysis.CSharp.Testing.XUnit`. No integration tests needed.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Analyzer test SK0001 (fire path): verify diagnostic fires when `DateTime.UtcNow` used in a non-Primitives class | SharedKernel.Analyzers.Tests | `●` |
| T-02 | Analyzer test SK0001 (pass path): verify no diagnostic when usage is inside a class in `SharedKernel.Primitives` namespace | SharedKernel.Analyzers.Tests | `●` |
| T-03 | Analyzer test SK0001 (DateTimeOffset variant): verify `DateTimeOffset.UtcNow` also triggers SK0001 | SharedKernel.Analyzers.Tests | `●` |
| T-04 | Analyzer test SK0002 (fire path): verify diagnostic fires on constructor parameter typed `Microsoft.FeatureManagement.IFeatureManager` | SharedKernel.Analyzers.Tests | `●` |
| T-05 | Analyzer test SK0002 (pass path): verify no diagnostic when `SharedKernel.FeatureManagement.IFeatureManager` is used | SharedKernel.Analyzers.Tests | `●` |
| T-06 | Analyzer test SK0003 (fire path): verify diagnostic fires on `throw new Exception("message")` | SharedKernel.Analyzers.Tests | `●` |
| T-07 | Analyzer test SK0003 (pass path): verify no diagnostic on `throw new DomainException(error)` | SharedKernel.Analyzers.Tests | `●` |
| T-08 | Analyzer test SK0004 (fire path): verify diagnostic fires on `return null` in an `Error?`-returning method | SharedKernel.Analyzers.Tests | `●` |
| T-09 | Analyzer test SK0004 (pass path): verify no diagnostic on `return Error.None` | SharedKernel.Analyzers.Tests | `●` |
| T-10 | Analyzer test SK0005 (fire path): verify diagnostic fires on `new DomainException("message")` string-only constructor | SharedKernel.Analyzers.Tests | `●` |
| T-11 | Analyzer test SK0005 (pass path): verify no diagnostic on `new DomainException(error)` with Error payload | SharedKernel.Analyzers.Tests | `●` |
| T-12 | Architecture test: verify `SharedKernelLayeringRules.DomainNeverReferencesPersistence` catches a contrived violation assembly; verify it passes for a clean assembly | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Docs <!-- phase-key: SK.00.Docs -->

> XML doc comments on all public APIs, rule documentation, and usage guide README.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | XML doc all public analyzer types, each `DiagnosticDescriptor`, and `AnalyzerBase` | SharedKernel.Analyzers | `●` |
| DO-02 | XML doc `ArchitectureRuleBase` and every method on `SharedKernelLayeringRules` | SharedKernel.ArchitectureTests | `●` |
| DO-03 | XML doc `SharedKernelBenchmarkConfig` and `[SharedKernelBenchmark]` attribute | SharedKernel.Benchmarks | `●` |
| DO-04 | Write `00.Governance/README.md`: how to reference `SharedKernel.Analyzers` (analyzer NuGet), how to use `SharedKernelLayeringRules` in an architecture test, how to apply `SharedKernel.Linter` | All | `●` |
| DO-05 | Document each diagnostic rule (SK0001–SK0005) in README with: rationale, violating example, compliant fix, suppression instructions | SharedKernel.Analyzers | `●` |

---

## Phase: Published <!-- phase-key: SK.00.Published -->

> NuGet packaging metadata, pack, and publish. `SharedKernel.Benchmarks` is excluded (dev-only).

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Add NuGet metadata to `SharedKernel.Analyzers.csproj`: set `<IncludeBuildOutput>false</IncludeBuildOutput>`, `<DevelopmentDependency>true</DevelopmentDependency>`, add `<Analyzer>` item group pointing to the assembly | SharedKernel.Analyzers | `●` |
| P-02 | Add NuGet metadata to `SharedKernel.ArchitectureTests.csproj`: description, version, authors, `<PrivateAssets>all</PrivateAssets>` guidance in README | SharedKernel.ArchitectureTests | `●` |
| P-03 | Add NuGet metadata to `SharedKernel.Linter.csproj`: description, version, authors; validate that `.editorconfig` and `.csharpierrc.json` are included as content in the packed `.nupkg` | SharedKernel.Linter | `●` |
| P-04 | Pack and publish `SharedKernel.Analyzers` to local feed; verify analyzer activates in a consumer project (SK0001 diagnostic fires on `DateTime.UtcNow`) | SharedKernel.Analyzers | `●` |
| P-05 | Pack and publish `SharedKernel.ArchitectureTests` to local feed; verify consumer can subclass `ArchitectureRuleBase` and call `SharedKernelLayeringRules` | SharedKernel.ArchitectureTests | `●` |
| P-06 | Pack and publish `SharedKernel.Linter` to local feed; verify `.editorconfig` and `.csharpierrc.json` appear in consumer project after restore | SharedKernel.Linter | `●` |

---

## Phase: Guard Purity Enforcement <!-- phase-key: SK.00.GuardPurity -->

> Enforce the guard clause purity contract: `IGuardClause` extension methods on the functional path (`Guard.Against.*`) must never throw — they must return `Error?`. Architecture tests load the `SharedKernel.Guards` assembly and assert the absence of throw IL opcodes in all `IGuardClause` extension methods outside the `Guard.Throw` companion class.

### GuardPurity — Goal

The two-path guard design (`Guard.Against.*` returns `Error?`, `Guard.Throw.*` throws) only delivers its value if the functional path is provably pure. This phase adds an architecture test that loads `SharedKernel.Guards` and uses a Mono.Cecil IL inspection predicate — bundled inside `NetArchTest.eNt` — to assert that no method on any type implementing `IGuardClause` contains a `throw` instruction, with an explicit exclusion for the `Guard.Throw` companion class. A Roslyn analyzer (SK0006) is also specified here; its implementation is a follow-on Core task within this phase.

### GuardPurity — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`, `SharedKernel.Analyzers`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/GuardPurityRules.cs` — `GuardPurityRules` static class housing `GuardAgainstMethodsMustNotThrow()` and the exclusion list
  - `SharedKernel.ArchitectureTests/Predicates/DoesNotContainThrowIlPredicate.cs` — custom `ICustomRule` / `MeetCustomPredicate` wrapper that inspects method bodies via Mono.Cecil `MethodBody.Instructions` for `OpCodes.Throw`
- Modified files:
  - `SharedKernel.ArchitectureTests/SharedKernelLayeringRules.cs` — no change; guard purity lives in its own static class `GuardPurityRules`
  - `00.Governance/CLAUDE.md` — refresh architecture test contracts section, add SK0006 to registry
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### GuardPurity — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0006 | GuardClauseThrow | Design | Warning | A method on a type implementing `IGuardClause` that is not inside the `Guard.Throw` class contains a `throw` expression — violates functional-path purity contract |

### GuardPurity — Implementation Rules

1. `GuardPurityRules.GuardAgainstMethodsMustNotThrow()` must load `SharedKernel.Guards` via `Types.InAssembly(typeof(IGuardClause).Assembly)` and apply `DoesNotContainThrowIlPredicate` scoped to types not named `Guard.Throw` (full name exclusion: `SharedKernel.Guards.Guard+Throw` or the configured companion class name).
2. `DoesNotContainThrowIlPredicate` must iterate `TypeDefinition.Methods` via `Mono.Cecil.TypeDefinition` (accessible through NetArchTest's internal Mono.Cecil bundling via reflection on the `TypeDefinition` property of `IType`) and check each `MethodDefinition.Body.Instructions` for `OpCodes.Throw`. If any throw opcode is found, the predicate fails for that type.
3. Because NetArchTest.eNt bundles Mono.Cecil internally (not as a transitive public NuGet reference), `DoesNotContainThrowIlPredicate` must access it through NetArchTest's `MeetCustomPredicate` API which exposes `IType` — the Mono.Cecil `TypeDefinition` is accessible as `IType.Definition` (internal property). If the API surface does not expose `TypeDefinition` publicly, the predicate must add `Mono.Cecil` as an explicit NuGet reference to `SharedKernel.ArchitectureTests` (version pinned at `>= 0.11.5`).
4. The exclusion for `Guard.Throw` is by full type name string comparison, not namespace prefix — the companion class may be a nested class (`Guard+Throw`) and must be matched exactly.
5. `GuardPurityRules` must be in `SharedKernel.ArchitectureTests`; it must not reference any runtime package from other SharedKernel domains.
6. SK0006 `GuardClauseThrowAnalyzer` must fire on `ThrowStatementSyntax` or `ThrowExpressionSyntax` nodes inside methods declared on types that implement `IGuardClause`, with containment check to exclude methods declared in the `Guard.Throw` class. The `IGuardClause` symbol check is by full metadata name `SharedKernel.Guards.IGuardClause`.
7. SK0006 severity is `Warning` at introduction; the path to `Error` is gated on confirmation that `Guard.Throw` exclusion logic is stable (not causing false positives in the `Throw.*` extension class).

### GuardPurity — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/GuardPurityRules.cs` | SharedKernel.ArchitectureTests | Create | Static class with `GuardAgainstMethodsMustNotThrow()` factory |
| `Predicates/DoesNotContainThrowIlPredicate.cs` | SharedKernel.ArchitectureTests | Create | Custom NetArchTest predicate — Mono.Cecil IL throw inspection |
| `Diagnostics/SK0006_GuardClauseThrowAnalyzer.cs` | SharedKernel.Analyzers | Create (Core task) | Roslyn analyzer: fires on `throw` inside `IGuardClause` method bodies outside `Guard.Throw` |

### GuardPurity — Acceptance Criteria

- [ ] `GuardPurityRules.GuardAgainstMethodsMustNotThrow()` returns an `IArchRule` that fails when loaded against an assembly containing a `throw` inside an `IGuardClause` extension method (validated by a test fixture using a contrived violation assembly)
- [ ] The same rule passes when applied to a clean `IGuardClause` implementation that returns `Error?` only
- [ ] `Guard.Throw` companion class methods are excluded — the rule must not flag legitimate throw-side extensions
- [ ] SK0006 `DiagnosticDescriptor` is registered in the analyzer registry with `HelpLinkUri` pointing to the README anchor
- [ ] Analyzer fire-path test: SK0006 fires on a minimal code snippet with `throw new Exception()` inside an `IGuardClause`-implementing method
- [ ] Analyzer pass-path test: SK0006 does not fire when the method is on a `Guard.Throw`-equivalent type

### GuardPurity — Dependencies

- Requires P-003 (`IGuardClause`, `Guard.Against`, `Guard.Throw` types in `SharedKernel.Guards`) to be complete: yes — the architecture test loads `typeof(IGuardClause).Assembly`; without that type existing the assembly load fails. The design is locked but implementation tasks are blocked on P-003.
- Unblocks: P-005 (Guard purity enforcement integrated into CI gate once this rule is at `Error` severity)

### GuardPurity — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (for `MeetCustomPredicate` API)
- `Mono.Cecil`: >= 0.11.5 (add explicitly if `NetArchTest.eNt` does not expose `IType.Definition` publicly)
- `Microsoft.CodeAnalysis.CSharp`: current pin (SK0006 follows same constraint as SK0001–SK0005)
- Target framework: `netstandard2.0` (Analyzers) / `net10.0` (ArchitectureTests)

### GuardPurity — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-11 | Define `GuardPurityRules` static class shape: `GuardAgainstMethodsMustNotThrow()` predicate design, `Guard.Throw` exclusion strategy (full type name match on `Guard+Throw`), Mono.Cecil IL opcode inspection approach via `DoesNotContainThrowIlPredicate` | SharedKernel.ArchitectureTests | `●` |
| D-12 | Define SK0006 `GuardClauseThrow` — trigger: `ThrowStatementSyntax` or `ThrowExpressionSyntax` inside method bodies on types implementing `IGuardClause` (full name: `SharedKernel.Guards.IGuardClause`); exclusion: containing type full name is `Guard+Throw` or is nested within it | SharedKernel.Analyzers | `●` |
| C-15 | Implement `DoesNotContainThrowIlPredicate` — custom NetArchTest `ICustomRule` that inspects `MethodDefinition.Body.Instructions` for `OpCodes.Throw` using Mono.Cecil; exclude property get/set accessors of compiler-generated types | SharedKernel.ArchitectureTests | `●` |
| C-16 | Implement `GuardPurityRules.GuardAgainstMethodsMustNotThrow()` — scoped to `Types.InAssembly(guardsAssembly).That().ImplementInterface(typeof(IGuardClause)).And().DoNotHaveName("Guard+Throw").Should().MeetCustomRule(new DoesNotContainThrowIlPredicate())` | SharedKernel.ArchitectureTests | `●` |
| C-17 | Implement SK0006 `GuardClauseThrowAnalyzer` — `SyntaxKind.ThrowStatement` and `SyntaxKind.ThrowExpression` walker; checks containing method's declaring type implements `IGuardClause`; skips if type name is `Guard` nested class `Throw` | SharedKernel.Analyzers | `●` |
| T-13 | Architecture test (fire path): load a contrived in-memory violation assembly with one `IGuardClause` extension that contains a `throw`; assert `GuardAgainstMethodsMustNotThrow()` rule fails and message names the offending method | SharedKernel.ArchitectureTests | `●` |
| T-14 | Architecture test (pass path): load a clean `IGuardClause` implementation that returns `Error?`; assert `GuardAgainstMethodsMustNotThrow()` rule passes | SharedKernel.ArchitectureTests | `●` |
| T-15 | Architecture test (exclusion): load an assembly where `Guard.Throw` class contains a `throw`; assert rule passes (excluded) | SharedKernel.ArchitectureTests | `●` |
| T-16 | Analyzer test SK0006 (fire path): `throw new InvalidOperationException()` inside a method on a class implementing `IGuardClause` triggers SK0006 | SharedKernel.Analyzers.Tests | `●` |
| T-17 | Analyzer test SK0006 (pass path): `throw` inside a method on `Guard.Throw` class does not trigger SK0006 | SharedKernel.Analyzers.Tests | `●` |
| DO-06 | Document guard purity rule in `00.Governance/README.md`: rationale, the two-path contract, exclusion list, SK0006 diagnostic entry with violating/compliant examples | SharedKernel.Analyzers | `●` |

---

## Phase: Caching Abstractions Enforcement <!-- phase-key: SK.00.CachingEnforcement -->

> Enforce the caching abstraction boundary: no production package outside the concrete caching packages and the composition root (`13.ServiceDefaults`) may reference `SharedKernel.Caching` or `SharedKernel.Caching.Redis` directly. Additionally, prevent `IRedisChannelService` from being misused as a substitute for `IMessageBus` in durable-messaging contexts.

### CachingEnforcement — Goal

Two complementary enforcement rules close the coupling drift vector introduced by the Caching domain (P-005, P-006, P-007). The first is a NetArchTest architecture rule asserting that only explicitly exempted assemblies may take a direct reference to the concrete caching packages; all other assemblies must use `SharedKernel.Caching.Abstractions`. The second is a Roslyn analyzer (SK0007) that warns when `IRedisChannelService` is injected into a class whose name or namespace signals durable-messaging intent (`Command`, `Event`, `DomainEvent`, or `IntegrationEvent`). Together they prevent invisible FusionCache coupling from creeping into application and domain layers, and prevent the Redis pub/sub channel from silently replacing the durable message bus.

### CachingEnforcement — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`, `SharedKernel.Analyzers`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/CachingAbstractionRules.cs` — `CachingAbstractionRules` static class housing `OnlyAllowedAssembliesMayReferenceConcreteCaching()` predicate
  - `SharedKernel.Analyzers/Diagnostics/SK0007_RedisChannelServiceMessagingSubstituteAnalyzer.cs` — Roslyn analyzer; fires when `IRedisChannelService` is injected in a messaging-context class
- Modified files:
  - `00.Governance/CLAUDE.md` — add SK0007 to diagnostic registry; add `CachingAbstractionRules` to architecture test contracts; add exemption list documentation
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### CachingEnforcement — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0007 | RedisChannelServiceMessagingSubstitute | Design | Warning | `IRedisChannelService` injected (constructor parameter, field, or property) in a class whose name or enclosing namespace contains `Command`, `Event`, `DomainEvent`, or `IntegrationEvent` — signals inappropriate use of Redis pub/sub as a durable message bus substitute |

### CachingEnforcement — Implementation Rules

1. `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching()` uses `Types.InAssembly(assembly)` and `.Should().NotHaveDependencyOn("SharedKernel.Caching")` scoped with a filter that excludes the three exempt assemblies: `SharedKernel.Caching` itself, `SharedKernel.Caching.Redis`, and any assembly whose name starts with `SharedKernel.ServiceDefaults` (the composition root). The method accepts a `params Assembly[]` parameter for the production assemblies under test; it does not hard-code assembly paths.
2. The rule must emit a `ConditionList` (not `IArchRule`) in line with the established `ArchitectureRuleBase` API (NetArchTest.eNt 1.3.2 fluent result type). `AssertRule` on the base class calls `.GetResult()` and throws `ArchitectureException` with the offending type names on failure.
3. Exemption list for the architecture rule: `SharedKernel.Caching`, `SharedKernel.Caching.Redis`, `SharedKernel.ServiceDefaults` (and any sub-namespace thereof). Any additional exemption must be explicitly listed in `00.Governance/CLAUDE.md` under the architecture test contracts section.
4. SK0007 `RedisChannelServiceMessagingSubstituteAnalyzer` operates on `SyntaxKind.ClassDeclaration` nodes. For each class, it:
   a. Collects all constructor parameters, field declarations, and property declarations whose declared type's name is `IRedisChannelService` (simple name match; no semantic model namespace resolution required — the simple name is unique within the SDK).
   b. Checks whether the class name or any ancestor namespace identifier contains any of the forbidden terms: `Command`, `Event`, `DomainEvent`, `IntegrationEvent` (case-sensitive, substring match).
   c. If both conditions hold, reports SK0007 on the injection site (parameter / field / property identifier token).
5. SK0007 must suppress correctly inside `SharedKernel.Caching` and `SharedKernel.Caching.Redis` namespaces — the service itself may declare `IRedisChannelService` freely. Suppression uses the same `SyntaxNode.Parent` namespace walk established by SK0001.
6. SK0007 severity is `Warning`. Escalation to `Error` is gated on field feedback confirming zero false positives on the `DomainEvent` substring match (some projects use `IDomainEventHandler` as a class name suffix).
7. Both rules (architecture rule and Roslyn analyzer) must be documented in `00.Governance/CLAUDE.md` with the full exemption list and rationale, so any consuming team can apply for a documented exemption.

### CachingEnforcement — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/CachingAbstractionRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: `OnlyAllowedAssembliesMayReferenceConcreteCaching(Assembly[])` → `ConditionList` |
| `Diagnostics/SK0007_RedisChannelServiceMessagingSubstituteAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0007 fire on `IRedisChannelService` injection in messaging-context class |

### CachingEnforcement — Acceptance Criteria

- [ ] NetArchTest rule exists asserting no assembly (except `SharedKernel.Caching`, `SharedKernel.Caching.Redis`, and `SharedKernel.ServiceDefaults`) references `SharedKernel.Caching` or `SharedKernel.Caching.Redis`
- [ ] Rule test (fire path) fails with a descriptive message identifying the offending assembly name when a non-exempt assembly references a concrete caching type
- [ ] Rule test (pass path) succeeds when only the three exempt assemblies are scanned
- [ ] SK0007 Roslyn analyzer warns when `IRedisChannelService` is injected into a class named or namespaced as a command, event, domain event, or integration event handler
- [ ] SK0007 pass-path test: no diagnostic when `IRedisChannelService` is injected into a class whose name and namespace contain no forbidden terms
- [ ] SK0007 suppression test: no diagnostic when injection occurs inside a `SharedKernel.Caching` namespace (the service's own definition)
- [ ] Both rules documented in `00.Governance/CLAUDE.md` with rationale and exemption list

### CachingEnforcement — Dependencies

- Requires P-005 (`SharedKernel.Caching` package defined — abstraction type names established): yes
- Requires P-006 (`SharedKernel.Caching.Redis` package defined — `IRedisChannelService` interface established): yes
- Requires P-007 (Caching domain fully integrated — type names stable): yes
- Depends on C-07/C-08 (`ArchitectureRuleBase` and `SharedKernelLayeringRules` patterns) for the `ConditionList` API shape: yes (already complete)
- Unblocks: CI architecture gate integration for the Caching domain

### CachingEnforcement — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — SK0007 follows same constraint as SK0001–SK0006)
- Target framework: `netstandard2.0` (Analyzers) / `net10.0` (ArchitectureTests)

### CachingEnforcement — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-13 | Define `CachingAbstractionRules` static class shape: `OnlyAllowedAssembliesMayReferenceConcreteCaching(Assembly[])` → `ConditionList`; define the three-assembly exemption list (`SharedKernel.Caching`, `SharedKernel.Caching.Redis`, `SharedKernel.ServiceDefaults`); document rationale | SharedKernel.ArchitectureTests | `●` |
| D-14 | Define SK0007 `RedisChannelServiceMessagingSubstitute` — trigger: `IRedisChannelService` in constructor param / field / property of a class whose name or enclosing namespace contains `Command`, `Event`, `DomainEvent`, or `IntegrationEvent`; suppression: inside `SharedKernel.Caching` or `SharedKernel.Caching.Redis` namespaces | SharedKernel.Analyzers | `●` |
| C-18 | Implement `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching(Assembly[])` — NetArchTest fluent predicate using `.Should().NotHaveDependencyOn("SharedKernel.Caching")` with assembly-name-based exemption filter | SharedKernel.ArchitectureTests | `●` |
| C-19 | Implement SK0007 `RedisChannelServiceMessagingSubstituteAnalyzer` — `ClassDeclarationSyntax` walker; simple name match on `IRedisChannelService`; substring check for `Command`/`Event`/`DomainEvent`/`IntegrationEvent` in class name and namespace ancestors; suppress inside `SharedKernel.Caching*` namespaces via parent walk | SharedKernel.Analyzers | `●` |
| T-18 | Architecture test (fire path): pass a contrived assembly reference that imports `SharedKernel.Caching` from an application-layer class; assert `CachingAbstractionRules` rule fails with the offending assembly name in the failure message | SharedKernel.ArchitectureTests | `●` |
| T-19 | Architecture test (pass path): pass only the three exempt assemblies; assert `CachingAbstractionRules` rule passes | SharedKernel.ArchitectureTests | `●` |
| T-20 | Analyzer test SK0007 (fire path): `IRedisChannelService` injected via constructor in a class named `PlaceOrderCommandHandler` in namespace `Application.Commands` triggers SK0007 | SharedKernel.Analyzers.Tests | `●` |
| T-21 | Analyzer test SK0007 (pass path): `IRedisChannelService` injected in a class named `CacheInvalidationService` with no forbidden name or namespace term — no diagnostic | SharedKernel.Analyzers.Tests | `●` |
| T-22 | Analyzer test SK0007 (suppression path): `IRedisChannelService` injected in a class within `SharedKernel.Caching.Redis` namespace — no diagnostic | SharedKernel.Analyzers.Tests | `●` |
| DO-07 | Document `CachingAbstractionRules` in `00.Governance/README.md`: rule rationale, exemption list, how to add a documented exemption for a non-standard composition root | SharedKernel.ArchitectureTests | `●` |
| DO-08 | Document SK0007 in `00.Governance/README.md`: rationale (Redis pub/sub is not a durable bus), violating example, compliant alternative, suppression instructions | SharedKernel.Analyzers | `●` |

---

## Phase: Domain Layer Purity Enforcement <!-- phase-key: SK.00.DomainLayerPurity -->

> Protect the architectural integrity of `03.Domain`: no infrastructure references, no domain event handlers, no direct system-clock usage, and no infrastructure-typed constructor parameters on domain services. Four NetArchTest predicates enforce these contracts on every governance test-suite run.

### DomainLayerPurity — Goal

`03.Domain` is the most architecturally sensitive layer in the SharedKernel. A single infrastructure import — an EF Core attribute added for "convenience", a MassTransit consumer placed alongside a domain event — propagates a hard dependency on a specific infrastructure stack to every service that references the domain. `DateTime.UtcNow` in domain logic makes unit tests non-deterministic and prevents in-memory time simulation for time-sensitive rules (e.g. "entity expires after 30 days"). Domain services that accept infrastructure constructor parameters break in-memory testability. This phase encodes all four purity invariants as NetArchTest predicates in `DomainLayerPurityRules`, adding them to the governance test suite so violations are caught at build time with zero false-positive risk when correctly scoped.

### DomainLayerPurity — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/DomainLayerPurityRules.cs` — static class housing all four predicates
- Modified files:
  - `00.Governance/CLAUDE.md` — add `DomainLayerPurityRules` to architecture test contracts section; document all four rules with rationale and examples
  - `00.Governance/state-map.md` — this update
- Deleted files: none
- Note: No new Roslyn analyzer SK ID is assigned. Rule 3 (clock usage) is enforced at assembly level via NetArchTest `DoesNotCallMemberPredicate`; SK0001 already handles per-call-site diagnostics for individual developers. The architecture test provides the gating enforcement pass that SK0001 cannot provide (assembly-level, post-compile).

### DomainLayerPurity — Diagnostic Registry Changes

No new SK diagnostic IDs. All four rules are pure NetArchTest architecture predicates:

- Rule 1 — `DomainAssembliesNeverReferenceInfrastructure`: NetArchTest assembly dependency scan; all assemblies under `03.Domain`
- Rule 2 — `DomainAssembliesNeverContainEventHandlers`: NetArchTest type interface scan via ICustomRule; all types in `03.Domain` assemblies
- Rule 3 — `DomainAssembliesNeverCallSystemClock`: NetArchTest ICustomRule + Mono.Cecil IL instruction scan; all types in `03.Domain` assemblies
- Rule 4 — `DomainServicesHaveNoInfrastructureConstructorParameters`: NetArchTest constructor parameter scan via ICustomRule; types implementing IDomainService

### DomainLayerPurity — Implementation Rules

1. `DomainAssembliesNeverReferenceInfrastructure` must use `.Should().NotHaveDependencyOnAny(forbiddenAssemblyNames)` where `forbiddenAssemblyNames` is a fixed array covering: `"Microsoft.EntityFrameworkCore"`, `"MassTransit"`, `"StackExchange.Redis"`, `"RabbitMQ.Client"`, plus any assembly whose name contains `"EntityFramework"`, `"MassTransit"`, `"Redis"`, or `"RabbitMQ"`. Because NetArchTest `.NotHaveDependencyOn()` takes a single string and matches it as a substring of the referenced assembly name, calling it iteratively for each forbidden term is the correct pattern. The rule accepts a `params Assembly[]` to allow callers to supply the domain assemblies under test.
2. `DomainAssembliesNeverContainEventHandlers` must use `.Should().NotImplementInterface(typeof(IDomainEventHandler<>))` where `IDomainEventHandler<>` is sourced from `SharedKernel.Messaging.Abstractions`. Because NetArchTest may not support open-generic interface matching directly, the predicate must fall back to `.MeetCustomRule(new DoesNotImplementOpenGenericInterfacePredicate("IDomainEventHandler"))` — a new `ICustomRule` that checks type interface names via `TypeDefinition.Interfaces`. The failure message must include the offending type's full name and the assembly it was found in. This custom predicate reuses the same `Mono.Cecil` access pattern as `DoesNotContainThrowIlPredicate`.
3. `DomainAssembliesNeverCallSystemClock` must use a new `ICustomRule` (`DoesNotCallSystemClockPredicate`) that inspects `MethodDefinition.Body.Instructions` for `call` or `callvirt` opcodes whose operand is `System.DateTime::get_UtcNow`, `System.DateTime::get_Now`, `System.DateTimeOffset::get_UtcNow`, or `System.DateTimeOffset::get_Now`. Uses the same Mono.Cecil `TypeDefinition` access as `DoesNotContainThrowIlPredicate`. The predicate is registered as a method on `DomainLayerPurityRules` returning a `ConditionList`.
4. `DomainServicesHaveNoInfrastructureConstructorParameters` must use a new `ICustomRule` (`NoInfrastructureConstructorParametersPredicate`) that inspects `TypeDefinition.Methods` for methods with `IsConstructor == true` and checks each parameter's `ParameterDefinition.ParameterType.Namespace` against the forbidden namespace list: `"Microsoft.EntityFrameworkCore"`, `"MassTransit"`, `"StackExchange.Redis"`, `"RabbitMQ.Client"` (namespace prefix match). Applied only to types implementing `IDomainService`. Failure message must name the offending constructor parameter type.
5. All four factory methods accept `Assembly domainAssembly` as their first parameter (not `params Assembly[]` for Rules 2–4 which scope to a single domain assembly at a time). Rule 1 additionally accepts the domain assembly — the implementation filters to types within that assembly only.
6. All predicates reuse `DoesNotContainThrowIlPredicate`'s established pattern for Mono.Cecil `TypeDefinition` access: if `IType.Definition` is not publicly exposed by NetArchTest.eNt, `Mono.Cecil >= 0.11.5` is already referenced (established in GuardPurity phase) — no new NuGet dependency.
7. `DomainLayerPurityRules` must live in `SharedKernel.ArchitectureTests/Rules/` alongside `SharedKernelLayeringRules.cs` and `GuardPurityRules.cs`. It must not reference any runtime domain or infrastructure package.
8. The `IDomainService` interface is defined in `SharedKernel.Domain` (`03.Domain`). The consuming architecture test project must reference `SharedKernel.Domain` directly to supply the assembly reference. `DomainLayerPurityRules` itself does not hard-code an assembly path — the caller supplies `typeof(IDomainService).Assembly`.
9. Failure messages for all four predicates must identify the offending element: Rule 1 → offending assembly reference name; Rule 2 → offending type full name; Rule 3 → offending type + method name; Rule 4 → offending type name + offending parameter type name.
10. `DoesNotImplementOpenGenericInterfacePredicate` checks `TypeDefinition.Interfaces` — each `InterfaceImplementation.InterfaceType` is inspected for `Name.StartsWith("IDomainEventHandler")`. This covers both the non-generic and the open-generic form in IL. The predicate is internal to `SharedKernel.ArchitectureTests` and lives in `Predicates/DoesNotImplementOpenGenericInterfacePredicate.cs`.
11. `DoesNotCallSystemClockPredicate` inspects all `MethodDefinition` bodies in the type; for each `Instruction` where `OpCode` is `Call` or `Callvirt`, the operand is cast to `MethodReference` and the `FullName` is checked against the four forbidden property getter strings. This is the same IL walking pattern as `DoesNotContainThrowIlPredicate`. The predicate lives in `Predicates/DoesNotCallSystemClockPredicate.cs`.
12. `NoInfrastructureConstructorParametersPredicate` is scoped to types whose `TypeDefinition.Interfaces` contains an entry whose `InterfaceType.Name` equals `"IDomainService"`. Only these types are inspected. The predicate lives in `Predicates/NoInfrastructureConstructorParametersPredicate.cs`.

### DomainLayerPurity — File-Level Plan

All files in `SharedKernel.ArchitectureTests`:

- `Rules/DomainLayerPurityRules.cs` — Create — static class: four predicate factory methods returning ConditionList
- `Predicates/DoesNotImplementOpenGenericInterfacePredicate.cs` — Create — ICustomRule: checks TypeDefinition.Interfaces for open-generic event handler name prefix
- `Predicates/DoesNotCallSystemClockPredicate.cs` — Create — ICustomRule: walks IL instructions for DateTime/DateTimeOffset property getter call opcodes
- `Predicates/NoInfrastructureConstructorParametersPredicate.cs` — Create — ICustomRule: inspects constructors of IDomainService implementors for infra-namespace params

### DomainLayerPurity — Acceptance Criteria

- [ ] NetArchTest rule `DomainAssembliesNeverReferenceInfrastructure` fails with offending reference name when a domain assembly references EntityFramework, MassTransit, Redis, or RabbitMQ
- [ ] Architecture test asserts no type in `03.Domain` assemblies implements `IDomainEventHandler<TEvent>`; failure message names the offending type
- [ ] IL-inspection rule flags `DateTime.UtcNow`, `DateTime.Now`, `DateTimeOffset.UtcNow`, `DateTimeOffset.Now` usage in domain assembly types; failure message names the offending type and method
- [ ] NetArchTest rule asserts `IDomainService` constructors contain no infrastructure-namespace type parameters; failure names the offending type and parameter
- [ ] All four rules documented in `00.Governance/CLAUDE.md` with rationale, offending-pattern example, compliant-pattern example
- [ ] Each of the four rules has at least one fire-path test and one pass-path test row in this state-map

### DomainLayerPurity — Dependencies

- Requires P-032 (`IDomainService`, `IDomainEventHandler<TEvent>` types defined in `SharedKernel.Domain`): yes — `DomainLayerPurityRules` loads `typeof(IDomainService).Assembly`; without the type existing the assembly load fails at runtime in tests
- Requires `DoesNotContainThrowIlPredicate` (Mono.Cecil pattern) from `SK.00.GuardPurity` to be complete: yes (already complete — Mono.Cecil access pattern is established)
- Unblocks: CI architecture gate integration for the Domain layer

### DomainLayerPurity — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref in `SharedKernel.ArchitectureTests` from GuardPurity phase — no change)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new SK rule, no change)
- Target framework: `net10.0` (ArchitectureTests)

### DomainLayerPurity — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-15 | Define `DomainAssembliesNeverReferenceInfrastructure` predicate shape: forbidden assembly name terms (`EntityFramework`, `MassTransit`, `Redis`, `RabbitMQ`), iterative `.NotHaveDependencyOn()` call pattern, failure message contract | SharedKernel.ArchitectureTests | `●` |
| D-16 | Define `DomainAssembliesNeverContainEventHandlers` predicate shape: `DoesNotImplementOpenGenericInterfacePredicate` design — `TypeDefinition.Interfaces` inspection for `IDomainEventHandler` name prefix; failure message includes offending type full name | SharedKernel.ArchitectureTests | `●` |
| D-17 | Define `DomainAssembliesNeverCallSystemClock` predicate shape: `DoesNotCallSystemClockPredicate` design — IL instruction walk for `DateTime::get_UtcNow`, `DateTime::get_Now`, `DateTimeOffset::get_UtcNow`, `DateTimeOffset::get_Now`; failure message includes offending type and method | SharedKernel.ArchitectureTests | `●` |
| D-18 | Define `DomainServicesHaveNoInfrastructureConstructorParameters` predicate shape: `NoInfrastructureConstructorParametersPredicate` design — scope to `IDomainService` implementors, inspect constructor `ParameterDefinition.ParameterType.Namespace` for infra namespace prefix match; failure message includes offending type and parameter type | SharedKernel.ArchitectureTests | `●` |
| C-20 | Implement `DoesNotImplementOpenGenericInterfacePredicate` in `Predicates/` — `ICustomRule` checking `TypeDefinition.Interfaces` for entries whose `InterfaceType.Name` starts with `"IDomainEventHandler"`; return false with offending type full name on violation | SharedKernel.ArchitectureTests | `●` |
| C-21 | Implement `DoesNotCallSystemClockPredicate` in `Predicates/` — `ICustomRule` walking all `MethodDefinition.Body.Instructions` for `Call`/`Callvirt` opcodes whose operand `MethodReference.FullName` matches any of the four forbidden property getters | SharedKernel.ArchitectureTests | `●` |
| C-22 | Implement `NoInfrastructureConstructorParametersPredicate` in `Predicates/` — `ICustomRule` scoped to types implementing `IDomainService`; inspects `TypeDefinition.Methods` where `IsConstructor` is true; checks each `ParameterDefinition.ParameterType.Namespace` against forbidden namespace prefix list | SharedKernel.ArchitectureTests | `●` |
| C-23 | Implement `DomainLayerPurityRules` static class in `Rules/` — four factory methods: `DomainAssembliesNeverReferenceInfrastructure(Assembly)` → `ConditionList`, `DomainAssembliesNeverContainEventHandlers(Assembly)` → `ConditionList`, `DomainAssembliesNeverCallSystemClock(Assembly)` → `ConditionList`, `DomainServicesHaveNoInfrastructureConstructorParameters(Assembly)` → `ConditionList` | SharedKernel.ArchitectureTests | `●` |
| T-23 | Architecture test Rule 1 (fire path): pass a contrived domain assembly that references `Microsoft.EntityFrameworkCore`; assert `DomainAssembliesNeverReferenceInfrastructure` fails and failure message contains the offending assembly name | SharedKernel.ArchitectureTests | `●` |
| T-24 | Architecture test Rule 1 (pass path): pass a clean domain assembly with no infrastructure references; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-25 | Architecture test Rule 2 (fire path): pass a domain assembly containing a type that implements `IDomainEventHandler<TEvent>`; assert `DomainAssembliesNeverContainEventHandlers` fails and failure message contains the offending type full name | SharedKernel.ArchitectureTests | `●` |
| T-26 | Architecture test Rule 2 (pass path): pass a clean domain assembly with no event handler implementations; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-27 | Architecture test Rule 3 (fire path): pass a domain assembly where a domain entity method calls `DateTime.UtcNow`; assert `DomainAssembliesNeverCallSystemClock` fails and failure message names the offending type and method | SharedKernel.ArchitectureTests | `●` |
| T-28 | Architecture test Rule 3 (pass path): pass a clean domain assembly where time is consumed via `IClock.UtcNow`; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-29 | Architecture test Rule 4 (fire path): pass a domain assembly where an `IDomainService` implementation has an `IRepository` (EF Core namespace) constructor parameter; assert `DomainServicesHaveNoInfrastructureConstructorParameters` fails and failure message names the offending type and parameter type | SharedKernel.ArchitectureTests | `●` |
| T-30 | Architecture test Rule 4 (pass path): pass a clean `IDomainService` implementation whose constructor accepts only `IClock` and other domain interfaces; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| DO-09 | Document all four `DomainLayerPurityRules` predicates in `00.Governance/CLAUDE.md` and `00.Governance/README.md`: rationale for each rule, offending-pattern example, compliant-pattern example, cross-reference to root `CLAUDE.md` hard rules | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Domain Gold-Standard Architecture Rules <!-- phase-key: SK.00.DomainGoldStandard -->

> Encode the WO-011 domain conventions as build-time enforcement rules: IDomainService must extend DomainService, infrastructure dispatch code must not couple to IAggregateRoot<TId>, IDomainEvent implementors must carry [DomainEventVersion], and specification constructors must not apply conflicting ordering calls.

### DomainGoldStandard — Goal

WO-011 introduced several high-value conventions — the `DomainService` abstract base, the `IHasDomainEvents` interface as the narrow dispatch coupling point, the `[DomainEventVersion]` schema attribute, and the dual-ordering prohibition on `Specification<T>` constructors. Without enforcement these remain advisory guidelines that well-meaning developers will violate in good faith. This phase encodes them as a NetArchTest predicate (Rule 1) and three Roslyn analyzers (Rules 2–4), closing the gap between convention and contract at build time with near-zero false-positive rates when scoped correctly.

### DomainGoldStandard — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`, `SharedKernel.Analyzers`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/DomainGoldStandardRules.cs` — static class housing `DomainServicesMustExtendAbstractBase(Assembly)`
  - `SharedKernel.Analyzers/Diagnostics/SK0008_AggregateRootDispatchCouplingAnalyzer.cs` — Roslyn analyzer: SK0008 fire when `IAggregateRoot<>` injected in dispatch-context class
  - `SharedKernel.Analyzers/Diagnostics/SK0009_DomainEventMissingVersionAttributeAnalyzer.cs` — Roslyn analyzer: SK0009 fire when IDomainEvent implementation lacks `[DomainEventVersion]`
  - `SharedKernel.Analyzers/Diagnostics/SK0010_SpecificationOrderingConflictAnalyzer.cs` — Roslyn analyzer: SK0010 fire when specification constructor calls both `ApplyOrderBy` and `ApplyOrderByDescending`
- Modified files:
  - `00.Governance/CLAUDE.md` — add SK0008–SK0010 to diagnostic registry; add `DomainGoldStandardRules` to architecture test contracts; document all four rules with rationale and examples
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### DomainGoldStandard — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0008 | AggregateRootDispatchCoupling | Design | Warning | A type in a namespace or class name containing `Interceptor`, `Publisher`, `Outbox`, or `Dispatcher` injects a constructor parameter typed `IAggregateRoot<>` — prefer `IHasDomainEvents` for narrower coupling |
| SK0009 | DomainEventMissingVersionAttribute | Design | Warning | A type implementing `IDomainEvent` (directly or transitively) lacks a `[DomainEventVersion]` attribute declaration — schema versioning discipline is required |
| SK0010 | SpecificationOrderingConflict | Design | Warning | A constructor body calls both `ApplyOrderBy(...)` and `ApplyOrderByDescending(...)` — the conflicting ordering directives produce non-deterministic sort results |

### DomainGoldStandard — Implementation Rules

1. `DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(Assembly)` uses NetArchTest fluent API: `Types.InAssembly(assembly).That().ImplementInterface(typeof(IDomainService)).And().AreNotAbstract().Should().Inherit(typeof(DomainService))`. The `DomainService` abstract class itself is excluded via `.AreNotAbstract()` — the abstract base passes. No `ICustomRule` required; NetArchTest's `.Inherit()` predicate is sufficient.
2. Rule 1 failure message must name the offending type. Because NetArchTest generates the failure list from `.GetResult().FailingTypeNames`, no additional predicate is needed — the default failure output names all non-conforming types.
3. SK0008 `AggregateRootDispatchCouplingAnalyzer` operates on `ConstructorDeclarationSyntax` nodes. For each constructor parameter whose type syntax resolves to a name containing `IAggregateRoot` (simple name contains check — the generic form uses angle brackets so check `.Identifier.Text` or `.ToString()` for `"IAggregateRoot"`), it checks whether the containing class name or any ancestor namespace identifier contains any of: `"Interceptor"`, `"Publisher"`, `"Outbox"`, `"Dispatcher"` (case-sensitive substring match). If the condition holds, SK0008 is reported on the parameter type identifier. No semantic model required — the simple name `IAggregateRoot` is unique within the SDK.
4. SK0008 uses the standard `SyntaxNode.Parent` namespace walk (same pattern as SK0001, SK0007) for suppression. No suppression namespace is defined for SK0008 — the rule fires in all namespaces where dispatch-context names appear.
5. SK0009 `DomainEventMissingVersionAttributeAnalyzer` operates on `ClassDeclarationSyntax` and `RecordDeclarationSyntax` nodes. For each type declaration, it checks whether the type implements an interface whose name is `IDomainEvent` (base list simple name check — `BaseList.Types` checked for `IDomainEvent`). If it does, the analyzer checks whether the type's `AttributeLists` contain an attribute whose name is `DomainEventVersion` or `DomainEventVersionAttribute`. If the attribute is absent, SK0009 is reported on the type identifier. No semantic model required for attribute name check — simple name match is sufficient and unique within the SDK.
6. SK0009 must not fire on abstract types — an abstract base that implements `IDomainEvent` without `[DomainEventVersion]` is legitimate as a shared base for versioned events. Suppress by checking `Modifiers.Any(SyntaxKind.AbstractKeyword)`.
7. SK0010 `SpecificationOrderingConflictAnalyzer` operates on `ConstructorDeclarationSyntax` nodes. Within the constructor body, it collects all `InvocationExpressionSyntax` nodes whose method name is `ApplyOrderBy` or `ApplyOrderByDescending` (simple name check on the `MemberAccessExpression.Name.Identifier.Text`). If both names appear at least once within the same constructor body, SK0010 is reported on the constructor identifier. No scoping to Specification subclasses is required at the syntax level — the method names `ApplyOrderBy`/`ApplyOrderByDescending` are unique to `Specification<T>` within the SDK. A false-positive from a different type calling a coincidentally-named method is acceptable given the low probability.
8. All three SK analyzers follow `netstandard2.0` constraint. Zero new NuGet dependencies beyond `Microsoft.CodeAnalysis.CSharp`.
9. Rule 1 (`DomainGoldStandardRules`) depends on `SharedKernel.Domain` assembly containing `IDomainService` and `DomainService` types. The consuming test project must reference `SharedKernel.Domain` directly; `DomainGoldStandardRules` accepts `Assembly` as a parameter and does not hard-code type paths.
10. `DomainGoldStandardRules` lives in `SharedKernel.ArchitectureTests/Rules/` alongside `DomainLayerPurityRules.cs`. It must not reference any runtime domain or infrastructure package directly.

### DomainGoldStandard — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/DomainGoldStandardRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: `DomainServicesMustExtendAbstractBase(Assembly)` → `ConditionList` |
| `Diagnostics/SK0008_AggregateRootDispatchCouplingAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0008 — IAggregateRoot injection in dispatch-context classes |
| `Diagnostics/SK0009_DomainEventMissingVersionAttributeAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0009 — IDomainEvent implementors missing [DomainEventVersion] |
| `Diagnostics/SK0010_SpecificationOrderingConflictAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0010 — constructor calls both ApplyOrderBy and ApplyOrderByDescending |

### DomainGoldStandard — Acceptance Criteria

- [ ] Rule 1 NetArchTest: `IDomainService` implementors not extending `DomainService` are detected; failure message names the offending type; `DomainService` itself does not appear as a violation
- [ ] SK0008 Roslyn analyzer: injection of `IAggregateRoot<>` as a constructor parameter in a class/namespace named with dispatch-context terms produces a warning with a link to `IHasDomainEvents`
- [ ] SK0009 Roslyn analyzer: `IDomainEvent` implementing type (non-abstract) lacking `[DomainEventVersion]` attribute produces a warning; abstract base types are exempt
- [ ] SK0010 Roslyn analyzer: specification constructors calling both `ApplyOrderBy` and `ApplyOrderByDescending` produce a warning; constructors calling only one are clean
- [ ] All four rules documented in `00.Governance/CLAUDE.md` with rationale, offending-pattern example, compliant-pattern example
- [ ] Rule 1 has fire-path and pass-path architecture test fixtures
- [ ] Each of SK0008, SK0009, SK0010 has a fire-path analyzer test and a pass-path analyzer test

### DomainGoldStandard — Dependencies

- Requires P-045 (`DomainService` abstract class in `SharedKernel.Domain`): yes — Rule 1 uses `typeof(DomainService)` as the expected base
- Requires P-047 (`IDomainService` interface in `SharedKernel.Domain`): yes — Rule 1 scopes to `IDomainService` implementors; SK0008 uses `IAggregateRoot<>` from the same domain package
- Requires P-053 (`IDomainEvent`, `[DomainEventVersion]`, `Specification<T>` with `ApplyOrderBy`/`ApplyOrderByDescending` in `SharedKernel.Domain`): yes — SK0009 checks `IDomainEvent` interface membership; SK0010 checks ordering method call names
- Unblocks: CI architecture gate integration for the Domain gold-standard contract

### DomainGoldStandard — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref — no change; not needed for Rule 1 but already present)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — SK0008–SK0010 follow same constraint)
- Target framework: `netstandard2.0` (Analyzers) / `net10.0` (ArchitectureTests)

### DomainGoldStandard — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-19 | Define `DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(Assembly)` shape: NetArchTest `.ImplementInterface(typeof(IDomainService)).And().AreNotAbstract().Should().Inherit(typeof(DomainService))`; failure message lists offending type names from `.GetResult().FailingTypeNames` | SharedKernel.ArchitectureTests | `●` |
| D-20 | Define SK0008 `AggregateRootDispatchCoupling` — trigger: `IAggregateRoot<>` constructor parameter in class/namespace containing `Interceptor`, `Publisher`, `Outbox`, or `Dispatcher`; no suppression namespace; link to `IHasDomainEvents` in fix message | SharedKernel.Analyzers | `●` |
| D-21 | Define SK0009 `DomainEventMissingVersionAttribute` — trigger: type implementing `IDomainEvent` (base list check) without `[DomainEventVersion]` attribute; exempt abstract types; no suppression namespace | SharedKernel.Analyzers | `●` |
| D-22 | Define SK0010 `SpecificationOrderingConflict` — trigger: constructor body containing both `ApplyOrderBy` and `ApplyOrderByDescending` invocations; simple name check; no type-scoping required | SharedKernel.Analyzers | `●` |
| C-24 | Implement `DomainGoldStandardRules` static class in `Rules/` — single factory method `DomainServicesMustExtendAbstractBase(Assembly)` returning `ConditionList` using NetArchTest fluent `.Inherit()` predicate | SharedKernel.ArchitectureTests | `●` |
| C-25 | Implement SK0008 `AggregateRootDispatchCouplingAnalyzer` — `ConstructorDeclarationSyntax` walker; IAggregateRoot simple name check on param type; dispatch-context substring check on class name and ancestor namespaces; report SK0008 on param type identifier | SharedKernel.Analyzers | `●` |
| C-26 | Implement SK0009 `DomainEventMissingVersionAttributeAnalyzer` — `ClassDeclarationSyntax` and `RecordDeclarationSyntax` walker; base list check for `IDomainEvent`; attribute list check for `DomainEventVersion`; skip abstract types; report SK0009 on type identifier | SharedKernel.Analyzers | `●` |
| C-27 | Implement SK0010 `SpecificationOrderingConflictAnalyzer` — `ConstructorDeclarationSyntax` walker; collect `ApplyOrderBy` and `ApplyOrderByDescending` invocations in body; if both present, report SK0010 on constructor identifier | SharedKernel.Analyzers | `●` |
| T-31 | Architecture test Rule 1 (fire path): pass a contrived assembly containing an `IDomainService` implementor that does NOT extend `DomainService`; assert `DomainServicesMustExtendAbstractBase` fails and failure message names the offending type | SharedKernel.ArchitectureTests | `●` |
| T-32 | Architecture test Rule 1 (pass path): pass an assembly where all `IDomainService` implementors extend `DomainService`; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-33 | Analyzer test SK0008 (fire path): `IAggregateRoot<Order>` constructor parameter in class `OrderPublisher` (namespace `Messaging`) triggers SK0008 | SharedKernel.Analyzers.Tests | `●` |
| T-34 | Analyzer test SK0008 (pass path): `IHasDomainEvents` constructor parameter in class `OrderPublisher` — no diagnostic | SharedKernel.Analyzers.Tests | `●` |
| T-35 | Analyzer test SK0009 (fire path): `record OrderCreated : IDomainEvent { ... }` without `[DomainEventVersion]` attribute triggers SK0009 | SharedKernel.Analyzers.Tests | `●` |
| T-36 | Analyzer test SK0009 (pass path): `[DomainEventVersion(1)] record OrderCreated : IDomainEvent { ... }` — no diagnostic | SharedKernel.Analyzers.Tests | `●` |
| T-37 | Analyzer test SK0009 (abstract exempt path): `abstract class DomainEventBase : IDomainEvent { }` without `[DomainEventVersion]` — no diagnostic | SharedKernel.Analyzers.Tests | `●` |
| T-38 | Analyzer test SK0010 (fire path): specification constructor calling both `ApplyOrderBy(x => x.Name)` and `ApplyOrderByDescending(x => x.CreatedAt)` triggers SK0010 | SharedKernel.Analyzers.Tests | `●` |
| T-39 | Analyzer test SK0010 (pass path): specification constructor calling only `ApplyOrderBy(x => x.Name)` — no diagnostic | SharedKernel.Analyzers.Tests | `●` |
| DO-10 | Document all four DomainGoldStandard rules in `00.Governance/CLAUDE.md` and `00.Governance/README.md`: rationale, offending-pattern example, compliant-pattern example | SharedKernel.ArchitectureTests, SharedKernel.Analyzers | `●` |

---

## Phase: Contracts Layer Purity Architecture Rules <!-- phase-key: SK.00.ContractsPurity -->

> Protect the `04.Contracts` layer from accumulating domain logic, domain type leakage, `Result<T>` misuse, and non-sealed integration events — all with high-confidence NetArchTest rules that fire in CI before violations reach any downstream consumer.

### ContractsPurity — Goal

`04.Contracts` is the widest-referenced package in the platform: every service boundary touches it. A contracts package that silently accumulates domain logic, exposes `Entity<TId>` return types, returns `Result<T>` across service boundaries, or ships non-sealed integration events will corrupt downstream consumers at serialization time. The five rules in this phase provide a CI gate that catches each violation class with near-zero false positives. Rule 5 is documented only — the mono-repo boundary makes NetArchTest enforcement against external consumer assemblies impractical.

### ContractsPurity — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/ContractsPurityRules.cs` — static class housing all four NetArchTest predicates
  - `SharedKernel.ArchitectureTests/Predicates/NoNonTrivialMethodsPredicate.cs` — ICustomRule: heuristic method-count check for non-trivial logic in contracts types
- Modified files:
  - `00.Governance/CLAUDE.md` — add `ContractsPurityRules` to architecture test contracts section; document all five rules with rationale, offending-pattern example, compliant-pattern example
  - `00.Governance/state-map.md` — this update
- Deleted files: none
- Note: No new Roslyn analyzer SK IDs. All enforcement is via NetArchTest + one ICustomRule predicate.

### ContractsPurity — Diagnostic Registry Changes

No new SK diagnostic IDs. All rules are pure NetArchTest architecture predicates or documentation-level rules:

- Rule 1 — `ContractsAssembliesHaveNoNonTrivialMethods`: ICustomRule heuristic — method count threshold + allowlist for constructors/operators/property accessors
- Rule 2 — `ContractsAssembliesHaveNoDomainTypeOnPublicSurface`: NetArchTest dependency scan for `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `Specification<T>` as public member types
- Rule 3 — `ContractsAssembliesHaveNoResultTypeOnPublicSurface`: NetArchTest dependency scan for `Result<T>` and `Result` from `SharedKernel.Primitives`
- Rule 4 — `IntegrationEventImplementationsMustBeSealed`: NetArchTest type predicate — non-abstract `IIntegrationEvent` implementors must be sealed or records
- Rule 5 — Documentation-only: microservices must not reference `SharedKernel.Domain` directly unless they implement domain logic

### ContractsPurity — Implementation Rules

1. `ContractsAssembliesHaveNoNonTrivialMethods` uses a custom `ICustomRule` (`NoNonTrivialMethodsPredicate`) that inspects `TypeDefinition.Methods` for each type in the contracts assembly. The heuristic: any method that is not a constructor (`IsConstructor`), not a property getter/setter (`IsGetter` or `IsSetter`), not a static operator (`IsSpecialName` and name starts with `"op_"`), and not `ToString`/`Equals`/`GetHashCode` (by name) is counted as non-trivial. If a type has any non-trivial method, the predicate returns false with the offending type name and method name in the failure message. The threshold is zero non-trivial methods — one non-trivial method is a violation. The predicate lives in `Predicates/NoNonTrivialMethodsPredicate.cs`.
2. `ContractsAssembliesHaveNoDomainTypeOnPublicSurface` uses `Types.InAssembly(assembly).That().ArePublic().Should().NotHaveDependencyOnAny("SharedKernel.Domain.Entity", "SharedKernel.Domain.AggregateRoot", "SharedKernel.Domain.ValueObject", "SharedKernel.Domain.Specification")`. Because NetArchTest's `.NotHaveDependencyOn()` matches on assembly name substring (not type name), the correct approach is to check for a dependency on the `SharedKernel.Domain` assembly itself — but only for public types that expose domain types as property or return types. The practical approach for this rule: use `.Should().NotHaveDependencyOn("SharedKernel.Domain")` on types scoped to `04.Contracts`. This is a conservative check; it will catch any type in contracts that has any binary reference to the domain assembly. Exception: the `EventEnvelope<TEvent>` generic type constraint (`where TEvent : IDomainEvent`) is permitted — it is a constraint, not a public property type. Document this exception in the implementation notes.
3. `ContractsAssembliesHaveNoResultTypeOnPublicSurface` uses `Types.InAssembly(assembly).That().ArePublic().Should().NotHaveDependencyOn("SharedKernel.Primitives")`. This catches any contracts type that references `Result<T>` or `Result` (both live in `SharedKernel.Primitives`). The failure message must include the specific type name from `.GetResult().FailingTypeNames`.
4. `IntegrationEventImplementationsMustBeSealed` uses a custom `ICustomRule` (`SealedIntegrationEventPredicate`) OR a NetArchTest fluent predicate. Because NetArchTest.eNt 1.3.2 supports `.BeSealed()` on `ConditionList`, use: `Types.InAssembly(assembly).That().ImplementInterface(typeof(IIntegrationEvent)).And().AreNotAbstract().Should().BeSealed()`. If `.BeSealed()` is not available in the pinned version, fall back to a custom `ICustomRule` that inspects `TypeDefinition.IsSealed` or checks for `record` (records are `IsSealed` in IL). The failure message must name the offending type.
5. Rule 5 (documentation-only) is not implemented as a NetArchTest rule. It is recorded in `CLAUDE.md` with architectural rationale: microservices should reference `SharedKernel.Contracts` for cross-service types; referencing `SharedKernel.Domain` directly from a microservice that is not a DDD-domain service represents an inappropriate layering coupling. No code enforcement is applicable at the mono-repo level.
6. The `EventEnvelope<TEvent> where TEvent : IDomainEvent` constraint is a declared exemption for Rule 2. The `IDomainEvent` type reference appears as a generic type constraint in IL, not as a field or property type — NetArchTest's dependency scanner may or may not pick it up as a dependency on `SharedKernel.Domain`. If it does, `EventEnvelope<TEvent>` must be added to an explicit allowlist in the predicate. Document this in `CLAUDE.md` under the rule.
7. All four factory methods in `ContractsPurityRules` accept `Assembly contractsAssembly` as their parameter. They return `ConditionList` (consistent with the established `ArchitectureRuleBase` API).
8. `ContractsPurityRules` lives in `SharedKernel.ArchitectureTests/Rules/`. It must not hard-code assembly paths. The consuming test project supplies the `04.Contracts` assembly via `typeof(SomeContractsType).Assembly`.
9. All predicates reuse the established Mono.Cecil access pattern from `DoesNotContainThrowIlPredicate`. No new NuGet dependencies.

### ContractsPurity — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/ContractsPurityRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: four predicate factory methods + Rule 5 documentation reference |
| `Predicates/NoNonTrivialMethodsPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: heuristic non-trivial method detection for contracts types |

### ContractsPurity — Acceptance Criteria

- [ ] NetArchTest Rule 1 exists: detects non-trivial method bodies in `04.Contracts` assemblies; allowlist covers constructors, property accessors, operators, `ToString`/`Equals`/`GetHashCode`; fires correctly on a violation fixture
- [ ] NetArchTest Rule 2 exists: detects `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, or `Specification<T>` presence in `04.Contracts` public types via domain assembly dependency scan; fires on violation; `EventEnvelope<TEvent>` constraint exemption documented
- [ ] NetArchTest Rule 3 exists: detects `Result<T>` or `Result` in `04.Contracts` public API via Primitives dependency scan; fires with the specific type name in the failure message
- [ ] NetArchTest Rule 4 exists: detects non-sealed, non-abstract `IIntegrationEvent` implementations; fires with the offending type name
- [ ] Rule 5 is documented in `00.Governance/CLAUDE.md` with rationale — not a NetArchTest rule but an architectural guideline
- [ ] Rules 1–4 each have at least one violation fixture test demonstrating the rule fires correctly
- [ ] All rules documented in `00.Governance/CLAUDE.md` with rationale, offending-pattern example, compliant-pattern example
- [ ] Governance test suite passes with all new rules; no false positives on existing SharedKernel assemblies

### ContractsPurity — Dependencies

- Requires P-059 (`04.Contracts` package complete with `IIntegrationEvent`, `EventEnvelope<TEvent>`, `PagedList`, `Envelope<T>` types established): yes — Rules 2, 3, and 4 reference types from `04.Contracts` and `SharedKernel.Primitives`
- Requires `DomainLayerPurityRules` (`DoesNotContainThrowIlPredicate` Mono.Cecil pattern) to be complete: yes (already complete — Mono.Cecil access pattern is established in `NoNonTrivialMethodsPredicate`)
- Unblocks: CI architecture gate integration for the Contracts layer

### ContractsPurity — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — `.BeSealed()` availability to be confirmed; fallback ICustomRule documented)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref — no change)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new SK rules, no change)
- Target framework: `net10.0` (ArchitectureTests)

### ContractsPurity — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-23 | Define `ContractsAssembliesHaveNoNonTrivialMethods` shape: `NoNonTrivialMethodsPredicate` heuristic design — zero non-trivial methods threshold; allowlist: constructors, property getters/setters, operators (`op_` prefix), `ToString`/`Equals`/`GetHashCode` by name | SharedKernel.ArchitectureTests | `●` |
| D-24 | Define `ContractsAssembliesHaveNoDomainTypeOnPublicSurface` shape: `.Should().NotHaveDependencyOn("SharedKernel.Domain")` on public contracts types; document `EventEnvelope<TEvent>` generic-constraint exemption | SharedKernel.ArchitectureTests | `●` |
| D-25 | Define `ContractsAssembliesHaveNoResultTypeOnPublicSurface` shape: `.Should().NotHaveDependencyOn("SharedKernel.Primitives")` on public contracts types; failure message must include offending type name | SharedKernel.ArchitectureTests | `●` |
| D-26 | Define `IntegrationEventImplementationsMustBeSealed` shape: `.ImplementInterface(typeof(IIntegrationEvent)).And().AreNotAbstract().Should().BeSealed()` — with ICustomRule fallback if `.BeSealed()` unavailable in NetArchTest 1.3.2 | SharedKernel.ArchitectureTests | `●` |
| C-28 | Implement `NoNonTrivialMethodsPredicate` in `Predicates/` — `ICustomRule` inspecting `TypeDefinition.Methods`; exclude constructors, property accessors, operators, `ToString`/`Equals`/`GetHashCode`; return false with offending type+method name on first non-trivial method found | SharedKernel.ArchitectureTests | `●` |
| C-29 | Implement `ContractsPurityRules` static class in `Rules/` — four factory methods: `ContractsAssembliesHaveNoNonTrivialMethods(Assembly)`, `ContractsAssembliesHaveNoDomainTypeOnPublicSurface(Assembly)`, `ContractsAssembliesHaveNoResultTypeOnPublicSurface(Assembly)`, `IntegrationEventImplementationsMustBeSealed(Assembly)` — all return `ConditionList` | SharedKernel.ArchitectureTests | `●` |
| T-40 | Architecture test Rule 1 (fire path): pass a contrived contracts assembly containing a type with a non-trivial method body (e.g., a `Validate()` method with conditional logic); assert `ContractsAssembliesHaveNoNonTrivialMethods` fails and names the offending type and method | SharedKernel.ArchitectureTests | `●` |
| T-41 | Architecture test Rule 1 (pass path): pass a clean contracts assembly of pure DTOs and records; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-42 | Architecture test Rule 2 (fire path): pass a contracts assembly containing a public property of type `AggregateRoot<Guid>`; assert `ContractsAssembliesHaveNoDomainTypeOnPublicSurface` fails | SharedKernel.ArchitectureTests | `●` |
| T-43 | Architecture test Rule 3 (fire path): pass a contracts assembly with a public method returning `Result<string>`; assert `ContractsAssembliesHaveNoResultTypeOnPublicSurface` fails and failure message contains the offending type name | SharedKernel.ArchitectureTests | `●` |
| T-44 | Architecture test Rule 4 (fire path): pass a contracts assembly with a non-sealed class implementing `IIntegrationEvent`; assert `IntegrationEventImplementationsMustBeSealed` fails and names the offending type | SharedKernel.ArchitectureTests | `●` |
| T-45 | Architecture test Rule 4 (pass path): pass a contracts assembly where all `IIntegrationEvent` implementations are sealed classes or records; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| DO-11 | Document all five ContractsPurity rules in `00.Governance/CLAUDE.md` and `00.Governance/README.md`: rationale, offending-pattern example, compliant-pattern example; Rule 5 documented as guideline with architectural rationale | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Persistence Architecture Enforcement <!-- phase-key: SK.00.PersistenceEnforcement -->

> Protect the EF Core persistence layer's three most critical contracts: `SaveChangesAsync` is only callable by `EfUnitOfWork`, `IRepository` must never return `IQueryable`, and `03.Domain` types must never reference EF Core, Npgsql, or any `SharedKernel.Persistence.*` assembly.

### PersistenceEnforcement — Goal

WO-013 introduced the EF Core persistence layer (`SharedKernel.Persistence.EfCore`) with three load-bearing invariants that, if violated, cause cascading failures: calling `SaveChangesAsync` directly bypasses audit interceptors and soft-delete logic; exposing `IQueryable<T>` on a write-side repository couples application handlers to EF Core internals and breaks the read/write split; and any EF Core or Npgsql reference inside `03.Domain` destroys DDD isolation, making domain logic impossible to test without a database. All three invariants are already stated as hard rules in the root `CLAUDE.md` layering table. This phase encodes them as NetArchTest predicates in a new `PersistenceLayerProtectionRules` static class with two supporting `ICustomRule` predicates, making them CI-enforceable build-time gates.

### PersistenceEnforcement — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/PersistenceLayerProtectionRules.cs` — static class housing all three predicates
  - `SharedKernel.ArchitectureTests/Predicates/NoDirectSaveChangesPredicate.cs` — `ICustomRule`: IL inspection for direct `DbContext.SaveChanges[Async]` call opcodes in types outside `SharedKernel.Persistence.EfCore`
  - `SharedKernel.ArchitectureTests/Predicates/NoIQueryableReturnPredicate.cs` — `ICustomRule`: IL inspection for `IQueryable` return types on methods of `IRepository<T,TId>` implementing types
- Modified files:
  - `00.Governance/CLAUDE.md` — add `PersistenceLayerProtectionRules` to architecture test contracts section; document all three rules with rationale and examples
  - `00.Governance/state-map.md` — this update
- Deleted files: none
- Note: No new Roslyn analyzer SK IDs. All three rules are pure NetArchTest architecture predicates with `ICustomRule` predicates for IL-level checks. The existing `SharedKernelLayeringRules.DomainNeverReferencesPersistence` covers the high-level layering; Rule 3 in this phase extends it with Npgsql and `SharedKernel.Persistence.*` scoping via the established IL predicate approach.

### PersistenceEnforcement — Diagnostic Registry Changes

No new SK diagnostic IDs. All rules are pure NetArchTest architecture predicates:

- Rule 1 — `OnlyEfUnitOfWorkMayCallSaveChanges`: `ICustomRule` (`NoDirectSaveChangesPredicate`) — IL instruction walk for `call`/`callvirt` to `DbContext::SaveChanges` or `DbContext::SaveChangesAsync` in types not in `SharedKernel.Persistence.EfCore` namespace
- Rule 2 — `RepositoriesMustNotExposeIQueryable`: `ICustomRule` (`NoIQueryableReturnPredicate`) — IL inspection of method return types on `IRepository` implementing types; fails on `IQueryable` return type name match
- Rule 3 — `DomainAssembliesNeverReferencePersistenceStack`: NetArchTest iterative `.NotHaveDependencyOn()` calls covering `Microsoft.EntityFrameworkCore`, `Npgsql`, and `SharedKernel.Persistence` — complementary to and more specific than `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure`

### PersistenceEnforcement — Implementation Rules

1. `OnlyEfUnitOfWorkMayCallSaveChanges` uses `NoDirectSaveChangesPredicate` — an `ICustomRule` that walks `TypeDefinition.Methods`, and for each `MethodDefinition.Body.Instructions`, checks for `Call` or `Callvirt` opcodes whose operand is a `MethodReference` with `DeclaringType.Name` equal to `"DbContext"` and `Name` equal to `"SaveChanges"` or `"SaveChangesAsync"`. Returns false (rule violated) for the first type found containing such a call. The predicate is applied only to types whose `TypeDefinition.Namespace` does NOT start with `"SharedKernel.Persistence.EfCore"` — the exemption is implemented inside the predicate as an early-return guard. Failure message includes the offending type name and method name.

2. `RepositoriesMustNotExposeIQueryable` uses `NoIQueryableReturnPredicate` — an `ICustomRule` scoped to types whose `TypeDefinition.Interfaces` contains an entry whose `InterfaceType.Name` starts with `"IRepository"`. For each such type, all `TypeDefinition.Methods` where `IsConstructor` is false and `IsGetter` is false are inspected. If any method's `ReturnType.Name` is `"IQueryable"` or `ReturnType.FullName` contains `"IQueryable"` (covers both the non-generic and generic form in IL), the predicate returns false. Failure message includes the offending type name and the method name returning `IQueryable`.

3. `DomainAssembliesNeverReferencePersistenceStack` uses iterative `.Should().NotHaveDependencyOn()` calls — one per forbidden term — following the same pattern established in `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure`. The forbidden terms are: `"Microsoft.EntityFrameworkCore"`, `"Npgsql"`, `"SharedKernel.Persistence"`. Three calls are required (one per term) because NetArchTest matches each as a substring of the referenced assembly's full name. This is intentionally additive with `DomainAssembliesNeverReferenceInfrastructure`: the latter covers broad infra terms (`EntityFramework`, `MassTransit`, `Redis`, `RabbitMQ`); this rule adds Npgsql and the in-repo persistence packages as a second gate scoped specifically to the EF Core persistence domain.

4. All three factory methods accept `Assembly` as their parameter and return `ConditionList`. Method signatures:
   - `PersistenceLayerProtectionRules.OnlyEfUnitOfWorkMayCallSaveChanges(Assembly)` → `ConditionList`
   - `PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable(Assembly)` → `ConditionList`
   - `PersistenceLayerProtectionRules.DomainAssembliesNeverReferencePersistenceStack(Assembly)` → `ConditionList`

5. `NoDirectSaveChangesPredicate` and `NoIQueryableReturnPredicate` reuse the same Mono.Cecil `TypeDefinition` access pattern established by `DoesNotContainThrowIlPredicate`. If `NetArchTest.eNt` does not expose `IType.Definition` publicly, the existing explicit `Mono.Cecil >= 0.11.5` NuGet reference (established in GuardPurity phase) covers both new predicates — no new NuGet dependency is introduced.

6. `NoDirectSaveChangesPredicate` exemption logic: the namespace guard (`TypeDefinition.Namespace.StartsWith("SharedKernel.Persistence.EfCore")`) is applied as the first check inside the predicate's evaluation method. Types in the exempted namespace are returned as passing (true) unconditionally, regardless of what methods they call. This is the `EfUnitOfWork`-exclusion mechanism.

7. `NoIQueryableReturnPredicate` interface scope: the `"IRepository"` name prefix check on `TypeDefinition.Interfaces` is deliberately broad — it covers `IRepository<T,TId>`, `IReadRepository<T,TId>`, and any sub-interface with the prefix. If a `IReadRepository` returning `IQueryable` is found, the rule still fires; read-side query surfaces belong to Specifications, not raw `IQueryable`. If the consuming test project wishes to scope to write-side only, the predicate must be called on the write-side assembly specifically.

8. `PersistenceLayerProtectionRules` lives in `SharedKernel.ArchitectureTests/Rules/` alongside the other rules static classes. It must not reference `SharedKernel.Persistence.EfCore` or any runtime production package directly. The consuming test project supplies the assembly under test via `typeof(SomePersistenceType).Assembly`.

9. Rule 3 is distinct from `SharedKernelLayeringRules.DomainNeverReferencesPersistence` (which is a broad "domain assembly has no dependency on anything named Persistence") — this new rule is scoped to the specific persistence technology stack (EF Core, Npgsql, in-repo packages) and documents the rationale tied to WO-013's specific packages.

10. Failure messages for all three predicates must identify the offending element: Rule 1 → offending type name + method name containing the `SaveChanges` call; Rule 2 → offending type name + method name returning `IQueryable`; Rule 3 → NetArchTest's default failure output lists the offending assembly reference name.

### PersistenceEnforcement — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/PersistenceLayerProtectionRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: three predicate factory methods returning ConditionList |
| `Predicates/NoDirectSaveChangesPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: IL walk for SaveChanges/SaveChangesAsync calls; exempts SharedKernel.Persistence.EfCore namespace |
| `Predicates/NoIQueryableReturnPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: IL inspection of IRepository implementors for IQueryable return types |

### PersistenceEnforcement — Acceptance Criteria

- [ ] Rule 1 exists and tested: a class calling `SaveChangesAsync` directly (outside `SharedKernel.Persistence.EfCore`) fails the rule; a class using only `IUnitOfWork` passes
- [ ] Rule 2 exists and tested: a type implementing `IRepository<T,TId>` with an `IQueryable<T>` returning method fails the rule; an `IRepository<T,TId>` with no `IQueryable` returns passes
- [ ] Rule 3 exists and tested: a domain type referencing `Microsoft.EntityFrameworkCore` fails; a clean domain type with no EF Core, Npgsql, or `SharedKernel.Persistence.*` dependency passes
- [ ] All three rules documented in `00.Governance/CLAUDE.md` with rationale, offending-pattern example, compliant-pattern example
- [ ] All rules run cleanly against the current SharedKernel codebase without false positives

### PersistenceEnforcement — Dependencies

- Requires P-066 (`SharedKernel.Persistence.EfCore` complete with `IUnitOfWork`, `EfUnitOfWork`, `IRepository<T,TId>` types established): yes — `NoIQueryableReturnPredicate` scopes to `IRepository` implementors; `NoDirectSaveChangesPredicate` exempts the `SharedKernel.Persistence.EfCore` namespace by name
- Requires `DoesNotContainThrowIlPredicate` (Mono.Cecil pattern) from `SK.00.GuardPurity` to be complete: yes (already complete — Mono.Cecil access pattern established; `Mono.Cecil >= 0.11.5` already referenced)
- Unblocks: CI architecture gate integration for the Persistence layer

### PersistenceEnforcement — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref in `SharedKernel.ArchitectureTests` — no change; covers both new predicates)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new SK rules, no change)
- Target framework: `net10.0` (ArchitectureTests)

### PersistenceEnforcement — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-27 | Define `OnlyEfUnitOfWorkMayCallSaveChanges` predicate shape: `NoDirectSaveChangesPredicate` design — IL walk for `DbContext::SaveChanges` and `DbContext::SaveChangesAsync` call opcodes; exemption: types in `SharedKernel.Persistence.EfCore` namespace pass unconditionally; failure message includes offending type + method name | SharedKernel.ArchitectureTests | `●` |
| D-28 | Define `RepositoriesMustNotExposeIQueryable` predicate shape: `NoIQueryableReturnPredicate` design — scope to `IRepository`-prefix implementing types; inspect method return types for `IQueryable` name match; failure message includes offending type + method name | SharedKernel.ArchitectureTests | `●` |
| D-29 | Define `DomainAssembliesNeverReferencePersistenceStack` predicate shape: three iterative `.NotHaveDependencyOn()` calls for `"Microsoft.EntityFrameworkCore"`, `"Npgsql"`, `"SharedKernel.Persistence"`; document relationship to `DomainAssembliesNeverReferenceInfrastructure` (additive, not replacing) | SharedKernel.ArchitectureTests | `●` |
| C-30 | Implement `NoDirectSaveChangesPredicate` in `Predicates/` — `ICustomRule` walking `TypeDefinition.Methods.Body.Instructions` for `Call`/`Callvirt` to `DbContext::SaveChanges` or `DbContext::SaveChangesAsync`; namespace-based exemption for `SharedKernel.Persistence.EfCore`; return false with offending type + method name on violation | SharedKernel.ArchitectureTests | `●` |
| C-31 | Implement `NoIQueryableReturnPredicate` in `Predicates/` — `ICustomRule` scoped to types whose `TypeDefinition.Interfaces` contains an `IRepository`-prefix entry; inspects non-constructor, non-getter method return types for `IQueryable` name match; return false with offending type + method name on violation | SharedKernel.ArchitectureTests | `●` |
| C-32 | Implement `PersistenceLayerProtectionRules` static class in `Rules/` — three factory methods: `OnlyEfUnitOfWorkMayCallSaveChanges(Assembly)` → `ConditionList`, `RepositoriesMustNotExposeIQueryable(Assembly)` → `ConditionList`, `DomainAssembliesNeverReferencePersistenceStack(Assembly)` → `ConditionList` | SharedKernel.ArchitectureTests | `●` |
| T-46 | Architecture test Rule 1 (fire path): pass a contrived assembly containing a class that calls `DbContext.SaveChangesAsync()` directly (not in `SharedKernel.Persistence.EfCore` namespace); assert `OnlyEfUnitOfWorkMayCallSaveChanges` fails and failure message names the offending type | SharedKernel.ArchitectureTests | `●` |
| T-47 | Architecture test Rule 1 (pass path): pass an assembly containing a class that injects and calls only `IUnitOfWork`; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-48 | Architecture test Rule 2 (fire path): pass an assembly containing a type implementing `IRepository<Order, Guid>` with a method returning `IQueryable<Order>`; assert `RepositoriesMustNotExposeIQueryable` fails and failure message names the offending type and method | SharedKernel.ArchitectureTests | `●` |
| T-49 | Architecture test Rule 2 (pass path): pass an assembly containing an `IRepository<Order, Guid>` implementation with no `IQueryable` returning methods; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-50 | Architecture test Rule 3 (fire path): pass a domain assembly that references `Microsoft.EntityFrameworkCore` (e.g., uses `[Key]` attribute); assert `DomainAssembliesNeverReferencePersistenceStack` fails | SharedKernel.ArchitectureTests | `●` |
| T-51 | Architecture test Rule 3 (pass path): pass a clean domain assembly with no EF Core, Npgsql, or `SharedKernel.Persistence.*` dependency; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| DO-12 | Document all three `PersistenceLayerProtectionRules` predicates in `00.Governance/CLAUDE.md` and `00.Governance/README.md`: rationale for each rule tied to WO-013 EF Core contracts, offending-pattern example, compliant-pattern example, cross-reference to root `CLAUDE.md` hard layering rules | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Persistence Architecture Rules Phase 2 — Interface Migration Enforcement <!-- phase-key: SK.00.PersistenceEnforcement2 -->

> Extend `SharedKernel.ArchitectureTests` with four additional architecture enforcement rules that codify the P-078 interface migration corrections: `IUserContext` and tenant-identity contracts must be declared only in `SharedKernel.Security.Abstractions`, `IQueryable<T>` must not appear on any `IReadRepository` public method, and `GetByIdAsync` must not appear on any `IReadRepository` implementor.

### PersistenceEnforcement2 — Goal

P-078 relocated `IUserContext` from a local persistence copy into `SharedKernel.Security.Abstractions` and removed the erroneous `GetByIdAsync` duplication from `IReadRepository`. Without automated architecture rules these corrections will regress: teams copying old templates will redeclare `IUserContext` locally, and contributors unfamiliar with the P-080 decision will re-add `GetByIdAsync` on read repositories. Each of the four rules here corresponds directly to a correctness decision recorded in P-078's work order, making the fix durable at build time.

### PersistenceEnforcement2 — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/PersistenceInterfaceOwnershipRules.cs` — new static class housing all four predicates
  - `SharedKernel.ArchitectureTests/Predicates/InterfaceDeclarationOwnershipPredicate.cs` — `ICustomRule`: checks that a named interface type is not declared in any assembly other than the designated owner
  - `SharedKernel.ArchitectureTests/Predicates/NoGetByIdOnReadRepositoryPredicate.cs` — `ICustomRule`: scoped to `IReadRepository` implementors; fails if any declares a method named `GetByIdAsync`
- Modified files:
  - `SharedKernel.ArchitectureTests/Rules/PersistenceLayerProtectionRules.cs` — no change (existing rules are unchanged; new rules live in the new static class)
  - `00.Governance/CLAUDE.md` — add `PersistenceInterfaceOwnershipRules` and its predicates to the Architecture Test Contracts section; add new rules under Persistence Enforcement section
  - `00.Governance/state-map.md` — this update
- Deleted files: none
- Note: No new Roslyn analyzer SK IDs — all four rules are pure NetArchTest architecture predicates. The existing `NoIQueryableReturnPredicate` already covers `IRepository`-prefix types; Rule 3 here extends its intent explicitly to `IReadRepository` public method signatures (not just return types) using the same predicate with a targeted failure message.

### PersistenceEnforcement2 — Diagnostic Registry Changes

No new SK diagnostic IDs. All rules are pure NetArchTest architecture predicates:

- Rule 1 — `IUserContextDeclaredOnlyInSecurityAbstractions`: `InterfaceDeclarationOwnershipPredicate` — type name `IUserContext` declared outside `SharedKernel.Security.Abstractions` assembly fails this rule
- Rule 2 — `TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions`: same predicate pattern for `ITenantProvider` and `ICurrentTenantService` — both must be declared only in `SharedKernel.Security.Abstractions`
- Rule 3 — `IReadRepositoryMustNotExposeIQueryable`: extends existing IQueryable enforcement explicitly to `IReadRepository` implementing types — any public method or property returning `IQueryable` fails
- Rule 4 — `NoGetByIdAsyncOnReadRepository`: `NoGetByIdOnReadRepositoryPredicate` — any class implementing `IReadRepository<,>` that declares a method named `GetByIdAsync` fails this rule

### PersistenceEnforcement2 — Implementation Rules

1. `PersistenceInterfaceOwnershipRules.IUserContextDeclaredOnlyInSecurityAbstractions(params Assembly[] assemblies)` takes the production assemblies to scan (excluding `SharedKernel.Security.Abstractions` itself — the caller must not pass it). Uses `Types.InAssembly(assembly).That().HaveName("IUserContext").Should().NotExist()` per non-owner assembly. Alternatively, uses `InterfaceDeclarationOwnershipPredicate` which checks `TypeDefinition.Name == "IUserContext"` across provided assemblies and fails if any such type is found. Failure message must name the offending assembly and type. The ownership assembly (`SharedKernel.Security.Abstractions`) is never passed to this method — it is the correct home.

2. `PersistenceInterfaceOwnershipRules.TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions(params Assembly[] assemblies)` follows the identical pattern for both `ITenantProvider` and `ICurrentTenantService`. Uses `InterfaceDeclarationOwnershipPredicate` configured with both names (OR match). Failure message must name the offending type name and the assembly it was found in.

3. `PersistenceInterfaceOwnershipRules.IReadRepositoryMustNotExposeIQueryable(Assembly assembly)` uses `NoIQueryableReturnPredicate` (already defined in `SharedKernel.ArchitectureTests`) but scoped specifically to types whose `TypeDefinition.Interfaces` contains an entry whose `InterfaceType.Name` starts with `"IReadRepository"` (not the broader `"IRepository"` prefix used by the existing rule). This prevents the broader rule from being relied upon for `IReadRepository` — the failure message must identify it as an `IReadRepository`-specific violation. Returns `ConditionList`.

4. `PersistenceInterfaceOwnershipRules.NoGetByIdAsyncOnReadRepository(Assembly assembly)` uses `NoGetByIdOnReadRepositoryPredicate` — an `ICustomRule` that scopes to types whose `TypeDefinition.Interfaces` contains an entry whose `InterfaceType.Name` starts with `"IReadRepository"`. For each such type, inspects `TypeDefinition.Methods` for any entry whose `Name` equals `"GetByIdAsync"` (exact match). If found, returns false (rule violated) with a failure message naming the offending type and pointing to `FindByIdAsync` as the correct alternative. Returns `ConditionList`.

5. `InterfaceDeclarationOwnershipPredicate` is a new `ICustomRule` in `Predicates/`. It is constructed with a set of interface names to check (e.g., `{ "IUserContext" }` or `{ "ITenantProvider", "ICurrentTenantService" }`). For each type, if `TypeDefinition.Name` is in the configured name set, the predicate returns false (violation). The predicate is instantiated per call site — it never caches state. Failure message includes the offending type name and its `TypeDefinition.Module.Assembly.Name.Name` for assembly identification.

6. `NoGetByIdOnReadRepositoryPredicate` is a new `ICustomRule` in `Predicates/`. Scope check: `TypeDefinition.Interfaces` contains entry with `InterfaceType.Name` starting with `"IReadRepository"`. Method scan: iterates `TypeDefinition.Methods` for `Name == "GetByIdAsync"`. Returns false with failure message: `"{offendingType}.GetByIdAsync must be removed — use FindByIdAsync (returns Result<T>) or GetAsync (returns T?) instead. GetByIdAsync was removed in P-080 to eliminate duplication."`. Returns `ConditionList` from `PersistenceInterfaceOwnershipRules`.

7. Both new predicates reuse the established Mono.Cecil `TypeDefinition` access pattern from `DoesNotContainThrowIlPredicate`. No new NuGet dependency — the existing `Mono.Cecil >= 0.11.5` explicit reference in `SharedKernel.ArchitectureTests` covers both.

8. All four factory methods in `PersistenceInterfaceOwnershipRules` return `ConditionList` and accept the assembly(-ies) under test as parameters. No assembly paths are hard-coded. The consuming test project must supply the relevant production assembly via `typeof(SomeProductionType).Assembly`.

9. Rule 3 (`IReadRepositoryMustNotExposeIQueryable`) is distinct from `PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable` — the latter covers the broad `IRepository` prefix; this rule is scoped specifically to `IReadRepository` and carries a targeted failure message for read-side violations. Both rules may run in the same test suite without conflict.

10. Each of the four rules must carry a failure message pointing to the correct pattern: Rule 1 → declare `IUserContext` only in `SharedKernel.Security.Abstractions`; Rule 2 → declare `ITenantProvider`/`ICurrentTenantService` only in `SharedKernel.Security.Abstractions`; Rule 3 → use `Specification<T>` for query surface on `IReadRepository`; Rule 4 → remove `GetByIdAsync`, use `FindByIdAsync` or `GetAsync`.

### PersistenceEnforcement2 — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/PersistenceInterfaceOwnershipRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: four predicate factory methods returning ConditionList |
| `Predicates/InterfaceDeclarationOwnershipPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: detects named interface type declarations in non-owner assemblies |
| `Predicates/NoGetByIdOnReadRepositoryPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: scoped to IReadRepository implementors; fails on GetByIdAsync method declaration |

### PersistenceEnforcement2 — Acceptance Criteria

- [ ] Rule 1 exists as `[Fact]`: `IUserContext` declared in any assembly other than `SharedKernel.Security.Abstractions` fails the rule; failure message names the offending assembly
- [ ] Rule 2 exists as `[Fact]`: `ITenantProvider` or `ICurrentTenantService` declared outside `SharedKernel.Security.Abstractions` fails; failure message names the offending type
- [ ] Rule 3 exists as `[Fact]`: an `IReadRepository<,>` implementor with an `IQueryable<T>` public method fails; a clean implementor passes
- [ ] Rule 4 exists as `[Fact]`: an `IReadRepository<,>` implementor with a `GetByIdAsync` method fails; failure message points to `FindByIdAsync` / `GetAsync`; a clean implementor without `GetByIdAsync` passes
- [ ] All four tests carry a descriptive failure message pointing to the correct pattern and package
- [ ] All four tests pass against the codebase after P-078 and P-080 are complete
- [ ] `00.Governance/CLAUDE.md` updated with the four new rules under a Persistence Interface Ownership section

### PersistenceEnforcement2 — Dependencies

- Requires P-078 (`IUserContext` moved to `SharedKernel.Security.Abstractions`, `GetByIdAsync` removed from `IReadRepository`) to be complete: yes — the rules are meaningless before the migration occurs; passing the test requires the correct state
- Depends on `NoIQueryableReturnPredicate` from `SK.00.PersistenceEnforcement` for Rule 3's underlying IL check: yes (already defined)
- Unblocks: P-083 architecture gates integrated into CI after P-078/P-080 completion

### PersistenceEnforcement2 — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref — no change; covers both new predicates)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new SK rules, no change)
- Target framework: `net10.0` (ArchitectureTests)

### PersistenceEnforcement2 — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-30 | Define `PersistenceInterfaceOwnershipRules` static class shape: four predicate factory methods; `InterfaceDeclarationOwnershipPredicate` design (TypeDefinition.Name set match); `NoGetByIdOnReadRepositoryPredicate` design (IReadRepository scope + GetByIdAsync name match); failure message contract for each rule | SharedKernel.ArchitectureTests | `●` |
| C-33 | Implement `InterfaceDeclarationOwnershipPredicate` in `Predicates/` — `ICustomRule` constructed with a set of interface names to detect; returns false with offending type name + assembly name for any type whose `TypeDefinition.Name` is in the configured set | SharedKernel.ArchitectureTests | `●` |
| C-34 | Implement `NoGetByIdOnReadRepositoryPredicate` in `Predicates/` — `ICustomRule` scoped to types with `IReadRepository`-prefix interfaces; returns false if any method is named `"GetByIdAsync"`; failure message names offending type and references `FindByIdAsync`/`GetAsync` as the correct alternative | SharedKernel.ArchitectureTests | `●` |
| C-35 | Implement `PersistenceInterfaceOwnershipRules` static class in `Rules/` — four factory methods: `IUserContextDeclaredOnlyInSecurityAbstractions(params Assembly[])`, `TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions(params Assembly[])`, `IReadRepositoryMustNotExposeIQueryable(Assembly)`, `NoGetByIdAsyncOnReadRepository(Assembly)` — all return `ConditionList` | SharedKernel.ArchitectureTests | `●` |
| T-52 | Architecture test Rule 1 (fire path): pass an assembly containing a type named `IUserContext` (not `SharedKernel.Security.Abstractions`); assert `IUserContextDeclaredOnlyInSecurityAbstractions` fails and failure message names the offending assembly | SharedKernel.ArchitectureTests | `●` |
| T-53 | Architecture test Rule 1 (pass path): pass only assemblies that do not declare `IUserContext`; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-54 | Architecture test Rule 2 (fire path): pass an assembly declaring `ITenantProvider` outside `SharedKernel.Security.Abstractions`; assert `TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions` fails | SharedKernel.ArchitectureTests | `●` |
| T-55 | Architecture test Rule 2 (pass path): pass assemblies with no `ITenantProvider` or `ICurrentTenantService` declarations; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-56 | Architecture test Rule 3 (fire path): pass an assembly with an `IReadRepository<Order, Guid>` implementor that has a method returning `IQueryable<Order>`; assert `IReadRepositoryMustNotExposeIQueryable` fails and failure message names the offending type and method | SharedKernel.ArchitectureTests | `●` |
| T-57 | Architecture test Rule 3 (pass path): pass a clean `IReadRepository<Order, Guid>` implementation with no IQueryable methods; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-58 | Architecture test Rule 4 (fire path): pass an assembly with a class implementing `IReadRepository<Order, Guid>` that declares `GetByIdAsync`; assert `NoGetByIdAsyncOnReadRepository` fails and failure message references the correct alternatives | SharedKernel.ArchitectureTests | `●` |
| T-59 | Architecture test Rule 4 (pass path): pass a class implementing `IReadRepository<Order, Guid>` with no `GetByIdAsync` method; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| DO-13 | Document all four `PersistenceInterfaceOwnershipRules` predicates in `00.Governance/CLAUDE.md`: rationale tying each rule to P-078/P-080 migration decision, offending-pattern example, compliant-pattern example, failure message content | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Architecture Rule — IUserContext Audit String Adapter and Repository Contract Completeness <!-- phase-key: SK.00.PersistenceContractCompleteness -->

> Two new architecture enforcement rules ensure audit trail GUID format consistency (SK0011 Roslyn analyzer) and that all `IRepository<,>` and `IReadRepository<,>` implementors in the persistence layer declare the batch methods added in P-093: `ExistsAsync` and `GetByIdsAsync`.

### PersistenceContractCompleteness — Goal

P-091 established a defined GUID string conversion pattern for `AuditInterceptor` and `SoftDeleteInterceptor`. Without enforcement, a contributor adding a new interceptor or modifying existing ones can silently introduce `Guid.ToString("N")` (produces `"d3e4f5a6..."` — no hyphens) instead of the canonical `ToString()` / `ToString("D")` (produces `"d3e4f5a6-..."` with hyphens), causing inconsistent `CreatedBy`/`ModifiedBy` column values across services that share the same audit schema. P-093 added `ExistsAsync` and `GetByIdsAsync` to the repository interfaces; any concrete repository that does not implement these methods will compile successfully (the interface is satisfied) but will fail at runtime with `NotImplementedException` if the base class provides a stub. An architecture rule with a descriptive failure message surfaces this gap earlier.

### PersistenceContractCompleteness — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`, `SharedKernel.Analyzers`
- New files:
  - `SharedKernel.Analyzers/Diagnostics/SK0011_GuidFormatCodeMisuseAnalyzer.cs` — Roslyn analyzer: SK0011 fires when `Guid.ToString("N")`, `"B"`, `"P"`, or `"X"` format codes are detected; only `ToString()` and `ToString("D")` permitted
  - `SharedKernel.ArchitectureTests/Rules/RepositoryContractCompletenessRules.cs` — new static class housing Rules 2 and 3
- Modified files:
  - `00.Governance/CLAUDE.md` — add SK0011 to diagnostic registry; add `RepositoryContractCompletenessRules` to architecture test contracts section; document all three rules under Persistence Enforcement
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### PersistenceContractCompleteness — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0011 | GuidFormatCodeMisuse | Design | Warning | `Guid.ToString(string)` called with a format argument other than `"D"` — format codes `"N"`, `"B"`, `"P"`, `"X"` are forbidden; only `ToString()` (no argument) or `ToString("D")` produce the canonical hyphenated lowercase format required for consistent audit column values |

### PersistenceContractCompleteness — Implementation Rules

1. SK0011 `GuidFormatCodeMisuseAnalyzer` operates on `InvocationExpressionSyntax` nodes of the form `<expression>.ToString(<stringLiteralArgument>)`. The analyzer fires when:
   - The method name is `ToString` (simple name check on the `MemberAccessExpressionSyntax.Name`)
   - The invocation has exactly one argument that is a `LiteralExpressionSyntax` of kind `SyntaxKind.StringLiteralExpression`
   - The string literal value (after stripping quotes) is one of `"N"`, `"B"`, `"P"`, or `"X"` (case-insensitive match — these are all defined format specifiers for `Guid`)
   - The receiver expression's type resolves to `System.Guid` (requires `SemanticModel.GetTypeInfo` to verify the receiver is a Guid — this is the one place where a minimal semantic model check is required to avoid false positives from non-Guid `ToString("N")` calls)
   Severity: `Warning`. Fix message: use `ToString()` or `ToString("D")` for the canonical hyphenated lowercase GUID format.

2. SK0011 must target `netstandard2.0` (same constraint as all other SK analyzers). The semantic model check (`GetTypeInfo`) is the minimum required for correctness — the Guid type is a BCL type resolvable without additional NuGet references beyond `Microsoft.CodeAnalysis.CSharp`.

3. SK0011 suppression: the analyzer does not define a suppression namespace. It fires globally. Suppress per-call-site via `#pragma warning disable SK0011` when a non-`"D"` format is genuinely required (e.g., a compact Guid used as a URL segment). Document the suppression requirement in the `HelpLinkUri` entry.

4. `RepositoryContractCompletenessRules.AllRepositoryImplementorsMustHaveExistsAsync(Assembly assembly)` uses NetArchTest fluent API:
   `Types.InAssembly(assembly).That().ImplementInterface(typeof(IRepository<,>)).And().AreNotAbstract().Should().HaveMember("ExistsAsync")`
   Because NetArchTest.eNt 1.3.2 may not support `HaveMember()` for open-generic interface matching, the preferred approach is a custom `ICustomRule` (`HasMethodNamePredicate`) that inspects `TypeDefinition.Methods` for a method named `"ExistsAsync"`. Scope: types whose `TypeDefinition.Interfaces` contains any entry whose `InterfaceType.Name` starts with `"IRepository"` (and not `"IReadRepository"`). Returns false (rule violated) with failure message: `"{offendingType} implements IRepository<,> but does not declare ExistsAsync. Add ExistsAsync per the interface contract defined in P-093."` Returns `ConditionList`.

5. `RepositoryContractCompletenessRules.AllReadRepositoryImplementorsMustHaveGetByIdsAsync(Assembly assembly)` follows the identical pattern for `IReadRepository`-prefix types, checking for a method named `"GetByIdsAsync"`. Failure message: `"{offendingType} implements IReadRepository<,> but does not declare GetByIdsAsync. Add GetByIdsAsync(IEnumerable<TId> ids) per the interface contract defined in P-093."` Returns `ConditionList`.

6. Both Rules 2 and 3 use a shared `HasRequiredMethodPredicate` (or per-rule ICustomRule instances) that accepts the required method name as a constructor parameter and the interface name prefix for scoping. This avoids duplicating the same IL inspection logic for two methods. The predicate lives in `Predicates/HasRequiredMethodPredicate.cs`.

7. `HasRequiredMethodPredicate` is a new `ICustomRule` in `Predicates/`. Constructor: `HasRequiredMethodPredicate(string interfaceNamePrefix, string requiredMethodName)`. For each type, checks `TypeDefinition.Interfaces` for the prefix scope, then checks `TypeDefinition.Methods` for a method matching `requiredMethodName` (exact name match, any overload). Returns false (rule violated) if no such method is found. Failure message includes the offending type name, the required method name, and the implementing interface.

8. All methods in `RepositoryContractCompletenessRules` accept `Assembly` as their parameter and return `ConditionList`. The consuming test project must reference `SharedKernel.Persistence.Abstractions` to supply the assembly containing `IRepository<,>` and `IReadRepository<,>` implementors. The class lives in `SharedKernel.ArchitectureTests/Rules/`.

9. `RepositoryContractCompletenessRules` must not hard-code assembly paths. It uses `Types.InAssembly(assembly)` exclusively. The caller supplies the persistence assembly via `typeof(SomeRepositoryType).Assembly`.

10. `HasRequiredMethodPredicate` reuses the same Mono.Cecil `TypeDefinition` access pattern as the existing predicates. No new NuGet dependency.

### PersistenceContractCompleteness — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Diagnostics/SK0011_GuidFormatCodeMisuseAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0011 fires on `Guid.ToString("N"/"B"/"P"/"X")` — enforces canonical `"D"` hyphenated format |
| `Rules/RepositoryContractCompletenessRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: two predicate factory methods — `AllRepositoryImplementorsMustHaveExistsAsync`, `AllReadRepositoryImplementorsMustHaveGetByIdsAsync` |
| `Predicates/HasRequiredMethodPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: scoped to interface-prefix types; fails if required method name is absent |

### PersistenceContractCompleteness — Acceptance Criteria

- [ ] SK0011 exists: `Guid.ToString("N")` in `AuditInterceptor` triggers Warning; `Guid.ToString("D")` and plain `Guid.ToString()` do not trigger; documented in `00.Governance/CLAUDE.md`
- [ ] SK0011 semantic model check: the analyzer must not fire on a non-Guid type's `ToString("N")` call (e.g., `someString.ToString("N")` or `intValue.ToString("N")`)
- [ ] Rule 2 exists: `[Fact]` test asserts all `IRepository<,>` implementors in `06.Persistence` assemblies have `ExistsAsync`; test passes on P-093-updated repositories; documented in `00.Governance/CLAUDE.md`
- [ ] Rule 3 exists: `[Fact]` test asserts all `IReadRepository<,>` implementors in `06.Persistence` assemblies have `GetByIdsAsync`; documented in `00.Governance/CLAUDE.md`
- [ ] All three rules run as `[Fact]` tests in the governance test suite; each has a descriptive failure message
- [ ] Governance test suite passes with all new rules included

### PersistenceContractCompleteness — Dependencies

- Requires P-091 (`AuditInterceptor` and `SoftDeleteInterceptor` updated to use canonical GUID format): yes — SK0011 must pass on the updated interceptors; before P-091 the rule would fire as a warning on any existing non-`"D"` format usage
- Requires P-093 (`ExistsAsync` on `IRepository<,>`, `GetByIdsAsync` on `IReadRepository<,>` added to the persistence interface): yes — Rules 2 and 3 assert method presence; without P-093 no existing implementations have these methods and both rules fail
- Depends on `HasRequiredMethodPredicate` being a new predicate (no existing predicate serves this purpose): no prior dependency
- Unblocks: CI contract completeness gate for all current and future `IRepository<,>` and `IReadRepository<,>` implementors in downstream services

### PersistenceContractCompleteness — Tooling Version Notes

- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — SK0011 follows same constraint; semantic model `GetTypeInfo` is available in this version)
- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref — no change; `HasRequiredMethodPredicate` uses same access pattern)
- Target framework: `netstandard2.0` (Analyzers) / `net10.0` (ArchitectureTests)

### PersistenceContractCompleteness — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-31 | Define SK0011 `GuidFormatCodeMisuse` — trigger: `Guid.ToString(string)` with format argument in `{"N","B","P","X"}` (case-insensitive); semantic model receiver-type check required to avoid false positives; severity: Warning; suppression: per-call-site `#pragma warning disable SK0011`; fix message: use `ToString()` or `ToString("D")` | SharedKernel.Analyzers | `●` |
| D-32 | Define `RepositoryContractCompletenessRules` static class shape: `AllRepositoryImplementorsMustHaveExistsAsync(Assembly)` → `ConditionList`; `AllReadRepositoryImplementorsMustHaveGetByIdsAsync(Assembly)` → `ConditionList`; `HasRequiredMethodPredicate` design — interface name prefix scope + method name existence check via `TypeDefinition.Methods` | SharedKernel.ArchitectureTests | `●` |
| C-36 | Implement SK0011 `GuidFormatCodeMisuseAnalyzer` — `InvocationExpressionSyntax` walker; extract `ToString` method name + single string literal argument; `SemanticModel.GetTypeInfo` check on receiver for `System.Guid`; report SK0011 on the format argument literal if format code is in `{"N","B","P","X"}` | SharedKernel.Analyzers | `●` |
| C-37 | Implement `HasRequiredMethodPredicate` in `Predicates/` — `ICustomRule` constructed with `interfaceNamePrefix` and `requiredMethodName`; scopes to types whose `TypeDefinition.Interfaces` contains a prefix-matching entry; returns false with failure message if no method matching `requiredMethodName` is found in `TypeDefinition.Methods` | SharedKernel.ArchitectureTests | `●` |
| C-38 | Implement `RepositoryContractCompletenessRules` static class in `Rules/` — two factory methods using `HasRequiredMethodPredicate("IRepository", "ExistsAsync")` and `HasRequiredMethodPredicate("IReadRepository", "GetByIdsAsync")`; both return `ConditionList` | SharedKernel.ArchitectureTests | `●` |
| T-60 | Analyzer test SK0011 (fire path): `guid.ToString("N")` triggers SK0011; `guid.ToString("B")` triggers SK0011 | SharedKernel.Analyzers.Tests | `●` |
| T-61 | Analyzer test SK0011 (pass path): `guid.ToString()` (no argument) does not trigger SK0011; `guid.ToString("D")` does not trigger SK0011 | SharedKernel.Analyzers.Tests | `●` |
| T-62 | Analyzer test SK0011 (non-Guid pass path): `someString.ToString("N")` does not trigger SK0011 — semantic model receiver-type guard prevents false positive | SharedKernel.Analyzers.Tests | `●` |
| T-63 | Architecture test Rule 2 (fire path): pass an assembly with an `IRepository<Order, Guid>` implementor that does NOT declare `ExistsAsync`; assert `AllRepositoryImplementorsMustHaveExistsAsync` fails and failure message names the offending type | SharedKernel.ArchitectureTests | `●` |
| T-64 | Architecture test Rule 2 (pass path): pass an assembly where all `IRepository<,>` implementors declare `ExistsAsync`; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-65 | Architecture test Rule 3 (fire path): pass an assembly with an `IReadRepository<Order, Guid>` implementor that does NOT declare `GetByIdsAsync`; assert `AllReadRepositoryImplementorsMustHaveGetByIdsAsync` fails and names the offending type | SharedKernel.ArchitectureTests | `●` |
| T-66 | Architecture test Rule 3 (pass path): pass an assembly where all `IReadRepository<,>` implementors declare `GetByIdsAsync`; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| DO-14 | Document SK0011 and both `RepositoryContractCompletenessRules` predicates in `00.Governance/CLAUDE.md`: rationale tied to P-091 audit trail consistency and P-093 interface contract, offending-pattern examples, compliant-pattern examples, failure message content | SharedKernel.Analyzers, SharedKernel.ArchitectureTests | `●` |

---

## Phase: EfCore Package Hygiene Architecture Rules <!-- phase-key: SK.00.EfCorePackageHygiene -->

> Three new NetArchTest enforcement rules closing the regression vectors introduced by WO-017: no concrete downcast of `ISpecificationEvaluator<T>`, `IUnitOfWork` implementors must have exactly one public constructor, and the application layer must never reference `IDbContextTransaction` directly.

### EfCorePackageHygiene — Goal

WO-017 introduced three precision fixes to the EF Core persistence layer: P-097 replaced a concrete downcast of `ISpecificationEvaluator<T>` with a proper abstraction (exposing `GetProjectedQuery` on the interface), P-098 reduced `EfUnitOfWork` to a single constructor to eliminate DI ambiguity, and P-099 introduced `ITransactionalUnitOfWork` as the only permitted transaction surface in application code. Each of these fixes is trivially reversible by a future contributor who is not aware of the rationale. This phase encodes all three as CI-blocking NetArchTest architecture rules: Rule 1 prevents the concrete-downcast anti-pattern from returning, Rule 2 prevents a second constructor from being added to `IUnitOfWork` implementors, and Rule 3 enforces the transaction abstraction boundary by ensuring no type in the `05.Application` layer ever imports `IDbContextTransaction` directly.

### EfCorePackageHygiene — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/EfCorePackageHygieneRules.cs` — static class housing all three predicate factory methods
  - `SharedKernel.ArchitectureTests/Predicates/NoSpecificationEvaluatorDowncastPredicate.cs` — `ICustomRule`: IL inspection for casts from `ISpecificationEvaluator<>` to any concrete class in the `SharedKernel.Persistence.EfCore` assembly
  - `SharedKernel.ArchitectureTests/Predicates/NoDbContextTransactionInApplicationPredicate.cs` — `ICustomRule`: dependency name check for `IDbContextTransaction` references in types whose namespace matches an `05.Application` pattern
- Modified files:
  - `00.Governance/CLAUDE.md` — add `EfCorePackageHygieneRules` to architecture test contracts section; document all three rules with rationale, offending/compliant patterns, and exemptions
  - `00.Governance/state-map.md` — this update
- Deleted files: none
- Note: No new Roslyn analyzer SK IDs. All three rules are pure NetArchTest architecture predicates (two backed by custom `ICustomRule` predicates, one using NetArchTest fluent API directly).

### EfCorePackageHygiene — Diagnostic Registry Changes

No new SK diagnostic IDs. All rules are pure NetArchTest architecture predicates:

- Rule 1 — `NoSpecificationEvaluatorDowncastInEfCoreAssembly`: `ICustomRule` (`NoSpecificationEvaluatorDowncastPredicate`) — IL instruction walk for `castclass` opcodes whose target type name starts with `"SpecificationEvaluator"` inside the `SharedKernel.Persistence.EfCore` assembly
- Rule 2 — `IUnitOfWorkImplementorsMustHaveExactlyOneConstructor`: NetArchTest fluent API — scope to types implementing `IUnitOfWork` in `06.Persistence`; assert exactly one public constructor each
- Rule 3 — `ApplicationLayerMustNotReferenceDbContextTransaction`: `ICustomRule` (`NoDbContextTransactionInApplicationPredicate`) — dependency namespace check for `Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction` on types in assemblies whose name matches the `05.Application` pattern; exempts `SharedKernel.Persistence.EfCore` namespace

### EfCorePackageHygiene — Implementation Rules

1. `EfCorePackageHygieneRules.NoSpecificationEvaluatorDowncastInEfCoreAssembly(Assembly assembly)` uses `NoSpecificationEvaluatorDowncastPredicate` — an `ICustomRule` that walks `TypeDefinition.Methods.Body.Instructions` for each method. For each instruction whose `OpCode` is `Mono.Cecil.Cil.OpCodes.Castclass`, the operand is cast to `TypeReference` and its `Name` is checked for the prefix `"SpecificationEvaluator"` (case-sensitive). If any such cast is found in a type inside the `SharedKernel.Persistence.EfCore` assembly, the predicate returns false. Failure message includes the offending type name and method name containing the cast. Returns `ConditionList`.
   - Rationale: the downcast `(SpecificationEvaluator<T>)evaluator` on an `ISpecificationEvaluator<T>` reference was the pre-P-097 pattern that P-097 eliminated by exposing `GetProjectedQuery` on the interface. This rule ensures the pattern cannot silently return.
   - Offending pattern: `var concreteEval = (SpecificationEvaluator<T>)_evaluator;`
   - Compliant pattern: `_evaluator.GetProjectedQuery(query, spec)` — uses the interface method

2. `EfCorePackageHygieneRules.IUnitOfWorkImplementorsMustHaveExactlyOneConstructor(Assembly assembly)` uses a custom `ICustomRule` (`SingleConstructorPredicate`) that scopes to types whose `TypeDefinition.Interfaces` contains an entry whose `InterfaceType.Name` equals `"IUnitOfWork"`. For each such type, the predicate counts `TypeDefinition.Methods` where `IsConstructor` is true and `IsStatic` is false (instance constructors only). If the count is not exactly 1, the predicate returns false with a failure message naming the offending type and the actual constructor count. Returns `ConditionList`.
   - Rationale: `EfUnitOfWork` was reduced to a single constructor in P-098 to resolve DI ambiguity. Adding a second constructor (e.g., a "convenience constructor") would reintroduce the ambiguity silently — two registrations would compete for injection.
   - Offending pattern: `class EfUnitOfWork : IUnitOfWork { public EfUnitOfWork(DbContext ctx) { } public EfUnitOfWork() { } }`
   - Compliant pattern: `class EfUnitOfWork : IUnitOfWork { public EfUnitOfWork(DbContext ctx) { } }`
   - Note: `SingleConstructorPredicate` is a new `ICustomRule` in `Predicates/SingleConstructorPredicate.cs` — no prior predicate covers this pattern.

3. `EfCorePackageHygieneRules.ApplicationLayerMustNotReferenceDbContextTransaction(Assembly assembly)` uses `NoDbContextTransactionInApplicationPredicate` — an `ICustomRule` that checks `TypeDefinition.Methods.Body.Instructions` for `Call` or `Callvirt` opcodes whose `MethodReference.DeclaringType.FullName` contains `"IDbContextTransaction"`, and also checks `TypeDefinition.Fields` and `TypeDefinition.Methods` parameter types for any `TypeReference` whose `FullName` contains `"IDbContextTransaction"`. Exemption: types whose `TypeDefinition.Namespace` starts with `"SharedKernel.Persistence.EfCore"` or `"SharedKernel.Persistence"` are returned as passing (true) unconditionally — the persistence layer itself is the only legitimate user of `IDbContextTransaction`. Failure message includes the offending type name and the reference location (field, parameter, or method call). Returns `ConditionList`.
   - Rationale: `ITransactionalUnitOfWork` is the only permitted transaction entry point for application handlers (P-099). Direct injection of `IDbContextTransaction` couples the application handler to EF Core's specific transaction implementation, undermining the abstraction boundary.
   - Offending pattern: `class CreateOrderHandler { public CreateOrderHandler(IDbContextTransaction tx) { } }`
   - Compliant pattern: `class CreateOrderHandler { public CreateOrderHandler(ITransactionalUnitOfWork unitOfWork) { } }`
   - Exemption: `SharedKernel.Persistence.EfCore` and `SharedKernel.Persistence.*` namespace types — the persistence implementation layer is allowed to reference `IDbContextTransaction` internally.

4. All three factory methods accept `Assembly assembly` as their parameter and return `ConditionList`. Method signatures:
   - `EfCorePackageHygieneRules.NoSpecificationEvaluatorDowncastInEfCoreAssembly(Assembly)` → `ConditionList`
   - `EfCorePackageHygieneRules.IUnitOfWorkImplementorsMustHaveExactlyOneConstructor(Assembly)` → `ConditionList`
   - `EfCorePackageHygieneRules.ApplicationLayerMustNotReferenceDbContextTransaction(Assembly)` → `ConditionList`

5. All three predicates reuse the established Mono.Cecil `TypeDefinition` access pattern. No new NuGet dependencies — the existing `Mono.Cecil >= 0.11.5` explicit reference in `SharedKernel.ArchitectureTests` covers all new predicates.

6. `EfCorePackageHygieneRules` lives in `SharedKernel.ArchitectureTests/Rules/` alongside the existing rule static classes. It must not reference `SharedKernel.Persistence.EfCore` or any runtime production package directly. The consuming test project supplies the assembly under test via `typeof(SomePersistenceType).Assembly` or `typeof(SomeApplicationType).Assembly`.

7. Failure messages for all three predicates must identify the offending element:
   - Rule 1 → offending type name + method name containing the downcast instruction
   - Rule 2 → offending type name + actual constructor count (e.g., `"EfUnitOfWork has 2 public constructors; expected exactly 1."`)
   - Rule 3 → offending type name + the declaration site (field, constructor parameter, or method return type) referencing `IDbContextTransaction`

8. `NoSpecificationEvaluatorDowncastPredicate` and `NoDbContextTransactionInApplicationPredicate` live in `Predicates/`. `SingleConstructorPredicate` also lives in `Predicates/`. All three are internal to `SharedKernel.ArchitectureTests`.

9. Rule 3 consumer note: the consuming architecture test project must pass the `05.Application` assembly (e.g., `typeof(SomeApplicationHandler).Assembly`). The persistence-layer assemblies must NOT be passed — the exemption inside the predicate is a safety net, not the primary enforcement mechanism. The primary mechanism is scoping the test to the application assembly only.

### EfCorePackageHygiene — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/EfCorePackageHygieneRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: three predicate factory methods returning ConditionList |
| `Predicates/NoSpecificationEvaluatorDowncastPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: IL walk for castclass opcodes targeting SpecificationEvaluator concrete types |
| `Predicates/SingleConstructorPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: counts instance constructors on IUnitOfWork implementors; fails if count ≠ 1 |
| `Predicates/NoDbContextTransactionInApplicationPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: dependency check for IDbContextTransaction references; exempts Persistence namespace |

### EfCorePackageHygiene — Acceptance Criteria

- [ ] Rule 1 exists as a `[Fact]` test asserting no `ISpecificationEvaluator<>` → concrete-type casts exist in `SharedKernel.Persistence.EfCore` assembly; fire-path test uses a contrived violation fixture; pass-path test uses a clean fixture
- [ ] Rule 2 exists asserting all `IUnitOfWork` implementors in `06.Persistence` have exactly one public constructor; passes on `EfUnitOfWork` after P-098; fire-path test catches a two-constructor implementor; pass-path test passes on a single-constructor implementor
- [ ] Rule 3 exists asserting no type in `05.Application` namespace pattern references `IDbContextTransaction`; exempts `SharedKernel.Persistence.*` assemblies; fire-path test catches an application handler injecting `IDbContextTransaction`; pass-path test passes on a handler using `ITransactionalUnitOfWork`
- [ ] All three rules documented in `00.Governance/CLAUDE.md` with rationale, offending-pattern example, compliant-pattern example, and exemptions
- [ ] All three rules run as `[Fact]` tests with descriptive failure messages
- [ ] Governance test suite passes with all new rules included; no false positives on existing SharedKernel assemblies

### EfCorePackageHygiene — Dependencies

- Requires P-097 (`ISpecificationEvaluator<T>` interface has `GetProjectedQuery` — the downcast no longer occurs in `SharedKernel.Persistence.EfCore`): yes — Rule 1 must pass cleanly after P-097; before P-097 it would fire on the existing code
- Requires P-098 (`EfUnitOfWork` reduced to a single constructor): yes — Rule 2 must pass on `EfUnitOfWork`; before P-098 `EfUnitOfWork` may have had two constructors and the rule would fire
- Requires P-099 (`ITransactionalUnitOfWork` defined in persistence abstractions): yes — Rule 3 references `IDbContextTransaction` as the forbidden type; `ITransactionalUnitOfWork` must exist for the compliant alternative to be actionable
- Unblocks: CI architecture gate integration for EfCore package hygiene contracts

### EfCorePackageHygiene — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref in `SharedKernel.ArchitectureTests` — no change; covers all three new predicates)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new SK analyzer rules, no change)
- Target framework: `net10.0` (ArchitectureTests)

### EfCorePackageHygiene — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-33 | Define Rule 1 shape: `NoSpecificationEvaluatorDowncastInEfCoreAssembly` — `NoSpecificationEvaluatorDowncastPredicate` design: IL walk for `castclass` opcodes targeting type names starting with `"SpecificationEvaluator"`; scoped to `SharedKernel.Persistence.EfCore` assembly; failure message: offending type + method name | SharedKernel.ArchitectureTests | `●` |
| D-34 | Define Rule 2 shape: `IUnitOfWorkImplementorsMustHaveExactlyOneConstructor` — `SingleConstructorPredicate` design: scope to types implementing `IUnitOfWork` interface; count instance constructors (IsConstructor && !IsStatic); fail if count ≠ 1; failure message: offending type + actual constructor count | SharedKernel.ArchitectureTests | `●` |
| D-35 | Define Rule 3 shape: `ApplicationLayerMustNotReferenceDbContextTransaction` — `NoDbContextTransactionInApplicationPredicate` design: check field types, constructor parameter types, and method call operands for `IDbContextTransaction` FullName substring; exemption: `SharedKernel.Persistence.*` namespace prefix passes unconditionally; failure message: offending type + declaration site | SharedKernel.ArchitectureTests | `●` |
| C-39 | Implement `NoSpecificationEvaluatorDowncastPredicate` in `Predicates/` — `ICustomRule` walking `TypeDefinition.Methods.Body.Instructions` for `castclass` opcodes; check `TypeReference.Name.StartsWith("SpecificationEvaluator")`; return false with offending type + method name on violation | SharedKernel.ArchitectureTests | `●` |
| C-40 | Implement `SingleConstructorPredicate` in `Predicates/` — `ICustomRule` scoped to types whose `TypeDefinition.Interfaces` contains an entry with `InterfaceType.Name == "IUnitOfWork"`; counts `TypeDefinition.Methods` where `IsConstructor && !IsStatic`; returns false with offending type name + actual count if count ≠ 1 | SharedKernel.ArchitectureTests | `●` |
| C-41 | Implement `NoDbContextTransactionInApplicationPredicate` in `Predicates/` — `ICustomRule`; namespace exemption (`SharedKernel.Persistence.*`) as first guard; checks field `FieldType.FullName`, method parameter `ParameterType.FullName`, and `Call`/`Callvirt` operand `DeclaringType.FullName` for `"IDbContextTransaction"` substring; returns false with offending type + site description on violation | SharedKernel.ArchitectureTests | `●` |
| C-42 | Implement `EfCorePackageHygieneRules` static class in `Rules/` — three factory methods: `NoSpecificationEvaluatorDowncastInEfCoreAssembly(Assembly)` → `ConditionList`, `IUnitOfWorkImplementorsMustHaveExactlyOneConstructor(Assembly)` → `ConditionList`, `ApplicationLayerMustNotReferenceDbContextTransaction(Assembly)` → `ConditionList` | SharedKernel.ArchitectureTests | `●` |
| T-67 | Architecture test Rule 1 (fire path): pass a contrived assembly containing a method that casts `ISpecificationEvaluator<T>` to `SpecificationEvaluator<T>` via `castclass`; assert `NoSpecificationEvaluatorDowncastInEfCoreAssembly` fails and failure message names the offending type and method | SharedKernel.ArchitectureTests | `●` |
| T-68 | Architecture test Rule 1 (pass path): pass an assembly where `ISpecificationEvaluator<T>` is always used via its interface methods; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-69 | Architecture test Rule 2 (fire path): pass an assembly containing a class implementing `IUnitOfWork` with two public constructors; assert `IUnitOfWorkImplementorsMustHaveExactlyOneConstructor` fails and failure message names the offending type and reports actual constructor count | SharedKernel.ArchitectureTests | `●` |
| T-70 | Architecture test Rule 2 (pass path): pass an assembly containing a class implementing `IUnitOfWork` with exactly one public constructor; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-71 | Architecture test Rule 3 (fire path): pass an application assembly containing a class with a constructor parameter typed `IDbContextTransaction`; assert `ApplicationLayerMustNotReferenceDbContextTransaction` fails and failure message names the offending type and constructor parameter | SharedKernel.ArchitectureTests | `●` |
| T-72 | Architecture test Rule 3 (pass path): pass an application assembly containing a class with a constructor parameter typed `ITransactionalUnitOfWork` (and no `IDbContextTransaction` references); assert rule passes | SharedKernel.ArchitectureTests | `●` |
| DO-15 | Document all three `EfCorePackageHygieneRules` predicates in `00.Governance/CLAUDE.md`: rationale tying each rule to WO-017 (P-097, P-098, P-099 decisions), offending-pattern example, compliant-pattern example, exemptions, failure message content | SharedKernel.ArchitectureTests | `●` |

---

## Phase: TenantedDbContext Tenant-Filter Guard <!-- phase-key: SK.00.TenantedDbContextGuard -->

> Prevent silent multi-tenancy misconfiguration in `TenantedDbContext` subclasses and arbitrary `IgnoreQueryFilters()` calls in non-designated code paths. Two Roslyn analyzers close the gap between the documentation-only rule in `06.Persistence/CLAUDE.md` and build-time enforcement.

### TenantedDbContextGuard — Goal

`TenantedDbContext.OnModelCreating` applies the tenant query filter via `base.OnModelCreating(modelBuilder)`. When a subclass overrides `OnModelCreating` and omits the `base` call (valid for performance or test-isolation reasons), `ApplyTenantFilters` is silently not applied — every query returns all tenants' data. This is a security incident, not a correctness issue. Similarly, `IgnoreQueryFilters()` called from arbitrary service repository subclasses bypasses tenant isolation outside the two designated cross-tenant access methods on `TenantedRepository<,>`. The `06.Persistence/CLAUDE.md` documents these rules but there is no enforcement mechanism. Two Roslyn analyzers — SK0201 and SK0202 — enforce both invariants at IDE and CI level, making the misconfiguration impossible to ship unnoticed.

### TenantedDbContextGuard — Scope

- Package(s) affected: `SharedKernel.Analyzers`
- New files:
  - `SharedKernel.Analyzers/Diagnostics/SK0201_TenantedDbContextOnModelCreatingAnalyzer.cs` — Roslyn analyzer; fires when a `TenantedDbContext` subclass overrides `OnModelCreating` without calling `base.OnModelCreating` or `ApplyTenantFilters`
  - `SharedKernel.Analyzers/Diagnostics/SK0202_IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer.cs` — Roslyn analyzer; fires when `IgnoreQueryFilters()` is called in any class outside `SharedKernel.Persistence.EfCore` namespace
- Modified files:
  - `00.Governance/CLAUDE.md` — add SK0201 and SK0202 to diagnostic registry; add `MultiTenancyGuardRules` section to architecture test contracts; document rationale, exemption list, and CI configuration guidance
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### TenantedDbContextGuard — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0201 | TenantedDbContextOnModelCreatingGuard | Design | Warning | A class that inherits from `TenantedDbContext` overrides `OnModelCreating` without calling either `base.OnModelCreating(modelBuilder)` or `this.ApplyTenantFilters(modelBuilder)` — tenant query filters will not be applied, exposing all tenants' data |
| SK0202 | IgnoreQueryFiltersOutsideTenantedRepository | Design | Warning | `IgnoreQueryFilters()` is called in a class that is not `TenantedRepository<,>` and is not in the `SharedKernel.Persistence.EfCore` namespace — direct use outside the designated cross-tenant access point may bypass tenant isolation |

### TenantedDbContextGuard — Implementation Rules

1. SK0201 `TenantedDbContextOnModelCreatingAnalyzer` operates on `MethodDeclarationSyntax` nodes. For each override of `OnModelCreating`, it walks the declaring class hierarchy (via `BaseList`) to determine whether any ancestor's simple name is `TenantedDbContext` (simple name check — unique within the SDK; no semantic model required for type name). If the class inherits `TenantedDbContext` (directly or indirectly, checked via ancestor `BaseList` names), the analyzer inspects the method body for: (a) any `InvocationExpressionSyntax` whose `MemberAccessExpression.Name.Identifier.Text` is `OnModelCreating` and the receiver expression is `base`, or (b) any `InvocationExpressionSyntax` whose name is `ApplyTenantFilters`. If neither is found, SK0201 is reported on the method identifier.
2. For SK0201 ancestry check: because `TenantedDbContext` subclasses may be several inheritance levels deep, a simple `BaseList` check on the immediately declaring class is insufficient. The analyzer must walk the declared base type chain using `BaseList.Types` on each parent `ClassDeclarationSyntax` in the syntax tree. If the syntax tree does not include the full chain (cross-file or cross-assembly), the analyzer applies a conservative rule: if ANY ancestor in the current file's syntax tree contains `TenantedDbContext` as a base name, the rule applies. Cross-assembly ancestry requires SemanticModel; for the initial implementation, use syntax-only check on the declaring class and its immediately visible base names — document this limitation in the implementation rules.
3. SK0201 severity is `Warning`. CI configuration guidance: services should add `<WarningsAsErrors>SK0201</WarningsAsErrors>` or the equivalent `dotnet_diagnostic.SK0201.severity = error` in their `.editorconfig` to enforce as an error.
4. SK0202 `IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer` operates on `InvocationExpressionSyntax` nodes. For each invocation whose simple method name is `IgnoreQueryFilters` (no arguments — the EF Core method takes no parameters), the analyzer checks whether the declaring class is exempt. Exemption: the containing class's namespace (determined by walking `SyntaxNode.Parent` to find `NamespaceDeclarationSyntax` or `FileScopedNamespaceDeclarationSyntax`) starts with `SharedKernel.Persistence.EfCore`. If the namespace is NOT exempt, SK0202 is reported on the `IgnoreQueryFilters` invocation expression.
5. SK0202 additional class-name exemption: if the immediate containing class name (simple name of `ClassDeclarationSyntax`) is `TenantedRepository` (exact match), SK0202 does not fire. This covers the case where `TenantedRepository` is defined outside the `SharedKernel.Persistence.EfCore` namespace in a test fixture or downstream service re-export. The class-name check is in addition to the namespace check — either exemption suppresses the diagnostic.
6. SK0202 severity is `Warning`. CI configuration guidance: services should add `<WarningsAsErrors>SK0202</WarningsAsErrors>` or `dotnet_diagnostic.SK0202.severity = error`. Test fixtures that set up explicit isolation (cross-tenant admin tests) may use `#pragma warning disable SK0202` with a code comment explaining the intentional bypass.
7. Both analyzers follow `netstandard2.0` constraint. Zero new NuGet dependencies beyond `Microsoft.CodeAnalysis.CSharp`.
8. SK0201 and SK0202 use no `SemanticModel` for their primary trigger checks — syntax-only is sufficient. SK0202 uses the established `SyntaxNode.Parent` namespace walk pattern (same as SK0001, SK0007). SK0201 uses a `BaseList` name scan for the `TenantedDbContext` ancestry check.
9. Exemption list (must be documented in `00.Governance/CLAUDE.md`):
   - SK0201: no exemptions — every `TenantedDbContext` subclass MUST call `base.OnModelCreating` or `ApplyTenantFilters`. Test subclasses are not exempt; test isolation must be explicit.
   - SK0202: `SharedKernel.Persistence.EfCore.*` namespace (covers `TenantedRepository<,>` and all internal types), class name `TenantedRepository` (exact match), and per-call-site `#pragma warning disable SK0202` with a documented comment.

### TenantedDbContextGuard — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Diagnostics/SK0201_TenantedDbContextOnModelCreatingAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0201 — `TenantedDbContext` subclass overrides `OnModelCreating` without tenant filter setup call |
| `Diagnostics/SK0202_IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0202 — `IgnoreQueryFilters()` called outside exempt namespace/class |

### TenantedDbContextGuard — Acceptance Criteria

- [ ] SK0201 emits a diagnostic when a `TenantedDbContext` subclass overrides `OnModelCreating` and calls neither `base.OnModelCreating(modelBuilder)` nor `ApplyTenantFilters(modelBuilder)`
- [ ] SK0201 does NOT emit when `base.OnModelCreating(modelBuilder)` is present
- [ ] SK0201 does NOT emit when `ApplyTenantFilters(modelBuilder)` is called explicitly (without calling `base`)
- [ ] SK0202 emits a diagnostic when `IgnoreQueryFilters()` is called in a class outside `SharedKernel.Persistence.EfCore` namespace and not named `TenantedRepository`
- [ ] SK0202 does NOT emit when called inside a class whose namespace starts with `SharedKernel.Persistence.EfCore`
- [ ] SK0202 does NOT emit when called inside a class named `TenantedRepository` (exact match)
- [ ] Both rules documented in `00.Governance/CLAUDE.md` with rationale, exemption list, and CI configuration guidance (`<WarningsAsErrors>` and `.editorconfig` patterns)
- [ ] All existing governance tests pass — no regressions

### TenantedDbContextGuard — Dependencies

- Requires P-108 (`TenantedDbContext` with `OnModelCreating`/`ApplyTenantFilters` defined in `SharedKernel.Persistence.EfCore`): yes — SK0201 checks for these method names; without them defined the analyzer would be registering rules against non-existent patterns; the type names are the trigger anchor
- Requires C-01 (`AnalyzerBase` abstract class from SK.00.Core): yes — both analyzers extend `AnalyzerBase` for `CreateDescriptor` and `HelpLinkUri`
- Unblocks: CI enforcement of multi-tenancy misconfiguration detection across all downstream services

### TenantedDbContextGuard — Tooling Version Notes

- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — SK0201 and SK0202 follow same constraint as SK0001–SK0011)
- Target framework: `netstandard2.0` (Analyzers only — no ArchitectureTests work in this phase)

### TenantedDbContextGuard — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-36 | Define SK0201 `TenantedDbContextOnModelCreatingGuard` — trigger: `OnModelCreating` override in a class whose `BaseList` includes `TenantedDbContext` (simple name); body must contain `base.OnModelCreating(...)` or `ApplyTenantFilters(...)` invocation; neither present fires SK0201; document ancestry-check limitation (syntax-only, single file) | SharedKernel.Analyzers | `●` |
| D-37 | Define SK0202 `IgnoreQueryFiltersOutsideTenantedRepository` — trigger: `IgnoreQueryFilters()` invocation (zero arguments) anywhere outside `SharedKernel.Persistence.EfCore*` namespace and outside class named `TenantedRepository`; namespace walk via `SyntaxNode.Parent`; document exemption list and `#pragma` suppression guidance | SharedKernel.Analyzers | `●` |
| C-43 | Implement SK0201 `TenantedDbContextOnModelCreatingAnalyzer` — `MethodDeclarationSyntax` walker; filter to `OnModelCreating` overrides; check containing class `BaseList` for `TenantedDbContext` simple name; inspect body for `base.OnModelCreating` or `ApplyTenantFilters` invocations; report SK0201 on method identifier if neither found | SharedKernel.Analyzers | `●` |
| C-44 | Implement SK0202 `IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer` — `InvocationExpressionSyntax` walker; filter to `IgnoreQueryFilters` simple name with zero arguments; namespace walk via parent `NamespaceDeclarationSyntax`/`FileScopedNamespaceDeclarationSyntax`; class-name check for `TenantedRepository`; report SK0202 if neither exemption applies | SharedKernel.Analyzers | `●` |
| T-73 | Analyzer test SK0201 (fire path): `TenantedDbContext` subclass overrides `OnModelCreating` with a body containing only `base.OnModelCreating` call removed and entity configuration — no filter call present; verify SK0201 fires on the method identifier | SharedKernel.Analyzers.Tests | `●` |
| T-74 | Analyzer test SK0201 (pass path — base call): `TenantedDbContext` subclass overrides `OnModelCreating` and calls `base.OnModelCreating(modelBuilder)` — verify no SK0201 diagnostic | SharedKernel.Analyzers.Tests | `●` |
| T-75 | Analyzer test SK0201 (pass path — explicit ApplyTenantFilters): `TenantedDbContext` subclass overrides `OnModelCreating`, calls `this.ApplyTenantFilters(modelBuilder)` without `base.OnModelCreating` — verify no SK0201 diagnostic | SharedKernel.Analyzers.Tests | `●` |
| T-76 | Analyzer test SK0202 (fire path): `IgnoreQueryFilters()` called in a class within `Application.Repositories` namespace (not `SharedKernel.Persistence.EfCore`) — verify SK0202 fires on the invocation | SharedKernel.Analyzers.Tests | `●` |
| T-77 | Analyzer test SK0202 (pass path — namespace exempt): `IgnoreQueryFilters()` called inside a class whose namespace is `SharedKernel.Persistence.EfCore.MultiTenancy` — verify no SK0202 diagnostic | SharedKernel.Analyzers.Tests | `●` |
| T-78 | Analyzer test SK0202 (pass path — class name exempt): `IgnoreQueryFilters()` called inside a class named `TenantedRepository` in a non-exempt namespace — verify no SK0202 diagnostic | SharedKernel.Analyzers.Tests | `●` |
| DO-16 | Document SK0201 and SK0202 in `00.Governance/CLAUDE.md` and `00.Governance/README.md`: rationale (silent data leak risk), exemption list, CI configuration guidance (`<WarningsAsErrors>SK0201;SK0202</WarningsAsErrors>` and `dotnet_diagnostic.SK02xx.severity = error`), suppression instructions for intentional cross-tenant test fixtures | SharedKernel.Analyzers | `●` |
| D-38 | Add SK0201 and SK0202 to the `00.Governance/CLAUDE.md` diagnostic registry with full descriptor blocks (category, severity, trigger, fix, suppress, note) | SharedKernel.Analyzers | `●` |

---

## Phase: Governance: Architecture Rules for DB Encryption Pattern Correctness <!-- phase-key: SK.00.EncryptionPatternGuard -->

> Four architecture enforcement rules that prevent the most likely misuse patterns of the WO-019 AES-256-GCM encryption subsystem: wrong-layer cryptographic cipher usage, encryption attributes on domain entities, `IEncryptionRotationJob` injection in domain/application types, and direct `EncryptedValueConverter<T>` instantiation bypassing the `EncryptionModelConvention` auto-wire.

### EncryptionPatternGuard — Goal

With hundreds of services consuming the SharedKernel encryption subsystem, four specific misuse patterns will recur. Developers familiar with attribute-based encryption frameworks (e.g., NHibernate, Hibernate) will try to add `[Encrypted]` or `[EncryptedColumn]` attributes directly to domain entity classes. Developers who see `EncryptedValueConverter<T>` surfaced by IntelliSense will instantiate it directly and pass it to `.HasConversion()`, bypassing the model convention that wires it automatically and causing duplicate or inconsistent converter registration. Contributors will try to trigger key rotation from a MediatR handler (correct layer is a hosted service or management endpoint). Others will call `AesGcm`, `Aes`, or `SymmetricAlgorithm` directly in domain or application code rather than routing through the persistence-layer converter. All four patterns are either security violations (wrong-layer crypto) or correctness violations (duplicate converter registration causing double-encryption of stored data). Rule-as-code at the governance layer makes all four impossible to ship undetected — they produce IDE diagnostics and CI failures.

### EncryptionPatternGuard — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/EncryptionPatternGuardRules.cs` — static class housing all four predicate factory methods
  - `SharedKernel.ArchitectureTests/Predicates/NoAesCipherInDomainOrApplicationPredicate.cs` — `ICustomRule`: IL inspection for references to `AesGcm`, `Aes`, or `SymmetricAlgorithm` types from `System.Security.Cryptography` in domain or application layer types
  - `SharedKernel.ArchitectureTests/Predicates/NoEncryptionAttributeOnDomainEntityPredicate.cs` — `ICustomRule`: type attribute scan for any attribute whose name contains `"Encrypt"` as a substring on types in `03.Domain` assemblies
  - `SharedKernel.ArchitectureTests/Predicates/NoEncryptionRotationJobInjectionPredicate.cs` — `ICustomRule`: constructor parameter type scan for `IEncryptionRotationJob` in types whose namespace starts with domain or application namespace patterns
  - `SharedKernel.ArchitectureTests/Predicates/NoDirectEncryptedValueConverterInstantiationPredicate.cs` — `ICustomRule`: IL inspection for `newobj` opcodes whose operand type name contains `"EncryptedValueConverter"` inside types implementing `IEntityTypeConfiguration<>`; exempts `EncryptionModelConvention` by full type name
- Modified files:
  - `00.Governance/CLAUDE.md` — add `EncryptionPatternGuardRules` to architecture test contracts section; add SK0301–SK0304 to diagnostic registry; document all four rules with rationale, offending pattern, compliant pattern, exemption list
  - `00.Governance/state-map.md` — this update
- Deleted files: none
- Note: SK0301–SK0304 introduce a new 03xx ID block dedicated to encryption-domain governance rules. SK0001–SK0011 cover general SharedKernel patterns; SK0201–SK0202 cover EF Core multi-tenancy; the 03xx block covers the encryption subsystem. No new Roslyn analyzer `.cs` files — all four rules are pure NetArchTest architecture predicates backed by `ICustomRule` IL/attribute-inspection predicates. This keeps the enforcement at the assembly level (post-compile) rather than per-call-site, which is correct for architectural boundary rules.

### EncryptionPatternGuard — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0301 | DirectCryptoInDomainOrApplication | Security | Warning | `AesGcm`, `Aes`, or `SymmetricAlgorithm` referenced in a type whose namespace starts with a `03.Domain` or `05.Application` pattern — crypto belongs exclusively in `06.Persistence` (converter) and `12.Security` (JWT signing) |
| SK0302 | EncryptionAttributeOnDomainEntity | Design | Warning | A class in a `03.Domain` assembly carries a custom attribute whose name contains `"Encrypt"` as a substring — use `PropertyBuilder<T>.Encrypt()` in `IEntityTypeConfiguration<T>` instead |
| SK0303 | EncryptionRotationJobInDomainOrApplication | Design | Warning | `IEncryptionRotationJob` appears as a constructor parameter type in a type whose namespace starts with a `03.Domain` or `05.Application` pattern — rotation is an infrastructure operation; register it in a hosted service, Hangfire job, or management endpoint |
| SK0304 | DirectEncryptedValueConverterInstantiation | Design | Warning | `new EncryptedValueConverter<T>(...)` called directly inside an `IEntityTypeConfiguration<T>` implementation — use the `.Encrypt()` `PropertyBuilder` extension and let `EncryptionModelConvention` apply the converter automatically; direct instantiation causes duplicate or inconsistent converter registration |

### EncryptionPatternGuard — Implementation Rules

1. `EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication(Assembly domainAssembly)` and `NoCryptoCipherInDomainOrApplication(Assembly applicationAssembly)` may be overloaded or the method may accept a `params Assembly[]`. The implementation uses `NoAesCipherInDomainOrApplicationPredicate` — an `ICustomRule` that walks `TypeDefinition.Methods.Body.Instructions` for `Call`, `Callvirt`, and `Newobj` opcodes whose operand `TypeReference.Namespace` equals `"System.Security.Cryptography"` and `TypeReference.Name` is one of `"AesGcm"`, `"Aes"`, `"SymmetricAlgorithm"`. Also checks `TypeDefinition.Fields` for field types in the same namespace/name set. Passes for types whose `TypeDefinition.Namespace` starts with `"SharedKernel.Persistence"` or `"SharedKernel.Security"` — these are the legitimate users. Returns `ConditionList`.

2. The `NoAesCipherInDomainOrApplicationPredicate` exemption is implemented as a first-check guard: types in `SharedKernel.Persistence.*` or `SharedKernel.Security.*` namespaces return true (pass) unconditionally. The predicate must be called against domain/application assemblies only; the consuming test must supply the correct assemblies. The exemption guard is a safety net for any accidentally passed assembly.

3. `EncryptionPatternGuardRules.NoEncryptionAttributeOnDomainEntities(Assembly domainAssembly)` uses `NoEncryptionAttributeOnDomainEntityPredicate` — an `ICustomRule` that inspects `TypeDefinition.CustomAttributes` for each type in the domain assembly. For each `CustomAttribute`, checks whether `AttributeType.Name` contains `"Encrypt"` (case-insensitive substring). If any such attribute is found, returns false (rule violated) with failure message naming the offending type and the attribute type name. This is attribute-reflection based — no IL instruction walk required, only `TypeDefinition.CustomAttributes` enumeration via Mono.Cecil. Returns `ConditionList`.

4. `EncryptionPatternGuardRules.NoEncryptionRotationJobInjectionInDomainOrApplication(Assembly assembly)` uses `NoEncryptionRotationJobInjectionPredicate` — an `ICustomRule` that scopes to all types in the assembly (not interface-scoped). For each type, iterates `TypeDefinition.Methods` where `IsConstructor` is true. For each constructor, inspects `MethodDefinition.Parameters` for any `ParameterDefinition.ParameterType.Name` equal to `"IEncryptionRotationJob"` (exact name match; no namespace resolution needed — the simple name is unique within the SDK). If found, returns false with failure message naming the offending type and the constructor where the injection occurs. Returns `ConditionList`.
   - Exemption: types whose `TypeDefinition.Namespace` starts with `"SharedKernel.Persistence"` return true unconditionally — the interface's own package may reference it. Classes named `*RotationJob*`, `*HostedService*`, `*Controller*`, or `*Activity*` (suffix/prefix match on the simple type name) are also exempt — these are the legitimate consumers.

5. `EncryptionPatternGuardRules.NoDirectEncryptedValueConverterInstantiation(Assembly assembly)` uses `NoDirectEncryptedValueConverterInstantiationPredicate` — an `ICustomRule` that:
   a. Scopes to types whose `TypeDefinition.Interfaces` contains an entry whose `InterfaceType.Name` starts with `"IEntityTypeConfiguration"` (the EF Core configuration interface prefix).
   b. For each such type, walks `TypeDefinition.Methods.Body.Instructions` for `Newobj` opcodes whose operand `MethodReference.DeclaringType.Name` contains `"EncryptedValueConverter"` (substring match on the type being instantiated).
   c. Exemption: types whose `TypeDefinition.Name` is `"EncryptionModelConvention"` (exact match) return true unconditionally — the convention itself instantiates `EncryptedValueConverter<T>` legitimately as the auto-wire mechanism.
   d. Returns false with failure message naming the offending `IEntityTypeConfiguration<T>` implementor and the method containing the direct instantiation. Returns `ConditionList`.

6. All four factory methods in `EncryptionPatternGuardRules` accept `Assembly` (or `params Assembly[]` where noted) and return `ConditionList`. Method signatures:
   - `EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication(params Assembly[])` → `ConditionList`
   - `EncryptionPatternGuardRules.NoEncryptionAttributeOnDomainEntities(Assembly)` → `ConditionList`
   - `EncryptionPatternGuardRules.NoEncryptionRotationJobInjectionInDomainOrApplication(params Assembly[])` → `ConditionList`
   - `EncryptionPatternGuardRules.NoDirectEncryptedValueConverterInstantiation(Assembly)` → `ConditionList`

7. All four predicates reuse the established Mono.Cecil `TypeDefinition` access pattern from `DoesNotContainThrowIlPredicate`. No new NuGet dependencies — the existing `Mono.Cecil >= 0.11.5` explicit reference in `SharedKernel.ArchitectureTests` covers all four predicates.

8. `EncryptionPatternGuardRules` lives in `SharedKernel.ArchitectureTests/Rules/`. It must not reference `SharedKernel.Persistence.EfCore` or any runtime production package directly. The consuming test project supplies assemblies under test via `typeof(SomeProductionType).Assembly`.

9. SK0303 exemption class-name patterns (`*RotationJob*`, `*HostedService*`, `*Controller*`, `*Activity*`) are applied by substring check on `TypeDefinition.Name`. This is a broad-but-safe exemption: these class-name patterns identify the legitimate infrastructure consumers of the rotation job. No additional namespace exemption is needed for these — the class name check is the primary discriminator.

10. SK0304 `"IEntityTypeConfiguration"` interface prefix scope ensures the rule only fires inside EF Core entity configuration classes — not in general application code that might create a converter for other purposes. `EncryptionModelConvention` is the sole type exempt by name; any other type (including test fixtures that mock the convention) is subject to the rule. Test fixtures that need to test converter wiring should use `.Encrypt()` in test entity configurations, not `new EncryptedValueConverter<T>()`.

11. Failure messages for all four predicates must be actionable:
    - SK0301 → `"Direct use of {cipherTypeName} (System.Security.Cryptography) detected in {offendingType}. Use the persistence-layer EncryptedValueConverter via the .Encrypt() configuration extension instead."`
    - SK0302 → `"Encryption attribute [{attributeName}] found on domain entity {offendingType}. Place encryption configuration in IEntityTypeConfiguration<T> using PropertyBuilder<T>.Encrypt() instead."`
    - SK0303 → `"IEncryptionRotationJob is injected in {offendingType} constructor. Move key rotation to a hosted service, Hangfire job, or management endpoint — not domain or application handlers."`
    - SK0304 → `"Direct instantiation of EncryptedValueConverter<T> detected in {offendingType}.{offendingMethod}. Use the .Encrypt() PropertyBuilder extension — EncryptionModelConvention applies the converter automatically."`

### EncryptionPatternGuard — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/EncryptionPatternGuardRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: four predicate factory methods returning ConditionList |
| `Predicates/NoAesCipherInDomainOrApplicationPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: IL walk for AesGcm/Aes/SymmetricAlgorithm references; exempts Persistence and Security namespaces |
| `Predicates/NoEncryptionAttributeOnDomainEntityPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: TypeDefinition.CustomAttributes scan for "Encrypt"-containing attribute names on domain types |
| `Predicates/NoEncryptionRotationJobInjectionPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: constructor parameter type scan for IEncryptionRotationJob; exempts Persistence namespace and designated class-name patterns |
| `Predicates/NoDirectEncryptedValueConverterInstantiationPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: Newobj IL opcode scan for EncryptedValueConverter<T> inside IEntityTypeConfiguration<T> implementors; exempts EncryptionModelConvention by name |

### EncryptionPatternGuard — Acceptance Criteria

- [ ] SK0301 fires when `AesGcm`, `Aes`, or `SymmetricAlgorithm` is referenced in `03.Domain` or `05.Application` namespace types
- [ ] SK0301 does not fire for types in `SharedKernel.Persistence.*` or `SharedKernel.Security.*` namespaces
- [ ] SK0302 fires when a class in a `03.Domain` assembly carries an attribute whose name contains `"Encrypt"` as a substring
- [ ] SK0302 does not fire for non-domain types (types whose namespace does not map to a domain assembly)
- [ ] SK0303 fires when `IEncryptionRotationJob` is injected (constructor parameter) in a type in `03.Domain` or `05.Application`
- [ ] SK0303 does not fire for types named `*RotationJob*`, `*HostedService*`, `*Controller*`, or `*Activity*`; does not fire for `SharedKernel.Persistence.*` namespaced types
- [ ] SK0304 fires when `EncryptedValueConverter<T>` is directly instantiated (`newobj`) in an `IEntityTypeConfiguration<T>` implementation
- [ ] SK0304 does not fire for `EncryptionModelConvention` (the legitimate internal instantiation)
- [ ] All four rules have compliant-pass and violation-fire test fixtures (fire path and pass path per rule = 8 minimum test tasks)
- [ ] `00.Governance/CLAUDE.md` updated with all four new rules, rationale, exemption lists, and failure messages
- [ ] All existing governance tests pass — no regressions

### EncryptionPatternGuard — Dependencies

- Requires P-112 (`EncryptedValueConverter<T>`, `IEncryptionRotationJob`, `EncryptionModelConvention`, `IEntityTypeConfiguration<T>` extension `.Encrypt()` defined in `SharedKernel.Persistence.EfCore`): yes — `NoDirectEncryptedValueConverterInstantiationPredicate` uses these type names as anchors; `NoEncryptionRotationJobInjectionPredicate` uses `IEncryptionRotationJob` simple name; without these types defined the predicates are checking for names that don't exist yet (rules will trivially pass, which is safe but vacuous)
- Requires `DoesNotContainThrowIlPredicate` (Mono.Cecil pattern established in SK.00.GuardPurity): yes (already complete — Mono.Cecil access pattern and `>= 0.11.5` NuGet reference are established)
- Unblocks: CI encryption-pattern gate across all downstream services consuming the WO-019 encryption subsystem

### EncryptionPatternGuard — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref in `SharedKernel.ArchitectureTests` — no change; covers all four new predicates)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new Roslyn analyzer files in this phase; no change)
- Target framework: `net10.0` (ArchitectureTests only)

### EncryptionPatternGuard — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-39 | Define `EncryptionPatternGuardRules` static class shape: four predicate factory methods; SK0301 — `NoAesCipherInDomainOrApplicationPredicate` IL walk design (AesGcm/Aes/SymmetricAlgorithm, Persistence+Security exemptions); SK0302 — `NoEncryptionAttributeOnDomainEntityPredicate` attribute scan design ("Encrypt" substring); SK0303 — `NoEncryptionRotationJobInjectionPredicate` constructor parameter scan design (IEncryptionRotationJob simple name, class-name exemption patterns); SK0304 — `NoDirectEncryptedValueConverterInstantiationPredicate` Newobj IL scan design (EncryptedValueConverter name in IEntityTypeConfiguration scope, EncryptionModelConvention exemption); failure message contract for each rule | SharedKernel.ArchitectureTests | `●` |
| C-45 | Implement `NoAesCipherInDomainOrApplicationPredicate` in `Predicates/` — `ICustomRule`; namespace exemption (`SharedKernel.Persistence.*`, `SharedKernel.Security.*`) as first guard; walk `TypeDefinition.Methods.Body.Instructions` for Call/Callvirt/Newobj opcodes and `TypeDefinition.Fields` for field types whose `Namespace == "System.Security.Cryptography"` and `Name` in `{"AesGcm","Aes","SymmetricAlgorithm"}`; return false with SK0301 failure message on first match | SharedKernel.ArchitectureTests | `●` |
| C-46 | Implement `NoEncryptionAttributeOnDomainEntityPredicate` in `Predicates/` — `ICustomRule`; iterate `TypeDefinition.CustomAttributes`; for each, check `AttributeType.Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase)`; return false with SK0302 failure message naming offending type and attribute on first match | SharedKernel.ArchitectureTests | `●` |
| C-47 | Implement `NoEncryptionRotationJobInjectionPredicate` in `Predicates/` — `ICustomRule`; namespace exemption (`SharedKernel.Persistence.*`) and class-name exemptions (`*RotationJob*`, `*HostedService*`, `*Controller*`, `*Activity*` substring checks on `TypeDefinition.Name`) as first guard; iterate constructors in `TypeDefinition.Methods` where `IsConstructor`; check `ParameterDefinition.ParameterType.Name == "IEncryptionRotationJob"`; return false with SK0303 failure message on match | SharedKernel.ArchitectureTests | `●` |
| C-48 | Implement `NoDirectEncryptedValueConverterInstantiationPredicate` in `Predicates/` — `ICustomRule`; scope to types whose `TypeDefinition.Interfaces` contains entry with `InterfaceType.Name.StartsWith("IEntityTypeConfiguration")`; exempt types whose `TypeDefinition.Name == "EncryptionModelConvention"`; walk `TypeDefinition.Methods.Body.Instructions` for Newobj opcodes where `MethodReference.DeclaringType.Name.Contains("EncryptedValueConverter")`; return false with SK0304 failure message naming offending type and method | SharedKernel.ArchitectureTests | `●` |
| C-49 | Implement `EncryptionPatternGuardRules` static class in `Rules/` — four factory methods: `NoCryptoCipherInDomainOrApplication(params Assembly[])` → `ConditionList`, `NoEncryptionAttributeOnDomainEntities(Assembly)` → `ConditionList`, `NoEncryptionRotationJobInjectionInDomainOrApplication(params Assembly[])` → `ConditionList`, `NoDirectEncryptedValueConverterInstantiation(Assembly)` → `ConditionList` | SharedKernel.ArchitectureTests | `●` |
| T-79 | Architecture test SK0301 (fire path): pass a domain assembly containing a type that references `System.Security.Cryptography.AesGcm` directly; assert `NoCryptoCipherInDomainOrApplication` fails and failure message names the offending type | SharedKernel.ArchitectureTests | `●` |
| T-80 | Architecture test SK0301 (pass path): pass a domain assembly with no direct crypto cipher references; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-81 | Architecture test SK0302 (fire path): pass a domain assembly containing a class decorated with an `[EncryptedColumn]` attribute; assert `NoEncryptionAttributeOnDomainEntities` fails and failure message names the offending type and attribute | SharedKernel.ArchitectureTests | `●` |
| T-82 | Architecture test SK0302 (pass path): pass a domain assembly where no class carries an "Encrypt*" attribute; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-83 | Architecture test SK0303 (fire path): pass an application assembly containing a MediatR handler constructor that injects `IEncryptionRotationJob`; assert `NoEncryptionRotationJobInjectionInDomainOrApplication` fails and failure message names the offending type | SharedKernel.ArchitectureTests | `●` |
| T-84 | Architecture test SK0303 (pass path): pass an application assembly containing a hosted service class named `EncryptionKeyRotationHostedService` that injects `IEncryptionRotationJob`; assert rule passes (exempt by class-name pattern `*HostedService*`) | SharedKernel.ArchitectureTests | `●` |
| T-85 | Architecture test SK0304 (fire path): pass a persistence assembly containing an `IEntityTypeConfiguration<Order>` implementation that calls `new EncryptedValueConverter<string>(...)` directly; assert `NoDirectEncryptedValueConverterInstantiation` fails and failure message names the offending type and method | SharedKernel.ArchitectureTests | `●` |
| T-86 | Architecture test SK0304 (pass path): pass an assembly containing `EncryptionModelConvention` (which legitimately instantiates `EncryptedValueConverter<T>`) and an `IEntityTypeConfiguration<Order>` implementation that uses `.Encrypt()` only; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| DO-17 | Document all four `EncryptionPatternGuardRules` predicates in `00.Governance/CLAUDE.md`: rationale tying each rule to WO-019 encryption subsystem correctness, exemption lists with rationale, failure message content, compliant vs. offending pattern for each; add SK0301–SK0304 to the CLAUDE.md diagnostic registry | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Governance: Messaging Architecture Rules — No Raw IBus Injection, No IMessageBus Singleton, No Domain Messaging, No Hardcoded Queue URIs <!-- phase-key: SK.00.MessagingArchRules -->

> Enforce four critical messaging misuse patterns that the compiler cannot catch: MassTransit transport types injected outside `07.Messaging`, `IEventPublisher` used in the domain layer, `IMessageBus`/`IEventPublisher` registered as singletons, and hardcoded queue/exchange URI strings passed to `GetSendEndpoint`.

### MessagingArchRules — Goal

With hundreds of microservices consuming `SharedKernel.Messaging`, developers under deadline pressure reach for the most IntelliSense-visible type — often `IBus`, `IPublishEndpoint`, or `ISendEndpointProvider` from MassTransit directly, bypassing the abstraction entirely. Without rule-as-code enforcement, transport-specific types leak into application handlers, making transport swaps impossible and breaking the abstraction boundary. Domain types injecting `IEventPublisher` collapse the DDD event propagation model (domain events must remain internal to the domain; integration events flow outward through the application layer via `IDomainEventDispatcher`). Singleton registration of `IMessageBus`/`IEventPublisher` compiles and runs in development but causes scope pollution, race conditions, and incorrect MassTransit consume-scope lifecycle under concurrent production load. Hardcoded `new Uri("queue:...")` strings passed to `GetSendEndpoint` produce environment-specific addresses that break across dev/staging/prod with different broker configurations.

Two rules (SK0701, SK0702) are NetArchTest assembly-level predicates. Two rules (SK0703, SK0704) are Roslyn syntax analyzers for per-call-site enforcement.

### MessagingArchRules — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`, `SharedKernel.Analyzers`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/MessagingArchitectureRules.cs` — static class housing SK0701 and SK0702 predicate factory methods
  - `SharedKernel.ArchitectureTests/Predicates/NoDirectBusInjectionOutsideMessagingPredicate.cs` — `ICustomRule`: constructor parameter scan for `IBus`, `IPublishEndpoint`, `ISendEndpointProvider` in types whose namespace does not start with `SharedKernel.Messaging`
  - `SharedKernel.ArchitectureTests/Predicates/NoEventPublisherInDomainLayerPredicate.cs` — `ICustomRule`: constructor parameter scan for `IEventPublisher` in types implementing `Entity`, `AggregateRoot`, `ValueObject`, `DomainService`, or residing in a `*.Domain.*` namespace
  - `SharedKernel.Analyzers/Diagnostics/SK0703_MessageBusSingletonRegistrationAnalyzer.cs` — Roslyn analyzer: fires on `AddSingleton<IMessageBus,...>()` or `AddSingleton<IEventPublisher,...>()` call expressions
  - `SharedKernel.Analyzers/Diagnostics/SK0704_HardcodedQueueUriAnalyzer.cs` — Roslyn analyzer: fires on `new Uri("queue:...")` or `new Uri("exchange:...")` literal argument passed to `GetSendEndpoint`
- Modified files:
  - `00.Governance/CLAUDE.md` — add SK0701–SK0704 to diagnostic registry; add `MessagingArchitectureRules` to architecture test contracts section; document all four rules with rationale, exemption lists, and offending/compliant patterns
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### MessagingArchRules — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0701 | NoDirectBusInjectionOutsideMessaging | Design | Warning | `MassTransit.IBus`, `MassTransit.IPublishEndpoint`, or `MassTransit.ISendEndpointProvider` appears as a constructor parameter in a type whose namespace does not start with `SharedKernel.Messaging` — use `IMessageBus` or `IEventPublisher` from SharedKernel.Messaging.Abstractions instead |
| SK0702 | NoEventPublisherInDomainLayer | Design | Warning | `IEventPublisher` appears as a constructor parameter in a type that implements `Entity`, `AggregateRoot`, `ValueObject`, or `DomainService`, or resides in a `*.Domain.*` namespace — domain events are dispatched internally by `IDomainEventDispatcher`; integration events flow through the application layer only |
| SK0703 | MessageBusSingletonRegistration | Usage | Warning | `services.AddSingleton<IMessageBus,...>()` or `services.AddSingleton<IEventPublisher,...>()` detected — both interfaces must be registered as scoped to match MassTransit's per-consume-scope lifetime model; singleton registration causes scope pollution and race conditions under concurrent load |
| SK0704 | HardcodedQueueUriInGetSendEndpoint | Usage | Warning | A `new Uri(string)` expression with a literal value starting with `"queue:"` or `"exchange:"` is passed as an argument to a `GetSendEndpoint` invocation — endpoint addresses must be resolved via `IEndpointNameFormatter` convention; hardcoded URIs break across environments |

### MessagingArchRules — Implementation Rules

1. `MessagingArchitectureRules.NoDirectBusInjectionOutsideMessaging(params Assembly[] assemblies)` uses `NoDirectBusInjectionOutsideMessagingPredicate` — an `ICustomRule` that iterates `TypeDefinition.Methods` where `IsConstructor` is true. For each constructor, inspects each `ParameterDefinition.ParameterType.Name` for exact name match in `{"IBus", "IPublishEndpoint", "ISendEndpointProvider"}`. Exemption guard (first check): types whose `TypeDefinition.Namespace` starts with `"SharedKernel.Messaging"` return true (pass) unconditionally — `07.Messaging` package types may reference MassTransit transport interfaces freely. Returns false (rule violated) with failure message naming the offending type, constructor parameter name, and the recommended alternative (`IMessageBus` or `IEventPublisher`). Returns `ConditionList`.

2. Namespace exemption for SK0701 covers both `SharedKernel.Messaging.Abstractions` and `SharedKernel.Messaging.MassTransit` (and all sub-namespaces) via the `StartsWith("SharedKernel.Messaging")` prefix check. No additional exemptions. Any team requesting an exemption must document it in this file before applying a suppression.

3. `MessagingArchitectureRules.NoEventPublisherInDomainLayer(Assembly domainAssembly)` uses `NoEventPublisherInDomainLayerPredicate` — an `ICustomRule` that inspects constructor parameters for the simple name `"IEventPublisher"` (exact match). Scope: the predicate evaluates all types in the supplied assembly. Two scope signals are evaluated in sequence: (a) the type's `TypeDefinition.Namespace` contains `".Domain."` as a substring (namespace-based signal), OR (b) the type implements an interface whose `InterfaceType.Name` is in `{"IEntity", "IAggregateRoot", "IValueObject", "IDomainService"}` (interface-based signal). If either signal is true AND a constructor parameter type name is `"IEventPublisher"`, the predicate returns false with a failure message naming the offending type, the offending constructor parameter, and the correct flow (`IDomainEventDispatcher` → application handler → `IEventPublisher`). Returns `ConditionList`.
   - Note: the namespace signal `.Domain.` (with dots) is intentionally narrow — it avoids matching `IDomainEventHandler` or similar types in application namespaces that contain "Domain" as a word. The interface-based signal is the more reliable discriminator for SDK types.

4. `NoDirectBusInjectionOutsideMessagingPredicate` and `NoEventPublisherInDomainLayerPredicate` both reuse the established Mono.Cecil `TypeDefinition.Methods` constructor-parameter inspection pattern from `NoInfrastructureConstructorParametersPredicate` (established in DomainLayerPurity phase). No new NuGet dependency — `Mono.Cecil >= 0.11.5` is already referenced.

5. SK0703 `MessageBusSingletonRegistrationAnalyzer` operates on `InvocationExpressionSyntax` nodes. The trigger: a method name of `AddSingleton` (simple name, case-sensitive exact match on `MemberAccessExpressionSyntax.Name.Identifier.Text` or `IdentifierNameSyntax.Identifier.Text`) whose type arguments include a type name matching `"IMessageBus"` or `"IEventPublisher"` (simple name, case-sensitive). The check proceeds as follows:
   a. For each `InvocationExpressionSyntax`, check whether the method name is `"AddSingleton"`.
   b. If yes, inspect the type arguments (`GenericNameSyntax.TypeArgumentList.Arguments`) for any `TypeSyntax` whose string representation starts with `"IMessageBus"` or `"IEventPublisher"` (simple name prefix check, covers both the unbound form `AddSingleton<IMessageBus>()` and the bound form `AddSingleton<IMessageBus, MassTransitMessageBus>()`).
   c. If found, report SK0703 on the invocation expression.
   No semantic model required — the simple names `IMessageBus` and `IEventPublisher` are unique within the SDK. No suppression namespace — SK0703 fires globally; singleton registration of these interfaces is never correct. Suppress per-call-site via `#pragma warning disable SK0703` (use only if the DI container semantics are provably equivalent — document the reason).

6. SK0704 `HardcodedQueueUriAnalyzer` operates on `InvocationExpressionSyntax` nodes. The trigger: an invocation whose method name is `"GetSendEndpoint"` (simple name check, case-sensitive) has at least one argument that is an `ObjectCreationExpressionSyntax` (or `ImplicitObjectCreationExpressionSyntax`) of type `Uri` (simple name `"Uri"`) whose first argument is a `LiteralExpressionSyntax` of kind `StringLiteralExpression` whose value starts with `"queue:"` or `"exchange:"` (case-insensitive). If all conditions hold, SK0704 is reported on the `Uri` creation expression. No semantic model required — the method name `GetSendEndpoint` and type name `Uri` are checked syntactically; the queue/exchange scheme prefixes are the discriminating signal. No suppression namespace — suppress per-call-site via `#pragma warning disable SK0704` only when an environment-invariant queue address is required (e.g., a fixed dead-letter queue URI in a test fixture); document the rationale.

7. Both SK0703 and SK0704 follow the `netstandard2.0` constraint. Zero new NuGet dependencies beyond `Microsoft.CodeAnalysis.CSharp`. Both use syntax-only analysis; neither requires `SemanticModel`.

8. `MessagingArchitectureRules` lives in `SharedKernel.ArchitectureTests/Rules/`. It must not reference `SharedKernel.Messaging.MassTransit` or any MassTransit package directly. Type names (`IBus`, `IPublishEndpoint`, `ISendEndpointProvider`, `IEventPublisher`) are matched by simple name only — the predicates never hard-code a namespace-qualified name for MassTransit types. This keeps the predicate stable even if MassTransit renames a namespace.

9. `MessagingArchitectureRules` factory method signatures:
   - `MessagingArchitectureRules.NoDirectBusInjectionOutsideMessaging(params Assembly[])` → `ConditionList`
   - `MessagingArchitectureRules.NoEventPublisherInDomainLayer(Assembly)` → `ConditionList`

10. Failure messages must be actionable:
    - SK0701 → `"{offendingType} injects MassTransit transport type '{parameterTypeName}' directly. Use IMessageBus (for commands/queries) or IEventPublisher (for events) from SharedKernel.Messaging.Abstractions instead."`
    - SK0702 → `"{offendingType} injects IEventPublisher in the domain layer. Domain events are dispatched internally by IDomainEventDispatcher. Correct flow: domain event → IDomainEventDispatcher → application handler → IEventPublisher."`
    - SK0703 → `"IMessageBus and IEventPublisher must be registered as Scoped, not Singleton. Singleton registration breaks MassTransit's per-consume-scope lifetime and causes race conditions under concurrent load. Use AddScoped<{typeName}, ...>() instead."`
    - SK0704 → `"Do not pass a hardcoded queue or exchange URI string to GetSendEndpoint. Use convention-based endpoint resolution via IEndpointNameFormatter to produce environment-agnostic addresses."`

11. Architecture tests for SK0701 and SK0702 require two test cases each (fire path and pass path) as per the governance test rules. Analyzer tests for SK0703 and SK0704 require fire path and pass path (two test rows minimum per analyzer rule).

12. `MessagingArchitectureRules` factory methods accept assembly parameters supplied by the consuming test project via `typeof(SomeMessagingOrDomainType).Assembly`. No assembly paths are hard-coded in the predicates.

### MessagingArchRules — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/MessagingArchitectureRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: two predicate factory methods for SK0701 and SK0702 returning ConditionList |
| `Predicates/NoDirectBusInjectionOutsideMessagingPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: constructor parameter scan for IBus/IPublishEndpoint/ISendEndpointProvider; exempts SharedKernel.Messaging.* namespace |
| `Predicates/NoEventPublisherInDomainLayerPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: constructor parameter scan for IEventPublisher in domain-layer types (namespace signal and interface-signal detection) |
| `Diagnostics/SK0703_MessageBusSingletonRegistrationAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0703 fires on AddSingleton<IMessageBus,...>() or AddSingleton<IEventPublisher,...>() calls |
| `Diagnostics/SK0704_HardcodedQueueUriAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0704 fires on new Uri("queue:...") or new Uri("exchange:...") passed to GetSendEndpoint |

### MessagingArchRules — Acceptance Criteria

- [ ] `SK0701` fires when `IBus`, `IPublishEndpoint`, or `ISendEndpointProvider` is injected as a constructor parameter in a type outside `SharedKernel.Messaging.*`; does not fire for types within that namespace
- [ ] `SK0702` fires when `IEventPublisher` is a constructor parameter of a domain-layer type (interface-signal or `.Domain.` namespace signal); does not fire for application-layer types
- [ ] `SK0703` fires when `IMessageBus` or `IEventPublisher` appears as the first type argument to `AddSingleton`; does not fire for `AddScoped`
- [ ] `SK0704` fires when a string literal `"queue:..."` or `"exchange:..."` is passed inside a `new Uri(...)` argument to `GetSendEndpoint`; does not fire for convention-based resolution without a literal URI
- [ ] All four rules have compliant-pass and violation-fire test fixtures in `SharedKernel.ArchitectureTests` (SK0701, SK0702) and `SharedKernel.Analyzers.Tests` (SK0703, SK0704)
- [ ] All existing governance tests pass — no regressions
- [ ] `00.Governance/CLAUDE.md` updated with all four new rules, rationale, exemption lists, and failure message content

### MessagingArchRules — Dependencies

- Requires P-117 (`IMessageBus`, `IEventPublisher`, `PublishContext` defined in `SharedKernel.Messaging.Abstractions`; `MassTransitMessageBus`, `MassTransitEventPublisher`, `ConsumerBase<TMessage>`, `MessagingBusBuilder` in `SharedKernel.Messaging.MassTransit`): yes — SK0701 predicate uses the `IBus`/`IPublishEndpoint`/`ISendEndpointProvider` simple names that only exist once MassTransit is integrated; SK0702 uses `IEventPublisher` whose simple name must be stable; without P-117 the predicates check names that may not yet be canonical
- Requires `NoInfrastructureConstructorParametersPredicate` pattern (Mono.Cecil constructor inspection) from `SK.00.DomainLayerPurity` to be complete: yes (already complete — pattern established)
- Unblocks: CI messaging abstraction boundary gate across all downstream services consuming `SharedKernel.Messaging`

### MessagingArchRules — Tooling Version Notes

- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — SK0703 and SK0704 follow same constraint as SK0001–SK0011; syntax-only analysis, no semantic model required)
- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref in `SharedKernel.ArchitectureTests` — no change; covers both new predicates)
- Target framework: `netstandard2.0` (Analyzers) / `net10.0` (ArchitectureTests)

### MessagingArchRules — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-40 | Define `MessagingArchitectureRules` static class shape: two predicate factory methods; `NoDirectBusInjectionOutsideMessagingPredicate` design — constructor parameter name match for `IBus`/`IPublishEndpoint`/`ISendEndpointProvider`; exemption: `SharedKernel.Messaging.*` namespace prefix; failure message contract for SK0701 | SharedKernel.ArchitectureTests | `●` |
| D-41 | Define `NoEventPublisherInDomainLayerPredicate` shape: constructor parameter scan for `IEventPublisher` simple name; scope signals — namespace contains `".Domain."` OR implements entity/aggregate/value-object/domain-service interface; failure message contract for SK0702 | SharedKernel.ArchitectureTests | `●` |
| D-42 | Define SK0703 `MessageBusSingletonRegistration` — trigger: `AddSingleton` invocation with type argument whose name starts with `"IMessageBus"` or `"IEventPublisher"`; syntax-only (no SemanticModel); global scope; per-call-site `#pragma` suppression only; fix message: use `AddScoped` | SharedKernel.Analyzers | `●` |
| D-43 | Define SK0704 `HardcodedQueueUriInGetSendEndpoint` — trigger: `GetSendEndpoint` invocation containing a `new Uri(string)` argument whose literal value starts with `"queue:"` or `"exchange:"` (case-insensitive); syntax-only; global scope; per-call-site `#pragma` suppression only; fix message: use `IEndpointNameFormatter` convention | SharedKernel.Analyzers | `●` |
| C-50 | Implement `NoDirectBusInjectionOutsideMessagingPredicate` in `Predicates/` — `ICustomRule`; namespace exemption (`SharedKernel.Messaging.*`) as first guard; iterate `TypeDefinition.Methods` where `IsConstructor`; check `ParameterDefinition.ParameterType.Name` against `{"IBus","IPublishEndpoint","ISendEndpointProvider"}`; return false with SK0701 failure message naming offending type, parameter name, and recommended alternative | SharedKernel.ArchitectureTests | `●` |
| C-51 | Implement `NoEventPublisherInDomainLayerPredicate` in `Predicates/` — `ICustomRule`; for each type, evaluate domain-layer membership via namespace signal (`TypeDefinition.Namespace.Contains(".Domain.")`) OR interface signal (`TypeDefinition.Interfaces` contains `IEntity`, `IAggregateRoot`, `IValueObject`, or `IDomainService` by name); if in domain layer, check constructors for `ParameterDefinition.ParameterType.Name == "IEventPublisher"`; return false with SK0702 failure message on violation | SharedKernel.ArchitectureTests | `●` |
| C-52 | Implement `MessagingArchitectureRules` static class in `Rules/` — two factory methods: `NoDirectBusInjectionOutsideMessaging(params Assembly[])` → `ConditionList`, `NoEventPublisherInDomainLayer(Assembly)` → `ConditionList` | SharedKernel.ArchitectureTests | `●` |
| C-53 | Implement SK0703 `MessageBusSingletonRegistrationAnalyzer` — `InvocationExpressionSyntax` walker; filter to `AddSingleton` method name; check type arguments for `"IMessageBus"` or `"IEventPublisher"` simple name prefix; report SK0703 on the invocation expression; syntax-only, no SemanticModel | SharedKernel.Analyzers | `●` |
| C-54 | Implement SK0704 `HardcodedQueueUriAnalyzer` — `InvocationExpressionSyntax` walker; filter to `GetSendEndpoint` method name; inspect arguments for `ObjectCreationExpressionSyntax` or `ImplicitObjectCreationExpressionSyntax` of type `Uri`; check first argument for `StringLiteralExpression` whose value starts with `"queue:"` or `"exchange:"` (case-insensitive); report SK0704 on the Uri creation expression; syntax-only, no SemanticModel | SharedKernel.Analyzers | `●` |
| T-87 | Architecture test SK0701 (fire path): pass an application assembly containing a command handler class with a constructor parameter typed `IBus`; assert `NoDirectBusInjectionOutsideMessaging` fails and failure message names the offending type and parameter | SharedKernel.ArchitectureTests | `●` |
| T-88 | Architecture test SK0701 (pass path): pass an assembly containing a type in `SharedKernel.Messaging.MassTransit` namespace that injects `IBus`; assert rule passes (namespace exempt) | SharedKernel.ArchitectureTests | `●` |
| T-89 | Architecture test SK0701 (pass path — abstraction): pass an application assembly containing a command handler that injects `IMessageBus` only; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-90 | Architecture test SK0702 (fire path): pass a domain assembly containing a domain service class with a constructor parameter typed `IEventPublisher`; assert `NoEventPublisherInDomainLayer` fails and failure message names the offending type and correct flow | SharedKernel.ArchitectureTests | `●` |
| T-91 | Architecture test SK0702 (pass path): pass an application assembly containing an application service class with a constructor parameter typed `IEventPublisher`; assert rule passes (application layer is not domain layer) | SharedKernel.ArchitectureTests | `●` |
| T-92 | Analyzer test SK0703 (fire path): `services.AddSingleton<IMessageBus, MassTransitMessageBus>()` triggers SK0703; `services.AddSingleton<IEventPublisher, MassTransitEventPublisher>()` triggers SK0703 | SharedKernel.Analyzers.Tests | `●` |
| T-93 | Analyzer test SK0703 (pass path): `services.AddScoped<IMessageBus, MassTransitMessageBus>()` does not trigger SK0703; `services.AddScoped<IEventPublisher, MassTransitEventPublisher>()` does not trigger SK0703 | SharedKernel.Analyzers.Tests | `●` |
| T-94 | Analyzer test SK0704 (fire path): `provider.GetSendEndpoint(new Uri("queue:order-commands"))` triggers SK0704; `provider.GetSendEndpoint(new Uri("exchange:order-events"))` triggers SK0704 | SharedKernel.Analyzers.Tests | `●` |
| T-95 | Analyzer test SK0704 (pass path): `provider.GetSendEndpoint(formatter.GetDestinationAddress<OrderCommand>())` (no literal Uri construction) does not trigger SK0704; a `new Uri(someVariable)` (non-literal argument) does not trigger SK0704 | SharedKernel.Analyzers.Tests | `●` |
| DO-18 | Document all four `MessagingArchitectureRules` in `00.Governance/CLAUDE.md` and `00.Governance/README.md`: rationale for each rule (transport abstraction, domain purity, lifecycle correctness, environment-agnostic addressing), exemption lists, offending-pattern example, compliant-pattern example, failure message content, CI configuration guidance | SharedKernel.ArchitectureTests, SharedKernel.Analyzers | `●` |

---

## Phase: Governance: Extended Messaging Architecture Rules — Fault Consumers, Scheduling, Singleton Guards <!-- phase-key: SK.00.ExtendedMessagingArchRules -->

> Extend `SharedKernel.ArchitectureTests` and `SharedKernel.Analyzers` with four additional messaging enforcement rules covering fault-consumer registration misuse, direct `MassTransit.IMessageScheduler` injection, saga state classes missing `SagaStateBase`, and batch consumers registered via the wrong builder method. Introduced by WO-021 P-133 to close the misuse vectors created by P-125 through P-131.

### ExtendedMessagingArchRules — Goal

Each new messaging capability introduced in P-125 through P-131 (fault consumers, scheduling, sagas, batch consumers) creates a new class of misuse that the compiler cannot catch. Without rule enforcement, teams will bypass `AddFaultConsumer` and register fault consumers manually (breaking the MassTransit adapter chain), inject `MassTransit.IMessageScheduler` directly (re-introducing transport coupling), create saga state classes without `SagaStateBase` (missing version field, causing saga persistence failures), and register batch consumers via `AddConsumer` (ignoring batch configuration, resulting in one-by-one processing). Architecture tests and Roslyn analyzers encode platform decisions as executable contracts that run in CI.

### ExtendedMessagingArchRules — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`, `SharedKernel.Analyzers`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/ExtendedMessagingArchitectureRules.cs` — static class housing SK0706 and SK0707 NetArchTest predicate factory methods
  - `SharedKernel.ArchitectureTests/Predicates/NoDirectSchedulerInjectionOutsideMessagingPredicate.cs` — `ICustomRule`: constructor parameter scan for `MassTransit.IMessageScheduler` simple name in types outside `SharedKernel.Messaging.*`
  - `SharedKernel.ArchitectureTests/Predicates/SagaStateMustExtendSagaStateBasePredicate.cs` — `ICustomRule`: checks types implementing `ISaga` (simple name) extend `SagaStateBase` (simple name, inheritance chain check via `TypeDefinition.BaseType`)
  - `SharedKernel.Analyzers/Diagnostics/SK0705_FaultConsumerDirectRegistrationAnalyzer.cs` — Roslyn analyzer: fires on `services.AddScoped<IFaultConsumer<...>>()` or `services.AddSingleton<IFaultConsumer<...>>()` registrations
  - `SharedKernel.Analyzers/Diagnostics/SK0708_BatchConsumerRegisteredViaAddConsumerAnalyzer.cs` — Roslyn analyzer: fires on `AddConsumer<T>()` where `T` is a type whose simple name or base type contains `BatchConsumerBase`
- Modified files:
  - `00.Governance/CLAUDE.md` — add SK0705–SK0708 to diagnostic registry; add `ExtendedMessagingArchitectureRules` to architecture test contracts section; document all four rules with rationale, exemption lists, and offending/compliant patterns
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### ExtendedMessagingArchRules — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0705 | FaultConsumerDirectRegistration | Usage | Warning | `services.AddScoped<IFaultConsumer<T>>()` or `services.AddSingleton<IFaultConsumer<T>>()` detected — fault consumers must be registered via `MessagingBusBuilder.AddFaultConsumer<TMessage, TConsumer>()` to wire the MassTransit `Fault<T>` adapter correctly |
| SK0706 | DirectMassTransitSchedulerInjection | Design | Warning | `MassTransit.IMessageScheduler` appears as a constructor parameter in a type outside `SharedKernel.Messaging.*` — inject `SharedKernel.Messaging.Abstractions.IMessageScheduler` instead to preserve transport independence |
| SK0707 | SagaStateMustExtendSagaStateBase | Design | Warning | A class implementing `ISaga` does not extend `SagaStateBase` from `SharedKernel.Messaging.MassTransit` — all saga state classes must extend `SagaStateBase` to carry the platform's standard correlation ID, version, and audit fields |
| SK0708 | BatchConsumerRegisteredViaAddConsumer | Usage | Warning | A subclass of `BatchConsumerBase<T>` is registered via `AddConsumer<T>()` — batch consumers must be registered via `MessagingBusBuilder.AddBatchConsumer<T>()` to apply `MessageLimit` and `TimeLimit` batch configuration |

### ExtendedMessagingArchRules — Implementation Rules

1. SK0705 `FaultConsumerDirectRegistrationAnalyzer` operates on `InvocationExpressionSyntax` nodes. Trigger conditions (both must hold):
   - The method name is one of `"AddScoped"` or `"AddSingleton"` (exact match on `MemberAccessExpressionSyntax.Name.Identifier.Text`).
   - At least one type argument is a `GenericNameSyntax` whose `Identifier.Text` is `"IFaultConsumer"` (simple name check — unique within the SDK; covers `AddScoped<IFaultConsumer<TMessage>>()` and `AddScoped<IFaultConsumer<TMessage>, TImpl>()`).
   Syntax-only; no SemanticModel required. Reports SK0705 on the invocation expression. No suppression namespace — suppress per-call-site via `#pragma warning disable SK0705` only when explicitly bypassing the builder (document the rationale inline).

2. SK0706 `DirectMassTransitSchedulerInjection` is a NetArchTest architecture predicate (not a Roslyn analyzer). The rule: `NoDirectSchedulerInjectionOutsideMessagingPredicate` — an `ICustomRule` that iterates `TypeDefinition.Methods` where `IsConstructor` is true. For each constructor, checks each `ParameterDefinition.ParameterType.Name` for exact name match `"IMessageScheduler"`. The discriminating check is whether the parameter type's `ParameterType.Namespace` starts with `"MassTransit"` — this distinguishes `MassTransit.IMessageScheduler` (forbidden) from `SharedKernel.Messaging.Abstractions.IMessageScheduler` (permitted). Namespace exemption guard (first check): types whose `TypeDefinition.Namespace` starts with `"SharedKernel.Messaging"` return true unconditionally — the messaging package itself may use `MassTransit.IMessageScheduler` freely for adapter wiring. Failure message: `"{offendingType} injects MassTransit.IMessageScheduler directly. Use SharedKernel.Messaging.Abstractions.IMessageScheduler to preserve transport independence."` Returns `ConditionList`.

3. SK0707 `SagaStateMustExtendSagaStateBase` is a NetArchTest architecture predicate. The rule: `SagaStateMustExtendSagaStateBasePredicate` — an `ICustomRule` that scopes to types whose `TypeDefinition.Interfaces` contains an entry with `InterfaceType.Name == "ISaga"` (exact simple name match). For each such type, checks whether the inheritance chain (walking `TypeDefinition.BaseType` iteratively, stopping at `null` or `"Object"`) contains any `TypeReference` whose `Name == "SagaStateBase"` (exact simple name match). If no such ancestor is found, returns false (rule violated) with failure message: `"{offendingType} implements ISaga but does not extend SagaStateBase. All saga state classes must extend SagaStateBase to carry correlation ID, version, and audit fields."` Returns `ConditionList`.

4. SK0708 `BatchConsumerRegisteredViaAddConsumerAnalyzer` operates on `InvocationExpressionSyntax` nodes. Trigger conditions (both must hold):
   - The method name is `"AddConsumer"` (exact match on `MemberAccessExpressionSyntax.Name.Identifier.Text` or `IdentifierNameSyntax.Identifier.Text`).
   - The method has exactly one type argument whose `ToString()` or `Identifier.Text` contains `"BatchConsumer"` (simple name substring check — unique within the SDK for `BatchConsumerBase<T>` subclasses).
   Because type argument name checking requires the consumer class name to contain `"BatchConsumer"`, this rule is a naming-convention-guided heuristic. If the consumer class name does not contain `"BatchConsumer"`, the rule will not fire (a false negative). Document this limitation in `CLAUDE.md`. Syntax-only; no SemanticModel required. Reports SK0708 on the invocation expression. No suppression namespace — suppress per-call-site via `#pragma warning disable SK0708`.

5. `ExtendedMessagingArchitectureRules` static class factory method signatures:
   - `ExtendedMessagingArchitectureRules.NoDirectMassTransitSchedulerInjection(params Assembly[])` → `ConditionList`
   - `ExtendedMessagingArchitectureRules.SagaStatesMustExtendSagaStateBase(Assembly)` → `ConditionList`

6. `NoDirectSchedulerInjectionOutsideMessagingPredicate` distinguishes between the two `IMessageScheduler` interfaces by checking `ParameterDefinition.ParameterType.Namespace`: `"MassTransit"` → forbidden; `"SharedKernel.Messaging.Abstractions"` → permitted. This namespace check requires that the IL assembly references are resolved, which Mono.Cecil does from the loaded assembly metadata. If the namespace cannot be resolved (e.g., the assembly is tested in isolation without the MassTransit reference), the predicate falls back to checking whether `ParameterType.Scope.Name` contains `"MassTransit"` as a substring — the module scope name includes the assembly name.

7. `SagaStateMustExtendSagaStateBasePredicate` walks `TypeDefinition.BaseType` iteratively (each step resolves `BaseType.Resolve()` to get the next `TypeDefinition`). The walk terminates when `BaseType` is null or `BaseType.Name` is `"Object"`. This handles multi-level inheritance chains (e.g., `OrderSagaState : SagaAuditBase : SagaStateBase`). If `BaseType.Resolve()` returns null (the base type is in an unloaded assembly), the predicate must treat the type as possibly-compliant (return true — fail open rather than false-positive) and document this limitation.

8. Both architecture predicates (`NoDirectSchedulerInjectionOutsideMessagingPredicate`, `SagaStateMustExtendSagaStateBasePredicate`) reuse the established Mono.Cecil `TypeDefinition` access pattern from `DoesNotContainThrowIlPredicate`. No new NuGet dependency — the existing `Mono.Cecil >= 0.11.5` explicit reference covers both.

9. SK0705 and SK0708 both follow `netstandard2.0` constraint. Zero new NuGet dependencies beyond `Microsoft.CodeAnalysis.CSharp`. Both use syntax-only analysis; neither requires `SemanticModel`.

10. `ExtendedMessagingArchitectureRules` lives in `SharedKernel.ArchitectureTests/Rules/` alongside `MessagingArchitectureRules.cs`. It must not reference any MassTransit package directly. Type names are matched by simple name only.

11. Exemption list for SK0706 (`NoDirectSchedulerInjectionOutsideMessagingPredicate`):
    - Types whose `TypeDefinition.Namespace` starts with `"SharedKernel.Messaging"` — the messaging package adapter layer may reference `MassTransit.IMessageScheduler` for internal wiring. Any additional exemption must be documented in `00.Governance/CLAUDE.md` before applying.

12. SK0708 naming convention requirement: batch consumer implementation classes must contain `"BatchConsumer"` in their class name to be detected by the analyzer. Teams naming their batch consumer `OrderProcessor` (without `"BatchConsumer"` in the name) will not receive the SK0708 diagnostic. Document this limitation in `00.Governance/CLAUDE.md` and `00.Governance/README.md`; recommend the naming convention `{Purpose}BatchConsumer` as an enforcement aid.

### ExtendedMessagingArchRules — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/ExtendedMessagingArchitectureRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: two predicate factory methods for SK0706 and SK0707 returning ConditionList |
| `Predicates/NoDirectSchedulerInjectionOutsideMessagingPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: constructor parameter scan for MassTransit.IMessageScheduler (namespace-disambiguated); exempts SharedKernel.Messaging.* |
| `Predicates/SagaStateMustExtendSagaStateBasePredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: inheritance-chain walk for ISaga implementors; fails if SagaStateBase is absent in BaseType chain |
| `Diagnostics/SK0705_FaultConsumerDirectRegistrationAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0705 fires on AddScoped/AddSingleton with IFaultConsumer<T> type argument |
| `Diagnostics/SK0708_BatchConsumerRegisteredViaAddConsumerAnalyzer.cs` | SharedKernel.Analyzers | Create | Roslyn analyzer: SK0708 fires on AddConsumer<T> where T contains "BatchConsumer" in its name |

### ExtendedMessagingArchRules — Acceptance Criteria

- [ ] `SK0705` fires when `IFaultConsumer<T>` is registered via `services.AddScoped` or `services.AddSingleton`; does not fire for `AddFaultConsumer` registrations
- [ ] `SK0706` fires when `MassTransit.IMessageScheduler` is a constructor dependency outside `SharedKernel.Messaging.*`; does not fire for `SharedKernel.Messaging.Abstractions.IMessageScheduler`
- [ ] `SK0707` fires when a class implements `ISaga` but does not extend `SagaStateBase`; does not fire for classes extending `SagaStateBase`
- [ ] `SK0708` fires when a `BatchConsumerBase<T>` subclass (identified by name containing `"BatchConsumer"`) is registered via `AddConsumer<T>()`; does not fire for `AddBatchConsumer<T>()` registrations
- [ ] All four rules have compliant-pass and violation-fire test fixtures in `SharedKernel.ArchitectureTests` (SK0706, SK0707) and `SharedKernel.Analyzers.Tests` (SK0705, SK0708)
- [ ] All existing governance tests (MSG0101–MSG0104 mapped as SK0701–SK0704) still pass — no regressions
- [ ] `00.Governance/CLAUDE.md` updated with the four new rules, rationale, exemption lists, and SK0708 naming-convention limitation documented

### ExtendedMessagingArchRules — Dependencies

- Requires P-125 through P-131 (`IFaultConsumer<T>` interface, `MassTransit.IMessageScheduler` adapter, `SagaStateBase`, `BatchConsumerBase<T>` defined in `SharedKernel.Messaging.MassTransit`): yes — SK0705, SK0706, SK0707, and SK0708 reference these type names as their detection anchors; before these types exist the rules are checking for names that do not yet exist (rules will trivially pass, which is safe but vacuous)
- Requires `MessagingArchitectureRules` (`SK.00.MessagingArchRules`) to be complete: yes — establishes the established Mono.Cecil constructor-parameter inspection pattern in `SharedKernel.ArchitectureTests`
- Unblocks: CI enforcement of extended messaging misuse patterns across all downstream services

### ExtendedMessagingArchRules — Tooling Version Notes

- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — SK0705 and SK0708 follow same constraint as all other SK analyzers; syntax-only analysis, no SemanticModel required)
- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref in `SharedKernel.ArchitectureTests` — no change; covers both new predicates)
- Target framework: `netstandard2.0` (Analyzers) / `net10.0` (ArchitectureTests)

### ExtendedMessagingArchRules — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-44 | Define SK0705 `FaultConsumerDirectRegistration` — trigger: `AddScoped` or `AddSingleton` invocation with a type argument whose simple name is `"IFaultConsumer"` (GenericNameSyntax identifier text check); syntax-only; global scope; per-call-site `#pragma` suppression; fix message: use `MessagingBusBuilder.AddFaultConsumer<TMessage, TConsumer>()` | SharedKernel.Analyzers | `●` |
| D-45 | Define SK0706 `DirectMassTransitSchedulerInjection` — `NoDirectSchedulerInjectionOutsideMessagingPredicate` shape: constructor parameter scan for `IMessageScheduler`; namespace disambiguation (`ParameterType.Namespace.StartsWith("MassTransit")` → forbidden vs. `"SharedKernel.Messaging.Abstractions"` → permitted); exemption: `SharedKernel.Messaging.*` namespace prefix; failure message naming offending type and recommended alternative | SharedKernel.ArchitectureTests | `●` |
| D-46 | Define SK0707 `SagaStateMustExtendSagaStateBase` — `SagaStateMustExtendSagaStateBasePredicate` shape: scope to `ISaga` implementing types; `TypeDefinition.BaseType` iterative walk stopping at null/Object; check each ancestor `Name == "SagaStateBase"`; fail-open if `BaseType.Resolve()` returns null (unloaded assembly); failure message naming offending type | SharedKernel.ArchitectureTests | `●` |
| D-47 | Define SK0708 `BatchConsumerRegisteredViaAddConsumer` — trigger: `AddConsumer` invocation (exact name) with a single type argument containing `"BatchConsumer"` as a substring in the type argument identifier text; syntax-only; document naming-convention dependency (class must contain `"BatchConsumer"` to be detected); no suppression namespace; per-call-site `#pragma` suppression | SharedKernel.Analyzers | `●` |
| C-55 | Implement `NoDirectSchedulerInjectionOutsideMessagingPredicate` in `Predicates/` — `ICustomRule`; namespace exemption (`SharedKernel.Messaging.*`) as first guard; iterate `TypeDefinition.Methods` where `IsConstructor`; check `ParameterDefinition.ParameterType.Name == "IMessageScheduler"` AND `ParameterType.Namespace.StartsWith("MassTransit")` (or `Scope.Name.Contains("MassTransit")` fallback); return false with SK0706 failure message on violation | SharedKernel.ArchitectureTests | `●` |
| C-56 | Implement `SagaStateMustExtendSagaStateBasePredicate` in `Predicates/` — `ICustomRule`; scope to types whose `TypeDefinition.Interfaces` contains an entry with `InterfaceType.Name == "ISaga"`; walk `TypeDefinition.BaseType` iteratively (`Resolve()` each step); check `TypeReference.Name == "SagaStateBase"` at each level; fail-open if `Resolve()` returns null; return false with SK0707 failure message if `SagaStateBase` not found in chain | SharedKernel.ArchitectureTests | `●` |
| C-57 | Implement `ExtendedMessagingArchitectureRules` static class in `Rules/` — two factory methods: `NoDirectMassTransitSchedulerInjection(params Assembly[])` → `ConditionList`, `SagaStatesMustExtendSagaStateBase(Assembly)` → `ConditionList` | SharedKernel.ArchitectureTests | `●` |
| C-58 | Implement SK0705 `FaultConsumerDirectRegistrationAnalyzer` — `InvocationExpressionSyntax` walker; filter to `AddScoped` or `AddSingleton` method names; check type arguments for `GenericNameSyntax` whose `Identifier.Text == "IFaultConsumer"`; report SK0705 on the invocation expression; syntax-only, no SemanticModel | SharedKernel.Analyzers | `●` |
| C-59 | Implement SK0708 `BatchConsumerRegisteredViaAddConsumerAnalyzer` — `InvocationExpressionSyntax` walker; filter to `AddConsumer` method name; check type argument identifier text for `"BatchConsumer"` substring; report SK0708 on the invocation expression; syntax-only, no SemanticModel | SharedKernel.Analyzers | `●` |
| T-96 | Architecture test SK0706 (fire path): pass an application assembly containing a command handler with a constructor parameter typed `MassTransit.IMessageScheduler` (namespace attribute `"MassTransit"`); assert `NoDirectMassTransitSchedulerInjection` fails and failure message names the offending type | SharedKernel.ArchitectureTests | `●` |
| T-97 | Architecture test SK0706 (pass path): pass an application assembly containing a service that injects `SharedKernel.Messaging.Abstractions.IMessageScheduler` only; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-98 | Architecture test SK0707 (fire path): pass an assembly containing a class that implements `ISaga` but extends `object` directly (no `SagaStateBase`); assert `SagaStatesMustExtendSagaStateBase` fails and failure message names the offending type | SharedKernel.ArchitectureTests | `●` |
| T-99 | Architecture test SK0707 (pass path): pass an assembly containing a saga state class that implements `ISaga` and extends `SagaStateBase`; assert rule passes | SharedKernel.ArchitectureTests | `●` |
| T-100 | Analyzer test SK0705 (fire path): `services.AddScoped<IFaultConsumer<OrderPlaced>, OrderFaultConsumer>()` triggers SK0705; `services.AddSingleton<IFaultConsumer<OrderPlaced>>()` triggers SK0705 | SharedKernel.Analyzers.Tests | `●` |
| T-101 | Analyzer test SK0705 (pass path): `builder.AddFaultConsumer<OrderPlaced, OrderFaultConsumer>()` does not trigger SK0705; `services.AddScoped<IOrderFaultHandler, OrderFaultHandler>()` (no `IFaultConsumer` type arg) does not trigger SK0705 | SharedKernel.Analyzers.Tests | `●` |
| T-102 | Analyzer test SK0708 (fire path): `builder.AddConsumer<OrderBatchConsumer>()` where the type name contains `"BatchConsumer"` triggers SK0708 | SharedKernel.Analyzers.Tests | `●` |
| T-103 | Analyzer test SK0708 (pass path): `builder.AddBatchConsumer<OrderBatchConsumer>()` does not trigger SK0708; `builder.AddConsumer<OrderCommandConsumer>()` where the type name does not contain `"BatchConsumer"` does not trigger SK0708 | SharedKernel.Analyzers.Tests | `●` |
| DO-19 | Document all four `ExtendedMessagingArchitectureRules` in `00.Governance/CLAUDE.md` and `00.Governance/README.md`: rationale (adapter-chain integrity, transport independence, saga-state consistency, batch-configuration enforcement), exemption lists, SK0708 naming-convention limitation, offending-pattern example, compliant-pattern example, failure message content | SharedKernel.ArchitectureTests, SharedKernel.Analyzers | `●` |

---

## Phase: Redis Package Topology Architecture Rules <!-- phase-key: SK.00.RedisTopology -->

> Extend `SharedKernel.ArchitectureTests` with a new `RedisTopologyRules` static class that mechanically enforces the post-split five-package Redis topology from P-140–P-144 (`SharedKernel.Caching.Redis.Core`, `.Redis`, `.Redis.DistributedLocking`, `.Redis.HashStore`, `.Redis.PubSub`), and re-affirms the `02.Caching` ↔ `07.Messaging` exclusion boundary across the new package set. Introduced by WO-023 P-145. No new SK diagnostic IDs — all five checks are assembly-dependency-graph predicates using the established `ConditionList` / `.Should().NotHaveDependencyOn()` pattern from `CachingAbstractionRules`.

### RedisTopology — Goal

The five-package Redis topology (P-140–P-144) is only "gold standard" if a developer cannot silently reintroduce a sibling-to-sibling dependency (e.g., `Redis.DistributedLocking` → `Redis.HashStore` "because it's already a transitive dependency anyway") or let `07.Messaging` and `02.Caching` drift back together. This phase adds five NetArchTest assembly-dependency predicates to `SharedKernel.ArchitectureTests`, mirroring the precedent set by Phase 17's "Redis and FusionCache must never reference each other" rule (`SharedKernelLayeringRules`/`CachingAbstractionRules`) and WO-021/P-133's messaging architecture rules. No Roslyn analyzer or Mono.Cecil IL inspection is required — every check in this phase is a pure assembly-reference (dependency graph) check, the same category as `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching`.

### RedisTopology — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/RedisTopologyRules.cs` — static class housing five predicate factory methods (see Implementation Rules below)
  - `SharedKernel.ArchitectureTests/SharedKernel.ArchitectureTests.Tests/RedisTopologyRulesTests.cs` — fire-path and pass-path tests for all five predicates
- Modified files:
  - `00.Governance/CLAUDE.md` — add `RedisTopologyRules` to architecture test contracts; add five new implementation rules; update Changelog
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### RedisTopology — Diagnostic Registry Changes (analyzers only)

_None._ This phase introduces zero new Roslyn analyzers and zero new SK diagnostic IDs. All five checks are NetArchTest `ConditionList` assembly-dependency predicates, consistent with `CachingAbstractionRules` (SK0007's companion architecture rule, which also introduced no separate ID for the architecture-side check).

### RedisTopology — Implementation Rules

1. `RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages(Assembly redisCoreAssembly)` → `ConditionList`
   Asserts that `SharedKernel.Caching.Redis.Core` has no dependency on any of the four capability package assembly-name prefixes: `"SharedKernel.Caching.Redis.DistributedLocking"`, `"SharedKernel.Caching.Redis.HashStore"`, `"SharedKernel.Caching.Redis.PubSub"`, and `"SharedKernel.Caching.Redis"` exact-match for the L2 backplane package (must not match the `.Redis.Core` assembly itself — use an exact-name comparison or a prefix list that excludes `"SharedKernel.Caching.Redis.Core"`). Iterates four `.Should().NotHaveDependencyOn(term)` calls, one per forbidden term, following the established iterative pattern from `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure`. Failure message names the offending capability package.
   Rationale: `Redis.Core` is the shared connection/health/resilience foundation. A reference from Core to any capability package is a layering inversion — capability packages depend on Core, never the reverse.

2. `RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther(params Assembly[] capabilityAssemblies)` → `ConditionList`
   Asserts pairwise that none of the four capability packages (`Redis` (L2), `Redis.DistributedLocking`, `Redis.HashStore`, `Redis.PubSub`) references any of the other three. The caller passes all four capability assemblies; the predicate iterates each assembly and asserts `.Should().NotHaveDependencyOn(otherPackageName)` for the three sibling package names (excluding itself and excluding `"SharedKernel.Caching.Redis.Core"` and `"SharedKernel.Caching.Abstractions"`, both of which are permitted dependencies). Builds the combined `ConditionList` using the same multi-assembly iteration approach as `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching` (first assembly drives the returned `ConditionList`; the remaining assemblies' checks are asserted via additional `ConditionList` instances combined by the consuming test, OR the method returns `ConditionList[]` — see Tooling Version Notes for the resolution). Failure message names the offending capability package and the sibling it references.
   Rationale: sibling role-packages must depend only on `.{Provider}.Core` (root `CLAUDE.md` "Provider role-split variant" rule) — a sibling-to-sibling reference (e.g., `Redis.DistributedLocking` → `Redis.HashStore`) is exactly the shortcut this phase forecloses.

3. `RedisTopologyRules.PubSubNeverReferencesMessaging(Assembly pubSubAssembly)` → `ConditionList`
   Asserts that `SharedKernel.Caching.Redis.PubSub` has no dependency on any assembly whose name starts with `"SharedKernel.Messaging"`. Single `.Should().NotHaveDependencyOn("SharedKernel.Messaging")` call — the prefix `"SharedKernel.Messaging"` covers both `SharedKernel.Messaging.Abstractions` and `SharedKernel.Messaging.MassTransit` as NetArchTest matches by assembly-name substring (established behavior, documented for `CachingAbstractionRules`).
   Rationale: codifies the Issue 3 boundary — `Redis.PubSub` is an ephemeral, no-delivery-guarantee signaling channel (`ICacheInvalidationBus`/`IRedisChannelService`) and must never become a backdoor path into the durable `IMessageBus` abstraction.

4. `RedisTopologyRules.MessagingNeverReferencesCaching(params Assembly[] messagingAssemblies)` → `ConditionList`
   Asserts that no assembly in `SharedKernel.Messaging.*` (the caller supplies `SharedKernel.Messaging.Abstractions` and `SharedKernel.Messaging.MassTransit`) has a dependency on any assembly whose name starts with `"SharedKernel.Caching"`. Iterates `.Should().NotHaveDependencyOn("SharedKernel.Caching")` per supplied assembly, combining `ConditionList` results the same way as rule 2. Failure message names the offending messaging assembly.
   Rationale: this is the structural converse of rule 3 and of the root `CLAUDE.md` hard rule ("`07.Messaging` must never reference any `SharedKernel.Caching.*` package, and no `SharedKernel.Caching.*` package may reference any `SharedKernel.Messaging.*` package"). Both directions must be independently asserted because NetArchTest dependency checks are directional — passing rule 3 does not imply rule 4 passes.

5. `RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies(Assembly abstractionsAssembly)` → `ConditionList`
   Re-verification (not a new rule) of the existing guarantee that `SharedKernel.Caching.Abstractions` has zero dependencies beyond `Microsoft.Extensions.DependencyInjection.Abstractions`. Asserts `.Should().NotHaveDependencyOn(term)` for each of: `"SharedKernel.Caching.Redis"`, `"StackExchange.Redis"`, `"Microsoft.EntityFrameworkCore"`, `"MassTransit"` — the four infrastructure families that must never leak into the abstractions package across the now five-package Redis topology. This is the same shape as `SharedKernelLayeringRules.CoreReferencesNothing`, scoped to `SharedKernel.Caching.Abstractions`.
   Rationale: confirms that the five-package split did not introduce a transitive dependency from any new capability package back into the abstraction the capability packages themselves implement.

6. All five factory methods accept `Assembly` / `params Assembly[]` parameters supplied by the consuming test project via `typeof(SomeTypeInPackage).Assembly` — no assembly paths are hard-coded, consistent with every prior `*Rules` static class in `SharedKernel.ArchitectureTests`.

7. `RedisTopologyRules` lives in `SharedKernel.ArchitectureTests/Rules/` alongside `CachingAbstractionRules.cs`. It must not reference any `StackExchange.Redis`, `MassTransit`, or EF Core package directly — all checks are assembly-name-string-based via NetArchTest's dependency scanner, matching the existing `CachingAbstractionRules` implementation style (no Mono.Cecil, no new `ICustomRule`, no new NuGet dependency).

8. Exemption list (permitted cross-references within the Redis topology — must hold for rules 1 and 2 to pass without modification):
   - `Redis.Core` → `SharedKernel.Caching.Abstractions` (permitted; Core implements abstraction-facing health/connection contracts)
   - `Redis` (L2), `Redis.DistributedLocking`, `Redis.HashStore`, `Redis.PubSub` → `Redis.Core` (permitted; the shared foundation)
   - `Redis` (L2), `Redis.DistributedLocking`, `Redis.HashStore`, `Redis.PubSub` → `SharedKernel.Caching.Abstractions` (permitted; each implements abstraction interfaces)
   Any additional exemption beyond this list must be documented in `00.Governance/CLAUDE.md` under `RedisTopologyRules` before it is applied in code.

### RedisTopology — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/RedisTopologyRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: five predicate factory methods (`RedisCoreNeverReferencesCapabilityPackages`, `CapabilityPackagesNeverReferenceEachOther`, `PubSubNeverReferencesMessaging`, `MessagingNeverReferencesCaching`, `CachingAbstractionsHasNoInfrastructureDependencies`) returning `ConditionList` / `ConditionList[]` |
| `SharedKernel.ArchitectureTests.Tests/RedisTopologyRulesTests.cs` | SharedKernel.ArchitectureTests.Tests | Create | Fire-path and pass-path tests for all five predicates (minimum 10 test cases) |

### RedisTopology — Acceptance Criteria

- [ ] NetArchTest rule: `SharedKernel.Caching.Redis.Core` has no project reference to any of the four capability packages (`Redis`, `Redis.DistributedLocking`, `Redis.HashStore`, `Redis.PubSub`)
- [ ] NetArchTest rule: the four capability packages do not reference each other pairwise (12 directional pairs collapse to the iterative per-assembly check in rule 2)
- [ ] NetArchTest rule: `SharedKernel.Caching.Redis.PubSub` has no reference to any `SharedKernel.Messaging.*` assembly
- [ ] NetArchTest rule: no `SharedKernel.Messaging.*` assembly (`Abstractions`, `MassTransit`) references any `SharedKernel.Caching.*` assembly
- [ ] NetArchTest rule: `SharedKernel.Caching.Abstractions` has zero dependencies beyond `Microsoft.Extensions.DependencyInjection.Abstractions` (re-verified against `Redis.Core`, `Redis.DistributedLocking`, `Redis.HashStore`, `Redis.PubSub`, `StackExchange.Redis`, EF Core, MassTransit)
- [ ] All five rules documented in `00.Governance/CLAUDE.md` under `RedisTopologyRules` with the exemption list from Implementation Rule 8
- [ ] All new and existing architecture tests pass (no regressions to the 66 existing architecture tests)
- [ ] `dotnet build` clean; no new compile warnings

### RedisTopology — Dependencies

- Requires P-140 (`SharedKernel.Caching.Redis.Core` package exists with the connection/health/resilience surface): yes
- Requires P-141 (`SharedKernel.Caching.Redis` L2 backplane package exists): yes
- Requires P-142 (`SharedKernel.Caching.Redis.DistributedLocking` package exists): yes
- Requires P-143 (`SharedKernel.Caching.Redis.HashStore` package exists): yes
- Requires P-144 (`SharedKernel.Caching.Redis.PubSub` package exists): yes
- Requires `CachingAbstractionRules` (`SK.00.CachingEnforcement`) to be complete: yes — establishes the `ConditionList` assembly-dependency-string predicate pattern reused by all five new rules
- Requires `MessagingArchitectureRules` (`SK.00.MessagingArchRules`) to be complete: yes — establishes the `SharedKernel.Messaging.*` prefix convention reused by rules 3 and 4
- Unblocks: CI architecture gate integration for the full five-package Redis topology; closes the WO-023 governance loop opened by P-140–P-144

### RedisTopology — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change; all five rules use only the fluent `.Should().NotHaveDependencyOn(...)` surface already in use by `CachingAbstractionRules`)
- `Mono.Cecil`: not required for this phase — no IL inspection; do not add new predicates to `Predicates/`
- `Microsoft.CodeAnalysis.CSharp`: not applicable — no analyzer work in this phase
- Target framework: `net10.0` (`SharedKernel.ArchitectureTests` and its test project)

### RedisTopology — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-48 | Define `RedisTopologyRules` static class shape: five factory methods (`RedisCoreNeverReferencesCapabilityPackages`, `CapabilityPackagesNeverReferenceEachOther`, `PubSubNeverReferencesMessaging`, `MessagingNeverReferencesCaching`, `CachingAbstractionsHasNoInfrastructureDependencies`); document the permitted-cross-reference exemption list (Rule 8); resolve the multi-`ConditionList` return shape for rules 2 and 4 | SharedKernel.ArchitectureTests | `●` |
| C-60 | Implement `RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages(Assembly)` — iterative `.Should().NotHaveDependencyOn(term)` over the four capability package name prefixes, excluding `"SharedKernel.Caching.Redis.Core"` itself | SharedKernel.ArchitectureTests | `●` |
| C-61 | Implement `RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther(params Assembly[])` — for each supplied capability assembly, assert `.Should().NotHaveDependencyOn(siblingName)` for each of the other three sibling package names, excluding `Redis.Core` and `Caching.Abstractions` (permitted) | SharedKernel.ArchitectureTests | `●` |
| C-62 | Implement `RedisTopologyRules.PubSubNeverReferencesMessaging(Assembly)` — single `.Should().NotHaveDependencyOn("SharedKernel.Messaging")` call against the `Redis.PubSub` assembly | SharedKernel.ArchitectureTests | `●` |
| C-63 | Implement `RedisTopologyRules.MessagingNeverReferencesCaching(params Assembly[])` — for each supplied `SharedKernel.Messaging.*` assembly, assert `.Should().NotHaveDependencyOn("SharedKernel.Caching")` | SharedKernel.ArchitectureTests | `●` |
| C-64 | Implement `RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies(Assembly)` — iterative `.Should().NotHaveDependencyOn(term)` over `"SharedKernel.Caching.Redis"`, `"StackExchange.Redis"`, `"Microsoft.EntityFrameworkCore"`, `"MassTransit"` against the `SharedKernel.Caching.Abstractions` assembly | SharedKernel.ArchitectureTests | `●` |
| T-104 | Architecture test (fire path): contrived assembly reference where `Redis.Core` imports a type from `Redis.HashStore`; assert `RedisCoreNeverReferencesCapabilityPackages` fails with `Redis.HashStore` named in the failure message | SharedKernel.ArchitectureTests.Tests | `●` |
| T-105 | Architecture test (pass path): real `SharedKernel.Caching.Redis.Core` assembly (P-140) referencing only `SharedKernel.Caching.Abstractions`; assert `RedisCoreNeverReferencesCapabilityPackages` passes | SharedKernel.ArchitectureTests.Tests | `●` |
| T-106 | Architecture test (fire path): contrived assembly reference where `Redis.DistributedLocking` imports a type from `Redis.HashStore`; assert `CapabilityPackagesNeverReferenceEachOther` fails with both package names in the failure message | SharedKernel.ArchitectureTests.Tests | `●` |
| T-107 | Architecture test (pass path): real `Redis`, `Redis.DistributedLocking`, `Redis.HashStore`, `Redis.PubSub` assemblies (P-141–P-144), each referencing only `Redis.Core` and `Caching.Abstractions`; assert `CapabilityPackagesNeverReferenceEachOther` passes | SharedKernel.ArchitectureTests.Tests | `●` |
| T-108 | Architecture test (fire path): contrived assembly reference where `Redis.PubSub` imports a type from `SharedKernel.Messaging.Abstractions`; assert `PubSubNeverReferencesMessaging` fails | SharedKernel.ArchitectureTests.Tests | `●` |
| T-109 | Architecture test (pass path): real `SharedKernel.Caching.Redis.PubSub` assembly (P-144) with no `SharedKernel.Messaging.*` reference; assert `PubSubNeverReferencesMessaging` passes | SharedKernel.ArchitectureTests.Tests | `●` |
| T-110 | Architecture test (fire path): contrived assembly reference where `SharedKernel.Messaging.MassTransit` imports a type from `SharedKernel.Caching.Redis.PubSub`; assert `MessagingNeverReferencesCaching` fails with the offending messaging assembly named in the failure message | SharedKernel.ArchitectureTests.Tests | `●` |
| T-111 | Architecture test (pass path): real `SharedKernel.Messaging.Abstractions` and `SharedKernel.Messaging.MassTransit` assemblies with no `SharedKernel.Caching.*` reference; assert `MessagingNeverReferencesCaching` passes | SharedKernel.ArchitectureTests.Tests | `●` |
| T-112 | Architecture test (pass path): real `SharedKernel.Caching.Abstractions` assembly (re-verification) referencing only `Microsoft.Extensions.DependencyInjection.Abstractions`; assert `CachingAbstractionsHasNoInfrastructureDependencies` passes against `Redis.Core`, `StackExchange.Redis`, EF Core, and MassTransit terms | SharedKernel.ArchitectureTests.Tests | `●` |
| DO-20 | Document `RedisTopologyRules` (all five predicates) in `00.Governance/CLAUDE.md` architecture test contracts section: rationale, permitted-cross-reference exemption list (Rule 8), offending/compliant pattern examples, and cross-reference to root `CLAUDE.md` Issue 3 boundary rule | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Reflection Guard Architecture Rules <!-- phase-key: SK.00.ReflectionGuard -->

> Enforce the platform-wide prohibition on reflection-based generic method invocation (`GetMethod`/`GetMethods` + `MakeGenericMethod` + `Invoke`). P-147 demonstrated that documentation alone cannot prevent this pattern from shipping; this phase converts the documentation convention into a mechanical NetArchTest rule with an explicit, reviewable allow-list for any future justified exception.

### ReflectionGuard — Goal

P-147 fixed `EncryptionRotationService.LoadBatchAsync` which shipped the exact pattern that `06.Persistence/CLAUDE.md` documented as forbidden (expression-tree approach is the gold standard). The violation was justified by an inline comment claiming it was "not a hot path." This phase introduces a platform-wide NetArchTest `ICustomRule` (`NoMakeGenericMethodReflectionPredicate`) that flags any method body containing a `MakeGenericMethod` IL call opcode in production assemblies. Legitimate exceptions — if any ever arise — are handled by a `ReflectionExemptionRegistry` allow-list embedded in `SharedKernel.ArchitectureTests`, requiring a written governance rationale before the exception is registered. The fixed `EncryptionRotationService` from P-147 (which eliminated `MakeGenericMethod` in favour of expression trees) passes the rule without needing an exception. Consistent with how P-145 (WO-023) converted the `02.Caching`/`07.Messaging` mutual-exclusion documentation into `RedisTopologyRules`, this phase converts the reflection prohibition into `ReflectionGuardRules`.

### ReflectionGuard — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/ReflectionGuardRules.cs` — static class housing `NoMakeGenericMethodReflection(Assembly assembly)` factory method
  - `SharedKernel.ArchitectureTests/Predicates/NoMakeGenericMethodReflectionPredicate.cs` — `ICustomRule` walking `MethodDefinition.Body.Instructions` for `MakeGenericMethod` call opcodes; respects `ReflectionExemptionRegistry`
  - `SharedKernel.ArchitectureTests/ReflectionExemptionRegistry.cs` — static allow-list of explicitly approved (type full name, method name) pairs that are exempt from SK0012; each entry must carry an XML doc comment with the governance rationale and the work order that approved it
- Modified files:
  - `00.Governance/CLAUDE.md` — add SK0012 to diagnostic registry; add `ReflectionGuardRules` to architecture test contracts; add `NoMakeGenericMethodReflectionPredicate` predicate; add `ReflectionExemptionRegistry` allow-list mechanism; add implementation rules
  - `00.Governance/state-map.md` — this update
- Deleted files: none
- Note: No Roslyn analyzer is introduced for this rule. The reflection pattern is only detectable post-compile (IL level) — `MakeGenericMethod` is not visible as a simple syntax pattern since `MethodInfo` is returned from runtime calls. Consistent with SK0301–SK0304 and SK0706–SK0707, this is a NetArchTest `ICustomRule` predicate enforced at assembly level.

### ReflectionGuard — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0012 | MakeGenericMethodReflection | Design | Warning | A method body contains a `MakeGenericMethod` IL call opcode and the type is not in `ReflectionExemptionRegistry`; platform-wide scope across all numbered domains |

### ReflectionGuard — Implementation Rules

1. `NoMakeGenericMethodReflectionPredicate` inspects `TypeDefinition.Methods` for each type. For each `MethodDefinition` with a non-null `Body`, it walks `MethodDefinition.Body.Instructions` for any `Instruction` where `OpCode` is `Call` or `Callvirt` and `MethodReference.Name == "MakeGenericMethod"` (exact name match). If found, the predicate checks `ReflectionExemptionRegistry.IsExempt(typeFullName, methodName)` before returning false. Returns false (rule violated) only when the call is found AND the type+method combination is not in the allow-list. Failure message: `"{TypeDefinition.FullName}.{method.Name} calls MakeGenericMethod. Use typed dispatch or expression trees instead. If this is a genuinely justified exception, register the type and method in ReflectionExemptionRegistry with a written governance rationale."` Lives in `Predicates/` folder.
2. `ReflectionExemptionRegistry` is a static class in `SharedKernel.ArchitectureTests/ReflectionExemptionRegistry.cs`. It exposes `IsExempt(string typeFullName, string methodName)` returning `bool`. Internally it maintains a `HashSet<(string, string)>` of approved (typeFullName, methodName) pairs. Each registered entry must carry an XML `<remarks>` doc comment stating: the governance rationale, the work order that approved the exception, and the date approved. The registry ships empty — no exemptions are pre-populated (the fixed P-147 code no longer uses `MakeGenericMethod` and therefore needs no exemption).
3. `ReflectionGuardRules.NoMakeGenericMethodReflection(Assembly assembly)` returns `ConditionList` using `Types.InAssembly(assembly).That().AreNotAbstract().Should().MeetCustomRule(new NoMakeGenericMethodReflectionPredicate())`. The `.AreNotAbstract()` filter excludes compiler-generated abstract helper types. The factory method accepts a single `Assembly`; consumers call it once per production assembly under test.
4. The rule is scoped to production assemblies — test projects must not be passed to `NoMakeGenericMethodReflection`. The consuming architecture test project must reference production assemblies via `typeof(SomeProductionType).Assembly` and must not pass `SharedKernel.ArchitectureTests` itself or any `.Tests` project assembly.
5. `NoMakeGenericMethodReflectionPredicate` reuses the established Mono.Cecil `TypeDefinition` access pattern from `DoesNotContainThrowIlPredicate`. No new NuGet dependency — the existing `Mono.Cecil >= 0.11.5` explicit reference in `SharedKernel.ArchitectureTests` covers this predicate.
6. The `ReflectionExemptionRegistry` allow-list is the ONLY mechanism for authorising a `MakeGenericMethod` call in production code. Inline `#pragma` suppressions, `// ReSharper disable` comments, or `[SuppressMessage]` attributes on the calling method are not valid substitutes — the allow-list requires governance review because the rule is assembly-level (not per-call-site).
7. The motivating incident (P-147 `EncryptionRotationService.LoadBatchAsync`) is documented as the canonical example in `CLAUDE.md` and the `ReflectionExemptionRegistry` header comment to explain why the registry starts empty and why the expression-tree / typed-dispatch alternatives are always preferred.
8. SK0012 severity is `Warning` at introduction, consistent with the established SK diagnostic lifecycle. Escalation to `Error` is gated on confirmation that the `ReflectionExemptionRegistry` allow-list mechanism produces zero false positives across all platform assemblies on the first governance scan.

### ReflectionGuard — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/ReflectionGuardRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: `NoMakeGenericMethodReflection(Assembly)` → `ConditionList` |
| `Predicates/NoMakeGenericMethodReflectionPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: Mono.Cecil IL walk for `MakeGenericMethod` call opcodes; consults `ReflectionExemptionRegistry` before failing |
| `ReflectionExemptionRegistry.cs` | SharedKernel.ArchitectureTests | Create | Static allow-list of approved (typeFullName, methodName) pairs exempt from SK0012; ships empty |

### ReflectionGuard — Acceptance Criteria

- [ ] `NoMakeGenericMethodReflectionPredicate` fires when a method body contains a `MakeGenericMethod` call opcode (fire-path test)
- [ ] The fixed `EncryptionRotationService` from P-147 (which uses expression trees, not `MakeGenericMethod`) passes the rule without any allow-list registration (pass-path test)
- [ ] A type+method registered in `ReflectionExemptionRegistry` is exempt from SK0012 — the predicate returns true for that entry (exemption-path test)
- [ ] `ReflectionExemptionRegistry` ships empty — no pre-populated exemptions
- [ ] Rule documented in `00.Governance/CLAUDE.md` with the P-147 `EncryptionRotationService` incident as the motivating example
- [ ] `dotnet build` and architecture test suite both clean after the rule is added (all existing platform assemblies pass because P-147 already eliminated the only known violation)

### ReflectionGuard — Dependencies

- Requires P-147 (`EncryptionRotationService` refactored to expression trees — eliminating the only known `MakeGenericMethod` violation) to be complete: yes — without P-147 the new rule would immediately fail on the existing `EncryptionRotationService` code, requiring a bootstrap exemption that contradicts the design intent
- Requires `DoesNotContainThrowIlPredicate` (Mono.Cecil `TypeDefinition` access pattern) from `SK.00.GuardPurity` to be complete: yes (already complete — Mono.Cecil access pattern is established)
- Unblocks: CI architecture gate for the platform-wide reflection prohibition

### ReflectionGuard — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref in `SharedKernel.ArchitectureTests` — no change)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new Roslyn analyzer, no change)
- Target framework: `net10.0` (ArchitectureTests)

### ReflectionGuard — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-49 | Define SK0012 `MakeGenericMethodReflection` rule shape: `NoMakeGenericMethodReflectionPredicate` design — IL walk for `MakeGenericMethod` call opcodes in `MethodDefinition.Body.Instructions`; `ReflectionExemptionRegistry` allow-list design (static `HashSet<(string, string)>`, `IsExempt(typeFullName, methodName)`); `ReflectionGuardRules.NoMakeGenericMethodReflection(Assembly)` factory shape returning `ConditionList`; document P-147 motivating incident; define severity lifecycle (Warning → Error) | SharedKernel.ArchitectureTests | `●` |
| C-65 | Implement `NoMakeGenericMethodReflectionPredicate` in `Predicates/` and `ReflectionExemptionRegistry` in root of `SharedKernel.ArchitectureTests/` — predicate: Mono.Cecil IL walk for `MakeGenericMethod` name match on Call/Callvirt opcodes; consults `IsExempt(typeFullName, methodName)` before returning false; failure message names offending type and method; registry: static class with empty `HashSet<(string,string)>` allow-list, `IsExempt` method, XML doc header documenting P-147 incident and expression-tree alternative | SharedKernel.ArchitectureTests | `●` |
| C-66 | Implement `ReflectionGuardRules` static class in `Rules/` — single factory method `NoMakeGenericMethodReflection(Assembly assembly)` → `ConditionList` using `Types.InAssembly(assembly).That().AreNotAbstract().Should().MeetCustomRule(new NoMakeGenericMethodReflectionPredicate())` | SharedKernel.ArchitectureTests | `●` |
| T-113 | Architecture test (fire path): contrived in-memory assembly containing a method that calls `typeof(SomeClass).GetMethod("Foo").MakeGenericMethod(typeof(int)).Invoke(...)` — compiled to IL; assert `NoMakeGenericMethodReflection` fails and failure message names the offending type and method | SharedKernel.ArchitectureTests | `●` |
| T-114 | Architecture test (pass path — expression tree): contrived assembly containing the P-147 fixed pattern (expression tree dispatching equivalent of the rotation method) with no `MakeGenericMethod` call; assert `NoMakeGenericMethodReflection` passes | SharedKernel.ArchitectureTests | `●` |
| T-115 | Architecture test (exemption path): contrived assembly with a `MakeGenericMethod` call; register the offending type+method in `ReflectionExemptionRegistry`; assert `NoMakeGenericMethodReflection` passes with the exemption in place; verify the registry `IsExempt` returns true for that entry and false for unregistered entries | SharedKernel.ArchitectureTests | `●` |
| DO-21 | Document SK0012 `MakeGenericMethodReflection` and `ReflectionGuardRules` in `00.Governance/CLAUDE.md`: rule rationale, P-147 `EncryptionRotationService` incident as motivating example, `ReflectionExemptionRegistry` allow-list usage instructions (how to request an exception, required XML doc format), offending-pattern example, compliant alternatives (typed dispatch, expression trees), failure message content | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Communication Layer Architecture Rules <!-- phase-key: SK.00.CommunicationArchRules -->

> **Trigger:** WO-025 P-159. **Depends on:** P-154 (Rest package), P-155 (Internal package), P-156 (Grpc package), P-157 (GraphQL package) — all communication packages must exist before architecture rules can reference their assemblies.

P-145 (WO-023) demonstrated that documenting the `02.Caching`/`07.Messaging` mutual-exclusion rule was insufficient — the pattern was violated in a package whose own CLAUDE.md documented it as forbidden. The same risk applies here: the `11.Communication` layering rules are clear in the root CLAUDE.md but nothing mechanically prevents a developer from injecting `HttpClient` directly, importing `Grpc.Net.Client` in an application assembly, or registering a raw `FilterInputType<T>`. This phase converts those documentation rules into build-time failures.

### CommunicationArchRules — Goal

Introduce `CommunicationLayeringRules` (NetArchTest static class) and SK0013 `RawHttpClientConstructorInjection` (Roslyn analyzer) to mechanically enforce the five invariants in the `11.Communication` family:

1. `11.Communication.*` packages never reference `02.Caching.*`, `05.Application`, `06.Persistence.*`, or `07.Messaging.*`.
2. `SharedKernel.Communication.Internal` never references `SharedKernel.Communication.Rest`, `.Grpc`, or `.GraphQL` — dependency flows into Internal, not out of it.
3. No production constructor declares a parameter of raw type `HttpClient` (SK0013 Roslyn analyzer); exempt: `DelegatingHandler` subclasses in `SharedKernel.Communication.Rest`.
4. No assembly outside `SharedKernel.Communication.Grpc` inherits from `Grpc.Core.Interceptors.Interceptor` as a base class.
5. No assembly referencing `HotChocolate.Data` inherits from `FilterInputType<T>` or `SortInputType<T>` directly without going through `FilterBase<T>` or `SortBase<T>` from `SharedKernel.Communication.GraphQL` (exemption: `SharedKernel.Communication.GraphQL` itself for defining those bases).

The hardcoded URI guard (rule described in the P-159 spec) remains documentation-enforced — mechanical enforcement is feasible only via a Roslyn analyzer that carries an unacceptable false-positive risk on legitimate URI construction patterns outside typed clients. This decision is recorded in `CLAUDE.md`.

### CommunicationArchRules — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`, `SharedKernel.Analyzers`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/CommunicationLayeringRules.cs` — static class housing all NetArchTest-based Communication rules
  - `SharedKernel.ArchitectureTests/Predicates/NoDirectGrpcInterceptorInheritancePredicate.cs` — ICustomRule: checks TypeDefinition.BaseType chain for `Grpc.Core.Interceptors.Interceptor`; exempt: types in `SharedKernel.Communication.Grpc` namespace
  - `SharedKernel.ArchitectureTests/Predicates/NoDirectHotChocolateFilterSortInheritancePredicate.cs` — ICustomRule: checks TypeDefinition.BaseType chain for `FilterInputType` or `SortInputType` without `FilterBase` or `SortBase` in the chain; exempt: types in `SharedKernel.Communication.GraphQL` namespace
  - `SharedKernel.Analyzers/Analyzers/RawHttpClientConstructorInjectionAnalyzer.cs` — SK0013, `netstandard2.0`, syntax-only
- Modified files:
  - `00.Governance/CLAUDE.md` — add SK0013 to diagnostic registry; add `CommunicationLayeringRules` to architecture test contracts; add two new ICustomRule predicates; add implementation rules; add Changelog entry
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### CommunicationArchRules — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0013 | RawHttpClientConstructorInjection | Usage | Warning | A constructor parameter is typed as `HttpClient` (exact simple name match) in a type that is not a `DelegatingHandler` subclass and not in the `SharedKernel.Communication.Rest` namespace |

### CommunicationArchRules — Implementation Rules

1. `CommunicationLayeringRules.CommunicationPackagesNeverReferencesForbiddenLayers(Assembly assembly)` uses iterative `.Should().NotHaveDependencyOn(term)` calls — one per forbidden namespace term: `"SharedKernel.Caching"`, `"SharedKernel.Application"`, `"SharedKernel.Persistence"`, `"SharedKernel.Messaging"`. The caller supplies one of the four `11.Communication.*` assemblies. The method returns one `ConditionList` per forbidden term — callers must assert each element. This is consistent with the iterative pattern from `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure`.
2. `CommunicationLayeringRules.CommunicationInternalNeverReferencesOtherCommunicationPackages(Assembly internalAssembly)` asserts that `SharedKernel.Communication.Internal` has no dependency on `"SharedKernel.Communication.Rest"`, `"SharedKernel.Communication.Grpc"`, or `"SharedKernel.Communication.GraphQL"`. Three iterative `.Should().NotHaveDependencyOn(term)` calls — returns one `ConditionList` per term. The caller must pass only the `SharedKernel.Communication.Internal` assembly.
3. `CommunicationLayeringRules.NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc(Assembly assembly)` uses `NoDirectGrpcInterceptorInheritancePredicate` (ICustomRule) — see below. Returns `ConditionList`. The caller passes any assembly that is NOT `SharedKernel.Communication.Grpc`; passing `SharedKernel.Communication.Grpc` itself is not useful since the exemption inside the predicate would pass everything unconditionally.
4. `CommunicationLayeringRules.NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL(Assembly assembly)` uses `NoDirectHotChocolateFilterSortInheritancePredicate` (ICustomRule) — see below. Returns `ConditionList`. Caller passes any assembly that references `HotChocolate.Data` but is NOT `SharedKernel.Communication.GraphQL`.
5. `NoDirectGrpcInterceptorInheritancePredicate` — namespace exemption guard (first check): types whose `TypeDefinition.Namespace.StartsWith("SharedKernel.Communication.Grpc")` return true unconditionally. For all other types, walks the `TypeDefinition.BaseType` chain iteratively. At each step checks `TypeReference.Name == "Interceptor"` (exact simple name match) AND `TypeReference.Namespace` contains `"Grpc.Core.Interceptors"` (substring). Fail-open: if `BaseType.Resolve()` returns null at any step, returns true (not in scope / unloaded assembly). Returns false (rule violated) if the chain terminates with a match on `Interceptor` from the `Grpc.Core.Interceptors` namespace. Failure message: `"{TypeDefinition.FullName} inherits from Grpc.Core.Interceptors.Interceptor directly. gRPC interceptor implementations must live in SharedKernel.Communication.Grpc — never in application or domain assemblies."` Lives in `Predicates/` folder.
6. `NoDirectHotChocolateFilterSortInheritancePredicate` — namespace exemption guard (first check): types whose `TypeDefinition.Namespace.StartsWith("SharedKernel.Communication.GraphQL")` return true unconditionally. For all other types, walks the `TypeDefinition.BaseType` chain iteratively. At each step checks `TypeReference.Name` against `{"FilterInputType", "SortInputType"}` (exact simple name match, covers both generic and non-generic IL forms since `FilterInputType<T>` in IL has `TypeReference.Name == "FilterInputType\`1"` — use `StartsWith("FilterInputType")` and `StartsWith("SortInputType")` to cover both). A type passes (returns true) if the BaseType chain contains `FilterBase` or `SortBase` BEFORE reaching `FilterInputType` or `SortInputType` (i.e., it extended the platform wrapper). A type fails if it reaches `FilterInputType` or `SortInputType` in the chain without first passing through `FilterBase` or `SortBase`. Fail-open on null `Resolve()`. Failure message: `"{TypeDefinition.FullName} inherits from {FilterInputType/SortInputType} directly. Use FilterBase<T> or SortBase<T> from SharedKernel.Communication.GraphQL to apply platform naming and exposure conventions."` Lives in `Predicates/` folder.
7. SK0013 `RawHttpClientConstructorInjectionAnalyzer` operates on `ConstructorDeclarationSyntax` nodes. For each constructor parameter list, checks whether any `ParameterSyntax.Type` is a `SimpleNameSyntax` or `IdentifierNameSyntax` whose `Identifier.Text == "HttpClient"` (exact match). Two exemptions applied in order before firing: (a) namespace walk via `SyntaxNode.Parent` checking for any `NamespaceDeclarationSyntax` or `FileScopedNamespaceDeclarationSyntax` whose `Name.ToString().StartsWith("SharedKernel.Communication.Rest")` — same SyntaxNode.Parent walk pattern as SK0001/SK0007/SK0202; (b) base type walk via `ClassDeclarationSyntax.BaseList.Types` checking for any type whose simple name is `"DelegatingHandler"` (exact match) — `DelegatingHandler` subclasses legitimately accept `HttpClient` in their constructors as part of the delegating chain. No `SemanticModel` required. Reports on the `ParameterSyntax` token if neither exemption applies. Severity: Warning. Category: Usage.
8. SK0013 fires globally (no blanket suppression namespace). Per-constructor suppression via `#pragma warning disable SK0013` is permitted only when the injection is legitimately required (document the rationale inline). The `DelegatingHandler` exemption must not be broadened beyond the exact class name check without a governance review.
9. The hardcoded URI guard (no production typed client may assign `BaseAddress` or `Address` from a string literal or `IConfiguration` value directly) remains **documentation-enforced only** — no NetArchTest rule or Roslyn analyzer. Reason: detecting URI assignment to `HttpClient.BaseAddress` or `GrpcChannel` configuration in a constructor or method body requires semantic model type resolution of the assignment target; false positives on legitimate URI construction patterns (tests, startup config helpers, utilities) are too high to justify a platform-wide rule at this time. If a dedicated test harness for typed-client factories is introduced in the future, a targeted rule may be feasible. This decision is recorded here and in `CLAUDE.md`.
10. `NoDirectGrpcInterceptorInheritancePredicate` and `NoDirectHotChocolateFilterSortInheritancePredicate` reuse the established Mono.Cecil `TypeDefinition.BaseType` chain-walk pattern from `SagaStateMustExtendSagaStateBasePredicate`. No new NuGet dependency — `Mono.Cecil >= 0.11.5` already referenced in `SharedKernel.ArchitectureTests` covers both new predicates.
11. `CommunicationLayeringRules` introduces zero new SK diagnostic IDs beyond SK0013. All NetArchTest predicates use the established `NotHaveDependencyOn(term)` / `MeetCustomRule(...)` patterns. No Mono.Cecil is needed for the layering and Internal-isolation rules (pure NetArchTest dependency graph checks).

### CommunicationArchRules — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/CommunicationLayeringRules.cs` | SharedKernel.ArchitectureTests | Create | Static class: `CommunicationPackagesNeverReferencesForbiddenLayers`, `CommunicationInternalNeverReferencesOtherCommunicationPackages`, `NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc`, `NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL` → `ConditionList` |
| `Predicates/NoDirectGrpcInterceptorInheritancePredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: BaseType chain walk for `Grpc.Core.Interceptors.Interceptor`; exempt: `SharedKernel.Communication.Grpc` namespace |
| `Predicates/NoDirectHotChocolateFilterSortInheritancePredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule: BaseType chain walk for `FilterInputType`/`SortInputType` without `FilterBase`/`SortBase` in chain; exempt: `SharedKernel.Communication.GraphQL` namespace |
| `Analyzers/RawHttpClientConstructorInjectionAnalyzer.cs` | SharedKernel.Analyzers | Create | SK0013 Roslyn analyzer; syntax-only; `netstandard2.0`; ConstructorDeclarationSyntax walk; `DelegatingHandler` and `SharedKernel.Communication.Rest` exemptions |

### CommunicationArchRules — Acceptance Criteria

- [ ] NetArchTest rule: `CommunicationPackagesNeverReferencesForbiddenLayers` fires when a `11.Communication.*` assembly references `SharedKernel.Caching`, `SharedKernel.Application`, `SharedKernel.Persistence`, or `SharedKernel.Messaging`
- [ ] NetArchTest rule: `CommunicationInternalNeverReferencesOtherCommunicationPackages` fires when `SharedKernel.Communication.Internal` references `SharedKernel.Communication.Rest`, `.Grpc`, or `.GraphQL`
- [ ] SK0013: fires when a production constructor declares a parameter of type `HttpClient` (not a `DelegatingHandler` subclass, not inside `SharedKernel.Communication.Rest`)
- [ ] SK0013: does NOT fire in `DelegatingHandler` subclasses or inside `SharedKernel.Communication.Rest` namespace
- [ ] NetArchTest rule: `NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc` fires when an assembly outside `SharedKernel.Communication.Grpc` has a type inheriting from `Grpc.Core.Interceptors.Interceptor`
- [ ] NetArchTest rule: `NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL` fires when a type inherits from `FilterInputType<T>` or `SortInputType<T>` directly without going through `FilterBase<T>` or `SortBase<T>`
- [ ] All rules documented in `00.Governance/CLAUDE.md` with motivating principle and exemption mechanism for each
- [ ] `dotnet build` and full architecture test suite clean with 0 violations on current codebase

### CommunicationArchRules — Dependencies

- Requires P-154 (Communication.Rest), P-155 (Communication.Internal), P-156 (Communication.Grpc), P-157 (Communication.GraphQL) to be complete: yes — all four communication packages must exist before their assemblies can be referenced from architecture tests
- Requires `SK.00.ReflectionGuard` (SK0012, Mono.Cecil pattern established) to be complete: yes (already complete)
- Unblocks: CI architecture gate for the entire `11.Communication` capability domain

### CommunicationArchRules — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing explicit ref in `SharedKernel.ArchitectureTests` — no change; required for the two new ICustomRule predicates)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — SK0013 follows the same constraint as all prior SK Roslyn analyzers)
- Target framework: `netstandard2.0` (Analyzers — SK0013), `net10.0` (ArchitectureTests — CommunicationLayeringRules and predicates)

### CommunicationArchRules — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-50 | Define SK0013 `RawHttpClientConstructorInjection` rule shape: trigger (ConstructorDeclarationSyntax parameter type == "HttpClient"), two exemptions (`DelegatingHandler` base class and `SharedKernel.Communication.Rest` namespace), severity (Warning), category (Usage), no SemanticModel required; define `CommunicationLayeringRules` factory method signatures and parameter types; define `NoDirectGrpcInterceptorInheritancePredicate` and `NoDirectHotChocolateFilterSortInheritancePredicate` ICustomRule shapes (BaseType chain walk, fail-open on null Resolve(), namespace exemptions); document hardcoded-URI-guard as documentation-only (feasibility concern) | SharedKernel.Analyzers, SharedKernel.ArchitectureTests | `●` |
| C-67 | Implement `CommunicationLayeringRules` static class in `Rules/`: four factory methods (`CommunicationPackagesNeverReferencesForbiddenLayers`, `CommunicationInternalNeverReferencesOtherCommunicationPackages`, `NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc`, `NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL`) using iterative `.Should().NotHaveDependencyOn(...)` and `.Should().MeetCustomRule(...)` patterns consistent with existing Rules/ classes | SharedKernel.ArchitectureTests | `●` |
| C-68 | Implement `NoDirectGrpcInterceptorInheritancePredicate` in `Predicates/`: ICustomRule, Mono.Cecil BaseType chain walk, exact name `"Interceptor"` + namespace substring `"Grpc.Core.Interceptors"`, namespace exemption `SharedKernel.Communication.Grpc`, fail-open on null Resolve(); failure message names offending type; and implement `NoDirectHotChocolateFilterSortInheritancePredicate` in `Predicates/`: ICustomRule, Mono.Cecil BaseType chain walk, StartsWith `"FilterInputType"`/`"SortInputType"` detection vs. `"FilterBase"`/`"SortBase"` chain check, namespace exemption `SharedKernel.Communication.GraphQL`, fail-open on null Resolve(); failure message names offending type and direct HotChocolate base | SharedKernel.ArchitectureTests | `●` |
| C-69 | Implement SK0013 `RawHttpClientConstructorInjectionAnalyzer` in `Analyzers/`: Roslyn `DiagnosticAnalyzer`, `netstandard2.0`, registers on `ConstructorDeclarationSyntax`, syntax-only check for `HttpClient` parameter type; applies `DelegatingHandler` base class exemption (ClassDeclarationSyntax.BaseList simple name check) and `SharedKernel.Communication.Rest` namespace exemption (SyntaxNode.Parent walk, same pattern as SK0001/SK0007/SK0202); reports on the offending ParameterSyntax; DiagnosticDescriptor with HelpLinkUri, category "Usage", severity Warning | SharedKernel.Analyzers | `●` |
| T-116 | Architecture test (fire path): contrived in-memory assembly whose namespace is `"SomeApp.Caching"` — contains a class referencing `SharedKernel.Caching`; assert `CommunicationPackagesNeverReferencesForbiddenLayers` fails with the forbidden caching dependency in the failure message | SharedKernel.ArchitectureTests | `●` |
| T-117 | Architecture test (pass path): contrived assembly with no forbidden layer references (references only `SharedKernel.Core` and `SharedKernel.Contracts`); assert `CommunicationPackagesNeverReferencesForbiddenLayers` passes | SharedKernel.ArchitectureTests | `●` |
| T-118 | Architecture test (fire path): contrived in-memory assembly named `SharedKernel.Communication.Internal` that references `SharedKernel.Communication.Rest`; assert `CommunicationInternalNeverReferencesOtherCommunicationPackages` fails with the forbidden sibling reference | SharedKernel.ArchitectureTests | `●` |
| T-119 | Architecture test (pass path): contrived `Communication.Internal`-shaped assembly with no references to `.Rest`, `.Grpc`, `.GraphQL`; assert `CommunicationInternalNeverReferencesOtherCommunicationPackages` passes | SharedKernel.ArchitectureTests | `●` |
| T-120 | Architecture test (fire path): contrived assembly containing a class that directly inherits `Grpc.Core.Interceptors.Interceptor` and is NOT in `SharedKernel.Communication.Grpc`; assert `NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc` fails with the offending type name in the failure message | SharedKernel.ArchitectureTests | `●` |
| T-121 | Architecture test (pass path): contrived assembly containing a type that inherits from a platform wrapper (e.g., `GrpcClientInterceptorBase`) rather than `Grpc.Core.Interceptors.Interceptor` directly; assert `NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc` passes | SharedKernel.ArchitectureTests | `●` |
| T-122 | Architecture test (fire path): contrived assembly containing a class that directly inherits `FilterInputType<T>` (HotChocolate) without `FilterBase<T>` in the BaseType chain, and is NOT in `SharedKernel.Communication.GraphQL`; assert `NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL` fails | SharedKernel.ArchitectureTests | `●` |
| T-123 | Architecture test (pass path): contrived assembly containing a class that inherits from `FilterBase<T>` (which in turn inherits `FilterInputType<T>`); assert `NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL` passes because `FilterBase` is in the chain before `FilterInputType` | SharedKernel.ArchitectureTests | `●` |
| T-124 | Analyzer test (fire path — SK0013): class with constructor `public OrderHandler(HttpClient client, IOrderRepository repo)` not in `SharedKernel.Communication.Rest` namespace, not a `DelegatingHandler` subclass; assert SK0013 fires on the `HttpClient` parameter | SharedKernel.Analyzers | `●` |
| T-125 | Analyzer test (pass path — SK0013 DelegatingHandler exemption): class `public class TenantIdDelegatingHandler : DelegatingHandler` with constructor `public TenantIdDelegatingHandler(HttpClient client)` inside `SharedKernel.Communication.Rest`; assert SK0013 does NOT fire | SharedKernel.Analyzers | `●` |
| T-126 | Analyzer test (pass path — SK0013 typed client): class with constructor `public OrderServiceClient(IHttpClientFactory factory)` — no `HttpClient` parameter; assert SK0013 does NOT fire | SharedKernel.Analyzers | `●` |
| DO-22 | Document SK0013 `RawHttpClientConstructorInjection` and `CommunicationLayeringRules` in `00.Governance/CLAUDE.md`: SK0013 trigger, exemptions (DelegatingHandler, SharedKernel.Communication.Rest namespace), motivating principle (IHttpClientFactory lifecycle — connection pooling, DNS refresh, handler lifetime); CommunicationLayeringRules factory method signatures; two ICustomRule predicates with BaseType chain walk details; hardcoded URI guard documented as documentation-only with feasibility rationale | SharedKernel.Analyzers, SharedKernel.ArchitectureTests | `●` |

---

## Phase: Governance: Architecture Rules for WO-026 Communication Quality Improvements <!-- phase-key: SK.00.WO026CommunicationQuality -->

> **Trigger:** WO-026 P-167. **Depends on:** P-163 (Communication.Grpc dead-reference removal), P-165 (ResultEnvelopeExtensions in Contracts), P-166 (PagedResponseType.FromPagedList in GraphQL).

P-163 (WO-026) removed a dead `SharedKernel.Contracts` reference from `SharedKernel.Communication.Grpc`. Without a mechanical rule, this reference can silently re-enter on any future Grpc package PR. This phase adds one NetArchTest rule to lock that boundary permanently and documents the four WO-026 architectural patterns as governance conventions.

### WO026CommunicationQuality — Goal

Lock the WO-026 architectural decisions against regression:

1. `SharedKernel.Communication.Grpc` must never reference `SharedKernel.Contracts` — the Grpc package is a protocol adapter that communicates via Protobuf; the `SharedKernel.Contracts` DTO layer is a cross-service concern that should not flow into gRPC transport code. The reference was added accidentally (dead import) and was removed in P-163; the NetArchTest rule mechanically prevents re-introduction.
2. The four `What Goes Where` patterns established in WO-026 are documented as governance conventions: `ResultEnvelopeExtensions.ToEnvelope()` mapping, `PagedResponseType<T>.FromPagedList(pagedList)`, `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds`, and the inline factory pattern for multiple typed REST clients.
3. The inline `Result<T>` → `Envelope<T>` mapping anti-pattern is documented as a platform violation with a future SK0xxx mechanical enforcement tracker.

### WO026CommunicationQuality — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files: none — one factory method added to the existing `CommunicationLayeringRules.cs`
- Modified files:
  - `SharedKernel.ArchitectureTests/Rules/CommunicationLayeringRules.cs` — add `GrpcNeverReferencesContracts(Assembly grpcAssembly)` factory method
  - `00.Governance/CLAUDE.md` — add `GrpcNeverReferencesContracts` to `CommunicationLayeringRules`; add four WO-026 `What Goes Where` governance conventions; document inline `Result<T>` → `Envelope<T>` mapping as a platform violation; add Changelog entry
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### WO026CommunicationQuality — Diagnostic Registry Changes

No new SK diagnostic IDs. The new rule is a pure NetArchTest assembly-dependency check (the same pattern as all other `CommunicationLayeringRules` factory methods). The future Roslyn analyzer for inline `Result<T>` → `Envelope<T>` mapping is tracked as a backlog item here — no ID assigned yet.

### WO026CommunicationQuality — Implementation Rules

1. `CommunicationLayeringRules.GrpcNeverReferencesContracts(Assembly grpcAssembly)` uses a single `.Should().NotHaveDependencyOn("SharedKernel.Contracts")` call on `Types.InAssembly(grpcAssembly)`. Returns `ConditionList`. The caller passes `typeof(SomeTypeInGrpcPackage).Assembly` — no assembly path is hard-coded. The term `"SharedKernel.Contracts"` is the exact namespace of the `SharedKernel.Contracts` package; NetArchTest's `NotHaveDependencyOn` matches it as a namespace prefix (StartsWith) consistent with the established matching contract documented in the Implementation Rules section.
2. The factory method lives in the existing `CommunicationLayeringRules` static class alongside the four methods introduced in P-159. No new static class is created.
3. Failure message: `"SharedKernel.Communication.Grpc has a dependency on SharedKernel.Contracts. The gRPC package is a protocol adapter — cross-service DTO types must not flow into gRPC transport code. Reference was introduced accidentally in P-163 and is forbidden by this rule (WO-026)."` The failure is reported via `ConditionList.GetResult()` using the NetArchTest standard failure format.
4. The permitted exemption list for `CommunicationLayeringRules` does NOT include `SharedKernel.Contracts` for `SharedKernel.Communication.Grpc` — this exemption must never be added. If a future Grpc package genuinely needs to reference a type from `SharedKernel.Contracts`, a governance review must be opened and the rule must be explicitly revised with the rationale documented here before any exemption is applied.
5. Four WO-026 `What Goes Where` governance conventions are documented in `00.Governance/CLAUDE.md` (not in the root CLAUDE.md — the root brain is updated separately via `/sync-brain`):
   - `Result<T>` → `Envelope<T>` boundary mapping extension: `04.Contracts/SharedKernel.Contracts` via `ResultEnvelopeExtensions.ToEnvelope()` / `ToResult()`. These are pure static extension methods on `Result<T>` and `Envelope<T>`; they introduce no cross-layer dependency because `SharedKernel.Contracts` already references `SharedKernel.Primitives` (where `Result<T>` lives).
   - `PagedResponseType<T>` from a `PagedList<T>` source: `11.Communication.GraphQL` via `PagedResponseType<T>.FromPagedList(pagedList)`. The `PagedResponseType<T>` type lives in `SharedKernel.Communication.GraphQL` and is the GraphQL-transport-safe projection of the internal `PagedList<T>` shape from `04.Contracts`.
   - Endpoint resolution cache TTL: `11.Communication.Internal` via `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds`. All endpoint cache TTL configuration must go through this options type — never through a raw `TimeSpan` or `int` field on a typed client.
   - Multiple typed REST clients sharing service discovery: `11.Communication.Rest` via the inline factory pattern (pass the resolved base address directly to each `AddRestClient<TClient>()` call using `IServiceEndpointResolver` injected into the DI callback). The `ServiceDiscoveryResolvingHandler` is a framework-internal type and must NOT be registered directly as a `DelegatingHandler` from consuming code.
6. The inline `Result<T>` → `Envelope<T>` anti-pattern is documented as a platform violation: writing `if (result.IsSuccess) Envelope<T>.Ok(result.Value) else Envelope<T>.Fail(result.Error)` at service controller/endpoint boundaries is forbidden — always use `result.ToEnvelope()` from `SharedKernel.Contracts.Mapping`. A future Roslyn analyzer (SK0014 or next available ID in the 11xx Communication block) is tracked in the backlog; no ID is assigned until that phase is planned.

### WO026CommunicationQuality — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/CommunicationLayeringRules.cs` | SharedKernel.ArchitectureTests | Modify | Add `GrpcNeverReferencesContracts(Assembly grpcAssembly)` → `ConditionList` factory method |

### WO026CommunicationQuality — Acceptance Criteria

- [ ] NetArchTest rule: `CommunicationLayeringRules.GrpcNeverReferencesContracts` fires when `SharedKernel.Communication.Grpc` assembly has any dependency on `SharedKernel.Contracts`; rule documented with P-163 rationale
- [ ] NetArchTest rule pass path: clean `SharedKernel.Communication.Grpc` assembly (post P-163) passes the rule with no violations
- [ ] Full architecture test suite passes with 0 violations on the WO-026 codebase
- [ ] `00.Governance/CLAUDE.md` updated with `GrpcNeverReferencesContracts` factory method in `CommunicationLayeringRules`
- [ ] Four WO-026 `What Goes Where` governance conventions documented in `00.Governance/CLAUDE.md`
- [ ] Inline `Result<T>` → `Envelope<T>` mapping platform violation documented in `00.Governance/CLAUDE.md` with future SK0xxx backlog note
- [ ] `00.Governance/CLAUDE.md` changelog entry added for WO-026

### WO026CommunicationQuality — Dependencies

- Requires P-163 (`SharedKernel.Communication.Grpc` dead-reference removal — the rule fires against the actual assembly, so the assembly must be clean before the test is added): yes — without P-163 the new rule would immediately fail on the existing codebase, requiring a bootstrap workaround
- Requires P-165 (`ResultEnvelopeExtensions.ToEnvelope()` / `ToResult()` in `SharedKernel.Contracts`): yes — the `What Goes Where` convention documents these extension methods; they must exist before the convention is accurate
- Requires P-166 (`PagedResponseType<T>.FromPagedList(pagedList)` in `SharedKernel.Communication.GraphQL`): yes — same as P-165
- Requires `SK.00.CommunicationArchRules` (P-159) to be complete: yes — `GrpcNeverReferencesContracts` is added to the existing `CommunicationLayeringRules` class
- Unblocks: WO-026 governance closeout; CI architecture gate confirming Grpc/Contracts boundary

### WO026CommunicationQuality — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new Roslyn analyzer in this phase)
- Target framework: `net10.0` (ArchitectureTests only)

### WO026CommunicationQuality — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-51 | Define `CommunicationLayeringRules.GrpcNeverReferencesContracts(Assembly)` factory method shape: single `.Should().NotHaveDependencyOn("SharedKernel.Contracts")` call; failure message naming P-163 rationale; no exemption permitted; document four WO-026 `What Goes Where` governance conventions and inline mapping anti-pattern in `00.Governance/CLAUDE.md` | SharedKernel.ArchitectureTests | `●` |
| C-70 | Implement `CommunicationLayeringRules.GrpcNeverReferencesContracts(Assembly grpcAssembly)` in `Rules/CommunicationLayeringRules.cs` — single `Types.InAssembly(grpcAssembly).Should().NotHaveDependencyOn("SharedKernel.Contracts")` returning `ConditionList`; add XML doc comment with P-163 rationale and WO-026 context | SharedKernel.ArchitectureTests | `●` |
| T-127 | Architecture test (fire path): contrived in-memory assembly shaped as `SharedKernel.Communication.Grpc` that contains a type referencing a type in `SharedKernel.Contracts`; assert `GrpcNeverReferencesContracts` fails and failure message names the dependency | SharedKernel.ArchitectureTests | `●` |
| T-128 | Architecture test (pass path): pass the real post-P-163 `SharedKernel.Communication.Grpc` assembly (or a clean contrived fixture with no Contracts reference); assert `GrpcNeverReferencesContracts` passes | SharedKernel.ArchitectureTests | `●` |
| DO-23 | Document `GrpcNeverReferencesContracts`, the four WO-026 `What Goes Where` governance conventions, and the inline `Result<T>` → `Envelope<T>` platform violation (with future SK0xxx backlog note) in `00.Governance/CLAUDE.md`; update Changelog entry | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Governance: ServiceDefaults Liveness/Readiness and Composition-Root Layering Rules <!-- phase-key: SK.00.ServiceDefaultsGovernance -->

> **Trigger:** WO-027 P-173. **Depends on:** P-170 (13.ServiceDefaults Core phase — not yet implemented; this phase is designed now but its assembly-dependent test fixtures cannot run against the real `SharedKernel.ServiceDefaults` assembly until P-170 ships).

`13.ServiceDefaults/CLAUDE.md` already states two hard rules as mechanically enforced: (1) every `IHealthCheck` carries either `"live"` or `"ready"` tags, never both, and dependency-specific checks always carry `"ready"` and never `"live"`; (2) `13.ServiceDefaults`/`SharedKernel.MultiTenancy` are the only production assemblies permitted to reference concrete provider packages. Rule (2) was only ever mechanically true for `02.Caching` providers (`SharedKernelLayeringRules` Rule 1, P-009, WO-003) — written before `13.ServiceDefaults` existed. This phase closes both gaps before `13.ServiceDefaults` ships its first package, mirroring the precedent set by `CachingAbstractionRules` (P-009) and `RedisTopologyRules` (P-145).

### SK.00.ServiceDefaultsGovernance — Goal

Add two architecture-enforcement rule groups to `SharedKernel.ArchitectureTests`:

1. **`HealthCheckTagIntegrityRules`** — a Mono.Cecil-based `ICustomRule` group that inspects `HealthCheckRegistration` construction sites (or, if call-site inspection proves infeasible, the tag arrays passed to `Add*HealthCheck`/`Add*ReadinessCheck` extension methods declared in `SharedKernel.ServiceDefaults`) and fails when: (a) a registration carries both `"live"` and `"ready"` simultaneously; (b) a registration for a named dependency check (Redis/database/RabbitMQ/Azure Service Bus/cache) does not carry `"ready"`, or carries `"live"`.
2. **`CompositionRootExclusivityRules`** — extends the `SharedKernelLayeringRules` family (mirroring `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching`) so that no production assembly other than `SharedKernel.ServiceDefaults`, `SharedKernel.MultiTenancy`, and the concrete provider packages themselves may reference `SharedKernel.Persistence.EfCore`, `SharedKernel.Persistence.PostgreSQL`, `SharedKernel.Persistence.Dapper`, `SharedKernel.Messaging.MassTransit`, or `SharedKernel.Security.Oidc`.

No new SK Roslyn diagnostic IDs in this phase unless the health-check tag inspection proves expressible as syntax-only (see Implementation Rule 1 below for the decision gate) — the default design path is NetArchTest `ICustomRule`, consistent with every other assembly-level governance rule shipped in this domain (`RedisTopologyRules`, `CachingAbstractionRules`, `CommunicationLayeringRules`).

### SK.00.ServiceDefaultsGovernance — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/HealthCheckTagIntegrityRules.cs`
  - `SharedKernel.ArchitectureTests/Predicates/NoConflictingLivenessReadinessTagsPredicate.cs`
  - `SharedKernel.ArchitectureTests/Predicates/DependencyHealthChecksCarryReadyNotLivePredicate.cs`
  - `SharedKernel.ArchitectureTests/Rules/CompositionRootExclusivityRules.cs`
- Modified files:
  - `00.Governance/CLAUDE.md` — add `HealthCheckTagIntegrityRules` and `CompositionRootExclusivityRules` to Architecture Test Contracts; add implementation rules; add Changelog entry
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### SK.00.ServiceDefaultsGovernance — Diagnostic Registry Changes

No new SK diagnostic IDs are assigned in this phase. Both rule groups are NetArchTest `ICustomRule` / `ConditionList` predicates operating at the assembly level (post-compile), consistent with `RedisTopologyRules`, `CachingAbstractionRules`, `ReflectionGuardRules`, and `EncryptionPatternGuardRules`. If a future need arises for a syntax-only Roslyn complement (e.g., catching a malformed tag array literal at edit time), the next available sequential-block ID is **SK0014** — not assigned here; tracked as a backlog note only (see Implementation Rule 1).

### SK.00.ServiceDefaultsGovernance — Implementation Rules

1. **Health check tag inspection approach (decision gate).** `HealthCheckRegistration` tags are supplied as an `IEnumerable<string>` constructor argument or via the `AddCheck(...).WithTags(...)` chain — both are *runtime values*, not compile-time-resolvable constants, in the general case. However, every dependency-specific extension shipped by `SharedKernel.ServiceDefaults` itself (`AddRedisHealthCheck`, `AddDatabaseReadinessCheck<TContext>`, etc.) is expected to pass a **literal string array** (e.g., `new[] { "ready" }`) at its own call site inside the `SharedKernel.ServiceDefaults` assembly — this is the only place the rule can mechanically inspect, and it is also the only place that needs inspecting, because these extensions are the platform's sole sanctioned health-check registration surface (consuming services call the extension, not `AddCheck` directly). The predicate therefore scopes to `MethodDefinition` bodies inside `SharedKernel.ServiceDefaults` whose name matches `Add*HealthCheck` or `Add*ReadinessCheck` (prefix/suffix match on `MethodDefinition.Name`), walks `Ldstr` IL opcodes within that method body to collect string literals passed into any `Newarr`/array-initializer or `WithTags`/tag-parameter call, and evaluates the literal set against the two checks below. This is an IL-literal-collection technique, not a full data-flow analysis — if a tag value is ever computed dynamically (e.g., from configuration) inside one of these extensions, the rule will not see it and the gate becomes advisory only for that call site; this limitation must be documented in the rule's XML doc and in this CLAUDE.md entry once implemented.
2. `HealthCheckTagIntegrityRules.NoConflictingLivenessReadinessTags(Assembly serviceDefaultsAssembly)` → `ConditionList`. Uses `NoConflictingLivenessReadinessTagsPredicate` (`ICustomRule`). Scope: methods in the supplied assembly whose name starts with `"Add"` and ends with `"HealthCheck"` or `"ReadinessCheck"`. Fails if the collected literal tag set for a single registration call contains both `"live"` and `"ready"`.
3. `HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive(Assembly serviceDefaultsAssembly, params string[] dependencyCheckMethodNamePrefixes)` → `ConditionList`. Uses `DependencyHealthChecksCarryReadyNotLivePredicate` (`ICustomRule`). The caller supplies the recognized dependency-check method-name prefixes (e.g., `"AddRedis"`, `"AddDatabase"`, `"AddRabbitMq"`, `"AddAzureServiceBus"`, `"AddCache"`) rather than the rule hard-coding them — this keeps the predicate generic and lets the consuming test project enumerate the actual extension method names shipped by `SharedKernel.ServiceDefaults` once P-170 lands, instead of the governance layer guessing at names that don't exist yet. Fails if a matching method's collected literal tag set does not contain `"ready"`, or does contain `"live"`.
4. `CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders(params Assembly[] assembliesUnderTest)` → `ConditionList[]`. Mirrors `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching` exactly: pure NetArchTest `.Should().NotHaveDependencyOn(term)` checks, one `ConditionList` per forbidden term, no Mono.Cecil. Forbidden terms: `"SharedKernel.Persistence.EfCore"`, `"SharedKernel.Persistence.PostgreSQL"`, `"SharedKernel.Persistence.Dapper"`, `"SharedKernel.Messaging.MassTransit"`, `"SharedKernel.Security.Oidc"`. The caller supplies the assemblies to check — must NOT include `SharedKernel.ServiceDefaults`, `SharedKernel.MultiTenancy`, or any of the five concrete provider packages themselves (self-reference exclusion, same discipline as `RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther`). Typical caller usage: pass `05.Application`, `03.Domain`, `04.Contracts`, `11.Communication.*`, `12.Security.Abstractions` assemblies — i.e., every production assembly that is NOT the composition root and NOT a provider package.
5. Composition-root exemption list (assemblies that MAY reference the five concrete provider packages): `SharedKernel.ServiceDefaults`, `SharedKernel.MultiTenancy`, and each provider package referencing itself trivially. This exemption list must be kept in `00.Governance/CLAUDE.md` alongside the rule, in the same format as the `CachingAbstractionRules` and `RedisTopologyRules` exemption lists. Any additional exemption must be documented there before being applied in code.
6. Both new predicate classes reuse the established Mono.Cecil `TypeDefinition`/`MethodDefinition` access pattern from `NoMakeGenericMethodReflectionPredicate` (IL instruction walk) — no new NuGet dependency; `Mono.Cecil >= 0.11.5` already referenced in `SharedKernel.ArchitectureTests`.
7. `CompositionRootExclusivityRules` introduces no Mono.Cecil dependency at all — it is pure `NetArchTest.eNt` dependency-graph checking, identical in style to `SharedKernelLayeringRules.CachingReferencesOnlyCore` and `CachingAbstractionRules`.
8. Both rule groups must be assembly-parameterized (`Assembly` / `params Assembly[]`) with no hard-coded assembly paths — required because `SharedKernel.ServiceDefaults` and `SharedKernel.MultiTenancy` do not exist as buildable assemblies until P-170 lands. Until then, the architecture-test project for this phase must use contrived in-memory fixture assemblies (same `CSharpCompilation` + `MetadataReference.CreateFromImage` technique documented for `RedisTopologyRulesTests`), not the real packages. The phase-implementer must re-run the suite against the real `SharedKernel.ServiceDefaults` assembly once P-170 ships and confirm no fixture-vs-reality drift — record that confirmation in a future Changelog entry, not in this one.
9. Failure messages must name the offending assembly/type and the specific forbidden term or tag conflict — consistent with every existing predicate's failure-message discipline in this domain.

### SK.00.ServiceDefaultsGovernance — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/HealthCheckTagIntegrityRules.cs` | SharedKernel.ArchitectureTests | Create | Two factory methods: `NoConflictingLivenessReadinessTags`, `DependencyHealthChecksCarryReadyNotLive` |
| `Predicates/NoConflictingLivenessReadinessTagsPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule — IL `Ldstr` literal collection scoped to `Add*HealthCheck`/`Add*ReadinessCheck` methods; fails on simultaneous `"live"`+`"ready"` |
| `Predicates/DependencyHealthChecksCarryReadyNotLivePredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule — same IL literal collection, scoped further to caller-supplied dependency-check method-name prefixes; fails when `"ready"` absent or `"live"` present |
| `Rules/CompositionRootExclusivityRules.cs` | SharedKernel.ArchitectureTests | Create | `OnlyAllowedAssembliesMayReferenceConcreteProviders(params Assembly[])` → `ConditionList[]`; mirrors `CachingAbstractionRules` pattern for Persistence/Messaging/Security providers |

### SK.00.ServiceDefaultsGovernance — Acceptance Criteria

- [ ] `HealthCheckTagIntegrityRules.NoConflictingLivenessReadinessTags` fails against a contrived fixture method that passes both `"live"` and `"ready"` in the same registration's tag literal set
- [ ] `HealthCheckTagIntegrityRules.NoConflictingLivenessReadinessTags` passes against a contrived fixture where every registration carries exactly one of `"live"`/`"ready"`
- [ ] `HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive` fails against a contrived `AddRedisHealthCheck`-shaped fixture method missing the `"ready"` tag
- [ ] `HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive` fails against a contrived dependency-check fixture method carrying `"live"`
- [ ] `HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive` passes against a contrived dependency-check fixture method carrying only `"ready"`
- [ ] `CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders` fails when a non-exempt contrived assembly references any of the five forbidden provider terms
- [ ] `CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders` passes when only exempt assemblies (`SharedKernel.ServiceDefaults`-shaped, `SharedKernel.MultiTenancy`-shaped fixtures) are scanned
- [ ] Both rules documented in `00.Governance/CLAUDE.md` with rationale and the composition-root exemption list
- [ ] Full governance architecture test suite passes with both new rule groups included (fixture-based; real-assembly re-verification deferred to post-P-170 follow-up, tracked via Dependencies section below)

### SK.00.ServiceDefaultsGovernance — Dependencies

- Requires P-170 (`13.ServiceDefaults` Core phase) to be complete for **real-assembly verification**: no for design/implementation of the rule predicates themselves (they are written and tested against contrived in-memory fixtures per Implementation Rule 8); yes for the final confirmation pass against the actual `SharedKernel.ServiceDefaults` assembly, which must be tracked as a follow-up task once P-170 ships (not blocking this phase's completion).
- Requires `SK.00.CachingEnforcement` (P-009) pattern precedent: yes — `CompositionRootExclusivityRules` is a direct structural mirror of `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching`.
- Unblocks: mechanical enforcement of the composition-root exception claimed in `13.ServiceDefaults/CLAUDE.md`; closes the same category of aspirational-but-unenforced-rule gap previously closed for Redis topology (P-145).

### SK.00.ServiceDefaultsGovernance — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing pin — no change; both new predicates reuse the existing reference)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new Roslyn analyzer in this phase)
- Target framework: `net10.0` (ArchitectureTests only)

### SK.00.ServiceDefaultsGovernance — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-52 | Define `HealthCheckTagIntegrityRules` factory method signatures (`NoConflictingLivenessReadinessTags(Assembly)`, `DependencyHealthChecksCarryReadyNotLive(Assembly, params string[])`); define `NoConflictingLivenessReadinessTagsPredicate` and `DependencyHealthChecksCarryReadyNotLivePredicate` ICustomRule shapes (IL `Ldstr` literal collection scoped to `Add*HealthCheck`/`Add*ReadinessCheck` method names); define `CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders(params Assembly[])` mirroring `CachingAbstractionRules`; document the data-flow limitation (literal-only tag detection) | SharedKernel.ArchitectureTests | `●` |
| C-71 | Implement `NoConflictingLivenessReadinessTagsPredicate` in `Predicates/`: ICustomRule, Mono.Cecil method-name scope filter (`Add*HealthCheck`/`Add*ReadinessCheck`), `Ldstr` IL walk collecting string literals per registration call, fails on `{"live","ready"}` subset match | SharedKernel.ArchitectureTests | `●` |
| C-72 | Implement `DependencyHealthChecksCarryReadyNotLivePredicate` in `Predicates/`: ICustomRule, same IL literal collection technique scoped further by caller-supplied method-name-prefix list, fails when `"ready"` absent or `"live"` present | SharedKernel.ArchitectureTests | `●` |
| C-73 | Implement `HealthCheckTagIntegrityRules` static class in `Rules/`: two factory methods wiring the two predicates above into `ConditionList` via `.Should().MeetCustomRule(...)` | SharedKernel.ArchitectureTests | `●` |
| C-74 | Implement `CompositionRootExclusivityRules` static class in `Rules/`: `OnlyAllowedAssembliesMayReferenceConcreteProviders(params Assembly[])` → `ConditionList[]`, one element per forbidden term (`SharedKernel.Persistence.EfCore`, `.PostgreSQL`, `.Dapper`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Security.Oidc`), iterative `.Should().NotHaveDependencyOn(term)` pattern mirroring `CachingAbstractionRules` | SharedKernel.ArchitectureTests | `●` |
| T-129 | Architecture test (fire path): contrived fixture method shaped as `AddDatabaseReadinessCheck` whose body pushes both `"live"` and `"ready"` string literals into the tag array; assert `NoConflictingLivenessReadinessTags` fails naming the offending method | SharedKernel.ArchitectureTests | `●` |
| T-130 | Architecture test (pass path): contrived fixture assembly where every `Add*HealthCheck`/`Add*ReadinessCheck` method carries exactly one of `"live"`/`"ready"`; assert `NoConflictingLivenessReadinessTags` passes | SharedKernel.ArchitectureTests | `●` |
| T-131 | Architecture test (fire path): contrived `AddRedisHealthCheck`-shaped fixture method whose tag literal set is `{"ready"}` is correctly recognized as passing, then a second fixture `AddRabbitMqHealthCheck` whose tag set is `{}` (no `"ready"`) is asserted to fail `DependencyHealthChecksCarryReadyNotLive` | SharedKernel.ArchitectureTests | `●` |
| T-132 | Architecture test (fire path): contrived `AddAzureServiceBusHealthCheck`-shaped fixture method whose tag literal set is `{"live"}`; assert `DependencyHealthChecksCarryReadyNotLive` fails naming the offending method and the forbidden `"live"` tag | SharedKernel.ArchitectureTests | `●` |
| T-133 | Architecture test (pass path): contrived `AddCacheHealthCheck`-shaped fixture method whose tag literal set is `{"ready"}` only; assert `DependencyHealthChecksCarryReadyNotLive` passes | SharedKernel.ArchitectureTests | `●` |
| T-134 | Architecture test (fire path): contrived non-exempt assembly (shaped as `05.Application`) containing a type that references `SharedKernel.Persistence.EfCore`; assert `OnlyAllowedAssembliesMayReferenceConcreteProviders` fails on the `SharedKernel.Persistence.EfCore` element with the offending type named | SharedKernel.ArchitectureTests | `●` |
| T-135 | Architecture test (fire path): contrived non-exempt assembly containing a type that references `SharedKernel.Messaging.MassTransit`; assert the corresponding `ConditionList` element fails | SharedKernel.ArchitectureTests | `●` |
| T-136 | Architecture test (fire path): contrived non-exempt assembly containing a type that references `SharedKernel.Security.Oidc`; assert the corresponding `ConditionList` element fails | SharedKernel.ArchitectureTests | `●` |
| T-137 | Architecture test (pass path): contrived fixture assemblies shaped as `SharedKernel.ServiceDefaults` and `SharedKernel.MultiTenancy` that DO reference the five forbidden provider terms; assert every `ConditionList` element in the returned array passes (composition-root exemption holds) | SharedKernel.ArchitectureTests | `●` |
| T-138 | Architecture test (pass path): contrived non-exempt assembly referencing none of the five forbidden terms (only `SharedKernel.Core`/`SharedKernel.Contracts`); assert every element of the returned `ConditionList[]` passes | SharedKernel.ArchitectureTests | `●` |
| DO-24 | Document `HealthCheckTagIntegrityRules` (both predicates, IL-literal-collection limitation) and `CompositionRootExclusivityRules` (forbidden-term list, composition-root exemption list) in `00.Governance/CLAUDE.md`; cross-reference `13.ServiceDefaults/CLAUDE.md`'s existing claim and confirm wording now matches actual mechanical coverage; add Changelog entry; add a follow-up note for the post-P-170 real-assembly re-verification pass | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Governance: Architecture Rule Banning Bare Health-Check String Literals Where a Constants Class Exists <!-- phase-key: SK.00.HealthCheckConstantsGuard -->

> **Trigger:** WO-028 P-178. **Depends on:** P-177 (13.ServiceDefaults — introduces `HealthCheckTags`/`HealthCheckNames` constants classes and the five sibling files that should have used them but didn't). This phase is additive to `SK.00.ServiceDefaultsGovernance` (P-173) — `HealthCheckTagIntegrityRules` enforces *tag mutual-exclusivity semantics* ("live" vs "ready"); this phase enforces a *source-discipline* concern (bare literals vs. named constants) that is orthogonal and must never be merged into or confused with the P-173 rule group.

P-177's audit found two generations of the same mistake in `13.ServiceDefaults`: `HealthCheckTags` was built correctly as a constants class, but five sibling files kept hardcoding default health-check *names* as bare string literals instead of extending the same discipline. Nothing mechanically caught the inconsistency. This phase closes that gap with a general-purpose rule shape — not hardcoded to `HealthCheckTags`/`HealthCheckNames` by name — so the same predicate generalizes to any future domain that introduces its own well-known-string constants class.

### SK.00.HealthCheckConstantsGuard — Goal

Add `HealthCheckConstantsUsageRules` to `SharedKernel.ArchitectureTests`: a NetArchTest `ICustomRule` group that fails when a method body in the supplied assembly passes a bare string literal as an argument to a recognized health-check registration API (`IHealthChecksBuilder.Add`, `.AddCheck`, `HealthCheckRegistration` constructor, or a tag-array/tag-parameter argument at one of those call sites) **while a sibling "constants class"** — defined generically as any `public static` (or `internal static`) class in the same assembly whose member fields are all `const string` or `static readonly string` — **already exists in that same assembly**. The rule does not know or care what the constants class is named; it only detects the *shape* (an all-string-constants static class) and the *coexistence* of that shape with a bare-literal call site for a health-check API in the same assembly. This makes the rule reusable for any future domain that introduces its own well-known-string constants class guarding a registration API of the same general shape.

This is a NetArchTest `ICustomRule` / Mono.Cecil predicate, consistent with every other rule shipped in this domain (`RedisTopologyRules`, `HealthCheckTagIntegrityRules`, `ReflectionGuardRules`). No new SK Roslyn diagnostic ID is required for the default detection path; a documented decision gate evaluates whether a syntax-only Roslyn complement is feasible (see Implementation Rule 1).

### SK.00.HealthCheckConstantsGuard — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/HealthCheckConstantsUsageRules.cs`
  - `SharedKernel.ArchitectureTests/Predicates/StringConstantsClassDetector.cs` (shared helper — detects the "all-string-constants static class" shape; not itself an `ICustomRule`)
  - `SharedKernel.ArchitectureTests/Predicates/NoBareHealthCheckLiteralWhereConstantsExistPredicate.cs`
- Modified files:
  - `00.Governance/CLAUDE.md` — add `HealthCheckConstantsUsageRules` to Architecture Test Contracts; add implementation rules; add Changelog entry; cross-reference `HealthCheckTagIntegrityRules` (P-173) to clarify the two rule groups are additive, not overlapping
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### SK.00.HealthCheckConstantsGuard — Diagnostic Registry Changes

No new SK diagnostic ID is assigned in this phase. The rule is implemented as a NetArchTest `ICustomRule` / `ConditionList` predicate, operating at the assembly level (post-compile) — consistent with `HealthCheckTagIntegrityRules`, `RedisTopologyRules`, `ReflectionGuardRules`. If a future syntax-only Roslyn complement is justified (see Implementation Rule 1 decision gate), the next available sequential-block ID is **SK0014** — not assigned here; tracked as a backlog note only.

### SK.00.HealthCheckConstantsGuard — Implementation Rules

1. **Detection-surface decision gate.** A fully general "is this string literal duplicating a value already exposed by a constants class" check requires either (a) IL inspection comparing literal values against the constants class's actual field *values* (catches duplication even without a naming hint), or (b) a much weaker structural heuristic that only checks "does a string-constants class exist in this assembly at all" without value comparison. This phase adopts (a) as the default: the predicate resolves every `const string` / `static readonly string` field's literal value on every detected constants class in the assembly, then flags any bare string literal at a health-check registration call site whose value exactly matches one of those resolved constant values. This avoids false positives on unrelated string literals (e.g., a genuinely one-off check name with no constant equivalent) while still catching the exact P-177 incident shape (a literal that duplicates an existing constant). A pure "any literal + any constants class coexist" heuristic (shape-only, no value comparison) is rejected as too noisy — it would fire on every call site in an assembly that happens to contain any string-constants class anywhere, regardless of relevance.
2. **Constants-class shape detection (`StringConstantsClassDetector`).** A type qualifies as a "string constants class" if: `TypeDefinition.IsAbstract && TypeDefinition.IsSealed` (the C# `static class` IL shape) AND it declares at least one field, AND every field on the type is either `IsLiteral` with `FieldType.FullName == "System.String"` (a `const string`) or `IsInitOnly && IsStatic` with `FieldType.FullName == "System.String"` (a `static readonly string`). Mixed-type constants classes (containing non-string constants alongside string constants) still qualify — only the string-typed fields contribute literal values to the comparison set; non-string fields are ignored, not disqualifying. This is a reusable helper class, not itself an `ICustomRule` — `HealthCheckConstantsUsageRules` and any future similarly-shaped rule may call it directly.
3. **Health-check API call-site detection.** Recognized call sites, matched by `MethodReference.Name` plus a declaring-type/namespace check to avoid false positives on unrelated `Add`/`AddCheck` methods elsewhere in the platform:
   - `MethodReference.Name == "Add"` where `MethodReference.DeclaringType.Name` is `"IHealthChecksBuilder"` or `"HealthChecksBuilderAddCheckExtensions"` (the actual Microsoft.Extensions.Diagnostics.HealthChecks extension-method host type — confirm exact type name during implementation; record the confirmed name in `00.Governance/CLAUDE.md` once verified)
   - `MethodReference.Name == "AddCheck"` with the same declaring-type scoping
   - `MethodReference.Name == ".ctor"` where `MethodReference.DeclaringType.Name == "HealthCheckRegistration"`
   For each matched call, walks the immediate argument-producing IL instructions (the established `Ldstr`-literal-collection technique from `HealthCheckTagIntegrityRules`) to collect every string literal feeding the call — covering both the check-name argument and any tag-array/tag-parameter argument in the same call.
4. `HealthCheckConstantsUsageRules.NoBareHealthCheckLiteralWhereConstantsExist(Assembly assembly)` → `ConditionList`. Uses `NoBareHealthCheckLiteralWhereConstantsExistPredicate` (`ICustomRule`). Algorithm: (a) run `StringConstantsClassDetector` once across the supplied assembly to build the set of resolved string constant values; if the set is empty, the rule passes unconditionally for that assembly (no constants class exists yet — nothing to enforce, matching the P-177 "before" state where `HealthCheckTags` did not yet exist); (b) for each recognized health-check call site (Implementation Rule 3), collect its literal string arguments; (c) fail if any collected literal exactly matches a value in the resolved constants set. Failure message names the offending method, the literal value, and the constants class + field name that already exposes that value — giving the developer the exact fix (replace the literal with `ConstantsClassName.FieldName`).
5. **Generality requirement.** Neither the predicate nor the rule factory method may reference `"HealthCheckTags"`, `"HealthCheckNames"`, or any other concrete constants-class name as a string literal anywhere in the implementation. The only domain-specific knowledge baked into this phase is the *call-site* detection (Implementation Rule 3) — the constants-class detection (Implementation Rule 2) and the literal-matching algorithm (Implementation Rule 4) are fully generic and would work unmodified if pointed at a differently-named constants class in a different assembly. This is the acceptance-critical distinction from `HealthCheckTagIntegrityRules`, which is intentionally scoped narrowly to `"live"`/`"ready"` tag semantics.
6. **Regression fixtures required.** The test fixture for the fire-path case must reproduce the exact pre-P-177 shape: a `HealthCheckTags`-equivalent constants class (any name) coexisting with a sibling extension method that passes a bare literal duplicating one of its values. The pass-path fixture must reproduce the post-P-177 shape: the same constants class, with the sibling method referencing the constant by field access (`HealthCheckTags.Live`) instead of a literal — Mono.Cecil sees a `Ldsfld`/field-reference IL instruction at that argument position, not an `Ldstr` literal, so it is never collected by the literal walk and the rule passes.
7. Reuses the established `Ldstr` IL-literal-collection technique from `NoConflictingLivenessReadinessTagsPredicate`/`DependencyHealthChecksCarryReadyNotLivePredicate` (P-173) for the call-site literal collection — no new IL-walking technique is introduced. `StringConstantsClassDetector` is the one new technique in this phase: a `TypeDefinition.Fields`-based literal-value resolver, distinct from both the prior opcode-presence and Ldstr-literal-collection techniques.
8. No new NuGet dependency — `Mono.Cecil >= 0.11.5` (existing pin) covers all field and IL inspection needs.
9. Failure messages must name the offending assembly/type/method, the literal value, and the resolved constants-class + field name — consistent with every existing predicate's failure-message discipline in this domain.

### SK.00.HealthCheckConstantsGuard — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Predicates/StringConstantsClassDetector.cs` | SharedKernel.ArchitectureTests | Create | Reusable helper — detects the "all-string-constants static class" shape and resolves literal field values; not itself an ICustomRule |
| `Predicates/NoBareHealthCheckLiteralWhereConstantsExistPredicate.cs` | SharedKernel.ArchitectureTests | Create | ICustomRule — flags bare string-literal arguments to health-check registration APIs that duplicate a value already exposed by a detected constants class |
| `Rules/HealthCheckConstantsUsageRules.cs` | SharedKernel.ArchitectureTests | Create | Single factory method `NoBareHealthCheckLiteralWhereConstantsExist(Assembly)` wiring the predicate into a `ConditionList` |

### SK.00.HealthCheckConstantsGuard — Acceptance Criteria

- [ ] `HealthCheckConstantsUsageRules.NoBareHealthCheckLiteralWhereConstantsExist` fails against a contrived fixture reproducing the pre-P-177 shape (a string-constants class plus a sibling method passing a bare literal duplicating one of its values to `AddCheck`/`HealthCheckRegistration`)
- [ ] The same rule passes against a contrived fixture reproducing the post-P-177 shape (the sibling method references the constant via field access instead of a literal)
- [ ] The rule passes (vacuously) against a fixture assembly that contains no string-constants class at all (covers any domain before it adopts the constants-class pattern)
- [ ] Neither the predicate nor the rule factory method contains the literal strings `"HealthCheckTags"` or `"HealthCheckNames"` anywhere in the implementation — verified by code review, confirming the rule generalizes to any future domain's constants class
- [ ] Rule documented in `00.Governance/CLAUDE.md` with rationale, explicitly scoped as additive to (not a replacement for) `HealthCheckTagIntegrityRules` (P-173)'s tag-integrity rules and `CompositionRootExclusivityRules` (P-173)
- [ ] Full governance architecture test suite passes with the new rule included

### SK.00.HealthCheckConstantsGuard — Dependencies

- Requires P-177 (13.ServiceDefaults `HealthCheckTags`/`HealthCheckNames` introduction) for **real-assembly verification only**: design/implementation proceeds now against contrived in-memory fixtures (same `CSharpCompilation` + `MetadataReference.CreateFromImage` technique as `RedisTopologyRulesTests` and `SK.00.ServiceDefaultsGovernance`), since the actual pre/post-P-177 file shapes are not yet available as a buildable assembly at design time.
- Additive to (does not replace or modify) `SK.00.ServiceDefaultsGovernance` (P-173) — `HealthCheckTagIntegrityRules` and `CompositionRootExclusivityRules` are untouched by this phase.
- Unblocks: a permanent, mechanically-enforced backstop against magic-string drift for any current or future SharedKernel domain that introduces a well-known-string constants class.

### SK.00.HealthCheckConstantsGuard — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing pin — no change; the new `StringConstantsClassDetector` reuses the existing reference)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new Roslyn analyzer in this phase)
- Target framework: `net10.0` (ArchitectureTests only)

### SK.00.HealthCheckConstantsGuard — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-53 | Define `StringConstantsClassDetector` shape-detection algorithm (static + sealed type, all fields const-string or static-readonly-string, mixed-type classes allowed) and its literal-value resolution contract; define `NoBareHealthCheckLiteralWhereConstantsExistPredicate` ICustomRule shape (health-check call-site detection per Implementation Rule 3, literal-vs-constants-set comparison per Implementation Rule 4); define `HealthCheckConstantsUsageRules.NoBareHealthCheckLiteralWhereConstantsExist(Assembly)` factory signature; document the generality requirement (no concrete constants-class name literals in the implementation) | SharedKernel.ArchitectureTests | `●` |
| C-75 | Implement `StringConstantsClassDetector` in `Predicates/`: Mono.Cecil `TypeDefinition.Fields` walk, `IsAbstract && IsSealed` static-class shape check, `IsLiteral`/`IsInitOnly+IsStatic` field filter restricted to `System.String` field type, returns the resolved set of (declaring type name, field name, literal value) tuples across the assembly | SharedKernel.ArchitectureTests | `●` |
| C-76 | Implement `NoBareHealthCheckLiteralWhereConstantsExistPredicate` in `Predicates/`: ICustomRule, calls `StringConstantsClassDetector` once per assembly scan, recognizes `IHealthChecksBuilder.Add`/`AddCheck`/`HealthCheckRegistration` constructor call sites, reuses the `Ldstr` literal-collection technique from `HealthCheckTagIntegrityRules` to gather call-site literals, fails on exact-value match against the resolved constants set, vacuous pass when the constants set is empty | SharedKernel.ArchitectureTests | `●` |
| C-77 | Implement `HealthCheckConstantsUsageRules` static class in `Rules/`: single factory method wiring the predicate into a `ConditionList` via `.Should().MeetCustomRule(...)` | SharedKernel.ArchitectureTests | `●` |
| T-139 | Architecture test (fire path): contrived fixture assembly reproducing the pre-P-177 shape — a string-constants class (any name, not `HealthCheckTags`) plus a sibling method passing a bare literal that exactly duplicates one of the class's field values to a `HealthCheckRegistration`-shaped or `AddCheck`-shaped call; assert the rule fails, naming the offending method, the literal, and the matching constant | SharedKernel.ArchitectureTests | `●` |
| T-140 | Architecture test (pass path): same fixture shape as T-139 but the sibling method references the constant via field access (`Ldsfld`) instead of a literal (`Ldstr`); assert the rule passes | SharedKernel.ArchitectureTests | `●` |
| T-141 | Architecture test (pass path, vacuous case): contrived fixture assembly containing health-check registration call sites with bare literals but NO string-constants class anywhere in the assembly; assert the rule passes (nothing to enforce yet) | SharedKernel.ArchitectureTests | `●` |
| T-142 | Architecture test (generality check): a second contrived fixture using a differently-named and differently-shaped constants class (e.g., a class named `WidgetRegistrationNames` unrelated to health checks in name, but still a static all-string-constants class) plus a sibling health-check call site duplicating one of its values; assert the rule still fires — confirming the predicate does not rely on any hardcoded class-name string | SharedKernel.ArchitectureTests | `●` |
| DO-25 | Document `HealthCheckConstantsUsageRules` and `StringConstantsClassDetector` in `00.Governance/CLAUDE.md`: rationale, the value-comparison detection approach (Implementation Rule 1 decision gate), the generality requirement, and an explicit cross-reference clarifying this rule is additive to (not a replacement for) `HealthCheckTagIntegrityRules`/`CompositionRootExclusivityRules` (P-173); add Changelog entry; confirm the exact `Microsoft.Extensions.Diagnostics.HealthChecks` declaring-type name used in call-site detection (Implementation Rule 3) once implementation confirms it | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Governance: Architecture Rules Banning Hand-Rolled ProblemDetails and Inline Result-to-HTTP Branching (`SK.00.PresentationArchRules`) <!-- phase-key: SK.00.PresentationArchRules -->

> WO-031 P-199. Depends on P-194 (14.Presentation `ResultHttpExtensions`/`Error.ToProblemDetails()` must exist before real-assembly verification — design/implementation proceeds now against contrived in-memory fixtures, same technique as `SK.00.ServiceDefaultsGovernance`/`SK.00.HealthCheckConstantsGuard`).

### SK.00.PresentationArchRules — Goal

Two new NetArchTest rules in `SharedKernel.ArchitectureTests`, mirroring the precedent already established for raw `HttpClient` injection (SK0013, P-159) and inline `Result`↔`Envelope` mapping (WO-026 P-166/167's documented backlog item): (1) ban direct construction of `Microsoft.AspNetCore.Mvc.ProblemDetails` / `Microsoft.AspNetCore.Http.HttpValidationProblemDetails` outside `SharedKernel.Presentation.WebApi`; (2) ban inline `Result`/`Result<T>.IsSuccess`/`.IsFailure` branching co-occurring with an `IResult`/`ActionResult`/`ActionResult<T>` return in the same method, outside `SharedKernel.Presentation.WebApi`, unless the method also routes through `ResultHttpExtensions.ToProblemDetailsResult()`. `14.Presentation` only achieves its purpose if consuming services are mechanically prevented from bypassing it — not just told to via documentation.

### SK.00.PresentationArchRules — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests` only.
- New files:
  - `Rules/PresentationLayeringRules.cs` — `NoDirectProblemDetailsConstructionOutsideWebApi(params Assembly[])`, `NoInlineResultBranchBeforeHttpResultOutsideWebApi(params Assembly[])`
  - `Predicates/NoDirectProblemDetailsConstructionPredicate.cs` — `ICustomRule`, `Newobj` IL match on `ProblemDetails`/`HttpValidationProblemDetails` full names
  - `Predicates/NoInlineResultBranchBeforeHttpResultPredicate.cs` — `ICustomRule`, method-level co-occurrence check (IsSuccess/IsFailure signal + IResult/ActionResult return-type signal + absence of ToProblemDetailsResult escape-hatch signal)
- Modified files: `00.Governance/CLAUDE.md` (already updated by this planning pass — Architecture Test Contracts + Implementation Rules + Changelog)
- Deleted files: none
- No new SK diagnostic ID — both rules are `ICustomRule` predicates over Mono.Cecil IL inspection, following this domain's existing precedent (`RedisTopologyRules`, `CompositionRootExclusivityRules`, `GrpcNeverReferencesContracts`) that boundary-mapping prohibitions do not always require minting a new Roslyn analyzer.

### SK.00.PresentationArchRules — Diagnostic Registry Changes

None. No new SK ID assigned in this phase.

### SK.00.PresentationArchRules — Implementation Rules

1. Neither predicate carries an internal namespace exemption for `SharedKernel.Presentation.WebApi` — exclusion is achieved entirely by the consuming test project never passing that assembly to either factory method (no single namespace prefix covers every legitimate in-package construction site: `ErrorProblemDetailsExtensions`, the global `IExceptionHandler`, and `ResultHttpExtensions` itself all legitimately trigger both signals).
2. `NoDirectProblemDetailsConstructionPredicate` matches via exact `MethodReference.DeclaringType.FullName` equality against `"Microsoft.AspNetCore.Mvc.ProblemDetails"` and `"Microsoft.AspNetCore.Http.HttpValidationProblemDetails"` on a `Newobj` opcode — reuses the `Newobj`-walk pattern from `NoDirectEncryptedValueConverterInstantiationPredicate`.
3. `NoInlineResultBranchBeforeHttpResultPredicate` is a method-level co-occurrence check, not a control-flow/statement-order analysis — it is a deliberate over-approximation (same documented-limitation philosophy as `HealthCheckTagIntegrityRules`'s literal-collection technique).
4. The escape-hatch signal (`MethodReference.Name == "ToProblemDetailsResult"` anywhere in the method body) suppresses the violation regardless of the other two signals being present.
5. `Result`/`Result<T>` type-name matching targets `"Result"` exact or `"Result\`1"` IL-generic-arity-suffixed prefix, with no `DeclaringType.Namespace` guard for `SharedKernel.Primitives` — add the namespace guard only if a real false-positive (an unrelated `Result` type elsewhere in the platform) is ever confirmed.
6. Both predicates reuse the existing `Mono.Cecil >= 0.11.5` reference already in `SharedKernel.ArchitectureTests` — zero new NuGet dependencies.

### SK.00.PresentationArchRules — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/PresentationLayeringRules.cs` | SharedKernel.ArchitectureTests | Create | Two factory methods wiring the two predicates into `ConditionList` |
| `Predicates/NoDirectProblemDetailsConstructionPredicate.cs` | SharedKernel.ArchitectureTests | Create | `ICustomRule` — IL `Newobj` match on ProblemDetails/HttpValidationProblemDetails |
| `Predicates/NoInlineResultBranchBeforeHttpResultPredicate.cs` | SharedKernel.ArchitectureTests | Create | `ICustomRule` — method-level three-signal co-occurrence check |

### SK.00.PresentationArchRules — Acceptance Criteria

- [ ] `NoDirectProblemDetailsConstructionOutsideWebApi` fails against a contrived fixture assembly (not named `SharedKernel.Presentation.WebApi`) containing a type that directly `new`s `ProblemDetails`
- [ ] `NoDirectProblemDetailsConstructionOutsideWebApi` fails the same way for `HttpValidationProblemDetails`
- [ ] `NoDirectProblemDetailsConstructionOutsideWebApi` passes against a fixture with no direct construction (e.g., constructs via a factory method only)
- [ ] `NoInlineResultBranchBeforeHttpResultOutsideWebApi` fails against a contrived fixture method that reads `Result.IsSuccess`/`IsFailure` and returns `IResult`/`ActionResult`/`ActionResult<T>` with no `ToProblemDetailsResult` call in the same method
- [ ] `NoInlineResultBranchBeforeHttpResultOutsideWebApi` passes against a fixture method exhibiting the same two signals but that also calls a member named `ToProblemDetailsResult`
- [ ] `NoInlineResultBranchBeforeHttpResultOutsideWebApi` passes (vacuously) against a fixture with no `IsSuccess`/`IsFailure` usage at all
- [ ] Both rules documented in `00.Governance/CLAUDE.md` alongside the existing architecture test contracts (done in this planning pass — verify against final implementation, no discrepancy expected)
- [ ] Full `SharedKernel.ArchitectureTests.Tests` suite still green after the two new rules are added

### SK.00.PresentationArchRules — Dependencies

- Requires P-194 (14.Presentation `Error.ToProblemDetails()`/`ResultHttpExtensions` existence) for **real-assembly verification only** — design/implementation proceeds now against contrived in-memory fixtures (same `CSharpCompilation` + `MetadataReference.CreateFromImage` technique as `RedisTopologyRulesTests`, `SK.00.ServiceDefaultsGovernance`, and `SK.00.HealthCheckConstantsGuard`), since `SharedKernel.Presentation.WebApi` is being concurrently built under WO-031.
- Not dependent on and does not modify `SK.00.CommunicationArchRules` (SK0013) or `SK.00.WO026CommunicationQuality` (`GrpcNeverReferencesContracts`) — sibling precedent only, no shared code.
- Unblocks: closes the WO-026 P-166/167 documented backlog note ("A future Roslyn analyzer... is tracked as a backlog item") — though implemented here as a NetArchTest rule rather than a Roslyn analyzer, since the detection surface (IL method-body co-occurrence) matches this domain's existing `ICustomRule` precedent more closely than a syntax-only analyzer would.

### SK.00.PresentationArchRules — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing pin — no change; both new predicates reuse the existing reference)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new Roslyn analyzer in this phase)
- Target framework: `net10.0` (ArchitectureTests only)

### SK.00.PresentationArchRules — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-54 | Define `NoDirectProblemDetailsConstructionPredicate` shape (Newobj IL match on full-name set `{ProblemDetails, HttpValidationProblemDetails}`) and `NoInlineResultBranchBeforeHttpResultPredicate` shape (three-signal method-level co-occurrence: IsSuccess/IsFailure call, IResult/ActionResult/ActionResult\<T\> return-type or local-variable type, ToProblemDetailsResult escape hatch); define both `PresentationLayeringRules` factory signatures (`params Assembly[]`, no internal namespace exemption); document the caller-controlled exclusion convention | SharedKernel.ArchitectureTests | `●` |
| C-78 | Implement `NoDirectProblemDetailsConstructionPredicate` in `Predicates/`: Mono.Cecil `Newobj` opcode walk, exact `MethodReference.DeclaringType.FullName` match against the two-type set | SharedKernel.ArchitectureTests | `●` |
| C-79 | Implement `NoInlineResultBranchBeforeHttpResultPredicate` in `Predicates/`: three-signal pass over `MethodDefinition.Body.Instructions`/`ReturnType`/`Variables`; implement `PresentationLayeringRules` static class in `Rules/` wiring both predicates into `ConditionList` via `.Should().MeetCustomRule(...)` | SharedKernel.ArchitectureTests | `●` |
| T-143 | Architecture test (fire path): contrived fixture with a type directly constructing `ProblemDetails` via `newobj`; assert `NoDirectProblemDetailsConstructionOutsideWebApi` fails, naming the offending type/method | SharedKernel.ArchitectureTests | `●` |
| T-144 | Architecture test (pass path): contrived fixture with no direct `ProblemDetails`/`HttpValidationProblemDetails` construction (constructs via a factory method only); assert the rule passes; cover `HttpValidationProblemDetails` in the same or a companion fire-path case | SharedKernel.ArchitectureTests | `●` |
| T-145 | Architecture test (fire path): contrived fixture method reading `Result.IsSuccess`/`IsFailure` and returning `IResult`/`ActionResult`/`ActionResult<T>` with no `ToProblemDetailsResult` call; assert `NoInlineResultBranchBeforeHttpResultOutsideWebApi` fails, naming the offending type/method | SharedKernel.ArchitectureTests | `●` |
| T-146 | Architecture test (pass path): same two signals as T-145 but the method also calls a member named `ToProblemDetailsResult`; assert the rule passes. Companion vacuous-pass case: fixture with no `IsSuccess`/`IsFailure` usage at all; assert the rule passes | SharedKernel.ArchitectureTests | `●` |
| DO-26 | Document `PresentationLayeringRules` and both new predicates in `00.Governance/CLAUDE.md`: rationale, the caller-controlled exclusion convention (no internal namespace guard), the method-level co-occurrence over-approximation limitation, and the explicit cross-reference closing the WO-026 P-166/167 backlog note; add Changelog entry (already pre-written by this planning pass — verify accuracy against final implementation, no edits expected unless a discrepancy is found) | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Governance: Architecture Enforcement for the Extended Application Pipeline (`SK.00.ApplicationPipelineArchRules`) <!-- phase-key: SK.00.ApplicationPipelineArchRules -->

> WO-036 P-225. Depends on `05.Application` P-220 (`TracingBehavior`), P-221 (streaming query vocabulary), P-222 (`ResilienceBehavior`), P-224 (`CacheInvalidationBehavior`) for **real-assembly verification only** — design/implementation proceeds now against contrived in-memory fixtures (same `CSharpCompilation` + `MetadataReference.CreateFromImage` technique as `SK.00.ServiceDefaultsGovernance`/`SK.00.HealthCheckConstantsGuard`/`SK.00.PresentationArchRules`), since WO-036 is design-only as of 2026-06-30 (`05.Application/state-map.md` Core phase C-18..C-29 not yet started).

### SK.00.ApplicationPipelineArchRules — Goal

Four new architecture-enforcement surfaces in `SharedKernel.ArchitectureTests`, covering the four new `05.Application` capabilities from WO-036: (1) infrastructure-purity for the three new behaviors (`TracingBehavior<,>`, `ResilienceBehavior<,>`, `CacheInvalidationBehavior<,>`) — mirrors the existing, undocumented-as-a-named-rule purity expectation already implicit in `ApplicationNeverReferencesConcreteInfrastructure` (`SharedKernelLayeringRules`), made explicit and behavior-scoped; (2) a generic-constraint non-interference check proving none of the (eventually ten) `IPipelineBehavior<,>` implementations accidentally satisfy `IStreamRequest<TResponse>`'s shape; (3) an IL-level ban on hand-rolled retry/backoff (`Task.Delay`-in-a-loop fingerprint) anywhere under `05.Application` production code outside `ResilienceBehavior` itself, extending the existing `System.Random`/`DateTime.UtcNow` hand-rolled-primitive prohibition pattern; (4) a reusable, governance-owned reflection-based pipeline-order assertion helper that `05.Application`'s own test suite uses to mechanically pin `ApplicationBehaviorsBuilder.Build()`'s registration sequence, so a future edit cannot silently reorder the ten-named-slot pipeline. Every prior `05.Application` work order paired new behaviors with a governance phase for exactly this reason — see `SK.00.CommunicationArchRules`, `SK.00.PresentationArchRules`, `SK.00.RedisTopology` precedent: documented rules without mechanical enforcement decay under time pressure.

### SK.00.ApplicationPipelineArchRules — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests` only.
- New files:
  - `Rules/ApplicationPipelineRules.cs` — `BehaviorsNeverReferenceConcreteInfrastructure(params Assembly[])`, `NoExistingBehaviorMatchesStreamRequestConstraint(Assembly)`, `NoHandRolledRetryLoopOutsideResilienceBehavior(Assembly)`
  - `Predicates/NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate.cs` — `ICustomRule`, scoped to a caller-supplied behavior-type-name set (`TracingBehavior`, `ResilienceBehavior`, `CacheInvalidationBehavior`), asserting zero member/field/method-signature reference to `SharedKernel.Caching.FusionCache`, `SharedKernel.Caching.Redis*`, `SharedKernel.Persistence.*` (excluding `.Abstractions`), `SharedKernel.Messaging.*` (excluding `.Abstractions`) namespaces
  - `Predicates/NoGenericConstraintMatchesStreamRequestPredicate.cs` — `ICustomRule`, inspects each `IPipelineBehavior<,>`-implementing `TypeDefinition`'s `GenericParameter.Constraints` for the open generic `TRequest` parameter, fails if any constraint type's `FullName` or interface-closure includes `IStreamRequest\`1` / `IStreamRequestHandler\`2`
  - `Predicates/NoTaskDelayOutsideResilienceBehaviorPredicate.cs` — `ICustomRule`, Mono.Cecil `Call`/`Callvirt` IL walk for `MethodReference.FullName` matching `System.Threading.Tasks.Task::Delay`, scoped to the supplied assembly, with a single class-name exemption (`ResilienceBehavior`, exact match)
  - `PipelineOrderAssertion.cs` (public, non-`ICustomRule` helper — reflection-based, not NetArchTest) — `AssertRegistrationOrder(IServiceCollection services, params Type[] expectedBehaviorTypesInOrder)`, walks the registered `ServiceDescriptor` entries for the open generic `IPipelineBehavior<,>` and asserts the closed-generic implementation types appear in the supplied order; consumed by `05.Application.Behaviors.Tests`, never by `00.Governance`'s own test suite as a fire/pass NetArchTest case
- Modified files: `00.Governance/CLAUDE.md` (already updated by this planning pass — Architecture Test Contracts + Implementation Rules + Changelog)
- Deleted files: none
- No new SK diagnostic ID — all three `ICustomRule` predicates and the reflection helper are pure Mono.Cecil/reflection checks, following this domain's established precedent (`RedisTopologyRules`, `CompositionRootExclusivityRules`, `GrpcNeverReferencesContracts`, `PresentationLayeringRules`) that boundary-mapping and ordering prohibitions do not always require minting a new Roslyn analyzer.

### SK.00.ApplicationPipelineArchRules — Diagnostic Registry Changes

None. No new SK ID assigned in this phase.

### SK.00.ApplicationPipelineArchRules — Implementation Rules

1. `NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate` is constructed with a caller-supplied `HashSet<string>` of exact simple type names (`"TracingBehavior"`, `"ResilienceBehavior"`, `"CacheInvalidationBehavior"`) — never hardcoded inside the predicate — so a future fourth infra-adjacent behavior can be added by the caller without a predicate code change, mirroring `HealthCheckTagIntegrityRules`'s caller-supplied-prefix-list convention.
2. The infra-purity check excludes `.Abstractions` sub-namespaces explicitly (`SharedKernel.Persistence.Abstractions`, `SharedKernel.Messaging.Abstractions` are never referenced by these behaviors anyway per the root layering ceiling, but the predicate must not accidentally flag a namespace-prefix false positive against a hypothetical future abstractions-only reference) — same exclusion discipline as `CachingAbstractionRules`'s exemption list.
3. `NoGenericConstraintMatchesStreamRequestPredicate` is a **structural, not runtime**, check: it inspects IL generic-parameter constraint metadata on each closed `IPipelineBehavior<,>` implementor, not actual DI resolution behavior. This is intentionally a stronger, earlier-failing guarantee than a runtime DI test — if a future behavior's `TRequest` constraint is loosened in a way that could structurally satisfy `IStreamRequest<TResponse>`, this rule fails at the architecture-test stage before any runtime wiring is attempted.
4. `NoTaskDelayOutsideResilienceBehaviorPredicate` reuses the established `DoesNotCallSystemClockPredicate` Call/Callvirt-opcode-presence technique (exact `MethodReference.FullName` match: `"System.Threading.Tasks.Task::Delay"` and `"System.Threading.Tasks.Task::Delay(System.Int32)"`/`"...TimeSpan,...)"` overload forms — match on `MethodReference.Name == "Delay"` AND `DeclaringType.FullName == "System.Threading.Tasks.Task"` to cover all overloads in one check). This is a **fingerprint heuristic**, not a full retry-loop detector: it flags `Task.Delay` usage, the one IL-detectable signal common to virtually every hand-rolled retry/backoff loop, and accepts the documented false-positive risk that a legitimate non-retry `Task.Delay` call elsewhere in `05.Application` would also be flagged — at the time of this phase, no such legitimate call site is known to exist outside `ResilienceBehavior`.
5. `PipelineOrderAssertion` is the one artifact in this phase that is **not** a `ConditionList`/`ICustomRule` — it is a plain reflection helper class (`IServiceCollection.BuildServiceProvider()` is deliberately NOT called; the assertion walks `ServiceDescriptor.ServiceType`/`ImplementationType` directly off the unbuilt `IServiceCollection`, since `ApplicationBehaviorsBuilder.Build()` registers behaviors as `ServiceDescriptor` entries against the open generic `IPipelineBehavior<,>`, and MediatR resolves them in registration order). It ships in `SharedKernel.ArchitectureTests` (the shared cross-domain test-infrastructure-adjacent package per this domain's existing role) rather than `16.Testing` because it is purpose-built for asserting *architectural* ordering invariants, not general test fixtures — consistent with this package already being the home for `ArchitectureRuleBase`/predicate helpers consumed by other domains' test suites.
6. All three `ICustomRule` predicates and the reflection helper reuse the existing `Mono.Cecil >= 0.11.5` / `Microsoft.Extensions.DependencyInjection.Abstractions` references already available transitively in `SharedKernel.ArchitectureTests` — zero new NuGet dependencies.
7. `00.Governance` does not and will never reference `05.Application` or `05.Application.Behaviors` directly (layering: `00.Governance` references nothing) — all three `ICustomRule` predicates are designed and tested here against contrived in-memory fixture assemblies; the real-assembly wiring (passing the actual `SharedKernel.Application.Behaviors` assembly to these factory methods, and consuming `PipelineOrderAssertion` from `05.Application.Behaviors.Tests`) is `05.Application`'s own responsibility once WO-036's Core phase (C-18..C-29) ships — mirrors the exact cross-domain consumption pattern already established for `CachingAbstractionRules`/`RedisTopologyRules` (consumed by `02.Caching`'s own test suites) and `PersistenceLayerProtectionRules` (consumed by `06.Persistence`'s own test suites). `00.Governance` ships the mechanism; the owning domain's test project invokes it against its own real assembly.

### SK.00.ApplicationPipelineArchRules — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/ApplicationPipelineRules.cs` | SharedKernel.ArchitectureTests | Create | Three factory methods wiring the three new predicates into `ConditionList` |
| `Predicates/NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate.cs` | SharedKernel.ArchitectureTests | Create | `ICustomRule` — caller-supplied behavior-name set, infra-namespace reference ban |
| `Predicates/NoGenericConstraintMatchesStreamRequestPredicate.cs` | SharedKernel.ArchitectureTests | Create | `ICustomRule` — IL generic-constraint structural check against `IStreamRequest<TResponse>` |
| `Predicates/NoTaskDelayOutsideResilienceBehaviorPredicate.cs` | SharedKernel.ArchitectureTests | Create | `ICustomRule` — `Task.Delay` Call/Callvirt fingerprint, `ResilienceBehavior` self-exemption |
| `PipelineOrderAssertion.cs` | SharedKernel.ArchitectureTests | Create | Public reflection helper — `ServiceDescriptor` registration-order assertion, consumed by `05.Application.Behaviors.Tests` |

### SK.00.ApplicationPipelineArchRules — Acceptance Criteria

- [x] `BehaviorsNeverReferenceConcreteInfrastructure` fails against a contrived fixture assembly containing a type named `TracingBehavior`/`ResilienceBehavior`/`CacheInvalidationBehavior` that references a forbidden concrete-infrastructure namespace (one fixture per behavior name, or a parameterized case covering all three)
- [x] `BehaviorsNeverReferenceConcreteInfrastructure` passes against a fixture where the same three behavior names reference only `SharedKernel.Caching.Abstractions`/`SharedKernel.Primitives`/`MediatR`
- [x] `NoExistingBehaviorMatchesStreamRequestConstraint` fails against a contrived fixture where an `IPipelineBehavior<,>` implementor's `TRequest` generic parameter carries a constraint structurally satisfying `IStreamRequest<TResponse>`
- [x] `NoExistingBehaviorMatchesStreamRequestConstraint` passes against a fixture mirroring the real seven/ten WO-035/WO-036 behavior constraint shapes (`IRequest<TResponse>`-rooted constraints only)
- [x] `NoHandRolledRetryLoopOutsideResilienceBehavior` fails against a contrived fixture method outside a type named `ResilienceBehavior` that calls `Task.Delay` inside a loop construct
- [x] `NoHandRolledRetryLoopOutsideResilienceBehavior` passes against a fixture where the only `Task.Delay` call site is inside a type named `ResilienceBehavior`, and against a fixture with no `Task.Delay` call at all (vacuous pass)
- [x] `PipelineOrderAssertion.AssertRegistrationOrder` correctly asserts a passing case (a contrived `IServiceCollection` with `IPipelineBehavior<,>` registrations added in the expected order) and correctly throws/fails a negative case (registrations added out of order) — both proven via a governance-owned unit test of the helper itself, distinct from `05.Application`'s own future consumption of it
- [x] All four artifacts documented in `00.Governance/CLAUDE.md` alongside the existing architecture test contracts (verified against final implementation — zero discrepancy found, no edits needed)
- [x] Full `SharedKernel.ArchitectureTests.Tests` suite still green after the three new rules and the helper are added (117/117 passing)

### SK.00.ApplicationPipelineArchRules — Dependencies

- Requires `05.Application` P-220 (`TracingBehavior`), P-222 (`ResilienceBehavior`), P-224 (`CacheInvalidationBehavior`) for **real-assembly verification only** — design/implementation proceeds now against contrived in-memory fixtures, since WO-036 is design-only as of 2026-06-30.
- Requires P-221 (streaming query vocabulary, `IStreamQuery<TResponse>`/`IStreamRequest<TResponse>`) for real-assembly verification of `NoExistingBehaviorMatchesStreamRequestConstraint` — fixture-only until then.
- Not dependent on and does not modify `SK.00.PresentationArchRules`, `SK.00.CommunicationArchRules`, or `SK.00.RedisTopology` — sibling precedent only, no shared code.
- Unblocks: gives `05.Application`'s own Tests phase (T-13..T-18, T-17 specifically — the reusable pipeline test harness) a governance-owned, pre-built ordering-assertion primitive to consume rather than hand-rolling reflection-based order verification inside `05.Application.Behaviors.Tests` itself.

### SK.00.ApplicationPipelineArchRules — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing pin — no change; all three new predicates reuse the existing reference)
- `Microsoft.Extensions.DependencyInjection.Abstractions`: existing transitive reference (no version pin change — `PipelineOrderAssertion` uses only `IServiceCollection`/`ServiceDescriptor`, already available)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new Roslyn analyzer in this phase)
- Target framework: `net10.0` (ArchitectureTests only)

### SK.00.ApplicationPipelineArchRules — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-55 | Define all three predicate shapes (`NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate` — caller-supplied behavior-name set + forbidden-namespace set; `NoGenericConstraintMatchesStreamRequestPredicate` — IL generic-parameter-constraint structural match against `IStreamRequest\`1`; `NoTaskDelayOutsideResilienceBehaviorPredicate` — Call/Callvirt fingerprint on `System.Threading.Tasks.Task::Delay` with `ResilienceBehavior` self-exemption) and the `PipelineOrderAssertion` reflection-helper shape (`ServiceDescriptor` walk against unbuilt `IServiceCollection`, no container build); define `ApplicationPipelineRules` factory signatures | SharedKernel.ArchitectureTests | `●` |
| C-80 | Implement `NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate` in `Predicates/` | SharedKernel.ArchitectureTests | `●` |
| C-81 | Implement `NoGenericConstraintMatchesStreamRequestPredicate` in `Predicates/` | SharedKernel.ArchitectureTests | `●` |
| C-82 | Implement `NoTaskDelayOutsideResilienceBehaviorPredicate` in `Predicates/` | SharedKernel.ArchitectureTests | `●` |
| C-83 | Implement `ApplicationPipelineRules` static class in `Rules/` wiring the three predicates into `ConditionList` factory methods | SharedKernel.ArchitectureTests | `●` |
| C-84 | Implement `PipelineOrderAssertion` public reflection helper (root namespace, alongside `ArchitectureRuleBase`) | SharedKernel.ArchitectureTests | `●` |
| T-147 | Architecture test (fire path): contrived fixture with a type named `TracingBehavior` (or `ResilienceBehavior`/`CacheInvalidationBehavior`) referencing a forbidden concrete-infrastructure namespace; assert `BehaviorsNeverReferenceConcreteInfrastructure` fails | SharedKernel.ArchitectureTests | `●` |
| T-148 | Architecture test (pass path): contrived fixture with the same three behavior names referencing only permitted namespaces (`SharedKernel.Caching.Abstractions`, `SharedKernel.Primitives`, `MediatR`); assert the rule passes | SharedKernel.ArchitectureTests | `●` |
| T-149 | Architecture test (fire path): contrived fixture with an `IPipelineBehavior<,>` implementor whose `TRequest` constraint structurally satisfies `IStreamRequest<TResponse>`; assert `NoExistingBehaviorMatchesStreamRequestConstraint` fails | SharedKernel.ArchitectureTests | `●` |
| T-150 | Architecture test (pass path): contrived fixture mirroring the real WO-035/WO-036 behavior constraint shapes (`IRequest<TResponse>`-rooted only); assert the rule passes | SharedKernel.ArchitectureTests | `●` |
| T-151 | Architecture test (fire path): contrived fixture with a `Task.Delay` call inside a loop in a type NOT named `ResilienceBehavior`; assert `NoHandRolledRetryLoopOutsideResilienceBehavior` fails | SharedKernel.ArchitectureTests | `●` |
| T-152 | Architecture test (pass path): contrived fixture with `Task.Delay` only inside a type named `ResilienceBehavior`; assert the rule passes. Companion vacuous-pass case: fixture with no `Task.Delay` call at all | SharedKernel.ArchitectureTests | `●` |
| T-153 | Unit test (governance-owned, not NetArchTest): `PipelineOrderAssertion.AssertRegistrationOrder` against a contrived `IServiceCollection` — passing case (registrations in expected order) and failing case (registrations out of order) both proven | SharedKernel.ArchitectureTests | `●` |
| DO-27 | Document `ApplicationPipelineRules`, all three predicates, and `PipelineOrderAssertion` in `00.Governance/CLAUDE.md`: rationale, the caller-supplied behavior-name-set convention, the `Task.Delay` fingerprint-heuristic limitation, and the cross-domain consumption pattern (`00.Governance` ships the mechanism, `05.Application` invokes it against its own real assembly); add Changelog entry (already pre-written by this planning pass — verify accuracy against final implementation, no edits expected unless a discrepancy is found) | SharedKernel.ArchitectureTests | `●` |

---

## Phase: Governance: Architecture Rules Locking the Cryptography Delegation and IUnitOfWork Bridge (`SK.00.CryptoDelegationAndUowSeamGuard`) <!-- phase-key: SK.00.CryptoDelegationAndUowSeamGuard -->

> WO-037 P-229. Depends on `06.Persistence` P-227 (`EncryptedValueConverter` delegates to `SharedKernel.Cryptography`'s `ISymmetricEncryptionService`/`AesGcmEncryptionService`) and P-228 (`EfUnitOfWork` implements both `SharedKernel.Application.Behaviors.IUnitOfWork` and `SharedKernel.Persistence.Abstractions.IUnitOfWork`) for **real-assembly verification only**. Both are design-only as of 2026-06-30 (dispatched to `persistence-arch-planner`, not yet implemented) — design here proceeds against the documented target shape and against contrived in-memory fixtures, the same technique established for every prior governance phase that races ahead of its triggering domain's implementation (`SK.00.ServiceDefaultsGovernance`, `SK.00.HealthCheckConstantsGuard`, `SK.00.PresentationArchRules`, `SK.00.ApplicationPipelineArchRules`).

### SK.00.CryptoDelegationAndUowSeamGuard — Goal

Two new architecture-enforcement surfaces in `SharedKernel.ArchitectureTests`. First, a platform-wide generalization of the existing SK0301 intent: today `EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication` (backed by `NoAesCipherInDomainOrApplicationPredicate`) only scopes its check to assemblies the caller explicitly passes (in practice `03.Domain`/`05.Application`), and exempts any type under `SharedKernel.Persistence.*` or `SharedKernel.Security.*` wholesale — which is precisely how `06.Persistence`'s hand-rolled `AesGcm` violation (the one P-227 fixes) was structurally invisible to this rule despite living in an "exempt" namespace. Once P-227 lands, `SharedKernel.Cryptography`'s `AesGcmEncryptionService` becomes the *only* legitimate direct caller of `AesGcm`/`Aes`/`SymmetricAlgorithm`/`RandomNumberGenerator` (for ciphertext/nonce material) anywhere in the platform. This phase ships that narrower, platform-wide rule and reconciles it with SK0301 so the suite carries one coherent crypto-isolation rule, not two overlapping ones with different exemption lists. Second, a structural-distinctness guard asserting `SharedKernel.Application.Behaviors.IUnitOfWork` and `SharedKernel.Persistence.Abstractions.IUnitOfWork` are never merged into a single type declaration and never made to inherit one another — protecting the local-seam pattern (the same pattern already proven for `IAuthorizationContext` and `IIdempotencyKeyStore`) from a future "simplification" that would silently reintroduce the `05.Application` → `06.Persistence` layering violation WO-037's analysis explicitly declined to take.

### SK.00.CryptoDelegationAndUowSeamGuard — Scope

- Package(s) affected: `SharedKernel.ArchitectureTests` only.
- New files:
  - `Rules/CryptoIsolationRules.cs` — static class, `NoRawSymmetricCipherOutsideCryptography(params Assembly[])` → `ConditionList`
  - `Predicates/NoRawSymmetricCipherOutsideCryptographyPredicate.cs` — `ICustomRule`
  - `Rules/UnitOfWorkSeamRules.cs` — static class, `UnitOfWorkInterfacesRemainDistinct(Assembly applicationBehaviorsAssembly, Assembly persistenceAbstractionsAssembly)` → `ConditionList`
  - `Predicates/UnitOfWorkInterfacesRemainDistinctPredicate.cs` — `ICustomRule`
- Modified files:
  - `SharedKernel.ArchitectureTests/Predicates/NoAesCipherInDomainOrApplicationPredicate.cs` — exemption list narrowed (see Implementation Rule 2)
  - `00.Governance/CLAUDE.md` — diagnostic registry SK0301 entry updated (superseded-and-narrowed, not retired); `EncryptionPatternGuardRules` note updated; two new architecture test contract entries added; Changelog entry appended
- Deleted files: none
- No new SK diagnostic ID for the `IUnitOfWork`-distinctness rule (pure structural NetArchTest/`ICustomRule` check, following the `RedisTopologyRules`/`CompositionRootExclusivityRules`/`PresentationLayeringRules` precedent of SK-less rules for boundary/shape prohibitions). SK0301 itself is retained (not retired — its ID is not reusable per root governance ID-retirement discipline) but its predicate's exemption list is narrowed; no new ID is minted for the generalized platform-wide check because it is the same diagnostic intent at wider scope, not a new diagnosable condition.

### SK.00.CryptoDelegationAndUowSeamGuard — Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0301 | DirectCryptoInDomainOrApplication *(scope widened, same ID)* | Security | Warning | `AesGcm`, `Aes`, `SymmetricAlgorithm`, or `RandomNumberGenerator` (ciphertext/nonce use) referenced by any type platform-wide outside `SharedKernel.Cryptography` — exemption narrowed from `SharedKernel.Persistence.*`/`SharedKernel.Security.*` (wholesale) to `SharedKernel.Cryptography` only |

No new SK ID is assigned in this phase. SK0301's documented trigger and exemption list are updated in place (see Implementation Rules) — this is the "reconcile, do not duplicate" path the acceptance criteria require.

### SK.00.CryptoDelegationAndUowSeamGuard — Implementation Rules

1. `NoRawSymmetricCipherOutsideCryptographyPredicate` is a platform-wide `ICustomRule`: unlike `NoAesCipherInDomainOrApplicationPredicate` (which only ever sees the assemblies a caller explicitly passes — historically just `03.Domain`/`05.Application`), the new predicate is designed to be invoked against **every** production assembly in the platform by the consuming test suite (mirroring how `ReflectionGuardRules.NoMakeGenericMethodReflection` and `PersistenceLayerProtectionRules.OnlyEfUnitOfWorkMayCallSaveChanges` are each invoked once per assembly across the whole solution, not just the two layers most likely to violate it). It checks two surfaces, both already-established Mono.Cecil techniques: (a) `TypeDefinition.Fields` for field types in `System.Security.Cryptography` named `AesGcm`/`Aes`/`SymmetricAlgorithm` (reused from `NoAesCipherInDomainOrApplicationPredicate`); (b) `TypeDefinition.Methods.Body.Instructions` for `Call`/`Callvirt`/`Newobj` opcodes whose resolved `TypeReference` matches the same three cipher types, **plus** `MethodReference.DeclaringType.FullName == "System.Security.Cryptography.RandomNumberGenerator"` (new surface — covers `RandomNumberGenerator.Fill`/`GetBytes`/`Create` static and instance call forms via `DeclaringType` match rather than a single method-name match, since `RandomNumberGenerator` exposes multiple static entry points).
2. Exemption: types whose `TypeDefinition.Namespace` starts with `"SharedKernel.Cryptography"` (exact package prefix — replaces the two-namespace exemption list `SharedKernel.Persistence.*`/`SharedKernel.Security.*` used by the existing SK0301 predicate) return true unconditionally. This is the sole legitimate-caller exemption platform-wide once P-227 lands. No `SharedKernel.Security.*` exemption is carried forward in the new rule: `12.Security.Oidc`'s JWT signing path, if it directly references BCL HMAC/asymmetric types, is a `SharedKernel.Cryptography.IHmacSigner`/`IAsymmetricSignatureService` consumer per the root CLAUDE.md (`01.Core/SharedKernel.Cryptography` — IHmacSigner, IAsymmetricSignatureService), not a raw-BCL-cipher caller; if `12.Security.Oidc` is found to reference `Aes`/`AesGcm`/`SymmetricAlgorithm` directly when this rule is wired against its real assembly, that is itself a violation this rule is correctly designed to catch, not a false positive to suppress.
3. `EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication`'s own predicate (`NoAesCipherInDomainOrApplicationPredicate`, the SK0301-backing implementation) has its exemption list narrowed from `{"SharedKernel.Persistence", "SharedKernel.Security"}` (namespace-prefix `StartsWith`) to `{"SharedKernel.Cryptography"}` only, matching Rule 2 above — this is the "reconcile SK0301 with the new platform-wide rule" acceptance criterion, achieved by narrowing SK0301's own exemption rather than leaving two predicates with conflicting exemption lists. After this change, SK0301 (still scoped by the caller to `03.Domain`/`05.Application` assemblies specifically) and the new `NoRawSymmetricCipherOutsideCryptographyPredicate` (scoped platform-wide by the caller passing every production assembly) share **identical** exemption logic and trigger logic, differing only in which assemblies the consuming test suite passes in. SK0301 is therefore a narrower special case of the platform-wide rule by construction, not a contradictory duplicate — satisfying the acceptance criterion without deleting the existing SK ID or its documented history.
4. `UnitOfWorkInterfacesRemainDistinctPredicate` takes two `Assembly` parameters (not one) — `applicationBehaviorsAssembly` (expected to contain `SharedKernel.Application.Behaviors.IUnitOfWork`) and `persistenceAbstractionsAssembly` (expected to contain `SharedKernel.Persistence.Abstractions.IUnitOfWork`). It resolves both `TypeDefinition`s by exact full name. Three checks, each an independent failure mode: (a) both types must be found (if either is missing, the rule fails — "the interface was renamed or removed, which is itself a seam-pattern violation requiring governance review"); (b) the two resolved `TypeDefinition`s must not be `ReferenceEquals`-identical after resolution (catches an accidental type-forwarding/alias merge); (c) neither `TypeDefinition`'s `Interfaces` collection (for the interface-inheritance case, an interface "inherits" by listing the other in its own base-interface list) may contain an entry whose `InterfaceType.FullName` equals the other's full name — catches `interface IUnitOfWork : SharedKernel.Persistence.Abstractions.IUnitOfWork` being introduced on the `05.Application.Behaviors` side or vice versa.
5. This is a **negative-space / regression-guard** rule: there is no "fire path" in the sense of an existing bad pattern in the codebase today (both interfaces are independently declared, which is the desired state per P-228's design) — the fire-path test fixture must be a *contrived* pair of assemblies where one interface inherits the other, proving the predicate would catch a future merge attempt. This mirrors the established technique for negative-space rules in this domain (e.g. `RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther`, `CommunicationLayeringRules.GrpcNeverReferencesContracts` — both also guard against a state that does not currently exist in the codebase but must be mechanically prevented from being introduced).
6. Both predicates reuse the existing `Mono.Cecil >= 0.11.5` reference already available in `SharedKernel.ArchitectureTests` — zero new NuGet dependencies. `UnitOfWorkInterfacesRemainDistinctPredicate` additionally needs no new technique beyond `TypeDefinition.Interfaces` enumeration, already used by `DoesNotImplementOpenGenericInterfacePredicate` and the `SagaStateMustExtendSagaStateBasePredicate` base-type-chain walk.
7. `00.Governance` does not and will never reference `01.Core/SharedKernel.Cryptography`, `05.Application`, or `06.Persistence` directly (layering: `00.Governance` references nothing). Both rules are designed and tested here against contrived in-memory fixture assemblies that mimic the documented P-227/P-228 target shape (a fixture type named `AesGcmEncryptionService` under a `SharedKernel.Cryptography` namespace calling real `AesGcm`/`RandomNumberGenerator` APIs for the pass-path fixture; two independently-declared `IUnitOfWork`-named interface fixtures in two separate contrived namespaces for the `UnitOfWorkInterfacesRemainDistinct` pass-path fixture). Real-assembly wiring (passing the actual `SharedKernel.Cryptography`, `SharedKernel.Application.Behaviors`, and `SharedKernel.Persistence.Abstractions`/`.EfCore` assemblies) is `01.Core`'s, `05.Application`'s, and `06.Persistence`'s own responsibility once P-227/P-228 ship — mirrors the cross-domain consumption pattern already established for `CachingAbstractionRules`, `PersistenceLayerProtectionRules`, and `ApplicationPipelineRules`: `00.Governance` ships the mechanism, the owning domains' test projects invoke it against their own real assemblies.

### SK.00.CryptoDelegationAndUowSeamGuard — File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/CryptoIsolationRules.cs` | SharedKernel.ArchitectureTests | Create | `NoRawSymmetricCipherOutsideCryptography(params Assembly[])` factory wiring the new predicate into `ConditionList` |
| `Predicates/NoRawSymmetricCipherOutsideCryptographyPredicate.cs` | SharedKernel.ArchitectureTests | Create | `ICustomRule` — platform-wide `AesGcm`/`Aes`/`SymmetricAlgorithm`/`RandomNumberGenerator` ban outside `SharedKernel.Cryptography` |
| `Predicates/NoAesCipherInDomainOrApplicationPredicate.cs` | SharedKernel.ArchitectureTests | Modify | Narrow exemption list from `{SharedKernel.Persistence, SharedKernel.Security}` to `{SharedKernel.Cryptography}` |
| `Rules/UnitOfWorkSeamRules.cs` | SharedKernel.ArchitectureTests | Create | `UnitOfWorkInterfacesRemainDistinct(Assembly, Assembly)` factory wiring the new predicate into `ConditionList` |
| `Predicates/UnitOfWorkInterfacesRemainDistinctPredicate.cs` | SharedKernel.ArchitectureTests | Create | `ICustomRule` — asserts the two `IUnitOfWork` declarations are never merged or made to inherit one another |

### SK.00.CryptoDelegationAndUowSeamGuard — Acceptance Criteria

- [ ] `NoRawSymmetricCipherOutsideCryptography` fails against a contrived fixture type outside a `SharedKernel.Cryptography`-prefixed namespace that references `AesGcm`, `Aes`, or `SymmetricAlgorithm` directly (one fixture per cipher type, or a parameterized case)
- [ ] `NoRawSymmetricCipherOutsideCryptography` fails against a contrived fixture type outside `SharedKernel.Cryptography` that calls a `RandomNumberGenerator` member for byte/nonce generation
- [ ] `NoRawSymmetricCipherOutsideCryptography` passes against a contrived fixture type inside a `SharedKernel.Cryptography`-prefixed namespace performing the same cipher/RNG calls (proves the sole-legitimate-caller exemption)
- [ ] `UnitOfWorkInterfacesRemainDistinct` fails against a contrived two-assembly fixture where one `IUnitOfWork`-named interface's base-interface list contains the other (inheritance-merge fire path)
- [ ] `UnitOfWorkInterfacesRemainDistinct` fails against a contrived fixture where only one `IUnitOfWork`-named type exists across both supplied assemblies (collapse-to-one-type fire path)
- [ ] `UnitOfWorkInterfacesRemainDistinct` passes against a contrived two-assembly fixture with two independently-declared `IUnitOfWork`-named interfaces, neither referencing the other (mirrors the real P-228 target shape)
- [ ] `NoAesCipherInDomainOrApplicationPredicate`'s exemption list is verifiably narrowed to `SharedKernel.Cryptography` only (existing SK0301 fire/pass tests re-verified against the narrowed exemption — no existing `SharedKernel.Persistence`/`SharedKernel.Security` fixture in the current test suite was relying on the wider exemption to pass; if one is found, it is itself evidence of a latent violation to flag, not a reason to widen the exemption back)
- [ ] SK0301's `00.Governance/CLAUDE.md` registry entry documents the narrowed exemption and cross-references the new platform-wide rule as its generalization — no contradictory duplicate rule description left in the file
- [ ] Full `SharedKernel.ArchitectureTests.Tests` suite remains green after both new rules and the SK0301 predicate narrowing are added
- [ ] `00.Governance/CLAUDE.md` changelog records the new rules, confirms no new SK number was assigned, and records the SK0301 exemption narrowing

### SK.00.CryptoDelegationAndUowSeamGuard — Dependencies

- Requires `06.Persistence` P-227 (`EncryptedValueConverter` → `SharedKernel.Cryptography` delegation) for **real-assembly verification only** of `NoRawSymmetricCipherOutsideCryptography` against the actual `SharedKernel.Persistence.EfCore` and `SharedKernel.Cryptography` assemblies — design/implementation proceeds now against contrived fixtures, since P-227 is design-only as of 2026-06-30.
- Requires `06.Persistence` P-228 (`EfUnitOfWork` implementing both `IUnitOfWork` interfaces) for **real-assembly verification only** of `UnitOfWorkInterfacesRemainDistinct` against the actual `SharedKernel.Application.Behaviors` and `SharedKernel.Persistence.Abstractions` assemblies — same fixture-first posture.
- Not dependent on and does not modify `SK.00.EncryptionPatternGuard`'s other three rules (`NoEncryptionAttributeOnDomainEntities`, `NoEncryptionRotationJobInjectionInDomainOrApplication`, `NoDirectEncryptedValueConverterInstantiation`) — only the `NoCryptoCipherInDomainOrApplication`/SK0301 predicate's exemption list is touched.
- Unblocks: gives `01.Core` (`SharedKernel.Cryptography`), `05.Application`, and `06.Persistence` a governance-owned, pre-built pair of mechanisms to wire into their own test suites once P-227/P-228 ship real code — consistent with this domain's standing practice of shipping the mechanism ahead of the triggering domain's implementation.

### SK.00.CryptoDelegationAndUowSeamGuard — Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing pin — no change; both new predicates reuse the existing reference)
- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — no new Roslyn analyzer in this phase)
- Target framework: `net10.0` (ArchitectureTests only)

### SK.00.CryptoDelegationAndUowSeamGuard — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-56 | Define `NoRawSymmetricCipherOutsideCryptographyPredicate` shape (field + IL-walk surfaces, `RandomNumberGenerator` `DeclaringType` match, `SharedKernel.Cryptography` sole exemption) and `UnitOfWorkInterfacesRemainDistinctPredicate` shape (two-assembly resolution, identity check, bidirectional base-interface check); define `CryptoIsolationRules`/`UnitOfWorkSeamRules` factory signatures; define the SK0301 exemption-narrowing change to `NoAesCipherInDomainOrApplicationPredicate` | SharedKernel.ArchitectureTests | `●` |
| C-85 | Implement `NoRawSymmetricCipherOutsideCryptographyPredicate` in `Predicates/` | SharedKernel.ArchitectureTests | `●` |
| C-86 | Implement `CryptoIsolationRules` static class in `Rules/` wiring the predicate into a `ConditionList` factory method | SharedKernel.ArchitectureTests | `●` |
| C-87 | Implement `UnitOfWorkInterfacesRemainDistinctPredicate` in `Predicates/` | SharedKernel.ArchitectureTests | `●` |
| C-88 | Implement `UnitOfWorkSeamRules` static class in `Rules/` wiring the predicate into a `ConditionList` factory method | SharedKernel.ArchitectureTests | `●` |
| C-89 | Narrow `NoAesCipherInDomainOrApplicationPredicate`'s exemption list from `{SharedKernel.Persistence, SharedKernel.Security}` to `{SharedKernel.Cryptography}` | SharedKernel.ArchitectureTests | `●` |
| T-154 | Architecture test (fire path): contrived fixture type outside `SharedKernel.Cryptography` referencing `AesGcm`/`Aes`/`SymmetricAlgorithm`; assert `NoRawSymmetricCipherOutsideCryptography` fails | SharedKernel.ArchitectureTests | `●` |
| T-155 | Architecture test (fire path): contrived fixture type outside `SharedKernel.Cryptography` calling a `RandomNumberGenerator` member; assert `NoRawSymmetricCipherOutsideCryptography` fails | SharedKernel.ArchitectureTests | `●` |
| T-156 | Architecture test (pass path): contrived fixture type inside a `SharedKernel.Cryptography`-prefixed namespace performing the same cipher/RNG calls; assert the rule passes | SharedKernel.ArchitectureTests | `●` |
| T-157 | Architecture test (fire path): contrived two-assembly fixture where one `IUnitOfWork`-named interface's base list contains the other; assert `UnitOfWorkInterfacesRemainDistinct` fails | SharedKernel.ArchitectureTests | `●` |
| T-158 | Architecture test (fire path): contrived fixture where only one `IUnitOfWork`-named type exists across both supplied assemblies; assert `UnitOfWorkInterfacesRemainDistinct` fails | SharedKernel.ArchitectureTests | `●` |
| T-159 | Architecture test (pass path): contrived two-assembly fixture with two independently-declared, non-referencing `IUnitOfWork`-named interfaces; assert the rule passes | SharedKernel.ArchitectureTests | `●` |
| T-160 | Re-run existing SK0301 fire/pass tests (`NoCryptoCipherInDomainOrApplication`) against the narrowed `NoAesCipherInDomainOrApplicationPredicate` exemption list; confirm no regression and add a fixture proving a `SharedKernel.Persistence`-namespaced type calling `AesGcm` directly now fails (previously exempt, now correctly flagged) | SharedKernel.ArchitectureTests | `●` |
| DO-28 | Document `CryptoIsolationRules`, `NoRawSymmetricCipherOutsideCryptographyPredicate`, `UnitOfWorkSeamRules`, `UnitOfWorkInterfacesRemainDistinctPredicate`, and the SK0301 exemption narrowing in `00.Governance/CLAUDE.md`: rationale, the platform-wide-vs-scoped relationship between the new rule and SK0301, the negative-space/regression-guard nature of the `IUnitOfWork`-distinctness rule, and the cross-domain consumption pattern; add Changelog entry (already pre-written by this planning pass — verify accuracy against final implementation) | SharedKernel.ArchitectureTests | `●` |

---

<!-- phase-key: SK.00.MetricsOutcomeTagAndMisregistrationGuard -->
## Phase SK.00.MetricsOutcomeTagAndMisregistrationGuard — Governance: Architecture Enforcement for WO-038 Application Audit Findings

### Goal

The WO-038 audit of `05.Application` surfaced four recurring architectural anti-patterns that will keep resurfacing as new pipeline behaviors and streaming behaviors ship: (1) closed-generic `ResiliencePipeline<TResponse>` DI registration silently falling back to a no-op pipeline, (2) the non-streaming `MetricsBehavior` (P-217) recording `RequestDuration` with no `outcome` tag — unlike its streaming counterpart `StreamMetricsBehavior` (P-234) — making success/failure/exception/cached/duplicate/unauthorized outcomes indistinguishable in dashboards, (3) a type implementing `IStreamPipelineBehavior<,>` being registered against the wrong MediatR interface (`IPipelineBehavior<,>`), silently never invoked, and (4) `typeof(TRequest).Name` (short name) used for metric/log/cache keys instead of the collision-safe `typeof(TRequest).FullName ?? typeof(TRequest).Name` pattern mandated by P-231. This phase mechanically enforces all four findings inside `00.Governance`, mirroring the standing practice (WO-036 P-225, WO-037 P-229) of shipping the governance mechanism as its own phase rather than leaving a documented-but-unenforced rule to decay under time pressure.

### Scope

- Package(s) affected: `SharedKernel.Analyzers` (three new Roslyn analyzers, SK0014–SK0016), `SharedKernel.ArchitectureTests` (one new NetArchTest `ICustomRule` predicate + factory method, no new SK ID)
- New files:
  - `SharedKernel.Analyzers/Analyzers/SK0014ClosedGenericResiliencePipelineRegistrationAnalyzer.cs`
  - `SharedKernel.Analyzers/Analyzers/SK0015StreamPipelineBehaviorMisregistrationAnalyzer.cs`
  - `SharedKernel.Analyzers/Analyzers/SK0016RequestTypeShortNameUsageAnalyzer.cs`
  - `SharedKernel.ArchitectureTests/Predicates/RequestDurationRecordMissingOutcomeTagPredicate.cs`
  - `SharedKernel.ArchitectureTests/Rules/MetricsInstrumentationRules.cs`
  - Corresponding test files in `SharedKernel.Analyzers.Tests/` and `SharedKernel.ArchitectureTests.Tests/`
- Modified files: `00.Governance/CLAUDE.md` (diagnostic registry, architecture test contracts, implementation rules, changelog — already applied by this planning pass)
- Deleted files: none

### Diagnostic Registry Changes (analyzers only)

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0014 | ClosedGenericResiliencePipelineRegistration | Usage | Warning | `ResiliencePipeline<T>` (arity-1 generic) referenced anywhere — DI registration, parameter, field, or local — instead of the non-generic string-keyed `ResiliencePipeline` |
| SK0015 | StreamPipelineBehaviorMisregistration | Usage | Warning | `AddTransient`/`AddScoped`/`AddSingleton<IPipelineBehavior<,>, T>()` where `T` implements `IStreamPipelineBehavior<,>`, outside the `AddStreamingBehaviors()` method |
| SK0016 | RequestTypeShortNameUsage | Design | Warning | `typeof(X).Name` used standalone (no `.FullName ??` companion) inside `SharedKernel.Application`/`SharedKernel.Application.Behaviors` namespaces |

No SK ID assigned to the `RequestDurationRecordsIncludeOutcomeTag` check — it is a NetArchTest `ICustomRule`, consistent with the `HealthCheckTagIntegrityRules` (WO-027 P-173) precedent of SK-less rules for tag/instrumentation completeness checks.

### Implementation Rules

1. SK0014 is syntax-only (no `SemanticModel`) — the arity-1 `ResiliencePipeline<T>` generic-name form is textually distinguishable from the correct arity-0 `ResiliencePipeline` simple name; fires globally, no namespace exemption.
2. SK0015 requires `SemanticModel.GetSymbolInfo` on both DI-registration type arguments to resolve whether the second type argument implements `MediatR.IStreamPipelineBehavior<,>` — a naming-heuristic approach (mirroring SK0708's `"BatchConsumer"` substring convention) was deliberately rejected here because the five known streaming behavior names are a convention, not a structural guarantee, and a semantic check avoids the false-negative risk of a future streaming behavior not following the `Stream*` naming prefix. The `AddStreamingBehaviors` self-exemption check remains syntax-only and short-circuits before the semantic-model call.
3. SK0016 is namespace-scoped as a trigger-IN condition (`SharedKernel.Application`/`SharedKernel.Application.Behaviors`) — the inverse of the usual trigger-everywhere-except-exemption shape used by SK0001/SK0007/SK0013 — because the `typeof(X).Name` collision risk this rule targets is intrinsic to MediatR request-type tag/key construction, which lives exclusively in this domain.
4. `RequestDurationRecordMissingOutcomeTagPredicate` reuses the `Ldstr` literal-collection IL technique from `HealthCheckTagIntegrityRules` (WO-027 P-173) — no new Mono.Cecil technique, only a new call-site search target (`Histogram<T>.Record`, first use of this search target in the domain). It is a method-level co-occurrence check (not data-flow), same documented-limitation philosophy as `HealthCheckTagIntegrityRules`.
5. The `MetricsInstrumentationRules` rule is designed and tested against CONTRIVED in-memory fixtures ONLY for this phase. It is EXPECTED TO FAIL if pointed at the real `SharedKernel.Application.Behaviors` assembly until a companion `05.Application` phase retrofits the non-streaming `MetricsBehavior<,>` (P-217) to emit the `"outcome"` tag. Retrofitting `MetricsBehavior<,>` is production code in `05.Application` and is explicitly OUT OF SCOPE for `00.Governance` — this domain writes no implementation files for other domains (see Cross-Domain Dependencies below).
6. All three new analyzers target `netstandard2.0` and pin `Microsoft.CodeAnalysis.CSharp 4.14.0`, matching every prior SK analyzer. No new NuGet dependency for the NetArchTest predicate — reuses the existing `Mono.Cecil >= 0.11.5` reference.

### File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Analyzers/SK0014ClosedGenericResiliencePipelineRegistrationAnalyzer.cs` | SharedKernel.Analyzers | Create | Flags `ResiliencePipeline<T>` (arity-1) usage anywhere |
| `Analyzers/SK0015StreamPipelineBehaviorMisregistrationAnalyzer.cs` | SharedKernel.Analyzers | Create | Flags `IStreamPipelineBehavior<,>` implementors registered against `IPipelineBehavior<,>` outside `AddStreamingBehaviors()` |
| `Analyzers/SK0016RequestTypeShortNameUsageAnalyzer.cs` | SharedKernel.Analyzers | Create | Flags standalone `typeof(X).Name` inside `SharedKernel.Application*` |
| `Predicates/RequestDurationRecordMissingOutcomeTagPredicate.cs` | SharedKernel.ArchitectureTests | Create | IL Ldstr-literal-collection check for `Histogram<T>.Record` call sites missing an `"outcome"` tag |
| `Rules/MetricsInstrumentationRules.cs` | SharedKernel.ArchitectureTests | Create | `RequestDurationRecordsIncludeOutcomeTag(Assembly)` factory method wiring the predicate into a `ConditionList` |
| `SharedKernel.Analyzers.Tests/SK0014*Tests.cs`, `SK0015*Tests.cs`, `SK0016*Tests.cs` | SharedKernel.Analyzers.Tests | Create | Fire-path + pass-path tests per analyzer |
| `SharedKernel.ArchitectureTests.Tests/MetricsInstrumentationRulesTests.cs` | SharedKernel.ArchitectureTests.Tests | Create | Fire-path + pass-path tests against contrived in-memory fixtures |
| `00.Governance/CLAUDE.md` | — | Modify | Diagnostic registry, architecture test contracts, implementation rules, changelog (already applied) |

### Acceptance Criteria

- [ ] SK0014 fires on `AddSingleton<ResiliencePipeline<TResponse>>()` (and any other arity-1 `ResiliencePipeline<T>` usage) and does not fire on the non-generic `ResiliencePipeline` form
- [ ] SK0015 fires on an `IStreamPipelineBehavior<,>`-implementing type registered via `AddTransient(typeof(IPipelineBehavior<,>), typeof(StreamXxxBehavior<,>))` outside `AddStreamingBehaviors()`, and does not fire on registrations inside that method or on unary `IPipelineBehavior<,>` registrations
- [ ] SK0016 fires on standalone `typeof(TRequest).Name` inside `SharedKernel.Application*` namespaces and does not fire on the `typeof(TRequest).FullName ?? typeof(TRequest).Name` pattern or on `typeof(X).Name` usage outside those namespaces
- [ ] `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag` fires (contrived fixture) on a `Histogram<T>.Record` call with no `"outcome"` Ldstr literal in the same method body, and passes (contrived fixture) when the literal is present
- [ ] All three new SK IDs (SK0014, SK0015, SK0016) and the new `MetricsInstrumentationRules`/`RequestDurationRecordMissingOutcomeTagPredicate` are recorded in `00.Governance/CLAUDE.md` — diagnostic registry, architecture test contracts, implementation rules, and changelog
- [ ] Full `SharedKernel.ArchitectureTests.Tests` and `SharedKernel.Analyzers.Tests` suites remain green after the new tests land (baseline: 125/125 architecture tests as of SK.00.CryptoDelegationAndUowSeamGuard closeout, plus the existing analyzer suite) — the new `MetricsInstrumentationRules` test suite uses contrived fixtures only, so it does not turn red against the real (not-yet-retrofitted) `MetricsBehavior<,>`

### Dependencies

- Requires nothing from `05.Application` to design or implement this phase — all four checks are designed and tested against contrived in-memory fixtures, consistent with the standing pattern for governance phases whose triggering domain has already shipped or is mid-flight (WO-036 P-225, WO-037 P-229).
- Depends on `05.Application` for **real-assembly verification only** of `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag` — this rule is EXPECTED TO FAIL against the real `SharedKernel.Application.Behaviors` assembly until a companion `05.Application` work order retrofits `MetricsBehavior<,>` (P-217) to emit the `"outcome"` tag on `RequestDuration`, matching `StreamMetricsBehavior`'s (P-234) existing tag shape. Retrofitting `MetricsBehavior<,>` itself is 05.Application production code and is explicitly NOT performed by this governance phase — `00.Governance` writes planning/enforcement artifacts only, never implementation files for another domain.
- Depends on `05.Application`'s already-shipped `MediatR.IPipelineBehavior<,>`/`IStreamPipelineBehavior<,>` and `ApplicationBehaviorsBuilder.AddStreamingBehaviors()` shapes (P-217, P-231, P-234 — all complete per `05.Application/state-map.md` as of 2026-07-02) for real-assembly re-verification of SK0015 once the implementer chooses to run it against the real assembly (SK0015 is a Roslyn analyzer and fires at compile time against any consuming project, so no explicit "real-assembly wiring" step is required the way NetArchTest rules need one).
- Unblocks: gives `05.Application` a governance-owned, pre-built enforcement mechanism to wire the `MetricsInstrumentationRules` check into its own test suite once the `MetricsBehavior<,>` outcome-tag retrofit ships — consistent with this domain's standing practice of shipping the mechanism ahead of (or alongside) the triggering domain's implementation.

### Tooling Version Notes

- `Microsoft.CodeAnalysis.CSharp`: 4.14.0 (existing pin — SK0014/SK0015/SK0016 reuse it, no version change)
- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- `Mono.Cecil`: >= 0.11.5 (existing pin — `RequestDurationRecordMissingOutcomeTagPredicate` reuses the existing reference)
- Target framework: `netstandard2.0` (Analyzers) / `net10.0` (ArchitectureTests)

### SK.00.MetricsOutcomeTagAndMisregistrationGuard — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-57 | Define trigger/fix/suppress shape for SK0014 (arity-1 `ResiliencePipeline<T>` generic-name match), SK0015 (semantic-model interface-implementation check + method-name self-exemption), and SK0016 (namespace-scoped `typeof(X).Name` without `.FullName` companion); define `RequestDurationRecordMissingOutcomeTagPredicate` shape (`Histogram<T>.Record` call-site scan + companion `Ldstr "outcome"` check) and `MetricsInstrumentationRules` factory signature | SharedKernel.Analyzers, SharedKernel.ArchitectureTests | `●` |
| C-90 | Implement SK0014 `ClosedGenericResiliencePipelineRegistrationAnalyzer` | SharedKernel.Analyzers | `●` |
| C-91 | Implement SK0015 `StreamPipelineBehaviorMisregistrationAnalyzer` (semantic-model based) | SharedKernel.Analyzers | `●` |
| C-92 | Implement SK0016 `RequestTypeShortNameUsageAnalyzer` | SharedKernel.Analyzers | `●` |
| C-93 | Implement `RequestDurationRecordMissingOutcomeTagPredicate` in `Predicates/` | SharedKernel.ArchitectureTests | `●` |
| C-94 | Implement `MetricsInstrumentationRules` static class in `Rules/`, wiring the predicate into a `ConditionList` factory method | SharedKernel.ArchitectureTests | `●` |
| T-161 | Analyzer test (fire path): `AddSingleton<ResiliencePipeline<TResponse>>()` triggers SK0014 | SharedKernel.Analyzers.Tests | `●` |
| T-162 | Analyzer test (pass path): non-generic `ResiliencePipeline` keyed registration does not trigger SK0014 | SharedKernel.Analyzers.Tests | `●` |
| T-163 | Analyzer test (fire path): a contrived `IStreamPipelineBehavior<,>` implementor registered via `AddTransient(typeof(IPipelineBehavior<,>), typeof(StreamFixtureBehavior<,>))` outside `AddStreamingBehaviors()` triggers SK0015 | SharedKernel.Analyzers.Tests | `●` |
| T-164 | Analyzer test (pass path): the same registration inside a method named `AddStreamingBehaviors`, and a plain `IPipelineBehavior<,>`-only implementor registered anywhere, do not trigger SK0015 | SharedKernel.Analyzers.Tests | `●` |
| T-165 | Analyzer test (fire path): standalone `typeof(TRequest).Name` inside a `SharedKernel.Application`-namespaced fixture triggers SK0016 | SharedKernel.Analyzers.Tests | `●` |
| T-166 | Analyzer test (pass path): `typeof(TRequest).FullName ?? typeof(TRequest).Name` inside the same namespace, and standalone `typeof(X).Name` outside `SharedKernel.Application*`, do not trigger SK0016 | SharedKernel.Analyzers.Tests | `●` |
| T-167 | Architecture test (fire path): contrived fixture with a `Histogram<double>.Record(...)` call and no `"outcome"` Ldstr literal in the same method; assert `RequestDurationRecordsIncludeOutcomeTag` fails | SharedKernel.ArchitectureTests.Tests | `●` |
| T-168 | Architecture test (pass path): contrived fixture with a `Histogram<double>.Record(...)` call carrying an `"outcome"` Ldstr literal in the same method; assert the rule passes | SharedKernel.ArchitectureTests.Tests | `●` |
| DO-29 | Verify SK0014, SK0015, SK0016, `MetricsInstrumentationRules`, and `RequestDurationRecordMissingOutcomeTagPredicate` documentation in `00.Governance/CLAUDE.md` (diagnostic registry, architecture test contracts, implementation rules — pre-written by this planning pass) against the final implementation; add closeout Changelog entry | SharedKernel.Analyzers, SharedKernel.ArchitectureTests | `●` |

---

<!-- phase-key: SK.00.DomainEventDispatcherReflectionExemption -->
## Phase SK.00.DomainEventDispatcherReflectionExemption — Governance: Register MediatRDomainEventDispatcher's SK0012 Reflection Exemption

### Goal

`SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher.PublishSingle` calls `MethodInfo.MakeGenericMethod` to build a cached, closed-generic MediatR publish delegate per concrete runtime `IDomainEvent` type — a documented, deliberate exception to the platform-wide SK0012 prohibition, explicitly modeled on the already-accepted `07.Messaging.MassTransitEventPublisher.BuildPublisher` precedent for "publish-by-runtime-type through a generic API" (both `05.Application/CLAUDE.md` and the type's own XML docs say so). The *only* sanctioned exception mechanism for SK0012 is registration in `ReflectionExemptionRegistry` — no `#pragma`, `[SuppressMessage]`, or inline comment is accepted — and the registry has shipped empty since its introduction in WO-024 P-153. This phase registers the exemption pre-emptively, before any future phase points `ReflectionGuardRules.NoMakeGenericMethodReflection` at the real `SharedKernel.Application` assembly, so that wiring does not break the build against a call site the platform has already, independently decided is legitimate. Registry-entry-only — no change to `NoMakeGenericMethodReflectionPredicate` or `ReflectionGuardRules` logic.

### Scope

- Package(s) affected: `SharedKernel.ArchitectureTests` (registry entry + one new real-assembly test class in `SharedKernel.ArchitectureTests.Tests`)
- New files: `SharedKernel.ArchitectureTests.Tests/ReflectionGuardRulesRealAssemblyTests.cs` (or an added test class/method inside the existing `ReflectionGuardRulesTests.cs` — implementer's choice; document whichever is used)
- Modified files: `SharedKernel.ArchitectureTests/ReflectionExemptionRegistry.cs` (add one `AllowList` entry), `00.Governance/CLAUDE.md`
- Deleted files: none

### Diagnostic Registry Changes (analyzers only)

None. No new SK ID. SK0012 (`MakeGenericMethodReflection`) is unchanged — this phase only populates its exemption allow-list. The SK0012 registry entry's "Note" in `00.Governance/CLAUDE.md` is corrected (see Implementation Rules) to retract the previously inaccurate "all production assemblies pass this rule" claim.

### Implementation Rules

1. **Do not hand-guess the registry key.** `PublishSingle`'s `MakeGenericMethod` call is inside `PublishDelegateCache.GetOrAdd(eventType, static t => {...})` — a closure-free `static` lambda. Roslyn compiles closure-free static lambdas onto a compiler-generated `<>c` singleton cache class nested inside the declaring type (Mono.Cecil `TypeDefinition.FullName` uses `/` as the nested-type separator), with a synthesized method name shaped like `<PublishSingle>b__{token}_{ordinal}` — not the literal `MediatRDomainEventDispatcher`/`PublishSingle` pair a source-level reading suggests. The exact ordinal is not knowable without compiling.
2. **Derive the exact pair empirically, red-then-green.** Add the real-assembly test FIRST, pointed at the compiled `SharedKernel.Application.dll`, with the exemption absent (or temporarily removed via the existing `internal Unregister` test hook, mirroring T-115's pattern). Read the exact `{TypeDefinition.FullName}.{method.Name}` values out of `ReflectionGuardRules`'s documented failure message. Copy those verified values — not assumed ones — into `ReflectionExemptionRegistry.AllowList`. Then flip the test to assert the passing case.
3. **Governance rationale documentation.** `AllowList` is a `private static readonly HashSet<(string,string)>` field initializer — individual tuple elements cannot carry a compiler-recognized `///` XML doc comment (only member/type declarations can). Satisfy the class's own "written XML doc comment" requirement with a clearly demarcated block comment (`// ===== WO-039 Exemption: MediatRDomainEventDispatcher =====`) immediately above the added tuple, stating: the rationale (runtime-only event-type dispatch, mirroring the accepted `MassTransitEventPublisher.BuildPublisher` precedent — see Implementation Rule 5 for that precedent's own unregistered status), the approving work order (WO-039) and date (2026-07-03), and the reviewing team member. This is the FIRST real entry in the registry — the comment-block format established here is the template every future SK0012 exemption request should follow, since `///` cannot be used on collection-initializer elements.
4. No change to `NoMakeGenericMethodReflectionPredicate` or `ReflectionGuardRules` — this phase is additive to `ReflectionExemptionRegistry` and to the test suite only, per the phase input's explicit constraint.
5. **Known open gap, out of scope for this phase.** Direct source inspection during design (2026-07-03) confirms `07.Messaging`'s `SharedKernel.Messaging.MassTransit.MassTransitEventPublisher.BuildPublisher` (the structurally identical lambda-closure `MakeGenericMethod` pattern cited as this exemption's own precedent) and `MessagingBusBuilder.AddActivity` (a second, differently-shaped `GetMethods().Single(...).MakeGenericMethod(...)` call) are themselves UNREGISTERED in `ReflectionExemptionRegistry` today. Both will break the build the instant `ReflectionGuardRules.NoMakeGenericMethodReflection` is ever pointed at the real `SharedKernel.Messaging.MassTransit` assembly. This phase does not register either — P-240's own acceptance criteria scope it to `05.Application` only. Flagged here as a candidate follow-up work order so a future "wire SK0012 into every production assembly" phase is not surprised by it.
6. `SharedKernel.Application`'s assembly already exists and is buildable (C-06 complete per `05.Application/state-map.md`, confirmed 2026-07-03) — unlike several prior governance phases that had to design against contrived fixtures because the triggering domain hadn't shipped yet (WO-027 P-173, WO-036 P-225, WO-037 P-229), this phase uses the REAL compiled assembly directly as a test-fixture input, not a contrived stand-in.
7. `SharedKernel.ArchitectureTests.Tests` taking a `ProjectReference` on `SharedKernel.Application` for this one test is a test-only dependency, not a production reference — it does not violate the "`00.Governance` references nothing" layering rule, which governs production package references only (the same reasoning already applied to T-105/T-107/T-109/T-111/T-112/T-128, which reference real packages from other domains as pass-path fixtures).

### File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `ReflectionExemptionRegistry.cs` | SharedKernel.ArchitectureTests | Modify | Add the verified `(typeFullName, methodName)` `AllowList` entry for `MediatRDomainEventDispatcher`'s `MakeGenericMethod` call site, with the governance rationale block comment |
| `ReflectionGuardRulesRealAssemblyTests.cs` (or equivalent addition) | SharedKernel.ArchitectureTests.Tests | Create/Modify | Real-assembly fire-path (exemption absent) and pass-path (exemption present) tests against the compiled `SharedKernel.Application` assembly |
| `00.Governance/CLAUDE.md` | — | Modify | `ReflectionExemptionRegistry`/SK0012 documentation updated to record the first real entry and correct the prior "all assemblies pass" claim; changelog entry |

### Acceptance Criteria

- [ ] `ReflectionExemptionRegistry.AllowList` contains the empirically-verified `(typeFullName, methodName)` entry for `MediatRDomainEventDispatcher`'s `MakeGenericMethod`-calling method, with a governance rationale block comment stating the rationale, WO-039, and the approval date
- [ ] A new architecture test points `ReflectionGuardRules.NoMakeGenericMethodReflection` at the real, compiled `SharedKernel.Application` assembly and asserts it PASSES with the exemption registered
- [ ] A companion test proves the exemption is load-bearing: with the entry temporarily unregistered (via the existing `internal Unregister` test hook), the same real-assembly check FAILS — ruling out an accidental vacuous pass
- [ ] `00.Governance/CLAUDE.md`'s `ReflectionExemptionRegistry` documentation is updated to state the registry is no longer empty and records this first real entry; the SK0012 "all production assemblies pass" note is corrected to reflect that the rule has never actually been run against every production assembly, and the 07.Messaging open gap (Implementation Rule 5) is recorded
- [ ] No change to `NoMakeGenericMethodReflectionPredicate` or `ReflectionGuardRules` logic — verified by diff review
- [ ] Full `SharedKernel.ArchitectureTests.Tests` suite remains green after the new tests land (baseline: 127/127 as of `SK.00.MetricsOutcomeTagAndMisregistrationGuard` closeout, 2026-07-03; expect 129/129 after this phase's two new tests)

### Dependencies

- Requires `05.Application` C-06 (`MediatRDomainEventDispatcher`) to exist as a buildable assembly — already complete (`05.Application/state-map.md`, confirmed 2026-07-03). No further cross-domain work needed to implement this phase.
- Unblocks: any future phase that wires `ReflectionGuardRules.NoMakeGenericMethodReflection` into a real, all-production-assemblies check (referenced by the P-240 requirement as motivation) can now include `SharedKernel.Application` without a build break.
- Does NOT unblock the same wiring for `07.Messaging` — see Implementation Rule 5's open gap. That is tracked as a candidate follow-up, not a dependency of this phase.

### Tooling Version Notes

- `Mono.Cecil`: >= 0.11.5 (existing pin — no change; the real-assembly test reuses the existing `ReflectionGuardRules` factory method unchanged)
- `NetArchTest.eNt`: >= 1.3.2 (existing pin — no change)
- Target framework: `net10.0` (`SharedKernel.ArchitectureTests`, `SharedKernel.ArchitectureTests.Tests`); no `SharedKernel.Analyzers` involvement in this phase

### SK.00.DomainEventDispatcherReflectionExemption — Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-58 | Define the empirical-verification procedure (red-then-green against the real compiled assembly) for determining the exact `TypeDefinition.FullName`/`MethodDefinition.Name` pair for `MediatRDomainEventDispatcher`'s `MakeGenericMethod` call site; do not assume the source-level declaring type/method name given the closure-free static-lambda compilation behavior | SharedKernel.ArchitectureTests | `○` |
| C-95 | Add the verified `(typeFullName, methodName)` entry to `ReflectionExemptionRegistry.AllowList` with the governance rationale block comment (rationale, WO-039, approval date, reviewer) | SharedKernel.ArchitectureTests | `○` |
| T-169 | Architecture test (real-assembly, fire path): `ReflectionGuardRules.NoMakeGenericMethodReflection` against the real `SharedKernel.Application` assembly FAILS when the `MediatRDomainEventDispatcher` entry is temporarily unregistered (via the existing `internal Unregister` test hook) — proves the exemption is load-bearing, not a vacuous pass | SharedKernel.ArchitectureTests.Tests | `○` |
| T-170 | Architecture test (real-assembly, pass path): `ReflectionGuardRules.NoMakeGenericMethodReflection` against the real `SharedKernel.Application` assembly PASSES with the registered exemption in place | SharedKernel.ArchitectureTests.Tests | `○` |
| DO-30 | Update `00.Governance/CLAUDE.md`: record the first real `ReflectionExemptionRegistry` entry, correct the prior "all production assemblies pass this rule" note on SK0012 to reflect actual verification status, document the closure-free-static-lambda Mono.Cecil naming fact as a reusable implementation note, and record the open `07.Messaging` gap (Implementation Rule 5); add Changelog entry | SharedKernel.ArchitectureTests | `○` |

---

## Cross-Domain Dependencies

_No active cross-domain dependencies for prior phases. `00.Governance` references nothing in production code._

`SK.00.MetricsOutcomeTagAndMisregistrationGuard` depends on `05.Application` for **real-assembly verification only**:

| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
| `SK.00.MetricsOutcomeTagAndMisregistrationGuard` | `05.Application` | `MetricsBehavior<,>` (P-217) retrofitted to emit an `"outcome"` tag on `RequestDuration`, matching `StreamMetricsBehavior`'s (P-234) existing shape — required only to re-point `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag` at the real `SharedKernel.Application.Behaviors` assembly; not required for this phase's own design/implementation/tests, which use contrived fixtures | `○` Pending (05.Application retrofit not yet dispatched) |

<!--
Format when active:
| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
-->

---

## Overall Progress

> Counts updated whenever a task state changes. Total tasks: 393.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.00.Design` | Design | 29 | 29 | 0 | `●` |
| `SK.00.Scaffold` | Scaffold | 10 | 10 | 0 | `●` |
| `SK.00.Core` | Core | 39 | 39 | 0 | `●` |
| `SK.00.Tests` | Tests | 51 | 51 | 0 | `●` |
| `SK.00.Docs` | Docs | 12 | 12 | 0 | `●` |
| `SK.00.Published` | Published | 6 | 6 | 0 | `●` |
| `SK.00.GuardPurity` | Guard Purity Enforcement | 11 | 11 | 0 | `●` |
| `SK.00.CachingEnforcement` | Caching Abstractions Enforcement | 11 | 11 | 0 | `●` |
| `SK.00.DomainLayerPurity` | Domain Layer Purity Enforcement | 16 | 16 | 0 | `●` |
| `SK.00.DomainGoldStandard` | Domain Gold-Standard Architecture Rules | 19 | 19 | 0 | `●` |
| `SK.00.ContractsPurity` | Contracts Layer Purity Architecture Rules | 13 | 13 | 0 | `●` |
| `SK.00.PersistenceEnforcement` | Persistence Architecture Enforcement | 13 | 13 | 0 | `●` |
| `SK.00.PersistenceEnforcement2` | Persistence Architecture Rules Phase 2 — Interface Migration Enforcement | 14 | 14 | 0 | `●` |
| `SK.00.PersistenceContractCompleteness` | Architecture Rule — IUserContext Audit String Adapter and Repository Contract Completeness | 15 | 15 | 0 | `●` |
| `SK.00.EfCorePackageHygiene` | Governance: Architecture Rules for EfCore Package Hygiene | 13 | 13 | 0 | `●` |
| `SK.00.TenantedDbContextGuard` | Governance: TenantedDbContext Tenant-Filter Guard Architecture Rule | 13 | 13 | 0 | `●` |
| `SK.00.EncryptionPatternGuard` | Governance: Architecture Rules for DB Encryption Pattern Correctness | 14 | 14 | 0 | `●` |
| `SK.00.MessagingArchRules` | Governance: Messaging Architecture Rules — No Raw IBus Injection, No IMessageBus Singleton, No Domain Messaging, No Hardcoded Queue URIs | 20 | 20 | 0 | `●` |
| `SK.00.ExtendedMessagingArchRules` | Governance: Extended Messaging Architecture Rules — Fault Consumers, Scheduling, Singleton Guards | 20 | 20 | 0 | `●` |
| `SK.00.RedisTopology` | Governance: Architecture Rules for Redis Package Topology | 16 | 16 | 0 | `●` |
| `SK.00.ReflectionGuard` | Governance: Architecture Rule Forbidding Reflection-Based Generic Method Invocation | 7 | 7 | 0 | `●` |
| `SK.00.CommunicationArchRules` | Governance: Architecture Rules for Communication Layer | 16 | 16 | 0 | `●` |
| `SK.00.WO026CommunicationQuality` | Governance: Architecture Rules for WO-026 Communication Quality Improvements | 5 | 5 | 0 | `●` |
| `SK.00.ServiceDefaultsGovernance` | Governance: ServiceDefaults Liveness/Readiness and Composition-Root Layering Rules | 16 | 16 | 0 | `●` |
| `SK.00.HealthCheckConstantsGuard` | Governance: Architecture Rule Banning Bare Health-Check String Literals Where a Constants Class Exists | 9 | 9 | 0 | `●` |
| `SK.00.PresentationArchRules` | Governance: Architecture Rules Banning Hand-Rolled ProblemDetails and Inline Result-to-HTTP Branching | 9 | 9 | 0 | `●` |
| `SK.00.ApplicationPipelineArchRules` | Governance: Architecture Enforcement for the Extended Application Pipeline | 13 | 13 | 0 | `●` |
| `SK.00.CryptoDelegationAndUowSeamGuard` | Governance: Architecture Rules Locking the Cryptography Delegation and IUnitOfWork Bridge | 14 | 14 | 0 | `●` |
| `SK.00.MetricsOutcomeTagAndMisregistrationGuard` | Governance: Architecture Enforcement for WO-038 Application Audit Findings | 15 | 15 | 0 | `●` |
| `SK.00.DomainEventDispatcherReflectionExemption` | Governance: Register MediatRDomainEventDispatcher's SK0012 Reflection Exemption | 5 | 0 | 5 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-05-15] Sub state-map initialized — phase key registry, all 6 phases scaffolded at ○ (51 tasks total)
- [2026-05-15] Phase Guard Purity Enforcement added (SK.00.GuardPurity) — 11 tasks across Design (D-11, D-12), Core (C-15, C-16, C-17), Tests (T-13–T-17), Docs (DO-06); SK0006 GuardClauseThrow registered; total tasks now 62 — WO-002 P-004
- [2026-05-15] D-01–D-12 → ● in SK.00.Design — all 12 design tasks complete, diagnostic registry and contracts finalized (state-map-phase)
- [2026-05-15] S-01–S-10 → ● in SK.00.Scaffold — all 10 scaffold tasks complete, all packages wired and solution registered (state-map-phase)
- [2026-05-15] C-01–C-14 → ● in SK.00.Core — all 14 core tasks complete, analyzers SK0001–SK0005, arch rules, benchmark config, linter content authored (state-map-phase)
- [2026-05-15] T-01–T-12 → ● in SK.00.Tests — all 12 test tasks complete, analyzer tests SK0001–SK0005 and arch layering rule test authored (state-map-phase)
- [2026-05-15] DO-01–DO-05 → ● in SK.00.Docs — XML docs verified on all public APIs; README.md written with rule docs, usage guides, and SK0001–SK0005 entries (state-map-phase)
- [2026-05-15] P-01–P-06 → ● in SK.00.Published — NuGet metadata added, packages packed to local feed, SK0001 verified firing in consumer (state-map-phase)
- [2026-05-15] C-15–C-17, T-13–T-17, DO-06 → ● in SK.00.GuardPurity — all 11 tasks complete, GuardPurity phase fully done (state-map-phase)
- [2026-05-18] Phase Caching Abstractions Enforcement added (SK.00.CachingEnforcement) — 11 tasks: D-13–D-14, C-18–C-19, T-18–T-22, DO-07–DO-08; SK0007 RedisChannelServiceMessagingSubstitute registered; CachingAbstractionRules arch predicate defined; total tasks now 73 — WO-003 P-009
- [2026-05-22] Phase Domain Layer Purity Enforcement added (SK.00.DomainLayerPurity) — 16 tasks: D-15–D-18, C-20–C-23, T-23–T-30, DO-09; four NetArchTest predicates in DomainLayerPurityRules; three new ICustomRule predicates (DoesNotImplementOpenGenericInterfacePredicate, DoesNotCallSystemClockPredicate, NoInfrastructureConstructorParametersPredicate); no new SK IDs; total tasks now 89 — WO-008 P-034
- [2026-05-22] D-13–D-18 → ● in SK.00.Design — all 6 remaining design tasks verified complete (specs fully present in CLAUDE.md); SK.00.Design promoted to ● with 18/18 tasks done (state-map-phase)
- [2026-05-30] Phase Domain Gold-Standard Architecture Rules added (SK.00.DomainGoldStandard) — 19 tasks: D-19–D-22, C-24–C-27, T-31–T-39, DO-10; SK0008 AggregateRootDispatchCoupling, SK0009 DomainEventMissingVersionAttribute, SK0010 SpecificationOrderingConflict registered; DomainGoldStandardRules arch predicate defined; total tasks now 144 — WO-011 P-056
- [2026-05-30] Phase Contracts Layer Purity Architecture Rules added (SK.00.ContractsPurity) — 13 tasks: D-23–D-26, C-28–C-29, T-40–T-45, DO-11; four NetArchTest predicates in ContractsPurityRules; NoNonTrivialMethodsPredicate ICustomRule; no new SK IDs; Rule 5 documentation-only guideline — WO-012 P-063
- [2026-05-31] D-19–D-26 → ● in SK.00.Design — all 8 remaining design tasks complete; SK.00.Design promoted to ● (state-map-phase)
- [2026-05-31] C-18–C-19, T-18–T-22 → ● in SK.00.CachingEnforcement; C-20–C-23, T-23–T-30 → ● in SK.00.DomainLayerPurity; C-24–C-27, T-31–T-39 → ● in SK.00.DomainGoldStandard; C-28–C-29, T-40–T-45 → ● in SK.00.ContractsPurity (state-map-phase)
- [2026-05-31] DO-07–DO-11 → ● in SK.00.Docs — README.md extended with SK0007–SK0010 Roslyn analyzer docs, CachingAbstractionRules, DomainLayerPurityRules, DomainGoldStandardRules, ContractsPurityRules architecture test docs; Overall Progress table corrected for Core/Tests/Docs/CachingEnforcement/DomainLayerPurity/DomainGoldStandard/ContractsPurity — all phase keys now ● (state-map-phase)
- [2026-06-01] Phase Persistence Architecture Enforcement added (SK.00.PersistenceEnforcement) — 13 tasks: D-27–D-29, C-30–C-32, T-46–T-51, DO-12; three NetArchTest predicates in PersistenceLayerProtectionRules; two new ICustomRule predicates (NoDirectSaveChangesPredicate, NoIQueryableReturnPredicate); no new SK IDs; total tasks now 157 — WO-013 P-075
- [2026-06-02] D-27–D-29 → ● in SK.00.Design — all three persistence enforcement design tasks complete; specs fully present in CLAUDE.md (PersistenceLayerProtectionRules, NoDirectSaveChangesPredicate, NoIQueryableReturnPredicate); SK.00.Design promoted to ● with 29/29 tasks done (state-map-phase)
- [2026-06-02] Phase Persistence Architecture Rules Phase 2 added (SK.00.PersistenceEnforcement2) — 14 tasks: D-30, C-33–C-35, T-52–T-59, DO-13; four new NetArchTest predicates in PersistenceInterfaceOwnershipRules; two new ICustomRule predicates (InterfaceDeclarationOwnershipPredicate, NoGetByIdOnReadRepositoryPredicate); no new SK IDs; total tasks now 171 — WO-014 P-083
- [2026-06-02] Phase Architecture Rule — IUserContext Audit String Adapter and Repository Contract Completeness added (SK.00.PersistenceContractCompleteness) — 15 tasks: D-31–D-32, C-36–C-38, T-60–T-66, DO-14; SK0011 GuidFormatCodeMisuse registered; RepositoryContractCompletenessRules arch predicates defined; HasRequiredMethodPredicate ICustomRule; total tasks now 186 — WO-016 P-096
- [2026-06-03] C-30, C-31, C-32 → ● in SK.00.Core — NoDirectSaveChangesPredicate, NoIQueryableReturnPredicate, PersistenceLayerProtectionRules implemented; SK.00.Core promoted to ● (state-map-phase)
- [2026-06-03] Phase EfCore Package Hygiene Architecture Rules added (SK.00.EfCorePackageHygiene) — 13 tasks: D-33–D-35, C-39–C-42, T-67–T-72, DO-15; three NetArchTest predicates in EfCorePackageHygieneRules; three new ICustomRule predicates (NoSpecificationEvaluatorDowncastPredicate, SingleConstructorPredicate, NoDbContextTransactionInApplicationPredicate); no new SK IDs; total tasks now 199 — WO-017 P-103
- [2026-06-04] Phase TenantedDbContext Tenant-Filter Guard added (SK.00.TenantedDbContextGuard) — 13 tasks: D-36–D-38, C-43–C-44, T-73–T-78, DO-16; SK0201 TenantedDbContextOnModelCreatingGuard and SK0202 IgnoreQueryFiltersOutsideTenantedRepository registered; two Roslyn analyzers closing the silent multi-tenancy misconfiguration gap; total tasks now 212 — WO-018 P-110
- [2026-06-04] T-46–T-51 → ● in SK.00.PersistenceEnforcement — PersistenceLayerProtectionRules tests: all 6 fire/pass paths for Rules 1–3 passing; 12/13 done (state-map-phase)
- [2026-06-04] Phase Governance: Architecture Rules for DB Encryption Pattern Correctness added (SK.00.EncryptionPatternGuard) — 14 tasks: D-39, C-45–C-49, T-79–T-86, DO-17; SK0301 DirectCryptoInDomainOrApplication, SK0302 EncryptionAttributeOnDomainEntity, SK0303 EncryptionRotationJobInDomainOrApplication, SK0304 DirectEncryptedValueConverterInstantiation registered (new 03xx ID block for encryption-domain rules); four NetArchTest predicates in EncryptionPatternGuardRules; four new ICustomRule predicates (NoAesCipherInDomainOrApplicationPredicate, NoEncryptionAttributeOnDomainEntityPredicate, NoEncryptionRotationJobInjectionPredicate, NoDirectEncryptedValueConverterInstantiationPredicate); total tasks now 226 — WO-019 P-114
- [2026-06-04] SK.00.Tests Overall Progress count corrected — stale count showed 45/51; actual state is 51/51 (T-46–T-51 were marked ● in prior session for SK.00.PersistenceEnforcement but SK.00.Tests cumulative counter was not refreshed); SK.00.Tests promoted to ● — housekeeping pass
- [2026-06-04] DO-12 → ● in SK.00.Docs — PersistenceLayerProtectionRules documented in 00.Governance/README.md: all three predicates (OnlyEfUnitOfWorkMayCallSaveChanges, RepositoriesMustNotExposeIQueryable, DomainAssembliesNeverReferencePersistenceStack) with rationale, offending/compliant examples, and cross-references to root CLAUDE.md hard layering rules; SK.00.Docs phase now 12/12 ● (state-map-phase)
- [2026-06-04] SK.00.PersistenceEnforcement Overall Progress count corrected — stale counter showed 12/13 (◐); all 13 tasks (D-27–D-29, C-30–C-32, T-46–T-51, DO-12) confirmed ● in task table; counter refreshed to 13/13 (0 pending); SK.00.PersistenceEnforcement promoted to ● — housekeeping pass
- [2026-06-05] D-33–D-35, C-39–C-42, T-67–T-72, DO-15 → ● in SK.00.EfCorePackageHygiene — all 13 tasks complete; EfCorePackageHygieneRules + 3 ICustomRule predicates implemented; 6/6 tests passing (state-map-phase)
- [2026-06-05] D-30, C-33–C-35, T-52–T-59, DO-13 → ● in SK.00.PersistenceEnforcement2 — all 14 tasks complete; PersistenceInterfaceOwnershipRules, InterfaceDeclarationOwnershipPredicate, NoGetByIdOnReadRepositoryPredicate implemented; 8 tests passing (state-map-phase)
- [2026-06-05] D-31–D-32, C-36–C-38, T-60–T-66, DO-14 → ● in SK.00.PersistenceContractCompleteness — all 13 tasks complete; SK0011 GuidFormatCodeMisuseAnalyzer (semantic model Guid receiver check), HasRequiredMethodPredicate, RepositoryContractCompletenessRules implemented; 10+5 tests passing (state-map-phase)
- [2026-06-05] D-36–D-38, C-43–C-44, T-73–T-78, DO-16 → ● in SK.00.TenantedDbContextGuard — all 13 tasks complete; SK0201 + SK0202 Roslyn analyzers implemented; 12 new tests passing (68 total) (state-map-phase)
- [2026-06-05] D-39, C-45–C-49, T-79–T-86, DO-17 → ● in SK.00.EncryptionPatternGuard — all 14 tasks complete; EncryptionPatternGuardRules + 4 ICustomRule predicates (SK0301–SK0304) implemented; 8 tests passing (57 total) (state-map-phase)
- [2026-06-08] Phase SK.00.MessagingArchRules added — 20 tasks: D-40–D-43, C-50–C-54, T-87–T-95, DO-18; SK0701 NoDirectBusInjectionOutsideMessaging, SK0702 NoEventPublisherInDomainLayer (NetArchTest predicates), SK0703 MessageBusSingletonRegistration, SK0704 HardcodedQueueUriInGetSendEndpoint (Roslyn analyzers); new 07xx messaging ID block; total tasks now 246 — WO-020 P-123
- [2026-06-09] D-40–D-43, C-50–C-54, T-87–T-95, DO-18 → ● in SK.00.MessagingArchRules — NoDirectBusInjectionOutsideMessagingPredicate, NoEventPublisherInDomainLayerPredicate, MessagingArchitectureRules implemented; SK0703 MessageBusSingletonRegistrationAnalyzer, SK0704 HardcodedQueueUriAnalyzer implemented; 78 analyzer tests pass, 62 arch tests pass; SK.00.MessagingArchRules → ● (state-map-phase)
- [2026-06-09] SK.00.MessagingArchRules → ● — all 20 tasks complete; promoted to root state-map (state-map-phase)
- [2026-06-09] Phase SK.00.ExtendedMessagingArchRules added — 20 tasks: D-44–D-47, C-55–C-59, T-96–T-103, DO-19; SK0705 FaultConsumerDirectRegistration, SK0708 BatchConsumerRegisteredViaAddConsumer (Roslyn analyzers); SK0706 DirectMassTransitSchedulerInjection, SK0707 SagaStateMustExtendSagaStateBase (NetArchTest predicates); 07xx messaging block extended; total tasks now 266 — WO-021 P-133
- [2026-06-10] D-44–D-47, C-55–C-59, T-96–T-103, DO-19 → ● in SK.00.ExtendedMessagingArchRules — all 20 tasks complete; SK0705 FaultConsumerDirectRegistrationAnalyzer and SK0708 BatchConsumerRegisteredViaAddConsumerAnalyzer implemented (Roslyn, netstandard2.0, syntax-only); SK0706 NoDirectSchedulerInjectionOutsideMessagingPredicate and SK0707 SagaStateMustExtendSagaStateBasePredicate implemented (NetArchTest ICustomRule, ExtendedMessagingArchitectureRules factory); fixed a SagaStateMustExtendSagaStateBasePredicate bug where SagaStateBase itself (which implements ISaga directly) was incorrectly flagged as a violation — added a self-exemption check; 85 analyzer tests pass, 66 architecture tests pass (0 failures); README.md documented with SK0705–SK0708 sections and a new ExtendedMessagingArchitectureRules architecture-test section; SK.00.ExtendedMessagingArchRules → ● (state-map-phase)
- [2026-06-12] Phase SK.00.RedisTopology added — 16 tasks: D-48, C-60–C-64, T-104–T-112, DO-20; new RedisTopologyRules static class (five ConditionList predicates, no Mono.Cecil, no new SK IDs) enforcing the five-package Redis topology from P-140–P-144 (Redis.Core never references capability packages, capability packages never reference each other, Redis.PubSub never references `SharedKernel.Messaging.*`, `SharedKernel.Messaging.*` never references `SharedKernel.Caching.*`, Caching.Abstractions re-verified dependency-free); total tasks now 282 — WO-023 P-145
- [2026-06-15] D-48, C-60–C-64, T-104–T-112, DO-20 → ● in SK.00.RedisTopology — all 16 tasks complete; corrected NotHaveDependencyOn matching contract (no trailing dots, namespace StartsWith) across all five predicates; CapabilityPackagesNeverReferenceEachOther redesigned to return ConditionList[] with per-package own-term dictionary; 9/9 RedisTopologyRulesTests pass, 75/75 full ArchitectureTests.Tests suite pass, 0 build warnings/errors; README architecture-test docs and implementation rules updated (state-map-phase)
- [2026-06-16] Phase SK.00.ReflectionGuard added — 7 tasks: D-49, C-65–C-66, T-113–T-115, DO-21; SK0012 MakeGenericMethodReflection registered (NetArchTest ICustomRule, no Roslyn analyzer — pattern is IL-only detectable); NoMakeGenericMethodReflectionPredicate + ReflectionExemptionRegistry allow-list mechanism; ReflectionGuardRules static class; motivating incident: P-147 EncryptionRotationService.LoadBatchAsync; registry ships empty (P-147 fix eliminated the only known violation); total tasks now 289 — WO-024 P-153
- [2026-06-18] Phase SK.00.CommunicationArchRules added — 17 tasks: D-50, C-67–C-69, T-116–T-126, DO-22; SK0013 RawHttpClientConstructorInjection registered (Roslyn syntax-only, netstandard2.0, DelegatingHandler and SharedKernel.Communication.Rest exemptions); CommunicationLayeringRules static class (four NetArchTest predicates: forbidden-layer isolation for 11.Communication.*, Communication.Internal sibling isolation, gRPC interceptor direct-inheritance guard, HotChocolate FilterInputType/SortInputType direct-inheritance guard); NoDirectGrpcInterceptorInheritancePredicate and NoDirectHotChocolateFilterSortInheritancePredicate (ICustomRule, Mono.Cecil BaseType chain walk); hardcoded-URI guard documented as documentation-only (feasibility concern); total tasks now 306 — WO-025 P-159
- [2026-06-17] D-49, C-65–C-66, T-113–T-115, DO-21 → ● in SK.00.ReflectionGuard — all 7 tasks complete; NoMakeGenericMethodReflectionPredicate (Mono.Cecil Call/Callvirt IL walk for exact "MakeGenericMethod" name match) implemented in Predicates/; ReflectionExemptionRegistry (static allow-list, ships empty, internal Register/Unregister for test support via InternalsVisibleTo) implemented in root; ReflectionGuardRules.NoMakeGenericMethodReflection(Assembly) factory implemented in Rules/; 3/3 new tests pass (T-113 fire, T-114 pass, T-115 exemption), 78/78 full ArchitectureTests.Tests suite pass, 0 build warnings/errors; CLAUDE.md already contained complete SK0012 documentation from WO-024 planning — verified accurate, no duplication; SK.00.ReflectionGuard → ●
- [2026-06-18] D-50, C-67–C-69, T-116–T-126, DO-22 → ● in SK.00.CommunicationArchRules — all 16 tasks complete; CommunicationLayeringRules (4 factory methods), NoDirectGrpcInterceptorInheritancePredicate, NoDirectHotChocolateFilterSortInheritancePredicate, SK0013 RawHttpClientConstructorInjectionAnalyzer implemented; 93 analyzer tests pass, 86 arch tests pass; SK.00.CommunicationArchRules → ● (state-map-phase)
- [2026-06-18] Phase SK.00.WO026CommunicationQuality added — 5 tasks: D-51, C-70, T-127–T-128, DO-23; GrpcNeverReferencesContracts NetArchTest rule (adds to existing CommunicationLayeringRules class); no new SK IDs; four WO-026 What Goes Where governance conventions; inline Result/Envelope mapping documented as platform violation with future SK0xxx backlog note; total tasks now 311 — WO-026 P-167
- [2026-06-19] D-51, C-70, T-127–T-128, DO-23 → ● in SK.00.WO026CommunicationQuality — all 5 tasks complete; GrpcNeverReferencesContracts implemented in CommunicationLayeringRules.cs; 2 new tests (T-127 fire-path, T-128 pass-path against the real SharedKernel.Communication.Grpc assembly) pass; 88/88 full ArchitectureTests.Tests suite passes, 0 build warnings/errors; CLAUDE.md documentation pre-written by governance-arch-planner verified accurate against the implementation, no edits needed; SK.00.WO026CommunicationQuality → ● (state-map-phase)
- [2026-06-19] Phase SK.00.HealthCheckConstantsGuard added — 9 tasks (corrected from an initial miscount of 8 — the Task Rows table has D-53, C-75–C-77, T-139–T-142, DO-25 = 9 rows): D-53, C-75–C-77, T-139–T-142, DO-25; new HealthCheckConstantsUsageRules static class (single ICustomRule predicate: NoBareHealthCheckLiteralWhereConstantsExist) plus a new reusable StringConstantsClassDetector helper (Mono.Cecil field-shape detection + literal-value resolution — a third IL technique distinct from opcode-presence and Ldstr-literal-collection); generalized rule shape with no hardcoded constants-class names, explicitly additive to SK.00.ServiceDefaultsGovernance (P-173); no new SK IDs; total tasks now 337 — WO-028 P-178, depends on P-177
- [2026-06-19] Phase SK.00.ServiceDefaultsGovernance added — 16 tasks (corrected from an initial miscount of 17 — the Overall Progress summary table is authoritative; D-52, C-71–C-74, T-129–T-138, DO-24 = 16 rows): HealthCheckTagIntegrityRules (two ICustomRule predicates: NoConflictingLivenessReadinessTags, DependencyHealthChecksCarryReadyNotLive — IL Ldstr literal-collection technique scoped to Add*HealthCheck/Add*ReadinessCheck method names, documented data-flow limitation for non-literal tags); CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders mirroring CachingAbstractionRules for Persistence (.EfCore/.PostgreSQL/.Dapper), Messaging (.MassTransit), and Security (.Oidc) provider families; no new SK IDs; closes the WO-003/P-009 caching-only scope gap in 13.ServiceDefaults/CLAUDE.md's composition-root claim; real-assembly verification deferred as a tracked follow-up pending P-170; total tasks now 327 — WO-027 P-173
- [2026-06-24] D-52, C-71–C-74, T-129–T-138, DO-24 → ● in SK.00.ServiceDefaultsGovernance — all 16 tasks complete; HealthCheckTagIntegrityRules (NoConflictingLivenessReadinessTagsPredicate, DependencyHealthChecksCarryReadyNotLivePredicate) and CompositionRootExclusivityRules (OnlyAllowedAssembliesMayReferenceConcreteProviders) implemented in SharedKernel.ArchitectureTests using contrived in-memory CSharpCompilation fixtures (SharedKernel.ServiceDefaults/.MultiTenancy not yet buildable when this phase was designed); 10 new tests (T-129–T-138) pass, 98/98 full ArchitectureTests.Tests suite passes, 0 build warnings/errors; CLAUDE.md documentation pre-written by governance-arch-planner verified accurate against the implementation, no edits needed; note for follow-up — 13.ServiceDefaults' P-170 has since landed with a real SharedKernel.ServiceDefaults assembly matching the expected shape (HealthCheckTags.Live/.Ready constants, Add*HealthCheck literal tag arrays); a future session should add a real-assembly ProjectReference re-verification pass (non-blocking per Implementation Rule 8); SK.00.ServiceDefaultsGovernance → ● (state-map-phase)
- [2026-06-25] Phase SK.00.PresentationArchRules added — 9 tasks: D-54, C-78–C-79, T-143–T-146, DO-26; new PresentationLayeringRules static class (two ICustomRule predicates: NoDirectProblemDetailsConstructionPredicate via Newobj IL match on ProblemDetails/HttpValidationProblemDetails full names; NoInlineResultBranchBeforeHttpResultPredicate via method-level three-signal co-occurrence check); no new SK IDs; no internal namespace exemption — caller excludes SharedKernel.Presentation.WebApi by never passing it to either factory method; closes the WO-026 P-166/167 documented backlog note for mechanical Result-to-HTTP boundary enforcement; depends on P-194 for real-assembly verification only, contrived in-memory fixtures used at design/implementation time (same technique as SK.00.ServiceDefaultsGovernance/SK.00.HealthCheckConstantsGuard); total tasks now 346 — WO-031 P-199
- [2026-06-24] D-53, C-75–C-77, T-139–T-142, DO-25 → ● in SK.00.HealthCheckConstantsGuard — all 9 tasks complete (Task Rows table has 9 rows; corrected from the stale "8" count in the Overall Progress row and phase-added changelog line); StringConstantsClassDetector, NoBareHealthCheckLiteralWhereConstantsExistPredicate, HealthCheckConstantsUsageRules implemented; confirmed exact Microsoft.Extensions.Diagnostics.HealthChecks declaring-type names via Mono.Cecil inspection of .NET 10 reference assemblies and recorded in CLAUDE.md, resolving prior placeholders; 4 new tests (T-139–T-142) pass, 102/102 full ArchitectureTests.Tests suite passes, 0 build warnings/errors; generality requirement verified — zero occurrences of "HealthCheckTags"/"HealthCheckNames" in the three implementation files (reworded 3 XML-doc passages that referenced them by name); SK.00.HealthCheckConstantsGuard → ● (state-map-phase)
- [2026-06-25] D-54, C-78–C-79, T-143–T-146, DO-26 → ● in SK.00.PresentationArchRules — all 9 tasks complete; NoDirectProblemDetailsConstructionPredicate and NoInlineResultBranchBeforeHttpResultPredicate implemented in Predicates/, PresentationLayeringRules implemented in Rules/; 6 new tests (T-143 fire path, T-144 pass path + companion HttpValidationProblemDetails fire-path case, T-145 fire path, T-146 pass path + companion vacuous-pass case) pass, 108/108 full ArchitectureTests.Tests suite passes, 0 build warnings/errors; fixed a self-inflicted false positive in the first T-144 fixture draft (the fixture's own "factory" was itself constructing ProblemDetails via newobj in the same assembly, correctly flagged since the predicate carries no namespace exemption — reworked the fixture to isolate the assertion to the calling type alone); CLAUDE.md documentation (Architecture Test Contracts, Implementation Rules, Changelog) pre-written by governance-arch-planner verified accurate against the implementation, no edits needed; added the missing `<!-- phase-key: SK.00.PresentationArchRules -->` heading marker and Phase Key Registry row (both were absent from the original planning pass); SK.00.PresentationArchRules → ● (state-map-phase)
- [2026-06-30] Phase SK.00.ApplicationPipelineArchRules added — 13 tasks: D-55, C-80–C-84, T-147–T-153, DO-27; new `ApplicationPipelineRules` static class (three ICustomRule predicates: `NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate` — caller-supplied behavior-name set, infra-namespace reference ban, mirrors `HealthCheckTagIntegrityRules`'s caller-supplied-prefix convention; `NoGenericConstraintMatchesStreamRequestPredicate` — new IL generic-parameter-constraint structural technique, distinct from every prior opcode-presence/Ldstr-literal-collection/field-shape technique, proving no `IPipelineBehavior<,>` implementor's `TRequest` constraint structurally satisfies `IStreamRequest<TResponse>`; `NoTaskDelayOutsideResilienceBehaviorPredicate` — `Task.Delay` Call/Callvirt fingerprint heuristic with a `ResilienceBehavior` self-exemption, extending the existing hand-rolled-primitive prohibition pattern to retry/backoff); new `PipelineOrderAssertion` public reflection helper (not NetArchTest/ICustomRule — walks `ServiceDescriptor` entries off an unbuilt `IServiceCollection` to assert `IPipelineBehavior<,>` registration order, ships in `SharedKernel.ArchitectureTests` for `05.Application.Behaviors.Tests` to consume against its own real `ApplicationBehaviorsBuilder.Build()` output); no new SK IDs; depends on `05.Application` P-220/P-221/P-222/P-224 for real-assembly verification only — WO-036 is design-only as of 2026-06-30, so design/implementation proceeds against contrived in-memory fixtures (same technique as SK.00.ServiceDefaultsGovernance/SK.00.HealthCheckConstantsGuard/SK.00.PresentationArchRules); total tasks now 359 — WO-036 P-225, depends on 05.Application P-220/P-221/P-222/P-224 (governance-arch-planner)
- [2026-06-30] D-55, C-80–C-84, T-147–T-153, DO-27 → ● in SK.00.ApplicationPipelineArchRules — all 13 tasks complete; ApplicationPipelineRules, NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate, NoGenericConstraintMatchesStreamRequestPredicate, NoTaskDelayOutsideResilienceBehaviorPredicate, and PipelineOrderAssertion implemented; added Microsoft.Extensions.DependencyInjection.Abstractions (10.0.1) package reference to SharedKernel.ArchitectureTests for PipelineOrderAssertion's IServiceCollection/ServiceDescriptor surface; added MediatR (12.4.1, matching 05.Application's pin) and Microsoft.Extensions.DependencyInjection (10.0.9, transitive-conflict-resolved) to the test project only, for fixture compilation — 00.Governance still references nothing in production code; 9 new tests (T-147/T-148 fire+pass, T-149/T-150 fire+pass, T-151/T-152 fire+pass+vacuous-pass, T-153 PipelineOrderAssertion passing+failing) pass, 117/117 full ArchitectureTests.Tests suite passes, 0 build warnings/errors; two implementation-detail fixes discovered via direct Mono.Cecil IL inspection during testing (not spec discrepancies): (1) NetArchTest's Types.GetAllTypes deliberately excludes [CompilerGenerated] types, so async-lowered Task.Delay calls inside compiler-generated state-machine structs are invisible to IL-walk predicates — T-151/T-152 fixtures use synchronous Task.Delay(...).GetAwaiter().GetResult() instead of await; (2) a GenericParameter constraint's TypeReference.FullName for a closed-generic interface includes the generic-argument list (e.g. "MediatR.IStreamRequest`1<TResponse>"), so NoGenericConstraintMatchesStreamRequestPredicate compares against GenericInstanceType.ElementType.FullName for the open-generic form; CLAUDE.md documentation pre-written by governance-arch-planner verified accurate against the final implementation — zero discrepancy found, no edits made; SK.00.ApplicationPipelineArchRules → ● (state-map-phase)
- [2026-07-02] D-56, C-85–C-89, T-154–T-160, DO-28 → ● in SK.00.CryptoDelegationAndUowSeamGuard — all 14 tasks complete; CryptoIsolationRules + NoRawSymmetricCipherOutsideCryptographyPredicate (platform-wide AesGcm/Aes/SymmetricAlgorithm/RandomNumberGenerator guard), UnitOfWorkSeamRules + UnitOfWorkInterfacesRemainDistinctPredicate (negative-space IUoW-distinctness guard), SK0301 exemption narrowed to SharedKernel.Cryptography only; 8 new tests pass, 125/125 full suite passes (state-map-phase)
- [2026-07-03] Phase SK.00.MetricsOutcomeTagAndMisregistrationGuard added — 15 tasks: D-57, C-90–C-94, T-161–T-168, DO-29; SK0014 ClosedGenericResiliencePipelineRegistration (syntax-only), SK0015 StreamPipelineBehaviorMisregistration (semantic-model, second after SK0011), SK0016 RequestTypeShortNameUsage (syntax-only, trigger-IN namespace scope) registered (general-purpose sequential block, next after SK0013); MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag + RequestDurationRecordMissingOutcomeTagPredicate added (no new SK ID — reuses HealthCheckTagIntegrityRules' Ldstr literal-collection technique against a new Histogram<T>.Record call-site search); addresses all four WO-038 application-audit findings; the outcome-tag rule's MetricsBehavior<,> (P-217) retrofit is explicitly out of this domain's jurisdiction — tracked as a new Cross-Domain Dependency on 05.Application for real-assembly verification only; design/tests use contrived in-memory fixtures; total tasks now 388 — WO-038 P-235, depends on P-234 (governance-arch-planner)
- [2026-06-30] Phase SK.00.CryptoDelegationAndUowSeamGuard added — 13 tasks: D-56, C-85–C-89, T-154–T-160, DO-28; new `CryptoIsolationRules` static class (`NoRawSymmetricCipherOutsideCryptographyPredicate` — platform-wide ICustomRule banning direct `AesGcm`/`Aes`/`SymmetricAlgorithm` field/IL references and `RandomNumberGenerator` calls outside a `SharedKernel.Cryptography`-prefixed namespace, the sole legitimate caller once 06.Persistence P-227 lands); new `UnitOfWorkSeamRules` static class (`UnitOfWorkInterfacesRemainDistinctPredicate` — two-assembly ICustomRule asserting `SharedKernel.Application.Behaviors.IUnitOfWork` and `SharedKernel.Persistence.Abstractions.IUnitOfWork` are never merged into one type or made to inherit one another; a negative-space/regression-guard rule protecting the local-seam pattern already proven for IAuthorizationContext/IIdempotencyKeyStore); SK0301 (DirectCryptoInDomainOrApplication) reconciled in place — its backing `NoAesCipherInDomainOrApplicationPredicate` exemption list narrowed from `{SharedKernel.Persistence, SharedKernel.Security}` to `{SharedKernel.Cryptography}` only, making it a caller-scoped special case of the new platform-wide rule rather than a contradictory duplicate; no new SK ID assigned; depends on 06.Persistence P-227/P-228 for real-assembly verification only — both design-only as of 2026-06-30, so design/implementation proceeds against contrived in-memory fixtures matching the documented target shape (same technique as SK.00.ServiceDefaultsGovernance/SK.00.HealthCheckConstantsGuard/SK.00.PresentationArchRules/SK.00.ApplicationPipelineArchRules); total tasks now 372 — WO-037 P-229, depends on 06.Persistence P-227/P-228 (governance-arch-planner)
- [2026-07-03] D-57, C-90–C-94, T-161–T-168, DO-29 → ● in SK.00.MetricsOutcomeTagAndMisregistrationGuard — all 15 tasks complete; SK0014 ClosedGenericResiliencePipelineRegistrationAnalyzer, SK0015 StreamPipelineBehaviorMisregistrationAnalyzer, SK0016 RequestTypeShortNameUsageAnalyzer, RequestDurationRecordMissingOutcomeTagPredicate, and MetricsInstrumentationRules implemented; two implementation-detail fixes discovered during testing (not spec discrepancies caught by DO-29's verification pass): (1) SK0015 — `INamedTypeSymbol.AllInterfaces` returns empty for an unbound generic type symbol (the shape `typeof(StreamFixtureBehavior<,>)` produces), so the interface-implementation check walks `type.OriginalDefinition.AllInterfaces` instead, confirmed via a standalone Roslyn symbol-inspection scratch script; (2) `RequestDurationRecordMissingOutcomeTagPredicate` initially used a `Name.StartsWith("Histogram")` heuristic — narrowed to match the pre-written CLAUDE.md contract exactly (`GenericInstanceType` + `ElementType.FullName == "System.Diagnostics.Metrics.Histogram\`1"`); 8 new tests (T-161–T-168) pass, 105/105 SharedKernel.Analyzers.Tests and 127/127 SharedKernel.ArchitectureTests.Tests pass, 0 build warnings/errors; CLAUDE.md documentation pre-written by governance-arch-planner verified accurate (Diagnostic Registry, Architecture Test Contracts, Implementation Rules) with one closeout changelog entry added; Phase Key Registry gap confirmed again (same as SK.00.ServiceDefaultsGovernance/SK.00.HealthCheckConstantsGuard) — this phase key has a full phase section and Overall Progress row but no Phase Key Registry row; SK.00.MetricsOutcomeTagAndMisregistrationGuard → ● (state-map-phase)
- [2026-07-03] Phase SK.00.DomainEventDispatcherReflectionExemption added — 5 tasks: D-58, C-95, T-169–T-170, DO-30; registers `SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher`'s `MakeGenericMethod` call site (inside `PublishSingle`'s `ConcurrentDictionary.GetOrAdd` factory delegate) as the FIRST real entry in `ReflectionExemptionRegistry`, pre-empting a build break the moment SK0012 is pointed at the real `SharedKernel.Application` assembly; no new SK ID, no predicate/rule-logic change — registry-entry-only, with two new real-assembly tests (T-169 fire path with exemption temporarily unregistered, T-170 pass path with exemption registered) proving the exemption is load-bearing; flags a KEY RISK for the implementer — the call site sits inside a closure-free `static` lambda, so Roslyn compiles it onto a compiler-generated `<>c` nested cache class, not the literal `MediatRDomainEventDispatcher`/`PublishSingle` pair a source-level reading suggests; the exact `TypeDefinition.FullName`/`MethodDefinition.Name` pair must be derived empirically (red-then-green against the compiled assembly), never hand-guessed; also corrects the SK0012 registry's prior inaccurate "all production assemblies pass this rule" claim and documents a newly-discovered, still-OPEN gap — `07.Messaging`'s `MassTransitEventPublisher.BuildPublisher` (same closure pattern, cited as this exemption's own precedent) and `MessagingBusBuilder.AddActivity` are themselves unregistered and unverified against SK0012 (confirmed by direct source inspection 2026-07-03), out of scope for this phase, flagged as a candidate follow-up; also backfilled two Phase Key Registry rows missing from prior sessions (`SK.00.MetricsOutcomeTagAndMisregistrationGuard` P-235, and this phase's own `SK.00.DomainEventDispatcherReflectionExemption` P-240) — direct correction per the recurring gap noted in the entry immediately above; total tasks now 393 — WO-039 P-240 (governance-arch-planner)
