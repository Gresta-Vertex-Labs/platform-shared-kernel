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
restore the packed packages in CI; the analyzer consumer's build must report `SK0001`.

## Public Entry Points

- **Analyzers** — every rule reports **Warning** by default; consumers escalate in `.editorconfig`.
- **Architecture tests** — rule classes in `SharedKernel.ArchitectureTests/Rules/*Rules.cs` return `ConditionList`;
  assert through `ArchitectureRuleBase.AssertRule`/`AssertRules` or `GetResult()`. Helpers: `SecureDefaultsAssertion`,
  `PipelineOrderAssertion`, `LoggingEventIdIntegrityAssertion`, `WellKnownConstantOwnershipAssertion`, `RuleAnchor`,
  `ReflectionExemptionRegistry`, predicates in `Predicates/`. Consumer docs: `SharedKernel.ArchitectureTests` README.
- **Linter** — property `SharedKernelLinterEnforceFormatting`; targets `SharedKernelLinterFormat`, `InstallSharedKernelLinterConfig`.
- **Benchmarks** — `[SharedKernelBenchmark]` + `BenchmarkRunner.Run<T>()`.

### Analyzer rules

**45 active rules.** The authoritative index (title, category, what it flags, the fix) is the `SharedKernel.Analyzers`
README (`SharedKernel.Analyzers/README.md`, "Rule index" and "Rules"); the IDE help-link targets are the `<a id>` rows
of the rule index in `tools/Governance/README.md`.

| ID range | Area |
| --- | --- |
| SK0001–SK0042 | General conventions: clock, `Result`/`Error`, exceptions, logging, magic strings, pipeline markers, per-capability client and literal rules |
| SK0201–SK0202 | Persistence tenancy (`TenantedDbContext`, `IgnoreQueryFilters`) |
| SK0703–SK0708 | Messaging registration |

Gaps: retired, never reused — SK0015, SK0019, SK0707. Architecture tests, not analyzers (they need a whole assembly) —
SK0012 `ReflectionGuardRules`, SK0301–SK0303 `EncryptionPatternGuardRules`, SK0701–SK0702 `MessagingArchitectureRules`,
SK0706 `ExtendedMessagingArchitectureRules`.

**Adding an analyzer rule** — one change carries all of:

1. The next free ID in the matching range (general: **SK0043**); never a retired or architecture-test ID.
2. The analyzer in `SharedKernel.Analyzers/Diagnostics/SKnnnn_{Name}Analyzer.cs`, its descriptor built with
   `AnalyzerBase.CreateDescriptor(…, readmeAnchor: "sknnnn-{name}")`.
3. A row carrying `<a id="sknnnn-{name}"></a>` in the rule index of `tools/Governance/README.md` (`HelpLinkReadmeAnchorTests`).
4. An index row and a `#### SKnnnn` entry in the `SharedKernel.Analyzers` README; bump its rule count and badge.
5. A row under "New Rules" in `AnalyzerReleases.Unshipped.md`.
6. Firing and non-firing tests, plus a `RealKernelTypeNameTests` case when the rule matches a real kernel or third-party type.

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
8. Call `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` where generated code would false-positive (`[LoggerMessage]` output for SK0020/SK0021).
9. Literal-vs-constant rules (SK0022, SK0024, SK0027) discriminate on **syntax shape** (`LiteralExpressionSyntax`), so any named constant passes; receivers resolve through the semantic model.
10. Rules unsafe in every assembly (SK0011, SK0014, SK0022, SK0023, SK0030, SK0703) have no namespace exemption; exemptions walk `SyntaxNode.Parent` for (file-scoped) namespaces.
11. SK0040 matches `SharedKernel.Application.Authorization.RequirePermissionAttribute` (inherited) and `SharedKernel.Application.Idempotency.IIdempotentRequest` only — a same-named attribute elsewhere never matches. SK0036 exempts the `SharedKernel.Presentation.Grpc` prefix. SK0016/SK0017/SK0041 match the `SharedKernel.Application` namespace prefix.
12. **Rule factories take every assembly, anchor and forbidden term from the caller**; no hardcoded allow-lists inside predicates (only exception: `ReflectionExemptionRegistry`, each entry citing its case).
13. NetArchTest `NotHaveDependencyOn(term)` is a `StartsWith` over **namespaces**, not assembly names; a term that prefixes the scanned assembly's own namespace matches itself. Use `AssemblyReferenceAllowListPredicate` for assembly-reference questions.
14. Mono.Cecil: a `static class` is `IsAbstract && IsSealed`; `const string` folds to `ldstr`, `static readonly` is `ldsfld`; static lambdas compile onto `<>c`; inspect async methods through their state machines.
15. In-memory fixture assemblies use names that cannot collide with a loaded real assembly. No static mutable state anywhere in this domain.
16. Linter: the format check runs only when `ContinuousIntegrationBuild=true` or `SharedKernelLinterEnforceFormatting=true`; `InstallSharedKernelLinterConfig` never overwrites an existing `.editorconfig` unless `SharedKernelLinterOverwriteConfig=true`.

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

## Logging

Analyzers and architecture tests do not log. The `00` EventId block is unused;
`LoggingEventIdIntegrityAssertion` is the helper other domains use to check their own blocks.

## Cross-Domain Couplings

- **Every domain** — rule classes and analyzers encode other domains' purity rules by metadata name; renaming or moving a type named here updates `RealKernelTypeNameTests` or a real-assembly pass-path test in the same change.
- **01.Core** — `LoggingEventIdRanges` (EventId blocks), `WellKnownHeaders`/`WellKnownBaggageKeys` (named by SK0022's message).
- **05.Application** — `PipelineOrderAssertion` locks the built-in pipeline order through `AddSharedKernelApplication(…)`.
- **Build (`eng/`)** — `SharedKernelTiers.targets`, `verify-tier-errors.sh`; documented in `CONTRIBUTING.md` and `eng/README.md`.
- No production package references this domain; analyzer-only references are the only allowed edges into it.

## Testing

- Unit lane only: `SharedKernel.Analyzers.Tests`, `SharedKernel.ArchitectureTests.Tests`, `SharedKernel.Linter.Tests` (nested in each package); no container fixtures.
- Analyzer tests use `CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>` with `{|SKnnnn:…|}` markup: at least one firing and one non-firing case per rule.
- Every architecture rule: fire path, pass path, exemption path where one exists, and a pass path against the **real** assembly for platform rules.
- Meta-tests: `RuleExecutionCoverageTests.EveryPublicRuleMethod_IsCalledByAtLeastOneTest`, `RuleAnchorValidationTests`, `RealKernelTypeNameTests`, `HelpLinkReadmeAnchorTests`.
- `_verification/` consumers prove the packed packages restore and work outside the repo.

## Known Limitations

- NetArchTest matches namespaces, so a rule cannot distinguish two assemblies sharing a namespace prefix without `AssemblyReferenceAllowListPredicate`.
- Analyzers match kernel types by name; a type moved without updating the analyzer silently stops firing unless a `RealKernelTypeNameTests` case covers it.
- SK0708 detects batch consumers by name (`BatchConsumer` in the type name), not by interface.
