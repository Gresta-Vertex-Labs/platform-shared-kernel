# 00.Governance — Domain Brain

> The enforcement layer: Roslyn analyzers, architecture-test rule factories and assertions, a BenchmarkDotNet
> configuration and a distributable formatting/style package. It ships **no runtime code**; no production package
> may reference it. It does **not** own the tier check — that is MSBuild (`eng/SharedKernelTiers.targets`); this
> domain owns the tests proving that check still works and the purity rules the tier matrix cannot express.
> Enforce at build time, fail loudly with the fix in the message, zero runtime cost.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Analyzers` | Tooling | Roslyn analyzers (`SKnnnn`), `netstandard2.0`, development dependency |
| `SharedKernel.ArchitectureTests` | Tooling | NetArchTest + Mono.Cecil rule factories returning `ConditionList`, IL assertion helpers, `ArchitectureRuleBase`; `net10.0`, development dependency |
| `SharedKernel.Linter` | Tooling | Content only: CSharpier check/format targets (`build/SharedKernel.Linter.props`/`.targets`) and the shared `config/.editorconfig` |
| `SharedKernel.Benchmarks` | — (not packable, untiered) | `SharedKernelBenchmarkConfig` / `[SharedKernelBenchmark]` for the repo's own benchmarks |

`_verification/` holds three standalone consumers (`AnalyzerConsumer`, `ArchTestConsumer`, `LinterConsumer`) that
restore the packed packages in CI; the analyzer consumer's build must report `SK0001` (and `SK0043`/`SK0044`, which proves the
two newest ids and the SK0044 opt-in). `_verification/SelfAnalysisProbe` is the odd one out: a project with one deliberate violation per
self-analysed rule, outside the solution, built by `SelfAnalysis/verify-self-analysis.sh`.

`SelfAnalysis/` is content, not a package: `ObservabilityMode.props` (the one switch), `SharedKernel.SelfAnalysis.props` (the build wiring a
`src/` project gets), `kernel-report.globalconfig` / `kernel-enforce.globalconfig` (severity of every SK id and `CA2200`) and
`verify-self-analysis.sh`. devops-lead imports the props once into the root `Directory.Build.targets` (after the tier is known); this domain owns the files.

## Public Entry Points

- **Analyzers** — every rule reports **Warning** by default; consumers escalate in `.editorconfig`.
- **Architecture tests** — rule classes in `SharedKernel.ArchitectureTests/Rules/*Rules.cs` return `ConditionList`;
  assert through `ArchitectureRuleBase.AssertRule`/`AssertRules` or `GetResult()`. Helpers: `SecureDefaultsAssertion`,
  `PipelineOrderAssertion`, `LoggingEventIdIntegrityAssertion`, `WellKnownConstantOwnershipAssertion`, `RuleAnchor`,
  `ReflectionExemptionRegistry`, predicates in `Predicates/`. Consumer docs: `SharedKernel.ArchitectureTests` README.
- **Observability assertions** (`SharedKernel.ArchitectureTests.Observability`) — `PackageEventIdBlockAssertion`, `TelemetryNamingAssertion`,
  `LogTemplateAssertion`, `ErrorCodeFormatAssertion` (each `Find(...)` returns an `ObservabilityReport`; `Assert(..., ObservabilityMode, TextWriter?)`
  applies the mode), the model (`ObservabilityFinding`, `ScanTarget`, `ObservabilityReport`), `ObservabilityEnforcement` (`Configured`, `Apply`),
  `DomainRegistry` (root `CLAUDE.md` table), `EventIdOwnership` (`## Logging` tables) and `ObservabilitySarifReader`.
- **Linter** — property `SharedKernelLinterEnforceFormatting`; targets `SharedKernelLinterFormat`, `InstallSharedKernelLinterConfig`.
- **Benchmarks** — `[SharedKernelBenchmark]` + `BenchmarkRunner.Run<T>()`.

### Analyzer rules

**47 active rules.** The authoritative index (title, category, what it flags, the fix) is the `SharedKernel.Analyzers`
README (`SharedKernel.Analyzers/README.md`, "Rule index" and "Rules"); the IDE help-link targets are the `<a id>` rows
of the rule index in `tools/Governance/README.md`.

| ID range | Area |
| --- | --- |
| SK0001–SK0044 | General conventions: clock, `Result`/`Error`, exceptions (SK0043), logging (SK0044 is opt-in), magic strings (SK0022 incl. metric tag keys), pipeline markers, per-capability client and literal rules |
| SK0201–SK0202 | Persistence tenancy (`TenantedDbContext`, `IgnoreQueryFilters`) |
| SK0703–SK0708 | Messaging registration |

Gaps: retired, never reused — SK0015, SK0019, SK0707. Architecture tests, not analyzers (they need a whole assembly) —
SK0012 `ReflectionGuardRules`, SK0301–SK0303 `EncryptionPatternGuardRules`, SK0701–SK0702 `MessagingArchitectureRules`,
SK0706 `ExtendedMessagingArchitectureRules`.

**Adding an analyzer rule** — one change carries all of:

1. The next free ID in the matching range (general: **SK0045**); never a retired or architecture-test ID.
2. The analyzer in `SharedKernel.Analyzers/Diagnostics/SKnnnn_{Name}Analyzer.cs`, its descriptor built with
   `AnalyzerBase.CreateDescriptor(…, readmeAnchor: "sknnnn-{name}")`.
3. A row carrying `<a id="sknnnn-{name}"></a>` in the rule index of `tools/Governance/README.md` (`HelpLinkReadmeAnchorTests`).
4. An index row and a `#### SKnnnn` entry in the `SharedKernel.Analyzers` README; bump its rule count and badge.
5. A row under "New Rules" in `AnalyzerReleases.Unshipped.md` (severity `Disabled` for an `isEnabledByDefault: false` rule, else RS2001 fires).
6. The id in `SharedKernelObservabilityRuleIds` (`SelfAnalysis/SharedKernel.SelfAnalysis.props`) and in both `kernel-report.globalconfig` and `kernel-enforce.globalconfig` (`ObservabilitySwitchTests` compares all three with the analyzer's descriptors).
7. Firing and non-firing tests, plus a `RealKernelTypeNameTests` case when the rule matches a real kernel or third-party type.

### Architecture-test rule families

| Rule classes | What they enforce |
| --- | --- |
| `SharedKernelLayeringRules` | Only what tiers cannot express: `ContractsNeverReferencesDomain`, `DomainNeverReferencesContracts`, `ModelNeverReferencesLogging`, `TestingNeverReferencedByProduction` |
| `DomainLayerPurityRules`, `DomainGoldStandardRules`, `GuardPurityRules`, `CoreArchitectureRules` | Domain purity (no infrastructure, clock, handlers), domain-service base, `Guard.Against` never throws, `TryAdd*` registration |
| `ContractsPurityRules` | Integration events sealed and behaviour-free; no domain or `Result` type on the contracts surface |
| `ApplicationPipelineRules`, `UnitOfWorkSeamRules`, `MetricsInstrumentationRules` | Behaviors reference no concrete infrastructure (`PipelineNeverReferencesCachingPollyOrHosting`); `IUnitOfWork`/`IRequestContext`/`IAuditTrailWriter` declared only in `SharedKernel.Execution`; duration histograms carry `outcome` |
| `PersistenceLayerProtectionRules`, `PersistenceInterfaceOwnershipRules`, `RepositoryContractCompletenessRules`, `EfCorePackageHygieneRules`, `PersistenceNamespaceConventionRules` | One `SaveChanges` call site, no `IQueryable` escape, read repositories never track, interface ownership, EF Core hygiene, namespace placement |
| `RedisTopologyRules` | Redis.Core never references role packages; siblings independent; pub/sub ↛ messaging, messaging ↛ caching; `Caching.Abstractions` declares no provider types; no RedLock |
| `StorageTopologyRules`, `SearchTopologyRules`, `IntelligenceTopologyRules`, `WorkflowTopologyRules` | Abstractions free of vendor SDKs; sibling providers independent; no health-checks dependency in providers; the raw Temporal accessor never consumed in-repo |
| `CommunicationLayeringRules`, `PresentationLayeringRules` | gRPC client/server never reference `SharedKernel.Contracts`; interceptors/filters through platform bases; `ProblemDetails` shaped only in `Presentation.WebApi`; no inline `Result` branch before an HTTP result; OpenAPI stack only in `Presentation.OpenApi` (`NoOpenApiStackDependencyOutsideOpenApiAddOn`) |
| `SecurityArchitectureRules`, `CryptoIsolationRules`, `EncryptionPatternGuardRules` | No request context in the domain; no singleton `IUserContext`; DPoP parsing only in Oidc; client-certificate reads only in Mtls; no raw cipher outside `SharedKernel.Cryptography`; no encryption attribute on domain entities |
| `MessagingArchitectureRules`, `ExtendedMessagingArchitectureRules` | No raw bus/publisher/scheduler injection outside messaging; no publisher in the domain |
| `HealthCheckTagIntegrityRules`, `HealthCheckConstantsUsageRules` | Dependency checks tagged `ready`, never `live`; no bare health-check name/tag literal |
| `ReflectionGuardRules` | No `MakeGenericMethod` dispatch outside `ReflectionExemptionRegistry` |
| `PackageEventIdBlockAssertion`, `TelemetryNamingAssertion`, `LogTemplateAssertion`, `ErrorCodeFormatAssertion` (assertions, not `ConditionList` factories) | Platform-wide observability in report mode: EventId sub-block ownership, `ActivitySource`/`Meter` names and instrument units, log-template style, error-code format |

Repo-graph tests (in `SharedKernel.ArchitectureTests.Tests`, reading csproj and README files):

- `DependencyGraphRulesTests` — known tier on every packable project, tier matrix, ASP.NET Core only in Host/Testing,
  Testing packages only in Testing/tests, MediatR only in the mediator adapter, analyzer-only references target
  Tooling, no cycles. Its second part (file `OptionalDependencySatelliteRulesTests.cs`, same `partial class`):
  MassTransit core loads no transport/Azure/EF Core; satellites declare an edge; `Presentation.Grpc` ↛ `WebApi`;
  `Presentation.Core` depends on no sibling.
- `TestingPackagesNeverReferencedByProductionTests`.
- `PackageReadmeStandardTests` — every packable package README follows `docs/package-readme-standard.md`; public
  READMEs name capabilities, not domain ids; domain README package badges match the packages owned.

## Rules & Invariants

1. **Never add an architecture rule that restates a tier edge.** Change the matrix, a tier, or `<SharedKernelAllowedAdapterReferences>` instead.
2. Every tier diagnostic (SKTIER000–006) is an **error** — no baseline, no downgrade. Keep `eng/verify-tier-errors.sh` and `DependencyGraphRulesTests` in step with `eng/SharedKernelTiers.targets`.
3. A `ReferenceOutputAssembly="false"` reference is exempt from the matrix and may only target a Tooling project (the one such edge: `Presentation.WebApi` → `Presentation.WebApi.Generators`).
4. **Analyzers**: `netstandard2.0`, only `Microsoft.CodeAnalysis.CSharp` 4.14.0 (pinned in `Directory.Packages.props`; the test project matches), no SharedKernel reference — match kernel types by metadata name/namespace.
5. IDs use the `SK` prefix and are **never reused**; a retired rule moves to "Removed Rules" in `AnalyzerReleases.Unshipped.md` and keeps a retired row in both rule indexes telling users to delete suppressions.
6. RS2008 is satisfied, never suppressed: every new ID goes in `AnalyzerReleases.Unshipped.md`.
7. Every `DiagnosticDescriptor` is built by `AnalyzerBase.CreateDescriptor` with a `readmeAnchor` that exists as an explicit `<a id>` in `tools/Governance/README.md` (`HelpLinkReadmeAnchorTests`).
8. Call `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` where generated code would false-positive (`[LoggerMessage]` output for SK0020/SK0021). **Exception: a rule that inspects an attribute on a `[LoggerMessage]` method (SK0044)** — the generator adds a `[GeneratedCode]` part to that very partial method, so the driver treats the whole merged symbol as generated and skips every node action inside it, attribute arguments included; `None` makes the rule silently never fire on a real partial method (an unpartial fixture hides it). Analyze generated code and skip generated trees by hand (`.g.cs`, an `<auto-generated>` header).
9. Literal-vs-constant rules (SK0022, SK0024, SK0027) discriminate on **syntax shape** (`LiteralExpressionSyntax`), so any named constant passes; receivers resolve through the semantic model.
10. Rules unsafe in every assembly (SK0011, SK0014, SK0022, SK0023, SK0030, SK0703) have no namespace exemption; exemptions walk `SyntaxNode.Parent` for (file-scoped) namespaces.
11. SK0040 matches `SharedKernel.Application.Authorization.RequirePermissionAttribute` (inherited) and `SharedKernel.Application.Idempotency.IIdempotentRequest` only — a same-named attribute elsewhere never matches. SK0036 exempts the `SharedKernel.Presentation.Grpc` prefix. SK0016/SK0017/SK0041 match the `SharedKernel.Application` namespace prefix.
12. **Rule factories take every assembly, anchor and forbidden term from the caller**; no hardcoded allow-lists inside predicates (only exception: `ReflectionExemptionRegistry`, each entry citing its case).
13. NetArchTest `NotHaveDependencyOn(term)` is a `StartsWith` over **namespaces**, not assembly names; a term that prefixes the scanned assembly's own namespace matches itself. Use `AssemblyReferenceAllowListPredicate` for assembly-reference questions.
14. Mono.Cecil: a `static class` is `IsAbstract && IsSealed`; `const string` folds to `ldstr`, `static readonly` is `ldsfld`; static lambdas compile onto `<>c`; inspect async methods through their state machines.
15. In-memory fixture assemblies use names that cannot collide with a loaded real assembly. No static mutable state anywhere in this domain.
16. Linter: the format check runs only when `ContinuousIntegrationBuild=true` or `SharedKernelLinterEnforceFormatting=true`; `InstallSharedKernelLinterConfig` never overwrites an existing `.editorconfig` unless `SharedKernelLinterOverwriteConfig=true`.
17. **Observability rules ship in report mode.** One property, `SharedKernelObservabilityMode` in `tools/Governance/SelfAnalysis/ObservabilityMode.props` (`report` default, `enforce`), drives the kernel self-analysis severity (`kernel-report.globalconfig`: SK analyzers and CA2200 at `warning`, listed in `WarningsNotAsErrors`; `kernel-enforce.globalconfig`: `error`) and the platform-wide assertions (`ObservabilityEnforcement.Configured` reads the value stamped into `SharedKernel.ArchitectureTests.Tests`; `SHAREDKERNEL_OBSERVABILITY_MODE` overrides it). Going to failing is that one edit; every assertion also takes the mode as a parameter so the firing tests prove the enforce path. A new SK id must be added to `SharedKernelObservabilityRuleIds` and to both globalconfigs (`ObservabilitySwitchTests` compares them with the analyzer's descriptors). SK0034 is `suggestion` in both modes (advisory by decision). The severity file reaches the compiler as `EditorConfigFiles` added inside a target: a static `GlobalAnalyzerConfigFiles` item is copied during evaluation, which a `Directory.Build.targets` import is too late for. EventId ownership is read from the `## Logging` tables of the domain `CLAUDE.md` files (columns `Sub-block`/`Range` and `Package`/`Owner`; a package family shares one sub-block, base at 1-49, Adapters at 50-99), not assumed one block per package; an assembly with no declared range must keep to one 100-wide sub-block that nobody else uses; error-code format tolerates domains without an `ErrorCodes` class and caller-supplied protocol-mirror prefixes (`grpc`, `http`); no check looks at an error factory's visibility. Per-project SARIF files (`sharedkernel-observability.sarif`, next to the assembly) carry the analyzer findings into the platform report.
18. **Platform assertions read built assemblies with Mono.Cecil, return findings, and never throw by themselves.** A finding carries rule, domain (longest folder prefix in the root `CLAUDE.md` domain table), assembly and site; forbidden placeholder names, name patterns, protocol prefixes and exemptions (each with a reason citing a domain `CLAUDE.md`) come from the caller (rule 12). A `const` is indistinguishable from a literal in IL (rule 14), so anything about "named constant versus literal" is an analyzer (SK0022, SK0044), never an assertion. A project whose DLL is missing is an `unscanned` finding; a name or unit the straight-line stack walk cannot follow (a field, a parameter, a merge point) is `unverifiable`: listed, never a failure.
19. SK0043 (an exception that exposes an `Error` or implements `IHasError` must derive `SharedKernelException`) matches by metadata name, any accessibility, no namespace exemption; a private `Error` property does not count as exposing. SK0044 (bare numeric literal as a `[LoggerMessage]` EventId) is off by default and enabled for the kernel by the self-analysis config, because literal ids are legitimate in a consuming service. SK0043 checks the base type only, never constructor shape (03.Domain exempts two exceptions from the `(Error)` constructors). SK0022's fifth shape (metric and span tag keys: `TagList.Add`, `new KeyValuePair<string, object?>(literal, …)`, a `TagList` initializer, the tag collection passed to `ActivitySource.StartActivity`) is a warning for consumers like shapes 1-4.
20. *(planned, SK.00.ObservabilityEnforce)* **The flip leaves one behaviour.** No report mode, override variable, `WarningsNotAsErrors` list or mode parameter remains; `kernel.globalconfig` fixes every SK id and `CA2200` at `error` for the kernel (SK0034 at `suggestion`, SK0044 enabled) and the platform assertions throw one aggregated exception. Consumers' default severities do not change. A new SK id must appear in `kernel.globalconfig` (a test compares it with the analyzer's descriptors). A suppression of these rules in `src/` is legal only when the owning domain's `CLAUDE.md` names the file and the reason (an assertion checks).

## Decisions

| Decision | Why |
| --- | --- |
| Tier dependency rules live in MSBuild, not NetArchTest | Fails before compile, with one matrix instead of per-package rules |
| Analyzers default to Warning | Consumers adopt incrementally and escalate in `.editorconfig` |
| SK0034 is advisory only | An amount + currency pair is sometimes legitimate (wire DTOs) |
| Syntax-shape matching for literal rules | Any named constant passes regardless of where it is declared |
| Explicit `<a id>` anchors for help links | GitHub heading slugs are unstable; an explicit anchor is byte-exact and test-checked |
| `RealKernelTypeNameTests` compile analyzer fixtures against real kernel assemblies | A renamed kernel type breaks a test instead of silently disabling the analyzer |
| `RuleExecutionCoverageTests` fails on a public rule with no calling test | A rule never seen failing is not wired up |
| Benchmarks never run under `dotnet test` | BenchmarkDotNet needs a Release, out-of-process run |
| Tag-key literals at metric call sites extend SK0022 (a fifth shape) instead of a new rule | One rule, "no raw literal at a cross-cutting call site"; `Activity.SetTag` is already shape 2 |
| The bare-EventId-literal check is an analyzer, not a test | IL folds `const` into the attribute blob; only syntax shape tells a literal from a named constant |
| SK0044 is `isEnabledByDefault: false` | It encodes kernel convention (L1); consumers may opt in |
| The telemetry IL check has no allow-list for Temporal SDK instruments | They are created by the SDK through `CustomMetricMeter`, never at a kernel `Meter.Create*` call site |
| *(planned, SK.00.ObservabilityEnforce)* The flip changes the kernel's self-analysis and the platform assertions, not analyzer defaults | Consumers keep Warning and escalate in `.editorconfig`; SK0034 stays advisory in the kernel too |
| *(planned, SK.00.ObservabilityEnforce)* `unscanned` fails, `unverifiable` is listed | A missing DLL hides a whole assembly; a unit loaded from a field is a limit of IL analysis, not a violation |

## Logging

Analyzers and architecture tests do not log. The `00` EventId block is unused;
`LoggingEventIdIntegrityAssertion` is the helper other domains use to check their own blocks.

## Cross-Domain Couplings

- **Every domain** — rule classes and analyzers encode other domains' purity rules by metadata name; renaming or moving a type named here updates `RealKernelTypeNameTests` or a real-assembly pass-path test in the same change.
- **01.Core** — `LoggingEventIdRanges` (EventId blocks), `WellKnownHeaders`/`WellKnownBaggageKeys` (named by SK0022's message). `Error`, `IHasError`, `SharedKernelException` (SK0043 by metadata name) and `ErrorCodes` (the error-code format check).
- **Build (`src/`)** — the self-analysis props are imported into the root build files by devops-lead; this domain owns the files, not the import.
- **01.Core, 13.ServiceDefaults** — their `## Logging` ownership tables are read by the EventId ownership assertion; **11, 12, 14** — protocol-mirror and protocol error values are exempted by entries citing their `CLAUDE.md`. **14.Presentation** — the GraphQL → WebApi/Core references are covered by the graph and layering tests *(planned, SK.00.ObservabilityEnforce)*.
- **05.Application** — `PipelineOrderAssertion` locks the built-in pipeline order through `AddSharedKernelApplication(…)`.
- **Build (`eng/`)** — `SharedKernelTiers.targets`, `verify-tier-errors.sh`; documented in `CONTRIBUTING.md` and `eng/README.md`.
- No production package references this domain; analyzer-only references are the only allowed edges into it.

## Testing

- Unit lane only: `SharedKernel.Analyzers.Tests`, `SharedKernel.ArchitectureTests.Tests`, `SharedKernel.Linter.Tests` (nested in each package); no container fixtures.
- Analyzer tests use `CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>` with `{|SKnnnn:…|}` markup: at least one firing and one non-firing case per rule.
- Every architecture rule: fire path, pass path, exemption path where one exists, and a pass path against the **real** assembly for platform rules.
- Meta-tests: `RuleExecutionCoverageTests.EveryPublicRuleMethod_IsCalledByAtLeastOneTest`, `RuleAnchorValidationTests`, `RealKernelTypeNameTests`, `HelpLinkReadmeAnchorTests`.
- `_verification/` consumers prove the packed packages restore and work outside the repo.
- Observability (report mode): `ObservabilitySwitchTests` (default `report`, `Apply` never throws in report mode and throws one aggregate in enforce mode, the override variable wins, the rule list and both severity files equal the analyzer's descriptors), one test class per assertion with collision-proof `Fixture.*` assemblies (fire path, pass path, enforce throws, report does not) plus a pass path against a real assembly, and `PlatformObservabilityReportTests`, which runs all four assertions over every built production assembly and adds the SARIF findings when the files exist. The platform tests assert the report is produced and every finding names its domain, never that it is empty; they write `artifacts/observability/platform-report.md`.
- `SelfAnalysis/verify-self-analysis.sh` builds the probe in both modes and a real `src/` project through a stand-in `Directory.Build.targets` (the root import, simulated): warnings only in report mode, errors in enforce mode, and (`--pack <csproj>`) no analyzer dependency in the `.nuspec`.

## Known Limitations

- NetArchTest matches namespaces, so a rule cannot distinguish two assemblies sharing a namespace prefix without `AssemblyReferenceAllowListPredicate`.
- Analyzers match kernel types by name; a type moved without updating the analyzer silently stops firing unless a `RealKernelTypeNameTests` case covers it.
- SK0708 detects batch consumers by name (`BatchConsumer` in the type name), not by interface.
- The IL stack walk behind the telemetry assertion follows straight-line code and locals only: a name or unit from a field, a parameter, a call or a branch merge is `unverifiable`, not a violation. A `static readonly string` name is reported as not constant; a `const` in another assembly satisfies the pattern but not the "public const in this assembly" check.
- EventId ownership is read from prose tables by column header and package word (full name, name after `SharedKernel.`, a leading-dot suffix, or the last segment, matched inside the table's own domain). A table without `Sub-block`/`Range` and `Package`/`Owner` columns, or a package word that names no assembly, declares nothing; that assembly is then held to the default (one unshared 100-wide sub-block). Telling 01.Core and 13.ServiceDefaults to keep one shape is a standing obligation.
- The self-analysis severity file reaches the compiler only through the target in `SharedKernel.SelfAnalysis.props`; the probe and `verify-self-analysis.sh` exist because importing it from a csproj (where a static item works) hides the evaluation-order trap of importing it from `Directory.Build.targets`.
