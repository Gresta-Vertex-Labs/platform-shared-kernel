---
name: project_streaming_asnotracking_exception
description: StreamAsync/StreamProjectedAsync force AsNoTracking unconditionally (the one exception to honoring spec.AsNoTracking) and treat Skip/Take as a row-window
metadata:
  type: project
---

P-149 added `StreamAsync(ISpecification<TAggregate> spec, ct) → IAsyncEnumerable<TAggregate>`
and `StreamProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, ct) → IAsyncEnumerable<TResult>`
to `IReadRepository<TAggregate, TId>` (BCL `IAsyncEnumerable<T>` — zero new
`.Abstractions` dependency).

**Forced AsNoTracking — THE documented exception:** `EfReadRepository`'s
implementation calls `GetQuery`/`GetProjectedQuery` via `ISpecificationEvaluator<T>`,
then applies `.AsNoTracking()` **unconditionally**, regardless of
`spec.AsNoTracking`. This is explicitly called out everywhere (CLAUDE.md Interface
Contracts, Hard Violations, AOT notes, Test Rules) as the ONE place in this codebase
where a specification's `AsNoTracking` flag is NOT honored.

**Why:** A long-lived streaming enumeration under change tracking would grow the
`ChangeTracker` unbounded for the lifetime of the enumeration (which could be very
long for exports/batch jobs) — this is a memory-leak-shaped footgun the framework
closes by policy, not by convention.

**Skip/Take as row-window:** `spec.Skip`/`spec.Take`, if set, are applied as a normal
row-window by the evaluator BEFORE the query becomes `IAsyncEnumerable<T>` — no
special-casing. E.g. `Skip(10).Take(5)` streams exactly 5 items.

**Implementation detail:** `.AsAsyncEnumerable()` + `[EnumeratorCancellation]` on the
`CancellationToken` parameter (compiler-generated async iterator pattern, AOT-safe
as of EF Core 8+).

**How to apply:** If a future read-side streaming or export capability is added,
follow this same "forced AsNoTracking, Skip/Take as window" pattern rather than
inventing a new convention. If tracked entities are genuinely required, the caller
must use `ListAsync`/`ListPagedAsync` instead — streaming is read-only by design.
