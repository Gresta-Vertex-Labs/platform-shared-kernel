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

SK0008  AggregateRootDispatchCoupling
    Category  : Design
    Severity  : Warning
    Trigger   : A constructor parameter is typed as IAggregateRoot<> (simple name contains
                "IAggregateRoot") inside a class whose name or any enclosing namespace
                identifier contains any of: "Interceptor", "Publisher", "Outbox",
                "Dispatcher" (case-sensitive substring match)
    Fix       : Replace IAggregateRoot<TId> with IHasDomainEvents — dispatch code only
                needs to raise domain events, not the full aggregate identity surface
    Note      : No suppression namespace defined. Simple name check; no semantic model
                required. SK0008 is assigned in WO-011 P-056.

SK0009  DomainEventMissingVersionAttribute
    Category  : Design
    Severity  : Warning
    Trigger   : A non-abstract class or record that declares IDomainEvent in its base list
                does not carry a [DomainEventVersion] attribute — schema versioning
                discipline is required for all concrete domain event types
    Fix       : Add [DomainEventVersion(N)] where N is the current schema version;
                increment N on any breaking property change (add, remove, rename)
    Exempt    : Abstract types (those with the abstract modifier) are excluded — abstract
                base event classes used as shared bases do not need a version attribute
    Note      : Base list check is simple name match ("IDomainEvent"). Attribute check is
                simple name match ("DomainEventVersion" or "DomainEventVersionAttribute").
                No semantic model required.

SK0010  SpecificationOrderingConflict
    Category  : Design
    Severity  : Warning
    Trigger   : A constructor body contains invocations of both ApplyOrderBy(...) and
                ApplyOrderByDescending(...) — the conflicting ordering directives produce
                non-deterministic sort results at query execution time
    Fix       : Use only one ordering direction per specification constructor; apply
                secondary sorting via ThenBy / ThenByDescending overloads if needed
    Note      : Simple name check on invocation method names — "ApplyOrderBy" and
                "ApplyOrderByDescending". Scoped to ConstructorDeclarationSyntax bodies.
                No type-scoping to Specification<T> subclasses required; method names
                are unique within the SDK.

SK0011  GuidFormatCodeMisuse
    Category  : Design
    Severity  : Warning
    Trigger   : Guid.ToString(string) called with a format argument whose value is
                "N", "B", "P", or "X" (case-insensitive) — these produce GUID string
                representations that diverge from the canonical hyphenated lowercase format
                required for consistent audit column values (CreatedBy, ModifiedBy).
                Requires SemanticModel.GetTypeInfo on the receiver to confirm the receiver
                is System.Guid — prevents false positives on non-Guid ToString("N") calls.
    Fix       : Use ToString() (no argument) or ToString("D") — both produce the canonical
                lowercase hyphenated format "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx".
    Suppress  : Per-call-site via #pragma warning disable SK0011 when a compact format is
                genuinely required (e.g., URL segment). Document the suppression in code.
    Note      : Introduced in WO-016 P-096. Unlike SK0001–SK0010, SK0011 requires a minimal
                semantic model check (GetTypeInfo) to resolve the receiver type.

SK0301  DirectCryptoInDomainOrApplication
    Category  : Security
    Severity  : Warning
    Trigger   : A type in a `03.Domain` or `05.Application` assembly references
                `System.Security.Cryptography.AesGcm`, `System.Security.Cryptography.Aes`,
                or `System.Security.Cryptography.SymmetricAlgorithm` — detected via IL
                instruction walk (Call/Callvirt/Newobj opcodes) and field type inspection
    Exempt    : Types whose namespace starts with `SharedKernel.Persistence.*` or
                `SharedKernel.Security.*` — these are the only legitimate crypto consumers
                in the platform (persistence-layer converter and JWT signing respectively)
    Fix       : Remove direct cipher usage from domain/application code. Route all
                field-level encryption through the persistence-layer `EncryptedValueConverter<T>`
                wired via `PropertyBuilder<T>.Encrypt()` in `IEntityTypeConfiguration<T>`.
    Note      : Implemented as a NetArchTest `ICustomRule`
                (`NoAesCipherInDomainOrApplicationPredicate`) — not a per-call-site
                Roslyn analyzer. Enforced at assembly level (post-compile). Introduced
                in WO-019 P-114. Part of the new 03xx encryption-domain ID block.

SK0302  EncryptionAttributeOnDomainEntity
    Category  : Design
    Severity  : Warning
    Trigger   : A class in a `03.Domain` assembly carries a custom attribute whose
                `Name` contains `"Encrypt"` as a substring (case-insensitive) — detected
                via `TypeDefinition.CustomAttributes` enumeration (Mono.Cecil attribute
                reflection, no IL instruction walk required)
    Exempt    : Non-domain types (types outside the domain assembly under test). The
                predicate is always scoped to the domain assembly passed by the caller.
    Fix       : Remove the `[Encrypted*]` / `[*Encrypt*]` attribute from the domain entity.
                Place encryption configuration in `IEntityTypeConfiguration<T>.Configure()`
                using `PropertyBuilder<T>.Encrypt()` — `EncryptionModelConvention` applies
                `EncryptedValueConverter<T>` automatically at model finalization. Never
                annotate domain entity classes with infrastructure-specific attributes.
    Note      : Implemented as a NetArchTest `ICustomRule`
                (`NoEncryptionAttributeOnDomainEntityPredicate`). Introduced in WO-019 P-114.

SK0303  EncryptionRotationJobInDomainOrApplication
    Category  : Design
    Severity  : Warning
    Trigger   : `IEncryptionRotationJob` appears as a constructor parameter type (by simple
                name exact match) in a type whose namespace maps to `03.Domain` or
                `05.Application` — detected via `TypeDefinition.Methods` where
                `IsConstructor` is true, checking `ParameterDefinition.ParameterType.Name`
    Exempt    : Types whose `TypeDefinition.Namespace` starts with `SharedKernel.Persistence.*`
                (the interface's own package). Types whose `TypeDefinition.Name` contains
                any of: `"RotationJob"`, `"HostedService"`, `"Controller"`, `"Activity"`
                as a substring (class-name based exemption for designated infrastructure
                consumers of the rotation job).
    Fix       : Move `IEncryptionRotationJob` injection to a hosted service
                (`IHostedService` implementation), a Hangfire/Temporal job class, or a
                management API controller. Never inject it in a MediatR handler, domain
                service, or any type in `03.Domain` / `05.Application`.
    Note      : Implemented as a NetArchTest `ICustomRule`
                (`NoEncryptionRotationJobInjectionPredicate`). Introduced in WO-019 P-114.

SK0304  DirectEncryptedValueConverterInstantiation
    Category  : Design
    Severity  : Warning
    Trigger   : A `newobj` IL opcode whose operand `MethodReference.DeclaringType.Name`
                contains `"EncryptedValueConverter"` (substring) is found inside a method
                body of a type whose `TypeDefinition.Interfaces` contains an entry with
                `InterfaceType.Name.StartsWith("IEntityTypeConfiguration")` — detected via
                IL instruction walk in `NoDirectEncryptedValueConverterInstantiationPredicate`
    Exempt    : Types whose `TypeDefinition.Name == "EncryptionModelConvention"` (exact
                match) — the convention itself legitimately instantiates the converter as
                the auto-wire mechanism and must never be flagged.
    Fix       : Replace `new EncryptedValueConverter<string>(...)` + `.HasConversion(converter)`
                with `.Encrypt()` on `PropertyBuilder<T>`. The `EncryptionModelConvention`
                (registered via `EfCorePersistenceBuilder.WithEncryption()`) detects the
                marker and applies the converter automatically at model finalization.
                Direct instantiation bypasses the convention, producing duplicate or
                inconsistent converter registration (double-encryption of stored data).
    Note      : Implemented as a NetArchTest `ICustomRule`
                (`NoDirectEncryptedValueConverterInstantiationPredicate`). Introduced
                in WO-019 P-114. Scope: `IEntityTypeConfiguration<T>` implementors only —
                general application code that is not an EF Core configuration class is
                not subject to this rule.

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

DomainGoldStandardRules  (static class — domain convention enforcement predicates)
    .DomainServicesMustExtendAbstractBase(Assembly)  → ConditionList
        Asserts that every non-abstract type implementing IDomainService also inherits from
        DomainService abstract class. Uses NetArchTest fluent API:
        Types.InAssembly(assembly).That().ImplementInterface(typeof(IDomainService))
            .And().AreNotAbstract().Should().Inherit(typeof(DomainService))
        The DomainService abstract class itself is excluded via .AreNotAbstract() — it
        passes naturally. Failure message lists all non-conforming type names from
        .GetResult().FailingTypeNames. No ICustomRule required.

    Rationale (Rule 1): DomainService provides CheckRule(IBusinessRule) access and acts
        as the DI anchor for all domain services. Direct IDomainService implementation
        bypasses these shared capabilities, forcing copy-paste of CheckRule logic.
    Offending pattern: class PricingService : IDomainService { ... }
    Compliant pattern: class PricingService : DomainService { ... }
    Note: consuming test project must reference SharedKernel.Domain to supply the assembly.

    SK0008 companion rule — documented in Diagnostic Rule Registry above:
        Infrastructure dispatch code should inject IHasDomainEvents, not IAggregateRoot<TId>.
        The architecture enforces the IDomainService→DomainService chain; the Roslyn analyzer
        enforces the narrower dispatch coupling separately.

    SK0009 companion rule — documented in Diagnostic Rule Registry above:
        Every non-abstract IDomainEvent implementor must carry [DomainEventVersion].

    SK0010 companion rule — documented in Diagnostic Rule Registry above:
        Specification constructors must not call both ApplyOrderBy and ApplyOrderByDescending.

ContractsPurityRules  (static class — contracts layer purity predicates)
    All factory methods accept Assembly contractsAssembly and return ConditionList.

    .ContractsAssembliesHaveNoNonTrivialMethods(Assembly)  → ConditionList
        Asserts no type in the contracts assembly contains a non-trivial method — defined as
        any method that is not a constructor, property getter/setter, static operator
        (IsSpecialName and name starts with "op_"), or one of ToString/Equals/GetHashCode.
        Uses NoNonTrivialMethodsPredicate (ICustomRule — see below). Failure message
        includes the offending type name and first non-trivial method name.

        Rationale: DTOs and event payloads carry state, not behaviour. Any non-trivial method
        in 04.Contracts signals domain logic leakage into the contracts layer.
        Offending pattern: public class OrderDto { public bool IsExpired() => Deadline < DateTime.UtcNow; }
        Compliant pattern: public record OrderDto(Guid Id, DateTimeOffset Deadline);

    .ContractsAssembliesHaveNoDomainTypeOnPublicSurface(Assembly)  → ConditionList
        Asserts no public type in the contracts assembly has a dependency on
        "SharedKernel.Domain" (the domain assembly). Uses
        .Should().NotHaveDependencyOn("SharedKernel.Domain") scoped to public types.
        Exemption: EventEnvelope<TEvent> where TEvent : IDomainEvent — the generic
        constraint references IDomainEvent; if NetArchTest picks this up as a dependency,
        EventEnvelope must be explicitly excluded from the scan or the constraint must be
        defined against a marker interface in SharedKernel.Primitives instead of SharedKernel.Domain.
        Document the resolution in this CLAUDE.md if the exemption is applied.

        Rationale: Integration events and DTOs must be independent projections. Exposing
        Entity<TId>, AggregateRoot<TId>, ValueObject, or Specification<T> on a contracts
        public surface ties the wire format to the domain model, breaking polyglot consumers.
        Offending pattern: public class OrderSummaryDto { public Order DomainOrder { get; set; } }
        Compliant pattern: public record OrderSummaryDto(Guid OrderId, string Status);

    .ContractsAssembliesHaveNoResultTypeOnPublicSurface(Assembly)  → ConditionList
        Asserts no public type in the contracts assembly has a dependency on
        "SharedKernel.Primitives" (where Result<T> and Result live). Uses
        .Should().NotHaveDependencyOn("SharedKernel.Primitives") scoped to public types.
        Failure message must include the specific offending type name from
        .GetResult().FailingTypeNames.

        Rationale: Result<T> is an intra-service discriminated union. Envelope<T> is the
        cross-service HTTP wrapper. Exposing Result<T> in a serialized response payload causes
        deserialization failures in any JSON client that does not share the SharedKernel.Primitives
        assembly, breaking the polyglot contract model.
        Offending pattern: public class CreateOrderResponse { public Result<Guid> OrderId { get; set; } }
        Compliant pattern: public record CreateOrderResponse(Guid OrderId);

    .IntegrationEventImplementationsMustBeSealed(Assembly)  → ConditionList
        Asserts every non-abstract type implementing IIntegrationEvent is sealed.
        Uses: Types.InAssembly(assembly).That().ImplementInterface(typeof(IIntegrationEvent))
            .And().AreNotAbstract().Should().BeSealed()
        If .BeSealed() is not available in NetArchTest.eNt 1.3.2, fall back to a custom
        ICustomRule that inspects TypeDefinition.IsSealed (records are IsSealed in IL).
        Failure message names the offending type.

        Rationale: Non-sealed integration events are an inheritance trap. A sub-event changes
        the wire format without incrementing [DomainEventVersion], causing silent schema drift.
        sealed or record ensures the wire contract is closed.
        Offending pattern: public class OrderCreatedEvent : IIntegrationEvent { ... }
        Compliant pattern: public sealed record OrderCreatedEvent : IIntegrationEvent { ... }

    Rule 5 (documentation-only — not a NetArchTest rule):
        Microservices must not reference SharedKernel.Domain directly unless they implement
        domain logic. Cross-service DTO types are in SharedKernel.Contracts; domain types
        (Entity, ValueObject, AggregateRoot) are internal to services that own the domain.
        Rationale: referencing SharedKernel.Domain from a microservice that is not a DDD-domain
        service creates an invisible coupling to the domain model that breaks when the domain
        model evolves. The contracts package is the stable public surface.

NoNonTrivialMethodsPredicate  (class : ICustomRule — internal predicate)
    Inspects TypeDefinition.Methods for each type. A method is non-trivial if all of the
    following are false: IsConstructor, IsGetter, IsSetter, (IsSpecialName and Name starts
    with "op_"), Name is "ToString" or "Equals" or "GetHashCode".
    Returns false (rule violated) for the first non-trivial method found; failure message
    includes the declaring type name and the method name. Lives in Predicates/ folder.
    Used by ContractsPurityRules.ContractsAssembliesHaveNoNonTrivialMethods.

PersistenceLayerProtectionRules  (static class — EF Core persistence layer contract predicates)
    All factory methods accept Assembly as their parameter and return ConditionList.
    .OnlyEfUnitOfWorkMayCallSaveChanges(Assembly)        → ConditionList
        Asserts that no type outside the SharedKernel.Persistence.EfCore namespace calls
        DbContext.SaveChanges or DbContext.SaveChangesAsync directly. Uses
        NoDirectSaveChangesPredicate (ICustomRule — see below). Types whose
        TypeDefinition.Namespace starts with "SharedKernel.Persistence.EfCore" are
        exempted unconditionally inside the predicate. Failure message names the offending
        type and method containing the direct SaveChanges call.
        Rationale: calling SaveChangesAsync directly bypasses the EF Core interceptor chain
        (AuditInterceptor, SoftDeleteInterceptor, OutboxInterceptor, ConcurrencyInterceptor).
        Only EfUnitOfWork may commit — all other code must call IUnitOfWork.CommitAsync().
        Offending pattern: await _dbContext.SaveChangesAsync();
        Compliant pattern: await _unitOfWork.CommitAsync();

    .RepositoriesMustNotExposeIQueryable(Assembly)        → ConditionList
        Asserts that no type implementing an IRepository-prefixed interface has a method
        returning IQueryable. Uses NoIQueryableReturnPredicate (ICustomRule — see below).
        Scope: types whose TypeDefinition.Interfaces contains an entry with
        InterfaceType.Name starting with "IRepository". Inspects all non-constructor,
        non-getter methods for IQueryable return type (name match on "IQueryable"). Failure
        message names the offending type and method returning IQueryable.
        Rationale: IQueryable<T> leaks EF Core expression-tree execution semantics into the
        application layer, making handler code dependent on EF Core internals. Query surface
        belongs exclusively on IReadRepository via Specification<T>; the write-side
        IRepository<T,TId> is scoped to mutation operations only.
        Offending pattern: IQueryable<Order> GetAll();
        Compliant pattern: Task<IReadOnlyList<Order>> FindAsync(ISpecification<Order> spec);

    .DomainAssembliesNeverReferencePersistenceStack(Assembly) → ConditionList
        Asserts that no type in the supplied domain assembly has a dependency on any of the
        persistence-stack assembly name substrings: "Microsoft.EntityFrameworkCore",
        "Npgsql", "SharedKernel.Persistence". Uses iterative .Should().NotHaveDependencyOn()
        calls — one per forbidden term — consistent with the pattern in
        DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure. This rule is
        additive (not replacing) that rule: it adds Npgsql and the in-repo persistence
        packages as a second, WO-013-scoped gate.
        Rationale: any EF Core, Npgsql, or SharedKernel.Persistence.* reference inside
        03.Domain destroys DDD isolation and makes domain logic impossible to unit-test
        without a database. This is a hard rule from the root CLAUDE.md layering table.
        Offending pattern: [Key] attribute from Microsoft.EntityFrameworkCore on a domain entity
        Compliant pattern: domain entity with no infrastructure annotations

PersistenceInterfaceOwnershipRules  (static class — interface declaration ownership predicates)
    All factory methods return ConditionList. Introduced in WO-014 P-083.
    .IUserContextDeclaredOnlyInSecurityAbstractions(params Assembly[] assemblies)
                                            → ConditionList
        Asserts that no type named "IUserContext" is declared in any of the supplied
        assemblies. The correct declaration home is SharedKernel.Security.Abstractions,
        which must NOT be passed to this method. Uses InterfaceDeclarationOwnershipPredicate
        configured with {"IUserContext"}. Failure message names the offending type and
        the assembly it was found in.
        Rationale: IUserContext was migrated to SharedKernel.Security.Abstractions in P-078.
        Local redeclarations in persistence or application layers duplicate the contract and
        break the single-source-of-truth principle for security identity abstractions.
        Offending pattern: interface IUserContext { Guid UserId { get; } } inside
            SharedKernel.Persistence.EfCore
        Compliant pattern: inject SharedKernel.Security.Abstractions.IUserContext

    .TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions(params Assembly[] assemblies)
                                            → ConditionList
        Asserts that no type named "ITenantProvider" or "ICurrentTenantService" is declared
        in any of the supplied assemblies. Uses InterfaceDeclarationOwnershipPredicate
        configured with {"ITenantProvider","ICurrentTenantService"}. Failure message names
        the offending type name and assembly.
        Rationale: Same as IUserContext — tenant identity contracts belong exclusively in
        SharedKernel.Security.Abstractions; duplicates in other layers cause silent mismatches.
        Offending pattern: interface ICurrentTenantService inside SharedKernel.Persistence.EfCore
        Compliant pattern: reference SharedKernel.Security.Abstractions.ICurrentTenantService

    .IReadRepositoryMustNotExposeIQueryable(Assembly assembly)  → ConditionList
        Asserts that no type implementing an IReadRepository-prefixed interface has a method
        or property returning IQueryable. Uses NoIQueryableReturnPredicate (already defined)
        scoped to "IReadRepository" interface prefix specifically (not the broader "IRepository"
        used by PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable). Both
        rules may run in the same suite without conflict; this one carries an IReadRepository-
        specific failure message for clearer diagnostics.
        Rationale: IQueryable on a read-side repository leaks EF Core execution semantics into
        application handlers reading via the read-side abstraction. Query surface belongs on
        Specification<T>, not on raw IQueryable return types.

    .NoGetByIdAsyncOnReadRepository(Assembly assembly)  → ConditionList
        Asserts that no class implementing an IReadRepository-prefixed interface declares a
        method named "GetByIdAsync". Uses NoGetByIdOnReadRepositoryPredicate (ICustomRule).
        Failure message: "{type}.GetByIdAsync must be removed — use FindByIdAsync (returns
        Result<T>) or GetAsync (returns T?) instead. GetByIdAsync was removed in P-080 to
        eliminate duplication with IRepository."
        Rationale: P-080 removed GetByIdAsync from IReadRepository to eliminate duplication
        with the write-side IRepository. Redeclaring it on a concrete implementor reintroduces
        the anti-pattern and diverges from the platform read/write split contract.
        Offending pattern: Task<Order?> GetByIdAsync(Guid id) on an IReadRepository implementor
        Compliant pattern: use FindByIdAsync(id) → Result<Order> or GetAsync(id) → Order?

InterfaceDeclarationOwnershipPredicate  (class : ICustomRule — internal predicate)
    Constructed with a set of interface type names to detect (e.g., {"IUserContext"} or
    {"ITenantProvider","ICurrentTenantService"}). For each type inspected, checks if
    TypeDefinition.Name is in the configured name set. Returns false (rule violated) with
    failure message including the offending type name and TypeDefinition.Module.Assembly.Name.Name
    for assembly identification. Stateless per evaluation — no cached state.
    Lives in Predicates/ folder. Used by PersistenceInterfaceOwnershipRules.

NoGetByIdOnReadRepositoryPredicate  (class : ICustomRule — internal predicate)
    Scope check: TypeDefinition.Interfaces contains an entry with InterfaceType.Name starting
    with "IReadRepository". For each such type, iterates TypeDefinition.Methods for any entry
    whose Name equals "GetByIdAsync" (exact match). Returns false (rule violated) with failure
    message naming the offending type and referencing FindByIdAsync/GetAsync as the correct
    alternatives. Lives in Predicates/ folder. Used by
    PersistenceInterfaceOwnershipRules.NoGetByIdAsyncOnReadRepository.

RepositoryContractCompletenessRules  (static class — repository interface contract completeness predicates)
    All factory methods accept Assembly and return ConditionList. Introduced in WO-016 P-096.
    .AllRepositoryImplementorsMustHaveExistsAsync(Assembly assembly)  → ConditionList
        Asserts that every non-abstract type implementing an IRepository-prefixed interface
        (not IReadRepository) declares a method named "ExistsAsync". Uses
        HasRequiredMethodPredicate("IRepository", "ExistsAsync"). Failure message:
        "{type} implements IRepository<,> but does not declare ExistsAsync. Add ExistsAsync
        per the interface contract defined in P-093."
        Rationale: ExistsAsync was added to IRepository<,> in P-093. Any concrete repository
        that does not implement it will compile (if a stub is provided by the base class) but
        fail at runtime. This rule surfaces the gap at build time with a readable message.
        Offending pattern: class OrderRepository : IRepository<Order, Guid> with no ExistsAsync
        Compliant pattern: class OrderRepository : IRepository<Order, Guid> {
            public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) { ... } }

    .AllReadRepositoryImplementorsMustHaveGetByIdsAsync(Assembly assembly)  → ConditionList
        Asserts that every non-abstract type implementing an IReadRepository-prefixed interface
        declares a method named "GetByIdsAsync". Uses
        HasRequiredMethodPredicate("IReadRepository", "GetByIdsAsync"). Failure message:
        "{type} implements IReadRepository<,> but does not declare GetByIdsAsync. Add
        GetByIdsAsync(IEnumerable<TId> ids) per the interface contract defined in P-093."
        Rationale: GetByIdsAsync was added to IReadRepository<,> in P-093 for batch lookups.
        Offending pattern: class OrderReadRepository : IReadRepository<Order, Guid> with no GetByIdsAsync
        Compliant pattern: class OrderReadRepository : IReadRepository<Order, Guid> {
            public Task<IReadOnlyList<Order>> GetByIdsAsync(IEnumerable<Guid> ids, ...) { ... } }

HasRequiredMethodPredicate  (class : ICustomRule — internal predicate)
    Constructed with (string interfaceNamePrefix, string requiredMethodName). For each type,
    checks TypeDefinition.Interfaces for an entry whose InterfaceType.Name starts with
    interfaceNamePrefix. For matching types, checks TypeDefinition.Methods for any method
    whose Name equals requiredMethodName (exact match, any overload). Returns false (rule
    violated) if no such method is found; failure message includes the offending type name,
    the required method name, and the implementing interface prefix. Reuses the established
    Mono.Cecil TypeDefinition access pattern — no new NuGet dependency.
    Lives in Predicates/ folder. Used by RepositoryContractCompletenessRules.

NoDirectSaveChangesPredicate  (class : ICustomRule — internal predicate)
    Walks TypeDefinition.Methods for each type. For each MethodDefinition.Body.Instructions,
    checks for Call or Callvirt opcodes whose operand is a MethodReference with
    DeclaringType.Name equal to "DbContext" and Name equal to "SaveChanges" or
    "SaveChangesAsync". Returns false (rule violated) for the first type found containing
    such a call, with failure message including the declaring type name and method name.
    Exemption: types whose TypeDefinition.Namespace starts with "SharedKernel.Persistence.EfCore"
    are returned as passing (true) unconditionally — this is the EfUnitOfWork exclusion.
    Lives in Predicates/ folder. Used by PersistenceLayerProtectionRules.OnlyEfUnitOfWorkMayCallSaveChanges.

NoIQueryableReturnPredicate  (class : ICustomRule — internal predicate)
    Scopes to types whose TypeDefinition.Interfaces contains an entry whose
    InterfaceType.Name starts with "IRepository". For each such type, inspects all
    TypeDefinition.Methods where IsConstructor is false and IsGetter is false. If any
    method's ReturnType.Name is "IQueryable" or ReturnType.FullName contains "IQueryable",
    returns false (rule violated) with failure message including the declaring type name and
    method name. Covers both IQueryable and IQueryable<T> in IL. Lives in Predicates/ folder.
    Used by PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable.

EncryptionPatternGuardRules  (static class — encryption subsystem misuse enforcement predicates; WO-019 P-114)
    All factory methods accept Assembly (or params Assembly[]) and return ConditionList.
    .NoCryptoCipherInDomainOrApplication(params Assembly[])          → ConditionList
        Asserts that no type in the supplied assemblies references
        System.Security.Cryptography.AesGcm, System.Security.Cryptography.Aes, or
        System.Security.Cryptography.SymmetricAlgorithm directly. Uses
        NoAesCipherInDomainOrApplicationPredicate (ICustomRule — see below).
        Exemption: types whose TypeDefinition.Namespace starts with "SharedKernel.Persistence.*"
        or "SharedKernel.Security.*" pass unconditionally — these are the only legitimate
        crypto consumers. Failure message names the offending type and the cipher type
        referenced.
        Rationale: cipher usage in 03.Domain or 05.Application destroys layering isolation
        and bypasses the platform-managed AES-256-GCM key rotation lifecycle. All
        field-level encryption must route through EncryptedValueConverter<T>.
        Offending pattern: class OrderEncryptionHelper { private AesGcm _cipher = new(key); }
        Compliant pattern: configure encryption via PropertyBuilder<T>.Encrypt() in
            IEntityTypeConfiguration<T>; never reference AesGcm in domain/application code.

    .NoEncryptionAttributeOnDomainEntities(Assembly)                 → ConditionList
        Asserts that no type in the supplied domain assembly carries a custom attribute
        whose name contains "Encrypt" as a substring (case-insensitive). Uses
        NoEncryptionAttributeOnDomainEntityPredicate (ICustomRule — see below).
        Attribute detection via TypeDefinition.CustomAttributes enumeration (no IL walk).
        Failure message names the offending type and the offending attribute type name.
        Rationale: attribute-based encryption (e.g., [EncryptedColumn], [Encrypted]) on
        domain entity classes is the primary misuse pattern from developers familiar with
        other ORM frameworks. It leaks infrastructure concerns into the domain layer and
        bypasses EncryptionModelConvention, preventing the platform key-rotation lifecycle
        from operating correctly.
        Offending pattern: [EncryptedColumn] public string Ssn { get; private set; }
            on a domain entity class
        Compliant pattern: builder.Property(x => x.Ssn).Encrypt(); inside
            IEntityTypeConfiguration<Order>.Configure()

    .NoEncryptionRotationJobInjectionInDomainOrApplication(params Assembly[]) → ConditionList
        Asserts that no type in the supplied assemblies injects IEncryptionRotationJob as
        a constructor parameter. Uses NoEncryptionRotationJobInjectionPredicate (ICustomRule
        — see below). Detection via TypeDefinition.Methods where IsConstructor, checking
        ParameterDefinition.ParameterType.Name == "IEncryptionRotationJob" (exact name match).
        Exemption list:
          - Types whose TypeDefinition.Namespace starts with "SharedKernel.Persistence.*"
          - Types whose TypeDefinition.Name contains any of: "RotationJob", "HostedService",
            "Controller", "Activity" (substring match — designates legitimate infrastructure
            consumers of the rotation job interface)
        Failure message names the offending type and the constructor where injection occurs.
        Rationale: IEncryptionRotationJob is an infrastructure operation. Injecting it in
        a MediatR handler or domain service incorrectly places key-rotation responsibility
        in the application layer, conflating business logic with infrastructure lifecycle
        management. Rotation must be triggered from a hosted service, Hangfire job, Temporal
        activity, or management endpoint — not from command/query handlers.
        Offending pattern: class RotateKeysCommandHandler(IEncryptionRotationJob rotationJob)
        Compliant pattern: class EncryptionKeyRotationHostedService(IEncryptionRotationJob rotationJob)

    .NoDirectEncryptedValueConverterInstantiation(Assembly)          → ConditionList
        Asserts that no type implementing IEntityTypeConfiguration<T> directly instantiates
        EncryptedValueConverter<T> via a newobj IL opcode. Uses
        NoDirectEncryptedValueConverterInstantiationPredicate (ICustomRule — see below).
        Scope: types whose TypeDefinition.Interfaces contains an entry with
        InterfaceType.Name.StartsWith("IEntityTypeConfiguration"). Walks
        TypeDefinition.Methods.Body.Instructions for Newobj opcodes where
        MethodReference.DeclaringType.Name.Contains("EncryptedValueConverter").
        Exemption: types whose TypeDefinition.Name == "EncryptionModelConvention" (exact
        match) return true unconditionally — the convention is the sole legitimate
        instantiation site.
        Failure message names the offending IEntityTypeConfiguration<T> implementor and
        the method containing the direct instantiation.
        Rationale: EncryptionModelConvention (registered via EfCorePersistenceBuilder
        .WithEncryption()) detects the .Encrypt() marker and applies EncryptedValueConverter<T>
        automatically at model finalization. Direct instantiation bypasses the convention,
        causing either duplicate converter registration (double-encryption) or inconsistent
        key-version handling across the model. The .Encrypt() extension is the only safe
        call site.
        Offending pattern: builder.Property(x => x.Ssn)
            .HasConversion(new EncryptedValueConverter<string>(options));
        Compliant pattern: builder.Property(x => x.Ssn).Encrypt();

NoAesCipherInDomainOrApplicationPredicate  (class : ICustomRule — internal predicate)
    Namespace exemption guard (first check): types whose TypeDefinition.Namespace starts
    with "SharedKernel.Persistence" or "SharedKernel.Security" return true unconditionally.
    For all other types, checks two surfaces for System.Security.Cryptography cipher types:
      (1) TypeDefinition.Fields — checks FieldDefinition.FieldType.Namespace ==
          "System.Security.Cryptography" and FieldDefinition.FieldType.Name in
          {"AesGcm", "Aes", "SymmetricAlgorithm"}
      (2) TypeDefinition.Methods.Body.Instructions — for Call, Callvirt, and Newobj opcodes,
          checks the resolved TypeReference.Namespace and TypeReference.Name against the
          same set.
    Returns false (rule violated) on the first match, with failure message naming the
    offending type and the cipher type name. Lives in Predicates/ folder. Used by
    EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication.

NoEncryptionAttributeOnDomainEntityPredicate  (class : ICustomRule — internal predicate)
    For each type, iterates TypeDefinition.CustomAttributes. For each CustomAttribute,
    checks AttributeType.Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase).
    Returns false (rule violated) for the first type found carrying a matching attribute,
    with failure message naming the offending type and the attribute type name. No IL
    instruction walk required — attribute inspection only. Lives in Predicates/ folder.
    Used by EncryptionPatternGuardRules.NoEncryptionAttributeOnDomainEntities.

NoEncryptionRotationJobInjectionPredicate  (class : ICustomRule — internal predicate)
    Namespace exemption guard: types whose TypeDefinition.Namespace starts with
    "SharedKernel.Persistence" return true unconditionally.
    Class-name exemption guard: types whose TypeDefinition.Name contains any of
    "RotationJob", "HostedService", "Controller", "Activity" (substring, case-sensitive)
    return true unconditionally.
    For all other types, iterates TypeDefinition.Methods where IsConstructor is true.
    For each constructor, checks ParameterDefinition.ParameterType.Name ==
    "IEncryptionRotationJob" (exact name match — unique within the SDK).
    Returns false (rule violated) on the first match, with failure message naming the
    offending type and the constructor signature. Lives in Predicates/ folder. Used by
    EncryptionPatternGuardRules.NoEncryptionRotationJobInjectionInDomainOrApplication.

NoDirectEncryptedValueConverterInstantiationPredicate  (class : ICustomRule — internal predicate)
    Type-scope guard (first check): types whose TypeDefinition.Interfaces does NOT contain
    any entry with InterfaceType.Name starting with "IEntityTypeConfiguration" return true
    unconditionally (not in scope — not an EF Core configuration class).
    Convention exemption: types whose TypeDefinition.Name == "EncryptionModelConvention"
    (exact match) return true unconditionally.
    For all remaining types (IEntityTypeConfiguration<T> implementors other than the
    convention), walks TypeDefinition.Methods.Body.Instructions for Newobj opcodes.
    For each Newobj instruction, checks MethodReference.DeclaringType.Name.Contains(
    "EncryptedValueConverter") (substring, case-sensitive).
    Returns false (rule violated) on the first match, with failure message naming the
    offending type name and the method name where the direct instantiation occurs.
    Lives in Predicates/ folder. Used by
    EncryptionPatternGuardRules.NoDirectEncryptedValueConverterInstantiation.

EfCorePackageHygieneRules  (static class — EfCore package hygiene predicates; WO-017 P-103)
    All factory methods accept Assembly as their parameter and return ConditionList.
    .NoSpecificationEvaluatorDowncastInEfCoreAssembly(Assembly)  → ConditionList
        Asserts that no type in the supplied assembly performs a castclass instruction whose
        target TypeReference.Name starts with "SpecificationEvaluator". Uses
        NoSpecificationEvaluatorDowncastPredicate (ICustomRule — see below). The rule is
        scoped to the SharedKernel.Persistence.EfCore assembly. Failure message names the
        offending type and method containing the cast instruction.
        Rationale: P-097 eliminated the concrete downcast of ISpecificationEvaluator<T> by
        adding GetProjectedQuery to the interface. Without this rule a future refactor can
        silently re-introduce the cast (SpecificationEvaluator<T>)evaluator, bypassing the
        abstraction and preventing interface substitution.
        Offending pattern: var concreteEval = (SpecificationEvaluator<T>)_evaluator;
        Compliant pattern: _evaluator.GetProjectedQuery(query, spec);

    .IUnitOfWorkImplementorsMustHaveExactlyOneConstructor(Assembly)  → ConditionList
        Asserts that every non-abstract type implementing IUnitOfWork has exactly one public
        instance constructor. Uses SingleConstructorPredicate (ICustomRule — see below).
        Scope: types whose TypeDefinition.Interfaces contains an entry with
        InterfaceType.Name == "IUnitOfWork". Counts TypeDefinition.Methods where
        IsConstructor is true and IsStatic is false. Fails if count ≠ 1. Failure message
        names the offending type and its actual constructor count.
        Rationale: EfUnitOfWork was reduced to a single constructor in P-098 to resolve DI
        ambiguity caused by two competing registrations. A second "convenience constructor"
        would silently reintroduce the ambiguity, causing runtime DI resolution failures.
        Offending pattern: class EfUnitOfWork : IUnitOfWork {
            public EfUnitOfWork(AppDbContext ctx) { }
            public EfUnitOfWork() { }  // ← second constructor — DI ambiguity regression
        }
        Compliant pattern: class EfUnitOfWork : IUnitOfWork {
            public EfUnitOfWork(AppDbContext ctx) { }
        }

    .ApplicationLayerMustNotReferenceDbContextTransaction(Assembly)  → ConditionList
        Asserts that no type in the supplied assembly references
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction via field type,
        constructor parameter type, or method call operand. Uses
        NoDbContextTransactionInApplicationPredicate (ICustomRule — see below).
        Exemption: types whose TypeDefinition.Namespace starts with "SharedKernel.Persistence"
        are returned as passing (true) unconditionally — the persistence layer itself may use
        IDbContextTransaction internally. The consuming test must pass only the 05.Application
        assembly; persistence assemblies must not be included in the scan.
        Failure message names the offending type and the declaration site (field, constructor
        parameter, or method reference).
        Rationale: ITransactionalUnitOfWork (P-099) is the only permitted transaction entry
        point for application handlers. Direct injection of IDbContextTransaction couples
        application code to EF Core's specific transaction implementation, making the
        transaction abstraction boundary unenforceable.
        Offending pattern: class CreateOrderHandler {
            public CreateOrderHandler(IDbContextTransaction tx) { }
        }
        Compliant pattern: class CreateOrderHandler {
            public CreateOrderHandler(ITransactionalUnitOfWork unitOfWork) { }
        }
        Exemptions: SharedKernel.Persistence.* namespaces (persistence implementation layer).

NoSpecificationEvaluatorDowncastPredicate  (class : ICustomRule — internal predicate)
    Walks TypeDefinition.Methods for each type. For each MethodDefinition.Body.Instructions,
    checks for Castclass opcodes whose operand TypeReference.Name starts with
    "SpecificationEvaluator" (case-sensitive). Returns false (rule violated) for the first
    method found containing such a cast, with failure message including the declaring type
    name and method name. Lives in Predicates/ folder. Used by
    EfCorePackageHygieneRules.NoSpecificationEvaluatorDowncastInEfCoreAssembly.

SingleConstructorPredicate  (class : ICustomRule — internal predicate)
    Scopes to types whose TypeDefinition.Interfaces contains an entry with
    InterfaceType.Name == "IUnitOfWork". For each such type, counts TypeDefinition.Methods
    where IsConstructor is true and IsStatic is false (instance constructors only). Returns
    false (rule violated) if the count is not exactly 1, with failure message including the
    offending type name and the actual constructor count. Returns true (passes) for types
    not implementing IUnitOfWork — the predicate self-scopes. Lives in Predicates/ folder.
    Used by EfCorePackageHygieneRules.IUnitOfWorkImplementorsMustHaveExactlyOneConstructor.

NoDbContextTransactionInApplicationPredicate  (class : ICustomRule — internal predicate)
    Exemption guard (first check): types whose TypeDefinition.Namespace starts with
    "SharedKernel.Persistence" return true unconditionally.
    For all other types, checks three surfaces for the substring "IDbContextTransaction" in
    the FullName of referenced types:
      (1) TypeDefinition.Fields — checks FieldDefinition.FieldType.FullName
      (2) TypeDefinition.Methods — for each MethodDefinition, checks
          MethodDefinition.Parameters[*].ParameterType.FullName
      (3) TypeDefinition.Methods.Body.Instructions — for Call/Callvirt opcodes, checks
          MethodReference.DeclaringType.FullName
    Returns false (rule violated) on the first match, with failure message naming the
    offending type and the specific declaration site. Lives in Predicates/ folder. Used by
    EfCorePackageHygieneRules.ApplicationLayerMustNotReferenceDbContextTransaction.
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
- `DomainGoldStandardRules.DomainServicesMustExtendAbstractBase` uses `.AreNotAbstract()` in the NetArchTest predicate chain to exclude the `DomainService` abstract base class itself. The consuming test project must reference `SharedKernel.Domain` so that `typeof(IDomainService)` and `typeof(DomainService)` can be resolved as assembly references.
- SK0008 `AggregateRootDispatchCouplingAnalyzer` checks `ConstructorDeclarationSyntax` parameter types — not `ObjectCreationExpression` or field declarations. The type name check for `IAggregateRoot` uses `SimpleNameSyntax` or `GenericNameSyntax` identifier text (not the full `ToString()`). Dispatch-context check applies to both the class name and all ancestor `NamespaceDeclarationSyntax` / `FileScopedNamespaceDeclarationSyntax` names via the established parent walk pattern. No semantic model required.
- SK0009 `DomainEventMissingVersionAttributeAnalyzer` operates on both `ClassDeclarationSyntax` and `RecordDeclarationSyntax`. The base list check is a simple name match — `BaseList.Types` iterated for any `SimpleNameSyntax` or `IdentifierNameSyntax` whose identifier text is `"IDomainEvent"`. Abstract types are excluded via `Modifiers.Any(SyntaxKind.AbstractKeyword)`. No semantic model required.
- SK0010 `SpecificationOrderingConflictAnalyzer` collects `InvocationExpressionSyntax` nodes from the constructor body. The method name is extracted from `MemberAccessExpressionSyntax.Name.Identifier.Text` or, for simple invocations, directly from `IdentifierNameSyntax.Identifier.Text`. Both `"ApplyOrderBy"` and `"ApplyOrderByDescending"` must appear for SK0010 to fire. No semantic model required.
- `ContractsPurityRules.ContractsAssembliesHaveNoDomainTypeOnPublicSurface` — if NetArchTest's dependency scanner picks up the `EventEnvelope<TEvent> where TEvent : IDomainEvent` generic constraint as a dependency on `SharedKernel.Domain`, the `EventEnvelope` type must be explicitly excluded from the scan using `.And().DoNotHaveName("EventEnvelope")` before the `.Should()` clause. Document the exclusion in the architecture test fixture.
- `ContractsPurityRules.IntegrationEventImplementationsMustBeSealed` — if `.BeSealed()` is not exposed by `NetArchTest.eNt` 1.3.2, implement a `SealedTypePredicate` ICustomRule that checks `TypeDefinition.IsSealed`. Record the API surface check result in `00.Governance/CLAUDE.md` once confirmed.
- `PersistenceLayerProtectionRules.OnlyEfUnitOfWorkMayCallSaveChanges` — the namespace exemption (`TypeDefinition.Namespace.StartsWith("SharedKernel.Persistence.EfCore")`) is evaluated as the first guard inside `NoDirectSaveChangesPredicate`. Do not apply the exemption at the `PersistenceLayerProtectionRules` call site — it belongs inside the predicate so the rule correctly self-documents the single permitted caller.
- `PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable` — the `"IRepository"` prefix check on `TypeDefinition.Interfaces` is intentionally broad: it covers `IRepository<T,TId>`, `IReadRepository<T,TId>`, and any sub-interface. `IQueryable` is matched by `ReturnType.Name == "IQueryable"` (non-generic) or `ReturnType.FullName.Contains("IQueryable")` (generic). Both checks are required to cover the IL representation of `IQueryable<T>`.
- `PersistenceLayerProtectionRules.DomainAssembliesNeverReferencePersistenceStack` is additive with `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure` — both rules may run in the same test suite. They are not duplicates: the latter covers broad infra terms; this rule adds Npgsql and `SharedKernel.Persistence.*` as a WO-013-scoped gate. Never remove either in favour of the other.
- `NoDirectSaveChangesPredicate` and `NoIQueryableReturnPredicate` reuse the established Mono.Cecil `TypeDefinition` access pattern from `DoesNotContainThrowIlPredicate`. The existing `Mono.Cecil >= 0.11.5` NuGet reference in `SharedKernel.ArchitectureTests` covers both new predicates — no new NuGet dependency is introduced.
- `PersistenceInterfaceOwnershipRules.IUserContextDeclaredOnlyInSecurityAbstractions` and `TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions` accept `params Assembly[]` — the caller must NOT pass `SharedKernel.Security.Abstractions` itself; only the assemblies to be checked for erroneous re-declarations are supplied. These rules do not assert presence in the owner — they assert absence everywhere else.
- `PersistenceInterfaceOwnershipRules.IReadRepositoryMustNotExposeIQueryable` is scoped to `"IReadRepository"` prefix specifically — it is separate from and complementary to `PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable` (which uses the broader `"IRepository"` prefix). Both may run in the same test suite targeting the same assembly; neither removes the need for the other.
- `PersistenceInterfaceOwnershipRules.NoGetByIdAsyncOnReadRepository` uses exact name match `"GetByIdAsync"` only — it does not suppress for abstract base classes. If an abstract base `IReadRepository` implementor declares `GetByIdAsync`, the rule fires on the base class too (intentional — the method must be removed at every declaration level).
- `InterfaceDeclarationOwnershipPredicate` is stateless and may be reused across multiple `PersistenceInterfaceOwnershipRules` factory methods with different name sets. Construct a new instance per call — do not share instances across rules to avoid name-set bleed.
- SK0011 `GuidFormatCodeMisuseAnalyzer` is the first SK analyzer to require a `SemanticModel.GetTypeInfo` check on the receiver expression. This is necessary to distinguish `Guid.ToString("N")` from `int.ToString("N")` (which is a valid numeric format specifier). The semantic model call is scoped only to `ToString` invocations with a single string literal argument — the cost is minimal.
- SK0011 fires globally with no suppression namespace. Suppression is per-call-site only (`#pragma warning disable SK0011`). The rationale is that non-`"D"` Guid formats are never correct in audit trail context; any other context (URL segments, log correlation IDs) should be explicitly opted out with an inline suppression and a comment.
- `HasRequiredMethodPredicate` must not be confused with a presence-enforcer at the interface level — it operates at the implementor (concrete type) level. It does not assert that the interface itself declares the method; it asserts that the concrete implementing type has the method in its `TypeDefinition.Methods`. This covers both direct declaration and inherited declaration (if the type inherits from a base that declares the method, NetArchTest's Mono.Cecil `TypeDefinition.Methods` may or may not include inherited methods — test this behavior and if inherited methods are not covered, scope the predicate to `BaseType` traversal as well).
- `RepositoryContractCompletenessRules` must be called with the assembly containing the concrete repository implementations (e.g., `SharedKernel.Persistence.EfCore`), not the abstractions assembly. The abstractions assembly contains interfaces, not implementations — `HasRequiredMethodPredicate` scopes to interface-implementing types, so an abstractions-only assembly will produce zero matches and the rule will trivially pass, masking real violations.
- `EfCorePackageHygieneRules.NoSpecificationEvaluatorDowncastInEfCoreAssembly` must be called with the `SharedKernel.Persistence.EfCore` assembly only. The `castclass` opcode check is a simple operand name prefix match — no semantic model or type hierarchy walk is required. The rule fires on any cast whose target `TypeReference.Name` starts with `"SpecificationEvaluator"`, covering both the generic (`SpecificationEvaluator<T>`) and any subclass forms in IL.
- `EfCorePackageHygieneRules.IUnitOfWorkImplementorsMustHaveExactlyOneConstructor` must be called with the `06.Persistence` assembly containing concrete `IUnitOfWork` implementors (e.g., `SharedKernel.Persistence.EfCore`). `SingleConstructorPredicate` self-scopes to `IUnitOfWork` implementors only — passing an unrelated assembly produces zero matches and the rule trivially passes without masking violations, provided the correct persistence assembly is also passed.
- `EfCorePackageHygieneRules.ApplicationLayerMustNotReferenceDbContextTransaction` must be called with the `05.Application` assembly. Passing persistence assemblies is redundant — the namespace exemption inside `NoDbContextTransactionInApplicationPredicate` is a safety net, not the primary enforcement mechanism. The "IDbContextTransaction" substring check covers the full interface name including namespace in the `FullName` property, ensuring `BeginTransactionAsync` return types and `IDbContextTransaction`-typed fields are both detected.
- `EncryptionPatternGuardRules` introduces a new 03xx SK ID block (SK0301–SK0304) dedicated to the WO-019 encryption subsystem. The 03xx block is separate from the sequential SK0001–SK0011 general-purpose block and the SK0201–SK0202 multi-tenancy block. Never backfill SK0012–SK0200 with encryption rules — those gaps are reserved for the respective domain blocks.
- `NoAesCipherInDomainOrApplicationPredicate` checks both field types and IL instruction operands for the three cipher types (`AesGcm`, `Aes`, `SymmetricAlgorithm`). The namespace guard (`SharedKernel.Persistence.*` and `SharedKernel.Security.*`) is applied as the first check, before any IL walking, to avoid false positives from the legitimate converter and JWT signing code paths.
- `NoEncryptionAttributeOnDomainEntityPredicate` uses a case-insensitive substring match on `"Encrypt"` — this deliberately catches all common forms: `[Encrypted]`, `[EncryptedColumn]`, `[EncryptAttribute]`, `[ShouldEncrypt]`, etc. If a future attribute with "Encrypt" in its name is legitimately placed on a domain type for non-encryption purposes, document the exemption in this file before adding a name-specific exclusion to the predicate.
- `NoEncryptionRotationJobInjectionPredicate` class-name exemptions (`*RotationJob*`, `*HostedService*`, `*Controller*`, `*Activity*`) are substring matches on `TypeDefinition.Name` (the simple CLR type name, not the full namespace-qualified name). This is intentionally broad to cover naming conventions like `EncryptionKeyRotationHostedService`, `KeyRotationActivity`, and `EncryptionManagementController`.
- `NoDirectEncryptedValueConverterInstantiationPredicate` is scoped to `IEntityTypeConfiguration<T>` implementors only (interface name prefix check). General application code that is not an EF Core configuration class is not subject to SK0304 — the rule is narrowly targeted at the EF Core model-building phase where the misuse pattern causes double-encryption.
- `EncryptionModelConvention` exemption in `NoDirectEncryptedValueConverterInstantiationPredicate` is by exact type name (`TypeDefinition.Name == "EncryptionModelConvention"`). If the convention class is renamed, update both the predicate and this rule entry in the same PR.
- All four predicates (SK0301–SK0304) reuse the Mono.Cecil `TypeDefinition` access pattern established by `DoesNotContainThrowIlPredicate`. No new NuGet dependency — the existing `Mono.Cecil >= 0.11.5` explicit reference in `SharedKernel.ArchitectureTests` covers all four.
- `EncryptionPatternGuardRules` factory methods are called with domain and application assemblies supplied by the consuming test project via `typeof(SomeDomainType).Assembly`. The factory methods never hard-code assembly paths.
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
- [2026-05-30] SK0008 AggregateRootDispatchCoupling, SK0009 DomainEventMissingVersionAttribute, SK0010 SpecificationOrderingConflict added to diagnostic registry; DomainGoldStandardRules and ContractsPurityRules added to architecture test contracts; NoNonTrivialMethodsPredicate documented; eight new implementation rules added — WO-011 P-056, WO-012 P-063
- [2026-06-01] PersistenceLayerProtectionRules added to architecture test contracts (three predicates: OnlyEfUnitOfWorkMayCallSaveChanges, RepositoriesMustNotExposeIQueryable, DomainAssembliesNeverReferencePersistenceStack); NoDirectSaveChangesPredicate and NoIQueryableReturnPredicate documented; five new implementation rules added — WO-013 P-075
- [2026-06-03] EfCorePackageHygieneRules added to architecture test contracts (three predicates: NoSpecificationEvaluatorDowncastInEfCoreAssembly, IUnitOfWorkImplementorsMustHaveExactlyOneConstructor, ApplicationLayerMustNotReferenceDbContextTransaction); NoSpecificationEvaluatorDowncastPredicate, SingleConstructorPredicate, NoDbContextTransactionInApplicationPredicate ICustomRules documented; three new implementation rules added — WO-017 P-103
- [2026-06-02] SK0011 GuidFormatCodeMisuse added to diagnostic registry (requires SemanticModel.GetTypeInfo — first SK analyzer with semantic check; fires on Guid.ToString("N"/"B"/"P"/"X"); no suppression namespace); PersistenceInterfaceOwnershipRules added to architecture test contracts (four predicates: IUserContextDeclaredOnlyInSecurityAbstractions, TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions, IReadRepositoryMustNotExposeIQueryable, NoGetByIdAsyncOnReadRepository); RepositoryContractCompletenessRules added (two predicates: AllRepositoryImplementorsMustHaveExistsAsync, AllReadRepositoryImplementorsMustHaveGetByIdsAsync); InterfaceDeclarationOwnershipPredicate, NoGetByIdOnReadRepositoryPredicate, HasRequiredMethodPredicate ICustomRules documented; eleven new implementation rules added — WO-014 P-083, WO-016 P-096
- [2026-06-04] SK0301 DirectCryptoInDomainOrApplication, SK0302 EncryptionAttributeOnDomainEntity, SK0303 EncryptionRotationJobInDomainOrApplication, SK0304 DirectEncryptedValueConverterInstantiation added to diagnostic registry (new 03xx block for encryption-domain rules; all implemented as NetArchTest ICustomRule predicates); EncryptionPatternGuardRules static class added to architecture test contracts with four predicates (NoCryptoCipherInDomainOrApplication, NoEncryptionAttributeOnDomainEntities, NoEncryptionRotationJobInjectionInDomainOrApplication, NoDirectEncryptedValueConverterInstantiation); four new ICustomRule predicates documented; seven new implementation rules added — WO-019 P-114
