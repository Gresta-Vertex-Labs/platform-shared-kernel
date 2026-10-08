# 03.Domain — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Domain` | Model | ● | `Entity`, `AggregateRoot` with audit/soft-delete/tenanted bases (`IHasTenant.TenantId` is a `TenantId`), `ValueObject`, `StronglyTypedId`, `DomainEvent`, `IBusinessRule`, `IPolicy`, specifications with offset/keyset paging, `Money`. References `SharedKernel.Execution`; logging-free; never references `SharedKernel.Contracts`. Ships with the repo-wide release train. |

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.
