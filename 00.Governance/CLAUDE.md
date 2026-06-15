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

SK0201  TenantedDbContextOnModelCreatingGuard
    Category  : Design
    Severity  : Warning
    Trigger   : A class whose BaseList contains TenantedDbContext (simple name match) declares
                an override of OnModelCreating; the method body does not contain any invocation
                of base.OnModelCreating(...) or ApplyTenantFilters(...) (simple name or member
                access, any receiver). Neither found → fires SK0201 on the method identifier.
    Fix       : Add base.OnModelCreating(modelBuilder) as the first call in the override body,
                or explicitly call this.ApplyTenantFilters(modelBuilder) when the base call must
                be deferred (advanced multi-context patterns only; document the rationale).
    Suppress  : Per-site via #pragma warning disable SK0201 with a comment explaining why the
                tenant filter is intentionally omitted or applied via another mechanism.
    Note      : Syntax-only check scoped to the current file. Cross-file or cross-assembly
                inheritance chains (e.g., class A : B : TenantedDbContext, where B is in a
                different file) are not resolved — only the immediate BaseList is inspected.
                For deep inheritance trees document the chain in a comment near the override.
                ID block: 02xx (multi-tenancy). Introduced in WO-020 SK.00.TenantedDbContextGuard.

SK0202  IgnoreQueryFiltersOutsideTenantedRepository
    Category  : Design
    Severity  : Warning
    Trigger   : An InvocationExpressionSyntax whose simple method name is IgnoreQueryFilters
                and whose argument list is empty (zero arguments) is found outside:
                  (a) any namespace whose qualified name starts with SharedKernel.Persistence.EfCore
                  (b) any class whose Identifier.Text is exactly "TenantedRepository"
                Namespace check is the same SyntaxNode.Parent walk used by SK0001/SK0007.
                Class check is exact string match on the immediate ClassDeclarationSyntax.
    Fix       : Move the IgnoreQueryFilters() call into a method on the TenantedRepository base
                class, or into a type inside SharedKernel.Persistence.EfCore. If a cross-tenant
                query is genuinely required in another layer, subclass TenantedRepository and
                expose a purpose-named query method — never scatter raw IgnoreQueryFilters() calls
                across the codebase.
    Suppress  : Per-site via #pragma warning disable SK0202 with an inline comment stating the
                business reason for the cross-tenant access and who approved the exemption.
    Exempt    : Namespace prefix SharedKernel.Persistence.EfCore (and all sub-namespaces) —
                the persistence implementation layer may use IgnoreQueryFilters() deliberately.
                Class exact name TenantedRepository — the designated cross-tenant repository
                base class is the single sanctioned call site outside the platform namespace.
                Any additional exemption class or namespace must be documented in this file
                before applying a suppression.
    Note      : ID block: 02xx (multi-tenancy). Introduced in WO-020 SK.00.TenantedDbContextGuard.

SK0703  MessageBusSingletonRegistration
    Category  : Usage
    Severity  : Warning
    Trigger   : An InvocationExpressionSyntax whose simple method name is "AddSingleton"
                contains a type argument whose simple name starts with "IMessageBus" or
                "IEventPublisher". Covers the two-argument form
                AddSingleton<IMessageBus, MassTransitMessageBus>() and the one-argument form
                AddSingleton<IMessageBus>(factory). Syntax-only check; no SemanticModel required.
    Fix       : Replace AddSingleton with AddScoped to match MassTransit's per-consume-scope
                lifetime model. Singleton registration of IMessageBus or IEventPublisher causes
                scope pollution and race conditions under concurrent load.
    Suppress  : Per-call-site via #pragma warning disable SK0703 only when the DI container
                semantics are provably equivalent to scoped behaviour (document the reason inline).
    Note      : No suppression namespace — SK0703 fires globally. ID block: 07xx
                (messaging-domain). Introduced in WO-020 P-123.

SK0704  HardcodedQueueUriInGetSendEndpoint
    Category  : Usage
    Severity  : Warning
    Trigger   : An InvocationExpressionSyntax whose simple method name is "GetSendEndpoint"
                has at least one argument that is an ObjectCreationExpressionSyntax (or
                ImplicitObjectCreationExpressionSyntax) of type "Uri" whose first argument is
                a StringLiteralExpression whose value starts with "queue:" or "exchange:"
                (case-insensitive). Syntax-only check; no SemanticModel required.
    Fix       : Remove the literal Uri construction and use convention-based endpoint resolution
                via IEndpointNameFormatter.GetDestinationAddress<TMessage>() or the equivalent
                MassTransit IEndpointNameFormatter helper. Convention-based addressing is
                environment-agnostic and survives broker configuration changes.
    Suppress  : Per-call-site via #pragma warning disable SK0704 only when a fixed,
                environment-invariant queue address is genuinely required (e.g., a dead-letter
                queue URI in an isolated test fixture); document the rationale inline.
    Note      : No suppression namespace — SK0704 fires globally. ID block: 07xx
                (messaging-domain). Introduced in WO-020 P-123.

SK0705  FaultConsumerDirectRegistration
    Category  : Usage
    Severity  : Warning
    Trigger   : An InvocationExpressionSyntax whose simple method name is "AddScoped"
                or "AddSingleton" has a type argument that is a GenericNameSyntax whose
                Identifier.Text is "IFaultConsumer" (simple name, exact match).
                Covers AddScoped<IFaultConsumer<TMessage>>() and the two-argument form
                AddScoped<IFaultConsumer<TMessage>, TImpl>(). Syntax-only; no SemanticModel.
    Fix       : Register fault consumers via MessagingBusBuilder.AddFaultConsumer
                <TMessage, TConsumer>() — the builder wires the MassTransit Fault<T>
                adapter that translates Fault<T> context to the platform IFaultConsumer<T>
                abstraction. Direct DI registration bypasses the adapter chain, causing
                the consumer to receive a raw MassTransit Fault<T> context with no
                platform translation.
    Suppress  : Per-call-site via #pragma warning disable SK0705 only when explicitly
                bypassing the builder (document the rationale inline).
    Note      : No suppression namespace — SK0705 fires globally. ID block: 07xx
                (messaging-domain). Introduced in WO-021 P-133.

SK0706  DirectMassTransitSchedulerInjection
    Category  : Design
    Severity  : Warning
    Trigger   : A constructor parameter whose ParameterType.Name == "IMessageScheduler"
                AND ParameterType.Namespace.StartsWith("MassTransit") is found in a type
                whose namespace does NOT start with "SharedKernel.Messaging". Detected
                via NoDirectSchedulerInjectionOutsideMessagingPredicate (ICustomRule) —
                not a per-call-site Roslyn analyzer. Distinguishes MassTransit.IMessageScheduler
                (forbidden) from SharedKernel.Messaging.Abstractions.IMessageScheduler
                (permitted) by namespace check on ParameterType. Fallback: if namespace
                cannot be resolved, checks ParameterType.Scope.Name.Contains("MassTransit").
    Exempt    : Types whose TypeDefinition.Namespace starts with "SharedKernel.Messaging" —
                the messaging adapter layer may reference MassTransit.IMessageScheduler
                for internal adapter wiring. Any additional exemption must be documented
                here before applying.
    Fix       : Inject SharedKernel.Messaging.Abstractions.IMessageScheduler instead.
                The platform scheduler abstraction preserves transport independence and
                is the only permitted scheduler injection point outside SharedKernel.Messaging.
    Note      : Implemented as a NetArchTest ICustomRule
                (NoDirectSchedulerInjectionOutsideMessagingPredicate) — enforced at
                assembly level (post-compile). Introduced in WO-021 P-133.

SK0707  SagaStateMustExtendSagaStateBase
    Category  : Design
    Severity  : Warning
    Trigger   : A class whose TypeDefinition.Interfaces contains an entry with
                InterfaceType.Name == "ISaga" (exact simple name match) does not have
                "SagaStateBase" in its BaseType inheritance chain (iterative
                TypeDefinition.BaseType walk stopping at null or "Object"). Fail-open
                if BaseType.Resolve() returns null (unloaded assembly dependency) — the
                type is treated as possibly-compliant to avoid false positives.
    Exempt    : None — every ISaga implementor must extend SagaStateBase. The
                fail-open policy (null BaseType.Resolve) is not an exemption; it is a
                limitation of assembly-isolation test setups.
    Fix       : Extend SagaStateBase from SharedKernel.Messaging.MassTransit. SagaStateBase
                provides the platform's standard CorrelationId, Version (optimistic
                concurrency), CreatedAt, and ModifiedAt audit fields required for correct
                saga persistence and version-conflict resolution.
    Note      : Implemented as a NetArchTest ICustomRule
                (SagaStateMustExtendSagaStateBasePredicate). Introduced in WO-021 P-133.

SK0708  BatchConsumerRegisteredViaAddConsumer
    Category  : Usage
    Severity  : Warning
    Trigger   : An InvocationExpressionSyntax whose simple method name is "AddConsumer"
                (exact match) has a single type argument whose Identifier.Text contains
                "BatchConsumer" as a substring (case-sensitive). This is a naming-convention-
                guided heuristic: if the batch consumer class name does not contain
                "BatchConsumer", SK0708 will not fire (false negative). Syntax-only;
                no SemanticModel required.
    Fix       : Replace AddConsumer<T>() with MessagingBusBuilder.AddBatchConsumer<T>()
                to apply MessageLimit and TimeLimit batch configuration. AddConsumer<T>()
                ignores batch configuration and processes messages one at a time, defeating
                the purpose of batch consumer registration.
    Suppress  : Per-call-site via #pragma warning disable SK0708 when a batch consumer
                class genuinely must be registered individually (e.g., for a test fixture
                that processes one-at-a-time by design).
    Naming    : Batch consumer implementation classes MUST contain "BatchConsumer" in
                their class name to be detected (e.g., OrderBatchConsumer, not OrderProcessor).
                Recommend the naming convention {Purpose}BatchConsumer as an enforcement aid.
                Classes not following this convention will not receive the SK0708 diagnostic.
    Note      : No suppression namespace — SK0708 fires globally. ID block: 07xx
                (messaging-domain). Introduced in WO-021 P-133.

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

RedisTopologyRules  (static class — five-package Redis topology enforcement predicates; WO-023 P-145)
    All factory methods accept Assembly (or params Assembly[]). No Mono.Cecil, no ICustomRule,
    no new SK diagnostic IDs — every predicate is a pure assembly-dependency-graph check using
    NetArchTest's .Should().NotHaveDependencyOn(...), in the same style as CachingAbstractionRules.

    Matching note: NetArchTest's NotHaveDependencyOn(term) compares term against each scanned
    type's dependency NAMESPACES using StartsWith, with NO trailing dot on either side. Every
    forbidden term in this class is therefore the EXACT namespace of the package it identifies
    (e.g. "SharedKernel.Caching.Redis.HashStore"), never a bare root prefix such as
    "SharedKernel.Caching.Redis" in a context where that would also match
    "SharedKernel.Caching.Redis.Core". The L2 backplane package (SharedKernel.Caching.Redis) has
    no single dedicated sub-namespace — its types live under SharedKernel.Caching.Redis.Batch and
    SharedKernel.Caching.Redis.Extensions, so both are used as its identifying terms.

    Self-dependency note: a type's dependency-namespace set includes its own declaring namespace.
    A package must never be checked against its own identifying term(s) — see
    .CapabilityPackagesNeverReferenceEachOther for how this is handled.

    .RedisCoreNeverReferencesCapabilityPackages(Assembly redisCoreAssembly) → ConditionList
        Asserts that SharedKernel.Caching.Redis.Core has no dependency on any of the five
        capability-package identifying namespace terms: "SharedKernel.Caching.Redis.Batch",
        "SharedKernel.Caching.Redis.Extensions" (L2), "SharedKernel.Caching.Redis.DistributedLocking",
        "SharedKernel.Caching.Redis.HashStore", "SharedKernel.Caching.Redis.PubSub". None of these
        terms is a prefix of "SharedKernel.Caching.Redis.Core" or
        "SharedKernel.Caching.Redis.Core.Extensions", so the check produces no self-collision for
        Redis.Core. Uses five iterative .Should().NotHaveDependencyOn(term) calls — same pattern as
        DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure. Failure message
        names the offending capability package.
        Rationale: Redis.Core is the shared connection/health/resilience foundation. A reference
        from Core to any capability package is a layering inversion.

    .CapabilityPackagesNeverReferenceEachOther(params Assembly[] capabilityAssemblies) → ConditionList[]
        Returns one ConditionList per element of capabilityAssemblies, in the same order. For
        each scanned assembly, resolves its own identifying namespace term(s) by assembly simple
        name via an internal Dictionary<string,string[]> keyed on the four real package names
        ("SharedKernel.Caching.Redis" → its two Batch/Extensions terms;
        "SharedKernel.Caching.Redis.DistributedLocking", ".HashStore", ".PubSub" → their own exact
        namespace each). The returned ConditionList asserts
        .Should().NotHaveDependencyOn(term) for every term belonging to the OTHER three packages
        only — the scanned assembly's own term(s) are excluded, eliminating the self-dependency
        false positive. An assembly whose simple name is not one of the four recognized package
        names (e.g., a test fixture) is checked against the FULL term set (it owns none of the
        four namespaces, so nothing is excluded). Excludes "SharedKernel.Caching.Redis.Core" and
        "SharedKernel.Caching.Abstractions" from every forbidden set — both are permitted
        dependencies (see exemption list below). Caller must assert .GetResult().IsSuccessful on
        EACH element of the returned array.
        Rationale: sibling role-packages must depend only on .{Provider}.Core (root CLAUDE.md
        "Provider role-split variant" rule) — a sibling-to-sibling reference (e.g.,
        Redis.DistributedLocking → Redis.HashStore) is exactly the shortcut this rule forecloses.

    .PubSubNeverReferencesMessaging(Assembly pubSubAssembly) → ConditionList
        Asserts that SharedKernel.Caching.Redis.PubSub has no dependency on any assembly whose
        name starts with "SharedKernel.Messaging". Single
        .Should().NotHaveDependencyOn("SharedKernel.Messaging") call — the prefix covers both
        SharedKernel.Messaging.Abstractions and SharedKernel.Messaging.MassTransit via NetArchTest's
        substring-based dependency matching.
        Rationale: codifies the Issue 3 boundary — Redis.PubSub is an ephemeral,
        no-delivery-guarantee signaling channel (ICacheInvalidationBus / IRedisChannelService) and
        must never become a backdoor path into the durable IMessageBus abstraction.

    .MessagingNeverReferencesCaching(params Assembly[] messagingAssemblies) → ConditionList
        Asserts that no type in any of SharedKernel.Messaging.* (caller supplies
        SharedKernel.Messaging.Abstractions and SharedKernel.Messaging.MassTransit) has a
        dependency on any assembly whose name starts with "SharedKernel.Caching". Single
        Types.InAssemblies(messagingAssemblies).That()...Should()
        .NotHaveDependencyOn("SharedKernel.Caching") call across all supplied assemblies.
        Rationale: structural converse of PubSubNeverReferencesMessaging and of the root
        CLAUDE.md hard rule ("07.Messaging must never reference any SharedKernel.Caching.*
        package, and no SharedKernel.Caching.* package may reference any SharedKernel.Messaging.*
        package"). Both directions are asserted independently because NetArchTest dependency
        checks are directional.

    .CachingAbstractionsHasNoInfrastructureDependencies(Assembly abstractionsAssembly) → ConditionList
        Re-verification of the existing guarantee that SharedKernel.Caching.Abstractions has zero
        dependencies beyond Microsoft.Extensions.DependencyInjection.Abstractions. Asserts
        .Should().NotHaveDependencyOn(term) for each of: "SharedKernel.Caching.Redis"
        (bare prefix — deliberately matches Redis.Core, Redis (L2), .DistributedLocking,
        .HashStore, .PubSub, all of which start with this string),
        "StackExchange.Redis", "Microsoft.EntityFrameworkCore", "MassTransit" — the four
        infrastructure families that must never leak into the abstractions package across the
        five-package Redis topology. Same shape as SharedKernelLayeringRules.CoreReferencesNothing,
        scoped to SharedKernel.Caching.Abstractions.
        Rationale: confirms the five-package split did not introduce a transitive dependency from
        any new capability package back into the abstraction the capability packages implement.

    Permitted cross-reference exemption list (required for rules 1 and 2 to pass unmodified):
        - Redis.Core → SharedKernel.Caching.Abstractions (permitted; Core implements
          abstraction-facing health/connection contracts)
        - Redis (L2), Redis.DistributedLocking, Redis.HashStore, Redis.PubSub → Redis.Core
          (permitted; the shared foundation)
        - Redis (L2), Redis.DistributedLocking, Redis.HashStore, Redis.PubSub →
          SharedKernel.Caching.Abstractions (permitted; each implements abstraction interfaces)
        Any additional exemption must be documented here before it is applied in code.

    Note: All five factory methods accept Assembly / params Assembly[] supplied by the consuming
    test project via typeof(SomeTypeInPackage).Assembly — no assembly paths are hard-coded.
    RedisTopologyRules lives in SharedKernel.ArchitectureTests/Rules/ alongside
    CachingAbstractionRules.cs and must not reference StackExchange.Redis, MassTransit, or EF Core
    directly. Cross-reference: root CLAUDE.md Issue 3 / hard rule on the 02.Caching ↔ 07.Messaging
    exclusion boundary (PubSubNeverReferencesMessaging and MessagingNeverReferencesCaching are the
    mechanical enforcement of that rule for the post-split topology).

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

MessagingArchitectureRules  (static class — messaging abstraction boundary enforcement predicates; WO-020 P-123)
    All factory methods accept Assembly (or params Assembly[]) and return ConditionList.
    .NoDirectBusInjectionOutsideMessaging(params Assembly[] assemblies)  → ConditionList
        Asserts that no type in the supplied assemblies injects MassTransit transport types
        (IBus, IPublishEndpoint, ISendEndpointProvider) as constructor parameters. Uses
        NoDirectBusInjectionOutsideMessagingPredicate (ICustomRule — see below).
        Exemption: types whose TypeDefinition.Namespace starts with "SharedKernel.Messaging"
        pass unconditionally — 07.Messaging package types may reference MassTransit transport
        interfaces freely. All other assemblies must inject IMessageBus or IEventPublisher.
        Failure message names the offending type, the offending parameter name, and the
        recommended alternative interface.
        Rationale: MassTransit transport types (IBus, IPublishEndpoint, ISendEndpointProvider)
        are an implementation detail of SharedKernel.Messaging.MassTransit. Injecting them
        directly in application handlers, domain services, or controllers bypasses the
        SharedKernel.Messaging.Abstractions boundary and makes transport swaps impossible.
        Offending pattern: class PlaceOrderHandler(IBus bus) { }
        Compliant pattern: class PlaceOrderHandler(IMessageBus messageBus) { }

    .NoEventPublisherInDomainLayer(Assembly domainAssembly)              → ConditionList
        Asserts that no type in the supplied domain assembly injects IEventPublisher as a
        constructor parameter when that type is identified as a domain-layer type. Uses
        NoEventPublisherInDomainLayerPredicate (ICustomRule — see below).
        Domain-layer membership is determined by two signals evaluated in order:
          (a) Namespace signal: TypeDefinition.Namespace contains ".Domain." as a substring
          (b) Interface signal: TypeDefinition.Interfaces contains an entry whose
              InterfaceType.Name is in {"IEntity", "IAggregateRoot", "IValueObject",
              "IDomainService"} (exact name match)
        Either signal triggers the check. If the type is a domain-layer type AND has a
        constructor parameter whose ParameterType.Name == "IEventPublisher", the rule fails.
        Failure message names the offending type, the parameter, and the correct dispatch flow:
        domain event → IDomainEventDispatcher → application handler → IEventPublisher.
        Rationale: domain types must not reach out to publish integration events directly.
        Domain events are raised internally and dispatched by IDomainEventDispatcher in the
        application layer. Injecting IEventPublisher in a domain type collapses the separation
        between domain events and integration events, breaking the DDD event propagation model.
        Offending pattern: class OrderAggregate(IEventPublisher publisher) : AggregateRoot<Guid>
        Compliant pattern: raise domain events via AddDomainEvent(); let the application layer
            dispatch them to IEventPublisher via IDomainEventDispatcher

    Exemption list (assemblies whose types are exempt from NoDirectBusInjectionOutsideMessaging):
        - SharedKernel.Messaging (abstraction package — may reference transport types for wiring)
        - SharedKernel.Messaging.MassTransit (concrete provider — owns transport type wiring)
        Any additional exemption must be documented here before it is applied in code.

ExtendedMessagingArchitectureRules  (static class — extended messaging misuse enforcement predicates; WO-021 P-133)
    All factory methods accept Assembly (or params Assembly[]) and return ConditionList.
    .NoDirectMassTransitSchedulerInjection(params Assembly[] assemblies)  → ConditionList
        Asserts that no type in the supplied assemblies injects MassTransit.IMessageScheduler
        as a constructor parameter. Uses NoDirectSchedulerInjectionOutsideMessagingPredicate
        (ICustomRule — see below).
        Exemption: types whose TypeDefinition.Namespace starts with "SharedKernel.Messaging"
        pass unconditionally — the messaging adapter layer may reference
        MassTransit.IMessageScheduler for internal wiring. All other assemblies must
        inject SharedKernel.Messaging.Abstractions.IMessageScheduler.
        Failure message: "{offendingType} injects MassTransit.IMessageScheduler directly.
        Use SharedKernel.Messaging.Abstractions.IMessageScheduler to preserve transport
        independence."
        Rationale: MassTransit.IMessageScheduler is an implementation detail of the
        MassTransit transport. Injecting it outside SharedKernel.Messaging couples
        application and domain code to a specific scheduler implementation, making
        transport swaps impossible and creating an invisible MassTransit dependency in
        layers that should be transport-agnostic.
        Offending pattern: class ScheduleReminderHandler(MassTransit.IMessageScheduler scheduler)
        Compliant pattern: class ScheduleReminderHandler(IMessageScheduler scheduler) // platform abstraction

    .SagaStatesMustExtendSagaStateBase(Assembly assembly)  → ConditionList
        Asserts that every type implementing ISaga also extends SagaStateBase.
        Uses SagaStateMustExtendSagaStateBasePredicate (ICustomRule — see below).
        Scope: types whose TypeDefinition.Interfaces contains an entry with
        InterfaceType.Name == "ISaga". Checks TypeDefinition.BaseType chain iteratively
        (resolving each step) for any ancestor whose TypeReference.Name == "SagaStateBase".
        Fail-open policy: if BaseType.Resolve() returns null (the base type is in an
        unloaded assembly), the type is treated as possibly-compliant (returns true)
        to avoid false positives in assembly-isolation test setups.
        Failure message: "{offendingType} implements ISaga but does not extend SagaStateBase.
        All saga state classes must extend SagaStateBase to carry correlation ID, version,
        and audit fields."
        Rationale: SagaStateBase provides the platform-standard CorrelationId, Version
        (optimistic concurrency counter), CreatedAt, and ModifiedAt fields. Without these
        fields, saga state persistence fails version-conflict detection, losing concurrent
        update safety. Audit fields are also required for the platform observability pipeline.
        Offending pattern: class OrderSagaState : ISagaVersion { ... } // no SagaStateBase
        Compliant pattern: class OrderSagaState : SagaStateBase { ... }

    Exemption list (assemblies whose types are exempt from NoDirectMassTransitSchedulerInjection):
        - SharedKernel.Messaging (abstraction package — may reference transport scheduler for wiring)
        - SharedKernel.Messaging.MassTransit (concrete provider — owns scheduler adapter wiring)
        Any additional exemption must be documented here before it is applied in code.

NoDirectSchedulerInjectionOutsideMessagingPredicate  (class : ICustomRule — internal predicate)
    Namespace exemption guard (first check): types whose TypeDefinition.Namespace starts with
    "SharedKernel.Messaging" return true unconditionally.
    For all other types, iterates TypeDefinition.Methods where IsConstructor is true. For each
    constructor, checks each ParameterDefinition for two conditions both true:
      (1) ParameterType.Name == "IMessageScheduler" (exact name match)
      (2) ParameterType.Namespace.StartsWith("MassTransit") — distinguishes
          MassTransit.IMessageScheduler (forbidden) from
          SharedKernel.Messaging.Abstractions.IMessageScheduler (permitted)
    Fallback: if ParameterType.Namespace is empty or null (type reference not fully resolved),
    checks ParameterType.Scope.Name.Contains("MassTransit") as a secondary discriminator using
    the assembly scope name. Returns false (rule violated) on the first match, with failure
    message naming the offending type and the parameter. Lives in Predicates/ folder. Used by
    ExtendedMessagingArchitectureRules.NoDirectMassTransitSchedulerInjection.

SagaStateMustExtendSagaStateBasePredicate  (class : ICustomRule — internal predicate)
    Scope check: types whose TypeDefinition.Interfaces contains an entry with
    InterfaceType.Name == "ISaga" (exact simple name match). Types not implementing ISaga
    return true unconditionally (not in scope).
    For each ISaga implementor, walks the TypeDefinition.BaseType chain iteratively:
      - At each step, checks TypeReference.Name == "SagaStateBase" (exact simple name match)
      - Advances by calling BaseType.Resolve() to get the next TypeDefinition
      - Terminates when BaseType is null or BaseType.Name is "Object"
      - Fail-open: if Resolve() returns null at any step (unloaded assembly), returns true
        unconditionally — avoids false positives when assemblies are not all loaded
    Returns false (rule violated) if the chain terminates without finding "SagaStateBase",
    with failure message naming the offending type. Lives in Predicates/ folder. Used by
    ExtendedMessagingArchitectureRules.SagaStatesMustExtendSagaStateBase.

NoDirectBusInjectionOutsideMessagingPredicate  (class : ICustomRule — internal predicate)
    Namespace exemption guard (first check): types whose TypeDefinition.Namespace starts with
    "SharedKernel.Messaging" return true unconditionally.
    For all other types, iterates TypeDefinition.Methods where IsConstructor is true. For each
    constructor, checks each ParameterDefinition.ParameterType.Name against the set
    {"IBus", "IPublishEndpoint", "ISendEndpointProvider"} (exact name match, case-sensitive).
    Returns false (rule violated) on the first match, with failure message:
    "{offendingType} injects MassTransit transport type '{parameterTypeName}' directly. Use
    IMessageBus (for commands/queries) or IEventPublisher (for events) from
    SharedKernel.Messaging.Abstractions instead."
    Lives in Predicates/ folder. Used by MessagingArchitectureRules.NoDirectBusInjectionOutsideMessaging.

NoEventPublisherInDomainLayerPredicate  (class : ICustomRule — internal predicate)
    For each type, evaluates domain-layer membership via two signals:
      (a) TypeDefinition.Namespace.Contains(".Domain.") — namespace signal (dots are required
          to prevent matching "IDomainEventHandler" or other application types with "Domain"
          as a word fragment without a namespace boundary)
      (b) TypeDefinition.Interfaces contains an entry with InterfaceType.Name in
          {"IEntity", "IAggregateRoot", "IValueObject", "IDomainService"} — interface signal
    If neither signal is true, returns true (not in scope — not a domain-layer type).
    If either signal is true, iterates TypeDefinition.Methods where IsConstructor is true.
    For each constructor, checks ParameterDefinition.ParameterType.Name == "IEventPublisher"
    (exact name match).
    Returns false (rule violated) on the first match, with failure message:
    "{offendingType} injects IEventPublisher in the domain layer. Domain events are dispatched
    internally by IDomainEventDispatcher. Correct flow: domain event → IDomainEventDispatcher
    → application handler → IEventPublisher."
    Lives in Predicates/ folder. Used by MessagingArchitectureRules.NoEventPublisherInDomainLayer.

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
- SK0201 `TenantedDbContextOnModelCreatingAnalyzer` scans `MethodDeclarationSyntax` nodes named `OnModelCreating` with the `override` modifier. Ancestry check walks `ClassDeclarationSyntax.BaseList.Types` for a type whose simple name is `TenantedDbContext`; if not found on the immediate class, walks parent `ClassDeclarationSyntax` nodes in the same file (syntax-only — cross-file ancestry is not resolved). Body scan calls `DescendantNodes().OfType<InvocationExpressionSyntax>()` on the method body and checks for: (a) `MemberAccessExpressionSyntax` with `BaseExpressionSyntax` receiver and `Name.Identifier.Text == "OnModelCreating"`, or (b) any invocation (simple name or member access) whose method name is `"ApplyTenantFilters"`. Fires on the method identifier if neither found. No suppression namespace — suppress per-site via `#pragma warning disable SK0201`.
- SK0202 `IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer` scans `InvocationExpressionSyntax` nodes. Filter: simple method name (from `IdentifierNameSyntax` or `MemberAccessExpressionSyntax.Name`) is `"IgnoreQueryFilters"` AND argument list is empty (zero arguments). Two exemptions checked in order: (1) namespace walk via `SyntaxNode.Parent` for any `NamespaceDeclarationSyntax` or `FileScopedNamespaceDeclarationSyntax` whose `Name.ToString()` starts with `"SharedKernel.Persistence.EfCore"` — same pattern as SK0001/SK0007; (2) `FirstAncestorOrSelf<ClassDeclarationSyntax>()` with `Identifier.Text == "TenantedRepository"` (exact string match). Reports on the full invocation expression if neither exemption applies. Any additional exemption class or namespace must be documented in `00.Governance/CLAUDE.md` under SK0202 before applying suppression.
- RS2008 (analyzer release tracking) must be suppressed via `<NoWarn>$(NoWarn);RS2008</NoWarn>` in `SharedKernel.Analyzers.csproj`. The release tracking text-file approach does not reliably suppress it with `EnforceExtendedAnalyzerRules=true`.
- `NoDirectBusInjectionOutsideMessagingPredicate` exemption guard (`TypeDefinition.Namespace.StartsWith("SharedKernel.Messaging")`) is applied as the first check, before any constructor parameter inspection. This covers both `SharedKernel.Messaging.Abstractions` and `SharedKernel.Messaging.MassTransit` and all sub-namespaces with a single prefix check. Any additional namespace exemption must be documented in `00.Governance/CLAUDE.md` under `MessagingArchitectureRules` before applying.
- `NoEventPublisherInDomainLayerPredicate` namespace signal uses `Contains(".Domain.")` with dots on both sides to narrow the match to a full namespace segment — this prevents `IDomainEventHandler` or application types with "Domain" as a bare word in a namespace fragment from triggering the domain-layer scope check. The interface signal is the more reliable discriminator and should be preferred for SDK types that inherit from known domain base types.
- SK0703 `MessageBusSingletonRegistrationAnalyzer` extracts type argument names from `GenericNameSyntax.TypeArgumentList.Arguments`. For the form `AddSingleton<IMessageBus>()` the first type argument is an `IdentifierNameSyntax` — check `.Identifier.Text`. For the two-argument form `AddSingleton<IMessageBus, MassTransitMessageBus>()` only the first type argument is checked. Simple name prefix `"IMessageBus"` covers `IMessageBus` and any future `IMessageBus<T>` variant; prefix `"IEventPublisher"` covers `IEventPublisher` and any future variant. No SemanticModel required.
- SK0704 `HardcodedQueueUriAnalyzer` checks for both `ObjectCreationExpressionSyntax` (`new Uri(...)`) and `ImplicitObjectCreationExpressionSyntax` (`new(...)` where the type is inferred). The type name check for `Uri` is a simple name check on `ObjectCreationExpressionSyntax.Type` (`IdentifierNameSyntax.Identifier.Text == "Uri"` or `QualifiedNameSyntax` whose rightmost segment is `"Uri"`). For implicit creation, the containing argument context must be a `GetSendEndpoint` invocation — the type inference resolves to `Uri` by declaration context. The queue/exchange scheme check is case-insensitive (`StartsWith("queue:", StringComparison.OrdinalIgnoreCase)` and `StartsWith("exchange:", StringComparison.OrdinalIgnoreCase)`).
- `MessagingArchitectureRules` and both predicates (`NoDirectBusInjectionOutsideMessagingPredicate`, `NoEventPublisherInDomainLayerPredicate`) reuse the established Mono.Cecil `TypeDefinition.Methods` constructor-parameter inspection pattern from `NoInfrastructureConstructorParametersPredicate`. No new NuGet dependency — `Mono.Cecil >= 0.11.5` already referenced in `SharedKernel.ArchitectureTests`.
- SK0703 and SK0704 are the first SK analyzers in the 07xx messaging block. Both follow the same `netstandard2.0` constraint and `Microsoft.CodeAnalysis.CSharp 4.14.0` pin as all other SK analyzers. Neither requires a SemanticModel — both are syntax-only, keeping analysis cost minimal on large codebases.
- SK0705 `FaultConsumerDirectRegistrationAnalyzer` checks both `AddScoped` and `AddSingleton` method names for the `IFaultConsumer` type argument simple name. The check uses `GenericNameSyntax.Identifier.Text == "IFaultConsumer"` on each type argument — this catches both the one-argument form `AddScoped<IFaultConsumer<TMessage>>()` and the two-argument form `AddScoped<IFaultConsumer<TMessage>, TImpl>()`. The outer generic wrapper (`IFaultConsumer<TMessage>`) is itself a `GenericNameSyntax` whose identifier is `IFaultConsumer` — no unwrapping required.
- SK0706 `NoDirectSchedulerInjectionOutsideMessagingPredicate` uses a two-condition check: `ParameterType.Name == "IMessageScheduler"` AND `ParameterType.Namespace.StartsWith("MassTransit")`. Both conditions must be true to fire. The namespace check is what distinguishes the MassTransit transport scheduler from the platform abstraction. If `ParameterType.Namespace` is empty (type reference not fully resolved in the test assembly), fall back to `ParameterType.Scope.Name.Contains("MassTransit")` — the Mono.Cecil scope name for an externally-referenced type includes the assembly name, which contains `"MassTransit"` for MassTransit types.
- SK0707 `SagaStateMustExtendSagaStateBasePredicate` uses a fail-open policy for unresolvable base types. If `BaseType.Resolve()` returns null at any point in the walk, the predicate returns true (passes) for that type. This prevents false positives in test assemblies that do not load all transitive dependencies. Document this limitation in test fixtures: if a saga state type has a non-loadable base that is itself a `SagaStateBase` descendant, the rule will not catch the violation.
- SK0708 `BatchConsumerRegisteredViaAddConsumerAnalyzer` is a naming-convention-guided heuristic. It fires only when the type argument to `AddConsumer<T>()` contains `"BatchConsumer"` as a substring in its identifier text. The naming convention `{Purpose}BatchConsumer` (e.g., `OrderBatchConsumer`, `InvoiceLineBatchConsumer`) must be enforced across the codebase for this rule to provide complete coverage. Teams naming batch consumers without the `BatchConsumer` suffix will not receive SK0708 diagnostics — this is a documented false-negative limitation, not a bug.
- `ExtendedMessagingArchitectureRules` and both predicates (`NoDirectSchedulerInjectionOutsideMessagingPredicate`, `SagaStateMustExtendSagaStateBasePredicate`) reuse the established Mono.Cecil `TypeDefinition.Methods` constructor-parameter inspection and `TypeDefinition.Interfaces` scope patterns. No new NuGet dependency — `Mono.Cecil >= 0.11.5` already referenced in `SharedKernel.ArchitectureTests` covers both new predicates.
- SK0705 and SK0708 follow the same `netstandard2.0` constraint and `Microsoft.CodeAnalysis.CSharp 4.14.0` pin as SK0703/SK0704. Both are syntax-only analyzers — no SemanticModel required. SK0706 and SK0707 are NetArchTest ICustomRule predicates (assembly-level, post-compile), consistent with SK0701/SK0702.
- `SharedKernel.Analyzers.Tests.csproj` must explicitly reference `Microsoft.CodeAnalysis.CSharp` at the same version pinned in `SharedKernel.Analyzers.csproj` (currently 4.14.0). The `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` package pulls Roslyn 1.0.1 as a transitive dependency, causing a version conflict that breaks the build without this explicit override.
- Namespace suppression in analyzers uses `SyntaxNode.Parent` walk to find `NamespaceDeclarationSyntax` or `FileScopedNamespaceDeclarationSyntax` ancestors, checking `.Name.ToString().StartsWith("SharedKernel.Primitives")`. Do not use `SemanticModel` for this check — syntax-only is sufficient and cheaper.
- `RedisTopologyRules` introduces zero new SK diagnostic IDs and zero new `ICustomRule`/Mono.Cecil predicates. All five factory methods are pure NetArchTest `ConditionList`(`[]`) assembly-dependency checks via `.Should().NotHaveDependencyOn(...)`, matching the implementation style of `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching` — namespace-string prefix matching only.
- **NetArchTest `NotHaveDependencyOn(term)` matching contract (critical)**: `term` is compared via `StartsWith` against each scanned type's set of dependency *namespaces* (the declaring namespace of every type referenced from a type's members) — NOT assembly names, and with NO trailing dot on either side of the comparison. A trailing dot on `term` (e.g. `"SharedKernel.Caching.Redis.HashStore."`) will NEVER match because dependency-namespace strings never carry a trailing dot — this was a confirmed regression during WO-023 P-145 and must not be reintroduced. Additionally, a type's dependency-namespace set includes its OWN declaring namespace (self-reference) — checking a package against its own identifying namespace term is a guaranteed false positive across every type in that package.
- `RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages` checks five exact-namespace terms iteratively (one `.Should().NotHaveDependencyOn(term)` call per term: `"SharedKernel.Caching.Redis.Batch"`, `"SharedKernel.Caching.Redis.Extensions"`, `"SharedKernel.Caching.Redis.DistributedLocking"`, `"SharedKernel.Caching.Redis.HashStore"`, `"SharedKernel.Caching.Redis.PubSub"`), following the established iterative pattern from `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure`. None of these five terms is a prefix of `"SharedKernel.Caching.Redis.Core"` or `"SharedKernel.Caching.Redis.Core.Extensions"` (the assembly under test), so no self-collision occurs — this is why the L2 package's two sub-namespaces (`.Batch`, `.Extensions`) are used instead of the bare `"SharedKernel.Caching.Redis"` root.
- `RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther` accepts `params Assembly[]` and returns `ConditionList[]` (NOT a single `ConditionList`) — one element per input assembly, in order. Internally it resolves each scanned assembly's OWN identifying namespace term(s) via a `Dictionary<string,string[]>` keyed by assembly simple name (`"SharedKernel.Caching.Redis"` → its Batch/Extensions terms; `"SharedKernel.Caching.Redis.DistributedLocking"`/`.HashStore`/`.PubSub` → their own exact namespace), then builds the forbidden-term set as the UNION of the OTHER three packages' terms only — excluding the scanned assembly's own term(s) avoids the self-dependency false positive described above. `"SharedKernel.Caching.Redis.Core"` and `"SharedKernel.Caching.Abstractions"` are never part of any forbidden set (permitted dependencies per the exemption list). Callers (including test fixtures whose assembly simple name is not one of the four recognized packages) must call `.GetResult()` on EVERY element of the returned array.
- `RedisTopologyRules.PubSubNeverReferencesMessaging` and `RedisTopologyRules.MessagingNeverReferencesCaching` are directional converses of each other and must both be asserted — NetArchTest dependency checks are one-directional, so passing one does not imply the other passes. Both reuse the `"SharedKernel.Messaging"` / `"SharedKernel.Caching"` prefix-matching convention already established by `MessagingArchitectureRules` and `CachingAbstractionRules` respectively — these two terms have no trailing dot and are deliberately broad prefixes (they must match every sub-namespace of the respective capability).
- `RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies` is a re-verification, not a new guarantee — it confirms the existing zero-dependency contract on `SharedKernel.Caching.Abstractions` still holds across the five-package Redis topology (`Redis.Core`, `Redis`, `Redis.DistributedLocking`, `Redis.HashStore`, `Redis.PubSub`) plus `StackExchange.Redis`, EF Core, and MassTransit. The term `"SharedKernel.Caching.Redis"` is used here WITHOUT a trailing dot deliberately — `SharedKernel.Caching.Abstractions` has zero dependencies, so the bare-prefix term cannot self-collide, and the prefix form is required to catch all five Redis sub-packages in one term. Same shape as `SharedKernelLayeringRules.CoreReferencesNothing`.
- `RedisTopologyRules` lives in `SharedKernel.ArchitectureTests/Rules/` alongside `CachingAbstractionRules.cs`. It introduces no new NuGet dependency — `NetArchTest.eNt >= 1.3.2` (existing pin) covers the entire phase. No new files in `Predicates/`; no `ICustomRule` is required.
- In-memory fixture assemblies compiled via `CSharpCompilation`/`Assembly.LoadFrom` for `RedisTopologyRulesTests` MUST use assembly names that do not collide with real `ProjectReference`d assemblies already loaded in the test `AssemblyLoadContext` (e.g., name a `SharedKernel.Caching.Redis.HashStore`-shaped fixture `"Fixture.<Scenario>.SharedKernel.Caching.Redis.HashStore"`, not `"SharedKernel.Caching.Redis.HashStore"`). `Assembly.LoadFrom(path)` for a simple name matching an already-loaded assembly returns the ALREADY-LOADED real assembly, not the fixture, causing `CS0234`/missing-type failures. Cross-fixture `MetadataReference`s must be built via `MetadataReference.CreateFromImage(ImmutableArray<byte>)` from the in-memory emitted bytes, not `CreateFromFile(Assembly.Location)`.

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
- [2026-06-05] SK0201 TenantedDbContextOnModelCreatingGuard and SK0202 IgnoreQueryFiltersOutsideTenantedRepository added to diagnostic registry (new 02xx multi-tenancy block); both implemented as Roslyn analyzers (syntax-only, no semantic model); SK0201 body-scan checks base.OnModelCreating and ApplyTenantFilters via InvocationExpressionSyntax descendants; SK0202 namespace-walk and class-name exemption follow the SK0001/SK0007 SyntaxNode.Parent pattern; two new implementation rules added — WO-020 SK.00.TenantedDbContextGuard
- [2026-06-08] SK0703 MessageBusSingletonRegistration and SK0704 HardcodedQueueUriInGetSendEndpoint added to diagnostic registry (new 07xx messaging-domain block; both Roslyn syntax-only analyzers); SK0701 NoDirectBusInjectionOutsideMessaging and SK0702 NoEventPublisherInDomainLayer added as NetArchTest predicates (MessagingArchitectureRules static class; NoDirectBusInjectionOutsideMessagingPredicate and NoEventPublisherInDomainLayerPredicate ICustomRule predicates documented); eleven new implementation rules added — WO-020 P-123
- [2026-06-09] SK.00.MessagingArchRules → ● — all 20 tasks complete; 78 analyzer + 62 arch tests passing; no new brain content (state-map-phase)
- [2026-06-09] SK0705 FaultConsumerDirectRegistration, SK0706 DirectMassTransitSchedulerInjection, SK0707 SagaStateMustExtendSagaStateBase, SK0708 BatchConsumerRegisteredViaAddConsumer added to diagnostic registry (07xx messaging block extended); ExtendedMessagingArchitectureRules static class added to architecture test contracts (NoDirectMassTransitSchedulerInjection, SagaStatesMustExtendSagaStateBase predicates); NoDirectSchedulerInjectionOutsideMessagingPredicate and SagaStateMustExtendSagaStateBasePredicate ICustomRule predicates documented; six new implementation rules added — WO-021 P-133
- [2026-06-12] RedisTopologyRules static class added to architecture test contracts (five predicates: RedisCoreNeverReferencesCapabilityPackages, CapabilityPackagesNeverReferenceEachOther, PubSubNeverReferencesMessaging, MessagingNeverReferencesCaching, CachingAbstractionsHasNoInfrastructureDependencies) enforcing the five-package Redis topology (Redis.Core, Redis, Redis.DistributedLocking, Redis.HashStore, Redis.PubSub) from P-140–P-144 and re-affirming the 02.Caching <-> 07.Messaging exclusion boundary; no new SK IDs, no Mono.Cecil — pure NetArchTest assembly-dependency checks; six new implementation rules added — WO-023 P-145
- [2026-06-15] RedisTopologyRules fixed and finalized — 9/9 RedisTopologyRulesTests pass (75/75 full ArchitectureTests.Tests suite, 0 build warnings/errors). Corrected the NotHaveDependencyOn matching contract (no trailing dots; namespace-based StartsWith match) across all five predicates. CapabilityPackagesNeverReferenceEachOther redesigned to return ConditionList[] (one per input assembly) using a per-package own-namespace-term dictionary to eliminate the self-dependency false positive. Documented the fixture-assembly-naming and CreateFromImage patterns for in-memory NetArchTest fixtures; seven implementation rules revised/added — WO-023 SK.00.RedisTopology closeout
