---
name: project_09search_published_phase
description: SK.09.Published phase completion — consumer-verify three-project split, negative-compile probe technique, 09.Search domain fully complete
type: project
---

09.Search reached Published on 2026-07-20 (SK.09.Published, P-01–P-08, 8/8). All six phases (Design/Scaffold/Core/Tests/Docs/Published) are now `●` for all three packages (`SharedKernel.Search.Abstractions`, `.Meilisearch`, `.ElasticSearch`) — WO-044 complete end to end, 139/139 tasks. No further `search-phase-implementer` work is expected on this domain unless a new work order lands.

**Why:** Final phase in the domain's implementation lifecycle — NuGet packaging, pack, and consumer verification through a real `IHost.StartAsync()`.

**How to apply:** If invoked again for `09.Search`, check `09.Search/state-map.md`'s Overall Progress first — it should already show 139/139 `●`. Only act if a new phase key (a new work order) has been added.

Key decisions made this session, useful if a future domain (or a future 09.Search work order) needs the same pattern:

1. **consumer-verify split into THREE projects, not one** (`09.Search/consumer-verify/Meilisearch/`, `ElasticSearch/`, `BothProviders/`) — every prior domain (`08.Storage`/`13.ServiceDefaults`/`14.Presentation`/`15.Integration`/`04.Contracts`) shipped exactly one `consumer-verify.csproj`. This domain needed three because the P-06 capability-segregation proof requires two **disjoint** compilation closures (Abstractions+Meilisearch only vs. Abstractions+ElasticSearch only) — a single project referencing both providers can never prove segregation, since both exclusive interfaces (`IAnalyticsSearch<>`/`ICursorSearch<>` vs `IInstantSearch<>`/`ITenantSearchTokenIssuer`) would simply be nameable together in one compilation. The third project (`BothProviders`) exists solely to demonstrate the same-`TDocument` dual-registration hard violation, which needs both providers referenced at once. **If a future domain has a similar provider-exclusive-contract compile-time-segregation requirement, expect the same N-project split, not a single harness.**

2. **The negative-compile-probe technique** — the mechanically honest way to prove "type X is unnameable from this compilation unit" as a genuine build-time fact, not an assertion in prose: append a disallowed `using` + bare field declaration of the forbidden type directly into the real harness `Program.cs`, with **no** matching `<ProjectReference>` added, run `dotnet build`, capture the real compiler diagnostics (got `CS0234` "namespace does not exist" + `CS0246` "type not found" in both directions here), then immediately revert the probe via a file backup (`cp file file.bak` → edit → verify → `cp file.bak file` → `rm file.bak`). **Rejected approach:** copying the project to an unrelated scratch directory with broken relative `<ProjectReference>` paths — this produces the *same* CS0246 for every reference in the file, not just the intentionally disallowed one, so it doesn't isolate the claim. Always do the probe **in place**, in the real project, with the reference genuinely absent by omission (not by path breakage).

3. **`IClock` must be registered by the consuming host** — already known from `SK.09.Tests`, but this session confirmed it bites at the real `Host.CreateApplicationBuilder()` composition level too (not just DI-container unit tests): any harness surface resolving `ISearchIndex<TDocument>` (which needs `IClock` inside `MeilisearchIndex`/`ElasticSearchIndex` for `SearchWriteReceipt.AcceptedAt`) throws `InvalidOperationException` unless the harness calls `builder.Services.AddSingleton<IClock, SystemClock>()` itself. Neither `AddSharedKernelMeilisearchSearch()` nor `AddSharedKernelElasticSearchSearch()` self-registers it — same precedent as `ILogger<T>`.

4. **ElasticSearch consumer-verify surfaces must set `ValidateEngineVersionOnStart=false`** — the option defaults `true` in production and makes a synchronous blocking `client.InfoAsync().GetAwaiter().GetResult()` network call on first `ElasticsearchClient` singleton resolution. Leaving it at the default would make a packaging-verification harness's pass/fail depend on Docker/a live cluster being present, which defeats the point of a lightweight consumer-verify harness (the live-engine guard already exists at `SK.09.Tests` T-22 against a real Testcontainers Elasticsearch).

5. **P-01 found nothing to fix** — all three `.csproj` files already carried `PackageReadmeFile` + packed `README.md` correctly since this domain's own Docs phase. This is the first domain where the `08.Storage`-flagged Published-phase gap (README wired into pack) did **not** recur — confirms front-loading that pair into the Docs-phase metadata block (as this domain's own `CLAUDE.md` already documented as a lesson) actually works.

See also [[project_09search_t26_and_completion]] for the SK.09.Tests/Docs completion context this phase built on.
