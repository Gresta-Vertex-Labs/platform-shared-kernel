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
    Trigger   : (redesigned P-555, the SharedKernel.FeatureManagement OpenFeature migration —
                the package's own IFeatureManager/FeatureDefinition/FeatureVariant abstractions
                were deleted, so this rule no longer points at them) Two independent shapes:
                (1) a constructor/method parameter, field, or property declared as one of the
                four Microsoft evaluator interfaces — Microsoft.FeatureManagement.IFeatureManager,
                IVariantFeatureManager, IFeatureManagerSnapshot, or IVariantFeatureManagerSnapshot;
                (2) any reference to the static property OpenFeature.Api.Instance (the process-
                global OpenFeature API) — AddSharedKernelFeatureManagement registers an ISOLATED
                Api instance in DI (OpenFeature.Hosting's CreateIsolated()), so Api.Instance has
                no provider and silently returns every flag's default
    Fix       : Inject OpenFeature's IFeatureClient (registered scoped by
                AddSharedKernelFeatureManagement) and evaluate a typed
                SharedKernel.FeatureManagement.FeatureFlag<T> instead
    Exempt    : Compilations whose AssemblyName is exactly SharedKernel.FeatureManagement — the
                package's own internal OpenFeature provider adapter
                (Internal/MicrosoftFeatureManagementProvider.cs) legitimately bridges
                Microsoft.FeatureManagement.IVariantFeatureManager into OpenFeature

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
                (pre-P-557 design — field encryption has been interceptor-based since P-557 and is enabled
                with `UseFieldEncryption()` since P-558; the rule stays as a guard against a converter) detects the
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
                an override of OnModelCreating; the method body does not invoke base.OnModelCreating(...).
                (P-558: ApplyTenantFilters no longer exists — the tenant filter is a model-finalizing
                convention and applies regardless — so it no longer satisfies the rule.)
    Fix       : Call base.OnModelCreating(modelBuilder) in the override: it applies the entity
                configurations of the context's assembly, the Money mapping and key generation.
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

SK0014  ClosedGenericResiliencePipelineRegistration
    Category  : Usage
    Severity  : Warning
    Trigger   : A GenericNameSyntax whose Identifier.Text == "ResiliencePipeline" and whose
                TypeArgumentList.Arguments.Count == 1 (i.e. the closed- or open-generic form
                ResiliencePipeline<T>) appearing anywhere in production code — as a DI
                registration type argument (AddSingleton<ResiliencePipeline<TResponse>>() /
                AddScoped / AddTransient / AddKeyedSingleton), a constructor or method
                parameter type, a field type, or a local variable type. Syntax-only; no
                SemanticModel required — the arity-1 generic form is textually distinguishable
                from the correct arity-0 "ResiliencePipeline" simple name (Polly v8's
                non-generic type).
    Fix       : Register and resolve Polly v8 resilience pipelines via the non-generic
                Polly.ResiliencePipeline type, keyed by a string policy name
                (ResiliencePipelineProvider<string> / AddResiliencePipeline("policy-name", ...)).
                A closed-generic ResiliencePipeline<TResponse> registration silently falls back
                to a no-op pipeline whenever the resolved key does not exactly match the closed
                type used at the call site, defeating retry/circuit-breaker protection with no
                runtime warning.
    Suppress  : Per-site via #pragma warning disable SK0014 only when a third-party library API
                genuinely requires the closed-generic Polly type; document the rationale inline.
    Note      : Introduced WO-038 P-235. Fires globally — a closed-generic ResiliencePipeline<T>
                registration is unsafe in any assembly, not only SharedKernel.Application; no
                namespace exemption is defined.

SK0015  StreamPipelineBehaviorMisregistration
    Category  : Usage
    Severity  : Warning
    Trigger   : An InvocationExpressionSyntax whose simple method name is "AddTransient",
                "AddScoped", or "AddSingleton" has a first type argument whose resolved symbol
                is the open generic MediatR.IPipelineBehavior<,> (arity 2) and a second type
                argument whose resolved ITypeSymbol.AllInterfaces includes an entry whose
                OriginalDefinition matches MediatR.IStreamPipelineBehavior<,> (arity 2) —
                found anywhere EXCEPT inside a MethodDeclarationSyntax whose Identifier.Text is
                exactly "AddStreamingBehaviors". Requires SemanticModel.GetSymbolInfo on both
                type-argument syntax nodes to resolve interface implementation — the second SK
                rule in this domain (after SK0011) requiring semantic model resolution.
    Fix       : Remove the ad-hoc IPipelineBehavior<,> registration for the streaming behavior
                type and call ApplicationBehaviorsBuilder.AddStreamingBehaviors() instead.
                MediatR dispatches IStreamRequest<TResponse> through IStreamPipelineBehavior<,>,
                never through IPipelineBehavior<,> — a streaming behavior registered against the
                wrong interface is silently never invoked.
    Suppress  : Per-call-site via #pragma warning disable SK0015 only for a deliberate hybrid
                unary/streaming behavior type; document why the type intentionally implements
                both interfaces.
    Note      : Introduced WO-038 P-235. The self-exemption is method-name-scoped
                ("AddStreamingBehaviors"), not namespace-scoped — the canonical builder method
                is the single sanctioned call site for streaming-behavior registration.

SK0016  RequestTypeShortNameUsage
    Category  : Design
    Severity  : Warning
    Trigger   : A MemberAccessExpressionSyntax whose Name is "Name" and whose Expression is a
                TypeOfExpressionSyntax (i.e. typeof(X).Name), found inside a file whose
                namespace declaration (NamespaceDeclarationSyntax or
                FileScopedNamespaceDeclarationSyntax) starts with "SharedKernel.Application"
                (covers both SharedKernel.Application and SharedKernel.Application.Behaviors),
                UNLESS the member access is the right-hand operand of a coalesce expression (??)
                whose left-hand operand is typeof(X).FullName for the syntactically-identical X
                (same TypeArgumentSyntax/TypeSyntax text). Syntax-only; no SemanticModel
                required — the FullName-coalesce companion is a textual match on the left
                operand.
    Fix       : Use typeof(TRequest).FullName ?? typeof(TRequest).Name for any metric tag, log
                scope key, or cache key that must remain unique across assemblies — two request
                types with the same short name in different namespaces/assemblies collide under
                typeof(X).Name alone.
    Suppress  : Per-call-site via #pragma warning disable SK0016 when the short name is
                genuinely sufficient (e.g. a user-facing display string where collision risk is
                irrelevant); document the rationale inline.
    Note      : Introduced WO-038 P-235, closing the P-231 fix's mechanical-enforcement gap.
                Namespace-scoped to SharedKernel.Application* by design — the collision risk is
                specific to MediatR request-type tag/key construction, which lives exclusively
                in this domain.

SK0017  CommandImplementsCacheableQuery
    Category  : Design
    Severity  : Warning
    Trigger   : A ClassDeclarationSyntax, RecordDeclarationSyntax, or StructDeclarationSyntax
                (non-abstract) whose declared symbol's INamedTypeSymbol.AllInterfaces contains
                an interface matching simple name "ICommandBase" (namespace starting with
                "SharedKernel.Application") AND also contains an interface whose
                OriginalDefinition matches the open generic "ICacheableQuery<TResponse>"
                (arity 1, same namespace prefix) — directly or transitively through a narrower
                interface (e.g. ICommand<TResponse> : ICommandBase). Requires SemanticModel
                resolution of the full interface closure — a BaseList simple-name check is
                insufficient because ICommandBase is typically implemented transitively.
    Fix       : Remove ICacheableQuery<TResponse> from the command type. Caching is
                queries-only by design — a command must never be cacheable. If a read-shaped
                result genuinely needs caching, model it as a query instead.
    Exempt    : Types carrying the abstract modifier (Modifiers.Any(SyntaxKind.AbstractKeyword))
                are excluded — the same exemption already applied by SK0009.
    Suppress  : Per-type via #pragma warning disable SK0017 with an inline comment documenting
                the rationale; fires globally, no suppression namespace.
    Note      : Introduced WO-040 P-248, closing one of the three remaining "not mechanically
                enforced — code review must catch this" callouts in 05.Application/CLAUDE.md's
                Hard Violations section. Third SK analyzer in this domain requiring a semantic
                interface-closure check, after SK0011 and SK0015. Runs inside a CONSUMING
                microservice's own compilation — the violation is a command/query type
                declaration, which never occurs inside SharedKernel.Application.Behaviors itself.

SK0018  QueryImplementsInvalidatesCache
    Category  : Design
    Severity  : Warning
    Trigger   : A ClassDeclarationSyntax, RecordDeclarationSyntax, or StructDeclarationSyntax
                (non-abstract) whose declared symbol's AllInterfaces contains an interface whose
                OriginalDefinition matches the open generic "IQuery<TResponse>" (arity 1,
                namespace prefix "SharedKernel.Application"), does NOT contain an interface
                matching simple name "ICommandBase" (same namespace prefix), AND also contains
                an interface matching simple name "IInvalidatesCache" (same namespace prefix).
    Fix       : Remove IInvalidatesCache from the query type. Cache invalidation is
                commands-only by design — a pure query must never invalidate cache entries as
                a side effect. If invalidation is genuinely required, model the operation as a
                command instead.
    Exempt    : Types carrying the abstract modifier are excluded (same exemption as SK0017).
                A type implementing ICommandBase alongside IQuery<TResponse> and
                IInvalidatesCache does NOT trigger SK0018 — that ICommandBase/ICacheableQuery
                distinction belongs to SK0017, not this rule; the two rules are mutually
                exclusive by the "does NOT contain ICommandBase" guard.
    Suppress  : Per-type via #pragma warning disable SK0018 with an inline comment documenting
                the rationale; fires globally, no suppression namespace.
    Note      : Introduced WO-040 P-248. Fourth SK analyzer in this domain requiring a semantic
                interface-closure check. Structural converse of SK0017 — together the two rules
                enforce the platform's queries-cache / commands-invalidate split documented in
                05.Application/CLAUDE.md.

SK0019  RetryableRequestWithoutIdempotency — REMOVED (P-544)
    Note      : Retracted when SharedKernel.Application.Behaviors dropped ResilienceBehavior
                and IRetryableRequest entirely (05.Application's redesign, P-544) — the rule's
                target type no longer exists. Removal recorded in
                SharedKernel.Analyzers/AnalyzerReleases.Unshipped.md under "### Removed Rules"
                per the standard release-tracking format (RS2007/RS2008 stay clean). Do not
                reintroduce this ID for an unrelated rule — retired diagnostic IDs are never
                recycled on this platform.

SK0007  RedisChannelServiceMessagingSubstitute
    Category  : Design
    Severity  : Warning
    Trigger   : IRedisChannelService (SharedKernel.Caching.Redis.PubSub since P-547) appears as
                a constructor parameter, field declaration, or property declaration in a class
                whose name or enclosing namespace contains any of the substrings: "Command",
                "Event", "DomainEvent", "IntegrationEvent" (case-sensitive substring match).
                The type is matched by its simple name whether written bare, namespace-qualified
                (SharedKernel.Caching.Redis.PubSub.IRedisChannelService) or global::-qualified
                (QualifiedNameSyntax / AliasQualifiedNameSyntax unwrap to the right-most name), and
                inside a nullable type. Signals inappropriate use of Redis pub/sub as a substitute
                for a durable IMessageBus.
    Suppress  : Inside any namespace starting with "SharedKernel.Caching" — the service's own
                definition and its provider package may reference IRedisChannelService freely.
                Suppression uses the SyntaxNode.Parent namespace walk (same as SK0001).
    Fix       : Inject IMessageBus (SharedKernel.Messaging.Abstractions) for commands,
                domain events, and integration events. Reserve IRedisChannelService for
                ephemeral, non-durable, cache-adjacent signaling only (cache invalidation itself
                travels over the FusionCache backplane, not this service).
    Note      : Severity escalation to Error is gated on field confirmation of zero false
                positives on the "DomainEvent" substring match — some projects use
                "IDomainEventHandler" as a class name suffix that is not a misuse.
    Exemption : Classes within SharedKernel.Caching* namespaces are always exempt.
                Any additional exemption must be documented in 00.Governance/CLAUDE.md.

SK0020  DirectILoggerExtensionMethodUsage
    Category  : Design
    Severity  : Warning
    Trigger   : An InvocationExpressionSyntax whose invoked method resolves (via
                SemanticModel.GetSymbolInfo) to a method whose ContainingType is exactly
                "Microsoft.Extensions.Logging.LoggerExtensions" and whose Name starts with
                "Log" (covers LogTrace/LogDebug/LogInformation/LogWarning/LogError/
                LogCritical and any future same-family extension method added to that static
                class), OR whose ContainingType is exactly "Microsoft.Extensions.Logging.ILogger"
                and whose Name == "Log" (covers the base interface's Log<TState> call site,
                e.g. logger.Log(LogLevel.Information, eventId, state, exception, formatter)).
                Requires SemanticModel resolution — the method-name family (LogInformation,
                LogWarning, etc.) is common enough across unrelated logging frameworks
                (Serilog's ILogger, NLog, custom ILogger-shaped wrapper types) that a
                syntax-only simple-name check would produce unacceptable false positives;
                the exact ContainingType match is the discriminator.
    Fix       : Author the log statement via the [LoggerMessage] source-generated
                partial-method pattern (Microsoft.Extensions.Logging.LoggerMessageAttribute)
                with an explicit EventId inside the calling assembly's domain-reserved range
                (SharedKernel.Primitives.Logging.LoggingEventIdRanges, P-249) instead of a
                direct ILogger extension-method call.
    Exempt    : Namespace SharedKernel.Testing (and sub-namespaces) — the in-memory
                ILogger/ILoggerFactory test double (P-258) legitimately implements/exercises
                the ILogger surface directly as its own subject under test. Suppression uses
                the established SyntaxNode.Parent namespace walk (same as SK0001/SK0007).
                Generated code is never analyzed — the analyzer calls
                context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None), so the
                compiler-generated partial-method bodies [LoggerMessage]'s own source generator
                emits (which internally call ILogger.Log) are never flagged; without this guard,
                every correct [LoggerMessage] declaration would ironically trigger SK0020 against
                its own generated implementation.
    Suppress  : Per-call-site via #pragma warning disable SK0020 when a direct call is
                genuinely required (e.g., a thin adapter around a third-party library that only
                accepts a raw ILogger); document the rationale inline.
    Note      : Introduced WO-041 P-250. Implemented in the same analyzer class as SK0021
                (LoggingAuthoringStyleAnalyzer) — a single DiagnosticAnalyzer emitting two
                DiagnosticDescriptors, since both diagnostics encode the same platform logging
                standard ("always [LoggerMessage], never hand-rolled") and share the
                GeneratedCodeAnalysisFlags.None guard and the SharedKernel.Testing exemption.
                This is the first two-diagnostics-one-analyzer-class shape in this domain;
                every prior SK analyzer was one class per ID.

SK0021  HandWrittenLoggerMessageDefineDelegate
    Category  : Design
    Severity  : Warning
    Trigger   : An InvocationExpressionSyntax whose Expression is a MemberAccessExpressionSyntax
                with an Expression identifier text of "LoggerMessage" (or the rightmost segment
                of a qualified "Microsoft.Extensions.Logging.LoggerMessage" name) and a Name
                identifier text starting with "Define" (covers Define and DefineScope across all
                generic arities: Define, Define<T1>, Define<T1,T2>, Define<T1,T2,T3>,
                Define<T1,T2,T3,T4>). Syntax-only — no SemanticModel required; the qualified
                "LoggerMessage.Define*" call shape is specific enough that a simple-name match
                carries negligible false-positive risk, consistent with the domain's
                cost-conscious "escalate to semantic model only when truly ambiguous" convention
                (e.g. SK0703's AddSingleton<T> simple-name match).
    Fix       : Replace the hand-written static Action<ILogger,...> delegate field plus its
                LoggerMessage.Define(...) initializer with a [LoggerMessage]-attributed static
                partial method declaration. The source generator produces the equivalent
                delegate-caching machinery automatically, with compile-time message-template
                validation the hand-written form does not get.
    Exempt    : Namespace SharedKernel.Testing (and sub-namespaces) — same rationale and
                SyntaxNode.Parent namespace-walk mechanism as SK0020, applied for symmetry, even
                though no known legitimate use exists there today. The
                ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None) guard is shared
                with SK0020 in the same analyzer class — not actually load-bearing for this rule
                today (LoggerMessage.Define is an implementation detail internal to the source
                generator's own emitted code, not something the generator's public-facing
                partial-method body calls directly), but applied uniformly across both
                diagnostics for consistency and future-proofing.
    Suppress  : Per-call-site via #pragma warning disable SK0021; document the rationale inline.
    Note      : Introduced WO-041 P-250. Implemented in LoggingAuthoringStyleAnalyzer alongside
                SK0020 — see that entry for the shared-analyzer-class rationale.

SK0022  CrossCuttingMagicStringLiteral
    Category  : Usage
    Severity  : Warning
    Trigger   : A raw string-literal token (LiteralExpressionSyntax of kind
                StringLiteralExpression) supplied at one of four recognized cross-cutting
                call-site shapes, each requiring SemanticModel.GetSymbolInfo to resolve the
                receiver/method/indexer to its EXACT ContainingType (syntax-only simple-name
                matching on "SetTag"/"GetSection"/etc. would collide with unrelated types):
                  (1) HTTP header indexer/setter — element-access or .Add/
                      .TryAddWithoutValidation invocation resolved to
                      System.Net.Http.Headers.HttpHeaders (or HttpRequestHeaders/
                      HttpResponseHeaders/HttpContentHeaders) or
                      Microsoft.AspNetCore.Http.IHeaderDictionary.
                  (2) Activity.SetBaggage / Activity.SetTag — invocation resolved to
                      System.Diagnostics.Activity.SetBaggage or .SetTag.
                  (3) IConfiguration.GetSection — invocation resolved to
                      Microsoft.Extensions.Configuration.IConfiguration.GetSection (or its
                      ConfigurationExtensions static-extension-method overload).
                  (4) ClaimsPrincipal/Claim comparison — a binary equality expression or
                      .Equals(...) against System.Security.Claims.Claim.Type, or an invocation
                      on System.Security.Claims.ClaimsPrincipal/ClaimsIdentity (HasClaim,
                      FindFirst, FindAll) whose string argument is the checked position.
                The rule flags the LITERAL SYNTAX SHAPE only — never a resolved value or
                declaring-class name. Any expression that is not itself a
                StringLiteralExpression at the checked position (an IdentifierNameSyntax,
                MemberAccessExpressionSyntax, or any other non-literal expression) passes
                clean, regardless of which class declares the referenced field — a
                domain-local constants class (mirroring SecurityClaimTypes,
                WebhookSignatureHeaders, HubGroupNaming) is just as valid as a reference to
                01.Core's WellKnownHeaders/WellKnownBaggageKeys.
    Fix       : Declare a named constant instead of the raw literal: use
                SharedKernel.Primitives.Propagation.WellKnownHeaders/WellKnownBaggageKeys
                (01.Core) if the value is a platform-shared correlation/tenant identifier
                consumed across multiple domains, or a domain-local static readonly/const
                constants class if it is specific to this package. The analyzer message
                references both remediation paths — it cannot semantically distinguish which
                applies; that judgment is left to the developer per the root CLAUDE.md
                decision guide.
    Exempt    : None — no suppression namespace. Fires globally, like SK0014/SK0017–19: a
                domain-local constants-holder class already satisfies the rule (it is
                referenced via Identifier/MemberAccess, never a literal), so there is no
                legitimate "exempt namespace" for a raw literal at any of these four shapes.
    Suppress  : Per-call-site via #pragma warning disable SK0022; document the rationale
                inline (e.g. a genuinely one-off literal with no plausible cross-call-site
                reuse).
    Note      : Introduced WO-042 P-264. Implemented in a SINGLE DiagnosticAnalyzer class
                (CrossCuttingMagicStringLiteralAnalyzer) covering all four call-site shapes
                under one DiagnosticDescriptor — the same underlying rule ("never a raw
                literal at a cross-cutting call site"), not four distinct standards. Design/
                implementation/tests for this phase used CONTRIVED fixtures only, per the
                phase's own scope boundary — see Cross-Domain Dependencies in
                00.Governance/state-map.md. As of the SK.00.MagicStringGuard closeout
                (2026-07-16), 01.Core P-259 has since SHIPPED — the real
                SharedKernel.Primitives.Propagation.WellKnownHeaders/WellKnownBaggageKeys
                types exist on disk — and P-260/P-261/P-262/P-263 have all landed too, so
                real-assembly wiring is now unblocked; it remains undone as of this closeout
                and is tracked as a follow-up, not implemented as part of this phase.

SK0023  NonSingletonAmazonS3ClientRegistration
    Category  : Usage
    Severity  : Warning
    Trigger   : An InvocationExpressionSyntax whose simple method name is "AddScoped" or
                "AddTransient" contains a type argument whose simple name is "IAmazonS3"
                (exact match, extracted the same way SK0703 extracts its IMessageBus/
                IEventPublisher type argument: GenericNameSyntax.TypeArgumentList
                .Arguments[0] as an IdentifierNameSyntax). Covers both the one-argument
                form AddScoped<IAmazonS3>(factory) and the two-argument form
                AddTransient<IAmazonS3, AmazonS3Client>(). Syntax-only; no SemanticModel
                required.
    Fix       : Register IAmazonS3 via AddSingleton. The client is thread-safe and
                connection/credential-pooled internally — scoped or transient registration
                constructs a new client (and thus a new connection pool) per resolution,
                which is expensive under load and can exhaust ephemeral ports.
    Suppress  : Per-call-site via #pragma warning disable SK0023 when a test fixture or a
                genuinely short-lived client is required; document the rationale inline.
    Note      : No suppression namespace — SK0023 fires globally, mirroring SK0703's/
                SK0014's "fires globally" convention: IAmazonS3 must be a singleton
                wherever it is registered. Since P-559 SharedKernel.Storage.S3/.Obs no longer
                register IAmazonS3 in DI at all (one client per connection inside an internal
                keyed singleton), so the rule now guards a consuming service's own client.
                Introduced in WO-043 P-271 — the structural inverse of SK0703
                (MessageBusSingletonRegistration, which flags AddSingleton for a type that
                must be scoped; SK0023 flags AddScoped/AddTransient for a type that must be
                singleton). Next sequential ID in the SK0001–SK00N general-purpose block
                (SK0022 was the prior ID) — a single-purpose, domain-adjacent rule; no new
                08xx ID block was opened for one rule, following the SK0011/SK0013
                precedent that a lone domain-specific rule stays in the sequential block.

SK0024  RawSearchFieldNameLiteral
    Category  : Usage
    Severity  : Warning
    Trigger   : A string-literal argument (LiteralExpressionSyntax of kind
                StringLiteralExpression) supplied at the field-name parameter position of
                one of eleven recognized SharedKernel.Search.Abstractions call-site shapes,
                each requiring SemanticModel.GetSymbolInfo to resolve the invoked method to
                its exact declaring type (syntax-only simple-name matching on "OrderBy"/
                "Where"/"In"/"Exists" would collide catastrophically with LINQ's own
                Enumerable/Queryable extension methods of the same names):
                  IQueryBuilder<TDocument> / SearchQueryBuilder<TDocument> — six methods:
                    .OrderBy(string field), .OrderByDescending(string field),
                    .SearchingIn(params string[] fields),
                    .Faceting(params string[] facetFields),
                    .WithNumericFacetStats(params string[] facetFields),
                    .Returning(params string[] fields). For the four params string[]
                    shapes, EVERY argument expression supplied at that parameter position
                    is checked individually — covers both SearchingIn("a", "b") and
                    SearchingIn(new[] { "a", "b" })/collection-expression forms.
                  SearchFilter static factories — five methods, field always at argument
                    position 0: .Eq(string field, SearchValue value),
                    .Ne(string field, SearchValue value),
                    .In(string field, params SearchValue[] values),
                    .Between(string field, SearchValue? from, SearchValue? to, bool, bool),
                    .Exists(string field).
                The rule flags the LITERAL SYNTAX SHAPE only — mirroring SK0022's
                declaring-class-agnostic discriminator exactly. A nameof(...) expression
                compiles to an InvocationExpressionSyntax, not a StringLiteralExpression, so
                it passes automatically without any resolved-value inspection; a reference
                to a domain-local field-constants class member passes identically.
    Fix       : Reference the field name via nameof(TDocument.PropertyName) or a
                domain-local field-constants class member populated from nameof(...) —
                never a raw string literal. This mirrors the platform's existing
                self-supplied-surface pattern (ISearchDocument.DocumentId,
                ILoggableRequest<TResponse>, ICacheableQuery.CacheKey) applied to refactor
                safety instead of redaction.
    Suppress  : Per-call-site via #pragma warning disable SK0024 when a field name is
                genuinely dynamic (e.g., driven by a runtime-configured facet list read
                from IConfiguration) and cannot be nameof()-backed; document the rationale
                inline.
    Note      : Introduced WO-044 P-278. Motivating hazard (09.Search/CLAUDE.md D-10): a
                typo'd field name is a REJECTED filter on Meilisearch (visible —
                SearchErrors.FieldNotFilterable/FieldNotSortable/FieldNotFacetable, checked
                against the registered SearchIndexDefinition before any I/O) and a SILENT
                ZERO-RESULT on ElasticSearch whenever the typo happens to also be a
                syntactically legal but nonexistent field reference at the ES query-DSL
                level — an asymmetry unique among the platform's magic-string hazards
                (SK0022's four targets fail identically loud, or simply do not compile, on
                both sides of whatever boundary they cross). The invisible-failure side is
                what earns this its own dedicated rule rather than folding into SK0022. The
                domain's own brain (D-10) states this rule "must land inside WO-044 rather
                than being deferred" for exactly this reason. Requires SemanticModel — the
                domain's eighth analyzer requiring semantic resolution, after SK0011,
                SK0015, SK0017–SK0019, SK0020, and SK0022.

SK0025  ObsoleteElasticsearchClientUsage
    Category  : Usage
    Severity  : Warning
    Trigger   : A SemanticModel.GetSymbolInfo resolution — registered on IdentifierNameSyntax
                and GenericNameSyntax nodes only (CORRECTED at implementation time from the
                originally-drafted "any IdentifierNameSyntax, GenericNameSyntax,
                QualifiedNameSyntax, or UsingDirectiveSyntax name node" — see Note) — filtered
                to ITypeSymbol resolutions only, whose ContainingAssembly.Name is exactly
                "NEST" or "Elasticsearch.Net" (case-sensitive, matching the exact published
                NuGet package/assembly names). Covers a fully-qualified Nest.ElasticClient
                reference and a bare ElasticClient/ConnectionSettings/QueryContainer symbol
                usage after a using Nest; directive — the ContainingAssembly-based
                discriminator generalizes to every TYPE either deprecated package exposes
                without enumerating them individually.
    Fix       : Use Elastic.Clients.Elasticsearch (pinned 9.4.2 in
                SharedKernel.Search.ElasticSearch — the platform-sanctioned client)
                instead. NEST and Elasticsearch.Net are deprecated on nuget.org,
                feature-frozen since client 8.13, and their support window closed at
                end-2025.
    Suppress  : Per-call-site via #pragma warning disable SK0025; no legitimate use case
                is known — suppression exists only for the mechanical completeness the
                rest of the registry provides.
    Note      : Introduced WO-044 P-278, the platform's first EOL-third-party-package
                prohibition rule. No suppression namespace — fires globally, platform-wide,
                not scoped to 09.Search or any particular namespace: a consuming
                microservice adding NEST directly (not only
                SharedKernel.Search.ElasticSearch itself) is exactly as unsafe. Requires
                SemanticModel — a syntax-only simple-name check on
                ElasticClient/ConnectionSettings was rejected because those names are
                generic enough to plausibly collide with unrelated types in other
                libraries; the ContainingAssembly.Name check is the precise,
                collision-free discriminator, consistent with SK0002's/SK0013's/SK0020's
                "exact declaring type/assembly, not simple name" discipline.
                Elastic.Clients.Elasticsearch types resolve to a DIFFERENT
                ContainingAssembly.Name ("Elastic.Clients.Elasticsearch") and therefore
                never trip this rule — the explicit pass-path case (09.Search/CLAUDE.md's
                own Technology Stack table already documents this exact prohibition in
                prose; this rule is its mechanical enforcement).
                CORRECTED AT IMPLEMENTATION TIME (2026-07-24): the design-time Trigger
                prose (drafted before any real cross-assembly test fixture had been
                built) additionally named QualifiedNameSyntax and UsingDirectiveSyntax as
                registered node kinds and did not anticipate two genuine false-positive
                sources discovered while writing T-205/T-206: (1) the NAMESPACE symbol
                resolved for the bare "Nest"/"Elasticsearch" segment of a using directive
                or the left-hand side of a qualified name ALSO carries a
                ContainingAssembly.Name equal to "NEST"/"Elasticsearch.Net" in Roslyn's
                symbol model, so an ITypeSymbol filter is required or the rule
                double/triple-fires on every using directive and qualified-name prefix in
                addition to the real type reference; (2) the "var" contextual keyword in
                `var client = new ElasticClient();` is itself an IdentifierNameSyntax whose
                GetSymbolInfo resolves to the INFERRED type, so without an explicit
                `Identifier.ValueText == "var"` guard the rule double-fires on every
                implicitly-typed local whose initializer names a deprecated type. Both
                findings were confirmed empirically (compile, run the rule, read the
                actual reported diagnostic count) via the same "SEPARATE-ASSEMBLY test
                technique" this rule's own test file introduces — the first analyzer test
                file in this project needing a genuinely separate compiled reference
                assembly with a specific AssemblyName (via
                Microsoft.CodeAnalysis.Testing.SolutionState.AdditionalProjects, keyed by
                an assembly name of literally "NEST"/"Elasticsearch.Net", rather than the
                established in-compilation-stub technique used by every other analyzer
                test file in this project — an in-compilation stub resolves to THIS TEST
                ASSEMBLY's own name, never to the literal deprecated-package assembly name
                the rule actually checks). Registering only IdentifierNameSyntax/
                GenericNameSyntax (never QualifiedNameSyntax) is not a narrowing of
                coverage — the rightmost segment of a qualified name (e.g. "ElasticClient"
                in "Nest.ElasticClient") is itself an IdentifierNameSyntax node visited
                independently by Roslyn's syntax walker, so every real type usage this
                rule needs to catch is still visited exactly once.

SK0026  RawIntelligenceProviderClientConstructorInjection
    Category  : Usage
    Severity  : Warning
    Trigger   : A ConstructorDeclarationSyntax parameter type resolved via
                SemanticModel.GetSymbolInfo to exactly one of three full type names:
                "Qdrant.Client.QdrantClient", "Milvus.Client.MilvusClient", or
                "Microsoft.SemanticKernel.Kernel" — unless the parameter's enclosing
                type sits inside a namespace (via the established SyntaxNode.Parent
                ancestor walk, same pattern as SK0001/SK0007/SK0013) starting with
                that type's OWNING provider package: "SharedKernel.AI.Qdrant" for
                QdrantClient, "SharedKernel.AI.Milvus" for MilvusClient,
                "SharedKernel.AI.SemanticKernel" for Kernel. A QdrantClient parameter
                inside SharedKernel.AI.Milvus still fires — there is no cross-exemption
                between the three owning packages.
    Fix       : Inject the neutral SharedKernel.AI.Abstractions contracts instead
                (IVectorCollection<TRecord>, IEmbeddingGenerator, ISemanticKernel,
                IVectorCollectionProvisioner, IVectorProviderDescriptor,
                ICompletionProviderDescriptor). If a raw-client escape hatch is
                genuinely required, it must follow the three-gate pattern documented
                in 10.Intelligence/CLAUDE.md (opt-in builder call, startup Warning,
                governance architecture test) and its XML doc must state IN CAPITALS
                that it bypasses tenant scoping.
    Suppress  : Per-constructor via #pragma warning disable SK0026; document the
                rationale inline.
    Note      : Introduced WO-045 P-286, the platform's first 10.Intelligence-domain
                diagnostic alongside SK0027. Requires SemanticModel exact-full-type-name
                resolution rather than SK0013's syntax-only simple-name check because
                "Kernel" is a highly collision-prone simple name — many unrelated types
                (a convolution kernel, an OS-kernel abstraction, a compute-shader
                kernel) could plausibly share that identifier elsewhere in a consuming
                service's own dependency graph; QdrantClient/MilvusClient alone would
                likely be safe as syntax-only checks, but all three are resolved
                uniformly via the semantic model for implementation consistency. This
                domain's tenth semantic-model analyzer (after SK0011, SK0015,
                SK0017–SK0019, SK0020, SK0022, SK0024, SK0025).
    CORRECTED AT IMPLEMENTATION TIME (2026-07-27, WO-048 Milvus retraction — SK.00.IntelligenceTopology
                closeout): the Trigger prose above was drafted (WO-045 P-286, 2026-07-21) against
                a THREE-provider design (Qdrant/Milvus/SemanticKernel). Before this phase's own
                implementation session, arch-lead ratified WO-048: `SharedKernel.AI.Milvus` was
                permanently retracted — `Milvus.Client` never shipped a stable release — and does
                not exist on disk and never will (verified directly: no
                `10.Intelligence/SharedKernel.AI.Milvus/` directory; `IntelligenceWellKnown` carries
                no `MilvusProviderName`). The SHIPPED analyzer resolves exactly TWO full type names
                — "Qdrant.Client.QdrantClient" (exempt inside "SharedKernel.AI.Qdrant") and
                "Microsoft.SemanticKernel.Kernel" (exempt inside "SharedKernel.AI.SemanticKernel")
                — never "Milvus.Client.MilvusClient", and there is no "SharedKernel.AI.Milvus"
                owning-package exemption. The "no cross-exemption between the three owning
                packages" sentence above is accordingly two owning packages, not three, in the
                real, shipped code. This entry is intentionally left as originally drafted rather
                than rewritten in place — the two-engine/three-provider design history remains
                accurate context for why the analyzer resolves via exact full-type-name matching
                per client rather than a single shared exemption — this note is the authoritative
                as-shipped correction.

SK0027  RawIntelligenceIdentifierLiteral
    Category  : Usage
    Severity  : Warning
    Trigger   : A raw string-literal argument (LiteralExpressionSyntax of kind
                StringLiteralExpression) supplied at the collection-name/field-name/
                embedding-model-id parameter position of one of eight recognized
                SharedKernel.AI.Abstractions call-site shapes, each requiring
                SemanticModel.GetSymbolInfo to resolve the invoked member to its exact
                declaring type — the structural twin of SK0024's discriminator:
                  VectorFilter static factories — five methods, field always at
                    argument position 0: .Eq(string field, VectorValue value),
                    .Ne(string field, VectorValue value),
                    .In(string field, params VectorValue[] values),
                    .Between(string field, VectorValue? from, VectorValue? to, bool, bool),
                    .Exists(string field).
                  IVectorCollectionProvisioner — three methods, collectionName always
                    at argument position 0: .CollectionExistsAsync(string
                    collectionName, ct), .DeleteCollectionAsync(string collectionName,
                    ct), .ProbeAsync(string collectionName, ct).
                  VectorCollectionDefinition.Create(string name, string
                    embeddingModelId, int dimension, VectorDistanceMetric metric,
                    IReadOnlyList<VectorFieldDefinition> fields) — BOTH name (position
                    0) and embeddingModelId (position 1) checked independently.
                  VectorCollectionDefinitionBuilder — two methods:
                    .EmbeddingModel(string modelId, int dimension) (position 0),
                    .Field(string name, VectorFieldKind kind, bool filterable = false)
                    (position 0).
                The rule flags the LITERAL SYNTAX SHAPE only — declaring-class-agnostic,
                identical discriminator to SK0022/SK0024: nameof(...) and any reference
                to a domain-local identifier-constants class member pass automatically.
                VectorCollectionCutoverRequest.StagingCollectionName/.LiveCollectionName
                are object-initializer PROPERTY assignments, not method-call arguments,
                and are explicitly OUT OF SCOPE for this rule's call-site-argument-
                position detection technique.
    Fix       : Reference the identifier via nameof(...) where applicable, or a
                domain-local static identifier-constants class member — never a raw
                string literal repeated at each call site.
    Suppress  : Per-call-site via #pragma warning disable SK0027 when the identifier is
                genuinely dynamic (e.g., a runtime-configured collection name read from
                IConfiguration) and cannot be constant-backed; document the rationale
                inline.
    Note      : Introduced WO-045 P-286. Motivating hazard is DIFFERENT FROM and
                sharper than SK0024's — do not cross-reference the two entries' rationale,
                the same discipline already established between SK0022 and SK0024. A
                typo'd collection name is IntelligenceErrors.CollectionNotFound
                (visible, checked before I/O) on BOTH providers per the ratified
                contract — symmetric-visible, unlike SK0024's Meilisearch-visible/
                ElasticSearch-silent asymmetry. The sharper hazard here is
                embeddingModelId: a VectorCollectionDefinition.Create/
                VectorCollectionDefinitionBuilder.EmbeddingModel call site and the
                record/query call sites that must independently supply the SAME
                embeddingModelId string have no shared compile-time link — a
                copy-pasted, typo'd literal at both places passes the
                IVectorRecord.ModelId == VectorCollectionDefinition.EmbeddingModelId
                guard cleanly, silently embedding/querying the corpus under a phantom
                model-identity string with zero engine-detectable error on either
                provider — Domain Invariant #1's exact "no engine can detect a
                model-identity mismatch" hazard, reproduced by a literal-copy-paste-typo
                instead of a genuine two-model mixup. This domain's eleventh
                semantic-model analyzer (after SK0011, SK0015, SK0017–SK0019, SK0020,
                SK0022, SK0024, SK0025, SK0026).

SK0028  NonDeterministicApiUsageInsideWorkflow
    Category  : Design
    Severity  : Warning
    Trigger   : Requires SemanticModel.GetSymbolInfo exact-type/exact-member resolution
                throughout — this domain's TWELFTH semantic-model analyzer (after SK0011,
                SK0015, SK0017–SK0019, SK0020, SK0022, SK0024, SK0025, SK0026, SK0027).
                Scope is determined by TYPE ATTRIBUTION/INHERITANCE, not namespace — the
                first analyzer in this domain to scope its trigger this way instead of the
                established SyntaxNode.Parent namespace-ancestor walk: a
                ClassDeclarationSyntax (or RecordDeclarationSyntax) whose declared symbol
                either carries a `[Workflow]` attribute (resolved to
                Temporalio.Workflows.WorkflowAttribute) or has WorkflowBase
                (SharedKernel.Workflows.Temporal.Authoring.WorkflowBase) anywhere in its
                base-type chain is IN SCOPE. A type with ActivityBase
                (SharedKernel.Workflows.Temporal.Authoring.ActivityBase) anywhere in its
                base chain, or carrying `[Activity]`, is checked FIRST and is unconditionally
                OUT OF SCOPE regardless of the workflow-scope check — the narrow SK0001
                carve-out this rule exists to encode. Within an in-scope type, fires on any
                of SEVEN forbidden shapes, each resolved to its exact declaring type/member
                (never a syntax-only simple-name match, to avoid collisions with unrelated
                same-named APIs a consuming service's own code might declare):
                  (1) DateTime.UtcNow / DateTime.Now / DateTimeOffset.UtcNow /
                      DateTimeOffset.Now — the same four-property set
                      DoesNotCallSystemClockPredicate already matches for domain purity,
                      resolved here via SemanticModel instead of IL.
                  (2) System.Guid.NewGuid() — exact static method resolution.
                  (3) `new Random()` — ObjectCreationExpressionSyntax resolved to
                      System.Random.
                  (4) System.Threading.Tasks.Task.Run / .Delay (static methods), and a
                      ConfigureAwait(false) invocation whose receiver resolves to
                      System.Threading.Tasks.Task or ValueTask (any generic arity) —
                      grouped under one shared "escapes the deterministic scheduler" trigger.
                  (5) Any member access resolved to ContainingType System.Environment or
                      System.IO.File (covers Environment.*/File.* in one check per type,
                      mirroring NoDbContextTransactionInApplicationPredicate's
                      DeclaringType-family matching style).
                  (6) A constructor parameter whose type resolves to
                      SharedKernel.Primitives.IClock.
                  (7) A constructor parameter whose type resolves to the open generic
                      Microsoft.Extensions.Logging.ILogger<T>.
    Fix       : Use Workflow.UtcNow / Workflow.NewGuid() / Workflow.Random (Temporalio's own
                deterministic, replay-safe primitives) for (1)/(2)/(3); use
                Workflow.DelayAsync / Workflow.WaitConditionAsync and the SDK's own task
                combinators for (4); move any environment/filesystem access into an activity
                for (5); never inject IClock or ILogger<T> into a `[Workflow]` type — use
                Workflow.Logger (bound automatically by WorkflowBase.Logger) for logging and
                Workflow.UtcNow rather than IClock for time. Dependencies reach workflow code
                only through activities.
    Exempt    : Types deriving from ActivityBase or attributed `[Activity]` — a hard, positive
                exclusion checked FIRST, not merely "does not match the trigger-in scope."
                Inside an activity every one of these seven APIs is ordinary, correct code
                (IClock/ILogger<T> injection is in fact MANDATORY there, per SK0001) — see
                17.Workflows/CLAUDE.md's own "ACTIVITIES ARE ORDINARY CODE" note.
    Suppress  : Per-call-site via #pragma warning disable SK0028; document the rationale
                inline — no legitimate case is known inside a genuine `[Workflow]` type. A
                private helper method shared between a `[Workflow]` type and an `[Activity]`
                type via a common static utility class neither directly extends is the one
                plausible false-negative shape (see Note), not a reason to suppress inside
                the workflow itself.
    Note      : Introduced WO-046 P-290 — the single highest-value analyzer this domain can
                ship, because every one of these seven shapes compiles cleanly and fails only
                on REPLAY, in production, at an arbitrary time later, taking down every
                in-flight execution of that workflow type simultaneously (surfaced platform-
                wide as `WorkflowErrors.DeterminismViolation`/`Unexpected`, per
                17.Workflows/CLAUDE.md). Method-body-local analysis only — a shared helper
                method called from both workflow and activity code is NOT analyzed for
                cross-call violations; documented limitation, not a defect, consistent with
                this domain's established over-approximation-over-data-flow philosophy
                (HealthCheckTagIntegrityRules, NoInlineResultBranchBeforeHttpResultPredicate,
                RequestDurationRecordMissingOutcomeTagPredicate). `HttpClient`/general I/O are
                documented Hard Violations in 17.Workflows/CLAUDE.md too but are deliberately
                NOT part of this rule's seven-shape trigger set — WO-046's own acceptance
                criteria scope the rule to exactly these seven; extending detection to
                `HttpClient`/general I/O is a candidate follow-up, not implemented here.

SK0029  RawTemporalClientConstructorInjection
    Category  : Usage
    Severity  : Warning
    Trigger   : A ConstructorDeclarationSyntax parameter type resolved via
                SemanticModel.GetSymbolInfo to exactly one of four full type names:
                "Temporalio.Client.ITemporalClient", "Temporalio.Client.TemporalClient",
                "Temporalio.Worker.TemporalWorker", or "Temporalio.Client.WorkflowHandle"
                (any generic arity) — unless the parameter's enclosing type sits inside a
                namespace (via the established SyntaxNode.Parent ancestor walk, same pattern
                as SK0001/SK0007/SK0013) starting with "SharedKernel.Workflows.Temporal". A
                SINGLE shared exemption namespace prefix — structurally closer to SK0013's
                one-namespace-exemption shape than to SK0026's per-client-type/per-package
                exemption mapping, because 17.Workflows has exactly one owning package, not
                three siblings.
    Fix       : Inject IWorkflowDispatcher (to start/signal/query workflows) or
                IWorkflowHandle (to interact with an already-started execution) instead —
                both from SharedKernel.Workflows.Temporal's own abstraction surface. If a
                genuine Visibility-API/schedule/namespace-administration/Nexus need remains
                unmet by either, the sanctioned path is the three-gate
                ITemporalRawClientAccessor escape hatch (composition-root
                `.AllowRawClientAccess()` opt-in, a startup `Warning` at EventId 17012, and
                this same phase's `WorkflowTopologyRules` architecture test below) — never a
                raw constructor-injected Temporalio.* client type.
    Suppress  : Per-constructor via #pragma warning disable SK0029; document the rationale
                inline — no legitimate use case outside SharedKernel.Workflows.Temporal
                itself is known.
    Note      : Introduced WO-046 P-290, structurally identical to SK0013 (raw HttpClient,
                P-159) and SK0026 (raw vector-DB/model-SDK client, P-286) — the platform's
                third instance of the "raw third-party client injected outside its owning
                abstraction package" prohibition shape. This domain's thirteenth
                semantic-model analyzer (after SK0028).

SK0030  ResultOutcomeDiscarded
    Category  : Usage
    Severity  : Warning
    Trigger   : An ExpressionStatementSyntax whose Expression is either an
                InvocationExpressionSyntax or an AwaitExpressionSyntax, where
                SemanticModel.GetTypeInfo(...) resolves the expression's type — for
                the await case, Roslyn's own unwrapped "what does `await x` evaluate
                to" type — to a type whose AllInterfaces closure (on the resolved
                INamedTypeSymbol, plus the type itself) contains an interface
                matching simple name "IHasSuccessFlag" with ContainingNamespace
                starting with "SharedKernel.Primitives" (the zero-member marker
                interface both Result and Result<T> implement, added at
                SharedKernel.Primitives.Results.IHasSuccessFlag by P-230
                specifically to enable this kind of reflection-free static
                analysis). No other ExpressionStatementSyntax.Expression syntax
                kind is registered: AssignmentExpressionSyntax — covering BOTH a
                genuine variable/field re-assignment ("result = Foo();") and an
                explicit discard ("_ = Foo();") — is never inspected at all, so
                both pass structurally with zero dedicated discard-detection logic.
                Passing the value as an argument ("Bar(Foo());"), returning it
                ("return Foo();"), and using it as the receiver of a further
                member-access/method chain whose own OUTER expression's resolved
                type does not implement IHasSuccessFlag ("Foo().Match(...);" where
                Match returns void) all pass for the identical structural reason —
                only the OUTERMOST ExpressionStatementSyntax.Expression's resolved
                type is ever checked, never a nested sub-expression's type. A
                fluent chain whose OUTERMOST call still resolves to an
                IHasSuccessFlag-implementing type ("Foo().Map(x => x + 1);", where
                Map itself returns Result<TNew>) correctly still fires — the
                chain's final produced Result is genuinely discarded, a different
                but equally real instance of the same hazard.
    Fix       : Assign the result to a variable and branch on IsSuccess/IsFailure,
                return it to the caller, pass it into a consuming method, or —
                when the outcome is genuinely irrelevant at this call site — make
                that explicit with `_ = SomeMethodReturningResult();` instead of a
                bare statement.
    Edge case : `await FooAsync();` as a bare statement, where FooAsync returns
                Task<Result<T>> or ValueTask<Result<T>>, DOES fire — the
                AwaitExpressionSyntax's resolved type is the unwrapped Result<T>,
                which still implements IHasSuccessFlag and is still discarded.
                This is deliberate, not a defect: an awaited-but-unobserved Result
                outcome is exactly the CS4014 (unawaited Task) analogy this rule
                exists to generalize past Task itself, one level further down.
    Suppress  : Per-call-site via #pragma warning disable SK0030, or — preferred,
                since it requires no suppression comment at all — rewrite the bare
                statement as an explicit discard assignment
                (`_ = SomeMethodReturningResult();`).
    Note      : Introduced WO-049 P-299. Requires SemanticModel resolution — this
                domain's fourteenth semantic-model analyzer (after SK0011, SK0015,
                SK0017–SK0019, SK0020, SK0022, SK0024, SK0025, SK0026, SK0027,
                SK0028, SK0029). Deliberately does NOT register on
                ObjectCreationExpressionSyntax — Result/Result<T> in this platform
                are produced exclusively via static factory methods
                (Result.Success()/Result<T>.Failure(...), never a public
                constructor call) — or on ConditionalAccessExpressionSyntax
                (`maybeService?.ReturnsResult();`); both are documented, deliberate
                scope limitations for this phase (false-negative risk accepted,
                mirroring this domain's SK0708/HealthCheckTagIntegrityRules
                over-approximation/under-approximation precedent), not
                oversights — a future phase may extend coverage to either shape if
                a real gap is found. Severity escalation to Error is gated on the
                real-source audit (this phase's own Tests-phase acceptance
                criterion) finding zero false positives across the sampled,
                already-shipped 05.Application/06.Persistence/07.Messaging/
                17.Workflows source.

SK0031  RawSecurityContextConstructorInjection
    Category  : Usage
    Severity  : Warning
    Trigger   : A ConstructorDeclarationSyntax parameter type is a SimpleNameSyntax or
                IdentifierNameSyntax whose Identifier.Text is exactly one of
                "IHttpContextAccessor", "ClaimsPrincipal", "HttpContext" (exact match),
                unless the enclosing type sits inside a namespace (via the established
                SyntaxNode.Parent ancestor walk, same pattern as SK0001/SK0007/SK0013)
                starting with "SharedKernel.Security.Oidc" or "SharedKernel.Security.ApiKey".
                No SemanticModel required — syntax-only check, mirroring SK0013's exact shape.
    Fix       : Inject SharedKernel.Security.Abstractions.IUserContext (for identity) or
                ITenantProvider (for tenant identity) instead of a raw HttpContext-family
                type. Application-layer and domain-adjacent code must never reach past the
                platform's identity/tenant abstraction into ASP.NET Core hosting internals.
    Suppress  : Per-constructor via #pragma warning disable SK0031 when a raw HttpContext-family
                type is genuinely required (e.g., a middleware component); document the
                rationale inline.
    Note      : Introduced in WO-057 P-373, the platform's first 12.Security-domain diagnostic.
                Mirrors SK0013's (raw HttpClient, P-159) exact syntax-only shape and
                namespace-exemption technique — a domain-specific instance of the "raw
                framework/infrastructure primitive injected outside its owning abstraction
                package" prohibition family (SK0013, SK0026, SK0029). The
                "SharedKernel.Security.ApiKey" exemption namespace is included per this phase's
                own acceptance criteria even though no such package exists in the platform as
                of this phase (12.Security ships only .Abstractions and .Oidc) — a
                forward-looking, currently-vacuous exemption prefix; if a future API-key
                provider package is ever added under a different name, this exemption must be
                revised in the same PR, mirroring the maintenance-obligation discipline already
                established for SharedKernelLayeringRules's forbidden-term lists.
                CORRECTED AT IMPLEMENTATION TIME (2026-08-13, SK.00.SecurityContextGuard
                closeout): the paragraph above was drafted by the arch-planner before this
                phase's own implementation session began, at a time when
                SharedKernel.Security.ApiKey did not yet exist on disk. By implementation time,
                12.Security had already shipped WO-057's P-366-P-372 in full — verified directly
                on disk, not assumed: 12.Security/SharedKernel.Security.ApiKey/ contains real,
                shipped source (Validation/ApiKeyAuthenticationHandler.cs,
                Validation/ApiKeyUserContext.cs, Extensions/ApiKeyServiceCollectionExtensions.cs,
                among others), packed at v1.0.0. [P-546, 2026-09-16: ApiKeyUserContext has since
                been removed — the package now maps identities through an IUserContextMapper;
                the namespace exemption is unaffected.] The "forward-looking, currently-vacuous"
                framing is therefore stale — SharedKernel.Security.ApiKey is a real, published
                sibling provider package, not a hypothetical future one. No code change was
                required: the shipped RawSecurityContextConstructorInjectionAnalyzer's exemption
                check is a plain namespace-prefix string match against
                "SharedKernel.Security.ApiKey", which was already written generically enough to
                work correctly against the real package the moment it existed. This entry is
                left as originally drafted rather than rewritten in place, matching this
                domain's established "annotate, never silently rewrite" convention (see the
                SK0025/StorageTopologyRules/SearchTopologyRules/IntelligenceTopologyRules
                CORRECTED-note precedent) — this note is the authoritative as-shipped
                correction.

SK0032  CorsWildcardOriginWithCredentials
    Category  : Security
    Severity  : Warning
    Trigger   : A CorsPolicyBuilder-typed receiver (semantic-model type check via
                GetTypeInfo/GetSymbolInfo against Microsoft.AspNetCore.Cors.Infrastructure.
                CorsPolicyBuilder — a syntax-only name match would misfire on any unrelated
                type coincidentally exposing AllowAnyOrigin/AllowCredentials/SetIsOriginAllowed
                methods) that has an AllowCredentials() invocation AND, anywhere in the same
                scope (the same local variable/parameter/field, tracked via a lightweight
                data-flow walk over both a single fluent invocation chain and separate
                statement-level invocations against the same identifier within one method or
                lambda body — CorsPolicyBuilder's fluent methods are just as often called as
                independent statements inside an `options.AddPolicy(name, builder => {...})`
                configuration delegate as chained), either an AllowAnyOrigin() invocation, OR a
                SetIsOriginAllowed(Func<string,bool>) invocation whose single lambda argument is
                syntactically unconditional-true (an ExpressionBody that is exactly the `true`
                LiteralExpressionSyntax, or a BlockBody consisting of exactly `return true;`).
    Fix       : Replace AllowAnyOrigin() with an explicit WithOrigins(...) allowlist, or replace
                an unconditional SetIsOriginAllowed(_ => true) with a real per-request origin
                check against a configured allowlist. Combining a wildcard/always-allow origin
                policy with AllowCredentials() is never a legitimate configuration on this
                platform — most browsers already reject the combination at the wire level, but
                ASP.NET Core's CorsService only rejects it at request-handling time, so a
                misconfigured policy fails silently per-request instead of failing fast at
                startup.
    Suppress  : Per-call-site via #pragma warning disable SK0032; no legitimate production case
                is known — document the rationale inline if ever suppressed.
    Limitation: The "unconditionally true" lambda-body detection is a SYNTACTIC pattern check
                only (literal `true` / `return true;`), not full data-flow or constant-propagation
                analysis — an indirect always-true path (e.g. a local `const bool always = true;
                return always;`, or a call to a helper that always returns true) is not caught.
                The same-scope tracking is bounded to a single method/lambda body — a builder
                reference passed into a separate helper method that calls AllowCredentials()
                elsewhere is not followed across the method boundary. Both are documented,
                intentional scope limits, mirroring this file's established "pattern/presence
                check, not full reachability analysis" convention (SK0028,
                HealthCheckTagIntegrityRules, NoSecurityContextSingletonRegistrationPredicate).
    Note      : Introduced WO-062 P-410. Joins the platform's semantic-model-assisted analyzer
                family (SK0011, SK0015, SK0017–SK0020, SK0022, SK0024–SK0031), requiring
                GetTypeInfo/GetSymbolInfo resolution of the receiver as CorsPolicyBuilder to avoid
                a false positive against an unrelated same-named method. Assigned before
                14.Presentation's own AddSharedKernelCors (P-404) has shipped past Design —
                mirrors the platform's established "design a governance rule ahead of its
                producing domain's Core phase" pattern (e.g. P-332's cursor-pagination DTO,
                WO-052) rather than a reason to defer authoring this rule.

SK0033  ReflectionBasedObjectMapperUsage
    Category  : Usage
    Severity  : Warning
    Trigger   : Three semantic-model-resolved shapes, all requiring the resolved symbol's
                ContainingAssembly.Name to equal exactly "AutoMapper" (mirroring SK0025's exact-
                assembly-name technique, not a syntax-only simple-name match — "Profile" and
                "Status"-class names are common enough elsewhere in this platform's own code to
                make a syntax-only match unsafe): (1) a ClassDeclarationSyntax whose BaseType
                resolves to AutoMapper.Profile; (2) a MethodDeclarationSyntax/LambdaExpressionSyntax
                parameter whose type resolves to AutoMapper.IMapperConfigurationExpression; (3) an
                InvocationExpressionSyntax whose resolved method symbol's simple name is exactly
                "AddAutoMapper" and whose ContainingNamespace starts with "AutoMapper". Fires
                globally, no suppression namespace — AutoMapper has no legitimate call site
                anywhere on this platform.
    Fix       : Replace with a Riok.Mapperly [Mapper] partial class (compile-time source-
                generated, zero runtime reflection, AOT-clean) or hand-written mapping code,
                colocated in whichever package/service owns the mapping direction. The kernel
                deliberately never wraps a mapper behind its own abstraction interface.
    Suppress  : Per-call-site via #pragma warning disable SK0033; no legitimate production case
                is known — document the rationale inline if ever suppressed.
    Limitation: Mapster's runtime (non-source-generated) adapter API is DELIBERATELY NOT covered
                by this rule. Mapster's `.Adapt<T>()`/`.BuildAdapter()` call-site syntax is
                IDENTICAL whether or not the companion `Mapster.SourceGenerator` package is
                installed and generating the implementation at compile time — there is no
                reliable syntactic or semantic-model discriminator between "this call resolves to
                a hand-written runtime reflection path" and "this call resolves to a source-
                generated implementation with the same public API." Rather than ship a rule with
                an uncontrolled false-positive rate against a legitimate Mapster source-generated
                consumer, this rule scopes to AutoMapper only, per this domain's established
                "narrow scope rather than ship false positives" convention (mirrors SK0022/
                SK0024's own documented scope limits). Mapster usage of either kind remains
                un-enforced by tooling — the root CLAUDE.md's "What Goes Where" guidance is the
                only mechanism naming Mapperly/hand-written mapping as the sanctioned choices.
    Note      : Introduced WO-079 P-486. No `SharedKernel.ArchitectureTests` counterpart — a
                per-compilation-unit source-level pattern the Roslyn analyzer resolves completely
                on its own, mirroring SK0030's "no architecture-test counterpart by design" note.
                Ungated — requires no compiled reference to any not-yet-shipped SharedKernel
                package; only a test-only PackageReference to the real `AutoMapper`/`Riok.Mapperly`
                NuGet packages for its own fixture compilation. Pinned `AutoMapper 15.1.1` (NOT the
                "v12+" floor originally assumed) — `12.0.1`/`13.0.1` both carry a disclosed
                high-severity DoS advisory (GHSA-rvv3-g6hj-g44x / CVE-2026-32933, uncontrolled
                recursion causing a stack overflow on a deeply-nested/self-referential object
                graph), patched at `15.1.1`/`15.0.1`; test-only, never shipped, but pinned clean
                anyway. `Riok.Mapperly` pinned `3.6.0`. All five tests (T-341–T-345) run against the
                REAL compiled packages, not fixture stand-ins — see the Implementation Rules entry
                below on referencing a net10.0-targeted real assembly from an analyzer test.

SK0034  AmountCurrencyPairCoupling
    Category  : Advisory  (NEW — the platform's first ADVISORY-ONLY category. Unlike every prior
                Warning-severity rule in this registry, which is either already enforced at
                Warning pending a future escalation to Error (SK0006, SK0007) or a permanent
                platform-wide prohibition kept at Warning by deliberate choice, SK0034 has NO
                escalation path to Error at all, by design — it is a heuristic nudge toward a
                better pattern, not a prohibition of a bad one. Do not escalate this rule to
                Error severity in any future phase without a fresh design review; its detection
                technique cannot achieve the near-zero false-positive bar every Error-severity
                rule on this platform requires.)
    Severity  : Warning
    Trigger   : Syntax-only (PredefinedTypeSyntax match, no SemanticModel — see Limitation): a
                class/record/struct declaration whose direct (non-inherited) members include
                BOTH (a) a `decimal`/`decimal?`-typed property or field whose identifier ends
                with one of "Amount", "Price", "Total", "Balance", AND (b) a `string`/`string?`-
                typed property or field whose identifier ends with one of "Currency",
                "CurrencyCode". Fires once per offending type, naming both matched members in
                the diagnostic message. Self-exempt: a type whose own identifier is exactly
                "Money" never fires, regardless of its members.
    Fix       : Replace the raw decimal+string pair with `03.Domain`'s `Money` value object
                (`SharedKernel.Domain.Monetary.Money`), which enforces ISO 4217 minor-unit-
                correct rounding and rejects cross-currency arithmetic — a pattern the raw pair
                cannot express and can silently violate (mixed currencies summed as if equal,
                minor-unit precision drift).
    Suppress  : Per-property-or-type via #pragma warning disable SK0034 — a legitimate case exists
                whenever the pair is a deliberate wire-format/read-model choice (e.g. a `04.Contracts`
                DTO or a `06.Persistence` Dapper projection intentionally avoiding a rich domain
                type at a serialization boundary); suppress with a one-line comment naming the
                reason, do not silently leave the warning unaddressed.
    Note      : Introduced WO-066 P-442, depends on `03.Domain` P-439 (`Money`) only for its OWN
                remediation message to point at a real, shipped type — the analyzer's detection
                logic references no compiled `SharedKernel.Domain` type and needs no
                ProjectReference to it; this rule can be fully implemented, tested, and even run
                against this repo's own already-shipped production sources before `Money` ships.
                No `SharedKernel.ArchitectureTests` counterpart — a per-compilation-unit source-
                level heuristic, mirroring SK0030's/SK0033's "no architecture-test counterpart by
                design" note.
    Limitation: A closed, hand-picked suffix list is inherently imprecise — it will both miss
                genuine amount/currency pairs named outside this list (false negative, the safer
                direction for a heuristic — mirrors SK0708's naming-heuristic precedent) and, in
                principle, could flag an unrelated decimal+string pair that happens to share both
                suffixes by coincidence (false positive). REAL-SOURCE AUDIT RESULT (T-350,
                mandatory before shipping): a raw `CSharpCompilation`+`WithAnalyzers` run scanned
                every `.cs` file in this repository's `00`–`20` numbered domains (excluding test/
                sample/generated paths) and found ZERO SK0034 diagnostics — no genuine false
                positive surfaced, so the suffix list ships UNNARROWED exactly as specified
                (`Amount`/`Price`/`Total`/`Balance` + `Currency`/`CurrencyCode`). Re-run this scan
                (`SK0034_AmountCurrencyPairAdvisoryAnalyzerTests.RealSourceAudit_ShippedProductionAssemblies_NoGenuineFalsePositive`)
                whenever new production source is added and treat any new hit the same way: narrow
                the suffix list only if the hit is a demonstrably unrelated pair, never on the
                strength of design-time reasoning alone.

SK0035  UnmaskedClassifiedDataAtLoggingCallSite
    Category  : Security
    Severity  : Warning
    Trigger   : Semantic-model analyzer over calls to a `[LoggerMessage]`-attributed method
                (`Microsoft.Extensions.Logging.LoggerMessageAttribute`). A member is CLASSIFIED when
                it carries an attribute that is, or derives at any depth from,
                `Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute` —
                except `NoDataClassificationAttribute` and its derivatives ("not personal data").
                For each argument, skipping `LogLevel`/`Exception`(-derived) parameters and any
                parameter that is itself classified (the logging generator redacts it under
                `EnableRedaction()`), report when: (1) the argument is a direct reference to a
                classified property/field; or (2) the argument's STATIC TYPE declares a classified
                property/field and the parameter is not `[LogProperties]`
                (`Microsoft.Extensions.Logging.LogPropertiesAttribute`, which makes the generator
                honor member classifications). `[LogProperties]` does not make shape (1) safe.
                Exempt: an argument that is a direct call to a
                `SharedKernel.DataPrivacy.Masking.PiiMasking` method or any
                `SharedKernel.DataPrivacy.Masking.Pseudonymizer` method. Message names the member,
                the attribute (e.g. `[EmailAddressData]`) and the parameter.
    Fix       : Mark the `[LoggerMessage]` parameter with the same classification attribute so
                log redaction masks it (or `[LogProperties]` for a whole object), or mask the
                value with `SharedKernel.DataPrivacy.PiiMasking.*` first.
    Suppress  : Per-call-site via #pragma warning disable SK0035; a legitimate case exists only
                when the value is already irreversibly transformed before the call in a way this
                analyzer cannot see through — document the rationale inline if ever suppressed.
    Note      : Introduced WO-076 P-476; retargeted P-554 when `01.Core`'s
                `SharedKernel.DataPrivacy` moved to Microsoft's compliance model (the former
                `DataClassificationAttribute(Restricted)`/`SensitiveDataCategoryAttribute` and
                `PiiMasking.Pan` no longer exist). Every type is resolved by fully-qualified
                metadata name (`Compilation.GetTypeByMetadataName(...)`), so contrived-fixture
                tests declare stand-ins inside the test compilation; a compilation without the
                compliance base type gets no diagnostics. Real-assembly verification
                (`SK0035_UnmaskedClassifiedDataLoggingAnalyzerTests.RealAssembly_ClassifiedProperty_ReportsDiagnostic`)
                runs against the compiled `SharedKernel.DataPrivacy.dll` and
                `Microsoft.Extensions.Compliance.Abstractions.dll` via the test-only
                `ProjectReference` (`PrivateAssets="all"`) in `SharedKernel.Analyzers.Tests.csproj`.
                Structurally distinct from SK0022 (magic strings) and SK0020/SK0021 (logging
                authoring shape) — this rule inspects the DATA flowing into a `[LoggerMessage]` call.
    Limitation: Pattern check, not data-flow analysis. Only a DIRECT member reference or a direct
                masking/pseudonymizing call is recognized — an intermediate local
                (`var x = entity.Email; logger.LogX(x);`) or a helper method that returns a
                classified member unmasked is not traced (mirrors SK0028,
                HealthCheckTagIntegrityRules). Classified members inherited from a base type of
                the argument's type are not inspected.

SK0036  RawRpcExceptionConstruction
    Category  : Usage
    Severity  : Warning
    Trigger   : Semantic-model analyzer (exact-type resolution via SemanticModel.GetSymbolInfo/
                GetTypeInfo on an ObjectCreationExpressionSyntax — NOT a syntax-only simple-name
                match, since "Status" is a dangerously generic simple name elsewhere on this
                platform and in consuming services, the same lesson SK0026 already recorded for
                "Kernel"): a `new RpcException(...)` or `new Status(...)` construction whose
                constructed type resolves to exactly `Grpc.Core.RpcException`/`Grpc.Core.Status`,
                anywhere outside the `SharedKernel.Presentation.Grpc` namespace (single shared
                exemption prefix, mirroring SK0029's one-owning-package shape, not SK0026's
                per-client-type mapping).
    Fix       : Return a `Result` and end it with `SharedKernel.Core.Extensions`' `ThrowIfFailure()` /
                `GetValueOrThrow()`, which throw `Error.ToException()`; `SharedKernel.Presentation.Grpc`'s
                exception interceptor maps that exception to the rich `google.rpc.Status` (status from
                `GrpcStatusCodeMap.Resolve`, `ErrorInfo` with the error code, `BadRequest` with the field
                errors). A hand-built `RpcException` keeps only its status code (and, for a client
                category, its message): the interceptor rebuilds it with reason `grpc.{status}` and drops
                its trailers (P-562 R30). History: until P-562 the
                package's own `…Grpc.Results.GrpcResultExtensions.ToGrpcResult()`; the redesign renamed it
                `ThrowIfFailure()`/`GetValueOrThrow()` in the root namespace; the final review (R32)
                removed those and the `ResultFailures` handoff (same signatures as Core's, CS0121). Each
                step changed only the message; the rule and its exemption prefix did not.
    Suppress  : Per-call-site via #pragma warning disable SK0036; no legitimate production case
                outside `SharedKernel.Presentation.Grpc` itself is known — document the rationale
                inline if ever suppressed.
    Note      : Introduced WO-074 P-469, mirrors the raw-`HttpClient` (SK0013/P-159), inline-
                `ProblemDetails` (P-199), and ad hoc-logging (SK0020/P-250) enforcement
                precedents. UNGATED — requires no compiled reference to
                `SharedKernel.Presentation.Grpc` (a namespace-string exemption match needs no
                ProjectReference) and needs no dependency on that package's own P-468 shipping;
                only the standalone, already-available `Grpc.Core.Api` NuGet package (containing
                `RpcException`/`Status` alone, not the full ASP.NET Core gRPC hosting stack) is
                needed as a test-only PackageReference for fixture compilation. No
                `SharedKernel.ArchitectureTests` counterpart, mirroring SK0013's own shape.
    Limitation: Detects direct construction only — a service that wraps `new RpcException(...)`
                inside its own locally-declared helper method still triggers at that helper's
                declaration site (correct), but a THIRD-PARTY library method that internally
                constructs and returns an `RpcException` is not, and cannot be, traced. This is a
                documented, intentional scope limit shared with every construction-path rule in
                this registry (SK0013, P-199's inline-ProblemDetails rule).

SK0037  ValueObjectMissingEnsureValid
    Category  : Design
    Severity  : Warning
    Trigger   : Semantic-model analyzer: a concrete class deriving from
                SharedKernel.Domain.ValueObjects.ValueObject (matched by fully qualified name) where no
                class in the source-declared chain down to ValueObject has every instance constructor
                either call EnsureValid() or delegate with : this(...). Reported on the class name.
    Fix       : Call EnsureValid() as the last statement of each constructor, after assigning members
    Exempt    : Abstract classes; SingleValueObject<TValue> subclasses (the base calls it); a class whose
                effective Validate() declares no rules ([], null, default, Array.Empty/Enumerable.Empty,
                or only yield break); generated code
    Note      : ValueObject does not validate in its base constructor, so an omitted call compiles and
                silently produces unvalidated values. Paired with 03.Domain explicit-validation model.

SK0038  IntegrationEventMissingAttribute
    Category  : Design
    Severity  : Warning
    Trigger   : A non-abstract class or record whose own base list names IIntegrationEvent
                carries no attribute simply named IntegrationEvent or
                IntegrationEventAttribute. Reported on the type name.
    Fix       : Add [IntegrationEvent("context.event-name", Version = N)] to declare the
                event's wire name and schema version
    Exempt    : Abstract types, structs, and types that implement IIntegrationEvent only
                through a base type
    Note      : Moves a run-time failure to compile time — SharedKernel.Contracts'
                EventEnvelope.Wrap and IntegrationEventDescriptor.For both refuse an event type
                whose attribute is missing or invalid, but only the first time it is published
                or consumed. Syntax-only and name-based, like SK0009: no semantic model, so a
                test fixture or consuming service may declare its own local IIntegrationEvent
                and IntegrationEventAttribute. Each partial declaration is checked on its own —
                the part that lists IIntegrationEvent must carry the attribute. Implemented with
                SK0039 in one analyzer class, IntegrationEventAttributeAnalyzer
                (Diagnostics/SK0038_IntegrationEventAttributeAnalyzer.cs).

SK0039  InvalidIntegrationEventAttribute
    Category  : Design
    Severity  : Warning
    Trigger   : A type covered by SK0038 whose [IntegrationEvent] attribute declares, as a
                literal, a wire name that breaks the name rule (1 to 128 lowercase ASCII letters
                and digits, in segments separated by a single '.', '-' or '_', no leading or
                trailing separator) or a Version below 1. Reported on the offending argument.
    Fix       : Use a valid name such as "orders.order-placed"; versions start at 1
    Limitation: A name or version supplied as anything other than a literal (a constant,
                nameof, an interpolated string) is not evaluated; the run-time check in
                IntegrationEventDescriptor still covers it.
    Note      : The name rule mirrors IntegrationEventDescriptor's own validation exactly —
                change the two together.

SK0040  PipelineMarkerResponseShapeMismatch
    Category  : Design
    Severity  : Warning
    Trigger   : A non-abstract class, record, or struct implementing
                SharedKernel.Application.Behaviors.Authorization.IAuthorizeRequest and/or
                SharedKernel.Application.Behaviors.Idempotency.IIdempotentRequest (resolved through
                the full interface closure via SemanticModel.GetDeclaredSymbol +
                INamedTypeSymbol.AllInterfaces, the same technique SK0017–SK0019 established), that
                also implements MediatR.IRequest<TResponse> (directly or transitively, e.g. through
                ICommand<TResponse>/IQuery<TResponse>) whose resolved TResponse is neither
                SharedKernel.Primitives.Results.Result nor a closed Result<T> (matched by simple
                name "Result", arity 0 or 1, containing namespace exactly
                "SharedKernel.Primitives.Results"). Reported on the type name, naming every matched
                marker and the actual resolved response type.
    Fix       : Declare the request's response as Result or a closed Result<T>, or remove the
                marker interface if the request genuinely needs neither authorization nor
                idempotency short-circuiting.
    Exempt    : (1) A type implementing IAuditableRequest<TResponse> or ILoggableRequest<TResponse>
                — READING FailureResponse.cs and both AuditingBehavior and LoggingBehavior found
                NEITHER ever calls FailureResponse.Create<TResponse>: both only forward the
                response next() already produced and classify it through
                Shared.ResponseOutcome.TryGetError, which treats a non-Result response as a
                success rather than requiring a static Failure(Error) factory — implementing either
                marker on a plain-DTO-response request compiles and runs without ever throwing, so
                this rule does not check them, matching the same verification discipline applied to
                every other marker in this file. (2) A type implementing a checked marker with no
                MediatR.IRequest<TResponse> at all — no behavior can ever resolve into that type's
                pipeline (a DI/runtime fact), so there is no hazard. (3) A resolved TResponse that
                is itself still an open type parameter or an unresolved/error type — the eventual
                closed shape cannot be determined at the declaration site; a closed Result<T> whose
                own type argument is an open type parameter still passes, since only the outer
                Result/Result<T> shape is checked. (4) Abstract types — the same exemption already
                applied by SK0009/SK0017/SK0018. (5) ValidationBehavior also calls
                FailureResponse.Create, but applies to every TRequest : IRequest<TResponse>
                unconditionally with no marker interface to gate scope on — out of reach for a
                type-declaration rule of this shape, and not part of this rule's trigger.
    Suppress  : Per-type via #pragma warning disable SK0040 with an inline comment documenting the
                rationale; fires globally, no suppression namespace.
    Note      : Introduced as a governance companion to 05.Application's P-544 pre-publish
                redesign. Motivating gap: FailureResponse.Create<TResponse>
                (SharedKernel.Application.Behaviors/Shared/FailureResponse.cs) binds to a public
                static Failure(Error) factory resolved via reflection per closed TResponse —
                Result takes a hardcoded fast path, every other TResponse must expose that factory
                or the call throws InvalidOperationException, at runtime, on the first
                authorization denial or duplicate submission. No SharedKernel.ArchitectureTests
                counterpart — a per-compilation-unit source-level marker/interface-closure check
                the Roslyn analyzer resolves completely on its own, mirroring SK0017–SK0019's/
                SK0030's "no architecture-test counterpart by design" note.

SK0041 is the next available sequential Roslyn-analyzer ID.
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
    All factory methods take an Assembly parameter and return ConditionList, EXCEPT
    SearchReferencesOnlyCoreAndContracts (WO-044 P-278),
    IntelligenceReferencesOnlyCoreAndContracts (WO-045 P-286), and
    WorkflowsReferencesOnlyCoreContractsAndApplication (WO-046 P-290), which return
    ConditionList[] — see their own entries below for why.
    .CoreReferencesNothing(Assembly)                → ConditionList
    .CachingReferencesOnlyCore(Assembly)            → ConditionList
    .DomainReferencesOnlyCore(Assembly)             → ConditionList
    .ContractsReferencesOnlyCore(Assembly)          → ConditionList
        04.Contracts may reference 01.Core only. Forbids SharedKernel.Domain as well as
        caching and every infrastructure layer: a wire contract is an independent, versioned
        projection of a domain model, never the model itself. SharedKernel.Primitives (Error,
        ValidationResult<T>) stays permitted. Proven against the real SharedKernel.Contracts
        assembly as well as contrived fixtures.
    .DomainNeverReferencesPersistence(Assembly)     → ConditionList  (hard rule)
    .DomainNeverReferencesMessaging(Assembly)       → ConditionList  (hard rule)
    .ApplicationNeverReferencesConcreteInfrastructure(Assembly) → ConditionList  (hard rule)
    .TestingNeverReferencedByProduction(Assembly)   → ConditionList  (hard rule)
        P-558: also forbids SharedKernel.Persistence.Testing (the one published 16.Testing package);
        TestingPackagesNeverReferencedByProductionTests scans every production csproj plus the IL of the
        six persistence assemblies.
    .PersistenceNeverReferencesApplicationOrSecurity(Assembly) → ConditionList  (P-557, REWRITTEN P-558)
        06.Persistence may reach 05.Application only through SharedKernel.Application.Abstractions
        (ApplicationAbstractionsNamespaces allow-list) — never MediatR, SharedKernel.Application's
        MediatR-bearing namespaces (Behaviors, Messaging, DomainEvents, Extensions, Streaming) or
        SharedKernel.Security. Paired with .PersistenceForbiddenAssemblyReferences(Assembly) →
        IReadOnlyList<string> (assembly-reference level) and a Roslyn source scan of 06.Persistence.
        PersistenceLayeringRulesTests also locks: EfCore IS the PostgreSQL provider, Dapper and Npgsql
        never reference EF Core.
    .SearchReferencesOnlyCoreAndContracts(Assembly) → ConditionList[]  (WO-044 P-278)
    .IntelligenceReferencesOnlyCoreAndContracts(Assembly) → ConditionList[]  (WO-045 P-286)
    .WorkflowsReferencesOnlyCoreContractsAndApplication(Assembly) → ConditionList[]  (WO-046 P-290)

    .SearchReferencesOnlyCoreAndContracts(Assembly searchAssembly)  → ConditionList[]
        Added to this EXISTING class rather than a new dedicated class, mirroring the
        precedent that a single new layering-BOUNDARY check on an existing numbered domain
        belongs alongside its siblings (CoreReferencesNothing,
        ContractsReferencesOnlyCore, etc.), while TOPOLOGY-INTERNAL checks
        (sibling-package non-reference, third-party-dependency purity) live in the
        domain's own dedicated *TopologyRules class (SearchTopologyRules, documented
        below). Asserts that the supplied 09.Search assembly has no dependency on any of
        FIFTEEN forbidden capability-domain namespace terms — every OTHER numbered
        domain's package family: "SharedKernel.Caching", "SharedKernel.Domain",
        "SharedKernel.Application", "SharedKernel.Persistence", "SharedKernel.Messaging",
        "SharedKernel.Storage", "SharedKernel.AI" (10.Intelligence's package family is
        named SharedKernel.AI.*, not SharedKernel.Intelligence.* — confirmed against the
        root CLAUDE.md Abstractions table: "SharedKernel.AI.Abstractions" / ".VectorDb"),
        "SharedKernel.Communication", "SharedKernel.Security", "SharedKernel.ServiceDefaults",
        "SharedKernel.MultiTenancy", "SharedKernel.Presentation", "SharedKernel.Integration",
        "SharedKernel.Testing", "SharedKernel.Workflows". Returns ConditionList[] (fifteen
        elements, one per forbidden term) — the FIRST method on this class to do so; every
        prior SharedKernelLayeringRules method returns a single ConditionList. This
        deliberately follows the newer domain-boundary-rule-class convention
        (CommunicationLayeringRules.CommunicationPackagesNeverReferencesForbiddenLayers's
        proven "one ConditionList per forbidden term" shape) rather than attempting a
        single positive OnlyHaveDependencyOnAny(...) assertion, which would require
        exhaustively enumerating every legitimate BCL/System.*/Microsoft.CSharp namespace
        alongside the two permitted SharedKernel terms — brittle and unproven at this
        scale in this codebase. FALLBACK NOTE: if NetArchTest.eNt >= 1.3.2 is confirmed at
        implementation time to expose .Should().NotHaveDependencyOnAny(string[]) as a
        single-ConditionList alternative, that is an acceptable simplification IF it
        preserves per-term failure-message granularity; otherwise keep the fifteen-element
        array. Caller must assert .GetResult().IsSuccessful on EACH element. None of the
        fifteen terms is a prefix of "SharedKernel.Search" — no self-collision. Excludes
        (by omission, never listed as forbidden) "SharedKernel.Primitives",
        "SharedKernel.Core", "SharedKernel.Configuration", "SharedKernel.FeatureManagement",
        "SharedKernel.Cryptography" (01.Core — permitted) and "SharedKernel.Contracts"
        (04.Contracts — permitted).
        Rationale: mirrors CommunicationLayeringRules.CommunicationPackagesNeverReferencesForbiddenLayers's
        four-term shape, scaled to the FULL platform domain roster because 09.Search's own
        brain states its layering wall even more starkly than 11.Communication's:
        "09.Search may only reference 01.Core and 04.Contracts. It must never reference
        03.Domain, 05.Application, 06.Persistence, 07.Messaging, 12.Security, or any other
        capability domain." The WO-044 phase input's own acceptance criteria name five of
        these terms explicitly and close with "or any other capability domain beyond
        01.Core/04.Contracts" — this method is the exhaustive, all-fifteen-domains
        mechanical form of that closing clause.
        MAINTENANCE OBLIGATION: per this file's own Implementation Rules ("If a new domain
        (folder XX) is added, the layering rules must be updated in the same PR"), a
        future 18.NewDomain addition MUST append its package-family term to this forbidden
        list in the SAME PR that adds the new domain, or SearchReferencesOnlyCoreAndContracts
        will silently under-enforce against it.

    .IntelligenceReferencesOnlyCoreAndContracts(Assembly intelligenceAssembly)  → ConditionList[]
        Added to this EXISTING class alongside SearchReferencesOnlyCoreAndContracts,
        following the same precedent that a layering-BOUNDARY check on an existing
        numbered domain belongs alongside its siblings, while TOPOLOGY-INTERNAL checks
        (sibling-package non-reference, third-party-dependency purity) live in the
        domain's own dedicated *TopologyRules class (IntelligenceTopologyRules,
        documented below). Asserts that the supplied 10.Intelligence assembly has no
        dependency on any of FIFTEEN forbidden capability-domain namespace terms —
        every OTHER numbered domain's package family: "SharedKernel.Caching",
        "SharedKernel.Domain", "SharedKernel.Application", "SharedKernel.Persistence",
        "SharedKernel.Messaging", "SharedKernel.Storage", "SharedKernel.Search",
        "SharedKernel.Communication", "SharedKernel.Security",
        "SharedKernel.ServiceDefaults", "SharedKernel.MultiTenancy",
        "SharedKernel.Presentation", "SharedKernel.Integration", "SharedKernel.Testing",
        "SharedKernel.Workflows". SAME FIFTEEN-TERM COUNT as
        SearchReferencesOnlyCoreAndContracts's own list — the symmetric swap:
        "SharedKernel.Search" is now forbidden (09.Search is a sibling domain
        10.Intelligence must never reference) and "SharedKernel.AI" is now EXCLUDED
        (10.Intelligence is the domain under test, omitted from its own forbidden
        list — self-exclusion by omission, not an explicit term, the same convention
        SearchReferencesOnlyCoreAndContracts uses for "SharedKernel.Search"). Returns
        ConditionList[] (fifteen elements, one per forbidden term), following
        SearchReferencesOnlyCoreAndContracts's own newer domain-boundary-rule-class
        convention rather than the older single-ConditionList shape most of this
        class's other methods use. Caller must assert .GetResult().IsSuccessful on
        EACH element. None of the fifteen terms is a prefix of "SharedKernel.AI" — no
        self-collision. Excludes (by omission, never listed as forbidden)
        "SharedKernel.Primitives", "SharedKernel.Core", "SharedKernel.Configuration",
        "SharedKernel.FeatureManagement", "SharedKernel.Cryptography" (01.Core —
        permitted) and "SharedKernel.Contracts" (04.Contracts — permitted).
        Rationale: mirrors 10.Intelligence/CLAUDE.md's own starkly-worded layering wall
        verbatim: "10.Intelligence may only reference 01.Core and 04.Contracts. It must
        never reference 03.Domain, 05.Application, 06.Persistence, 07.Messaging,
        09.Search, 12.Security, or any other capability domain." The WO-045 phase
        input's own acceptance criteria name several of these terms explicitly and
        close with "or any other capability domain beyond 01.Core/04.Contracts" — this
        method is the exhaustive, all-fifteen-domains mechanical form of that closing
        clause, the same relationship SearchReferencesOnlyCoreAndContracts has to its
        own WO-044 phase input.
        MAINTENANCE OBLIGATION (carried forward from SearchReferencesOnlyCoreAndContracts's
        own entry): per this file's own Implementation Rules ("If a new domain (folder
        XX) is added, the layering rules must be updated in the same PR"), a future
        18.NewDomain addition MUST append its package-family term to BOTH this method's
        list AND SearchReferencesOnlyCoreAndContracts's list (and every future sibling
        of this ConditionList[]-per-forbidden-term shape) in the SAME PR that adds the
        new domain, or each affected rule will silently under-enforce against it.

    .WorkflowsReferencesOnlyCoreContractsAndApplication(Assembly workflowsAssembly)  → ConditionList[]
        Added to this EXISTING class alongside SearchReferencesOnlyCoreAndContracts and
        IntelligenceReferencesOnlyCoreAndContracts, following the same precedent that a
        layering-BOUNDARY check on an existing numbered domain belongs alongside its
        siblings, while TOPOLOGY-INTERNAL checks (the raw-client-accessor-consumption and
        HealthChecks-dependency prohibitions specific to 17.Workflows) live in the domain's
        own dedicated WorkflowTopologyRules class (documented below). Asserts that the
        supplied 17.Workflows assembly has no dependency on any of FOURTEEN forbidden
        capability-domain namespace terms: "SharedKernel.Caching", "SharedKernel.Domain",
        "SharedKernel.Persistence", "SharedKernel.Messaging", "SharedKernel.Storage",
        "SharedKernel.Search", "SharedKernel.AI", "SharedKernel.Communication",
        "SharedKernel.Security", "SharedKernel.ServiceDefaults", "SharedKernel.MultiTenancy",
        "SharedKernel.Presentation", "SharedKernel.Integration", "SharedKernel.Testing".
        FOURTEEN terms, not the fifteen SearchReferencesOnlyCoreAndContracts/
        IntelligenceReferencesOnlyCoreAndContracts each use — the FIRST layering-boundary
        method on this class where the domain under test is permitted THREE upstream
        domains, not two: 17.Workflows/CLAUDE.md's own layering wall states "Only 01.Core,
        04.Contracts, and 05.Application are permitted" — so "SharedKernel.Application" is
        deliberately ABSENT from the forbidden list (unlike Search's and Intelligence's own
        fifteen-term lists, which both forbid it), alongside the usual self-exclusion
        ("SharedKernel.Workflows", omitted because 17.Workflows is the domain under test).
        Returns ConditionList[] (fourteen elements, one per forbidden term), following the
        same newer domain-boundary-rule-class convention as its two siblings. Caller must
        assert .GetResult().IsSuccessful on EACH element. None of the fourteen terms is a
        prefix of "SharedKernel.Workflows" — no self-collision. Excludes (by omission,
        never listed as forbidden) "SharedKernel.Primitives", "SharedKernel.Core",
        "SharedKernel.Configuration", "SharedKernel.FeatureManagement",
        "SharedKernel.Cryptography" (01.Core — permitted), "SharedKernel.Contracts"
        (04.Contracts — permitted), and "SharedKernel.Application" (05.Application —
        permitted, the distinguishing exclusion for this method).
        Rationale: mirrors 17.Workflows/CLAUDE.md's own Hard Violations bullet verbatim:
        "Referencing 02.Caching, 03.Domain, 06.Persistence, 07.Messaging, 08.Storage,
        09.Search, 10.Intelligence, 11.Communication, 12.Security, 13.ServiceDefaults,
        14.Presentation, or 15.Integration from 17.Workflows. Only 01.Core, 04.Contracts,
        and 05.Application are permitted." This method is the exhaustive, all-fourteen-
        domains mechanical form of that sentence — the same relationship
        SearchReferencesOnlyCoreAndContracts and IntelligenceReferencesOnlyCoreAndContracts
        each have to their own domain's brain.
        MAINTENANCE OBLIGATION (carried forward): a future 18.NewDomain addition MUST
        append its package-family term to THIS method's list AND both of its siblings'
        lists (and any future sibling of this ConditionList[]-per-forbidden-term shape) in
        the SAME PR that adds the new domain, or each affected rule will silently
        under-enforce against it.

WorkflowTopologyRules  (static class — 17.Workflows package topology enforcement predicates; WO-046 P-290)
    Both factory methods accept Assembly (or params Assembly[]) and return ConditionList.
    Unlike RedisTopologyRules/StorageTopologyRules/SearchTopologyRules/
    IntelligenceTopologyRules, 17.Workflows has no sibling provider packages — a single
    package, SharedKernel.Workflows.Temporal, is both the abstraction surface and the
    Temporal-specific implementation — so this class carries no
    "AbstractionsHasNoThirdPartyDependencies"/"ProviderPackagesNeverReferenceEachOther" pair.
    17.Workflows/CLAUDE.md's own cross-domain ask describes this class as "modelled
    one-for-one on StorageTopologyRules/SearchTopologyRules"; for a single-package domain
    that means restating the SAME two documented `NotHaveDependencyOn` gotchas (namespace
    `StartsWith`, no trailing dot; never check a package against its own identifying term)
    while the actual predicates are narrower, single-package equivalents of those classes'
    topology-INTERNAL concerns.

    .NoRawClientAccessorConsumptionInRepo(params Assembly[] repoAssemblies)  → ConditionList
        Asserts that no type in the supplied in-repo assemblies (typically
        SharedKernel.Workflows.Temporal itself, and any other in-repo SharedKernel.*
        assembly the caller chooses to include) has a constructor parameter or field
        whose type is exactly ITemporalRawClientAccessor. Uses
        NoRawClientAccessorConsumptionPredicate (ICustomRule — see below). Carries NO
        internal exemption — mirrors GrpcNeverReferencesContracts's "no exemption
        permitted" precedent. No exemption is needed for the accessor's own DI-registration
        wiring code either: that code PRODUCES an ITemporalRawClientAccessor instance (via
        a factory delegate passed to a DI registration call) rather than CONSUMING one as a
        constructor/field dependency, so it is never a false positive under this
        constructor/field-only detection technique.
        Failure message names the offending type and whether the consumption was via a
        constructor parameter or a field.
        Rationale: mechanizes gate 3 of 17.Workflows/CLAUDE.md's own three-gate
        ITemporalRawClientAccessor escape-hatch discipline verbatim: "A 00.Governance
        architecture test asserts no type inside this repo consumes it." The accessor is
        the genuine last resort for Visibility API queries, schedules, namespace
        administration, and Nexus operations that this package deliberately does not
        model — but it bypasses tenant scoping and workflow-ID composition entirely (stated
        IN CAPITALS on the accessor's own XML doc per that same brain section), so it must
        never be a dependency of any type living inside this platform's own mono-repo; only
        a CONSUMING microservice, having read and accepted that warning, may ever construct-
        inject it, and even then only after its own composition root calls
        `.AllowRawClientAccess()` — a check this rule does not attempt to correlate (see
        Note below).
        Scope note: "no type inside this repo" is read literally, per 17.Workflows/CLAUDE.md's
        own wording — this rule asserts an absolute prohibition on the SharedKernel mono-
        repo's own packages, not a per-consuming-microservice correlation with whether
        `.AllowRawClientAccess()` was called. A consuming microservice's own use of the
        accessor (after opting in) is verified by that microservice's own test suite, not
        by this platform-level rule — the same jurisdiction boundary already established
        for every other "repo-internal purity" rule in this file (e.g.
        DomainLayerPurityRules, ContractsPurityRules).

    .NoHealthChecksDependencyInWorkflows(Assembly workflowsAssembly)  → ConditionList
        Asserts that SharedKernel.Workflows.Temporal has no dependency on
        "Microsoft.Extensions.Diagnostics.HealthChecks". Single
        Types.InAssembly(workflowsAssembly).Should()
        .NotHaveDependencyOn("Microsoft.Extensions.Diagnostics.HealthChecks") call —
        deliberately the NARROW full term, not a bare "Microsoft.Extensions" prefix,
        mirroring IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages's
        precedent exactly: 17.Workflows legitimately needs OTHER Microsoft.Extensions.*
        packages (Hosting, DependencyInjection, Logging, Options) for its own DI/hosting
        wiring — only the HealthChecks-specific term is forbidden.
        Rationale: mechanizes 17.Workflows/CLAUDE.md's own Hard Violations bullet verbatim:
        "Implementing IHealthCheck, or referencing
        Microsoft.Extensions.Diagnostics.HealthChecks, anywhere in 17.Workflows. ProbeAsync
        returning Result<WorkflowServiceHealth> is the primitive; the adapter is
        13.ServiceDefaults's responsibility" — mirroring the 06.Persistence/08.Storage/
        09.Search/10.Intelligence readiness-probe split precedent.

    Note: introduced in WO-046 P-290. Zero new SK diagnostic ID, zero new NuGet dependency
    — NoHealthChecksDependencyInWorkflows is a pure NetArchTest namespace-dependency check;
    NoRawClientAccessorConsumptionInRepo reuses the established Mono.Cecil
    TypeDefinition.Methods (constructor-parameter) and TypeDefinition.Fields inspection
    pattern already used throughout this file (e.g.
    NoEncryptionRotationJobInjectionPredicate, NoDbContextTransactionInApplicationPredicate)
    — the existing Mono.Cecil >= 0.11.5 reference already covers it. Lives in
    SharedKernel.ArchitectureTests/Rules/WorkflowTopologyRules.cs.

NoRawClientAccessorConsumptionPredicate  (class : ICustomRule — internal predicate)
    For each type, checks two surfaces for exact type name "ITemporalRawClientAccessor"
    (simple name; unique within the SDK, matching the discriminator style already used by
    NoEncryptionRotationJobInjectionPredicate's "IEncryptionRotationJob" exact-name check):
      (1) TypeDefinition.Methods where IsConstructor is true — for each constructor,
          checks each ParameterDefinition.ParameterType.Name.
      (2) TypeDefinition.Fields — checks each FieldDefinition.FieldType.Name.
    Returns false (rule violated) on the first match across either surface, with failure
    message naming the offending type and which surface (constructor parameter vs. field)
    matched. No exemption — see WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo's
    own entry for why none is needed. Lives in Predicates/ folder. Used by
    WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo.

GuardPurityRules  (static class — guard clause functional-path purity predicates)
    .GuardAgainstMethodsMustNotThrow()      → ConditionList
        Loads typeof(IGuardClause).Assembly — SharedKernel.Core.dll since WO-082/P-508
        (SharedKernel.Guards was merged into SharedKernel.Core; the C# namespace was
        deliberately preserved) — scopes to types implementing IGuardClause, and asserts
        via DoesNotContainThrowIlPredicate that no method body contains a Mono.Cecil
        OpCodes.Throw instruction. The REAL re-scoping to the SharedKernel.Guards
        namespace (so unrelated SharedKernel.Core types are never policed) happens
        inside DoesNotContainThrowIlPredicate itself, not in this filter chain — see
        its own entry below for why.

DoesNotContainThrowIlPredicate  (class : ICustomRule — internal predicate)
    Inspects Mono.Cecil MethodDefinition.Body.Instructions for OpCodes.Throw.
    Returns false (rule violated) for the first method found containing a throw opcode.
    Failure message includes the declaring type name and method name for diagnostics.
    WO-082/P-508: scope guard evaluated FIRST — computes each type's EFFECTIVE
    namespace via a GetEffectiveNamespace walk up TypeDefinition.DeclaringType to the
    outermost enclosing type (Mono.Cecil leaves TypeDefinition.Namespace empty on every
    NESTED type — confirmed by direct inspection of the real SharedKernel.Core.dll;
    Guard/DefaultGuardClause, the sole real IGuardClause implementor, and Guard/Throw
    both report an empty Namespace). A type whose effective namespace is not
    "SharedKernel.Guards" or "SharedKernel.Guards.*" is reported compliant
    unconditionally regardless of throw content. NetArchTest's own built-in
    ResideInNamespaceStartingWith selection filter was tried first and rejected for
    exactly this reason — it reads the raw (nested-type-broken) Namespace property and
    would have silently excluded every guard type, making the rule vacuously pass.
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

RedisTopologyRules  (static class — caching package topology and abstractions-purity predicates; WO-023 P-145, extended P-547)
    All factory methods accept Assembly (or params Assembly[]). No new SK diagnostic IDs. Every
    predicate is a NetArchTest check: .Should().NotHaveDependencyOn(...) or .NotHaveNameMatching(...),
    except CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions, which uses the
    AssemblyReferenceAllowListPredicate ICustomRule (Mono.Cecil, Predicates/).

    Matching note: NetArchTest's NotHaveDependencyOn(term) compares term against each scanned
    type's dependency NAMESPACES using StartsWith, with NO trailing dot on either side. Every
    forbidden term in the capability-package rules is therefore the EXACT namespace of the package
    it identifies (e.g. "SharedKernel.Caching.Redis.HashStore"), never a bare root prefix such as
    "SharedKernel.Caching.Redis" in a context where that would also match
    "SharedKernel.Caching.Redis.Core". The L2 backplane package (SharedKernel.Caching.Redis) has
    no single dedicated sub-namespace — its types live under SharedKernel.Caching.Redis.Extensions
    (the ".Batch" term also stays in its term list), so both are
    used as its identifying terms.

    Self-dependency note: a type's dependency-namespace set includes its own declaring namespace.
    A package must never be checked against its own identifying term(s) — see
    .CapabilityPackagesNeverReferenceEachOther for how this is handled.

    .RedisCoreNeverReferencesCapabilityPackages(Assembly redisCoreAssembly) → ConditionList
        Asserts that SharedKernel.Caching.Redis.Core has no dependency on any of the five
        capability-package identifying namespace terms: "SharedKernel.Caching.Redis.Batch",
        "SharedKernel.Caching.Redis.Extensions" (L2), "SharedKernel.Caching.Redis.DistributedLocking",
        "SharedKernel.Caching.Redis.HashStore", "SharedKernel.Caching.Redis.PubSub". None of these
        terms is a prefix of "SharedKernel.Caching.Redis.Core" or
        "SharedKernel.Caching.Redis.Core.Extensions", so there is no self-collision.
        Rationale: Redis.Core is the shared connection/health/resilience foundation. A reference
        from Core to any capability package is a layering inversion.

    .CapabilityPackagesNeverReferenceEachOther(params Assembly[] capabilityAssemblies) → ConditionList[]
        Returns one ConditionList per element of capabilityAssemblies, in the same order. For
        each scanned assembly, resolves its own identifying namespace term(s) by assembly simple
        name via an internal Dictionary<string,string[]> keyed on the four real package names
        ("SharedKernel.Caching.Redis" → its Batch/Extensions terms;
        "SharedKernel.Caching.Redis.DistributedLocking", ".HashStore", ".PubSub" → their own exact
        namespace each), and forbids only the OTHER packages' terms. An assembly whose simple name
        is not one of the four (e.g. a test fixture) is checked against the full term set.
        "SharedKernel.Caching.Redis.Core" and "SharedKernel.Caching.Abstractions" are never
        forbidden. Caller must assert .GetResult().IsSuccessful on EACH element.
        Rationale: sibling role-packages depend only on .{Provider}.Core (root CLAUDE.md
        "Provider role-split variant" rule).

    .PubSubNeverReferencesMessaging(Assembly pubSubAssembly) → ConditionList
        Asserts that SharedKernel.Caching.Redis.PubSub has no dependency on "SharedKernel.Messaging"
        (prefix — covers .Abstractions and .MassTransit).
        Rationale: Redis.PubSub's IRedisChannelService is an ephemeral, at-most-once signaling
        channel and must never become a backdoor into the durable IMessageBus abstraction.

    .MessagingNeverReferencesCaching(params Assembly[] messagingAssemblies) → ConditionList
        Asserts that no type in the supplied SharedKernel.Messaging.* assemblies depends on
        "SharedKernel.Caching". Structural converse of PubSubNeverReferencesMessaging and of the
        root CLAUDE.md hard rule; both directions are asserted because NetArchTest dependency
        checks are directional.

    .CachingAbstractionsHasNoInfrastructureDependencies(Assembly abstractionsAssembly) → ConditionList
        Asserts .Should().NotHaveDependencyOn(term) for each of: "SharedKernel.Caching.Redis"
        (bare prefix — covers Redis.Core, Redis (L2), .DistributedLocking, .HashStore, .PubSub;
        the abstractions package cannot self-collide with it), "SharedKernel.Caching.FusionCache",
        "StackExchange.Redis", "ZiggyCreatures", "RedLockNet", "Polly",
        "Microsoft.Extensions.Caching", "Microsoft.Extensions.Options", "Microsoft.Extensions.Hosting",
        "Microsoft.EntityFrameworkCore", "MassTransit".
        Rationale: the provider-neutral contract must not use a provider, a library only a provider
        needs, or the options/hosting stacks (options and hosted services live in provider packages).
        Namespace terms cannot tell two assemblies sharing a namespace apart, so this rule is
        paired with the allow-list rule below.

    .CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions(Assembly abstractionsAssembly)
                                                                                     → ConditionList
        MeetCustomRule(new AssemblyReferenceAllowListPredicate(
            "Microsoft.Extensions.DependencyInjection.Abstractions")). The predicate reads
        TypeDefinition.Module.AssemblyReferences and passes only when every reference is BCL
        ("System", "System.*", "mscorlib", "netstandard") or on the allow-list. Every type is
        reported when the assembly gains a reference, because the violation belongs to the assembly.
        Rationale: a deny-list only catches what someone thought to list; any new package
        dependency (Options, a logging or hosting package, SharedKernel.Primitives, a provider)
        fails until the contract is deliberately widened here. ICachingBuilder exposing
        IServiceCollection is the one reason DI.Abstractions is allowed.

    .CachingAbstractionsDeclaresNoProviderSpecificTypes(Assembly abstractionsAssembly) → ConditionList
        .Should().NotHaveNameMatching("Redis|Fusion|RedLock|StackExchange|Garnet|Valkey|Memcache|Connection")
        over every type (case-sensitive, compiler-generated and nested types included).
        Rationale: a provider-shaped contract needs no provider dependency to leak in. The
        provider contracts live with their providers — IRedisChannelService in
        SharedKernel.Caching.Redis.PubSub, IRedisHashService/ITypedHashStore<T> in
        SharedKernel.Caching.Redis.HashStore, IRedisConnectionProbe in SharedKernel.Caching.Redis.Core.Health
        — each in that package's namespace, pinned by
        RedisTopologyRulesTests.ProviderSpecificContract_IsDeclaredByItsProviderPackage.

    .DistributedLockingNeverReferencesRedLock(Assembly distributedLockingAssembly) → ConditionList
        Asserts SharedKernel.Caching.Redis.DistributedLocking has no dependency on "RedLockNet".
        Rationale: the lock service issues its fencing token in the same atomic Lua script that
        claims the key, and keeps the lock alive and reports its loss itself. RedLock.net acquires
        through its own multi-step protocol and cannot issue a token in that step, so reintroducing
        it would reopen the gap between "lock acquired" and "token issued".

    Permitted cross-reference exemption list:
        - Redis (L2), Redis.DistributedLocking, Redis.HashStore, Redis.PubSub → Redis.Core
          (permitted; the shared foundation)
        - Redis (L2), Redis.DistributedLocking, Redis.HashStore, Redis.PubSub →
          SharedKernel.Caching.Abstractions (permitted; ICachingBuilder and the contracts they implement)
        - Redis.Core references no SharedKernel.Caching package at all (P-547); its readiness probe
          (IRedisConnectionProbe/RedisConnectionHealth) is its own type.
        Any additional exemption must be documented here before it is applied in code.

    Note: every factory method takes assemblies supplied by the consuming test project via
    typeof(SomeTypeInPackage).Assembly — no assembly paths are hard-coded. RedisTopologyRules
    lives in SharedKernel.ArchitectureTests/Rules/ alongside CachingAbstractionRules.cs and must
    not reference StackExchange.Redis, MassTransit, or EF Core directly. Cross-reference: root
    CLAUDE.md hard rule on the 02.Caching ↔ 07.Messaging exclusion boundary
    (PubSubNeverReferencesMessaging and MessagingNeverReferencesCaching are its mechanical
    enforcement).

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
    All factory methods accept Assembly contractsAssembly and return ConditionList. The two
    integration-event rules also take the IIntegrationEvent marker interface as a caller-supplied
    Type anchor (typeof(SharedKernel.Contracts.Events.IIntegrationEvent)), so this package declares
    no dependency on SharedKernel.Contracts; RuleAnchor rejects a null assembly and a non-interface
    anchor, which would otherwise select zero types and pass vacuously.
    Every rule is proven both ways — fails on a contrived violation fixture, passes against the
    real SharedKernel.Contracts assembly (ContractsPurityRulesTests). The real-assembly tests
    exist because these rules were once only ever run against fixtures.

    Contract types versus integration events: a contracts assembly legitimately carries behaviour
    on some types — validating factories (PageRequest.Create), projections (PagedList<T>.Map),
    codecs (PageCursor.Encode). The rules therefore judge what reaches the wire (public
    properties and fields), and the "no behaviour" rule applies only to integration events.

    .IntegrationEventsHaveNoNonTrivialMethods(Assembly contractsAssembly, Type integrationEventInterface)  → ConditionList
        Asserts no type implementing integrationEventInterface contains a non-trivial method.
        A method is trivial if it is a constructor, property getter/setter, static operator
        (IsSpecialName and name starts with "op_"), one of ToString/Equals/GetHashCode, or a
        compiler-generated record member (Deconstruct, PrintMembers, <Clone>$). Uses
        NoNonTrivialMethodsPredicate (ICustomRule — see below). Non-event contract types in the
        same assembly are not judged.

        Rationale: an integration event is a published fact — data only. A method on one is
        logic every consumer would have to reimplement, and a reason to change the event that is
        not a schema change.
        Offending pattern: [IntegrationEvent("orders.order-placed")]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, DateTimeOffset Deadline)
                : IIntegrationEvent { public bool IsExpired() => Deadline < DateTimeOffset.UtcNow; }
        Compliant pattern: the same record with no method body.

    .ContractsAssembliesHaveNoDomainTypeOnPublicSurface(Assembly)  → ConditionList
        Asserts no public type in the contracts assembly has a dependency on
        "SharedKernel.Domain". Uses .Should().NotHaveDependencyOn("SharedKernel.Domain") scoped
        to public types. No type is exempt — EventEnvelope<TEvent> is constrained to
        IIntegrationEvent (SharedKernel.Contracts), not IDomainEvent, so it needs none.

        Rationale: Integration events and DTOs must be independent projections. Exposing
        Entity<TId>, AggregateRoot<TId>, ValueObject, or Specification<T> on a contracts
        public surface ties the wire format to the domain model, breaking polyglot consumers.
        Offending pattern: public class OrderSummaryDto { public Order DomainOrder { get; set; } }
        Compliant pattern: public record OrderSummaryDto(Guid OrderId, string Status);

    .ContractsAssembliesHaveNoResultTypeOnPublicSurface(Assembly)  → ConditionList
        Asserts no public type in the contracts assembly exposes Result, Result<T>,
        ValidationResult or ValidationResult<T> (SharedKernel.Primitives) through a public
        property or field. Uses NoResultTypedPublicMemberPredicate (ICustomRule — see below).
        Public METHODS may still return these types: a validating factory
        (PageRequest.Create → ValidationResult<PageRequest>) or a codec (PageCursor.Decode →
        Result<T>) runs inside the service and never reaches the wire. Referencing
        SharedKernel.Primitives for Error is likewise allowed — a dependency-level check cannot
        tell a property from a factory, which is why this is a member-level rule.

        Rationale: Result<T> is an intra-service outcome type. Putting one in a serialized
        payload causes deserialization failures in any JSON client that does not share the
        SharedKernel.Primitives assembly. Map the outcome to a success payload or an RFC 9457
        problem response at the boundary instead.
        Offending pattern: public class CreateOrderResponse { public Result<Guid> OrderId { get; set; } }
        Compliant pattern: public record CreateOrderResponse(Guid OrderId);

    .IntegrationEventImplementationsMustBeSealed(Assembly contractsAssembly, Type integrationEventInterface)  → ConditionList
        Asserts every non-abstract type implementing integrationEventInterface is sealed.
        Uses: Types.InAssembly(assembly).That().ImplementInterface(integrationEventInterface)
            .And().AreNotAbstract().Should().BeSealed()
        Failure message names the offending type.

        Rationale: Non-sealed integration events are an inheritance trap. A sub-event changes
        the wire format without a new [IntegrationEvent(..., Version = n)], causing silent schema
        drift. sealed ensures the wire contract is closed.
        Offending pattern: public class OrderCreatedEvent : IIntegrationEvent { ... }
        Compliant pattern: public sealed record OrderCreatedEvent : IIntegrationEvent { ... }

    SK0038/SK0039 companion rules — documented in Diagnostic Rule Registry above:
        Every non-abstract IIntegrationEvent implementor must carry a valid
        [IntegrationEvent("name", Version = n)] attribute.

    Removed: ContractsLayeringRules.NoDirectEventEnvelopeConstructionOutsideContracts and its
        NoDirectEventEnvelopeConstructionPredicate. EventEnvelope<TEvent> has no public
        constructor or setter, so construction outside EventEnvelope.Wrap no longer compiles and
        the IL scan had nothing left to catch.

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
    Record members are also trivial: any method carrying [CompilerGenerated], any name starting
    with "<" (e.g. <Clone>$), and Deconstruct/PrintMembers.
    Returns false (rule violated) for the first non-trivial method found; failure message
    includes the declaring type name and the method name. Lives in Predicates/ folder.
    Used by ContractsPurityRules.IntegrationEventsHaveNoNonTrivialMethods.

NoResultTypedPublicMemberPredicate  (class : ICustomRule — Mono.Cecil predicate)
    Fails a type exposing a public property (public getter or setter) or public field whose
    type is, or contains, Result / Result`1 / ValidationResult / ValidationResult`1 declared in
    SharedKernel.Primitives or a child namespace (matched on the outermost declaring type's
    namespace, so the check does not depend on which sub-namespace the outcome types live in).
    The member type is inspected recursively — generic arguments and TypeSpecification element
    types — so Result<Guid>?, Result[] and IReadOnlyList<ValidationResult<T>> are all caught.
    Method return types and parameters are deliberately NOT inspected. Lives in Predicates/.
    Used by ContractsPurityRules.ContractsAssembliesHaveNoResultTypeOnPublicSurface.

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
        Offending pattern: interface IUserContext { string? SubjectId { get; } } inside
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

    .ReadOnlyRepositoriesNeverTrack(Assembly assembly)  → ConditionList   (P-558; replaces NoGetByIdAsyncOnReadRepository)
        Asserts, by IL scan (nested types — async state machines, closures — included), that no
        read-only repository (implements an IReadRepository-prefixed interface and no IRepository-prefixed
        one) calls a method named AsTracking. Uses
        ReadOnlyRepositoryNeverTracksPredicate. Tested against the real EfCore assembly.
        Rationale: P-558 made IReadRepository<T,TId> "never tracked" and IRepository "always
        tracked" (tracking left the specification). GetByIdAsync is now ON the read contract (it loads
        the whole aggregate, untracked) — the old rule forbidding it was deleted.

InterfaceDeclarationOwnershipPredicate  (class : ICustomRule — internal predicate)
    Constructed with a set of interface type names to detect (e.g., {"IUserContext"} or
    {"ITenantProvider","ICurrentTenantService"}). For each type inspected, checks if
    TypeDefinition.Name is in the configured name set. Returns false (rule violated) with
    failure message including the offending type name and TypeDefinition.Module.Assembly.Name.Name
    for assembly identification. Stateless per evaluation — no cached state.
    Lives in Predicates/ folder. Used by PersistenceInterfaceOwnershipRules.

ReadOnlyRepositoryNeverTracksPredicate  (class : ICustomRule — internal predicate, P-558)
    Scans every method body of a read-only repository type and its nested types for a call/callvirt to a
    method named AsTracking (any declaring type or overload). Lives in Predicates/.
    Used by PersistenceInterfaceOwnershipRules.ReadOnlyRepositoriesNeverTrack.

RepositoryContractCompletenessRules  (static class — repository interface contract completeness predicates)
    All factory methods accept Assembly and return ConditionList. Introduced in WO-016 P-096.
    .AllReadRepositoryImplementorsMustHaveGetByIdAsync(Assembly assembly)  → ConditionList   (P-558; replaces AllRepositoryImplementorsMustHaveExistsAsync)
        Asserts that every non-abstract IReadRepository implementor provides GetByIdAsync (declared,
        inherited or explicit). Uses HasRequiredMethodPredicate("IReadRepository", "GetByIdAsync").

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
    P-558: skips interfaces themselves and accepts an inherited or explicit-interface implementation
    (the EfCore repositories implement the read contract explicitly and via a base class); the
    excludeReadRepository parameter was removed.

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
        Rationale (pre-P-557 design; encryption is interceptor-based, enabled with UseFieldEncryption()
        since P-558 — the rule remains a guard): EncryptionModelConvention detects the .Encrypt() marker and applies EncryptedValueConverter<T>
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

    .CryptographyCoreHasNoThirdPartyDependencies(Assembly cryptographyAssembly)  → ConditionList
        Added by WO-081/P-504 (SK.00.SyncCryptoGateAndArgon2ConfinementLock). Asserts that the
        supplied SharedKernel.Cryptography core assembly has no dependency on any of three
        forbidden substrings: "Konscious" (Konscious.Security.Cryptography.Argon2 —
        SharedKernel.Cryptography.Argon2's third-party dependency, P-495), "Azure.Security.KeyVault",
        and "Azure.Identity" (SharedKernel.Cryptography.KeyVault.Azure's third-party dependencies,
        P-447). Iterative Types.InAssembly(cryptographyAssembly).Should().NotHaveDependencyOn(term)
        calls, one per forbidden term — the exact same multi-term shape as
        CachingAbstractionsHasNoInfrastructureDependencies (RedisTopologyRules), applied here to
        Cryptography's own two third-party-dependency sibling packages instead of Caching's Redis
        family.
        Rationale: both KeyVault.Azure and Argon2 are deliberately-siblinged packages precisely so
        SharedKernel.Cryptography's own core stays zero-third-party-dependency (a documented
        platform invariant, restated by every sibling-package phase since WO-034). Prior to this
        rule, that confinement guarantee was verified exactly once, for KeyVault.Azure only, via a
        `.nuspec`-inspection technique living in 01.Core's own SharedKernel.Consumer.Tests (P-447) —
        a DIFFERENT project, a DIFFERENT technique (packed-metadata inspection vs. compiled
        AssemblyReference inspection), and outside 00.Governance's own jurisdiction entirely. This
        rule gives 00.Governance its own independent, mechanically-enforced version of the same
        guarantee, extended to cover Argon2 (not yet shipped) from day one rather than retrofitted
        after the fact.
        GATING status — a genuine correction of WO-081/P-504's own stated "Depends on: P-492, P-495"
        line: this rule needs NEITHER. It targets the ALREADY-SHIPPED SharedKernel.Cryptography core
        assembly (real and buildable since WO-033) — SHIPPED and GATING-NOW, T-364:
        CryptographyCoreHasNoThirdPartyDependencies_RealCryptographyAssembly_RulePasses in
        CryptoIsolationRulesTests.cs.
        IMPLEMENTATION-TIME TECHNIQUE CORRECTION (T-365, regression proof): the design above and
        WO-081/P-504's own acceptance criterion prescribed a temporary
        `<PackageReference Include="Konscious.Security.Cryptography.Argon2">` added directly to
        SharedKernel.Cryptography.csproj, confirmed to make the rule fail, then reverted before
        commit. Attempted at implementation time and found genuinely infeasible: this repo uses
        NuGet Central Package Management (`Directory.Packages.props`, ManagePackageVersionsCentrally
        = true) with per-project `VersionOverride` DISABLED platform-wide — confirmed via a real,
        fully-reverted attempt (`dotnet build` → error NU1013, "projects that use central package
        management are configured to disable this feature"). Adding a central `PackageVersion` entry
        for a package that will never actually ship there was judged out of scope for a throwaway
        sanity check, and out of this domain's own jurisdiction (Directory.Packages.props is
        devops-lead's). Reproduced instead as a PERMANENT, shipped test —
        CryptographyCoreHasNoThirdPartyDependencies_KonsciousNamespacedDependency_RuleFails in
        CryptoIsolationRulesTests.cs — using the same compiled-in-memory-fixture technique T-154/
        T-155 already established for NoRawSymmetricCipherOutsideCryptography (a stand-in type
        declared directly in a `Konscious.Security.Cryptography`-prefixed namespace inside the
        fixture source, referenced by a second fixture type). Not a weaker proof:
        NotHaveDependencyOn(term) works by namespace-prefix matching against a type's resolved
        dependencies — it cannot distinguish "this namespace came from a real NuGet package" from
        "this namespace came from a type declared in fixture source," so the fixture exercises the
        identical code path a real Argon2 reference would. See
        SK.00.SyncCryptoGateAndArgon2ConfinementLock's own state-map.md entry for the full record.

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

CoreArchitectureRules  (static class — 01.Core-domain conventions; the platform's FIRST
                        01.Core-domain architecture-rule class; WO-083 P-523)
    .DiExtensionsUseTryAddRegistrationConvention(params Assembly[])  → ConditionList
        Asserts that no type in the supplied assemblies contains a Call/Callvirt instruction
        invoking Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions'
        plain AddSingleton, AddScoped, or AddTransient — 01.Core's own P-518 (WO-083)
        standardized every one of its own DI extension methods onto TryAddSingleton/
        TryAddScoped/TryAddTransient/TryAddEnumerable
        (Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
        — a DIFFERENT declaring type) instead. Uses
        NoPlainServiceCollectionRegistrationPredicate (ICustomRule — see below).
        Motivating defect (P-518): before that phase, roughly two-thirds of 01.Core's own
        registration call sites used the plain Add* verb — AddSharedKernelCryptography()
        called twice silently double-registered all nine services, and a consumer's own
        pre-registered ISymmetricEncryptionService implementation was silently overwritten
        instead of honored (the opposite of "first registration wins," the override
        convention library code is expected to respect). TryAdd* makes both failure modes
        structurally impossible.
        Detection precision: AddSingleton/AddScoped/AddTransient and TryAddSingleton/
        TryAddScoped/TryAddTransient/TryAddEnumerable are entirely disjoint method NAMES — no
        BCL overload of either family ever shares a name with the other — so a name-only
        match (mirroring NoSecurityContextSingletonRegistrationPredicate's own "AddSingleton"
        name-only match, WO-057 P-373) would already be unambiguous. This predicate
        ADDITIONALLY requires the resolved callee's DeclaringType.FullName to equal
        "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions"
        (confirmed via direct reflection against the platform's pinned
        Microsoft.Extensions.DependencyInjection(.Abstractions) 10.0.11 at authoring time,
        not assumed) — costs nothing extra (both facts are on the same resolved
        MethodReference operand) and forecloses a hypothetical unrelated same-named method
        appearing in a future 01.Core package.
        Non-generic-overload coverage note (documented, not a limitation): the non-generic
        Add(Type,Type)/Add(Type,Func<IServiceProvider,object>) overloads compile to the
        identical "Call to a MethodReference named AddSingleton" IL shape a closed-generic
        call's GenericInstanceMethod operand already produces (GenericInstanceMethod's own
        Name/DeclaringType resolve exactly like the non-generic overload's) — both shapes are
        already covered without special-casing either.
        NO EXEMPTION LIST of any kind — verified unnecessary, not merely assumed, by reading
        the real source directly before writing the rule: (1) a genuine multi-implementation
        collection registration — SharedKernel.Validation.AddNationalIdValidator<TValidator>()
        (resolved via sp.GetServices<INationalIdValidator>() — a TryAddSingleton there would
        silently drop every country validator after the first) and
        SharedKernel.Cryptography.KeyVault.Azure's IValidateOptions<AzureKeyVaultCryptographyOptions>
        registration (coexisting with the BCL's own DataAnnotationValidateOptions<T>
        registered by the preceding AddValidatedOptions call against the same service type —
        P-518's own implementation session found and fixed a real regression here, a
        TryAddSingleton silently dropping the custom cross-field validator) — both use
        TryAddEnumerable, which already passes structurally since it is not in the forbidden
        name set; this rule asserts absence of the forbidden verbs, never presence of any one
        particular compliant verb, so it needed no per-service-type exception list to get
        this right. (2) SharedKernel.FeatureManagement's deliberately-untouched third-party
        Microsoft.FeatureManagement.ServiceCollectionExtensions.AddFeatureManagement(...) call
        — a DIFFERENT method name ("AddFeatureManagement", not "AddSingleton"/"AddScoped"/
        "AddTransient") on a DIFFERENT declaring type than ServiceCollectionServiceExtensions
        — structurally cannot match either check, so no exemption was needed for it either.
        Scope (P-523): scoped to 01.Core's own DI extension methods for this phase only — the
        caller supplies exactly the nine 01.Core assemblies that declare their own DI
        extension method(s) (SharedKernel.Primitives, .Configuration, .Compression,
        .Cryptography, .Cryptography.Argon2, .Cryptography.KeyVault.Azure,
        .FeatureManagement, .Localization, .Validation). Nothing about the predicate itself is
        01.Core-specific — extending it to another domain's own DI extension methods in a
        future work order is a drop-in reuse of the same predicate, not a redesign.
        Non-vacuous verification WITHOUT editing 01.Core (per this session's explicit "do not
        edit 01.Core" instruction — this family's usual "temporarily-wrong-expectation,
        confirmed to fail, then reverted" proof does not apply to an absence check with no
        expected-value argument to perturb): (a) three contrived fixtures (one per forbidden
        verb) compiled through this exact predicate class prove it genuinely fires; (b) the
        real, compiled SharedKernel.Cryptography.dll's AddSharedKernelCryptography method body
        was inspected read-only via a standalone Mono.Cecil script (against the build output,
        never the source) and confirmed to contain nine genuine TryAddSingleton/
        TryAddKeyedSingleton calls — proving the real-assembly pass-path test scans
        substantial real IL, not an empty method body.
        Failure message names the offending type/method and the forbidden verb.
        Offending pattern: services.AddSingleton<IHmacSigner, HmacSha256Signer>();
        Compliant pattern: services.TryAddSingleton<IHmacSigner, HmacSha256Signer>();

    Note: Introduced in WO-083 P-523 (SK.00.CoreDiRegistrationConventionLock). Lives in
    SharedKernel.ArchitectureTests/Rules/CoreArchitectureRules.cs. Reuses the existing
    Mono.Cecil >= 0.11.5 and NetArchTest.eNt references — no new NuGet dependency, no new SK
    diagnostic ID.

NoPlainServiceCollectionRegistrationPredicate  (class : ICustomRule — internal predicate)
    For every method body declared on a scanned type, flags a Call/Callvirt instruction
    whose resolved MethodReference.Name is "AddSingleton", "AddScoped", or "AddTransient"
    AND whose MethodReference.DeclaringType.FullName equals
    "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions". Returns
    false (rule violated) on the first match, with failure message naming the offending
    type. Lives in Predicates/ folder. Used by
    CoreArchitectureRules.DiExtensionsUseTryAddRegistrationConvention.

UnitOfWorkSeamRules  (static class — shared-contract single-declaration guard; WO-037 P-229, REWRITTEN P-558)
    .SharedContractsAreNotRedeclared(Assembly assembly)  → ConditionList
        Asserts that no type in the assembly is an interface named IUnitOfWork,
        ITransactionalUnitOfWork, IPersistenceTransaction, IRequestContext, IAuditTrailWriter,
        ICurrentActorContext or ICurrentTenantContext unless it is declared in
        SharedKernel.Application.Abstractions. Run against every 05/06/13/16 assembly (tests include
        the real assemblies).
        Rationale (P-558): the unit of work, the caller and the audit writer are ONE contract each,
        owned by 05.Application/SharedKernel.Application.Abstractions (MediatR-free) and implemented
        directly by 06.Persistence and 13.ServiceDefaults.Security. P-557 had two copies of each (05
        local seams + 06 local seams) bridged by adapters in 13.ServiceDefaults.Persistence; the
        adapters drifted (the transactional one never actually worked) and are deleted. A redeclared
        copy would silently bring the bridge problem back.
    Note: the pre-P-558 rule UnitOfWorkInterfacesRemainDistinct and its predicate
    (UnitOfWorkInterfacesRemainDistinctPredicate) asserted the OPPOSITE (two distinct IUnitOfWork
    interfaces) and were deleted with the merge. No SK diagnostic ID.

PersistenceNamespaceConventionRules  (static class — consumer-facing namespace layout of 06.Persistence; P-558)
    .FindMisplacedExtensions(params Assembly[] assemblies)  → IReadOnlyList<string>  (empty = holds)
        Checks every public extension method of a public static class by its receiver type (simple
        name, so no EF Core/hosting reference): receivers IServiceCollection, IHostApplicationBuilder,
        EfCorePersistenceBuilder<T>, DbContextOptionsBuilder must live in namespace
        SharedKernel.Persistence (RegistrationNamespace); ModelBuilder, EntityTypeBuilder<T>,
        PropertyBuilder<T>, ComplexTypePropertyBuilder<T>, MigrationBuilder, DatabaseFacade,
        IQueryable<T>, DbSet<T> in SharedKernel.Persistence.EfCore (EfCoreHelpersNamespace).
        Rationale: a multi-tenant service needed ~22 usings before P-558; now ~7.
        NOTE: NetArchTest prefix rules that forbid "SharedKernel.Persistence.EfCore"/".Npgsql"/".Dapper"
        still match the builder/extension classes by type-name prefix; layers that must not see
        persistence forbid "SharedKernel.Persistence" wholesale.

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
        Compliant pattern: raise domain events via RaiseDomainEvent(); let the application layer
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

    Note: WO-039 P-240 previously registered a ReflectionExemptionRegistry entry for
    SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher's PublishSingle
    factory-delegate closure. P-544 retired that entry: 05.Application's redesign rewrote
    the dispatcher's runtime-type-dispatch technique from a cached MakeGenericMethod
    delegate to Type.MakeGenericType + Activator.CreateInstance (see DispatchAsync/
    BuildNotification in the current source) — a technique this rule's IL walk does not
    match at all (it looks only for a call named exactly "MakeGenericMethod"), so no
    exemption is needed for the current implementation. ReflectionExemptionRegistry.AllowList
    is currently empty. The KNOWN OPEN GAP that 07.Messaging's
    MassTransitEventPublisher.BuildPublisher/MessagingBusBuilder.AddActivity remain
    unregistered MakeGenericMethod call sites is unaffected by this retraction and is still
    a candidate follow-up work order in that domain. Introduced in WO-024 P-153.

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
    Held empty from introduction (WO-024 P-153) through WO-038. From 2026-07-06 (WO-039
    P-240) through P-544 it carried one entry for
    SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher's PublishSingle
    factory-delegate closure. P-544 retired that entry when 05.Application's redesign
    rewrote the dispatcher off MakeGenericMethod entirely (see the SK0012/ReflectionGuardRules
    entry's Note above) — the registry is currently empty again. The fixed P-147
    EncryptionRotationService uses expression trees and needs no exemption. Any team
    requesting an exemption must:
      1. Open a governance review in the root state-map with a written rationale.
      2. Add the (typeFullName, methodName) pair to this registry with the required XML docs.
      3. Reference the work order in both the XML doc and the exemption registration.
    No other suppression mechanism is accepted: #pragma warning disable SK0012,
    [SuppressMessage], or inline comments do not exempt a type from this rule.

    REUSABLE IMPLEMENTATION NOTE (retained from the retired entry, for the NEXT exemption
    request) — closure-free static-lambda naming: a MakeGenericMethod call inside a
    closure-free `static` lambda (e.g. `SomeCache.GetOrAdd(key, static t => { ...
    MakeGenericMethod ... })`) is compiled by Roslyn onto a compiler-generated `<>c`
    singleton cache class nested inside the declaring type (Mono.Cecil
    TypeDefinition.FullName uses "/" as the nesting separator, e.g. "Outer/<>c"), with a
    synthesized method name shaped like "<ContainingMethodName>b__{token}_{ordinal}" — NOT
    the source-level declaring type/method name a naive reading suggests. The exact pair
    must be determined empirically (a temporary Mono.Cecil IL-walk against the real compiled
    assembly, or reading the ReflectionGuardRules failure message with the exemption
    temporarily absent) — never hand-derived from source. Separately, NetArchTest's own
    Types.InAssembly(...) type-discovery layer never surfaces such compiler-generated
    closure types to any ICustomRule (confirmed empirically during the retired
    MediatRDomainEventDispatcher exemption's verification) — a future exemption for a
    closure-based call site should invoke the predicate directly against the real,
    Mono.Cecil-loaded closure TypeDefinition (bypassing NetArchTest's scan) to prove the
    exemption is genuinely load-bearing, mirroring the pattern the now-removed
    ReflectionGuardRulesRealAssemblyTests established. KNOWN OPEN GAP (unaffected by this
    retraction): 07.Messaging's MassTransitEventPublisher.BuildPublisher and
    MessagingBusBuilder.AddActivity remain unregistered MakeGenericMethod call sites — a
    candidate follow-up work order in that domain, not yet dispatched.

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
            types for primitive extensions and SharedKernel.Contracts for pagination types

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
        DTO layer (SharedKernel.Contracts — pagination types, integration event payloads)
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
        (its problem factory, the exception handler — the fallback ExceptionHandlerOptions.ExceptionHandler
        since P-562 R5, no longer an IExceptionHandler — and any future ProblemDetails factory all
        legitimately construct the type).
        Failure message: "{TypeDefinition.FullName}.{method} directly constructs {ProblemDetails |
        HttpValidationProblemDetails}. Use Error.ToProblemDetails() / ResultHttpExtensions from
        SharedKernel.Presentation.WebApi instead."
        Rationale: hand-rolled ProblemDetails construction outside the WebApi package bypasses the
        platform's single error-shape mapping (ErrorTypeStatusCodeMap, traceId population,
        Detail-suppression-outside-Development) and reintroduces the inconsistent error-body problem
        14.Presentation exists to close. Mirrors the precedent set by SK0013 (raw HttpClient) —
        mechanical enforcement, not documentation-only guidance.
        Offending pattern: return Results.Problem(new ProblemDetails { Title = "Bad request",
            Status = 400 }); inside a microservice endpoint
        Compliant pattern (P-562): a typed result from the WebApi core — result.ToOk(),
            error.ToErrorResult() — or, when a ProblemDetails object itself is needed,
            error.ToProblemDetails(httpContext). (ToProblemDetailsResult was deleted by P-562.)
        P-562: SharedKernel.Presentation.OpenApi/.SignalR/.Grpc are NOT exempt and pass unexempted —
            SignalR and gRPC present errors through the core's ErrorPresentation (HubException
            message, google.rpc.Status), the OpenAPI add-on describes the problem shape with
            OpenApiSchema objects (ProblemDetailsSchema), never a ProblemDetails instance. Proven
            against the real assemblies; a control test proves the real WebApi assembly FAILS the
            rule (its problem factory news up ProblemDetails) — the reason it is the one exclusion.

    .NoInlineResultBranchBeforeHttpResultOutsideWebApi(params Assembly[] assemblies) → ConditionList
        Asserts that no method body in the supplied assemblies reads Result/Result<T>.IsSuccess or
        .IsFailure and, within the same method, also constructs/returns a value of an HTTP response
        type — Microsoft.AspNetCore.Http.IResult, a typed-results union
        (Microsoft.AspNetCore.Http.HttpResults.Results`2..`6, P-562), Microsoft.AspNetCore.Mvc.IActionResult
        (P-562), ActionResult, or ActionResult<T> — without that same method also mapping through the
        WebApi core: a call to any member of SharedKernel.Presentation.WebApi.ResultHttpExtensions /
        .Errors.ErrorProblemDetailsExtensions, or a newobj of SharedKernel.Presentation.WebApi.ErrorHttpResult
        (P-562; matched by declaring type, so a service's own same-named ToOk does not count — until P-562
        the escape hatch was a member named "ToProblemDetailsResult", which that redesign deleted; its
        final review moved ErrorHttpResult out of .Errors (R21) and deleted ResultActionResultExtensions/
        ToActionResult (R19), and a stale ErrorHttpResult name had flagged a compliant
        `new ErrorHttpResult(error)`). The names are strings (this package references no runtime
        package), so PresentationLayeringRulesTests also compiles fixtures against the real WebApi,
        Primitives and ASP.NET Core assemblies of the test host: every mapping name and the typed-results
        namespace is pinned by a real-type test. Uses
        NoInlineResultBranchBeforeHttpResultPredicate (ICustomRule — see below). This is a coarser,
        method-level co-occurrence check (IsSuccess/IsFailure callsite + HTTP-result return/local
        type + absence of a core mapping callsite, all within one MethodDefinition) — not a
        full control-flow analysis of "immediately before returning." A method containing all three
        signals is flagged regardless of statement ordering; this is a deliberate over-approximation
        favoring detection over precision, consistent with the documented limitation already
        recorded for HealthCheckTagIntegrityRules's literal-collection technique (no full data-flow
        analysis).
        Caller supplies every assembly to be checked EXCEPT SharedKernel.Presentation.WebApi itself —
        same caller-controlled exclusion convention as the sibling rule above (ResultHttpExtensions's
        own implementation legitimately reads IsSuccess/IsFailure and returns typed results).
        Failure: NetArchTest reports the offending type in FailingTypeNames.
        Rationale: inline "if (result.IsSuccess) ... else ..." branching immediately before
        returning an HTTP response type duplicates the platform's Result→HTTP mapping logic at every
        call site. Implemented as a NetArchTest rule rather than a Roslyn analyzer because the
        detection surface is IL-level method-body co-occurrence, consistent with how SK0301-style
        domain/application misuse rules are implemented. Limitation: an async handler's body lives in
        its void-returning state machine, so it is flagged only when the machine keeps the HTTP result
        in a local.
        Offending pattern: if (result.IsSuccess) return TypedResults.Ok(result.Value); return
            TypedResults.Problem(statusCode: 404); inside a Minimal API endpoint delegate or controller action
        Compliant pattern: return result.ToOk(); — or a written-out failure branch that routes through
            the core: if (result.IsFailure) return result.Error.ToErrorResult();

    .NoOpenApiStackDependencyOutsideOpenApiAddOn(params Assembly[] assemblies) → ConditionList   (P-562)
        Asserts that no type in the supplied assemblies depends on "Asp.Versioning",
        "Microsoft.AspNetCore.OpenApi", "Microsoft.OpenApi" or "Scalar.AspNetCore" (the third-party
        stack of SharedKernel.Presentation.OpenApi). Types.InAssemblies(...).Should()
        .NotHaveDependencyOn(term).And()... — the same multi-term shape as
        CryptoIsolationRules.CryptographyCoreHasNoThirdPartyDependencies and
        RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies.
        Rationale: P-562's package layout (D0) puts every third-party dependency of the HTTP boundary
        in the OpenAPI add-on, so the WebApi core every HTTP service references stays dependency-free
        and SignalR/gRPC never pull in a document generator; it is also the domain's most fragile
        coupling (Asp.Versioning.OpenApi reflects over Microsoft.AspNetCore.OpenApi internals).
        Caller passes SharedKernel.Presentation.WebApi, .SignalR and .Grpc — never .OpenApi (caller-
        controlled exclusion). A consuming service's own assemblies are out of scope (a service may
        reference Asp.Versioning to declare [ApiVersion]). Proven against the real assemblies, plus a
        control proving the real add-on FAILS (non-vacuous) and a contrived Asp.Versioning fire path.

    .GrpcNeverReferencesContracts(Assembly grpcAssembly) → ConditionList
        Asserts that no type in SharedKernel.Presentation.Grpc has any dependency on the
        SharedKernel.Contracts namespace. Single Types.InAssembly(grpcAssembly).Should()
        .NotHaveDependencyOn("SharedKernel.Contracts") call — an exact structural mirror of
        CommunicationLayeringRules.GrpcNeverReferencesContracts for the sibling 11.Communication
        gRPC package.
        Rationale: mechanizes the root CLAUDE.md Hard rule "SharedKernel.Presentation.Grpc must
        never reference 04.Contracts." SharedKernel.Presentation.Grpc takes a deliberate
        ProjectReference on SharedKernel.Presentation.WebApi (since P-562 for everything the
        protocols on the shared pipeline must agree on: ErrorPresentation, AddSharedKernelAuthorization
        behind RequirePermission and its siblings, the correlation id). SharedKernel.Presentation.WebApi
        no longer references 04.Contracts (the reference was unused and has been removed), so
        SharedKernel.Contracts.dll is no longer in SharedKernel.Presentation.Grpc's reference
        closure through WebApi at all; this rule now guards against a direct or transitive
        reference being reintroduced and used. NotHaveDependencyOn is the correct, sufficient
        mechanism either way: it inspects each scanned type's ACTUAL Mono.Cecil-observed dependency
        namespaces, never the assembly-level reference list a ProjectReference populates — a type
        merely being reachable via the reference closure does not fail this check, only an actual
        SharedKernel.Contracts.* type USE inside a SharedKernel.Presentation.Grpc type does.
        Empirically confirmed: the real, shipped assembly passes today (no such use exists) while
        remaining able to catch the violation the moment one is introduced.
        Failure message: "SharedKernel.Presentation.Grpc has a dependency on SharedKernel.Contracts.
        The gRPC package is a protocol adapter — cross-service DTO types must not flow into gRPC
        transport code."
        No exemption is permitted for this rule, mirroring
        CommunicationLayeringRules.GrpcNeverReferencesContracts's own "no exemption" precedent.
        Note: coordinator-directed extension to WO-074/P-469, evaluated and accepted by this domain
        in the same implementation pass rather than deferred to a separately-planned phase — the
        mechanism and rationale are a direct, near-zero-novelty copy of the already-ratified
        sibling-domain rule (WO-026 P-167). Not tracked under any D-/C-/T- task ID in this file's
        SK.00.GrpcErrorMappingGuard phase table (that table covers only SK0036) — see the Changelog
        entry for this addition's own record.

    Permitted exemption list:
        - SharedKernel.Presentation.WebApi — never passed to the two WebApi-exclusion factory methods
          by the caller; there is no internal namespace-prefix exemption inside either predicate. Any
          future legitimate exception (e.g., a second presentation package that must also construct
          ProblemDetails directly) must be documented here before being added to either predicate
          as an internal exemption — until then, exclusion is achieved exclusively by caller choice
          of which assemblies to pass, identical in spirit to RedisTopologyRules's caller-supplied
          assembly lists. After P-562 the three sibling packages (.OpenApi, .SignalR, .Grpc) are
          checked, not exempt.
        - SharedKernel.Presentation.OpenApi — never passed to NoOpenApiStackDependencyOutsideOpenApiAddOn
          (it owns that stack).

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
      (2) HTTP-result-type signal: MethodDefinition.ReturnType.Name is "IResult", "IActionResult"
          (P-562), "ActionResult", or starts with "ActionResult`1", or the type is a typed-results
          union — Namespace "Microsoft.AspNetCore.Http.HttpResults" and Name starting "Results`"
          (P-562) — OR any local variable (MethodDefinition.Body.Variables) typed identically, to also
          catch the "build a local, return it later" shape
      (3) Escape-hatch signal (P-562): a Call or Callvirt whose MethodReference.DeclaringType.FullName
          is SharedKernel.Presentation.WebApi.ResultHttpExtensions or
          .Errors.ErrorProblemDetailsExtensions, or a Newobj of
          SharedKernel.Presentation.WebApi.ErrorHttpResult (root namespace since P-562 R21; R19 deleted
          ResultActionResultExtensions, formerly also listed), anywhere in the method body —
          presence of this signal suppresses the violation regardless of signals (1) and (2). Before
          P-562: a member named "ToProblemDetailsResult" (deleted by that redesign).
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
        Asserts that no type named "TracingBehavior" or "CacheInvalidationBehavior" (exact simple
        name match, caller-supplied HashSet<string> — never hardcoded inside the predicate) in the
        supplied assemblies has a member, field, or method-signature reference to a forbidden
        concrete-infrastructure namespace: "SharedKernel.Caching.FusionCache",
        "SharedKernel.Caching.Redis" (bare prefix — matches Redis.Core and all four Redis
        capability packages), "SharedKernel.Persistence" (excluding
        "SharedKernel.Persistence.Abstractions"), "SharedKernel.Messaging" (excluding
        "SharedKernel.Messaging.Abstractions"). Uses
        NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate (ICustomRule — see below).
        Failure message names the offending behavior type and the forbidden namespace referenced.
        As of P-544, CacheInvalidationBehavior lives in the sibling
        SharedKernel.Application.Behaviors.Caching package, not SharedKernel.Application.Behaviors
        itself — callers pass both assemblies.
        Rationale: mirrors the existing, already-enforced
        SharedKernelLayeringRules.ApplicationNeverReferencesConcreteInfrastructure guarantee, made
        explicit and behavior-scoped for the two behaviors — the same purity
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

    P-544 REMOVAL NOTE: .NoHandRolledRetryLoopOutsideResilienceBehavior and its backing
        NoTaskDelayOutsideResilienceBehaviorPredicate were retracted when 05.Application's redesign
        dropped ResilienceBehavior/IRetryableRequest entirely — the rule existed solely to police
        Task.Delay usage relative to that one now-nonexistent type. Do not reintroduce a
        ResilienceBehavior-specific rule by this name; if 05.Application ever reintroduces a retry
        mechanism, design its governance rule fresh against that mechanism's actual shape.

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

MetricsInstrumentationRules  (static class — Histogram outcome-tag completeness predicate; WO-038 P-235)
    .RequestDurationRecordsIncludeOutcomeTag(Assembly assembly)  → ConditionList
        Asserts that every method in the supplied assembly containing a Call/Callvirt
        instruction whose MethodReference.Name == "Record" and whose MethodReference
        .DeclaringType is a GenericInstanceType whose ElementType.FullName ==
        "System.Diagnostics.Metrics.Histogram`1" also contains, in the SAME method body, an
        Ldstr instruction whose operand is exactly "outcome". Uses
        RequestDurationRecordMissingOutcomeTagPredicate (ICustomRule — see below). Reuses the
        IL Ldstr literal-collection technique first established by HealthCheckTagIntegrityRules
        (WO-027 P-173) — no new Mono.Cecil technique, but the search predicate (a
        Histogram<T>.Record call-site scan) is new: the first Histogram<T>.Record call-site
        check in this domain.
        Exempt    : None. Every Histogram<double>.Record call site in the target assembly must
                    carry an "outcome" tag literal. A future legitimate exception (e.g. a
                    histogram with no outcome concept) must be documented here, by type or
                    method name, before being exempted.
        Failure message names the offending type, method, and the Record call site.
        Rationale: the WO-038 audit found MetricsBehavior<,> (P-217) records
        sharedkernel.application.request.duration with no outcome tag, making it impossible to
        distinguish success/failure/exception/cached/duplicate/unauthorized outcomes in
        dashboards. This rule mechanically closes the gap so no future Histogram<T>.Record call
        site in 05.Application/05.Application.Behaviors can regress to a bare, outcome-less
        measurement.
        Offending pattern: ApplicationDiagnostics.RequestDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("request.name", requestName));
        Compliant pattern: ApplicationDiagnostics.RequestDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("request.name", requestName),
            new KeyValuePair<string, object?>("outcome", outcome));
        Note (P-544): the histogram's unit changed ms → s and it now resolves through
        IMeterFactory (see ApplicationMetrics in 05.Application/CLAUDE.md) — this rule inspects
        the Record call site's tag literals only, so the unit/resolution change does not affect
        its detection logic. Real-assembly verification against 05.Application's own
        MetricsBehavior<,> remains that domain's responsibility.

RequestDurationRecordMissingOutcomeTagPredicate  (class : ICustomRule — internal predicate)
    For each type, walks TypeDefinition.Methods.Body.Instructions. Collects every method that
    contains a Call/Callvirt instruction whose MethodReference.Name == "Record" and whose
    MethodReference.DeclaringType is a GenericInstanceType with ElementType.FullName ==
    "System.Diagnostics.Metrics.Histogram`1". For each such method, re-scans the SAME
    Instructions collection for an Ldstr instruction whose operand (cast to string) equals
    exactly "outcome". Returns false (rule violated) for the first Record-containing method
    found without a companion "outcome" Ldstr literal, with failure message naming the
    declaring type, the method, and the Record call site. Lives in Predicates/ folder. Used by
    MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag. This is a method-level
    co-occurrence check, not a data-flow analysis — the same documented-limitation philosophy
    already established by HealthCheckTagIntegrityRules and
    NoInlineResultBranchBeforeHttpResultPredicate: an "outcome" literal used for a genuinely
    unrelated purpose elsewhere in the same method would satisfy the check without actually
    tagging the Record call. This is accepted as a deliberate over-approximation; narrow it
    only if a real false negative is found in production code.

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
        Rationale: IUnitOfWork.ExecuteInTransactionAsync (SharedKernel.Application.Abstractions;
        ITransactionalUnitOfWork until P-558) is the only permitted transaction entry point for
        application handlers. Direct injection of IDbContextTransaction couples
        application code to EF Core's specific transaction implementation, making the
        transaction abstraction boundary unenforceable.
        Offending pattern: class CreateOrderHandler {
            public CreateOrderHandler(IDbContextTransaction tx) { }
        }
        Compliant pattern: class CreateOrderHandler {
            public CreateOrderHandler(IUnitOfWork unitOfWork) { }   // ExecuteInTransactionAsync
        }
        Exemptions: SharedKernel.Persistence.* namespaces (persistence implementation layer).

    .NoDirectEfPropertyUsageInEfCoreAssembly(Assembly)  → ConditionList
        Asserts that no method body in the supplied SharedKernel.Persistence.EfCore assembly
        contains a Call or Callvirt IL opcode targeting Microsoft.EntityFrameworkCore.EF::Property
        (MethodReference.Name == "Property" AND MethodReference.DeclaringType.FullName ==
        "Microsoft.EntityFrameworkCore.EF"). Uses NoDirectEfPropertyUsagePredicate (ICustomRule —
        see below). Carries NO exemption mechanism — no namespace guard, no allow-list registry —
        mirroring NoSpecificationEvaluatorDowncastInEfCoreAssembly's own zero-exemption precedent
        exactly. Failure message names the offending type and method containing the direct call.
        Rationale: EF.Property<TProperty>(object entity, string propertyName) called directly in
        ordinary executable code forces client-side evaluation of the surrounding query — the exact
        defect class fixed once already at P-105 (EfReadRepository.GetByIdsAsync) and again at P-316
        (TenantedRepository's two GetByIdForTenantAsync* methods), proving a documented lesson alone
        does not prevent a second, independent occurrence in the same package. The rule's IL
        Call/Callvirt opcode-presence technique structurally, automatically distinguishes this direct
        misuse from the platform's one legitimate EF.Property<T> pattern — a tenant/shadow-property
        global query filter built via HasQueryFilter(Expression<Func<TEntity,bool>> filter) — because
        the C# compiler never emits a Call/Callvirt opcode targeting EF.Property when the call appears
        inside a lambda whose CONVERTED type is Expression<TDelegate>: it lowers the entire lambda body
        into System.Linq.Expressions.Expression-builder calls instead, referencing EF.Property<T>'s
        MethodInfo only as metadata (ldtoken/GetMethodFromHandle, or an Expression.Call(MethodInfo, ...)
        argument), never as a direct invocation. No exemption is therefore needed for that legitimate
        case — it is excluded by construction, not by an allow-list.
        Offending pattern: return dbSet.AsEnumerable()
            .FirstOrDefault(e => EF.Property<TId>(e, "Id").Equals(id));  // client-side evaluation
        Compliant pattern (P-316-corrected shape): build the predicate via
            Expression.Property(parameterExpr, "Id") + Expression.Lambda<Func<T,bool>>(...) so the
            comparison translates to SQL instead of pulling every row into memory first.
        Legitimate, never-flagged pattern: modelBuilder.Entity<T>().HasQueryFilter(
            e => EF.Property<Guid>(e, "TenantId") == _currentTenantService.TenantId);  // inside an
            Expression<Func<T,bool>> lambda — no Call opcode against EF.Property is ever emitted here.
        Note: Introduced in WO-051 P-327. No new SK diagnostic ID — the fourth predicate in this
        class, all four carrying none, following the class's established "EfCore-package-scoped
        regression-prevention rule, no SK ID" shape rather than the platform-wide SK0013/SK0020
        Roslyn-analyzer shape. Motivating incidents: P-105 (EfReadRepository.GetByIdsAsync) and P-316
        (TenantedRepository) — the second occurrence of the identical defect class in the same
        package is what elevated this from "fix the bug" to "mechanically prevent recurrence,"
        mirroring the SK0012/ReflectionExemptionRegistry precedent (P-147) for the same escalation
        pattern. UNVERIFIABLE against the real, corrected assembly until 06.Persistence's P-316 ships
        — see Cross-Domain Dependencies in 00.Governance/state-map.md's SK.00.EfPropertyUsageGuard
        phase block for the explicit GATING (not deferred) status, mirroring the
        SK.00.SearchTopology/SK.00.IntelligenceTopology/SK.00.WorkflowTopology precedent.
        CORRECTED AT IMPLEMENTATION TIME (2026-07-31, SK.00.EfPropertyUsageGuard closeout): by the
        time this phase was implemented, 06.Persistence's P-316 had already shipped — confirmed on
        disk, not assumed: 06.Persistence/state-map.md's C-101 is ● Complete, and TenantedRepository.cs
        contains no direct EF.Property<TId> call for either GetByIdForTenantAsync or
        GetByIdForTenantIncludingDeletedAsync (both use the private static BuildIdEqualsPredicate
        helper's Expression.Parameter/Property/Equal/Lambda construction instead); the one surviving
        EF.Property<bool> call in that file (the soft-delete re-filter) is passed as an
        Expression<Func<T,bool>> argument to IQueryable<T>.Where — the exact structural
        self-exemption shape T-274 proves, not a violation. Real-assembly verification was therefore
        wired in THIS phase, not deferred: SharedKernel.ArchitectureTests.Tests.csproj gained a
        test-only ProjectReference (PrivateAssets="all") to SharedKernel.Persistence.EfCore, and
        EfCorePackageHygieneRulesTests.NoDirectEfPropertyUsageInEfCoreAssembly_RealEfCoreAssembly_RulePasses
        confirms zero violations against the shipped assembly — mirroring the
        SK.00.StorageTopology/SK.00.SearchTopology/SK.00.IntelligenceTopology/SK.00.WorkflowTopology
        precedent for this exact class of dependency-resolved-before-implementation finding.

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
        ConditionList per forbidden term (four total since P-558 deleted the .PostgreSQL package, in order):
        "SharedKernel.Persistence.EfCore", "SharedKernel.Persistence.Dapper",
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

ServiceDefaultsSchedulingLayeringRules  (static class — mechanical lock on the 13.ServiceDefaults→19.Scheduling grant; WO-073/P-466, coordinator-directed follow-up)
    .OnlyReachesSchedulerProbeTypes(Assembly serviceDefaultsAssembly) → ConditionList
        Asserts that no type in the supplied SharedKernel.ServiceDefaults assembly (including any
        nested/compiler-generated type) has any dependency on SharedKernel.Scheduling other than
        exactly SharedKernel.Scheduling.Probes.ISchedulerServiceProbe or
        SharedKernel.Scheduling.Probes.SchedulerServiceHealth. Uses
        ServiceDefaultsOnlyReachesSchedulerProbeTypesPredicate (ICustomRule — see below).
        Rationale: mechanizes the root CLAUDE.md Hard rule granting 13.ServiceDefaults a narrow,
        individually-named ProjectReference to SharedKernel.Scheduling "solely to resolve
        ISchedulerServiceProbe/SchedulerServiceHealth for AddSchedulerReadinessCheck. No other
        19.Scheduling type may be reached through this exception." Once the real ProjectReference
        exists (confirmed on disk at SharedKernel.ServiceDefaults.csproj), a plain
        NotHaveDependencyOn("SharedKernel.Scheduling") check is too broad — it would also forbid
        the two permitted types — so this rule needs a genuine allow-listed-type check, not a
        namespace-prefix ban.
        THIS IS ITS OWN, SEPARATELY-EARNED GRANT — never reasoned about by analogy to the sibling
        13.ServiceDefaults→17.Workflows grant (P-291/WO-047; now also mechanically locked by
        ServiceDefaultsWorkflowLayeringRules — see below), and deliberately NOT folded into one
        parameterized "any higher-numbered probe grant" helper shared with it — the root brain is
        emphatic that doing so would quietly license the exact analogy it forbids. A future grant
        of this shape needs its own independently-designed predicate and rule.
        Offending pattern: a type inside SharedKernel.ServiceDefaults constructing, injecting, or
            otherwise referencing IScheduledJobRegistry, ScheduledCommandJob<TCommand>,
            SchedulingOptions, MisfirePolicy/OverlapPolicy, ScheduledJobDefinition, TenantScope, or
            any other SharedKernel.Scheduling.* type — including the internal SchedulerServiceProbe
            implementation, which sits in the SAME Probes namespace as the two permitted types
        Compliant pattern: exactly the shape SchedulerReadinessHealthCheck already uses — inject
            ISchedulerServiceProbe, call .ProbeAsync(...), read the returned SchedulerServiceHealth
        No exemption is permitted for this rule — the grant itself already IS the exemption; this
        rule exists precisely to keep that exemption from silently widening.
        Note: introduced as a coordinator-directed extension once 13.ServiceDefaults shipped
        AddSchedulerReadinessCheck() and the real ProjectReference (confirmed on disk). Real-
        assembly verification is IMPLEMENTED, not merely designed — the real, shipped
        SharedKernel.ServiceDefaults assembly passes with zero violations (its only
        SharedKernel.Scheduling consumption is exactly ISchedulerServiceProbe/SchedulerServiceHealth
        via SchedulerReadinessHealthCheck/SchedulerReadinessHealthCheckExtensions). Not tracked
        under any D-/C-/T- task ID in any phase's task table in this file — see the Changelog entry
        for this addition's own record. No new SK diagnostic ID.

ServiceDefaultsOnlyReachesSchedulerProbeTypesPredicate  (class : ICustomRule — internal predicate)
    Detection surface: for the supplied type AND every type nested inside it (recursively —
    compiler-generated async state machines and lambda/local-function display classes are always
    nested types), inspects field types, method return types, method parameter types, method-body
    local-variable types, method-body Call/Callvirt/Newobj/field-access instruction operand
    declaring/field/return types, the base type, and implemented interfaces.
    GenericInstanceType arguments are unwrapped (e.g. Task<SchedulerServiceHealth> is inspected for
    SchedulerServiceHealth as a generic argument, not only for Task<> itself).
    LOAD-BEARING DESIGN CHOICE, empirically necessary, not defensive redundancy: the real,
    sanctioned consumption site (SchedulerReadinessHealthCheck.CheckHealthAsync) is an async
    method — the C# compiler lowers its body into a compiler-generated state-machine NESTED TYPE,
    and the actual `call instance ISchedulerServiceProbe::ProbeAsync()` IL instruction (returning
    Task<SchedulerServiceHealth>) and the local SchedulerServiceHealth-typed variable both live
    inside that nested state machine's MoveNext method — never in the outer type's own members. A
    predicate scoped only to the outer TypeDefinition NetArchTest hands in would silently miss any
    future violation introduced inside an async method (exactly the shape most likely to recur in
    a health-check adapter). Confirmed by a dedicated fire-path test proving a forbidden reference
    reachable ONLY from inside an async state machine (never from the outer type's own
    fields/method signatures) is still caught.
    Fires false (rule violated) on the first forbidden reference found, anywhere in the type or its
    nested-type closure. A reference resolves as forbidden when its namespace starts with
    "SharedKernel.Scheduling" AND its FullName is neither
    "SharedKernel.Scheduling.Probes.ISchedulerServiceProbe" nor
    "SharedKernel.Scheduling.Probes.SchedulerServiceHealth".
    Lives in Predicates/ folder. Used by
    ServiceDefaultsSchedulingLayeringRules.OnlyReachesSchedulerProbeTypes. This predicate is
    purpose-built for exactly this one grant — see that rule's own "never generalize" note above;
    the same discipline applies here.

ServiceDefaultsWorkflowLayeringRules  (static class — mechanical lock on the 13.ServiceDefaults→17.Workflows grant; WO-047/P-291, closed by root Phase Backlog P-490/WO-080)
    .OnlyReachesWorkflowProbeTypes(Assembly serviceDefaultsAssembly) → ConditionList
        Asserts that no type in the supplied SharedKernel.ServiceDefaults assembly (including any
        nested/compiler-generated type) has any dependency on SharedKernel.Workflows.Temporal other
        than exactly SharedKernel.Workflows.Temporal.Health.IWorkflowServiceProbe or
        SharedKernel.Workflows.Temporal.Health.WorkflowServiceHealth. Uses
        ServiceDefaultsOnlyReachesWorkflowProbeTypesPredicate (ICustomRule — see below).
        Rationale: mechanizes the root CLAUDE.md Hard rule granting 13.ServiceDefaults a narrow,
        individually-named ProjectReference to SharedKernel.Workflows.Temporal "solely to resolve
        IWorkflowServiceProbe/WorkflowServiceHealth for AddWorkflowReadinessCheck. No other
        17.Workflows type may be reached through this exception." This grant predates the sibling
        19.Scheduling grant (WO-047 vs. WO-073) but was, until P-490, the unenforced one — an
        inversion of the order in which the two grants were made.
        THIS IS ITS OWN, SEPARATELY-EARNED GRANT — never reasoned about by analogy to the sibling
        13.ServiceDefaults→19.Scheduling grant (P-466/WO-073, ServiceDefaultsSchedulingLayeringRules
        above), and deliberately NOT folded into one parameterized "any higher-numbered probe grant"
        helper shared with it — same root-brain instruction, same discipline, applied in both
        directions.
        Offending pattern: a type inside SharedKernel.ServiceDefaults constructing, injecting, or
            otherwise referencing ITemporalClient, TemporalOptions, WorkflowBase/ActivityBase,
            IWorkflowDispatcher, ITemporalRawClientAccessor, CommandActivity<TCommand>,
            IWorkflowIdFactory, or any other SharedKernel.Workflows.Temporal.* type — including the
            internal WorkflowServiceProbe implementation, which sits in the SAME Health namespace as
            the two permitted types
        Compliant pattern: exactly the shape WorkflowReadinessHealthCheck already uses — inject
            IWorkflowServiceProbe, call .ProbeAsync(...), read the returned WorkflowServiceHealth
        No exemption is permitted for this rule — the grant itself already IS the exemption; this
        rule exists precisely to keep that exemption from silently widening.
        Note: both traps ServiceDefaultsOnlyReachesSchedulerProbeTypesPredicate hit were
        independently re-verified against the real Workflows shape (not assumed by analogy) and
        both applied again — see the predicate entry below. Real-assembly verification is
        IMPLEMENTED, not merely designed: the real, shipped SharedKernel.ServiceDefaults assembly
        passes with zero violations (its only SharedKernel.Workflows.Temporal consumption is exactly
        IWorkflowServiceProbe/WorkflowServiceHealth via
        WorkflowReadinessHealthCheck/WorkflowReadinessHealthCheckExtensions), and this was proven
        NON-VACUOUS by a temporary in-session reintroduction of a forbidden TemporalOptions field
        into the real WorkflowReadinessHealthCheck.cs (the real-assembly test genuinely failed),
        reverted before commit. Not tracked under any D-/C-/T- task ID in any phase's task table in
        this file — closed directly from the root Phase Backlog (P-490/WO-080); see the Changelog
        entry for this addition's own record. No new SK diagnostic ID.

ServiceDefaultsOnlyReachesWorkflowProbeTypesPredicate  (class : ICustomRule — internal predicate)
    Detection surface: identical technique to ServiceDefaultsOnlyReachesSchedulerProbeTypesPredicate
    (see above) — for the supplied type AND every type nested inside it (recursively), inspects
    field types, method return types, method parameter types, method-body local-variable types,
    method-body Call/Callvirt/Newobj/field-access instruction operand declaring/field/return types,
    the base type, and implemented interfaces. GenericInstanceType arguments are unwrapped.
    LOAD-BEARING DESIGN CHOICE, empirically necessary, not defensive redundancy: the real,
    sanctioned consumption site (WorkflowReadinessHealthCheck.CheckHealthAsync) is an async method —
    the C# compiler lowers its body into a compiler-generated state-machine NESTED TYPE, and the
    actual `callvirt instance IWorkflowServiceProbe::ProbeAsync()` IL instruction (returning
    Task<Result<WorkflowServiceHealth>>) and the local WorkflowServiceHealth-typed variable both
    live inside that nested state machine's MoveNext method — never in the outer type's own
    members. Confirmed by a dedicated fire-path test proving a forbidden reference reachable ONLY
    from inside an async state machine (never from the outer type's own fields/method signatures)
    is still caught.
    Fires false (rule violated) on the first forbidden reference found, anywhere in the type or its
    nested-type closure. A reference resolves as forbidden when its namespace starts with
    "SharedKernel.Workflows.Temporal" AND its FullName is neither
    "SharedKernel.Workflows.Temporal.Health.IWorkflowServiceProbe" nor
    "SharedKernel.Workflows.Temporal.Health.WorkflowServiceHealth".
    Lives in Predicates/ folder. Used by
    ServiceDefaultsWorkflowLayeringRules.OnlyReachesWorkflowProbeTypes. This predicate is
    purpose-built for exactly this one grant — see that rule's own "never generalize" note above;
    the same discipline applies here.

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

NoDirectEfPropertyUsagePredicate  (class : ICustomRule — internal predicate; WO-051 P-327)
    No exemption guard — carries none, mirroring NoSpecificationEvaluatorDowncastPredicate's own
    zero-exemption shape. For each type, walks TypeDefinition.Methods.Body.Instructions for Call
    or Callvirt opcodes whose operand MethodReference satisfies both:
      (1) MethodReference.Name == "Property" (exact match)
      (2) MethodReference.DeclaringType.FullName == "Microsoft.EntityFrameworkCore.EF" (exact match)
    Returns false (rule violated) on the first match, with failure message naming the offending
    type and method. Lives in Predicates/ folder. Used by
    EfCorePackageHygieneRules.NoDirectEfPropertyUsageInEfCoreAssembly.
    Structural note: this technique never fires against EF.Property<T> usage embedded inside a
    lambda whose converted type is Expression<TDelegate> (e.g. a HasQueryFilter(...) global query
    filter) — the C# compiler lowers such lambda bodies into Expression-builder calls instead of
    emitting a Call/Callvirt against EF.Property directly, so the platform's one legitimate usage
    pattern is excluded by construction, not by an allow-list. See
    EfCorePackageHygieneRules.NoDirectEfPropertyUsageInEfCoreAssembly's own entry above for the
    full rationale and the empirical-verification obligation on this claim.

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

LoggingEventIdIntegrityAssertion  (public class — reflection/Mono.Cecil-based platform-wide
invariant helper, not ConditionList/ICustomRule; WO-041 P-250)
    .AssertGloballyUniqueAndInRange(IReadOnlyDictionary<Assembly, (int RangeMin, int RangeMax)> assemblyRanges)
        Mirrors ApplicationPipelineRules.PipelineOrderAssertion's precedent — a plain public
        helper, not a NetArchTest ConditionList, because "every [LoggerMessage] EventId across
        every shipped assembly is globally unique and falls inside its own assembly's reserved
        range" is a cross-assembly, whole-platform invariant with no single-assembly "fire on a
        contrived violating assembly" shape a ConditionList/ICustomRule scan naturally expresses.
        For each Assembly key in assemblyRanges, loads the assembly via Mono.Cecil
        (AssemblyDefinition.ReadAssembly(assembly.Location)) and walks EVERY TypeDefinition in
        the module PLUS every TypeDefinition.NestedTypes entry RECURSIVELY. This deliberately
        does NOT use NetArchTest's Types.InAssembly(...) projection — SK0012's own documented gap
        (see ReflectionGuardRules above) proved that projection is blind to compiler-generated
        and nested types; walking Mono.Cecil's ModuleDefinition.Types/NestedTypes directly
        sidesteps that gap entirely for this rule, so a [LoggerMessage] partial method declared
        inside a nested logging-helper class (a common authoring pattern) is never missed.
        For each MethodDefinition carrying a CustomAttribute whose AttributeType.FullName ==
        "Microsoft.Extensions.Logging.LoggerMessageAttribute", extracts the EventId via the
        attribute's "EventId" named property (CustomAttribute.Properties,
        CustomAttributeNamedArgument.Name == "EventId") if present, falling back to the first
        int-typed positional CustomAttribute.ConstructorArguments entry for the
        constructor-overload authoring style (covers both `[LoggerMessage(EventId = 5042,
        Level = LogLevel.Information, Message = "...")]` and
        `[LoggerMessage(5042, LogLevel.Information, "...")]`). Collects every (EventId,
        DeclaringType, MethodName, Assembly) tuple found across ALL supplied assemblies into one
        aggregate pass, then performs two independent checks:
          (1) Global uniqueness: any EventId value shared by more than one distinct
              (DeclaringType, MethodName) tuple across the ENTIRE aggregate set — including
              across different assemblies — is a collision.
          (2) Range membership: for each tuple, RangeMin <= EventId <= RangeMax using the
              caller-supplied range for that tuple's OWN declaring assembly (looked up from
              assemblyRanges by the Assembly the tuple was discovered in).
        Aggregates every violation found (does not stop at the first) and throws a single
        test-framework-agnostic assertion exception listing every collision (naming both
        offending declaring-type/method sites) and every out-of-range EventId (naming the
        offending type/method, the actual EventId, and the expected range) — mirroring
        PipelineOrderAssertion's aggregate-failure-message convention.
        Caller-supplied range dictionary: 00.Governance never references SharedKernel.Primitives
        directly (00.Governance references nothing). The consuming test project builds the
        assemblyRanges dictionary itself, typically as
        { typeof(SomeApplicationType).Assembly: (LoggingEventIdRanges.Application,
        LoggingEventIdRanges.Application + 999), ... } for every shipped production assembly —
        keeping SharedKernel.Primitives.Logging.LoggingEventIdRanges (P-249) as the single
        source of truth for range values while this helper itself stays dependency-free.
        Rationale: the WO-041 audit found a confirmed live EventId collision
        (SharedKernel.Caching.Redis.Core vs SharedKernel.Caching.Redis.PubSub, both using
        4001/4002) plus three internal collisions inside SharedKernel.Messaging.MassTransit —
        this is the mechanical enforcement of the per-domain LoggingEventIdRanges registry the
        root CLAUDE.md's Logging Conventions section now mandates; a documented convention alone
        already drifted once and will drift again under multi-team, multi-package growth without
        a build-time gate.
        Real-assembly status: EXPECTED TO FAIL if pointed at the platform's real shipped
        assemblies today — the WO-041 audit's own confirmed collisions have not yet been
        retrofitted (all ten WO-041 phases are `○` Pending per root CLAUDE.md as of 2026-07-08,
        including 01.Core's own P-249 LoggingEventIdRanges registry this rule's real-world range
        dictionary depends on). Design/implementation/tests for this phase use contrived
        in-memory multi-assembly Mono.Cecil fixtures only, consistent with every
        "designed-ahead-of-a-pending-retrofit" precedent in this domain
        (SK.00.ServiceDefaultsGovernance, SK.00.MetricsOutcomeTagAndMisregistrationGuard,
        SK.00.CryptoDelegationAndUowSeamGuard, SK.00.ApplicationPipelineArchRules).

WellKnownConstantOwnershipAssertion  (public class — Mono.Cecil-based cross-assembly platform-
wide invariant helper, not ConditionList/ICustomRule; WO-042 P-264)
    .AssertSoleDeclaration(IReadOnlyDictionary<string, string> canonicalValues,
                           IReadOnlyCollection<string> owningTypeFullNames,
                           IReadOnlyCollection<Assembly> assembliesToScan)
        Mirrors LoggingEventIdIntegrityAssertion's precedent — a plain public helper, not a
        NetArchTest ConditionList, because "no assembly other than the owning one may declare
        its own independently-valued literal for a canonical cross-cutting value" is a
        cross-assembly, whole-platform invariant with no single-assembly "fire on a contrived
        violating assembly" shape a ConditionList/ICustomRule scan naturally expresses.
        Reuses StringConstantsClassDetector's field-shape + literal-value resolution technique
        (WO-028 P-178) — extended here to walk EVERY TypeDefinition in each scanned assembly
        (not only the abstract-sealed "constants class" shape StringConstantsClassDetector's
        original caller assumed), so a stray const/static readonly string field on an ordinary
        class is caught too. For each TypeDefinition whose FullName is NOT present in
        owningTypeFullNames, resolves every const/static readonly string field's literal value
        (const via FieldDefinition.Constant; static readonly via the .cctor Ldstr->Stsfld walk,
        same as StringConstantsClassDetector) and checks it against every value in
        canonicalValues. Any match is a violation — a second, independently-declared field
        holding the exact same string value as a canonical cross-cutting constant, outside the
        type(s) that are supposed to own it.
        Aggregates every violation found across the ENTIRE supplied assembly set before
        throwing (does not stop at the first) and throws a single test-framework-agnostic
        assertion exception naming every offending declaring-type/field/value — mirroring
        LoggingEventIdIntegrityAssertion's and PipelineOrderAssertion's aggregate-failure-
        message convention.
        Caller-supplied everything: 00.Governance never references SharedKernel.Primitives
        directly (00.Governance references nothing). The consuming test project supplies
        canonicalValues (the real WellKnownHeaders/WellKnownBaggageKeys values),
        owningTypeFullNames (e.g. "SharedKernel.Primitives.Propagation.WellKnownHeaders",
        "SharedKernel.Primitives.Propagation.WellKnownBaggageKeys"), and assembliesToScan
        (every other shipped production assembly) — keeping 01.Core's WellKnownHeaders/
        WellKnownBaggageKeys (P-259) as the single source of truth for the canonical values
        while this helper itself stays dependency-free.
        Rationale: motivated by the exact incident class P-261 exemplified — a
        "CorrelationId" vs "correlation.id" mismatch between a hand-rolled literal and the
        value 01.Core's registry actually declared. A documented "always reference the
        01.Core constant" convention alone is exactly the kind of rule this domain's own
        precedent (SK0013, PresentationLayeringRules, SK0020/SK0021) has shown will drift
        without a build-time gate.
        Real-assembly status: design/implementation/tests for this phase used contrived
        in-memory multi-assembly Mono.Cecil fixtures only, consistent with every
        "designed-ahead-of-a-pending-dependency" precedent in this domain
        (SK.00.ServiceDefaultsGovernance, SK.00.MetricsOutcomeTagAndMisregistrationGuard,
        SK.00.CryptoDelegationAndUowSeamGuard, SK.00.LoggingStandardEnforcement) —
        real-assembly wiring was explicitly out of scope for this phase and was not
        attempted. CORRECTION recorded at the SK.00.MagicStringGuard closeout (2026-07-16):
        the dependency this note originally described as pending has since resolved —
        01.Core's WellKnownHeaders/WellKnownBaggageKeys (P-259) shipped 2026-07-14 at
        SharedKernel.Primitives.Propagation (NOT the "...CrossCutting" namespace this
        phase's design prose assumed — confirmed by reading the shipped file), and the
        P-260/P-261/P-262/P-263 consuming-domain retrofits are all `●` Complete per the root
        state-map.md Phase Backlog. A real-assembly re-verification pass is now unblocked
        but remains undone — tracked as a candidate follow-up phase, not implemented here.

SecureDefaultsAssertion  (public class — Mono.Cecil-based default-option-property-value
invariant helper, not ConditionList/ICustomRule; WO-060 P-390)
    .AssertEnumPropertyDefaultEquals(Type optionsType, string propertyName, string expectedEnumMemberName)
        Mirrors PipelineOrderAssertion's/LoggingEventIdIntegrityAssertion's/
        WellKnownConstantOwnershipAssertion's precedent — a plain public helper, not a
        NetArchTest ConditionList, because "a specific options type's specific property
        resolves to a specific default value" has no single-assembly "fire on a contrived
        violating assembly" shape a ConditionList naturally expresses, and no source-level
        anti-pattern a Roslyn analyzer could target — the motivating P-385/P-386 defect
        (MtlsAuthenticationOptions.AllowedCertificateTypes/.RevocationMode shipped as
        CertificateTypes.All/X509RevocationMode.NoCheck) was a correctly-shaped, syntactically
        unremarkable property initializer carrying the wrong constant.
        Introduces this domain's NEWEST Mono.Cecil technique — constructor/property-initializer
        ENUM-DEFAULT-VALUE RESOLUTION. Loads optionsType's TypeDefinition via Mono.Cecil,
        locates its parameterless instance constructor (IsConstructor && Parameters.Count == 0
        — Roslyn emits property-initializer assignments at the start of every constructor body,
        in declaration order, before any explicit constructor logic), finds the Stfld targeting
        propertyName's compiler-generated backing field (<propertyName>k__BackingField), reads
        the immediately-preceding Ldc_I4-family opcode's loaded integral constant, and resolves
        the enum member name by matching that constant against the property's enum type's own
        Fields (excluding the special value__ field) Constant values. Compares the resolved
        member name (case-sensitive) against expectedEnumMemberName.
        This is a DIFFERENT technique from StringConstantsClassDetector's field-shape+
        literal-value resolution (which resolves const/static readonly string FIELDS directly)
        — here the value is resolved from a property AUTO-INITIALIZER assigned inside a
        constructor body, and the value type is an enum's underlying integral constant, not a
        string.
        Throws a single test-framework-agnostic assertion exception naming the property, the
        actual resolved default, and the expected value on mismatch — the same
        aggregate-failure-message convention as PipelineOrderAssertion/
        LoggingEventIdIntegrityAssertion/WellKnownConstantOwnershipAssertion.
        Called twice against MtlsAuthenticationOptions: once for AllowedCertificateTypes
        (expected "Chained"), once for RevocationMode (expected "Online" since P-546, which
        matches ASP.NET Core's own CertificateAuthenticationOptions default; WO-060/P-386 had
        set "Offline") — replacing the shipped WO-058 "All"/"NoCheck" defaults that were weaker
        than plain ASP.NET Core's own default.

    .AssertStringCollectionPropertyDefaultExcludes(Type optionsType, string propertyName, IReadOnlyCollection<string> forbiddenValues, bool requireNonEmpty = true)
        Extends the established Ldstr literal-collection technique (HealthCheckTagIntegrityRules/
        MetricsInstrumentationRules) to a NEW call-site shape — a constructor-body
        array/collection-initializer feeding a property's backing-field Stfld, rather than a
        method-name-prefix-scoped call site. Collects every Ldstr operand appearing in the
        initializer sequence immediately preceding the Stfld targeting propertyName's backing
        field.
        Fails if the collected set is empty (covers BOTH a null default — which produces zero
        collected literals since no array/collection-initializer IL exists at all — AND an
        explicit empty-array default, Array.Empty<string>()/new string[0] — which produces zero
        Ldstr operands — both shapes satisfy the "empty, null" acceptance-criterion wording via
        the same one non-empty check) unless requireNonEmpty: false.
        Fails if the collected set's intersection with forbiddenValues (case-insensitive
        comparison — JWS alg values are compared case-insensitively here defensively, even
        though RFC 7518 defines "none" as lowercase-exact, since this check validates a
        HARDENED DEFAULT rather than parsing untrusted wire input) is non-empty.
        Throws the same single aggregate assertion-exception shape as
        AssertEnumPropertyDefaultEquals on failure, naming the property, the actual collected
        default set, and the forbidden values matched.
        Real-assembly use against 12.Security ENDED with P-546 (2026-09-16): the redesigned
        OidcAuthenticationOptions.ValidAlgorithms/DpopOptions.ValidAlgorithms default to an EMPTY
        collection (configuration binding appends to a non-empty default list, so a configured
        value could never replace it), with the documented defaults (RS256/PS256/ES256) applied
        in SharedKernel.Security.Oidc's internal OidcDefaults — so there is no initializer
        literal to scan. T-310 instead builds the real AddOidcAuthentication(configuration) and
        asserts the CONFIGURED JwtBearerOptions.TokenValidationParameters.ValidAlgorithms is
        non-empty and excludes {"none", "HS256", "HS384", "HS512"}, and that configuring any of
        those values fails startup validation (OptionsValidationException). The method itself,
        and its contrived fixtures T-305–T-308, are unchanged.

    Caller-supplied everything: 00.Governance never references SharedKernel.Security.Mtls/
    .Oidc directly (00.Governance references nothing). The consuming test project supplies
    optionsType via typeof(MtlsAuthenticationOptions) (test-only ProjectReference,
    PrivateAssets="all"; the Oidc algorithm lock is a configured-options test since P-546), and
    expectedEnumMemberName/forbiddenValues from the real, shipped hardened-default values —
    matching the same "caller supplies the assembly/values, never hard-code them here"
    discipline established by every prior non-ConditionList helper in this file.
    Real-assembly status: CORRECTED AT IMPLEMENTATION TIME (2026-08-18,
    SK.00.SecureDefaultsLock closeout) — this phase's own authoring-time prose above (and its
    matching state-map.md Dependencies/Cross-Domain Dependencies sections) claimed 12.Security's
    P-386/P-387 were still `○ QUEUED`/not dispatched. Verified false at implementation time:
    12.Security had already shipped its full WO-060 scope (12.Security/state-map.md's C-39/C-40/
    C-41 all `●` Complete, SharedKernel.Security.Mtls packed 2.0.0, SharedKernel.Security.Oidc
    packed 4.0.0) before this phase's implementation session began — mirroring
    SK.00.SenderConstrainedCredentialGuard's own immediately-preceding closeout correction in the
    same WO-060 run exactly. Real-assembly re-verification is therefore IMPLEMENTED (not
    deferred) as GATING tests in SecureDefaultsAssertionTests: AssertEnumPropertyDefaultEquals is
    re-pointed at the real MtlsAuthenticationOptions for both AllowedCertificateTypes (expected
    "Chained") and RevocationMode (expected "Online" since P-546; "Offline" at the time of this
    closeout); AssertStringCollectionPropertyDefaultExcludes was re-pointed at the real
    SecurityOptions.JwtOptions.ValidAlgorithms and DpopOptions.ValidAlgorithms at the time —
    SUPERSEDED by P-546: SecurityOptions no longer exists, and T-310 now asserts the configured
    JwtBearerOptions built by AddOidcAuthentication (see the method note above).
    Verified non-vacuous: a temporary sanity test (removed before commit) pointed each assertion
    at a deliberately wrong expectation against these same real types and confirmed every one
    failed — the real properties are genuinely read, not silently skipped.

    Two new methods added by WO-061/P-401 (SK.00.TenantAndMtlsBoundaryLock), applying the identical
    "a hardened default/security decision documented only in prose eventually drifts" lesson to
    13.ServiceDefaults for the first time (previously applied twice to 12.Security: P-373, P-390):

    .AssertStringCollectionPropertyDefaultEquals(Type optionsType, string propertyName, IReadOnlyList<string> expectedValuesInOrder)
        Reuses the exact Ldstr literal-collection sub-technique already built for
        AssertStringCollectionPropertyDefaultExcludes (collect every Ldstr operand in the
        constructor-body array/collection-initializer feeding propertyName's backing-field Stfld,
        in IL emission order) but performs an ORDER-SENSITIVE sequence-equality comparison against
        expectedValuesInOrder instead of a forbidden-intersection check. Necessary because
        TenantResolutionOptions.StrategyOrder's security property depends on relative order under
        first-non-null-result-wins semantics, not merely membership — [Header, Claim, Database] and
        [Claim, Header, Database] contain identical elements but only the latter is secure (WO-061
        P-393's corrected default). Fails if the collected sequence's length differs from
        expectedValuesInOrder's length, or if any positional element differs (ordinal string
        comparison — StrategyName values are platform-defined constants, not user input, so no
        case-insensitive leniency here unlike AssertStringCollectionPropertyDefaultExcludes's JWS-
        algorithm comparison).
        Called against TenantResolutionOptions.StrategyOrder, expectedValuesInOrder =
        [TenantResolutionStrategyNames.Claim, .Header, .Database] — the WO-061/P-393 corrected
        default, replacing the shipped WO-027 [Header, Claim, Database] order that let an unsigned,
        caller-supplied X-Tenant-Id header outrank a cryptographically-verified JWT tenant claim for
        the same request.

    .AssertMethodBodyInvokesMethod(Type declaringType, string methodName, Type calleeDeclaringType, string calleeMethodName)
        Introduces this domain's NEWEST Mono.Cecil technique — METHOD-BODY INVOCATION-PRESENCE
        ASSERTION, the inverse of every other technique in this file (which all detect and FAIL ON
        an unwanted call site; this one FAILS ON A MISSING expected call site). Loads declaringType's
        TypeDefinition via Mono.Cecil, locates the single method matching methodName (throws a
        distinct setup exception, never a silent false pass/fail, if zero or more than one method
        matches), and scans its instruction body for a Call/Callvirt instruction whose resolved
        MethodReference.Name == calleeMethodName and DeclaringType.FullName ==
        calleeDeclaringType.FullName. Fails if no matching instruction is found. This is the correct
        technique for locking a [LoggerMessage]-source-generated call site in place — the source
        generator emits a real static/instance method body invoked via a plain Call instruction,
        structurally identical to any hand-written method call, so no special-casing is needed for
        its source-generated origin.
        Called against the real, shipped SharedKernel.ServiceDefaults.Security.
        MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate registration method
        (WO-061 P-394/P-395), asserting it still calls
        MtlsLog.ForwardedHeaderTrustBoundaryUnconfigured (ServiceDefaultsLog before WO-084) when TrustedNetworks is left
        empty at registration time — closing the exact "warning silently deleted in a future edit"
        regression this check exists to prevent.
        CONFIRMED BY DIRECT MONO.CECIL INSPECTION AT IMPLEMENTATION TIME that a plain
        single-method-body scan is INSUFFICIENT for this real call site: the warning call lives
        inside the C# `.PostConfigure<ILoggerFactory>((configuredOptions, loggerFactory) => {...})`
        lambda argument, which Roslyn lowers to its own method
        (`<AddMtlsForwardedHeaderCertificate>b__0_0`) on a compiler-generated `<>c` closure type
        nested inside MtlsForwardedHeaderExtensions — the enclosing method's own IL contains only a
        delegate-construction sequence (ldftn/newobj against a cached static field), never the
        callee call itself. AssertMethodBodyInvokesMethod was therefore extended beyond the design
        prose's original single-body scan: when the direct method body contains no matching call,
        it additionally scans every method on every nested type of declaringType whose name starts
        with "<{methodName}>b__" (the Roslyn-emitted naming convention for a lambda declared inside
        methodName, whether hosted on the shared <>c cache type or a per-call
        <>c__DisplayClassN_M closure type). This is a necessary technique extension, not scope
        creep — without it, this check could never pass against the one real call site WO-061
        motivated it to protect.

    Both methods throw the same single aggregate assertion-exception shape as
    AssertEnumPropertyDefaultEquals/AssertStringCollectionPropertyDefaultExcludes — naming the
    property/method, the actual resolved state, and the expected state.
    Caller-supplied everything, same discipline as the rest of this class: the consuming test
    project supplies optionsType/declaringType via typeof(TenantResolutionOptions)/
    typeof(MtlsForwardedHeaderExtensions) (test-only ProjectReference, PrivateAssets="all", to
    SharedKernel.MultiTenancy/SharedKernel.ServiceDefaults), and
    expectedValuesInOrder/calleeDeclaringType/calleeMethodName from the real, shipped corrected
    values. calleeDeclaringType (MtlsLog since WO-084, previously ServiceDefaultsLog) is resolved via
    declaringType.Assembly.GetType("SharedKernel.ServiceDefaults.Logging.MtlsLog")
    rather than typeof(...) in the consuming test, since MtlsLog is internal to
    SharedKernel.ServiceDefaults.Security.Mtls and no InternalsVisibleTo grant exists (or should exist) to this
    governance test project — Assembly.GetType(string) resolves a Type object by name regardless
    of accessibility, and this helper only ever compares FullName, never invokes a member through
    it.
    Real-assembly status: CORRECTED AT IMPLEMENTATION TIME (2026-08-19, SK.00.TenantAndMtlsBoundaryLock
    closeout) — this phase's own authoring-time prose above (and its matching state-map.md
    Dependencies/Cross-Domain Dependencies sections) claimed 13.ServiceDefaults's P-393/P-394/P-395
    were still Design-locked only (D-20–D-27), Scaffold/Core/Tests/Docs Not Started. Verified false
    at implementation time: 13.ServiceDefaults had already shipped its full WO-061 scope
    (171/171 + 51/51 tests green) before this phase's implementation session began — mirroring
    SK.00.SenderConstrainedCredentialGuard's/SK.00.SecureDefaultsLock's own precedent exactly for
    this now-repeated class of dependency-resolved-before-implementation finding. Real-assembly
    re-verification is therefore IMPLEMENTED (not deferred) as GATING tests in
    SecureDefaultsAssertionTests: AssertStringCollectionPropertyDefaultEquals is re-pointed at the
    real TenantResolutionOptions.StrategyOrder (expected [Claim, Header, Database]);
    AssertMethodBodyInvokesMethod is re-pointed at the real AddMtlsForwardedHeaderCertificate,
    asserting it calls MtlsLog.ForwardedHeaderTrustBoundaryUnconfigured (ServiceDefaultsLog before WO-084). Verified
    non-vacuous: a temporary sanity test (removed before commit) pointed each assertion at a
    deliberately wrong expectation (the pre-P-393 [Header, Claim, Database] order; a nonexistent
    callee method name) against these same real types and confirmed both failed.

    Sixth method added by WO-062/P-410 (SK.00.CorsWildcardCredentialsGuard), extending the
    "a hardened default/security decision documented only in prose eventually drifts" lesson to
    a NEW shape — proving a startup GUARD genuinely throws, not merely that a default VALUE holds
    or that an expected CALL SITE is present:

    .AssertMethodBodyThrowsExceptionType(Type declaringType, string methodName, Type expectedExceptionType)
        Introduces this domain's NEWEST Mono.Cecil technique — METHOD-BODY THROW-PRESENCE
        ASSERTION, a sibling to AssertMethodBodyInvokesMethod's invocation-presence-assertion
        technique (both fail on the ABSENCE of an expected element, not the presence of an
        unwanted one) but targets a Newobj instruction constructing expectedExceptionType
        followed anywhere later in the same method body's IL by a Throw opcode, instead of a
        Call/Callvirt. Reuses AssertMethodBodyInvokesMethod's closure-method-scanning extension
        (proven necessary by T-318): when the direct method body contains no matching
        Newobj-then-Throw sequence, it additionally scans every method on every nested type of
        declaringType whose name starts with "<{methodName}>b__" — a startup validation guard is
        just as plausibly registered via a PostConfigure/Validate-style lambda as
        AddMtlsForwardedHeaderCertificate's own shape was.
        DOCUMENTED LIMITATION (intentional, consistent with every presence-based technique in
        this file): a whole-method-body-plus-closures presence check, not a reachability/
        control-flow check tied to the specific dangerous-configuration branch — it cannot
        distinguish "throws only when the wildcard+credentials combination is detected" from
        "throws unconditionally for every configuration" or "throws for an unrelated reason
        elsewhere in the same method." The acceptance bar this method proves is "the guard
        exists and constructs+throws the expected exception type," not "the guard is provably
        correct for every input" — the latter remains 14.Presentation's own unit-test
        responsibility inside its own domain, not this one's.
        Throws the same single aggregate assertion-exception shape as the class's other five
        methods — naming the method, whether a matching Newobj-then-Throw sequence was found,
        and the expected exception type.
        Caller-supplied everything, same discipline as the rest of this class: the consuming
        test project supplies declaringType via the real AddSharedKernelCors-owning type
        (test-only ProjectReference, PrivateAssets="all", to SharedKernel.Presentation.WebApi)
        and expectedExceptionType from 14.Presentation's real, shipped guard-exception type — the
        implementer must confirm both the real declaring type/method name and the real exception
        type against 14.Presentation's shipped source before writing the real-assembly test,
        never assume either from this document (mirrors SK.00.TenantAndMtlsBoundaryLock's Rule 8
        discipline exactly).
        Real-assembly status: CORRECTED AT IMPLEMENTATION TIME (2026-08-20,
        SK.00.CorsWildcardCredentialsGuard closeout) — this phase's own authoring-time prose above
        claimed 14.Presentation's P-404 (AddSharedKernelCors) was still `◐` Dispatched (Design
        only). Verified false at implementation time: 14.Presentation had already shipped its full
        P-404 scope (SharedKernel.Presentation.WebApi packed 1.2.0) before this phase's
        implementation session began — mirroring the SK.00.SenderConstrainedCredentialGuard/
        SK.00.SecureDefaultsLock/SK.00.TenantAndMtlsBoundaryLock precedent for this exact class of
        stale-dependency finding. UNLIKE those three precedents, resolving the dependency did NOT
        simply unblock the originally-planned real-assembly test — reading the real, shipped
        Cors/CorsExtensions.cs/CorsPolicyOptionsValidator.cs source directly (per this phase's own
        Rule 10 "never assume a name, confirm against shipped source" instruction) found a genuine
        DESIGN/REALITY MISMATCH: AddSharedKernelCors does not construct-and-throw a named exception
        from its own method body at all. It registers CorsPolicyOptionsValidator (an
        IValidateOptions<CorsPolicyOptions> whose Validate method returns
        ValidateOptionsResult.Fail(...) on the dangerous combination) via
        services.AddOptions<CorsPolicyOptions>().Configure(configure).ValidateOnStart() — the
        actual throw new OptionsValidationException(...) this produces happens entirely inside
        Microsoft.Extensions.Options's own OptionsFactory<TOptions> machinery at
        IHost.StartAsync(), FRAMEWORK code that never appears in SharedKernel.Presentation.WebApi's
        own IL. AssertMethodBodyThrowsExceptionType was therefore STRUCTURALLY UNABLE to ever pass
        against this real assembly, regardless of how correctly the guard itself behaves — not a
        defect in either domain's work, a shape this phase's design prose simply predated (it
        anticipated an inline-throw guard shape; 14.Presentation chose the idiomatic
        IValidateOptions+ValidateOnStart route instead). Resolved by re-pointing the
        ALREADY-SHIPPED AssertMethodBodyInvokesMethod (P-401) at the real, internal
        CorsPolicyOptionsValidator.Validate (resolved via Assembly.GetType(string), mirroring
        T-318's identical technique for an inaccessible internal type) instead, asserting it
        genuinely calls Microsoft.Extensions.Options.ValidateOptionsResult.Fail — a technique
        matching the guard's ACTUAL shape, reusing proven infrastructure rather than adding a
        narrowly-motivated seventh method to this class for one call site. Wired directly in
        SecureDefaultsAssertionTests as a GATING test (T-328) rather than deferred, verified
        non-vacuous via a temporary sanity-check test (a deliberately-wrong callee method name,
        confirmed to fail, then reverted before commit). AssertMethodBodyThrowsExceptionType itself
        remains proven only via T-326/T-327's contrived fixtures — no real call site for it exists
        on this platform as of this phase — and remains available as a generically useful technique
        for a future guard that genuinely throws directly from its own method body.
        P-562 RE-POINT (2026-09-23): 14.Presentation's redesign deleted AddSharedKernelCors,
        CorsPolicyOptions, CorsPolicyNames and CorsPolicyOptionsValidator; CORS became
        SharedKernelWebApiOptions.Cors (WebApiOptions until R22 renamed it; the validator kept its
        name), validated by the internal WebApiOptionsValidator that
        AddSharedKernelWebApi registers through SharedKernel.Configuration's AddValidatedOptions (bind +
        ValidateOnStart). Same guard, same validator-based shape, so T-328 follows it with the same
        technique as a three-call-site chain: AddSharedKernelWebApi → OptionsExtensions.AddValidatedOptions;
        WebApiOptionsValidator.Validate → ValidateCors (same-type sibling); Validate →
        ValidateOptionsResult.Fail. The dangerous-combination branch inside ValidateCors is proven
        behaviorally by 14.Presentation's CorsTests and consumer-verify (Surface 2). The nested
        Log.CorsConfigurationInvalid call could not be locked: AssertMethodBodyInvokesMethod compares
        a nested callee's reflection FullName ('+') with Mono.Cecil's ('/'), so nested callee types
        never match — a known limitation of that method, not worked around here.

    Seventh real-world application, added by WO-063/P-420 (SK.00.CorrelationIdValidationGuard) —
    introduces NO new method on this class, CONFIRMED at implementation time (2026-08-21):
    `14.Presentation`'s CorrelationIdMiddleware gained a format-validation check
    (CorrelationIdOptions.MaxLength/AllowedCharacterPattern) rejecting/regenerating a
    caller-supplied correlation-id value before it reaches HttpContext.Items/Activity.SetBaggage/
    the response header (P-415). This phase's own authoring-time prose recorded 14.Presentation's
    P-415 as `○` Design-only (D-57/S-25/C-64/C-65 all `○` Not started) — STALE by implementation
    time: that domain had already shipped its full WO-063 scope end to end
    (SharedKernel.Presentation.WebApi re-packed to 1.3.0) before this phase's implementation
    session began, mirroring the SK.00.SenderConstrainedCredentialGuard/SK.00.SecureDefaultsLock/
    SK.00.TenantAndMtlsBoundaryLock/SK.00.CorsWildcardCredentialsGuard precedent for this exact
    class of stale-dependency finding.

    UNLIKE SK.00.CorsWildcardCredentialsGuard's design/reality mismatch, the real shipped shape
    matched this phase's design exactly: CorrelationIdMiddleware.ResolveCorrelationId is a
    conditional check-then-substitute — it calls its own PRIVATE instance method IsValidFormat
    (declared on the SAME type, not a different one) and falls back to regenerating a fresh value
    when the check fails, never throwing. The EXISTING AssertMethodBodyInvokesMethod was therefore
    the correct technique with no re-pointing surprise and AssertMethodBodyThrowsExceptionType's
    documented fallback was never needed — this is the FIRST phase in the "lock a not-yet-shipped
    hardened guard" family to genuinely add zero new production code to this class. The call site
    lives directly in ResolveCorrelationId's own IL body, not inside a lambda closure, so the
    closure-scanning extension is not exercised by this particular real call site (it remains
    proven by T-318's/T-328's own real call sites).

    Real-assembly status: IMPLEMENTED (not deferred) — wired directly in SecureDefaultsAssertionTests
    as a GATING test (T-331) alongside contrived fire/pass-path fixture tests (T-329/T-330), rather
    than tracked as a Cross-Domain Dependency follow-up. Verified non-vacuous via a temporary
    sanity-check test (a deliberately-wrong callee method name, "IsValidFormatXyzSanityCheck",
    confirmed to fail with the same message shape T-330's contrived fixture produces, then reverted
    before commit).
    P-562 RE-POINT (2026-09-23): the middleware became internal
    (SharedKernel.Presentation.WebApi.Correlation.CorrelationIdMiddleware, applied by
    UseSharedKernelWebApi(); the …WebApi.Middleware namespace is gone) and the pair was renamed
    Resolve/IsValid; an invalid or missing value is now replaced by the trace id. T-331 resolves the
    type with Assembly.GetType(string) (T-318's technique) and asserts Resolve → IsValid.

    Eighth real-world application, added by WO-064/P-432 (SK.00.WebhookSsrfGuardLock) — applies the
    "a hardened default documented only in prose eventually drifts" lesson to 15.Integration's new
    outbound-webhook SSRF guard (IWebhookUrlValidator/PrivateNetworkWebhookUrlValidator, P-422),
    the platform's first webhook/outbound-delivery-domain application of this family. UNLIKE every
    prior application, this one locks TWO independent facts with two DIFFERENT techniques rather
    than one — a default DI registration is a structural fact this class already has the right
    shape for; fail-closed IP-range behavior is a computed fact it structurally cannot honestly
    prove, so a second, non-IL technique was introduced specifically for it rather than stretched
    to fit:

    .AssertMethodBodyRegistersSingleton(Type declaringType, string methodName, Type serviceType, Type implementationType)
        Introduces this domain's NEWEST Mono.Cecil technique for THIS class — a
        DI-REGISTRATION-PRESENCE ASSERTION. Generalizes/INVERTS
        SecurityArchitectureRules.NoSecurityContextSingletonRegistrationPredicate's (WO-057 P-373)
        generic-instance-method-argument inspection technique (GenericInstanceMethod.GenericArguments)
        from "assert ABSENCE of a singleton registration for a forbidden type" to "assert PRESENCE
        of a singleton registration for exactly the given service→implementation pair." Loads
        declaringType's TypeDefinition, locates the single method matching methodName (throws a
        distinct setup exception, never a silent false pass/fail, on zero or more than one match),
        and scans its instruction body — plus, reusing AssertMethodBodyInvokesMethod's proven
        closure-scanning extension (T-318/T-328), every method on every nested type whose name
        starts with "<{methodName}>b__" — for a Call/Callvirt instruction whose resolved
        MethodReference.Name == "AddSingleton" and whose GenericInstanceMethod.GenericArguments
        equal [serviceType, implementationType] in that order. Fails if no matching instruction is
        found.
        Called (once shipped) against the real DI extension class hosting AddSharedKernelWebhooks
        inside SharedKernel.Integration.Webhooks, asserting it registers
        IWebhookUrlValidator → PrivateNetworkWebhookUrlValidator — proving the default SSRF guard's
        registration cannot be silently dropped by a future refactor with every 15.Integration-owned
        unit test still green (those tests exercise the guard via a spy/fake IWebhookUrlValidator,
        per 15.Integration/CLAUDE.md's own H-08 design, so they cannot themselves prove the REAL
        default implementation stays wired in).
        Caller-supplied everything, same discipline as the rest of this class: the consuming test
        project supplies declaringType/serviceType/implementationType via typeof(...) (test-only
        ProjectReference, PrivateAssets="all", to SharedKernel.Integration.Webhooks).
        Both AddSingleton<TService,TImplementation>() and TryAddSingleton<TService,TImplementation>()
        are accepted method-name shapes — this phase's own authoring-time design prose assumed
        plain AddSingleton (reasoning from WithUrlValidator<T>()'s documented "last call wins"
        override semantics), but direct inspection of the real, shipped AddSharedKernelWebhooks
        found it uses TryAddSingleton instead (a deliberate, correct choice by 15.Integration —
        TryAddSingleton is what lets WithUrlValidator<T>() be called either before or after
        AddSharedKernelWebhooks() and still win, since that override method itself calls
        RemoveAll<IWebhookUrlValidator>() immediately followed by a plain AddSingleton). Matching
        only one of the two names would either miss the one real caller this phase exists to lock,
        or reject a legitimate future AddSingleton-based default elsewhere on the platform — so both
        are accepted, confirmed against real source before this method was written, not assumed.
        Real-assembly status: IMPLEMENTED (not deferred) — the Cross-Domain Dependency this phase's
        own authoring-time prose recorded against 15.Integration's P-422 (H-06/H-07) as `○` Not
        started was STALE by implementation time: that domain had already shipped
        IWebhookUrlValidator/PrivateNetworkWebhookUrlValidator and their default TryAddSingleton
        registration past Design into Core before this phase's implementation session began,
        mirroring this file's own now-repeated dependency-resolved-before-implementation pattern.
        Wired directly in SecureDefaultsAssertionTests as a GATING test (T-335), verified
        non-vacuous via a temporary sanity check (a deliberately-wrong implementation type against
        the same real registration method, confirmed to fail, then reverted before commit). The real
        call site (services.TryAddSingleton<IWebhookUrlValidator, PrivateNetworkWebhookUrlValidator>())
        lives directly in AddSharedKernelWebhooks's own IL body, not inside a lambda closure, so no
        closure-scanning extension is exercised by this particular call site (that extension remains
        proven by T-318/T-328/T-331's own real call sites).

    Technique B (the fail-closed IP-range-behavior half of this phase's acceptance criterion) is
    DELIBERATELY NOT a method on this class. Every method above proves a STRUCTURAL fact (a
    constant's value, a call site's presence, a throw's presence, a registration's presence);
    "rejects the documented private/loopback/link-local/metadata IP ranges" is a COMPUTED BEHAVIOR
    of arbitrary range-membership logic (the real, shipped PrivateNetworkWebhookUrlValidator uses
    hardcoded byte-range comparisons per octet, not CIDR-string parsing) — no sound,
    representation-agnostic static IL technique exists for this half of the check. Instead, this
    phase specifies the FIRST genuinely EXECUTED real-assembly test in this file — a deliberate,
    explicitly-flagged departure from this class's "IL-only, never execute the assembly under test"
    discipline — living directly in SecureDefaultsAssertionTests.cs as an xUnit [Theory] rather than
    as a reusable SecureDefaultsAssertion helper method, since it is a one-off assertion tied to one
    real type rather than a general-purpose technique a future lock could reuse. It resolves the
    real, compiled PrivateNetworkWebhookUrlValidator directly (constructed via
    Options.Create(new WebhookDeliveryOptions()) — no DI container needed, its one constructor
    dependency is IOptions<WebhookDeliveryOptions>) and invokes its public IWebhookUrlValidator
    contract method against a fixed table of representative targets: 127.0.0.1 (loopback), 10.0.0.1
    (RFC 1918 private), 169.254.169.254 (link-local/cloud-metadata — the AWS/Azure/GCP
    instance-metadata endpoint and SSRF's single most common real-world target), and 8.8.8.8
    (public-internet control address), asserting reject/reject/reject/accept. Uses IP-literal hosts
    exclusively so the test performs no actual network round-trip, remaining fully deterministic
    and offline — Dns.GetHostAddressesAsync against an IP-literal host resolves purely locally per
    BCL contract, mirroring 15.Integration's own H-08 test-design constraint ("never a real DNS
    lookup or network call") applied here to a governance test instead of a domain test.
    Real-assembly status: IMPLEMENTED (not deferred) — same resolved Cross-Domain Dependency as
    AssertMethodBodyRegistersSingleton above. Wired directly in SecureDefaultsAssertionTests as a
    GATING [Theory] (T-336), verified non-vacuous via a temporary sanity check (inverting every
    expected accept/reject outcome, confirmed all four cases fail, then reverted before commit).

    Ninth real-world application, added by WO-065/P-437 (SK.00.CacheEncryptionAndRedisValidationLock)
    — the first application of this "a security- or correctness-relevant default documented only in
    prose eventually drifts" lesson to `02.Caching`, and the first to broaden the family's scope
    beyond a pure security vulnerability to a correctness/efficiency regression (reversing a
    compress-then-encrypt composition order silently defeats compression's size benefit — the
    identical ordering contract `07.Messaging`'s own opt-in payload transform already documents,
    P-346). Locks TWO independent facts from `02.Caching`'s planned-but-not-yet-implemented
    `AddCacheEncryption()` (Phase 42/P-433) and `AddRedisConnection` (Phase 45/P-436) — designed
    against `02.Caching/state-map.md`'s own planned phase descriptions per the dispatcher's explicit
    note, not against shipped source, since neither phase existed past planning at authoring time:

    Technique A (composition-ordering behavior) is DELIBERATELY NOT a new `SecureDefaultsAssertion`
    method, for the same reason `SK.00.WebhookSsrfGuardLock`'s Technique B was not — "compression
    runs before encryption" is a COMPUTED BEHAVIOR of two composed decorators/serializer stages, not
    a structural fact (a constant's value, a call site's presence) any IL technique in this file can
    honestly prove without assuming a specific, not-yet-fixed decorator class shape. Instead this is
    the SECOND genuinely EXECUTED real-assembly test in this file (after `SK.00.WebhookSsrfGuardLock`'s
    Technique B), living directly in a test file rather than as a reusable `SecureDefaultsAssertion`
    method. It builds the real `AddCacheEncryption()`-composed pipeline via a minimal `IServiceCollection`
    with both compression and encryption enabled, serializes a HIGHLY COMPRESSIBLE payload (a long
    repeated-character string) through it, and captures the final byte length that would be persisted.
    It then encrypts the SAME payload directly via `01.Core`'s existing `ISymmetricEncryptionService`
    (no compression) as a baseline and asserts the pipeline's output is meaningfully smaller than that
    baseline (below a fixed ratio threshold) — proving compression measurably ran before/around
    encryption. This is a BLACK-BOX, representation-agnostic technique: it makes no assumption about
    the pipeline's internal decorator class names, only its public `AddCacheEncryption()` entry point
    and resulting serialize/deserialize round trip. It directly measures the phase input's own stated
    harm ("reversing this order silently defeats compression's size benefit") rather than trying to
    reverse-engineer the exact byte layout — if the real order were reversed (encrypt-then-attempt-
    compress), compressing high-entropy ciphertext yields near-zero size reduction (a well-known
    property of compression algorithms against high-entropy input), so the assertion correctly fails
    and catches the regression. A round-trip-correctness precondition (decrypt+decompress via the same
    pipeline's read path recovers the original payload exactly) guards against the size assertion
    accidentally passing against corrupted or no-op output.
    DOCUMENTED LIMITATION (same class as every technique in this file): a size-ratio threshold is a
    reliable, low-false-positive signal for any straightforward correct-vs-reversed implementation,
    not a byte-exact structural proof — consistent with `SK.00.WebhookSsrfGuardLock`'s own "computed
    behavior, not a structural fact" precedent for departing from IL-only discipline.

    Technique B (redis options-validation eagerness) reuses the EXISTING `AssertMethodBodyInvokesMethod`
    unchanged — zero new production code, mirroring `SK.00.CorrelationIdValidationGuard`'s precedent —
    re-pointed at `SharedKernel.Caching.Redis.Core.Extensions.RedisConnectionCoreExtensions.AddRedisConnection`,
    asserting it calls `Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions.ValidateOnStart`.
    This is the correct target for "genuinely executes at startup rather than merely decorating the
    options type with inert attributes": `ValidateOnStart()` is the marker that causes
    `Microsoft.Extensions.Options`'s own host-startup validation machinery to run eagerly at
    `IHost.StartAsync()` — a `[Required]`/`DataAnnotations` attribute on `RedisConnectionOptions` alone
    is inert until something triggers validation, and a registered `IValidateOptions<RedisConnectionOptions>`
    without `.ValidateOnStart()` only validates lazily on first options access, not at startup. If the
    real registration instead wires a custom validator without calling `.ValidateOnStart()`, this check
    correctly reports the gap rather than passing — mirroring `SK.00.CorsWildcardCredentialsGuard`'s
    validator-vs-throw design/reality-mismatch lesson applied pre-emptively here rather than
    retroactively.
    No new SK diagnostic ID is consumed here.
    <para>
    <strong>Real-assembly status (Technique A/B — `SK.00.CacheEncryptionAndRedisValidationLock`/WO-065/
    P-437).</strong> CORRECTED AT IMPLEMENTATION TIME (2026-08-24) — this phase's own authoring-time
    prose above (and its matching `state-map.md` Dependencies/Cross-Domain Dependencies sections) claimed
    `02.Caching`'s P-433/P-436 were still `○` planned only, not yet dispatched. Verified false at
    implementation time: `02.Caching` had already shipped its full Phase 42
    (`CacheEncryptionSerializer`/`CacheEncryptionCachingBuilderExtensions`/`BrotliCompressionExtensions`,
    `SharedKernel.Caching.FusionCache`) and Phase 45
    (`RedisConnectionCoreExtensions.AddRedisConnection` wired to
    `.ValidateDataAnnotations().ValidateOnStart()`, `SharedKernel.Caching.Redis.Core`) scope before this
    phase's implementation session began — mirroring this file's own now-nine-times-repeated
    dependency-resolved-before-implementation pattern. **Rule 6's `calleeDeclaringType` was also
    corrected**: it originally named `Microsoft.Extensions.Options.OptionsBuilderExtensions` — the real
    `ValidateOnStart<TOptions>` extension resolves under the `Microsoft.Extensions.DependencyInjection`
    namespace instead (same NuGet package, different namespace), confirmed by direct inspection before
    the test was written. Real-assembly re-verification is therefore IMPLEMENTED (not deferred) as
    GATING tests in `SecureDefaultsAssertionTests`: Technique A
    (`CacheEncryptionPipeline_RealAddCacheEncryption_CompressesBeforeEncrypting`, T-337) builds the real
    pipeline against a fresh `IEncryptionKeyProvider` fixture and an 8192-character highly-compressible
    payload, confirming a 95-byte stored output against an 8249-byte (16498/2) encrypt-only baseline,
    plus a round-trip-correctness precondition; Technique B
    (`AssertMethodBodyInvokesMethod_RealAddRedisConnection_ValidateOnStartCallSiteHolds`, T-340)
    re-points the corrected callee at the real `AddRedisConnection`. **Re-locked for P-547:**
    `AddRedisConnection` now has two overloads, so T-340 selects the
    `(IServiceCollection, Action<RedisConnectionOptions>)` overload by parameter types (it calls
    `ValidateOnStart` directly), and the companion
    `AssertMethodBodyInvokesMethod_RealAddRedisConnectionConfigurationOverload_ValidatesOnStartThroughAddValidatedOptions`
    locks the `(IServiceCollection, IConfiguration, Action<RedisConnectionOptions>?)` overload in two links —
    it calls `OptionsExtensions.AddValidatedOptions`, and `OptionsExtensions.BindAndValidateOnStart` (where
    every `AddValidatedOptions` overload ends) calls `ValidateOnStart`. Verified non-vacuous: a temporary
    sanity test (removed before commit) inverted Technique A's size expectation (confirmed to fail — 95
    actual vs. a required >16498) and pointed Technique B at a deliberately-wrong callee method name
    (confirmed to fail) against these same real types. 241/241 `SharedKernel.ArchitectureTests.Tests`
    pass (237 baseline + 4).
    </para>
    <para>
    <strong>T-337 RE-LOCKED 2026-09-08</strong> against `02.Caching`'s `SK.02.CacheEncryptionAadBinding`
    phase (WO-081), which shipped the same day and deleted `CacheEncryptionSerializer` entirely.
    `AddCacheEncryption()` no longer decorates `IFusionCacheSerializer` (which never receives the
    cache key, so it is structurally incapable of deriving key-bound associated data) — it now wraps
    `ICacheService` with a new `Encryption.EncryptedCacheService` instead. T-337's original design,
    still resolving `IFusionCacheSerializer` and asserting a size ratio on ITS output, had gone
    VACUOUS: after the `02.Caching` change that serializer performs compression only (encryption moved
    one layer up), so the assertion kept passing for the wrong reason — a compressed-only payload for
    repeated-character input is still trivially smaller than an encrypt-only baseline. Found during a
    routine full-solution build (a same-day `01.Core` P-491 change had also broken the test's compile,
    surfacing the deeper defect), not by this file's own suite, which stayed green throughout — the
    lock was passing while verifying nothing, precisely the failure mode it exists to prevent.
    T-337 is now re-pointed at the real, current architecture, same test name, same file: it registers
    its own minimal in-memory `ICacheService` double (`SpyInnerCacheService`) BEFORE calling
    `AddSharedKernelCaching()` — that method registers its default via `TryAddSingleton`, a no-op once
    a registration already exists, so `AddCacheEncryption()`'s "wrap whatever `ICacheService` is
    currently registered" logic ends up wrapping the double directly, a clean interception point for
    exactly the `EncryptedPayload` `EncryptedCacheService` hands to its inner store. The same
    compress-then-encrypt size-ratio invariant is asserted at the correct layer — `ICacheService.
    SetAsync`/`GetAsync`, not `IFusionCacheSerializer.Serialize`/`Deserialize`. `FixtureEncryptionKeyProvider`
    was also corrected to genuinely implement `ISynchronousEncryptionKeyProvider` (P-492/WO-081) — an
    honest claim, it performs no I/O — though T-337 itself now uses `EncryptAsync` exclusively for its
    baseline, matching how the real `EncryptedCacheService` always calls the async crypto surface.
    Verified non-vacuous via a temporary sanity mutation (removing `.AddBrotliCompression()` from the
    chain), confirmed to fail, then reverted before commit. Not tracked under any new task ID — a
    defect repair to already-`●` test code, triggered entirely by sibling domains' same-day WO-081
    shipments (`01.Core` P-491/P-492, `02.Caching` `SK.02.CacheEncryptionAadBinding`), no
    `00.Governance` production/analyzer behavior changed. 259/259 `SharedKernel.ArchitectureTests.Tests`
    pass.
    </para>

    Tenth real-world application, added by WO-081/P-504 (SK.00.SyncCryptoGateAndArgon2ConfinementLock)
    — applies the "a security- or correctness-relevant default/guard documented only in prose
    eventually drifts" lesson to `01.Core`'s new synchronous-provider capability gate
    (P-492's `ISynchronousEncryptionKeyProvider`/`EncryptionKeyProviderCapabilities`, gating
    `AesGcmEncryptionService`'s retained sync `Encrypt`/`Decrypt`/`EncryptToString`/`DecryptToString`;
    P-493's mirrored `ISynchronousAsymmetricKeyProvider`/`AsymmetricKeyProviderCapabilities`, gating
    `RsaSignatureService`/`EcdsaSignatureService`'s retained sync `Sign`/`Verify`) — the platform's
    defense against a KMS-backed key provider silently thread-pool-starving a sync caller via an
    unconditional `.GetAwaiter().GetResult()` bridge. Reuses BOTH `AssertMethodBodyInvokesMethod`
    (constructor invokes the capability check; gated member invokes the guard helper) AND
    `AssertMethodBodyThrowsExceptionType` (the guard helper constructs-and-throws
    `NotSupportedException`) UNCHANGED — zero new method on this class, the THIRD phase in this
    family to add zero new production code (after `SK.00.CorrelationIdValidationGuard` and
    `SK.00.CacheEncryptionAndRedisValidationLock`'s Technique B).
    DELIBERATELY SCOPED to the gate's OWN implementation (`AesGcmEncryptionService`'s/
    `RsaSignatureService`'s/`EcdsaSignatureService`'s own constructor and gated-member IL), never a
    repository-wide "no production code calls a sync `ISymmetricEncryptionService`/
    `IAsymmetricSignatureService` member" scan. This distinction matters concretely:
    `07.Messaging`'s payload-transform serializer decorators (P-346) were proven by direct
    reflection against the installed MassTransit assembly to be hard-synchronous with no async
    overload anywhere — that call site is a legitimate, structurally-unavoidable consumer of the
    sync members, not a violation. A call-site-presence scan would flag it; this technique, which
    only inspects the three gate-implementing types' own method bodies, structurally cannot.
    IMPLEMENTATION-TIME FINDING #1 — RESOLVED EARLIER THAN AUTHORED, NOT DEFERRED: this phase's own
    authoring-time text honestly recorded Technique A as FULLY UNVERIFIABLE — `01.Core`'s P-492/P-493
    were Design-locked only at authoring time. CONFIRMED RESOLVED before this phase's implementation
    session began: `01.Core` shipped both past Design into Core (real, compiled
    `ISynchronousEncryptionKeyProvider`/`EncryptionKeyProviderCapabilities`/
    `ISynchronousAsymmetricKeyProvider`/`AsymmetricKeyProviderCapabilities` types, and the gated
    members themselves) before implementation began — the real-assembly tests were wired directly as
    GATING, never deferred: `SecureDefaultsAssertionTests.SyncCryptoGate_RealAesGcmEncryptionService_
    ConstructionTimeGateGenuinelyWired` (T-362) and `.SyncCryptoGate_RealRsaAndEcdsaSignatureServices_
    ConstructionTimeGateGenuinelyWired` (T-363, both algorithms via one test). Both verified
    non-vacuous via a temporary sanity mutation (wrong callee/exception type), confirmed to fail,
    then reverted before commit.
    IMPLEMENTATION-TIME FINDING #2 — a real design/reality mismatch in this phase's own
    Implementation Rule 2, which assumed each gated member (`Encrypt`/`Decrypt`/`EncryptToString`/
    `DecryptToString`/`Sign`/`Verify`) constructs-and-throws `NotSupportedException` DIRECTLY in its
    own IL body. Direct inspection of the real, shipped `AesGcmEncryptionService`/`RsaSignatureService`/
    `EcdsaSignatureService` found instead that each type centralizes the throw in ONE private
    `ThrowIfNotGenuinelySynchronous(string)` helper every gated member calls as its first statement —
    a single-throw-site design, not four/two independent ones. Contrived fixtures (T-360/T-361) and
    the real-assembly tests (T-362/T-363) were both shaped to match: they prove (a) the constructor
    invokes the capability check, (b) each gated member invokes the private guard helper
    (`AssertMethodBodyInvokesMethod` again, pointed at the helper — not at the gated member's own
    absent throw), and (c) the guard helper itself constructs-and-throws `NotSupportedException`
    (`AssertMethodBodyThrowsExceptionType`, pointed at the helper).
    CORRECTS WO-081/P-504's own stated "Depends on: P-492, P-495" line for this half of the phase:
    the asymmetric gate (`IAsymmetricSignatureService`'s `Sign`/`Verify`) is `P-493`'s, not `P-492`'s
    — the phase's own "What is needed" text names "every sync `ISymmetricEncryptionService`/
    `IAsymmetricSignatureService` member" explicitly, so `P-493`'s Core is a real, additional
    prerequisite this documentation records even though WO-081's dispatch text omitted it.
    ALSO EVALUATED AND DELIBERATELY DEFERRED (not implemented here): `06.Persistence`'s own P-498
    design for `PreWarmedEncryptionKeyProvider` (an `IEncryptionKeyProvider` that "honestly earns"
    `ISynchronousEncryptionKeyProvider` by never touching its wrapped inner provider synchronously)
    suggests a generalized, platform-wide "no type claiming `ISynchronousEncryptionKeyProvider`/
    `ISynchronousAsymmetricKeyProvider` may contain a blocking-bridge call
    (`.GetAwaiter().GetResult()`/`.Result`/`.Wait()`) anywhere in its own method bodies" guard would
    be a MORE durable lock than this phase's own narrower gate-reachability check, since it would
    catch ANY future marker-claiming implementer cheating the contract, not just `AesGcmEncryptionService`
    itself. Not implemented in this phase: `06.Persistence`'s own T-142/T-143 already behaviorally
    prove the one shipped example (call-counting test doubles against `PreWarmedEncryptionKeyProvider`'s
    real warm/miss paths) more precisely than a generic IL scan could, and inventing new WO scope
    beyond P-504's own dispatched acceptance criteria is not this planning pass's call to make.
    Recorded here as a candidate follow-up phase for a future work order, not silently dropped.
    No new SK diagnostic ID — this is a real-assembly regression-lock on an already-designed
    (not-yet-implemented) guard's structure, not a source-level anti-pattern a Roslyn analyzer should
    catch. No SK diagnostic ID is consumed here.

    Eleventh real-world application/self-repair, 2026-09-09, triggered by `01.Core`'s WO-083 P-524
    (key-material zeroization) — a SECOND governance lock disturbed by a domain change in the same
    overall session, after `SK.00.CacheEncryptionAndRedisValidationLock`/T-337's earlier vacuous
    break. P-524 added `internal string AesGcmEncryptionService.EncryptToString(string, byte[],
    Action<byte[]>?)` alongside the pre-existing `public EncryptToString(string, byte[])` (a
    test-only, `InternalsVisibleTo`-gated seam letting a test observe the intermediate plaintext
    buffer before it is zeroed). This made T-363's own real-assembly test —
    `SyncCryptoGate_RealAesGcmEncryptionService_ConstructionTimeGateGenuinelyWired` — throw instead
    of assert: `AssertMethodBodyInvokesMethod`'s name-only resolution now found TWO methods named
    `EncryptToString` on `AesGcmEncryptionService` and rejected the ambiguity outright ("found 2
    methods named 'EncryptToString' — ambiguous").
    FIXED THE HELPER, NOT `01.Core` — a lock that breaks on any overload addition to a locked type is
    fragile by construction, and renaming `01.Core`'s method to placate a governance helper's
    limitation would be the tail wagging the dog. Two extensions to `AssertMethodBodyInvokesMethod`
    itself, both additive/backward-compatible (all fourteen pre-existing positional call sites across
    this file's own test suite compile and behave identically unchanged):
    <list type="bullet">
    <item>
    A new optional `Type[]? parameterTypes = null` FIFTH parameter. When the method name resolves to
    more than one method AND `parameterTypes` is supplied, the ambiguity is resolved by an exact
    positional parameter-type match (via a new `CecilStyleFullName(Type)` helper rendering a
    reflection `Type` the way Mono.Cecil renders a `TypeReference.FullName` — handles arrays and
    closed generic types, not just plain types) instead of being rejected outright. Left `null` (the
    default) for every pre-existing call site, preserving the original ambiguous-name-is-an-error
    behavior exactly.
    </item>
    <item>
    SAME-DECLARING-TYPE SIBLING-DELEGATION FOLLOW-THROUGH, proven NECESSARY (not merely convenient) by
    direct inspection of the real, shipped source before writing it: the public
    `EncryptToString(string, byte[])`'s ENTIRE body is
    `=> EncryptToString(plaintext, associatedData, captureIntermediatePlaintextForTesting: null)` — it
    never calls `ThrowIfNotGenuinelySynchronous` directly; only the internal 3-arg overload it
    forwards to does. Disambiguating to the public overload alone (via the new `parameterTypes`
    parameter) would therefore have made the check report the guard as unwired even though every real
    caller of the public entry point genuinely reaches it. `MethodBodyInvokes` now recurses
    (visited-`HashSet<MethodDefinition>`-guarded against self-/mutual-recursion cycles) into a
    same-declaring-type sibling method a call site targets when that call site is not itself the
    target callee — bounded to the SAME declaring type only (never crosses into a different type's
    implementation, so it can never be satisfied by an unrelated type happening to also call the
    guard).
    </item>
    </list>
    `SecureDefaultsAssertionTests` re-pointed the ambiguous `EncryptToString` case (and, for
    consistency, `Encrypt`/`Decrypt`/`DecryptToString` too, even though only `EncryptToString` was
    actually ambiguous) at the PUBLIC 2-arg overload explicitly via the new parameter. Verified
    non-vacuous via a temporary deliberately-wrong callee name (`"ThrowIfNotGenuinelySynchronousXXX"`,
    reverted before commit): the re-pointed assertion still fails loudly, confirming the fix did not
    accidentally make the check pass vacuously by resolving the internal testing overload instead of
    genuinely tracing the public one's real call graph. `SharedKernel.ArchitectureTests.Tests`:
    259/259 pass, 0 build warnings/errors.

ApplicationBehaviorsCacheInvalidationOrderingLockTests  (test class, no production Rules/Predicates class — root Phase Backlog P-489/WO-080, last phase in WO-080)
    Third genuinely EXECUTED real-composed-pipeline test in this project (Technique A shape,
    after T-336/SK.00.WebhookSsrfGuardLock and T-337/SK.00.CacheEncryptionAndRedisValidationLock)
    — proves, against the REAL, compiled SharedKernel.Application.Behaviors.dll, that
    CacheInvalidationBehavior's eviction observably follows TransactionBehavior's commit, and
    (in a second test) that AuditingBehavior's write still lands inside that same commit at the
    same time — both invariants proven simultaneously, since they pull in opposite registration
    directions relative to TransactionBehavior and a lock proving only one could pass while
    silently breaking the other.
    Builds a real IServiceCollection, calls the real AddSharedKernelApplicationBehaviors()
    .AddXBehavior()...Build() chain, registers the real MediatR pipeline, and dispatches a real
    command through it end to end via ISender — never a hand-rolled substitute pipeline, and
    never a static Mono.Cecil IL walk (this ordering is an emergent runtime property of MediatR's
    onion-wrapping, not visible in ApplicationBehaviorsBuilder.Build's own method-body IL).
    DELIBERATE, INDEPENDENT DUPLICATE of 05.Application's own in-domain regression test
    (CacheInvalidationTransactionOrderingTests.cs, P-488) — the whole point of P-489 is that this
    lock survives even a future edit that weakens or deletes that domain's own test, since it is
    owned by 00.Governance and consumes the real compiled binary via a new test-only
    ProjectReference (SharedKernel.Application.Behaviors.csproj, PrivateAssets="all") rather than
    depending on 05.Application's own test project.
    Verified non-vacuous: ApplicationBehaviorsBuilder.cs's registration order was temporarily
    reverted in-session to the pre-fix defect (CacheInvalidationBehavior registered AFTER
    TransactionBehavior), both tests were re-run and genuinely failed (eviction observed before
    the commit), then the file was fully reverted before commit (git diff confirmed empty,
    SharedKernel.Application.Behaviors.Tests re-confirmed at its unchanged 187/187).
    No new SK diagnostic ID. Not tracked under any phase key in this file's own Phase Key
    Registry — dispatched and closed directly against a root Phase Backlog entry, same shape as
    P-490. 253/253 SharedKernel.ArchitectureTests.Tests pass (251 baseline + 2).

StorageTopologyRules  (static class — 08.Storage package topology enforcement predicates; WO-043 P-271, reworked P-559)
    Accepts Assembly (or params Assembly[]) and returns ConditionList, or IReadOnlyList<string> for the
    two assembly-reference halves. Mirrors RedisTopologyRules's documented NotHaveDependencyOn matching
    contract (namespace StartsWith, no trailing dot, self-collision awareness). No Mono.Cecil, no
    ICustomRule. Topology since P-559: SharedKernel.Storage.Obs is a thin provider over
    SharedKernel.Storage.S3 (OBS is served through its S3-compatible API), so .Obs → .S3 is allowed
    and only the reverse direction is forbidden. This deliberately replaces the original sibling rule
    (ProviderPackagesNeverReferenceEachOther, removed), which forced .Obs to duplicate the whole S3
    implementation.

    WHY EVERY NAMESPACE CHECK HAS AN ASSEMBLY-REFERENCE HALF: the storage packages put their
    registration entry points in the shared SharedKernel.Storage namespace (AddSharedKernelStorage(),
    AddS3, AddObs, S3StorageBuilder), so a dependency on, say, AddObs is invisible to a
    NotHaveDependencyOn("SharedKernel.Storage.Obs") namespace check. The *ForbiddenAssemblyReferences
    methods inspect referenced assembly names to close that gap. Assert both halves of each rule.

    .AbstractionsHasNoThirdPartyDependencies(Assembly abstractionsAssembly) → ConditionList
        Asserts that SharedKernel.Storage.Abstractions has no dependency on any of four
        forbidden terms: "Amazon" (bare prefix — catches every AWSSDK.S3 namespace, since
        AWSSDK.S3's root namespace is "Amazon"), "SharedKernel.Storage.S3",
        "SharedKernel.Storage.Obs", and "SharedKernel.Configuration" (the Options-validation
        package only the provider packages need). Four iterative .Should().NotHaveDependencyOn(term)
        calls. None of the four terms is a prefix of the abstractions' own SharedKernel.Storage
        namespace — no self-collision.
        Rationale: 08.Storage/CLAUDE.md documents SharedKernel.Storage.Abstractions as having no
        cloud SDK dependency — only SharedKernel.Primitives plus
        Microsoft.Extensions.DependencyInjection.Abstractions for the store registry.

    .AbstractionsForbiddenAssemblyReferences(Assembly abstractionsAssembly) → IReadOnlyList<string>
        The assembly-level half of the rule above: returns every referenced assembly name starting
        with "AWSSDK", "SharedKernel.Storage.S3", "SharedKernel.Storage.Obs" or
        "SharedKernel.Configuration". Empty when compliant.

    .S3NeverReferencesObs(Assembly s3Assembly) → ConditionList
        Asserts that no type in SharedKernel.Storage.S3 depends on the "SharedKernel.Storage.Obs"
        namespace. Rationale: .S3 knowing about one S3-compatible vendor would be a cycle in intent,
        and every S3-only service would restore the OBS package. Vendor differences belong in .Obs's
        compatibility profile, passed down through AddS3Compatible.

    .S3ForbiddenAssemblyReferences(Assembly s3Assembly) → IReadOnlyList<string>
        The assembly-level half of S3NeverReferencesObs: returns every referenced assembly name
        starting with "SharedKernel.Storage.Obs". Empty when compliant.

    .OnlyProviderPackagesMayReferenceAmazonS3(params Assembly[] assembliesUnderTest)
                                            → ConditionList
        Asserts that no type in the supplied assemblies has a dependency on "Amazon.S3". Single
        Types.InAssemblies(assembliesUnderTest).That()...Should().NotHaveDependencyOn(
        "Amazon.S3") call — the structural sibling of
        CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders and
        CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching. The caller
        supplies every production assembly to check and must NEVER include
        SharedKernel.Storage.S3 or SharedKernel.Storage.Obs themselves — exclusion is achieved
        entirely by caller choice of which assemblies to pass, the same caller-controlled
        exclusion convention as PresentationLayeringRules.
        Rationale: application code must inject the named stores (IFileStorage,
        ITenantFileStorage, IFileStorageFactory from SharedKernel.Storage.Abstractions) — never a
        concrete Amazon.S3.IAmazonS3 type. The providers are wired at the composition root via
        AddSharedKernelStorage().AddS3(configuration) or .AddObs(configuration) followed by
        .AddStore(name).

    Permitted exemption list (caller-controlled — carries NO internal namespace guard,
    consistent with PresentationLayeringRules/CompositionRootExclusivityRules):
        - SharedKernel.Storage.S3 and SharedKernel.Storage.Obs — the only two assemblies
          permitted to reference "Amazon.S3"; achieved by the caller never passing either to
          OnlyProviderPackagesMayReferenceAmazonS3.
        Any additional exemption must be documented here before it is applied in code.

    Note: introduced in WO-043 P-271. Designed at authoring time (2026-07-16) against contrived
    in-memory assemblies only, because 08.Storage's own state-map then showed every phase at
    ○/empty and P-265/P-266/P-267 were still Design-in-progress/not-started. CORRECTED at this
    phase's own implementation closeout (2026-07-18): by the time the governance-phase-implementer
    session began, 08.Storage had independently reached Published — P-265/P-266/P-267 are all
    ● Complete in the root state-map, and SharedKernel.Storage.Abstractions/.S3/.Obs exist as
    real, clean-building assemblies (verified via `dotnet build --configuration Release` on all
    three, zero warnings/errors, 2026-07-18). Real-assembly verification was therefore wired in
    THIS phase rather than deferred as a follow-up: SharedKernel.ArchitectureTests.Tests.csproj
    gained test-only ProjectReferences (PrivateAssets="all") to all three real 08.Storage
    assemblies, and StorageTopologyRulesTests.cs carries three additional Real*-suffixed tests
    (AbstractionsHasNoThirdPartyDependencies_RealAbstractionsAssembly_RulePasses,
    ProviderPackagesNeverReferenceEachOther_RealS3AndObsAssemblies_BothElementsPass,
    OnlyProviderPackagesMayReferenceAmazonS3_RealNonProviderAssembly_RulePasses) alongside the
    six contrived-fixture fire/pass-path tests (T-194–T-199) that remain the primary red/green
    proof per the phase spec. All three real-assembly checks pass — the real
    SharedKernel.Storage.Abstractions references only SharedKernel.Primitives, SharedKernel.Storage.S3
    and SharedKernel.Storage.Obs never reference each other, and no real assembly outside the two
    providers references Amazon.S3. No architecture-rule discrepancy was found against the real
    packages. Contrived in-memory assemblies via CSharpCompilation + MetadataReference.CreateFromImage
    (the RedisTopologyRulesTests/CompositionRootExclusivityRulesTests technique) remain in place as
    the primary proof, per the phase spec's own instruction that they "remain the primary red/green
    proof" even when real-assembly verification becomes possible.

    P-559 update (2026-09-22): ProviderPackagesNeverReferenceEachOther was removed with the storage
    redesign and replaced by S3NeverReferencesObs + S3ForbiddenAssemblyReferences;
    AbstractionsForbiddenAssemblyReferences was added. StorageTopologyRulesTests now carries
    contrived fire/pass tests for each rule plus real-assembly checks
    (AbstractionsHasNoThirdPartyDependencies_RealAbstractionsAssembly_RulePasses,
    S3NeverReferencesObs_RealS3Assembly_BothHalvesPass, RealObsAssembly_BuildsOnTheS3Provider,
    OnlyProviderPackagesMayReferenceAmazonS3_RealNonProviderAssembly_RulePasses).

SearchTopologyRules  (static class — 09.Search package topology enforcement predicates; WO-044 P-278)
    Both factory methods accept Assembly (or two named Assembly parameters) and return
    ConditionList (or ConditionList[]). Mirrors StorageTopologyRules's structure and its
    documented NotHaveDependencyOn matching contract exactly (namespace StartsWith, no
    trailing dot, self-collision awareness) but scoped to 09.Search's two sibling provider
    packages instead of 08.Storage's two. No Mono.Cecil, no ICustomRule — every check is a
    pure NetArchTest .Should().NotHaveDependencyOn(...) assembly-dependency-graph predicate.

    .AbstractionsHasNoThirdPartyDependencies(Assembly abstractionsAssembly) → ConditionList
        Asserts that SharedKernel.Search.Abstractions has no dependency on any of six
        forbidden terms: "Meilisearch" (the MeiliSearch SDK's root namespace — CONFIRM
        EXACT CASING against the real published package at implementation time, per the
        StorageTopologyRules "Amazon" precedent for a bare-prefix third-party term),
        "Elastic" (bare prefix — catches Elastic.Clients.Elasticsearch and any other
        Elastic.* library in one term), "SharedKernel.Search.Meilisearch",
        "SharedKernel.Search.ElasticSearch", "SharedKernel.Configuration" (the
        Options-validation package only the two provider packages need — Abstractions
        itself references only SharedKernel.Primitives and SharedKernel.Contracts), and
        "Microsoft.Extensions" (09.Search/CLAUDE.md states Abstractions carries "zero
        PackageReference entries of any kind — not even
        Microsoft.Extensions.DependencyInjection.Abstractions, because no DI extension
        lives there"; this sixth term defends that explicit prose rule mechanically, not
        only the third-party-SDK rule the other five terms cover). Six iterative
        .Should().NotHaveDependencyOn(term) calls, the same iterative pattern as
        DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure and
        RedisTopologyRules/StorageTopologyRules's sibling methods. None of the six terms
        is a prefix of "SharedKernel.Search.Abstractions" — no self-collision.
        Rationale: 09.Search/CLAUDE.md documents SharedKernel.Search.Abstractions as having
        zero third-party NuGet dependencies and referencing only SharedKernel.Primitives
        (01.Core) and SharedKernel.Contracts (04.Contracts, for the guarded
        ToPagedList() bridge only) — the strictest dependency posture of any Abstractions
        package in the platform (stricter than SharedKernel.Caching.Abstractions, which is
        permitted Microsoft.Extensions.DependencyInjection.Abstractions). This
        mechanically confirms the abstraction never accidentally couples to either
        concrete SDK, to either provider package, to the Options-validation package, or to
        any Microsoft.Extensions.* dependency at all.

    .ProviderPackagesNeverReferenceEachOther(Assembly meilisearchAssembly, Assembly elasticSearchAssembly)
                                            → ConditionList[]
        Returns exactly two elements, in order: [0] SharedKernel.Search.Meilisearch must
        not depend on "SharedKernel.Search.ElasticSearch"; [1] SharedKernel.Search.ElasticSearch
        must not depend on "SharedKernel.Search.Meilisearch". TWO NAMED Assembly parameters
        (not params Assembly[]) — mirroring StorageTopologyRules' former ProviderPackagesNeverReferenceEachOther's
        (removed P-559) and UnitOfWorkSeamRules.UnitOfWorkInterfacesRemainDistinct's two-named-parameter
        convention: the rule's whole purpose is comparing two specific, named packages, so
        positional params would obscure which assembly is expected to be which. Only two
        packages exist here and neither identifying namespace
        ("SharedKernel.Search.Meilisearch", "SharedKernel.Search.ElasticSearch") is a
        prefix of the other or of its own declaring assembly — no lookup table needed,
        unlike RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther's four-package
        case. Caller must assert .GetResult().IsSuccessful on EACH element.
        Rationale: 09.Search/CLAUDE.md's own Provider role note states explicitly that
        SharedKernel.Search.Meilisearch and SharedKernel.Search.ElasticSearch "are sibling
        .{Provider} packages, not a .{Provider}.Core / .{Provider}.{Role} split, and they
        must never reference each other" — shared implementation shape (options-validation
        flow, the SearchFilter walker skeleton, receipt mapping, probe sequencing) is
        deliberately DUPLICATED rather than factored into a shared
        SharedKernel.Search.Core, mirroring the 08.Storage .S3/.Obs precedent exactly.

    Note: introduced in WO-044 P-278. Designed against contrived in-memory assemblies only at
    authoring time (2026-07-19) — 09.Search/state-map.md then showed the entire Design phase
    (D-01 through D-28, covering P-272/P-273/P-274) at ○, with only bare .csproj skeletons on
    disk for all three packages. CORRECTED at this phase's own implementation closeout
    (2026-07-24): by the time the governance-phase-implementer session began, 09.Search had
    independently reached Published — verified directly on disk, not assumed from prose —
    139/139 tasks ● across all six phases (Design/Scaffold/Core/Tests/Docs/Published), and
    SharedKernel.Search.Abstractions/.Meilisearch/.ElasticSearch exist as real, buildable
    assemblies. Real-assembly verification was therefore wired in THIS phase per the phase
    input's own GATING acceptance criterion, exactly mirroring the SK.00.StorageTopology
    precedent for the identical class of dependency-resolved-before-implementation finding:
    SharedKernel.ArchitectureTests.Tests.csproj gained three test-only ProjectReferences
    (PrivateAssets="all") to the real 09.Search assemblies, and SearchTopologyRulesTests.cs
    carries two additional Real*-suffixed tests
    (AbstractionsHasNoThirdPartyDependencies_RealAbstractionsAssembly_RulePasses,
    ProviderPackagesNeverReferenceEachOther_RealMeilisearchAndElasticSearchAssemblies_BothElementsPass)
    alongside the six contrived-fixture fire/pass-path tests (T-208–T-211, one extra
    Microsoft.Extensions-specific fire-path case beyond the minimum) that remain the primary
    red/green proof per the phase spec. All real-assembly checks pass — the real
    SharedKernel.Search.Abstractions references only SharedKernel.Primitives and
    SharedKernel.Contracts (confirmed zero Microsoft.Extensions/Meilisearch/Elastic/
    Configuration dependency), and SharedKernel.Search.Meilisearch/.ElasticSearch never
    reference each other. No architecture-rule discrepancy was found against the real
    packages — the CS8509/CS8524 WarningsNotAsErrors entries on both provider .csproj files
    (09.Search/CLAUDE.md's own documented, deliberate exhaustiveness-diagnostic downgrade) are
    the only build warnings observed and are expected, not a defect this phase's tests should
    (or do) flag.

IntelligenceTopologyRules  (static class — 10.Intelligence package topology enforcement predicates; WO-045 P-286)
    All factory methods accept Assembly (or three named/params Assembly parameters) and
    return ConditionList (or ConditionList[]). Mirrors StorageTopologyRules/
    SearchTopologyRules's structure and documented NotHaveDependencyOn matching contract
    exactly (namespace StartsWith, no trailing dot, self-collision awareness) but scoped
    to 10.Intelligence's THREE sibling provider packages (Qdrant/Milvus/SemanticKernel)
    instead of two. No Mono.Cecil, no ICustomRule — every check is a pure NetArchTest
    .Should().NotHaveDependencyOn(...) assembly-dependency-graph predicate.

    .AbstractionsHasNoThirdPartyDependencies(Assembly abstractionsAssembly) → ConditionList
        Asserts that SharedKernel.AI.Abstractions has no dependency on any of EIGHT
        forbidden terms: "Qdrant" (Qdrant.Client's root namespace, bare prefix),
        "Milvus" (Milvus.Client's root namespace, bare prefix), "Microsoft.SemanticKernel"
        (bare prefix), "SharedKernel.AI.Qdrant", "SharedKernel.AI.Milvus",
        "SharedKernel.AI.SemanticKernel", "SharedKernel.Configuration" (the
        Options-validation package only the three provider packages need — Abstractions
        itself references only SharedKernel.Primitives and, if genuinely needed,
        SharedKernel.Contracts), and "Microsoft.Extensions" (10.Intelligence/CLAUDE.md's
        Hard Violations list states .Abstractions takes zero PackageReference beyond
        SharedKernel.Primitives/.Contracts ProjectReferences and ships no DI extension —
        the same strictest zero-Microsoft.Extensions-anything posture
        SearchTopologyRules established for 09.Search). Eight iterative
        .Should().NotHaveDependencyOn(term) calls. None of the eight terms is a prefix
        of "SharedKernel.AI.Abstractions" — no self-collision. TWO MORE forbidden terms
        than StorageTopologyRules's five-term analog and SearchTopologyRules's six-term
        analog, because 10.Intelligence has THREE sibling providers, not two — three
        bare-prefix third-party-SDK terms and three "SharedKernel.AI.{Provider}" terms,
        instead of two of each.
        Rationale: mirrors 10.Intelligence/CLAUDE.md's own Hard Violations entry
        verbatim — "SharedKernel.AI.Abstractions taking a PackageReference that has not
        been explicitly adjudicated and recorded in this file. The default is zero."

    .ProviderPackagesNeverReferenceEachOther(Assembly qdrantAssembly, Assembly milvusAssembly, Assembly semanticKernelAssembly)
                                            → ConditionList[]
        THREE named Assembly parameters — the FIRST three-named-parameter shape in this
        domain (StorageTopologyRules/SearchTopologyRules/UnitOfWorkSeamRules all use
        exactly two, since Storage and Search each have only two sibling providers;
        10.Intelligence has three). Returns SIX ConditionLists, in order:
            [0] Qdrant !-> "SharedKernel.AI.Milvus"
            [1] Qdrant !-> "SharedKernel.AI.SemanticKernel"
            [2] Milvus !-> "SharedKernel.AI.Qdrant"
            [3] Milvus !-> "SharedKernel.AI.SemanticKernel"
            [4] SemanticKernel !-> "SharedKernel.AI.Qdrant"
            [5] SemanticKernel !-> "SharedKernel.AI.Milvus"
        None of the three identifying namespaces ("SharedKernel.AI.Qdrant",
        "SharedKernel.AI.Milvus", "SharedKernel.AI.SemanticKernel") is a prefix of
        another or of its own declaring assembly — no Dictionary<string,string[]>
        lookup table is needed (unlike RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther's
        four-package case), the same reasoning StorageTopologyRules/SearchTopologyRules
        already established for two packages, extended here to three. Caller must
        assert .GetResult().IsSuccessful on EACH of the six elements.
        Rationale: 10.Intelligence/CLAUDE.md states explicitly: "Sibling packages never
        reference each other, in any direction, at project or type level" — and each of
        P-280/P-281/P-282's own acceptance criteria names the exact pair of forbidden
        sibling references for its own package. This predicate is the exhaustive,
        all-six-directions mechanical form of those three acceptance criteria taken
        together.

    .NoHealthChecksDependencyAcrossIntelligencePackages(params Assembly[] intelligenceAssemblies) → ConditionList
        Asserts that no type in any of the supplied 10.Intelligence assemblies (caller
        supplies Abstractions plus the three providers) has a dependency on
        "Microsoft.Extensions.Diagnostics.HealthChecks". Single
        Types.InAssemblies(intelligenceAssemblies).That()...Should()
        .NotHaveDependencyOn("Microsoft.Extensions.Diagnostics.HealthChecks") call
        across all supplied assemblies — deliberately the NARROW full term, not the
        bare "Microsoft.Extensions" prefix AbstractionsHasNoThirdPartyDependencies
        uses, because the three PROVIDER packages legitimately need OTHER
        Microsoft.Extensions.* packages (DependencyInjection, Options, Logging) for
        their DI wiring — only Abstractions itself carries the
        zero-Microsoft.Extensions-anything constraint.
        Rationale: mechanizes Domain Invariant #8 / 10.Intelligence/CLAUDE.md's own Hard
        Violations entry verbatim: "Implementing IHealthCheck, or referencing
        Microsoft.Extensions.Diagnostics.HealthChecks, anywhere in 10.Intelligence." —
        ProbeAsync-shaped members on IVectorCollectionProvisioner/
        ICompletionProviderDescriptor are the sanctioned readiness primitive; wiring
        into AddHealthChecks() remains a 13.ServiceDefaults concern, mirroring the
        06.Persistence/08.Storage/09.Search readiness-probe split precedent.

    Permitted exemption list: none carried internally by any of the three factory
    methods — exclusion in every case is achieved entirely by which assemblies the
    caller chooses to pass (the PresentationLayeringRules/CompositionRootExclusivityRules/
    StorageTopologyRules caller-controlled-exclusion convention), never an internal
    namespace guard inside a predicate.

    Note: introduced in WO-045 P-286. UNVERIFIABLE against real assemblies as of this
    phase's authoring (2026-07-21) — 10.Intelligence/CLAUDE.md states "No production
    .cs file has been written yet"; only the original placeholder .csproj inventory
    exists on disk, even though the SharedKernel.AI.Abstractions interface CONTRACT
    itself is already ratified (P-279's Design phase). Real-assembly wiring is a
    GATING acceptance criterion on this phase per the phase input itself, mirroring
    SearchTopologyRules's precedent rather than the older non-blocking-follow-up
    precedent — see Dependencies in 00.Governance/state-map.md's
    SK.00.IntelligenceTopology phase block for the explicit blocking status.

    CORRECTED AT IMPLEMENTATION TIME (2026-07-27, WO-048 Milvus retraction — SK.00.IntelligenceTopology
    closeout): everything above this note was drafted (WO-045 P-286, 2026-07-21) against a
    THREE-provider design (Qdrant/Milvus/SemanticKernel), before 10.Intelligence's own Scaffold/
    Core/Tests/Docs/Published phases had shipped any code. Before this phase's own implementation
    session, arch-lead ratified WO-048: `SharedKernel.AI.Milvus` was permanently retracted —
    `Milvus.Client` never shipped a stable release — and does not exist on disk and never will
    (verified directly: no `10.Intelligence/SharedKernel.AI.Milvus/` directory; `IntelligenceWellKnown`
    carries no `MilvusProviderName`). `10.Intelligence` ships exactly TWO providers
    (`SharedKernel.AI.Qdrant`, `SharedKernel.AI.SemanticKernel`), and the SHIPPED
    `IntelligenceTopologyRules` class is implemented against that two-provider reality, not the
    three-provider shape documented above:
      — `.AbstractionsHasNoThirdPartyDependencies` carries SIX forbidden terms, not eight — the
        two Milvus terms ("Milvus", "SharedKernel.AI.Milvus") are absent from the real forbidden-term
        array. The six real terms are: "Qdrant", "Microsoft.SemanticKernel", "SharedKernel.AI.Qdrant",
        "SharedKernel.AI.SemanticKernel", "SharedKernel.Configuration", "Microsoft.Extensions".
      — `.ProviderPackagesNeverReferenceEachOther` is the TWO-named-Assembly-parameter form
        (`qdrantAssembly`, `semanticKernelAssembly`) returning a TWO-element `ConditionList[]`
        (`[0]` Qdrant !-> "SharedKernel.AI.SemanticKernel"; `[1]` SemanticKernel !->
        "SharedKernel.AI.Qdrant") — mirroring `StorageTopologyRules`/`SearchTopologyRules`'s own
        two-named-parameter convention exactly, NOT the three-named-parameter/six-element form
        drafted above. There is no Milvus assembly to pass, and no
        `Dictionary<string,string[]>` lookup table is needed for the same two-package reasoning
        `StorageTopologyRules`/`SearchTopologyRules` already established.
      — `.NoHealthChecksDependencyAcrossIntelligencePackages` is unchanged in shape (still
        `params Assembly[]`, still the narrow `"Microsoft.Extensions.Diagnostics.HealthChecks"`
        term) — only the caller now supplies two provider assemblies (Qdrant, SemanticKernel)
        plus Abstractions, not three.
      — `SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts` (documented above,
        WO-045 P-286) is UNAFFECTED by the retraction — its fifteen-term forbidden list already
        excluded "SharedKernel.AI" by self-exclusion and never named Milvus as a term; it is
        implemented exactly as originally documented, no correction needed.
    Real-assembly verification is NO LONGER a pending/GATING-open item as of this closeout — it was
    completed in this same phase: `10.Intelligence` reached Published (all six phases `●`, WO-048)
    before this phase's implementation session, so `SharedKernel.ArchitectureTests.Tests.csproj`
    gained test-only `ProjectReference`s (`PrivateAssets="all"`) to the three real, shipped
    `SharedKernel.AI.Abstractions`/`.Qdrant`/`.SemanticKernel` assemblies (there is no
    `SharedKernel.AI.Milvus` reference — that package does not exist), and `IntelligenceTopologyRulesTests.cs`
    carries three `Real*`-suffixed tests confirming all three factory methods pass against the
    shipped packages — no real violation surfaced. The contrived in-memory fixtures remain the
    primary red/green proof, per the same convention `StorageTopologyRulesTests`/
    `SearchTopologyRulesTests` established. This entry (and the SK0026 diagnostic-registry entry
    above) is intentionally left as originally drafted rather than rewritten in place — the
    three-provider/two-engine design history remains accurate context for why the class exists in
    this shape; this note is the authoritative as-shipped correction, matching this domain's
    established "annotate, never silently rewrite" convention (see SK0025's/StorageTopologyRules's/
    SearchTopologyRules's own CORRECTED notes for precedent).

SecurityArchitectureRules  (static class — 12.Security hard-rule enforcement predicates; WO-057 P-373)
    .DomainNeverReferencesTenantProvider(Assembly domainAssembly)  → ConditionList
        Asserts that no type in the supplied 03.Domain assembly has a field, constructor/method
        parameter, or IL instruction operand (Call/Callvirt/Newobj) whose resolved type is
        exactly SharedKernel.Security.Abstractions.ITenantProvider. Uses
        NoTenantProviderReferenceInDomainPredicate (ICustomRule — see below), reusing the
        established three-surface inspection technique from
        NoDbContextTransactionInApplicationPredicate (field types, constructor/method parameter
        types, instruction-operand declaring/constructed types).
        Carries NO exemption — 12.Security/CLAUDE.md states unconditionally "Domain code
        (03.Domain) must never reference ITenantProvider — it receives tenantId as a primitive,"
        with no carve-out for any 03.Domain sub-namespace.
        Failure message names the offending type and the surface (field/parameter/instruction)
        where the reference was found.
        Rationale: ITenantProvider is a request-scoped, infrastructure-facing abstraction
        (resolved from IHttpContextAccessor/claims at the application composition root); a
        03.Domain type accepting it directly would silently reintroduce the exact
        infrastructure-in-domain coupling the platform's DomainLayerPurityRules/
        PersistenceLayerProtectionRules.DomainAssembliesNeverReferencePersistenceStack
        precedent already forecloses for persistence — this rule closes the identical gap for
        the one remaining infrastructure-facing seam (12.Security) that had no mechanical
        check at all.
        Offending pattern: class PricingPolicy(ITenantProvider tenantProvider) : DomainService { ... }
        Compliant pattern: class PricingPolicy : DomainService {
            public ValidationResult<Money> Reprice(Guid tenantId, ...) { ... } } — the application layer
            resolves ITenantProvider.TenantId and passes it as a Guid primitive into the
            domain call.

    .NoSingletonRegistrationOfSecurityContextTypes(params Assembly[] assemblies)  → ConditionList
        Asserts that no method body in the supplied assemblies contains a Call/Callvirt IL
        instruction targeting a closed GenericInstanceMethod named "AddSingleton" whose
        GenericArguments include a type whose FullName is exactly
        "SharedKernel.Security.Abstractions.IUserContext" or
        "SharedKernel.Security.Abstractions.ITenantProvider". Uses
        NoSecurityContextSingletonRegistrationPredicate (ICustomRule — see below) — the
        domain's newest Mono.Cecil technique, GENERIC-INSTANCE-METHOD-ARGUMENT INSPECTION,
        distinct from every prior technique catalogued in this file (opcode-presence, Ldstr
        literal-collection, field-shape+literal-value resolution, generic-parameter-constraint
        inspection, Newobj-target matching, DeclaringType+Name matching): it inspects the
        GenericArguments collection on a resolved GenericInstanceMethod operand, not the
        method's declaring type or its parameters.
        Carries NO exemption — IUserContext/ITenantProvider must never be singleton anywhere
        in the platform's own DI extension methods, matching 12.Security/CLAUDE.md's own
        unconditional "Never register as singleton" Implementation Rule.
        Known limitation (documented, not a defect, mirroring HealthCheckTagIntegrityRules's
        own documented data-flow limitation): the non-generic AddSingleton(Type, Type) /
        AddSingleton(Type, Func<IServiceProvider, object>) overloads are not detected — only
        the closed-generic AddSingleton<TService>(...) / AddSingleton<TService, TImpl>(...)
        forms. Deliberate consequence (P-546): the one sanctioned singleton-shaped IUserContext
        registration — 06.Persistence's EfCorePersistenceBuilder.Build() placeholder,
        ServiceDescriptor.Singleton(typeof(IUserContext), AnonymousUserContext.Instance), which
        the 12.Security authentication packages remove before registering their scoped context —
        uses the non-generic form, so it is not flagged; 12.Security/CLAUDE.md requires
        placeholders to use exactly that form so this rule stays meaningful. The real
        SharedKernel.Security.Oidc.Extensions.OidcServiceCollectionExtensions.AddOidcAuthentication
        registers IUserContext/ITenantProvider with TryAddScoped (T-291).
        Failure message names the offending type, method, and which of the two forbidden types
        was registered singleton.
        Rationale: IUserContext/ITenantProvider are documented as request-scoped — one instance
        per HTTP request, resolved from IHttpContextAccessor at construction time. A singleton
        registration would capture the FIRST resolved request's identity/tenant context and
        silently leak it across every subsequent request on the same process — a severe
        cross-tenant/cross-user data-leak defect, not a style violation.
        Offending pattern: services.AddSingleton<IUserContext, MyUserContext>();
        Compliant pattern: services.AddScoped<IUserContext, MyUserContext>();

NoTenantProviderReferenceInDomainPredicate  (class : ICustomRule — internal predicate)
    No exemption guard — carries none, mirroring NoSpecificationEvaluatorDowncastPredicate's
    own zero-exemption precedent. For each type, checks three surfaces for an exact type-name
    match against "SharedKernel.Security.Abstractions.ITenantProvider":
      (1) TypeDefinition.Fields — checks FieldDefinition.FieldType.FullName
      (2) TypeDefinition.Methods — for each MethodDefinition (including constructors), checks
          each ParameterDefinition.ParameterType.FullName
      (3) TypeDefinition.Methods.Body.Instructions — for Call/Callvirt/Newobj opcodes, checks
          the resolved operand's declaring/constructed type FullName
    Returns false (rule violated) on the first match across any of the three surfaces, with
    failure message naming the offending type and the surface where the reference was found.
    Reuses the established Mono.Cecil three-surface inspection pattern from
    NoDbContextTransactionInApplicationPredicate. Lives in Predicates/ folder. Used by
    SecurityArchitectureRules.DomainNeverReferencesTenantProvider.

NoSecurityContextSingletonRegistrationPredicate  (class : ICustomRule — internal predicate)
    For each type, walks TypeDefinition.Methods.Body.Instructions for Call/Callvirt opcodes
    whose operand is a GenericInstanceMethod (Mono.Cecil's IL representation of a closed
    generic method call) whose ElementMethod.Name == "AddSingleton" (exact match). For each
    match, iterates the GenericInstanceMethod.GenericArguments collection and checks each
    argument's FullName against the set {"SharedKernel.Security.Abstractions.IUserContext",
    "SharedKernel.Security.Abstractions.ITenantProvider"}. Returns false (rule violated) on
    the first match, with failure message naming the offending type, method, and the matched
    type's simple name. Lives in Predicates/ folder. Used by
    SecurityArchitectureRules.NoSingletonRegistrationOfSecurityContextTypes.

    .DpopProofValidationNeverDuplicatedOutsideOidc(params Assembly[] assemblies)  → ConditionList
        (WO-058 P-383) Asserts that no type outside SharedKernel.Security.Oidc references the
        DPoP header-name literal or performs proof-JWT parsing. Uses
        NoDpopProofValidationDuplicationPredicate (ICustomRule — see below), which checks TWO
        independent surfaces inside every method body — either match violates the rule:
          (a) an Ldstr IL instruction whose operand is exactly "DPoP" (case-sensitive, the
              RFC 9449 canonical header name) — reuses the Ldstr literal-collection technique
              already established by HealthCheckTagIntegrityRules/MetricsInstrumentationRules.
          (b) a Call/Callvirt/Newobj instruction whose resolved operand's declaring type
              FullName is exactly "System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler" or
              "Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler" — reuses SK0301's
              (NoAesCipherInDomainOrApplicationPredicate) raw-type-reference technique, applied
              to JWT-proof-parsing types instead of cipher types.
        Exemption: SharedKernel.Security.Oidc only — the real, DPoP-proof-validating
        implementation package once 12.Security's P-376 ships. No second forward-looking
        exemption prefix (unlike SK0031's two-namespace shape) — DPoP is exclusively an
        OIDC/JWT-bearer-adjacent concern, not spread across every identity provider package.
        Failure message names the offending type and which surface ("DPoP" literal vs.
        JWT-handler type reference) matched.
        Rationale: mirrors this domain's own SecurityContextGuard (P-373) motivation, applied
        proactively rather than retroactively — mechanize "this validation logic lives in
        exactly one package" for a new sender-constraining credential mechanism BEFORE it
        ships, rather than waiting for a future gold-standard review to discover the drift.
        Offending pattern: a type outside SharedKernel.Security.Oidc reads
        Request.Headers["DPoP"] and hand-parses the proof JWT itself.
        Compliant pattern: the type calls into SharedKernel.Security.Oidc's own DPoP
        proof-validation surface instead of duplicating header-name literals or JWT parsing.
        UNVERIFIABLE against real assemblies as of this phase's authoring (12.Security's P-376
        is planned but not yet implemented) — proven now only against contrived fixtures; real-
        assembly re-verification is a Cross-Domain Dependency, not force-failed or deferred
        silently.
        CORRECTED AT IMPLEMENTATION TIME (2026-08-18, SK.00.SenderConstrainedCredentialGuard
        closeout): by implementation time, 12.Security had already shipped P-376 in full —
        confirmed on disk, not assumed — SharedKernel.Security.Oidc is packed at 4.0.0 and ships
        a real Dpop/DpopProofValidator.cs implementing the full RFC 9449 algorithm (including the
        "ath" binding). The "UNVERIFIABLE"/Cross-Domain Dependency framing above is therefore
        stale; real-assembly verification is IMPLEMENTED, not deferred — see T-299 in
        SharedKernel.ArchitectureTests.Tests/SecurityArchitectureRulesTests.cs. Confirmed the pass
        is genuine, not vacuous: the real DpopProofValidator's ProofHeaderName const field
        inlines to an Ldstr "DPoP" at its one usage site, and the type also holds a
        JsonWebTokenHandler field — both detection surfaces are genuinely present in the scanned
        assembly and pass only because the SharedKernel.Security.Oidc namespace exemption covers
        them. Left as originally drafted rather than rewritten in place, per this domain's
        "annotate, never silently rewrite" convention. [P-546, 2026-09-16: the redesigned
        package declares the "DPoP" literal as OidcAuthenticationDefaults.DpopScheme, and
        Dpop/DpopProofValidator.cs still holds a JsonWebTokenHandler field; T-299 now locates the
        assembly through OidcServiceCollectionExtensions.]

    .ClientCertificateAccessNeverDuplicatedOutsideMtls(params Assembly[] assemblies)  → ConditionList
        (WO-058 P-383) Asserts that no type outside SharedKernel.Security.Mtls reads
        HttpContext.Connection.ClientCertificate directly. Uses
        NoRawClientCertificateAccessOutsideMtlsPredicate (ICustomRule — see below), a
        single-surface IL match: a Call/Callvirt instruction whose resolved
        MethodReference.Name == "get_ClientCertificate" and
        MethodReference.DeclaringType.FullName == "Microsoft.AspNetCore.Http.ConnectionInfo" —
        the property-getter shape of HttpContext.Connection.ClientCertificate. This single,
        precise signal satisfies both halves of the phase's own "HttpContext.Connection.
        ClientCertificate/X509Certificate2" acceptance-criterion phrasing in one match, since
        ConnectionInfo.ClientCertificate IS declared as X509Certificate2? — a bare "any
        X509Certificate2 type reference" surface was deliberately rejected as a second
        condition because 01.Core/SharedKernel.Cryptography's IAsymmetricSignatureService
        legitimately handles X.509-adjacent cryptographic material for unrelated
        (non-HTTP-connection) signing/verification purposes; a broad type-reference match
        would false-positive there.
        Exemption: SharedKernel.Security.Mtls only — the new sibling provider package
        12.Security's P-377 will ship.
        Failure message names the offending type and method containing the
        ClientCertificate-getter call.
        Rationale: same proactive-locality motivation as DpopProofValidationNever
        DuplicatedOutsideOidc above — mTLS client-certificate trust/validation logic must live
        in exactly one package, mechanized before SharedKernel.Security.Mtls ships rather than
        discovered as drift in a later review.
        Offending pattern: a type outside SharedKernel.Security.Mtls reads
        httpContext.Connection.ClientCertificate directly to perform its own trust decision.
        Compliant pattern: the type calls into SharedKernel.Security.Mtls's own
        certificate-validation surface instead of reading the raw connection property itself.
        UNVERIFIABLE against real assemblies as of this phase's authoring (12.Security's P-377
        is planned but not yet implemented — no SharedKernel.Security.Mtls package exists on
        disk) — proven now only against contrived fixtures; real-assembly re-verification is a
        Cross-Domain Dependency, not force-failed or deferred silently.
        CORRECTED AT IMPLEMENTATION TIME (2026-08-18, SK.00.SenderConstrainedCredentialGuard
        closeout): by implementation time, 12.Security had already shipped P-377 in full —
        confirmed on disk, not assumed — SharedKernel.Security.Mtls is packed at 2.0.0 and ships
        real client-certificate validation. The "UNVERIFIABLE"/"no package exists on disk"
        framing above is therefore stale; real-assembly verification is IMPLEMENTED, not
        deferred — see T-300 in
        SharedKernel.ArchitectureTests.Tests/SecurityArchitectureRulesTests.cs. Verified this is a
        genuine pass, not a vacuous one: the real MtlsAuthenticationHandler never calls
        ConnectionInfo.get_ClientCertificate directly — it reads
        CertificateValidatedContext.ClientCertificate (a DIFFERENT declaring type the predicate
        deliberately does not match), because the ASP.NET Core certificate-authentication
        middleware itself already resolved the certificate from ConnectionInfo before invoking
        this package's handler. Mono.Cecil genuinely walks every method body in the real
        assembly (the same technique T-296's contrived fixture proves fires correctly against a
        type that DOES call the getter); the real code simply never performs that specific IL
        shape. Left as originally drafted rather than rewritten in place, per this domain's
        "annotate, never silently rewrite" convention.

NoDpopProofValidationDuplicationPredicate  (class : ICustomRule — internal predicate)
    No exemption guard beyond the SharedKernel.Security.Oidc namespace check performed by the
    caller. For each type, walks TypeDefinition.Methods.Body.Instructions once, checking each
    instruction against the two surfaces described above (Ldstr "DPoP" literal; JWT-handler
    type reference on Call/Callvirt/Newobj operands). Returns false (rule violated) on the
    first match across either surface, with failure message naming the offending type and the
    matched surface. Lives in Predicates/ folder. Used by
    SecurityArchitectureRules.DpopProofValidationNeverDuplicatedOutsideOidc.

NoRawClientCertificateAccessOutsideMtlsPredicate  (class : ICustomRule — internal predicate)
    No exemption guard beyond the SharedKernel.Security.Mtls namespace check performed by the
    caller. For each type, walks TypeDefinition.Methods.Body.Instructions for Call/Callvirt
    opcodes whose resolved MethodReference matches Name == "get_ClientCertificate" and
    DeclaringType.FullName == "Microsoft.AspNetCore.Http.ConnectionInfo". Returns false (rule
    violated) on the first match, with failure message naming the offending type and method.
    Lives in Predicates/ folder. Used by
    SecurityArchitectureRules.ClientCertificateAccessNeverDuplicatedOutsideMtls.
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
- `GuardPurityRules` lives in `SharedKernel.ArchitectureTests` — it must not reference any runtime domain package. The `IGuardClause` type is loaded reflectively via `typeof(IGuardClause).Assembly`; since WO-082/P-508 (`SharedKernel.Guards` merged into `SharedKernel.Core`, namespace preserved) the consuming test project must reference `SharedKernel.Core` directly to supply the assembly reference — never `SharedKernel.Guards`, which no longer exists as an independent project.
- `DoesNotContainThrowIlPredicate` inspects IL via Mono.Cecil `MethodDefinition.Body.Instructions`. If `NetArchTest.eNt` does not expose `IType.Definition` as a public property, add `Mono.Cecil >= 0.11.5` explicitly to `SharedKernel.ArchitectureTests.csproj`.
- The `Guard.Throw` exclusion in `GuardPurityRules` must be a full nested-type name match (`"Guard+Throw"` or equivalent CLR name) — not a namespace prefix match, which would be too broad.
- WO-082/P-508: `DoesNotContainThrowIlPredicate` must scope itself to the `SharedKernel.Guards` namespace (checked via a `GetEffectiveNamespace` walk up `TypeDefinition.DeclaringType`, never NetArchTest's built-in `ResideInNamespaceStartingWith`, which reads the raw `Namespace` property — always empty for a nested type in Mono.Cecil, which would silently drop every real guard type from the check) now that `typeof(IGuardClause).Assembly` also hosts unrelated `SharedKernel.Core` types (base exceptions, BCL/railway extensions). Any future predicate that needs to scope itself to a specific namespace within a shared/merged assembly should reuse this same effective-namespace walk, not the built-in namespace filter.
- SK0006 `GuardClauseThrowAnalyzer` follows the same `netstandard2.0` constraint as SK0001–SK0005. No new NuGet dependencies beyond `Microsoft.CodeAnalysis.CSharp`.
- Each architecture test for `GuardPurityRules` must exercise the fire path (violation fixture), the pass path (clean fixture), and the exclusion path (`Guard.Throw` fixture) — three test cases minimum.
- `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching` uses `.Should().NotHaveDependencyOn("SharedKernel.Caching")` — the string is the assembly name prefix, matched by NetArchTest's dependency scanner against referenced assembly names. Two calls are required: one for `"SharedKernel.Caching"` (catches both the main package and Redis because `.Caching.Redis` contains `.Caching` as a prefix) and optionally one scoped specifically to `"SharedKernel.Caching.Redis"` for a more targeted failure message.
- SK0007 `RedisChannelServiceMessagingSubstituteAnalyzer` operates on `ClassDeclarationSyntax` nodes only. It uses a simple name match (`IRedisChannelService`) without semantic model symbol resolution — the simple name is unique within the SDK. `GetTypeName` unwraps `QualifiedNameSyntax` (right-most name) and `AliasQualifiedNameSyntax` (`global::`), so `SharedKernel.Caching.Redis.PubSub.IRedisChannelService` and `global::SharedKernel.Caching.Redis.PubSub.IRedisChannelService` are caught like the bare name (P-547; before that a qualified name fell through to `ToString()` and slipped past). Namespace suppression uses the same `SyntaxNode.Parent` walk pattern established by SK0001.
- SK0007 forbidden-context terms are: `"Command"`, `"Event"`, `"DomainEvent"`, `"IntegrationEvent"` — case-sensitive substring match applied to both the class name and all ancestor namespace identifier strings. The check on `"Event"` intentionally covers `"DomainEvent"` and `"IntegrationEvent"` as substrings; all four terms are listed explicitly for documentation clarity.
- SK0007 severity escalation to `Error` is gated on field confirmation of zero false positives on the `"DomainEvent"` substring — some projects name classes `IDomainEventHandler` without misusing Redis pub/sub. Until confirmed, severity remains `Warning`.
- Architecture tests for `CachingAbstractionRules` require two test cases minimum: one fire-path (non-exempt assembly references concrete caching) and one pass-path (only exempt assemblies scanned). No exclusion-path test is needed because exemption is enforced by the caller choosing which assemblies to pass, not by an internal filter.
- `DomainGoldStandardRules.DomainServicesMustExtendAbstractBase` uses `.AreNotAbstract()` in the NetArchTest predicate chain to exclude the `DomainService` abstract base class itself. The consuming test project must reference `SharedKernel.Domain` so that `typeof(IDomainService)` and `typeof(DomainService)` can be resolved as assembly references.
- SK0008 `AggregateRootDispatchCouplingAnalyzer` checks `ConstructorDeclarationSyntax` parameter types — not `ObjectCreationExpression` or field declarations. The type name check for `IAggregateRoot` uses `SimpleNameSyntax` or `GenericNameSyntax` identifier text (not the full `ToString()`). Dispatch-context check applies to both the class name and all ancestor `NamespaceDeclarationSyntax` / `FileScopedNamespaceDeclarationSyntax` names via the established parent walk pattern. No semantic model required.
- SK0009 `DomainEventMissingVersionAttributeAnalyzer` operates on both `ClassDeclarationSyntax` and `RecordDeclarationSyntax`. The base list check is a simple name match — `BaseList.Types` iterated for any `SimpleNameSyntax` or `IdentifierNameSyntax` whose identifier text is `"IDomainEvent"`. Abstract types are excluded via `Modifiers.Any(SyntaxKind.AbstractKeyword)`. No semantic model required.
- SK0010 `SpecificationOrderingConflictAnalyzer` collects `InvocationExpressionSyntax` nodes from the constructor body. The method name is extracted from `MemberAccessExpressionSyntax.Name.Identifier.Text` or, for simple invocations, directly from `IdentifierNameSyntax.Identifier.Text`. Both `"ApplyOrderBy"` and `"ApplyOrderByDescending"` must appear for SK0010 to fire. No semantic model required.
- `ContractsPurityRules.ContractsAssembliesHaveNoDomainTypeOnPublicSurface` carries no exemption. `EventEnvelope<TEvent>` is constrained to `IIntegrationEvent` (declared in `SharedKernel.Contracts` itself), so nothing in the real contracts assembly depends on `SharedKernel.Domain`; a fixture whose envelope-shaped type is constrained to a domain type fails the rule, and that is the intended behaviour.
- Every `ContractsPurityRules` factory method and `SharedKernelLayeringRules.ContractsReferencesOnlyCore` must keep a pass-path test against the real `SharedKernel.Contracts` assembly (`typeof(EventEnvelope).Assembly`) alongside its contrived fixtures. The earlier rule set was proven only on fixtures and failed the first time it met the real package.
- `ContractsPurityRules` judges wire surface, not behaviour, except on integration events: a factory, projection or codec method on a non-event contract type (`PageRequest.Create`, `PagedList<T>.Map`, `PageCursor.Decode`) is legitimate and may return `Result`/`ValidationResult`. Never widen `IntegrationEventsHaveNoNonTrivialMethods` back to every type, and never turn `NoResultTypedPublicMemberPredicate` into a dependency-level check — both would fail the real package.
- `ContractsPurityRules.IntegrationEventImplementationsMustBeSealed` uses NetArchTest's own `.BeSealed()` — no custom `SealedTypePredicate` is needed.
- `PersistenceLayerProtectionRules.OnlyEfUnitOfWorkMayCallSaveChanges` — the namespace exemption (`TypeDefinition.Namespace.StartsWith("SharedKernel.Persistence.EfCore")`) is evaluated as the first guard inside `NoDirectSaveChangesPredicate`. Do not apply the exemption at the `PersistenceLayerProtectionRules` call site — it belongs inside the predicate so the rule correctly self-documents the single permitted caller.
- `PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable` — the `"IRepository"` prefix check on `TypeDefinition.Interfaces` is intentionally broad: it covers `IRepository<T,TId>`, `IReadRepository<T,TId>`, and any sub-interface. `IQueryable` is matched by `ReturnType.Name == "IQueryable"` (non-generic) or `ReturnType.FullName.Contains("IQueryable")` (generic). Both checks are required to cover the IL representation of `IQueryable<T>`.
- `PersistenceLayerProtectionRules.DomainAssembliesNeverReferencePersistenceStack` is additive with `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure` — both rules may run in the same test suite. They are not duplicates: the latter covers broad infra terms; this rule adds Npgsql and `SharedKernel.Persistence.*` as a WO-013-scoped gate. Never remove either in favour of the other.
- `NoDirectSaveChangesPredicate` and `NoIQueryableReturnPredicate` reuse the established Mono.Cecil `TypeDefinition` access pattern from `DoesNotContainThrowIlPredicate`. The existing `Mono.Cecil >= 0.11.5` NuGet reference in `SharedKernel.ArchitectureTests` covers both new predicates — no new NuGet dependency is introduced.
- `PersistenceInterfaceOwnershipRules.IUserContextDeclaredOnlyInSecurityAbstractions` and `TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions` accept `params Assembly[]` — the caller must NOT pass `SharedKernel.Security.Abstractions` itself; only the assemblies to be checked for erroneous re-declarations are supplied. These rules do not assert presence in the owner — they assert absence everywhere else.
- `PersistenceInterfaceOwnershipRules.IReadRepositoryMustNotExposeIQueryable` is scoped to `"IReadRepository"` prefix specifically — it is separate from and complementary to `PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable` (which uses the broader `"IRepository"` prefix). Both may run in the same test suite targeting the same assembly; neither removes the need for the other.
- `PersistenceInterfaceOwnershipRules.ReadOnlyRepositoriesNeverTrack` (P-558) replaced `NoGetByIdAsyncOnReadRepository`: `GetByIdAsync` is part of the read contract now; the rule instead fails a read-only repository (an `IReadRepository` implementor with no `IRepository` interface) that calls `AsTracking` anywhere in its methods or nested types. Call it with the assembly containing the concrete repositories.
- `InterfaceDeclarationOwnershipPredicate` is stateless and may be reused across multiple `PersistenceInterfaceOwnershipRules` factory methods with different name sets. Construct a new instance per call — do not share instances across rules to avoid name-set bleed.
- SK0011 `GuidFormatCodeMisuseAnalyzer` is the first SK analyzer to require a `SemanticModel.GetTypeInfo` check on the receiver expression. This is necessary to distinguish `Guid.ToString("N")` from `int.ToString("N")` (which is a valid numeric format specifier). The semantic model call is scoped only to `ToString` invocations with a single string literal argument — the cost is minimal.
- SK0011 fires globally with no suppression namespace. Suppression is per-call-site only (`#pragma warning disable SK0011`). The rationale is that non-`"D"` Guid formats are never correct in audit trail context; any other context (URL segments, log correlation IDs) should be explicitly opted out with an inline suppression and a comment.
- `HasRequiredMethodPredicate` must not be confused with a presence-enforcer at the interface level — it operates at the implementor (concrete type) level. It does not assert that the interface itself declares the method; it asserts that the concrete implementing type has the method in its `TypeDefinition.Methods`. This covers both direct declaration and inherited declaration (if the type inherits from a base that declares the method, NetArchTest's Mono.Cecil `TypeDefinition.Methods` may or may not include inherited methods — test this behavior and if inherited methods are not covered, scope the predicate to `BaseType` traversal as well).
- `RepositoryContractCompletenessRules` must be called with the assembly containing the concrete repository implementations (e.g., `SharedKernel.Persistence.EfCore`), not the abstractions assembly. The abstractions assembly contains interfaces, not implementations — `HasRequiredMethodPredicate` scopes to interface-implementing types, so an abstractions-only assembly will produce zero matches and the rule will trivially pass, masking real violations.
- `EfCorePackageHygieneRules.NoSpecificationEvaluatorDowncastInEfCoreAssembly` must be called with the `SharedKernel.Persistence.EfCore` assembly only. The `castclass` opcode check is a simple operand name prefix match — no semantic model or type hierarchy walk is required. The rule fires on any cast whose target `TypeReference.Name` starts with `"SpecificationEvaluator"`, covering both the generic (`SpecificationEvaluator<T>`) and any subclass forms in IL.
- `EfCorePackageHygieneRules.IUnitOfWorkImplementorsMustHaveExactlyOneConstructor` must be called with the `06.Persistence` assembly containing concrete `IUnitOfWork` implementors (e.g., `SharedKernel.Persistence.EfCore`). `SingleConstructorPredicate` self-scopes to `IUnitOfWork` implementors only — passing an unrelated assembly produces zero matches and the rule trivially passes without masking violations, provided the correct persistence assembly is also passed.
- `EfCorePackageHygieneRules.ApplicationLayerMustNotReferenceDbContextTransaction` must be called with the `05.Application` assembly. Passing persistence assemblies is redundant — the namespace exemption inside `NoDbContextTransactionInApplicationPredicate` is a safety net, not the primary enforcement mechanism. The "IDbContextTransaction" substring check covers the full interface name including namespace in the `FullName` property, ensuring `BeginTransactionAsync` return types and `IDbContextTransaction`-typed fields are both detected.
- `EfCorePackageHygieneRules.NoDirectEfPropertyUsageInEfCoreAssembly` (WO-051 P-327) must be called with the `SharedKernel.Persistence.EfCore` assembly only, mirroring its sibling `NoSpecificationEvaluatorDowncastInEfCoreAssembly`. `NoDirectEfPropertyUsagePredicate` matches on `MethodReference.Name == "Property"` AND `MethodReference.DeclaringType.FullName == "Microsoft.EntityFrameworkCore.EF"` — a `DeclaringType`+`Name` match, not a bare method-name match, consistent with how `NoDbContextTransactionInApplicationPredicate` already discriminates BCL/framework static-method calls whose bare name alone could collide.
- `NoDirectEfPropertyUsagePredicate` carries NO exemption mechanism — no namespace guard, no allow-list registry — mirroring `NoSpecificationEvaluatorDowncastPredicate`'s own zero-exemption precedent. This is deliberate: the platform's one known legitimate `EF.Property<T>` pattern (a `HasQueryFilter(Expression<Func<TEntity,bool>> filter)` tenant/shadow-property global query filter) is structurally, automatically excluded, because the C# compiler never emits a `Call`/`Callvirt` opcode targeting `EF.Property` when the call appears inside a lambda whose converted type is `Expression<TDelegate>` — it lowers the entire lambda body into `System.Linq.Expressions.Expression`-builder calls instead, referencing `EF.Property<T>`'s `MethodInfo` only as metadata. This claim must be confirmed empirically during implementation (compile the `HasQueryFilter`-shaped pass-path fixture, inspect the emitted IL) per this domain's established "confirmed empirically, not assumed" discipline (SK0025/`StorageTopologyRules`/`SearchTopologyRules` precedent) — do not trust this design note without that verification. If a genuinely justified direct usage is ever found, it requires a governance review and an explicit revision of this entry (e.g., adding a small allow-list registry mirroring `ReflectionExemptionRegistry`'s shape) before any exemption is applied in code.
- `NoDirectEfPropertyUsageInEfCoreAssembly` introduces zero new SK diagnostic ID and zero new NuGet dependency — the fourth predicate in `EfCorePackageHygieneRules`, all four carrying no SK ID, following that class's established "EfCore-package-scoped regression-prevention rule, no SK ID" shape rather than the platform-wide SK0013 (raw-`HttpClient`)/SK0020 (ad hoc-logging) Roslyn-analyzer shape its own motivating phase input cites — a deliberate choice: the IL-opcode-presence technique avoids needing to duplicate the C# compiler's own expression-tree-conversion determination (`SemanticModel.GetTypeInfo(lambda).ConvertedType` against `Expression<TDelegate>`) inside a Roslyn analyzer's semantic-model logic. Motivating incidents: P-105 (`EfReadRepository.GetByIdsAsync`) and P-316 (`TenantedRepository`) — the second independent occurrence of the identical defect class is what elevated this from a one-time fix to a mechanically-enforced prohibition, mirroring the SK0012/`ReflectionExemptionRegistry` escalation precedent (P-147).
- `NoDirectEfPropertyUsageInEfCoreAssembly` is UNVERIFIABLE against the real, corrected `SharedKernel.Persistence.EfCore` assembly until `06.Persistence` P-316 ships — as of this rule's introduction, `TenantedRepository`'s `EF.Property<TId>` call sites are still live in shipped source. Design/implementation/initial tests use contrived in-memory Mono.Cecil fixtures only; real-assembly re-verification is a GATING acceptance criterion (not a non-blocking follow-up), mirroring the `SK.00.SearchTopology`/`SK.00.IntelligenceTopology`/`SK.00.WorkflowTopology` precedent — see the Cross-Domain Dependencies entry in `00.Governance/state-map.md`'s `SK.00.EfPropertyUsageGuard` phase block. **CORRECTED at implementation closeout (2026-07-31):** `06.Persistence` P-316 had already shipped by implementation time — confirmed on disk (`06.Persistence/state-map.md`'s C-101 is `●` Complete; `TenantedRepository.cs` uses `BuildIdEqualsPredicate`'s `Expression.Parameter`/`Property`/`Equal`/`Lambda` construction for both `GetByIdForTenantAsync`/`GetByIdForTenantIncludingDeletedAsync`, with no direct `EF.Property<TId>` call remaining). Real-assembly verification was wired in this phase, not deferred: `SharedKernel.ArchitectureTests.Tests.csproj` gained a test-only `ProjectReference` (`PrivateAssets="all"`) to `SharedKernel.Persistence.EfCore`, and `EfCorePackageHygieneRulesTests.NoDirectEfPropertyUsageInEfCoreAssembly_RealEfCoreAssembly_RulePasses` confirms zero violations.
- **Fixture-design gotcha discovered while implementing T-272 (SK.00.EfPropertyUsageGuard, 2026-07-31):** proving an IL-opcode-presence `ICustomRule` "fires inside an ordinary (non-expression-tree) lambda" requires the fixture's lambda to capture ONLY `this`, never a local variable or method parameter. A lambda capturing a local/parameter compiles onto a compiler-generated nested `<>c__DisplayClass` type — confirmed empirically via a temporary recording `ICustomRule` that NetArchTest's `Types.InAssembly(...).Should().MeetCustomRule(...)` scan visits only top-level types, never nested/compiler-generated ones (the SAME platform-documented gap already recorded for SK0012/`ReflectionGuardRules`/`MediatRDomainEventDispatcher`, WO-039 P-240 — now confirmed to apply identically here, not a defect in `NoDirectEfPropertyUsagePredicate`'s own IL-walk logic, which was independently proven correct by direct invocation against the nested `TypeDefinition` during diagnosis). A lambda capturing only `this` (e.g., via an instance field assigned immediately before the lambda runs) is instead compiled by Roslyn as a plain private instance method directly on the enclosing top-level type — which NetArchTest's scan DOES visit. Apply this fixture-design pattern to any future test in this file needing to prove a Mono.Cecil-IL-walk `ICustomRule` fires on lambda-embedded IL; capturing a local/parameter will make the fire-path assertion silently pass for the wrong reason (the rule never actually inspects the type containing the violation), not because the rule is correct.
- `EncryptionPatternGuardRules` introduces a new 03xx SK ID block (SK0301–SK0304) dedicated to the WO-019 encryption subsystem. The 03xx block is separate from the sequential SK0001–SK0011 general-purpose block and the SK0201–SK0202 multi-tenancy block. Never backfill SK0012–SK0200 with encryption rules — those gaps are reserved for the respective domain blocks.
- `NoAesCipherInDomainOrApplicationPredicate` checks both field types and IL instruction operands for the three cipher types (`AesGcm`, `Aes`, `SymmetricAlgorithm`). The namespace guard (`SharedKernel.Persistence.*` and `SharedKernel.Security.*`) is applied as the first check, before any IL walking, to avoid false positives from the legitimate converter and JWT signing code paths.
- `NoEncryptionAttributeOnDomainEntityPredicate` uses a case-insensitive substring match on `"Encrypt"` — this deliberately catches all common forms: `[Encrypted]`, `[EncryptedColumn]`, `[EncryptAttribute]`, `[ShouldEncrypt]`, etc. If a future attribute with "Encrypt" in its name is legitimately placed on a domain type for non-encryption purposes, document the exemption in this file before adding a name-specific exclusion to the predicate.
- `NoEncryptionRotationJobInjectionPredicate` class-name exemptions (`*RotationJob*`, `*HostedService*`, `*Controller*`, `*Activity*`) are substring matches on `TypeDefinition.Name` (the simple CLR type name, not the full namespace-qualified name). This is intentionally broad to cover naming conventions like `EncryptionKeyRotationHostedService`, `KeyRotationActivity`, and `EncryptionManagementController`.
- `NoDirectEncryptedValueConverterInstantiationPredicate` is scoped to `IEntityTypeConfiguration<T>` implementors only (interface name prefix check). General application code that is not an EF Core configuration class is not subject to SK0304 — the rule is narrowly targeted at the EF Core model-building phase where the misuse pattern causes double-encryption.
- `EncryptionModelConvention` exemption in `NoDirectEncryptedValueConverterInstantiationPredicate` is by exact type name (`TypeDefinition.Name == "EncryptionModelConvention"`). If the convention class is renamed, update both the predicate and this rule entry in the same PR.
- All four predicates (SK0301–SK0304) reuse the Mono.Cecil `TypeDefinition` access pattern established by `DoesNotContainThrowIlPredicate`. No new NuGet dependency — the existing `Mono.Cecil >= 0.11.5` explicit reference in `SharedKernel.ArchitectureTests` covers all four.
- `EncryptionPatternGuardRules` factory methods are called with domain and application assemblies supplied by the consuming test project via `typeof(SomeDomainType).Assembly`. The factory methods never hard-code assembly paths.
- `NoAesCipherInDomainOrApplicationPredicate`'s namespace exemption was narrowed in WO-037 P-229 from `{"SharedKernel.Persistence", "SharedKernel.Security"}` to `{"SharedKernel.Cryptography"}` only — the motivating incident was a hand-rolled `AesGcm` usage shipped inside `SharedKernel.Persistence.*` that this predicate exempted wholesale because it was only ever invoked against `03.Domain`/`05.Application`, never against `06.Persistence` itself. SK0301 remains scoped to whichever assemblies the caller passes; `CryptoIsolationRules.NoRawSymmetricCipherOutsideCryptography` is the platform-wide generalization intended to be invoked against every production assembly. No new SK ID was minted — see the `CryptoIsolationRules` entry in Architecture Test Contracts for the full reconciliation rationale.
- `SK.00.CacheEncryptionAndRedisValidationLock` (WO-065 P-437, shipped 2026-08-24) is the ninth application of the "lock a documented-but-not-yet-mechanized default" family and the first inside `02.Caching`. Technique A (cache-encryption compress-then-encrypt ordering) is a genuinely EXECUTED real-assembly test — the second in this file, after `SK.00.WebhookSsrfGuardLock`'s Technique B — never a new `SecureDefaultsAssertion` method, because composition order is a computed behavior no IL technique can honestly prove without assuming a not-yet-fixed decorator shape. It asserts a SIZE-RATIO threshold (pipeline output vs. an encrypt-only baseline for a highly-compressible payload), not a byte-exact structural claim — this is intentional, mirroring the size-based, representation-agnostic verification style. Technique B (`AddRedisConnection` eager startup validation) reuses `AssertMethodBodyInvokesMethod` unchanged, targeting `Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions.ValidateOnStart` as the callee (CORRECTED at implementation time from the phase's original `Microsoft.Extensions.Options.OptionsBuilderExtensions` — same NuGet package, different namespace) — proving `.ValidateOnStart()` is genuinely wired is the correct way to distinguish eager startup validation from inert `DataAnnotations` attributes or a lazily-validated `IValidateOptions<T>`. Both real-assembly tests are GATING and IMPLEMENTED — `02.Caching`'s P-433/P-436 had already shipped before this phase's implementation session began. **T-337 RE-LOCKED 2026-09-08** (same session as `SK.00.SyncCryptoGateAndArgon2ConfinementLock`, WO-081) after `02.Caching`'s `SK.02.CacheEncryptionAadBinding` deleted `CacheEncryptionSerializer` and moved `AddCacheEncryption()` to decorate `ICacheService` instead of `IFusionCacheSerializer` — the original assertion had gone VACUOUS (measuring the serializer's compression-only output after the architecture change, not the real encrypted pipeline). Re-pointed at the correct layer via a new minimal in-memory `ICacheService` double; see this class's own type-level remarks for the full incident record.
- Both `02.Caching` dependencies (P-433/P-436, its own Phases 42/45) were still in the planning stage — not yet dispatched for implementation — when this rule was designed. Per this domain's own repeated experience (eight prior occurrences), the implementer must re-verify against `02.Caching/state-map.md` directly before writing either real-assembly test rather than trusting the "not yet implemented" framing recorded at design time — it has resolved before implementation in every prior phase in this family.
- `CryptoIsolationRules` and `UnitOfWorkSeamRules` (WO-037 P-229) introduce zero new SK diagnostic IDs and zero new NuGet dependencies — both reuse the existing `Mono.Cecil >= 0.11.5` reference. `UnitOfWorkInterfacesRemainDistinctPredicate` is a negative-space/regression-guard rule: both `IUnitOfWork` interfaces are independently declared today (the desired state), so its fire-path test fixtures must use contrived two-assembly pairs proving the predicate would catch a future interface-merge or interface-inheritance attempt — there is no existing bad pattern in the codebase to point the fire-path test at.
- `NoRawSymmetricCipherOutsideCryptographyPredicate`'s `RandomNumberGenerator` surface uses a `MethodReference.DeclaringType.FullName` match rather than a single method-name match, because `RandomNumberGenerator` exposes multiple static and instance entry points (`Fill`, `GetBytes`, `Create`, etc.) — a `DeclaringType` check catches all of them in one IL walk pass, consistent with how `NoDirectSaveChangesPredicate` matches `DbContext.SaveChanges`/`SaveChangesAsync` by declaring-type-plus-name rather than enumerating every overload individually.
- `UnitOfWorkSeamRules.SharedContractsAreNotRedeclared` (P-558) takes one `Assembly` and is asserted once per 05/06/13/16 assembly; the former two-assembly `UnitOfWorkInterfacesRemainDistinct` was deleted with the contract merge.
- SK0201 `TenantedDbContextOnModelCreatingAnalyzer` scans `MethodDeclarationSyntax` nodes named `OnModelCreating` with the `override` modifier. Ancestry check walks `ClassDeclarationSyntax.BaseList.Types` for a type whose simple name is `TenantedDbContext`; if not found on the immediate class, walks parent `ClassDeclarationSyntax` nodes in the same file (syntax-only — cross-file ancestry is not resolved). Body scan calls `DescendantNodes().OfType<InvocationExpressionSyntax>()` on the method body and checks for a `MemberAccessExpressionSyntax` with `BaseExpressionSyntax` receiver and `Name.Identifier.Text == "OnModelCreating"`. Fires on the method identifier if not found. (P-558 removed the former `ApplyTenantFilters` alternative: the tenant filter is a model-finalizing convention now.) No suppression namespace — suppress per-site via `#pragma warning disable SK0201`.
- SK0202 `IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer` scans `InvocationExpressionSyntax` nodes. Filter: simple method name (from `IdentifierNameSyntax` or `MemberAccessExpressionSyntax.Name`) is `"IgnoreQueryFilters"` AND argument list is empty (zero arguments). Two exemptions checked in order: (1) namespace walk via `SyntaxNode.Parent` for any `NamespaceDeclarationSyntax` or `FileScopedNamespaceDeclarationSyntax` whose `Name.ToString()` starts with `"SharedKernel.Persistence.EfCore"` — same pattern as SK0001/SK0007; (2) `FirstAncestorOrSelf<ClassDeclarationSyntax>()` with `Identifier.Text == "TenantedRepository"` (exact string match). Reports on the full invocation expression if neither exemption applies. Any additional exemption class or namespace must be documented in `00.Governance/CLAUDE.md` under SK0202 before applying suppression.
- RS2008 (analyzer release tracking) is **satisfied, never suppressed**. There is no `NoWarn` for it in `SharedKernel.Analyzers.csproj` and there must not be one. This rule previously said the opposite; that was wrong and hid a broken setup — Roslyn only recognises the filenames `AnalyzerReleases.Shipped.md`/`AnalyzerReleases.Unshipped.md`, and the files had been named `AnalyzerReleaseTracking.*.txt`, so they were registered as `AdditionalFiles` correctly but were never read, and RS2008 fired for every rule regardless of their contents. With the correct filenames the build is 0 warnings with `EnforceExtendedAnalyzerRules=true`. All 41 rules are recorded in `AnalyzerReleases.Shipped.md` under `## Release 1.0`; a new rule goes into `AnalyzerReleases.Unshipped.md` first and moves across when a release is cut. Adding a rule without recording it makes RS2008 fire, which is the intended behaviour — verified non-vacuous (removing one row yields exactly one RS2008 warning).
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
- `RedisTopologyRules` introduces zero new SK diagnostic IDs. Seven of its eight factory methods are pure NetArchTest checks (`.Should().NotHaveDependencyOn(...)`, or `.NotHaveNameMatching(...)` for `CachingAbstractionsDeclaresNoProviderSpecificTypes`); the eighth, `CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions` (P-547), uses the `AssemblyReferenceAllowListPredicate` `ICustomRule` because only the assembly's real reference list can catch a dependency nobody put on a deny-list.
- **NetArchTest `NotHaveDependencyOn(term)` matching contract (critical)**: `term` is compared via `StartsWith` against each scanned type's set of dependency *namespaces* (the declaring namespace of every type referenced from a type's members) — NOT assembly names, and with NO trailing dot on either side of the comparison. A trailing dot on `term` (e.g. `"SharedKernel.Caching.Redis.HashStore."`) will NEVER match because dependency-namespace strings never carry a trailing dot — this was a confirmed regression during WO-023 P-145 and must not be reintroduced. Additionally, a type's dependency-namespace set includes its OWN declaring namespace (self-reference) — checking a package against its own identifying namespace term is a guaranteed false positive across every type in that package.
- `RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages` checks five exact-namespace terms iteratively (one `.Should().NotHaveDependencyOn(term)` call per term: `"SharedKernel.Caching.Redis.Batch"`, `"SharedKernel.Caching.Redis.Extensions"`, `"SharedKernel.Caching.Redis.DistributedLocking"`, `"SharedKernel.Caching.Redis.HashStore"`, `"SharedKernel.Caching.Redis.PubSub"`), following the established iterative pattern from `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure`. None of these five terms is a prefix of `"SharedKernel.Caching.Redis.Core"` or `"SharedKernel.Caching.Redis.Core.Extensions"` (the assembly under test), so no self-collision occurs — this is why the L2 package's two sub-namespaces (`.Batch`, `.Extensions`) are used instead of the bare `"SharedKernel.Caching.Redis"` root.
- `RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther` accepts `params Assembly[]` and returns `ConditionList[]` (NOT a single `ConditionList`) — one element per input assembly, in order. Internally it resolves each scanned assembly's OWN identifying namespace term(s) via a `Dictionary<string,string[]>` keyed by assembly simple name (`"SharedKernel.Caching.Redis"` → its Batch/Extensions terms; `"SharedKernel.Caching.Redis.DistributedLocking"`/`.HashStore`/`.PubSub` → their own exact namespace), then builds the forbidden-term set as the UNION of the OTHER three packages' terms only — excluding the scanned assembly's own term(s) avoids the self-dependency false positive described above. `"SharedKernel.Caching.Redis.Core"` and `"SharedKernel.Caching.Abstractions"` are never part of any forbidden set (permitted dependencies per the exemption list). Callers (including test fixtures whose assembly simple name is not one of the four recognized packages) must call `.GetResult()` on EVERY element of the returned array.
- `RedisTopologyRules.PubSubNeverReferencesMessaging` and `RedisTopologyRules.MessagingNeverReferencesCaching` are directional converses of each other and must both be asserted — NetArchTest dependency checks are one-directional, so passing one does not imply the other passes. Both reuse the `"SharedKernel.Messaging"` / `"SharedKernel.Caching"` prefix-matching convention already established by `MessagingArchitectureRules` and `CachingAbstractionRules` respectively — these two terms have no trailing dot and are deliberately broad prefixes (they must match every sub-namespace of the respective capability).
- `RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies` forbids every caching provider (`SharedKernel.Caching.Redis` prefix, `SharedKernel.Caching.FusionCache`), every provider library (`StackExchange.Redis`, `ZiggyCreatures`, `RedLockNet`, `Polly`, `Microsoft.Extensions.Caching`), the options and hosting stacks (`Microsoft.Extensions.Options`, `Microsoft.Extensions.Hosting` — added P-547, when options types and hosted services left the package), EF Core and MassTransit. The term `"SharedKernel.Caching.Redis"` is used WITHOUT a trailing dot deliberately — the abstractions package cannot self-collide with it, and the prefix catches all five Redis packages in one term. Because namespace terms cannot separate two assemblies sharing a namespace, it is always asserted together with `CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions`.
- `AssemblyReferenceAllowListPredicate` (`Predicates/`, P-547) takes the allowed assembly simple names as `params string[]` and treats `System`, `System.*`, `mscorlib` and `netstandard` as always allowed. It checks `TypeDefinition.Module.AssemblyReferences`, so it fails every type once the assembly gains an unlisted reference — expected, since the violation is the assembly's. Reusable for any other package whose contract is "references only X".
- `RedisTopologyRules.CachingAbstractionsDeclaresNoProviderSpecificTypes` matches simple type names against `Redis|Fusion|RedLock|StackExchange|Garnet|Valkey|Memcache|Connection`. The companion theory `ProviderSpecificContract_IsDeclaredByItsProviderPackage` pins `IRedisChannelService` → `SharedKernel.Caching.Redis.PubSub`, `IRedisHashService`/`ITypedHashStore<T>` → `SharedKernel.Caching.Redis.HashStore`, and `IRedisConnectionProbe` → `SharedKernel.Caching.Redis.Core` (assembly; namespace `SharedKernel.Caching.Redis.Core.Health`). `ConnectionHealthState`, `RedisConnectionHealthTracker` and the Redis circuit breaker were removed in the P-547 redesign; the theory now takes the expected namespace as a separate argument.
- `RedisTopologyRules.DistributedLockingNeverReferencesRedLock` locks the Lua-script lock implementation in place: RedLock.net cannot issue a fencing token atomically with acquisition.
- `RedisTopologyRules` lives in `SharedKernel.ArchitectureTests/Rules/` alongside `CachingAbstractionRules.cs`. It introduces no new NuGet dependency — the project's existing `NetArchTest.Rules` and `Mono.Cecil` references cover every method.
- In-memory fixture assemblies compiled via `CSharpCompilation`/`Assembly.LoadFrom` for `RedisTopologyRulesTests` MUST use assembly names that do not collide with real `ProjectReference`d assemblies already loaded in the test `AssemblyLoadContext` (e.g., name a `SharedKernel.Caching.Redis.HashStore`-shaped fixture `"Fixture.<Scenario>.SharedKernel.Caching.Redis.HashStore"`, not `"SharedKernel.Caching.Redis.HashStore"`). `Assembly.LoadFrom(path)` for a simple name matching an already-loaded assembly returns the ALREADY-LOADED real assembly, not the fixture, causing `CS0234`/missing-type failures. Cross-fixture `MetadataReference`s must be built via `MetadataReference.CreateFromImage(ImmutableArray<byte>)` from the in-memory emitted bytes, not `CreateFromFile(Assembly.Location)`.
- `ReflectionGuardRules.NoMakeGenericMethodReflection(Assembly)` uses `.That().AreNotAbstract()` before the `.Should().MeetCustomRule(...)` call to exclude compiler-generated abstract helper types (e.g., state machine types generated by async/await) that may contain unusual IL patterns. This is the same filter philosophy used by `DomainGoldStandardRules.DomainServicesMustExtendAbstractBase`.
- `NoMakeGenericMethodReflectionPredicate` checks `MethodReference.Name == "MakeGenericMethod"` (exact name, case-sensitive). This name is unique to `System.Reflection.MethodInfo.MakeGenericMethod` — no other BCL API uses this exact name. No namespace or declaring-type check is needed; the name match is sufficient and avoids false positives from custom extension methods that would need to be deliberately named `MakeGenericMethod` to trigger the rule.
- `ReflectionExemptionRegistry.IsExempt(typeFullName, methodName)` uses `TypeDefinition.FullName` (the CLR full name including namespace and enclosing types, e.g., `"SharedKernel.Persistence.EfCore.EncryptionRotationService"`) and `MethodDefinition.Name` (the simple method name, e.g., `"LoadBatchAsync"`). Both strings are matched case-sensitively. If a method is overloaded, all overloads with the same name are covered by a single registry entry — the registry is method-name-scoped, not signature-scoped, to avoid brittle signature strings in the allow-list.
- `ReflectionGuardRules` is the only SK0012 enforcement mechanism — there is no Roslyn analyzer complement. The reason: `MethodInfo.MakeGenericMethod` is called at runtime on a variable of type `MethodInfo` returned from `GetMethod`/`GetMethods`; there is no compile-time syntax pattern to detect. A Roslyn analyzer would only fire on the literal string `"MakeGenericMethod"` passed to invocation expressions, missing any case where the `MethodInfo` variable is obtained from a method call, stored, and then `.MakeGenericMethod(...)` is called on it in a separate statement. IL inspection is the only reliable detection mechanism.
- `ReflectionGuardRules.NoMakeGenericMethodReflection` must be called with production assemblies only — never pass `SharedKernel.ArchitectureTests` itself, any `.Tests` project, or `SharedKernel.Benchmarks`. The `ReflectionExemptionRegistry` is part of `SharedKernel.ArchitectureTests` and references to `MakeGenericMethod` inside the test predicate infrastructure itself are not in scope (the predicate is called on the types of the SUPPLIED assembly, not on the predicate's own class).
- SK0012 is the next sequential ID in the SK0001–SK00N general-purpose block (SK0001–SK0011 were the prior sequential IDs). The 02xx, 03xx, and 07xx blocks are separate domain-specific ranges. SK0012 begins in the general-purpose block because the reflection prohibition is platform-wide and does not belong to any single capability domain.
- The P-147 motivating incident (EncryptionRotationService.LoadBatchAsync) MUST be referenced in the `ReflectionExemptionRegistry.cs` class-level XML doc comment and in the `NoMakeGenericMethodReflectionPredicate.cs` file header comment. This ensures future readers understand why the registry exists and why expression trees are always preferred over the `MakeGenericMethod` shortcut.
- **Closure-free `static` lambdas compile onto a compiler-generated `<>c` nested cache class, not the declaring type — this matters for every `ReflectionExemptionRegistry` entry.** A `static` lambda with no captured state (e.g. `PublishDelegateCache.GetOrAdd(eventType, static t => {...})` in `SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher.PublishSingle`, and the structurally identical pattern in `07.Messaging`'s `MassTransitEventPublisher.BuildPublisher`) is compiled by Roslyn onto a private method on a compiler-generated `<>c` singleton cache class nested inside the declaring type (Mono.Cecil `TypeDefinition.FullName` uses `/` as the nested-type separator, e.g. `Outer/<>c`), with a synthesized method name shaped like `<ContainingMethodName>b__{token}_{ordinal}` — NOT the source-level declaring type and method name a naive reading would suggest. Any `ReflectionExemptionRegistry` entry for a `MakeGenericMethod` call inside such a lambda must be derived empirically (compile, run the rule, read the exact names from the `ReflectionGuardRules` failure message) rather than assumed from source — the ordinal is not part of any documented compiler contract and is sensitive to lambda count/order within the file. Discovered while registering the WO-039 P-240 `MediatRDomainEventDispatcher` exemption (the platform's first real `ReflectionExemptionRegistry` entry).
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
- `PresentationLayeringRules` introduces zero new SK diagnostic IDs — both rules are pure NetArchTest `ConditionList` predicates over Mono.Cecil IL inspection, mirroring the existing precedent that boundary-mapping prohibitions (raw `HttpClient`, `Result`↔HTTP and `ProblemDetails` construction) are enforced via this domain's `ICustomRule` predicates rather than always minting a new Roslyn analyzer. Neither predicate carries an internal namespace exemption — exclusion of `SharedKernel.Presentation.WebApi` is achieved entirely by the consuming test project never passing that assembly to either factory method. Document any future internal exemption here before adding one to either predicate.
- `NoDirectProblemDetailsConstructionPredicate` matches on `MethodReference.DeclaringType.FullName` exact string equality against `"Microsoft.AspNetCore.Mvc.ProblemDetails"` and `"Microsoft.AspNetCore.Http.HttpValidationProblemDetails"` — both are concrete framework types, so a `newobj` opcode is always the construction site (no factory-method indirection to account for, unlike `EncryptedValueConverter<T>`). Reuses the `Newobj`-walk pattern from `NoDirectEncryptedValueConverterInstantiationPredicate` — no new NuGet dependency.
- `NoInlineResultBranchBeforeHttpResultPredicate` is a **method-level co-occurrence check, not a control-flow analysis**. It does not verify that the `IsSuccess`/`IsFailure` read occurs immediately before the HTTP result return — it only verifies that both signals appear somewhere in the same method body and that the body never maps through the WebApi core (a `ResultHttpExtensions`/`ErrorProblemDetailsExtensions` call or a `new ErrorHttpResult`; the `ToProblemDetailsResult` escape hatch was deleted by P-562). This is a deliberate over-approximation (same documented-limitation philosophy as `HealthCheckTagIntegrityRules`'s literal-collection technique) — a method that reads `IsSuccess` for an unrelated logging decision and separately returns an `IResult` for an unrelated reason would also be flagged. If this produces real false positives in practice, narrow the check to control-flow adjacency in a follow-up phase; do not narrow it speculatively now.
- `NoInlineResultBranchBeforeHttpResultPredicate`'s `Result`/`Result<T>` type-name match (`"Result"` exact or `"Result\`1"` prefix for the IL generic-arity-suffixed name) targets `SharedKernel.Primitives.Result`/`Result<T>` specifically. If a consuming assembly defines an unrelated type also named `Result` with its own `IsSuccess`/`IsFailure` properties, this predicate cannot distinguish them without a `DeclaringType.Namespace` check — add a namespace guard (`"SharedKernel.Primitives"`) if this false-positive risk is ever confirmed in practice; it is not added pre-emptively because no such collision is known to exist in this platform's codebase today.
- `PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi` and `.NoInlineResultBranchBeforeHttpResultOutsideWebApi` both accept `params Assembly[]` — the caller is responsible for never including `SharedKernel.Presentation.WebApi` in the supplied list. Unlike most prior `ICustomRule` predicates in this domain, there is no internal `TypeDefinition.Namespace.StartsWith(...)` guard inside either predicate; this is a deliberate design choice because no single namespace prefix covers every legitimate in-package construction site (the problem factory, the exception handler, `ResultHttpExtensions` itself, and any future factory all legitimately trigger both signals).
- `NoInlineResultBranchBeforeHttpResultPredicate` matches the WebApi mapping types (`ResultHttpExtensions`, `Errors.ErrorProblemDetailsExtensions`, `ErrorHttpResult`) and the typed-results namespace by string, because `SharedKernel.ArchitectureTests` references no runtime package. A stale name matches nothing, and a stand-in fixture declaring the same stale name keeps passing (P-562 R21 moved `ErrorHttpResult` to the root namespace; the rule then flagged a compliant `new ErrorHttpResult(error)` while its fixture test stayed green). Every such name must stay pinned by a test that compiles against the real assembly (`PresentationLayeringRulesTests.CompileAgainstRealAssemblies`); when a WebApi mapping type moves or a new one is added, update the predicate and those tests together.
- `PresentationLayeringRules` lives in `SharedKernel.ArchitectureTests/Rules/PresentationLayeringRules.cs`; its two `ICustomRule` predicates live in `Predicates/`. Both reuse the existing `Mono.Cecil >= 0.11.5` reference — no new NuGet dependency introduced by this phase.
- **`const string` vs `static readonly string` produce different IL at the *consuming* call site** — this matters for any future test fixture or predicate reasoning about field-reference detection. The C# compiler const-folds every `const string` field reference into a bare `Ldstr` literal at each call site (no `Ldsfld`, no trace that a constant was referenced at all); only `static readonly string` field references compile to `Ldsfld`. `StringConstantsClassDetector.ResolveStringConstants` correctly resolves the *declaring* type's own value for both field kinds (via `FieldDefinition.Constant` for `const`, via a `.cctor` `Ldstr`→`Stsfld` walk for `static readonly`), but `NoBareHealthCheckLiteralWhereConstantsExistPredicate`'s pass-path (field access instead of literal) only holds for `static readonly string` constants classes — a `const string` constants class can never produce a passing fixture for the "field access, not literal" scenario, because Roslyn erases the field reference before Mono.Cecil ever sees the consuming method's IL. Discovered while building the T-140 pass-path fixture for `SK.00.HealthCheckConstantsGuard` (WO-028 P-178); document this if a future domain's constants-class convention is ever questioned for using `const` instead of `static readonly`.
- `ApplicationPipelineRules` introduces zero new SK diagnostic IDs — its remaining checks are pure Mono.Cecil `ICustomRule` predicates, mirroring the established precedent (`RedisTopologyRules`, `CompositionRootExclusivityRules`, `GrpcNeverReferencesContracts`, `PresentationLayeringRules`) that boundary-mapping and structural-purity prohibitions do not always require minting a new Roslyn analyzer. `00.Governance` never references `05.Application`/`05.Application.Behaviors` directly (layering: `00.Governance` references nothing) — both predicates and `PipelineOrderAssertion` are designed and tested here against contrived in-memory fixture assemblies; `05.Application` is responsible for invoking them against its own real assembly. P-544 retracted the third check, `.NoHandRolledRetryLoopOutsideResilienceBehavior` (and its backing `NoTaskDelayOutsideResilienceBehaviorPredicate`), when 05.Application's redesign dropped `ResilienceBehavior`/`IRetryableRequest` entirely — see the "P-544 REMOVAL NOTE" in the Architecture Test Contracts entry above.
- `NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate`'s behavior-name set (`"TracingBehavior"`, `"CacheInvalidationBehavior"`) and forbidden-namespace set are both caller-supplied `HashSet<string>` constructor parameters, never hardcoded inside the predicate — the same caller-supplied-list convention already established by `HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive`'s `dependencyCheckMethodNamePrefixes` parameter. This lets a future infra-adjacent behavior be covered by a caller-side change alone, no predicate code change required. As of P-544, `CacheInvalidationBehavior` lives in the sibling `SharedKernel.Application.Behaviors.Caching` assembly — callers pass both assemblies to `ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure`.
- `NoGenericConstraintMatchesStreamRequestPredicate` introduces the platform's **fourth distinct Mono.Cecil technique** for this domain, alongside opcode-presence (`NoMakeGenericMethodReflectionPredicate`), `Ldstr` literal-collection (`HealthCheckTagIntegrityRules`), and field-shape/literal-value resolution (`StringConstantsClassDetector`): **IL generic-parameter-constraint inspection** (`GenericParameter.Constraints` on an open generic type's type parameter, with interface-closure resolution). This is a structural check at the type-definition level, not an instruction walk — document any new constraint-inspection helper here if a future rule needs the same technique, so it is reused rather than redefined.
- `PipelineOrderAssertion` is the first artifact in `SharedKernel.ArchitectureTests` that is **not** a `ConditionList`/`ICustomRule` — it is a plain public reflection helper operating on an unbuilt `IServiceCollection`'s `ServiceDescriptor` entries, never calling `BuildServiceProvider()`. It exists in this package (not `16.Testing`) because it asserts an architectural invariant (fixed `IPipelineBehavior<,>` registration order), the same rationale that already places `ArchitectureRuleBase` and every `ICustomRule` predicate here rather than in shared test infrastructure. `05.Application.Behaviors.Tests` is the intended consumer — see `05.Application/state-map.md` T-17/T-18 (WO-036).
- SK0014 `ClosedGenericResiliencePipelineRegistrationAnalyzer`, SK0015 `StreamPipelineBehaviorMisregistrationAnalyzer`, and SK0016 `RequestTypeShortNameUsageAnalyzer` (WO-038 P-235) are the next three sequential IDs in the SK0001–SK00N general-purpose block (SK0012, SK0013 were the prior two). All three target `netstandard2.0` and pin `Microsoft.CodeAnalysis.CSharp 4.14.0`, same as every prior SK analyzer.
- SK0015 `StreamPipelineBehaviorMisregistrationAnalyzer` is the second SK analyzer in this domain (after SK0011) that requires `SemanticModel.GetSymbolInfo` — resolving whether a DI-registration type argument implements `MediatR.IStreamPipelineBehavior<,>` cannot be done from syntax alone (unlike SK0703/SK0705/SK0708's naming-heuristic approach), because the five known streaming behavior names are an enumerable convention, not a structural guarantee; using the interface-implementation check instead avoids a `"Stream"`-prefix naming-heuristic false-negative risk. The self-exemption check (`AddStreamingBehaviors` method name) remains syntax-only — it is evaluated on the enclosing `MethodDeclarationSyntax` before the semantic-model call is made, to short-circuit the more expensive symbol resolution inside the one sanctioned call site.
- SK0016 `RequestTypeShortNameUsageAnalyzer`'s namespace scope (`SharedKernel.Application`/`SharedKernel.Application.Behaviors`) is a trigger-IN scope, not a trigger-OUTSIDE-with-exemption scope — this is the inverse of the pattern used by SK0001/SK0007/SK0013 (which fire everywhere except a named namespace). The inversion is deliberate: the `typeof(TRequest).Name` collision risk is intrinsic to MediatR pipeline-behavior tag/key construction, which lives exclusively in this domain, so scoping the rule to fire only inside it avoids false positives from unrelated `typeof(X).Name` usage elsewhere in the platform (e.g. legitimate short-name display strings).
- `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag` (WO-038 P-235) reuses the `Ldstr` literal-collection technique from `HealthCheckTagIntegrityRules` (WO-027 P-173) — no new Mono.Cecil technique is introduced, only a new call-site search target (`Histogram<T>.Record`). This rule is designed and tested against CONTRIVED in-memory fixtures only — real-assembly verification against `05.Application`'s own `MetricsBehavior<,>` (which, as of P-544, resolves its `ApplicationMetrics` histogram through `IMeterFactory` and records duration in seconds, not milliseconds) is `05.Application`'s responsibility, outside this domain's jurisdiction (`00.Governance` references nothing and writes no implementation files for other domains).
- `ClosedGenericResiliencePipelineRegistrationAnalyzer` (SK0014) fires globally with no suppression namespace, unlike most namespace-scoped SK analyzers — `ResiliencePipeline<T>` (arity 1) is unsafe as a DI-registered or injected type in any assembly, not only `SharedKernel.Application`. Suppression is per-site only (`#pragma warning disable SK0014`).
- SK0017 `CommandImplementsCacheableQueryAnalyzer`, SK0018 `QueryImplementsInvalidatesCacheAnalyzer`, and SK0019 `RetryableRequestWithoutIdempotencyAnalyzer` are the domain's third, fourth, and fifth analyzers requiring a `SemanticModel`-resolved interface closure (`INamedTypeSymbol.AllInterfaces`), after SK0011 and SK0015. A `BaseList` simple-name check is insufficient for these three rules because `ICommandBase`/`IQuery<TResponse>` are typically implemented transitively (e.g. through `ICommand<TResponse> : ICommandBase`), not declared directly on the command/query type.
- All three interface matches (SK0017–SK0019) use `OriginalDefinition` + `ContainingNamespace` prefix check (`"SharedKernel.Application"`, covering both `SharedKernel.Application` and `SharedKernel.Application.Behaviors`) rather than exact-assembly `INamedTypeSymbol` identity — this is deliberate so analyzer test fixtures stay self-contained: a fixture-local interface declared inside a matching-namespace code block in the SAME test compilation satisfies the check, with no `ProjectReference` to the real `SharedKernel.Application`/`SharedKernel.Application.Behaviors` assemblies required for fire/pass-path tests.
- SK0017/SK0018/SK0019 all exclude types carrying the `abstract` modifier (`Modifiers.Any(SyntaxKind.AbstractKeyword)`) — the same exemption already established by SK0009 — so a generic abstract request base class spanning multiple marker-interface families behind a type parameter is not prematurely flagged; concrete (non-abstract) types further down the same inheritance chain are still checked via the full `AllInterfaces` closure.
- SK0017/SK0018/SK0019 fire globally with no namespace-scoped trigger condition — unlike SK0016's trigger-IN scope, these three are explicitly consumer-side rules: the violation (a command/query type implementing an incompatible marker-interface combination) occurs in a CONSUMING microservice's own type declarations, never inside `SharedKernel.Application`/`SharedKernel.Application.Behaviors` itself, which declares no command or query types at all (only the generic pipeline-behavior classes that consume them). This is why the zero-false-positive requirement against this domain's own shipped source is a structural argument (verifiable by inspection), not a real-assembly architecture test the way NetArchTest `ICustomRule` phases require.
- SK0017, SK0018, and SK0019 are the next three sequential IDs in the SK0001–SK00N general-purpose block (SK0016 was the prior ID). They introduce zero new `SharedKernel.ArchitectureTests` artifacts — pure Roslyn analyzers, `netstandard2.0`, `Microsoft.CodeAnalysis.CSharp` 4.14.0, matching every prior SK analyzer.
- SK0020 `DirectILoggerExtensionMethodUsage` and SK0021 `HandWrittenLoggerMessageDefineDelegate` are the next two sequential IDs in the SK0001–SK00N general-purpose block (SK0019 was the prior ID), and the first pair in this domain implemented as a SINGLE `DiagnosticAnalyzer` class (`LoggingAuthoringStyleAnalyzer`) emitting two `DiagnosticDescriptor`s rather than one class per ID — justified because both encode the same platform logging standard ("always `[LoggerMessage]`, never hand-rolled") and share both the `GeneratedCodeAnalysisFlags.None` guard and the `SharedKernel.Testing` namespace exemption. Do not split them into two classes purely to match the one-class-per-ID convention; do not add a third, unrelated diagnostic to this class either — the shared-class rationale is "same standard, same guards," not "convenient batching."
- `LoggingAuthoringStyleAnalyzer` MUST call `context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` (or an equivalent generated-file exclusion) before registering its syntax-node actions. This is load-bearing, not stylistic: `[LoggerMessage]`'s own source generator emits a partial-method body that internally calls `ILogger.Log` directly — without this guard, SK0020 would fire against the compiler-generated implementation of every correct `[LoggerMessage]` declaration platform-wide, defeating the rule's entire purpose.
- SK0020 requires `SemanticModel.GetSymbolInfo` on the invoked method to resolve `ContainingType` exactly (`Microsoft.Extensions.Logging.LoggerExtensions` for the six named extension methods; `Microsoft.Extensions.Logging.ILogger` + `Name == "Log"` for the interface-level call) — a syntax-only simple-name check on `LogInformation`/`LogWarning`/etc. was rejected because those names collide with unrelated logging frameworks (Serilog `ILogger`, NLog, custom wrapper types) commonly present in consuming microservices' own dependency trees. SK0021 is syntax-only by contrast — the qualified `LoggerMessage.Define*` call shape is specific enough that the domain's usual "escalate only when truly ambiguous" cost discipline (see SK0011, SK0015, SK0017–19 for the semantic-model precedents, and SK0703/SK0007 for the syntax-only precedents) favors the cheaper check here.
- `LoggingEventIdIntegrityAssertion` (WO-041 P-250) is the domain's second non-`ConditionList`/`ICustomRule` public helper, after `ApplicationPipelineRules.PipelineOrderAssertion` — both exist because their invariant ("EventId global uniqueness + range membership across every shipped assembly," "fixed pipeline registration order") has no single-assembly "fire on a contrived violating assembly" shape a `ConditionList` naturally expresses. Unlike every `ICustomRule` predicate in this domain, `LoggingEventIdIntegrityAssertion` does NOT use `NetArchTest.Types.InAssembly(...)` — it walks `Mono.Cecil` `ModuleDefinition.Types` and `TypeDefinition.NestedTypes` recursively by hand, specifically to avoid the SK0012-documented gap where NetArchTest's own type-discovery layer never surfaces compiler-generated or nested types to an `ICustomRule`. Do not "simplify" this helper to use `Types.InAssembly(...)` — doing so would silently reintroduce that exact blind spot for `[LoggerMessage]` methods declared inside nested logging-helper classes.
- `LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange` takes an `IReadOnlyDictionary<Assembly, (int RangeMin, int RangeMax)>` supplied entirely by the caller — `00.Governance` never references `SharedKernel.Primitives` (it references nothing). The consuming test project is responsible for building this dictionary from `SharedKernel.Primitives.Logging.LoggingEventIdRanges` (P-249) values, keeping that registry the single source of truth for range numbers while this helper stays dependency-free, matching the same "caller supplies the assembly, never hard-code a path" discipline used by every `ConditionList` factory method in this file.
- `LoggingEventIdIntegrityAssertion` is EXPECTED TO FAIL if pointed at the platform's real shipped assemblies as of this phase (2026-07-08) — the WO-041 audit's own confirmed collisions (`SharedKernel.Caching.Redis.Core` vs `SharedKernel.Caching.Redis.PubSub`, both 4001/4002; three internal `SharedKernel.Messaging.MassTransit` collisions) have not been retrofitted, and `01.Core`'s own `LoggingEventIdRanges` registry (P-249) is itself still `○` Pending. Design and tests for this phase use contrived in-memory multi-assembly Mono.Cecil fixtures only — do not attempt a real-assembly wiring pass until every WO-041 domain retrofit phase ships; track that as a follow-up, not a blocking condition on this phase.
- **Referencing a REAL net10.0-targeted assembly (third-party NuGet or in-repo `SharedKernel.*`) from an analyzer test, when the rule's own design requires exact assembly-identity resolution and a fixture stand-in cannot substitute (WO-079/P-486 SK0033, WO-076/P-476 SK0035):** `CSharpAnalyzerTest`'s built-in `ReferenceAssemblies` presets in this pinned `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` version (`1.1.2`) are all netstandard/older-.NET vintage — even `ReferenceAssemblies.Net.Net80`'s own `System.Runtime` reference is version `8.0.0.0`/`9.0.0.0`, lower than the exact `10.0.0.0` a net10.0-targeted assembly requires — producing a `CS1705` assembly-version-mismatch compiler error the moment such an assembly is added via `TestState.AdditionalReferences`. Two techniques, chosen by whether the real assembly ships a `netstandard2.0` asset: (a) if it does (SK0033's `AutoMapper`/`Riok.Mapperly.Abstractions`/`Microsoft.Extensions.DependencyInjection.Abstractions`) — load that specific asset directly from the local NuGet global-packages cache (honor `NUGET_PACKAGES`, fall back to `~/.nuget/packages`) rather than `typeof(X).Assembly.Location`, which resolves whichever TFM-specific asset NuGet selected for the TEST PROJECT itself (i.e. the conflicting net10.0 one); (b) if it does not (SK0035's real, net10.0-only `SharedKernel.DataPrivacy.dll`) — bypass `CSharpAnalyzerTest` entirely and build a raw `CSharpCompilation` + `Compilation.WithAnalyzers(...)` manually, supplying references from this TEST HOST PROCESS's own `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")` list (guaranteed net10.0-exact, since the host itself runs on net10.0) plus the real assembly's own `typeof(X).Assembly.Location` (safe here — no separately-versioned reference set is being mixed in). The same raw-`CSharpCompilation` technique, without any real-assembly reference at all, is also the correct way to run a syntax-only analyzer against a large real-source corpus for an empirical audit (SK0034's `RealSourceAudit_...` test, T-350) — only `typeof(object).Assembly.Location` is needed there, since a syntax-only analyzer needs no semantic resolution. `new ReferenceAssemblies("net10.0")` (requesting a TFM this testing package version predates) was tried and rejected — it silently resolves to an EMPTY reference set, not a NuGet-restored net10.0 set, producing far worse errors (`System.Object` itself unresolvable) than the `CS1705` it was meant to avoid.
- `LoggingAuthoringStyleAnalyzer`'s (SK0020/SK0021) test fixtures in `SharedKernel.Analyzers.Tests` deliberately do NOT reference the real `Microsoft.Extensions.Logging.Abstractions` NuGet package — `CSharpAnalyzerTest`'s default (older) reference-assembly set conflicts with a `net10.0`-targeted build of that package (`CS1705` `System.Runtime` version mismatch). Instead, each test compiles a self-contained in-compilation stub declaring the exact real namespace/type/member names the analyzer checks against (`Microsoft.Extensions.Logging.ILogger`, `LoggerExtensions`, `LoggerMessage`, `LoggerMessageAttribute`) — the same technique already used by SK0013's `IHttpClientFactory` fixture. By contrast, `LoggingEventIdIntegrityAssertionTests` in `SharedKernel.ArchitectureTests.Tests` DOES reference the real `Microsoft.Extensions.Logging.Abstractions` package directly for its `CompileInMemory` fixtures — no such conflict exists there because those fixtures are compiled and then loaded in-process via `Assembly.LoadFrom` against the current running runtime, never through the analyzer-testing framework's isolated reference-assembly sandbox. Any future SK analyzer whose test fixtures need a BCL-adjacent type from a versioned `Microsoft.Extensions.*` package should default to the in-compilation stub technique to avoid this class of reference-assembly conflict.
- SK0022 `CrossCuttingMagicStringLiteralAnalyzer` (WO-042 P-264) is the next sequential ID in the SK0001–SK00N general-purpose block (SK0021 was the prior ID). It is ONE `DiagnosticAnalyzer` class covering FOUR distinct call-site shapes (HTTP header indexer/setter, `Activity.SetBaggage`/`.SetTag`, `IConfiguration.GetSection`, `ClaimsPrincipal`/`Claim` comparison) under a SINGLE `DiagnosticDescriptor` — narrower than SK0020/SK0021's "one class, two IDs" precedent, here it is "one class, one ID, four trigger shapes," because all four encode the exact same underlying rule ("never a raw literal at a cross-cutting call site"), not four separate standards.
- SK0022's core discriminator is SYNTAX SHAPE, not resolved value or declaring-class identity: the checked argument/indexer-key position must be a `LiteralExpressionSyntax` of kind `StringLiteralExpression` to fire. Any other expression shape (`IdentifierNameSyntax`, `MemberAccessExpressionSyntax`, or anything else) passes automatically — the analyzer never inspects what a referenced field's value IS or which class declares it. This carries forward the acceptance-critical generality requirement first established for `NoBareHealthCheckLiteralWhereConstantsExistPredicate` (WO-028 P-178): the implementation must never contain a specific constants-class name as a string literal or type check. A domain-local constants class (`SecurityClaimTypes`, `WebhookSignatureHeaders`, `HubGroupNaming`) satisfies the rule exactly as well as a reference to `01.Core`'s `WellKnownHeaders`/`WellKnownBaggageKeys` — the analyzer cannot and does not distinguish them.
- Each of SK0022's four call-site shapes requires `SemanticModel.GetSymbolInfo` to resolve the receiver/method/indexer's exact `ContainingType` (`System.Net.Http.Headers.HttpHeaders`/`Microsoft.AspNetCore.Http.IHeaderDictionary`; `System.Diagnostics.Activity`; `Microsoft.Extensions.Configuration.IConfiguration`/`ConfigurationExtensions`; `System.Security.Claims.Claim`/`ClaimsPrincipal`/`ClaimsIdentity`) — a syntax-only simple-name check on method names like `SetTag`/`GetSection`/`FindFirst` was rejected as too collision-prone against unrelated types sharing those common names, the same discipline already applied to SK0020.
- SK0022 fires globally with NO namespace-scoped exemption (unlike SK0001/SK0007/SK0013/SK0020/SK0021's `SharedKernel.Testing`/other suppression-namespace pattern) — there is no legitimate namespace where a raw literal at one of these four call-site shapes should pass; a domain-local constants holder already satisfies the rule anywhere it is used, by construction (it is a reference, never a literal).
- `WellKnownConstantOwnershipAssertion` (WO-042 P-264) is the domain's THIRD non-`ConditionList`/`ICustomRule` public helper, after `ApplicationPipelineRules.PipelineOrderAssertion` and `LoggingEventIdIntegrityAssertion` — same rationale: "no other assembly may redeclare `01.Core`'s canonical cross-cutting literal values under a different name" is a cross-assembly, whole-platform invariant with no single-assembly "fire on one contrived violating assembly" shape. It extends `StringConstantsClassDetector`'s field-shape + literal-value resolution technique (WO-028 P-178) to walk every `TypeDefinition` in a scanned assembly, not only the `abstract sealed` "constants class" shape the original caller assumed — a stray `const`/`static readonly string` field on an ordinary class must be caught too.
- `WellKnownConstantOwnershipAssertion.AssertSoleDeclaration` takes a fully caller-supplied `canonicalValues` dictionary, `owningTypeFullNames` exclusion list, and `assembliesToScan` collection — `00.Governance` never references `SharedKernel.Primitives` (it references nothing). The consuming test project builds `canonicalValues` from the real `SharedKernel.Primitives.CrossCutting.WellKnownHeaders`/`WellKnownBaggageKeys` field values (P-259) once that package ships, matching the same "caller supplies the assembly/values, never hard-code them here" discipline established by `LoggingEventIdIntegrityAssertion` and every `ConditionList` factory method in this file.
- SK0022 and `WellKnownConstantOwnershipAssertion` were designed and tested against CONTRIVED in-memory Mono.Cecil fixtures only, consistent with every "designed-ahead-of-a-pending-dependency" precedent in this domain — at this phase's authoring (2026-07-14), only `01.Core`'s D-30 design for `WellKnownHeaders`/`WellKnownBaggageKeys` (P-259) was locked. **CORRECTION recorded at the SK.00.MagicStringGuard closeout (2026-07-16):** `01.Core` P-259 shipped the same day this phase was authored, and `P-260`/`P-261`/`P-262`/`P-263` (the consuming-domain retrofits) all landed by 2026-07-16 — every dependency this phase originally flagged as blocking real-assembly verification is now resolved. The real types live at `SharedKernel.Primitives.Propagation.WellKnownHeaders`/`WellKnownBaggageKeys` — **not** the `SharedKernel.Primitives.CrossCutting` namespace this phase's design prose (and the analyzer's own diagnostic message, and `WellKnownConstantOwnershipAssertion`'s XML doc examples) assumed; both were corrected to the real namespace during this closeout. A real-assembly wiring pass (a `SharedKernel.ArchitectureTests.Tests` project reference to the real `SharedKernel.Primitives.dll`, exercising `WellKnownConstantOwnershipAssertion.AssertSoleDeclaration` against every other shipped production assembly) is now genuinely unblocked but was NOT implemented as part of this phase — it is scope the phase spec explicitly deferred, not a gap in this closeout; track it as a candidate follow-up work order.
- **Empirically-verified SK0022 call-site-shape resolution (recorded during implementation, WO-042 P-264):** three of the four shapes' real BCL types (`System.Net.Http.Headers.HttpHeaders`/`HttpRequestHeaders`, `System.Security.Claims.Claim`/`ClaimsPrincipal`/`ClaimsIdentity`) compile and resolve correctly as-is inside `CSharpAnalyzerTest`'s default sandbox — no in-compilation stub is needed for these three, the same "already part of the default reference-assembly closure" precedent SK0013's real `System.Net.Http.HttpClient` fixture already established. `System.Diagnostics.Activity` is the ONE exception: the sandbox's default reference set resolves an old `System.Diagnostics.DiagnosticSource, Version=4.0.5.0` contract whose `Activity` type predates the `.SetTag`/`.SetBaggage` fluent overloads (added in .NET 5) — confirmed via a live `CS1061` compile error, not assumed. Adding a second, newer `System.Diagnostics.DiagnosticSource` reference via `TestState.AdditionalReferences` does NOT fix this — it produces a live `CS0433` "type exists in both assembly versions" ambiguity, since both the old (sandbox-default) and new (added) versions of the SAME-NAMED assembly are simultaneously on the reference list. The working fix is to REPLACE the entire reference set for just the Activity-shape tests via `test.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;` (a full modern framework closure with the old `DiagnosticSource` nowhere in it, rather than an addition alongside it) — confirmed green. Only `Microsoft.Extensions.Configuration.IConfiguration` and `Microsoft.AspNetCore.Http.IHeaderDictionary` needed an in-compilation stub (per the original phase-spec instruction) — both are genuinely external, separately-versioned/SDK-gated packages absent from the default sandbox closure entirely, so no stub-vs-real-type conflict was possible for either.
- SK0023 `NonSingletonAmazonS3ClientRegistrationAnalyzer` (WO-043 P-271) is the next sequential ID in the SK0001–SK00N general-purpose block (SK0022 was the prior ID) and the platform's first storage-domain diagnostic. It is the structural inverse of SK0703 `MessageBusSingletonRegistrationAnalyzer`: SK0703 flags `AddSingleton<IMessageBus>` because that type must be *scoped*; SK0023 flags `AddScoped<IAmazonS3>`/`AddTransient<IAmazonS3>` because that type must be *singleton*. Type-argument extraction reuses SK0703's exact technique (`GenericNameSyntax.TypeArgumentList.Arguments[0]` as an `IdentifierNameSyntax`, simple-name exact match) — syntax-only, no `SemanticModel`, covering both the one-argument factory form and the two-argument `TService,TImplementation` form.
- SK0023 fires globally with no suppression namespace, mirroring SK0703's/SK0014's "fires globally" convention — `Amazon.S3.IAmazonS3` must be a singleton wherever it is registered platform-wide, not only inside `SharedKernel.Storage.S3`/`SharedKernel.Storage.Obs`. A single narrow storage-domain rule does not warrant opening a new `08xx` ID block (the multi-rule `02xx`/`03xx`/`07xx` blocks exist for multi-tenancy/encryption/messaging subsystems with several related rules each) — SK0023 stays in the sequential general-purpose block, following the SK0011 (persistence)/SK0013 (communication) precedent that a lone domain-specific rule does not need its own block.
- `StorageTopologyRules` (WO-043 P-271, reworked P-559) introduces zero new SK diagnostic IDs, zero new Mono.Cecil technique, and zero new `ICustomRule` — its `ConditionList` rules are pure `NetArchTest` `.Should().NotHaveDependencyOn(...)` checks, mirroring `RedisTopologyRules`, and its two `*ForbiddenAssemblyReferences` methods read `Assembly.GetReferencedAssemblies()`, because the storage entry points share the `SharedKernel.Storage` namespace and a namespace check cannot see them. Since P-559 `.Obs` builds on `.S3` by design, so the sibling rule `ProviderPackagesNeverReferenceEachOther` was replaced by the one-way `S3NeverReferencesObs`.
- `StorageTopologyRules.OnlyProviderPackagesMayReferenceAmazonS3` carries NO internal namespace exemption — exclusion of `SharedKernel.Storage.S3`/`.Obs` is achieved entirely by the caller never passing either assembly to the factory method, the same caller-controlled-exclusion convention already established by `PresentationLayeringRules` and `CompositionRootExclusivityRules`. Document any future internal exemption here before adding one.
- **`StorageTopologyRules` real-assembly status — CORRECTED at implementation closeout (2026-07-18).** At this phase's authoring (2026-07-16), `08.Storage`'s own `state-map.md` showed every phase at `○`/empty and P-265/P-266/P-267 as not-yet-shipped, so the phase spec instructed CONTRIVED-fixtures-only design. By the time this phase was implemented (2026-07-18), `08.Storage` had independently reached Published — P-265/P-266/P-267 are all `●` Complete, and `SharedKernel.Storage.Abstractions`/`.S3`/`.Obs` exist as real, clean-building assemblies. The contrived in-memory fixtures (`CSharpCompilation` + `MetadataReference.CreateFromImage`, the `RedisTopologyRulesTests`/`CompositionRootExclusivityRulesTests` technique) remain the PRIMARY red/green proof, per the phase spec's own instruction — T-194–T-199 all use contrived fixtures. Real-assembly verification was ADDITIONALLY wired in this same phase (not deferred as a follow-up, since the dependency the phase spec flagged as blocking had already resolved): `SharedKernel.ArchitectureTests.Tests.csproj` gained test-only `ProjectReference`s (`PrivateAssets="all"`) to all three real `08.Storage` assemblies, and three `Real*`-suffixed tests confirm all three `StorageTopologyRules` factory methods pass against the shipped packages with zero discrepancy from the design-time contrived-fixture behavior.
- SK0024 `RawSearchFieldNameLiteralAnalyzer` and SK0025 `ObsoleteElasticsearchClientUsageAnalyzer` (WO-044 P-278) are the next two sequential IDs in the SK0001–SK00N general-purpose block (SK0023 was the prior ID) and the platform's first `09.Search`-domain diagnostics. Both require `SemanticModel` resolution — SK0024 the domain's eighth semantic-model analyzer (after SK0011, SK0015, SK0017–SK0019, SK0020, SK0022), SK0025 the ninth — because neither rule's discriminator is expressible as a safe syntax-only simple-name check without unacceptable false-positive risk (`OrderBy`/`Where`/`In`/`Exists` collide with LINQ; `ElasticClient`/`ConnectionSettings` are generic enough names to exist in unrelated libraries).
- SK0024 is the platform's first refactor-safety/`nameof()`-encouragement rule, distinct in INTENT from SK0022's cross-cutting-wire-contract magic-string prohibition even though both share the identical literal-vs-reference syntax-shape discriminator (`LiteralExpressionSyntax` of kind `StringLiteralExpression`, declaring-class-agnostic — a domain-local field-constants class satisfies the rule exactly as well as an inline `nameof(...)`). Do not merge SK0024 into SK0022's four call-site shapes or attempt to generalize SK0022 to cover it — SK0022 fires on genuinely cross-cutting/wire-contract literals (HTTP headers, OTel baggage, config sections, claim types), while SK0024's motivating hazard is the Meilisearch-visible/ElasticSearch-silent asymmetry specific to `09.Search`'s two-provider query surface, a different rationale that this file's own SK0022 entry does not and should not reference.
- SK0024's eleven recognized call-site shapes (six on `IQueryBuilder<TDocument>`/`SearchQueryBuilder<TDocument>`, five on `SearchFilter`'s static factories) are resolved via `SemanticModel.GetSymbolInfo` against `SharedKernel.Search.Abstractions`'s exact declaring types. CONFIRMED at implementation time (2026-07-24) directly against the real shipped source: `IQueryBuilder<TDocument>` lives at `SharedKernel.Search.Abstractions.Querying.IQueryBuilder` (`Querying/IQueryBuilder.cs`) and `SearchFilter` at `SharedKernel.Search.Abstractions.Models.SearchFilter` (`Models/SearchFilter.cs`) — both namespaces match the analyzer's hardcoded `QueryBuilderInterfaceFullName`/`SearchFilterFullName` constants exactly; `SearchQueryBuilder<TDocument>` (the concrete sealed implementor, `Querying/SearchQueryBuilder.cs`) is matched by name (`SharedKernel.Search.Abstractions.Querying.SearchQueryBuilder`) though no test fixture exercises it directly since application code invokes these methods through the `IQueryBuilder<TDocument>` interface returned by `SearchQuery.For<TDocument>()`, never through the concrete type. For the four `params string[]` shapes (`SearchingIn`, `Faceting`, `WithNumericFacetStats`, `Returning`), the analyzer walks every argument expression at that parameter position individually — both the multi-argument call form and any array/collection-expression form (`ArrayCreationExpressionSyntax`, `ImplicitArrayCreationExpressionSyntax`, and `CollectionExpressionSyntax` are all handled).
- `SearchTopologyRules` (WO-044 P-278) mirrors `StorageTopologyRules`'s structure and its documented `NotHaveDependencyOn` matching contract exactly (namespace `StartsWith`, no trailing dot, self-collision awareness) but is scoped to `09.Search`'s two SIBLING provider packages (`SharedKernel.Search.Meilisearch`/`.ElasticSearch`, mirroring `08.Storage`'s `.S3`/`.Obs` sibling-not-`.Core`-split precedent exactly, per `09.Search/CLAUDE.md`'s own explicit rejection of a `SharedKernel.Search.Core`). `.AbstractionsHasNoThirdPartyDependencies` carries a SIXTH forbidden term (`"Microsoft.Extensions"`) beyond `StorageTopologyRules`'s five-term analog, because `09.Search/CLAUDE.md` documents `SharedKernel.Search.Abstractions` as having a stricter dependency posture than `SharedKernel.Storage.Abstractions` — zero `PackageReference` of any kind, not even `Microsoft.Extensions.DependencyInjection.Abstractions` (which `SharedKernel.Caching.Abstractions` IS permitted). No new Mono.Cecil technique and no new `ICustomRule` — pure `NetArchTest` checks, zero new NuGet dependency.
- `SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts` (WO-044 P-278) is the first method on `SharedKernelLayeringRules` to return `ConditionList[]` instead of a single `ConditionList` — every sibling method on that class (`CoreReferencesNothing`, `ContractsReferencesOnlyCore`, etc.) predates the newer domain-boundary-rule-class convention (`RedisTopologyRules`/`CommunicationLayeringRules`/`StorageTopologyRules`) of returning one `ConditionList` per forbidden term for per-term failure-message granularity; this method deliberately follows that newer convention rather than the older single-`ConditionList` shape of its own siblings, because it is the first `SharedKernelLayeringRules` method checking against more than two or three forbidden terms (fifteen, one per every OTHER numbered domain's package family). Document any future simplification to a single `.NotHaveDependencyOnAny(string[])` call here before applying it — see the method's own entry above for the exact fallback condition.
- The fifteen-term forbidden list inside `SearchReferencesOnlyCoreAndContracts` is an EXPLICIT enumeration, not a derived/reflective one — per this file's own long-standing Implementation Rule ("Architecture tests in `SharedKernelLayeringRules` must mirror the layering table in the root `CLAUDE.md` exactly. If a new domain (folder XX) is added, the layering rules must be updated in the same PR"), a future `18.NewDomain` addition to the root `CLAUDE.md` Folder Map MUST append its package-family namespace term to this list in the SAME PR that adds the new domain, or this rule will silently under-enforce against the new domain the way it would against any of the fourteen domains already listed if one were accidentally omitted today. `"SharedKernel.AI"` (not `"SharedKernel.Intelligence"`) is the correct term for `10.Intelligence` — confirmed against the root `CLAUDE.md` Abstractions table (`SharedKernel.AI.Abstractions` / `.VectorDb`), a package-family-name-vs-folder-name mismatch worth flagging explicitly since it is the one term in the list that does not match its folder name.
- **Real-assembly status for `SK0024`/`SK0025`/`SearchTopologyRules`/`SearchReferencesOnlyCoreAndContracts` — CORRECTED at implementation closeout (2026-07-24).** At this phase's authoring (2026-07-19), `09.Search`'s own `state-map.md` showed the entire Design phase (D-01 through D-28, covering P-272/P-273/P-274) at `○` and only bare `.csproj` skeletons existed on disk for all three packages, so the phase spec instructed CONTRIVED-fixtures-only design. By the time this phase was implemented (2026-07-24), `09.Search` had independently reached Published — 139/139 tasks `●` across all six phases — and `SharedKernel.Search.Abstractions`/`.Meilisearch`/`.ElasticSearch` exist as real, buildable assemblies. Real-assembly verification was therefore wired in THIS phase per the phase's own GATING acceptance criterion, exactly mirroring the `SK.00.StorageTopology` precedent for the identical class of dependency-resolved-before-implementation finding: `SharedKernel.ArchitectureTests.Tests.csproj` gained three test-only `ProjectReference`s (`PrivateAssets="all"`) to the real `09.Search` assemblies, and `SearchTopologyRulesTests.cs` carries two `Real*`-suffixed tests confirming both `SearchTopologyRules` factory methods pass against the shipped packages — no discrepancy from the design-time contrived-fixture behavior was found. The six contrived-fixture fire/pass-path tests (T-208–T-211, plus one extra `Microsoft.Extensions`-specific fire-path case) remain the primary red/green proof, per the phase spec's own instruction that they "remain the primary red/green proof" even when real-assembly verification becomes possible.
- **SK0025 implementation-time correction (discovered while writing T-205/T-206, 2026-07-24).** The design-time Trigger prose (see the SK0025 diagnostic-registry entry's own correction note for the full account) additionally named `QualifiedNameSyntax`/`UsingDirectiveSyntax` as registered syntax kinds and did not anticipate two genuine false-positive sources: (1) a NAMESPACE symbol (the bare `"Nest"` segment of a `using` directive, or the left-hand side of a qualified name) ALSO carries a `ContainingAssembly.Name` equal to the deprecated package's assembly name in Roslyn's symbol model — an `ITypeSymbol` filter is required, or the rule fires on every `using` directive and qualified-name prefix in addition to the real type reference; (2) the `"var"` contextual keyword in an implicitly-typed local declaration is itself an `IdentifierNameSyntax` whose `GetSymbolInfo` resolves to the INFERRED type — an explicit `Identifier.ValueText == "var"` guard is required, or the rule double-fires on every `var client = new ElasticClient();`-shaped statement. `SK0025_ObsoleteElasticsearchClientUsageAnalyzerTests.cs` is the first analyzer test file in this project needing a genuinely separate compiled reference assembly with a specific `AssemblyName` (via `Microsoft.CodeAnalysis.Testing.SolutionState.AdditionalProjects`, keyed by an assembly name of literally `"NEST"`/`"Elasticsearch.Net"`/`"Elastic.Clients.Elasticsearch"`) rather than the established in-compilation-stub technique (SK0013/SK0017/SK0020-22/SK0024) — an in-compilation stub resolves to the TEST ASSEMBLY's own name, never to the literal deprecated-package assembly name `SK0025`'s `ContainingAssembly.Name` check actually compares against. `Microsoft.CodeAnalysis.Testing.ProjectState`'s `AssemblyName` property is read-only and always equals its `Name` constructor argument — confirmed empirically via a scratch reflection probe before use, not assumed from undocumented API shape.
- SK0026 `RawIntelligenceProviderClientConstructorInjectionAnalyzer` and SK0027 `RawIntelligenceIdentifierLiteralAnalyzer` (WO-045 P-286) are the next two sequential IDs in the SK0001–SK00N general-purpose block (SK0025 was the prior ID) and the platform's first `10.Intelligence`-domain diagnostics. Both require `SemanticModel` resolution — SK0026 exact-full-type-name resolution (not SK0013's syntax-only simple-name check) because `Microsoft.SemanticKernel.Kernel`'s simple name `"Kernel"` is highly collision-prone; SK0027 for the same reason SK0024 does (its eight call-site method names are common enough to collide with unrelated types without an exact-declaring-type check). SK0026 is this domain's tenth semantic-model analyzer (after SK0011, SK0015, SK0017–SK0019, SK0020, SK0022, SK0024, SK0025); SK0027 is the eleventh.
- SK0026's namespace exemption is per-client-type, not a single shared prefix — a `QdrantClient` parameter is exempt only inside `SharedKernel.AI.Qdrant`, `MilvusClient` only inside `SharedKernel.AI.Milvus`, `Kernel` only inside `SharedKernel.AI.SemanticKernel`. There is no cross-exemption between the three owning packages; a `QdrantClient` parameter injected inside `SharedKernel.AI.Milvus` still fires SK0026, since that would itself be exactly the sibling-package violation `IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther` independently catches at the assembly level — the two rules are complementary, not redundant.
- SK0027 is structurally parallel to SK0024 (identical literal-vs-reference syntax-shape discriminator, declaring-class-agnostic) but its motivating hazard must never be described as the same as SK0024's or cross-referenced against it — the same discipline already established between SK0022 and SK0024. SK0024's hazard is an engine-VISIBILITY asymmetry; SK0027's hazard is that a `VectorCollectionDefinition.Create`/`.EmbeddingModel` call site and the record/query call sites supplying the same `embeddingModelId` string have no shared compile-time link, so a copy-pasted typo passes the model-identity guard cleanly on BOTH providers — see the SK0027 diagnostic-registry entry for the full rationale.
- SK0027's eight call-site shapes cover `VectorFilter`'s five static factories (field always position 0, the structural twin of SK0024's five `SearchFilter` shapes), `IVectorCollectionProvisioner`'s three `collectionName`-taking members, and BOTH string parameters of `VectorCollectionDefinition.Create` plus both single-string-parameter members of `VectorCollectionDefinitionBuilder`. `VectorCollectionCutoverRequest.StagingCollectionName`/`.LiveCollectionName` are object-initializer property assignments, not method-call arguments, and are explicitly OUT OF SCOPE for this rule's argument-position detection technique — the same documented method-call-only limitation already carried by SK0024.
- `IntelligenceTopologyRules` (WO-045 P-286) mirrors `StorageTopologyRules`/`SearchTopologyRules`'s structure and `NotHaveDependencyOn` matching contract exactly but is scoped to THREE sibling provider packages, not two, per `10.Intelligence/CLAUDE.md`'s explicit statement that "no `.Core` is extracted ... duplication ... is deliberate, mirroring the `09.Search` precedent exactly." `.AbstractionsHasNoThirdPartyDependencies` carries an EIGHT-term forbidden list (two more than `SearchTopologyRules`'s six) — three bare-prefix third-party-SDK terms and three `"SharedKernel.AI.{Provider}"` sibling-package terms, instead of two of each, plus `"SharedKernel.Configuration"` and `"Microsoft.Extensions"` carried over unchanged. No new Mono.Cecil technique and no new `ICustomRule` — pure `NetArchTest` checks, zero new NuGet dependency.
- `IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther` is the FIRST three-named-Assembly-parameter shape in this domain — every prior sibling-non-reference predicate uses exactly two named parameters, since every prior sibling-provider domain (08.Storage, 09.Search) has exactly two providers. Returns a SIX-element `ConditionList[]`; no `Dictionary<string,string[]>` lookup table is needed since none of the three identifying namespaces is a prefix of another.
- `IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages` deliberately uses the NARROW term `"Microsoft.Extensions.Diagnostics.HealthChecks"`, not the bare `"Microsoft.Extensions"` prefix `.AbstractionsHasNoThirdPartyDependencies` uses — the three provider packages legitimately need other `Microsoft.Extensions.*` packages for DI wiring; only `SharedKernel.AI.Abstractions` carries the zero-`Microsoft.Extensions`-anything posture. This is the first `*TopologyRules` class in this domain to need a rule DISTINCT from its `AbstractionsHasNoThirdPartyDependencies` sibling for a narrower, cross-package term — `StorageTopologyRules`/`SearchTopologyRules` never needed one because neither of those domains' phase inputs named a specific forbidden-dependency rule beyond the Abstractions-purity and sibling-non-reference checks.
- `SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts` follows `SearchReferencesOnlyCoreAndContracts`'s `ConditionList[]`-per-forbidden-term convention with the SAME fifteen-term count — `"SharedKernel.Search"` swapped IN (09.Search is now forbidden for 10.Intelligence) and `"SharedKernel.AI"` swapped OUT (10.Intelligence is the domain under test, excluded by omission). A future `18.NewDomain` addition MUST append its package-family term to BOTH this method's list AND `SearchReferencesOnlyCoreAndContracts`'s list (and any future sibling of this shape) in the SAME PR that adds the new domain.
- Real-assembly status for SK0026/SK0027/`IntelligenceTopologyRules`/`IntelligenceReferencesOnlyCoreAndContracts` — UNVERIFIABLE at authoring time (2026-07-21). Unlike most "designed-ahead-of-a-pending-dependency" precedents in this domain, the WO-045 phase input's own acceptance criteria make real-assembly verification a GATING condition on this phase's completion, mirroring `SK.00.SearchTopology`'s precedent. `10.Intelligence/CLAUDE.md` states explicitly: "No production `.cs` file has been written yet" — the `SharedKernel.AI.Abstractions` interface contract is locked (P-279's Design phase, `10.Intelligence/CLAUDE.md`'s own Interface Contracts section), but its Scaffold/Core/Tests/Docs/Published phases, and all of P-280/P-281/P-282, are `○ Not started` per `10.Intelligence/state-map.md`. Design, implementation, and initial tests for this phase use CONTRIVED in-memory assemblies via `CSharpCompilation` + `MetadataReference.CreateFromImage` (the `RedisTopologyRulesTests`/`StorageTopologyRulesTests`/`SearchTopologyRulesTests` technique); the real-assembly re-verification pass MUST be completed, and its own acceptance-criterion checkbox explicitly closed, before this phase can be marked fully `●` complete.
- **`SK.00.IntelligenceTopology` — CORRECTED at implementation closeout (2026-07-27, WO-048 Milvus retraction).** Every bullet above this one describing SK0026/`IntelligenceTopologyRules` was drafted (WO-045 P-286, 2026-07-21) against a THREE-provider design. Before this phase's own implementation session, arch-lead ratified WO-048: `SharedKernel.AI.Milvus` was permanently retracted — `Milvus.Client` never shipped a stable release — and does not exist on disk and never will (verified directly: no `10.Intelligence/SharedKernel.AI.Milvus/` directory; `IntelligenceWellKnown` carries no `MilvusProviderName`). The SHIPPED implementation is adapted to the real two-provider (`SharedKernel.AI.Qdrant`/`.SemanticKernel`) reality: SK0026 resolves exactly TWO full type names (`Qdrant.Client.QdrantClient`, `Microsoft.SemanticKernel.Kernel`) with two owning-package exemptions, not three; `IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies` carries SIX forbidden terms, not eight (the two Milvus terms are absent); `.ProviderPackagesNeverReferenceEachOther` is the TWO-named-Assembly-parameter form returning a TWO-element `ConditionList[]`, mirroring `StorageTopologyRules`/`SearchTopologyRules` exactly, not the three-named-parameter/six-element form drafted above; `.NoHealthChecksDependencyAcrossIntelligencePackages` is unchanged in shape, only its caller-supplied assembly list shrinks from three to two providers. `SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts` is UNAFFECTED — its fifteen-term list never named Milvus and is implemented exactly as originally documented. SK0027 is likewise UNAFFECTED — it targets `SharedKernel.AI.Abstractions` call sites only, with no Milvus dependency. Real-assembly verification (the GATING acceptance criterion the bullet above states MUST be completed) is DONE as of this closeout: `10.Intelligence` reached Published (all six phases `●`, WO-048) before this phase's implementation session, so `SharedKernel.ArchitectureTests.Tests.csproj` gained test-only `ProjectReference`s to the three real, shipped assemblies (no Milvus reference exists to add), and `IntelligenceTopologyRulesTests.cs` carries three `Real*`-suffixed tests confirming all three factory methods pass against the shipped packages — no real violation surfaced. Every bullet above (and the SK0026/`IntelligenceTopologyRules` diagnostic-registry/architecture-test-contract entries earlier in this file) is intentionally left as originally drafted rather than rewritten in place, matching this domain's established "annotate, never silently rewrite" convention (see the SK0025/`StorageTopologyRules`/`SearchTopologyRules` CORRECTED-note precedents).
- SK0028 `NonDeterministicApiUsageInsideWorkflowAnalyzer` and SK0029 `RawTemporalClientConstructorInjectionAnalyzer` (WO-046 P-290) are the next two sequential IDs in the SK0001–SK00N general-purpose block (SK0027 was the prior ID) — the platform's first `17.Workflows`-domain diagnostics, and, following the `SK0011`/`SK0013`/`SK0023` precedent that a domain's diagnostics stay in the sequential block rather than opening a new per-domain range, no `17xx` block is opened. SK0028 is this domain's twelfth semantic-model analyzer; SK0029 is the thirteenth.
- SK0028 is the first analyzer in this domain to scope its trigger by TYPE ATTRIBUTION/INHERITANCE (`[Workflow]` attribute or `WorkflowBase` in the base-type chain) rather than the established namespace-ancestor `SyntaxNode.Parent` walk every prior namespace-scoped SK analyzer uses (SK0001/SK0007/SK0013/SK0016/etc.). The `ActivityBase`/`[Activity]` exclusion is checked FIRST and is a hard, positive exclusion — not merely "outside the trigger-in scope" — because every one of SK0028's seven forbidden shapes is not just tolerated but in some cases MANDATORY inside an activity (`IClock`/`ILogger<T>` injection, per `SK0001`). This is the platform's first analyzer explicitly encoding a per-API-shape INVERSION of another rule's own trigger condition (SK0001 mandates `IClock`; SK0028 bans it, in the one narrow context where mandating it would be wrong).
- SK0028's seven forbidden shapes are fixed by WO-046's own acceptance criteria: the four-property clock set (reusing `DoesNotCallSystemClockPredicate`'s exact property list), `Guid.NewGuid()`, `new Random()`, the `Task.Run`/`Task.Delay`/`ConfigureAwait(false)` scheduler-escape trio, `Environment.*`/`File.*` member access, and constructor-injected `IClock`/`ILogger<T>`. `HttpClient` and general I/O are documented `17.Workflows/CLAUDE.md` Hard Violations too but are deliberately NOT added to this rule's trigger set — do not silently extend SK0028's shape list without a governance review; a future extension is a candidate follow-up, not an oversight to "complete" here.
- SK0029 mirrors SK0013's single-shared-namespace-exemption shape (`SharedKernel.Workflows.Temporal` prefix, one term) rather than SK0026's per-client-type/per-owning-package exemption mapping (three terms) — because 17.Workflows has exactly one owning package, not three sibling providers. Do not generalize SK0029's exemption to a per-client-type dictionary unless 17.Workflows itself ever splits into sibling packages.
- `WorkflowTopologyRules` (WO-046 P-290) is the first `*TopologyRules` class in this domain scoped to a domain with NO sibling provider packages — it therefore has no `AbstractionsHasNoThirdPartyDependencies`/`ProviderPackagesNeverReferenceEachOther` pair (there is nothing to split or compare). Its two methods (`NoRawClientAccessorConsumptionInRepo`, `NoHealthChecksDependencyInWorkflows`) are each single-package analogues of concerns those sibling-domain classes address differently — do not add an `AbstractionsHasNoThirdPartyDependencies`-shaped method to this class unless `SharedKernel.Workflows.Temporal` is ever split into an `.Abstractions` + `.Temporal` pair; until then, third-party-dependency purity for this package is `SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication`'s job (capability-domain terms) plus `NoHealthChecksDependencyInWorkflows` (the one specific third-party-package prohibition 17.Workflows/CLAUDE.md names explicitly) — there is no general "only `Temporalio.*` third-party packages allowed" check, since NetArchTest's dependency scan cannot cheaply distinguish "an intentional `Temporalio.*` reference" from "an intentional `Microsoft.Extensions.Hosting` reference" without an exhaustive allow-list this phase does not attempt.
- `NoRawClientAccessorConsumptionPredicate` checks constructor-parameter and field TYPE NAMES ONLY (exact simple-name match on `"ITemporalRawClientAccessor"`) — it does not, and structurally cannot, verify whether the consuming microservice's own composition root called `.AllowRawClientAccess()` first. That correlation is out of scope for a platform-level architecture test (it would require inspecting a consuming microservice's `Program.cs`, which is never one of the assemblies this repo builds) — the phrase "no in-repo type consumes it" in `17.Workflows/CLAUDE.md` is read literally: this rule guarantees the SharedKernel mono-repo's OWN packages never depend on the accessor, not that every downstream consumer's opt-in is well-formed.
- `SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication` is the first layering-boundary method on that class permitting THREE upstream domains (`01.Core`, `04.Contracts`, `05.Application`) rather than two — its forbidden-term list is therefore FOURTEEN terms, not the fifteen `SearchReferencesOnlyCoreAndContracts`/`IntelligenceReferencesOnlyCoreAndContracts` each use, with `"SharedKernel.Application"` the one term deliberately absent that appears in both of those sibling lists. Any future layering-boundary method for a domain permitted more than two upstream references should follow this same reduced-forbidden-list pattern rather than over-including a permitted term by copy-paste from the fifteen-term precedent.
- Real-assembly status for SK0028/SK0029/`WorkflowTopologyRules`/`WorkflowsReferencesOnlyCoreContractsAndApplication` — UNVERIFIABLE at authoring time (2026-07-22), and, following the `SK.00.SearchTopology`/`SK.00.IntelligenceTopology` precedent (not the older non-blocking-follow-up precedent), real-assembly verification is a GATING condition on this phase's completion. `17.Workflows/state-map.md` shows its entire Design/Scaffold/Core phases at `○` as of this phase's authoring — no production `.cs` file has shipped for `SharedKernel.Workflows.Temporal`. Design, implementation, and initial tests for this phase use CONTRIVED in-memory assemblies via `CSharpCompilation` + `MetadataReference.CreateFromImage` (the `RedisTopologyRulesTests`/`StorageTopologyRulesTests`/`SearchTopologyRulesTests`/`IntelligenceTopologyRulesTests` technique); the real-assembly re-verification pass MUST be completed, and its own acceptance-criterion checkbox explicitly closed, before this phase can be marked fully `●` complete.
- SK0030 `ResultOutcomeDiscardedAnalyzer` (WO-049 P-299) is the next sequential ID in the SK0001–SK00N general-purpose block (SK0029 was the prior ID). It registers on exactly two `SyntaxKind`s — the `ExpressionStatement`'s `InvocationExpressionSyntax`/`AwaitExpressionSyntax` child shapes — and deliberately never inspects `AssignmentExpressionSyntax` at all. This single structural exclusion is what makes BOTH the "assigned to a variable/field" pass case and the "explicit discard `_ = ...`" pass case fall out for free, with zero `SemanticModel.GetSymbolInfo`/`IDiscardSymbol` check needed — cheaper and simpler than a naive first design pass (which would otherwise need to distinguish a true compiler discard from a real local variable literally named `_`) would produce.
- SK0030's interface match uses the same "simple name + `ContainingNamespace` prefix" discriminator established by SK0017–SK0019 (`"IHasSuccessFlag"` + `"SharedKernel.Primitives"` prefix, matching the real type's actual home at `SharedKernel.Primitives.Results.IHasSuccessFlag` — confirmed by reading the shipped `01.Core` source, not assumed) rather than an exact-assembly `INamedTypeSymbol` identity check, for the same reason SK0017–SK0019 chose it: analyzer test fixtures can declare a fixture-local `IHasSuccessFlag`-named interface inside a matching-namespace code block in the SAME test compilation, with no `ProjectReference` to the real `SharedKernel.Primitives` assembly required for fire/pass-path unit tests. The check walks `INamedTypeSymbol.AllInterfaces` (plus the resolved type itself) — never a `BaseList`/syntax-only check — since `Result`/`Result<T>` and any consumer-defined `IHasSuccessFlag` implementor may satisfy the interface transitively.
- For the `AwaitExpressionSyntax` shape, `SemanticModel.GetTypeInfo(awaitExpressionSyntax)` already returns Roslyn's own unwrapped "what does `await x` evaluate to" type — no manual `Task<T>`/`ValueTask<T>` unwrapping logic is written or needed. This is what makes `await FooAsync();` as a bare statement (where `FooAsync` returns `Task<Result<T>>`/`ValueTask<Result<T>>`) resolve directly to `Result<T>` and correctly FIRE — the documented edge case explicitly called out by this phase's own acceptance criteria, and the CS4014 analogy one level further down: an awaited-but-unobserved `Result` outcome is exactly as dangerous as an unawaited `Task`.
- SK0030 only ever inspects the OUTERMOST `ExpressionStatementSyntax.Expression`'s resolved type — never a nested sub-expression's type. This single design choice is what makes "passed as an argument" (`Bar(Foo());`, outer type is `Bar`'s return type) and "receiver of a member-access/method chain" (`Foo().Match(...);`, outer type is `Match`'s return type) both pass without any special-case logic — while a fluent chain whose OUTERMOST call still resolves to an `IHasSuccessFlag`-implementing type (`Foo().Map(x => x + 1);`, where `Map` itself returns `Result<TNew>`) correctly still FIRES, since the chain's final produced `Result` is genuinely, separately discarded. This nuance is documented in the analyzer's own XML doc and proven by an explicit fire/pass test pair so a future reader does not "fix" the fire case as a false positive.
- SK0030 deliberately does NOT register on `ObjectCreationExpressionSyntax` (`Result`/`Result<T>` in this platform are produced exclusively via static factory methods — `Result.Success()`/`Result<T>.Failure(...)` — never a public constructor call) or on `ConditionalAccessExpressionSyntax` (`maybeService?.ReturnsResult();`). Both are documented, deliberate scope limitations for this phase — accepted false-negative risk, mirroring this domain's established `SK0708`/`HealthCheckTagIntegrityRules` "document the limitation, revisit only on a real finding" discipline — not oversights.
- SK0030 has NO `SharedKernel.ArchitectureTests` counterpart, the first single-analyzer phase in this file where that is explicitly by design rather than a pending gap. Unlike every `*TopologyRules`-style phase, a bare-statement `Result` discard is inherently a per-syntax-tree, per-compilation-unit concern the Roslyn analyzer already resolves completely inside each consuming project's own build — there is no assembly-dependency-graph or IL-level aspect to this rule an `ICustomRule`/`ConditionList` predicate could usefully add.
- SK0030 is the platform's first analyzer whose "GATING, not deferred" verification pass audits ALREADY-SHIPPED code across other domains (`05.Application.Behaviors`, `06.Persistence.EfCore`, `07.Messaging.MassTransit`, `17.Workflows.Temporal` — all four already Published as of this phase's authoring, 2026-07-27) rather than waiting on a not-yet-implemented dependency, unlike every `*TopologyRules` "designed ahead of a pending phase" precedent in this file (`SearchTopologyRules`, `IntelligenceTopologyRules`, `WorkflowTopologyRules`). Because this domain's own Test Rules forbid compiling real source files directly ("never test analyzers by compiling real source files manually" — `CSharpAnalyzerTest`/inline-markup only), the audit is executed as a manual/tool-assisted review: grep/read each domain's shipped `Result`/`Result<T>`-consuming call sites, catalog every DISTINCT real consumption shape encountered, then encode each shape as a representative (paraphrased, not literally copy-pasted) pass-path fixture. If the audit finds a real call site structurally matching SK0030's fire condition, that is a genuine, previously-invisible defect in the OWNING domain's shipped code, not a false positive to "fix" by weakening the rule — record it as a new finding and as a candidate follow-up work order in the owning domain, per this agent's jurisdiction boundary (`00.Governance` never implements another domain's production code).

- SK0033 `ReflectionBasedObjectMapperUsageAnalyzer` (WO-079 P-486) is a REDIRECT, not a straight accept, of the proposal that prompted it — `arch-lead` declined to ship a `SharedKernel.Mapping` package (wrapping Mapperly, a compile-time source generator, behind a kernel-owned runtime interface would defeat the entire reason to choose it over AutoMapper) and instead asked this domain to mechanize only the platform-wide prohibition half of the decision. It resolves all three trigger shapes by `ContainingAssembly.Name == "AutoMapper"` exact match (SK0025's technique), never a syntax-only simple-name check, because "Profile" is common enough elsewhere in this codebase (and in consuming services) to make a bare BaseList name match unsafe. Mapster is explicitly NOT enforced — its runtime and source-generated adapter call syntax is indistinguishable, and this domain's established convention is to narrow scope rather than ship a rule with an uncontrolled false-positive rate (see the SK0033 diagnostic entry's own Limitation). No new `SharedKernel.Mapping`/`.Mapper` package exists anywhere in this repo as a result of this phase, per its own acceptance criteria. Ungated — needs no compiled reference to any not-yet-shipped SharedKernel package, only a test-only `PackageReference` to the real `AutoMapper`(`15.1.1`, patched past the disclosed GHSA-rvv3-g6hj-g44x DoS advisory)/`Riok.Mapperly`(`3.6.0`) NuGet packages for fixture compilation — all five tests run against the real compiled packages (see the net10.0-real-assembly-reference Implementation Rules entry above for the technique this required).
- SK0034 `AmountCurrencyPairAdvisoryAnalyzer` (WO-066 P-442) introduces this registry's first ADVISORY category — a rule with NO escalation path to Error, ever, by design, distinct from SK0006/SK0007's "Warning pending future escalation to Error" shape. This is a deliberate judgment call, not a mechanical default: the phase input itself framed the rule as inherently heuristic and asked for it to ship advisory-only, and this domain's own review found the detection technique (a closed suffix-list co-occurrence check on ONE type's direct members) narrow enough to be worth shipping rather than declining outright — unlike the four capabilities `arch-lead` itself already declined earlier this session on non-mechanical-detectability grounds. The suffix list (`Amount`/`Price`/`Total`/`Balance` for the decimal side, `Currency`/`CurrencyCode` for the string side) is syntax-only (`PredefinedTypeSyntax` match) — no SemanticModel needed, since `decimal`/`string` are BCL keyword types resolvable from syntax alone. Implementation empirically validated the false-positive rate (T-350) via a raw `CSharpCompilation`+`WithAnalyzers` scan of every `.cs` file in this repository's numbered domains (excluding test/sample/generated paths) — ZERO diagnostics, no genuine false positive, so the suffix list ships UNNARROWED exactly as specified; see the SK0034 diagnostic entry's own Limitation for the full result and the re-run instruction. Depends on `03.Domain` P-439 (`Money`) only for the REMEDIATION MESSAGE to name a real, shipped type — the detection logic itself references no compiled `SharedKernel.Domain` type and ran against this repo's sources (predating `Money`'s own shape check) without needing it.
- SK0035 `UnmaskedClassifiedDataLoggingAnalyzer` (WO-076 P-476, retargeted P-554) is deliberately the ONE piece of `01.Core`'s `SharedKernel.DataPrivacy` story this domain mechanically enforces — the rest (taxonomy, masking-helper correctness, data-subject-request handling) stays a documented convention, per this domain's "decline unenforceable rules rather than ship weak ones" precedent. Since P-554 it follows Microsoft's compliance model: "classified" means an attribute deriving (at any depth) from `Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute`, never `NoDataClassificationAttribute`; a classified `[LoggerMessage]` parameter (and, for whole objects, a `[LogProperties]` parameter) is safe because log redaction masks it. Every type is resolved by FULLY-QUALIFIED METADATA NAME rather than a compiled `ProjectReference`, the technique WO-040/P-248's marker-interface rules (SK0017–SK0019) established, so contrived-fixture tests declare stand-ins inside the test compilation; one real-assembly test (a raw `CSharpCompilation`+`WithAnalyzers` run, per the net10.0-real-assembly-reference Implementation Rules entry above) locks the names to the compiled `SharedKernel.DataPrivacy`/`Microsoft.Extensions.Compliance.Abstractions` types. Composes with, but must never be described as duplicating, SK0022 (HTTP headers/Activity baggage/IConfiguration/ClaimsPrincipal, never LoggerMessage arguments) and SK0020/SK0021 (the LOGGING CALL'S OWN SHAPE) — SK0035 inspects argument PROVENANCE at a logging call site.
- SK0036 `RawRpcExceptionConstructionAnalyzer` (WO-074 P-469) is UNGATED despite the root state-map's own "Depends on: P-468" framing — re-verified directly against `14.Presentation/state-map.md` per this domain's now nine-times-repeated "confirm, never assume a stated dependency actually blocks this phase's own work" discipline (`SK.00.SenderConstrainedCredentialGuard` through `SK.00.CacheEncryptionAndRedisValidationLock`): the analyzer resolves `Grpc.Core.RpcException`/`Grpc.Core.Status` by exact semantic-model type match, needing only the standalone `Grpc.Core.Api` NuGet package (already independently available, containing solely these two types, not the full `Grpc.AspNetCore` server hosting stack `SharedKernel.Presentation.Grpc` itself will pin) as a test-only `PackageReference` — no compiled reference to `SharedKernel.Presentation.Grpc` is needed for the analyzer, its exemption-namespace check (a plain string-prefix match), or any of its contrived fire/pass-path fixture tests. Unlike SK0013's precedent (where "HttpClient" was judged sufficiently unique as a bare simple name), this rule uses full semantic-model type resolution from the start — "Status" is exactly the kind of dangerously generic simple name SK0026's "Kernel" lesson warned against defaulting to syntax-only for. No Cross-Domain Dependencies entry is added for this phase — there is genuinely nothing pending it.

---

## WO-026 Governance Conventions

> These conventions encode architectural decisions made in WO-026. They are the authoritative reference for the patterns established in that work order. The root CLAUDE.md "What Goes Where" table should be updated via `/sync-brain` to reflect these entries.

### Cross-Service DTO Boundary Mapping

- **There is no response-wrapper DTO.** `04.Contracts` ships no success/error envelope for HTTP or service-to-service results. An HTTP success body is the value itself; an HTTP failure body is always RFC 9457 `ProblemDetails`. A `Result<T>` never crosses a process boundary as a serialized object — `ContractsPurityRules.ContractsAssembliesHaveNoResultTypeOnPublicSurface` keeps it off every public property and field of a contracts assembly.
- **Inbound (producing service):** `Result<T>` → HTTP goes through `14.Presentation`'s typed results only — `ResultHttpExtensions` (`ToOk`, `ToCreated`, `ToOkWithETag`, `ToNoContent`, `ToErrorResult`, …), for minimal APIs and MVC controllers alike (P-562; `ToProblemDetailsResult` was deleted, and the final review's R19 deleted the MVC `ToActionResult` family). Inline `IsSuccess`/`IsFailure` branching before returning an HTTP result type outside `SharedKernel.Presentation.WebApi` is a platform violation, mechanically enforced by `PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi`; hand-rolled `ProblemDetails` construction is caught by `PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi`.
- **Outbound (calling service):** a response is mapped back to `Result<T>` through `11.Communication.Rest`'s `ReadResultAsync<T>`, which reads the value on success and the `ProblemDetails` body on failure. Never deserialize a response into an ad hoc `{ isSuccess, value, error }` shape — a second format would break `ReadResultAsync<T>` for every other caller.
- **Integration events:** the one cross-service event wire format is `04.Contracts`' `EventEnvelope<TEvent>` (CloudEvents 1.0), created only through `EventEnvelope.Wrap(...)` — it has no public constructor or setter, so the compiler enforces this and no architecture test is needed. Each concrete event carries `[IntegrationEvent("name", Version = n)]`, checked at compile time by SK0038/SK0039.

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
- [2026-07-03] SK.00.MetricsOutcomeTagAndMisregistrationGuard → ● closeout — SK0014 ClosedGenericResiliencePipelineRegistrationAnalyzer, SK0015 StreamPipelineBehaviorMisregistrationAnalyzer, and SK0016 RequestTypeShortNameUsageAnalyzer implemented in SharedKernel.Analyzers/Diagnostics/; RequestDurationRecordMissingOutcomeTagPredicate implemented in Predicates/ and MetricsInstrumentationRules in Rules/; verified against the pre-written CLAUDE.md spec (diagnostic registry, architecture test contracts, implementation rules all matched) with one correction made to the implementation to match the documented contract exactly — the predicate now requires MethodReference.DeclaringType to be a GenericInstanceType whose ElementType.FullName == "System.Diagnostics.Metrics.Histogram`1" (narrowed from an initial Name.StartsWith("Histogram") heuristic) before scanning for the companion "outcome" Ldstr literal; SK0015's implementation required one fix beyond the written spec — INamedTypeSymbol.AllInterfaces returns empty for an unbound generic type symbol (the shape produced by typeof(StreamFixtureBehavior<,>)), so the interface-implementation check walks type.OriginalDefinition.AllInterfaces instead, confirmed via a standalone Roslyn symbol-inspection script before patching; T-161–T-168 added (8 new tests: 4 analyzer fire/pass pairs, 2 architecture-test fire/pass fixtures); 105/105 SharedKernel.Analyzers.Tests and 127/127 SharedKernel.ArchitectureTests.Tests pass, 0 build warnings/errors (state-map-phase)
- [2026-07-03] SK0014 ClosedGenericResiliencePipelineRegistration, SK0015 StreamPipelineBehaviorMisregistration, SK0016 RequestTypeShortNameUsage added to diagnostic registry (general-purpose sequential block, next after SK0013; SK0014 and SK0016 syntax-only, SK0015 the domain's second semantic-model analyzer after SK0011); MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag and RequestDurationRecordMissingOutcomeTagPredicate added to architecture test contracts (no new SK ID — reuses the HealthCheckTagIntegrityRules Ldstr literal-collection technique against a new Histogram<T>.Record call-site search); five new implementation rules added; addresses all four WO-038 application audit findings (closed-generic ResiliencePipeline<TResponse> registration, missing outcome tag on RequestDuration, IStreamPipelineBehavior misregistration against IPipelineBehavior<,>, typeof(TRequest).Name short-name collision risk); the outcome-tag rule's real-assembly retrofit of MetricsBehavior<,> (P-217) is explicitly OUT OF SCOPE for this domain (production code in 05.Application) — tracked as a companion 05.Application dependency, not implemented here; design/tests use contrived in-memory fixtures only — WO-038 P-235, depends on 05.Application P-217/P-234 for real-assembly verification only (governance-arch-planner)
- [2026-07-03] WO-039 P-240 PLANNED (design only — not yet implemented) — the phase spec proposes registering SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher's MakeGenericMethod call site (inside PublishSingle's ConcurrentDictionary.GetOrAdd factory delegate) in ReflectionExemptionRegistry, pre-emptively, so SK0012 can be safely pointed at the real SharedKernel.Application assembly; documented that the call site sits inside a closure-free `static` lambda, so the Mono.Cecil-observed key will be the compiler-generated `<>c` nested cache class, not the literal MediatRDomainEventDispatcher/PublishSingle pair — new reusable implementation-rules bullet added on this Roslyn/Mono.Cecil fact; corrected the SK0012 "all production assemblies pass this rule" note (never actually verified platform-wide) and recorded a newly-discovered OPEN gap — 07.Messaging's MassTransitEventPublisher.BuildPublisher (same closure pattern, itself this exemption's own cited precedent) and MessagingBusBuilder.AddActivity remain unregistered/unverified against SK0012, tracked as a candidate follow-up; no new SK ID; no predicate/rule-logic change; **implementation status (2026-07-06 correction): D-58/C-95/T-169/T-170/DO-30 are all still `○` in state-map.md and ReflectionExemptionRegistry.AllowList ships empty in source — a prior version of this changelog entry incorrectly described the registry entry as already shipped; corrected during a /dispatch-phase cross-check of WO-039** — WO-039 P-240 (governance-arch-planner)
- [2026-07-06] SK.00.DomainEventDispatcherReflectionExemption ● complete (D-58, C-95, T-169, T-170, DO-30) — ReflectionExemptionRegistry.AllowList now ships with its FIRST real entry: ("SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher/<>c", "<PublishSingle>b__8_0"), determined empirically (red-then-green, D-58) via a temporary Mono.Cecil IL-walk against the real compiled SharedKernel.Application.dll rather than assumed from source, with a governance rationale block comment (WO-039, 2026-07-06, per C-95's spec — a HashSet field initializer cannot carry a compiler-recognized `///` doc comment on an individual tuple). MAJOR DISCOVERY during test authoring (not anticipated by the phase spec): NetArchTest.Rules' own type-discovery layer (Types.InAssembly(assembly), used internally by ReflectionGuardRules.NoMakeGenericMethodReflection) never surfaces compiler-generated closure types — such as the `<>c` singleton display class that actually contains PublishSingle's MakeGenericMethod call — to any ICustomRule, confirmed via an instrumented recording ICustomRule with and without the `.AreNotAbstract()` filter. This means the end-to-end rule call currently reports success regardless of whether this exemption is registered — the violation is never reached by NetArchTest's own type enumeration. T-169/T-170 were therefore implemented at the predicate layer (ReflectionGuardRulesRealAssemblyTests invokes NoMakeGenericMethodReflectionPredicate.MeetsRule directly against the real, Mono.Cecil-loaded `<>c` TypeDefinition) — proving the exemption and the predicate's IL-walk logic are genuinely load-bearing (fails when unregistered, passes when registered) — plus a third documentation test asserting and explaining the current end-to-end NetArchTest behavior, so a future NetArchTest upgrade or type-discovery fix is caught by a changed assertion rather than silently altering coverage. No change made to NoMakeGenericMethodReflectionPredicate or ReflectionGuardRules logic (verified by diff review, per phase scope). SK0012's diagnostic-registry note, the ReflectionExemptionRegistry documentation, and this file's prior "PLANNED"/"still empty" language all corrected to reflect the shipped entry and the discovered NetArchTest gap; the 07.Messaging open gap (MassTransitEventPublisher.BuildPublisher, MessagingBusBuilder.AddActivity — both still unregistered, P-240 scoped to 05.Application only) recorded as a candidate follow-up work order, now additionally noting the NetArchTest closure-visibility gap would need resolving too before that follow-up could enforce end-to-end. New candidate follow-up work order recorded: extend ReflectionGuardRules.NoMakeGenericMethodReflection (or a sibling factory method) to walk TypeDefinition.NestedTypes recursively via Mono.Cecil directly, rather than relying solely on NetArchTest's Types.InAssembly(...) projection, so closure-based MakeGenericMethod call sites are caught end-to-end. 130/130 SharedKernel.ArchitectureTests.Tests passing (127 baseline + 3 new), 0 build warnings/errors — WO-039 P-240 (governance-phase-implementer)
- [2026-07-07] SK0017 CommandImplementsCacheableQuery, SK0018 QueryImplementsInvalidatesCache, SK0019 RetryableRequestWithoutIdempotency added to diagnostic registry (general-purpose sequential block, next after SK0016; third/fourth/fifth SK analyzers in this domain requiring SemanticModel-resolved AllInterfaces closure, after SK0011 and SK0015); closes the last three "not mechanically enforced — code review must catch this" callouts in 05.Application/CLAUDE.md's Hard Violations section (the fourth pattern from the same audit family, typeof(TRequest).Name short-name usage, was already closed by SK0016 in WO-038 P-235); all three are consumer-side rules firing in ANY assembly declaring a command/query type — zero new SharedKernel.ArchitectureTests artifacts, pure Roslyn analyzers; six new implementation rules added — WO-040 P-248 (governance-arch-planner)
- [2026-07-08] Phase SK.00.LoggingStandardEnforcement added — SK0020 DirectILoggerExtensionMethodUsage and SK0021 HandWrittenLoggerMessageDefineDelegate added to diagnostic registry (general-purpose sequential block, next after SK0019; the domain's first two-diagnostics-one-analyzer-class shape — LoggingAuthoringStyleAnalyzer — since both encode the same "always [LoggerMessage], never hand-rolled" standard and share the GeneratedCodeAnalysisFlags.None guard plus the SharedKernel.Testing exemption; SK0020 requires SemanticModel.GetSymbolInfo ContainingType resolution to avoid false positives against unrelated logging frameworks, SK0021 is syntax-only); LoggingEventIdIntegrityAssertion added to architecture test contracts (WO-041 P-250) — the domain's second non-ConditionList/ICustomRule public helper after PipelineOrderAssertion, walking Mono.Cecil ModuleDefinition.Types/NestedTypes recursively (deliberately bypassing NetArchTest's Types.InAssembly(...) to avoid the SK0012-documented compiler-generated/nested-type blind spot) to assert global EventId uniqueness and per-assembly range membership against a caller-supplied Assembly→range dictionary sourced from 01.Core's SharedKernel.Primitives.Logging.LoggingEventIdRanges (P-249); mechanizes the root CLAUDE.md's new Logging Conventions section (WO-041 P-249/P-250) the same way SK0013/SK0014/SK0017-19 mechanized the raw-HttpClient/ProblemDetails/marker-interface conventions; EXPECTED TO FAIL against real shipped assemblies until every WO-041 domain retrofit ships (all ten are `○` Pending, including 01.Core's own P-249) — design/tests use contrived in-memory Mono.Cecil fixtures only; six new implementation rules added — WO-041 P-250, depends on 01.Core P-249 (governance-arch-planner)
- [2026-07-09] SK.00.LoggingStandardEnforcement → ● closeout — LoggingAuthoringStyleAnalyzer (SK0020/SK0021) implemented in SharedKernel.Analyzers/Diagnostics/; LoggingEventIdIntegrityAssertion implemented in SharedKernel.ArchitectureTests/; verified against the pre-written CLAUDE.md spec (diagnostic registry, architecture test contracts, all eleven implementation-rule bullets) — no discrepancy found, no edits required to those sections. One new implementation-rule bullet added below to record a technique divergence discovered during test authoring: SK0020/SK0021 analyzer test fixtures (SharedKernel.Analyzers.Tests) use a self-contained in-compilation stub of the Microsoft.Extensions.Logging surface — CSharpAnalyzerTest's default (older) reference-assembly set conflicts (CS1705 System.Runtime version mismatch) with a net10.0-targeted Microsoft.Extensions.Logging.Abstractions package reference, the same stub technique already used by SK0013's IHttpClientFactory fixture — while LoggingEventIdIntegrityAssertionTests' CompileInMemory fixtures reference the real Microsoft.Extensions.Logging.Abstractions package directly with no such conflict, since those fixtures compile and run in-process against the current runtime rather than through the analyzer-testing framework's isolated reference-assembly sandbox. 14 new analyzer tests (T-177–T-180) — 125/125 SharedKernel.Analyzers.Tests pass; 3 new architecture tests (T-181–T-183, including the load-bearing nested-type case) — 133/133 SharedKernel.ArchitectureTests.Tests pass; 0 build warnings/errors. This is the last `○` phase key — every phase key in 00.Governance/state-map.md is now `●` (governance-phase-implementer, state-map-phase)
- [2026-07-14] Phase SK.00.MagicStringGuard added — SK0022 CrossCuttingMagicStringLiteral added to diagnostic registry (general-purpose sequential block, next after SK0021; ONE analyzer class covering FOUR call-site shapes — HTTP header indexer/setter, Activity.SetBaggage/.SetTag, IConfiguration.GetSection, ClaimsPrincipal/Claim comparison — under a single DiagnosticDescriptor since all four encode the same underlying rule; fires globally with no suppression namespace; discriminates purely on literal-vs-reference syntax shape, never resolved value or declaring-class identity, carrying forward the WO-028/HealthCheckConstantsGuard generality requirement); WellKnownConstantOwnershipAssertion added to architecture test contracts (WO-042 P-264) — the domain's third non-ConditionList/ICustomRule public helper after PipelineOrderAssertion and LoggingEventIdIntegrityAssertion, extending StringConstantsClassDetector's field-shape + literal-value resolution to walk every TypeDefinition (not only constants-class shapes) and flag any non-owning assembly redeclaring a canonical 01.Core cross-cutting literal value; mechanizes the exact incident class P-261 exemplified (a "CorrelationId" vs "correlation.id" mismatch) the same way SK0013/PresentationLayeringRules/SK0020-21 mechanized the raw-HttpClient/ProblemDetails/logging conventions; EXPECTED TO FAIL / UNVERIFIABLE against real assemblies until 01.Core's WellKnownHeaders/WellKnownBaggageKeys (P-259) ship past design (only D-30 is locked as of this phase; C-43/T-34/DO-16 pending) AND the P-260/P-261/P-262/P-263 consuming-domain retrofits land — design/tests use contrived in-memory Mono.Cecil fixtures only; six new implementation rules added — WO-042 P-264, depends on 01.Core P-259/P-260/P-261/P-262/P-263 (governance-arch-planner)
- [2026-07-16] Phase SK.00.StorageTopology added — SK0023 NonSingletonAmazonS3ClientRegistration added to diagnostic registry (general-purpose sequential block, next after SK0022; platform's first storage-domain diagnostic; structural inverse of SK0703 — flags AddScoped/AddTransient registration of IAmazonS3 instead of AddSingleton; syntax-only, no SemanticModel; fires globally with no suppression namespace; stays in the sequential block rather than opening a new 08xx block, following the SK0011/SK0013 precedent for a lone domain-specific rule); StorageTopologyRules added to architecture test contracts (WO-043 P-271) — three pure NetArchTest predicates mirroring RedisTopologyRules exactly but scoped to 08.Storage's two provider packages (AbstractionsHasNoThirdPartyDependencies, ProviderPackagesNeverReferenceEachOther using a two-named-Assembly-parameter signature mirroring UnitOfWorkSeamRules, OnlyProviderPackagesMayReferenceAmazonS3 with caller-controlled exclusion mirroring PresentationLayeringRules/CompositionRootExclusivityRules); zero new SK ID block, zero new Mono.Cecil technique, zero new NuGet dependency; UNVERIFIABLE against real assemblies as of this phase's authoring — 08.Storage's own state-map shows every phase empty/○ and P-265 itself is only ◐ (Design) in the root state-map, P-266/P-267 not started — design/tests use contrived in-memory Mono.Cecil fixtures only, consistent with every "designed-ahead-of-a-pending-dependency" precedent in this domain; six new implementation rules added — WO-043 P-271, depends on 08.Storage P-265/P-266/P-267 (governance-arch-planner)
- [2026-07-16] SK.00.MagicStringGuard → ● closeout — CrossCuttingMagicStringLiteralAnalyzer (SK0022) implemented in SharedKernel.Analyzers/Diagnostics/; WellKnownConstantOwnershipAssertion implemented in SharedKernel.ArchitectureTests/, reusing StringConstantsClassDetector.ResolveStringFieldsOnType (already correctly extended to walk every TypeDefinition, not only constants-class shapes); found this entire phase already substantially implemented on disk from a prior uncommitted session — verified empirically rather than trusting it: 140/140 SharedKernel.Analyzers.Tests pass (125 baseline + 15 new: T-184 four fire-path cases, T-185 two, T-186 one, T-187 three, T-188 five pass-path cases including a fixture-local domain constants class proving declaring-class-agnosticism), 135/135 SharedKernel.ArchitectureTests.Tests pass (133 baseline + T-189/T-190), 0 build warnings/errors both projects; confirmed the acceptance-critical generality requirement holds — grepped the analyzer source for any hardcoded constants-class name, found none; File-Level Plan path discrepancy resolved per the phase input's own instruction — kept the on-disk Diagnostics/SK0022_CrossCuttingMagicStringLiteralAnalyzer.cs convention (matching every SK00NN sibling file) over the spec's guessed Analyzers/ path; added the missing `<!-- phase-key: SK.00.MagicStringGuard -->` marker to 00.Governance/state-map.md (same gap class as SK.00.PresentationArchRules/SK.00.MetricsOutcomeTagAndMisregistrationGuard before it). MAJOR CORRECTION discovered during this verification pass: this file's prior "EXPECTED TO FAIL / UNVERIFIABLE" real-assembly-status language (written 2026-07-14, the same day this phase was authored) was already stale by the time of this closeout — 01.Core P-259 shipped `WellKnownHeaders`/`WellKnownBaggageKeys` that same day, and P-260/P-261/P-262/P-263 (the 11.Communication/13.ServiceDefaults/14.Presentation/07.Messaging consuming-domain retrofits) all landed by 2026-07-16, per the root state-map.md Phase Backlog. Also discovered the shipped types live at `SharedKernel.Primitives.Propagation.WellKnownHeaders`/`WellKnownBaggageKeys` — NOT the `SharedKernel.Primitives.CrossCutting` namespace this phase's design prose, the analyzer's own diagnostic message, and WellKnownConstantOwnershipAssertion's XML doc examples all assumed; corrected the namespace in all three source locations plus this file's diagnostic-registry Note and architecture-test-contract "Real-assembly status" prose, and corrected the "EXPECTED TO FAIL/UNVERIFIABLE" claims to record the now-resolved dependency. Real-assembly wiring itself (pointing WellKnownConstantOwnershipAssertion at the real SharedKernel.Primitives.dll) remains unimplemented — genuinely out of this phase's own stated scope, not a gap in this closeout — and is recorded as a ready-to-dispatch candidate follow-up. This is the last `○` phase key — every phase key in 00.Governance/state-map.md is now `●` except SK.00.StorageTopology (governance-phase-implementer, state-map-phase)
- [2026-07-16] SK.00.MagicStringGuard → ● closeout — CrossCuttingMagicStringLiteralAnalyzer (SK0022) implemented in SharedKernel.Analyzers/Diagnostics/; WellKnownConstantOwnershipAssertion implemented in SharedKernel.ArchitectureTests/, backed by a new StringConstantsClassDetector.ResolveStringFieldsOnType(TypeDefinition) public entry point (extracted from the existing private field-shape+literal-value resolution logic, now reusable against ANY TypeDefinition, not only the abstract-sealed constants-class shape); verified against the pre-written CLAUDE.md spec (diagnostic registry, architecture test contracts, all ten implementation-rule bullets) — no discrepancy found, no edits required to those sections. One new implementation-rule bullet added above to record an empirically-verified technique divergence discovered during test authoring: three of SK0022's four real BCL call-site types (System.Net.Http.Headers.HttpHeaders/HttpRequestHeaders, System.Security.Claims.Claim/ClaimsPrincipal/ClaimsIdentity) compile correctly as-is inside the CSharpAnalyzerTest sandbox with NO stub needed (mirroring SK0013's HttpClient precedent) — only System.Diagnostics.Activity required special handling: the sandbox's default reference set resolves an old DiagnosticSource 4.0.5.0 contract whose Activity lacks .SetTag/.SetBaggage (confirmed via a live CS1061), and naively adding a newer DiagnosticSource reference via TestState.AdditionalReferences produces a live CS0433 same-assembly-different-version ambiguity rather than fixing it; the working fix is a full reference-set REPLACEMENT via test.ReferenceAssemblies = ReferenceAssemblies.Net.Net80 for just the Activity-shape tests. Only Microsoft.Extensions.Configuration.IConfiguration and Microsoft.AspNetCore.Http.IHeaderDictionary needed the originally-planned in-compilation stub, since both are genuinely absent from the sandbox's default closure. 15 new analyzer tests (T-184–T-188, covering all four call-site shapes' fire/pass paths plus a fixture-local-constants-class pass-path proving declaring-class-agnosticism) — 140/140 SharedKernel.Analyzers.Tests pass; 2 new architecture tests (T-189–T-190) — 135/135 SharedKernel.ArchitectureTests.Tests pass; 0 build warnings/errors on both projects. Real-assembly wiring against 01.Core's actual WellKnownHeaders/WellKnownBaggageKeys remains explicitly deferred per the phase's own non-gating acceptance criterion — tracked in Cross-Domain Dependencies until 01.Core C-43/T-34/DO-16 ship and P-260–P-263 land (governance-phase-implementer, state-map-phase)
- [2026-07-18] SK.00.StorageTopology → ● closeout — NonSingletonAmazonS3ClientRegistrationAnalyzer (SK0023) implemented in SharedKernel.Analyzers/Diagnostics/; StorageTopologyRules implemented in SharedKernel.ArchitectureTests/Rules/, exactly matching the pre-written CLAUDE.md spec (all three factory-method signatures, forbidden-term lists, two-named-parameter ProviderPackagesNeverReferenceEachOther, caller-controlled-exclusion OnlyProviderPackagesMayReferenceAmazonS3) — no discrepancy found, no edits required to the diagnostic registry or architecture-test-contract sections themselves. **STALE-DEPENDENCY CORRECTION (the main finding of this closeout):** the phase spec (authored 2026-07-16) instructed contrived-fixtures-only design because 08.Storage's own state-map then showed every phase at ○/empty and P-265 was only ◐ (Design). Before implementation began (2026-07-18), 08.Storage had independently reached Published — verified directly on disk, not assumed from prose: `dotnet build --configuration Release` on all three of SharedKernel.Storage.Abstractions/.S3/.Obs succeeds with 0 warnings/0 errors, and the root state-map.md Phase Backlog shows P-265/P-266/P-267 all `●` Complete (closed 2026-07-18, the same day as this implementation session). Corrected the stale Cross-Domain Dependencies table rows (00.Governance/state-map.md, `SK.00.StorageTopology depends on 08.Storage` section) and the "UNVERIFIABLE"/"before ... exist as buildable assemblies" prose in this file's StorageTopologyRules architecture-test-contract Note and Implementation Rules bullet — same precedent SK.00.MagicStringGuard's closeout set for this exact class of dependency-resolved-before-implementation correction. Per the phase input's explicit instruction, real-assembly verification was ADDITIONALLY wired in this same phase rather than deferred as a follow-up: SharedKernel.ArchitectureTests.Tests.csproj gained three test-only ProjectReferences (PrivateAssets="all") to the real 08.Storage assemblies; three Real*-suffixed tests confirm all three StorageTopologyRules factory methods pass against the shipped packages with zero discrepancy from contrived-fixture behavior — no real violation surfaced. The six contrived-fixture fire/pass-path tests (T-194–T-199) remain the primary red/green proof, exactly as the phase spec required; one fixture-authoring pitfall was hit and fixed during T-196's authoring — the initial ProviderPackagesNeverReferenceEachOther fire-path fixtures referenced a `const int` field on the "other" provider's stub type, which the C# compiler const-folds into a bare literal at the call site (no Ldsfld, no assembly reference emitted), so NetArchTest's dependency-namespace scan never observed the cross-reference; switched to constructor-injecting an interface type (a genuine metadata reference) instead, mirroring RedisTopologyRulesTests's own established pattern — the same const-folding class of pitfall already documented for `const string` vs `static readonly string` in the HealthCheckConstantsGuard closeout, now confirmed to apply identically to `const int`. 4 new analyzer tests (T-191–T-193 plus one unrelated-interface pass-path) — 144/144 SharedKernel.Analyzers.Tests pass; 10 new architecture tests (T-194–T-199 plus 3 real-assembly tests) — 145/145 SharedKernel.ArchitectureTests.Tests pass; 0 build warnings/errors across both projects. This is the last `○` phase key — every phase key in 00.Governance/state-map.md is now `●` (governance-phase-implementer, state-map-phase)
- [2026-07-19] Phase SK.00.SearchTopology added — SK0024 RawSearchFieldNameLiteral and SK0025 ObsoleteElasticsearchClientUsage added to diagnostic registry (general-purpose sequential block, next after SK0023; platform's first 09.Search-domain diagnostics; SK0024 is the domain's eighth semantic-model analyzer and the platform's first refactor-safety/nameof()-encouragement rule, distinct in intent from SK0022 despite sharing its literal-vs-reference discriminator; SK0025 is the ninth semantic-model analyzer and the platform's first EOL-third-party-package-prohibition rule, firing platform-wide on any ContainingAssembly.Name match against NEST/Elasticsearch.Net); SearchTopologyRules added to architecture test contracts (WO-044 P-278) — two pure NetArchTest predicates mirroring StorageTopologyRules (AbstractionsHasNoThirdPartyDependencies with a sixth Microsoft.Extensions forbidden term beyond Storage's five-term analog, ProviderPackagesNeverReferenceEachOther with a two-named-Assembly-parameter signature mirroring StorageTopologyRules/UnitOfWorkSeamRules); SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts added — the first method on that class returning ConditionList[] instead of a single ConditionList, an explicit fifteen-forbidden-term enumeration of every other numbered domain's package family (including the SharedKernel.AI/10.Intelligence package-family-name-vs-folder-name mismatch); zero new Mono.Cecil technique, zero new NuGet dependency; UNVERIFIABLE against real assemblies as of this phase's authoring — 09.Search's entire Design phase (D-01–D-28) is ○ and only bare .csproj skeletons exist on disk for all three packages — but UNLIKE every prior designed-ahead precedent in this domain, real-assembly verification is a GATING acceptance criterion per the phase input itself, not a non-blocking follow-up; seven new implementation rules added; one Cross-Domain Dependencies block added (09.Search P-272/P-273/P-274, explicitly marked gating, not the usual non-blocking-follow-up shape) — WO-044 P-278, depends on 09.Search P-272/P-273/P-274 (governance-arch-planner)
- [2026-07-21] WO-045: 00.Governance gap-filled for the new 10.Intelligence domain — SK0026 RawIntelligenceProviderClientConstructorInjection and SK0027 RawIntelligenceIdentifierLiteral added to diagnostic registry (general-purpose sequential block, next after SK0025; SK0026 the domain's tenth semantic-model analyzer, requiring exact-full-type-name resolution for QdrantClient/MilvusClient/Kernel because "Kernel" is a highly collision-prone simple name; SK0027 the eleventh, structurally parallel to SK0024 but grounded in the sharper, domain-specific model-identity-mismatch hazard Domain Invariant #1 describes); IntelligenceTopologyRules added to architecture test contracts (WO-045 P-286) — three pure NetArchTest predicates mirroring StorageTopologyRules/SearchTopologyRules, scoped to 10.Intelligence's THREE sibling providers (AbstractionsHasNoThirdPartyDependencies with an eight-term forbidden list, ProviderPackagesNeverReferenceEachOther as the domain's first three-named-Assembly-parameter sibling-non-reference check returning a six-element ConditionList[], and NoHealthChecksDependencyAcrossIntelligencePackages mechanizing Domain Invariant #8's explicit HealthChecks prohibition); SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts added, mirroring SearchReferencesOnlyCoreAndContracts's fifteen-term ConditionList[] shape with SharedKernel.Search swapped in and SharedKernel.AI swapped out; zero new Mono.Cecil technique, zero new NuGet dependency; UNVERIFIABLE against real assemblies as of this phase's authoring — 10.Intelligence/CLAUDE.md states "No production .cs file has been written yet," so real-assembly verification is a GATING acceptance criterion per the phase input itself, mirroring SK.00.SearchTopology's precedent (arch-lead, WO-045, P-286)
- [2026-07-22] Phase SK.00.WorkflowTopology added — WO-046 (P-290): SK0028 NonDeterministicApiUsageInsideWorkflow and SK0029 RawTemporalClientConstructorInjection added to diagnostic registry (general-purpose sequential block, next after SK0027; platform's first 17.Workflows-domain diagnostics; SK0028 is the domain's twelfth semantic-model analyzer and the first to scope its trigger by type attribution/inheritance ([Workflow]/WorkflowBase in-scope, ActivityBase/[Activity] positively excluded first) rather than the established namespace-ancestor walk, firing on seven forbidden non-deterministic/side-effecting shapes — the four-property clock set, Guid.NewGuid(), new Random(), the Task.Run/Task.Delay/ConfigureAwait(false) scheduler-escape trio, Environment.*/File.*, and constructor-injected IClock/ILogger<T> — explicitly encoding the narrow SK0001 inversion (IClock banned inside workflows, mandatory inside activities); SK0029 the thirteenth semantic-model analyzer, structurally identical to SK0013/SK0026 but with a single shared exemption namespace since 17.Workflows has one owning package, not siblings); WorkflowTopologyRules added to architecture test contracts (WO-046 P-290) — the first *TopologyRules class scoped to a domain with no sibling provider packages, carrying two methods instead of the Storage/Search/Intelligence Abstractions+Siblings pair: NoRawClientAccessorConsumptionInRepo (via new NoRawClientAccessorConsumptionPredicate, mechanizing 17.Workflows/CLAUDE.md's own three-gate ITemporalRawClientAccessor escape-hatch gate 3, scoped literally to "no in-repo type" — not a per-consuming-microservice AllowRawClientAccess() correlation, which is out of this rule's jurisdiction) and NoHealthChecksDependencyInWorkflows (mirroring IntelligenceTopologyRules's narrow-term precedent); SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication added — the first layering-boundary method on that class permitting THREE upstream domains (01.Core/04.Contracts/05.Application), yielding a FOURTEEN-term forbidden list (not fifteen) with "SharedKernel.Application" the one term deliberately absent versus its Search/Intelligence siblings; zero new Mono.Cecil technique, zero new NuGet dependency; UNVERIFIABLE against real assemblies as of this phase's authoring — 17.Workflows/state-map.md shows its entire Design/Scaffold/Core phases at ○, so real-assembly verification is a GATING acceptance criterion per the phase input itself, mirroring SK.00.SearchTopology's/SK.00.IntelligenceTopology's precedent (arch-lead, WO-046, P-290)
- [2026-07-24] SK.00.SearchTopology → ● closeout — RawSearchFieldNameLiteralAnalyzer (SK0024) and ObsoleteElasticsearchClientUsageAnalyzer (SK0025) implemented in SharedKernel.Analyzers/Diagnostics/; SearchTopologyRules implemented in SharedKernel.ArchitectureTests/Rules/SearchTopologyRules.cs; SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts added to the existing Rules/SharedKernelLayeringRules.cs. **STALE-DEPENDENCY CORRECTION (the main finding of this closeout, same class as SK.00.StorageTopology's):** the phase spec (authored 2026-07-19) instructed contrived-fixtures-only design because 09.Search's own state-map then showed the entire Design phase at ○ with only bare .csproj skeletons on disk. Before implementation began (2026-07-24), 09.Search had independently reached Published — verified directly on disk: 139/139 tasks ● across all six phases, all three assemblies build clean. Real-assembly verification was therefore wired in THIS phase per the phase's own GATING acceptance criterion (Acceptance Criteria's own explicit checkbox, not a non-blocking follow-up like most prior precedents): SharedKernel.ArchitectureTests.Tests.csproj gained three test-only ProjectReferences (PrivateAssets="all") to the real SharedKernel.Search.Abstractions/.Meilisearch/.ElasticSearch assemblies; two Real*-suffixed tests confirm both SearchTopologyRules factory methods pass against the shipped packages — no real violation surfaced, the real Abstractions package genuinely carries zero third-party/Microsoft.Extensions dependency and the two providers genuinely never reference each other. The eight contrived-fixture fire/pass-path tests (T-208–T-211, one extra Microsoft.Extensions-specific fire-path case beyond the two-per-shape minimum) remain the primary red/green proof, exactly as the phase spec required. SK0024's eleven call-site shapes were confirmed against the real shipped source before implementation (not guessed): IQueryBuilder<TDocument> at SharedKernel.Search.Abstractions.Querying.IQueryBuilder, SearchFilter at SharedKernel.Search.Abstractions.Models.SearchFilter — both namespaces locked exactly as the analyzer's hardcoded constants expect, resolving the phase spec's own "not yet locked as of authoring" open question. **SK0025 implementation-time correction (the second finding):** the design-time Trigger prose named QualifiedNameSyntax/UsingDirectiveSyntax as registered syntax kinds; empirical testing (compile, run, read the actual diagnostic count) revealed two false-positive sources neither the design prose nor any prior SK analyzer in this domain had to handle — a NAMESPACE symbol (the bare "Nest"/"Elasticsearch" segment of a using directive or qualified-name prefix) resolves to a ContainingAssembly.Name matching the deprecated package too, and the "var" contextual keyword is itself an IdentifierNameSyntax resolving to the inferred type — fixed with an ITypeSymbol filter plus an explicit `Identifier.ValueText == "var"` guard; registering only IdentifierNameSyntax/GenericNameSyntax (never QualifiedNameSyntax) does not narrow real coverage, since a qualified name's rightmost segment is itself visited as an independent IdentifierNameSyntax node. SK0025's test file introduces this project's first genuinely-separate-compiled-reference-assembly technique (Microsoft.CodeAnalysis.Testing.SolutionState.AdditionalProjects, keyed by a literal AssemblyName of "NEST"/"Elasticsearch.Net"/"Elastic.Clients.Elasticsearch") rather than the established in-compilation-stub technique — required because SK0025's own discriminator is the referenced assembly's literal name, which an in-compilation stub can never simulate (it always resolves to the test assembly's own name); ProjectState.AssemblyName's read-only, Name-derived shape was confirmed via a scratch reflection probe before use. Both corrections recorded in the diagnostic registry, architecture-test-contract Note, and two new/updated Implementation Rules bullets. 21 new analyzer tests (T-200–T-207 plus additional coverage for all eleven/four call-site shapes named in the acceptance criteria) — 165/165 SharedKernel.Analyzers.Tests pass (144 baseline + 21); 10 new architecture tests (T-208–T-213 plus 2 real-assembly tests) — 155/155 SharedKernel.ArchitectureTests.Tests pass (145 baseline + 10); 0 build warnings/errors introduced by this phase's own code (the two CS8509/CS8524 warnings observed during the real-assembly build are 09.Search's own pre-existing, deliberately-downgraded-but-visible WarningsNotAsErrors entries, documented in 09.Search/CLAUDE.md, not a defect this phase's tests flag or should flag). This is the last `○` phase key — every phase key in 00.Governance/state-map.md is now `●` (governance-phase-implementer, state-map-phase)
- [2026-07-27] SK.00.IntelligenceTopology → ● closeout — RawIntelligenceProviderClientConstructorInjectionAnalyzer (SK0026) and RawIntelligenceIdentifierLiteralAnalyzer (SK0027) implemented in SharedKernel.Analyzers/Diagnostics/; IntelligenceTopologyRules implemented in SharedKernel.ArchitectureTests/Rules/IntelligenceTopologyRules.cs; SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts added to the existing Rules/SharedKernelLayeringRules.cs. **MANDATORY 2-PROVIDER ADAPTATION (the defining finding of this closeout, a stale-dependency correction of a different kind than SK.00.StorageTopology's/SK.00.SearchTopology's):** the phase spec (authored 2026-07-21, WO-045 P-286) was drafted for THREE sibling providers (Qdrant/Milvus/SemanticKernel). On 2026-07-27, arch-lead ratified WO-048, permanently retracting SharedKernel.AI.Milvus — Milvus.Client never shipped a stable release — before this phase's implementation session began. Verified directly on disk per the phase input's own instruction: no 10.Intelligence/SharedKernel.AI.Milvus/ directory exists, and IntelligenceWellKnown carries no MilvusProviderName. The SHIPPED implementation is therefore adapted to the real two-provider reality, not the three-provider design: SK0026 resolves exactly TWO full type names (Qdrant.Client.QdrantClient exempt inside SharedKernel.AI.Qdrant; Microsoft.SemanticKernel.Kernel exempt inside SharedKernel.AI.SemanticKernel) with no Milvus.Client.MilvusClient resolution and no SharedKernel.AI.Milvus exemption; IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies carries SIX forbidden terms, not eight (the two Milvus terms — "Milvus", "SharedKernel.AI.Milvus" — are dropped); .ProviderPackagesNeverReferenceEachOther is the TWO-named-Assembly-parameter form (qdrantAssembly, semanticKernelAssembly) returning a TWO-element ConditionList[], mirroring StorageTopologyRules/SearchTopologyRules exactly rather than the three-named-parameter/six-element form originally drafted; .NoHealthChecksDependencyAcrossIntelligencePackages is unchanged in shape, only its typical caller-supplied assembly count shrinks from three to two providers. SK0027 and SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts are UNAFFECTED by the retraction and implemented exactly as originally designed (SK0027 targets only SharedKernel.AI.Abstractions call sites; the fifteen-term forbidden list never named Milvus and already self-excluded "SharedKernel.AI" by omission). Per this domain's established "annotate, never silently rewrite" convention, the original three-provider prose in the SK0026 diagnostic-registry entry, the IntelligenceTopologyRules architecture-test-contract entry, and the WO-045-era Implementation Rules bullets are all left as originally drafted, with a CORRECTED note appended to each recording the WO-048 divergence and the as-shipped shape — mirroring the SK0025/StorageTopologyRules/SearchTopologyRules CORRECTED-note precedent exactly. **Real-assembly verification (this phase's own GATING acceptance criterion) is DONE, not deferred:** 10.Intelligence reached Published (all six phases ●, WO-048) before this phase's implementation session, so SharedKernel.ArchitectureTests.Tests.csproj gained three test-only ProjectReferences (PrivateAssets="all") to the real, shipped SharedKernel.AI.Abstractions/.Qdrant/.SemanticKernel assemblies (there is no SharedKernel.AI.Milvus reference to add — that package does not exist), and IntelligenceTopologyRulesTests.cs carries three Real*-suffixed tests confirming all three IntelligenceTopologyRules factory methods pass against the shipped packages — no real violation surfaced, the real Abstractions package genuinely carries zero third-party/Microsoft.Extensions dependency and Qdrant/SemanticKernel genuinely never reference each other. The contrived in-memory fire/pass-path fixtures (12 tests across AbstractionsHasNoThirdPartyDependencies/ProviderPackagesNeverReferenceEachOther/NoHealthChecksDependencyAcrossIntelligencePackages) remain the primary red/green proof, per the established convention. 20 new analyzer tests (6 for SK0026 covering both client types' fire/pass paths plus the cross-owning-package and unrelated-Kernel-type cases; 14 for SK0027 covering all eleven real call-site shapes' fire paths plus nameof()/identifier-constants-class pass paths) — 185/185 SharedKernel.Analyzers.Tests pass (165 baseline + 20); 15 new architecture tests (12 in IntelligenceTopologyRulesTests, 3 in LayeringRulesTests for IntelligenceReferencesOnlyCoreAndContracts) — 170/170 SharedKernel.ArchitectureTests.Tests pass (155 baseline + 15); 0 build warnings/errors introduced by this phase's own code (the pre-existing CS8509/CS8524 warnings observed during the real-assembly build belong to 09.Search's/10.Intelligence's own pre-existing, documented switch-exhaustiveness downgrades, not a defect this phase's tests flag or should flag). Two analyzer-test authoring pitfalls hit and fixed while writing SK0026's fixtures: (1) a `using` directive placed after other namespace-member-declarations already present in the same compiled TestCode string is invalid — every `using` in a per-test source fragment must precede the concatenated stub namespaces, or use `global::`-qualified references instead, which is what this file does; (2) nesting `using Qdrant.Client;` inside a `namespace SharedKernel.AI.Qdrant.*` block produces a genuine CS0234, because the bare leading "Qdrant" segment binds against the enclosing namespace's own trailing "Qdrant" segment rather than the global root — both fixed by using `global::Qdrant.Client.QdrantClient`/`global::Microsoft.SemanticKernel.Kernel` at every constructor-parameter reference site instead of a `using` directive. This is the last `○` phase key — every phase key in 00.Governance/state-map.md is now `●` (governance-phase-implementer, state-map-phase)
- [2026-07-27] SK.00.WorkflowTopology → ● closeout — NonDeterministicApiUsageInsideWorkflowAnalyzer (SK0028) and RawTemporalClientConstructorInjectionAnalyzer (SK0029) implemented in SharedKernel.Analyzers/Diagnostics/; NoRawClientAccessorConsumptionPredicate implemented in Predicates/; WorkflowTopologyRules implemented in SharedKernel.ArchitectureTests/Rules/WorkflowTopologyRules.cs; SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication added to the existing Rules/SharedKernelLayeringRules.cs. Verified against the pre-written CLAUDE.md spec (Diagnostic Rule Registry SK0028/SK0029 entries, Architecture Test Contracts entries for WorkflowTopologyRules/NoRawClientAccessorConsumptionPredicate/SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication, and the corresponding Implementation Rules bullets) — no discrepancy found anywhere, no edits required to those sections. **Real-assembly verification (this phase's own GATING acceptance criterion) is DONE, not deferred:** 17.Workflows reached Published (all six phases ●, WO-046 P-287, 158/158 tests) before this implementation session — verified directly on disk. SharedKernel.ArchitectureTests.Tests.csproj gained a test-only ProjectReference (PrivateAssets="all") to the real SharedKernel.Workflows.Temporal.csproj; WorkflowTopologyRulesTests.cs carries two Real*-suffixed tests (NoRawClientAccessorConsumptionInRepo_RealWorkflowsAssembly_RulePasses, NoHealthChecksDependencyInWorkflows_RealWorkflowsAssembly_RulePasses) confirming both factory methods pass against the shipped assembly — no real violation surfaced, the real SharedKernel.Workflows.Temporal only PRODUCES ITemporalRawClientAccessor via DI-registration factory wiring (never consumes it as a ctor/field dependency) and carries zero dependency on Microsoft.Extensions.Diagnostics.HealthChecks. The contrived in-memory fixtures (7 tests across NoRawClientAccessorConsumptionInRepo/NoHealthChecksDependencyInWorkflows, plus 2 in LayeringRulesTests for WorkflowsReferencesOnlyCoreContractsAndApplication) remain the primary red/green proof, per the established convention. 30 new analyzer tests (21 for SK0028 covering all seven forbidden shapes' fire paths — including bonus Task.Run and [Workflow]-attribute-without-base-class scoping coverage — plus the ActivityBase-exclusion/[Activity]-attribute/both-attributes-exclusion-wins/sanctioned-Workflow.*-primitives/ordinary-non-workflow-type pass paths; 9 for SK0029 covering all four raw client types' fire/pass paths plus the unrelated-same-simple-name pass path) — 215/215 SharedKernel.Analyzers.Tests pass (185 baseline + 30); 9 new architecture tests — 179/179 SharedKernel.ArchitectureTests.Tests pass (170 baseline + 9); 0 build warnings/errors introduced by this phase's own code. **Task-count correction (recurring pattern, same class as SK.00.CommunicationArchRules/SK.00.ServiceDefaultsGovernance):** this phase's own prose said "26 tasks," but its task table has always held 27 rows (D-65 + C-118–C-122 [5] + T-233–T-252 [20] + DO-37 = 27) — corrected in 00.Governance/state-map.md's Overall Progress row and total-tasks summary at this closeout. This is the last `○` phase key — every phase key in 00.Governance/state-map.md is now `●`. Promoted to root state-map.md (Phase Backlog P-290 closed to `●` Complete) (state-map-phase, governance-phase-implementer)
- [2026-07-27] Phase SK.00.ResultDiscardGuard added — SK0030 ResultOutcomeDiscarded added to diagnostic registry (general-purpose sequential block, next after SK0029; this domain's fourteenth semantic-model analyzer; the platform's Result/Result<T> analogue of CS4014's unawaited-Task warning, made possible by the IHasSuccessFlag zero-member marker interface 01.Core added at P-230 (WO-038, SharedKernel.Primitives.Results.IHasSuccessFlag) specifically for this purpose and left unused for it since — confirmed by reading the shipped 01.Core source directly rather than assuming its namespace); fires on a bare ExpressionStatementSyntax wrapping an InvocationExpressionSyntax/AwaitExpressionSyntax whose SemanticModel-resolved OUTERMOST type implements IHasSuccessFlag; registers on exactly those two syntax kinds and deliberately never inspects AssignmentExpressionSyntax at all, so a genuine variable/field re-assignment and an explicit `_ = ...` discard both pass structurally with zero dedicated IDiscardSymbol detection logic — the single cheapest design that satisfies every non-firing shape the phase input names; documents the deliberate `await FooAsync();`-as-bare-statement fire case (Task<Result<T>>/ValueTask<Result<T>> unwrap via Roslyn's own GetTypeInfo to a still-discarded Result<T>) as the CS4014 analogy one level further down, and the fluent-chain nuance where a chain whose OWN outermost call still returns a Result-implementing type correctly still fires; deliberately does not register on ObjectCreationExpressionSyntax or ConditionalAccessExpressionSyntax (documented scope limitations, not oversights, mirroring SK0708/HealthCheckTagIntegrityRules's over-approximation discipline); has NO SharedKernel.ArchitectureTests counterpart — the first single-analyzer phase in this file where that is explicitly by design, since a bare-statement discard is a pure per-compilation-unit Roslyn concern with no assembly-dependency-graph or IL aspect; this domain's first analyzer whose "GATING, not deferred" verification pass audits ALREADY-SHIPPED code across other domains (05.Application.Behaviors, 06.Persistence.EfCore, 07.Messaging.MassTransit, 17.Workflows.Temporal — all four already Published as of this phase's authoring) rather than waiting on a not-yet-implemented dependency, executed as representative fixtures modeled on real consumption shapes per this domain's own "never compile real source files directly" Test Rule, not literal file-I/O; no Cross-Domain Dependencies entry added — nothing this phase needs is pending; six new implementation rules added — WO-049 P-299 (governance-arch-planner)
- [2026-07-27] SK.00.ResultDiscardGuard → ● closeout — ResultOutcomeDiscardedAnalyzer (SK0030) implemented in SharedKernel.Analyzers/Diagnostics/. Verified against the pre-written CLAUDE.md spec (diagnostic registry entry, all six Implementation Rules bullets) — no discrepancy in documented BEHAVIOR, one deliberate implementation refinement made for textual fidelity: the first working draft registered on `SyntaxKind.ExpressionStatement` and pattern-matched on `statement.Expression`'s syntax kind; refactored to register directly on `SyntaxKind.InvocationExpression`/`SyntaxKind.AwaitExpression` (checking `context.Node.Parent is ExpressionStatementSyntax`) to match the spec's literal "registers on exactly two SyntaxKinds" wording precisely — confirmed functionally identical across all 18 test cases before and after the refactor (re-ran the full suite both times). **REAL-SOURCE AUDIT (this phase's own GATING acceptance criterion) — DONE, not deferred, and it found a genuine, previously-invisible defect, not zero findings.** Per this domain's Test Rule ("never test analyzers by compiling real source files manually"), the audit was executed as a manual read (via a research sub-agent plus independent `grep` corroboration by this implementer directly) across every production `.cs` file (excluding `bin/`/`obj/`/`*.Tests`) in `05.Application`/`05.Application.Behaviors`, `06.Persistence.EfCore`, `07.Messaging.MassTransit`, and `17.Workflows.Temporal` — all four already Published. Eight distinct real consumption shapes were catalogued and each encoded as a representative (paraphrased, never copy-pasted) pass-path fixture: T-266 (pipeline-behavior `var result = await next(); if (result is IHasSuccessFlag f && !f.IsSuccess) {...} return result;`, modeled on `CacheInvalidationBehavior`/`ResponseOutcomeClassifier`), T-267 (repository `return error;`/`return Result<T>.Success(entity);` via the implicit `Error`→`Result<T>` conversion, modeled on `06.Persistence.EfCore` repository methods and `EncryptedValueConverter`), T-268 (consumer `var result = await _mediator.Send(...); if (result.IsFailure) throw ...;`, modeled on the architecturally-analogous MassTransit-consumer-dispatches-a-Result-returning-MediatR-command shape — `07.Messaging.MassTransit`'s OWN production code was confirmed to never touch `SharedKernel.Primitives.Results.Result` at all, its `MessagingOptionsValidator` returns the unrelated `Microsoft.Extensions.Options.ValidateOptionsResult`), and T-269 (`CommandActivity`/`WorkflowFailureMapper`'s `return await WorkflowFailureMapperFixture.ToApplicationFailureAsync(result);` — a `ReturnStatementSyntax`, never registered regardless of the wrapped `await`). **GENUINE FINDING:** `05.Application/SharedKernel.Application.Behaviors/FireAndForget/FireAndForgetBackgroundConsumer.cs` line 57 — `await sender.Send(command, stoppingToken).ConfigureAwait(false);` as a bare statement. `command` is statically typed `IFireAndForgetCommand : ICommand : ICommandBase, IRequest<Result>`, so MediatR's generic `ISender.Send<TResponse>` overload resolves `TResponse` to `SharedKernel.Primitives.Results.Result` — the awaited expression's resolved type is `Result`, which implements `IHasSuccessFlag`, SK0030's exact fire condition. The outcome is never assigned, returned, passed as an argument, or explicitly discarded via `_ = ...`. This is a genuine, previously-invisible defect in `05.Application`'s own shipped code, confirmed empirically (not assumed) by reading `FireAndForgetBackgroundConsumer.cs`, `IFireAndForgetCommand.cs`, and `ICommand.cs` directly, and independently corroborated by a targeted `grep` across all four domains' production trees for the identical bare-`await`-`Send`/`Publish` shape (no second occurrence found). Per this phase's own Implementation Rule 7 and Test Rules, the finding is recorded here rather than "fixed" by narrowing SK0030's trigger — **T-270** (`RealSourceAudit_FireAndForgetBackgroundConsumerShape_ReportsSk0030`) reproduces this exact shape as a paraphrased FIRE-path fixture (never the literal file), proving SK0030 mechanically catches this real, already-shipped pattern; T-270's own XML doc carries the full audit record (all eight shapes, the 07.Messaging.MassTransit correction, and the finding) as the permanent, machine-checked record of this audit. **Escalated as a candidate follow-up work order for `05.Application`** (not implemented here — production code in another domain is outside this agent's jurisdiction, `00.Governance` never implements another domain's production code): rewrite the call site as `_ = await sender.Send(command, stoppingToken).ConfigureAwait(false);` — zero behavior change, since the surrounding `try/catch`'s handler-fault log is already the only observation this fire-and-forget dispatch path is designed to have; the discard would simply become mechanically unambiguous instead of implicit. 18 new analyzer tests (T-253–T-270, covering all five fire-path shapes, all eight pass-path shapes including the four real-pattern-audit fixtures, and the one real-violation-reproduction fire fixture) — 233/233 SharedKernel.Analyzers.Tests pass (215 baseline + 18); 0 build warnings/errors. No `SharedKernel.ArchitectureTests` changes made, per this phase's own explicit no-counterpart design — 179/179 SharedKernel.ArchitectureTests.Tests still pass unchanged, confirmed as a regression check. This is the last `○` phase key — every phase key in 00.Governance/state-map.md is now `●` (governance-phase-implementer, state-map-phase)
- [2026-08-13] Phase SK.00.SecurityContextGuard added — WO-057 (P-373): `12.Security/CLAUDE.md` has documented three hard rules since its original build-out (2026-06-02) and never mechanized any of them — this phase closes all three at once, the platform's first `12.Security`-domain governance phase. SK0031 `RawSecurityContextConstructorInjection` added to diagnostic registry (general-purpose sequential block, next after SK0030; syntax-only, mirrors SK0013's exact shape and namespace-exemption technique, but with TWO exemption prefixes — `SharedKernel.Security.Oidc` and a forward-looking, currently-vacuous `SharedKernel.Security.ApiKey`, since no such package exists in the platform yet). New `SecurityArchitectureRules` static class added to architecture test contracts — the platform's first dedicated `12.Security` architecture-rule class, with two factory methods carrying no SK ID: `DomainNeverReferencesTenantProvider` (reuses `NoDbContextTransactionInApplicationPredicate`'s established three-surface fields/parameters/instructions inspection technique, zero exemption) and `NoSingletonRegistrationOfSecurityContextTypes` (introduces this domain's NEWEST Mono.Cecil technique — generic-instance-method-argument inspection via `GenericInstanceMethod.GenericArguments` — the IL-level analogue of SK0703's syntax-level generic-type-argument extraction, chosen because this phase's own acceptance criteria explicitly call for an architecture-test rule rather than an analyzer for the singleton-lifetime check). Real-assembly verification is NON-GATING and immediately available, unlike most recent phases in this file — both `12.Security` (`.Abstractions`/`.Oidc`) and `03.Domain` are already fully Published, so no Cross-Domain Dependencies entry was added. Six new implementation rules added — WO-057, P-373 (governance-arch-planner)
- [2026-07-30] Phase SK.00.EfPropertyUsageGuard added — WO-051 (P-327): a fourth predicate added to the existing `EfCorePackageHygieneRules` class — `NoDirectEfPropertyUsageInEfCoreAssembly`, backed by new `NoDirectEfPropertyUsagePredicate` — mechanizing the platform's SECOND occurrence of the identical "client-side evaluation via `EF.Property<TId>`" defect class (first fixed at P-105 `EfReadRepository.GetByIdsAsync`, reintroduced and now being independently fixed again at `06.Persistence`'s P-316 `TenantedRepository`). Uses the established `Call`/`Callvirt` IL opcode-presence Mono.Cecil technique (`MethodReference.Name == "Property"` + `MethodReference.DeclaringType.FullName == "Microsoft.EntityFrameworkCore.EF"`), chosen deliberately over a Roslyn analyzer because it structurally, automatically distinguishes the P-316 bug shape (a direct, client-side-evaluated `EF.Property<T>` call, compiling to a `Call` opcode) from the platform's one legitimate `EF.Property<T>` pattern (used inside a `HasQueryFilter(Expression<Func<TEntity,bool>> filter)` global query filter, which the C# compiler lowers to `Expression`-builder calls with no `Call` opcode against `EF.Property` at all) — without needing to duplicate the compiler's own expression-tree-conversion determination inside a Roslyn analyzer's semantic-model logic. No new SK diagnostic ID — follows `EfCorePackageHygieneRules`'s existing zero-SK-ID shape (all four predicates now carry none), not the SK0013/SK0020 Roslyn-analyzer shape the phase input's own motivating comparison cites. Carries NO exemption mechanism, mirroring `NoSpecificationEvaluatorDowncastInEfCoreAssembly`'s own zero-exemption precedent — the structural self-exemption for expression-tree-embedded usage is the only "exception," and it must be confirmed empirically against real compiled IL during implementation (not merely assumed from this design's prose), per this domain's established "confirmed empirically, not assumed" discipline (SK0025/`StorageTopologyRules`/`SearchTopologyRules` precedent). UNVERIFIABLE/GATING against the real, corrected assembly as of this phase's authoring — `06.Persistence` P-316 is planned in `06.Persistence/state-map.md` but not yet implemented, and the offending `EF.Property<TId>` call sites are still live in shipped source; design/implementation proceed now against four contrived in-memory Mono.Cecil fixtures, mirroring the `SK.00.SearchTopology`/`SK.00.IntelligenceTopology`/`SK.00.WorkflowTopology` GATING-not-deferred precedent (dispatched last in its run by the arch-lead specifically to respect this dependency direction) rather than a non-blocking follow-up; five new implementation rules added; one Cross-Domain Dependencies block added (`06.Persistence` P-316, explicitly marked gating) — WO-051 P-327, depends on 06.Persistence P-316 (governance-arch-planner)
- [2026-07-31] SK.00.EfPropertyUsageGuard → ● closeout — `NoDirectEfPropertyUsagePredicate` implemented in `Predicates/`; `EfCorePackageHygieneRules.NoDirectEfPropertyUsageInEfCoreAssembly` implemented as the fourth method in `Rules/EfCorePackageHygieneRules.cs`. Verified against the pre-written CLAUDE.md spec (architecture-test-contract entry, all Implementation Rules bullets) — no discrepancy in documented behavior. **STALE-DEPENDENCY CORRECTION (same class as SK.00.StorageTopology/SK.00.SearchTopology/SK.00.IntelligenceTopology/SK.00.WorkflowTopology):** by the time this phase was implemented, `06.Persistence`'s P-316 had already shipped — confirmed on disk, not assumed: `06.Persistence/state-map.md`'s C-101 is `●` Complete, and `TenantedRepository.cs`'s own source confirms both `GetByIdForTenantAsync`/`GetByIdForTenantIncludingDeletedAsync` now build the Id-equality predicate via the private static `BuildIdEqualsPredicate` helper (`Expression.Parameter`/`Property`/`Equal`/`Lambda`), with no direct `EF.Property<TId>` call remaining — the one surviving `EF.Property<bool>` call (the soft-delete re-filter) is passed as an `Expression<Func<T,bool>>` argument to `IQueryable<T>.Where`, itself the exact structural self-exemption shape T-274 proves. Real-assembly verification (this phase's own GATING acceptance criterion) is therefore DONE, not deferred: `SharedKernel.ArchitectureTests.Tests.csproj` gained a test-only `ProjectReference` (`PrivateAssets="all"`) to `SharedKernel.Persistence.EfCore` (confirmed building clean, 0 errors, via `dotnet build --configuration Release` before wiring the reference), and a new `Real*`-suffixed test (`NoDirectEfPropertyUsageInEfCoreAssembly_RealEfCoreAssembly_RulePasses`) confirms zero violations against the shipped assembly — no real violation surfaced. **GENUINE IMPLEMENTATION-TIME FINDING (T-272):** the first fixture draft for "EF.Property called inside a `Func<T,bool>` lambda" captured the `id` method parameter, which Roslyn compiles onto a nested `<>c__DisplayClass0_0` type — confirmed empirically via a temporary recording `ICustomRule` (added then removed) that NetArchTest's `Types.InAssembly(...).Should().MeetCustomRule(...)` scan visits only the top-level `ClientSideFilterFixture` type, never its nested display class — the same platform-documented NetArchTest closure-visibility gap already recorded for SK0012/`ReflectionGuardRules`/`MediatRDomainEventDispatcher` (WO-039 P-240), now confirmed to apply identically to this predicate (the predicate's own IL-walk logic was independently proven correct by direct invocation against the nested `TypeDefinition` during diagnosis). Fixed by redesigning the fixture so the lambda captures only `this` (via an instance field assigned immediately before the lambda runs) rather than a local/parameter — a `this`-only-capturing lambda compiles as a plain private instance method directly on the enclosing top-level type, which NetArchTest's scan does visit; recorded as a reusable fixture-design technique in Implementation Rules, not merely a limitation. 5 new tests (T-271–T-274 plus the real-assembly test) — 184/184 `SharedKernel.ArchitectureTests.Tests` pass (179 baseline + 5), 0 build warnings/errors introduced by this phase's own code. This is the last `○` phase key — every phase key in `00.Governance/state-map.md` is now `●`. Promoted to root `state-map.md` (Phase Backlog P-327 closed to `●` Complete) (state-map-phase, governance-phase-implementer)
- [2026-08-04] Phase SK.00.EventEnvelopeConstructionGuard added — WO-054 (P-350), depends on `07.Messaging` P-340 for real-assembly PASS-PATH verification only (the FIRE-path check against the CURRENT, uncorrected assembly is non-gating and can run immediately). New `ContractsLayeringRules` static class added to architecture test contracts — a NEW class rather than a fifth predicate on the existing `ContractsPurityRules`, since that class governs `04.Contracts`'s own internal purity, not how other assemblies must construct its types — with a single method, `NoDirectEventEnvelopeConstructionOutsideContracts`, backed by new `NoDirectEventEnvelopeConstructionPredicate`: a `Newobj` IL-opcode match on THREE conditions (`DeclaringType.Namespace == "SharedKernel.Contracts.Events"`, `DeclaringType.Name` equal to `EventEnvelope`'s generic-arity-suffixed simple name, and `Parameters.Count == 0`) — the domain's first constructor-arity discriminator on a generic-`Newobj` predicate, deliberately excluding `EventEnvelope<TEvent>`'s separate one-parameter record copy constructor (used by `with` expressions) from the zero-parameter object-initializer fire condition, since a `with` expression mutates an already-`Wrap`-constructed envelope rather than fabricating new envelope identity/routing metadata from scratch. No new SK diagnostic ID — `PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi` (WO-031 P-199) is the closer structural analog than the phase input's own cited SK0013/SK0020 Roslyn-analyzer precedents, and the phase's own acceptance criteria require verification against "real compiled assemblies," this domain's established IL/Mono.Cecil idiom rather than the Roslyn-analyzer "real-source audit" idiom. Motivated by this WO-054 review's finding that `07.Messaging`'s shipped `MassTransitEventPublisher.PublishEnvelope<TEvent>` violates `EventEnvelope<TEvent>`'s own XML-doc-mandated `EventEnvelope.Wrap<TEvent>()`-only construction contract and silently drops the `TenantId` field shipped for cross-service tenant routing (WO-052/P-331) — proof that a documented-only convention is not sufficient for the platform's one cross-service event wire format. Real-assembly verification is SPLIT, a first for this file: the fire-path check against the CURRENT, uncorrected `SharedKernel.Messaging.MassTransit` assembly is non-gating and runs immediately (proving the rule catches the real, already-shipped defect that motivated this phase), while only the pass-path check against the eventual P-340-corrected assembly is GATING; five new implementation rules added; one Cross-Domain Dependencies block added (`07.Messaging` P-340, pass-path verification only) — WO-054 P-350, depends on 07.Messaging P-340 (governance-arch-planner)
- [2026-08-07] SK.00.EventEnvelopeConstructionGuard → ● closeout — `NoDirectEventEnvelopeConstructionPredicate` implemented in `Predicates/`; `ContractsLayeringRules.NoDirectEventEnvelopeConstructionOutsideContracts` implemented as a new class in `Rules/`. Verified against the pre-written CLAUDE.md spec (architecture-test-contract entry, all six Implementation Rules bullets) — no discrepancy in documented behavior; the three-condition `Newobj` match (`Namespace == "SharedKernel.Contracts.Events"`, `Name == "EventEnvelope`1"`, `Parameters.Count == 0`) was confirmed empirically before implementation, per this domain's "confirmed empirically, not assumed" discipline: a temporary Mono.Cecil IL-dump probe (compiled and run via `dotnet test`, then deleted) against a real fixture proved `new EventEnvelope<TEvent> { ... }` emits exactly the predicted `Newobj` shape, `EventEnvelope.Wrap<TEvent>()` calls emit only a `Call` (no `Newobj` at all), and — a genuine discovery not anticipated by the design prose — a `with` expression compiles to a `Callvirt` against the compiler-synthesized `<Clone>$` method, never a `Newobj`, so the record's private one-parameter copy constructor is never even reachable from a caller's own IL (external code cannot legally `newobj` a `private` constructor — CS0122). This makes the `Parameters.Count == 0` condition a defensive-but-structurally-unreachable discriminator for real caller code — documented as such in the predicate's XML doc rather than silently treated as load-bearing. **STALE-DEPENDENCY CORRECTION (same class as `SK.00.StorageTopology`/`SK.00.SearchTopology`/`SK.00.IntelligenceTopology`/`SK.00.WorkflowTopology`/`SK.00.EfPropertyUsageGuard`, but the first in this file where the dependency resolved AFTER the phase's own authoring date rather than merely before implementation began):** `07.Messaging`'s P-340 (`SK.07.EnvelopeTenancy`, ET-04) shipped 2026-08-05 — confirmed on disk, not assumed: `07.Messaging/state-map.md` shows ET-01–ET-09 all `●` Complete, and `MassTransitEventPublisher.cs`'s own source confirms `PublishEnvelope<TEvent>` now constructs the envelope exclusively via `EventEnvelope.Wrap(...)`, with no raw object-initializer remaining. T-279 (the NON-GATING fire-path test against the CURRENT real assembly) was therefore UNSATISFIABLE AS WRITTEN — there is no longer a live violation to reproduce. Per this phase's own explicit instruction (do not force a red test or weaken the rule to manufacture a fire path against real code), T-279 was not executed as originally specified; the contrived fire-path fixture (T-275, mirroring the exact real, historical `MassTransitEventPublisher.PublishEnvelope<TEvent>` object-initializer shape field-by-field) remains the PRIMARY red proof, exactly as the phase's own dependency note anticipated as the fallback. T-280 (the GATING pass-path test) is DONE, not deferred — a single test, `ContractsLayeringRulesTests.NoDirectEventEnvelopeConstructionOutsideContracts_RealMassTransitAssembly_RulePasses`, discharges both T-279's and T-280's real-assembly obligations: it points the rule at the real, already-corrected `SharedKernel.Messaging.MassTransit` assembly and confirms zero violations. No `.csproj` change was needed for this — `SharedKernel.ArchitectureTests.Tests.csproj` already carried a test-only `ProjectReference` to `SharedKernel.Messaging.MassTransit` (added for `RedisTopologyRulesTests`, WO-023 P-145), so the File-Level Plan's own anticipated csproj-modification task was a no-op once verified against the actual project file. Cross-Domain Dependencies row for `SK.00.EventEnvelopeConstructionGuard`/`07.Messaging` P-340 corrected in `state-map.md` from `○` Pending to `●` Resolved with evidence; T-279's task row amended to record the invalidated premise and its actual disposition; T-280's task row confirmed GATING-satisfied. Two-assembly contrived-fixture technique used for T-275–T-278 (a "Contracts-stub" assembly containing the stubbed `EventEnvelope`/`Wrap` types, separate from the "caller" assembly under test) — required, not stylistic: `EventEnvelope.Wrap`'s own stub implementation legitimately contains a `Newobj`, so a single combined fixture assembly would produce a false violation against the stub factory itself rather than the caller under test; this mirrors the real caller-controlled-exclusion (the real `SharedKernel.Contracts.dll` is never passed to the rule either). 5 new tests (T-275–T-278 plus the one combined real-assembly test) — 189/189 `SharedKernel.ArchitectureTests.Tests` pass (184 baseline + 5), 0 build warnings/errors introduced by this phase's own code. This is the last `○` phase key — every phase key in `00.Governance/state-map.md` is now `●` (governance-phase-implementer, state-map-phase)
- SK0031 `RawSecurityContextConstructorInjectionAnalyzer` (WO-057 P-373) mirrors SK0013's exact syntax-only shape — a `ConstructorDeclarationSyntax` parameter simple-name check against `{"IHttpContextAccessor","ClaimsPrincipal","HttpContext"}` with a `SyntaxNode.Parent` namespace-ancestor exemption walk. Unlike SK0013's single exemption namespace, SK0031 checks TWO prefixes (`SharedKernel.Security.Oidc`, `SharedKernel.Security.ApiKey`) — the second is forward-looking and currently vacuous (no `SharedKernel.Security.ApiKey` package exists in the platform as of this phase, which ships only `.Abstractions` and `.Oidc`); do not remove it speculatively, but revise it the moment an actual API-key provider package ships under a different name. **CORRECTED AT IMPLEMENTATION TIME (2026-08-13, SK.00.SecurityContextGuard closeout):** by the time this phase was implemented, `12.Security` had already shipped `SharedKernel.Security.ApiKey` (WO-057's P-366–P-372, v1.0.0) — confirmed on disk, not assumed. The "forward-looking and currently vacuous" framing above is therefore stale; `SharedKernel.Security.ApiKey` is a real, published sibling provider package. No analyzer code change was needed — the exemption is a plain namespace-prefix string match, already generic enough to work against the real package the moment it existed. Left as originally drafted rather than rewritten in place, per this domain's "annotate, never silently rewrite" convention.
- `SecurityArchitectureRules` (WO-057 P-373) is the platform's first dedicated architecture-rule class for `12.Security` — mirrors `MessagingArchitectureRules`'s "protect this domain's hard rules mechanically" shape rather than a `*TopologyRules` shape, since `12.Security` has no sibling provider packages to check for cross-reference (only `.Abstractions` and `.Oidc`).
- `NoTenantProviderReferenceInDomainPredicate` reuses the established three-surface (fields, constructor/method parameters, instruction-operand types) inspection technique from `NoDbContextTransactionInApplicationPredicate` — no new Mono.Cecil technique introduced by this predicate.
- `NoSecurityContextSingletonRegistrationPredicate` introduces this domain's NEWEST Mono.Cecil technique — generic-instance-method-argument inspection (`GenericInstanceMethod.GenericArguments`) — distinct from every prior technique catalogued in this file. It is the IL-level architecture-test analogue of SK0703's syntax-level `GenericNameSyntax.TypeArgumentList` extraction, applied here because this phase's own acceptance criteria explicitly call for an "architecture-test rule," not an analyzer, for the singleton-registration check (unlike SK0703/SK0023, which are Roslyn analyzers for their respective singleton/non-singleton misregistration classes).
- `NoSecurityContextSingletonRegistrationPredicate` carries a documented limitation: it detects only the closed-generic `AddSingleton<TService>(...)`/`AddSingleton<TService,TImpl>(...)` call shapes, not the non-generic `AddSingleton(Type, Type)`/`AddSingleton(Type, Func<...>)` overloads — mirroring `HealthCheckTagIntegrityRules`'s own documented data-flow limitation; revisit only if a real false negative is found in production DI wiring.
- Both `SecurityArchitectureRules` factory methods reuse the existing `Mono.Cecil >= 0.11.5` reference already in `SharedKernel.ArchitectureTests` — zero new NuGet dependency. Neither carries an SK diagnostic ID — following the `EfCorePackageHygieneRules`/`RedisTopologyRules` "boundary/regression rule, no ID" convention; only the constructor-injection check (SK0031) is a Roslyn analyzer with an assigned ID.
- Real-assembly status for all three `SK.00.SecurityContextGuard` rules is NON-GATING and immediately verifiable — unlike most recent phases in this file, `12.Security` (both `.Abstractions` and `.Oidc`) and `03.Domain` are both already fully Published as of this phase's authoring (`12.Security/CLAUDE.md`'s own changelog records `SK.12.Published complete`; `03.Domain` shipped `SharedKernel.Domain` v1.7.0 at WO-051). Real-assembly tests should be wired in the implementation phase, not deferred as a follow-up — no Cross-Domain Dependencies entry is needed.
- `SecurityArchitectureRules.DpopProofValidationNeverDuplicatedOutsideOidc`/`.ClientCertificateAccessNeverDuplicatedOutsideMtls` (WO-058 P-383) extend the EXISTING `SecurityArchitectureRules` class from P-373 — not a new class — with two more locality predicates, applying the `SK.00.SecurityContextGuard` lesson proactively: mechanize "this validation logic lives in exactly one package" for a new sender-constraining credential mechanism BEFORE it ships, rather than waiting for a future gold-standard review to discover the drift. Neither carries an SK diagnostic ID — both are `NetArchTest` `ICustomRule` predicates, per this phase's own acceptance criteria, following the same "boundary/regression rule, no ID" convention as `SecurityArchitectureRules`'s first two methods; SK0032 remains the next available sequential Roslyn-analyzer ID, not consumed here.
- `NoDpopProofValidationDuplicationPredicate` checks TWO independent surfaces (Ldstr `"DPoP"` literal — reusing `HealthCheckTagIntegrityRules`'s Ldstr literal-collection technique; JWT-proof-parsing type reference on `JwtSecurityTokenHandler`/`JsonWebTokenHandler` — reusing SK0301's raw-type-reference technique) inside a single predicate, mirroring `NoTenantProviderReferenceInDomainPredicate`'s "multiple detection surfaces feeding one predicate" shape. Exemption: `SharedKernel.Security.Oidc` only — no second forward-looking exemption prefix, unlike SK0031's two-namespace shape, since DPoP is exclusively an OIDC/JWT-bearer-adjacent concern.
- `NoRawClientCertificateAccessOutsideMtlsPredicate` is a single-surface match on `ConnectionInfo.get_ClientCertificate`'s property-getter call — deliberately NOT a broad `X509Certificate2` type-reference check, to avoid a false positive against `01.Core/SharedKernel.Cryptography`'s unrelated `IAsymmetricSignatureService` X.509-adjacent signing/verification code. Exemption: `SharedKernel.Security.Mtls` only — the new sibling provider package `12.Security`'s P-377 will ship.
- Both `SK.00.SenderConstrainedCredentialGuard` rules are UNVERIFIABLE against real assemblies as of this phase's authoring — `12.Security`'s P-376 (DPoP) and P-377 (`SharedKernel.Security.Mtls`) are planned but not yet implemented, dispatched deliberately last in WO-058's dependency order for that reason. Design, implementation, and all contrived fire/pass-path fixture tests proceed now; real-assembly re-verification is tracked as a Cross-Domain Dependencies entry, not force-failed or silently deferred — mirroring the `SK.00.MagicStringGuard`/`SK.00.EfPropertyUsageGuard` precedent for a not-yet-implemented dependency.
- `SecureDefaultsAssertion` (WO-060 P-390) is the domain's FOURTH non-`ConditionList`/`ICustomRule` public helper, after `ApplicationPipelineRules.PipelineOrderAssertion`, `LoggingEventIdIntegrityAssertion`, and `WellKnownConstantOwnershipAssertion` — chosen because "a specific type's specific property resolves to a specific default value" has no single-assembly assembly-dependency shape a `ConditionList` expresses, and the motivating P-385/P-386 defect (`MtlsAuthenticationOptions` shipping `CertificateTypes.All`/`X509RevocationMode.NoCheck`) was a correctly-shaped, syntactically unremarkable property initializer carrying the wrong constant — no source-level anti-pattern for a Roslyn analyzer to target either.
- `SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals` introduces this domain's NEWEST Mono.Cecil technique — constructor/property-initializer ENUM-DEFAULT-VALUE RESOLUTION: locates the parameterless instance constructor's `Ldc_I4`-then-`Stfld` sequence targeting a named property's compiler-generated backing field, and resolves the loaded integral constant against the property's enum type's own `Fields` (excluding `value__`). This is a DIFFERENT technique from `StringConstantsClassDetector`'s field-shape+literal-value resolution (which resolves `const`/`static readonly string` FIELDS directly, never a property auto-initializer assigned inside a constructor body, and never an enum's integral constant).
- `SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes` extends the established `Ldstr` literal-collection technique (`HealthCheckTagIntegrityRules`/`MetricsInstrumentationRules`) to a NEW call-site shape — a constructor-body array/collection-initializer feeding a property's backing-field `Stfld`, rather than a method-name-prefix-scoped call site. A `null` default and an explicit empty-array default (`Array.Empty<string>()`/`new string[0]`) both produce zero collected `Ldstr` operands and are caught by the SAME non-empty check — no separate detection logic is needed for "empty" vs. "null," satisfying both halves of the phase's own acceptance-criterion wording with one check.
- Both `SecureDefaultsAssertion` methods reuse the existing `Mono.Cecil >= 0.11.5` reference already in `SharedKernel.ArchitectureTests` — zero new NuGet dependency. Neither method carries an SK diagnostic ID — following the `SecurityArchitectureRules`/`PipelineOrderAssertion`/`LoggingEventIdIntegrityAssertion`/`WellKnownConstantOwnershipAssertion` "boundary/regression rule, no ID" convention; SK0032 remains the next available sequential Roslyn-analyzer ID, not consumed here.
- `SecureDefaultsAssertion` is called with `typeof(MtlsAuthenticationOptions)` for `AllowedCertificateTypes` (expected `"Chained"`) and `RevocationMode` (expected `"Online"` since P-546, matching ASP.NET Core's own certificate-authentication default; the WO-060/P-386 correction had set `"Offline"`) — replacing the shipped WO-058 `"All"`/`"NoCheck"` defaults (T-309). **P-546 (2026-09-16):** the JWS algorithm lock (T-310) no longer calls `AssertStringCollectionPropertyDefaultExcludes` — `SecurityOptions` is gone, and `OidcAuthenticationOptions.ValidAlgorithms`/`DpopOptions.ValidAlgorithms` default to empty because configuration binding appends to a non-empty default list. T-310 builds the real `AddOidcAuthentication(configuration)` and asserts the configured `JwtBearerOptions.TokenValidationParameters.ValidAlgorithms` is non-empty and excludes `{"none", "HS256", "HS384", "HS512"}`, and that configuring any of those values throws `OptionsValidationException` at startup validation. **CORRECTED AT IMPLEMENTATION TIME (2026-08-18, `SK.00.SecureDefaultsLock` closeout):** "once implemented against real types" is stale — both real-assembly calls are implemented now, as GATING tests (see the correction bullet immediately below).
- **CORRECTED AT IMPLEMENTATION TIME (2026-08-18, `SK.00.SecureDefaultsLock` closeout):** the bullet above originally read "Both `SK.00.SecureDefaultsLock` checks are UNVERIFIABLE/non-gating on this phase's own completion... `12.Security`'s P-386... and P-387... are both `○ QUEUED`/design-locked but not yet dispatched for implementation." Verified false at implementation time, mirroring `SK.00.SenderConstrainedCredentialGuard`'s own immediately-preceding closeout correction in the same WO-060 run: `12.Security` had already shipped its full WO-060 scope (`12.Security/state-map.md`'s C-39/C-40/C-41 all `●` Complete) before this phase's implementation session began. Real-assembly re-verification is IMPLEMENTED (not deferred) as GATING tests in `SecureDefaultsAssertionTests` — `AssertEnumPropertyDefaultEquals_RealMtlsAuthenticationOptions_HardenedDefaultsHold` and `AssertStringCollectionPropertyDefaultExcludes_RealOidcAllowlists_HardenedDefaultsHold` — both confirmed non-vacuous via a temporary sanity check (deliberately-wrong expectations against the same real types, confirmed to fail, then removed before commit). [P-546: the Oidc test is now `AddOidcAuthentication_RealAssembly_RejectsSymmetricAndNoneAlgorithms`, asserting configured `JwtBearerOptions` — see the bullet above.]
- `SK.00.SyncCryptoGateAndArgon2ConfinementLock`'s Technique A (WO-081 P-504) is this class's TENTH real-world application, reusing `AssertMethodBodyInvokesMethod`/`AssertMethodBodyThrowsExceptionType` unchanged against `01.Core`'s `AesGcmEncryptionService`/`RsaSignatureService`/`EcdsaSignatureService` synchronous-provider gate (P-492/P-493) — IMPLEMENTED, not deferred: `01.Core` had already shipped P-492/P-493 past Design into Core before this phase's implementation session began, resolving what its own authoring text honestly recorded as "fully unverifiable, one level further removed than every prior occurrence." Real, shipped source also corrected the design: each gated type centralizes its throw in one private `ThrowIfNotGenuinelySynchronous` helper, not four/two independent throw sites — see the class's own "Tenth real-world application" entry above for the full record, including the deliberate scoping that avoids flagging `07.Messaging`'s legitimate hard-synchronous serializer call site.
- `CryptoIsolationRules.CryptographyCoreHasNoThirdPartyDependencies` (WO-081 P-504) is this domain's first `00.Governance`-owned, NetArchTest-level re-verification of a Cryptography sibling-package dependency-confinement guarantee — previously checked only via `01.Core`'s own `.nuspec`-inspection technique in `SharedKernel.Consumer.Tests`, a different project and a different technique. Fully ungated: it targets the already-shipped `SharedKernel.Cryptography` core assembly, needing neither `SharedKernel.Cryptography.Argon2` (P-495) nor the synchronous-provider gate (P-492/P-493) to exist. Its regression proof (T-365) is a permanent compiled-in-memory-fixture test, not a real `Konscious` `PackageReference` mutation as originally designed — this repo's NuGet Central Package Management disables per-project `VersionOverride` platform-wide (confirmed via a real, fully-reverted attempt, `error NU1013`), so the originally-prescribed technique was infeasible without editing a root build-configuration file outside this domain's jurisdiction; see the method's own entry above for the full record.
- [2026-08-13] SK.00.SecurityContextGuard → ● closeout — `RawSecurityContextConstructorInjectionAnalyzer` (SK0031) implemented in `SharedKernel.Analyzers/Diagnostics/`, mirroring SK0013's `ConstructorDeclarationSyntax`-parameter simple-name-match + `SyntaxNode.Parent` namespace-ancestor walk exactly, extended to three forbidden type names (`IHttpContextAccessor`/`ClaimsPrincipal`/`HttpContext`) and two exemption prefixes. `NoTenantProviderReferenceInDomainPredicate` and `NoSecurityContextSingletonRegistrationPredicate` implemented in `Predicates/`; `SecurityArchitectureRules` implemented as a new class in `Rules/` with both factory methods. Verified against the pre-written CLAUDE.md spec (diagnostic registry, architecture test contracts, all six Implementation Rules bullets) — no discrepancy found, no code-behavior edits required. **STALE-PROSE CORRECTION (verified against real disk state, not assumed):** `SharedKernel.Security.ApiKey` now genuinely exists — `12.Security` shipped WO-057's P-366–P-372 in full (v1.0.0) since this phase was authored — so the SK0031 diagnostic-registry Note and the corresponding Implementation Rules bullet describing the `SharedKernel.Security.ApiKey` exemption prefix as "forward-looking and currently vacuous" are now stale; corrected in place with "CORRECTED AT IMPLEMENTATION TIME" annotations (per this domain's "annotate, never silently rewrite" convention) rather than rewritten — no analyzer code change was needed, since the exemption is a plain namespace-prefix string match that already worked generically against the real package. Real-assembly verification (non-gating, immediately available since both `12.Security` and `03.Domain` were already Published) was wired directly rather than deferred: `SharedKernel.ArchitectureTests.Tests.csproj` gained two test-only `ProjectReference`s (`PrivateAssets="all"`) to `SharedKernel.Security.Abstractions`/`.Oidc`; `SecurityArchitectureRulesTests.DomainNeverReferencesTenantProvider_RealDomainAssembly_RulePasses` and `.NoSingletonRegistrationOfSecurityContextTypes_RealOidcAssembly_RulePasses` both confirm zero violations against the real, shipped assemblies — `SharedKernel.Domain` has no dependency on `12.Security` at all, and the real `SecurityServiceCollectionExtensions.RegisterUserContextAndTenantProvider` registers both `IUserContext`/`ITenantProvider` via `AddScoped`, never `AddSingleton`. One fixture-authoring fix during T-288/T-289: the initial DI-registration fixtures called the stub `AddSingleton`/`AddScoped` extension methods via dot-syntax without a `using` directive in scope, producing a genuine `CS1061` compile failure — fixed by calling the stub methods via their static form, which compiles to the identical `Call`-to-`GenericInstanceMethod` IL shape the predicate inspects either way. 7 new analyzer tests (T-281–T-285 plus two extra pass-path cases) — 240/240 `SharedKernel.Analyzers.Tests` pass (233 baseline + 7); 6 new architecture tests (T-286–T-291) — 195/195 `SharedKernel.ArchitectureTests.Tests` pass (189 baseline + 6); 0 build warnings/errors introduced by this phase's own code. This is the last `○` phase key — every phase key in `00.Governance/state-map.md` is now `●`. Promoted to root `state-map.md` (Phase Backlog P-373 closed to `●` Complete) (governance-phase-implementer, state-map-phase, sync-brain)
- [2026-08-13] Phase SK.00.SenderConstrainedCredentialGuard added — WO-058 (P-383), depends on `12.Security` P-376 (DPoP) and P-377 (new `SharedKernel.Security.Mtls` sibling provider package) for real-assembly verification only, both dispatched to `security-arch-planner` in the same run but NOT yet implemented as of this phase's authoring — dispatched deliberately last in WO-058's dependency order for that reason. Applies `SK.00.SecurityContextGuard`'s (P-373) own lesson proactively rather than retroactively: mechanize a "this validation logic lives in exactly one package" locality rule for each of two new sender-constraining credential mechanisms BEFORE either ships, instead of waiting for a future gold-standard review to discover the drift. Extends the EXISTING `SecurityArchitectureRules` class (not a new class, per this phase's own explicit instruction) with two more zero-SK-ID `ICustomRule`-backed factory methods: `DpopProofValidationNeverDuplicatedOutsideOidc` (two-surface match inside `NoDpopProofValidationDuplicationPredicate` — an `Ldstr "DPoP"` literal, reusing `HealthCheckTagIntegrityRules`'s Ldstr literal-collection technique, OR a `JwtSecurityTokenHandler`/`JsonWebTokenHandler` type reference, reusing SK0301's raw-type-reference technique — outside `SharedKernel.Security.Oidc`, single exemption namespace, no `SharedKernel.Security.ApiKey`-style second forward-looking prefix since DPoP is exclusively OIDC/JWT-bearer-adjacent) and `ClientCertificateAccessNeverDuplicatedOutsideMtls` (single-surface match inside `NoRawClientCertificateAccessOutsideMtlsPredicate` — a `ConnectionInfo.get_ClientCertificate` property-getter call outside `SharedKernel.Security.Mtls`; deliberately NOT a broad `X509Certificate2` type-reference check, to avoid a false positive against `01.Core/SharedKernel.Cryptography`'s unrelated `IAsymmetricSignatureService` X.509-adjacent signing/verification code). No new SK diagnostic ID — both are architecture-test `ICustomRule` predicates per this phase's own acceptance criteria, not Roslyn analyzers; SK0032 remains the next available sequential ID, unconsumed. Both rules are UNVERIFIABLE against real assemblies as of this phase's authoring; design, implementation, and all eight contrived fire/pass-path fixture tests (T-292–T-298) proceed now, per this phase's own explicit non-gating acceptance criterion, and the two real-assembly re-verification tests (T-299, T-300) are tracked as a new Cross-Domain Dependencies block, not silently deferred. Five new implementation rules added — WO-058 P-383, depends on 12.Security P-376/P-377 (governance-arch-planner)
- [2026-08-17] Phase SK.00.SecureDefaultsLock added — WO-060 (P-390), depends on `12.Security` P-386 (`MtlsAuthenticationOptions` corrected defaults) and P-387 (JWS signing-algorithm allowlist) for real-assembly verification only — both dispatched to `security-arch-planner` in the same `/dispatch-phase` run immediately before this phase and, as of authoring, `○ QUEUED`/design-locked, not yet dispatched for implementation. Motivated by two configuration defects (P-385, P-386) found inside already-reviewed, already-tested, already-published WO-058 `12.Security` code — `MtlsAuthenticationOptions` shipped `AllowedCertificateTypes = CertificateTypes.All`/`RevocationMode = X509RevocationMode.NoCheck`, weaker than plain ASP.NET Core's own framework default — extending `SK.00.SecurityContextGuard`'s (P-373) "documented convention alone drifts" lesson from code-shape/call-site conventions to DEFAULT VALUES themselves: nothing today asserts a default-constructed options instance's property values, so a future edit could reintroduce a weaker default with every existing test still green. Introduces a NEW public helper class, `SecureDefaultsAssertion` — the domain's fourth non-`ConditionList`/`ICustomRule` helper after `PipelineOrderAssertion`/`LoggingEventIdIntegrityAssertion`/`WellKnownConstantOwnershipAssertion` — with two methods, neither carrying an SK ID (SK0032 remains next-available, unconsumed): `.AssertEnumPropertyDefaultEquals` introduces this domain's newest Mono.Cecil technique (constructor/property-initializer enum-default-value resolution); `.AssertStringCollectionPropertyDefaultExcludes` extends the established `Ldstr` literal-collection technique to a new call-site shape (a constructor-body array/collection-initializer feeding a property's backing field), asserting the collected default set is non-empty (covering both `null` and explicit-empty-array shapes via one check) and excludes a caller-supplied forbidden set (`"none"` plus symmetric algorithms). Both checks are UNVERIFIABLE/non-gating on this phase's own completion, mirroring `SK.00.SenderConstrainedCredentialGuard`'s immediately-preceding precedent exactly (same WO-060 run, same "not yet dispatched" reason) — design, implementation, and ten contrived fire/pass-path fixture tests proceed now; the two real-assembly re-verification tests are tracked as a new Cross-Domain Dependencies block. Six new implementation rules added — WO-060 P-390, depends on 12.Security P-386/P-387 (governance-arch-planner)
- [2026-08-18] SK.00.SenderConstrainedCredentialGuard → ● closeout — `NoDpopProofValidationDuplicationPredicate`/`NoRawClientCertificateAccessOutsideMtlsPredicate` implemented in `Predicates/`; `SecurityArchitectureRules.DpopProofValidationNeverDuplicatedOutsideOidc`/`.ClientCertificateAccessNeverDuplicatedOutsideMtls` added as two new methods on the EXISTING `SecurityArchitectureRules` class (not a new class). Verified against the pre-written CLAUDE.md spec (Architecture Test Contracts entries, all five Implementation Rules bullets) — zero discrepancy found, no code-behavior edits required. **STALE-DEPENDENCY CORRECTION (verified against real disk state, not assumed — per this phase's own explicit "CRITICAL" instruction):** this phase's own authoring-time prose, and the root Cross-Domain Dependencies block, claimed `12.Security` P-376/P-377 were "not yet dispatched for implementation." By implementation time `12.Security` had already shipped its full WO-058 **and** WO-060 scope earlier in the same overall session — confirmed on disk: `SharedKernel.Security.Oidc` packed at `4.0.0` ships a real `Dpop/DpopProofValidator.cs` (full RFC 9449, including the `ath` binding); `SharedKernel.Security.Mtls` packed at `2.0.0` ships real client-certificate validation. T-299/T-300 were therefore implemented as the GATING real-assembly checks they were always specified to be, not deferred — both the two Architecture Test Contracts "UNVERIFIABLE against real assemblies" passages and `00.Governance/state-map.md`'s Cross-Domain Dependencies block were corrected in place with "CORRECTED AT IMPLEMENTATION TIME" annotations (this domain's "annotate, never silently rewrite" convention) rather than rewritten. Both real-assembly passes were verified NON-VACUOUS, not merely green — a finding this phase's own Design Notes explicitly asked for: T-299's real `DpopProofValidator` genuinely contains both detection surfaces (a const-inlined `Ldstr "DPoP"` and a `JsonWebTokenHandler` field) and passes only because the `SharedKernel.Security.Oidc` namespace exemption covers it — a real, exercised exemption boundary. T-300 is a real pass for a different, equally legitimate reason: the shipped `MtlsAuthenticationHandler` never calls `ConnectionInfo.get_ClientCertificate` directly — it reads the framework-already-resolved `CertificateValidatedContext.ClientCertificate` (a different declaring type the predicate deliberately does not match), because the ASP.NET Core certificate-authentication middleware itself resolves the certificate before invoking this package's handler — so Mono.Cecil correctly reaches and evaluates every real method body, the exemption boundary just is not exercised by this particular package's code. `SharedKernel.ArchitectureTests.Tests.csproj` gained one new test-only `ProjectReference` (`PrivateAssets="all"`) to `SharedKernel.Security.Mtls` (`.Oidc` was already referenced from `SK.00.SecurityContextGuard`/P-373). 9 new architecture tests (T-292–T-300) — 204/204 `SharedKernel.ArchitectureTests.Tests` pass (195 baseline + 9), 0 build warnings/errors introduced by this phase's own code (pre-existing NU1903 security-advisory warnings from `SharedKernel.Security.Oidc`'s transitive `System.Security.Cryptography.Xml` dependency, and pre-existing CS8509/CS8524 exhaustiveness downgrades in `09.Search`/`10.Intelligence`, are unrelated to this phase). No new SK diagnostic ID — SK0032 remains the next available sequential ID, still unconsumed. `SK.00.SecureDefaultsLock` remains `○` — this is NOT the last open phase key in `00.Governance/state-map.md`. Promoted to root `state-map.md` (Phase Backlog P-383 closed to `●` Complete) (governance-phase-implementer, state-map-phase, sync-brain)
- [2026-08-18] SK.00.SecureDefaultsLock → ● closeout — `SecureDefaultsAssertion` implemented as a new public class in `SharedKernel.ArchitectureTests` (domain-root, alongside `PipelineOrderAssertion`/`LoggingEventIdIntegrityAssertion`/`WellKnownConstantOwnershipAssertion`), with both `.AssertEnumPropertyDefaultEquals` and `.AssertStringCollectionPropertyDefaultExcludes`. Verified against the pre-written CLAUDE.md spec (both technique paragraphs, the empty/null-default and forbidden-value detection paragraphs) — one deliberate implementation refinement from the drafted design, not a discrepancy: enum MEMBER-NAME resolution is done via ordinary reflection (`Enum.GetName` against the property's own live `PropertyInfo.PropertyType`) rather than a second Mono.Cecil `TypeReference.Resolve()` hop, because the property's enum type may be declared in a framework-shared assembly (`Microsoft.AspNetCore.Authentication.Certificate`'s `CertificateTypes`) not guaranteed resolvable as a standalone file the way Mono.Cecil's default resolver expects — Mono.Cecil is used only to read the constructor's integral constant, reflection resolves what that constant means; documented in the class's own XML doc rather than left implicit. Nested-type resolution (`SecurityOptions.JwtOptions`) required a dedicated Mono.Cecil-vs-reflection full-name-separator fix not explicitly called out in the design prose: .NET reflection separates a nested type with `+` while Mono.Cecil separates it with `/`, so type lookup walks the declaring-type chain matching simple names at each level instead of comparing `FullName` strings directly. **STALE-DEPENDENCY CORRECTION (verified against real disk state, not assumed — per this phase's own explicit "CRITICAL" instruction, mirroring `SK.00.SenderConstrainedCredentialGuard`'s immediately-preceding closeout correction in the same WO-060 run):** this phase's own authoring-time prose, the CLAUDE.md `SecureDefaultsAssertion` documentation block, and `00.Governance/state-map.md`'s Cross-Domain Dependencies block all claimed `12.Security` P-386/P-387 were `○ QUEUED`/not yet dispatched. By implementation time `12.Security` had already shipped its full WO-060 scope earlier in the same overall session — confirmed on disk: `12.Security/state-map.md`'s C-39 (`MtlsAuthenticationOptions.AllowedCertificateTypes = CertificateTypes.Chained`, `.RevocationMode = X509RevocationMode.Offline`), C-40 (`SecurityOptions.JwtOptions.ValidAlgorithms = ["PS256", "ES256"]`), and C-41 (`DpopOptions.ValidAlgorithms = ["PS256", "ES256"]`) are all `●` Complete, with `SharedKernel.Security.Mtls` packed `2.0.0` and `SharedKernel.Security.Oidc` packed `4.0.0`. T-309/T-310 were therefore implemented as the GATING real-assembly checks they were always specified to be, not deferred — the CLAUDE.md documentation block and `00.Governance/state-map.md`'s Dependencies/Cross-Domain Dependencies sections were corrected in place with "CORRECTED AT IMPLEMENTATION TIME" annotations (this domain's "annotate, never silently rewrite" convention) rather than rewritten. Both real-assembly passes were verified NON-VACUOUS: a temporary sanity-check test (added, run, then removed before commit — never left in the permanent suite) pointed each assertion at a deliberately WRONG expectation against the same real `MtlsAuthenticationOptions`/`SecurityOptions.JwtOptions`/`DpopOptions` types and confirmed all four failed, proving the real properties are genuinely read rather than silently skipped — the exact discipline this phase's own Design Notes demanded ("if it passes against both the right and wrong expectations, it isn't reading anything"). No `SharedKernel.ArchitectureTests.Tests.csproj` change was needed — test-only `ProjectReference`s to `SharedKernel.Security.Mtls`/`.Oidc` (`PrivateAssets="all"`) already existed from `SK.00.SecurityContextGuard`/`SK.00.SenderConstrainedCredentialGuard`. 11 new architecture tests (T-301–T-310, with T-306 split into two `[Fact]` methods for its two distinct empty/null cases) — 215/215 `SharedKernel.ArchitectureTests.Tests` pass (204 baseline + 11), 0 build warnings/errors introduced by this phase's own code. No new SK diagnostic ID — SK0032 remains the next available sequential ID, still unconsumed. This is the last `○` phase key — every phase key in `00.Governance/state-map.md` is now `●`. Promoted to root `state-map.md` (Phase Backlog P-390 closed to `●` Complete) (governance-phase-implementer, state-map-phase, sync-brain)
- [2026-08-19] Phase SK.00.TenantAndMtlsBoundaryLock added — WO-061 (P-401), depends on `13.ServiceDefaults` P-393 (`TenantResolutionOptions.StrategyOrder`'s corrected `[Claim, Header, Database]` default) and P-394 (`MtlsForwardedHeaderOptions.TrustedNetworks` trust-boundary allowlist + its P-395 startup warning) for real-assembly verification only — both dispatched to `servicedefaults-arch-planner` in the same run immediately before this phase and, as of authoring, Design-locked (`13.ServiceDefaults/state-map.md`'s D-20–D-27) but Scaffold/Core/Tests/Docs still Not Started. The third domain in a row (after `12.Security` twice, WO-058/WO-060) hit by the identical "hardened default/security log statement silently regresses with every existing test still green" bug class — `13.ServiceDefaults`'s WO-061 review found a cross-tenant-impersonation-shaped default-ordering bug (`StrategyOrder`'s old `[Header, Claim, Database]` let an unsigned `X-Tenant-Id` header outrank a verified JWT tenant claim) and a trust-boundary gap in the mTLS-forwarded-header path. Extends the EXISTING `SecureDefaultsAssertion` class (not a new class, mirroring the `SecurityArchitectureRules`-extension precedent) with two more methods, neither carrying an SK ID (SK0032 remains next-available, unconsumed): `.AssertStringCollectionPropertyDefaultEquals` reuses `AssertStringCollectionPropertyDefaultExcludes`'s Ldstr literal-collection sub-technique but performs an ORDER-SENSITIVE sequence-equality comparison instead of a forbidden-intersection check — required because `StrategyOrder`'s security property depends on relative order, not mere membership; `.AssertMethodBodyInvokesMethod` introduces this domain's newest Mono.Cecil technique, a METHOD-BODY INVOCATION-PRESENCE ASSERTION — the inverse of every prior technique in this file (all of which fail on an unwanted call site; this one fails on a MISSING expected call site), the correct shape for locking a `[LoggerMessage]`-generated warning call in place. Both checks are UNVERIFIABLE/non-gating on this phase's own completion, mirroring `SK.00.SenderConstrainedCredentialGuard`'s/`SK.00.SecureDefaultsLock`'s own precedent exactly, applied for the first time against `13.ServiceDefaults` rather than `12.Security` — design, implementation, and eight contrived fire/pass-path fixture tests (T-311–T-316 plus two extra boundary cases) proceed now; the two real-assembly re-verification tests (T-317, T-318) are tracked as a new Cross-Domain Dependencies block. Explicit implementation-order note carried into the phase's own Dependencies section: do not attempt T-317/T-318 until `13.ServiceDefaults/state-map.md` confirms P-393/P-394's Core phase (not merely Design) is complete. Six new implementation rules added — WO-061 P-401, depends on 13.ServiceDefaults P-393/P-394 (governance-arch-planner)
- [2026-08-20] Phase SK.00.CorsWildcardCredentialsGuard added — WO-062 (P-410), depends on `14.Presentation` P-404 (`AddSharedKernelCors` CORS policy convention builder) for real-assembly verification only — dispatched to `presentation-arch-planner` immediately before this phase and, per the dispatcher's own note, `◐` Dispatched (Design only as of authoring); `AddSharedKernelCors` is not yet implemented in source. Two mechanisms, mirroring `SK.00.SecurityContextGuard`'s/`SK.00.SenderConstrainedCredentialGuard`'s/`SK.00.SecureDefaultsLock`'s/`SK.00.TenantAndMtlsBoundaryLock`'s now-established "documented convention alone drifts, mechanize it" family: (1) a genuinely new Roslyn analyzer, `CorsWildcardOriginWithCredentialsAnalyzer` (SK0032 — the first sequential-block ID consumed since SK0031/WO-057), platform-wide, catching a raw `CorsPolicyBuilder.AllowAnyOrigin()`/`SetIsOriginAllowed(_ => true)` combined with `AllowCredentials()` on the same builder instance anywhere in the repository's own production source — both in a single fluent chain and across separate statements on the same local/parameter/field, since ASP.NET Core's `AddPolicy(name, builder => {...})` configuration delegate commonly uses the latter shape; (2) a SIXTH method on the EXISTING `SecureDefaultsAssertion` class (not a new class), `.AssertMethodBodyThrowsExceptionType` — this domain's newest Mono.Cecil technique, a METHOD-BODY THROW-PRESENCE ASSERTION (a `Newobj`-then-`Throw` presence check, sibling to `AssertMethodBodyInvokesMethod`'s call-presence check, reusing its proven closure-method-scanning extension) — verifying `AddSharedKernelCors`'s own startup guard genuinely constructs and throws its documented exception type when the dangerous combination is configured. Both checks are UNVERIFIABLE/non-gating on this phase's own completion: SK0032's contrived fire/pass-path fixture tests (7 rows) and `AssertMethodBodyThrowsExceptionType`'s two contrived fixture tests proceed now; the one real-assembly re-verification test is tracked as a new Cross-Domain Dependencies entry, not silently deferred. No new NuGet dependency in either package — SK0032 reuses `Microsoft.CodeAnalysis.CSharp`'s existing semantic-model APIs, `AssertMethodBodyThrowsExceptionType` reuses the existing `Mono.Cecil >= 0.11.5` reference. 14 new tasks (D-73 [1] + C-135–C-136 [2] + T-319–T-328 [10] + DO-45 [1]) — WO-062 P-410, depends on 14.Presentation P-404 (governance-arch-planner)
- [2026-08-19] SK.00.TenantAndMtlsBoundaryLock → ● closeout — `AssertStringCollectionPropertyDefaultEquals`/`AssertMethodBodyInvokesMethod` added as the third and fourth methods on the EXISTING `SecureDefaultsAssertion` class (not a new class). **STALE-DEPENDENCY CORRECTION (verified against real disk state, not assumed, per this phase's own explicit instruction — the fourth occurrence of this exact class of finding in this file, after `SK.00.SenderConstrainedCredentialGuard`/`SK.00.SecureDefaultsLock` twice):** this phase's own authoring-time prose, and `00.Governance/state-map.md`'s Cross-Domain Dependencies block, claimed `13.ServiceDefaults` P-393/P-394/P-395 were Design-locked only. Verified false at implementation time: `13.ServiceDefaults` had already shipped its full WO-061 scope (171/171 `SharedKernel.ServiceDefaults.Tests` + 51/51 `SharedKernel.MultiTenancy.Tests` green, root Phase Backlog P-393–P-400 all `●` Complete) before this phase's implementation session began. T-317/T-318 were therefore implemented as the GATING real-assembly checks the state-map's own dispatch note flagged as newly-live, not left deferred — both the CLAUDE.md documentation block and `00.Governance/state-map.md`'s Dependencies/Cross-Domain Dependencies sections were corrected in place with "CORRECTED AT IMPLEMENTATION TIME" annotations rather than rewritten. **GENUINE IMPLEMENTATION-TIME FINDING, not anticipated by the design prose:** `AssertMethodBodyInvokesMethod`'s originally-specified plain single-method-body scan is insufficient for the real `AddMtlsForwardedHeaderCertificate` call site — confirmed by direct Mono.Cecil inspection (a temporary probe program, run then discarded) that the `ServiceDefaultsLog.ForwardedHeaderTrustBoundaryUnconfigured` call lives inside the C# `.PostConfigure<ILoggerFactory>(...)` lambda argument, which Roslyn compiles to its own method on a nested `<>c` compiler-generated closure type (`<AddMtlsForwardedHeaderCertificate>b__0_0`) — the enclosing method's own IL contains only a delegate-construction sequence (`ldftn`/`newobj`), never the callee call itself. The technique was extended (not redesigned) to additionally scan every method on every nested type of `declaringType` whose name starts with `<{methodName}>b__` when the direct body scan finds nothing — a necessary extension, since without it this check could never pass against the one real call site WO-061 motivated it to protect. `AssertStringCollectionPropertyDefaultEquals`'s order-sensitive reuse of the existing backward-walk-then-reverse `Ldstr`-collection technique was confirmed correct against the real `TenantResolutionOptions` constructor's IL (a `newarr`/`dup`/`ldc.i4.N`/`ldstr`/`stelem.ref` collection-expression lowering, not a flat sequence of `Ldstr` instructions) without any code change — the existing technique already reconstructs forward order correctly for this IL shape. Both real-assembly tests use `Assembly.GetType(string)` rather than `typeof(...)` to resolve `ServiceDefaultsLog` as the callee-declaring type, since it is `internal` to `SharedKernel.ServiceDefaults` with no `InternalsVisibleTo` grant to this governance test project — `Assembly.GetType` resolves a `Type` object by name regardless of accessibility, and the helper only ever compares `FullName`, never invokes a member. `SharedKernel.ArchitectureTests.Tests.csproj` gained two new test-only `ProjectReference`s (`PrivateAssets="all"`) to `SharedKernel.MultiTenancy` and `SharedKernel.ServiceDefaults` (neither previously referenced by this project). Both real-assembly passes verified NON-VACUOUS via a temporary sanity-check test (added, run, confirmed to fail against a deliberately wrong expected order / a nonexistent callee method name, then removed before commit — never left in the permanent suite). 8 new contrived-fixture tests (T-311–T-316, with T-313/T-314 as two extra length-mismatch boundary cases) plus 2 real-assembly GATING tests (T-317/T-318) — 223/223 `SharedKernel.ArchitectureTests.Tests` pass (215 baseline + 8), 0 build warnings/errors introduced by this phase's own code. No new SK diagnostic ID — SK0032 remains the next available sequential ID, still unconsumed. This is the last `○` phase key — every phase key in `00.Governance/state-map.md` is now `●`. Promoted to root `state-map.md` (Phase Backlog P-401 closed to `●` Complete) (governance-phase-implementer, state-map-phase, sync-brain)
- [2026-08-20] SK.00.CorsWildcardCredentialsGuard → ● closeout — `CorsWildcardOriginWithCredentialsAnalyzer` (SK0032) implemented in `SharedKernel.Analyzers/Diagnostics/`, joining the platform's semantic-model-assisted analyzer family; `AnalyzerBase` gained a new `Security` category constant (first analyzer to use it — every prior analyzer used only `Usage`/`Design`). `AssertMethodBodyThrowsExceptionType` implemented as the sixth method on the existing `SecureDefaultsAssertion` class, proven via two contrived fixtures. **STALE-DEPENDENCY CORRECTION WITH A GENUINE DESIGN/REALITY MISMATCH (the fifth occurrence of this review cycle's "dependency already shipped by implementation time" finding, but the first where resolving the dependency did NOT simply unblock the originally-planned test):** `14.Presentation` had already shipped its full P-404 scope (`SharedKernel.Presentation.WebApi` packed `1.2.0`) before this implementation session began, but reading the real, shipped `Cors/CorsExtensions.cs`/`CorsPolicyOptionsValidator.cs` source directly found `AddSharedKernelCors` does not construct-and-throw a named exception at all — it registers `CorsPolicyOptionsValidator` (`IValidateOptions<CorsPolicyOptions>` → `ValidateOptionsResult.Fail(...)`) via `ValidateOnStart()`, so the real `OptionsValidationException` throw happens entirely inside `Microsoft.Extensions.Options`'s own framework IL at `IHost.StartAsync()` — never inside this assembly. `AssertMethodBodyThrowsExceptionType` was therefore structurally unable to ever pass against the real assembly, regardless of correctness — a shape this phase's design prose predated, not a defect in either domain's work. Resolved by re-pointing the already-shipped `AssertMethodBodyInvokesMethod` (P-401) at the real, internal `CorsPolicyOptionsValidator.Validate` instead (resolved via `Assembly.GetType(string)`, mirroring T-318's technique for an inaccessible internal type), asserting it genuinely calls `ValidateOptionsResult.Fail` — verified non-vacuous via a temporary deliberately-wrong-callee-name sanity check, confirmed to fail, then reverted before commit. `SharedKernel.Analyzers.Tests`'s SK0032 fixtures declare a minimal same-namespace/same-name `CorsPolicyBuilder` stand-in type directly in fixture source rather than referencing the real `Microsoft.AspNetCore.Cors` assembly, which produces a CS1705 version mismatch against this pinned testing package's netstandard2.0-vintage default reference assemblies. `SharedKernel.ArchitectureTests.Tests.csproj` gained one new test-only `ProjectReference` (`PrivateAssets="all"`) to `SharedKernel.Presentation.WebApi`. 7 new analyzer tests (247/247 `SharedKernel.Analyzers.Tests` pass) and 3 new architecture tests (226/226 `SharedKernel.ArchitectureTests.Tests` pass), 0 build warnings/errors introduced by this phase's own code. This is the last `○` phase key — every phase key in `00.Governance/state-map.md` is now `●`. Promoted to root `state-map.md` (Phase Backlog P-410 closed to `●` Complete) (governance-phase-implementer, state-map-phase, sync-brain)
- [2026-08-20] Phase SK.00.CorrelationIdValidationGuard added — WO-063 (P-420), depends on `14.Presentation` P-415 (`CorrelationIdMiddleware`'s new `CorrelationIdOptions.MaxLength`/`AllowedCharacterPattern` format-validation check) for real-assembly verification only — dispatched to `presentation-arch-planner` immediately before this phase and, confirmed directly against `14.Presentation/state-map.md`, `○` Design-only as of authoring (D-57/S-25/C-64/C-65 all `○` Not started). Applies this review cycle's now-five-times-repeated "a security-relevant boundary check documented and tested only inside its own producing domain drifts" lesson to `14.Presentation`'s correlation-id validation guard specifically, distinct from that same domain's CORS guard already locked by `SK.00.CorsWildcardCredentialsGuard`. UNLIKE every phase in this family before it, this one adds ZERO new production code to `SharedKernel.ArchitectureTests` and ZERO new SK ID (SK0033 remains next-available, unconsumed) — it is designed to reuse the two already-shipped Mono.Cecil techniques exactly as they stand: `AssertMethodBodyInvokesMethod` (WO-061/P-401) as the primary technique against `CorrelationIdMiddleware.ResolveCorrelationId`'s expected call to its validation helper, with `AssertMethodBodyThrowsExceptionType` (WO-062/P-410) documented as the fallback should the shipped guard instead throw — pre-emptively applying the exact design/reality-mismatch lesson `SK.00.CorsWildcardCredentialsGuard` itself only learned retroactively. Design and contrived fire/pass-path fixture tests (T-329/T-330) proceed now; the one real-assembly re-verification test (T-331) is tracked as a new Cross-Domain Dependencies entry, not silently deferred. 5 new tasks (D-74 [1] + T-329–T-331 [3] + DO-46 [1]) — WO-063 P-420, depends on 14.Presentation P-415 (governance-arch-planner)
- [2026-08-21] SK.00.CorrelationIdValidationGuard → ● closeout — the phase's own "re-verify rather than trust the note's framing" instruction was followed: `14.Presentation/state-map.md` confirmed WO-063 shipped end to end (all `SK.14.*` phase keys `●`, `SharedKernel.Presentation.WebApi` re-packed `1.3.0`) before this implementation session began, and `CorrelationIdMiddleware.cs`'s own source confirmed the real shape matched this phase's design exactly — `ResolveCorrelationId` calls its own private `IsValidFormat` instance method (declared on the SAME type) and falls back to regenerating a fresh value when the check fails, never throwing. UNLIKE `SK.00.CorsWildcardCredentialsGuard`'s validator-vs-throw mismatch, no design/reality gap was found here — this is the FIRST phase in the "lock a not-yet-shipped hardened guard" family to genuinely add zero new production code to `SecureDefaultsAssertion`. `AssertMethodBodyInvokesMethod` re-pointed directly at the real `CorrelationIdMiddleware.ResolveCorrelationId`/`IsValidFormat` call site — implemented as a GATING test (T-331) rather than deferred, alongside two contrived fixture tests (T-329 pass-path, T-330 fire-path) mirroring the exact shape. Verified non-vacuous via a temporary sanity-check test (a deliberately-wrong callee method name, `"IsValidFormatXyzSanityCheck"`, confirmed to fail with the identical message shape T-330's contrived fixture produces, then reverted before commit). No new `ProjectReference` needed — the existing test-only reference to `SharedKernel.Presentation.WebApi` (added by `SK.00.CorsWildcardCredentialsGuard`) already covers this call site. 3 new architecture tests (229/229 `SharedKernel.ArchitectureTests.Tests` pass), 0 build warnings/errors introduced by this phase's own code. No new SK diagnostic ID — SK0033 remains the next available sequential ID, still unconsumed. This is the last `○` phase key — every phase key in `00.Governance/state-map.md` is now `●`. Closes WO-063 (P-411–P-420) entirely. Promoted to root `state-map.md` (Phase Backlog P-420 closed to `●` Complete) (governance-phase-implementer, state-map-phase, sync-brain)
- [2026-08-21] Phase SK.00.WebhookSsrfGuardLock added — WO-064 (P-432), depends on `15.Integration` P-422 (`IWebhookUrlValidator`/`PrivateNetworkWebhookUrlValidator`, the default fail-closed SSRF guard registered inside `AddSharedKernelWebhooks()`) for real-assembly verification only — confirmed directly against `15.Integration/state-map.md`: H-06 through H-09 are all `○` Not started as of authoring. Applies this review cycle's now-six-times-repeated "a security-relevant default documented and tested only inside its own producing domain drifts" lesson to `15.Integration` for the first time — the platform's first webhook/outbound-delivery-domain application of this family. UNLIKE every phase in this family before it, this one locks TWO independent facts with two DIFFERENT techniques: (1) a NEW seventh method on the existing `SecureDefaultsAssertion` class, `AssertMethodBodyRegistersSingleton(Type declaringType, string methodName, Type serviceType, Type implementationType)` — generalizing/inverting `SecurityArchitectureRules.NoSecurityContextSingletonRegistrationPredicate`'s (WO-057 P-373) generic-instance-method-argument technique from "assert absence" to "assert presence" — proving the default `IWebhookUrlValidator` registration still exists; (2) a genuinely EXECUTED real-assembly test (this file's first), deliberately NOT a `SecureDefaultsAssertion` method, invoking the real `PrivateNetworkWebhookUrlValidator` against a fixed IP-literal table (loopback/RFC 1918 private/link-local-metadata/public) and asserting reject/reject/reject/accept — chosen because "fail-closed for the documented ranges" is a computed behavior no sound static IL technique can honestly prove without assuming an unconfirmed literal representation, unlike this file's five other methods, which all prove structural facts. No new SK diagnostic ID — SK0033 remains the next available sequential Roslyn-analyzer ID, not consumed here. Design and contrived fire/pass/boundary-path fixture tests for the DI-registration-presence technique proceed now; both real-assembly GATING tests are tracked as a new Cross-Domain Dependencies block (two rows) — WO-064 P-432, depends on 15.Integration P-422 (governance-arch-planner)
- [2026-08-21] SK.00.WebhookSsrfGuardLock → ● closeout — `AssertMethodBodyRegistersSingleton` implemented as the seventh method on the EXISTING `SecureDefaultsAssertion` class (not a new class), proven via three contrived fixtures (T-332 pass-path, T-333 fire-path/registration-removed, T-334 fire-path/boundary — same service interface registered with a DIFFERENT implementation type). **STALE-DEPENDENCY CORRECTION (verified against real disk state, not assumed, per this phase's own explicit re-verify instruction — the sixth occurrence of this exact class of finding in this file):** this phase's own authoring-time prose, and `00.Governance/state-map.md`'s Cross-Domain Dependencies block, claimed `15.Integration` P-422 (H-06 through H-09) were all `○` Not started. Verified false at implementation time: `15.Integration` had already shipped `IWebhookUrlValidator`/`PrivateNetworkWebhookUrlValidator` and their default registration inside `AddSharedKernelWebhooks` before this phase's implementation session began (confirmed by reading `Dispatch/IWebhookUrlValidator.cs`/`PrivateNetworkWebhookUrlValidator.cs` and `Extensions/ServiceCollectionExtensions.cs` directly on disk). Both T-335 (Technique A) and T-336 (Technique B) were therefore implemented as the GATING real-assembly checks they were always specified to be, not deferred — the CLAUDE.md documentation block and `00.Governance/state-map.md`'s Dependencies/Cross-Domain Dependencies sections were corrected in place with "CORRECTED AT IMPLEMENTATION TIME" annotations rather than rewritten. **GENUINE DESIGN/REALITY DRIFT, corrected at implementation time (flagged in advance by the dispatching agent, not discovered cold):** this phase's own Implementation Rule 3 assumed the real registration would use plain `AddSingleton<TService,TImplementation>()`; the real, shipped `AddSharedKernelWebhooks` instead uses `services.TryAddSingleton<IWebhookUrlValidator, PrivateNetworkWebhookUrlValidator>()` (`Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions`) — a deliberate, correct choice by `15.Integration` (it is what lets `WithUrlValidator<T>()` win regardless of call order, since that override method itself does `RemoveAll<IWebhookUrlValidator>()` then a plain `AddSingleton`). Rather than narrowing the phase's own design after the fact, `AssertMethodBodyRegistersSingleton` was written from the start to accept BOTH `"AddSingleton"` and `"TryAddSingleton"` as matching registration-method names (a two-element `HashSet<string>` check on `GenericInstanceMethod.ElementMethod.Name`) — matching only one would either miss the one real caller this phase exists to lock, or silently reject a legitimate future `AddSingleton`-based default elsewhere on the platform. `WithUrlValidator<T>()`'s own `services.AddSingleton<IWebhookUrlValidator, TValidator>()` call site was confirmed to never accidentally satisfy a check pointed at `AddSharedKernelWebhooks` — beyond being scoped to a different named method entirely, its second generic argument is the OPEN generic method parameter `TValidator`, not a closed `PrivateNetworkWebhookUrlValidator` reference, so `GenericInstanceMethod.GenericArguments`'s `FullName` can never match. Both real-assembly tests verified NON-VACUOUS via temporary sanity-check mutations (T-335: a deliberately-wrong implementation type, `typeof(object)`, against the same real registration method, confirmed to fail with the same message shape T-333's contrived fixture produces; T-336: all four expected accept/reject outcomes inverted, confirmed all four `[Theory]` cases fail) — both mutations run, confirmed, then reverted before commit. `SharedKernel.ArchitectureTests.Tests.csproj` gained one new test-only `ProjectReference` (`PrivateAssets="all"`) to `SharedKernel.Integration.Webhooks`; the shared `CompileInMemory` fixture-compilation helper in `SecureDefaultsAssertionTests.cs` gained one new reference (`Microsoft.Extensions.DependencyInjection.Abstractions`, already a direct `PackageReference` in this test project since T-153 — provides both `IServiceCollection`/`AddSingleton` and `TryAddSingleton`, confirmed via binary inspection of the installed 10.0.9 package that both extension classes live in the one Abstractions-only assembly) so T-332–T-334's fixture source can compile `services.AddSingleton<TService,TImplementation>()`. 8 new architecture tests (T-332–T-336, with T-336 an `[Theory]` covering 4 IP-literal cases) — 237/237 `SharedKernel.ArchitectureTests.Tests` pass (229 baseline + 8), 0 build warnings/errors introduced by this phase's own code. No new SK diagnostic ID — SK0033 remains the next available sequential ID, still unconsumed. This is the last `○` phase key — every phase key in `00.Governance/state-map.md` is now `●`. Promoted to root `state-map.md` (Phase Backlog P-432 closed to `●` Complete) (governance-phase-implementer, state-map-phase, sync-brain)
- [2026-08-24] Phase SK.00.CacheEncryptionAndRedisValidationLock added — WO-065 (P-437), depends on `02.Caching` P-433 (its own Phase 42, `AddCacheEncryption()`'s compress-then-encrypt composition ordering) and P-436 (its own Phase 45, `AddRedisConnection`'s options-validation eagerness) for real-assembly verification only. Both were dispatched to `caching-arch-planner` immediately before this phase and, per the dispatcher's own explicit note, are planned in `02.Caching/state-map.md` (Phases 42/45) but NOT yet dispatched for implementation. Applies this review cycle's now-eight-times-repeated "a security- or correctness-relevant default documented and tested only inside its own producing domain drifts" lesson to `02.Caching` for the first time, and broadens the family beyond pure security to a correctness/efficiency regression — reversing compress-then-encrypt ordering silently defeats compression's size benefit, the identical ordering contract `07.Messaging`'s own payload transform already documents (P-346). Locks TWO independent facts with two different techniques: Technique A (composition ordering) is a genuinely EXECUTED real-assembly test — the second in this file, after `SK.00.WebhookSsrfGuardLock`'s Technique B — never a new `SecureDefaultsAssertion` method, since ordering is a computed behavior of two composed pipeline stages that no IL technique can honestly prove without assuming a not-yet-fixed decorator shape; it asserts a size-ratio threshold (pipeline output vs. an encrypt-only baseline for a highly-compressible payload), a black-box, representation-agnostic technique. Technique B (redis validation eagerness) reuses the EXISTING `AssertMethodBodyInvokesMethod` unchanged — zero new production code, the SECOND phase in this family to need none, after `SK.00.CorrelationIdValidationGuard` — re-pointed at `AddRedisConnection`, asserting it calls `Microsoft.Extensions.Options.OptionsBuilderExtensions.ValidateOnStart`, the correct way to distinguish genuine eager startup validation from inert attributes or lazy `IValidateOptions<T>` validation. No new SK diagnostic ID — SK0033 remains the next available sequential Roslyn-analyzer ID, not consumed here. Design and Technique B's contrived fire/pass-path fixture tests proceed now; both real-assembly GATING tests are tracked as a new Cross-Domain Dependencies block — per this domain's now-repeated experience, the implementer must re-verify against `02.Caching/state-map.md` directly rather than trusting this note's "not yet implemented" framing. Two new implementation rules added; six new tasks (D-76, T-337–T-340, DO-48) — WO-065 P-437, depends on 02.Caching P-433/P-436 (governance-arch-planner)
- [2026-08-24] SK.00.CacheEncryptionAndRedisValidationLock shipped end to end — all 6 tasks (D-76, T-337–T-340, DO-48) complete. Re-verified against `02.Caching/state-map.md` directly per this phase's own explicit instruction, rather than trusting the stale "Phases 42/45 planned only" framing: `02.Caching` had already shipped its full WO-065 scope (Phase 42 `AddCacheEncryption()`, Phase 45 `AddRedisConnection` validation) before this implementation session began — both real-assembly GATING tests implemented directly, not deferred. Technique A (T-337, this file's second genuinely EXECUTED real-assembly test): builds the real composed `AddBrotliCompression()`+`AddCacheEncryption()` pipeline, confirms a 95-byte stored output for an 8192-character highly-compressible payload against a 8249-byte encrypt-only baseline (well under the documented 50% threshold), plus a round-trip-correctness precondition. Technique B (T-338/T-339 contrived fixtures, T-340 real-assembly): reuses `AssertMethodBodyInvokesMethod` completely unchanged — zero new production code. **Design/reality namespace correction applied**: Rule 6 named the wrong declaring type (`Microsoft.Extensions.Options.OptionsBuilderExtensions`); the real `ValidateOnStart` resolves under `Microsoft.Extensions.DependencyInjection` instead — corrected in Rule 6/T-340's text, in the `SecureDefaultsAssertion` class remarks, and in the "Implementation Rules" quick-reference bullet above, in the same pass. Both real-assembly tests verified NON-VACUOUS via temporary sanity-check mutations (an inverted size expectation for T-337; a deliberately-wrong callee method name for T-340), confirmed to fail, then reverted before commit. `SharedKernel.ArchitectureTests.Tests.csproj` gained two new test-only `ProjectReference`s (`SharedKernel.Caching.FusionCache`, `SharedKernel.Cryptography`) plus `PackageReference Microsoft.Extensions.Options` (pinned `10.0.9`, matching the floor `SharedKernel.Search.Meilisearch` already forces transitively — `10.0.0` triggers an NU1605 downgrade error); `SharedKernel.Caching.Redis.Core` needed no new reference (already present for `RedisTopologyRulesTests`). 241/241 `SharedKernel.ArchitectureTests.Tests` pass (237 baseline + 4), 0 build warnings/errors introduced. This is the last `○` phase key — every phase key in `00.Governance/state-map.md` is now `●`. Promoted to root `state-map.md` (Phase Backlog P-437 closed to `●` Complete) (state-map-phase, governance-phase-implementer)
- [2026-08-26] SK0033 `ReflectionBasedObjectMapperUsage` added to diagnostic registry — WO-079 P-486, a REDIRECT of arch-lead's declined `SharedKernel.Mapping` package proposal into a platform-wide prohibition on AutoMapper (`Profile` subclass/`IMapperConfigurationExpression`/`.AddAutoMapper(...)`, all resolved by `ContainingAssembly.Name == "AutoMapper"` exact match); Mapster deliberately left unenforced (indistinguishable runtime-vs-source-generated call syntax, documented Limitation). No `SharedKernel.ArchitectureTests` counterpart. Ungated. Phase `SK.00.MapperEnforcement` added — 8 tasks: D-77, C-138, T-341–T-345, DO-49 (governance-arch-planner)
- [2026-08-26] SK0034 `AmountCurrencyPairCoupling` added to diagnostic registry — WO-066 P-442, this registry's first ADVISORY-ONLY category (no escalation path to Error, ever, by design, distinct from SK0006/SK0007's escalation-pending shape); a syntax-only closed-suffix-list co-occurrence heuristic (decimal `Amount`/`Price`/`Total`/`Balance` + string `Currency`/`CurrencyCode` on the same type) nudging toward `03.Domain`'s new `Money`. Depends on `03.Domain` P-439 only for its remediation message, not its detection logic — can run against this repo's current sources today. Empirical false-positive validation against this repo's own shipped production sources is a mandatory Tests-phase task (T-350) with a documented narrowing contingency. No `SharedKernel.ArchitectureTests` counterpart. Phase `SK.00.MoneyCurrencyAdvisory` added — 8 tasks: D-78, C-139, T-346–T-350, DO-50 (governance-arch-planner)
- [2026-08-26] SK0035 `UnmaskedClassifiedDataAtLoggingCallSite` added to diagnostic registry — WO-076 P-476, the one mechanically-enforceable piece of `01.Core`'s new `SharedKernel.DataPrivacy` story (classification taxonomy/masking-helper correctness/data-subject-request handling stay convention-only, deliberately). Resolves `DataClassificationAttribute`/`SensitiveDataCategoryAttribute`/`PiiMasking` by fully-qualified metadata name (WO-040/P-248's marker-interface technique), so Design/Core/contrived-fixture Tests proceed now with zero dependency on the not-yet-shipped real package; only a real-assembly re-verification test is GATING on `01.Core` P-474 shipping past Design into Core, tracked as a new Cross-Domain Dependencies entry. Composes with but is structurally distinct from SK0022 and SK0020/SK0021 — the first rule in this registry to inspect argument provenance at a logging call site rather than the call's own syntactic shape. No `SharedKernel.ArchitectureTests` counterpart. Phase `SK.00.DataPrivacyLoggingGuard` added — 8 tasks: D-79, C-140, T-351–T-355, DO-51 (governance-arch-planner)
- [2026-08-26] SK0036 `RawRpcExceptionConstruction` added to diagnostic registry — WO-074 P-469, mirrors the raw-`HttpClient` (SK0013)/inline-`ProblemDetails` (P-199)/ad hoc-logging (SK0020) enforcement precedents for `SharedKernel.Presentation.Grpc`'s sanctioned `Result<T>`-to-`RpcException` mapping path. Semantic-model exact-type resolution of `Grpc.Core.RpcException`/`Grpc.Core.Status` (not a syntax-only simple-name match — "Status" is exactly the generic-simple-name hazard SK0026's "Kernel" lesson warned against), single shared exemption namespace (`SharedKernel.Presentation.Grpc`, mirrors SK0029's shape). UNGATED despite the root state-map's "Depends on: P-468" framing — re-verified directly against `14.Presentation/state-map.md`, confirmed the analyzer needs only the standalone `Grpc.Core.Api` NuGet package (not the full `SharedKernel.Presentation.Grpc` package) for both itself and its fixture tests; no Cross-Domain Dependencies entry added, there is genuinely nothing pending. No `SharedKernel.ArchitectureTests` counterpart. Phase `SK.00.GrpcErrorMappingGuard` added — 7 tasks: D-80, C-141, T-356–T-359, DO-52 (governance-arch-planner)
- [2026-08-26] SK.00.MapperEnforcement/SK.00.MoneyCurrencyAdvisory/SK.00.DataPrivacyLoggingGuard shipped end to end — all 24 tasks (D-77/C-138/T-341–T-345/DO-49, D-78/C-139/T-346–T-350/DO-50, D-79/C-140/T-351–T-355/DO-51) complete; 263/263 `SharedKernel.Analyzers.Tests` pass (252 baseline + 11 new: 5 SK0033, 5 SK0034, 6 SK0035, net +16 across the three phases against a mid-session baseline recount), 241/241 `SharedKernel.ArchitectureTests.Tests` unaffected (no new architecture-test artifact by any of the three phases' own design), 0 build warnings/errors in `SharedKernel.Analyzers`. **STALE-DEPENDENCY CORRECTION confirmed for SK0035 (verified against real disk state, not assumed):** `01.Core`'s `SharedKernel.DataPrivacy` (P-474) had already shipped past Design into Core before this session began, contrary to the phase's own authoring-time "not yet implemented" framing — T-355 (real-assembly re-verification) was implemented directly rather than deferred, the tenth occurrence of this exact finding class in this file. **GENUINE DESIGN/REALITY DRIFT found and corrected before shipping, not patched after the fact:** the real `SharedKernel.DataPrivacy` package nests `DataClassificationAttribute`/`DataClassification`/`SensitiveDataCategoryAttribute` under `SharedKernel.DataPrivacy.Classification` and `PiiMasking` under `SharedKernel.DataPrivacy.Masking` — one namespace level deeper than SK0035's own design assumed (flat `SharedKernel.DataPrivacy.*`); `UnmaskedClassifiedDataLoggingAnalyzer`'s metadata-name constants and every contrived-fixture stub were corrected to match before any test ran against them — see the SK0035 diagnostic entry's Note and the SK0034/SK0035 Implementation Rules bullets above for the full record. **NEW TECHNIQUE established, recorded above as a standing Implementation Rules bullet:** referencing a REAL net10.0-targeted assembly from an analyzer test — needed for SK0033 (real `AutoMapper 15.1.1`/`Riok.Mapperly 3.6.0`, both pinned in `Directory.Packages.props`, `AutoMapper` bumped past the disclosed GHSA-rvv3-g6hj-g44x DoS advisory rather than accepting the phase's "v12+" floor) and SK0035 (real `SharedKernel.DataPrivacy` via a new test-only `ProjectReference`, `PrivateAssets="all"`) — `CSharpAnalyzerTest`'s built-in `ReferenceAssemblies` presets in this pinned testing package version are all netstandard/pre-net10.0 vintage and produce `CS1705` against any net10.0-targeted real assembly; SK0034's mandatory T-350 real-source audit (ZERO diagnostics against this repo's own shipped production sources — suffix list ships unnarrowed) independently needed the same underlying raw-`CSharpCompilation`+`WithAnalyzers` technique for an unrelated reason (scanning hundreds of real source files, not one real assembly). **Root propagation deliberately withheld this session** — other domain implementers were running concurrently against the root `state-map.md`/`CLAUDE.md` (explicit shared-file protocol) — Phase Backlog P-486/P-442/P-476 remain open at the root pending a future session's S8/S8a propagation pass against the then-current root file (governance-phase-implementer, state-map-phase)
- [2026-09-04] SK.00.GrpcErrorMappingGuard shipped end to end — all 7 tasks (D-80/C-141/T-356–T-359/DO-52) complete, now that `14.Presentation`'s P-468 (`SharedKernel.Presentation.Grpc`) shipped and unblocked the coordinator's dispatch. This phase's own design was already fully ungated (Implementation Rule 6) and needed no re-verification beyond a sanity check: the real package's sanctioned mapping path (`SharedKernel.Presentation.Grpc.Results.GrpcResultExtensions.ToGrpcResult()`/`.ToGrpcResult<T>()`) and its two legitimately-constructing interceptors (`GrpcExceptionInterceptor`/`GrpcAuthorizationInterceptor`) all live under sub-namespaces of `SharedKernel.Presentation.Grpc` exactly as the single shared exemption-prefix design assumed — zero design/reality drift. `RawRpcExceptionConstructionAnalyzer` (SK0036) implemented exactly per plan; all 4 tests pass against the REAL `Grpc.Core.Api` package (`2.80.0`, already centrally pinned) — no `CS1705` conflict existed at all here (unlike SK0033/SK0035), since `Grpc.Core.Api` ships no net10.0-specific asset, so this net10.0 test project's own NuGet-selected netstandard2.0/2.1 asset is exactly what `typeof(Grpc.Core.RpcException).Assembly.Location` resolves — a plain `MetadataReference.CreateFromFile` sufficed. **SAME-PASS COORDINATOR-DIRECTED EXTENSION, evaluated and accepted:** `PresentationLayeringRules.GrpcNeverReferencesContracts` added to `SharedKernel.ArchitectureTests` (see that class's own entry above) — the coordinator found the root `CLAUDE.md` Hard rule "`SharedKernel.Presentation.Grpc` must never reference `04.Contracts`" holds only at the direct-`ProjectReference` level, not the reachable-type level, because of the package's own deliberate `SharedKernel.Presentation.WebApi` reference (D-73/D-74) — `NotHaveDependencyOn` (Mono.Cecil actual-type-usage inspection, not assembly-reference-list inspection) is the correct, sufficient, near-zero-novelty mirror of the already-ratified `CommunicationLayeringRules.GrpcNeverReferencesContracts` (WO-026 P-167); a real-assembly pass-path test empirically confirms the real package passes today with zero false positives. `SharedKernel.Analyzers.Tests`: 267/267 pass (263 baseline + 4 SK0036). `SharedKernel.ArchitectureTests.Tests`: 243/243 pass (241 baseline + 2, the coordinator's required floor). ROOT PROPAGATION DELIBERATELY WITHHELD — other domain implementers were running concurrently against the root `state-map.md`/`CLAUDE.md` this session; Phase Backlog P-469 remains open at the root pending a future session's S8/S8a propagation pass (governance-phase-implementer, state-map-phase)
- [2026-09-04] Second coordinator-directed extension, surfaced by the `13.ServiceDefaults` implementer as part of P-466's own acceptance criteria: `ServiceDefaultsSchedulingLayeringRules.OnlyReachesSchedulerProbeTypes` + `ServiceDefaultsOnlyReachesSchedulerProbeTypesPredicate` added (see their own Architecture Test Contracts entries above), mechanizing the `13.ServiceDefaults`→`19.Scheduling` grant now that `13.ServiceDefaults` shipped `AddSchedulerReadinessCheck()` and the real `ProjectReference` (confirmed on disk). Genuinely different mechanical shape than `GrpcNeverReferencesContracts`: the grant permits exactly two named types inside a namespace (`SharedKernel.Scheduling.Probes`) that ALSO contains a third, forbidden, `internal`-visibility type — a flat namespace-prefix ban would have banned the permitted types too, so this required a real allow-listed-exact-type-name `ICustomRule`, not a `NotHaveDependencyOn` call. Deliberately kept SEPARATE from the sibling `13.ServiceDefaults`→`17.Workflows` grant (P-291/WO-047, which has no equivalent mechanical lock anywhere in this file) — no shared/parameterized helper was written, per the root brain's explicit "never reason about either grant by analogy to the other" instruction. The predicate recurses into `TypeDefinition.NestedTypes`, proven load-bearing (not defensive) by a dedicated fire-path test: the real sanctioned consumption site is `async`, so the C# compiler lowers its body into a nested state-machine type, and a forbidden reference hidden entirely inside an `async` method (invisible to the outer type's own signatures) is still caught only because of that recursion. Four new tests — two contrived fire-paths, one contrived pass-path, one REAL-ASSEMBLY pass-path proving the actual shipped `SharedKernel.ServiceDefaults` passes with zero violations. `SharedKernel.ArchitectureTests.Tests`: 247/247 pass (243 baseline + 4, above the coordinator's required floor). No new `ProjectReference` needed in the test csproj — `SharedKernel.Scheduling` was already pulled in transitively via the pre-existing `SharedKernel.ServiceDefaults` reference. ROOT PROPAGATION DELIBERATELY WITHHELD, same reason as above (governance-phase-implementer, state-map-phase)
- [2026-09-04] Root Phase Backlog P-490/WO-080 implemented: `ServiceDefaultsWorkflowLayeringRules.OnlyReachesWorkflowProbeTypes` + `ServiceDefaultsOnlyReachesWorkflowProbeTypesPredicate` added (see their own Architecture Test Contracts entries above), mechanizing the OLDER, longer-standing `13.ServiceDefaults`→`17.Workflows` grant (P-291/WO-047) — an inversion this file itself flagged when the newer `19.Scheduling` lock shipped one entry above and this one still had none. Deliberately kept SEPARATE from `ServiceDefaultsSchedulingLayeringRules` — no shared/parameterized helper, both classes' XML docs now cross-reference each other with an explicit "never merge" warning, and `ServiceDefaultsSchedulingLayeringRules`'s own remark (previously "no equivalent mechanical lock yet exists for the Workflows grant") now names this class instead of being left stale. Both traps the Scheduling predicate hit were independently re-verified against the real Workflows shape rather than assumed by analogy, and both applied again: the two permitted types (`IWorkflowServiceProbe`/`WorkflowServiceHealth`) share their `SharedKernel.Workflows.Temporal.Health` namespace with the forbidden internal `WorkflowServiceProbe` implementation (exact-`FullName` allow-list required, not a namespace-prefix ban), and the real sanctioned consumption site (`WorkflowReadinessHealthCheck.CheckHealthAsync`) is `async`, hiding the real probe call inside a compiler-generated nested state-machine type (recursive `TypeDefinition.NestedTypes` walk required). Four new tests mirroring the Scheduling test's exact shape — two contrived fire-paths, one contrived pass-path, one REAL-ASSEMBLY pass-path proving the actual shipped `SharedKernel.ServiceDefaults` passes with zero violations. **Verified non-vacuous per this phase's own explicit acceptance criterion:** `WorkflowReadinessHealthCheck.cs` was temporarily mutated in-session to add a forbidden `TemporalOptions` field, the real-assembly test genuinely failed, then the file was fully reverted (`git diff` confirmed empty) — no `13.ServiceDefaults`/`17.Workflows` production code changed by this phase. `WorkflowTopologyRules.cs`'s doc-comment mention of the `13.ServiceDefaults` readiness-probe split now names `ServiceDefaultsWorkflowLayeringRules.OnlyReachesWorkflowProbeTypes` by name (this phase's fourth acceptance criterion). No new `ProjectReference` needed in the test csproj — both `SharedKernel.Workflows.Temporal` and `SharedKernel.ServiceDefaults` were already referenced from prior phases. `SharedKernel.ArchitectureTests.Tests`: 251/251 pass (247 baseline + 4). No new SK diagnostic ID. Not tracked under any phase key in `00.Governance/state-map.md` — dispatched and closed directly against a root Phase Backlog entry, same shape as the two coordinator-directed extensions above; ROOT PROPAGATION DELIBERATELY WITHHELD for the same shared-file-protocol reason, but this phase's own root Phase Backlog entry (`### P-490`) still needs a direct `○`→`●` status flip at the root by a future session/coordinator, distinct from an S8/S8a task-completion propagation (governance-phase-implementer, state-map-phase)
- [2026-09-04] Root Phase Backlog P-489/WO-080 implemented — the LAST phase in WO-080, dispatched once `05.Application`'s P-488 shipped the corrected `CacheInvalidationBehavior`/`TransactionBehavior` registration order (confirmed on disk at `ApplicationBehaviorsBuilder.cs` lines 470-474). New `ApplicationBehaviorsCacheInvalidationOrderingLockTests` (see its own Architecture Test Contracts entry above) — a third genuinely EXECUTED real-composed-pipeline test in this project (Technique A shape, after T-336/T-337), proving against the REAL `SharedKernel.Application.Behaviors.dll` that `CacheInvalidationBehavior`'s eviction observably follows `TransactionBehavior`'s commit, AND that `AuditingBehavior`'s write still lands inside that same commit — both invariants proven simultaneously since they pull in opposite registration directions relative to `TransactionBehavior`. Deliberate, INDEPENDENT duplicate of `05.Application`'s own in-domain regression test (`CacheInvalidationTransactionOrderingTests.cs`, P-488) — this lock survives even a future edit that weakens or deletes that domain's own test. New test-only `ProjectReference` to `SharedKernel.Application.Behaviors.csproj` (`PrivateAssets="all"`) added to `SharedKernel.ArchitectureTests.Tests.csproj`. **Verified non-vacuous, coordinator-directed, mirroring P-490's own bar exactly:** `ApplicationBehaviorsBuilder.cs`'s registration order was temporarily reverted in-session to the pre-fix defect, both new tests genuinely failed, then the file was fully reverted (`git diff` confirmed empty, `SharedKernel.Application.Behaviors.Tests` re-confirmed unchanged at 187/187). No `05.Application` production code changed by this phase. No new SK diagnostic ID, no new `Rules/`/`Predicates/` production class. `SharedKernel.ArchitectureTests.Tests`: 253/253 pass (251 baseline + 2). Not tracked under any phase key in `00.Governance/state-map.md` — dispatched and closed directly against a root Phase Backlog entry. ROOT PROPAGATION NOT WITHHELD THIS TIME — the coordinator confirmed P-488/P-489/P-490 are all being flipped to `●` Complete directly at the root by the coordinator itself, closing WO-080 end to end (governance-phase-implementer, state-map-phase)
- [2026-09-08] Phase `SK.00.SyncCryptoGateAndArgon2ConfinementLock` added — WO-081 (P-504), the LAST phase dispatched in this wave (deliberately, since P-504 depends on `01.Core`'s P-492/P-495 — the dispatcher overrode its own domain-number-ascending sort to respect the real dependency). All eight upstream WO-081 domains are already design-locked this session. Two independent techniques: Technique A reuses `SecureDefaultsAssertion.AssertMethodBodyInvokesMethod`/`.AssertMethodBodyThrowsExceptionType` UNCHANGED (zero new production code — the THIRD phase in this family to do so) against `01.Core`'s not-yet-implemented synchronous-provider gate (`AesGcmEncryptionService`'s P-492 gate, `RsaSignatureService`'s/`EcdsaSignatureService`'s P-493 gate), deliberately scoped to the gate's OWN method bodies so `07.Messaging`'s legitimate hard-synchronous serializer call site (proven by reflection against the installed MassTransit assembly to have no async overload) can never be flagged. Technique B is a genuinely NEW `CryptoIsolationRules.CryptographyCoreHasNoThirdPartyDependencies` method (`SharedKernel.ArchitectureTests/Rules/CryptoIsolationRules.cs`), mirroring `CachingAbstractionsHasNoInfrastructureDependencies`'s multi-term shape, confirming `SharedKernel.Cryptography` never references `Konscious.Security.Cryptography` (P-495, not yet shipped) or `Azure.Security.KeyVault`/`Azure.Identity` (P-447, already shipped — this domain's first independent NetArchTest-level re-check of a guarantee previously verified only via `01.Core`'s own `.nuspec`-inspection technique in `SharedKernel.Consumer.Tests`). **TWO evaluated corrections of WO-081/P-504's own dispatch text, neither a rubber-stamp:** (1) P-504's stated "Depends on: P-492, P-495" is INCOMPLETE for Technique A — the asymmetric half of the gate is `P-493`'s, not `P-492`'s, per the phase's own "every sync `ISymmetricEncryptionService`/`IAsymmetricSignatureService` member" wording; (2) Technique B needs NEITHER stated dependency — it targets the ALREADY-SHIPPED `SharedKernel.Cryptography` core assembly and is proven non-vacuous TODAY via a temporary reintroduced `Konscious.Security.Cryptography.Argon2` `PackageReference`, confirmed to fail, to be reverted before commit — directly satisfying WO-081's own AC#2 with zero dependency on P-495 ever shipping. Technique A is honestly recorded as FULLY UNVERIFIABLE/GATING-DEFERRED, not merely "not yet dispatched" — `16.Testing` confirmed directly on disk that no P-492/P-493 production type exists anywhere in `01.Core/SharedKernel.Cryptography/` yet (every `D-*` task `●`, every `C-*`/`T-*`/`DO-*`/`P-*` task `○`), one level further removed than every prior "designed against a not-yet-shipped dependency" occurrence in this family (where the producing domain had usually already shipped Core by implementation time, a 4-out-of-6-plus rate). Also EVALUATED AND DELIBERATELY DEFERRED, not invented here: a generalized "no blocking-bridge call in any `ISynchronousEncryptionKeyProvider`/`ISynchronousAsymmetricKeyProvider` implementer" marker-honesty guard, suggested by `06.Persistence`'s own P-498 design for `PreWarmedEncryptionKeyProvider` (which "honestly earns" the marker by never touching its inner provider synchronously) — `06.Persistence`'s own T-142/T-143 already behaviorally cover the one shipped example more precisely than a generic IL scan could; recorded as a candidate follow-up phase for a future work order. 9 tasks: D-81, C-142, T-360–T-365, DO-53. No new SK diagnostic ID — SK0037 remains next available (governance-arch-planner)
- [2026-09-08] SK.00.SyncCryptoGateAndArgon2ConfinementLock shipped end to end — 8/9 tasks (D-81, C-142, T-360–T-365) complete, closed by this DO-53 documentation pass. Technique A's Cross-Domain Dependency on `01.Core` P-492/P-493 turned out RESOLVED, not deferred as authored — `01.Core` had shipped both past Design into Core before this implementation session began; T-362/T-363 wired directly as GATING real-assembly tests. Real, shipped source corrected the design: `AesGcmEncryptionService`/`RsaSignatureService`/`EcdsaSignatureService` each centralize their throw in one private `ThrowIfNotGenuinelySynchronous` guard method every gated member calls, not four/two independent throw sites as Implementation Rule 2 assumed — T-360–T-363 shaped to match. Technique B's `CryptoIsolationRules.CryptographyCoreHasNoThirdPartyDependencies` implemented exactly as designed (T-364, GATING-immediate); T-365's prescribed real `Konscious.Security.Cryptography.Argon2` `PackageReference` mutation was attempted and found genuinely infeasible — this repo's NuGet Central Package Management disables per-project `VersionOverride` platform-wide (`error NU1013`, confirmed via a real, fully-reverted attempt) — reproduced instead as a permanent, shipped compiled-in-memory-fixture test mirroring T-154/T-155's own precedent. **Same session, same domain, before this phase's own work**: the already-`●` `SK.00.CacheEncryptionAndRedisValidationLock`/T-337 was found broken and gone VACUOUS by three same-day WO-081 sibling shipments (01.Core P-491/P-492, 02.Caching's `SK.02.CacheEncryptionAadBinding` deleting `CacheEncryptionSerializer`) — re-locked against the real, current `ICacheService`-level `EncryptedCacheService` architecture rather than merely patched; see that phase's own type-level remarks addendum for the full record. `SharedKernel.ArchitectureTests.Tests`: 259/259 pass (253 baseline + 6 new SyncCryptoGate/CryptoIsolation tests), 0 build warnings/errors. Full-solution build (`Platform.SharedKernel.slnx`, Release) confirmed clean — 0 errors. No new SK diagnostic ID — SK0037 remains next available (governance-phase-implementer, sync-brain)
- [2026-09-09] `SecureDefaultsAssertion.AssertMethodBodyInvokesMethod`'s Eleventh real-world application/self-repair — `01.Core`'s WO-083 P-524 (key-material zeroization) added an `internal EncryptToString(string, byte[], Action<byte[]>?)` testing overload alongside the pre-existing `public EncryptToString(string, byte[])`, making T-363's real-assembly test throw "found 2 methods named 'EncryptToString' — ambiguous" instead of asserting (the SECOND governance lock disturbed by a domain change this session, after `SK.00.CacheEncryptionAndRedisValidationLock`/T-337). Fixed the HELPER, not `01.Core`: added an optional `Type[]? parameterTypes = null` fifth parameter for exact-signature disambiguation (all fourteen pre-existing positional call sites unaffected), plus same-declaring-type sibling-delegation follow-through in `MethodBodyInvokes` (visited-set-guarded against cycles) — proven NECESSARY, not merely convenient, since the public `EncryptToString(string, byte[])`'s entire body only forwards to the internal 3-arg overload that actually calls the guard; without following that call graph, disambiguating to the public overload alone would have reported the guard as unwired despite every real caller genuinely reaching it. Verified non-vacuous via a temporary deliberately-wrong callee name, reverted before commit. `SharedKernel.ArchitectureTests.Tests`: 259/259 pass (governance-phase-implementer)
- [2026-09-09] Phase `SK.00.CoreDiRegistrationConventionLock` added and shipped end to end — 11/11 tasks (D-82, C-143, T-366–T-372, DO-54), mechanizing root P-523 (WO-083, depends on `01.Core` P-518, which was CONFIRMED ALREADY SHIPPED on disk before this phase began — direct grep across every production `.cs` file in `01.Core` found zero plain `Add*` registration call sites remaining). New `CoreArchitectureRules` (the platform's FIRST `01.Core`-domain architecture-rule class) + `NoPlainServiceCollectionRegistrationPredicate`, documented above. Root P-523's own naive reading ("always use TryAdd") was corrected before any code was written by reading `01.Core`'s own P-518 design record: `SharedKernel.Validation.AddNationalIdValidator<TValidator>()` and `SharedKernel.Cryptography.KeyVault.Azure`'s `IValidateOptions<T>` registration both deliberately use `TryAddEnumerable`, not `TryAddSingleton` — the rule therefore asserts absence of the forbidden `Add*` verbs only, never presence of one particular compliant verb, needing no per-service-type exemption list. `SharedKernel.FeatureManagement`'s deliberately-untouched third-party `AddFeatureManagement(...)` call was also verified (not assumed) to need no exemption — different method name, different declaring type, structurally unreachable by the predicate. Non-vacuous verification deliberately never touched `01.Core` (per this session's explicit instruction): three contrived fixtures (one per forbidden verb) plus a standalone read-only Mono.Cecil inspection of the real, compiled `SharedKernel.Cryptography.dll` confirming nine genuine `TryAddSingleton`/`TryAddKeyedSingleton` calls in `AddSharedKernelCryptography`'s real IL. `SharedKernel.ArchitectureTests.Tests`: 266/266 pass (259 baseline + 7 new), 0 build warnings/errors. No new SK diagnostic ID — SK0037 remains next available. ROOT PROPAGATION DELIBERATELY WITHHELD per this session's explicit operating instructions — the coordinator owns the root Phase Backlog `### P-523` status flip (governance-phase-implementer, state-map-phase)
- [2026-09-10] Root Phase Backlog P-508/WO-082 implemented — `01.Core`'s P-505 merged `SharedKernel.Guards` into `SharedKernel.Core` (namespace `SharedKernel.Guards.*` preserved); this domain's two `ProjectReference`s to the now-deleted project (`SharedKernel.ArchitectureTests.csproj`, `.Tests.csproj`) re-pointed to `SharedKernel.Core.csproj`. Re-scoped `GuardPurityRules`/`DoesNotContainThrowIlPredicate` to the `SharedKernel.Guards` namespace specifically, not the whole (now much larger) hosting assembly — see both classes' updated Architecture Test Contracts and Implementation Rules entries above for the full mechanism, including the confirmed pitfall that NetArchTest's built-in `ResideInNamespaceStartingWith` would have silently excluded every guard type (Mono.Cecil leaves `TypeDefinition.Namespace` empty on nested types) and made the rule vacuously pass; fixed via a `GetEffectiveNamespace` walk-up-to-outermost-enclosing-type helper embedded inside the predicate, mirroring this domain's established namespace-exemption-inside-the-predicate convention (`PersistenceLayerProtectionRules`). Non-vacuity proven in both directions via contrived fixtures against the real `GuardPurityRules.GuardAgainstMethodsMustNotThrow(Assembly)` overload, plus two temporary in-session perturb-and-revert probes of the governance-owned predicate file itself (never `01.Core`) confirming both the new namespace guard and the pre-existing throw-detection are genuinely load-bearing. SK0006 (`GuardClauseThrowAnalyzer`) and its tests needed no change — the analyzer already resolves `IGuardClause` by fully-qualified metadata name, never by assembly, and its tests are fully self-contained via an inline fixture. `SharedKernel.ArchitectureTests.Tests`: 268/268 pass (266 baseline + 2 new); `SharedKernel.Analyzers.Tests`: 267/267 pass (unchanged). Full-solution build clean. Full details and the exact non-vacuity record are in `00.Governance/state-map.md`'s own P-508 changelog entry. ROOT PROPAGATION DELIBERATELY WITHHELD per this session's explicit operating instructions ("Do not touch ... root `state-map.md`") — the root Phase Backlog `### P-508` entry still needs its own status flip by the coordinator (governance-phase-implementer, sync-brain)
- [2026-09-14] WO-084/P-535: ServiceDefaults layering-grant and forwarded-header tests re-pointed at the split integration packages (agent)
- [2026-09-15] Contracts redesign: removed `ContractsLayeringRules`/`NoDirectEventEnvelopeConstructionPredicate` (construction outside `EventEnvelope.Wrap` no longer compiles); `ContractsReferencesOnlyCoreAndDomain` renamed `ContractsReferencesOnlyCore` and now forbids `SharedKernel.Domain`; `ContractsPurityRules` rewritten — `IntegrationEventsHaveNoNonTrivialMethods` judges only `IIntegrationEvent` types, no `EventEnvelope` exemption, new `NoResultTypedPublicMemberPredicate`, every rule proven against the real `SharedKernel.Contracts` assembly; SK0038/SK0039 `IntegrationEventAttributeAnalyzer` added to the registry (SK0040 next); Cross-Service DTO Boundary Mapping rewritten without `Envelope<T>` (coordinator)
- [2026-09-15] SK0037 `ValueObjectMissingEnsureValid` added to the Diagnostic Rule Registry, which previously jumped from SK0036 to SK0038 (coordinator)
- [2026-09-15] SK0040 `PipelineMarkerResponseShapeMismatchAnalyzer` added — pre-publish companion to `05.Application`'s P-544 redesign. Reads `FailureResponse.cs` and every behavior calling it before writing the rule: only `AuthorizationBehavior`/`IAuthorizeRequest` and `IdempotencyBehavior`/`IIdempotentRequest` genuinely construct a failed response through `FailureResponse.Create<TResponse>()`, which requires a `Result`/closed `Result<T>` response or throws `InvalidOperationException` at runtime. `AuditingBehavior`/`IAuditableRequest<TResponse>` and `LoggingBehavior`/`ILoggableRequest<TResponse>` were BOTH found, by reading their source, to never call it — both only forward the response `next()` already produced and classify it through `ResponseOutcome.TryGetError`, which degrades gracefully for a non-`Result` response — so neither is checked by this rule, and this applies to `IAuditableRequest` too even though the phase input's own marker list did not flag it for verification the way it flagged `ILoggableRequest`; independent verification found the identical exemption applies to both. `ValidationBehavior` also calls `FailureResponse.Create` but has no marker interface gating its scope (`TRequest : IRequest<TResponse>` unconditionally), so it is structurally out of reach for a type-declaration rule of this shape and not part of the trigger. Interface-closure resolution reuses `MarkerInterfaceHelpers.HasInterface` (WO-040/P-248's technique) for the two markers, plus new local logic resolving `MediatR.IRequest<TResponse>`'s closed type argument and checking it against `SharedKernel.Primitives.Results.Result`/`Result<T>` by exact namespace. An open type parameter or unresolved/error response type is never flagged (cannot determine the eventual closed shape); a closed `Result<T>` whose own type argument is still open still passes (only the outer shape is checked). Abstract types exempted, matching SK0009/SK0017/SK0018. No `SharedKernel.ArchitectureTests` counterpart — pure Roslyn analyzer, mirrors SK0017–SK0019's/SK0030's "no architecture-test counterpart by design" note. `SharedKernel.Analyzers.Tests`: 329/329 pass (10 new SK0040 tests: 3 fire-path including a both-markers-at-once case, 7 pass-path covering `Result`/closed `Result<T>` responses, no-`IRequest<>`, the `IAuditableRequest`/`ILoggableRequest` exclusions, and both open-generic shapes). `SharedKernel.ArchitectureTests.Tests` build currently fails — confirmed unrelated to this change: `05.Application.Behaviors`/`SharedKernel.Application`'s own `PublicApi.Analyzers` gate (RS0016) is failing on `IIdempotentRequest.Fingerprint`/`AnonymousRequestContext`/`SystemRequestContext`, all mid-edit by a concurrent `05.Application` session per this task's own stated constraint — not something `00.Governance` may fix, and this rule has no dependency on any of those in-flight members. Phase `SK.00.PipelineMarkerResponseShapeGuard` added — 13 tasks: D-84, C-146, T-379–T-388, DO-56. Root Backlog ID: P-544 (governance-phase-implementer)
- [2026-09-16] P-546 security redesign: security rule docs now use the `SharedKernel.Security.Abstractions` namespace for `IUserContext`/`ITenantProvider` (the `.Abstractions.Abstractions` namespace is gone) and a `string? SubjectId` example; `NoSingletonRegistrationOfSecurityContextTypes` documents the non-generic `AnonymousUserContext.Instance` placeholder from `06.Persistence` as deliberately unflagged, with real-assembly tests locating Oidc through `OidcServiceCollectionExtensions` (`AddOidcAuthentication` uses `TryAddScoped`); `SecureDefaultsAssertion` T-309 now expects `MtlsAuthenticationOptions.RevocationMode` default `Online` (was `Offline`); T-310 now asserts the configured `JwtBearerOptions.TokenValidationParameters.ValidAlgorithms` excludes `none`/`HS*` and that configuring a forbidden algorithm fails startup validation, because `SecurityOptions` is removed and the Oidc algorithm collections default to empty (configuration binding appends); historical notes naming `ApiKeyUserContext`, `DpopProofValidator.ProofHeaderName` and the old T-310 test name annotated rather than rewritten (coordinator)
- [2026-09-18] P-554: SK0035 retargeted to Microsoft's compliance model after `SharedKernel.DataPrivacy`'s redesign — classified = any `Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute`-derived attribute except `NoDataClassificationAttribute`; a classified parameter (and `[LogProperties]` for whole objects) is safe; `Pseudonymizer` calls exempt alongside `PiiMasking`; Restricted-tier/`SensitiveDataCategory` checks removed; message now names the attribute and suggests classifying the parameter (agent)
- [2026-09-21] P-558: UnitOfWorkSeamRules.SharedContractsAreNotRedeclared, ReadOnlyRepositoriesNeverTrack, PersistenceNamespaceConventionRules, Persistence.Testing guard; SK0201 base-call only (agent)
- [2026-09-22] Docs updated for `08.Storage`'s P-559 redesign: `StorageTopologyRules` contract block rewritten (`ProviderPackagesNeverReferenceEachOther` removed; `S3NeverReferencesObs`, `S3ForbiddenAssemblyReferences`, `AbstractionsForbiddenAssemblyReferences` documented; `.Obs` → `.S3` allowed); SK0023 note corrected (providers no longer register `IAmazonS3`) (coordinator)
- [2026-09-23] P-562 (`14.Presentation` redesign), wave 4: `PresentationLayeringRules` adjusted — `NoInlineResultBranchBeforeHttpResultPredicate`'s escape hatch is now the WebApi core's typed-result mapping surface matched by declaring type (`ResultHttpExtensions`/`ResultActionResultExtensions`/`ErrorProblemDetailsExtensions`, `new ErrorHttpResult`) instead of the deleted `ToProblemDetailsResult`, and its HTTP signal gains typed-results unions (`Results<…>`) and `IActionResult`; new `NoOpenApiStackDependencyOutsideOpenApiAddOn` keeps `Asp.Versioning`/`Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi`/`Scalar.AspNetCore` inside `SharedKernel.Presentation.OpenApi`; real-assembly tests prove `.OpenApi`/`.SignalR`/`.Grpc` pass the WebApi-exclusion rules unexempted (with a real-WebApi control that fails). SK0036's message/docs/tests name `ThrowIfFailure()`/`GetValueOrThrow()` (rule unchanged). `SecureDefaultsAssertionTests` T-328 re-pointed at `WebApiOptionsValidator` (the deleted `CorsPolicyOptionsValidator`'s successor; chain `AddSharedKernelWebApi → AddValidatedOptions`, `Validate → ValidateCors`, `Validate → ValidateOptionsResult.Fail`) and T-331 at the now-internal `Correlation.CorrelationIdMiddleware.Resolve → IsValid` (agent)
- [2026-09-24] P-562 final review, integration stream I3: `NoInlineResultBranchBeforeHttpResultPredicate` follows R21/R19 — `ErrorHttpResult` is matched in the WebApi root namespace (the stale `…Errors.ErrorHttpResult` name flagged a compliant `new ErrorHttpResult(error)` while the stand-in fixture stayed green) and `ResultActionResultExtensions` is dropped; new tests compile fixtures against the test host's real WebApi, Primitives and ASP.NET Core assemblies, pinning every mapping name and the typed-results namespace (each verified by a deliberately stale name, reverted). `PresentationLayeringRulesTests` anchors the gRPC assembly on `GrpcHostBuilderExtensions` (R32 removed `GrpcResultExtensions`; the test project had stopped compiling). SK0036's message, docs and tests name `SharedKernel.Core.Extensions`' `ThrowIfFailure()`/`GetValueOrThrow()` (R32 removed the gRPC package's own; rule and exemption prefix unchanged). Doc-only: `WebApiOptions` → `SharedKernelWebApiOptions` (R22), the fallback exception handler instead of `IExceptionHandler` (R5), no MVC `ToActionResult` (R19). `PresentationPreconditionCodesTests` (R7) re-verified against `ConcurrencyVersion.ConflictErrorCode` and `StorageErrorCodes` after X4 (agent)
