---
name: project-09search-wo055-tests-unblocked-t27-t34
description: SK.09.Tests WO-055 sub-pass (T-27–T-34) unblocked and shipped — 16.Testing P-355 cleared, real blocker-clearance verification steps, and four reusable test techniques with confirmed real-engine findings
type: project
---
> WO-086 (2026-09): `ISearchIndexProvisioner.ProbeAsync` was removed — readiness is one `IReadinessProbe` per registered index (`SearchIndexReadinessProbe`, `search-{provider}-{index}`); the search-local `TenantScope` (string-keyed `TenantScope.Of(...)`) is now the single `SharedKernel.Execution.Tenancy.TenantScope` (`Global`, `For(TenantId)`, `FromNullable`); container fixtures live in `16.Testing/SharedKernel.Testing.Internal/Containers/` and the in-memory fakes in `SharedKernel.Search.Testing`. The findings below are history.

Session: the `⚑` Blocked WO-055 sub-pass (T-27–T-34, recorded blocked 2026-08-10 pending `16.Testing`'s P-355) was re-verified and found genuinely unblocked, then fully implemented and shipped 2026-08-11. `SK.09.Tests` is now 34/34 `●`; only `SK.09.Published`'s P-09/P-10 (re-pack) remain across the whole domain.

**Why this matters:** confirms the domain's own "annotate, never silently rewrite, always re-verify on disk before trusting a prior blocker record" discipline works end-to-end across a multi-session blocker — the blocker record from `[[project_09search_wo055_tests_phase_blocked]]` was genuinely temporary, not permanent, and the unblock happened exactly the way that entry's own "unblocks when" clause predicted.

**How to apply:** if a future session finds `SK.09.Tests` (or any WO-055-adjacent phase) marked blocked again, do not trust the record — always re-run the verification sequence below first.

## Blocker-clearance verification sequence (repeatable pattern)
1. Read the blocking file directly (`16.Testing/SharedKernel.Testing/Search/InMemorySearchIndex.cs`) — check for the specific missing members named in the blocker record.
2. Run a real `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` — 0 errors is the actual proof, not the source read alone (a source read can miss a subtler compile error elsewhere).
3. Run a real `dotnet build` on each of the three `09.Search` `.Tests` projects individually — confirms the transitive failure is actually gone for every consumer, not just the root package.
Only after all three pass does the blocker actually clear. This sequence took ~2 minutes total and immediately resolved what could have been another blocked-session report.

## Four reusable test techniques discovered this session (recorded in `09.Search/CLAUDE.md` Test Rules — this is the compressed pointer, not a duplicate)

1. **Counting-serializer spy via `Elastic.Clients.Elasticsearch`'s `SourceSerializerFactory` seam** (T-27, proves ES bulk-write serializes each document exactly once). `ElasticsearchClientSettings(NodePool, SourceSerializerFactory)` — the factory delegate is `Serializer SourceSerializerFactory(Serializer builtIn, IElasticsearchClientSettings settings)`. Wrap a real `DefaultSourceSerializer` in a `Serializer` subclass overriding `Serialize<T>`/`Serialize(object,Type,...)`/`SerializeAsync` variants (all abstract on `Elastic.Transport.Serializer`, discovered by reflecting the real compiled `Elastic.Transport` 0.17.1 assembly — NOT the `8.0.1` the domain's own CLAUDE.md Technology Stack table claims; the actual resolved version per `project.assets.json` is `0.17.1`, a stale-doc discrepancy worth flagging if ever touching that table again). Reflection-invoke the PRIVATE `SerializeAndBatch`/`SerializeIndexOperation` methods directly rather than going through the public `IndexManyAsync` — this avoids any network dependency entirely (no real ES container needed), since serialization is fully client-side before the transport call.

2. **Fake `HttpMessageHandler` under a real SDK client** (T-28, proves Meilisearch `GetAsync`'s null-deserialization path). `global::Meilisearch.MeilisearchClient` ships no interface (confirmed precedent from `MeilisearchPreflightValidationTests`), so the established "null client" trick only proves no-I/O, never a controlled response. Building `new MeilisearchClient(new HttpClient(fakeHandler), apiKey)` where the fake handler returns `200 OK` + literal `"null"` body reaches a scenario a real healthy engine can never itself produce (JSON `null` for an existing document) — `JsonElement.Deserialize<T>()` returns `null` for a reference type when the underlying token is JSON `null`.

3. **Verified real-engine finding — Meilisearch fails the WHOLE TASK when a document lacks its declared primary-key field.** Provision an index with `PrimaryKey("sku")`, submit a document whose `sku` property is `[JsonIgnore(Condition = WhenWritingNull)]`-omitted from the wire payload — produces a genuine `TaskInfoStatus.Failed`. Combined with `MeilisearchOptions.DefaultBatchSize = 1` (one doc per batch), this is the ONLY reliable way found to force a genuine per-batch/per-document Meilisearch write failure through the real engine — the domain's only other write-rejection path (`InvalidDocumentId`) rejects the WHOLE `IndexManyAsync` call upfront before any batch dispatches, so it can never exercise the per-batch failure-attribution code path.

4. **Verified real-engine finding — ElasticSearch produces a genuine per-item `mapper_parsing_exception` independent of siblings.** A document field declared `SearchFieldKind.Integer` (mapped ES `integer`) fed a non-numeric string (via an `object`-typed C# property — STJ serializes by runtime type, so `Stock = 10L` → number, `Stock = "x"` → string) is rejected for THAT document alone; `response.ItemsWithErrors` reports it while siblings in the same `_bulk` batch succeed. Reliable technique for forcing genuine per-item ES bulk failures without fabricating client-side rejection.

## Regression baseline update
345 → 371 tests (172 Abstractions [+17], 96 Meilisearch [+4], 103 ElasticSearch [+5]), zero failures, zero regressions. Future sessions should cite 371 as the new "full suite" baseline, not 345.

## Cross-references
- [[project_09search_wo055_tests_phase_blocked]] — the prior session that correctly found this blocked and wrote zero test code rather than working around it; this session is the direct sequel.
- [[project_09search_t26_and_completion]] — established the cross-provider parity suite pattern and the Meilisearch-empty-`Or`-rejects/ES-empty-`Or`-match-all divergence this session's engine-behavior findings sit alongside.
