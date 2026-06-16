---
name: sk-diagnostic-registry
description: Current SK diagnostic ID assignments — all assigned IDs, block conventions, and next available ID per block
metadata:
  type: project
---

SK diagnostic ID registry as of 2026-06-16. Next available sequential ID: **SK0013**.

## Sequential block (SK0001–SK0012) — general SharedKernel patterns

| ID | Rule Name | Status |
|----|-----------|--------|
| SK0001 | DirectDateTimeUsage | Defined |
| SK0002 | DirectMicrosoftFeatureManagerUsage | Defined |
| SK0003 | RawExceptionThrow | Defined |
| SK0004 | NullErrorReturn | Defined |
| SK0005 | StringOnlyExceptionConstructor | Defined |
| SK0006 | GuardClauseThrow | Defined (WO-002 P-004) — Warning; escalation to Error gated on Guard+Throw exclusion stability |
| SK0007 | RedisChannelServiceMessagingSubstitute | Defined (WO-003 P-009) — Warning; escalation to Error gated on zero false positives on "DomainEvent" substring match |
| SK0008 | AggregateRootDispatchCoupling | Defined (WO-011 P-056) — Warning |
| SK0009 | DomainEventMissingVersionAttribute | Defined (WO-011 P-056) — Warning; abstract types exempt |
| SK0010 | SpecificationOrderingConflict | Defined (WO-011 P-056) — Warning |
| SK0011 | GuidFormatCodeMisuse | Defined (WO-016 P-096) — Warning; requires SemanticModel.GetTypeInfo on receiver; first SK analyzer with semantic model check |
| SK0012 | MakeGenericMethodReflection | Defined (WO-024 P-153) — Warning; MakeGenericMethod IL call in any method body; NetArchTest ICustomRule (NoMakeGenericMethodReflectionPredicate); allow-list via ReflectionExemptionRegistry; NO Roslyn analyzer (IL-only detectable); motivating incident: P-147 EncryptionRotationService; escalation to Error gated on zero false positives across all platform assemblies |

## Multi-tenancy block (SK0201–SK0202) — EF Core tenant-filter guard

| ID | Rule Name | Status |
|----|-----------|--------|
| SK0201 | TenantedDbContextOnModelCreatingGuard | Defined (WO-018 P-110) — Warning; TenantedDbContext subclass overrides OnModelCreating without base call or ApplyTenantFilters |
| SK0202 | IgnoreQueryFiltersOutsideTenantedRepository | Defined (WO-018 P-110) — Warning; IgnoreQueryFilters() called outside SharedKernel.Persistence.EfCore namespace and outside class named TenantedRepository |

## Encryption-domain block (SK0301–SK0304) — WO-019 AES-256-GCM encryption subsystem

| ID | Rule Name | Status |
|----|-----------|--------|
| SK0301 | DirectCryptoInDomainOrApplication | Defined (WO-019 P-114) — Warning; AesGcm/Aes/SymmetricAlgorithm referenced in domain or application layer; NetArchTest ICustomRule (NoAesCipherInDomainOrApplicationPredicate) |
| SK0302 | EncryptionAttributeOnDomainEntity | Defined (WO-019 P-114) — Warning; [Encrypt*] attribute on domain entity class; NetArchTest ICustomRule (NoEncryptionAttributeOnDomainEntityPredicate) |
| SK0303 | EncryptionRotationJobInDomainOrApplication | Defined (WO-019 P-114) — Warning; IEncryptionRotationJob constructor injection in domain/application types; NetArchTest ICustomRule (NoEncryptionRotationJobInjectionPredicate) |
| SK0304 | DirectEncryptedValueConverterInstantiation | Defined (WO-019 P-114) — Warning; new EncryptedValueConverter<T>() called in IEntityTypeConfiguration<T> implementors other than EncryptionModelConvention; NetArchTest ICustomRule (NoDirectEncryptedValueConverterInstantiationPredicate) |

## Messaging-domain block (SK0701–SK0708) — WO-020 P-123 and WO-021 P-133 messaging rules

| ID | Rule Name | Status |
|----|-----------|--------|
| SK0701 | NoDirectBusInjectionOutsideMessaging | Defined (WO-020 P-123) — Warning; IBus/IPublishEndpoint/ISendEndpointProvider constructor injection outside SharedKernel.Messaging.*; NetArchTest ICustomRule (NoDirectBusInjectionOutsideMessagingPredicate) |
| SK0702 | NoEventPublisherInDomainLayer | Defined (WO-020 P-123) — Warning; IEventPublisher constructor injection in domain-layer types (namespace or interface signal); NetArchTest ICustomRule (NoEventPublisherInDomainLayerPredicate) |
| SK0703 | MessageBusSingletonRegistration | Defined (WO-020 P-123) — Warning; AddSingleton<IMessageBus,...>() or AddSingleton<IEventPublisher,...>(); Roslyn syntax-only analyzer (no SemanticModel) |
| SK0704 | HardcodedQueueUriInGetSendEndpoint | Defined (WO-020 P-123) — Warning; new Uri("queue:...") or new Uri("exchange:...") literal passed to GetSendEndpoint; Roslyn syntax-only analyzer (no SemanticModel) |
| SK0705 | FaultConsumerDirectRegistration | Defined (WO-021 P-133) — Warning; AddScoped/AddSingleton with IFaultConsumer<T> type argument; Roslyn syntax-only analyzer; fix: use MessagingBusBuilder.AddFaultConsumer<TMessage, TConsumer>() |
| SK0706 | DirectMassTransitSchedulerInjection | Defined (WO-021 P-133) — Warning; MassTransit.IMessageScheduler constructor injection outside SharedKernel.Messaging.*; NetArchTest ICustomRule (NoDirectSchedulerInjectionOutsideMessagingPredicate); namespace-disambiguated from platform abstraction |
| SK0707 | SagaStateMustExtendSagaStateBase | Defined (WO-021 P-133) — Warning; ISaga implementor missing SagaStateBase in BaseType chain; NetArchTest ICustomRule (SagaStateMustExtendSagaStateBasePredicate); fail-open on unresolvable base types |
| SK0708 | BatchConsumerRegisteredViaAddConsumer | Defined (WO-021 P-133) — Warning; AddConsumer<T>() where T name contains "BatchConsumer"; Roslyn syntax-only analyzer; naming-convention heuristic (false-negative if class name lacks "BatchConsumer") |

## Block conventions

- **SK0001–SK0012**: general SharedKernel coding patterns (mix of Roslyn analyzers and NetArchTest ICustomRules, sequential)
- **SK0013–SK0199**: reserved for future sequential general-purpose rules; next is SK0013
- **SK0201–SK0299**: EF Core / multi-tenancy domain block; next is SK0203
- **SK0301–SK0399**: encryption subsystem block; next is SK0305
- **SK0701–SK0799**: messaging-domain block (domain 07); next is SK0709
- Never backfill gaps between blocks. Never reuse a published ID even if a rule is renamed.

**Why:** Tracking this prevents ID gaps, reuse, and block collisions. Block numbers follow the domain number (02xx = domain 02 EfCore/multi-tenancy, 03xx = encryption subsystem, 07xx = messaging domain). The 07xx block was introduced in WO-020 P-123; extended to SK0705–SK0708 in WO-021 P-133.

**How to apply:** Before assigning a new SK ID, verify this registry. Use SK0013 for the next general-purpose rule, SK0203 for the next multi-tenancy rule, SK0305 for the next encryption rule, SK0709 for the next messaging rule. Update this memory file whenever a new rule is assigned.

**Key design decision (P-153):** SK0012 is a NetArchTest ICustomRule predicate, NOT a Roslyn analyzer. Reason: MethodInfo.MakeGenericMethod is called at runtime on a variable — there is no compile-time syntax pattern to detect reliably. IL inspection (Mono.Cecil Call/Callvirt opcode name match on "MakeGenericMethod") is the only reliable detection mechanism. This is the first sequential SK rule that is NOT a Roslyn analyzer — all prior SK0001–SK0011 were Roslyn analyzers.
