# 00.Governance — Domain Brain

## What This Domain Is

The enforcement layer: Roslyn analyzers, architecture-test rules, a BenchmarkDotNet configuration and a distributable
formatting/style package. It ships **no runtime code**. Every package here is **Tooling tier**
(`<SharedKernelTier>Tooling</SharedKernelTier>`): it references no SharedKernel package at runtime, and no production
package may reference it.

The dependency rules between the platform's own packages are **not** architecture tests any more. They are the tier
matrix, enforced by the build (`eng/SharedKernelTiers.targets`). This domain owns the tests that prove that check keeps
working and the purity rules the matrix cannot express.

Philosophy: **Enforce at build time. Fail loudly with the fix in the message. Zero runtime cost.**

Per-rule consumer documentation (why, what it flags, what it does not, examples, suppression) lives in
[`README.md`](README.md); the architecture-rule catalog lives in
[`SharedKernel.ArchitectureTests/README.md`](SharedKernel.ArchitectureTests/README.md). This file holds maintainer
rules only. History is in [`state-map.md`](state-map.md).

---

## Packages

| Package | Role | Target | Published |
| --- | --- | --- | --- |
| `SharedKernel.Analyzers` | Roslyn analyzers (SKnnnn diagnostics) | `netstandard2.0` | yes, development dependency |
| `SharedKernel.ArchitectureTests` | NetArchTest + Mono.Cecil rule factories, IL assertions, `ArchitectureRuleBase` | `net10.0` | yes, development dependency |
| `SharedKernel.Linter` | Content-only: CSharpier format check/format target and the shared `.editorconfig` | — (no DLL) | yes, development dependency |
| `SharedKernel.Benchmarks` | `SharedKernelBenchmarkConfig` / `[SharedKernelBenchmark]` | `net10.0` | no |

`_verification/` holds three standalone consumers (`AnalyzerConsumer`, `ArchTestConsumer`, `LinterConsumer`) that
restore the packed packages in CI; the analyzer consumer's build must report `SK0001`.

| Concern | Technology |
| --- | --- |
| Analyzers | `Microsoft.CodeAnalysis.CSharp` 4.14.0 (pinned; the test project pins the same version) |
| Analyzer tests | `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` (`CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>`) |
| Architecture rules | `NetArchTest.eNt`, `Mono.Cecil` |
| Benchmarks | `BenchmarkDotNet` 0.15.x |
| Formatting | CSharpier (version pinned by the package), `.editorconfig` |

---

## Tier enforcement (what replaced the numbered layers)

Every packable csproj declares `<SharedKernelTier>`: Foundation, Model, Abstractions, Adapter, Host, Testing or
Tooling. `eng/SharedKernelTiers.targets` (imported by `Directory.Build.targets`) runs before `CoreCompile` and fails
the build — every diagnostic is an **error**; there is no baseline and no downgrade:

| Code | Meaning |
| --- | --- |
| SKTIER000 | Unknown tier name |
| SKTIER001 | A `ProjectReference` to a tier this project's tier may not reference |
| SKTIER002 | An Adapter → Adapter reference not declared in `<SharedKernelAllowedAdapterReferences>` |
| SKTIER003 | A Model/Abstractions project takes a runtime NuGet package outside `Microsoft.Extensions.*.Abstractions` |
| SKTIER004 | A tiered project references a project that declares no tier |
| SKTIER005 | A packable library declares no tier |
| SKTIER006 | Any ASP.NET Core reference (framework or `Microsoft.AspNetCore.*` package, transitive included) below Host/Testing |

Matrix: Foundation → Foundation; Model → Foundation, Model; Abstractions → Foundation, Model, Abstractions; Adapter →
those plus declared adapters; Host → everything but Testing/Tooling; Testing → everything but Tooling; Tooling →
nothing. Test projects, consumer-verify harnesses and samples declare no tier and are not checked.

The proofs that the check itself still works:

- **`eng/verify-tier-errors.sh`** — builds two throw-away probe projects that must fail with SKTIER001 and SKTIER006;
  run by `verify.yml`'s `tier-check` job. A downgrade, a wrong condition or a target that stops running would leave
  the normal build green, so never remove it.
- **`DependencyGraphRulesTests`** (in `SharedKernel.ArchitectureTests.Tests`, reading the csproj graph):
  `EveryPackableProject_DeclaresAKnownTier`, `EveryDirectReference_RespectsTheTierMatrix`,
  `AspNetCore_IsReferencedOnlyByHostAndTestingProjects`, `TestingPackages_AreReferencedOnlyByTestingProjectsOrTests`,
  `MediatR_IsReferencedOnlyByTheMediatorAdapter`, `ProjectReferenceGraph_HasNoCycles`.
- **`OptionalDependencySatelliteRulesTests`** — the MassTransit core references no transport, Azure or EF Core package
  (and its assembly loads none); satellites are Adapters with a declared edge to the core; `Presentation.Grpc` never
  reaches `WebApi`; `Presentation.Core` is Host with no ASP.NET Core/Presentation dependency; `Presentation.SignalR`
  references no Redis; GraphQL is a Presentation Host package.
- **`TestingPackagesNeverReferencedByProductionTests`** — no production project references a Testing package; the
  core `SharedKernel.Testing` depends only on Foundation and Model packages.

**Never add an architecture rule that restates a tier edge** ("X may only reference Y"). Change the matrix, a tier, or
`<SharedKernelAllowedAdapterReferences>` instead. Adding a package means: declare its tier, add it to
`Platform.SharedKernel.slnx` and the right `.slnf`, and check the MAX_PATH budget for its test assembly.

---

## Analyzer registry

All analyzers report **Warning** by default; consumers escalate through `.editorconfig`. Full reference:
[`README.md` → Analyzer rule reference](README.md#rule-reference).

| Block | Rules |
| --- | --- |
| Core standards | SK0001 DirectDateTimeUsage · SK0002 DirectMicrosoftFeatureManagerUsage · SK0003 RawExceptionThrow · SK0004 NullErrorReturn · SK0005 StringOnlyExceptionConstructor · SK0006 GuardClauseThrow · SK0007 RedisChannelServiceMessagingSubstitute · SK0008 AggregateRootDispatchCoupling · SK0009 DomainEventMissingVersionAttribute · SK0010 SpecificationOrderingConflict · SK0011 GuidFormatCodeMisuse · SK0037 ValueObjectMissingEnsureValid · SK0038 IntegrationEventMissingAttribute · SK0039 InvalidIntegrationEventAttribute |
| Application / communication | SK0013 RawHttpClientConstructorInjection · SK0014 ClosedGenericResiliencePipelineRegistration · SK0016 RequestTypeShortNameUsage · SK0017 CommandImplementsCacheableQuery · SK0018 QueryImplementsInvalidatesCache · SK0040 PipelineMarkerResponseShapeMismatch · SK0041 DuplicateCacheableQueryName |
| Logging | SK0020 DirectILoggerExtensionMethodUsage · SK0021 HandWrittenLoggerMessageDefineDelegate (one analyzer, `LoggingAuthoringStyleAnalyzer`) |
| Cross-cutting, security, privacy | SK0022 CrossCuttingMagicStringLiteral · SK0023 NonSingletonAmazonS3ClientRegistration · SK0024 RawSearchFieldNameLiteral · SK0025 ObsoleteElasticsearchClientUsage · SK0026 RawIntelligenceProviderClientConstructorInjection · SK0027 RawIntelligenceIdentifierLiteral · SK0028 NonDeterministicApiUsageInsideWorkflow · SK0029 RawTemporalClientConstructorInjection · SK0030 ResultOutcomeDiscarded · SK0031 RawSecurityContextConstructorInjection · SK0032 CorsWildcardOriginWithCredentials · SK0033 ReflectionBasedObjectMapperUsage · SK0034 AmountCurrencyPairCoupling (advisory, never escalated) · SK0035 UnmaskedClassifiedDataAtLoggingCallSite · SK0036 RawRpcExceptionConstruction |
| Persistence | SK0042 NonConstantDapperSqlArgument · SK0201 TenantedDbContextOnModelCreatingGuard · SK0202 IgnoreQueryFiltersOutsideTenantedRepository |
| Messaging | SK0703 MessageBusSingletonRegistration · SK0704 HardcodedQueueUriInGetSendEndpoint · SK0705 FaultConsumerDirectRegistration · SK0708 BatchConsumerRegisteredViaAddConsumer |
| Removed | SK0015 (streams have the kernel `IStreamPipelineBehavior<,>`; no mediator registration — P-567), SK0019 (target type removed — P-544) |

IDs that are architecture tests rather than analyzers (they need a whole assembly): SK0012 `ReflectionGuardRules`,
SK0301–SK0303 `EncryptionPatternGuardRules`, SK0701–SK0702 `MessagingArchitectureRules`, SK0706
`ExtendedMessagingArchitectureRules`. SK0707 (saga states) was retired with sagas (P-560).

---

## Architecture rules

`SharedKernel.ArchitectureTests` ships rule factories that return `ConditionList` (or `ConditionList[]`, one element
per scanned assembly) plus IL assertion helpers. Consumers assert them through `ArchitectureRuleBase.AssertRule`/
`AssertRules`, or call `GetResult()` themselves. The catalog, which rules are portable and which police this
platform's own packages, is in the package README.

| Rule class | Rules |
| --- | --- |
| `SharedKernelLayeringRules` | `ContractsNeverReferencesDomain`, `DomainNeverReferencesContracts`, `ModelNeverReferencesLogging`, `TestingNeverReferencedByProduction` — only what the tier matrix cannot express |
| `DomainLayerPurityRules`, `DomainGoldStandardRules`, `GuardPurityRules`, `CoreArchitectureRules` | Domain purity (no infrastructure, clock, event handlers), domain-service base, `Guard.Against` never throws, `TryAdd*` registration convention |
| `ContractsPurityRules` | Integration events are sealed, behaviour-free; no domain or `Result` type on the contracts surface |
| `ApplicationPipelineRules`, `UnitOfWorkSeamRules`, `MetricsInstrumentationRules` | Behaviors reference no concrete infrastructure; `Application.Pipeline` references no cache/Polly/hosting/Core; `.Pipeline.Caching` only `Caching.Abstractions`; `IUnitOfWork`/`IRequestContext`/`IAuditTrailWriter` declared only in `SharedKernel.Execution`; duration histograms carry an `outcome` tag |
| `PersistenceLayerProtectionRules`, `PersistenceInterfaceOwnershipRules`, `RepositoryContractCompletenessRules`, `EfCorePackageHygieneRules`, `PersistenceNamespaceConventionRules` | One `SaveChanges` call site, no `IQueryable` escape, read repositories never track, one declaring assembly for `IUserContext`, no tenant-identity interface next to `IRequestContext.TenantId`, EF Core hygiene, namespace placement |
| `RedisTopologyRules` | Redis core never references its role packages; siblings independent; pub/sub ↛ messaging and messaging ↛ caching; `Caching.Abstractions` references only DI abstractions and declares no provider types; no RedLock |
| `StorageTopologyRules`, `SearchTopologyRules`, `IntelligenceTopologyRules`, `WorkflowTopologyRules` | Abstractions free of vendor SDKs; sibling providers independent (Obs → S3 only); S3 SDK only in providers; no health-checks dependency in providers; raw Temporal accessor never consumed in-repo |
| `CommunicationLayeringRules`, `PresentationLayeringRules` | gRPC (client and server) never references `SharedKernel.Contracts`; interceptors/filters through platform bases; `ProblemDetails` shaped only in `Presentation.WebApi`; no inline `Result` branch before an HTTP result |
| `SecurityArchitectureRules`, `CryptoIsolationRules`, `EncryptionPatternGuardRules` | No request context in the domain; no singleton `IUserContext`; DPoP parsing only in Oidc; client-certificate reads only in Mtls; no raw cipher outside `SharedKernel.Cryptography`; no encryption attribute on domain entities |
| `MessagingArchitectureRules`, `ExtendedMessagingArchitectureRules` | No raw bus/publisher/scheduler injection outside messaging; no publisher in the domain |
| `HealthCheckTagIntegrityRules`, `HealthCheckConstantsUsageRules` | Dependency checks tagged `ready`, never `live`; no bare health-check name/tag literal where constants exist |
| `ReflectionGuardRules` | No `MakeGenericMethod` dispatch outside `ReflectionExemptionRegistry` |

Assertion helpers (not `ConditionList`s): `SecureDefaultsAssertion`, `PipelineOrderAssertion`,
`LoggingEventIdIntegrityAssertion`, `WellKnownConstantOwnershipAssertion`; `RuleAnchor` validates the anchor types
some rules take.

Meta-tests that keep the rule set honest:

- **`RuleExecutionCoverageTests.EveryPublicRuleMethod_IsCalledByAtLeastOneTest`** — a public rule method no test calls
  fails. Every new rule needs a fire-path test, a pass-path test and, where the rule
  names SharedKernel packages, a pass-path test against the **real** assembly.
- **`RuleAnchorValidationTests`** — anchored rules reject null, non-interface and sealed anchors.
- **`RealKernelTypeNameTests`** (Analyzers.Tests) — analyzer fixtures compiled against the real kernel assemblies, so a
  renamed or moved kernel type breaks the test instead of silently disabling the analyzer.
- **`HelpLinkReadmeAnchorTests`** — every analyzer's `HelpLinkUri` resolves to a heading in `README.md`.

---

## Implementation Rules

**Analyzers**
- Target `netstandard2.0` (the Roslyn host); no NuGet dependency beyond `Microsoft.CodeAnalysis.CSharp` 4.14.0; no
  SharedKernel reference. Match kernel types by metadata name/namespace, never by referencing the assembly.
- IDs use the `SK` prefix and are **never reused**. A retired rule moves to "Removed Rules" in
  `AnalyzerReleases.Unshipped.md` and keeps a stub section in `README.md` telling users to delete suppressions.
- RS2008 (release tracking) is satisfied, never suppressed: every new ID goes in `AnalyzerReleases.Unshipped.md`.
- Every `DiagnosticDescriptor` has a `HelpLinkUri` to its `README.md` section (locked by `HelpLinkReadmeAnchorTests`).
- Analyzers that inspect source must call `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` where
  generated code would false-positive (`[LoggerMessage]` output for SK0020/SK0021).
- Literal-vs-constant rules (SK0022, SK0024, SK0027) discriminate on **syntax shape** (`LiteralExpressionSyntax`), not
  on the resolved value or the declaring class, so any named constant passes. Receiver types are resolved with the
  semantic model.
- A namespace exemption walks `SyntaxNode.Parent` for (file-scoped) namespace declarations. Rules that are unsafe in
  every assembly (SK0011, SK0014, SK0022, SK0023, SK0030, SK0703) have no exemption.
- Tests use `CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>` with `{|SKnnnn:…|}` markup; at least one firing and one
  non-firing case per rule. When a rule's correctness depends on a real kernel or third-party type, add a
  `RealKernelTypeNameTests` case.

**Architecture tests**
- Rule factories take every assembly, anchor type and forbidden term from the caller; exemptions are expressed by what
  the caller passes, never by a hardcoded allow-list inside a predicate (the named exception is
  `ReflectionExemptionRegistry`, whose entries each cite their motivating case).
- NetArchTest `NotHaveDependencyOn(term)` matches the term with `StartsWith` against **namespaces** of referenced types,
  not assembly names; a forbidden term that is a prefix of the scanned assembly's own namespace matches itself. Use
  `AssemblyReferenceAllowListPredicate` when the question is about assembly references.
- Mono.Cecil predicates: a `static class` is `IsAbstract && IsSealed`; `const string` is folded into `ldstr` at the
  call site while `static readonly` is an `ldsfld`; closure-free `static` lambdas compile onto the `<>c` nested type,
  so exemptions keyed by type must include it; async methods are inspected through their state machines.
- In-memory fixture assemblies must use names that cannot collide with a loaded real assembly.
- No static mutable state anywhere in this domain.

**Tiers**
- The tier check is MSBuild, not NetArchTest. Its tests read project files (`DependencyGraphRulesTests`) and its
  negative proof is `eng/verify-tier-errors.sh`; keep both whenever `eng/SharedKernelTiers.targets` changes.
- `SharedKernel.ArchitectureTests`, `SharedKernel.Analyzers` and `SharedKernel.Linter` are Tooling tier and
  development dependencies; `SharedKernel.Benchmarks` is not published and never runs under `dotnet test`.

**Linter**
- Ships no DLL (`IncludeBuildOutput=false`). The format check runs only when `ContinuousIntegrationBuild=true` or
  `SharedKernelLinterEnforceFormatting=true`; `SharedKernelLinterFormat` formats with the pinned CSharpier;
  `InstallSharedKernelLinterConfig` never overwrites an existing `.editorconfig` unless asked.

---

## Test Rules

- Analyzer tests: `SharedKernel.Analyzers/SharedKernel.Analyzers.Tests/`. Architecture-rule tests:
  `SharedKernel.ArchitectureTests/SharedKernel.ArchitectureTests.Tests/`. Linter: `SharedKernel.Linter.Tests`.
- Every rule: fire path, pass path, and (for exemptions) the exemption path; platform rules also against the real
  assembly. A rule you have never seen fail is not wired up.
- Benchmarks are run with `BenchmarkRunner.Run<T>()`, never through the test runner.

---

## Changelog

History up to P-574 is in [`state-map.md`](state-map.md) and the root [`CLAUDE.changelog.md`](../CLAUDE.changelog.md).

- [2026-09-26] WO-086 (P-563, P-574, P-575): numbered-layer rule classes deleted (`SharedKernelLayeringRules`' layer
  rules, `MessagingLayeringRules`, `CachingAbstractionRules`, `CompositionRootExclusivityRules`,
  `ServiceDefaults*LayeringRules` and the 13→17/13→19 grant rules); the tier check (`eng/SharedKernelTiers.targets`,
  SKTIER000–006) is an error with no baseline; `DependencyGraphRulesTests`, `OptionalDependencySatelliteRulesTests`,
  `RuleExecutionCoverageTests`, `RealKernelTypeNameTests` and `eng/verify-tier-errors.sh` added; SK0015 removed. This
  brain rewritten from 5,600 lines to the final state.
