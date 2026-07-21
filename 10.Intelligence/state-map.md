# 10.Intelligence — State Map

> **What this file is:** Phase and task tracker for all work within `10.Intelligence`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.10.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
| --- | --- | --- |
| `SK.10.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.10.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.10.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.10.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.10.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.10.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement IEmbeddingGenerator | SK.10.Core | SharedKernel.AI.Abstractions | ◐ |
-->

---

## Blocked

_Nothing currently blocked._

> Record an inbound blocker here only with **on-disk evidence** (a path checked, a package confirmed absent from nuget.org), never an assumption. The `09.Search` precedent: its `16.Testing` container-fixture blocker was recorded with the exact directory listing that proved it, re-verified on every subsequent session, and annotated — not silently rewritten — when it cleared.

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.AI.Abstractions` | Design | `○` | Not designed. Bare placeholder `.csproj` on disk (`TargetFramework`/`ImplicitUsings`/`Nullable` only — zero references, zero `.cs` files), already registered in `Platform.SharedKernel.slnx` under `/10.Intelligence/`. Intended surface per the root brain: `ISemanticKernel` / `IEmbeddingGenerator` abstractions and the vector-retrieval contract. Target: zero `PackageReference`, `ProjectReference` to `SharedKernel.Primitives` (and `SharedKernel.Contracts` only if genuinely needed) — the `Microsoft.Extensions.AI.Abstractions` adopt-vs-redeclare decision is the one thing that could change that, and it is an explicit `SK.10.Design` call |
| `SharedKernel.AI.VectorDb` | Design | `○` | Not designed. Bare placeholder `.csproj` on disk, registered in `.slnx`. **Package identity is itself an open Design decision** — the root brain's Abstractions table lists this single package as the sole implementor, but its own naming convention requires an `.Abstractions` + `.{Provider}` split once a capability has more than one provider, and this domain is briefed for both Qdrant and Milvus. See `10.Intelligence/CLAUDE.md` → Packages for the three candidate shapes. Any change to the package set requires a root-brain edit via `/sync-brain` |
| `SharedKernel.AI.VectorDb.Tests` | Design | `○` | Bare placeholder `.csproj` on disk, nested correctly inside `SharedKernel.AI.VectorDb/`, registered in `.slnx` |
| `SharedKernel.AI.Tests` | Design | `○` | Bare placeholder `.csproj` on disk, registered in `.slnx` — **non-conventional**: it sits at the domain root rather than nested inside the project it tests, which the root brain's Test Project Rules forbid. Design must either re-home it as `SharedKernel.AI.Abstractions/SharedKernel.AI.Abstractions.Tests/` or justify the exception explicitly |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.10.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference — `Result`, `Result<T>`, `Error`, `ErrorType` | **Available — verified against source.** `Error` is a `sealed record (string Code, string Message, ErrorType Type)` exposing exactly six factories (`Unexpected`, `Validation`, `NotFound`, `Conflict`, `Unauthorized`, `BusinessRule`) plus the `None` sentinel. **There is no `Error.Failure` factory** — every "the operation failed" case routes through `Error.Unexpected` |
| `SK.10.Scaffold` | `01.Core` | `SharedKernel.Configuration` ProjectReference — `AddValidatedOptions` (provider packages only; **never** Abstractions) | **Available — verified against source.** Exactly one overload: `AddValidatedOptions<TOptions>(this IServiceCollection, IConfigurationSection)`, wiring `AddOptions` → `Bind` → `ValidateDataAnnotations` → `ValidateOnStart`. It takes an `IConfigurationSection` (not an `IConfiguration`), no configuring lambda, no custom `IValidateOptions<T>` — every provider DI extension must be shaped around that single signature |
| `SK.10.Scaffold` | `04.Contracts` | `SharedKernel.Contracts` ProjectReference — only if a paged/enveloped result bridge is genuinely required | **Available, but not yet justified.** `09.Search` took this reference solely for a guarded `ToPagedList()` bridge. Do not take it speculatively — the layering rules permit it, the zero-dependency `.Abstractions` bar argues against it |
| `SK.10.Core` | `01.Core` | `LoggingEventIdRanges.Intelligence` (10000–10999) `EventId` range registry for `[LoggerMessage]` logging | **Available — verified against source (2026-07-21).** `public const int Intelligence = 10000;` at `01.Core/SharedKernel.Primitives/Logging/LoggingEventIdRanges.cs`, alongside `PackageSubBlockWidth = 100`. Sub-block allocation is this domain's own responsibility, 100-wide in package declaration order; Abstractions `10000–10099` is expected to stay permanently unused (no logging in an abstraction package), matching `09.Search`'s 9000–9099 |
| `SK.10.Core` | `01.Core` | `IClock` — any timestamp on a receipt/result; never `DateTimeOffset.UtcNow` (SK0001) | Available. Note it is **not** self-registered by any `AddSharedKernelXxx()` extension anywhere in the platform — registration is uniformly the consuming host's responsibility |
| `SK.10.Tests` | `16.Testing` | Container fixture(s) for the chosen vector database(s) | **NOT ON DISK — verified 2026-07-21.** `16.Testing/SharedKernel.Testing/Containers/` holds exactly six fixtures: `PostgreSqlContainerFixture`, `RedisContainerFixture`, `RabbitMqContainerFixture`, `MinioContainerFixture`, `MeilisearchContainerFixture`, `ElasticsearchContainerFixture`. None for Qdrant, Milvus, or any vector database. Design must record this as a real downstream `16.Testing` obligation; the existence of `Testcontainers.Qdrant` / `Testcontainers.Milvus` on nuget.org is **unverified** and must be checked rather than assumed (`09.Search` discovered `Testcontainers.Meilisearch` does not exist at all). Never hand-roll a competing container setup inside a `.Tests` project |
| `SK.10.Tests` | `16.Testing` | In-memory doubles for this domain's abstractions | **NOT ON DISK — verified 2026-07-21.** `16.Testing/SharedKernel.Testing/` holds `Application/`, `Caching/`, `Clocks/`, `Communication/`, `Containers/`, `Contracts/`, `Domain/`, `Fakers/`, `Logging/`, `Messaging/`, `Persistence/`, `Search/`, `Security/`, `ServiceDefaults/`, `Storage/` — there is no AI/intelligence folder. A downstream `16.Testing` obligation, mirroring the shipped `Search/` and `Storage/` double sets |
| `SK.10.Tests` | `16.Testing` | Standard test package set plus a `SharedKernel.Testing` ProjectReference for every `.Tests` project | **Available.** Confirmed repo-wide set: `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0, `NSubstitute` 5.3.0. `SharedKernel.Testing` deliberately carries none of these — each `.Tests` project adds them directly. Its in-memory `ILogger`/`ILoggerFactory` double (`Logging/`) is the mandated way to assert on `EventId`/level/structured properties |

> **Downstream notes (not inbound blockers for this domain):** once the contract is locked, three other domains inherit work this domain must **record but never perform** — `16.Testing` (container fixtures + in-memory doubles), `13.ServiceDefaults` (a vector-store readiness health-check adapter over this domain's probe primitive, plus `HealthCheckNames`/`HealthCheckTags` constants — verified 2026-07-21 that neither holds an intelligence/vector entry today, and a string-name-only telemetry extension taking no `ProjectReference` to `10.Intelligence`), and `00.Governance` (topology rules, a `SharedKernelLayeringRules` method asserting `10.Intelligence` references only `01.Core`/`04.Contracts`, and any new `SK00xx` analyzer — `SK0023` is the highest implemented on disk, with `09.Search`'s `SK0024`/`SK0025` planned but not yet implemented, so verify the real highest allocated ID before claiming one). Track each in its own domain's state-map.

---

## Phase: Design <!-- phase-key: SK.10.Design -->

> Lock every interface shape, model record, error factory, options contract, and DI extension signature in `10.Intelligence/CLAUDE.md` before any implementation begins — the `08.Storage` and `09.Search` precedent, where the full contract was ratified ahead of Scaffold and never renegotiated. The governing rule for every task in this phase: **`SharedKernel.AI.Abstractions` contains no type that a candidate provider cannot implement completely and correctly.**

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |

_No tasks defined yet. Run `/arch` to produce a work order, then `/dispatch-phase` to route it to `intelligence-arch-planner`, which populates this table._

---

## Phase: Scaffold <!-- phase-key: SK.10.Scaffold -->

> Project wiring only — `.csproj` references and package pins, folder structure, solution registration, namespace-only stubs. No logic.

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |

_No tasks defined yet._

---

## Phase: Core <!-- phase-key: SK.10.Core -->

> Full implementation of every interface, adapter, filter translator, model record, error factory, options type, and DI registration locked at Design.

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |

_No tasks defined yet._

---

## Phase: Tests <!-- phase-key: SK.10.Tests -->

> Unit coverage, fail-loud rejection tests with no-I/O proof, the shared cross-provider conformance suite, and real-container provider scenarios. Never a mocked client for behavioural coverage; never a live paid model endpoint in the default suite.

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |

_No tasks defined yet._

---

## Phase: Docs <!-- phase-key: SK.10.Docs -->

> XML doc comments on every public API, `GenerateDocumentationFile` + `TreatWarningsAsErrors` + the full NuGet metadata block on every production `.csproj`, and a `README.md` per package. Include `<PackageReadmeFile>README.md</PackageReadmeFile>` **and** `<None Include="README.md" Pack="true" PackagePath="\" />` in the **same** edit here, not at Published — omitting the pair emits `NU5039` at pack time even when the README exists on disk, which is exactly what `08.Storage` missed.

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |

_No tasks defined yet._

---

## Phase: Published <!-- phase-key: SK.10.Published -->

> NuGet packaging, pack, and consumer verification through a real `IHost.StartAsync()` — never just `BuildServiceProvider()`. Mirrors the `08.Storage`/`09.Search`/`13.ServiceDefaults`/`14.Presentation`/`15.Integration`/`04.Contracts` `consumer-verify` console-harness precedent.

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |

_No tasks defined yet._

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | ⚑ Blocked | State |
| --- | --- | :---: | :---: | :---: | :---: | :---: |
| `SK.10.Design` | Design | 0 | 0 | 0 | 0 | `○` |
| `SK.10.Scaffold` | Scaffold | 0 | 0 | 0 | 0 | `○` |
| `SK.10.Core` | Core | 0 | 0 | 0 | 0 | `○` |
| `SK.10.Tests` | Tests | 0 | 0 | 0 | 0 | `○` |
| `SK.10.Docs` | Docs | 0 | 0 | 0 | 0 | `○` |
| `SK.10.Published` | Published | 0 | 0 | 0 | 0 | `○` |

**0 tasks defined. The `10.Intelligence` domain has not been designed yet** — four bare placeholder `.csproj` files exist on disk and are registered in `Platform.SharedKernel.slnx`, with zero `.cs` content in any of them. No work order has been raised for this domain and no phase has been dispatched.

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-07-21] Sub state-map initialized as a **structure-only template** — phase key registry (`SK.10.Design` through `SK.10.Published`), Package Board reflecting verified on-disk reality (four bare placeholder `.csproj` files, all registered in `Platform.SharedKernel.slnx`, zero `.cs` content; the `.VectorDb` single-package-vs-provider-split question and the non-conventional domain-root `SharedKernel.AI.Tests` project both recorded as open Design decisions), Cross-Domain Dependencies with on-disk-verified statuses (`LoggingEventIdRanges.Intelligence` = 10000 confirmed present in `01.Core`; `16.Testing` confirmed to ship **no** vector-database container fixture and **no** AI/intelligence in-memory-double folder), and all six phase sections present with their phase-key anchors and empty task tables. **No phase tasks authored** — per explicit instruction, this is the template only; `intelligence-arch-planner` owns populating it (root, user request)
