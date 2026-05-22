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
| D-13 | Define `CachingAbstractionRules` static class shape: `OnlyAllowedAssembliesMayReferenceConcreteCaching(Assembly[])` → `ConditionList`; define the three-assembly exemption list (`SharedKernel.Caching`, `SharedKernel.Caching.Redis`, `SharedKernel.ServiceDefaults`); document rationale | SharedKernel.ArchitectureTests | `○` |
| D-14 | Define SK0007 `RedisChannelServiceMessagingSubstitute` — trigger: `IRedisChannelService` in constructor param / field / property of a class whose name or enclosing namespace contains `Command`, `Event`, `DomainEvent`, or `IntegrationEvent`; suppression: inside `SharedKernel.Caching` or `SharedKernel.Caching.Redis` namespaces | SharedKernel.Analyzers | `○` |
| C-18 | Implement `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching(Assembly[])` — NetArchTest fluent predicate using `.Should().NotHaveDependencyOn("SharedKernel.Caching")` with assembly-name-based exemption filter | SharedKernel.ArchitectureTests | `○` |
| C-19 | Implement SK0007 `RedisChannelServiceMessagingSubstituteAnalyzer` — `ClassDeclarationSyntax` walker; simple name match on `IRedisChannelService`; substring check for `Command`/`Event`/`DomainEvent`/`IntegrationEvent` in class name and namespace ancestors; suppress inside `SharedKernel.Caching*` namespaces via parent walk | SharedKernel.Analyzers | `○` |
| T-18 | Architecture test (fire path): pass a contrived assembly reference that imports `SharedKernel.Caching` from an application-layer class; assert `CachingAbstractionRules` rule fails with the offending assembly name in the failure message | SharedKernel.ArchitectureTests | `○` |
| T-19 | Architecture test (pass path): pass only the three exempt assemblies; assert `CachingAbstractionRules` rule passes | SharedKernel.ArchitectureTests | `○` |
| T-20 | Analyzer test SK0007 (fire path): `IRedisChannelService` injected via constructor in a class named `PlaceOrderCommandHandler` in namespace `Application.Commands` triggers SK0007 | SharedKernel.Analyzers.Tests | `○` |
| T-21 | Analyzer test SK0007 (pass path): `IRedisChannelService` injected in a class named `CacheInvalidationService` with no forbidden name or namespace term — no diagnostic | SharedKernel.Analyzers.Tests | `○` |
| T-22 | Analyzer test SK0007 (suppression path): `IRedisChannelService` injected in a class within `SharedKernel.Caching.Redis` namespace — no diagnostic | SharedKernel.Analyzers.Tests | `○` |
| DO-07 | Document `CachingAbstractionRules` in `00.Governance/README.md`: rule rationale, exemption list, how to add a documented exemption for a non-standard composition root | SharedKernel.ArchitectureTests | `○` |
| DO-08 | Document SK0007 in `00.Governance/README.md`: rationale (Redis pub/sub is not a durable bus), violating example, compliant alternative, suppression instructions | SharedKernel.Analyzers | `○` |

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
| D-15 | Define `DomainAssembliesNeverReferenceInfrastructure` predicate shape: forbidden assembly name terms (`EntityFramework`, `MassTransit`, `Redis`, `RabbitMQ`), iterative `.NotHaveDependencyOn()` call pattern, failure message contract | SharedKernel.ArchitectureTests | `○` |
| D-16 | Define `DomainAssembliesNeverContainEventHandlers` predicate shape: `DoesNotImplementOpenGenericInterfacePredicate` design — `TypeDefinition.Interfaces` inspection for `IDomainEventHandler` name prefix; failure message includes offending type full name | SharedKernel.ArchitectureTests | `○` |
| D-17 | Define `DomainAssembliesNeverCallSystemClock` predicate shape: `DoesNotCallSystemClockPredicate` design — IL instruction walk for `DateTime::get_UtcNow`, `DateTime::get_Now`, `DateTimeOffset::get_UtcNow`, `DateTimeOffset::get_Now`; failure message includes offending type and method | SharedKernel.ArchitectureTests | `○` |
| D-18 | Define `DomainServicesHaveNoInfrastructureConstructorParameters` predicate shape: `NoInfrastructureConstructorParametersPredicate` design — scope to `IDomainService` implementors, inspect constructor `ParameterDefinition.ParameterType.Namespace` for infra namespace prefix match; failure message includes offending type and parameter type | SharedKernel.ArchitectureTests | `○` |
| C-20 | Implement `DoesNotImplementOpenGenericInterfacePredicate` in `Predicates/` — `ICustomRule` checking `TypeDefinition.Interfaces` for entries whose `InterfaceType.Name` starts with `"IDomainEventHandler"`; return false with offending type full name on violation | SharedKernel.ArchitectureTests | `○` |
| C-21 | Implement `DoesNotCallSystemClockPredicate` in `Predicates/` — `ICustomRule` walking all `MethodDefinition.Body.Instructions` for `Call`/`Callvirt` opcodes whose operand `MethodReference.FullName` matches any of the four forbidden property getters | SharedKernel.ArchitectureTests | `○` |
| C-22 | Implement `NoInfrastructureConstructorParametersPredicate` in `Predicates/` — `ICustomRule` scoped to types implementing `IDomainService`; inspects `TypeDefinition.Methods` where `IsConstructor` is true; checks each `ParameterDefinition.ParameterType.Namespace` against forbidden namespace prefix list | SharedKernel.ArchitectureTests | `○` |
| C-23 | Implement `DomainLayerPurityRules` static class in `Rules/` — four factory methods: `DomainAssembliesNeverReferenceInfrastructure(Assembly)` → `ConditionList`, `DomainAssembliesNeverContainEventHandlers(Assembly)` → `ConditionList`, `DomainAssembliesNeverCallSystemClock(Assembly)` → `ConditionList`, `DomainServicesHaveNoInfrastructureConstructorParameters(Assembly)` → `ConditionList` | SharedKernel.ArchitectureTests | `○` |
| T-23 | Architecture test Rule 1 (fire path): pass a contrived domain assembly that references `Microsoft.EntityFrameworkCore`; assert `DomainAssembliesNeverReferenceInfrastructure` fails and failure message contains the offending assembly name | SharedKernel.ArchitectureTests | `○` |
| T-24 | Architecture test Rule 1 (pass path): pass a clean domain assembly with no infrastructure references; assert rule passes | SharedKernel.ArchitectureTests | `○` |
| T-25 | Architecture test Rule 2 (fire path): pass a domain assembly containing a type that implements `IDomainEventHandler<TEvent>`; assert `DomainAssembliesNeverContainEventHandlers` fails and failure message contains the offending type full name | SharedKernel.ArchitectureTests | `○` |
| T-26 | Architecture test Rule 2 (pass path): pass a clean domain assembly with no event handler implementations; assert rule passes | SharedKernel.ArchitectureTests | `○` |
| T-27 | Architecture test Rule 3 (fire path): pass a domain assembly where a domain entity method calls `DateTime.UtcNow`; assert `DomainAssembliesNeverCallSystemClock` fails and failure message names the offending type and method | SharedKernel.ArchitectureTests | `○` |
| T-28 | Architecture test Rule 3 (pass path): pass a clean domain assembly where time is consumed via `IClock.UtcNow`; assert rule passes | SharedKernel.ArchitectureTests | `○` |
| T-29 | Architecture test Rule 4 (fire path): pass a domain assembly where an `IDomainService` implementation has an `IRepository` (EF Core namespace) constructor parameter; assert `DomainServicesHaveNoInfrastructureConstructorParameters` fails and failure message names the offending type and parameter type | SharedKernel.ArchitectureTests | `○` |
| T-30 | Architecture test Rule 4 (pass path): pass a clean `IDomainService` implementation whose constructor accepts only `IClock` and other domain interfaces; assert rule passes | SharedKernel.ArchitectureTests | `○` |
| DO-09 | Document all four `DomainLayerPurityRules` predicates in `00.Governance/CLAUDE.md` and `00.Governance/README.md`: rationale for each rule, offending-pattern example, compliant-pattern example, cross-reference to root `CLAUDE.md` hard rules | SharedKernel.ArchitectureTests | `○` |

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

> Counts updated whenever a task state changes. Total tasks: 89.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.00.Design` | Design | 18 | 12 | 6 | `◐` |
| `SK.00.Scaffold` | Scaffold | 10 | 10 | 0 | `●` |
| `SK.00.Core` | Core | 20 | 14 | 6 | `◐` |
| `SK.00.Tests` | Tests | 25 | 12 | 13 | `◐` |
| `SK.00.Docs` | Docs | 9 | 5 | 4 | `◐` |
| `SK.00.Published` | Published | 6 | 6 | 0 | `●` |
| `SK.00.GuardPurity` | Guard Purity Enforcement | 11 | 11 | 0 | `●` |
| `SK.00.CachingEnforcement` | Caching Abstractions Enforcement | 11 | 0 | 11 | `○` |
| `SK.00.DomainLayerPurity` | Domain Layer Purity Enforcement | 16 | 0 | 16 | `○` |

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
