---
name: project_09search_wo055_core_phase
description: 09.Search WO-055 SK.09.Core sub-pass (C-49–C-55) — ES bulk raw-PostData rewrite, Meilisearch throttle restructuring, cross-domain 16.Testing collateral handling
type: project
---

Session: WO-055's final `SK.09.Core` sub-pass, 7 tasks (C-49–C-55), all shipped 2026-08-10. `SK.09.Core` now 55/55 `●`. Design (D-29–D-35) and Scaffold (S-14) were already `●` from prior sessions — see [[project_09search_wo055_design_phase]].

## C-49 — ElasticSearch bulk single-serialization fix — the resolved SDK-shape finding

D-29 deferred the exact mechanism to Core time. Resolved by reflecting the real compiled `Elastic.Clients.Elasticsearch` 9.4.2 / `Elastic.Transport` assemblies via a throwaway console harness (`dotnet run` against a scratch csproj referencing the real NuGet package) — the domain's own established "reflect the real compiled assembly" methodology.

**Finding:** `Elastic.Clients.Elasticsearch.Core.Bulk.BulkIndexOperation<T>` has **no** pre-serialized-bytes constructor — only `ctor(T document)` and `ctor(T document, IndexName index)`. So the first candidate mechanism D-29 named does not exist.

**Resolved mechanism:** route through `Elastic.Transport`'s raw `PostData` seam.
- `ElasticsearchClient.Transport` is `ITransport<IElasticsearchClientSettings>`, implementing non-generic `ITransport`.
- `Elastic.Transport.TransportExtensions.RequestAsync<TResponse>(ITransport, HttpMethod, string path, PostData postData, CancellationToken)` — the SAME low-level entry point the SDK's own generated typed methods (`BulkAsync`, `IndexAsync`, etc.) call internally. `HttpMethod` here is `Elastic.Transport.HttpMethod` — **ambiguous** with `System.Net.Http.HttpMethod` under `ImplicitUsings` (that BCL namespace is a default global using for ALL SDK project types, not just Web SDK) — must fully-qualify as `Elastic.Transport.HttpMethod.POST` at the call site even with `using Elastic.Transport;` present.
- `BulkResponse` has a public parameterless ctor, satisfying `RequestAsync<TResponse>`'s `where TResponse : TransportResponse, new()` constraint.
- Serialize each document's `_bulk` action-meta+source pair EXACTLY ONCE via `BulkOperationsCollection.Serialize(stream, _client.ElasticsearchClientSettings, SerializationFormatting.None)` — the SDK's OWN serialization routine (never a standalone raw STJ call), so it's byte-identical to what `BulkAsync` would produce and automatically honors `.WithSourceSerializerContext(...)`.
- That buffer's `.Length` feeds chunk-boundary sizing AND, unmodified, becomes part of the wire body via concatenation.
- **Verified functionally** (not just asserted): concatenating N independently-serialized single-document `BulkOperationsCollection` buffers is byte-identical to serializing them together as one collection — confirmed via a live probe against the real SDK (each buffer is an independently complete, newline-terminated `{action}\n{source}\n` pair; NDJSON has no wrapping structure so concatenation is safe).
- Refresh mode travels as a **query-string parameter on the path** (`"{alias}/_bulk?refresh={value}"`, matching `EndpointPath.PathAndQuery`'s documented shape) since the typed `BulkRequest.Refresh` property is bypassed entirely.
- No special content-type handling needed: `ProductRegistration.DefaultContentType` is used uniformly for ALL requests through the client (confirmed via reflection — no bulk-specific content-type override exists anywhere in the SDK), so the raw `RequestAsync<TResponse>` call gets identical headers to any typed call.

`DeleteManyAsync` is UNAFFECTED by this fix — it builds `BulkDeleteOperation`s with no document body, so no serialization redundancy existed there; it still calls the typed `_client.BulkAsync(request, ct)`.

**Why this matters for future ES SDK work:** this is the second time this domain has had to reflect the real 9.4.2 assembly to resolve an "open SDK-shape question" (first was D-23's leaf-query constructor shape at original Core-phase). The technique — build a throwaway console project referencing the exact pinned package version, reflect types via `BindingFlags.Public|NonPublic`, and where needed write a small functional probe (not just signature inspection) — is the domain's reliable way to de-risk ES SDK uncertainty before committing to an implementation.

## C-50 — Meilisearch GetAsync fix

Trivial: replaced `?? throw new InvalidOperationException(...)` with `Result<TDocument>.Failure(SearchErrors.EngineFault(SearchWellKnown.MeilisearchProviderName, "GetAsync", "..."))`, reusing the existing `MeilisearchEngineFault` log call (no new EventId). Re-verified against a real Meilisearch container (existing GetAsync tests) — no regression.

## C-51/C-52 — SearchBulkWriteOptions + additive overloads

`SearchBulkWriteOptions` is a sealed record with a validating `init` accessor on `MaxBatchesPerSecond` (backing field + throw `ArgumentException` for `<= 0`) — this is the idiomatic pattern for a guarded record property in this codebase when there's no dedicated static factory method (contrast `TenantScope.Of`/`SearchFilter.Between`, which ARE static factories because the type has more than one member or non-trivial construction).

The two new 4-arg overloads on `ISearchIndex<TDocument>` are genuinely distinct overloads (never a default-param insertion) per D-33. When adding XML-doc `<see cref="...">` references to an overloaded member from elsewhere in the same file, a bare `<see cref="MethodName"/>` throws `CS0419` (ambiguous cref) once 2+ overloads exist — must either fully qualify with parameter types (`<see cref="MethodName(Type1, Type2, ...)"/>`) or fall back to `<c>MethodName</c>` prose. Hit this in `ISearchIndex.cs`'s pre-existing class-level `<remarks>` ("Bulk partial failure is not collapsed" note) which referenced `IndexManyAsync`/`DeleteManyAsync` by bare name — fixed by switching to `<c>` tags.

## C-53/C-54 — throttle asymmetry between IndexManyAsync and DeleteManyAsync, on BOTH providers

**Key design realization** (not explicit in D-34's text, an implementer judgment call applied consistently to both providers): `DeleteManyAsync` on BOTH ElasticSearch and Meilisearch dispatches the caller's WHOLE id collection as a SINGLE request — neither provider has ever chunked deletes. So the 4-arg `DeleteManyAsync` overload accepts `bulkOptions` for interface parity but has **no observable throttle effect** (nothing to pace between, since there's only one dispatch). This is NOT a bug or a silent-degradation violation — it's the honest consequence of "the throttle paces an existing batch-dispatch loop" when no such loop exists for deletes. Documented via an XML-doc `<remarks>` block on each 4-arg `DeleteManyAsync` override, explicitly cross-referencing the sibling `IndexManyAsync` overload that DOES chunk. Do NOT introduce new chunking to `DeleteManyAsync` to make the throttle "do something" — that would change "today's unthrottled behavior" when unconfigured, violating the phase's own byte-for-byte-preservation requirement.

**ElasticSearch IndexManyAsync throttle:** trivial — insert `await Task.Delay(...)` before each batch dispatch after the first, inside the existing `foreach (var batch in SerializeAndBatch(...))` loop. No restructuring beyond C-49's own rewrite.

**Meilisearch IndexManyAsync throttle — required genuine restructuring**, exactly as D-34 anticipated: the SDK's `AddDocumentsInBatchesAsync(documents, batchSize, primaryKey, ct)` issues every batch internally with zero seam for an inter-batch pause. Replaced with an explicit `for` loop chunking `documentList` into `DefaultBatchSize`-sized slices via `List<T>.GetRange(start, count)` and calling the SDK's single-batch `AddDocumentsAsync(batch, primaryKey, ct)` per slice (the same call `IndexAsync` already makes with a one-element list). This preserves EXACTLY the same batch boundaries `AddDocumentsInBatchesAsync` produced (sequential, order-preserving, `DefaultBatchSize`-sized) — verified by keeping the SAME `batchIndex`/`batchStart` math the pre-existing failure-attribution logic (further down in the method) already used, so that logic needed zero changes. Gotcha: the failure-attribution block had its own local `var documentList = documents.ToList();` — after hoisting `documentList` to the top of the method (needed for the batching loop), the inner redeclaration became a `CS0136` variable-shadowing error and had to be removed.

## Cross-domain collateral: 16.Testing's InMemorySearchIndex<TDocument> break is EXPECTED, do not fix it here

The moment C-52 ships the two new `ISearchIndex<TDocument>` interface members, `16.Testing/SharedKernel.Testing/Search/InMemorySearchIndex.cs` stops compiling (CS0535, missing interface members). **This is fully anticipated and pre-documented** — both `09.Search/CLAUDE.md` and `16.Testing/CLAUDE.md` explicitly record this as `16.Testing`'s own P-355, "the domain's 16.Testing in-memory fake gets updated in its own dependent phase, never bundled into the producing domain's phase" (per root `CLAUDE.md`'s own "What Goes Where" row for this exact scenario). **Do not touch 16.Testing files to fix this** — it's a different domain's phase.

Consequence: all three `09.Search` `.Tests` projects (which reference `SharedKernel.Testing`) fail to build transitively the moment C-52 ships, and this Core phase's own spec explicitly says "Test-writing is deliberately deferred to SK.09.Tests — this phase contains only Implement/Confirm tasks," so there's no mandate to run tests in this phase at all.

**Verification workaround used (recommended for any future producing-domain phase hitting this same cross-domain-fake-break pattern):** applied a TEMPORARY, purely local, NEVER-COMMITTED patch to `16.Testing/SharedKernel.Testing/Search/InMemorySearchIndex.cs` — added two stub 4-arg overloads that simply delegate to the existing 3-arg implementations (ignoring `bulkOptions`) — solely to unblock local `dotnet test` execution. Ran all three `.Tests` projects (155 Abstractions + 92 Meilisearch + 98 ElasticSearch = 345), confirmed 100% green, THEN reverted via `git checkout -- <file>` before finishing (confirmed via `git status` showing 16.Testing clean). This is legitimate because it's diagnostic-only and never persisted — distinct from "building a competing ad-hoc fake," which is prohibited. Verified `git status 16.Testing/` was clean BEFORE starting the patch and clean again AFTER reverting.

**Bonus finding from this verification: Docker IS available in this environment.** The Meilisearch/ElasticSearch `.Tests` projects' real-backend tests (via `16.Testing`'s `MeilisearchContainerFixture`/`ElasticsearchContainerFixture`) ran successfully against real Docker containers during this diagnostic run (92/92 and 98/98 passing, ~56s and ~3m25s respectively) — this gave genuine real-engine confirmation that C-49's ES bulk rewrite and C-50's Meilisearch GetAsync fix introduced zero regressions, stronger evidence than the Core phase's own spec required. Future `09.Search` Core-phase sessions touching either provider's write/read path should consider this same diagnostic-patch-then-revert technique to get real-container confirmation even when the phase spec doesn't mandate test-writing.

## Real regression found and fixed in EXISTING test (in-scope, not deferred)

`SharedKernel.Search.Abstractions.Tests/ContractShapeTests.cs`'s `EveryWriteMember_TakesNonOptional_SearchWriteConsistencyParameter` theory used `SearchIndexType.GetMethod(memberName)` — this throws `AmbiguousMatchException` the moment `IndexManyAsync`/`DeleteManyAsync` become overloaded (C-52). This is a genuine regression in MY OWN package's existing test (not 16.Testing's), so it's in scope to fix even though "test-writing is deferred" — fixing an EXISTING test broken by my own change is maintenance, not new test authorship. Fixed by switching to `GetMethods().Where(m => m.Name == memberName)` and asserting the `SearchWriteConsistency` parameter is non-optional on EVERY matching overload.

## Related memories
[[project_09search_wo055_design_phase]] — the D-29–D-35/S-14 predecessor session that locked the design this phase implements.
[[project_09search_core_phase]] — original Core-phase SDK reflection-verification technique this session's C-49 investigation directly extends.
