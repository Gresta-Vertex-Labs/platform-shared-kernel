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
| `SK.00.CachingEnforcement` | Caching Abstractions Enforcement | All tasks in Phase: Caching Abstractions Enforcement are `●` |
| `SK.00.DomainLayerPurity` | Domain Layer Purity Enforcement | All tasks in Phase: Domain Layer Purity Enforcement are `●` |
| `SK.00.DomainGoldStandard` | Domain Gold-Standard Architecture Rules | All tasks in Phase: Domain Gold-Standard Architecture Rules are `●` |
| `SK.00.ContractsPurity` | Contracts Layer Purity Architecture Rules | All tasks in Phase: Contracts Layer Purity Architecture Rules are `●` |

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

## Cross-Domain Dependencies

_No active cross-domain dependencies. `00.Governance` references nothing._

<!--
Format when active:
| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
-->

---

## Overall Progress

> Counts updated whenever a task state changes. Total tasks: 144.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.00.Design` | Design | 26 | 26 | 0 | `●` |
| `SK.00.Scaffold` | Scaffold | 10 | 10 | 0 | `●` |
| `SK.00.Core` | Core | 36 | 36 | 0 | `●` |
| `SK.00.Tests` | Tests | 45 | 45 | 0 | `●` |
| `SK.00.Docs` | Docs | 11 | 11 | 0 | `●` |
| `SK.00.Published` | Published | 6 | 6 | 0 | `●` |
| `SK.00.GuardPurity` | Guard Purity Enforcement | 11 | 11 | 0 | `●` |
| `SK.00.CachingEnforcement` | Caching Abstractions Enforcement | 11 | 11 | 0 | `●` |
| `SK.00.DomainLayerPurity` | Domain Layer Purity Enforcement | 16 | 16 | 0 | `●` |
| `SK.00.DomainGoldStandard` | Domain Gold-Standard Architecture Rules | 19 | 19 | 0 | `●` |
| `SK.00.ContractsPurity` | Contracts Layer Purity Architecture Rules | 13 | 13 | 0 | `●` |

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
