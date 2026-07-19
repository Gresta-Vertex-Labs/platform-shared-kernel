---
name: seam-rule-adjudications
description: Concrete calls on what belongs in .Abstractions vs a provider package vs declined outright, with the engine asymmetry that decided each one
metadata:
  type: feedback
---

The whole domain design turns on one question asked of every proposed neutral-surface member: can
**both** Meilisearch and ElasticSearch implement this completely and correctly? Below are the
specific adjudications made during WO-044 (P-272/273/274), so a future "just add one more thing"
request can be checked against precedent instead of re-litigated from scratch.

**Why this matters:** a leaky search abstraction doesn't throw at runtime — it returns a plausible,
silently wrong answer (dropped tenant filter leaks cross-tenant data; a coerced clause returns wrong
facet counts). So the bar for "accepted into `.Abstractions`" is much stricter than for most other
capability domains in this repo.

**How to apply:** when a new capability request lands, ask "which engine breaks, and how (throw /
degrade / approximate / no-op)?" before designing anything. If the answer is "neither breaks,"
design it as a neutral member. If one engine has no honest equivalent, it becomes a provider-package
contract (compile-time swap failure), never a neutral member with a runtime capability flag.

**Declined outright (not even provider-exclusive) — filter/index-definition surface:**
- Fuzzy/TypoTolerance node — Meilisearch applies it by default with word-length thresholds; ES
  requires explicit `fuzziness` with different edit-distance behavior. A boolean that's a no-op on
  one side and a query-plan change on the other.
- Boost/FunctionScore — not expressible in Meilisearch's filter DSL at all (it's index-settings
  there, not per-query).
- IsNull/IsEmpty — no faithful ES equivalent for IS EMPTY; IS NULL conflates null-value with
  field-absent.
- GeoRadius — present on both but divergent distance semantics/units; deferred pending real-container
  verification, not designed blind.
- Prefix/Wildcard/Regex — analysis-time concerns, expressed through tokenization instead.
- Nested/object-array filter — the sharpest one. ES `nested` mapping preserves intra-element field
  correlation; Meilisearch flattens. `size=="M" AND colour=="red"` over
  `variants:[{M,blue},{L,red}]` matches on Meilisearch and does NOT match on ES-with-nested. Rejected
  as a correctness hazard, not a feature gap. Mandated workaround: flatten at document-mapping time
  into a precomputed composite filterable field, filter with `In(...)`.
- Score/relevance value on `SearchHit`/`SearchResults` — Meilisearch bucket-sorts through ranking
  rules, ES computes BM25; no shared scale, range, or monotonicity. `Rank` (0-based page ordinal) is
  the only portable ordering signal.
- Analyzer/tokenizer/normalizer/language knob on `SearchFieldDefinition` — the single type to guard
  hardest; every "just one more knob" request here is a lie about the other engine. Only six
  `SearchFieldKind` values and four role booleans, permanently.
- Synonym map on `SearchIndexDefinition` — considered and rejected in a prior conversation (see the
  arch-lead's own worked example): synonym semantics diverge between engines.
- Optimistic-concurrency `Version` member — ES has `if_seq_no`/`if_primary_term`, Meilisearch has
  nothing comparable (last-write-by-arrival-order). Rather than carry a member one adapter silently
  ignores, the contract carries none and documents the requirement instead (upstream ordering via
  DocumentId partitioning).
- Capability-flags enum on `ISearchProviderDescriptor` — an `if (caps.HasFlag(...))` branch at a call
  site is itself the violation; the compile-error-on-swap mechanism (provider-exclusive contracts) is
  the only sanctioned capability-branching mechanism.

**Pushed to provider packages (provider-exclusive contracts, not neutral):**
- `IInstantSearch<TDocument>` + `ITenantSearchTokenIssuer` → `SharedKernel.Search.Meilisearch` only.
  Typo tolerance/prefix/crop and engine-enforced per-tenant JWT search tokens have no ES equivalent
  (ES doc-level security is commercial-tier; the OSS substitute is deployment config, not a runtime
  API — a neutral "issue a scoped token" contract whose ES adapter returns a locally-signed,
  unenforced token would be actively dangerous: identical at the type level, opposite in effect).
- `IAnalyticsSearch<TDocument>` + `ICursorSearch<TDocument>` → `SharedKernel.Search.ElasticSearch`
  only. Meilisearch offers facetDistribution + facetStats (min/max) and nothing else — no sum/avg/
  cardinality/percentiles/date_histogram. The gap is absence, not degree — a neutral aggregation
  model would be simultaneously a bad ES client and an unimplementable Meilisearch adapter. Cursor
  deep-pagination: Meilisearch has no `search_after`/PIT and is hard-capped by `maxTotalHits`, so
  faking a cursor via a loop would silently stop at 1000 docs — declined rather than faked.

**Accepted into `.Abstractions` despite being the "one place the thin-abstraction premise is
compromised":**
- `SearchFieldDefinition`/`SearchIndexDefinition` field-role declarations (Searchable/Filterable/
  Sortable/Facetable). Unavoidable: Meilisearch REJECTS a filter/sort on an attribute absent from its
  index settings, while ES accepts any mapped field at query time. If field roles were provider-
  private, the identical `SearchRequest` would succeed on ES and 400 on Meilisearch.
- `MaxTotalHits` forced to 1000 (Meilisearch's ceiling) on BOTH providers, including ES whose native
  `index.max_result_window` default is 10,000. Deliberately hobbles the stronger engine so a query
  proven legal on one provider is guaranteed legal on the other. Documented as "the change most
  likely to generate 'the abstraction broke my search' tickets" — expected friction, taken anyway.

**Escape hatch, not a widened neutral surface:**
- `IMeilisearchRawClientAccessor` / `IElasticSearchRawClientAccessor` — genuine last resort (ES
  percolators/`function_score`; Meilisearch hybrid/vector/federated multi-search). Three gates:
  opt-in `.AllowRawClientAccess()`, startup `Warning` log, governance architecture test asserting no
  in-repo consumer. **The hatch bypasses tenant scoping** — TenantScope injection happens inside the
  neutral translator, so a raw client call gets none of it. Never relax any of the three gates.
