---
name: envelope-namespace-rename
description: WO-052/P-328 — Envelope/Envelope namespace-type collision fix (Envelope → Envelopes) and the pagination-DTO design pattern reused for CursorPagedList<T>
metadata:
  type: project
---

> WO-086 (2026-09): historical. Since the P-543 redesign `EventEnvelope<TEvent>` is constrained to `TEvent : class, IIntegrationEvent`, `SharedKernel.Contracts` (Model tier) never references `SharedKernel.Domain` (`SharedKernelLayeringRules.ContractsNeverReferencesDomain`), `Envelope<T>`/`ContractsJsonContext` were deleted, and `TenantId` stays `Guid?` on the wire (not `SharedKernel.Execution`'s `TenantId` type).

## Envelope/Envelope namespace-type collision fix (WO-052 / P-328, designed 2026-07-31)

Since inception, `Envelope`/`Envelope<T>` lived in namespace `SharedKernel.Contracts.Envelope` — identical to the type name itself, a well-known C# ambiguity footgun. The domain's own CLAUDE.md carried a mandatory workaround for it from the start ("use a using alias or fully-qualified names"), and that workaround had already been duplicated verbatim across the package's own tests plus two external consumers (`11.Communication.Rest`, `16.Testing`) before the fix landed.

**Fix:** rename the namespace/folder to `SharedKernel.Contracts.Envelopes` (plural) — matching the sibling `Events/`/`Pagination/`/`Mapping/`/`Serialization/` folders' naming convention, which never had this problem because none of them share a name with a type they contain. Zero change to `Envelope`/`Envelope<T>`'s members, factory methods, or implicit operators — namespace-only move.

**SemVer:** this is a breaking *source* change (every consumer's `using` statement/alias must update) even though nothing about runtime behavior changes — treated as a major version bump (`1.1.0` → target `2.0.0`), not a patch/minor.

**Why it took this long to fix:** the phase's own rationale (P-328's "Why this is needed") states it explicitly — fixing a namespace early (package still v1.1.0, only two known external consumers) is materially cheaper than fixing it after wide downstream adoption, since `Envelope<T>` is the platform's single most widely-used cross-service response wrapper. **Lesson for future planning:** when a design smell is identified early but "works around it" rather than "fixes it," periodically re-flag it before adoption grows — the earlier arch-planner sessions (WO-011/WO-012) documented the collision as an accepted workaround rather than a defect to fix; WO-052 is the first time it was escalated to an actual fix.

## Design pattern: `CursorPagedList<T>` mirrors `PagedList<T>`'s construction discipline (WO-052 / P-332)

New sealed record in `Pagination/`, namespace `SharedKernel.Contracts.Pagination`. Properties: `Items` (`IReadOnlyList<T>`), `NextCursor` (`string?`, opaque forward cursor), `HasMore` (`bool`). `Create`-only construction — `internal` primary constructor + `[JsonConstructor]` for STJ deserialization, exactly like `PagedList<T>`.

**Deliberately no `TotalCount`/`Page`/`PageSize`.** This is the whole point of keyset/cursor pagination — those three fields either require an expensive `COUNT(*)` (defeating the reason to use keyset pagination) or don't make sense for an infinite-scroll/actively-written result set. Don't ever add them "for consistency with PagedList<T>" — that would silently reintroduce the cost the type exists to avoid. When to use which: `PagedList<T>` for small/random-access/UI-paged sets needing a total count; `CursorPagedList<T>` for large/actively-written/infinite-scroll sets.

**Doc-only cross-reference, no compile dependency:** the type is conceptually paired with `03.Domain`'s `KeysetSpecification<T, TKey>` (shipped v1.7.0, P-308/WO-051) and anticipates `06.Persistence`'s still-queued EF Core seek-query translation (P-317/WO-051) — but `SharedKernel.Contracts` takes **no new project reference** for this. It's a "design-ahead-of-Core" DTO, same pattern as `16.Testing`'s provider-folder precedent: the DTO ships now because it has no technical dependency on the persistence-side translation; only its first *real producer* waits on P-317/318.

**How to apply:** any future pagination-shaped DTO in this package should be checked against this same discipline — factory-only construction, computed-not-stored derived properties where applicable, and no field that can't be honestly supported by the access pattern the type represents.
