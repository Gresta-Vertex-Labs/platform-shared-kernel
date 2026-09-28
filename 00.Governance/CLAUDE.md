# 00.Governance — Domain Brain

> The enforcement layer: Roslyn analyzers, architecture-test rule factories and assertions, a BenchmarkDotNet
> configuration and a distributable formatting/style package. It ships **no runtime code**; no production package
> may reference it. It does **not** own the tier check itself — that is MSBuild (`eng/SharedKernelTiers.targets`,
> imported by `Directory.Build.targets`); this domain owns the tests proving that check still works and the purity
> rules the tier matrix cannot express. Philosophy: enforce at build time, fail loudly with the fix in the message,
> zero runtime cost. Consumer rule docs: `README.md` (analyzers) and `SharedKernel.ArchitectureTests/README.md`
> (architecture rules). The living board is `state-map.md`.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Analyzers` | Tooling | Roslyn analyzers (`SKnnnn`), `netstandard2.0`, packed as a development dependency |
| `SharedKernel.ArchitectureTests` | Tooling | NetArchTest + Mono.Cecil rule factories returning `ConditionList`, IL assertion helpers, `ArchitectureRuleBase`; `net10.0`, development dependency |
| `SharedKernel.Linter` | Tooling | Content only (`IncludeBuildOutput=false`): CSharpier format check/format targets (`build/SharedKernel.Linter.props`/`.targets`) and the shared `.editorconfig` (`config/`) |
| `SharedKernel.Benchmarks` | — (not packable, untiered) | `SharedKernelBenchmarkConfig` / `[SharedKernelBenchmark]` for the repo's own benchmarks |

`_verification/` holds three standalone consumers (`AnalyzerConsumer`, `ArchTestConsumer`, `LinterConsumer`) that
restore the packed packages in CI; the analyzer consumer's build must report `SK0001`.

## Public Entry Points

- **Analyzers** — reference the package; every rule reports **Warning** by default and consumers escalate in
  `.editorconfig` (`dotnet_diagnostic.SKnnnn.severity`). Each `HelpLinkUri` points at its `README.md` section.
- **Architecture tests** — rule classes in `SharedKernel.ArchitectureTests/Rules/*Rules.cs` return `ConditionList`
  (or one per scanned assembly); assert through `ArchitectureRuleBase.AssertRule`/`AssertRules` or `GetResult()`.
  Assertion helpers: `SecureDefaultsAssertion`, `PipelineOrderAssertion` (by type, or by name for the kernel's internal
  behaviors after `AddSharedKernelApplication(…)`), `LoggingEventIdIntegrityAssertion`,
  `WellKnownConstantOwnershipAssertion`; `RuleAnchor` validates anchor types; `ReflectionExemptionRegistry`.
- **Linter** — MSBuild properties/targets: `SharedKernelLinterEnforceFormatting`, `SharedKernelLinterFormat`,
  `InstallSharedKernelLinterConfig`.
- **Benchmarks** — `[SharedKernelBenchmark]` + `BenchmarkRunner.Run<T>()`.

### Analyzer rules

| ID | Rule |
| --- | --- |
| SK0001 | No `DateTime`/`DateTimeOffset.Now/UtcNow` — inject `IClock` |
| SK0002 | No `Microsoft.FeatureManagement` or ambient OpenFeature `Api.Instance` — inject `IFeatureClient` + `FeatureFlag<T>` |
| SK0003 | No raw `Exception`/`ApplicationException` throw — `Result` or a typed kernel exception |
| SK0004 | Never return `null` for `Error` — return `Error.None` |
| SK0005 | A `SharedKernelException` subclass is constructed with an `Error`, not a string only |
| SK0006 | An `IGuardClause` functional-path method never throws |
| SK0007 | `IRedisChannelService` is not a messaging substitute — inject `IMessageBus` |
| SK0008 | Dispatch code depends on `IHasDomainEvents`, not `IAggregateRoot` |
| SK0009 | A domain event carries `[DomainEventVersion]` |
| SK0010 | A specification constructor uses one primary ordering direction |
| SK0011 | `Guid.ToString` with a non-canonical format code |
| SK0013 | No explicit constructor taking `HttpClient` — typed client with a primary constructor |
| SK0014 | Register Polly pipelines as non-generic, string-keyed `ResiliencePipeline` |
| SK0016 | `typeof(X).Name` without a `FullName` companion in tags/keys |
| SK0017 | A command never implements `ICacheableQuery<T>` |
| SK0018 | A query never implements `IInvalidatesCache` |
| SK0020 / SK0021 | `[LoggerMessage]` only — no `ILogger.LogXxx` extension calls, no hand-written `LoggerMessage.Define` (`LoggingAuthoringStyleAnalyzer`) |
| SK0022 | No raw literal at header/baggage/tag/configuration-section/claim call sites — named constant |
| SK0023 | `IAmazonS3` registered as Singleton |
| SK0024 | No raw literal as a search field name |
| SK0025 | No NEST / Elasticsearch.Net (obsolete client) |
| SK0026 | No raw vector-DB/model-SDK client injected outside its provider package |
| SK0027 | No raw literal as a vector collection/field/model identifier |
| SK0028 | No non-deterministic or side-effecting API inside a `[Workflow]` type |
| SK0029 | No raw Temporal client injected outside `SharedKernel.Workflows.Temporal` |
| SK0030 | A `Result` outcome is never silently discarded (`_ =` to discard explicitly) |
| SK0031 | No `IHttpContextAccessor`/`ClaimsPrincipal`/`HttpContext` constructor injection — `IUserContext`/`IRequestContext` |
| SK0032 | CORS wildcard/always-allow origin combined with `AllowCredentials()` |
| SK0033 | No reflection-based object mapper (AutoMapper) — Mapperly or hand-written |
| SK0034 | Advisory: a raw decimal amount + string currency pair — consider `Money` (never escalated) |
| SK0035 | A privacy-classified member passed to an unclassified `[LoggerMessage]` parameter |
| SK0036 | No raw `RpcException`/`Status` construction outside `SharedKernel.Presentation.Grpc` |
| SK0037 | A `ValueObject` constructor must call `EnsureValid()` |
| SK0038 / SK0039 | An `IIntegrationEvent` needs a valid `[IntegrationEvent]` attribute |
| SK0040 | `[RequirePermission]`/`IIdempotentRequest` only on a request whose response is `Result`/`Result<T>` |
| SK0041 | Two `ICacheableQuery<T>` types share a simple type name |
| SK0042 | Dapper `sql` argument must be a compile-time constant |
| SK0201 | A `TenantedDbContext.OnModelCreating` override must call `base` |
| SK0202 | `IgnoreQueryFilters()` only inside `SharedKernel.Persistence.EfCore` or a `TenantedRepository` |
| SK0703 | `IMessageBus`/`IEventPublisher` registered Scoped, never Singleton |
| SK0704 | No hardcoded queue/exchange URI in `GetSendEndpoint` |
| SK0705 | `IFaultConsumer<T>` registered through `AddFaultConsumer`, never directly |
| SK0708 | A batch consumer registered through `AddBatchConsumer`, not `AddConsumer` |

Retired, never reused: SK0015, SK0019 (listed under "Removed Rules" in `AnalyzerReleases.Unshipped.md`), SK0707.
IDs that are architecture tests, not analyzers (they need a whole assembly): SK0012 `ReflectionGuardRules`,
SK0301–SK0303 `EncryptionPatternGuardRules`, SK0701–SK0702 `MessagingArchitectureRules`, SK0706
`ExtendedMessagingArchitectureRules`.

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

Repo-graph tests (in `SharedKernel.ArchitectureTests.Tests`, reading csproj files): `DependencyGraphRulesTests` (known
tier on every packable project, tier matrix, ASP.NET Core only in Host/Testing, Testing packages only in
Testing/tests, MediatR only in the mediator adapter, analyzer-only references target Tooling, no cycles),
`OptionalDependencySatelliteRulesTests` (MassTransit core loads no transport/Azure/EF Core; satellites declare an edge;
`Presentation.Grpc` ↛ `WebApi`; `Presentation.Core` depends on no sibling), `TestingPackagesNeverReferencedByProductionTests`.

## Rules & Invariants

1. **Never add an architecture rule that restates a tier edge.** Change the matrix, a tier, or `<SharedKernelAllowedAdapterReferences>` instead.
2. Every tier diagnostic (SKTIER000–006) is an **error** — no baseline, no downgrade. Keep `eng/verify-tier-errors.sh` (probes that must fail with SKTIER001/006, run by `verify.yml`'s `tier-check` job) and `DependencyGraphRulesTests` in step with `eng/SharedKernelTiers.targets`.
3. A `ReferenceOutputAssembly="false"` reference is exempt from the matrix and may only target a Tooling project (the one such edge: `Presentation.WebApi` → `Presentation.WebApi.Generators`).
4. **Analyzers**: `netstandard2.0`, only `Microsoft.CodeAnalysis.CSharp` 4.14.0 (pinned in `Directory.Packages.props`; the test project matches), no SharedKernel reference — match kernel types by metadata name/namespace.
5. IDs use the `SK` prefix and are **never reused**; a retired rule moves to "Removed Rules" in `AnalyzerReleases.Unshipped.md` and keeps a stub `README.md` section telling users to delete suppressions.
6. RS2008 is satisfied, never suppressed: every new ID goes in `AnalyzerReleases.Unshipped.md`.
7. Every `DiagnosticDescriptor` has a `HelpLinkUri` to its `README.md` heading (`HelpLinkReadmeAnchorTests`).
8. Call `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` where generated code would false-positive (`[LoggerMessage]` output for SK0020/SK0021).
9. Literal-vs-constant rules (SK0022, SK0024, SK0027) discriminate on **syntax shape** (`LiteralExpressionSyntax`), so any named constant passes; receivers resolve through the semantic model.
10. Rules unsafe in every assembly (SK0011, SK0014, SK0022, SK0023, SK0030, SK0703) have no namespace exemption; exemptions walk `SyntaxNode.Parent` for (file-scoped) namespaces.
11. SK0040 matches `SharedKernel.Application.Authorization.RequirePermissionAttribute` (inherited) and `SharedKernel.Application.Idempotency.IIdempotentRequest` only — a same-named attribute elsewhere never matches. SK0036 exempts the `SharedKernel.Presentation.Grpc` prefix. SK0016/SK0017/SK0041 match the `SharedKernel.Application` namespace prefix.
12. **Rule factories take every assembly, anchor and forbidden term from the caller**; no hardcoded allow-lists inside predicates (only exception: `ReflectionExemptionRegistry`, each entry citing its case).
13. NetArchTest `NotHaveDependencyOn(term)` is a `StartsWith` over **namespaces**, not assembly names; a term that prefixes the scanned assembly's own namespace matches itself. Use `AssemblyReferenceAllowListPredicate` for assembly-reference questions.
14. Mono.Cecil: a `static class` is `IsAbstract && IsSealed`; `const string` folds to `ldstr`, `static readonly` is `ldsfld`; static lambdas compile onto `<>c`; inspect async methods through their state machines.
15. In-memory fixture assemblies use names that cannot collide with a loaded real assembly. No static mutable state anywhere in this domain.
16. Linter: the format check runs only when `ContinuousIntegrationBuild=true` or `SharedKernelLinterEnforceFormatting=true`; `InstallSharedKernelLinterConfig` never overwrites an existing `.editorconfig` unless asked.

## Decisions

| Decision | Why |
| --- | --- |
| Tier dependency rules live in MSBuild, not NetArchTest | Fails before compile, in every consumer of the repo, with one matrix instead of per-package rules |
| Analyzers default to Warning | Consumers adopt incrementally and escalate in `.editorconfig` |
| SK0034 is advisory only | An amount + currency pair is sometimes legitimate (wire DTOs) |
| Syntax-shape matching for literal rules | Any named constant passes regardless of where it is declared |
| `RealKernelTypeNameTests` compile analyzer fixtures against real kernel assemblies | A renamed kernel type breaks a test instead of silently disabling the analyzer |
| `RuleExecutionCoverageTests` fails on a public rule with no calling test | A rule never seen failing is not wired up |
| Benchmarks never run under `dotnet test` | BenchmarkDotNet needs a Release, out-of-process run |

## Logging

Analyzers and architecture tests do not log. The `00` EventId block is unused;
`LoggingEventIdIntegrityAssertion` is the helper other domains use to check their own blocks.

## Cross-Domain Couplings

- **Every domain** — rule classes and analyzers encode other domains' purity rules by metadata name; when a domain renames or moves a type named here, `RealKernelTypeNameTests` or a real-assembly pass-path test must be updated in the same change.
- **01.Core** — `LoggingEventIdRanges` (EventId blocks), `WellKnownHeaders`/`WellKnownBaggageKeys` (named by SK0022's message).
- **05.Application** — `PipelineOrderAssertion` locks the built-in pipeline order through `AddSharedKernelApplication(…, app => app.UseMediatR().With…())`.
- **Build (`eng/`)** — `SharedKernelTiers.targets`, `verify-tier-errors.sh`; build topics are documented in `CONTRIBUTING.md` and `eng/README.md`.
- No production package references this domain; analyzer-only references are the only allowed edges into it.

## Testing

- Unit lane: `SharedKernel.Analyzers.Tests`, `SharedKernel.ArchitectureTests.Tests`, `SharedKernel.Linter.Tests` (nested in each package).
- Analyzer tests use `CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>` with `{|SKnnnn:…|}` markup: at least one firing and one non-firing case per rule; a `RealKernelTypeNameTests` case when correctness depends on a real kernel or third-party type.
- Every architecture rule: fire path, pass path, exemption path where one exists, and a pass path against the **real** assembly for platform rules.
- Meta-tests: `RuleExecutionCoverageTests.EveryPublicRuleMethod_IsCalledByAtLeastOneTest`, `RuleAnchorValidationTests`, `RealKernelTypeNameTests`, `HelpLinkReadmeAnchorTests`.
- `_verification/` consumers prove the packed packages restore and work outside the repo.

## Known Limitations

- NetArchTest matches namespaces, so a rule cannot distinguish two assemblies sharing a namespace prefix without `AssemblyReferenceAllowListPredicate`.
- Analyzers match kernel types by name; a type moved without updating the analyzer silently stops firing unless a `RealKernelTypeNameTests` case covers it.
- SK0708 detects batch consumers by name (`BatchConsumer` in the type name), not by interface.
