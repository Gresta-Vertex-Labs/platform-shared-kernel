# SharedKernel.ArchitectureTests

Pre-built architecture rules for the SharedKernel platform, ready to assert from your own test
suite. Each rule is a factory method that returns an evaluable NetArchTest condition — layering
direction, domain purity, provider isolation, forbidden injection patterns, and IL-level checks
that no compiler warning can express.

The rules encode decisions that are otherwise only written down in prose: that `03.Domain` never
reaches into persistence, that a raw `AesGcm` never appears outside the cryptography package,
that two sibling provider packages never reference each other. A prose rule is a rule someone
eventually breaks by accident; a rule here fails the build instead.

This package is **test-only infrastructure**. It is marked as a development dependency, so it
never flows into a consumer's production dependency graph.

## Install

```xml
<PackageReference Include="SharedKernel.ArchitectureTests" PrivateAssets="all" />
```

The version comes from your repository's single `SharedKernelVersion` property (central package management); every
SharedKernel package is released together. **Tier:** Tooling — it is never a runtime dependency of production code.

`PrivateAssets="all"` is redundant — the package already declares itself a development
dependency — but harmless, and explicit is fine.

Add a test runner yourself (xUnit, NUnit, MSTest); this package deliberately depends on none, and
carries no assertion library either.

The package ships its XML documentation, so every rule's purpose, its offending and compliant
patterns, and the meaning of each parameter are available in IntelliSense — you should rarely need
to come back to this file once you are writing code against it.

## How a rule works

Every rule is a **static factory** that returns a `ConditionList` — an unevaluated query. Nothing
is inspected until you call `.GetResult()`:

```csharp
var rule = DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure(domainAssembly);
var result = rule.GetResult();

result.IsSuccessful;      // false if the rule was violated
result.FailingTypeNames;  // the offending types
```

Assert on it however you like. Two convenience paths are provided by `ArchitectureRuleBase`:

```csharp
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.Rules;
using System.Reflection;
using Xunit;

public class ArchitectureTests : ArchitectureRuleBase
{
    private static readonly Assembly Domain = typeof(MyOrder).Assembly;

    [Fact]
    public void Domain_NeverCallsTheSystemClock()
    {
        // Throws ArchitectureRuleViolationException, listing every failing type.
        AssertRule(DomainLayerPurityRules.DomainAssembliesNeverCallSystemClock(Domain));
    }

    [Fact]
    public void Domain_NeverReferencesInfrastructure()
    {
        AssertRule(DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure(Domain));
    }
}
```

A violation fails the test with the offending types named:

```text
ArchitectureRuleViolationException:
  Architecture rule violated. Failing types: MyApp.Domain.Orders.Order,
  MyApp.Domain.Pricing.Discount
```

The same names are on `ArchitectureRuleViolationException.FailingTypeNames` if you want to assert
against them rather than read them.

Or skip the base class entirely and use your own assertion library against `GetResult()`. Nothing
in this package requires you to inherit from anything.

### Rules marked `[]`

Nine rules return `ConditionList[]` rather than a single `ConditionList`, because one NetArchTest
condition cannot express "none of these N dependencies". Pass those to `AssertRules(...)`. They
are marked `[]` in the catalog below.

## Caller-supplied anchor types

Four rules select the types they judge by interface or base class. Those anchors are **passed in
by you**, not hard-bound here, so this package declares no dependency on any other SharedKernel
package:

```csharp
GuardPurityRules.GuardAgainstMethodsMustNotThrow(
    typeof(IGuardClause).Assembly, typeof(IGuardClause));

DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(
    myAssembly, typeof(IDomainService), typeof(DomainService));

ContractsPurityRules.IntegrationEventImplementationsMustBeSealed(
    contractsAssembly, typeof(IIntegrationEvent));

ContractsPurityRules.IntegrationEventsHaveNoNonTrivialMethods(
    contractsAssembly, typeof(IIntegrationEvent));
```

Anchors are validated: passing a class where an interface is required throws `ArgumentException`
rather than silently selecting zero types and reporting a **vacuous pass**. That failure mode is
the one worth guarding against — a rule that inspects nothing looks identical to a rule that
found nothing wrong.

Because the anchor is yours, these rules also work against your own equivalents — your own
guard-clause marker, your own domain-service base.

---

## Which rules apply to your own service

Not every rule here is meant for your code, and the difference is worth knowing before you wire
one up and get a green result that means nothing.

**Portable rules — point them at your own assemblies.** Most rules take every assembly, type and
forbidden term from you, so they judge whatever you hand them: no clock calls in your domain
layer, no `IQueryable` escaping your repositories, no raw cipher outside your crypto package, no
`MakeGenericMethod` dispatch, one declaring assembly per shared constant, secure options defaults.
These are ordinary architecture rules that happen to be pre-written.

**Platform rules — they name SharedKernel packages internally.** Ten rule classes hardcode
`SharedKernel.*` package names in their forbidden-term lists, because their whole job is policing
this platform's own package boundaries:

`SharedKernelLayeringRules`, `RedisTopologyRules`, `CommunicationLayeringRules`, `ApplicationPipelineRules`,
`ContractsPurityRules`, `IntelligenceTopologyRules`, `SearchTopologyRules`,
`StorageTopologyRules`, `PersistenceLayerProtectionRules`, `PresentationLayeringRules`

Pointed at a service assembly that references none of the packages they forbid, these pass
trivially — not because your architecture is sound but because there was nothing to find. They are
useful to you in two cases: you are working inside this mono-repo, or you want to assert that your
service does *not* reach past a SharedKernel abstraction into a concrete provider (which
`StorageTopologyRules.OnlyProviderPackagesMayReferenceAmazonS3` expresses directly for S3, and is a real
thing worth pinning).

A rule that passes vacuously is the failure mode to watch for generally. When you adopt any rule,
make it fail once on purpose — add the violation, watch it go red, then remove it. A rule you have
never seen fail is a rule you do not yet know is wired up.

---

## Rule catalog

### Tiers and purity — `SharedKernelLayeringRules`

Which kernel package may reference which is enforced by the build, not by this package: every
kernel `.csproj` declares a `<SharedKernelTier>` (Foundation, Model, Abstractions, Adapter, Host,
Testing, Tooling) and `eng/SharedKernelTiers.targets` fails the build with an `SKTIER*` error on an
edge the tier matrix does not allow, including ASP.NET Core below the Host tier (`SKTIER006`). What
is left here are the rules the tier matrix cannot express:

| Rule | Enforces |
|---|---|
| `ContractsNeverReferencesDomain` | `SharedKernel.Contracts` never references `SharedKernel.Domain` (both Model tier) — a wire contract is not the domain model |
| `DomainNeverReferencesContracts` | `SharedKernel.Domain` never references `SharedKernel.Contracts` (both Model tier) |
| `ModelNeverReferencesLogging` | Domain and contracts assemblies stay logging-free (`Microsoft.Extensions.Logging.Abstractions` passes the tier allow-list, so the tier check cannot catch it) |
| `TestingNeverReferencedByProduction` | Hard rule — test helpers (`SharedKernel.Testing*`, `SharedKernel.*.Testing`, `SharedKernel.Persistence.Testing`) never appear as a production dependency |

### Domain purity

| Rule | Enforces |
|---|---|
| `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure` | No infrastructure dependency in a domain assembly |
| `DomainLayerPurityRules.DomainAssembliesNeverContainEventHandlers` | Handlers live in the application layer, not the domain |
| `DomainLayerPurityRules.DomainAssembliesNeverCallSystemClock` | No `DateTime.UtcNow`/`.Now` or `DateTimeOffset` equivalent — inject a clock |
| `DomainLayerPurityRules.DomainServicesHaveNoInfrastructureConstructorParameters` | No infrastructure type reaches a domain service's constructor |
| `DomainGoldStandardRules.DomainServicesMustExtendAbstractBase` | Every domain-service implementor extends the shared base rather than the bare interface |
| `GuardPurityRules.GuardAgainstMethodsMustNotThrow` | Functional-path guard clauses contain no `throw` IL — they return an error value |
| `CoreArchitectureRules.DiExtensionsUseTryAddRegistrationConvention` | Core DI extensions use `TryAdd*`, never plain `Add*`, so a consumer's own registration wins |

### Contracts

| Rule | Enforces |
|---|---|
| `ContractsPurityRules.IntegrationEventsHaveNoNonTrivialMethods` | Integration events are data only — no behaviour (other contract types may carry factories and projections) |
| `ContractsPurityRules.ContractsAssembliesHaveNoDomainTypeOnPublicSurface` | No domain type leaks onto a wire contract |
| `ContractsPurityRules.ContractsAssembliesHaveNoResultTypeOnPublicSurface` | No public property or field exposes `Result`/`Result<T>`/`ValidationResult`; a static factory may still return one |
| `ContractsPurityRules.IntegrationEventImplementationsMustBeSealed` | Every integration event is sealed |

Every Contracts rule is tested against the real `SharedKernel.Contracts` assembly as well as violation
fixtures. There is no envelope-construction rule: `EventEnvelope<TEvent>` has no public constructor, so
building one outside `EventEnvelope.Wrap` no longer compiles.

### Application pipeline

| Rule | Enforces |
|---|---|
| `ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure` | Named pipeline behaviors depend on abstractions only |
| `ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint` | No behavior's generic constraint accidentally captures stream requests |
| `ApplicationPipelineRules.PipelineNeverReferencesCachingPollyHostingOrCore` | `SharedKernel.Application.Pipeline` carries no cache, Polly, hosting or `SharedKernel.Core` dependency |
| `ApplicationPipelineRules.PipelineCachingNeverReferencesConcreteInfrastructure` | The caching behaviors reach `SharedKernel.Caching.Abstractions`, never a cache provider |
| `UnitOfWorkSeamRules.SharedContractsAreNotRedeclared` | `IUnitOfWork`, `IRequestContext` and `IAuditTrailWriter` are declared only in `SharedKernel.Execution` — no second copy (nor the deleted `ITransactionalUnitOfWork`/`IPersistenceTransaction`/`ICurrentActorContext`/`ICurrentTenantContext`) anywhere else |
| `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag` | Every duration histogram carries an `outcome` tag, so failures stay separable |

### Persistence

| Rule | Enforces |
|---|---|
| `PersistenceLayerProtectionRules.DomainAssembliesNeverReferencePersistenceStack` | No EF Core, Npgsql, or `SharedKernel.Persistence.*` in a domain assembly |
| `PersistenceLayerProtectionRules.OnlyEfUnitOfWorkMayCallSaveChanges` | `SaveChanges`/`SaveChangesAsync` is called in exactly one place |
| `PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable` | No `IQueryable` escapes a repository |
| `PersistenceInterfaceOwnershipRules.IReadRepositoryMustNotExposeIQueryable` | Same, for the read side |
| `PersistenceInterfaceOwnershipRules.ReadOnlyRepositoriesNeverTrack` | A read-repository implementation never returns tracked entities (IL scan, async state machines included) |
| `PersistenceInterfaceOwnershipRules.IUserContextDeclaredOnlyInSecurityAbstractions` | `IUserContext` has exactly one declaring assembly |
| `PersistenceInterfaceOwnershipRules.TenantIdentityInterfacesAreNeverRedeclared` | No second tenant-identity interface (`ITenantProvider`, `ICurrentTenantService`, `ITenantContextAccessor`) comes back next to `IRequestContext.TenantId` |
| `RepositoryContractCompletenessRules.AllReadRepositoryImplementorsMustHaveGetByIdAsync` | Every read repository implements the full contract |
| `RepositoryContractCompletenessRules.AllReadRepositoryImplementorsMustHaveGetByIdsAsync` | Every read repository implements the full contract |
| `EfCorePackageHygieneRules.NoDirectEfPropertyUsageInEfCoreAssembly` | No `EF.Property<T>` — use a typed expression |
| `EfCorePackageHygieneRules.NoSpecificationEvaluatorDowncastInEfCoreAssembly` | No `castclass` onto the specification evaluator |
| `EfCorePackageHygieneRules.IUnitOfWorkImplementorsMustHaveExactlyOneConstructor` | One constructor, so DI resolution stays unambiguous |
| `EfCorePackageHygieneRules.ApplicationLayerMustNotReferenceDbContextTransaction` | Transactions go through the abstraction, never `IDbContextTransaction` |
| `PersistenceNamespaceConventionRules.FindMisplacedExtensions` | Registration/builder extensions live in `SharedKernel.Persistence`, EF Core model/migration/query helpers in `SharedKernel.Persistence.EfCore` (receiver-type based) |

### Caching and Redis topology

| Rule | Enforces |
|---|---|
| `RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages` | The shared Redis core never depends on its own role packages |
| `RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther` `[]` | Sibling role packages stay independent |
| `RedisTopologyRules.PubSubNeverReferencesMessaging` | Ephemeral pub/sub never reaches durable messaging |
| `RedisTopologyRules.MessagingNeverReferencesCaching` | And the reverse direction is barred too |
| `RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies` | The caching abstraction stays dependency-free |

### Messaging

| Rule | Enforces |
|---|---|
| `MessagingArchitectureRules.NoDirectBusInjectionOutsideMessaging` | No raw transport interface is injected outside the messaging package |
| `MessagingArchitectureRules.NoEventPublisherInDomainLayer` | The domain never injects a publisher |
| `ExtendedMessagingArchitectureRules.NoDirectMassTransitSchedulerInjection` | No raw message scheduler outside the owning package |

### Provider topology

Applies the same shape across the multi-provider domains: the abstraction stays clean, and
siblings never see each other. Storage is the one exception: `SharedKernel.Storage.Obs` is built on
`SharedKernel.Storage.S3` by design, so its rule forbids only the reverse direction.

| Rule | Enforces |
|---|---|
| `StorageTopologyRules.AbstractionsHasNoThirdPartyDependencies` | No cloud SDK, provider package or `SharedKernel.Configuration` leaks into the storage abstraction (namespace half) |
| `StorageTopologyRules.AbstractionsForbiddenAssemblyReferences` | The same, by referenced assembly name — returns the offending names, empty when clean |
| `StorageTopologyRules.S3NeverReferencesObs` | The S3 provider never names OBS (namespace half) |
| `StorageTopologyRules.S3ForbiddenAssemblyReferences` | The same, by referenced assembly name — returns the offending names, empty when clean |
| `StorageTopologyRules.OnlyProviderPackagesMayReferenceAmazonS3` | The S3 SDK stays inside the S3 and OBS providers |
| `SearchTopologyRules.AbstractionsHasNoThirdPartyDependencies` | No engine SDK leaks into the search abstraction |
| `SearchTopologyRules.ProviderPackagesNeverReferenceEachOther` `[]` | Search providers stay independent |
| `IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies` | No model or vector-DB SDK leaks into the AI abstraction |
| `IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther` `[]` | AI providers stay independent |
| `IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages` | Probes are primitives; health-check wiring belongs to the host |
| `WorkflowTopologyRules.NoHealthChecksDependencyInWorkflows` | Same, for workflows |
| `WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo` | The raw-client escape hatch is never consumed in-repo |

### Communication and presentation

| Rule | Enforces |
|---|---|
| `CommunicationLayeringRules.GrpcNeverReferencesContracts` | Protobuf is the wire contract for gRPC, not a DTO package |
| `CommunicationLayeringRules.NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc` | Interceptors are built through the platform base |
| `CommunicationLayeringRules.NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL` | Filters/sorts extend the platform base, not HotChocolate directly |
| `PresentationLayeringRules.GrpcNeverReferencesContracts` | Same contract rule, server side |
| `PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi` | `ProblemDetails` is shaped in one place |
| `PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi` | No hand-rolled `IsSuccess` branch at an HTTP boundary |

### Security

| Rule | Enforces |
|---|---|
| `SecurityArchitectureRules.DomainNeverReferencesRequestContext` | `IRequestContext` never reaches the domain — it receives the tenant as a value |
| `SecurityArchitectureRules.NoSingletonRegistrationOfSecurityContextTypes` | Per-request identity is never captured in a singleton |
| `SecurityArchitectureRules.DpopProofValidationNeverDuplicatedOutsideOidc` | Proof-of-possession validation has one implementation |
| `SecurityArchitectureRules.ClientCertificateAccessNeverDuplicatedOutsideMtls` | Client-certificate access has one implementation |

### Cryptography

| Rule | Enforces |
|---|---|
| `CryptoIsolationRules.CryptographyCoreHasNoThirdPartyDependencies` | The crypto core stays dependency-free |
| `CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography` | No raw `Aes`/`AesGcm` outside the owning package |
| `EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication` | No cipher primitive in domain or application code |
| `EncryptionPatternGuardRules.NoEncryptionAttributeOnDomainEntities` | Encryption is configured in mapping, never on a domain entity |
| `EncryptionPatternGuardRules.NoEncryptionRotationJobInjectionInDomainOrApplication` | Key rotation is not a domain concern |

### Host composition and health checks

| Rule | Enforces |
|---|---|
| `HealthCheckConstantsUsageRules.NoBareHealthCheckLiteralWhereConstantsExist` | Health-check names come from constants, never retyped literals |
| `HealthCheckTagIntegrityRules.NoConflictingLivenessReadinessTags` | No check is tagged both `live` and `ready` |
| `HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive` | A dependency check gates readiness, never liveness — so a slow dependency does not trigger a restart |

### Reflection

| Rule | Enforces |
|---|---|
| `ReflectionGuardRules.NoMakeGenericMethodReflection` | No `MakeGenericMethod` dispatch — use typed dispatch, so trimming and AOT stay viable |

Exemptions for genuinely justified cases are declared through `ReflectionExemptionRegistry`;
`IsExempt` reports whether a given type-and-method pair is registered.

---

## Assertion helpers

Some invariants are not "which types may reference what" but "what a specific method body does".
These helpers inspect IL directly and throw on violation.

| Helper | Asserts |
|---|---|
| `LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange` | Every `[LoggerMessage]` `EventId` is unique platform-wide and inside its domain's reserved range |
| `PipelineOrderAssertion.AssertRegistrationOrder` | Registrations occur in the required relative order — where order is the correctness property, not a preference |
| `WellKnownConstantOwnershipAssertion.AssertSoleDeclaration` | A shared constant is declared in exactly one place, so two packages cannot drift apart on a wire value |
| `SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals` | An options enum's default is the secure value |
| `SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals` | A default collection matches exactly |
| `SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes` | A default collection omits a forbidden entry |
| `SecureDefaultsAssertion.AssertMethodBodyInvokesMethod` | A method genuinely calls the validation/registration it claims to |
| `SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton` | A registration is actually present |
| `SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType` | A guard genuinely throws rather than logging and continuing |

These exist because a secure default is only secure while nobody edits it. Asserting on the
compiled IL means the check cannot pass by reading a comment that still says the right thing.

## Writing your own rule

The custom predicates in the `Predicates` namespace are public `ICustomRule` implementations, so
you can compose them into your own conditions:

```csharp
Types.InAssembly(myAssembly)
     .That().ImplementInterface(typeof(IMyMarker))
     .Should().MeetCustomRule(new SingleConstructorPredicate());
```

Or implement `ICustomRule` yourself — `MeetsRule(TypeDefinition)` receives the Mono.Cecil type, so
method bodies, custom attributes, and IL opcodes are all reachable.

`StringConstantsClassDetector` is a reusable helper rather than a rule: `IsStringConstantsClass`
recognizes the `static class` of `const string` / `static readonly string` IL shape,
`ResolveStringConstants` collects every such literal in a module, and `ResolveStringFieldsOnType`
does the same for one type. Use it when your rule needs to know which string values are already
declared as named constants — that is how the "no bare literal where a constant exists" check
distinguishes a magic string from a legitimate constant reference.

One thing worth knowing if you write a namespace-scoped predicate: Mono.Cecil reports an **empty
`Namespace` for every nested type**. A namespace filter that reads `TypeDefinition.Namespace`
directly will silently skip nested types and can make a rule pass vacuously. Walk
`DeclaringType` to the outermost type first.

## Requirements

- .NET 10 (`net10.0`)
- A test runner of your choice

Dependencies: `NetArchTest.Rules`, `Mono.Cecil`, and
`Microsoft.Extensions.DependencyInjection.Abstractions`. No assertion library, no test runner, and
no other SharedKernel package.

## License

MIT
