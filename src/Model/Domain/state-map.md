# 03.Domain — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Domain` | Model | ● | `Entity`, `AggregateRoot` with audit/soft-delete/tenanted bases (`IHasTenant.TenantId` is a `TenantId`), `ValueObject`, `StronglyTypedId`, `DomainEvent`, `IBusinessRule`, `IPolicy`, specifications with offset/keyset paging, `Money`. References `SharedKernel.Execution`; logging-free; never references `SharedKernel.Contracts`. Ships with the repo-wide release train. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **01.Core (`SK.01.CoreConformance` C-02):** owns the `ErrorCodes.Domain.RuleViolated` value change to `domain.rule_violated`. Domain holds no literal and does not read the constant, so this phase needs nothing from it; the only trace of the old code in 03 is a test comment, removed by C-04.
- **06.Persistence (note only):** a stored `Money` amount with excess decimals now surfaces as `DomainException` with `ErrorType.Unexpected` (500) instead of `BusinessRuleViolationException` (422); check any persistence test or handler that expects the old type (`MoneyMapping`, `CurrencyValueConverter`).
- **00.Governance (`SK.00.ObservabilityReportOnly`):** the error-code format assertion must accept the exemption in D-03 and read `DomainErrorCodes` constants.
- **16.Testing / samples (note only):** `MoneyFaker` and `Shop.Ordering.Domain` use no changed code; verify nothing spells `money.currency_mismatch` by literal.
