# 04.Contracts — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Contracts` | Model | ● | Cross-service wire contracts: `IIntegrationEvent` + `[IntegrationEvent]`, CloudEvents `EventEnvelope<TEvent>` (built only by `EventEnvelope.Wrap`), `PagedList<T>`, `CursorPagedList<T>`, `PageRequest`/`CursorPageRequest`, `PageCursor`. Never references `SharedKernel.Domain`. Ships with the repo-wide release train. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.04.PagedListSourceGen` | Paged lists readable through a consumer-assembly source-generated JSON context | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.04.PagedListSourceGen — Paged lists readable through a source-generated JSON context `○`

**Work order:** WO-090 / P-628 · **Depends on:** None

Confirmed failure: serializing `PagedList<T>` and `CursorPagedList<T>` through a `JsonSerializerContext` declared in a consumer assembly works, but deserializing throws `NotSupportedException` ("Deserialization of types without a parameterless constructor, a singular parameterized constructor, or a parameterized constructor annotated with 'JsonConstructorAttribute' is not supported"). Cause: the `[JsonConstructor]` is `internal`, so the generator running in the consumer assembly cannot call it and ignores it; reflection STJ reaches it, which is why only source-generated clients fail. Verified with a scratch context over the 1.0.0 build; a scratch copy of the type with a `public` `[JsonConstructor]` round-trips through a source-generated context, still surfaces the constructor's `JsonException` and ignores computed members. Verdict: accept, additive. Make the JSON constructor of both lists public with the same signature and the same validation; wire shape, `Create`/`FromLookahead`/`Empty`/`Map` and equality do not change. Compatibility: no wire change, so older and newer consumers and values in flight are unaffected; 1.0.0 source and behavior stay compatible because a public constructor is only an addition and reflection STJ already uses the same constructor. Rejected alternative: a `[JsonConverter]` on the types, which would re-implement member matching, naming and case rules and change the reflection path as well. `EventEnvelope<TEvent>` keeps internal constructors by rule 5 and is out of scope.

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify: the JSON constructors of `PagedList<T>` (`items, page, pageSize, totalCount`) and `CursorPagedList<T>` (`items, nextCursor`) become `public`, parameters unchanged, `[EditorBrowsable(Never)]`, XML-documented as the serializer entry point that throws `JsonException` (`Create`/`FromLookahead` stay the construction API); they keep snapshotting items and running the shared `FindProblem` (rules 4, 10). Record the Decision and refine the reflection-STJ Decision | SharedKernel.Contracts | `○` |
| C-01 | Make both `[JsonConstructor]` constructors public, no other member change; add both to `PublicAPI.Unshipped.txt` (additions only, `Shipped` untouched) | SharedKernel.Contracts | `○` |
| T-01 | Round trip through a test-local source-generated context for a representative item type: both lists written then read equal the original, and the written JSON equals the reflection output | SharedKernel.Contracts.Tests | `○` |
| T-02 | Every broken paging rule in a document read through the generated context fails with `JsonException` (missing/null items, page < 1, pageSize < 1, negative totalCount, items over pageSize, blank or over-512 nextCursor); computed members ignored on read; names fixed under Web and default naming policies; reflection path results unchanged | SharedKernel.Contracts.Tests | `○` |
| T-03 | Direct calls of the now-public constructors: valid input equals the `Create` result (snapshot kept), invalid input throws `JsonException`; `with` still cannot reach a rejected state | SharedKernel.Contracts.Tests | `○` |
| T-04 | Consumer-style proof on the packed package: a `JsonSerializerContext` in `SharedKernel.Contracts.ConsumerVerify` registering both lists round-trips them, and an invalid document fails with `JsonException` | SharedKernel.Contracts.ConsumerVerify | `○` |
| DO-01 | README: how a service registers both lists for its item type in its `JsonSerializerContext` (snippet with real output); correct the "Why reflection-based" answer and the paging JSON bullet | SharedKernel.Contracts | `○` |
| DO-02 | Consumer skill: check `consumer/skill/sharedkernel/recipes/add-outbound-call.md` and `add-http-endpoint.md` for text that has clients read paged results through reflection and replace it with the source-generated registration, keeping each recipe's `proven-by` file true | consumer skill | `○` |

**Acceptance:**
- [ ] A `JsonSerializerContext` in a separate consumer-style assembly (`SharedKernel.Contracts.ConsumerVerify`, on the packed package) round-trips `PagedList<Item>` and `CursorPagedList<Item>`.
- [ ] A document that breaks a paging rule still fails with `JsonException` through that context; wire names and computed members are unchanged and the written JSON equals the reflection output.
- [ ] `PublicAPI.Unshipped.txt` shows additions only; no 1.0.0 signature is removed or changed, and `Create`/`FromLookahead`/`Empty`/`Map`/equality behave as before.
- [ ] The Contracts README documents the context registration; the consumer skill no longer tells a client to use reflection for paged results.

## Blocked

None.

## Cross-Domain Dependencies

- **Note for arch-lead:** `EventEnvelope<TEvent>` keeps internal constructors (rule 5), so it is probably not readable through a consumer-assembly source-generated context either (unverified); not part of SK.04.PagedListSourceGen.
- **Note for 11.Communication consumers:** once the phase ships, REST clients can pass a source-generated `JsonTypeInfo` for paged results; no package change needed there.
