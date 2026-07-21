---
name: wo045-intelligence-buildout
description: 10.Intelligence full build-out — package-split ratification (Shape C), Microsoft.Extensions.AI.Abstractions declined, P-279-286
metadata:
  type: project
---

10.Intelligence's first real build-out, dispatched as WO-045 (P-279–P-286) on 2026-07-21, directly on user request to analyze and design the `10.Intelligence` (AI / vector retrieval) capability domain to gold-standard, developer-friendly fit.

**Starting state:** the domain's own `10.Intelligence/CLAUDE.md` was already an exceptionally rigorous pre-Design brief — eight binding Domain Invariants, a full hard-violations list, AOT posture, and test rules, all derived correctly from the `09.Search`/`08.Storage` precedents (intersection-only seam rule, fail-loud tenant scope, streaming-not-Result-wrapped, probe-primitive-not-IHealthCheck). On disk: four bare placeholder `.csproj` files with zero `.cs` content (`SharedKernel.AI.Abstractions`, `SharedKernel.AI.VectorDb` (+ nested `.Tests`), and a non-conventional domain-root `SharedKernel.AI.Tests`).

**What made this WO different from WO-043/WO-044:** the brain didn't just document rules — it explicitly deferred two architectural-authority decisions to whoever ran Design, rather than settling them by accident:

1. **Package split.** Three shapes offered: (A) as-scaffolded single `.VectorDb` — violates the platform's own multi-provider-split naming rule the moment a second vector engine (Milvus) lands; (B) `.VectorDb.Qdrant`/`.VectorDb.Milvus` — misapplies the `.{Provider}.{Role}` pattern, which the root brain reserves for *one* technology serving multiple architectural roles (the `02.Caching.Redis.*` precedent), not two competing sibling engines; (C) `.Abstractions` + `.Qdrant` + `.Milvus` + a separate orchestration package — matches the `08.Storage` (`.S3`/`.Obs`) and `09.Search` (`.Meilisearch`/`.ElasticSearch`) sibling-provider precedent this domain most resembles.
2. **`Microsoft.Extensions.AI.Abstractions` adoption.** Adopting it as the `.Abstractions` type surface would put the platform's first-ever `PackageReference` inside a `.Abstractions` package — every prior one (`Caching`, `Persistence`, `Messaging`, `Storage`, `Search`) has zero third-party dependency. Re-declaring instead means every provider converts at its own boundary.

**Decision (UPGRADE verdict — arch-lead resolved both rather than leaving them to a later Design session):**
- Package split ratified as **Shape C**: `SharedKernel.AI.Abstractions` + `SharedKernel.AI.Qdrant` + `SharedKernel.AI.Milvus` + `SharedKernel.AI.SemanticKernel` (LLM orchestration named after its concrete technology, consistent with the platform's `.{Capability}.{Provider}` naming convention). The existing `SharedKernel.AI.VectorDb` placeholder is retired; the non-conventional domain-root `SharedKernel.AI.Tests` is re-homed to nest inside `.Abstractions` per the Test Project Rules — both folded into P-279's acceptance criteria as on-disk cleanup, not a separate phase.
- `Microsoft.Extensions.AI.Abstractions` **declined** as the `.Abstractions` surface. A hand-rolled, zero-`PackageReference` neutral contract is authored instead, preserving the platform's unbroken precedent. Providers remain free to adapt `Microsoft.Extensions.AI` types internally.

**Phases (P-279–P-286, 8 total — one more than WO-044's 7 because this domain has three sibling providers, not two):**
- P-279 (10.Intelligence, Depends: None) — Abstractions contract finalization + the two ratifications above, plus the on-disk cleanup.
- P-280 (10.Intelligence, Depends: P-279) — `SharedKernel.AI.Qdrant`, primary vector DB via `Qdrant.Client`.
- P-281 (10.Intelligence, Depends: P-279) — `SharedKernel.AI.Milvus`, secondary vector DB via `Milvus.Client`. Carries forward the domain brain's own flag that `Milvus.Client`'s maintenance status is unverified and historically lags the server release line (the `NEST`/`09.Search` EOL-check precedent) — made a hard precondition on the phase rather than silently dropped.
- P-282 (10.Intelligence, Depends: P-279) — `SharedKernel.AI.SemanticKernel`, LLM orchestration via `Microsoft.SemanticKernel`. Isolated in its own package specifically because Semantic Kernel's function-calling/plugin model is reflection-heavy and non-AOT-safe — never referenced from `.Abstractions` or the vector-DB providers, limiting blast radius per the root brain's AOT guidance.
- P-283 (16.Testing, Depends: None) — Qdrant + Milvus Testcontainers fixtures, mirroring the `MeilisearchContainerFixture` hand-rolled-if-no-official-module pattern.
- P-284 (16.Testing, Depends: P-279) — in-memory doubles for embedding generation (deterministic hash-derived vector — no model/network needed, directly serving this domain's non-determinism invariant), vector-collection, and orchestration (canned-response double, never generates text, so it can't violate the "never assert on model-generated text" test rule).
- P-285 (13.ServiceDefaults, Depends: P-279/280/281/282) — readiness health check + string-name-only telemetry wiring, zero ProjectReference back to 10.Intelligence.
- P-286 (00.Governance, Depends: P-279/280/281/282) — NetArchTest topology suite + layering assertion + raw-SDK-injection/raw-model-id-literal analyzer coverage.

Domain board was `○ Not Started` → `state-map-phase` called, now `◐ Design`. 16.Testing/13.ServiceDefaults/00.Governance were already past `○` (all `●`), so no `state-map-phase` calls for those three — backlog-only, per the standing rule.

`sync-brain` called: Folder Map row 10 rewritten from the vague "Qdrant / Milvus vector db, LLM orchestration" phrasing to name all three concrete provider packages; Abstractions table's `SharedKernel.AI.Abstractions` row corrected from the stale single `.VectorDb` entry to `.Qdrant`, `.Milvus`, `.SemanticKernel`; eight new "What Goes Where" rows added (embedding generation, vector-collection CRUD/query, LLM orchestration, Qdrant provider, Milvus provider, provider-exclusive vector-DB capability placement, readiness-probe split with 13.ServiceDefaults, 16.Testing in-memory doubles, raw-SDK-client-injection prohibition).

See [[project_phase_numbering]] for the numbering-state summary of this same work order.
