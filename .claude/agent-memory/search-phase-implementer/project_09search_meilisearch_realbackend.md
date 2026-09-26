---
name: project_09search_meilisearch_realbackend
description: SK.09.Tests T-13-T-17 (Meilisearch real-backend) session — 3 genuine production bugs found+fixed, MeiliSearch 0.20.0 SDK exception-inconsistency catalog, tenant-token gotchas, EnumerateAsync ordering confirmed
type: project
---
> WO-086 (2026-09): `ISearchIndexProvisioner.ProbeAsync` was removed — readiness is one `IReadinessProbe` per registered index (`SearchIndexReadinessProbe`, `search-{provider}-{index}`); the search-local `TenantScope` (string-keyed `TenantScope.Of(...)`) is now the single `SharedKernel.Execution.Tenancy.TenantScope` (`Global`, `For(TenantId)`, `FromNullable`); container fixtures live in `16.Testing/SharedKernel.Testing.Internal/Containers/` and the in-memory fakes in `SharedKernel.Search.Testing`. The findings below are history.

Completed 2026-07-20: `16.Testing`'s `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` landed
(confirmed on disk, smoke-tested), unblocking `SK.09.Tests` T-13–T-17 (Meilisearch real-backend). All five
implemented in `SharedKernel.Search.Meilisearch.Tests/{Containers,RealBackend}/` +
`SiblingIndependenceTests.cs`, 79/79 green. See [[project_09search_tests_phase]] for the prior
container-free session's findings (T-01–T-12/T-18–T-20) — that file's "blocked" framing at its top is now
stale; T-13–T-17 are done, documented here instead.

**Session note**: another process was actively writing into this exact `.Tests` project concurrently with
this agent's own work (files for T-13/14/15/16 appeared mid-session without this agent's own tool calls
succeeding at creating them) — converged on nearly the same design independently. Handled by always
reading current on-disk state immediately before every write and reviewing-rather-than-overwriting
existing content; caused no real conflict, but is worth remembering as "the filesystem can change between
your own tool calls in this environment — re-check before writing, every time," not just after a genuine
tool error.

## Three genuine production bugs found via real-backend testing, fixed (not deferred)

1. **`MeilisearchIndex.GetAsync` didn't catch the right exception for a missing document.**
   `Index.GetDocumentAsync<T>` throws a plain `System.Net.Http.HttpRequestException` (`StatusCode =
   NotFound`) on this SDK version's call path for a 404, not the SDK's own `MeilisearchApiError` — only
   the latter was caught. Fixed by adding a second `catch (HttpRequestException ex) when (ex.StatusCode ==
   HttpStatusCode.NotFound)` returning `SearchErrors.DocumentNotFound` alongside the existing
   `MeilisearchApiError` catch.
2. **`MeilisearchIndexProvisioner.IndexExistsAsync` (and `ProbeAsync`'s `IndexAddressable` check)
   ALWAYS reported `true`, for any index name, including ones that never existed.**
   `MeilisearchClient.GetIndexAsync(uid, ct)` does **not** throw for a non-existent index — it silently
   returns a synthetic `Index` object built from the requested uid with **no real network round trip ever
   performed**. Fixed by switching both call sites to `_client.Index(uid).GetSettingsAsync(ct)`, which
   **does** perform a real authenticated request and correctly throws — again as a plain
   `HttpRequestException` (not `MeilisearchApiError`) for both a missing index (404) and a mis-scoped key
   (403); both exception shapes are caught for forward-compatibility. **This was the single most dangerous
   of the three bugs** — `IndexExistsAsync` returning `true` unconditionally silently defeats
   `EnsureIndexAsync`'s own idempotent-creation logic (it would skip `CreateIndexAsync` even for a genuinely
   absent index) and made `ProbeAsync`'s mis-scoped-key detection structurally impossible.
3. **`MeilisearchResultMapper.MapFacets` NRE'd on virtually every non-faceted `SearchAsync` call.**
   `SearchResult<T>.FacetDistribution`/`.FacetStats` and `PaginatedSearchResult<T>`'s equivalents are
   genuinely **`null`** (not an empty dictionary) whenever the request did not ask for any facets — i.e.
   every ordinary search, not just an edge case. The old code called `.Count` unconditionally on
   `facetDistribution`. Fixed by widening both parameters to nullable and null-checking
   (`facetDistribution is null || facetDistribution.Count == 0`) before use.

**General lesson reinforced**: MeiliSearch 0.20.0's exception behavior is **wildly inconsistent per call
path** — never assume a new/unverified SDK call site throws the SDK's own `MeilisearchApiError`. Confirmed
failure-signaling shapes seen so far: `MeilisearchApiError` (some paths), a raw `HttpRequestException` with
`.StatusCode` populated (other paths — `GetDocumentAsync<T>`, `Index(uid).GetSettingsAsync()`), and — worst
of all, see below — a silently-swallowed default/garbage return value with **no exception at all**. Any
future new call site into this SDK must be empirically verified (real container, force the failure, observe
what actually happens), never assumed from another call site's behavior or from the SDK's own XML docs.

## The worst SDK-inconsistency case: a write rejection that throws NOTHING

`Index.AddDocumentsAsync` against an index the caller's key/token has no write action for does **not**
throw at all on this SDK version — it appears to return a `TaskInfo`-shaped value that leads a naive
`WaitForTaskAsync(that TaskInfo's TaskUid, ...)` poll to read back `Succeeded` (almost certainly because the
returned `TaskUid` is a garbage/default value — e.g. `0` — that happens to resolve to a real, unrelated,
already-succeeded task on the same index, such as the initial `EnsureIndexAsync` index-creation task,
producing a false-positive "it succeeded" reading). **Do not trust `AddDocumentsAsync`'s return value or
lack of a thrown exception as proof a write was accepted or rejected.** The only reliable proof for a
write-authorization test is a **decisive follow-up check with an unrestricted (master-key) client**: query
the document by id directly (`GetAsync`) and confirm it does or does not actually exist. This was
discovered mid-session chasing what first looked like a genuine multi-tenant security defect (a
`Search`-only-scoped key/token appearing to successfully write a document) — two rounds of task-uid-trusting
diagnostics both showed a false "succeeded," and only the direct-existence check revealed the write was
genuinely rejected all along. **Lesson for any future write-authorization/rejection test against this SDK:
go straight to the decisive existence check — never poll a task by the UID `AddDocumentsAsync` itself
returned when the call might have failed to properly surface an error.**

## Meilisearch key-management SDK surface (verified via reflection against the real 0.20.0 assembly)

```csharp
// Meilisearch.Key — ctor(), all properties settable
string KeyUid    // maps to JSON "key"  — the ACTUAL SECRET VALUE, despite the name (verified via a
                 // real round-trip deserialization probe, not assumed from the property name)
string Uid       // maps to JSON "uid"  — the UID identifier
string Name, Description
IEnumerable<KeyAction> Actions   // enum: All, AllGet, ChatCompletions, Search, DocumentsAll,
                                 // DocumentsAdd, DocumentsGet, DocumentsDelete, IndexesAll,
                                 // IndexesCreate, IndexesGet, IndexesUpdate, IndexesDelete, TasksGet,
                                 // TasksCancel, TasksDelete, SettingsAll, SettingsGet, SettingsUpdate,
                                 // StatsGet, DumpsCreate, Version, KeysAll, KeysGet, KeysCreate,
                                 // KeysUpdate, KeysDelete
IEnumerable<string> Indexes
DateTime? ExpiresAt, CreatedAt, UpdatedAt

MeilisearchClient.CreateKeyAsync(Key keyOptions, ct = default)        -> Task<Key>
MeilisearchClient.GetKeyAsync(string keyOrUid, ct = default)          -> Task<Key>
MeilisearchClient.GetKeysAsync(KeysQuery? query = null, ct = default) -> Task<ResourceResults<IEnumerable<Key>>>
MeilisearchClient.DeleteKeyAsync(string keyOrUid, ct = default)       -> Task<bool>
```

**The fixture's own master key is NOT resolvable via this surface** — `GetKeyAsync(fixture.ApiKey)` (or by
UID) throws `HttpRequestException` (404): the auto-generated master key is Meilisearch's bootstrap secret,
not a persisted `Key` database entity. Any test needing an `(ApiKey, ApiKeyUid)` pair for
`MeilisearchTenantTokenIssuer` (which needs both, matching, to sign+verify) must mint a **dedicated,
purpose-scoped key** via `CreateKeyAsync` instead — this is also the pattern a real deployment should use
(never issue tenant tokens signed directly by the master key).

## `MeilisearchTenantTokenIssuer` test-factory clock gotcha (real, would silently break every test)

`IssueAsync` computes `expiresAt = clock.UtcNow.Add(ttl)` and passes it directly to the real SDK's
`GenerateTenantToken(apiKeyUid, rules, apiKey, expiresAt.UtcDateTime)`, which **validates that DateTime
against real wall-clock time** server-side-equivalent logic ("Provide a valid UTC DateTime in the future").
`SharedKernel.Testing.Clocks.FakeClock`'s default baseline (fixed at 2024-01-01) is in the **past** relative
to real time in this repo's timeline (2026), so every `IssueAsync` call failed with
`TenantTokenIssuanceFailed` until the test-provider-factory's `CreateTenantTokenIssuer` methods were switched
to `SharedKernel.Primitives.Clocks.SystemClock`. **This is specific to the tenant-token issuer** —
`MeilisearchIndex`'s own `IClock` usage (for `SearchWriteReceipt.AcceptedAt`) is a purely local, unvalidated
timestamp and is unaffected; do not blanket-change every provider-factory clock to `SystemClock`, only the
ones whose value flows into a real engine-side time validation.

## `EnumerateAsync` ordering — CONFIRMED empirically (real container, v1.20.0)

`GET /indexes/{uid}/documents` (offset+limit) returns documents in **insertion order**, not
`DocumentId`-ascending order — verified via a dedicated out-of-id-order seed
(prod-010, prod-003, prod-007, prod-001, prod-009, prod-005 inserted in that scrambled order; the
returned enumeration matched insertion order, not id-ascending order). The shared `TestProductCorpus.All`
happens to be declared/seeded in ascending-`DocumentId` order, so a casual read of `EnumerateAsync`'s
output against that specific corpus looks "ascending by id" — that is a coincidence of this corpus's own
insertion order, not a Meilisearch ordering guarantee. Do not strengthen the `EnumerateAsync` "ordering
unspecified" contract note based on this — the finding confirms insertion order specifically, which is
still a distinct (if related) claim from "ascending by id," and ElasticSearch's own ordering (T-23) has not
yet been cross-checked against this for T-26 parity purposes.

## CORRECTED (2026-07-20, same-day T-26 session)

`CutoverAsync`'s pre-existing-live-index requirement IS real — the version of this note below was wrong,
kept struck-through for the record.

The original version of this note (written earlier the same day) claimed the T-16 cutover tests passed
with a never-provisioned `live` index "because Meilisearch itself doesn't require pre-existence" and
told future agents not to fix anything. **That conclusion was wrong** — it was observing the OUTPUT of a
fix that had already landed, not the underlying engine's native behavior, and mistakenly attributed the
adapter's own compensating logic to the engine. Ground truth, confirmed via raw HTTP directly against the
engine with NO adapter code in the path: `POST /swap-indexes` against a second index name that was never
created **fails** — the returned task's `status` is `failed`, `error.code` is `index_not_found`. Meilisearch's
swap genuinely requires both index names to pre-exist. `MeilisearchIndexProvisioner.CutoverAsync` now
compensates for this explicitly: it calls `IndexExistsAsync(request.LiveIndexName)` first, and if absent,
calls `CreateIndexAsync` (+ waits the task) to create an empty placeholder live index **before** issuing
the swap. This is why the T-16 tests pass without an explicit pre-provisioning step in the test itself —
the adapter does it for them, not the engine. If this compensating logic is ever removed from
`CutoverAsync`, the very first cutover of any freshly-provisioned service (staging exists, live never has)
will fail. Documented in `09.Search/CLAUDE.md` under `MeilisearchIndexProvisioner`'s Interface Contracts
block and in the T-26-session `state-map.md`/`CLAUDE.md` changelog entries.

~~## `CutoverAsync` does not require the live index to pre-exist~~ (WRONG — see correction above; original
text retained only so the reasoning trail is visible, never act on it): ~~Initially suspected a bug: two
of the T-16 cutover tests call `provisioner.CutoverAsync(...)` where `live` was never provisioned
beforehand. Expected `SwapIndexesAsync` to require both index names to already exist server-side and
therefore expected `CutoverAsync` to fail. Empirically this was unfounded — both tests passed against the
real v1.20.0 engine. Do not "fix" this — it is unnecessary and the current tests are correct as written.~~

## Scope discipline maintained

No hand-rolled competing container setup — the landed `16.Testing` `MeilisearchContainerFixture` was used
directly via a `[CollectionDefinition]`+`ICollectionFixture<T>` pair
(`Containers/MeilisearchCollection.cs`) and a companion `Containers/MeilisearchProviderFactory.cs`
(mirrors `08.Storage`'s `MinioProviderFactory` exactly), per the established cross-domain-dependency
pattern. `09.Search/CLAUDE.md`/`state-map.md` deliberately left untouched this session — a coordinating
session updates both centrally after the parallel ElasticSearch-side session (T-21–T-25) also finishes.

**UPDATE (2026-07-20, same-day coordinating session): done.** The coordinating session also implemented
T-26 (cross-provider parity suite) itself, found and corrected the `CutoverAsync` note above, and wrote
both `09.Search/CLAUDE.md` and `state-map.md` in full — all 26 `SK.09.Tests` tasks are `●`, see
[[project_09search_t26_and_completion]] for the T-26-specific findings (empty-`Or` divergence, the final
`EnumerateAsync`/tenant-facet CLAUDE.md wording) not covered by either provider-specific memory file.
