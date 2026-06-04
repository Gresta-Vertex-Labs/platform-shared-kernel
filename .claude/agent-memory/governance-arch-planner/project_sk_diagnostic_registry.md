---
name: sk-diagnostic-registry
description: Current SK diagnostic ID assignments — all assigned IDs, block conventions, and next available ID per block
metadata:
  type: project
---

SK diagnostic ID registry as of 2026-06-04. Next available sequential ID: **SK0012**.

## Sequential block (SK0001–SK0011) — general SharedKernel patterns

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

## Block conventions

- **SK0001–SK0011**: general SharedKernel coding patterns (Roslyn analyzers, sequential)
- **SK0012–SK0199**: reserved for future sequential general-purpose rules; next is SK0012
- **SK0201–SK0299**: EF Core / multi-tenancy domain block; next is SK0203
- **SK0301–SK0399**: encryption subsystem block; next is SK0305
- Never backfill gaps between blocks. Never reuse a published ID even if a rule is renamed.

**Why:** Tracking this prevents ID gaps, reuse, and block collisions. The 03xx block was introduced non-sequentially (skipping SK0012) as a deliberate namespace decision for the encryption domain — document it clearly so future phases use the correct block.

**How to apply:** Before assigning a new SK ID, verify this registry. Use SK0012 for the next general-purpose rule, SK0203 for the next multi-tenancy rule, SK0305 for the next encryption rule. Update this memory file whenever a new rule is assigned.
