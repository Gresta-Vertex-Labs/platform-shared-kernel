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

| Phase Key | Maps to Root Phase | Promotion Condition |
|-----------|-------------------|-------------------|
| `SK.00.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.00.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.00.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.00.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.00.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.00.Published` | Published | All tasks in Phase: Published are `●` |
| `SK.00.GuardPurity` | Guard Purity Enforcement | All tasks in Phase: Guard Purity Enforcement are `●` |

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
| P-01 | Add NuGet metadata to `SharedKernel.Analyzers.csproj`: set `<IncludeBuildOutput>false</IncludeBuildOutput>`, `<DevelopmentDependency>true</DevelopmentDependency>`, add `<Analyzer>` item group pointing to the assembly | SharedKernel.Analyzers | `○` |
| P-02 | Add NuGet metadata to `SharedKernel.ArchitectureTests.csproj`: description, version, authors, `<PrivateAssets>all</PrivateAssets>` guidance in README | SharedKernel.ArchitectureTests | `○` |
| P-03 | Add NuGet metadata to `SharedKernel.Linter.csproj`: description, version, authors; validate that `.editorconfig` and `.csharpierrc.json` are included as content in the packed `.nupkg` | SharedKernel.Linter | `○` |
| P-04 | Pack and publish `SharedKernel.Analyzers` to local feed; verify analyzer activates in a consumer project (SK0001 diagnostic fires on `DateTime.UtcNow`) | SharedKernel.Analyzers | `○` |
| P-05 | Pack and publish `SharedKernel.ArchitectureTests` to local feed; verify consumer can subclass `ArchitectureRuleBase` and call `SharedKernelLayeringRules` | SharedKernel.ArchitectureTests | `○` |
| P-06 | Pack and publish `SharedKernel.Linter` to local feed; verify `.editorconfig` and `.csharpierrc.json` appear in consumer project after restore | SharedKernel.Linter | `○` |

---

## Phase: Guard Purity Enforcement <!-- phase-key: SK.00.GuardPurity -->

> Enforce the guard clause purity contract: `IGuardClause` extension methods on the functional path (`Guard.Against.*`) must never throw — they must return `Error?`. Architecture tests load the `SharedKernel.Guards` assembly and assert the absence of throw IL opcodes in all `IGuardClause` extension methods outside the `Guard.Throw` companion class.

### Goal

The two-path guard design (`Guard.Against.*` returns `Error?`, `Guard.Throw.*` throws) only delivers its value if the functional path is provably pure. This phase adds an architecture test that loads `SharedKernel.Guards` and uses a Mono.Cecil IL inspection predicate — bundled inside `NetArchTest.eNt` — to assert that no method on any type implementing `IGuardClause` contains a `throw` instruction, with an explicit exclusion for the `Guard.Throw` companion class. A Roslyn analyzer (SK0006) is also specified here; its implementation is a follow-on Core task within this phase.

### Scope

- Package(s) affected: `SharedKernel.ArchitectureTests`, `SharedKernel.Analyzers`
- New files:
  - `SharedKernel.ArchitectureTests/Rules/GuardPurityRules.cs` — `GuardPurityRules` static class housing `GuardAgainstMethodsMustNotThrow()` and the exclusion list
  - `SharedKernel.ArchitectureTests/Predicates/DoesNotContainThrowIlPredicate.cs` — custom `ICustomRule` / `MeetCustomPredicate` wrapper that inspects method bodies via Mono.Cecil `MethodBody.Instructions` for `OpCodes.Throw`
- Modified files:
  - `SharedKernel.ArchitectureTests/SharedKernelLayeringRules.cs` — no change; guard purity lives in its own static class `GuardPurityRules`
  - `00.Governance/CLAUDE.md` — refresh architecture test contracts section, add SK0006 to registry
  - `00.Governance/state-map.md` — this update
- Deleted files: none

### Diagnostic Registry Changes

| ID | Rule Name | Category | Severity | Trigger Summary |
|----|-----------|----------|----------|-----------------|
| SK0006 | GuardClauseThrow | Design | Warning | A method on a type implementing `IGuardClause` that is not inside the `Guard.Throw` class contains a `throw` expression — violates functional-path purity contract |

### Implementation Rules

1. `GuardPurityRules.GuardAgainstMethodsMustNotThrow()` must load `SharedKernel.Guards` via `Types.InAssembly(typeof(IGuardClause).Assembly)` and apply `DoesNotContainThrowIlPredicate` scoped to types not named `Guard.Throw` (full name exclusion: `SharedKernel.Guards.Guard+Throw` or the configured companion class name).
2. `DoesNotContainThrowIlPredicate` must iterate `TypeDefinition.Methods` via `Mono.Cecil.TypeDefinition` (accessible through NetArchTest's internal Mono.Cecil bundling via reflection on the `TypeDefinition` property of `IType`) and check each `MethodDefinition.Body.Instructions` for `OpCodes.Throw`. If any throw opcode is found, the predicate fails for that type.
3. Because NetArchTest.eNt bundles Mono.Cecil internally (not as a transitive public NuGet reference), `DoesNotContainThrowIlPredicate` must access it through NetArchTest's `MeetCustomPredicate` API which exposes `IType` — the Mono.Cecil `TypeDefinition` is accessible as `IType.Definition` (internal property). If the API surface does not expose `TypeDefinition` publicly, the predicate must add `Mono.Cecil` as an explicit NuGet reference to `SharedKernel.ArchitectureTests` (version pinned at `>= 0.11.5`).
4. The exclusion for `Guard.Throw` is by full type name string comparison, not namespace prefix — the companion class may be a nested class (`Guard+Throw`) and must be matched exactly.
5. `GuardPurityRules` must be in `SharedKernel.ArchitectureTests`; it must not reference any runtime package from other SharedKernel domains.
6. SK0006 `GuardClauseThrowAnalyzer` must fire on `ThrowStatementSyntax` or `ThrowExpressionSyntax` nodes inside methods declared on types that implement `IGuardClause`, with containment check to exclude methods declared in the `Guard.Throw` class. The `IGuardClause` symbol check is by full metadata name `SharedKernel.Guards.IGuardClause`.
7. SK0006 severity is `Warning` at introduction; the path to `Error` is gated on confirmation that `Guard.Throw` exclusion logic is stable (not causing false positives in the `Throw.*` extension class).

### File-Level Plan

| File | Package | Action | Purpose |
|------|---------|--------|---------|
| `Rules/GuardPurityRules.cs` | SharedKernel.ArchitectureTests | Create | Static class with `GuardAgainstMethodsMustNotThrow()` factory |
| `Predicates/DoesNotContainThrowIlPredicate.cs` | SharedKernel.ArchitectureTests | Create | Custom NetArchTest predicate — Mono.Cecil IL throw inspection |
| `Diagnostics/SK0006_GuardClauseThrowAnalyzer.cs` | SharedKernel.Analyzers | Create (Core task) | Roslyn analyzer: fires on `throw` inside `IGuardClause` method bodies outside `Guard.Throw` |

### Acceptance Criteria

- [ ] `GuardPurityRules.GuardAgainstMethodsMustNotThrow()` returns an `IArchRule` that fails when loaded against an assembly containing a `throw` inside an `IGuardClause` extension method (validated by a test fixture using a contrived violation assembly)
- [ ] The same rule passes when applied to a clean `IGuardClause` implementation that returns `Error?` only
- [ ] `Guard.Throw` companion class methods are excluded — the rule must not flag legitimate throw-side extensions
- [ ] SK0006 `DiagnosticDescriptor` is registered in the analyzer registry with `HelpLinkUri` pointing to the README anchor
- [ ] Analyzer fire-path test: SK0006 fires on a minimal code snippet with `throw new Exception()` inside an `IGuardClause`-implementing method
- [ ] Analyzer pass-path test: SK0006 does not fire when the method is on a `Guard.Throw`-equivalent type

### Dependencies

- Requires P-003 (`IGuardClause`, `Guard.Against`, `Guard.Throw` types in `SharedKernel.Guards`) to be complete: yes — the architecture test loads `typeof(IGuardClause).Assembly`; without that type existing the assembly load fails. The design is locked but implementation tasks are blocked on P-003.
- Unblocks: P-005 (Guard purity enforcement integrated into CI gate once this rule is at `Error` severity)

### Tooling Version Notes

- `NetArchTest.eNt`: >= 1.3.2 (for `MeetCustomPredicate` API)
- `Mono.Cecil`: >= 0.11.5 (add explicitly if `NetArchTest.eNt` does not expose `IType.Definition` publicly)
- `Microsoft.CodeAnalysis.CSharp`: current pin (SK0006 follows same constraint as SK0001–SK0005)
- Target framework: `netstandard2.0` (Analyzers) / `net10.0` (ArchitectureTests)

### Task Rows

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-11 | Define `GuardPurityRules` static class shape: `GuardAgainstMethodsMustNotThrow()` predicate design, `Guard.Throw` exclusion strategy (full type name match on `Guard+Throw`), Mono.Cecil IL opcode inspection approach via `DoesNotContainThrowIlPredicate` | SharedKernel.ArchitectureTests | `●` |
| D-12 | Define SK0006 `GuardClauseThrow` — trigger: `ThrowStatementSyntax` or `ThrowExpressionSyntax` inside method bodies on types implementing `IGuardClause` (full name: `SharedKernel.Guards.IGuardClause`); exclusion: containing type full name is `Guard+Throw` or is nested within it | SharedKernel.Analyzers | `●` |
| C-15 | Implement `DoesNotContainThrowIlPredicate` — custom NetArchTest `ICustomRule` that inspects `MethodDefinition.Body.Instructions` for `OpCodes.Throw` using Mono.Cecil; exclude property get/set accessors of compiler-generated types | SharedKernel.ArchitectureTests | `○` |
| C-16 | Implement `GuardPurityRules.GuardAgainstMethodsMustNotThrow()` — scoped to `Types.InAssembly(guardsAssembly).That().ImplementInterface(typeof(IGuardClause)).And().DoNotHaveName("Guard+Throw").Should().MeetCustomRule(new DoesNotContainThrowIlPredicate())` | SharedKernel.ArchitectureTests | `○` |
| C-17 | Implement SK0006 `GuardClauseThrowAnalyzer` — `SyntaxKind.ThrowStatement` and `SyntaxKind.ThrowExpression` walker; checks containing method's declaring type implements `IGuardClause`; skips if type name is `Guard` nested class `Throw` | SharedKernel.Analyzers | `○` |
| T-13 | Architecture test (fire path): load a contrived in-memory violation assembly with one `IGuardClause` extension that contains a `throw`; assert `GuardAgainstMethodsMustNotThrow()` rule fails and message names the offending method | SharedKernel.ArchitectureTests | `○` |
| T-14 | Architecture test (pass path): load a clean `IGuardClause` implementation that returns `Error?`; assert `GuardAgainstMethodsMustNotThrow()` rule passes | SharedKernel.ArchitectureTests | `○` |
| T-15 | Architecture test (exclusion): load an assembly where `Guard.Throw` class contains a `throw`; assert rule passes (excluded) | SharedKernel.ArchitectureTests | `○` |
| T-16 | Analyzer test SK0006 (fire path): `throw new InvalidOperationException()` inside a method on a class implementing `IGuardClause` triggers SK0006 | SharedKernel.Analyzers.Tests | `○` |
| T-17 | Analyzer test SK0006 (pass path): `throw` inside a method on `Guard.Throw` class does not trigger SK0006 | SharedKernel.Analyzers.Tests | `○` |
| DO-06 | Document guard purity rule in `00.Governance/README.md`: rationale, the two-path contract, exclusion list, SK0006 diagnostic entry with violating/compliant examples | SharedKernel.Analyzers | `○` |

---

## Cross-Domain Dependencies

_No active cross-domain dependencies. `00.Governance` references nothing._

<!--
Format when active:
| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
-->

---

## Overall Progress

> Counts updated whenever a task state changes. Total tasks: 62.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.00.Design` | Design | 12 | 12 | 0 | `●` |
| `SK.00.Scaffold` | Scaffold | 10 | 10 | 0 | `●` |
| `SK.00.Core` | Core | 14 | 14 | 0 | `●` |
| `SK.00.Tests` | Tests | 12 | 12 | 0 | `●` |
| `SK.00.Docs` | Docs | 6 | 5 | 1 | `●` |
| `SK.00.Published` | Published | 6 | 0 | 6 | `○` |
| `SK.00.GuardPurity` | Guard Purity Enforcement | 11 | 0 | 11 | `○` |

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
