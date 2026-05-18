---
name: sk-diagnostic-registry
description: Current SK diagnostic ID assignments — last assigned ID and summary of all rules, so next ID is always known
metadata:
  type: project
---

SK diagnostic ID registry as of 2026-05-18. Next available ID: **SK0008**.

| ID | Rule Name | Status |
|----|-----------|--------|
| SK0001 | DirectDateTimeUsage | Defined |
| SK0002 | DirectMicrosoftFeatureManagerUsage | Defined |
| SK0003 | RawExceptionThrow | Defined |
| SK0004 | NullErrorReturn | Defined |
| SK0005 | StringOnlyExceptionConstructor | Defined |
| SK0006 | GuardClauseThrow | Defined (WO-002 P-004) — Warning; escalation to Error gated on Guard+Throw exclusion stability |
| SK0007 | RedisChannelServiceMessagingSubstitute | Defined (WO-003 P-009) — Warning; escalation to Error gated on zero false positives on "DomainEvent" substring match |

**Why:** Tracking this avoids gaps and reuse. Never reuse a published ID even if a rule is renamed — bump the registry.

**How to apply:** Before assigning a new SK ID in any phase, verify this registry. Use SK0008 for the next rule. Update this memory file whenever a new rule is assigned.
