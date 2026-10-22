# 04.Contracts — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Contracts` | Model | ● | Cross-service wire contracts: `IIntegrationEvent` + `[IntegrationEvent]`, CloudEvents `EventEnvelope<TEvent>` (built only by `EventEnvelope.Wrap`), `PagedList<T>`, `CursorPagedList<T>` (readable through a consumer-assembly source-generated context), `PageRequest`/`CursorPageRequest`, `PageCursor`. Never references `SharedKernel.Domain`. Ships with the repo-wide release train. |

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **Note for 11.Communication consumers:** REST clients can pass a source-generated `JsonTypeInfo` for paged results and envelopes (the Contracts README, recipe 7); no package change needed there.
