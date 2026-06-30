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
    Trigger   : A type in a `03.Domain` or `05.Application` assembly (the assemblies the
                caller passes to `EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication`)
                references `System.Security.Cryptography.AesGcm`, `System.Security.Cryptography.Aes`,
                or `System.Security.Cryptography.SymmetricAlgorithm` — detected via IL
                instruction walk (Call/Callvirt/Newobj opcodes) and field type inspection
    Exempt    : Types whose namespace starts with `SharedKernel.Cryptography` — the sole
                legitimate crypto consumer in the platform (narrowed in WO-037 P-229 from
                the original two-namespace exemption `SharedKernel.Persistence.*` /
                `SharedKernel.Security.*`; both layers now route through
                `SharedKernel.Cryptography`'s `ISymmetricEncryptionService`/`AesGcmEncryptionService`
                instead of touching BCL cipher types directly — see SK0301-GEN below)
    Fix       : Remove direct cipher usage from domain/application code. Route all
                field-level encryption through the persistence-layer `EncryptedValueConverter<T>`
                wired via `PropertyBuilder<T>.Encrypt()` in `IEntityTypeConfiguration<T>`,
                which itself delegates to `SharedKernel.Cryptography.ISymmetricEncryptionService`.
    Note      : Implemented as a NetArchTest `ICustomRule`
                (`NoAesCipherInDomainOrApplicationPredicate`) — not a per-call-site
                Roslyn analyzer. Enforced at assembly level (post-compile). Introduced
                in WO-019 P-114. Part of the 03xx encryption-domain ID block. Exemption
                list narrowed in WO-037 P-229 — see "SK0301 reconciliation" note under
                `CryptoIsolationRules` in Architecture Test Contracts below. This rule
                remains scoped to whichever assemblies the caller passes (in practice
                `03.Domain`/`05.Application`); `CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography`
                is the platform-wide generalization of the same trigger/exemption logic,
                intended to be invoked against every production assembly. No new SK ID was
                minted for the platform-wide rule — same diagnostic intent, wider caller-
                supplied scope, identical exemption namespace.

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

SK0012  MakeGenericMethodReflection
    Category  : Design
    Severity  : Warning
    Trigger   : A method body contains a Call or Callvirt IL opcode whose
                MethodReference.Name == "MakeGenericMethod" (exact match) and the
                type+method is not registered in ReflectionExemptionRegistry
    Scope     : Platform-wide across all numbered domains; enforced at assembly level
                (post-compile) via NoMakeGenericMethodReflectionPredicate (ICustomRule)
    Fix       : Replace GetMethod(...).MakeGenericMethod(...).Invoke(...) with typed
                dispatch or expression trees. The expression-tree pattern
                (Expression.Call + Expression.Lambda.Compile()) used in 06.Persistence
                TenantedDbContext is the platform gold standard.
    Exception : Register the type full name and method name in ReflectionExemptionRegistry
                with a written XML doc comment stating the governance rationale, the
                approving work order, and the approval date. No other suppression
                mechanism (#pragma, [SuppressMessage]) is accepted for this rule.
    Motivating incident: P-147 (WO-024) — EncryptionRotationService.LoadBatchAsync
                in 06.Persistence shipped GetMethod("LoadBatchAsync")
                .MakeGenericMethod(entityType).Invoke(...) while the same package's
                CLAUDE.md documented expression trees as the gold standard. Documentation
                alone did not prevent the violation from shipping; this rule makes it
                mechanically impossible without explicit governance review.
    Note      : Implemented as a NetArchTest ICustomRule
                (NoMakeGenericMethodReflectionPredicate). No Roslyn analyzer —
                the runtime MethodInfo.MakeGenericMethod call is not detectable
                as a simple syntax pattern; IL inspection is required. Introduced
                in WO-024 P-153. Severity escalation to Error is gated on
                confirmation that ReflectionExemptionRegistry produces zero false
                positives across all platform assemblies.

SK0013  RawHttpClientConstructorInjection
    Category  : Usage
    Severity  : Warning
    Trigger   : A ConstructorDeclarationSyntax parameter type is a SimpleNameSyntax or
                IdentifierNameSyntax whose Identifier.Text == "HttpClient" (exact match),
                and neither of the following exemptions applies:
                  (a) Namespace exemption: any ancestor NamespaceDeclarationSyntax or
                      FileScopedNamespaceDeclarationSyntax whose Name.ToString() starts
                      with "SharedKernel.Communication.Rest" (SyntaxNode.Parent walk,
                      same pattern as SK0001/SK0007/SK0202)
                  (b) Base-class exemption: the enclosing ClassDeclarationSyntax has a
                      BaseList containing a type whose simple name is "DelegatingHandler"
                      (exact match on the rightmost SimpleNameSyntax/IdentifierNameSyntax
                      in the base type entry)
                No SemanticModel required — syntax-only check.
    Fix       : Inject the named typed-client interface (TClient) via
                IHttpClientFactory-managed AddRestClient<TClient>() instead of accepting
                HttpClient directly. Direct HttpClient injection bypasses connection
                pooling, DNS refresh cycles, and handler lifetime management, which are
                production reliability concerns in .NET microservices.
    Suppress  : Per-constructor via #pragma warning disable SK0013 when a raw HttpClient
                is genuinely required (e.g., a unit-test helper); document the rationale
                inline.
    Note      : Introduced in WO-025 P-159. Exemption (a) covers all types inside the
                SharedKernel.Communication.Rest package itself (which legitimately manages
                HttpClient internally). Exemption (b) covers DelegatingHandler subclasses
                that receive the inner handler HttpClient as part of the delegating chain
                and must not be widened without a governance review and update to this entry.

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
        Exemption: types whose TypeDefinition.Namespace starts with "SharedKernel.Cryptography"
        pass unconditionally — this is the platform's sole legitimate crypto consumer
        (narrowed in WO-037 P-229 from the original two-namespace exemption
        "SharedKernel.Persistence.*" / "SharedKernel.Security.*" — both layers now route
        through SharedKernel.Cryptography's ISymmetricEncryptionService/AesGcmEncryptionService
        rather than touching BCL cipher types directly; see CryptoIsolationRules below for the
        platform-wide generalization of this same check). Failure message names the offending
        type and the cipher type referenced.
        Rationale: cipher usage in 03.Domain or 05.Application destroys layering isolation
        and bypasses the platform-managed AES-256-GCM key rotation lifecycle. All
        field-level encryption must route through EncryptedValueConverter<T>, which itself
        delegates to SharedKernel.Cryptography.
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
    with "SharedKernel.Cryptography" return true unconditionally. (Narrowed in WO-037 P-229
    from the original two-namespace exemption "SharedKernel.Persistence" / "SharedKernel.Security"
    — both layers route through SharedKernel.Cryptography now; see CryptoIsolationRules below.)
    For all other types, checks two surfaces for System.Security.Cryptography cipher types:
      (1) TypeDefinition.Fields — checks FieldDefinition.FieldType.Namespace ==
          "System.Security.Cryptography" and FieldDefinition.FieldType.Name in
          {"AesGcm", "Aes", "SymmetricAlgorithm"}
      (2) TypeDefinition.Methods.Body.Instructions — for Call, Callvirt, and Newobj opcodes,
          checks the resolved TypeReference.Namespace and TypeReference.Name against the
          same set.
    Returns false (rule violated) on the first match, with failure message naming the
    offending type and the cipher type name. Lives in Predicates/ folder. Used by
    EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication. This predicate remains
    scoped to whichever assemblies the caller passes (in practice 03.Domain/05.Application) —
    it is the narrower, SK0301-backing special case of the platform-wide
    NoRawSymmetricCipherOutsideCryptographyPredicate (see below), which shares the same
    exemption namespace and trigger logic but is intended to be invoked against every
    production assembly.

CryptoIsolationRules  (static class — platform-wide raw-cipher isolation predicate; WO-037 P-229)
    .NoRawSymmetricCipherOutsideCryptography(params Assembly[])  → ConditionList
        Asserts that no type in the supplied assemblies references
        System.Security.Cryptography.AesGcm, System.Security.Cryptography.Aes, or
        System.Security.Cryptography.SymmetricAlgorithm directly, AND that no type calls a
        System.Security.Cryptography.RandomNumberGenerator member (ciphertext/nonce
        generation). Uses NoRawSymmetricCipherOutsideCryptographyPredicate (ICustomRule —
        see below).
        Exemption: types whose TypeDefinition.Namespace starts with "SharedKernel.Cryptography"
        pass unconditionally — the sole legitimate caller platform-wide once 06.Persistence
        P-227 lands (EncryptedValueConverter delegates to
        SharedKernel.Cryptography.ISymmetricEncryptionService/AesGcmEncryptionService instead
        of constructing AesGcm directly). No SharedKernel.Security.* exemption is carried
        forward: 12.Security.Oidc's JWT signing path must consume
        SharedKernel.Cryptography.IHmacSigner/IAsymmetricSignatureService rather than
        referencing BCL HMAC/asymmetric cipher types directly — if 12.Security.Oidc is found
        to reference AesGcm/Aes/SymmetricAlgorithm/RandomNumberGenerator directly when this
        rule is wired against its real assembly, that is a genuine violation this rule is
        designed to catch, not a false positive to suppress.
        Failure message names the offending type and the cipher/RNG member referenced.
        Rationale: motivated by the 06.Persistence P-227 incident — a hand-rolled AesGcm
        usage shipped inside a namespace ("SharedKernel.Persistence.*") that the
        SK0301-backing predicate exempted wholesale, because that predicate was only ever
        invoked by the caller against 03.Domain/05.Application assemblies, never against
        06.Persistence itself. This rule closes that caller-scoping gap by being designed
        for platform-wide invocation: the consuming test suite is expected to pass every
        production assembly in the solution, not just the two layers most likely to violate
        it historically.
        Offending pattern: class SomeInfraHelper { private AesGcm _cipher = new(key); } in
            ANY package outside SharedKernel.Cryptography, including SharedKernel.Persistence.*
        Compliant pattern: inject SharedKernel.Cryptography.ISymmetricEncryptionService;
            never reference AesGcm/Aes/SymmetricAlgorithm/RandomNumberGenerator directly
            outside SharedKernel.Cryptography itself.

    Relationship to SK0301: SK0301 (DirectCryptoInDomainOrApplication, backed by
    NoAesCipherInDomainOrApplicationPredicate) and NoRawSymmetricCipherOutsideCryptography
    share IDENTICAL exemption logic (SharedKernel.Cryptography only) and near-identical
    trigger logic (the RandomNumberGenerator surface is new in the platform-wide rule) —
    they differ only in which assemblies the consuming test suite passes. SK0301 remains a
    narrower, caller-scoped special case by construction, not a contradictory duplicate. No
    new SK diagnostic ID was assigned for the platform-wide rule.

    Note: Introduced in WO-037 P-229. Lives in SharedKernel.ArchitectureTests/Rules/CryptoIsolationRules.cs.
    Reuses the existing Mono.Cecil >= 0.11.5 reference — no new NuGet dependency.

NoRawSymmetricCipherOutsideCryptographyPredicate  (class : ICustomRule — internal predicate)
    Namespace exemption guard (first check): types whose TypeDefinition.Namespace starts
    with "SharedKernel.Cryptography" return true unconditionally.
    For all other types, checks three surfaces:
      (1) TypeDefinition.Fields — checks FieldDefinition.FieldType.Namespace ==
          "System.Security.Cryptography" and FieldDefinition.FieldType.Name in
          {"AesGcm", "Aes", "SymmetricAlgorithm"} (reused from NoAesCipherInDomainOrApplicationPredicate)
      (2) TypeDefinition.Methods.Body.Instructions — for Call, Callvirt, and Newobj opcodes,
          checks the resolved TypeReference.Namespace and TypeReference.Name against the
          same cipher-type set.
      (3) TypeDefinition.Methods.Body.Instructions — for Call and Callvirt opcodes, checks
          MethodReference.DeclaringType.FullName == "System.Security.Cryptography.RandomNumberGenerator"
          (DeclaringType match rather than a single method-name match, since
          RandomNumberGenerator exposes multiple static/instance entry points: Fill,
          GetBytes, Create, etc. — a DeclaringType check catches all of them in one pass).
    Returns false (rule violated) on the first match across any of the three surfaces, with
    failure message naming the offending type and the cipher/RNG type name. Lives in
    Predicates/ folder. Used by CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography.

UnitOfWorkSeamRules  (static class — local-seam interface distinctness guard; WO-037 P-229)
    .UnitOfWorkInterfacesRemainDistinct(Assembly applicationBehaviorsAssembly, Assembly persistenceAbstractionsAssembly)
                                            → ConditionList
        Asserts that SharedKernel.Application.Behaviors.IUnitOfWork and
        SharedKernel.Persistence.Abstractions.IUnitOfWork remain two distinct interface
        declarations — never merged into a single type, never one inheriting the other. Uses
        UnitOfWorkInterfacesRemainDistinctPredicate (ICustomRule — see below). Takes TWO
        Assembly parameters (not params Assembly[]) — one expected to contain each interface
        by exact full name.
        Failure message names which check failed: missing type, identity collapse
        (ReferenceEquals match after resolution), or base-interface-list cross-reference in
        either direction.
        Rationale: the local-seam pattern (05.Application declares its own IUnitOfWork,
        bridged to 06.Persistence's IUnitOfWork at the composition root — the same pattern
        already proven for IAuthorizationContext and IIdempotencyKeyStore) only holds if the
        two interfaces stay genuinely independent. A future "simplification" that merges them
        or makes one inherit the other would silently reintroduce the 05.Application →
        06.Persistence layering violation the local-seam pattern exists to prevent. This is a
        negative-space / regression-guard rule — both interfaces are independently declared
        today (the desired state), so the fire-path test fixture must be a CONTRIVED pair of
        assemblies proving the predicate would catch a future merge attempt, mirroring the
        established technique for negative-space rules in this domain (e.g.
        RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther,
        CommunicationLayeringRules.GrpcNeverReferencesContracts).
        Offending pattern: interface IUnitOfWork : SharedKernel.Persistence.Abstractions.IUnitOfWork
            declared inside SharedKernel.Application.Behaviors (or the reverse direction)
        Compliant pattern: two independently-declared IUnitOfWork interfaces, bridged only by
            a concrete adapter (e.g. EfUnitOfWork implementing both) at the composition root —
            never by interface inheritance between the two abstractions themselves.

    Note: Introduced in WO-037 P-229. No new SK diagnostic ID — pure structural ICustomRule
    check, following the RedisTopologyRules/CompositionRootExclusivityRules/
    PresentationLayeringRules precedent of SK-less rules for boundary/shape prohibitions.
    Lives in SharedKernel.ArchitectureTests/Rules/UnitOfWorkSeamRules.cs. Reuses the existing
    Mono.Cecil >= 0.11.5 reference — no new NuGet dependency.

UnitOfWorkInterfacesRemainDistinctPredicate  (class : ICustomRule — internal predicate)
    Resolves both TypeDefinitions by exact full name:
      "SharedKernel.Application.Behaviors.IUnitOfWork" in applicationBehaviorsAssembly
      "SharedKernel.Persistence.Abstractions.IUnitOfWork" in persistenceAbstractionsAssembly
    Three independent checks, each a distinct failure mode:
      (1) Existence: both types must be found. If either is missing, returns false — a
          renamed or removed interface is itself a seam-pattern violation requiring
          governance review, not a silent pass.
      (2) Identity collapse: the two resolved TypeDefinitions must not be
          ReferenceEquals-identical after resolution — catches an accidental
          type-forwarding/alias merge collapsing both names onto one type.
      (3) Bidirectional base-interface check: neither TypeDefinition's Interfaces collection
          may contain an entry whose InterfaceType.FullName equals the other's full name —
          catches "interface IUnitOfWork : {other}.IUnitOfWork" being introduced on either
          side.
    Returns false (rule violated) on the first failing check, with failure message naming
    which check failed and the two full type names involved. Lives in Predicates/ folder.
    Used by UnitOfWorkSeamRules.UnitOfWorkInterfacesRemainDistinct. Reuses the
    TypeDefinition.Interfaces enumeration pattern already used by
    DoesNotImplementOpenGenericInterfacePredicate — no new technique, no new NuGet dependency.

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

ReflectionGuardRules  (static class — platform-wide reflection prohibition enforcement; WO-024 P-153)
    .NoMakeGenericMethodReflection(Assembly assembly)  → ConditionList
        Asserts that no method body in the supplied assembly contains a Call or Callvirt
        IL opcode whose MethodReference.Name == "MakeGenericMethod" (exact name match),
        unless the type+method combination is registered in ReflectionExemptionRegistry.
        Uses NoMakeGenericMethodReflectionPredicate (ICustomRule — see below).
        Scope: all non-abstract types in the supplied assembly. Factory method accepts a
        single Assembly; consumers call it once per production assembly under test.
        Exemption: entries in ReflectionExemptionRegistry.IsExempt(typeFullName, methodName)
        are returned as passing unconditionally — the allow-list is the sole exception
        mechanism; no per-call-site suppression is accepted.
        Failure message: "{TypeDefinition.FullName}.{method.Name} calls MakeGenericMethod.
        Use typed dispatch or expression trees instead. If this is a genuinely justified
        exception, register the type and method in ReflectionExemptionRegistry with a
        written governance rationale."
        Rationale: GetMethod(...).MakeGenericMethod(...).Invoke(...) bypasses compile-time
        type safety, creates invisible coupling between the caller and the generic method's
        signature, and makes refactoring (rename, parameter changes) silently break at
        runtime. The expression-tree dispatch pattern (Expression.Call + Lambda.Compile)
        used in TenantedDbContext is the platform gold standard and adds negligible
        overhead. Motivating incident: P-147 (WO-024) — EncryptionRotationService
        .LoadBatchAsync shipped this exact pattern while the same package's CLAUDE.md
        documented expression trees as the gold standard. Documentation alone did not
        prevent it.

    Note: All production assemblies in the platform pass this rule after P-147 eliminated
    the only known violation (EncryptionRotationService). The ReflectionExemptionRegistry
    ships empty. Introduced in WO-024 P-153.

NoMakeGenericMethodReflectionPredicate  (class : ICustomRule — internal predicate)
    For each type (non-abstract types only — abstract filter applied at factory level):
      1. Iterates TypeDefinition.Methods for each MethodDefinition with a non-null Body.
      2. For each MethodDefinition.Body.Instructions, checks for Instruction where
         OpCode is Call or Callvirt AND MethodReference.Name == "MakeGenericMethod"
         (exact name match, case-sensitive).
      3. If found, calls ReflectionExemptionRegistry.IsExempt(
             TypeDefinition.FullName, MethodDefinition.Name)
         before returning false.
      4. Returns false (rule violated) only when the MakeGenericMethod call is found AND
         IsExempt returns false. Failure message includes the declaring type full name and
         method name. Returns true (passes) if no MakeGenericMethod call exists or if the
         type+method is in the allow-list.
    Uses the established Mono.Cecil TypeDefinition IL-walk pattern from
    DoesNotContainThrowIlPredicate. No new NuGet dependency — Mono.Cecil >= 0.11.5
    already referenced in SharedKernel.ArchitectureTests.
    Lives in Predicates/ folder. Used by ReflectionGuardRules.NoMakeGenericMethodReflection.

ReflectionExemptionRegistry  (class — governance allow-list for SK0012 exceptions)
    Static class in SharedKernel.ArchitectureTests/ReflectionExemptionRegistry.cs.
    Exposes IsExempt(string typeFullName, string methodName) → bool.
    Internally maintains a HashSet<(string, string)> of approved (typeFullName, methodName)
    pairs. Each registered entry MUST carry an XML <remarks> doc comment stating:
      — the governance rationale (why typed dispatch or expression trees cannot be used)
      — the approving work order and date
      — the reviewing team member
    Ships empty — no exemptions are pre-populated. The fixed P-147 EncryptionRotationService
    uses expression trees and needs no exemption. Any team requesting an exemption must:
      1. Open a governance review in the root state-map with a written rationale.
      2. Add the (typeFullName, methodName) pair to this registry with the required XML docs.
      3. Reference the work order in both the XML doc and the exemption registration.
    No other suppression mechanism is accepted: #pragma warning disable SK0012,
    [SuppressMessage], or inline comments do not exempt a type from this rule.

CommunicationLayeringRules  (static class — Communication layer boundary enforcement predicates; WO-025 P-159)
    All factory methods accept Assembly (or params Assembly[]) and return ConditionList (or ConditionList[]).

    .CommunicationPackagesNeverReferencesForbiddenLayers(Assembly communicationAssembly) → ConditionList[]
        Asserts that no type in the supplied 11.Communication.* assembly has a dependency
        on any of the following forbidden namespace terms:
          "SharedKernel.Caching"    (covers all 02.Caching.* sub-packages via prefix)
          "SharedKernel.Application" (covers 05.Application)
          "SharedKernel.Persistence" (covers all 06.Persistence.* sub-packages via prefix)
          "SharedKernel.Messaging"   (covers all 07.Messaging.* sub-packages via prefix)
        Returns one ConditionList per forbidden term (four total), following the same
        iterative pattern as DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure.
        Caller must assert .GetResult().IsSuccessful on EACH element of the returned array.
        The caller supplies one of the four 11.Communication.* assemblies; the method is
        called once per communication assembly under test.
        Rationale: the root CLAUDE.md layering table permits 11.Communication.* to reference
        only 01.Core, 04.Contracts, and 12.Security abstractions. Any reference to caching,
        application, persistence, or messaging infrastructure from a communication package
        collapses the communication abstraction and makes protocol adapters impossible to
        unit-test independently of infrastructure concerns.
        Offending pattern: SharedKernel.Communication.Rest referencing IDistributedCache
            (from 02.Caching) to cache response payloads
        Compliant pattern: SharedKernel.Communication.Rest referencing only SharedKernel.Core
            types for primitive extensions and SharedKernel.Contracts for envelope types

    .CommunicationInternalNeverReferencesOtherCommunicationPackages(Assembly internalAssembly) → ConditionList[]
        Asserts that SharedKernel.Communication.Internal has no dependency on any of:
          "SharedKernel.Communication.Rest"
          "SharedKernel.Communication.Grpc"
          "SharedKernel.Communication.GraphQL"
        Returns one ConditionList per forbidden term (three total). Caller must assert each.
        Caller must pass only the SharedKernel.Communication.Internal assembly.
        Rationale: Communication.Internal is the service-discovery foundation. Dependency
        flow goes INTO Internal from the protocol packages (Rest, Grpc inject
        IServiceEndpointResolver from Internal) — Internal must never reach out to its
        consumers. A circular dependency would make Internal impossible to test in isolation
        and would entangle the service-discovery abstraction with protocol-specific concerns.
        Offending pattern: SharedKernel.Communication.Internal referencing
            SharedKernel.Communication.Rest to construct typed REST endpoints
        Compliant pattern: SharedKernel.Communication.Internal exposing only
            IServiceEndpointResolver; Rest/Grpc/GraphQL inject it

    .NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc(Assembly assembly) → ConditionList
        Asserts that no type in the supplied assembly inherits from
        Grpc.Core.Interceptors.Interceptor directly. Uses
        NoDirectGrpcInterceptorInheritancePredicate (ICustomRule — see below).
        Exemption: types whose TypeDefinition.Namespace starts with
        "SharedKernel.Communication.Grpc" pass unconditionally — the Communication.Grpc
        package is the sole legitimate host for gRPC interceptor implementations.
        Failure message: "{TypeDefinition.FullName} inherits from
        Grpc.Core.Interceptors.Interceptor directly. gRPC interceptor implementations
        must live in SharedKernel.Communication.Grpc — never in application or domain
        assemblies."
        Rationale: gRPC interceptors that inject request-scoped services (IUserContext,
        ITenantProvider, OTel tracer) must be registered once via the platform's
        AddSharedKernelGrpcCommunication() builder. Ad-hoc interceptor classes scattered
        across service assemblies bypass the platform registration, produce duplicate
        tracing spans, and cannot be unit-tested without a full gRPC channel. Keeping
        interceptor implementations inside Communication.Grpc is the single point of
        control.
        Note: the caller should pass any production assembly that is NOT
        SharedKernel.Communication.Grpc. Passing Communication.Grpc itself is not useful
        since the namespace exemption passes all its types unconditionally.

    .NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL(Assembly assembly) → ConditionList
        Asserts that no type in the supplied assembly inherits from
        HotChocolate FilterInputType (any generic or non-generic form) or
        SortInputType (any generic or non-generic form) without having FilterBase or
        SortBase from SharedKernel.Communication.GraphQL in its BaseType chain first.
        Uses NoDirectHotChocolateFilterSortInheritancePredicate (ICustomRule — see below).
        Exemption: types whose TypeDefinition.Namespace starts with
        "SharedKernel.Communication.GraphQL" pass unconditionally — the GraphQL package
        itself defines FilterBase<T> and SortBase<T> as the platform wrappers and
        legitimately inherits from FilterInputType<T> / SortInputType<T> to do so.
        Failure message: "{TypeDefinition.FullName} inherits from
        {FilterInputType/SortInputType} directly. Use FilterBase<T> or SortBase<T>
        from SharedKernel.Communication.GraphQL to apply platform naming and exposure
        conventions."
        Rationale: FilterInputType<T> and SortInputType<T> expose the full entity field
        surface to GraphQL clients by default, violating field-level access control and
        the snake_case naming convention enforced by the platform. FilterBase<T> and
        SortBase<T> are thin wrappers that apply the platform conventions (naming, allowed
        field subset, pagination shape) automatically. Inheriting directly bypasses this
        and risks over-exposing sensitive fields (e.g., internal flags, encrypted columns).
        Offending pattern: class OrderFilterType : FilterInputType<Order> { ... }
        Compliant pattern: class OrderFilterType : FilterBase<Order> { ... }

    .GrpcNeverReferencesContracts(Assembly grpcAssembly) → ConditionList
        Asserts that no type in SharedKernel.Communication.Grpc has any dependency on the
        SharedKernel.Contracts namespace. Single Types.InAssembly(grpcAssembly).Should()
        .NotHaveDependencyOn("SharedKernel.Contracts") call.
        Rationale: SharedKernel.Communication.Grpc is a protocol adapter. The cross-service
        DTO layer (SharedKernel.Contracts — PagedList, Envelope, integration event payloads)
        must not flow into gRPC transport code; the dependency was introduced as a dead import
        in P-163 and was removed in the same PR. This rule mechanically prevents re-introduction.
        Failure message: "SharedKernel.Communication.Grpc has a dependency on
        SharedKernel.Contracts. The gRPC package is a protocol adapter — cross-service DTO
        types must not flow into gRPC transport code (WO-026 P-163 rationale)."
        No exemption is permitted for this rule. Any future case where Grpc genuinely needs a
        Contracts type requires a governance review and an explicit revision of this entry before
        any exemption can be applied.
        Note: Introduced in WO-026 P-167. Added to the existing CommunicationLayeringRules
        static class alongside the four predicates from P-159.

    Permitted exemption list:
        - SharedKernel.Communication.Grpc namespace — for NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc
        - SharedKernel.Communication.GraphQL namespace — for NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL
        Any additional exemption must be documented here before it is applied in code.
        GrpcNeverReferencesContracts carries NO permitted exemptions — see rule note above.

NoDirectGrpcInterceptorInheritancePredicate  (class : ICustomRule — internal predicate)
    Namespace exemption guard (first check): types whose TypeDefinition.Namespace starts
    with "SharedKernel.Communication.Grpc" return true unconditionally.
    For all other types, walks the TypeDefinition.BaseType chain iteratively:
      - At each step, checks TypeReference.Name == "Interceptor" (exact simple name match)
        AND TypeReference.Namespace contains "Grpc.Core.Interceptors" (substring match)
        to distinguish from any other "Interceptor"-named type in other namespaces
      - Advances by calling BaseType.Resolve() to get the next TypeDefinition
      - Terminates when BaseType is null or BaseType.Name is "Object"
      - Fail-open: if Resolve() returns null at any step (unloaded assembly dependency),
        returns true unconditionally — avoids false positives in test setups that do not
        load all transitive gRPC dependencies
    Returns false (rule violated) if the chain finds a match, with failure message naming
    the offending type full name. Lives in Predicates/ folder. Used by
    CommunicationLayeringRules.NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc.
    Reuses the TypeDefinition.BaseType chain-walk pattern from
    SagaStateMustExtendSagaStateBasePredicate. No new NuGet dependency.

NoDirectHotChocolateFilterSortInheritancePredicate  (class : ICustomRule — internal predicate)
    Namespace exemption guard (first check): types whose TypeDefinition.Namespace starts
    with "SharedKernel.Communication.GraphQL" return true unconditionally.
    For all other types, walks the TypeDefinition.BaseType chain iteratively:
      - At each step, checks TypeReference.Name against two target sets:
          Forbidden set: TypeReference.Name.StartsWith("FilterInputType") OR
                         TypeReference.Name.StartsWith("SortInputType")
          Platform-wrapper set: TypeReference.Name.StartsWith("FilterBase") OR
                                 TypeReference.Name.StartsWith("SortBase")
        (StartsWith is used instead of exact match to handle generic type IL names
         such as "FilterInputType`1", "FilterBase`1", etc.)
      - If a platform-wrapper type (FilterBase/SortBase) is encountered BEFORE a
        forbidden type, the type is compliant — returns true
      - If a forbidden type (FilterInputType/SortInputType) is encountered WITHOUT
        a preceding FilterBase/SortBase in the chain, returns false (rule violated)
      - Advances by calling BaseType.Resolve() to get the next TypeDefinition
      - Terminates when BaseType is null or BaseType.Name is "Object"
      - Fail-open: if Resolve() returns null at any step, returns true unconditionally
    Returns false (rule violated) if a forbidden base is reached before a platform
    wrapper, with failure message naming the offending type and the direct HotChocolate
    base class name. Lives in Predicates/ folder. Used by
    CommunicationLayeringRules.NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL.
    Reuses the TypeDefinition.BaseType chain-walk pattern from
    SagaStateMustExtendSagaStateBasePredicate. No new NuGet dependency.

PresentationLayeringRules  (static class — 14.Presentation Result/HTTP boundary-mapping enforcement predicates; WO-031 P-199)
    All factory methods accept Assembly (or params Assembly[]) and return ConditionList.

    .NoDirectProblemDetailsConstructionOutsideWebApi(params Assembly[] assemblies) → ConditionList
        Asserts that no type in the supplied assemblies directly instantiates
        Microsoft.AspNetCore.Mvc.ProblemDetails or Microsoft.AspNetCore.Http.HttpValidationProblemDetails
        via a newobj IL opcode. Uses NoDirectProblemDetailsConstructionPredicate (ICustomRule — see
        below). The caller supplies every assembly to be checked EXCEPT
        SharedKernel.Presentation.WebApi itself — there is no internal namespace exemption inside
        the predicate; exclusion is achieved by the caller never passing that assembly, mirroring
        the calling convention of MessagingArchitectureRules.NoDirectBusInjectionOutsideMessaging's
        sibling rules where the in-package exemption is also available, except here it is
        caller-controlled rather than predicate-internal because there is no single discriminating
        namespace prefix shared by every legitimate construction site inside the WebApi package
        (ErrorProblemDetailsExtensions, the global IExceptionHandler, and any future ProblemDetails
        factory all legitimately construct the type).
        Failure message: "{TypeDefinition.FullName}.{method} directly constructs {ProblemDetails |
        HttpValidationProblemDetails}. Use Error.ToProblemDetails() / ResultHttpExtensions from
        SharedKernel.Presentation.WebApi instead."
        Rationale: hand-rolled ProblemDetails construction outside the WebApi package bypasses the
        platform's single error-shape mapping (ErrorTypeStatusCodeMap, traceId population,
        Detail-suppression-outside-Development) and reintroduces the inconsistent error-body problem
        14.Presentation exists to close. Mirrors the precedent set by SK0013 (raw HttpClient) and
        the WO-026 Result/Envelope inline-mapping prohibition — mechanical enforcement, not
        documentation-only guidance.
        Offending pattern: return Results.Problem(new ProblemDetails { Title = "Bad request",
            Status = 400 }); inside a microservice endpoint
        Compliant pattern: return error.ToProblemDetails() routed through Results.Problem(...), or
            simply result.ToProblemDetailsResult() via ResultHttpExtensions

    .NoInlineResultBranchBeforeHttpResultOutsideWebApi(params Assembly[] assemblies) → ConditionList
        Asserts that no method body in the supplied assemblies reads Result/Result<T>.IsSuccess or
        .IsFailure and, within the same method, also constructs/returns a value typed
        Microsoft.AspNetCore.Http.IResult, Microsoft.AspNetCore.Mvc.ActionResult, or
        Microsoft.AspNetCore.Mvc.ActionResult<T> — without that same method also containing a call
        to a member named "ToProblemDetailsResult" (the ResultHttpExtensions entry point). Uses
        NoInlineResultBranchBeforeHttpResultPredicate (ICustomRule — see below). This is a coarser,
        method-level co-occurrence check (IsSuccess/IsFailure callsite + IResult/ActionResult return
        type + absence of ToProblemDetailsResult callsite, all within one MethodDefinition) — not a
        full control-flow analysis of "immediately before returning." A method containing all three
        signals is flagged regardless of statement ordering; this is a deliberate over-approximation
        favoring detection over precision, consistent with the documented limitation already
        recorded for HealthCheckTagIntegrityRules's literal-collection technique (no full data-flow
        analysis).
        Caller supplies every assembly to be checked EXCEPT SharedKernel.Presentation.WebApi itself —
        same caller-controlled exclusion convention as the sibling rule above (ResultHttpExtensions's
        own implementation legitimately reads IsSuccess/IsFailure and returns IResult/ActionResult).
        Failure message: "{TypeDefinition.FullName}.{method} branches on Result.IsSuccess/IsFailure
        and returns {IResult | ActionResult | ActionResult<T>} without routing through
        ResultHttpExtensions.ToProblemDetailsResult(). Use result.ToProblemDetailsResult() instead of
        inline IsSuccess/IsFailure branching before an HTTP response."
        Rationale: inline "if (result.IsSuccess) ... else ..." branching immediately before
        returning an HTTP response type duplicates the platform's Result→HTTP mapping logic at every
        call site, exactly the precedent already closed for Result<T>→Envelope<T> boundary mapping
        (WO-026 P-166/167's documented backlog item — this phase is the SK0xxx-style mechanical
        closure of that backlog note, implemented as a NetArchTest rule rather than a Roslyn
        analyzer because the detection surface is IL-level method-body co-occurrence, consistent
        with how SK0301-style domain/application misuse rules are implemented).
        Offending pattern: if (result.IsSuccess) return Results.Ok(result.Value); else return
            Results.Problem(...); inside a Minimal API endpoint delegate or controller action
        Compliant pattern: return result.ToProblemDetailsResult(value => Results.Ok(value));

    Permitted exemption list:
        - SharedKernel.Presentation.WebApi — never passed to either factory method by the caller;
          there is no internal namespace-prefix exemption inside either predicate. Any future
          legitimate exception (e.g., a second presentation package that must also construct
          ProblemDetails directly) must be documented here before being added to either predicate
          as an internal exemption — until then, exclusion is achieved exclusively by caller choice
          of which assemblies to pass, identical in spirit to RedisTopologyRules's caller-supplied
          assembly lists.

    Note: Introduced in WO-031 P-199. No new SK diagnostic ID assigned — both rules are pure
    NetArchTest ConditionList predicates over Mono.Cecil IL inspection, following the same
    "boundary-mapping prohibition via architecture test, not Roslyn analyzer" precedent already
    established for SK-less rules in this domain (RedisTopologyRules, CompositionRootExclusivityRules,
    GrpcNeverReferencesContracts). Lives in SharedKernel.ArchitectureTests/Rules/PresentationLayeringRules.cs.

NoDirectProblemDetailsConstructionPredicate  (class : ICustomRule — internal predicate)
    For each type (no namespace exemption — see PresentationLayeringRules note above), walks
    TypeDefinition.Methods.Body.Instructions for Newobj opcodes. For each Newobj instruction,
    checks MethodReference.DeclaringType.FullName against the set
    {"Microsoft.AspNetCore.Mvc.ProblemDetails", "Microsoft.AspNetCore.Http.HttpValidationProblemDetails"}
    (exact full-name match, case-sensitive — both are sealed/concrete framework types with no
    subclass risk). Returns false (rule violated) on the first match, with failure message naming
    the offending type, method, and the constructed type's simple name. Reuses the established
    Mono.Cecil Newobj-walk pattern from NoDirectEncryptedValueConverterInstantiationPredicate.
    Lives in Predicates/ folder. Used by
    PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi.

NoInlineResultBranchBeforeHttpResultPredicate  (class : ICustomRule — internal predicate)
    For each type, iterates TypeDefinition.Methods. For each MethodDefinition with a non-null Body,
    evaluates three independent signals over MethodDefinition.Body.Instructions and
    MethodDefinition.ReturnType in a single pass:
      (1) IsSuccess/IsFailure signal: a Call or Callvirt instruction whose MethodReference.Name is
          "get_IsSuccess" or "get_IsFailure" and whose MethodReference.DeclaringType.Name is
          "Result" or starts with "Result`1" (covers both Result and Result<T> IL representations)
      (2) HTTP-result-type signal: MethodDefinition.ReturnType.Name is "IResult", "ActionResult", or
          ReturnType.Name starts with "ActionResult`1" — OR any local variable
          (MethodDefinition.Body.Variables) typed identically, to also catch the "build a local,
          return it later" shape
      (3) Escape-hatch signal: a Call or Callvirt instruction whose MethodReference.Name is
          "ToProblemDetailsResult" anywhere in the method body — presence of this signal suppresses
          the violation regardless of signals (1) and (2)
    Returns false (rule violated) only when signals (1) AND (2) are both present AND signal (3) is
    absent. Failure message includes the declaring type name, method name, and which HTTP result
    type was detected. This is a method-level co-occurrence check, not a statement-order or
    control-flow analysis — see the documented over-approximation rationale in
    PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi. Lives in
    Predicates/ folder. Used by
    PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi.

ApplicationPipelineRules  (static class — 05.Application extended-pipeline enforcement predicates; WO-036 P-225)
    All factory methods accept Assembly (or params Assembly[]) and return ConditionList.
    Predicates are designed and tested here against contrived in-memory fixture assemblies —
    00.Governance never references 05.Application/05.Application.Behaviors directly (layering:
    00.Governance references nothing). The owning domain (05.Application) is responsible for
    invoking these factory methods against its own real assembly once WO-036's Core phase ships,
    mirroring the existing cross-domain consumption pattern already established for
    CachingAbstractionRules/RedisTopologyRules (consumed by 02.Caching's own test suites) and
    PersistenceLayerProtectionRules (consumed by 06.Persistence's own test suites).

    .BehaviorsNeverReferenceConcreteInfrastructure(params Assembly[] assemblies)  → ConditionList
        Asserts that no type named "TracingBehavior", "ResilienceBehavior", or
        "CacheInvalidationBehavior" (exact simple name match, caller-supplied HashSet<string> —
        never hardcoded inside the predicate) in the supplied assemblies has a member, field, or
        method-signature reference to a forbidden concrete-infrastructure namespace:
        "SharedKernel.Caching.FusionCache", "SharedKernel.Caching.Redis" (bare prefix — matches
        Redis.Core and all four Redis capability packages), "SharedKernel.Persistence" (excluding
        "SharedKernel.Persistence.Abstractions"), "SharedKernel.Messaging" (excluding
        "SharedKernel.Messaging.Abstractions"). Uses
        NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate (ICustomRule — see below).
        Failure message names the offending behavior type and the forbidden namespace referenced.
        Rationale: mirrors the existing, already-enforced
        SharedKernelLayeringRules.ApplicationNeverReferencesConcreteInfrastructure guarantee, made
        explicit and behavior-scoped for the three new WO-036 behaviors — the same purity
        expectation CachingBehavior (SharedKernel.Caching.Abstractions only) already satisfies by
        construction. Abstractions-only references remain permitted; only concrete provider
        packages are forbidden.
        Offending pattern: class CacheInvalidationBehavior<TRequest,TResponse> {
            private readonly IConnectionMultiplexer _redis; /* SharedKernel.Caching.Redis */ }
        Compliant pattern: class CacheInvalidationBehavior<TRequest,TResponse> {
            private readonly ICacheService _cacheService; /* SharedKernel.Caching.Abstractions */ }

    .NoExistingBehaviorMatchesStreamRequestConstraint(Assembly behaviorsAssembly)  → ConditionList
        Asserts that no type implementing the open generic IPipelineBehavior<,> in the supplied
        assembly has a TRequest generic-parameter constraint that structurally satisfies
        MediatR's IStreamRequest<TResponse> (directly or via interface closure). Uses
        NoGenericConstraintMatchesStreamRequestPredicate (ICustomRule — see below). This is a
        structural IL generic-constraint check, not a runtime DI resolution test — it fails at
        the architecture-test stage, earlier than any runtime wiring attempt, if a future
        behavior's TRequest constraint is loosened in a way that could accidentally capture
        IStreamQuery<TResponse>/IStreamRequest<TResponse>.
        Failure message names the offending behavior type and the matching constraint type.
        Rationale: 05.Application/CLAUDE.md documents as an explicit, deliberate design decision
        that none of the platform's pipeline behaviors apply to the streaming query vocabulary
        (P-221) — ValidationBehavior's TRequest : IRequest<TResponse> constraint does not match
        IStreamRequest<TResponse> today, and extending any behavior to streaming is a future,
        deliberate phase, never silently assumed. This rule makes that documented fact mechanically
        verified rather than merely asserted in prose.
        Offending pattern: a hypothetical future behavior loosening its constraint to
            where TRequest : IBaseRequest (a common ancestor MediatR gives both unary and
            streaming requests) — would structurally start matching IStreamRequest<TResponse>
        Compliant pattern: every behavior constrains TRequest to IRequest<TResponse> or a
            subtype (ICommandBase, ICacheableQuery<TResponse>, IAuthorizeRequest, etc.) — never
            the shared IBaseRequest ancestor

    .NoHandRolledRetryLoopOutsideResilienceBehavior(Assembly behaviorsAssembly)  → ConditionList
        Asserts that no type other than exactly "ResilienceBehavior" (exact simple name match) in
        the supplied assembly calls System.Threading.Tasks.Task.Delay (any overload — matched on
        MethodReference.Name == "Delay" AND DeclaringType.FullName ==
        "System.Threading.Tasks.Task", covering both the int-millisecond and TimeSpan overloads in
        one check). Uses NoTaskDelayOutsideResilienceBehaviorPredicate (ICustomRule — see below).
        This is a fingerprint heuristic, not a full retry-loop detector — Task.Delay is the one
        IL-detectable signal common to virtually every hand-rolled retry/backoff loop; a
        legitimate non-retry Task.Delay call elsewhere in 05.Application would also be flagged
        (none is known to exist at the time of this phase).
        Failure message names the offending type, method, and the Task.Delay call site.
        Rationale: extends 05.Application/CLAUDE.md's existing prohibition on hand-rolled
        System.Random/DateTime.UtcNow usage to retry/backoff specifically, now that
        ResilienceBehavior exists as the platform-sanctioned alternative (IRetryableRequest +
        ApplicationBehaviorsBuilder.AddResilienceBehavior(...)) — documented as a Hard Violation
        in 05.Application/CLAUDE.md but not previously mechanically enforced.
        Offending pattern: a handler or behavior catching a transient exception and calling
            await Task.Delay(backoffMs, ct); before retrying inline
        Compliant pattern: implement IRetryableRequest on the request and rely on
            ResilienceBehavior's externally-registered Polly v8 resilience pipeline

    PipelineOrderAssertion  (public class — reflection-based registration-order helper, not ConditionList/ICustomRule)
        .AssertRegistrationOrder(IServiceCollection services, params Type[] expectedBehaviorTypesInOrder)
            Walks the ServiceDescriptor entries in the supplied (unbuilt) IServiceCollection whose
            ServiceType is the open generic IPipelineBehavior<,>, in registration order, and asserts
            their ImplementationType (closed-generic open-generic-definition compared via
            GetGenericTypeDefinition()) sequence exactly matches expectedBehaviorTypesInOrder.
            Deliberately does NOT call IServiceCollection.BuildServiceProvider() — MediatR resolves
            IPipelineBehavior<,> instances in registration order, so inspecting the unbuilt
            ServiceDescriptor list is sufficient and avoids the cost/side-effects of a full container
            build. Throws an assertion failure (test-framework-agnostic exception) naming the
            expected vs. actual sequence on mismatch.
        Rationale: ApplicationBehaviorsBuilder.Build() registers behaviors in a fixed,
        non-negotiable order (the ten-named-slot canonical sequence documented in
        05.Application/CLAUDE.md) regardless of .AddXBehavior() call order. Without a mechanical
        assertion, a future edit to Build() can silently reorder the sequence — this helper is the
        primitive 05.Application.Behaviors.Tests uses to pin that order permanently. Lives in
        SharedKernel.ArchitectureTests (not 16.Testing) because it asserts an *architectural*
        invariant (fixed pipeline composition order), not a general test fixture — the same
        rationale that places ArchitectureRuleBase and the ICustomRule predicates in this package
        rather than in shared test infrastructure.
        Note: ships as a plain public reflection helper, not a NetArchTest ConditionList — it has
        no "fire on a contrived violating assembly" shape, since its input is an IServiceCollection
        instance, not a compiled Assembly. Its own correctness (passing case + failing case) is
        proven by a governance-owned unit test (T-153), distinct from 05.Application's own future
        consumption of it against the real ApplicationBehaviorsBuilder.Build() output.

NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate  (class : ICustomRule — internal predicate)
    Constructed with (HashSet<string> behaviorTypeNames, HashSet<string> forbiddenNamespacePrefixes) —
    both caller-supplied, never hardcoded, mirroring HealthCheckTagIntegrityRules's
    caller-supplied-prefix-list convention. For each type whose TypeDefinition.Name is in
    behaviorTypeNames (exact match), inspects TypeDefinition.Fields (FieldType.Namespace) and
    TypeDefinition.Methods.Body.Instructions (Call/Callvirt/Newobj operand DeclaringType.Namespace)
    for any namespace starting with a forbidden prefix, excluding any namespace ending in
    ".Abstractions". Returns false (rule violated) on the first match, with failure message naming
    the offending behavior type and the forbidden namespace. Lives in Predicates/ folder. Used by
    ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure.

NoGenericConstraintMatchesStreamRequestPredicate  (class : ICustomRule — internal predicate)
    Scope check: types whose TypeDefinition.Interfaces contains an entry with InterfaceType.Name
    starting with "IPipelineBehavior" (open generic IPipelineBehavior`2). For each such type,
    inspects the GenericParameter.Constraints collection on the TRequest generic parameter (first
    generic parameter position) for any constraint TypeReference whose FullName matches
    "MediatR.IStreamRequest`1" or whose resolved interface closure (TypeDefinition.Interfaces,
    recursively) includes it. Returns false (rule violated) on a structural match, with failure
    message naming the offending behavior type and the matching constraint. Fail-open if
    TypeReference.Resolve() returns null (unloaded assembly dependency) — consistent with the
    fail-open policy already established by SagaStateMustExtendSagaStateBasePredicate. Lives in
    Predicates/ folder. Used by ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint.

NoTaskDelayOutsideResilienceBehaviorPredicate  (class : ICustomRule — internal predicate)
    Self-exemption guard (first check): types whose TypeDefinition.Name == "ResilienceBehavior"
    (exact match) return true unconditionally. For all other types, walks
    TypeDefinition.Methods.Body.Instructions for Call or Callvirt opcodes whose
    MethodReference.Name == "Delay" AND MethodReference.DeclaringType.FullName ==
    "System.Threading.Tasks.Task" (covers all Task.Delay overloads in one check — both
    DeclaringType and Name must match, avoiding false positives on unrelated "Delay" methods on
    other types). Returns false (rule violated) on the first match, with failure message naming the
    offending type and method. Lives in Predicates/ folder. Used by
    ApplicationPipelineRules.NoHandRolledRetryLoopOutsideResilienceBehavior.

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

HealthCheckTagIntegrityRules  (static class — liveness/readiness tag integrity predicates; WO-027 P-173)
    All factory methods accept Assembly (the SharedKernel.ServiceDefaults assembly under test) and
    return ConditionList. Both predicates use an IL Ldstr-literal-collection technique — they are
    NOT a full data-flow analysis (see Limitation note below).
    .NoConflictingLivenessReadinessTags(Assembly serviceDefaultsAssembly)  → ConditionList
        Asserts that no method in the supplied assembly whose name starts with "Add" and ends with
        "HealthCheck" or "ReadinessCheck" (the platform's Add*HealthCheck/Add*ReadinessCheck
        extension-method naming convention) collects a string-literal tag set containing both
        "live" and "ready" for the same registration call. Uses
        NoConflictingLivenessReadinessTagsPredicate (ICustomRule — see below). Failure message
        names the offending method and the conflicting tag pair.
        Rationale: 13.ServiceDefaults/CLAUDE.md states "live" and "ready" tags are mutually
        exclusive as a central design invariant — a check must signal either pure process
        liveness or readiness-to-serve-traffic, never both, because Kubernetes liveness and
        readiness probes have different failure semantics (liveness failure restarts the pod;
        readiness failure removes it from the Service endpoint list without restarting).
        Offending pattern: AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live", "ready" })
        Compliant pattern: AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })

    .DependencyHealthChecksCarryReadyNotLive(Assembly serviceDefaultsAssembly, params string[] dependencyCheckMethodNamePrefixes)  → ConditionList
        Asserts that every method in the supplied assembly whose name starts with one of the
        caller-supplied dependencyCheckMethodNamePrefixes (e.g., "AddRedis", "AddDatabase",
        "AddRabbitMq", "AddAzureServiceBus", "AddCache") collects a string-literal tag set that
        contains "ready" and does not contain "live". Uses
        DependencyHealthChecksCarryReadyNotLivePredicate (ICustomRule — see below). The prefix
        list is caller-supplied rather than hard-coded so the consuming test project can enumerate
        the actual extension method names shipped by SharedKernel.ServiceDefaults once P-170 lands,
        rather than this governance layer guessing names that do not exist yet. Failure message
        names the offending method and whether the failure was a missing "ready" tag or a present
        "live" tag.
        Rationale: every dependency-specific health check (Redis, database, RabbitMQ, Azure Service
        Bus, cache) reports whether the SERVICE can currently serve traffic given that dependency's
        state — it must never be wired to the liveness probe, because a transient dependency outage
        would otherwise restart a perfectly healthy process instead of just draining traffic from it.
        Offending pattern: AddRedisHealthCheck(...) registers tags: new[] { "live" }
        Compliant pattern: AddRedisHealthCheck(...) registers tags: new[] { "ready" }

    Limitation (documented, not a defect): HealthCheckRegistration tags are an IEnumerable<string>
    supplied at a call site — in the general case this is a runtime value, not a compile-time
    constant. Both predicates only see tag values expressed as string literals (Ldstr IL opcode)
    feeding directly into the array/initializer or tag-parameter argument at the call site being
    inspected. If a future SharedKernel.ServiceDefaults extension computes its tag set dynamically
    (e.g., reading a tag name from configuration), neither predicate observes that value and the
    gate becomes advisory only for that call site — this must be flagged in code review for any
    such case, since the architecture test cannot catch it.

CompositionRootExclusivityRules  (static class — provider-family composition-root boundary enforcement; WO-027 P-173)
    .OnlyAllowedAssembliesMayReferenceConcreteProviders(params Assembly[] assembliesUnderTest)
                                            → ConditionList[]
        Mirrors CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching exactly:
        pure NetArchTest .Should().NotHaveDependencyOn(term) checks, no Mono.Cecil. Returns one
        ConditionList per forbidden term (five total, in order): "SharedKernel.Persistence.EfCore",
        "SharedKernel.Persistence.PostgreSQL", "SharedKernel.Persistence.Dapper",
        "SharedKernel.Messaging.MassTransit", "SharedKernel.Security.Oidc". Caller must assert
        .GetResult().IsSuccessful on EACH element of the returned array.
        The caller supplies the assemblies to check — must NOT include SharedKernel.ServiceDefaults,
        SharedKernel.MultiTenancy, or any of the five concrete provider packages themselves (the
        self-reference exclusion discipline established by
        RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther). Typical caller usage: pass
        05.Application, 03.Domain, 04.Contracts, 11.Communication.*, 12.Security.Abstractions —
        every production assembly that is NOT the composition root and NOT a provider package.
        Rationale: 13.ServiceDefaults/CLAUDE.md claims "13.ServiceDefaults is the only composition
        root permitted to reference concrete providers" is mechanically enforced by this domain's
        SharedKernelLayeringRules — but SharedKernelLayeringRules.CachingReferencesOnlyCore (P-009,
        WO-003) only ever covered 02.Caching providers, written before 13.ServiceDefaults existed.
        This rule closes that gap for the Persistence, Messaging, and Security provider families,
        mirroring the precedent set for Redis topology in RedisTopologyRules (P-145).
        Offending pattern: a MediatR handler in SharedKernel.Application referencing
            SharedKernel.Persistence.EfCore.EfRepository<T,TId> directly instead of
            SharedKernel.Persistence.Abstractions.IRepository<T,TId>
        Compliant pattern: SharedKernel.Application references only
            SharedKernel.Persistence.Abstractions; SharedKernel.ServiceDefaults wires the concrete
            EfCore/PostgreSQL/Dapper/MassTransit/Oidc implementations via DI at the composition root

    Composition-root exemption list (assemblies that MAY reference the five concrete provider
    packages):
        - SharedKernel.ServiceDefaults  (composition root — wires all concrete providers)
        - SharedKernel.MultiTenancy     (composition root — tenant resolution may need direct
                                          provider access for tenant-aware connection routing)
        - Each of the five provider packages referencing itself trivially (not a real exemption —
          a package's own types are never "external references" to itself)
        Any additional exemption must be documented here before it is applied in code.

    Note: introduced in WO-027 P-173, before SharedKernel.ServiceDefaults and
    SharedKernel.MultiTenancy exist as buildable assemblies (P-170 dependency). The architecture
    test fixtures for this rule must use contrived in-memory assemblies (CSharpCompilation +
    MetadataReference.CreateFromImage, the same technique documented for RedisTopologyRulesTests)
    until P-170 ships a real SharedKernel.ServiceDefaults assembly. A follow-up confirmation pass
    against the real assembly is required once P-170 lands and must be recorded in a future
    Changelog entry — it is not a blocking condition for this phase's own completion.

NoConflictingLivenessReadinessTagsPredicate  (class : ICustomRule — internal predicate)
    Scope filter: MethodDefinition.Name starts with "Add" AND (ends with "HealthCheck" OR ends
    with "ReadinessCheck"). Methods outside this scope return true unconditionally (not a
    registration extension method).
    For each in-scope method, walks MethodDefinition.Body.Instructions collecting all Ldstr
    opcode operand string values that feed into the same array-initializer / tag-parameter
    argument context as a single logical registration call's tag list. Returns false (rule
    violated) if the collected literal set for that call contains both "live" and "ready",
    with failure message naming the method and the conflicting tag pair.
    Lives in Predicates/ folder. Used by
    HealthCheckTagIntegrityRules.NoConflictingLivenessReadinessTags.

DependencyHealthChecksCarryReadyNotLivePredicate  (class : ICustomRule — internal predicate)
    Constructed with a caller-supplied string[] of dependency-check method-name prefixes (e.g.,
    {"AddRedis","AddDatabase","AddRabbitMq","AddAzureServiceBus","AddCache"}). Scope filter:
    MethodDefinition.Name starts with any configured prefix. Methods outside this scope return
    true unconditionally.
    For each in-scope method, uses the same Ldstr literal-collection walk as
    NoConflictingLivenessReadinessTagsPredicate. Returns false (rule violated) if the collected
    literal set does not contain "ready", OR if it contains "live" — either condition fails
    independently, with a failure message stating which condition triggered (missing "ready" vs.
    present "live").
    Lives in Predicates/ folder. Used by
    HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive.

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

HealthCheckConstantsUsageRules  (static class — generalized magic-string-vs-constants-class guard; WO-028 P-178)
    Additive to HealthCheckTagIntegrityRules (WO-027 P-173) — a DIFFERENT concern. P-173 enforces
    "live"/"ready" tag mutual-exclusivity semantics. This rule enforces a source-discipline
    concern: a bare string literal must not duplicate a value already exposed by a sibling
    constants class. The two rule groups must never be merged or confused; both apply to
    SharedKernel.ServiceDefaults independently.
    .NoBareHealthCheckLiteralWhereConstantsExist(Assembly assembly)  → ConditionList
        Uses NoBareHealthCheckLiteralWhereConstantsExistPredicate (ICustomRule — see below).
        Algorithm: (a) runs StringConstantsClassDetector once across the supplied assembly to
        resolve the full set of (declaring type, field name, literal value) tuples exposed by
        every detected "string constants class" in that assembly; if the resolved set is empty,
        the rule passes unconditionally (nothing to enforce yet — covers any domain before it
        adopts the constants-class pattern); (b) for each recognized health-check registration
        call site (IHealthChecksBuilder.Add, .AddCheck, or HealthCheckRegistration's constructor),
        collects every string literal argument via the established Ldstr literal-collection
        technique (same technique as NoConflictingLivenessReadinessTagsPredicate /
        DependencyHealthChecksCarryReadyNotLivePredicate, P-173); (c) fails if any collected
        literal exactly matches a resolved constant value. Failure message names the offending
        method, the literal value, and the constants-class type + field name that already
        exposes that value.
        Generality requirement (acceptance-critical): neither this method nor its predicate may
        contain the literal strings "HealthCheckTags" or "HealthCheckNames" (or any other
        concrete constants-class name) anywhere in the implementation. Detection is entirely
        shape- and value-based (see StringConstantsClassDetector below) — the rule generalizes
        unmodified to any future domain that introduces its own well-known-string constants
        class guarding a registration API of the same general shape.
        Rationale: P-177's audit found two generations of the same mistake in one domain —
        HealthCheckTags was built correctly as a constants class, but five sibling files kept
        hardcoding default health-check names as bare literals instead of extending the same
        discipline. Nothing mechanically caught the inconsistency. This rule is the platform's
        third instance of turning a one-time manual fix into a permanent, mechanically-enforced
        guarantee (precedent: SharedKernelLayeringRules/CachingAbstractionRules P-009,
        RedisTopologyRules P-145).
        Offending pattern: builder.AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
            when a sibling HealthCheckTags.Live constant with value "live" already exists
        Compliant pattern: builder.AddCheck(HealthCheckNames.Self, () => HealthCheckResult.Healthy(), tags: new[] { HealthCheckTags.Live })

StringConstantsClassDetector  (class — reusable helper; NOT itself an ICustomRule; WO-028 P-178)
    Detects the "string constants class" shape and resolves the literal values it exposes.
    A type qualifies if: TypeDefinition.IsAbstract && TypeDefinition.IsSealed (the C# `static
    class` IL shape) AND it declares at least one field AND every field on the type is either
    (a) IsLiteral with FieldType.FullName == "System.String" (a `const string`), or
    (b) IsInitOnly && IsStatic with FieldType.FullName == "System.String" (a `static readonly
    string`). Mixed-type constants classes (string constants alongside non-string constants)
    still qualify — non-string fields are ignored, not disqualifying; only string-typed fields
    contribute to the resolved value set. Returns the full set of (declaring type name, field
    name, literal value) tuples found across every qualifying type in the supplied assembly.
    Lives in Predicates/ folder despite not being an ICustomRule — co-located with its sole
    consumer for discoverability. Used by
    HealthCheckConstantsUsageRules.NoBareHealthCheckLiteralWhereConstantsExist. This is the
    platform's third distinct Mono.Cecil IL/metadata technique in this domain: (1) opcode-
    presence (single opcode/method-name match, e.g. NoMakeGenericMethodReflectionPredicate),
    (2) Ldstr literal-collection (a set of string operands within a scoped method, e.g.
    NoConflictingLivenessReadinessTagsPredicate), (3) field-shape + literal-value resolution
    (this class — inspects TypeDefinition.Fields rather than method bodies).

NoBareHealthCheckLiteralWhereConstantsExistPredicate  (class : ICustomRule — internal predicate)
    Calls StringConstantsClassDetector once per assembly scan to build the resolved constant-
    value set. Recognizes health-check registration call sites by MethodReference.Name plus a
    declaring-type check. Confirmed exact declaring-type names (via Mono.Cecil inspection of
    Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions.dll /
    Microsoft.Extensions.Diagnostics.HealthChecks.dll, .NET 10 reference assemblies):
    "Add" on Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder (interface) or
    Microsoft.Extensions.DependencyInjection.HealthChecksBuilder (concrete); "AddCheck" on any
    declaring type whose simple name starts with "HealthChecksBuilder" (covers
    HealthChecksBuilderAddCheckExtensions and HealthChecksBuilderDelegateExtensions, both real
    extension-method host types in Microsoft.Extensions.Diagnostics.HealthChecks.dll); and
    ".ctor" on Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration. For each
    matched call, walks the Ldstr-literal-collection technique
    (reused from NoConflictingLivenessReadinessTagsPredicate) to gather every string literal
    argument at that call site — covering both the check-name argument and any tag-array/tag-
    parameter argument. Returns false (rule violated) on the first literal found whose value
    exactly matches a resolved constant value, with failure message naming the offending method,
    the literal value, and the matching constants-class type + field name. Returns true
    (passes) unconditionally if the resolved constant-value set is empty (no constants class
    exists yet in the assembly). Lives in Predicates/ folder. Used by
    HealthCheckConstantsUsageRules.NoBareHealthCheckLiteralWhereConstantsExist.
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
- `NoAesCipherInDomainOrApplicationPredicate`'s namespace exemption was narrowed in WO-037 P-229 from `{"SharedKernel.Persistence", "SharedKernel.Security"}` to `{"SharedKernel.Cryptography"}` only — the motivating incident was a hand-rolled `AesGcm` usage shipped inside `SharedKernel.Persistence.*` that this predicate exempted wholesale because it was only ever invoked against `03.Domain`/`05.Application`, never against `06.Persistence` itself. SK0301 remains scoped to whichever assemblies the caller passes; `CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography` is the platform-wide generalization intended to be invoked against every production assembly. No new SK ID was minted — see the `CryptoIsolationRules` entry in Architecture Test Contracts for the full reconciliation rationale.
- `CryptoIsolationRules` and `UnitOfWorkSeamRules` (WO-037 P-229) introduce zero new SK diagnostic IDs and zero new NuGet dependencies — both reuse the existing `Mono.Cecil >= 0.11.5` reference. `UnitOfWorkInterfacesRemainDistinctPredicate` is a negative-space/regression-guard rule: both `IUnitOfWork` interfaces are independently declared today (the desired state), so its fire-path test fixtures must use contrived two-assembly pairs proving the predicate would catch a future interface-merge or interface-inheritance attempt — there is no existing bad pattern in the codebase to point the fire-path test at.
- `NoRawSymmetricCipherOutsideCryptographyPredicate`'s `RandomNumberGenerator` surface uses a `MethodReference.DeclaringType.FullName` match rather than a single method-name match, because `RandomNumberGenerator` exposes multiple static and instance entry points (`Fill`, `GetBytes`, `Create`, etc.) — a `DeclaringType` check catches all of them in one IL walk pass, consistent with how `NoDirectSaveChangesPredicate` matches `DbContext.SaveChanges`/`SaveChangesAsync` by declaring-type-plus-name rather than enumerating every overload individually.
- `UnitOfWorkSeamRules.UnitOfWorkInterfacesRemainDistinct` is the only factory method in this domain that accepts exactly two named `Assembly` parameters (not a single `Assembly` or `params Assembly[]`) — this is deliberate: the rule's entire purpose is comparing two specific, named interfaces that live in two specific, named assemblies, so positional `params` would obscure which assembly is expected to hold which interface.
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
- `ReflectionGuardRules.NoMakeGenericMethodReflection(Assembly)` uses `.That().AreNotAbstract()` before the `.Should().MeetCustomRule(...)` call to exclude compiler-generated abstract helper types (e.g., state machine types generated by async/await) that may contain unusual IL patterns. This is the same filter philosophy used by `DomainGoldStandardRules.DomainServicesMustExtendAbstractBase`.
- `NoMakeGenericMethodReflectionPredicate` checks `MethodReference.Name == "MakeGenericMethod"` (exact name, case-sensitive). This name is unique to `System.Reflection.MethodInfo.MakeGenericMethod` — no other BCL API uses this exact name. No namespace or declaring-type check is needed; the name match is sufficient and avoids false positives from custom extension methods that would need to be deliberately named `MakeGenericMethod` to trigger the rule.
- `ReflectionExemptionRegistry.IsExempt(typeFullName, methodName)` uses `TypeDefinition.FullName` (the CLR full name including namespace and enclosing types, e.g., `"SharedKernel.Persistence.EfCore.EncryptionRotationService"`) and `MethodDefinition.Name` (the simple method name, e.g., `"LoadBatchAsync"`). Both strings are matched case-sensitively. If a method is overloaded, all overloads with the same name are covered by a single registry entry — the registry is method-name-scoped, not signature-scoped, to avoid brittle signature strings in the allow-list.
- `ReflectionGuardRules` is the only SK0012 enforcement mechanism — there is no Roslyn analyzer complement. The reason: `MethodInfo.MakeGenericMethod` is called at runtime on a variable of type `MethodInfo` returned from `GetMethod`/`GetMethods`; there is no compile-time syntax pattern to detect. A Roslyn analyzer would only fire on the literal string `"MakeGenericMethod"` passed to invocation expressions, missing any case where the `MethodInfo` variable is obtained from a method call, stored, and then `.MakeGenericMethod(...)` is called on it in a separate statement. IL inspection is the only reliable detection mechanism.
- `ReflectionGuardRules.NoMakeGenericMethodReflection` must be called with production assemblies only — never pass `SharedKernel.ArchitectureTests` itself, any `.Tests` project, or `SharedKernel.Benchmarks`. The `ReflectionExemptionRegistry` is part of `SharedKernel.ArchitectureTests` and references to `MakeGenericMethod` inside the test predicate infrastructure itself are not in scope (the predicate is called on the types of the SUPPLIED assembly, not on the predicate's own class).
- SK0012 is the next sequential ID in the SK0001–SK00N general-purpose block (SK0001–SK0011 were the prior sequential IDs). The 02xx, 03xx, and 07xx blocks are separate domain-specific ranges. SK0012 begins in the general-purpose block because the reflection prohibition is platform-wide and does not belong to any single capability domain.
- The P-147 motivating incident (EncryptionRotationService.LoadBatchAsync) MUST be referenced in the `ReflectionExemptionRegistry.cs` class-level XML doc comment and in the `NoMakeGenericMethodReflectionPredicate.cs` file header comment. This ensures future readers understand why the registry exists and why expression trees are always preferred over the `MakeGenericMethod` shortcut.
- `CommunicationLayeringRules.GrpcNeverReferencesContracts` uses a single `.Should().NotHaveDependencyOn("SharedKernel.Contracts")` call — consistent with the established `NotHaveDependencyOn` matching contract (namespace StartsWith, no trailing dot). The term `"SharedKernel.Contracts"` is the exact identifying namespace of the Contracts package. No exemption is permitted — see rule entry in Architecture Test Contracts. Introduced in WO-026 P-167.
- `HealthCheckTagIntegrityRules` introduces the platform's first IL **literal-collection** technique distinct from the established IL **opcode-presence** technique (e.g., `NoMakeGenericMethodReflectionPredicate` looks for a single opcode/method-name match; these two new predicates collect a *set* of `Ldstr` string operands within a scoped method and reason about set membership). This is a heavier pattern than prior predicates — document any new collection helper in `Predicates/` so it can be reused if a third tag-like literal-set rule is ever needed.
- `HealthCheckTagIntegrityRules` predicates are scoped by `MethodDefinition.Name` prefix/suffix matching (`"Add"` + `"HealthCheck"`/`"ReadinessCheck"` for the first; caller-supplied prefixes for the second) — neither predicate inspects arbitrary call sites across the whole assembly. This intentionally limits the rule to `SharedKernel.ServiceDefaults`'s own registration extension methods, which are documented (root `CLAUDE.md`) as the sole sanctioned health-check registration surface; consuming services are expected to call these extensions, not register `IHealthCheck` instances directly.
- The IL-literal-collection technique in `HealthCheckTagIntegrityRules` is **not** a full data-flow analysis. If a tag value is computed dynamically (e.g., from `IConfiguration`) rather than expressed as a literal at the call site, neither predicate observes it. This is a documented limitation, not a defect — record it in the predicate's XML doc and do not attempt to "fix" it with a heavier analysis unless a real false-negative is found in production code.
- `CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders` is the structural sibling of `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching` — same `ConditionList[]`-per-forbidden-term shape as `RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages`. It introduces zero new Mono.Cecil predicates; it is pure `NetArchTest.eNt` namespace-prefix dependency checking, consistent with the `NotHaveDependencyOn` matching contract documented earlier in this file (StartsWith on dependency namespaces, no trailing dot, no self-collision risk because none of the five forbidden terms is a prefix of `SharedKernel.ServiceDefaults` or `SharedKernel.MultiTenancy`).
- Both `HealthCheckTagIntegrityRules` and `CompositionRootExclusivityRules` were designed in WO-027 P-173 **before** `SharedKernel.ServiceDefaults` and `SharedKernel.MultiTenancy` exist as buildable assemblies (P-170 is still pending at design time). Test fixtures for this phase must use contrived in-memory assemblies built via `CSharpCompilation` + `MetadataReference.CreateFromImage` — the same technique documented for `RedisTopologyRulesTests` — rather than referencing the real packages. A follow-up confirmation pass against the real `SharedKernel.ServiceDefaults` assembly is required once P-170 ships; this is tracked as a follow-up, not a blocking condition on this phase's own completion.
- `HealthCheckConstantsUsageRules` is **additive to, not a replacement for,** `HealthCheckTagIntegrityRules` (P-173). The two enforce different concerns on the same `SharedKernel.ServiceDefaults` assembly: `HealthCheckTagIntegrityRules` enforces tag *semantics* ("live"/"ready" mutual exclusivity); `HealthCheckConstantsUsageRules` enforces *source discipline* (bare literals must not duplicate an existing constant). Never collapse these into a single rule group — they were introduced in separate work orders (P-173, P-178) for separate motivating incidents and may evolve independently.
- `StringConstantsClassDetector`'s shape check (`IsAbstract && IsSealed`) is the standard Mono.Cecil IL signature for a C# `static class` — there is no `IsStatic` flag on `TypeDefinition` in IL; `static` classes compile to `abstract sealed`. Do not attempt to find an `IsStatic` property on `TypeDefinition` — it does not exist for types (only for fields and methods).
- `NoBareHealthCheckLiteralWhereConstantsExistPredicate` and `HealthCheckConstantsUsageRules` must **never** contain a concrete constants-class name (e.g. `"HealthCheckTags"`, `"HealthCheckNames"`) as a string literal anywhere in the implementation. This is the acceptance-critical generality requirement from WO-028 P-178 — the rule must generalize unmodified to any future domain's constants class. Code review must reject any PR that adds a name-specific check to this rule; if a domain needs name-specific enforcement, that belongs in a new, separately-scoped rule, not a special case bolted onto this one.
- **Confirmed declaring-type names** (verified by direct Mono.Cecil inspection of the .NET 10 `Microsoft.AspNetCore.App.Ref` reference assemblies, package `Microsoft.Extensions.Diagnostics.HealthChecks` / `.Abstractions`): `Add(HealthCheckRegistration)` is declared on both the interface `Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder` and the concrete `Microsoft.Extensions.DependencyInjection.HealthChecksBuilder`. `AddCheck` overloads are declared across two extension-method host classes — `Microsoft.Extensions.DependencyInjection.HealthChecksBuilderAddCheckExtensions` and `Microsoft.Extensions.DependencyInjection.HealthChecksBuilderDelegateExtensions` — both matched by `NoBareHealthCheckLiteralWhereConstantsExistPredicate` via a `declaringTypeName.StartsWith("HealthChecksBuilder")` check rather than an exact-name list, so it also covers any future extension-method host class following the same naming convention. The `HealthCheckRegistration` constructor is `Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration::.ctor`. Verified against package version shipped with the .NET 10 SDK (`Microsoft.AspNetCore.App.Ref` 10.0.7) — re-verify if the platform ever pins an explicit `Microsoft.Extensions.Diagnostics.HealthChecks` NuGet version that diverges from the SDK-bundled one.
- `StringConstantsClassDetector` resolves literal *values*, not names — the predicate compares the bare literal's string value against the resolved constant value set, never against a field or class name. This is the design choice that lets the rule fire correctly regardless of what the constants class or its fields are named, and is what makes the rule catch the exact P-177 incident shape (a literal that happens to equal an existing constant's value) without requiring any naming convention from the consuming domain.
- `PresentationLayeringRules` introduces zero new SK diagnostic IDs — both rules are pure NetArchTest `ConditionList` predicates over Mono.Cecil IL inspection, mirroring the existing precedent that boundary-mapping prohibitions (raw `HttpClient`, `Result`↔`Envelope`, now `Result`↔HTTP and `ProblemDetails` construction) are enforced via this domain's `ICustomRule` predicates rather than always minting a new Roslyn analyzer. Neither predicate carries an internal namespace exemption — exclusion of `SharedKernel.Presentation.WebApi` is achieved entirely by the consuming test project never passing that assembly to either factory method. Document any future internal exemption here before adding one to either predicate.
- `NoDirectProblemDetailsConstructionPredicate` matches on `MethodReference.DeclaringType.FullName` exact string equality against `"Microsoft.AspNetCore.Mvc.ProblemDetails"` and `"Microsoft.AspNetCore.Http.HttpValidationProblemDetails"` — both are concrete framework types, so a `newobj` opcode is always the construction site (no factory-method indirection to account for, unlike `EncryptedValueConverter<T>`). Reuses the `Newobj`-walk pattern from `NoDirectEncryptedValueConverterInstantiationPredicate` — no new NuGet dependency.
- `NoInlineResultBranchBeforeHttpResultPredicate` is a **method-level co-occurrence check, not a control-flow analysis**. It does not verify that the `IsSuccess`/`IsFailure` read occurs immediately before the `IResult`/`ActionResult` return — it only verifies that both signals appear somewhere in the same method body and that no `ToProblemDetailsResult` call also appears in that body. This is a deliberate over-approximation (same documented-limitation philosophy as `HealthCheckTagIntegrityRules`'s literal-collection technique) — a method that reads `IsSuccess` for an unrelated logging decision and separately returns an `IResult` for an unrelated reason would also be flagged. If this produces real false positives in practice, narrow the check to control-flow adjacency in a follow-up phase; do not narrow it speculatively now.
- `NoInlineResultBranchBeforeHttpResultPredicate`'s `Result`/`Result<T>` type-name match (`"Result"` exact or `"Result\`1"` prefix for the IL generic-arity-suffixed name) targets `SharedKernel.Primitives.Result`/`Result<T>` specifically. If a consuming assembly defines an unrelated type also named `Result` with its own `IsSuccess`/`IsFailure` properties, this predicate cannot distinguish them without a `DeclaringType.Namespace` check — add a namespace guard (`"SharedKernel.Primitives"`) if this false-positive risk is ever confirmed in practice; it is not added pre-emptively because no such collision is known to exist in this platform's codebase today.
- `PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi` and `.NoInlineResultBranchBeforeHttpResultOutsideWebApi` both accept `params Assembly[]` — the caller is responsible for never including `SharedKernel.Presentation.WebApi` in the supplied list. Unlike most prior `ICustomRule` predicates in this domain, there is no internal `TypeDefinition.Namespace.StartsWith(...)` guard inside either predicate; this is a deliberate design choice because no single namespace prefix covers every legitimate in-package construction site (`ErrorProblemDetailsExtensions`, the global `IExceptionHandler`, `ResultHttpExtensions` itself, and any future factory all legitimately trigger both signals).
- `PresentationLayeringRules` lives in `SharedKernel.ArchitectureTests/Rules/PresentationLayeringRules.cs`; its two `ICustomRule` predicates live in `Predicates/`. Both reuse the existing `Mono.Cecil >= 0.11.5` reference — no new NuGet dependency introduced by this phase.
- **`const string` vs `static readonly string` produce different IL at the *consuming* call site** — this matters for any future test fixture or predicate reasoning about field-reference detection. The C# compiler const-folds every `const string` field reference into a bare `Ldstr` literal at each call site (no `Ldsfld`, no trace that a constant was referenced at all); only `static readonly string` field references compile to `Ldsfld`. `StringConstantsClassDetector.ResolveStringConstants` correctly resolves the *declaring* type's own value for both field kinds (via `FieldDefinition.Constant` for `const`, via a `.cctor` `Ldstr`→`Stsfld` walk for `static readonly`), but `NoBareHealthCheckLiteralWhereConstantsExistPredicate`'s pass-path (field access instead of literal) only holds for `static readonly string` constants classes — a `const string` constants class can never produce a passing fixture for the "field access, not literal" scenario, because Roslyn erases the field reference before Mono.Cecil ever sees the consuming method's IL. Discovered while building the T-140 pass-path fixture for `SK.00.HealthCheckConstantsGuard` (WO-028 P-178); document this if a future domain's constants-class convention is ever questioned for using `const` instead of `static readonly`.
- `ApplicationPipelineRules` introduces zero new SK diagnostic IDs — all three new checks are pure Mono.Cecil `ICustomRule` predicates, mirroring the established precedent (`RedisTopologyRules`, `CompositionRootExclusivityRules`, `GrpcNeverReferencesContracts`, `PresentationLayeringRules`) that boundary-mapping and structural-purity prohibitions do not always require minting a new Roslyn analyzer. `00.Governance` never references `05.Application`/`05.Application.Behaviors` directly (layering: `00.Governance` references nothing) — all three predicates and `PipelineOrderAssertion` are designed and tested here against contrived in-memory fixture assemblies; `05.Application` is responsible for invoking them against its own real assembly once WO-036's Core phase (`05.Application/state-map.md` C-18..C-29) ships.
- `NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate`'s behavior-name set (`"TracingBehavior"`, `"ResilienceBehavior"`, `"CacheInvalidationBehavior"`) and forbidden-namespace set are both caller-supplied `HashSet<string>` constructor parameters, never hardcoded inside the predicate — the same caller-supplied-list convention already established by `HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive`'s `dependencyCheckMethodNamePrefixes` parameter. This lets a future fourth infra-adjacent behavior be covered by a caller-side change alone, no predicate code change required.
- `NoGenericConstraintMatchesStreamRequestPredicate` introduces the platform's **fourth distinct Mono.Cecil technique** for this domain, alongside opcode-presence (`NoMakeGenericMethodReflectionPredicate`), `Ldstr` literal-collection (`HealthCheckTagIntegrityRules`), and field-shape/literal-value resolution (`StringConstantsClassDetector`): **IL generic-parameter-constraint inspection** (`GenericParameter.Constraints` on an open generic type's type parameter, with interface-closure resolution). This is a structural check at the type-definition level, not an instruction walk — document any new constraint-inspection helper here if a future rule needs the same technique, so it is reused rather than redefined.
- `NoTaskDelayOutsideResilienceBehaviorPredicate` is a documented **fingerprint heuristic, not a full retry-loop detector** — it flags any `Task.Delay` call outside a type named exactly `ResilienceBehavior`, accepting the risk that a legitimate non-retry `Task.Delay` use elsewhere in `05.Application` would also be flagged. This mirrors the same documented-limitation philosophy already established for `HealthCheckTagIntegrityRules`'s literal-collection technique and `NoInlineResultBranchBeforeHttpResultPredicate`'s method-level co-occurrence check — do not narrow or broaden this heuristic speculatively; only revise it if a real false positive or false negative is found in production code.
- `PipelineOrderAssertion` is the first artifact in `SharedKernel.ArchitectureTests` that is **not** a `ConditionList`/`ICustomRule` — it is a plain public reflection helper operating on an unbuilt `IServiceCollection`'s `ServiceDescriptor` entries, never calling `BuildServiceProvider()`. It exists in this package (not `16.Testing`) because it asserts an architectural invariant (fixed `IPipelineBehavior<,>` registration order), the same rationale that already places `ArchitectureRuleBase` and every `ICustomRule` predicate here rather than in shared test infrastructure. `05.Application.Behaviors.Tests` is the intended consumer — see `05.Application/state-map.md` T-17/T-18 (WO-036).

---

## WO-026 Governance Conventions

> These conventions encode architectural decisions made in WO-026. They are the authoritative reference for the patterns established in that work order. The root CLAUDE.md "What Goes Where" table should be updated via `/sync-brain` to reflect these entries.

### Cross-Service DTO Boundary Mapping

- `Result<T>` → `Envelope<T>` boundary mapping: `04.Contracts/SharedKernel.Contracts` via `ResultEnvelopeExtensions.ToEnvelope()` / `ToResult()`. These are pure static extension methods on `Result<T>` and `Envelope<T>`; they carry no cross-layer dependency cost because `SharedKernel.Contracts` already references `SharedKernel.Primitives`.
- **Platform violation — inline mapping forbidden:** Writing `if (result.IsSuccess) Envelope<T>.Ok(result.Value) else Envelope<T>.Fail(result.Error)` at service controller or endpoint boundaries is a platform violation. Always use `result.ToEnvelope()` from `SharedKernel.Contracts.Mapping`. Inline mapping diverges from the platform convention, duplicates the error-projection logic, and is undetectable by the current architecture test suite. A future Roslyn analyzer (next available ID in the general-purpose SK block after SK0013) is tracked as a backlog item to mechanically enforce this rule — no ID is assigned until that phase is planned.

### GraphQL Paged Response

- `PagedResponseType<T>` from a `PagedList<T>` source: `11.Communication.GraphQL` via `PagedResponseType<T>.FromPagedList(pagedList)`. The `PagedResponseType<T>` type is the GraphQL-transport-safe projection of the internal `PagedList<T>` shape from `04.Contracts`. Never construct a raw GraphQL connection type from `PagedList<T>` fields directly — the `FromPagedList` factory applies the correct cursor and total-count mapping.

### K8s Endpoint Resolution TTL

- Endpoint resolution cache TTL: `11.Communication.Internal` via `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds`. All endpoint cache TTL configuration must go through this options type — never through a raw `TimeSpan` or `int` field on a typed client constructor or DI registration. Changing the TTL in one place (options configuration) propagates to all endpoint resolvers; ad-hoc per-client TTL values fragment the discovery behaviour across the service mesh.

### Multiple Typed REST Clients with Service Discovery

- Multiple typed REST clients sharing service discovery: `11.Communication.Rest` via the inline factory pattern — pass the resolved base address from `IServiceEndpointResolver` directly into each `AddRestClient<TClient>()` registration callback. The `ServiceDiscoveryResolvingHandler` is a framework-internal type and must NOT be registered directly as a named `DelegatingHandler` from consuming service code; doing so bypasses the platform's handler-lifetime management and creates invisible coupling to the internal handler type name.

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
- [2026-06-16] SK0012 MakeGenericMethodReflection added to diagnostic registry (general-purpose sequential block, next after SK0011); ReflectionGuardRules static class added to architecture test contracts (single predicate: NoMakeGenericMethodReflection(Assembly) → ConditionList); NoMakeGenericMethodReflectionPredicate ICustomRule (Mono.Cecil Call/Callvirt opcode walk for MakeGenericMethod name match) and ReflectionExemptionRegistry allow-list mechanism documented; eight new implementation rules added; motivating incident: P-147 EncryptionRotationService.LoadBatchAsync — WO-024 P-153
- [2026-06-18] SK0013 RawHttpClientConstructorInjection added to diagnostic registry (Usage, Warning; DelegatingHandler base-class exemption and SharedKernel.Communication.Rest namespace exemption; syntax-only, no SemanticModel); CommunicationLayeringRules static class added to architecture test contracts (four predicates: CommunicationPackagesNeverReferencesForbiddenLayers, CommunicationInternalNeverReferencesOtherCommunicationPackages, NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc, NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL); NoDirectGrpcInterceptorInheritancePredicate and NoDirectHotChocolateFilterSortInheritancePredicate ICustomRule predicates documented; hardcoded-URI guard documented as documentation-only (feasibility concern); 93 analyzer + 86 arch tests pass — WO-025 P-159 (sync-brain)
- [2026-06-18] GrpcNeverReferencesContracts added to CommunicationLayeringRules (single NotHaveDependencyOn("SharedKernel.Contracts") call; no exemption permitted; locks P-163 dead-reference removal permanently); WO-026 Governance Conventions section added (four What Goes Where patterns: ResultEnvelopeExtensions.ToEnvelope, PagedResponseType.FromPagedList, K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds, inline REST client factory pattern); inline Result/Envelope mapping documented as platform violation with future SK0xxx backlog note — WO-026 P-167
- [2026-06-19] SK.00.WO026CommunicationQuality → ● — GrpcNeverReferencesContracts implemented and verified against pre-written spec (no discrepancy); 88/88 architecture tests passing — WO-026 closeout (sync-brain)
- [2026-06-19] HealthCheckConstantsUsageRules static class added to architecture test contracts (single predicate: NoBareHealthCheckLiteralWhereConstantsExist) — generalized magic-string-vs-constants-class guard, additive to (not a replacement for) HealthCheckTagIntegrityRules/CompositionRootExclusivityRules (P-173); StringConstantsClassDetector reusable helper documented (third distinct Mono.Cecil technique in this domain: field-shape + literal-value resolution, alongside opcode-presence and Ldstr literal-collection); no new SK IDs; five new implementation rules added; motivating incident: P-177 HealthCheckTags/HealthCheckNames inconsistency — WO-028 P-178
- [2026-06-19] HealthCheckTagIntegrityRules added to architecture test contracts (two predicates: NoConflictingLivenessReadinessTags, DependencyHealthChecksCarryReadyNotLive — first use of an IL Ldstr literal-collection technique distinct from prior opcode-presence predicates; documented data-flow limitation for non-literal tag values); CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders added, mirroring CachingAbstractionRules to extend the composition-root exclusivity claim in 13.ServiceDefaults/CLAUDE.md from caching-only (P-009 scope) to Persistence (.EfCore/.PostgreSQL/.Dapper), Messaging (.MassTransit), and Security (.Oidc) provider families; no new SK IDs; five new implementation rules added; designed ahead of P-170 (13.ServiceDefaults Core) using contrived in-memory fixtures, real-assembly re-verification tracked as a post-P-170 follow-up — WO-027 P-173
- [2026-06-25] PresentationLayeringRules added to architecture test contracts (two predicates: NoDirectProblemDetailsConstructionOutsideWebApi via NoDirectProblemDetailsConstructionPredicate — Newobj IL match on ProblemDetails/HttpValidationProblemDetails full names; NoInlineResultBranchBeforeHttpResultOutsideWebApi via NoInlineResultBranchBeforeHttpResultPredicate — method-level co-occurrence check for IsSuccess/IsFailure + IResult/ActionResult return + absence of ToProblemDetailsResult escape hatch); closes the WO-026 P-166/167 backlog note for a mechanical Result-to-HTTP boundary enforcement; no new SK IDs — both implemented as NetArchTest ICustomRule predicates, not Roslyn analyzers; no internal namespace exemption — caller excludes SharedKernel.Presentation.WebApi by never passing it; six new implementation rules added — WO-031 P-199
- [2026-06-24] SK.00.HealthCheckConstantsGuard → ● closeout — StringConstantsClassDetector and NoBareHealthCheckLiteralWhereConstantsExistPredicate implemented in Predicates/; HealthCheckConstantsUsageRules implemented in Rules/; confirmed exact Microsoft.Extensions.Diagnostics.HealthChecks declaring-type names via direct Mono.Cecil inspection of the .NET 10 reference assemblies (IHealthChecksBuilder/HealthChecksBuilder for Add, HealthChecksBuilderAddCheckExtensions/HealthChecksBuilderDelegateExtensions for AddCheck, HealthCheckRegistration for the constructor) and recorded them in this file, resolving the two prior "confirm during implementation" placeholders; corrected the T-140 pass-path fixture from `const string` to `static readonly string` after discovering Roslyn const-folds `const string` field references into a bare Ldstr at the call site (no Ldsfld) — only `static readonly string` produces the Ldsfld IL shape the rule's pass-path depends on; reworded three XML-doc passages in the implementation files that referenced "HealthCheckTags"/"HealthCheckNames" by name to keep the acceptance-critical generality requirement unambiguous (CLAUDE.md prose retains the real names in its own offending/compliant examples, consistent with every other rule's documentation); T-139–T-142 added (4 new tests), 102/102 full ArchitectureTests.Tests suite passes, 0 build warnings/errors (state-map-phase)
- [2026-06-25] SK.00.PresentationArchRules → ● closeout — NoDirectProblemDetailsConstructionPredicate and NoInlineResultBranchBeforeHttpResultPredicate implemented in Predicates/; PresentationLayeringRules implemented in Rules/, verified against the pre-written CLAUDE.md spec (Architecture Test Contracts, Implementation Rules, and Changelog entry all matched the shipped implementation exactly — no discrepancy found, no edits required); T-143–T-146 added (6 new tests: T-143 fire path, T-144 pass path plus a companion HttpValidationProblemDetails fire-path case, T-145 fire path, T-146 pass path plus a companion vacuous-pass case); fixed a self-inflicted false-positive in the first T-144 fixture draft — the fixture's own "factory method" was itself constructing ProblemDetails via newobj in the same assembly, which the predicate correctly flagged since it carries no namespace exemption; reworked the fixture so the factory call is an unimplemented external stub, isolating the assertion to OrderEndpoints alone; 108/108 full ArchitectureTests.Tests suite passes, 0 build warnings/errors (state-map-phase)
- [2026-06-30] ApplicationPipelineRules added to architecture test contracts (three ICustomRule predicates: BehaviorsNeverReferenceConcreteInfrastructure via NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate — caller-supplied behavior-name set + forbidden-namespace set, infra-purity for TracingBehavior/ResilienceBehavior/CacheInvalidationBehavior; NoExistingBehaviorMatchesStreamRequestConstraint via NoGenericConstraintMatchesStreamRequestPredicate — this domain's fourth distinct Mono.Cecil technique, IL generic-parameter-constraint inspection, proving no IPipelineBehavior<,> implementor structurally matches IStreamRequest<TResponse>; NoHandRolledRetryLoopOutsideResilienceBehavior via NoTaskDelayOutsideResilienceBehaviorPredicate — Task.Delay Call/Callvirt fingerprint heuristic with a ResilienceBehavior self-exemption); new PipelineOrderAssertion public reflection helper added — the first SharedKernel.ArchitectureTests artifact that is not a ConditionList/ICustomRule, walking ServiceDescriptor entries off an unbuilt IServiceCollection to assert IPipelineBehavior<,> registration order, intended for consumption by 05.Application.Behaviors.Tests against the real ApplicationBehaviorsBuilder.Build() output; no new SK IDs; five new implementation rules added; depends on 05.Application P-220/P-221/P-222/P-224 for real-assembly verification only — WO-036 is design-only as of 2026-06-30, so design proceeds against contrived in-memory fixtures (same technique as SK.00.ServiceDefaultsGovernance/SK.00.HealthCheckConstantsGuard/SK.00.PresentationArchRules) — WO-036 P-225 (governance-arch-planner)
- [2026-06-30] CryptoIsolationRules and UnitOfWorkSeamRules added to architecture test contracts (WO-037 P-229): CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography via NoRawSymmetricCipherOutsideCryptographyPredicate — platform-wide generalization of the SK0301-backing predicate, banning direct AesGcm/Aes/SymmetricAlgorithm field/IL references plus RandomNumberGenerator calls (new DeclaringType-match surface) outside a SharedKernel.Cryptography-prefixed namespace, motivated by the 06.Persistence P-227 incident where a hand-rolled AesGcm usage was structurally invisible to SK0301 because that rule was only ever invoked against 03.Domain/05.Application, never against 06.Persistence itself; UnitOfWorkSeamRules.UnitOfWorkInterfacesRemainDistinct via UnitOfWorkInterfacesRemainDistinctPredicate — a negative-space/regression-guard rule (two-assembly, three-check: existence, identity-collapse, bidirectional base-interface) asserting SharedKernel.Application.Behaviors.IUnitOfWork and SharedKernel.Persistence.Abstractions.IUnitOfWork are never merged or made to inherit one another, protecting the local-seam pattern already proven for IAuthorizationContext/IIdempotencyKeyStore; SK0301 reconciled in place — NoAesCipherInDomainOrApplicationPredicate's exemption list narrowed from {SharedKernel.Persistence, SharedKernel.Security} to {SharedKernel.Cryptography} only, making it a caller-scoped special case of the new platform-wide rule rather than a contradictory duplicate; no new SK ID assigned; three new implementation rules added; depends on 06.Persistence P-227/P-228 for real-assembly verification only — both design-only as of 2026-06-30, so design proceeds against contrived in-memory fixtures matching the documented target shape (same technique as SK.00.ServiceDefaultsGovernance/SK.00.HealthCheckConstantsGuard/SK.00.PresentationArchRules/SK.00.ApplicationPipelineArchRules) — WO-037 P-229 (governance-arch-planner)
