# 00.Governance — Domain Brain

## What This Domain Is

The enforcement and quality layer. Provides Roslyn diagnostic analyzers, shared NetArchTest architecture-rule helpers, BenchmarkDotNet configuration templates, and a distributable linter config (EditorConfig + CSharpier). This domain is **tooling only** — it ships no runtime code. No other SharedKernel domain may depend on it.

Philosophy: **Enforce at build time. Zero runtime cost. Tooling-only packages.**

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Analyzers` | Roslyn diagnostic analyzers — enforces SharedKernel coding rules at compile time | nothing (targets `netstandard2.0`) |
| `SharedKernel.ArchitectureTests` | NetArchTest-based base classes and pre-built layering-rule predicates for architecture tests | nothing (test-only, never shipped to production code) |
| `SharedKernel.Benchmarks` | BenchmarkDotNet configuration and baseline helpers for SharedKernel micro-benchmarks | nothing |
| `SharedKernel.Linter` | Content-only NuGet: distributes `.editorconfig`, `.csharpierrc.json`, `Directory.Build.props` snippet across all projects | nothing |

All packages target `net10.0` **except** `SharedKernel.Analyzers`, which must target `netstandard2.0` — Roslyn's compiler hosting API requires `netstandard2.0` compatibility.

Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Roslyn analyzers | `Microsoft.CodeAnalysis.CSharp` (netstandard2.0 compatible) |
| Analyzer testing | `Microsoft.CodeAnalysis.CSharp.Testing.XUnit` |
| Architecture rules | `NetArchTest.eNt` + `FluentAssertions` |
| Benchmarking | `BenchmarkDotNet` |
| Code formatting | `CSharpier` (distributed as `.csharpierrc.json` content file) |
| Style config | `.editorconfig` (distributed as NuGet content) |

---

## Diagnostic Rule Registry

### `SharedKernel.Analyzers` — diagnostic surface

All rule IDs are prefixed `SK`. Severity is `Warning` during development phases; raise to `Error` when the rule is stable and enforced in CI.

```
SK0001  DirectDateTimeUsage
    Category  : Usage
    Severity  : Warning
    Trigger   : Direct usage of DateTime.UtcNow, DateTime.Now, or DateTimeOffset.UtcNow
                outside SharedKernel.Primitives
    Fix       : Replace with IClock.UtcNow / IClock.Today injected via DI

SK0002  DirectMicrosoftFeatureManagerUsage
    Category  : Usage
    Severity  : Warning
    Trigger   : Reference to Microsoft.FeatureManagement.IFeatureManager in
                constructor parameters, field declarations, or method signatures
    Fix       : Inject SharedKernel.FeatureManagement.IFeatureManager instead

SK0003  RawExceptionThrow
    Category  : Design
    Severity  : Warning
    Trigger   : throw new Exception(...) or throw new ApplicationException(...) —
                fires only when the concrete thrown type IS System.Exception or
                System.ApplicationException (not subclasses); no Error payload check
    Fix       : Use Result<T>.Failure(error) or throw a typed SharedKernel exception
                (DomainException, NotFoundException, etc.) carrying an Error

SK0004  NullErrorReturn
    Category  : Design
    Severity  : Warning
    Trigger   : return null literal in a method whose return type is Error or Error?
    Fix       : Return Error.None to signal "no error" — never return null for Error

SK0005  StringOnlyExceptionConstructor
    Category  : Design
    Severity  : Warning
    Trigger   : new DomainException("message") / new NotFoundException("message") etc.
                — any SharedKernelException subclass constructed with a string only
    Fix       : Supply an Error payload: new DomainException(error)

SK0006  GuardClauseThrow
    Category  : Design
    Severity  : Warning
    Trigger   : ThrowStatementSyntax or ThrowExpressionSyntax inside a method declared on a
                type that implements SharedKernel.Guards.IGuardClause, where the containing
                type is NOT the Guard.Throw companion class (full name match:
                declaring type name is "Throw" nested within "Guard", i.e., "Guard+Throw")
    Fix       : Return Error? instead of throwing — use the functional path (Guard.Against.*)
                for purity; move throw-side behavior to the Guard.Throw companion class
    Note      : Severity escalation to Error is gated on confirming Guard+Throw exclusion
                logic produces zero false positives across all existing guard extensions

SK0007  RedisChannelServiceMessagingSubstitute
    Category  : Design
    Severity  : Warning
    Trigger   : IRedisChannelService appears as a constructor parameter, field declaration,
                or property declaration in a class whose name or enclosing namespace
                contains any of the substrings: "Command", "Event", "DomainEvent",
                "IntegrationEvent" (case-sensitive substring match). Signals inappropriate
                use of Redis pub/sub as a substitute for a durable IMessageBus.
    Suppress  : Inside SharedKernel.Caching or SharedKernel.Caching.Redis namespaces —
                the service's own definition may reference IRedisChannelService freely.
                Suppression uses the SyntaxNode.Parent namespace walk (same as SK0001).
    Fix       : Inject IMessageBus (SharedKernel.Messaging.Abstractions) for commands,
                domain events, and integration events. Reserve IRedisChannelService for
                cache invalidation signals and ephemeral, non-durable pub/sub only.
    Note      : Severity escalation to Error is gated on field confirmation of zero false
                positives on the "DomainEvent" substring match — some projects use
                "IDomainEventHandler" as a class name suffix that is not a misuse.
    Exemption : Classes within SharedKernel.Caching* namespaces are always exempt.
                Any additional exemption must be documented in 00.Governance/CLAUDE.md.
```

---

## Architecture Test Contracts

### `SharedKernel.ArchitectureTests` — public surface

```
ArchitectureRuleBase  (abstract base class)
    protected Types GetAssemblyTypes(Assembly assembly)
    protected ConditionList ShouldNotReference(Assembly assembly, string forbiddenNamespace)
    protected void AssertRule(ConditionList conditionList)
    Note: NetArchTest.Rules 1.3.2 does not expose IArchRule — the fluent result type
          is ConditionList. AssertRule calls .GetResult() on the ConditionList.

SharedKernelLayeringRules  (static class — pre-built predicates)
    All factory methods take an Assembly parameter and return ConditionList.
    .CoreReferencesNothing(Assembly)                → ConditionList
    .CachingReferencesOnlyCore(Assembly)            → ConditionList
    .DomainReferencesOnlyCore(Assembly)             → ConditionList
    .ContractsReferencesOnlyCoreAndDomain(Assembly) → ConditionList
    .DomainNeverReferencesPersistence(Assembly)     → ConditionList  (hard rule)
    .DomainNeverReferencesMessaging(Assembly)       → ConditionList  (hard rule)
    .ApplicationNeverReferencesConcreteInfrastructure(Assembly) → ConditionList  (hard rule)
    .TestingNeverReferencedByProduction(Assembly)   → ConditionList  (hard rule)

GuardPurityRules  (static class — guard clause functional-path purity predicates)
    .GuardAgainstMethodsMustNotThrow()      → IArchRule
        Loads SharedKernel.Guards assembly, scopes to types implementing IGuardClause,
        excludes the Guard.Throw companion class (full name "Guard+Throw"),
        and asserts via DoesNotContainThrowIlPredicate that no method body contains
        a Mono.Cecil OpCodes.Throw instruction.

DoesNotContainThrowIlPredicate  (class : ICustomRule — internal predicate)
    Inspects Mono.Cecil MethodDefinition.Body.Instructions for OpCodes.Throw.
    Returns false (rule violated) for the first method found containing a throw opcode.
    Failure message includes the declaring type name and method name for diagnostics.
    Note: if NetArchTest.eNt does not expose IType.Definition publicly, add
    Mono.Cecil >= 0.11.5 as an explicit NuGet reference to SharedKernel.ArchitectureTests.

CachingAbstractionRules  (static class — caching boundary enforcement predicates)
    .OnlyAllowedAssembliesMayReferenceConcreteCaching(params Assembly[] assemblies)
                                            → ConditionList
        Asserts that no type in the supplied assemblies has a dependency on
        "SharedKernel.Caching" or "SharedKernel.Caching.Redis".
        Uses NetArchTest fluent API: .Should().NotHaveDependencyOn(...).
        Returns ConditionList (not IArchRule) — call AssertRule() on ArchitectureRuleBase.

    Exemption list (assemblies that MAY reference concrete caching packages):
        - SharedKernel.Caching          (the abstraction+default impl package itself)
        - SharedKernel.Caching.Redis    (the concrete Redis L2 provider)
        - SharedKernel.ServiceDefaults  (composition root — the only place that wires providers)
        Any additional exemption must be documented here before it is applied in code.

    Note: The method accepts a params Assembly[] so consuming test classes supply the
    production assemblies under test; assembly paths are never hard-coded in the predicate.

DomainLayerPurityRules  (static class — domain layer purity predicates)
    All factory methods accept Assembly domainAssembly and return ConditionList.
    .DomainAssembliesNeverReferenceInfrastructure(Assembly)  → ConditionList
        Asserts that no type in the supplied domain assembly has a dependency on
        any of the forbidden assembly name substrings: "EntityFramework", "MassTransit",
        "Redis", "RabbitMQ". Uses iterative .Should().NotHaveDependencyOn(term) calls —
        one per forbidden term — because NetArchTest matches the argument as a substring
        of the referenced assembly's full name. Failure message names the offending
        reference. No exemptions — domain assemblies may never reference infrastructure.

    .DomainAssembliesNeverContainEventHandlers(Assembly)     → ConditionList
        Asserts that no type in the domain assembly implements IDomainEventHandler<TEvent>.
        Uses DoesNotImplementOpenGenericInterfacePredicate (ICustomRule) which inspects
        TypeDefinition.Interfaces for entries whose InterfaceType.Name starts with
        "IDomainEventHandler". Failure message includes the offending type's full name.
        Handlers belong in 05.Application or 07.Messaging — never in 03.Domain.

    .DomainAssembliesNeverCallSystemClock(Assembly)          → ConditionList
        Asserts that no method in the domain assembly calls DateTime.UtcNow, DateTime.Now,
        DateTimeOffset.UtcNow, or DateTimeOffset.Now directly. Uses
        DoesNotCallSystemClockPredicate (ICustomRule) which walks MethodDefinition.Body
        .Instructions looking for Call/Callvirt opcodes whose MethodReference.FullName
        matches any of the four forbidden property getter signatures. Failure message
        names the offending type and method. Only IClock.UtcNow is the permitted time
        source in domain and application assemblies.

    .DomainServicesHaveNoInfrastructureConstructorParameters(Assembly) → ConditionList
        Asserts that no type implementing IDomainService has constructor parameters whose
        ParameterDefinition.ParameterType.Namespace starts with any forbidden namespace:
        "Microsoft.EntityFrameworkCore", "MassTransit", "StackExchange.Redis",
        "RabbitMQ.Client". Uses NoInfrastructureConstructorParametersPredicate (ICustomRule)
        scoped to IDomainService implementors only. Failure message names the offending
        type and the offending parameter type. Domain services may only accept IClock,
        other domain interfaces, and 01.Core primitives in their constructors.

DoesNotImplementOpenGenericInterfacePredicate  (class : ICustomRule — internal predicate)
    Checks TypeDefinition.Interfaces for InterfaceImplementation entries whose
    InterfaceType.Name starts with a configured interface name prefix (default:
    "IDomainEventHandler"). Returns false (rule violated) for the first matching type.
    Covers both generic and non-generic IL forms. Lives in Predicates/ folder.

DoesNotCallSystemClockPredicate  (class : ICustomRule — internal predicate)
    Walks all MethodDefinition.Body.Instructions in the type. For each Instruction
    where OpCode is Call or Callvirt, casts the operand to MethodReference and checks
    FullName against: "System.DateTime::get_UtcNow", "System.DateTime::get_Now",
    "System.DateTimeOffset::get_UtcNow", "System.DateTimeOffset::get_Now".
    Returns false (rule violated) for the first match found; failure message includes
    declaring type name and method name. Lives in Predicates/ folder.

NoInfrastructureConstructorParametersPredicate  (class : ICustomRule — internal predicate)
    Scopes to types whose TypeDefinition.Interfaces contains an entry with
    InterfaceType.Name == "IDomainService". For each such type, iterates
    TypeDefinition.Methods where IsConstructor is true and checks each
    ParameterDefinition.ParameterType.Namespace for forbidden namespace prefixes:
    "Microsoft.EntityFrameworkCore", "MassTransit", "StackExchange.Redis", "RabbitMQ.Client".
    Returns false (rule violated) on the first offending parameter; failure message includes
    the offending type name and parameter type name. Lives in Predicates/ folder.
```

---

## Benchmark Configuration

### `SharedKernel.Benchmarks` — public surface

```
SharedKernelBenchmarkConfig  (class : ManualConfig)
    — adds Job.Default.WithWarmupCount(1).WithIterationCount(3).WithId("ShortRun")
      Note: Job.Short does not exist in BenchmarkDotNet 0.15.x; use the explicit form above
    — adds MemoryDiagnoser (allocation tracking)
    — disables HardwareCounters (unstable in CI containers)
    — outputs deterministic markdown summary via MarkdownExporter.GitHub

[SharedKernelBenchmark]  (attribute — shorthand for [Config(typeof(SharedKernelBenchmarkConfig))])
```

---

## Linter Config Distribution

### `SharedKernel.Linter` — distributed content

The package ships no DLL. It is a content-only NuGet that places files into the consuming project tree:

```
content/
  .editorconfig          → enforces indent style, charset, line endings
  .csharpierrc.json      → CSharpier formatting config (print width, tab width)
  build/
    SharedKernel.Linter.props   → imported by MSBuild; wires CSharpier as a build step in CI
    SharedKernel.Linter.targets → enforces .editorconfig on dotnet format --verify-no-changes in CI
```

Consuming projects add `<PackageReference Include="SharedKernel.Linter" PrivateAssets="all" />`. The `.props`/`.targets` auto-import on package restore.

---

## Implementation Rules

- `SharedKernel.Analyzers` **must** target `netstandard2.0` — Roslyn's compiler host is netstandard2.0. Using `net10.0` breaks the analyzer in VS/Rider and `dotnet build`.
- Analyzer diagnostic IDs follow the `SK` prefix — never reuse an ID after it is published. Bump the register if a rule is renamed.
- Each `DiagnosticDescriptor` must declare a `HelpLinkUri` pointing to the relevant rule entry in `00.Governance/README.md`.
- `SharedKernel.Analyzers` has **zero NuGet dependencies beyond** `Microsoft.CodeAnalysis.CSharp` — no Primitives reference, no Microsoft.Extensions packages.
- `SharedKernel.ArchitectureTests` is a **test-only package** (`PrivateAssets="all"`). It must never appear as a transitive dependency in production code.
- `SharedKernel.Benchmarks` is **not published to the production feed** — it is a dev-only project for ad-hoc and CI performance tracking.
- `SharedKernel.Linter` ships **no DLL** — set `<IncludeBuildOutput>false</IncludeBuildOutput>` and `<ContentTargetFolders>content</ContentTargetFolders>` in the `.csproj`.
- Architecture tests in `SharedKernelLayeringRules` must mirror the layering table in the root `CLAUDE.md` exactly. If a new domain (folder `XX`) is added, the layering rules must be updated in the same PR.
- `ArchitectureRuleBase` must use NetArchTest's fluent API — no direct `Assembly.GetReferencedAssemblies()` reflection in rule predicates.
- No static mutable state anywhere in this domain.
- All analyzer tests use `CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>` from `Microsoft.CodeAnalysis.CSharp.Testing` — never test analyzers by compiling real source files manually. Use inline diagnostic markup `{|SKNNNN:...|}` in `TestCode` to declare expected diagnostics; leave `TestCode` markup-free for pass-path tests.
- Benchmarks must be gated behind `[BenchmarkDotNet]` harness — never run by the default `dotnet test` runner (use `[MemoryDiagnoser]` + `BenchmarkRunner.Run<T>` in `Program.cs` / a dedicated benchmark runner).
- `SharedKernelBenchmarkConfig` must set a deterministic markdown exporter for CI artifact comparison.
- `GuardPurityRules` lives in `SharedKernel.ArchitectureTests` — it must not reference any runtime domain package. The `IGuardClause` type is loaded reflectively via `typeof(IGuardClause).Assembly`; the consuming test project must reference `SharedKernel.Guards` directly to supply the assembly reference.
- `DoesNotContainThrowIlPredicate` inspects IL via Mono.Cecil `MethodDefinition.Body.Instructions`. If `NetArchTest.eNt` does not expose `IType.Definition` as a public property, add `Mono.Cecil >= 0.11.5` explicitly to `SharedKernel.ArchitectureTests.csproj`.
- The `Guard.Throw` exclusion in `GuardPurityRules` must be a full nested-type name match (`"Guard+Throw"` or equivalent CLR name) — not a namespace prefix match, which would be too broad.
- SK0006 `GuardClauseThrowAnalyzer` follows the same `netstandard2.0` constraint as SK0001–SK0005. No new NuGet dependencies beyond `Microsoft.CodeAnalysis.CSharp`.
- Each architecture test for `GuardPurityRules` must exercise the fire path (violation fixture), the pass path (clean fixture), and the exclusion path (`Guard.Throw` fixture) — three test cases minimum.
- `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching` uses `.Should().NotHaveDependencyOn("SharedKernel.Caching")` — the string is the assembly name prefix, matched by NetArchTest's dependency scanner against referenced assembly names. Two calls are required: one for `"SharedKernel.Caching"` (catches both the main package and Redis because `.Caching.Redis` contains `.Caching` as a prefix) and optionally one scoped specifically to `"SharedKernel.Caching.Redis"` for a more targeted failure message.
- SK0007 `RedisChannelServiceMessagingSubstituteAnalyzer` operates on `ClassDeclarationSyntax` nodes only. It uses a simple name match (`IRedisChannelService`) without semantic model symbol resolution — the simple name is unique within the SDK. Namespace suppression uses the same `SyntaxNode.Parent` walk pattern established by SK0001.
- SK0007 forbidden-context terms are: `"Command"`, `"Event"`, `"DomainEvent"`, `"IntegrationEvent"` — case-sensitive substring match applied to both the class name and all ancestor namespace identifier strings. The check on `"Event"` intentionally covers `"DomainEvent"` and `"IntegrationEvent"` as substrings; all four terms are listed explicitly for documentation clarity.
- SK0007 severity escalation to `Error` is gated on field confirmation of zero false positives on the `"DomainEvent"` substring — some projects name classes `IDomainEventHandler` without misusing Redis pub/sub. Until confirmed, severity remains `Warning`.
- Architecture tests for `CachingAbstractionRules` require two test cases minimum: one fire-path (non-exempt assembly references concrete caching) and one pass-path (only exempt assemblies scanned). No exclusion-path test is needed because exemption is enforced by the caller choosing which assemblies to pass, not by an internal filter.
- RS2008 (analyzer release tracking) must be suppressed via `<NoWarn>$(NoWarn);RS2008</NoWarn>` in `SharedKernel.Analyzers.csproj`. The release tracking text-file approach does not reliably suppress it with `EnforceExtendedAnalyzerRules=true`.
- `SharedKernel.Analyzers.Tests.csproj` must explicitly reference `Microsoft.CodeAnalysis.CSharp` at the same version pinned in `SharedKernel.Analyzers.csproj` (currently 4.14.0). The `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` package pulls Roslyn 1.0.1 as a transitive dependency, causing a version conflict that breaks the build without this explicit override.
- Namespace suppression in analyzers uses `SyntaxNode.Parent` walk to find `NamespaceDeclarationSyntax` or `FileScopedNamespaceDeclarationSyntax` ancestors, checking `.Name.ToString().StartsWith("SharedKernel.Primitives")`. Do not use `SemanticModel` for this check — syntax-only is sufficient and cheaper.

---

## DI Registration

N/A — `00.Governance` is tooling-only. No runtime DI registration.

---

## AOT Compatibility

- `SharedKernel.Analyzers` runs inside the compiler host — AOT does not apply; Roslyn runs on the framework's CLR.
- `SharedKernel.ArchitectureTests` runs in test harness — AOT not required.
- `SharedKernel.Benchmarks` runs in a dedicated benchmark process — AOT not required but can be used to measure AOT startup cost with a separate benchmark target.
- `SharedKernel.Linter` ships only files — AOT not applicable.

---

## Test Rules

- Analyzer tests live in `SharedKernel.Analyzers/SharedKernel.Analyzers.Tests/`.
- Each diagnostic rule (SK0001–SK00N) must have at least:
  - One test that **expects the diagnostic to fire** on a minimal violating code snippet.
  - One test that **expects no diagnostic** on a compliant alternative.
- Analyzer tests use `CSharpAnalyzerTest<TAnalyzer, XUnitVerifier>` from `Microsoft.CodeAnalysis.CSharp.Testing.XUnit`.
- Architecture test helpers in `SharedKernel.ArchitectureTests` are validated in their own test project via contrived in-memory assemblies or known-violation assemblies included as test fixtures.
- Benchmark projects are excluded from `dotnet test` runs — gate them behind `[assembly: System.Diagnostics.Conditional("BENCHMARK")]` or a separate build target.

---

## Changelog

> Maintained by the governance domain agent. One line per significant change.

- [2026-05-15] Domain brain initialized — packages, diagnostic registry, architecture test contracts, benchmark config, linter distribution strategy
- [2026-05-15] SK0006 GuardClauseThrow added to diagnostic registry; GuardPurityRules and DoesNotContainThrowIlPredicate added to architecture test contracts; Mono.Cecil IL inspection implementation rules added — WO-002 P-004
- [2026-05-15] SK0003 trigger narrowed to exact types only; ArchitectureRuleBase/LayeringRules updated to ConditionList API; BenchmarkConfig Job.Short→explicit form; RS2008 suppression, Roslyn pin, and test pattern rules added — SK.00.Core implementation (sync-brain)
- [2026-05-18] SK0007 RedisChannelServiceMessagingSubstitute added to diagnostic registry; CachingAbstractionRules added to architecture test contracts with three-assembly exemption list; seven new implementation rules added for caching boundary enforcement — WO-003 P-009
