---
name: verified-facts
description: Cross-domain facts confirmed against real source during WO-044, safe to reuse without re-grepping
metadata:
  type: reference
---

Spot-checked directly against shipped source on 2026-07-19, not taken on faith from other domains'
CLAUDE.md prose:

- **`SharedKernel.Primitives.Error`** (`01.Core/SharedKernel.Primitives/Errors/Error.cs`) exposes
  exactly six static factories: `Unexpected`, `Validation`, `NotFound`, `Conflict`, `Unauthorized`,
  `BusinessRule` — each `(string code, string message)`. There is **no `Error.Failure`** and no
  `Error.Forbidden`. Any capability-domain `*Errors` static class that names a nonexistent factory or
  returns `Error.None` is wrong on sight.
- **`LoggingEventIdRanges.Search`** (`01.Core/SharedKernel.Primitives/Logging/LoggingEventIdRanges.cs`
  line 96) `= 9000`, exactly as every other domain's brain assumes. `09.Search`'s own sub-block split
  is Abstractions 9000-9099 (reserved, permanently unused — no logging in that package),
  Meilisearch 9100-9199, ElasticSearch 9200-9299.
- **`PagedList<T>`** (`04.Contracts/SharedKernel.Contracts/Pagination/PagedList.cs`) is a
  `sealed record`. This is why `09.Search` cannot extend it and instead declares its own
  `SearchResults<TDocument>` with a guarded, lossy `ToPagedList()` bridge rather than returning
  `PagedList<T>` directly.

**How to apply:** if a future session needs to re-verify one of these (e.g. before Scaffold/Core
implementation), a quick grep suffices — these are stable, foundational types unlikely to have
changed, but "the memory says X" is not "X exists now," so re-check before writing code that depends
on it, not before writing planning docs.
