---
name: sk-diagnostic-registry
description: Current SK diagnostic ID assignments — last assigned ID and summary of all rules, so next ID is always known
metadata:
  type: project
---

SK diagnostic ID registry as of 2026-05-30. Next available ID: **SK0011**.

| ID | Rule Name | Status |
|----|-----------|--------|
| SK0001 | DirectDateTimeUsage | Defined |
| SK0002 | DirectMicrosoftFeatureManagerUsage | Defined |
| SK0003 | RawExceptionThrow | Defined |
| SK0004 | NullErrorReturn | Defined |
| SK0005 | StringOnlyExceptionConstructor | Defined |
| SK0006 | GuardClauseThrow | Defined (WO-002 P-004) — Warning; escalation to Error gated on Guard+Throw exclusion stability |
| SK0007 | RedisChannelServiceMessagingSubstitute | Defined (WO-003 P-009) — Warning; escalation to Error gated on zero false positives on "DomainEvent" substring match |
| SK0008 | AggregateRootDispatchCoupling | Defined (WO-011 P-056) — Warning; fires when IAggregateRoot<> injected in dispatch-context constructor; fix: use IHasDomainEvents |
| SK0009 | DomainEventMissingVersionAttribute | Defined (WO-011 P-056) — Warning; fires on non-abstract IDomainEvent implementors lacking [DomainEventVersion]; abstract types exempt |
| SK0010 | SpecificationOrderingConflict | Defined (WO-011 P-056) — Warning; fires when constructor calls both ApplyOrderBy and ApplyOrderByDescending |

**Why:** Tracking this avoids gaps and reuse. Never reuse a published ID even if a rule is renamed — bump the registry.

**How to apply:** Before assigning a new SK ID in any phase, verify this registry. Use SK0011 for the next rule. Update this memory file whenever a new rule is assigned.
