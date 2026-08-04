# 16.Testing — Shared Test Infrastructure Brain

## What This Domain Is

The shared test-infrastructure layer. Every `.Tests` project in this repository — and every downstream microservice's test suite — references `SharedKernel.Testing` for fakes, in-memory test doubles, and container fixtures instead of hand-rolling them per project. This domain is exempt from the normal layering direction: it may reference **any** numbered layer, because it is test-only and is never shipped inside a production artifact.

Philosophy: **Deterministic, dependency-light, conformance-first.** A fake's job is to satisfy the exact interface contract of the thing it replaces — nothing more. No fake here may introduce flakiness (real clocks, real sleeps, unseeded randomness) or silently diverge from the production implementation's documented behavior.

> **Why this package exists:** without it, every `.Tests` project across `02`–`14` independently reinvents `FakeCacheService`-shaped classes, container bootstrapping, and auth stand-ins — with subtle behavioral drift between copies. `16.Testing` is the single source of truth for "what does a fake `ICacheService` look like," so a behavioral fix only has to happen once.

---

## Packages

| Package | Role | References |
|---------|------|------------|
| `SharedKernel.Testing` | Fakes, in-memory test doubles, Testcontainers fixtures, and Bogus faker conventions consumed by every `.Tests` project | Any layer's `.Abstractions` package (and, where a planning pass has justified it, a non-`.Abstractions` package — e.g. `SharedKernel.Persistence.EfCore`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Application.Behaviors` — on demand, as each capability area is added). Implemented: `SharedKernel.Caching.Abstractions`, `SharedKernel.Domain`, `SharedKernel.Primitives`, `SharedKernel.Contracts`, `SharedKernel.Security.Abstractions`, `SharedKernel.Messaging.Abstractions`, `SharedKernel.Messaging.MassTransit` (test-only), `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.EfCore`, `SharedKernel.Communication.Internal`, `SharedKernel.Application.Behaviors` (WO-040 — the first `16.Testing` reference to `05.Application`; local-seam fakes + a promoted MediatR pipeline test harness, both in the `Application/` folder). Also references `Microsoft.Extensions.Logging.Abstractions` directly (P-258/WO-041, `Logging/`) — a NuGet `PackageReference` to a cross-cutting BCL logging contract, not a `SharedKernel.*` `ProjectReference`; the first capability folder in this package anchored to a foundational BCL package rather than a numbered domain's own abstraction. **Design-ahead-of-Core, blocker now CLEARED (P-268/P-269/WO-043):** `SharedKernel.Storage.Abstractions` (08.Storage) — a `ProjectReference` is added in Scaffold (S-23). As of the original WO-043 design pass, `08.Storage.Abstractions.csproj` was a genuinely empty placeholder (zero `.cs` files), a harder blocker than this package's usual "design documents target shape ahead of upstream code" convention (e.g. P-226/WO-036's `ActivityRecorder`, which needed only a literal `ActivitySource` name string, never an actual type reference) — `Storage/InMemoryFileStorage`/`InMemoryBlobUriGenerator` could not compile as `: IFileStorage`/`: IBlobUriGenerator` until `08.Storage` shipped real code. **Re-verified directly on disk during this Design-phase pass (2026-07-17): the blocker has cleared.** `08.Storage`'s `SK.08.Core` phase is now `●` 30/30 — `IFileStorage` (nine members), `IBlobUriGenerator` (two members), all seven `Models/` records, and the nine-factory-method `StorageErrors` class are fully implemented and compile clean, with zero drift from the target shape documented below. Core-phase tasks C-61–C-63 are now `●` Complete (`Storage/InMemoryFileStorage.cs`, `Storage/InMemoryBlobUriGenerator.cs`, `Storage/StorageServiceCollectionExtensions.cs`) — their Tests/Docs follow-ons T-47/DO-18 remain a future session's work. Also references `AWSSDK.S3` directly (P-268/WO-043, `Containers/`) — a NuGet `PackageReference` used exclusively by `MinioContainerFixture`'s bucket-bootstrap step (mirrors the existing `TestHarnessFactory`-carries-`MassTransit`-in-`Messaging/` precedent: one file in the folder carries a heavier third-party reference, the rest of the folder stays isolated from it). **Design-ahead-of-Core, blocker now CLEARED (P-276/WO-044):** `SharedKernel.Search.Abstractions` (09.Search) — a `ProjectReference` is planned in Scaffold (S-28). At the original WO-044 dispatch pass (2026-07-19), `09.Search/SharedKernel.Search.Abstractions.csproj` was a genuinely empty placeholder (zero `.cs` files, verified directly on disk via `09.Search`'s own `state-map.md` Package Board, not merely undispatched) — the same hard-blocker shape `08.Storage/SharedKernel.Storage.Abstractions` was in when P-269/WO-043 was originally designed. **Re-verified directly on disk during this Design-phase confirmation pass (2026-07-19, same calendar day): the blocker has cleared.** `09.Search`'s `SK.09.Core` phase — and its two provider packages' — are now `●`: `ISearchIndex<TDocument>` (12 members), `ISearchIndexProvisioner` (5 members), `ISearchProviderDescriptor` (4 members + `Validate`), and every `Models`/`Errors`/`Constants` type the target shape below depends on are fully implemented and compile clean, with zero drift from the target shape documented below. `09.Search`'s own `state-map.md` Blocked section confirms the dependency direction is now the OPPOSITE of what this note originally recorded — `09.Search`'s own `SK.09.Tests` T-13–T-17/T-21–T-26 are `⚑` Blocked waiting on THIS package's `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` (P-275). `Search/InMemorySearchIndex`/`InMemorySearchIndexProvisioner`/`InMemorySearchProviderDescriptor` can now compile as `: ISearchIndex<TDocument>`/`: ISearchIndexProvisioner`/`: ISearchProviderDescriptor` — Core-phase implementation (C-66–C-69, plus T-50/T-51/DO-21/DO-22) is corrected from `⚑` Blocked back to `○` Pending in `state-map.md`; actually writing that code remains a future Core-phase implementer session, not performed in this Design-confirmation pass. Also references `Testcontainers.Elasticsearch` and the base `Testcontainers` package directly (P-275/WO-044, `Containers/`) — used exclusively by `ElasticsearchContainerFixture` and `MeilisearchContainerFixture` respectively, the fifth and sixth `Containers/` fixtures; carried NO blocker at any point, since both fixtures expose only flat scalar connection properties with zero `SharedKernel.Search.*` reference. Also references `Testcontainers.Qdrant`/`Testcontainers.Milvus` directly (P-283/WO-045, `Containers/`) — both CONFIRMED to exist on nuget.org (2026-07-21), latest stable `4.13.0`, matching this package's EXISTING `Testcontainers.*` floor exactly (no version-bump ceremony required, unlike the Elasticsearch addition); used exclusively by `QdrantContainerFixture`/`MilvusContainerFixture` (the seventh and eighth `Containers/` fixtures), which carry NO blocker for the same flat-scalar-connection-properties reason as their six siblings. **Design-ahead-of-Core, HARD-BLOCKED (P-284/WO-045) — the THIRD occurrence of this pattern (after P-269/WO-043 and P-276/WO-044):** `SharedKernel.AI.Abstractions` (10.Intelligence) — a `ProjectReference` was added in Scaffold (S-32, 2026-07-21). Re-verified directly on disk immediately before adding: `SharedKernel.AI.Abstractions.csproj` is still a genuinely empty placeholder, zero `.cs` files — even though `10.Intelligence`'s own Design phase (P-279) is fully RATIFIED and its `CLAUDE.md` documents a complete, zero-drift-verifiable 995-line interface contract, prose ratification is not compiled code. The reference builds cleanly (`dotnet build` 0 errors) but resolves to zero usable types. `Intelligence/InMemoryEmbeddingGenerator`/`InMemoryVectorCollection<TRecord>`/`InMemoryVectorCollectionProvisioner`/`InMemoryVectorProviderDescriptor`/`InMemorySemanticKernel`/`InMemoryCompletionProviderDescriptor` cannot compile as `: IEmbeddingGenerator`/`: IVectorCollection<TRecord>`/etc. until `10.Intelligence`'s own `SK.10.Core` (C-01) ships real code — Core/Tests/Docs tasks (C-73–C-79, T-54, DO-25) remain `⚑` Blocked in `state-map.md`; Design (D-128–D-138) and Scaffold (S-29–S-33) are both `●` complete. **Design-ahead-of-Core, blocker now CLEARED (P-288/WO-046 — the FOURTH such occurrence, and the fourth to resolve):** `SharedKernel.Workflows.Temporal` (17.Workflows) — a `ProjectReference` was added in Scaffold (S-34, 2026-07-23). As of the original design pass, `SharedKernel.Workflows.Temporal.csproj` was a genuinely empty placeholder (bare `TargetFramework`/`ImplicitUsings`/`Nullable`, zero references, zero `.cs` content beyond generated `obj/` files) even though `17.Workflows`'s own Design phase (P-287) was already fully ratified with a zero-drift-sourceable 519-line `CLAUDE.md`. **Re-verified directly on disk 2026-07-23: the blocker has cleared.** `17.Workflows/SharedKernel.Workflows.Temporal/` now ships 43 real `.cs` files — `IWorkflowDispatcher`, `IWorkflowHandle`/`IWorkflowHandle<TResult>`, `IWorkflowIdFactory`, `TenantScope`, `WorkflowStartOptions`, `WorkflowExecutionDescription`, `WorkflowErrors`, `WorkflowWellKnown`, `WorkflowBase` all compiled and re-read directly from source. Core-phase tasks C-80–C-84 are now `●` Complete (`Workflows/InMemoryWorkflowExecution.cs`, `InMemoryWorkflowDispatcher.cs`, `InMemoryWorkflowHandle.cs`, `InMemoryWorkflowHandle{TResult}.cs`, `WorkflowServiceCollectionExtensions.cs`) — their Tests follow-on (T-55) remains a future session's work; DO-26 is `●` (verified complete in the same pass). Unlike the three prior occurrences, this one carries no paired `Containers/` fixture — `17.Workflows`'s own brain states no Testcontainers dependency exists for that domain (`WorkflowEnvironment` replaces it), so the entire `Workflows/` folder cleared as a single unit rather than splitting into an unblocked-fixture/blocked-fake pair. **Design-ahead-of-Core, blocker now CLEARED (P-300/WO-049) — the FIFTH such occurrence, and the FIRST that started as only PARTIAL rather than whole-folder:** `SharedKernel.Cryptography` (01.Core) — a `ProjectReference` was added in Scaffold (S-37). Unlike every prior occurrence, `SharedKernel.Cryptography` was ALREADY fully `Published` (WO-034) at the original Design pass, with all seven baseline interfaces (`IOneWayHasher`, `ISymmetricEncryptionService`, `IEncryptionKeyProvider`, `IAsymmetricSignatureService`, `IAsymmetricKeyProvider`, `IHmacSigner`, `ISecureRandomGenerator`) shipped as real, zero-drift-verified `.cs` files — so `Cryptography/FakeOneWayHasher`/`FakeSecureRandomGenerator`/`FakeEncryptionKeyProvider`/`FakeAsymmetricKeyProvider`/`FakeSymmetricEncryptionService`/`FakeAsymmetricSignatureService`/`FakeHmacSigner` were fully unblocked from the start. Only `IContentHasher` (P-296) was unavailable at Design time, blocking `Cryptography/FakeContentHasher` and the composite `FakeCryptographyServiceCollectionExtensions.AddFakeCryptography()`. Also references `SharedKernel.FeatureManagement` (01.Core) — a `ProjectReference` was added in Scaffold (S-38); at Design time the existing boolean `IFeatureManager.IsEnabledAsync`/`IsEnabledAsync<TContext>` members were shipped, but `GetVariantAsync`/`GetVariantAsync<TContext>` (P-298) did not yet exist on the live `Abstractions/IFeatureManager.cs`, blocking the entire `FeatureManagement/` folder (`FakeFeatureManager`, `FakeFeatureManagementServiceCollectionExtensions.AddFakeFeatureManagement()`) since a fake must implement the FULL interface to compile. **Re-verified directly on disk during the Scaffold-phase implementation pass (2026-07-28, while confirming the S-37/S-38 `ProjectReference` claims): both blockers have cleared.** `01.Core`'s own `SK.01.P296` closed 2026-07-27 (`Hashing/IContentHasher.cs` now real and compiled) and `SK.01.P298` closed 2026-07-28 (`GetVariantAsync`/`GetVariantAsync<TContext>` now declared on the live interface) — both re-verified with zero drift from the target shape documented below. `Cryptography/`'s `FakeContentHasher`/`AddFakeCryptography()` (C-92/C-93) and the entire `FeatureManagement/` folder (C-94/C-95) were corrected from `⚑` Blocked back to `○` Pending in `state-map.md` during the Scaffold-phase pass, then **implemented in full during the following Core-phase pass (2026-07-28, C-85–C-95, 95/95)** — all 8 `Cryptography/` fakes plus `AddFakeCryptography()` and the `FeatureManagement/` fake plus `AddFakeFeatureManagement()` are now real, shipped code; proving/documenting both folders in `SharedKernel.Testing.SelfTests` remains a future Tests/Docs-phase session's work. |
| `SharedKernel.Testing.SelfTests` | Self-contained unit tests for standalone testing-infrastructure helpers that have no owning consuming-domain interface to anchor against (fluent builders, assertion-helper classes, faker-seeding conventions, recorder/double self-checks) — see Test Rules below for the decision rule | `SharedKernel.Testing` + the Standard Test Package Set (xUnit runner, FluentAssertions, NSubstitute) as direct package references |

There is no `.Abstractions`/`.{Provider}` split for `SharedKernel.Testing` itself — it is the "provider" of test doubles, and nothing downstream re-implements it. `SharedKernel.Testing.SelfTests` is a narrow, documented exception added in WO-029 — not a general-purpose `.Tests` project for the whole domain (see Test Rules below).

Targets `net10.0`. `SharedKernel.Testing` itself has **no nested `.Tests` project** — see Test Rules below for why; `SharedKernel.Testing.SelfTests` exists alongside it for a deliberately narrow purpose.

### Publishing / Consumption Model

Both packages in this domain are **never packed or published as a `.nupkg`**. `SharedKernel.Testing.csproj` and `SharedKernel.Testing.SelfTests.csproj` both carry `<IsPackable>false</IsPackable>` explicitly. Consumption is **`ProjectReference`-only**, within this mono-repo: every `.Tests` project across `02`–`14` adds a direct `ProjectReference` to `16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj` (per root `CLAUDE.md` Test Project Rules), not a NuGet package reference. This is a deliberate decision, not an oversight — packing remains a future work order if/when a downstream consumer outside this repo needs `SharedKernel.Testing` as an installable package. `SharedKernel.Testing.SelfTests` carries the same non-shipping status for the same reason — it is a self-test project only and has no reason to ever be packed.

---

## Technology Stack

| Concern | Technology |
|---------|------------|
| Fakes / in-memory test doubles | Pure C# 13 — implements the target interface directly; zero mocking-framework dependency (no NSubstitute/Moq inside this package) |
| Container orchestration | `Testcontainers` (base, pinned `4.13.0` — a DIRECT `PackageReference`, not merely transitive, since `MeilisearchContainerFixture` uses the generic builder API directly) + `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Testcontainers.RabbitMq`, `Testcontainers.Minio`, `Testcontainers.Elasticsearch` (all pinned `4.13.0` as of S-25–S-27/P-275/WO-044 — CONFIRMED on disk: bumped from `4.1.0` in the same Scaffold pass that added `.Elasticsearch`/base `Testcontainers`, an EXPLICIT version-alignment decision so every `Testcontainers.*` package this project references shares one version, never left to implicit NuGet resolution) — one NuGet package per engine, added only when that capability area's fixture is implemented. **There is no `Testcontainers.Meilisearch` package** (confirmed 404 on nuget.org) — `MeilisearchContainerFixture` is hand-rolled on the base `Testcontainers` package's generic `ContainerBuilder`/`IContainer` API instead. `Testcontainers.Elasticsearch`'s own default image (`elasticsearch:8.6.1`) is incompatible with `09.Search`'s pinned `Elastic.Clients.Elasticsearch` 9.4.2 client — `ElasticsearchContainerFixture` explicitly overrides it via the `ElasticsearchBuilder(string image)` constructor to `docker.elastic.co/elasticsearch/elasticsearch:9.4.2` — pinned at Core-phase implementation time to match the client version exactly. **CONSTRUCTOR RULE (confirmed at the S-27 4.1.0→4.13.0 bump):** every `Testcontainers.*` builder type's parameterless constructor (`RedisBuilder()`, `MinioBuilder()`, `RabbitMqBuilder()`, `PostgreSqlBuilder()`, and the base `ContainerBuilder()`/`ElasticsearchBuilder()`) is `[Obsolete]` as of `4.13.0` in favor of a `ctor(string image)` overload — confirmed directly against each package's own shipped XML doc comments, not assumed. Always construct `new XBuilder("repository:tag")` directly; never `new XBuilder().WithImage("repository:tag")` — the latter compiles but emits `CS0618` |
| Deterministic fake data | `Bogus` — seeding convention only; concrete `Faker<TEntity>` definitions for business aggregates stay in each consuming service's own test project |
| xUnit lifetime contract | `xunit.core` (the `Xunit.IAsyncLifetime` contract only) — added solely so container fixtures can implement `IAsyncLifetime` directly; no test runner, no `Xunit.Assert`, no `xunit.runner.visualstudio` |
| Thread-safe state | `System.Collections.Concurrent` (`ConcurrentDictionary`, `ConcurrentQueue`) — every stateful fake must tolerate parallel xUnit test collections |
| Structured log capture | `Microsoft.Extensions.Logging.Abstractions` (pinned `10.0.9`, matching this project's `Microsoft.Extensions.*` version convention) — `ILogger`/`ILoggerFactory`/`ILogger<T>` implemented directly; also reuses the package's own real `Microsoft.Extensions.Logging.Logger<>` open-generic adapter class for DI wiring, never a hand-rolled substitute |
| Object-storage bucket bootstrap | `AWSSDK.S3`, pinned `4.0.101.1` — CONFIRMED 2026-07-17 against the live `08.Storage/SharedKernel.Storage.S3/SharedKernel.Storage.S3.csproj` (same version `08.Storage` pins for both `.S3` and `.Obs`), superseding the prior "confirm at implementation time" placeholder — used exclusively by `Containers/MinioContainerFixture` to create the default test bucket after container startup via a short-lived `AmazonS3Client`; scoped to that one file only, per the same "one file carries the heavier reference" pattern already established for `TestHarnessFactory`/`MassTransit`. `Storage/InMemoryFileStorage`/`InMemoryBlobUriGenerator` never reference `AWSSDK.S3` — they implement `SharedKernel.Storage.Abstractions`' interfaces directly in pure C#, zero third-party dependency, mirroring that package's own zero-third-party-NuGet rule |
| Vector-database containers | `Testcontainers.Qdrant`, `Testcontainers.Milvus` — CONFIRMED on nuget.org 2026-07-21: both exist, latest stable `4.13.0`, MIT-licensed, co-maintained by the `Testcontainers`/`HofmeisterAn` org, used by Microsoft Semantic Kernel/Aspire and the official Qdrant .NET SDK — matching this package's EXISTING `Testcontainers.*` floor exactly (established at S-25–S-27/WO-044), so unlike the `Testcontainers.Elasticsearch` addition, NO version-bump ceremony is required elsewhere in `Containers/`. Both are OFFICIAL dedicated Testcontainers modules — neither needs the generic `ContainerBuilder` hand-rolled fallback `MeilisearchContainerFixture` required. Confirmed via direct source read (`QdrantBuilder.cs`/`QdrantContainer.cs`, `MilvusBuilder.cs`/`MilvusContainer.cs`, 2026-07-21): `QdrantContainer` exposes `GetGrpcConnectionString()`/`GetHttpConnectionString()` (ports 6334/6333) with a built-in `/readyz` HTTP wait strategy; `MilvusContainer` exposes `GetEndpoint()` (gRPC port 19530) with a built-in `Wait.ForUnixContainer().UntilContainerIsHealthy()` docker-healthcheck wait strategy (curl `/healthz` on management port 9091) and runs Milvus in genuine single-container STANDALONE mode via its own `DEPLOY_MODE=STANDALONE`/`ETCD_USE_EMBED=true`/`COMMON_STORAGETYPE=local` defaults — embedded etcd, no external etcd/MinIO sidecar, satisfying P-283's minimal-standalone-deployment acceptance criterion with zero fixture-level orchestration. Both builders' parameterless constructors are `[Obsolete]` at `4.13.0` exactly like their four siblings — the CONSTRUCTOR RULE above applies identically; candidate image-tag pins (`qdrant/qdrant:v1.13.4`, `milvusdb/milvus:v2.3.10` — each module's own last-known-good default before its parameterless ctor was obsoleted) are to be RE-verified via `docker manifest inspect` at Core-phase implementation time, mirroring the `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` precedent |

`SharedKernel.Testing` deliberately does **not** reference `FluentAssertions`, `NSubstitute`, or any xUnit runner package. Those belong to the **Standard Test Package Set** added directly by each `.Tests` project (see root `CLAUDE.md` Test Project Rules and each domain's own Test Rules section for the pinned versions). Mixing assertion/mocking libraries into a shared production-shaped dependency would force every consumer onto this package's framework choices.

---

## Folder / Namespace Map

```text
SharedKernel.Testing/
  Clocks/          — SharedKernel.Testing.Clocks         — IClock fake (01.Core) — FakeClock
  Caching/         — SharedKernel.Testing.Caching         — ICacheService / IDistributedLockService / ITenantCacheKeyProvider / ICacheInvalidationBus fakes (02.Caching), plus IRedisChannelService / IRedisHashService / ITypedHashStore<T> / ICacheWarmupStrategy fakes (02.Caching, P-306/WO-050)
  Domain/          — SharedKernel.Testing.Domain          — assertion helpers over 03.Domain primitives — DomainEventAssertions, BusinessRuleAssertions, SpecificationAssert, DomainVersionAssertions, SpecificationTestBuilder<T>, FakeDomainNotFoundException
  Contracts/       — SharedKernel.Testing.Contracts       — DTO test helpers (04.Contracts) — PagedListBuilder<T>, EnvelopeAssertions, IntegrationEventFaker<TEvent>, EventEnvelopeBuilder<TEvent>, PagedListAssertions
  Security/        — SharedKernel.Testing.Security        — IUserContext / ITenantProvider fakes (12.Security)
  Messaging/       — SharedKernel.Testing.Messaging       — IMessageBus / IEventPublisher in-memory doubles (07.Messaging), TestHarnessFactory
  Persistence/     — SharedKernel.Testing.Persistence     — IDbConnectionFactory / IRepository<TAggregate,TId> / IReadRepository<TAggregate,TId> / IUnitOfWork / ITransactionalUnitOfWork fakes + EF Core test helpers (06.Persistence) — FakeDbConnectionFactory, FakeRepository<TAggregate,TId>, FakeUnitOfWork, FakePersistenceTransaction (all P-335/WO-053, implemented C-102–C-105/SK.16.Core), TestSharedKernelDbContext, AggregateRootFaker, EfContextExtensions, ProjectionSpecificationBuilder, BulkAggregateFaker, WithDeletedSpecification, PersistenceTestHelpers
  Containers/      — SharedKernel.Testing.Containers      — Testcontainers IAsyncLifetime fixtures (PostgreSQL / Redis / RabbitMQ / MinIO / Meilisearch / Elasticsearch / Qdrant / Milvus)
  Communication/   — SharedKernel.Testing.Communication   — cross-cutting Communication test doubles (11.Communication) — MockServiceEndpointResolver, FakeHttpContextAccessor, HttpClientHandlerTestFactory, FakeHttpMessageHandler, ambient Activity helper, gRPC ServerCallContext stub, GraphQL test-executor factory
  ServiceDefaults/ — SharedKernel.Testing.ServiceDefaults  — tenant resolution and health check test doubles (13.ServiceDefaults) — StaticTenantProvider, FakeTenantResolutionStrategy, HealthCheckAssertionExtensions
  Fakers/          — SharedKernel.Testing.Fakers          — Bogus deterministic-seeding convention + abstract Faker<T> bases — FakerSeeding, EntityFaker<TEntity,TId>, SingleValueObjectFaker<TValueObject,TValue>
  Application/     — SharedKernel.Testing.Application     — local-seam test doubles + MediatR pipeline test harness (05.Application.Behaviors) — FakeUnitOfWork, FakeAuthorizationContext, FakeIdempotencyKeyStore, FakeIdempotencyResponseStore, ApplicationPipelineTestHarness
  Logging/         — SharedKernel.Testing.Logging          — structured log capture double (Microsoft.Extensions.Logging.Abstractions, cross-cutting — not owned by any single numbered domain) — LogRecord, InMemoryLogger, InMemoryLogger<TCategoryName>, InMemoryLoggerFactory, LoggerAssertions
  Storage/         — SharedKernel.Testing.Storage          — IFileStorage / IBlobUriGenerator in-memory doubles (08.Storage) — InMemoryFileStorage, InMemoryBlobUriGenerator (implemented P-269/WO-043)
  Search/          — SharedKernel.Testing.Search            — ISearchIndex<TDocument> / ISearchIndexProvisioner / ISearchProviderDescriptor in-memory doubles (09.Search) — InMemorySearchIndex<TDocument>, InMemorySearchIndexProvisioner, InMemorySearchProviderDescriptor (implemented P-276/WO-044, Core phase C-64–C-69, 2026-07-20)
  Intelligence/    — SharedKernel.Testing.Intelligence      — IEmbeddingGenerator / IVectorCollection<TRecord> / IVectorCollectionProvisioner / IVectorProviderDescriptor / ISemanticKernel / ICompletionProviderDescriptor in-memory doubles (10.Intelligence) — InMemoryEmbeddingGenerator, InMemoryVectorCollection<TRecord>, InMemoryVectorCollectionProvisioner, InMemoryVectorProviderDescriptor, InMemorySemanticKernel, InMemoryCompletionProviderDescriptor (implemented P-284/WO-045, Core phase C-73–C-79, proven Tests phase T-54, 2026-07-22)
  Workflows/       — SharedKernel.Testing.Workflows          — IWorkflowDispatcher / IWorkflowHandle / IWorkflowHandle<TResult> in-memory doubles (17.Workflows) — InMemoryWorkflowDispatcher, InMemoryWorkflowHandle, InMemoryWorkflowHandle<TResult> (implemented P-288/WO-046, Core phase C-80–C-84, 2026-07-23; no paired Containers/ fixture, since 17.Workflows needs none)
  Cryptography/    — SharedKernel.Testing.Cryptography      — IOneWayHasher / ISymmetricEncryptionService / IEncryptionKeyProvider / IAsymmetricSignatureService / IAsymmetricKeyProvider / IHmacSigner / ISecureRandomGenerator / IContentHasher fakes (01.Core/SharedKernel.Cryptography) — FakeOneWayHasher, FakeSecureRandomGenerator, FakeEncryptionKeyProvider, FakeAsymmetricKeyProvider, FakeSymmetricEncryptionService, FakeAsymmetricSignatureService, FakeHmacSigner, FakeContentHasher (P-300/WO-049 — implemented 2026-07-28, all 8 fakes + AddFakeCryptography() shipped; SK.16.Core complete, proven/documented in a future SK.16.Tests/SK.16.Docs session)
  FeatureManagement/ — SharedKernel.Testing.FeatureManagement — IFeatureManager fake, incl. variant support (01.Core/SharedKernel.FeatureManagement) — FakeFeatureManager (P-300/WO-049 — implemented 2026-07-28, FakeFeatureManager + AddFakeFeatureManagement() shipped; SK.16.Core complete, proven/documented in a future SK.16.Tests/SK.16.Docs session)
```

Each capability folder maps 1:1 to the numbered domain whose abstraction it fakes. A new capability folder is added only when a concrete consumer needs it — this map is aspirational scaffolding, not a commitment to build every row immediately (see per-type `STATUS` markers below). `Domain/` and `Fakers/` are deliberately split: `Fakers/` holds abstract `Bogus.Faker<T>` base classes (construction-time concerns); `Domain/` holds assertion/verification helpers (post-condition concerns) — both fake sibling-isolation from each other since neither references the other's types. `Application/` (added WO-040) fakes `05.Application.Behaviors`' own LOCAL seam interfaces (`IUnitOfWork`, `IAuthorizationContext`, `IIdempotencyKeyStore`/`IIdempotencyResponseStore`), never the real cross-domain interfaces those seams bridge to in production (`06.Persistence`, `12.Security`, `07.Messaging` are never referenced by anything in this folder) — see its Interface Contracts block below for the full rationale. `Logging/` (added P-258/WO-041) is the first folder anchored to a cross-cutting BCL contract (`Microsoft.Extensions.Logging.Abstractions`) rather than a numbered domain's own `.Abstractions` package — `[LoggerMessage]`-based structured logging (root `CLAUDE.md`'s WO-041 "Logging Conventions" section) is consumed by every domain and owned by none of them, so there is no single owning domain to model the folder after; it still obeys the sibling-isolation rule (never references `Caching/`, `Messaging/`, `Application/`, or any other capability folder). `Storage/` (added P-269/WO-043) is the newest folder — the first one mapped to `08.Storage` — and references `SharedKernel.Storage.Abstractions` only, never `SharedKernel.Storage.S3`/`.Obs` (the concrete provider packages) nor any sibling capability folder in this package (in particular, never `Containers/MinioContainerFixture` — the in-memory fake and the real-provider-integration fixture are deliberately independent test paths, mirroring the existing split between e.g. `Caching/FakeCacheService` and `Containers/RedisContainerFixture`). `Containers/` itself gains a fourth fixture, `MinioContainerFixture` (P-268/WO-043), which — like its three siblings — exposes only flat scalar connection properties and takes **no** dependency on `SharedKernel.Storage.Abstractions`; its bucket-bootstrap step is the one place in this entire package that references `AWSSDK.S3` (a third-party NuGet, not a `SharedKernel.*` type), scoped to that single file. `Containers/` gains a fifth and sixth fixture, `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` (P-275/WO-044) — same "flat scalar connection properties only, zero `SharedKernel.Search.*` dependency" shape as `MinioContainerFixture`, so this pair carries no build-time dependency on `09.Search`'s own code landing. `Search/` (P-276/WO-044) is the newest folder — the second one built against a package that was a genuinely empty placeholder on disk at design time (the first was `Storage/` at P-269/WO-043) — and references `SharedKernel.Search.Abstractions` only, never `SharedKernel.Search.Meilisearch`/`.ElasticSearch` (the concrete provider packages) nor any sibling capability folder in this package (in particular, never `Containers/MeilisearchContainerFixture`/`.ElasticsearchContainerFixture` — the in-memory fakes and the real-provider-integration fixtures are deliberately independent test paths, mirroring the existing `Storage/`-vs-`Containers/MinioContainerFixture` split exactly). Unlike every other folder in this package, the three `Search/` fakes are additionally independent OF EACH OTHER — see the SCOPE LOCK note in the `Search/` Interface Contracts block below for why. `Containers/` gains a seventh and eighth fixture, `QdrantContainerFixture`/`MilvusContainerFixture` (P-283/WO-045) — same "flat scalar connection properties only, zero `SharedKernel.AI.*` dependency" shape as their six siblings, so this pair carries no build-time dependency on `10.Intelligence`'s own code landing, and — unlike `MeilisearchContainerFixture` — both are built on OFFICIAL dedicated Testcontainers modules with no hand-rolled generic-builder fallback needed. `Intelligence/` (P-284/WO-045) is the newest folder — the third one built against a package that was a genuinely empty placeholder on disk at design time (after `Storage/` at P-269/WO-043 and `Search/` at P-276/WO-044) — and references `SharedKernel.AI.Abstractions` only, never `SharedKernel.AI.Qdrant`/`.Milvus`/`.SemanticKernel` (the concrete provider packages) nor any sibling capability folder in this package (in particular, never `Containers/QdrantContainerFixture`/`.MilvusContainerFixture` — the in-memory fakes and the real-provider-integration fixtures are deliberately independent test paths, mirroring the `Storage/`-vs-`Containers/MinioContainerFixture` / `Search/`-vs-`Containers/MeilisearchContainerFixture` split exactly). Like `Search/`, ALL SIX `Intelligence/` fakes are additionally independent of each other — see the SCOPE LOCK note in the `Intelligence/` Interface Contracts block below, the pattern's SECOND application, confirming it generalizes beyond `Search/`'s original three-type case. `Workflows/` (P-288/WO-046) is the newest folder — the fourth one built against a package that was a genuinely empty placeholder on disk at design time (after `Storage/` at P-269/WO-043, `Search/` at P-276/WO-044, and `Intelligence/` at P-284/WO-045) — and references `SharedKernel.Workflows.Temporal` only (the domain's single package; there is no `.Abstractions` split in `17.Workflows` to narrow against). Unlike its three predecessors, `Workflows/` gains **no paired `Containers/` fixture** — `17.Workflows`'s own brain states its testing story is `WorkflowEnvironment`/`WorkflowReplayer` (Temporal's own in-box time-skipping test server), not Testcontainers, so there is no sibling fixture to keep isolated from it. `InMemoryWorkflowDispatcher` and `InMemoryWorkflowHandle`/`InMemoryWorkflowHandle<TResult>` are additionally independent of `Containers/` and every other sibling folder — see the `Workflows/` Interface Contracts block below. `Cryptography/` and `FeatureManagement/` (both P-300/WO-049) are the newest folders — the SECOND and THIRD ever anchored to a `01.Core` package (`Clocks/`'s `IClock`/`SharedKernel.Primitives` was the first) and the FIFTH/SIXTH occasion this package's Design phase has run ahead of an upstream `01.Core` addition still shipping — but the FIRST time that "ahead of schedule" was only PARTIAL rather than whole-folder: `SharedKernel.Cryptography` is already fully `Published` (WO-034), so 7 of `Cryptography/`'s 8 fakes needed no upstream code to land at all; at the original Design pass, only `FakeContentHasher` (needed `IContentHasher`, P-296) and the composite `FakeCryptographyServiceCollectionExtensions.AddFakeCryptography()` (transitively) were blocked, while the entire `FeatureManagement/` folder (`FakeFeatureManager`, needed `IFeatureManager.GetVariantAsync`/`GetVariantAsync<TContext>`, P-298) was blocked as a whole, mirroring the earlier whole-folder pattern exactly since a missing interface member prevents even a partial implementation. **Both blockers cleared 2026-07-28** (re-verified during the Scaffold-phase S-37/S-38 pass) — `01.Core`'s `SK.01.P296`/`SK.01.P298` both shipped. **All 11 fakes/DI extensions across both folders were implemented in the same-day `SK.16.Core` pass (C-85–C-95, 2026-07-28)** — proving/documenting them in `SharedKernel.Testing.SelfTests` remains a future `SK.16.Tests`/`SK.16.Docs` session's work. `Cryptography/` references `SharedKernel.Cryptography` only, never `SharedKernel.FeatureManagement` or any sibling capability folder in this package; `FeatureManagement/` references `SharedKernel.FeatureManagement` only, never `SharedKernel.Cryptography` or any sibling folder — neither folder references `Clocks/`, `Security/`, or any other existing folder despite superficial thematic overlap (e.g., neither a fake token generator nor a fake feature flag needs a fake clock). Both folders' fakes are proven exclusively in `SharedKernel.Testing.SelfTests`, never a `01.Core`-owned `.Tests` project — see Test Rules below for why (the same rationale `FakeClock` already established). `Caching/` (P-306/WO-050 — implemented 2026-07-29) gains four more fakes for `02.Caching`'s last four un-faked abstractions — `IRedisChannelService`, `IRedisHashService`, `ITypedHashStore<T>`, `ICacheWarmupStrategy` — the first extension to an already-`**implemented**`-labeled folder in this domain's history rather than a brand-new folder; `SharedKernel.Caching.Abstractions` is already referenced, so this phase carried zero cross-domain blocker of any kind, hard or soft. `Caching/` now ships nine fakes total, proven/documented in a future `SK.16.Tests`/`SK.16.Docs` session. `Persistence/` (P-335/WO-053, Core-phase implemented 2026-08-04) is the SECOND folder in this domain's history to gain net-new fakes for an already-`**implemented**`-labeled folder (after `Caching/`'s own P-306/WO-050 extension) rather than stand up a new one — three brand-new types (`FakeRepository<TAggregate,TId>`, `FakeUnitOfWork`, `FakePersistenceTransaction`) plus the long-pending `FakeDbConnectionFactory` (documented `[STATUS: Planned]` since P-182/WO-029, 2026-06-22, finally built at C-102) join the existing EF Core test-helper set. Carried zero cross-domain blocker of any kind — the FIRST of this domain's six "design-ahead-of-schedule"-shaped phases to find none at all, since `06.Persistence` is long-`●`-Published, unlike the five prior occurrences at P-269/P-276/P-284/P-288/P-300. `FakeRepository<TAggregate,TId>` deliberately implements BOTH `IRepository<TAggregate,TId>` and `IReadRepository<TAggregate,TId>` in ONE type — a divergence from production's `EfRepository`/`EfReadRepository` two-class split (which exists to support WO-053/P-338's new read-replica routing, meaningless for an in-memory collection) — guaranteeing trivial read-after-write consistency. `06.Persistence`'s WO-053/P-337 `IRestorableRepository<TAggregate,TId>` (a sibling phase dispatched in this SAME work order) is explicitly scope-locked out of `FakeRepository<TAggregate,TId>` — confirmed absent from disk at the original Design pass, but re-verified during Core-phase implementation (2026-08-04) to have SHIPPED in the interim; deliberately still NOT implemented against, per the phase's own explicit instruction, and flagged as a genuine future follow-up rather than silently absorbed. `Persistence/` continues to reference `SharedKernel.Persistence.Abstractions` only, never `.EfCore`/`.PostgreSQL`/`.Dapper` (the concrete provider packages, already excluded from every fake in this folder), nor any sibling capability folder in this package. **Proven Tests-phase implemented (T-66–T-71, 2026-08-04)** — all four types are now covered by 50 tests in `SharedKernel.Testing.SelfTests/Persistence/`; documenting them (DO-33–DO-35) remains the one still-open `SK.16.Docs` task.

---

## Interface Contracts

### `Clocks/` — `IClock` fake (01.Core)

```text
FakeClock  (sealed class, implements IClock)
    .UtcNow                                                    → DateTimeOffset  (settable; thread-safe via private Lock)
    .Today                                                     → DateOnly        (derived from UtcNow)
    constructor(DateTimeOffset? initial = null)
        Defaults to a fixed, non-real instant (2024-01-01T00:00:00Z) when initial is omitted —
        never DateTimeOffset.UtcNow.
    .Advance(TimeSpan delta)                                   → void  (UtcNow += delta)
    .Set(DateTimeOffset value)                                 → void
    .SetUtcNow(DateTimeOffset value)                           → void  (alias for Set)
    NOTE: Used wherever production code takes an IClock dependency (AggregateRoot<TId>, audit
          interceptors, DomainEventVersionHelper-adjacent timestamping, etc.). Implemented in
          Clocks/FakeClock.cs; also registered as the IClock singleton by AddFakeDomainServices().
```

### `Caching/` — capability fakes (02.Caching) — **implemented**

```text
FakeCacheService  (sealed class, implements ICacheService)
    .Count                                                     → int
    .GetAsync<T>(string key, CancellationToken ct)             → ValueTask<T?>
    .SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct) → ValueTask
    .GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
                       CachePolicy policy, CancellationToken ct) → ValueTask<T>
    .RemoveAsync(string key, CancellationToken ct)             → ValueTask
    .RemoveByTagAsync(string tag, CancellationToken ct)        → ValueTask
    .GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct) → ValueTask<IReadOnlyDictionary<string, T?>>
    .SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct) → ValueTask
    .Clear()                                                   → void
    NOTE: Thread-safe via ConcurrentDictionary<string, object?> + per-key tag tracking. TTL and
          sliding-window timing from CachePolicy are intentionally NOT enforced — this fake verifies
          behavioral correctness (what was cached, under what key/tag), not expiry timing. Every
          requested key in GetManyAsync is present in the result dictionary; missing keys map to default.

FakeDistributedLockService  (sealed class, implements IDistributedLockService)
    .SimulateFailure                                           → bool  (settable; forces null on both acquire paths)
    .AcquireAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry, CancellationToken ct) → Task<IAsyncDisposable?>
    .AcquireRenewableAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry, CancellationToken ct) → ValueTask<IRenewableLock?>
    NOTE: Locks are always granted immediately — no contention simulation. SimulateFailure is the
          only failure-injection seam.

FakeRenewableLock  (sealed class, implements IRenewableLock)
    .RenewalCount                                              → int  (increments on every successful RenewAsync)
    .SimulateRenewalFailure                                    → bool  (settable; forces RenewAsync → false + IsAcquired → false)
    .IsAcquired                                                → bool  (false after DisposeAsync or a failed renewal)
    .RenewAsync(CancellationToken ct)                          → ValueTask<bool>
    .DisposeAsync()                                            → ValueTask
    NOTE: Returned exclusively by FakeDistributedLockService.AcquireRenewableAsync.

FakeTenantCacheKeyProvider  (sealed class, implements ITenantCacheKeyProvider)
    constructor()                                              — serviceName defaults to "test-svc"
    constructor(string serviceName)
    .BuildTenantKey(string tenantId, string entity, string id, params string[] extraSegments) → string
    .BuildKey(string entity, string id, params string[] extraSegments)                          → string
    .BuildKey(string entity, string id, int version, params string[] extraSegments)             → string
    NOTE: Key format mirrors the production TenantCacheKeyProvider exactly (see 02.Caching's own
          contract). Zero dependency on 12.Security or IHttpContextAccessor — safe in pure unit tests.

FakeCacheInvalidationBus  (sealed class, implements ICacheInvalidationBus)
    .PublishedInvalidations                                    → IReadOnlyList<CacheInvalidationMessage>
    .PublishKeyInvalidationAsync(string[] keys, CancellationToken ct)               → ValueTask
    .PublishTagInvalidationAsync(string[] tags, CancellationToken ct)               → ValueTask
    .PublishBroadcastInvalidationAsync(CancellationToken ct)                        → ValueTask
    .PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct) → ValueTask
    .OnInvalidation(Func<CacheInvalidationMessage, ValueTask> handler)              → void
        Registers a handler invoked synchronously on every publish call (any of the four overloads)
        so a test can chain a FakeCacheService.RemoveAsync as the downstream effect without async
        plumbing of its own.
    .Reset()                                                   → void  (clears recorded messages AND registered handlers)
    NOTE: Pure in-memory — zero dependency on IRedisChannelService or any Redis package, unlike the
          production RedisCacheInvalidationBus. References SharedKernel.Caching.Abstractions only.

FakeRedisChannelService  (sealed class, implements IRedisChannelService)
    .ConnectionHealth                                          → ConnectionHealthState  (settable; default Connected)
    .SimulateFailure                                           → bool  (settable, default false; when true, PublishAsync/
                                                                   SubscribeAsync/UnsubscribeAsync all throw
                                                                   InvalidOperationException instead of performing the
                                                                   operation)
    .PublishedMessages                                         → IReadOnlyList<(string Channel, string Message)>  (every
                                                                   successful PublishAsync call, append-only, thread-safe)
    .SubscribedChannels                                        → IReadOnlyList<string>  (every channel with at least one
                                                                   active — not yet unsubscribed — handler)
    .PublishAsync(string channel, string message, CancellationToken ct)                     → ValueTask
        Records (channel, message) into PublishedMessages, then synchronously invokes every currently-
        subscribed handler for that channel, in subscription order — genuine in-process pub/sub fan-out,
        not merely a recorded call, so a single-process test can prove a publish/subscribe round trip
        with no real Redis. A handler exception is caught and swallowed, never rethrown to the publisher
        — mirrors RedisChannelService's own documented "handler exceptions must never propagate to the
        Redis subscriber thread" rule (02.Caching's RedisChannelService rules).
    .SubscribeAsync(string channel, Func<string, ValueTask> handler, CancellationToken ct)   → ValueTask
        Registers handler for channel. A channel may accept more than one handler; PublishAsync invokes
        all of them.
    .UnsubscribeAsync(string channel, CancellationToken ct)                                  → ValueTask
        Removes every handler registered for channel. Unsubscribing a channel with no active subscription
        is a silent no-op (idempotent, mirrors this package's established idempotent-unsubscribe/-delete
        convention).
    .IsSubscribed(string channel)                                                            → bool
    .Reset()                                                                                  → void  (clears
                                                                   PublishedMessages and every subscription)
    NOTE: Backing store is a thread-safe channel→handler-list map (ConcurrentDictionary<string,
          ConcurrentBag<Func<string,ValueTask>>>, replaced wholesale on Unsubscribe rather than mutated
          in place, to avoid a torn-bag read during a concurrent publish). Deliberately does NOT
          replicate RedisChannelService's own reconnect/replay machinery (subscribing to
          IConnectionMultiplexer.ConnectionRestored/ConnectionFailed and resubscribing every channel) —
          ConnectionHealth is a plain settable property a test mutates directly to simulate what a
          health-check consumer would read, never driven by a real connection event. The first Caching/
          fake to simulate genuine cross-call interaction (publish → subscriber fan-out) rather than
          purely passive call recording — see the new Implementation Rules bullet below.

FakeRedisHashService  (sealed class, implements IRedisHashService)
    .SimulateFailure                                           → bool  (settable, default false; when true, every member
                                                                   throws InvalidOperationException instead of performing
                                                                   the operation)
    .GetFieldAsync<T>(string key, string field, JsonTypeInfo<T> typeInfo, CancellationToken ct)          → ValueTask<T?>
    .SetFieldAsync<T>(string key, string field, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct) → ValueTask
    .GetAllFieldsAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken ct)                    → ValueTask<IReadOnlyDictionary<string, T>>
    .DeleteFieldAsync(string key, string field, CancellationToken ct)                                    → ValueTask
    .IncrementFieldAsync(string key, string field, long delta, CancellationToken ct)                     → ValueTask<long>
    .Seed<T>(string key, string field, T value)                                                          → void  (test-setup
                                                                   helper — pre-populates a field without going through
                                                                   SetFieldAsync, mirrors Storage/InMemoryFileStorage.Seed)
    .Reset()                                                                                              → void
    NOTE: Backing store is a ConcurrentDictionary<(string Key, string Field), object?> keyed by (key,
          field), storing the boxed value directly. typeInfo is accepted for signature parity only and
          is NEVER invoked — this is an in-memory fake with no wire format to cross, unlike the real
          RedisHashService, which uses JsonTypeInfo<T> for genuine STJ (de)serialization against Redis'
          wire bytes. GetFieldAsync<T> on a missing (key, field) pair returns default(T); GetAllFieldsAsync<T>
          returns every field currently stored under key whose boxed value is assignable to T (an empty
          dictionary for a missing/empty key — never throws). DeleteFieldAsync on a missing (key, field)
          pair is a silent no-op (idempotent). IncrementFieldAsync treats a missing field as 0 before
          adding delta (matches Redis' own HINCRBY semantics), stores the result as a boxed long, and
          returns it; incrementing a field that currently holds a non-long value throws
          InvalidCastException — a fake-only guard surfacing a caller bug (using IncrementFieldAsync
          against a field populated via SetFieldAsync<T> for T != long), standing in for the WRONGTYPE
          error the real Redis command would raise in the equivalent case.

FakeTypedHashStore<T>  (sealed class, implements ITypedHashStore<T>)
    .SimulateFailure                                           → bool  (settable, default false; same shape as
                                                                   FakeRedisHashService.SimulateFailure)
    .GetFieldAsync(string key, string field, CancellationToken ct)                          → ValueTask<T?>
    .SetFieldAsync(string key, string field, T value, CancellationToken ct)                 → ValueTask
    .GetAllFieldsAsync(string key, CancellationToken ct)                                    → ValueTask<IReadOnlyDictionary<string, T>>
    .DeleteFieldAsync(string key, string field, CancellationToken ct)                       → ValueTask
    .IncrementFieldAsync(string key, string field, long delta, CancellationToken ct)        → ValueTask<long>
    .Seed(string key, string field, T value)                                                → void
    .Reset()                                                                                 → void
    NOTE: An INDEPENDENT fake with its own ConcurrentDictionary<(string Key, string Field), object?>
          backing store — deliberately NOT a thin wrapper composing FakeRedisHashService internally,
          mirroring this folder's established convention that its fakes stay independent of each other
          (FakeCacheService/FakeDistributedLockService/FakeTenantCacheKeyProvider/FakeCacheInvalidationBus
          share no state today either). Same GetField/missing-default, GetAllFields/type-filter,
          idempotent-Delete, and Increment/InvalidCastException-on-type-mismatch semantics as
          FakeRedisHashService above, minus the JsonTypeInfo<T> parameter — ITypedHashStore<T>'s whole
          purpose is to be the AOT-safe, no-per-call-JsonTypeInfo<T> wrapper (02.Caching's own
          ITypedHashStore rules), so the fake carries that simplification through faithfully. Register
          one FakeTypedHashStore<T> per DTO type, exactly like the real AddTypedHashStore<T>(JsonTypeInfo<T>)
          convention (02.Caching's own ITypedHashStore rules: "Register one ITypedHashStore<T> per DTO
          type... Multiple types may be registered independently").

FakeCacheWarmupStrategy  (sealed class, implements ICacheWarmupStrategy)
    constructor(string name, int order = 0, ConcurrentQueue<string>? executionLog = null)
    .Name                                                       → string  (fixed at construction)
    .Order                                                      → int  (fixed at construction)
    .CallCount                                                  → int  (Interlocked-incremented on every WarmupAsync call)
    .SimulateFailure                                            → bool  (settable, default false)
    .OnWarmup                                                   → Action<ICacheService>?  (settable; invoked synchronously,
                                                                   with the ICacheService instance WarmupAsync received,
                                                                   before the SimulateFailure check)
    .WarmupAsync(ICacheService cache, CancellationToken ct)     → ValueTask
        Increments CallCount; invokes OnWarmup(cache) if configured; appends Name to executionLog (if a
        non-null queue was supplied at construction) UNCONDITIONALLY — before evaluating SimulateFailure
        — then throws InvalidOperationException($"Simulated warmup failure for strategy '{Name}'.") when
        SimulateFailure is true.
    NOTE: A test proves CacheWarmupHostedService's documented "sorts/orders strategies, catches
          per-strategy exceptions, logs at Error, and continues" contract (02.Caching's own Cache warmup
          rules) by constructing several FakeCacheWarmupStrategy instances that all share the SAME
          ConcurrentQueue<string> passed to each constructor, mixing SimulateFailure = true/false across
          them, running them through the consumer's own ordering/dispatch loop, then asserting the
          queue's contents reflect every strategy having run — in Order — including the ones that threw
          (failure isolation), and CallCount == 1 on each even for the failing ones. The shared queue is
          an explicit, caller-supplied instance, never a static field — this package permits only two
          documented static-mutable-state exceptions (FakerSeeding.Apply, AmbientActivityTestHelper's
          ActivityListener), and this is not a third.

AddFakeCachingServices(this IServiceCollection)
    NOTE: DI convenience extension for Caching/ — registers six caching fakes as singletons in one
          call: FakeCacheService, FakeDistributedLockService, FakeTenantCacheKeyProvider,
          FakeCacheInvalidationBus (the original four), plus FakeRedisChannelService →
          IRedisChannelService and FakeRedisHashService → IRedisHashService (added P-306/WO-050).
          Supersedes the prior manual per-fake AddSingleton<T> pattern shown in DI Registration
          below (that pattern remains valid for callers who want only a subset).
          FakeTypedHashStore<T> and FakeCacheWarmupStrategy are NOT bundled into this call — see
          AddFakeTypedHashStore<T>() and AddFakeCacheWarmupStrategy() below.

AddFakeTypedHashStore<T>(this IServiceCollection)
    NOTE: Registers FakeTypedHashStore<T> as ITypedHashStore<T>, singleton — matches the real
          AddTypedHashStore<T>(JsonTypeInfo<T>)'s own per-DTO-type registration shape (minus the
          JsonTypeInfo<T> argument, which the fake never needs). Call once per T the test needs a typed
          hash store for; mirrors AddInMemorySearchIndex<TDocument>()'s per-type call convention.

AddFakeCacheWarmupStrategy(this IServiceCollection, string name, int order = 0,
                           ConcurrentQueue<string>? executionLog = null)                     → IServiceCollection
    NOTE: Registers a new FakeCacheWarmupStrategy(name, order, executionLog) as ICacheWarmupStrategy via
          plain AddSingleton — CORRECTED at Tests-phase implementation time (T-64/SK.16.Tests,
          2026-07-29): an earlier design/Core-phase draft specified TryAddEnumerable to "mirror"
          AddCacheWarmup<TStrategy>()'s own TryAddEnumerable multi-strategy registration shape
          (02.Caching's own DI startup guard rules); that mirroring does not actually hold. .NET's
          TryAddEnumerable infers a descriptor's "implementation type" from the factory delegate's own
          Func<IServiceProvider, TService> generic signature, not from what the factory constructs at
          runtime — a Func<IServiceProvider, ICacheWarmupStrategy> factory is therefore indistinguishable
          from the service type itself and made TryAddEnumerable throw ArgumentException on the very
          first registration (confirmed by a failing test, not by inspection). Switching to the
          two-type-parameter ServiceDescriptor.Singleton<TService, TImplementation> overload cleared the
          crash but exposed a second, deeper problem: TryAddEnumerable de-duplicates by
          (ServiceType, ImplementationType) only, and every call constructs the SAME concrete
          FakeCacheWarmupStrategy type regardless of name/order/executionLog — so a second/third
          differently-named call was silently DROPPED, never appearing in the resolved
          IEnumerable<ICacheWarmupStrategy>, defeating the documented "multiple independently-registered
          strategies" goal. The real AddCacheWarmup<TStrategy>() has no such problem because it is
          genuinely parameterized by a distinct generic TStrategy per call, which IS what
          TryAddEnumerable's per-implementation-type de-duplication is designed for. Plain AddSingleton
          (no de-duplication) is the correct, shipped implementation — call once per named strategy
          needed, passing the SAME executionLog queue instance across calls when the test wants to
          observe cross-strategy ordering.
```

### `Domain/` — domain primitive test helpers (03.Domain)

```text
FakeClock  (sealed class, implements IClock)
    .UtcNow                                                    → DateTimeOffset  (settable via SetUtcNow)
    .Today                                                     → DateOnly        (= DateOnly.FromDateTime(UtcNow.DateTime))
    constructor(DateTimeOffset? initial = null)
        Defaults to a fixed, non-real instant when initial is omitted — never DateTimeOffset.UtcNow.
    .SetUtcNow(DateTimeOffset value)                           → void
    .Advance(TimeSpan duration)                                → void  (UtcNow += duration)
    NOTE: Thread-safe via lock/Interlocked over the backing field. Lives in Clocks/ folder (namespace
          SharedKernel.Testing.Clocks), not Domain/ — see Folder/Namespace Map. Registered as IClock
          singleton by AddFakeDomainServices().

EntityFaker<TEntity, TId>  (abstract class, extends Bogus.Faker<TEntity>)
    where TEntity : Entity<TId>  where TId : notnull
    .WithClock(IClock clock)                                   → EntityFaker<TEntity, TId>  (fluent — wires a FakeClock for construction)
    NOTE: Abstract base only — not a complete auto-faker. Concrete fakers in consuming test projects
          declare their own RuleFor(...) definitions because domain invariants must be respected.
          Lives in Fakers/ folder (namespace SharedKernel.Testing.Fakers).

DomainEventAssertions  (static class — extension methods on IReadOnlyCollection<IDomainEvent>)
    .ContainsEventOfType<T>(this IReadOnlyCollection<IDomainEvent> events)           → T  (returns the matching event; throws InvalidOperationException if none found)
    .ContainsExactly<T>(this IReadOnlyCollection<IDomainEvent> events, int count)    → void  (throws if actual count != count)
    .HasNoEvents(this IReadOnlyCollection<IDomainEvent> events)                      → void  (throws if non-empty)
    .HasNoEventsOfType<T>(this IReadOnlyCollection<IDomainEvent> events)             → void  (throws if any present)
    .ContainsEventWithVersion<T>(this IReadOnlyCollection<IDomainEvent> events, int version) → T  [P-181] (asserts a T exists AND DomainEventVersionHelper.GetVersion(typeof(T)) == version)
    .HasRaisedExactlyNEvents(this IReadOnlyCollection<IDomainEvent> events, int n)   → void  [P-181] (any event type, not just T)
    NOTE: Framework-agnostic — throws InvalidOperationException with descriptive messages, zero
          dependency on xUnit/NUnit/FluentAssertions. Designed against AggregateRoot<TId>.DomainEvents'
          exact return type. Lives in Domain/ folder (namespace SharedKernel.Testing.Domain).

BusinessRuleAssertions  (static class — extension methods on IBusinessRule)
    .ShouldBeBroken(this IBusinessRule rule)                   → void  (throws InvalidOperationException with rule.Message if IsBroken() == false)
    .ShouldNotBeBroken(this IBusinessRule rule)                → void  (throws InvalidOperationException with rule.Message if IsBroken() == true)
    NOTE: Framework-agnostic, no test-framework dependency.

SpecificationAssert  (static class)
    .Satisfies<T>(ISpecification<T> spec, T entity)            → void  (throws if Criteria.Compile() applied to entity returns false; null Criteria = always satisfies)
    .DoesNotSatisfy<T>(ISpecification<T> spec, T entity)       → void  (throws if true)
    NOTE: Compiles ISpecification<T>.Criteria via .Compile() — reflection-based expression compilation,
          acceptable in test assemblies only, never production. Lets unit tests prove specification
          logic without a database. Standalone helper, no owning consuming-domain interface —
          proven in SharedKernel.Testing.SelfTests.

SingleValueObjectFaker<TValueObject, TValue>  (abstract class, extends Bogus.Faker<TValueObject>)
    where TValueObject : SingleValueObject<TValue>  where TValue : notnull
    .WithValue(TValue value)                                   → SingleValueObjectFaker<TValueObject, TValue>  (fluent)
    .WithRandomValue(Func<Bogus.Faker, TValue> generator)       → SingleValueObjectFaker<TValueObject, TValue>  (fluent)
    NOTE: Lives in Fakers/ folder.

DomainVersionAssertions  (static class)
    .ShouldHaveVersion<TEvent>(int expectedVersion)            → void  (asserts [DomainEventVersion(N)] present AND N == expectedVersion; throws plain exception, NOT FluentAssertions)
    .ShouldBeVersioned<TEvent>()                               → void  (asserts any [DomainEventVersion] attribute is present)
    NOTE: Plain exceptions with descriptive messages — SharedKernel.Testing itself never depends on
          FluentAssertions. Proven in SharedKernel.Testing.SelfTests (standalone assertion helper).

SpecificationTestBuilder<T>  (sealed class — fluent in-memory specification test helper)
    static .For(ISpecification<T> spec)                        → SpecificationTestBuilder<T>
    .Against(IEnumerable<T> entities)                          → SpecificationTestBuilder<T>  (fluent)
    .ExpectCount(int n)                                         → SpecificationTestBuilder<T>  (fluent)
    .ExpectMatch(Func<T, bool> predicate)                       → SpecificationTestBuilder<T>  (fluent)
    .Assert()                                                   → void  (evaluates spec.IsSatisfiedBy per entity; throws with descriptive failure including entity details)
    NOTE: Wraps Specification<T>.IsSatisfiedBy — in-domain/in-test use only per 03.Domain's own
          documented constraint. No owning consuming-domain interface (it is a builder, not an
          implementation) — proven in SharedKernel.Testing.SelfTests.

FakeDomainNotFoundException  (static factory class)
    static .For<TAggregate>(object id)                          → DomainNotFoundException
        Produces a valid DomainNotFoundException (03.Domain) for repository-fake not-found setups.
    NOTE: Factory, not a fake implementing an interface — proven in SharedKernel.Testing.SelfTests.

AddFakeDomainServices(this IServiceCollection)
    NOTE: Registers FakeClock as IClock singleton. Other domain test helpers above are static or
          plain Bogus-derived classes, not DI-registered — consistent with the Security/Persistence/
          Clocks convention of "DI registration only when swapping in for a production registration."
```

### `Contracts/` — DTO test helpers (04.Contracts)

```text
PagedListBuilder<T>  (sealed class — fluent test builder)
    .WithItems(IEnumerable<T> items)                            → PagedListBuilder<T>  (fluent; also sets default TotalCount = items.Count)
    .WithPage(int page)                                         → PagedListBuilder<T>  (fluent; default 1)
    .WithPageSize(int pageSize)                                 → PagedListBuilder<T>  (fluent; default 10)
    .WithTotalCount(int totalCount)                             → PagedListBuilder<T>  (fluent; overrides the WithItems-derived default)
    .Build()                                                    → PagedList<T>  (calls PagedList<T>.Create(...))
    static .Empty<T>()                                          → PagedList<T>  (zero items, TotalCount=0, Page=1, PageSize=10)
    NOTE: Eliminates repetitive PagedList<T>.Create(...) boilerplate in paged-query test setups.
          Standalone builder, no owning consuming-domain interface — proven in SelfTests.

EnvelopeAssertions  (static class — extension methods on Envelope / Envelope<T>)
    .ShouldBeSuccess(this Envelope envelope)                    → void  (throws with Error details if IsSuccess == false)
    .ShouldBeFailure(this Envelope envelope)                    → void  (throws if IsSuccess == true)
    .ShouldBeSuccess<T>(this Envelope<T> envelope)               → T  (returns Value for chaining; throws if failure)
    .ShouldBeFailure<T>(this Envelope<T> envelope, ErrorType? expectedType = null) → void  (throws if success; optionally asserts ErrorType)
    .ShouldHaveError<T>(this Envelope<T> envelope, string expectedCode)             → void  (throws if failure but code mismatches, or if success)
    NOTE: All throw InvalidOperationException, zero test-framework dependency. Standalone assertion
          helper, no owning consuming-domain interface — proven in SelfTests.
    NAMESPACE RENAME ADOPTED (P-330/WO-052, 2026-07-31): the underlying `Envelope`/`Envelope<T>` types
          were relocated by `04.Contracts` from namespace `SharedKernel.Contracts.Envelope` to
          `SharedKernel.Contracts.Envelopes` (folder `Envelope/` → `Envelopes/`, `04.Contracts` P-328,
          shipped in `SharedKernel.Contracts` v2.0.0) to eliminate a namespace-vs-type-name collision —
          the exact reason this file previously carried a `using EnvelopeNs = SharedKernel.Contracts.Envelope;`
          alias. `EnvelopeAssertions.cs`/`EnvelopeAssertionsTests.cs` now reference the type directly via
          a plain `using SharedKernel.Contracts.Envelopes;`, with zero `EnvelopeNs` alias remaining and
          every method signature/`<see cref>` doc reference unqualified `Envelope`/`Envelope<T>` — a
          purely mechanical `using`/type-reference update, no signature or behavior change.

IntegrationEventFaker<TEvent>  (abstract class, extends Bogus.Faker<TEvent>)
    where TEvent : IIntegrationEvent
    protected .RuleForEventId()                                 → void  (pre-wires EventId to f.Random.Guid())
    protected .RuleForOccurredOn()                               → void  (pre-wires OccurredOn to f.Date.RecentOffset())
    NOTE: Subclasses call these helpers in their constructor then add their own RuleFor declarations.
          Eliminates EventId/OccurredOn boilerplate on every integration event faker.

EventEnvelopeBuilder<TEvent>  (sealed class — fluent test builder)  where TEvent : IDomainEvent
    .WithPayload(TEvent @event)                                  → EventEnvelopeBuilder<TEvent>  (fluent)
    .WithSourceService(string name)                              → EventEnvelopeBuilder<TEvent>  (fluent; default "test-service")
    .WithCorrelationId(string id)                                → EventEnvelopeBuilder<TEvent>  (fluent; default Guid.NewGuid().ToString("N"))
    .WithCausationId(string id)                                  → EventEnvelopeBuilder<TEvent>  (fluent; default null)
    .Build()                                                     → EventEnvelope<TEvent>  (wraps EventEnvelope.Wrap<TEvent>(...))
    NOTE: Gives messaging/integration test setups a clean way to construct envelopes without knowing
          every metadata field.

PagedListAssertions  (static class — extension methods on PagedList<T>)
    .ShouldHaveTotalCount(this PagedList<T> list, int expected)  → void
    .ShouldHaveItems(this PagedList<T> list, params T[] expected) → void
    .ShouldBeEmpty(this PagedList<T> list)                       → void
    NOTE: Plain exception-throwing boolean checks — ZERO FluentAssertions reference, correcting an
          earlier superseded-phase draft that specified a FluentAssertions implementation. This
          package's standing hard rule (no assertion-library dependency of its own) takes precedence.
          Proven in SelfTests.

AddFakeContractsServices(this IServiceCollection)                                    [STATUS: Deferred — P-064/WO-012]
    NOTE: Deferred — none of the above Contracts/ helpers currently need DI registration (all are
          static or directly instantiable builders, consistent with the Security/Persistence/Clocks
          convention). Add only if a concrete DI-backed need surfaces in a future phase.
```

### `Security/` — auth mocks (12.Security)

```text
FakeUserContext  (sealed class, implements IUserContext)
    .UserId                                                    → Guid                      (settable; default: a fixed non-empty test Guid)
    .Email                                                     → string?                   (settable)
    .Username                                                  → string?                   (settable)
    .Roles                                                     → IReadOnlyCollection<string> (settable; default: empty)
    .Claims                                                    → IReadOnlyDictionary<string,string> (settable; default: empty)
    .IsAuthenticated                                            → bool                      (settable; default: true)
    .HasRole(string role)                                      → bool  (case-insensitive Roles.Contains)
    NOTE: Distinct from 12.Security's AnonymousUserContext, which is an immutable production
          fallback sentinel (always IsAuthenticated == false). FakeUserContext defaults to an
          authenticated user so most test setups need zero configuration; call the mutators to
          exercise unauthenticated or role-restricted paths explicitly.

FakeTenantProvider  (sealed class, implements ITenantProvider)
    .TenantId                                                  → Guid  (settable)
    constructor(Guid? tenantId = null)                         — defaults to a fixed non-empty test Guid, NOT Guid.Empty
    NOTE: Defaulting to a real tenant id (rather than Guid.Empty) means tenant-scoped code under
          test exercises the tenanted path by default. Set TenantId = Guid.Empty explicitly to
          test the no-tenant-resolved path.
```

### `Messaging/` — in-process bus/publisher doubles (07.Messaging)

```text
InMemoryMessageBus  (sealed class, implements IMessageBus)
    .PublishAsync<T>(T message, CancellationToken ct)                               → Task
    .PublishAsync<T>(T message, Action<PublishContext> configure, CancellationToken ct) → Task
    .SendAsync<T>(T command, CancellationToken ct)                                  → Task
    .RequestAsync<TRequest,TResponse>(TRequest request, CancellationToken ct)       → Task<TResponse>
        Configurable via SetResponseHandler<TRequest,TResponse>(Func<TRequest,TResponse>) — when no
        handler is registered for the requested type pair, throws InvalidOperationException with a
        descriptive message naming the missing TRequest/TResponse pair. CORRECTED (P-183/WO-029):
        supersedes an earlier draft of this contract that specified NotSupportedException; the
        carried-forward, dispatched P-183 spec is authoritative.
    .SetResponseHandler<TRequest,TResponse>(Func<TRequest,TResponse> handler)        → void
        Registers (or replaces) the handler RequestAsync<TRequest,TResponse> delegates to.
    .ExecuteRoutingSlipAsync(object routingSlip, CancellationToken ct)               → Task
        Throws NotSupportedException — true routing-slip orchestration requires a real broker
        round-trip; tests needing that fidelity use MassTransit's own TestHarness instead.
    .ShouldHavePublished<T>()                                  → T  (returns the recorded message; throws if none found)
    .ShouldHaveSent<T>()                                       → T  (returns the recorded message; throws if none found)
    .ShouldHavePublishedOnce<T>()                               → T  (throws if zero or more than one)
    .ShouldNotHavePublished<T>()                                → void  (throws if any recorded)
    NOTE: Records every PublishAsync/SendAsync call (message type + instance) into a thread-safe
          list even when no assertion is ever made. Assertion helpers are read-only queries over
          that list — they never mutate state, and return the matched message for further assertion
          chaining. Root CLAUDE.md WO-022 commitment; carried forward unchanged by P-183/WO-029
          (the canonical spec — supersedes the earlier, less complete P-011/P-124 drafts).

InMemoryEventPublisher  (sealed class, implements IEventPublisher)
    .PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)             → Task
    .PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct) → Task
    .Published                                                 → IReadOnlyList<object>
    .PublishedOf<TEvent>()                                     → IReadOnlyList<TEvent>
    NOTE: Does not wrap events in EventEnvelope<TEvent> — that's a MassTransitEventPublisher-specific
          transport concern (07.Messaging). This double records the raw TEvent instances only.
          Thread-safe under concurrent publish.

AddInMemoryMessageBus(this IServiceCollection)
AddInMemoryEventPublisher(this IServiceCollection)
    NOTE: Both register their respective double as a SINGLETON — a deliberate, documented deviation
          from IMessageBus/IEventPublisher's production "scoped" lifetime rule (07.Messaging). Tests
          need the same recorder instance to outlive the DI scope used by the system under test so
          assertions can run after the action completes.

TestHarnessFactory  (static class)
    .CreateAsync(string serviceName, Action<IBusRegistrationConfigurator>? configure = null) → Task<ITestHarness>
        CORRECTED at implementation time: exposed as CreateAsync (not the originally planned
        synchronous-looking Create) since starting a harness via ITestHarness.Start() is inherently
        asynchronous. Calls AddMassTransitTestHarness, applies SetKebabCaseEndpointNameFormatter and
        the configure callback, builds the provider, then starts and returns the harness.
        Configures a MassTransit.Testing.ITestHarness with platform defaults
        (KebabCaseEndpointNameFormatter, the given serviceName, pre-registered ConsumerBase<T>
        subclasses discovered via the configure callback).
    NOTE: May reference SharedKernel.Messaging.MassTransit as a test-only dependency — this is
          permitted because 16.Testing is never shipped inside a production artifact. InMemoryMessageBus
          and InMemoryEventPublisher themselves remain reference-isolated to
          SharedKernel.Messaging.Abstractions only; TestHarnessFactory is a separate file/type that
          carries the heavier MassTransit reference.
```

### `Persistence/` — connection factory fake and EF Core test helpers (06.Persistence)

```text
FakeDbConnectionFactory  (sealed class, implements IDbConnectionFactory)  — implemented C-102/SK.16.Core
    constructor(Func<IDbConnection> connectionFactory)
    .CreateConnectionAsync(CancellationToken ct)                → Task<IDbConnection>
        Returns connectionFactory() wrapped in Task.FromResult — no real ADO.NET connection opened.
    NOTE: Deliberately takes a caller-supplied Func<IDbConnection> rather than constructing its own
          substitute — this package must never take a hard dependency on a mocking framework
          (NSubstitute, Moq, etc.). The consuming .Tests project builds its own IDbConnection
          substitute (typically via NSubstitute, per the platform's Standard Test Package Set) and
          passes it in. Closes the gap noted in 13.ServiceDefaults's Test Rules ("16.Testing has no
          IDbConnectionFactory fake yet").

FakeRepository<TAggregate, TId>  (sealed class, implements IRepository<TAggregate,TId> AND IReadRepository<TAggregate,TId>)  — implemented C-103/SK.16.Core
    where TAggregate : IAggregateRoot<TId>  where TId : notnull
    constructor(Func<TAggregate, TId> idSelector, IEnumerable<TAggregate>? seed = null)
    .Items                                                       → IReadOnlyDictionary<TId, TAggregate>  (live snapshot)
    .SimulateFailure                                             → bool  (settable; gates the 6 write-side mutating members only)
    — IRepository<TAggregate,TId> members —
    .GetByIdAsync(TId id, ct)                                    → Task<TAggregate?>  (never soft-delete-filtered)
    .GetBySpecAsync(ISpecification<TAggregate> spec, ct)         → Task<TAggregate?>  (shared with IReadRepository — one impl)
    .AddAsync(TAggregate aggregate, ct)                          → Task  (throws InvalidOperationException on duplicate key)
    .UpdateAsync(TAggregate aggregate, ct)                       → Task  (throws KeyNotFoundException on missing key)
    .DeleteAsync(TAggregate aggregate, ct)                       → Task  (idempotent; ALWAYS a hard removal, even for ISoftDeletable)
    .ExistsAsync(TId id, ct)                                     → Task<bool>  (never soft-delete-filtered)
    .AddRangeAsync / .UpdateRangeAsync / .DeleteRangeAsync        → Task  (per-item semantics above, sequential, non-atomic)
    — IReadRepository<TAggregate,TId> members —
    .ListAsync / .CountAsync / .AnyAsync(spec, ct)                → the shared in-memory specification pipeline (see below)
    .GetByIdsAsync(IEnumerable<TId> ids, ct)                     → Task<IReadOnlyList<TAggregate>>  (missing id = no entry; order not guaranteed)
    .GetByIdsChunkedAsync(IEnumerable<TId> ids, int chunkSize, ct) → Task<IReadOnlyList<TAggregate>>  (identical results, chunked mechanically)
    .ListPagedAsync(spec, ct)                                    → Task<PagedList<TAggregate>>  (page/pageSize derived from spec.Skip/spec.Take)
    .ListProjectedAsync<TResult> / .GetBySpecProjectedAsync<TResult> / .ListPagedProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult>, ct)
        → pipeline through step 7, then Select(spec.Selector) as step 8
    .StreamAsync / .StreamProjectedAsync<TResult>(spec, ct)       → IAsyncEnumerable<T>  (genuine cancellable iterator; eagerly materialized
        first — no lazy DB source to stream from)
    .ListKeysetAsync<TKey>(KeysetSpecification<TAggregate,TKey>, ct)  where TKey : struct, IComparable<TKey>  → Task<KeysetPage<TAggregate,TKey>>
        Cursor/seek pagination — see the dedicated algorithm note below. CORRECTED at Core-phase
        implementation time (C-103): the live IReadRepository.ListKeysetAsync<TKey> constraint is
        `where TKey : struct, IComparable<TKey>` (matching KeysetSpecification<T,TKey>'s own shipped
        constraint, WO-051/C-39) — the earlier Design-phase draft's abbreviated `IComparable<TKey>`
        phrasing dropped the `struct` half; corrected here rather than silently coded around.
    .Seed(IEnumerable<TAggregate>) / .Reset()                    → void  (bypass SimulateFailure; Reset clears data only, never SimulateFailure)

    SHARED SPECIFICATION PIPELINE (in-memory mirror of 06.Persistence's canonical evaluator order):
        0. if !spec.IncludeDeleted: filter out items where (item is ISoftDeletable sd && sd.IsDeleted) — a
           runtime `is` check, never a generic constraint
        1. spec.Criteria?.Compile() via .Where(); null = all items
        2/2b/2c. Includes / StringIncludes / AsSplitQuery — N/A, deliberate in-memory no-ops (no ORM query
           plan or Cartesian-join concept)
        3/4. OrderBy/OrderByDescending then ThenBys, each spec.*.Compile()'d and applied via LINQ
        5. IsDistinct → .Distinct()
        6. AsNoTracking — N/A, no ChangeTracker concept
        7. Skip/Take — always last
        8. Select(spec.Selector) — projection overloads only, strictly after Skip/Take
        CountAsync/AnyAsync reuse this IDENTICAL pipeline (including Skip/Take if set) — no special-casing,
        mirroring ISpecificationEvaluator<T>.GetQuery's own method-agnostic real behavior.

    ListKeysetAsync<TKey> ALGORITHM: apply pipeline steps 0–1, then OrderBy/OrderByDescending/ThenBys
        (already correctly populated by KeysetSpecification<T,TKey>'s own base constructor — no special
        access to its protected keySelector/idSelector needed); skip Skip entirely; if spec.AfterKey is
        null, keep every sorted item (first page); otherwise SkipWhile past every item at-or-before the
        cursor (compiled sort-key selector + Comparer<object>.Default tiebreak on the id selector's result
        — assumes the id shape implements IComparable, true for Guid/int/long/string); Take(spec.Take!.Value
        + 1); if the result has an extra row, HasMore = true, trim to spec.Take.Value, derive
        NextAfterKey/NextAfterId from the trimmed list's last item; else HasMore = false, both null/default.

    NOTE: A SINGLE class implements BOTH IRepository<TAggregate,TId> and IReadRepository<TAggregate,TId> —
          a deliberate divergence from production's EfRepository/EfReadRepository two-class split (which
          exists to support WO-053/P-338's read-replica routing, meaningless for an in-memory collection).
          This guarantees trivial read-after-write consistency with no shared-store wiring needed.
          idSelector is mandatory because IAggregateRoot<TId> exposes no .Id member at all (IEntity<TId> is
          a zero-member marker) — production solves this via a compiled Expression.Property("Id") tree per
          closed generic type; this fake takes the simpler caller-supplied-delegate route instead.
          Writes are IMMEDIATE — there is no ChangeTracker-style staging. FakeUnitOfWork (below) is fully
          INDEPENDENT, with no constructor coupling to this type.
          IRestorableRepository<TAggregate,TId> (WO-053/P-337, a sibling 06.Persistence phase in the SAME
          work order) is explicitly NOT implemented — scope-locked out of P-335. CORRECTED at Core-phase
          implementation time (2026-08-04): re-verified on disk per the phase's own instruction and found
          the interface HAS shipped since the original Design-phase "confirmed absent" finding (D-175) —
          06.Persistence/SharedKernel.Persistence.Abstractions/Repositories/IRestorableRepository.cs now
          exists with its single RestoreAsync(TAggregate, ct) member. Per the phase's own explicit
          instruction, it was deliberately NOT implemented against even though it now compiles — flagged
          as a genuine future follow-up, not silently absorbed into this pass.

FakeUnitOfWork  (sealed class, implements ITransactionalUnitOfWork — transitively satisfies IUnitOfWork)  — implemented C-104/SK.16.Core
    NAMING COLLISION, DELIBERATE: a DIFFERENT type from the existing Application/FakeUnitOfWork
    (SharedKernel.Testing.Application namespace, implements 05.Application.Behaviors's unrelated
    single-member IUnitOfWork seam) — disambiguated by namespace only; both types' docs cross-reference
    each other explicitly, mirroring the same-named-real-interfaces precedent the root CLAUDE.md documents.
    .SaveChangesCallCount                                        → int
    .SaveChangesResult                                           → int  (settable, default 0)
    .SimulateFailure                                             → bool  (settable; forces SaveChangesAsync to throw)
    .SaveChangesAsync(ct)                                        → Task<int>  (pure counter — no interaction with any FakeRepository)
    .TransactionCount                                            → int
    .BeginTransactionAsync(ct)                                   → Task<IPersistenceTransaction>  (returns a new FakePersistenceTransaction)
    .ExecuteInTransactionAsync(Func<CancellationToken,Task>, ct) → Task  (invokes the delegate exactly once, no retry)
    .ExecuteInTransactionAsync<TResult>(Func<CancellationToken,Task<TResult>>, ct) → Task<TResult>  (same, returns the result)
    .Reset()                                                     → void  (clears counts, resets SaveChangesResult/SimulateFailure)
    NOTE: Deliberately never retries inside ExecuteInTransactionAsync — a documented divergence from the
          real contract's "the operation delegate may run MORE THAN ONCE" retrying-execution-strategy note.

FakePersistenceTransaction  (sealed class, implements IPersistenceTransaction, IAsyncDisposable)  — implemented C-105/SK.16.Core
    — returned exclusively by FakeUnitOfWork.BeginTransactionAsync
    .IsCommitted / .IsRolledBack / .IsDisposed                   → bool
    .CommitAsync(ct)                                             → Task  (InvalidOperationException if already committed/rolled back/disposed)
    .RollbackAsync(ct)                                           → Task  (same guard)
    .DisposeAsync()                                              → ValueTask  (idempotent)

TestSharedKernelDbContext  (abstract class, extends SharedKernelDbContext)
    — preconfigured with the SQLite in-memory provider
    — wires a no-op IUserContext (fixed "test-user") so AuditInterceptor resolves without a real HTTP context
    — wires a deterministic IClock (fixed snapshot, never real time) for stable interceptor timestamps
    — calls EnableSensitiveDataLogging() for readable test diagnostics
    .EnsureCreatedAsync()                                       → Task  (no migrations needed for SQLite tests)
    NOTE: Proven alongside 06.Persistence.EfCore's own test suite — extends a production base class
          and exercises real interceptor contracts.

AggregateRootFaker<TAggregate, TId>  (abstract class, extends Bogus.Faker<TAggregate>)
    where TAggregate : AggregateRoot<TId>  where TId : notnull
    — pre-configures CreatedBy / CreatedOn / IsDeleted = false to match EF interceptor expectations

TenantedAggregateFaker<TAggregate, TId>  (abstract class, extends AggregateRootFaker<TAggregate, TId>)
    — additionally populates a non-empty TenantId

EfContextExtensions  (static class)
    .DetachAll(this DbContext context)                          → void  (detaches all tracked entities for a fresh same-database reload within one test)
    .ReloadAsync<T>(this DbContext context, T entity)            → Task<T?>  (loads a fresh copy via a new scoped DbContext instance, asserts round-trip persistence)
    .RegisterOptions(DbContext context, DbContextOptions options) → void  [ADDED during Tests phase, T-21]
        Registers options keyed by context instance (ConditionalWeakTable<DbContext, DbContextOptions>)
        so ReloadAsync<T> can construct an equivalent fresh context. EF Core's internal per-context
        service provider does not register DbContextOptions/DbContextOptions<TContext> as a resolvable
        service for a context built standalone (outside AddDbContext) — ReloadAsync<T> throws
        InvalidOperationException if a context's options were never registered. TestSharedKernelDbContext
        calls this automatically from its own constructor; any other DbContext subclass passed to
        ReloadAsync<T> must call it manually first.

ProjectionSpecificationBuilder<TAggregate, TResult>  (sealed class — fluent builder)
    .WithCriteria(Expression<Func<TAggregate, bool>> criteria)   → ProjectionSpecificationBuilder<TAggregate, TResult>  (fluent)
    .WithSelector(Expression<Func<TAggregate, TResult>> selector) → ProjectionSpecificationBuilder<TAggregate, TResult>  (fluent)
    .Build()                                                     → IProjectionSpecification<TAggregate, TResult>
    NOTE: Produces IProjectionSpecification instances without a full concrete spec class per test.
          Standalone builder, no owning consuming-domain interface (it produces instances of an
          interface, it does not implement one itself) — proven in SelfTests.

BulkAggregateFaker<TAggregate, TId>  (sealed class wrapping an AggregateRootFaker<TAggregate, TId>)
    — generates a configurable-count List<TAggregate> via Bogus with all audit fields populated,
      for seeding bulk AddRangeAsync integration tests. Proven in SelfTests.

WithDeletedSpecification<TAggregate>  (sealed class, extends Specification<TAggregate>)
    static .Wrap(ISpecification<TAggregate> inner)               → ISpecification<TAggregate>
        Returns a copy of inner with IncludeDeleted = true; the original specification instance is
        left untouched. For soft-delete integration tests. Proven in SelfTests.

PersistenceTestHelpers  (static class)
    .AssertEntityTracked<T>(DbContext context, T entity)         → void  (throws if ChangeTracker reports EntityState.Detached)
    .AssertEntityNotTracked<T>(DbContext context, T entity)      → void  (throws if ChangeTracker reports any tracked state)
    NOTE: Fills the coverage gap around AsNoTracking behavioral verification. Proven in SelfTests.

SCOPE LOCK (P-182/WO-029, REAFFIRMED P-190/WO-030): No OutboxMessageFaker, OutboxAssertions, or
    PostgreSQL Testcontainer dependency is introduced by this set. P-190/WO-030 proposed lifting this
    lock on the premise that 06.Persistence had since shipped a real OutboxInterceptor/outbox contract
    — that premise is false and the proposal was rejected, not deferred. 06.Persistence's own CLAUDE.md
    states explicitly: "The outbox pattern is owned entirely by 07.Messaging via MassTransit's
    UseEntityFrameworkOutbox. No outbox types (OutboxMessage, IOutboxWriter, OutboxInterceptor) exist
    in this domain. Introducing any such type here is a hard violation." 07.Messaging's CLAUDE.md
    restates the same rule from the owning side. There is no SharedKernel-defined outbox message
    envelope type anywhere on the platform to mirror in a Faker<T> — MassTransit's EF Core outbox
    integration owns its own internal table schema, configured via 07.Messaging's OutboxOptions /
    .WithEntityFrameworkOutbox<TDbContext>(), never exposed as a SharedKernel contract. If outbox-
    pattern test tooling is wanted in the future, the correct target is 07.Messaging's MassTransit-
    owned outbox surface (proven via this package's own TestHarnessFactory/ITestHarness, already
    shipped in Messaging/) — never a 06.Persistence type, which cannot exist per that domain's own
    hard rule.
```

### `Containers/` — Testcontainers fixtures

```text
PostgreSqlContainerFixture  (sealed class, implements IAsyncLifetime)
    .ConnectionString                                          → string  (throws InvalidOperationException if read before InitializeAsync completes)
    .InitializeAsync()                                         → Task  (starts a pinned postgres image via Testcontainers.PostgreSql)
    .DisposeAsync()                                            → Task  (stops and removes the container)

RedisContainerFixture  (sealed class, implements IAsyncLifetime)
    .ConnectionString                                          → string
    .InitializeAsync() / .DisposeAsync()                       — same shape, wraps Testcontainers.Redis
    NOTE: Must be independently usable by each of the four split 02.Caching capability .Tests
          projects (FusionCache, Redis L2, DistributedLocking, HashStore, PubSub) without requiring
          all four capabilities wired up simultaneously (P-185/WO-029 alignment check).

RabbitMqContainerFixture  (sealed class, implements IAsyncLifetime)
    .ConnectionString                                          → string
    .InitializeAsync() / .DisposeAsync()                       — same shape, wraps Testcontainers.RabbitMq

    NOTE: Each fixture is consumed via xUnit's [CollectionDefinition] + ICollectionFixture<T> — one
          container instance shared across an entire test collection, never started per test method.
          Image tags are pinned (no ":latest") so CI runs are reproducible. These fixtures back the
          Testcontainers requirements already documented in 02.Caching (Redis), 06.Persistence
          (PostgreSQL/Dapper), and 07.Messaging (RabbitMQ) Test Rules sections. Centralizing here does
          not, in this pass, change those domains' existing inline Testcontainers setup — a future
          migration phase in each consuming domain adopts these fixtures.

MinioContainerFixture  (sealed class, implements IAsyncLifetime)
    .ServiceUrl                                                → string  (throws InvalidOperationException if read before InitializeAsync completes)
    .AccessKeyId                                               → string  (fixed test credential, sourced from the started
                                                                   Testcontainers.Minio MinioContainer's own root user — never
                                                                   a SharedKernel-invented value)
    .SecretAccessKey                                           → string  (same sourcing as AccessKeyId)
    .DefaultBucket                                             → string  (bootstrapped automatically during InitializeAsync —
                                                                   both S3.Tests and Obs.Tests receive an already-existing
                                                                   bucket with zero provider-specific setup of their own)
    .ForcePathStyle                                            → bool  (always true — MinIO requires path-style addressing;
                                                                   exposed as a property so no consumer has to hardcode this
                                                                   fact itself)
    .InitializeAsync()                                         → Task  (starts a pinned MinIO image via Testcontainers.Minio;
                                                                   once running, creates DefaultBucket via a short-lived
                                                                   Amazon.S3.AmazonS3Client pointed at ServiceUrl with
                                                                   ForcePathStyle=true)
    .DisposeAsync()                                            → Task  (stops and removes the container)
    NOTE: Property names are chosen to match SharedKernel.Storage.S3's S3StorageOptions 1:1
          (ServiceUrl/AccessKeyId/SecretAccessKey/ForcePathStyle/DefaultBucket — verified against the
          live 08.Storage/CLAUDE.md Interface Contracts) so SharedKernel.Storage.S3.Tests binds this
          fixture directly with zero renaming. SharedKernel.Storage.Obs.Tests binds the same
          ServiceUrl value into ObsStorageOptions's differently-named Endpoint property — a straight
          1:1 property assignment, never provider-specific branching logic, satisfying this
          fixture's "no provider-specific branching required by the consumer" design goal.
          MinioContainerFixture is the ONLY type in Containers/ permitted to carry an AWSSDK.S3
          reference (bucket bootstrap only) — PostgreSqlContainerFixture/RedisContainerFixture/
          RabbitMqContainerFixture remain isolated to their own single Testcontainers.* package,
          mirroring the existing "TestHarnessFactory is the only type in Messaging/ permitted to
          carry a SharedKernel.Messaging.MassTransit reference" rule. Deliberately takes NO
          ProjectReference to SharedKernel.Storage.Abstractions — it exposes flat scalar connection
          properties only, exactly like its three siblings expose a raw ConnectionString, so this
          fixture has zero build-time dependency on 08.Storage's own code landing (unlike
          Storage/InMemoryFileStorage below, which does).

MeilisearchContainerFixture  (sealed class, implements IAsyncLifetime)                [implemented P-275/WO-044, Core phase C-64, 2026-07-20]
    .Url                                                        → string  (throws InvalidOperationException if read before
                                                                   InitializeAsync completes; matches 09.Search's
                                                                   MeilisearchOptions.Url 1:1)
    .ApiKey                                                     → string  (the fixture's own fixed master key, a 44-char
                                                                   constant well above Meilisearch's 16-byte minimum
                                                                   key-length requirement; matches MeilisearchOptions.ApiKey
                                                                   1:1)
    .InitializeAsync()                                         → Task  (starts a pinned getmeili/meilisearch image via the
                                                                   generic Testcontainers.Builders.ContainerBuilder API,
                                                                   constructed directly as new ContainerBuilder(
                                                                   "getmeili/meilisearch:v1.20.0") — the tag chosen and
                                                                   pinned at Core-phase implementation time, confirmed to
                                                                   exist via `docker manifest inspect` against the real
                                                                   registry (no browsing access in the implementation
                                                                   environment) — NOT the parameterless ContainerBuilder() +
                                                                   .WithImage(...) pattern, which is [Obsolete]/CS0618 as of
                                                                   the 4.13.0 pin (see the Technology Stack CONSTRUCTOR RULE,
                                                                   confirmed at S-27). NO dedicated Testcontainers.Meilisearch
                                                                   NuGet module exists, confirmed 404 on nuget.org; port
                                                                   7700; env MEILI_MASTER_KEY=<ApiKey>,
                                                                   MEILI_NO_ANALYTICS=true; wait strategy is an
                                                                   unauthenticated GET /health returning 200 — the one route
                                                                   Meilisearch leaves unprotected by the master key
                                                                   regardless of key configuration, making it the correct
                                                                   universal readiness probe. Smoke-tested against real
                                                                   Docker: container starts, /health returns 200, an
                                                                   authenticated /indexes call succeeds.)
    .DisposeAsync()                                             → Task  (stops and removes the container)
    NOTE: The ONLY type in Containers/ hand-rolled on the generic builder API rather than a dedicated
          Testcontainers.{Engine} module — no such module exists for Meilisearch. Property names chosen
          to match MeilisearchOptions.Url/.ApiKey 1:1 so SharedKernel.Search.Meilisearch.Tests binds
          this fixture directly with zero renaming, mirroring MinioContainerFixture's established
          property-naming convention. Deliberately takes NO ProjectReference to
          SharedKernel.Search.Abstractions or .Meilisearch — flat scalar connection properties only, so
          this fixture has zero build-time dependency on 09.Search's own code landing (unlike
          Search/InMemorySearchIndex below, which does — see the Search/ section's BLOCKER-CLEARANCE
          VERIFICATION note; the blocker there cleared during the same-day Design-phase confirmation
          pass, 2026-07-19).

ElasticsearchContainerFixture  (sealed class, implements IAsyncLifetime)              [implemented P-275/WO-044, Core phase C-65, 2026-07-20]
    .Nodes                                                      → string[]  (single-element array containing the
                                                                   container's mapped HTTPS base URL; throws
                                                                   InvalidOperationException if read before InitializeAsync
                                                                   completes; matches ElasticSearchOptions.Nodes 1:1)
    .Username                                                   → string  (fixed "elastic" — matches
                                                                   ElasticSearchOptions.Username 1:1. CONCRETE MECHANISM
                                                                   (Core-phase implementation detail): sourced directly from
                                                                   the Testcontainers.Elasticsearch module's own public
                                                                   static constant ElasticsearchBuilder.DefaultUsername —
                                                                   the module exposes no WithUsername fluent method, so the
                                                                   module's own default is the only reachable value)
    .Password                                                   → string  (matches ElasticSearchOptions.Password 1:1.
                                                                   CONCRETE MECHANISM: sourced from the module's own public
                                                                   static constant ElasticsearchBuilder.DefaultPassword
                                                                   ("elastic") — this fixture never calls WithPassword, and
                                                                   ElasticsearchContainer exposes no GetPassword() accessor
                                                                   to read back a generated value, so the module's default is
                                                                   both what gets configured and the only observable value)
    .AllowInvalidCertificates                                   → bool  (ALWAYS true — matches
                                                                   ElasticSearchOptions.AllowInvalidCertificates 1:1; exposed
                                                                   as a property so no consumer has to hardcode this fact
                                                                   itself, mirroring MinioContainerFixture.ForcePathStyle's
                                                                   identical always-true precedent)
    .InitializeAsync()                                         → Task  (starts the OFFICIAL Testcontainers.Elasticsearch
                                                                   module's ElasticsearchBuilder, constructed directly as
                                                                   new ElasticsearchBuilder("docker.elastic.co/elasticsearch/
                                                                   elasticsearch:9.4.2") — the tag chosen and pinned at
                                                                   Core-phase implementation time to exactly match 09.Search's
                                                                   pinned Elastic.Clients.Elasticsearch 9.4.2 client version
                                                                   (same-version client/server lockstep), confirmed to exist
                                                                   via `docker manifest inspect` against the real registry —
                                                                   NOT the parameterless ElasticsearchBuilder() +
                                                                   .WithImage(...) pattern, which is [Obsolete]/CS0618 as of
                                                                   the 4.13.0 pin (see the Technology Stack CONSTRUCTOR RULE,
                                                                   confirmed at S-27); the module's own default, elasticsearch:
                                                                   8.6.1, is an unsupported pairing with 09.Search's pinned
                                                                   9.4.2 client per Elastic's published compatibility matrix;
                                                                   adds an EXPLICIT post-start poll loop against
                                                                   GET /_cluster/health (Basic auth, self-signed-cert bypass)
                                                                   beyond the module's own built-in wait strategy, closing the
                                                                   documented readiness race testcontainers-dotnet#955 — the
                                                                   loop uses Task.Delay only as a retry interval between real
                                                                   HTTP checks, never as a blind substitute for one; confirmed
                                                                   NECESSARY, not just theoretical, by a real-Docker smoke
                                                                   test where the container took ~18s from container-ready to
                                                                   cluster-status:"green")
    .DisposeAsync()                                             → Task  (stops and removes the container)
    NOTE: Built on the OFFICIAL Testcontainers.Elasticsearch module (unlike MeilisearchContainerFixture,
          a real module exists here — only its default image needs overriding). The module runs ES 9.x
          secure-by-default over HTTPS with a self-signed cert (does NOT set
          xpack.security.enabled=false) — handled via .AllowInvalidCertificates=true, never a
          trusted-CA workaround; .CertificateFingerprint and .ApiKey (both nullable on
          ElasticSearchOptions) are deliberately NOT exposed by this fixture for that reason. Property
          names chosen to match ElasticSearchOptions.Nodes/.Username/.Password 1:1 so
          SharedKernel.Search.ElasticSearch.Tests binds this fixture directly with zero renaming.
          Deliberately takes NO ProjectReference to SharedKernel.Search.Abstractions or .ElasticSearch.
          The sole carrier of the Testcontainers.Elasticsearch PackageReference in this package, mirroring
          MinioContainerFixture-is-the-only-AWSSDK.S3-carrier / TestHarnessFactory-is-the-only-MassTransit-
          carrier.

QdrantContainerFixture  (sealed class, implements IAsyncLifetime)
    .GrpcEndpoint                                               → string  (throws InvalidOperationException if read before
                                                                   InitializeAsync completes; the PRIMARY endpoint — Qdrant.Client,
                                                                   the official .NET SDK 10.Intelligence's own Technology Stack
                                                                   pins, is gRPC/protobuf-based)
    .HttpEndpoint                                               → string  (throws InvalidOperationException if read before
                                                                   InitializeAsync completes; a SECONDARY convenience for manual
                                                                   debugging/readiness-probe reuse, not because any current
                                                                   consumer needs it)
    .InitializeAsync()                                         → Task  (starts a pinned image via the OFFICIAL Testcontainers.Qdrant
                                                                   module's QdrantBuilder, constructed directly as
                                                                   new QdrantBuilder("qdrant/qdrant:v1.16.0") — the ctor(string
                                                                   image) overload per the CONSTRUCTOR RULE, NEVER the obsolete
                                                                   parameterless ctor() + .WithImage(...); relies exclusively on the
                                                                   module's own built-in HTTP /readyz wait strategy — a real-Docker
                                                                   smoke test found this sufficient with no readiness race, so no
                                                                   post-start poll override was added, confirming the
                                                                   ElasticsearchContainerFixture-style override was not needed here)
    CORRECTED (10.Intelligence Tests-phase implementation, 2026-07-24): the original v1.13.4 pin
          (confirmed only via `docker manifest inspect`, never smoke-tested for collection-metadata
          round-trip behaviour) predates Qdrant server's collection-level metadata feature entirely.
          Verified empirically: a CreateCollectionAsync(..., metadata: ...) call against a live v1.13.4
          server silently drops the metadata (GetCollectionInfoAsync().Config.Metadata comes back
          Count == 0, no error), even though Qdrant.Client 1.18.1's own proto genuinely carries the
          field client-side. Qdrant's GitHub release notes confirm collection metadata
          ("Add custom key-value metadata to collections", qdrant/qdrant#7123) shipped in server
          v1.16.0 (2024-11-17). Re-verified the identical probe against a live v1.16.0 server: the
          round-trip works. This is the production dependency SharedKernel.AI.Qdrant's
          QdrantCollectionProvisioner takes for persisting VectorCollectionDefinition.Fingerprint
          (schema-drift detection) — a pre-v1.16.0 server makes that detection silently inert. Pin
          bumped to v1.16.0, re-confirmed to resolve via `docker manifest inspect` before adoption.
    .GrpcEndpoint / .HttpEndpoint sourced from                    QdrantContainer.GetGrpcConnectionString() / .GetHttpConnectionString()
                                                                   (both confirmed string-returning via direct reflection against the
                                                                   shipped 4.13.0 DLL, since the module's XML docs alone do not state
                                                                   return types — gRPC port 6334, HTTP port 6333)
    .DisposeAsync()                                             → Task  (stops and removes the container)
    NOTE: Property names are NOT reconciled 1:1 against a QdrantOptions type (unlike MinioContainerFixture/
          MeilisearchContainerFixture/ElasticsearchContainerFixture, each matching a live, already-ratified
          XOptions type in the owning domain) — QdrantOptions is not yet designed in 10.Intelligence; its
          shape is folded into that domain's own Core-phase task C-05, not yet ratified at Design time. This
          fixture instead exposes the official Testcontainers.Qdrant module's own natural connection surface.
          A future reconciliation pass, once QdrantOptions lands, is a tracked cross-domain follow-up, not
          performed here. Deliberately takes NO ProjectReference to SharedKernel.AI.Abstractions or .Qdrant —
          flat scalar connection properties only, so this fixture has zero build-time dependency on
          10.Intelligence's own code landing (unlike Intelligence/InMemoryVectorCollection below, which does).

MilvusContainerFixture  (sealed class, implements IAsyncLifetime)
    .Endpoint                                                    → Uri  (throws InvalidOperationException if read before
                                                                   InitializeAsync completes; sourced from
                                                                   MilvusContainer.GetEndpoint() — confirmed Uri-returning via
                                                                   direct reflection against the shipped 4.13.0 DLL, since the
                                                                   module's XML docs alone do not state return types — gRPC
                                                                   port 19530)
    .InitializeAsync()                                         → Task  (starts a pinned image via the OFFICIAL Testcontainers.Milvus
                                                                   module's MilvusBuilder, constructed directly as
                                                                   new MilvusBuilder("milvusdb/milvus:v2.3.10") — the ctor(string
                                                                   image) overload per the CONSTRUCTOR RULE; the candidate pin was
                                                                   RE-verified via `docker manifest inspect` at Core-phase
                                                                   implementation time (2026-07-22) — resolves successfully, no
                                                                   drift from the candidate, used as-is; relies exclusively on the
                                                                   module's own built-in Wait.ForUnixContainer().UntilContainerIsHealthy()
                                                                   docker-healthcheck wait strategy (curl /healthz on management port
                                                                   9091) — a real-Docker smoke test at implementation time found this
                                                                   sufficient with no readiness race, so no override was added,
                                                                   mirroring the QdrantContainerFixture outcome above)
    .DisposeAsync()                                             → Task  (stops and removes the container)
    NOTE (MINIMAL STANDALONE DEPLOYMENT MODE — THE ACCEPTANCE-CRITERION FACT THIS FIXTURE MUST DOCUMENT):
          the Testcontainers.Milvus module's OWN DEFAULT configuration already runs Milvus in genuine
          single-container standalone mode via its own DEPLOY_MODE=STANDALONE / ETCD_USE_EMBED=true /
          COMMON_STORAGETYPE=local environment variables (embedded etcd, no external etcd/MinIO sidecar
          container, no docker-compose orchestration) — confirmed directly from the module's own
          MilvusBuilder.cs source on 2026-07-21, not assumed. This fixture uses that default AS-IS, with
          ZERO fixture-level environment-variable overrides — the module's own WithEtcdEndpoint() escape
          hatch (for switching to an EXTERNAL etcd) is deliberately never called.
    NOTE: Property naming is NOT reconciled against a MilvusOptions type, for the identical reason
          QdrantContainerFixture's NOTE gives above (MilvusOptions is folded into 10.Intelligence's own
          Core-phase task C-08, not yet ratified). Deliberately takes NO ProjectReference to
          SharedKernel.AI.Abstractions or .Milvus — flat scalar connection properties only.

BOTH NEW FIXTURES REQUIRE NO Testcontainers.* VERSION-ALIGNMENT CEREMONY (P-283/WO-045, in contrast to the
    P-275/WO-044 Elasticsearch addition): Testcontainers.Qdrant and Testcontainers.Milvus are BOTH confirmed
    on nuget.org (2026-07-21) at latest stable 4.13.0 — matching this package's EXISTING Testcontainers.*
    floor exactly (established at S-25–S-27/WO-044). No other Containers/ fixture's pin needs to move.
    Construct both new fixtures via each builder's ctor(string image) overload directly, per the
    CONSTRUCTOR RULE below — never the obsolete parameterless ctor() + .WithImage(...) shape.

VERSION-ALIGNMENT DECISION (P-275/WO-044, resolved explicitly — never left to implicit NuGet resolution):
    the four pre-existing Containers/ fixtures pinned Testcontainers.* at 4.1.0. Adding
    Testcontainers.Elasticsearch 4.13.0 lifts the transitive Testcontainers floor for the whole shared
    package. DECISION: bump ALL FOUR existing pins (.PostgreSql/.Redis/.RabbitMq/.Minio) to 4.13.0 in
    the same Scaffold pass, plus add a NEW direct PackageReference to the base Testcontainers package
    itself (4.13.0, for MeilisearchContainerFixture's hand-rolled generic-builder use) — a single
    consistent Testcontainers.* version across the whole Containers/ folder, avoiding a mixed-version
    transitive Testcontainers core-package resolution.

    CONFIRMED AT S-27 IMPLEMENTATION TIME (2026-07-19): this bump was NOT a drop-in across the 4.1.0→
    4.13.0 span. Each of the four existing fixtures' own builder type (RedisBuilder/MinioBuilder/
    RabbitMqBuilder/PostgreSqlBuilder) had its parameterless constructor obsoleted somewhere in that
    range in favor of a new ctor(string image) overload — building at 4.13.0 with the pre-existing
    `new XBuilder().WithImage("...")` call shape now emits CS0618. Fixed by rewriting all four
    Containers/*ContainerFixture.cs files to `new XBuilder("repository:tag")` directly — identical
    pinned image string, zero behavioral change, reconfirmed via a real-Docker run of
    SharedKernel.Testing.SelfTests' Containers/ suite (12/12 passing) after the fix. This is a
    platform-wide Testcontainers 4.13.0 pattern, not package-specific — the base ContainerBuilder
    (MeilisearchContainerFixture's target) and ElasticsearchBuilder (ElasticsearchContainerFixture's
    target) carry the identical ctor(string image) overload, confirmed via their own shipped XML docs
    before either fixture was implemented. See the Technology Stack CONSTRUCTOR RULE and both
    fixtures' corrected .InitializeAsync() design notes above — whoever implements C-64/C-65 should
    construct directly via the image-string constructor from the start, never ctor() + .WithImage(...).
```

### `Fakers/` — Bogus convention (02.Caching-and-beyond, cross-cutting)

```text
FakerSeeding  (static class)
    .Apply(int seed = 8675309)                                 → void
        Sets Bogus.Randomizer.Seed = new Random(seed). Call once per test assembly (e.g. from an
        xUnit AssemblyFixture or module initializer) so every Faker<T> in that run is deterministic
        across CI re-executions.
    NOTE: Concrete Faker<TAggregate> definitions for business entities are NOT defined in this
          package — they depend on each microservice's own aggregate shapes and belong in that
          service's own test project. SharedKernel.Testing ships only the shared determinism
          convention every one of those fakers should opt into.
```

### `Communication/` — cross-cutting Communication test doubles (11.Communication) — merges superseded P-158 + P-168

```text
MockServiceEndpointResolver  (sealed class, implements IServiceEndpointResolver)
    .Configure(string serviceName, Uri uri)                     → void  (registers a fixed resolution result for serviceName)
    .GetResolvedNames()                                         → IReadOnlyList<string>  (every serviceName ever passed to ResolveAsync, for assertion)
    NOTE: Supports per-service failure injection; never throws on an unconfigured name (falls back
          to a deterministic non-throwing default, mirroring the production resolver's "never throws"
          contract). References SharedKernel.Communication.Internal only.

FakeHttpContextAccessor  (sealed class, implements IHttpContextAccessor)
    constructor(HttpContext? context = null)
    — holds a fixed (or null) HttpContext with a configurable TenantId on its backing ITenantProvider
    NOTE: Consolidates ad-hoc duplicate fakes currently in SharedKernel.Communication.Rest.Tests and
          .Grpc.Tests. References Microsoft.AspNetCore.Http and SharedKernel.Security.Abstractions.

HttpClientHandlerTestFactory  (sealed class — fluent builder)
    .WithInnerHandler(HttpMessageHandler handler)                → HttpClientHandlerTestFactory  (fluent)
    .WithCorrelationIdHandler()                                  → HttpClientHandlerTestFactory  (fluent)
    .WithTenantIdHandler(Guid? tenantId)                         → HttpClientHandlerTestFactory  (fluent)
    .Build()                                                     → HttpMessageHandler  (returns the outermost handler for direct HttpClient construction)
    NOTE: Builds a pre-wired DelegatingHandler chain without a full ServiceCollection. References
          Microsoft.Extensions.Http only.

FakeHttpMessageHandler  (sealed class, extends HttpMessageHandler)
    — supports fixed and sequenced response fixtures (e.g., first call 503, second 200) for
      resilience-policy testing
    — allows post-call HttpRequestMessage inspection (which headers were injected)
    NOTE: Folded in from the superseded P-158.

AmbientActivityTestHelper  (sealed class, implements IDisposable)
    static .Start(ActivityTraceId traceId, ActivitySpanId? parentSpanId = null) → AmbientActivityTestHelper
        Sets Activity.Current to a new Activity with the given trace id (and optional parent span id);
        Dispose() restores the prior Activity.Current so no test leaks ambient state into the next one.
    .Activity                                                   → Activity  (the ambient activity created by Start)
    NOTE: Folded in from the superseded P-158. CORRECTED during the Tests phase (T-31): ActivitySource.
          StartActivity returns null when no ActivityListener is sampling the source — the default
          outside an OTel-instrumented host, which is every pure unit test using this helper. Fixed by
          registering a single static always-sampling ActivityListener scoped to this type's private
          ActivitySource only — a second documented exception to the "no static mutable state" rule
          below, same class as FakerSeeding.Apply (process-wide, deliberate, opt-in; never incidental
          shared state).

TestServerCallContext  (static factory / sealed helper)
    — produces a ServerCallContext-equivalent for testing gRPC interceptors in isolation, allowing
      post-execution metadata inspection
    NOTE: Folded in from the superseded P-158.

GraphQLTestExecutorFactory  (static class)
    — wires AddSharedKernelGraphQL() with test-safe defaults (AllowIntrospection = true, MaxPageSize = 10)
      onto an IRequestExecutorBuilder
    NOTE: Folded in from the superseded P-158.

ActivityRecorder  (sealed class, implements IDisposable)
    static .StartRecording(string activitySourceName)          → ActivityRecorder
        Registers a process-scoped ActivityListener filtered to the named ActivitySource only
        (Sample = AllDataAndRecorded), so only Activity instances started against that one source are
        captured — multiple ActivityRecorder instances recording different source names never interfere.
    .RecordedActivities                                        → IReadOnlyList<Activity>
        Every Activity started against the recorded source while recording is active, in start order.
    .Dispose()                                                 → void  (unregisters the ActivityListener)
    NOTE: Additive sibling to AmbientActivityTestHelper, not a replacement — AmbientActivityTestHelper sets
          Activity.Current to a caller-built Activity for propagation/correlation-id tests (an ambient-
          context setter); ActivityRecorder instead records spans started by code under test against a
          named, externally-owned ActivitySource (e.g. 05.Application's "SharedKernel.Application") so a
          test can assert span count/tags/duration after the fact (a recording listener) — distinct
          capabilities, designed for 05.Application's (design-only, WO-036) TracingBehavior test need.
          Zero dependency beyond BCL System.Diagnostics — no new PackageReference. Lives in Communication/
          folder alongside AmbientActivityTestHelper since both are ambient-Activity/OTel test-support
          helpers, not a new capability folder.

SCOPE LOCK (P-186/WO-029): SharedKernel.Testing must never take a project reference to
    SharedKernel.Communication.Rest, .Grpc, or .GraphQL. MockServiceEndpointResolver may reference
    SharedKernel.Communication.Internal for IServiceEndpointResolver only. Once these helpers exist,
    the ad-hoc duplicate fakes in SharedKernel.Communication.Rest.Tests and .Grpc.Tests must be
    removed in favor of them (tracked as a cross-domain follow-up, not a file edit performed here).
```

### `ServiceDefaults/` — tenant resolution and health check test doubles (13.ServiceDefaults) — carried forward unchanged from superseded P-174

```text
StaticTenantProvider  (sealed class, implements ITenantProvider)
    constructor(Guid tenantId)                                  — also usable with Guid.Empty for the no-tenant case
    .TenantId                                                   → Guid  (fixed at construction)
    NOTE: Implements a 12.Security-owned interface directly — trivial deterministic tenant context
          without standing up AmbientTenantProvider + middleware + HTTP context.

FakeTenantResolutionStrategy  (sealed class — structurally compatible, NOT a direct ITenantResolutionStrategy implementation)
    constructor(Guid? fixedResult = null)
    constructor(Func<HttpContext, CancellationToken, Task<Guid?>> resolver)
    .StrategyName                                               → string  (settable; mirrors ITenantResolutionStrategy.StrategyName shape)
    .TryResolveAsync(HttpContext context, CancellationToken ct)  → Task<Guid?>  (mirrors ITenantResolutionStrategy.TryResolveAsync signature)
    NOTE: Deliberately structural rather than a direct interface implementation — referencing
          ITenantResolutionStrategy directly would require a project reference to
          SharedKernel.MultiTenancy (13.ServiceDefaults), which is out of scope for this package per
          the scope lock below. A consuming service's test project that does take that reference can
          still use this type as a drop-in (duck-typed) substitute since the member shapes match
          exactly.

HealthCheckAssertionExtensions  (static class — extension methods on HealthCheckRegistration)
    .ShouldBeTaggedReady(this HealthCheckRegistration registration)    → void  (throws if "ready" tag absent)
    .ShouldNotBeTaggedLive(this HealthCheckRegistration registration)  → void  (throws if "live" tag present)
    NOTE: Verifies tag composition without booting a WebApplicationFactory. References
          Microsoft.Extensions.Diagnostics.HealthChecks only.

SCOPE LOCK (P-187/WO-029): SharedKernel.Testing must never take a project reference to
    SharedKernel.ServiceDefaults or SharedKernel.MultiTenancy. StaticTenantProvider and
    FakeTenantResolutionStrategy reference only SharedKernel.Security.Abstractions.
```

### `Application/` — local-seam test doubles + MediatR pipeline test harness (05.Application.Behaviors) — added WO-040

```text
FakeUnitOfWork  (sealed class, implements SharedKernel.Application.Behaviors.IUnitOfWork)
    NOTE: This is NOT a fake for SharedKernel.Persistence.Abstractions.IUnitOfWork — 05.Application
          ships its own, deliberately narrower local IUnitOfWork seam (single member,
          SaveChangesAsync(CancellationToken) → Task<int>), bridged to the real persistence
          IUnitOfWork only at each consuming service's composition root. This fake satisfies the
          LOCAL seam only. See root CLAUDE.md's own explicit disambiguation of the two same-named
          interfaces across domains.
    .SaveChangesCallCount                                      → int  (thread-safe via Interlocked)
    .SaveChangesResult                                         → int  (settable; default 1)
    .SimulateFailure                                           → bool (settable; when true,
                                                                   SaveChangesAsync throws
                                                                   InvalidOperationException instead
                                                                   of returning — SaveChangesCallCount
                                                                   still increments, since the call
                                                                   happened, it just faulted)
    .SaveChangesAsync(CancellationToken ct)                    → Task<int>
    NOTE: Lets a test assert TransactionBehavior's exact contract — SaveChangesAsync is called
          exactly once after next() returns, never called if next() throws — without a real
          persistence provider.

FakeAuthorizationContext  (sealed class, implements IAuthorizationContext from 05.Application.Behaviors)
    constructor(bool defaultResult = true)
        Unconfigured requirement strings evaluate to defaultResult — defaults to true so most
        pipeline tests need zero configuration, mirroring FakeUserContext's authenticated-by-default
        convention (Security/).
    .Allow(string requirement)                                 → FakeAuthorizationContext  (fluent)
    .Deny(string requirement)                                  → FakeAuthorizationContext  (fluent)
    .IsAuthorizedAsync(string requirement, CancellationToken ct) → Task<bool>
        Returns the configured value for requirement, or defaultResult if unconfigured.
    .AllOf(IEnumerable<string> requirements, CancellationToken ct) → Task<bool>
        Vacuous-true on an empty collection — matches the live IAuthorizationContext.AllOf XML doc
        exactly (05.Application/SharedKernel.Application.Behaviors/Authorization/IAuthorizationContext.cs):
        "An empty sequence returns true immediately (vacuous truth — no requirements to fail)."
        Otherwise true only if every requirement resolves true (short-circuit on first failure).
    .AnyOf(IEnumerable<string> requirements, CancellationToken ct) → Task<bool>
        CORRECTED at Design-confirmation time (D-60/WO-040): NOT vacuous-true — an empty collection
        returns FALSE, matching the live IAuthorizationContext.AnyOf XML doc verbatim: "An empty
        sequence returns false (nothing to satisfy)." An earlier draft of this contract incorrectly
        stated "same vacuous-true-on-empty rule" for AnyOf; that was never true of the live interface
        and is superseded by this correction. Otherwise true as soon as one requirement resolves true
        (short-circuit on first pass). NOTE: AuthorizationBehavior<,> itself never actually calls
        IAuthorizationContext.AnyOf with an empty collection — it guards with a `Count > 0` check
        first — so this empty-input edge case only matters for a test calling FakeAuthorizationContext
        .AnyOf(...) directly, not through the behavior.
    .RequirementsChecked                                       → IReadOnlyList<string>  (every
                                                                   requirement string passed to any
                                                                   of the three methods above, in
                                                                   call order, for test assertions)
    .Reset()                                                   → void  (clears the configured map
                                                                   and the recorded list)
    NOTE: Backed by ConcurrentDictionary<string,bool> + a thread-safe recorded-call list. This is
          NOT SharedKernel.Security.Abstractions.IUserContext — it fakes 05.Application's own
          narrower local seam only.

FakeIdempotencyKeyStore  (sealed class, implements IIdempotencyKeyStore from 05.Application.Behaviors ONLY)
    .HasProcessedAsync(string idempotencyKey, CancellationToken ct) → Task<bool>
    .MarkProcessedAsync(string idempotencyKey, CancellationToken ct) → Task
    .ProcessedKeys                                             → IReadOnlyCollection<string>
    .MarkAsProcessed(string idempotencyKey)                    → void  (test-setup helper — pre-seeds
                                                                   a key as already processed without
                                                                   going through MarkProcessedAsync,
                                                                   simulating "this key was already
                                                                   consumed by a prior run")
    NOTE: Deliberately does NOT implement IIdempotencyResponseStore — use this fake to exercise
          IdempotentCommandBehavior's non-replay default path (duplicate → Result.Failure
          (Error.Conflict)). Backed by a thread-safe ConcurrentDictionary<string, byte>.

FakeIdempotencyResponseStore  (sealed class, implements IIdempotencyKeyStore AND IIdempotencyResponseStore
                                from 05.Application.Behaviors)
    .HasProcessedAsync / .MarkProcessedAsync / .ProcessedKeys / .MarkAsProcessed(...)
        — identical semantics to FakeIdempotencyKeyStore above.
    .TryGetStoredResponseAsync(string idempotencyKey, CancellationToken ct) → Task<string?>
    .StoreResponseAsync(string idempotencyKey, string serializedResponse, CancellationToken ct) → Task
    .StoredResponses                                           → IReadOnlyDictionary<string,string>
    NOTE: Implements BOTH interfaces so IdempotentCommandBehavior's `is IIdempotencyResponseStore`
          runtime pattern-match succeeds — this is the fake to register for response-replay
          end-to-end tests. THIS MUST BE A SEPARATE CONCRETE TYPE FROM FakeIdempotencyKeyStore, not
          a constructor flag on one type: C# cannot toggle interface implementation at runtime, and
          IdempotentCommandBehavior's replay-capability detection depends on the CLR type actually
          implementing the second interface. A test seeds via MarkAsProcessed + StoreResponseAsync
          (or lets a first dispatch populate both naturally) then dispatches a second request with
          the same key and asserts the ORIGINAL response is replayed rather than a fresh
          Error.Conflict.

AddFakeApplicationBehaviorServices(this IServiceCollection)
    NOTE: Registers FakeUnitOfWork → IUnitOfWork, FakeAuthorizationContext (constructed with
          defaultResult: true) → IAuthorizationContext, and FakeIdempotencyKeyStore (the non-replay
          variant — the default production shape absent opt-in) → IIdempotencyKeyStore, all as
          singletons — mirrors AddFakeCachingServices()'s one-call bundling pattern. For
          response-replay tests, register FakeIdempotencyResponseStore manually instead:
          services.AddSingleton<IIdempotencyKeyStore, FakeIdempotencyResponseStore>();
          this call satisfies ApplicationBehaviorsBuilder's Build()-time missing-dependency guards
          for AddTransactionBehavior()/AddAuthorizationBehavior()/AddIdempotencyBehavior() in one
          step.

ApplicationPipelineTestHarness  (sealed class, implements IDisposable)
    NOTE: Public promotion of the internal-only PipelineTestHarness already proven in
          SharedKernel.Application.Behaviors.Tests/TestHarness/PipelineTestHarness.cs — same design,
          renamed to avoid ambiguity with 07.Messaging's TestHarnessFactory/MassTransit ITestHarness
          in the sibling Messaging/ folder. Wires a real ServiceCollection + AddMediatR + a
          caller-chosen subset of ApplicationBehaviorsBuilder-registered behaviors, and dispatches a
          request through the resulting pipeline.
    .Services                                                  → ServiceCollection  (exposes the
                                                                   underlying collection for
                                                                   additional test-specific
                                                                   registration, e.g. handlers or
                                                                   this folder's other fakes)
    .AddBehaviors()                                            → ApplicationBehaviorsBuilder
                                                                   (delegates to
                                                                   Services.AddSharedKernelApplicationBehaviors())
    .WithActivityCapture()                                     → ApplicationPipelineTestHarness
                                                                   (fluent; registers an opt-in
                                                                   ActivityListener filtered to the
                                                                   "SharedKernel.Application"
                                                                   ActivitySource)
    .Build<TMarker>()                                          → ApplicationPipelineTestHarness
                                                                   (registers AddMediatR from the
                                                                   assembly containing TMarker,
                                                                   builds the ServiceProvider; must
                                                                   be called after all behavior/
                                                                   handler registration)
    .SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) → Task<TResponse>
        Throws InvalidOperationException if called before Build<TMarker>().
    .CapturedActivities                                        → IReadOnlyList<Activity>  (populated
                                                                   only when WithActivityCapture()
                                                                   was called first)
    .CapturedMeasurements                                      → IReadOnlyList<(string InstrumentName,
                                                                   double Value,
                                                                   IReadOnlyList<KeyValuePair<string,object?>> Tags)>
        Always captured (no opt-in gate needed), filtered to the "SharedKernel.Application" Meter —
        mirrors the internal harness's always-on MeterListener.
    .Dispose()                                                 → void  (disposes the ActivityListener
                                                                   if registered, the MeterListener,
                                                                   and the ServiceProvider if built)
    NOTE: Implements its OWN local ActivityListener wiring (self-contained BCL System.Diagnostics
          code) rather than referencing Communication/ActivityRecorder, even though the two are
          functionally similar — the sibling-capability-folder-isolation hard rule forbids
          Application/ from referencing Communication/. This is a deliberate, accepted duplication
          of a small amount of ActivityListener boilerplate, not an oversight. No Add* DI extension
          — directly `new`-able, consistent with this package's builder-type convention
          (SpecificationTestBuilder<T>, ProjectionSpecificationBuilder<TAggregate,TResult>).
          CROSS-DOMAIN FOLLOW-UP (tracked, not performed here): the existing internal
          PipelineTestHarness at 05.Application.Behaviors.Tests/TestHarness/PipelineTestHarness.cs
          is a candidate to become a thin wrapper over (or be deleted in favor of) this public type
          once 05.Application.Behaviors.Tests adopts it — that is a 05.Application-side file edit,
          out of 16.Testing's jurisdiction; this domain never touches a .Tests project, in this
          domain or any other.

SCOPE LOCK (P-244/WO-040): Every type in Application/ references only SharedKernel.Application.Behaviors
    — never SharedKernel.Persistence.Abstractions, SharedKernel.Security.Abstractions, or
    SharedKernel.Messaging.Abstractions directly, even though the real production bridges for these
    local seams live in those domains. These fakes satisfy the LOCAL seam contracts only.
```

### `Logging/` — structured log capture double (Microsoft.Extensions.Logging.Abstractions, cross-cutting) — added P-258/WO-041

```text
LogRecord  (sealed record)
    .EventId                                                   → Microsoft.Extensions.Logging.EventId
    .LogLevel                                                  → Microsoft.Extensions.Logging.LogLevel
    .Message                                                   → string  (fully formatted, via the caller-supplied
                                                                   Func<TState,Exception?,string> formatter — never
                                                                   re-derived from .State)
    .State                                                      → IReadOnlyList<KeyValuePair<string,object?>>?
                                                                   (populated only when TState implements that
                                                                   interface — the exact shape both [LoggerMessage]'s
                                                                   source-generated state struct and standard
                                                                   structured-logging calls produce; null otherwise)
    .Exception                                                 → Exception?
    .Scopes                                                    → IReadOnlyList<object?>  (active BeginScope stack at
                                                                   the moment this record was logged, outer-to-inner)
    .TryGetProperty(string name, out object? value)            → bool  (scans .State for a KeyValuePair whose Key
                                                                   exactly matches name — case-sensitive, matching
                                                                   the root CLAUDE.md logging convention's PascalCase
                                                                   named-placeholder rule)
    NOTE: The primitive LoggerAssertions.ShouldHaveLoggedWithProperty is built on. Never string-parses .Message.

InMemoryLogger  (sealed class, implements Microsoft.Extensions.Logging.ILogger)
    .MinLevel                                                  → LogLevel  (settable; default LogLevel.Trace —
                                                                   captures everything by default)
    .Log<TState>(LogLevel, EventId, TState, Exception?, Func<TState,Exception?,string>) → void
        Appends a LogRecord (per above) to a thread-safe ConcurrentQueue<LogRecord>.
    .IsEnabled(LogLevel level)                                 → bool  (level >= MinLevel)
    .BeginScope<TState>(TState state)                          → IDisposable
        Pushes state onto an AsyncLocal<ScopeNode?>-backed immutable linked-list scope stack; the returned
        IDisposable pops exactly that node on Dispose(). AsyncLocal (not a plain field/thread-static) so
        nested `using (logger.BeginScope(...))` blocks compose correctly across await boundaries the same
        way a real logging provider's scope stack does, and parallel xUnit test collections never
        cross-contaminate each other's scope state.
    .Records                                                   → IReadOnlyList<LogRecord>  (snapshot of the queue)
    .Clear()                                                   → void  (empties the queue — for multi-phase
                                                                   single-test assertions)

InMemoryLogger<TCategoryName>  (sealed class, implements ILogger<TCategoryName>)
    NOTE: A directly `new`-able convenience type for tests that construct a handler under test by hand (no
          DI container) and need an ILogger<THandler> constructor argument — mirrors FakeUserContext's
          plain-new-able convention. Implemented via COMPOSITION, not inheritance (both InMemoryLogger and
          InMemoryLogger<TCategoryName> stay sealed, per this package's standing rule): holds a private
          InMemoryLogger instance and forwards Log/IsEnabled/BeginScope to it.
    .Records / .MinLevel / .Clear()                            — forward to the wrapped InMemoryLogger

InMemoryLoggerFactory  (sealed class, implements Microsoft.Extensions.Logging.ILoggerFactory)
    .CreateLogger(string categoryName)                         → ILogger  (returns/creates a per-category
                                                                   InMemoryLogger via
                                                                   ConcurrentDictionary<string,InMemoryLogger>
                                                                   .GetOrAdd — auto-creates, no pre-registration
                                                                   needed)
    .AddProvider(ILoggerProvider provider)                     → void  (documented no-op — this fake IS the
                                                                   entire logging pipeline for the test; it does
                                                                   not compose with additional providers)
    .Dispose()                                                 → void  (no-op — nothing to release)
    .GetLogger(string categoryName)                            → InMemoryLogger  (same GetOrAdd semantics as
                                                                   CreateLogger, exposed under a more discoverable
                                                                   name for assertion call sites)
    .Loggers                                                   → IReadOnlyDictionary<string,InMemoryLogger>
                                                                   (snapshot of every category created so far)

LoggerAssertions  (static class — extension methods on IReadOnlyList<LogRecord>, i.e. InMemoryLogger.Records)
    .ShouldHaveLogged(EventId eventId)                          → LogRecord  (first match; throws
                                                                   InvalidOperationException if none found)
    .ShouldHaveLogged(EventId eventId, LogLevel level)          → LogRecord  (level must also match)
    .ShouldHaveLoggedWithProperty(EventId eventId, string propertyName, object? expectedValue) → LogRecord
        Throws unless a record matches eventId AND LogRecord.TryGetProperty returns a value
        object.Equals-equal to expectedValue — asserts by structured property value, never by
        rendered-message string-matching.
    .ShouldNotHaveLogged(EventId eventId)                       → void  (throws if any match exists)
    .ShouldHaveLoggedCount(EventId eventId, int expectedCount)  → void
    NOTE: All throw plain InvalidOperationException — zero test-framework dependency, mirroring
          InMemoryMessageBus's Should* naming and EnvelopeAssertions's exception convention. Read-only
          queries — never mutate .Records.

AddInMemoryLoggerFactory(this IServiceCollection)
    NOTE: Registers InMemoryLoggerFactory as a SINGLETON ILoggerFactory (same "assertions must survive past
          the DI scope" rationale as AddInMemoryMessageBus/AddInMemoryEventPublisher), and additionally
          registers the REAL BCL open-generic Microsoft.Extensions.Logging.Logger<> adapter class (from
          Microsoft.Extensions.Logging.Abstractions itself — not a new SharedKernel type) as ILogger<>,
          exactly mirroring the mechanism Microsoft.Extensions.Logging's own AddLogging() uses internally —
          so ILogger<THandler> constructor-injected anywhere in the container under test resolves correctly
          through the fake with zero additional SharedKernel code.

SCOPE LOCK (P-258/WO-041): Logging/ references only Microsoft.Extensions.Logging.Abstractions — a NuGet
    PackageReference, not a SharedKernel.*.Abstractions ProjectReference. This is the first capability
    folder in this package anchored to a cross-cutting BCL contract rather than a specific numbered
    domain's own abstraction package. Logging/ must never reference any sibling capability folder
    (Caching/, Messaging/, Application/, etc.), per the standing sibling-isolation rule.
```

### `Storage/` — IFileStorage / IBlobUriGenerator in-memory doubles (08.Storage) — added P-269/WO-043

```text
InMemoryFileStorage  (sealed class, implements IFileStorage from SharedKernel.Storage.Abstractions)
    .UploadAsync(FileUploadRequest request, CancellationToken ct)              → Task<Result<FileReference>>
    .DownloadAsync(string bucket, string key, CancellationToken ct)            → Task<Result<FileDownload>>
    .DeleteAsync(string bucket, string key, CancellationToken ct)              → Task<Result>
    .ExistsAsync(string bucket, string key, CancellationToken ct)              → Task<Result<bool>>
    .GetMetadataAsync(string bucket, string key, CancellationToken ct)         → Task<Result<FileMetadata>>
    .CopyAsync(string sourceBucket, string sourceKey,
               string destinationBucket, string destinationKey,
               CancellationToken ct)                                          → Task<Result<FileReference>>
    .DeleteManyAsync(string bucket, IReadOnlyCollection<string> keys,
                      CancellationToken ct)                                    → Task<Result<IReadOnlyList<FileDeleteOutcome>>>
    .ListAsync(string bucket, string prefix, [EnumeratorCancellation] CancellationToken ct) → IAsyncEnumerable<FileMetadata>
    .CheckHealthAsync(string bucket, CancellationToken ct)                     → Task<Result>
    .SimulateFailure                                           → bool  (settable, default false — a SINGLE general-purpose
                                                                   write-path failure toggle, mirroring
                                                                   FakeDistributedLockService.SimulateFailure's naming/shape
                                                                   rather than five separate per-operation flags; when true,
                                                                   UploadAsync/CopyAsync/DeleteAsync/DeleteManyAsync's outer
                                                                   call all return the matching StorageErrors factory failure
                                                                   instead of performing the operation. Read-path members
                                                                   (DownloadAsync/ExistsAsync/GetMetadataAsync/ListAsync) and
                                                                   CheckHealthAsync are UNAFFECTED by this toggle)
    .UploadedKeys                                              → IReadOnlyList<(string Bucket, string Key)>  (every key ever
                                                                   successfully uploaded, thread-safe, append-only — never
                                                                   pruned on delete, mirroring InMemoryMessageBus's
                                                                   "records every call even when never asserted on" rule)
    .DeletedKeys                                               → IReadOnlyList<(string Bucket, string Key)>  (every key ever
                                                                   successfully deleted via DeleteAsync or DeleteManyAsync)
    .CopiedPairs                                                → IReadOnlyList<(string SourceBucket, string SourceKey,
                                                                   string DestinationBucket, string DestinationKey)>
    .WasUploaded(string bucket, string key)                     → bool  (query helper over UploadedKeys)
    .WasDeleted(string bucket, string key)                      → bool  (query helper over DeletedKeys)
    .WasCopied(string sourceBucket, string sourceKey,
               string destinationBucket, string destinationKey) → bool  (query helper over CopiedPairs)
    .Seed(string bucket, string key, Stream content, string contentType,
          IReadOnlyDictionary<string,string>? metadata = null)  → void  (test-setup helper — pre-populates storage without
                                                                   going through UploadAsync, mirroring
                                                                   FakeIdempotencyKeyStore.MarkAsProcessed's "pre-seed
                                                                   without the normal method" precedent; reads content
                                                                   fully but does not dispose the caller's stream, same
                                                                   ownership contract as UploadAsync)
    .Reset()                                                    → void  (clears all stored objects AND UploadedKeys/
                                                                   DeletedKeys/CopiedPairs)
    NOTE: Backing store is a ConcurrentDictionary<(string Bucket, string Key), StoredObject> holding
          fully-buffered content bytes (read once from the caller's Stream during UploadAsync/Seed —
          FileUploadRequest.Content is never disposed by this fake, per its documented caller-owned
          contract), ContentType, Metadata, a DETERMINISTIC incrementing ETag (an Interlocked-based
          sequence counter, e.g. "etag-{n}" — never a random Guid, keeping with this package's
          no-unseeded-randomness rule), null VersionId, and a FIXED, non-real LastModified instant
          (never DateTimeOffset.UtcNow) set on every upload/copy. This fake does NOT take an IClock
          constructor dependency and does NOT reference Clocks/FakeClock — sibling capability folders
          must never reference each other, so "never real time" is achieved here via an internal
          fixed baseline value, independently declared, exactly as Storage/'s own isolation demands.
          DownloadAsync wraps stored bytes in a FRESH MemoryStream per call so each returned
          FileDownload is independently disposable. ExistsAsync/GetMetadataAsync/DownloadAsync return
          StorageErrors.NotFound for a missing key — no failure-injection toggle needed since this
          path is already exercisable by simply never uploading/seeding the key. DeleteAsync is
          idempotent (absent key still returns Result.Success), matching the real contract exactly.
          CopyAsync on a missing source key returns StorageErrors.NotFound(sourceBucket, sourceKey),
          mirroring the real providers' documented source-404 mapping. DeleteManyAsync's per-key
          FileDeleteOutcome list treats each requested key's deletion as idempotent (absent key →
          Succeeded=true); the outer Result fails only when keys is null/empty (there is no
          transport-fault path in an in-memory fake, so empty input is the only non-SimulateFailure
          outer-failure trigger, matching the real contract's "outer Result fails ONLY when the batch
          call itself cannot be attempted" note). ListAsync is a REAL async iterator (yields lazily,
          checks ct.ThrowIfCancellationRequested() per item, never fully materializes before the
          first yield) filtered by bucket + Key.StartsWith(prefix), so a consuming test can assert
          cancellation-mid-enumeration behavior identically to the real provider's documented
          contract. CheckHealthAsync ALWAYS returns Result.Success() unconditionally — deliberately
          NOT gated by SimulateFailure, per this phase's explicit acceptance criterion; this is the
          one documented exception to that toggle's scope.

InMemoryBlobUriGenerator  (sealed class, implements IBlobUriGenerator from SharedKernel.Storage.Abstractions)
    .GeneratePresignedUploadUrl(PresignedUrlRequest request)                   → Result<PresignedUrl>
    .GeneratePresignedDownloadUrl(PresignedUrlRequest request)                 → Result<PresignedUrl>
    .GeneratedUploadUrls                                        → IReadOnlyList<PresignedUrlRequest>  (every request ever
                                                                   passed to GeneratePresignedUploadUrl)
    .GeneratedDownloadUrls                                      → IReadOnlyList<PresignedUrlRequest>  (same, for the
                                                                   download generator)
    NOTE: Returns a deterministic, inspectable Uri encoding bucket/key/mode/expiry directly in the URL
          string itself (e.g. "https://fake-storage.test/{bucket}/{key}?mode=upload&expirySeconds=
          {n}"), so a test can assert on URL content directly without needing the recorded-list
          accessors, though both are provided for convenience. PresignedUrl.ExpiresAt is computed as
          a FIXED internal non-real baseline instant + request.Expiry — never
          DateTimeOffset.UtcNow-derived, same "never real time" philosophy as InMemoryFileStorage
          above, independently declared (no Clocks/ dependency, per sibling-isolation). Honors the
          SAME StorageErrors.ExpiryTooLong validation as the real provider contract (Expiry exceeding
          the documented 7-day provider maximum) so a test asserting this error path behaves
          identically against the fake and a real provider.

AddInMemoryFileStorage(this IServiceCollection)
    NOTE: Registers InMemoryFileStorage → IFileStorage and InMemoryBlobUriGenerator → IBlobUriGenerator
          as SINGLETONS, mirroring AddInMemoryMessageBus()/AddInMemoryEventPublisher()'s naming
          convention. Unlike those two (which deliberately diverge from a scoped production
          lifetime), this registration's singleton lifetime matches IFileStorage/IBlobUriGenerator's
          own production lifetime exactly — both are already registered as singletons by
          AddSharedKernelS3Storage()/AddSharedKernelObsStorage() (08.Storage) — so there is no
          lifetime deviation to document here, unlike the messaging doubles.

SCOPE LOCK (P-269/WO-043): Storage/ references only SharedKernel.Storage.Abstractions — never
    SharedKernel.Storage.S3 or SharedKernel.Storage.Obs (the concrete provider packages), and never
    any sibling capability folder in this package (Caching/, Messaging/, Persistence/, Application/,
    Logging/, Containers/, etc.) — in particular, never Containers/MinioContainerFixture. The
    in-memory fake (fast, isolated, no Docker) and the real-provider-integration fixture (Docker,
    exercises the actual AWSSDK.S3-backed providers) are deliberately independent test paths serving
    different audiences: InMemoryFileStorage/InMemoryBlobUriGenerator are for a DOWNSTREAM
    MICROSERVICE's own fast unit tests of handler/service logic that merely depends on IFileStorage;
    MinioContainerFixture is for 08.Storage's OWN provider-behavior verification against a real
    S3-compatible endpoint. Neither substitutes for the other.

BLOCKER-CLEARANCE VERIFICATION (re-verified directly on disk, not assumed from CLAUDE.md prose):
    at the original WO-043 design pass, `08.Storage/SharedKernel.Storage.Abstractions/SharedKernel.Storage.Abstractions.csproj`
    was a genuinely empty placeholder — zero `.cs` files, no ProjectReference, no PackageReference —
    and `08.Storage`'s own `state-map.md` Package Board confirmed it was 100% Design-phase (`○`), not
    merely "design-only" in the softer sense already precedented in this package (e.g. P-226/WO-036's
    ActivityRecorder, which needed only a literal ActivitySource NAME STRING, never an actual type
    reference). **As of this Design-phase confirmation pass (2026-07-17), that blocker has cleared.**
    `08.Storage/SharedKernel.Storage.Abstractions` now ships real, compiled code: `IFileStorage`
    (nine members — Upload/Download/Delete/Exists/GetMetadata/Copy/DeleteMany/List/CheckHealth, read
    directly from `Abstractions/IFileStorage.cs`), `IBlobUriGenerator` (two members, read directly
    from `Abstractions/IBlobUriGenerator.cs`), all seven `Models/` records (`FileUploadRequest`,
    `FileReference`, `FileDownload`, `FileMetadata`, `FileDeleteOutcome`, `PresignedUrlRequest`,
    `PresignedUrl`), and the nine-factory-method `StorageErrors` class (`Errors/StorageErrors.cs`) —
    every one read directly from disk and matching the `InMemoryFileStorage`/`InMemoryBlobUriGenerator`
    target shape documented above with ZERO drift. `08.Storage`'s own `state-map.md` confirms
    `SK.08.Core` is `●` 30/30. `S3StorageOptions`'s `ServiceUrl`/`AccessKeyId`/`SecretAccessKey`/
    `ForcePathStyle`/`DefaultBucket` properties (read directly from `SharedKernel.Storage.S3/Options/S3StorageOptions.cs`)
    and `ObsStorageOptions`'s `Endpoint` (read directly from `SharedKernel.Storage.Obs/Options/ObsStorageOptions.cs`)
    likewise match `MinioContainerFixture`'s D-79 property-naming design exactly. `AWSSDK.S3` is
    confirmed pinned at `4.0.101.1` in `SharedKernel.Storage.S3.csproj` (matching the Technology
    Stack row's deferred-to-implementation-time confirmation instruction). `InMemoryFileStorage`/
    `InMemoryBlobUriGenerator` can now compile as `: IFileStorage`/`: IBlobUriGenerator` — Core-phase
    implementation (C-61–C-63, plus T-47/DO-18) is corrected from `⚑` Blocked back to `○` Pending in
    `state-map.md`; actually writing that code remains a future Core-phase implementer session, not
    performed in this Design-confirmation pass. See `state-map.md`'s Cross-Domain Dependencies table
    for the corrected `08.Storage` row (now `Available`).
```

### `Search/` — ISearchIndex<TDocument> / ISearchIndexProvisioner / ISearchProviderDescriptor in-memory doubles (09.Search) — added P-276/WO-044

```text
InMemorySearchIndex<TDocument>  (sealed class, implements ISearchIndex<TDocument> from SharedKernel.Search.Abstractions)
                                                                          [implemented P-276/WO-044, Core phase C-64–C-69, 2026-07-20]
    where TDocument : class, ISearchDocument
    constructor(SearchIndexDefinition definition)
        REQUIRED, never optional/nullable — the fail-loud field-role validation and the pagination
        ceiling both need a real declaration to validate against, exactly like a real adapter validates
        against the SAME registered definition before any I/O.
    .IndexName                                                 → string  (= definition.Name)

    — write —
    .IndexAsync(TDocument document, SearchWriteConsistency consistency, CancellationToken ct)
                                                                → Task<Result<SearchWriteReceipt>>
    .IndexManyAsync(IReadOnlyCollection<TDocument> documents, SearchWriteConsistency consistency,
                    CancellationToken ct)                       → Task<Result<SearchBulkReceipt>>
    .DeleteAsync(string documentId, SearchWriteConsistency consistency, CancellationToken ct)
                                                                → Task<Result<SearchWriteReceipt>>
    .DeleteManyAsync(IReadOnlyCollection<string> documentIds, SearchWriteConsistency consistency,
                     CancellationToken ct)                      → Task<Result<SearchBulkReceipt>>
    .DeleteByFilterAsync(SearchFilter filter, TenantScope tenantScope, SearchWriteConsistency consistency,
                         CancellationToken ct)                  → Task<Result<SearchWriteReceipt>>
    .ClearAsync(SearchWriteConsistency consistency, CancellationToken ct)
                                                                → Task<Result<SearchWriteReceipt>>
    .WaitUntilSearchableAsync(SearchWriteReceipt receipt, TimeSpan timeout, CancellationToken ct)
                                                                → Task<Result>

    — read —
    .SearchAsync(SearchRequest request, TenantScope tenantScope, CancellationToken ct)
                                                                → Task<Result<SearchResults<TDocument>>>
    .GetAsync(string documentId, TenantScope tenantScope, CancellationToken ct)
                                                                → Task<Result<TDocument>>
    .CountAsync(SearchFilter? filter, TenantScope tenantScope, CancellationToken ct)
                                                                → Task<Result<long>>

    — corpus walk —
    .EnumerateAsync(SearchFilter? filter, TenantScope tenantScope, int batchSize,
                    [EnumeratorCancellation] CancellationToken ct)  → IAsyncEnumerable<TDocument>

    — assertion / test-setup helpers —
    .IndexedDocumentIds                                        → IReadOnlyList<string>  (every DocumentId ever
                                                                   successfully passed to IndexAsync/IndexManyAsync,
                                                                   append-only, never pruned on delete)
    .DeletedDocumentIds                                        → IReadOnlyList<string>  (every DocumentId ever
                                                                   successfully deleted via DeleteAsync/DeleteManyAsync/
                                                                   DeleteByFilterAsync)
    .WasIndexed(string documentId)                             → bool
    .WasDeleted(string documentId)                             → bool
    .IsSearchable(string documentId)                           → bool  (true iff the document is CURRENTLY present in
                                                                   the backing store — every fake write is immediately
                                                                   searchable, so this doubles as "was this document
                                                                   indexed/deleted/searchable" per the phase's own
                                                                   acceptance criterion)
    .Seed(TDocument document)                                  → void  (test-setup helper — pre-populates the backing
                                                                   store WITHOUT going through IndexAsync, still enforcing
                                                                   the DocumentId charset rule since that is a
                                                                   document-contract invariant, not a write-path concern;
                                                                   mirrors Storage/InMemoryFileStorage.Seed)
    .SimulateFailure                                           → bool  (settable, default false — a SINGLE write-path
                                                                   toggle mirroring Storage/InMemoryFileStorage's
                                                                   precedent exactly; when true, every write-path member's
                                                                   outer call returns SearchErrors.WriteRejected instead of
                                                                   performing the operation. Read-path members and
                                                                   WaitUntilSearchableAsync are UNAFFECTED)
    .Reset()                                                   → void  (clears the backing store AND all recorded-history
                                                                   lists)

    NOTE (BACKING STORE): ConcurrentDictionary<string, TDocument> keyed by DocumentId.

    NOTE (WRITE PATH): upsert semantics only, no create-vs-update split, matching the real contract's
          own rule. IndexAsync/IndexManyAsync/Seed validate DocumentId against the A-Z a-z 0-9 - _
          charset rule BEFORE any state mutation, returning SearchErrors.InvalidDocumentId on
          violation — a document-CONTRACT-level rule enforced "on BOTH providers" per the real
          contract, so the fake enforces it too. SearchWriteReceipt.ProviderToken is a deterministic
          incrementing sequence (never a random Guid, mirroring Storage/InMemoryFileStorage's
          deterministic-ETag precedent); .AcceptedAt is a FIXED non-real baseline instant (never
          DateTimeOffset.UtcNow, no Clocks/FakeClock dependency, per sibling-isolation — independently
          declared, exactly like Storage/InMemoryFileStorage's own baseline). DeleteAsync/
          DeleteManyAsync are idempotent (an absent documentId still returns Result.Success with
          AffectedCount=0) — a fake-only convenience choice, since 09.Search's own CLAUDE.md does not
          yet document real single/bulk-delete idempotency as of this design pass; reconcile once
          09.Search's Core phase ships and documents real behavior. DeleteByFilterAsync takes
          TenantScope as a MANDATORY SEPARATE parameter (never folded into the filter tree) and injects
          it as the OUTERMOST AND clause. ClearAsync takes NO tenant/filter parameter and unconditionally
          empties the backing store. WaitUntilSearchableAsync returns Result.Success() deterministically
          with zero real delay, since every fake write is synchronously and immediately searchable — the
          fake's key simplification versus real provider write-to-searchable latency.

    NOTE (THE SHARED IN-MEMORY SearchFilter EVALUATOR): a private, reflection-based node evaluator over
          the closed 8-node AST (EqualFilter/NotEqualFilter/InFilter/RangeFilter/ExistsFilter/AndFilter/
          OrFilter/NotFilter), resolving SearchFilter.Field to a TDocument public property by name
          (case-insensitive), reused identically by DeleteByFilterAsync/SearchAsync/CountAsync/
          EnumerateAsync. Reflection use is explicitly sanctioned by this package's own existing
          Domain/SpecificationAssert precedent ("acceptable in this test-only package, never
          production"). A field name that fails to resolve to any TDocument property is a CALLING-TEST
          programming error and throws InvalidOperationException — distinct from the
          SearchErrors.FieldNot* Result-failure path below, which is checked earlier against the
          REGISTERED DEFINITION, so a well-formed definition never reaches this exception path.
          KNOWN COSMETIC WARNING (confirmed at Core-phase implementation time, 2026-07-20): the
          switch expression over the 8 SearchFilter subtypes carries no discard arm, mirroring
          09.Search's own MeilisearchFilterCompiler/ElasticSearchFilterCompiler translation-switch
          shape exactly per the interface's own "no discard arm" convention — this produces a CS8509
          "switch not exhaustive" warning under Roslyn. Confirmed NOT unique to this fake: a clean
          rebuild of SharedKernel.Search.Meilisearch reproduces the identical CS8509 on
          MeilisearchFilterCompiler.Compile, normally hidden by incremental-build caching. This is a
          pre-existing, unaddressed characteristic of the shipped 09.Search codebase, out of scope to
          fix from 16.Testing; TreatWarningsAsErrors is not actually wired into any csproj in this
          repo today, so it does not fail either domain's build.

    NOTE (SearchAsync's FAIL-LOUD VALIDATION PIPELINE, run BEFORE any in-memory read, in this order):
          (1) Sort fields against definition.Fields[].Sortable → SearchErrors.FieldNotSortable;
          (2) Filter fields against .Filterable → SearchErrors.FieldNotFilterable;
          (3) Facets/NumericFacetStats fields against .Facetable → SearchErrors.FieldNotFacetable;
          (4) pagination ceiling Page * PageSize <= definition.MaxTotalHits →
              SearchErrors.PaginationLimitExceeded;
          (5) definition.TenantField set AND tenantScope == TenantScope.None →
              SearchErrors.TenantScopeMissing.
          Matches the real contract's own "FAIL LOUD, NEVER DEGRADE... BEFORE any I/O" rule exactly,
          substituting "any in-memory read" for "any I/O". Reused verbatim by
          InMemorySearchProviderDescriptor.Validate as its own zero-I/O pre-flight logic.

    NOTE (SearchAsync's RESULT ASSEMBLY, runs only after the pipeline above passes): filter+tenant
          evaluation via the shared evaluator (tenant clause injected as the outermost AND); FreeText
          matching as a case-insensitive SUBSTRING match across Text-kind Searchable fields only — an
          explicitly documented SIMPLIFICATION, no relevance ranking, no MatchAllTerms term-splitting
          fidelity, since faithful BM25/Meilisearch-ranking-rule parity is out of scope for an
          in-memory fake; stable multi-key Sort; Page/PageSize paging; Facets computed by in-memory
          group-by (and min/max for NumericFacetStats) over the FULL filtered set pre-paging, truncated
          at definition.MaxFacetValues with Truncated=true when hit; TotalHits ALWAYS exact
          (Accuracy = TotalHitsAccuracy.Exact unconditionally — a deliberate, explicitly documented
          divergence from BOTH real providers, which default to estimate/lower-bound); Duration =
          TimeSpan.Zero.

    NOTE (GetAsync IS TENANT-CHECKED): CORRECTED at Core-phase implementation time (2026-07-20) — the
          shipped implementation is a raw ConcurrentDictionary.TryGetValue lookup by DocumentId,
          followed by a tenant-field comparison against the fetched document, rather than literally
          routing the id through the shared SearchFilter evaluator; this is functionally equivalent
          for the fake's purposes (a tenant mismatch — including TenantScope.None on a tenanted
          index — folds into SearchErrors.DocumentNotFound identically to a genuinely missing id,
          never a cross-tenant leak, never a thrown exception) while being simpler than building an
          ad hoc SearchFilter node keyed on a definition's PrimaryKeyField. GetAsync deliberately
          does NOT get the fail-closed TenantScopeMissing upfront check that CountAsync/
          DeleteByFilterAsync/EnumerateAsync/SearchAsync all share (below) — confirmed against the
          real MeilisearchIndex<TDocument>.GetAsync, which also treats GetAsync as a special case
          with its own simpler tenant-mismatch-only handling, distinct from the other four operations'
          shared CompileWithTenantScope-equivalent fail-closed path.

    NOTE (CountAsync/DeleteByFilterAsync/EnumerateAsync ARE TENANT-SCOPE-FAIL-CLOSED): an
          IMPLEMENTER JUDGMENT CALL made at Core-phase implementation time, extending beyond D-108's
          literal text (which scopes the 5-step fail-loud pipeline to SearchAsync only) — confirmed by
          reading MeilisearchIndex<TDocument>/the ElasticSearch equivalent directly: both real
          adapters route CountAsync/DeleteByFilterAsync/EnumerateAsync/SearchAsync through the SAME
          shared CompileWithTenantScope-equivalent helper, which fails closed with
          SearchErrors.TenantScopeMissing whenever definition.TenantField is set and the caller passes
          TenantScope.None — before any I/O, before any filter evaluation. The fake mirrors this for
          all three: CountAsync/DeleteByFilterAsync return a Result.Failure; EnumerateAsync throws
          SearchStreamException (its own not-Result-wrapped contract) carrying the same error.

    NOTE (CountAsync IS ALWAYS EXACT, zero divergence): reuses the same filter+tenant evaluation as
          SearchAsync; the fake has no I/O-cost difference between exact and estimated, so it matches
          the real contract's own "exact on both engines" note exactly — unlike SearchAsync.TotalHits,
          which diverges deliberately.

    NOTE (EnumerateAsync): a REAL async iterator (lazy yield, per-item
          ct.ThrowIfCancellationRequested()), mirroring Storage/InMemoryFileStorage.ListAsync's
          already-established pattern; NOT Result-wrapped — the one documented exception in the domain,
          matching both the real contract's own streaming precedent (06.Persistence P-149 / 08.Storage
          P-265) and Storage/InMemoryFileStorage.ListAsync; iteration order UNSPECIFIED-BUT-STABLE
          within an unmodified corpus, matching the real contract's own "ordering MUST NOT be relied
          upon" note; batchSize accepted for signature parity only.

InMemorySearchIndexProvisioner  (sealed class, implements ISearchIndexProvisioner from SharedKernel.Search.Abstractions)
                                                                          [implemented P-276/WO-044, Core phase C-64–C-69, 2026-07-20]
    .EnsureIndexAsync(SearchIndexDefinition definition, CancellationToken ct)      → Task<Result>
    .IndexExistsAsync(string indexName, CancellationToken ct)                     → Task<Result<bool>>
    .DeleteIndexAsync(string indexName, CancellationToken ct)                     → Task<Result>
    .CutoverAsync(IndexCutoverRequest request, CancellationToken ct)              → Task<Result>
    .ProbeAsync(string indexName, CancellationToken ct)                          → Task<Result<SearchIndexHealth>>
    .SimulateFailure                                           → bool  (settable, default false — write-path toggle
                                                                   mirroring InMemorySearchIndex's precedent:
                                                                   EnsureIndexAsync/DeleteIndexAsync/CutoverAsync only;
                                                                   IndexExistsAsync/ProbeAsync unaffected)
    .RegisteredIndexNames                                      → IReadOnlyList<string>  (test-introspection helper)
    .Reset()                                                   → void
    NOTE: Non-generic; tracks its OWN ConcurrentDictionary<string, SearchIndexDefinition> of registered
          index names, independent of any InMemorySearchIndex<TDocument> instance (see the SCOPE LOCK /
          non-coupling note below). EnsureIndexAsync is idempotent and ADDITIVE-ONLY (merges new Fields
          into an existing registration by Name, never drops one — matches the real contract's own
          "NEVER drops a field" rule; a same-Name field re-declared with a conflicting role returns
          SearchErrors.IndexDefinitionConflict). DeleteIndexAsync is idempotent (an absent name still
          returns Result.Success, mirroring Storage/InMemoryFileStorage.DeleteAsync). CutoverAsync
          requires request.StagingIndexName to be currently registered (else
          SearchErrors.CutoverFailed(request.StagingIndexName, request.LiveIndexName, "staging index
          not registered") — using the real contract's OWN dedicated cutover-failure factory, never a
          generic not-found); on success, reassigns request.LiveIndexName's registration to the staging
          definition and honors request.DeleteStagingAfterCutover (default true). ProbeAsync returns
          SearchErrors.IndexNotFound for an unregistered name; for a registered one, a deterministic
          ALWAYS-healthy SearchIndexHealth (Reachable=true, IndexAddressable=true, Searchable=true,
          DocumentCount=0 — this fake tracks no document store, see the non-coupling note —
          PendingWriteCount=0, EngineVersion="in-memory-fake", SchemaFingerprint=the registered
          definition's .Fingerprint, Latency=TimeSpan.Zero).

InMemorySearchProviderDescriptor  (sealed class, implements ISearchProviderDescriptor from SharedKernel.Search.Abstractions)
                                                                          [implemented P-276/WO-044, Core phase C-64–C-69, 2026-07-20]
    constructor(string providerName = "in-memory-fake")
        Defaults .ProviderName to a value deliberately NEITHER SearchWellKnown.MeilisearchProviderName
        NOR .ElasticSearchProviderName, so a consumer's own provider-name branching/logging code cannot
        mistake the fake for a real engine.
    .ProviderName                                              → string  (settable)
    .MaxTotalHits                                              → int  (settable, default SearchWellKnown.DefaultMaxTotalHits = 1000)
    .MaxFacetValues                                            → int  (settable, default SearchWellKnown.DefaultMaxFacetValues = 100)
    .RegisteredIndexes                                         → IReadOnlyList<string>
    .RegisterIndex(string indexName, SearchIndexDefinition definition)            → void
        Test-setup helper — populates BOTH .RegisteredIndexes and the field-role knowledge .Validate
        needs. This descriptor keeps its OWN independent SearchIndexDefinition map — it is NOT read
        through InMemorySearchIndexProvisioner (see the non-coupling note below).
    .Validate(string indexName, SearchRequest request)         → Result
        Runs the EXACT SAME zero-I/O pre-flight pipeline as InMemorySearchIndex<TDocument>.SearchAsync's
        fail-loud validation step (field-role checks, then pagination ceiling), against a registered
        definition — returns SearchErrors.IndexNotFound for an index name never passed to .RegisterIndex.
    .Reset()                                                   → void

SCOPE LOCK (P-276/WO-044): Search/ references only SharedKernel.Search.Abstractions — never
    SharedKernel.Search.Meilisearch or .ElasticSearch (the concrete provider packages), and never any
    sibling capability folder in this package (Caching/, Messaging/, Persistence/, Application/, Logging/,
    Storage/, Containers/, etc.) — in particular, never Containers/MeilisearchContainerFixture or
    .ElasticsearchContainerFixture (P-275). The in-memory fakes (fast, isolated, no Docker) and the
    real-provider-integration fixtures (Docker, exercise the actual provider SDKs) are deliberately
    independent test paths serving different audiences, mirroring the existing Storage/-vs-
    Containers/MinioContainerFixture split exactly.

    DELIBERATE NON-COUPLING BETWEEN THE THREE Search/ FAKES (unique to this folder — every other
    multi-type folder in this package still lets its types compose freely with each other): unlike a
    real provider package (where ISearchIndex<TDocument>, ISearchIndexProvisioner, and
    ISearchProviderDescriptor for the SAME provider share internal state under one DI-root
    registration), InMemorySearchIndex<TDocument>, InMemorySearchIndexProvisioner, and
    InMemorySearchProviderDescriptor are three fully INDEPENDENT sealed fakes with no constructor or
    type dependency on each other. Rationale: InMemorySearchIndex<TDocument> is generic per document
    type and by far the most commonly needed fake (a consumer testing a query handler that depends on
    ISearchIndex<TDocument> alone); ISearchIndexProvisioner/ISearchProviderDescriptor are non-generic
    and needed only by a consumer specifically testing rebuild-orchestration or startup
    pre-flight-validation logic. Forcing all three to share one backing registry would add ceremony
    most InMemorySearchIndex<TDocument>-only tests never touch, and would not even achieve full
    real-provider fidelity, since RegisteredIndexes/Validate/ProbeAsync.DocumentCount are
    DEFINITION-derived on the real contract too, never live-TDocument-document-store-derived (except
    DocumentCount, which this fake fixes at 0 as a documented simplification). A consuming test that
    wants two of these fakes to agree on one SearchIndexDefinition passes the SAME definition instance
    to each explicitly — never relies on implicit cross-fake state sharing.

AddInMemorySearchIndex<TDocument>(this IServiceCollection, SearchIndexDefinition definition)
    where TDocument : class, ISearchDocument                                      → IServiceCollection
                                                                          [implemented P-276/WO-044, Core phase C-64–C-69, 2026-07-20]
    NOTE: Registers InMemorySearchIndex<TDocument> as ISearchIndex<TDocument>, SINGLETON — a
          DELIBERATE deviation from the real production SCOPED lifetime (09.Search's own
          AddIndex<TDocument> registers scoped ISearchIndex<TDocument>), mirroring
          InMemoryMessageBus/InMemoryEventPublisher's own already-documented scoped-to-singleton
          deviation for the identical reason: the same recorded-history instance must outlive the
          system-under-test's DI scope so post-hoc assertions can run after the action completes.
          Call once per TDocument the test needs indexed.

AddInMemorySearchProvisioning(this IServiceCollection, string providerName = "in-memory-fake")
                                                                                    → IServiceCollection
                                                                          [implemented P-276/WO-044, Core phase C-64–C-69, 2026-07-20]
    NOTE: Registers InMemorySearchIndexProvisioner as ISearchIndexProvisioner AND
          InMemorySearchProviderDescriptor (constructed with providerName) as ISearchProviderDescriptor,
          BOTH singleton — matches BOTH interfaces' real production lifetime exactly (both are
          root/singleton-shaped in production already), so — unlike AddInMemorySearchIndex<TDocument>
          above — there is no lifetime deviation to flag here. One call bundles both non-generic fakes,
          mirroring AddFakeCachingServices()'s one-call-bundles-related-fakes pattern; the two
          registered instances remain independent per the non-coupling note above.

BLOCKER-CLEARANCE VERIFICATION (re-verified directly on disk, not assumed from 09.Search/CLAUDE.md
    prose alone — this domain's own established rule, first applied at P-269/WO-043): at the original
    WO-044 dispatch pass, 09.Search/SharedKernel.Search.Abstractions/SharedKernel.Search.Abstractions.csproj
    was a genuinely empty placeholder — bare TargetFramework/ImplicitUsings/Nullable only, zero
    references, zero .cs content — and the whole 09.Search domain was SK.09.Design ○, not yet ●. This
    was the HARD design-ahead-of-schedule case, not the soft one (contrast P-226/WO-036's
    ActivityRecorder, which needed only a literal string, never a real upstream type). **As of this
    Design-phase confirmation pass (2026-07-19, same calendar day as dispatch), that blocker has
    cleared.** 09.Search/SharedKernel.Search.Abstractions now ships real, compiled code: ISearchIndex
    <TDocument> (12 members — IndexName plus IndexAsync/IndexManyAsync/DeleteAsync/DeleteManyAsync/
    DeleteByFilterAsync/ClearAsync/WaitUntilSearchableAsync/SearchAsync/GetAsync/CountAsync/
    EnumerateAsync, read directly from Abstractions/ISearchIndex.cs), ISearchIndexProvisioner (5
    members, read directly from Abstractions/ISearchIndexProvisioner.cs), ISearchProviderDescriptor (4
    members + Validate, read directly from Abstractions/ISearchProviderDescriptor.cs), and every
    Models/Errors/Constants type this target shape depends on (SearchIndexDefinition,
    SearchFieldDefinition, SearchFieldKind, SearchWriteReceipt, SearchBulkReceipt, SearchItemFailure,
    SearchRequest, SearchResults<TDocument>, SearchHit<TDocument>, TotalHitsAccuracy, TenantScope,
    SearchWriteConsistency, IndexCutoverRequest, SearchIndexHealth, the closed 8-node SearchFilter AST
    and its node types, SearchValue, SearchWellKnown, SearchErrors) — every one read directly from disk
    and matching the InMemorySearchIndex/InMemorySearchIndexProvisioner/InMemorySearchProviderDescriptor
    target shape documented above with ZERO drift. 09.Search's own state-map.md confirms
    SharedKernel.Search.Abstractions/.Meilisearch/.ElasticSearch are all Core phase ●, and its own
    Blocked section confirms the dependency direction is now the OPPOSITE of what this note originally
    recorded — 09.Search's own SK.09.Tests T-13–T-17/T-21–T-26 are ⚑ Blocked waiting on THIS package's
    MeilisearchContainerFixture/ElasticsearchContainerFixture (P-275), not the reverse. MeilisearchOptions
    (Url/ApiKey) and ElasticSearchOptions (Nodes/Username/Password/AllowInvalidCertificates), read
    directly from SharedKernel.Search.Meilisearch/Options/MeilisearchOptions.cs and
    SharedKernel.Search.ElasticSearch/Options/ElasticSearchOptions.cs, likewise match the Containers/
    fixture design exactly. InMemorySearchIndex/InMemorySearchIndexProvisioner/
    InMemorySearchProviderDescriptor can now compile as : ISearchIndex<TDocument>/
    : ISearchIndexProvisioner/: ISearchProviderDescriptor — Core-phase implementation (C-66–C-69, plus
    T-50/T-51/DO-21/DO-22) is corrected from ⚑ Blocked back to ○ Pending in state-map.md; actually
    writing that code remains a future Core-phase implementer session, not performed in this
    Design-confirmation pass. See state-map.md's Cross-Domain Dependencies table for the corrected
    09.Search row (now Available).
    **UPDATE (Core-phase implementer session, 2026-07-20): C-64–C-69 are now `●` — the sentence
    immediately above is stale, retained verbatim for history per this domain's "annotate, never
    silently rewrite" convention. All five Search/ types plus both Containers/ fixtures now exist on
    disk, compile clean, and were verified via real Docker (Containers/) and a 65-assertion functional
    smoke harness (Search/). Only T-50/T-51 (Tests) and DO-21/DO-22 (Docs) remain to fully close
    WO-044.**
```

### `Intelligence/` — embedding / vector-collection / orchestration in-memory doubles (10.Intelligence)

> **RESOLVED 2026-07-22 (was a HARD BLOCKER, THIRD occurrence of this pattern after P-269/WO-043 and P-276/WO-044):** every type below is `: IEmbeddingGenerator` / `: IVectorCollection<TRecord>` / `: IVectorCollectionProvisioner` / `: IVectorProviderDescriptor` / `: ISemanticKernel` / `: ICompletionProviderDescriptor` against real compiled types in `SharedKernel.AI.Abstractions` (10.Intelligence). Re-verified directly on disk 2026-07-22 immediately before implementation: `10.Intelligence/SharedKernel.AI.Abstractions/` now ships 66 real `.cs` files across `Abstractions/`, `Models/`, `Constants/`, `Errors/`, `Exceptions/`; all six target interfaces and every supporting model/error/constant type were re-read directly from source, confirming **zero drift** from the target shape below (D-128–D-138, sourced from the live `10.Intelligence/CLAUDE.md` during the original Design pass). Core (C-73–C-79) is implemented and Tests (T-54)/Docs (DO-25) are unblocked in `state-map.md`.

```text
InMemoryEmbeddingGenerator  (sealed class, implements IEmbeddingGenerator from SharedKernel.AI.Abstractions)
    constructor(string modelId, int dimension)
        REQUIRED — ModelId/Dimension are zero-I/O properties bound at construction per the real
        contract's own note (cheap enough to validate a VectorCollectionDefinition pairing before any
        embedding call), mirroring InMemorySearchIndex<TDocument>'s "constructor requires a real
        declaration" pattern.
    .ModelId                                                   → string
    .Dimension                                                  → int
    .EmbedAsync(string text, CancellationToken ct)              → Task<Result<EmbeddingResult>>
    .EmbedManyAsync(IReadOnlyList<string> texts, CancellationToken ct) → Task<Result<EmbeddingBatchResult>>
    .EmbeddedTexts                                              → IReadOnlyList<string>  (every text ever
                                                                   SUCCESSFULLY embedded — single- and
                                                                   batch-call texts both append, in call order)
    .SimulateFailure                                            → bool  (settable; when true, EmbedAsync/
                                                                   EmbedManyAsync return
                                                                   Result.Failure(IntelligenceErrors.EngineFault(...))
                                                                   instead of embedding)
    .Reset()                                                    → void  (clears EmbeddedTexts)
    NOTE (DETERMINISTIC HASH-DERIVED VECTOR — THE PHASE'S OWN HARD ACCEPTANCE CRITERION): each embedding
          is SHA-256(UTF8 bytes of ModelId + '' + text), expanded deterministically into Dimension
          float components via a seeded (never time-based, never System.Random with an implicit seed,
          never Guid.NewGuid()) PRNG keyed off the hash bytes — the SAME (ModelId, text) pair always
          yields the byte-identical vector, across processes and machines. TokenUsage.PromptTokens =
          whitespace-split word count of text; .CompletionTokens = 0 ALWAYS (embedding calls have no
          completion half, per the real contract's own TokenUsage note); .TotalTokens = PromptTokens.
    NOTE (EmbedManyAsync): Embeddings[i] corresponds to texts[i] via the identical per-text algorithm;
          no per-item failure surface (matches EmbeddingBatchResult's own "atomic per request" design).

InMemoryVectorCollection<TRecord>  (sealed class, implements IVectorCollection<TRecord> from
                                    SharedKernel.AI.Abstractions)  where TRecord : class, IVectorRecord
    constructor(VectorCollectionDefinition definition)
        REQUIRED, never optional/nullable — the fail-loud model-identity/dimension/tenant-scope checks
        all need a real declaration to validate against, exactly like InMemorySearchIndex<TDocument>'s
        constructor requires a SearchIndexDefinition.
    .CollectionName                                             → string  (= definition.Name)

    — write —
    .UpsertAsync(TRecord record, TenantScope tenantScope, CancellationToken ct)
                                                                 → Task<Result<VectorWriteReceipt>>
    .UpsertManyAsync(IReadOnlyCollection<TRecord> records, TenantScope tenantScope, CancellationToken ct)
                                                                 → Task<Result<VectorBulkReceipt>>
    .DeleteAsync(string id, TenantScope tenantScope, CancellationToken ct)
                                                                 → Task<Result<VectorWriteReceipt>>
    .DeleteManyAsync(IReadOnlyCollection<string> ids, TenantScope tenantScope, CancellationToken ct)
                                                                 → Task<Result<VectorBulkReceipt>>
    .DeleteByFilterAsync(VectorFilter filter, TenantScope tenantScope, CancellationToken ct)
                                                                 → Task<Result<VectorWriteReceipt>>
    .WaitUntilQueryableAsync(VectorWriteReceipt receipt, TimeSpan timeout, CancellationToken ct)
                                                                 → Task<Result>

    — read —
    .QueryAsync(VectorQuery query, TenantScope tenantScope, CancellationToken ct)
                                                                 → Task<Result<VectorQueryResults<TRecord>>>
    .GetAsync(string id, TenantScope tenantScope, CancellationToken ct)
                                                                 → Task<Result<TRecord>>
    .CountAsync(VectorFilter? filter, TenantScope tenantScope, CancellationToken ct)
                                                                 → Task<Result<long>>

    — corpus walk —
    .ScrollAsync(VectorFilter? filter, TenantScope tenantScope, int batchSize,
                 [EnumeratorCancellation] CancellationToken ct)  → IAsyncEnumerable<TRecord>

    — assertion / test-setup helpers —
    .UpsertedIds                                                → IReadOnlyList<string>  (every Id ever
                                                                   SUCCESSFULLY upserted, append-only,
                                                                   never pruned on delete)
    .DeletedIds                                                 → IReadOnlyList<string>  (every Id ever
                                                                   SUCCESSFULLY deleted)
    .QueriedVectors                                             → IReadOnlyList<VectorQuery>  (every
                                                                   VectorQuery that passed the fail-loud
                                                                   pre-flight pipeline and executed against
                                                                   the store — the assertion helper
                                                                   satisfying the phase's own "was this
                                                                   vector queried" acceptance criterion; a
                                                                   REJECTED query is never recorded here)
    .WasUpserted(string id)                                     → bool
    .WasDeleted(string id)                                      → bool
    .IsQueryable(string id)                                     → bool  (true iff currently present in the
                                                                   backing store — every fake write is
                                                                   immediately queryable)
    .Seed(TRecord record)                                       → void  (test-setup helper — pre-populates
                                                                   the backing store WITHOUT going through
                                                                   UpsertAsync, still enforcing the Id
                                                                   non-null/non-whitespace rule)
    .SimulateFailure                                            → bool  (settable, default false — a SINGLE
                                                                   write-path toggle mirroring
                                                                   Storage/InMemoryFileStorage's and
                                                                   Search/InMemorySearchIndex<TDocument>'s
                                                                   precedent exactly; when true, every
                                                                   write-path member's outer call returns
                                                                   IntelligenceErrors.WriteRejected instead
                                                                   of performing the operation. Read-path
                                                                   members and WaitUntilQueryableAsync are
                                                                   UNAFFECTED)
    .Reset()                                                    → void  (clears the backing store AND all
                                                                   recorded-history lists)

    NOTE (BACKING STORE): ConcurrentDictionary<string, TRecord> keyed by Id.

    NOTE (WRITE-PATH FAIL-LOUD PRE-FLIGHT PIPELINE, run BEFORE any store mutation, per record, in this
          order): (1) Id non-null/non-whitespace → else IntelligenceErrors.InvalidRecordId(record.Id);
          (2) record.ModelId == definition.EmbeddingModelId → else
          IntelligenceErrors.EmbeddingModelMismatch(definition.Name, definition.EmbeddingModelId,
          record.ModelId); (3) record.Vector.Length == definition.Dimension → else
          IntelligenceErrors.DimensionMismatch(definition.Name, definition.Dimension,
          record.Vector.Length). Upsert-only semantics, no Create-vs-Update split, matching the real
          contract. VectorWriteReceipt.ProviderToken is a DETERMINISTIC incrementing sequence (never a
          random Guid, mirroring Storage/Search's precedent); .AcceptedAt is a FIXED non-real baseline
          instant (never DateTimeOffset.UtcNow, NO Clocks/FakeClock dependency per sibling-isolation —
          independently declared, exactly like Storage/Search's own baseline). DeleteAsync/
          DeleteManyAsync are idempotent (an absent id still returns Result.Success, AffectedCount=0) —
          a fake-only convenience choice pending 10.Intelligence's own Core phase documenting real
          single/bulk-delete idempotency, mirroring Search/'s identical open item. DeleteByFilterAsync
          takes TenantScope as a MANDATORY SEPARATE parameter (never folded into the filter tree),
          injected as the OUTERMOST AND clause. WaitUntilQueryableAsync returns Result.Success()
          deterministically with zero real delay, since every fake write is synchronously and
          immediately queryable — the fake's key simplification versus real provider write-to-queryable
          latency, mirroring Search/'s WaitUntilSearchableAsync fake simplification exactly.

    NOTE (READ-PATH FAIL-LOUD PRE-FLIGHT PIPELINE, run BEFORE any in-memory read, on QueryAsync, in this
          order): (1) query.ModelId == definition.EmbeddingModelId → else EmbeddingModelMismatch;
          (2) query.Vector.Length == definition.Dimension → else DimensionMismatch;
          (3) definition.TenantField set AND tenantScope == TenantScope.None → else
          IntelligenceErrors.TenantScopeMissing. CountAsync/DeleteByFilterAsync/ScrollAsync are likewise
          TENANT-SCOPE-FAIL-CLOSED (an implementer judgment call extending beyond the literal per-member
          design text, mirroring InMemorySearchIndex<TDocument>'s identical Core-phase extension of its
          own fail-loud pipeline to CountAsync/DeleteByFilterAsync/EnumerateAsync — to be reconciled
          once 10.Intelligence's own Qdrant/Milvus adapters ship and document which operations their own
          shared tenant-guard helper actually covers). ScrollAsync throws IntelligenceStreamException
          IMMEDIATELY (before yielding anything) carrying TenantScopeMissing when the check fails — its
          own not-Result-wrapped contract's failure path (Domain Invariant #7), mirroring
          Search/EnumerateAsync's identical SearchStreamException precedent. GetAsync deliberately does
          NOT get this upfront check — see below.

    NOTE (GetAsync IS TENANT-CHECKED, NOT TENANT-SCOPE-FAIL-CLOSED — mirrors InMemorySearchIndex<TDocument>
          .GetAsync's identical Core-phase judgment call): a raw ConcurrentDictionary.TryGetValue lookup
          by Id, followed by a tenant-field comparison against the fetched record's Metadata (if
          definition.TenantField is declared) — a tenant mismatch (INCLUDING TenantScope.None on a
          tenant-declaring collection) folds into IntelligenceErrors.RecordNotFound identically to a
          genuinely missing id, never a cross-tenant leak, never a thrown exception, never a separate
          TenantScopeMissing branch. Flagged explicitly as a mirrored precedent, to be reconciled once
          10.Intelligence's own Qdrant/Milvus adapters ship and document real GetAsync tenant-handling
          behavior (the real contract's own D-05 note only says "tenant-checked via a filtered lookup,"
          without distinguishing the TenantScope.None sub-case explicitly).

    NOTE (CountAsync IS ALWAYS EXACT, zero divergence): matches the real contract's own "exact on both
          engines" note exactly, unlike Search/'s TotalHitsAccuracy split — the fake has no I/O-cost
          difference between exact and estimated either way.

    NOTE (THE SHARED IN-MEMORY VectorFilter EVALUATOR — A DELIBERATE SIMPLIFICATION RELATIVE TO Search/'s
          REFLECTION-BASED EVALUATOR): VectorFilter.Field resolves via a DIRECT DICTIONARY LOOKUP into
          record.Metadata[Field] (a VectorValue) — NO REFLECTION NEEDED AT ALL, because IVectorRecord
          .Metadata is ALREADY IReadOnlyDictionary<string, VectorValue> keyed by field name (unlike
          ISearchDocument, which exposes typed C# properties requiring Search/'s reflection-based
          property resolution). A Field absent from record.Metadata is a non-match for
          Equal/NotEqual/In/Range and false for Exists. Reused identically by
          DeleteByFilterAsync/QueryAsync/CountAsync/ScrollAsync. The switch expression over the 8
          VectorFilter subtypes carries NO discard arm, mirroring the real contract's own "neither
          translation switch may carry a discard arm" hard rule and Search/'s identical AST shape —
          anticipate the same cosmetic CS8509 "not exhaustive" warning already confirmed pre-existing
          and harmless for Search/'s identical closed-AST switch shape, not a defect unique to this fake.

    NOTE (VectorHit.Score/.Rank COMPUTATION — A DELIBERATE FIDELITY IMPROVEMENT OVER THE Search/
          PRECEDENT): unlike full-text BM25/ranking-rule relevance (too complex to faithfully replicate
          in-memory — Search/'s own documented out-of-scope simplification), vector similarity is
          CLOSED-FORM ARITHMETIC this fake computes FAITHFULLY per definition.DistanceMetric: Cosine =
          dot(a,b)/(‖a‖·‖b‖); DotProduct = dot(a,b) directly; Euclidean = √Σ(aᵢ−bᵢ)² — a DISTANCE, smaller
          is better, the OPPOSITE direction of the other two, exactly matching VectorHit.Score's own
          loud documented warning on the real contract. Rank (0-based) is assigned after sorting by the
          metric's own correct directionality (descending for Cosine/DotProduct, ascending for
          Euclidean). VectorQuery.MinScore filtering is applied POST-scoring respecting that same
          directionality (Score >= MinScore for Cosine/DotProduct; Score <= MinScore for Euclidean).
          .Limit caps the returned Hits count (default IntelligenceWellKnown.DefaultQueryLimit).
          ReturnMetadata/ReturnVector are ACCEPTED but are DOCUMENTED NO-OPS — the fake always returns
          the full stored TRecord instance regardless of either flag, since a generic fake has no way to
          construct a partial TRecord without knowing the caller's concrete type's shape (a real
          adapter's own deserialization path can honor these; this simplification is recorded
          explicitly, mirroring Search/'s "documented simplification, never silently claimed as full
          fidelity" convention). Both query.Vector.Length and every stored record.Vector.Length are
          already guaranteed == definition.Dimension by the write/read pre-flight pipelines above, so no
          runtime shape mismatch is possible during scoring.

InMemoryVectorCollectionProvisioner  (sealed class, implements IVectorCollectionProvisioner from
                                      SharedKernel.AI.Abstractions)
    .EnsureCollectionAsync(VectorCollectionDefinition definition, CancellationToken ct)  → Task<Result>
    .CollectionExistsAsync(string collectionName, CancellationToken ct)                 → Task<Result<bool>>
    .DeleteCollectionAsync(string collectionName, CancellationToken ct)                 → Task<Result>
    .CutoverAsync(VectorCollectionCutoverRequest request, CancellationToken ct)          → Task<Result>
    .ProbeAsync(string collectionName, CancellationToken ct)              → Task<Result<VectorCollectionHealth>>
    .SimulateFailure                                            → bool  (settable, default false — write-path
                                                                   toggle: EnsureCollectionAsync/
                                                                   DeleteCollectionAsync/CutoverAsync only;
                                                                   CollectionExistsAsync/ProbeAsync unaffected)
    .RegisteredCollectionNames                                  → IReadOnlyList<string>  (test-introspection helper)
    .Reset()                                                    → void
    NOTE: Non-generic; tracks its OWN ConcurrentDictionary<string, VectorCollectionDefinition> of
          registered collection names, independent of any InMemoryVectorCollection<TRecord> instance
          (see the SCOPE LOCK / non-coupling note below). EnsureCollectionAsync is idempotent and
          ADDITIVE-ONLY (merges new Fields into an existing registration by Name, never drops one; a
          same-Name field re-declared with a conflicting Kind/Filterable returns
          IntelligenceErrors.CollectionDefinitionConflict). DeleteCollectionAsync is idempotent (an
          absent name still returns Result.Success). CutoverAsync requires
          request.StagingCollectionName to be currently registered (else
          IntelligenceErrors.CutoverFailed(staging, live, "staging collection not registered")); on
          success, reassigns request.LiveCollectionName's registration to the staging definition and
          honors request.DeleteStagingAfterCutover (default true). ProbeAsync returns
          IntelligenceErrors.CollectionNotFound for an unregistered name; for a registered one, a
          deterministic ALWAYS-healthy VectorCollectionHealth (Reachable=true, CollectionAddressable=true,
          Queryable=true, VectorCount=0 — this fake tracks no document store, see the non-coupling note
          — PendingWriteCount=0 — a FIXED zero, NOT null, a documented divergence from the real
          contract's "permanently nullable" honesty rule, since this fake models no real optimizer
          backlog to report against at all — EngineVersion="in-memory-fake", SchemaFingerprint=the
          registered definition's own .Fingerprint, Latency=TimeSpan.Zero).

InMemoryVectorProviderDescriptor  (sealed class, implements IVectorProviderDescriptor from
                                   SharedKernel.AI.Abstractions)
    constructor(string providerName = "in-memory-fake")
        Defaults .ProviderName to a value deliberately NEITHER IntelligenceWellKnown.QdrantProviderName
        NOR .MilvusProviderName, so a consumer's own provider-name branching/logging code cannot
        mistake the fake for a real engine (mirrors InMemorySearchProviderDescriptor's precedent).
    .ProviderName                                               → string  (settable)
    .MaxBatchSize                                               → int  (settable, default 1000)
    .MaxVectorDimension                                          → int  (settable, default 4096)
    .MaxFilterDepth                                              → int  (settable, default 10)
    .RegisteredCollections                                       → IReadOnlyList<string>
    .RegisterCollection(string collectionName, VectorCollectionDefinition definition) → void
        Test-setup helper — populates BOTH .RegisteredCollections and the definition knowledge
        .Validate needs. This descriptor keeps its OWN independent VectorCollectionDefinition map — it
        is NOT read through InMemoryVectorCollectionProvisioner (non-coupling note below).
    .Validate(string collectionName, VectorQuery query)         → Result
        Zero-I/O pre-flight: MaxFilterDepth against query.Filter's tree depth, MaxVectorDimension
        against query.Vector.Length, structural invariants — mirrors the real contract's own Validate
        exactly. Returns IntelligenceErrors.CollectionNotFound for a name never passed to
        .RegisterCollection.
    .Reset()                                                    → void

InMemorySemanticKernel  (sealed class, implements ISemanticKernel from SharedKernel.AI.Abstractions)
    constructor(string providerName = "in-memory-fake")
    .CompleteAsync(CompletionRequest request, CancellationToken ct)         → Task<Result<CompletionResult>>
    .CompleteStreamingAsync(CompletionRequest request, [EnumeratorCancellation] CancellationToken ct)
                                                                             → IAsyncEnumerable<CompletionChunk>
    .EnqueueResponse(CompletionResult result)                               → void
        Test-setup helper — appends a caller-supplied CANNED CompletionResult to an internal FIFO queue.
    .EnqueueStreamingResponse(IReadOnlyList<CompletionChunk> chunks)        → void
        Test-setup helper — appends a caller-supplied canned CHUNK SEQUENCE to a second FIFO queue.
    .EnqueueStreamingFailure(Error error)                                  → void
        Test-setup helper — the NEXT CompleteStreamingAsync call throws IntelligenceStreamException
        carrying the given Error partway through enumeration (after any chunks already queued ahead of
        it, or immediately if none).
    .SentRequests                                                          → IReadOnlyList<CompletionRequest>
        Every CompletionRequest ever passed to CompleteAsync/CompleteStreamingAsync, in call order — the
        assertion helper satisfying the phase's own "was this prompt sent" acceptance criterion, without
        inspecting internal queue state directly.
    .Reset()                                                               → void  (clears SentRequests and
                                                                               both response/failure queues)
    NOTE (NEVER GENERATES TEXT ITSELF — THE PHASE'S OWN HARD ACCEPTANCE CRITERION): CompleteAsync
          dequeues the next enqueued CompletionResult, or returns
          Result.Failure(IntelligenceErrors.CompletionFailed(providerName, "no canned response
          enqueued")) on an empty queue — it never synthesizes a CompletionResult itself.
          CompleteStreamingAsync dequeues and yields a canned chunk sequence verbatim, one chunk per
          yield, honoring [EnumeratorCancellation] between chunks; an empty response queue with no
          EnqueueStreamingFailure call yields zero chunks and completes (a legal, if degenerate,
          response shape — never treated as an error case).
    NOTE (CompleteStreamingAsync IS NOT Result-WRAPPED — Domain Invariant #7): mirrors the real
          contract's ISemanticKernel.CompleteStreamingAsync exactly.

InMemoryCompletionProviderDescriptor  (sealed class, implements ICompletionProviderDescriptor from
                                       SharedKernel.AI.Abstractions)
    constructor(string providerName = "in-memory-fake", int contextWindowTokens = 128000,
                int maxOutputTokens = 4096)
    .ProviderName                                               → string  (settable)
    .ContextWindowTokens                                         → int  (settable)
    .MaxOutputTokens                                             → int  (settable)
    .ValidateContextWindow(int estimatedTokens)                 → Result
        Returns IntelligenceErrors.ContextWindowExceeded(ContextWindowTokens, estimatedTokens) when
        estimatedTokens > ContextWindowTokens, else Result.Success() — mirrors the real contract's
        pre-dispatch guard exactly.

SCOPE LOCK (P-284/WO-045): Intelligence/ references only SharedKernel.AI.Abstractions — never
    SharedKernel.AI.Qdrant/.Milvus/.SemanticKernel (the concrete provider packages), and never any
    sibling capability folder in this package — in particular, never Containers/QdrantContainerFixture
    /.MilvusContainerFixture (P-283). The in-memory fakes (fast, isolated, no Docker) and the
    real-provider-integration fixtures (Docker, exercise the actual provider SDKs) are deliberately
    independent test paths, mirroring the existing Storage/-vs-Containers/MinioContainerFixture and
    Search/-vs-Containers/MeilisearchContainerFixture splits exactly.

    DELIBERATE NON-COUPLING BETWEEN ALL SIX Intelligence/ FAKES (the SECOND application of this pattern
    after Search/'s original three-type case, confirming it generalizes): InMemoryEmbeddingGenerator,
    InMemoryVectorCollection<TRecord>, InMemoryVectorCollectionProvisioner, InMemoryVectorProviderDescriptor,
    InMemorySemanticKernel, and InMemoryCompletionProviderDescriptor are six fully INDEPENDENT sealed
    fakes with no constructor or type dependency on each other. Rationale: InMemoryVectorCollection<TRecord>
    is generic per record type and by far the most commonly needed fake (a consumer testing a handler
    that depends on IVectorCollection<TRecord> alone); the other five are needed only by a consumer
    specifically testing embedding generation, rebuild-orchestration/pre-flight-validation, or LLM
    orchestration in isolation. Forcing all six to share state would add ceremony most
    InMemoryVectorCollection<TRecord>-only tests never touch. A consuming test that wants two of these
    fakes to agree on one VectorCollectionDefinition passes the SAME definition instance to each
    explicitly — never relies on implicit cross-fake state sharing.

IntelligenceServiceCollectionExtensions  (static class)
    AddInMemoryEmbeddingGenerator(this IServiceCollection, string modelId, int dimension)
                                                                             → IServiceCollection
        Registers InMemoryEmbeddingGenerator as IEmbeddingGenerator, SINGLETON — matches the real
        contract's own "engine/model clients are singletons" rule exactly; NO lifetime deviation to
        flag here, unlike the vector-collection extension below.
    AddInMemoryVectorCollection<TRecord>(this IServiceCollection, VectorCollectionDefinition definition)
        where TRecord : class, IVectorRecord                               → IServiceCollection
        Registers InMemoryVectorCollection<TRecord> as IVectorCollection<TRecord>, SINGLETON — a
        DELIBERATE deviation from the real production SCOPED per-collection registration (per
        10.Intelligence's own DI Registration rule: "per-collection and per-request services are
        scoped"), mirroring AddInMemorySearchIndex<TDocument>'s identical documented deviation: the same
        recorded-history instance must outlive the system-under-test's DI scope so post-hoc assertions
        can run after the action completes. Call once per TRecord the test needs.
    AddInMemoryVectorProvisioning(this IServiceCollection, string providerName = "in-memory-fake")
                                                                             → IServiceCollection
        Registers InMemoryVectorCollectionProvisioner as IVectorCollectionProvisioner AND
        InMemoryVectorProviderDescriptor (constructed with providerName) as IVectorProviderDescriptor,
        BOTH singleton — matches both interfaces' real "singleton, zero I/O" / "exactly one registration
        per provider" production shape exactly, no deviation. One call bundles both non-generic fakes,
        mirroring AddInMemorySearchProvisioning()'s one-call-bundles-related-fakes pattern.
    AddInMemorySemanticKernel(this IServiceCollection)                      → IServiceCollection
        Registers InMemorySemanticKernel as ISemanticKernel AND InMemoryCompletionProviderDescriptor as
        ICompletionProviderDescriptor, both singleton — the singleton choice for ISemanticKernel is
        ASSUMED, not yet confirmed against a real lifetime, since 10.Intelligence's own C-11
        (AddSharedKernelSemanticKernel() DI builder) has not shipped; reconcile once it does.
``` — only the minimal `Testcontainers.*`, `Bogus`, and `xunit.core` packages strictly required to implement fixtures and faker conventions. The Standard Test Package Set (xUnit runner, `FluentAssertions`, `NSubstitute`) is added per-`.Tests`-project, never transitively through this package. This includes the new `PagedListAssertions` (`Contracts/`) and `DomainVersionAssertions` (`Domain/`) — both are plain exception-throwing helpers, never FluentAssertions-backed, correcting an earlier superseded-phase draft for `PagedListAssertions`.

### `Workflows/` — `IWorkflowDispatcher` / `IWorkflowHandle` / `IWorkflowHandle<TResult>` in-memory doubles (17.Workflows)

> **BLOCKER-CLEARANCE VERIFICATION (re-verified directly on disk, not assumed from prose alone — the FOURTH occurrence of this domain's design-ahead-of-schedule pattern, after `Storage/` at P-269/WO-043, `Search/` at P-276/WO-044, `Intelligence/` at P-284/WO-045, and the fourth to resolve):** at the original WO-046 design pass, `17.Workflows/SharedKernel.Workflows.Temporal/SharedKernel.Workflows.Temporal.csproj` was a genuinely empty placeholder — bare `TargetFramework`/`ImplicitUsings`/`Nullable`, zero references, zero `.cs` content (the only `.cs` files on disk were compiler-generated `obj/` artifacts). `17.Workflows`'s own Design phase (P-287) was already fully ratified with a zero-drift-sourceable 519-line `CLAUDE.md` — every signature below was sourced directly from it, not guessed — but prose ratification is not compiled code a fake can `: Implement` against. **As of this Core-phase implementation pass (2026-07-23), that blocker has cleared.** `17.Workflows/SharedKernel.Workflows.Temporal/` now ships 43 real, compiled `.cs` files — `IWorkflowDispatcher`, `IWorkflowHandle`/`IWorkflowHandle<TResult>`, `IWorkflowIdFactory`, `TenantScope`, `WorkflowStartOptions`, `WorkflowExecutionDescription`, `WorkflowErrors`, `WorkflowWellKnown`, `WorkflowBase` — every one read directly from source. **Two corrections were made against the design below, both now the ground truth**: (1) the real `IWorkflowDispatcher.GetHandle`/`GetHandle<TResult>` return `IWorkflowHandle`/`IWorkflowHandle<TResult>` directly, never `Result`-wrapped, and are documented to throw `ArgumentException` for a null/whitespace `workflowId` or `TenantScope.None` — the tenant-scope-fail-loud NOTE below has been corrected to scope its "returns `Result.Failure`" claim to `StartAsync`/`DescribeAsync` only. (2) `WorkflowLifecycleStatus` is implemented `public`, not `internal` — it is the return type of the public `InMemoryWorkflowHandle.Status`/`InMemoryWorkflowHandle<TResult>.Status` members, and an `internal` enum leaking through a `public` property signature does not compile (`CS0051`); `InMemoryWorkflowExecution` is a mutable `internal sealed class` (not a "record" as originally drafted) — mutable in-place state does not fit record value-equality semantics. Unlike its three predecessors, this occurrence carries **no paired `Containers/` fixture**: `17.Workflows`'s own brain states its test story is Temporal's in-box `WorkflowEnvironment`/`WorkflowReplayer`, not Testcontainers, so there was nothing here for a container fixture to stand in for. Core-phase implementation (C-80–C-84) is now `●` Complete; `SK.16.Tests` (T-55, `SharedKernel.Testing.SelfTests`) remains a future session's work.

```text
InMemoryWorkflowDispatcher  (sealed class, implements IWorkflowDispatcher from SharedKernel.Workflows.Temporal)
    constructor(IWorkflowIdFactory? workflowIdFactory = null)
        When null, composes workflow ids itself using the SAME default format the real
        IWorkflowIdFactory documents — "{tenant}:{workflowType}:{businessKey}" via
        WorkflowWellKnown.IdSeparator — so a test gets realistic tenant-collision/AlreadyStarted
        behavior with zero wiring. A caller-supplied IWorkflowIdFactory is honored instead when
        given (e.g. a test asserting its OWN custom id-composition policy).

    — start —
    .StartAsync<TWorkflow>(WorkflowStartOptions options, TenantScope tenantScope, CancellationToken ct)
                                                                        → Task<Result<IWorkflowHandle>>
    .StartAsync<TWorkflow, TArgs>(TArgs args, WorkflowStartOptions options, TenantScope tenantScope,
                                  CancellationToken ct)                → Task<Result<IWorkflowHandle>>
    .StartAsync<TWorkflow, TArgs, TResult>(TArgs args, WorkflowStartOptions options,
                                           TenantScope tenantScope, CancellationToken ct)
                                                                        → Task<Result<IWorkflowHandle<TResult>>>

    — attach to an already-running execution —
    .GetHandle(string workflowId, string? runId, TenantScope tenantScope)          → IWorkflowHandle
    .GetHandle<TResult>(string workflowId, string? runId, TenantScope tenantScope) → IWorkflowHandle<TResult>

    — describe —
    .DescribeAsync(string workflowId, TenantScope tenantScope, CancellationToken ct)
                                                                        → Task<Result<WorkflowExecutionDescription>>

    — assertion / test-setup helpers —
    .StartedWorkflows                                          → IReadOnlyList<InMemoryWorkflowStartRecord>
                                                                   (WorkflowTypeName, composed WorkflowId,
                                                                   TenantScope, Args — every SUCCESSFUL
                                                                   start, append-only, in call order,
                                                                   mirroring InMemoryMessageBus's
                                                                   "records every call even when never
                                                                   asserted on" rule)
    .ShouldHaveStarted<TWorkflow>()                             → InMemoryWorkflowStartRecord  (first match;
                                                                   throws InvalidOperationException if none)
    .ShouldHaveStartedOnce<TWorkflow>()                         → InMemoryWorkflowStartRecord  (throws if
                                                                   zero or more than one)
    .ShouldNotHaveStarted<TWorkflow>()                          → void  (throws if any match exists)
    .CompleteWorkflow<TResult>(string workflowId, TResult result)  → void
        Test-setup helper — marks the backing execution's eventual result so a later
        IWorkflowHandle<TResult>.GetResultAsync call returns it. The fake never "runs" a workflow
        itself; this is the deliberate, explicit substitute for real execution. Throws
        InvalidOperationException if no execution exists for workflowId (start it via StartAsync first).
    .FailWorkflow(string workflowId, Error error)               → void
        Test-setup helper — the eventual-result analogue of CompleteWorkflow, for a workflow that is
        to be observed as having failed. Same InvalidOperationException guard as CompleteWorkflow.
    .ConfigureQueryHandler<TQueryResult>(string workflowId, string queryName, Func<TQueryResult> handler) → void
        NOTE: a necessary test-setup member the original design draft implied (via
        InMemoryWorkflowExecution.QueryHandlers) but never explicitly named on the dispatcher itself —
        without it, QueryHandlers could never be populated and QueryAsync would always return
        WorkflowErrors.QueryFailed. Added during Core-phase implementation (C-81). Throws
        InvalidOperationException if no execution exists for workflowId.
    .SimulateFailure                                            → bool  (settable, default false — when
                                                                   true, every StartAsync overload's and
                                                                   DescribeAsync's outer call returns
                                                                   WorkflowErrors.ServiceUnavailable
                                                                   instead of performing the operation.
                                                                   GetHandle is unaffected — it never
                                                                   round-trips in the real contract either)
    .Reset()                                                    → void  (clears the backing execution
                                                                   store AND StartedWorkflows)

    NOTE (BACKING STORE): a ConcurrentDictionary<string, InMemoryWorkflowExecution> keyed by the
          COMPOSED workflow id (post-IWorkflowIdFactory), shared with every InMemoryWorkflowHandle /
          InMemoryWorkflowHandle<TResult> returned for that id — a handle is a thin view over the same
          record the dispatcher wrote, never an independent copy.

    NOTE (TENANT-SCOPE FAIL-LOUD, BEFORE ANY STATE MUTATION OR LOOKUP): TenantScope.None passed to
          StartAsync (any overload) or DescribeAsync returns Result.Failure
          (WorkflowErrors.TenantScopeMissing) immediately, mirroring the fail-loud pre-flight pattern
          already established by Search/InMemorySearchIndex and Intelligence/InMemoryVectorCollection.
          **CORRECTED against the original design draft, which incorrectly lumped GetHandle into this
          same "returns Result.Failure" behavior**: the REAL IWorkflowDispatcher.GetHandle/
          GetHandle<TResult> return IWorkflowHandle/IWorkflowHandle<TResult> directly (never
          Result-wrapped) and are documented to throw ArgumentException for TenantScope.None or a
          null/whitespace workflowId — this fake's GetHandle/GetHandle<TResult> throw ArgumentException
          to match, confirmed against the real interface's own XML docs during Core-phase
          implementation. This is otherwise a DELIBERATE DESIGN CHOICE flagged for reconciliation once
          17.Workflows's own SK.17.Core further evolves — unlike a search index or vector collection, a
          workflow id composition has no per-definition "declares a tenant field" toggle to condition
          the check on, so this fake takes the more conservative, always-enforce reading of the real
          contract's own "TenantScope IS A MANDATORY, NON-NULLABLE, NON-DEFAULTED SEPARATE PARAMETER...
          MORE load-bearing here, not less" language.

    NOTE (StartAsync's AlreadyStarted SEMANTICS): if an execution already exists at the composed id
          with Status == Running, StartAsync returns Result.Failure(WorkflowErrors.AlreadyStarted)
          without mutating state — the fake's analogue of Temporal's own "at most one RUNNING execution
          per workflow id" enforcement (the platform's durable idempotency primitive). Honoring the
          full range of WorkflowStartOptions.IdReusePolicy/.IdConflictPolicy (e.g. terminate-and-restart
          semantics) beyond this conservative default is deferred to Core-phase implementation, gated on
          confirming those enum members against the real, not-yet-compiled WorkflowStartOptions shape —
          never guessed at Design time.

    NOTE (GetHandle NEVER ROUND-TRIPS, MATCHING REAL TEMPORAL SEMANTICS EXACTLY): both overloads
          construct and return a handle object unconditionally, regardless of whether an execution
          exists yet at that id — Temporal itself does not validate a workflow id's existence when you
          ask for a handle, only when you invoke an operation against it. A handle obtained for an id
          with no backing execution record returns WorkflowErrors.NotFound from every operation
          (SignalAsync/QueryAsync/CancelAsync/TerminateAsync/GetResultAsync), never from GetHandle
          itself. GetHandle/GetHandle<TResult> throw ArgumentException synchronously for a
          null/whitespace workflowId or TenantScope.None, matching the real interface's own documented
          exception contract exactly (confirmed during Core-phase implementation, C-81).

    NOTE (DescribeAsync): builds a WorkflowExecutionDescription from the backing execution record's
          Status/TenantScope (returns WorkflowErrors.NotFound if absent). WorkflowExecutionDescription's
          exact member shape is UNCONFIRMED at this Design pass — 17.Workflows ships no compiled model
          yet — and must be re-verified directly against the real type at Core-phase implementation
          time, never guessed here, mirroring this domain's own established discipline for any upstream
          record type that has not yet compiled (e.g. Search/'s SearchIndexHealth, Intelligence/'s
          VectorCollectionHealth, both confirmed only once their owning domain's Core phase shipped).

InMemoryWorkflowExecution  (internal sealed CLASS — mutable, the shared backing state, never public)
    NOTE: implemented as a mutable class rather than the originally-drafted "record" — an execution's
          Status/signal-history/eventual-result all mutate in place as dispatcher/handle members are
          called, which does not fit a record's value-equality/with-expression semantics. Status/
          TerminationReason are Volatile-guarded; SignalHistory/QueriesReceived/QueryHandlers are
          ConcurrentQueue/ConcurrentDictionary; the eventual result is an Interlocked.Exchange-guarded
          boxed (value, isFailure) pair — all per this domain's standing thread-safety rule.
    .WorkflowId / .RunId / .TaskQueue / .TenantScope / .WorkflowTypeName / .Args
    .Status                                                     → WorkflowLifecycleStatus
    .SignalHistory                                              → ConcurrentQueue<(string SignalName, object? Args)>
    .QueriesReceived                                             → ConcurrentQueue<string>
    .QueryHandlers                                              → per-query-name configured Func<object?> results,
                                                                   populated via InMemoryWorkflowDispatcher
                                                                   .ConfigureQueryHandler<TQueryResult>
    .TerminationReason                                          → string?
    .SetResult(object? value, bool isFailure) / .TryGetResult(out object? value, out bool isFailure)
        → the configured eventual result (a boxed TResult or a boxed SharedKernel.Primitives.Errors.Error),
          set only via InMemoryWorkflowDispatcher.CompleteWorkflow<TResult>/FailWorkflow

WorkflowLifecycleStatus  (public enum — CORRECTED from the original "internal enum" draft: it is the
                          return type of the public InMemoryWorkflowHandle.Status/
                          InMemoryWorkflowHandle<TResult>.Status members, and an internal enum leaking
                          through a public property signature does not compile, CS0051)
    Running | Completed | Cancelled | Terminated | Failed

InMemoryWorkflowHandle  (sealed class, implements IWorkflowHandle from SharedKernel.Workflows.Temporal)
    .WorkflowId                                                 → string
    .RunId                                                      → string?
    .SignalAsync<TSignalArgs>(string signalName, TSignalArgs args, CancellationToken ct) → Task<Result>
    .QueryAsync<TQueryResult>(string queryName, CancellationToken ct)                    → Task<Result<TQueryResult>>
    .CancelAsync(CancellationToken ct)                          → Task<Result>
    .TerminateAsync(string reason, CancellationToken ct)        → Task<Result>
    .SimulateFailure                                            → bool  (settable, default false — a
                                                                   SINGLE write-path toggle covering
                                                                   SignalAsync/CancelAsync/TerminateAsync
                                                                   only, mirroring the established
                                                                   Storage/Search/Intelligence
                                                                   .SimulateFailure convention;
                                                                   QueryAsync — a read — is unaffected)
    .Status                                                     → WorkflowLifecycleStatus
    .SignalsReceived                                            → IReadOnlyList<(string SignalName, object? Args)>
    .QueriesReceived                                            → IReadOnlyList<string>
    .ShouldHaveSignalled(string signalName)                     → object?  (returns the recorded args;
                                                                   throws InvalidOperationException if
                                                                   never signalled)
    .ShouldHaveSignalled<TSignalArgs>(string signalName)        → TSignalArgs  (typed variant, casts the
                                                                   recorded args)
    .ShouldNotHaveSignalled(string signalName)                  → void  (throws if any match exists)
    .ShouldHaveBeenQueried(string queryName)                    → void  (throws unless queryName appears
                                                                   in QueriesReceived)
    .ShouldHaveBeenCancelled()                                  → void  (throws unless Status == Cancelled)
    .ShouldHaveBeenTerminated(string? expectedReason = null)    → void  (throws unless Status ==
                                                                   Terminated, and — when expectedReason
                                                                   is supplied — the recorded reason
                                                                   matches exactly)
    NOTE (NotFound): every operation returns WorkflowErrors.NotFound if the backing
          InMemoryWorkflowExecution has never been created for this WorkflowId (see GetHandle's own
          note above) — never throws, matching the real contract's Result-returning shape.
    NOTE (CancelAsync/TerminateAsync ARE IDEMPOTENT): re-cancelling an already-Cancelled/Terminated/
          Completed/Failed execution still returns Result.Success without changing Status — a
          fake-only simplification pending 17.Workflows's own Core phase documenting real
          double-cancel/double-terminate semantics, mirroring the identical open item Storage/ and
          Search/ both carry for their own delete-path idempotency.
    NOTE (TerminateAsync's REASON GUARD): a null/empty/whitespace-only reason throws
          ArgumentException synchronously — a CALLER PROGRAMMING ERROR, not a Result-encoded business
          outcome, since the real IWorkflowHandle.TerminateAsync signature has no default-reason
          overload specifically so a caller must always supply one; the fake enforces that intent at
          the boundary rather than silently accepting a blank reason.
    NOTE (QueryAsync): dequeues... no — reads the CONFIGURED handler registered via
          InMemoryWorkflowExecution.QueryHandlers for queryName; an unconfigured query name returns
          WorkflowErrors.QueryFailed — the fake's direct analogue of the real contract's own "a query
          against an execution whose worker fleet is down returns QueryFailed rather than hanging."

InMemoryWorkflowHandle<TResult>  (sealed class, implements IWorkflowHandle<TResult> from
                                  SharedKernel.Workflows.Temporal)
    — composes a private InMemoryWorkflowHandle internally (NEVER inherits — every fake in this
      package is sealed with no extension point) and forwards .WorkflowId/.RunId/.SignalAsync/
      .QueryAsync/.CancelAsync/.TerminateAsync/.SimulateFailure/.Status/.ShouldHave*/.ShouldNotHave*
      to it verbatim —
    .GetResultAsync(CancellationToken ct)                       → Task<Result<TResult>>
    NOTE (DETERMINISTIC, NEVER BLOCKS, NEVER POLLS — THE PHASE'S OWN "no Task.Delay/Thread.Sleep"
          ACCEPTANCE CRITERION APPLIED TO THIS MEMBER SPECIFICALLY): the fake never executes a
          workflow, so there is no real completion event to await. GetResultAsync reads the backing
          execution's PendingResult — set only via InMemoryWorkflowDispatcher.CompleteWorkflow<TResult>/
          .FailWorkflow — and returns it immediately if present. If GetResultAsync is called before
          either was called for this workflow id, it throws InvalidOperationException with a message
          naming the workflow id and instructing the caller to call CompleteWorkflow/FailWorkflow
          first — never blocks, never spins, never Task.Delay-polls waiting for a result that this
          fake has no mechanism to ever produce on its own.

WorkflowServiceCollectionExtensions  (static class)
    AddInMemoryWorkflowDispatcher(this IServiceCollection)      → IServiceCollection
        Registers InMemoryWorkflowDispatcher as IWorkflowDispatcher, SINGLETON — a DELIBERATE
        deviation from the real production SCOPED lifetime (17.Workflows's own DI Registration
        section: "IWorkflowDispatcher is scoped — it captures the ambient correlation/tenant context
        of the current request"), mirroring InMemoryMessageBus/InMemorySearchIndex<TDocument>/
        InMemoryVectorCollection<TRecord>'s identical documented deviation: the same recorded-history
        instance must outlive the system-under-test's DI scope so post-hoc ShouldHaveStarted/
        ShouldHaveSignalled assertions can run after the action completes.

SCOPE LOCK (P-288/WO-046): Workflows/ references only SharedKernel.Workflows.Temporal (17.Workflows
    ships no `.Abstractions` split to narrow against — a deliberate single-package decision that
    domain's own brain ratifies explicitly) and never any sibling capability folder in this package.
    Unlike Storage/, Search/, and Intelligence/, there is no Containers/ fixture for this folder to stay
    isolated from — 17.Workflows's own Test Rules section states plainly that WorkflowEnvironment
    replaces the container fixture for that domain, so no Testcontainers.Temporal-shaped package or
    fixture exists anywhere in this package. Zero references to Temporalio or any real Temporal SDK
    type anywhere in Workflows/ — this fake satisfies IWorkflowDispatcher/IWorkflowHandle's own
    already-ratified C# signatures only, never a Temporal wire concept (RunId is treated as an opaque
    string, never parsed or validated against any real Temporal RunId format).
```

### `Cryptography/` — `01.Core`/`SharedKernel.Cryptography` fakes (P-300/WO-049)

```text
FakeOneWayHasher  (sealed class, implements IOneWayHasher)  — implemented C-85/SK.16.Core
    .Iterations                                            → int  (settable, default a tiny fixed value
                                                              e.g. 4 — never the real 600,000 default)
    Hash(string secret)                                    → string  (identical self-describing binary
                                                              format to Pbkdf2OneWayHasher: format marker +
                                                              iteration count + salt + subkey, Base64;
                                                              computed via the REAL Rfc2898DeriveBytes.Pbkdf2
                                                              at the tiny .Iterations value, not a fabricated
                                                              format — so a byte-identical round trip is
                                                              guaranteed without the real production cost)
    Verify(string hash, string secret)                     → HashVerificationResult  (decodes the hash's
                                                              embedded iteration count; returns
                                                              SuccessRehashNeeded whenever it differs from
                                                              the CURRENT .Iterations value — mutate
                                                              .Iterations between Hash/Verify calls to
                                                              deliberately simulate a rehash-needed scenario;
                                                              a malformed/undecodable hash returns Failed,
                                                              never throws)
    NOTE: mirrors Pbkdf2OneWayHasher's exact binary layout and mechanism, differing ONLY in where the
          iteration count comes from (a plain settable property vs. IOptionsMonitor<CryptographyOptions>)
          and its tiny default. TEST-ONLY — never production-safe; the whole point is to skip the real
          600,000-iteration cost, which would make this fake actively dangerous if ever wired into a
          production DI container by accident. XML docs must say so in capitals.

FakeSecureRandomGenerator  (sealed class, implements ISecureRandomGenerator)  — implemented C-86/SK.16.Core
    constructor(int? seed = null)
    NextBytes(int length)                                  → byte[]
    NextToken(int length = 32)                             → string  (URL-safe Base64, no padding —
                                                              identical encoding to production in both modes)
    NOTE: when seed is null (the default), both members delegate to the REAL
          System.Security.Cryptography.RandomNumberGenerator — byte-for-byte identical behavior to
          production's CryptoRandomGenerator, so two calls NEVER return equal output, deliberately
          preserving enough non-determinism to catch a hardcoded-token bug that a fully-deterministic fake
          would silently hide. When seed is supplied, backed by a seeded System.Random instead — fully
          reproducible across runs for the SAME seed, XML-doc-flagged IN CAPITALS as NOT cryptographically
          secure and never acceptable outside deterministic test assertions (e.g. snapshot-testing a
          generated token value).

FakeEncryptionKeyProvider  (sealed class, implements IEncryptionKeyProvider)  — implemented C-87/SK.16.Core
    constructor(string currentKeyId = "v1")                (seeds one key immediately)
    AddKey(string keyId)                                   → CryptographicKey  (fresh 32-byte material via
                                                              RandomNumberGenerator.Fill)
    SetCurrentKey(string keyId)                             → void
    RemoveKey(string keyId)                                 → void
    GetCurrentKey()                                         → CryptographicKey
    GetKey(string keyId)                                    → CryptographicKey?
    NOTE: promoted verbatim from SharedKernel.Cryptography.Tests' existing INTERNAL
          InMemoryEncryptionKeyProvider (01.Core/SharedKernel.Cryptography/SharedKernel.Cryptography.Tests/
          Symmetric/InMemoryEncryptionKeyProvider.cs, read directly, zero drift) into this package's public,
          shared surface — mirrors the WO-040 ApplicationPipelineTestHarness-promoted-from-PipelineTestHarness
          precedent. Multi-key-version support lets a test exercise a key-rotation
          Decrypt-of-an-older-payload scenario exactly like a real Key Vault-backed provider would. 01.Core's
          own test suite adopting this shared type in place of its private copy is a documented cross-domain
          follow-up, never performed by this domain.

FakeAsymmetricKeyProvider  (sealed class, implements IAsymmetricKeyProvider, IDisposable)  — implemented C-88/SK.16.Core
    GetRsaKey(string keyId)                                → RSA  (lazily generates+caches a 2048-bit key
                                                              pair per keyId; returns a FRESH handle cloned
                                                              via ExportParameters(true)/RSA.Create(...) so
                                                              the caller's using/Dispose() never invalidates
                                                              the cached original)
    GetEcdsaKey(string keyId)                              → ECDsa  (same pattern, P-256 curve)
    Dispose()                                               → void  (disposes every cached RSA/ECDsa instance)
    NOTE: promoted verbatim from SharedKernel.Cryptography.Tests' existing INTERNAL
          InMemoryAsymmetricKeyProvider (.../Signing/InMemoryAsymmetricKeyProvider.cs, read directly, zero
          drift) — same promotion rationale as FakeEncryptionKeyProvider above.

FakeSymmetricEncryptionService  (sealed class, implements ISymmetricEncryptionService)  — implemented C-89/SK.16.Core
    constructor(IEncryptionKeyProvider? keyProvider = null)  (defaults to an internally-owned
                                                              FakeEncryptionKeyProvider when omitted — zero-
                                                              config convenience; accepts any
                                                              IEncryptionKeyProvider, including a real one,
                                                              when supplied — mirrors AesGcmEncryptionService's
                                                              exact constructor shape for DI-swap parity)
    Encrypt(byte[] plaintext)                              → EncryptedPayload
    Decrypt(EncryptedPayload payload)                      → Result<byte[]>
    EncryptToString(string plaintext)                      → string
    DecryptToString(string encoded)                        → Result<string>
    .SimulateDecryptFailure                                → bool  (settable, default false — forces
                                                              Decrypt/DecryptToString to unconditionally
                                                              return the same Error.Unexpected/
                                                              CryptographyErrorCodes.DecryptionFailed shape
                                                              as a real tamper/wrong-key failure, without
                                                              needing to hand-corrupt bytes)
    .EncryptedPayloads                                     → IReadOnlyList<EncryptedPayload>  (every payload
                                                              ever produced by Encrypt/EncryptToString,
                                                              append-only, mirroring InMemoryFileStorage's
                                                              UploadedKeys introspection convention)
    NOTE: uses a deterministic, NON-cryptographic reversible transform (e.g. byte-XOR repeating the resolved
          key's material across the plaintext length) instead of real AES-GCM — XML-doc-flagged IN CAPITALS
          as NEVER SECURE, test-only, never a substitute for AesGcmEncryptionService in anything
          security-sensitive. Decrypt on a KeyId the supplied IEncryptionKeyProvider.GetKey cannot resolve
          returns the SAME CryptographyErrorCodes.UnknownKeyId-shaped failure as production — reuses the REAL
          CryptographyErrorCodes static class directly (the production error-code source itself, not a
          sibling-folder reference — CryptographyErrorCodes lives in SharedKernel.Cryptography, the single
          abstraction package this whole folder depends on).

FakeAsymmetricSignatureService  (sealed class, implements IAsymmetricSignatureService)  — implemented C-90/SK.16.Core
    constructor()                                           (no external dependency — self-contained)
    Sign(byte[] data, string keyId)                        → byte[]  (deterministic HMACSHA256(data) keyed
                                                              by SHA256(UTF8.GetBytes(keyId)) — a stable,
                                                              internally-derived per-keyId pseudo-key; NEVER
                                                              real RSA/ECDSA key generation)
    Verify(byte[] data, byte[] signature, string keyId)    → bool  (recomputes the same HMAC, compares via
                                                              CryptographicOperations.FixedTimeEquals)
    .SignedPayloads                                        → IReadOnlyList<(byte[] Data, string KeyId)>
    NOTE: a SINGLE fake instance/type is algorithm-agnostic and works uniformly across the unkeyed-default
          AND both CryptographyServiceCollectionExtensions.RsaSignatureServiceKey("Rsa")/
          EcdsaSignatureServiceKey("Ecdsa") keyed DI slots AddSharedKernelCryptography() establishes —
          AddFakeCryptography() reuses those SAME real constants (never redeclares fake-only key strings) so
          a test resolving GetRequiredKeyedService<IAsymmetricSignatureService>(RsaSignatureServiceKey) gets
          a working fake with zero call-site change from production.

FakeHmacSigner  (sealed class, implements IHmacSigner)  — implemented C-91/SK.16.Core
    constructor()                                           (no dependency)
    Sign(byte[] data, byte[] secret)                       → byte[]  (real HMACSHA256 — behaviorally
                                                              identical to production's HmacSha256Signer,
                                                              since HMAC is already fast/deterministic and
                                                              there is no cost to skip)
    Verify(byte[] data, byte[] signature, byte[] secret)   → bool  (CryptographicOperations.FixedTimeEquals,
                                                              same as production)
    .SignedPayloads / .VerifiedPayloads                    → IReadOnlyList<(byte[] Data, byte[] Secret)>
    NOTE: shipped for platform-completeness (every 01.Core-DI-registered abstraction gets a fake, no
          exceptions) and introspection — not because the real implementation is slow.

FakeContentHasher  (sealed class, implements IContentHasher — 01.Core P-296)
    — implemented C-92/SK.16.Core (2026-07-28); the D-159 blocker cleared 2026-07-27 when 01.Core's
      own SK.01.P296 shipped IContentHasher — see the BLOCKER-CLEARANCE note below for the full history
    ComputeHash(byte[] content)                            → byte[]
    ComputeHash(Stream content)                            → byte[]
    ComputeHashAsync(Stream content, CancellationToken ct = default) → ValueTask<byte[]>
    .HashedContent                                         → IReadOnlyList<byte[]>  (every content payload
                                                              ever hashed, append-only)
    NOTE: mirrors Sha256ContentHasher exactly (real SHA256.HashData(byte[])) since content hashing is
          already fast/deterministic and there is no reason to fake the algorithm — the sole value-add is
          completeness + introspection via .HashedContent. CORRECTED post-implementation: the Stream/async
          overloads fully buffer their input into a byte[] before hashing (via SHA256.HashData(byte[]) on
          the buffered bytes) rather than calling SHA256.HashData(Stream)/.HashDataAsync(Stream,...)
          directly, because the .HashedContent introspection list needs the raw payload bytes, which the
          Stream-based BCL overloads never expose. This is an accepted test-double trade-off against
          production's genuinely constant-memory streaming — test payloads are never blob-scale.

FakeCryptographyServiceCollectionExtensions.AddFakeCryptography(this IServiceCollection)
    — implemented C-93/SK.16.Core
    NOTE: registers, as singletons: IOneWayHasher→FakeOneWayHasher, IEncryptionKeyProvider→
          FakeEncryptionKeyProvider, ISymmetricEncryptionService→FakeSymmetricEncryptionService,
          IAsymmetricKeyProvider→FakeAsymmetricKeyProvider, IAsymmetricSignatureService→
          FakeAsymmetricSignatureService (unkeyed default + both RsaSignatureServiceKey/
          EcdsaSignatureServiceKey-keyed singletons), IHmacSigner→FakeHmacSigner,
          ISecureRandomGenerator→FakeSecureRandomGenerator (non-seeded constructor — genuinely random by
          default), IContentHasher→FakeContentHasher. Named class deliberately distinct from the real
          SharedKernel.Cryptography.Extensions.CryptographyServiceCollectionExtensions
          (FakeCryptographyServiceCollectionExtensions, not the same simple name) to avoid any
          static-member ambiguity given this folder deliberately reuses the REAL RsaSignatureServiceKey/
          EcdsaSignatureServiceKey constants. DELIBERATE DIVERGENCE from AddSharedKernelCryptography():
          production intentionally does NOT register IEncryptionKeyProvider/IAsymmetricKeyProvider
          (consumer-supplied by design); this fake bundle DOES, since a test wanting AddFakeCryptography()
          to work end-to-end with zero extra wiring needs SOME functioning key material — mirrors the
          Application/AddFakeApplicationBehaviorServices() precedent of satisfying a dependency production
          deliberately leaves to the consumer. Manual per-type services.AddSingleton<IX, FakeX>()
          registration remains valid for a subset, mirroring AddFakeCachingServices()'s documented
          manual-alternative precedent — this is how a caller obtains the 7 already-available fakes TODAY,
          ahead of this composite method unblocking.

BLOCKER-CLEARANCE VERIFICATION (P-300/WO-049 — the FIFTH design-ahead-of-schedule occurrence in this
    domain's history, and the FIRST *partial* one): `01.Core/SharedKernel.Cryptography` is already fully
    `Published` (WO-034) — all seven baseline interfaces above (IOneWayHasher through
    ISecureRandomGenerator) are real, compiled, zero-drift-verified types on disk (verified 2026-07-27),
    so FakeOneWayHasher through FakeHmacSigner are fully unblocked and implementable today. At the original
    Design pass, ONLY IContentHasher (01.Core's own SK.01.P296) was unavailable, so FakeContentHasher and
    the composite AddFakeCryptography() (its body calls AddSingleton<IContentHasher, FakeContentHasher>(),
    so the whole method failed to compile until that type existed) were `⚑` Blocked pending SK.01.P296's
    Core phase. **RE-VERIFIED DURING THE SCAFFOLD-PHASE PASS (2026-07-28, while confirming S-37's
    `ProjectReference` claim on disk): the blocker has cleared.** `01.Core`'s own `SK.01.P296` closed
    2026-07-27 (per `01.Core/CLAUDE.md`'s own changelog) and `01.Core/SharedKernel.Cryptography/Hashing/
    IContentHasher.cs` now exists on disk with its full three-member contract
    (`ComputeHash(byte[])`/`ComputeHash(Stream)`/`ComputeHashAsync(Stream, CancellationToken)`), matching
    this file's own Interface Contracts target shape with zero drift. `state-map.md`'s C-92/C-93/T-57/
    T-58/DO-28 were corrected `⚑`→`○` at the Scaffold-phase pass. **IMPLEMENTED (Core-phase pass,
    2026-07-28)**: `FakeContentHasher`/`FakeCryptographyServiceCollectionExtensions.AddFakeCryptography()`
    are now real, shipped types (C-92/C-93 `●`) — T-57/T-58/DO-28 (proving/documenting them in
    `SharedKernel.Testing.SelfTests`) remain a future `SK.16.Tests`/`SK.16.Docs` session's work.

DOCS-PHASE COMPLETION (DO-27/DO-28, 2026-07-28): all eight `Cryptography/` fakes now carry an explicit,
    capitalized `TEST-ONLY — NEVER PRODUCTION-SAFE` `<remarks>` paragraph — `FakeOneWayHasher` and
    `FakeSymmetricEncryptionService` already had one from the Core-phase pass; DO-27 added the missing
    paragraph to the other five (`FakeSecureRandomGenerator`, `FakeEncryptionKeyProvider`,
    `FakeAsymmetricKeyProvider`, `FakeAsymmetricSignatureService`, `FakeHmacSigner`), each worded to the
    specific reason that type must never reach a production DI container (non-seeded mode being
    byte-for-byte production-equivalent is NOT an exception; algorithmically-real HMACSHA256 is NOT an
    exception either — the root `CLAUDE.md` "16.Testing is never referenced by production code" hard rule
    is what actually forbids it, not the fake's internal fidelity to the real algorithm). `FakeContentHasher`
    and `AddFakeCryptography()`'s `IContentHasher` registration line (DO-28) were verified already
    documented from the Core-phase pass with no further change needed.
```

### `FeatureManagement/` — `01.Core`/`SharedKernel.FeatureManagement` fake (P-300/WO-049)

```text
FakeFeatureManager  (sealed class, implements IFeatureManager — 01.Core/SharedKernel.FeatureManagement)
    — implemented C-94/SK.16.Core
    NOTE (BLOCKER-CLEARANCE VERIFICATION): at the original Design pass, IFeatureManager.GetVariantAsync/
    GetVariantAsync<TContext> did not exist on the interface (the live Abstractions/IFeatureManager.cs
    declared only the two boolean IsEnabledAsync overloads, verified 2026-07-27) — a missing interface
    member blocks the ENTIRE type, not merely a call site, so this fake could not even be PARTIALLY
    written. The blocker cleared 2026-07-28 (re-verified during the Scaffold-phase pass while confirming
    S-38's `ProjectReference` claim on disk): `01.Core`'s own `SK.01.P298` closed 2026-07-28 and the live
    Abstractions/IFeatureManager.cs now declares both GetVariantAsync(string, CancellationToken)/
    GetVariantAsync<TContext>(string, TContext, CancellationToken) alongside the two pre-existing boolean
    overloads, matching this file's own Interface Contracts target shape with zero drift.
    **IMPLEMENTED (Core-phase pass, 2026-07-28)**: `FakeFeatureManager` is now a real, shipped type
    (C-94 `●`) — T-59/DO-29 (proving/documenting it in `SharedKernel.Testing.SelfTests`) remain a future
    `SK.16.Tests`/`SK.16.Docs` session's work.
    constructor()                                           (no dependency)
    SetEnabled(string feature, bool enabled)               → void  (populates a
                                                              ConcurrentDictionary<string,bool> override map)
    IsEnabledAsync(string feature, CancellationToken ct)   → ValueTask<bool>  (returns the configured
                                                              override, or false for an unconfigured feature
                                                              — a fake defaults feature flags CLOSED, the
                                                              safer choice)
    IsEnabledAsync<TContext>(string feature, TContext context, CancellationToken ct) → ValueTask<bool>
                                                              (same override map, context-agnostic — a
                                                              deterministic override map, not a rules engine,
                                                              is the fake's whole point)
    SetVariant(string feature, FeatureVariant variant)     → void  (populates a parallel override map for
                                                              the variant path)
    GetVariantAsync(string feature, CancellationToken ct)  → ValueTask<FeatureVariant>  (returns the
                                                              configured override, or the REAL
                                                              FeatureVariant.Unassigned sentinel (Name =
                                                              "Unassigned", Configuration = null) for an
                                                              unconfigured feature — CORRECTED at
                                                              implementation time (T-59 proving pass): the
                                                              shipped fake reuses IFeatureManager's own
                                                              production fallback sentinel via
                                                              `?? FeatureVariant.Unassigned` rather than a
                                                              fake-only ".Name = 'Default'" placeholder this
                                                              Design-phase draft originally specified — NEVER
                                                              throws, mirroring the real contract's documented
                                                              fallback requirement exactly, byte-for-byte)
    GetVariantAsync<TContext>(string feature, TContext context, CancellationToken ct) → ValueTask<FeatureVariant>
                                                              (same override map, context-agnostic)
    Reset()                                                 → void  (clears both override maps)
    NOTE: explicitly NOT a weighted-random allocator — a test asserting "60% of calls get VariantB" is
          testing Microsoft.FeatureManagement's own allocation engine, not this platform's neutral
          abstraction; this fake exists so APPLICATION CODE consuming IFeatureManager can be tested
          deterministically given a fixed flag/variant state, never to reimplement percentage-based rollout
          logic. Target shape sourced from 01.Core/CLAUDE.md's ratified P-298 Interface Contracts block.

FakeFeatureManagementServiceCollectionExtensions.AddFakeFeatureManagement(this IServiceCollection)
    — implemented C-95/SK.16.Core
    NOTE: registers IFeatureManager→FakeFeatureManager as a singleton, mirroring
          AddSharedKernelFeatureManagement()'s own singleton lifetime exactly — no deviation to document,
          unlike the messaging/search/workflow doubles.

DOCS-PHASE COMPLETION (DO-29, 2026-07-28): verified already documented from the Core-phase pass — no
    further change needed. `FakeFeatureManager`'s class `<remarks>` explicitly states it is "NOT a
    weighted-random allocator" (matching the NOTE above verbatim) and explains why in terms of testing
    application code deterministically rather than reimplementing `Microsoft.FeatureManagement`'s own
    percentage-based allocation engine; `AddFakeFeatureManagement()` carries a full `<summary>`/`<param>`/
    `<returns>` doc set.

SCOPE LOCK (P-300/WO-049): FeatureManagement/ references SharedKernel.FeatureManagement only — never
    SharedKernel.Cryptography (Cryptography/'s own dependency) or any other sibling capability folder in
    this package. Cryptography/ likewise never references SharedKernel.FeatureManagement or
    FeatureManagement/'s types — the two new folders are fully independent of each other despite landing in
    the same work order and sharing the same upstream domain (01.Core).
```

- Sibling capability folders (`Caching/`, `Domain/`, `Contracts/`, `Security/`, `Messaging/`, `Persistence/`, `Clocks/`, `Containers/`, `Communication/`, `ServiceDefaults/`, `Fakers/`, `Application/`, `Logging/`, `Storage/`, `Search/`, `Intelligence/`, `Workflows/`) must **never reference each other**. Each fake depends only on the single abstraction package it implements (e.g., `FakeCacheService` → `SharedKernel.Caching.Abstractions` only). Mirrors the platform's sibling-package-isolation rule already enforced in `02.Caching`. Standalone helpers with no owning abstraction (`SpecificationTestBuilder<T>`, `ProjectionSpecificationBuilder<TAggregate,TResult>`, etc.) depend only on the domain types they operate over, never on a sibling folder's fake types. Demonstrated concretely by `Application/ApplicationPipelineTestHarness` (WO-040): it needs `ActivityListener`-based span capture, functionally similar to `Communication/ActivityRecorder`, but the rule forbids reusing it across folders — it hand-rolls its own small, self-contained `ActivityListener` wiring instead. A little duplicated boilerplate across sibling folders is the accepted cost of this rule; it is not a bug to "fix" by punching a hole in the isolation rule.
- A local-seam interface that a consuming domain deliberately ships with zero implementation (e.g. `05.Application.Behaviors`' own `IUnitOfWork`/`IAuthorizationContext`/`IIdempotencyKeyStore`) is faked against **that domain's own narrower interface**, never against a same-named interface owned by a different domain — `Application/FakeUnitOfWork` implements `SharedKernel.Application.Behaviors.IUnitOfWork`, never `SharedKernel.Persistence.Abstractions.IUnitOfWork`; the two are unrelated types that happen to share a name (root `CLAUDE.md` documents the same disambiguation). Fakes in this domain must document which of two same-named interfaces they satisfy whenever a naming collision like this exists.
- When a real production implementation of an interface may **optionally** implement a second, additive capability interface detected at runtime via an `is`-check (e.g. `IIdempotencyKeyStore` optionally also implementing `IIdempotencyResponseStore`, `05.Application.Behaviors` P-242), the corresponding fakes must ship as **two separate concrete types** — one implementing only the base interface, one implementing both — never as a single type with a constructor flag that claims to toggle the optional capability. C# interface implementation is a compile-time, per-type fact; it cannot be turned on/off at runtime, so a flag-based single-type fake would make the `is`-check always succeed (or always fail) regardless of the flag, silently breaking whichever test path the flag was supposed to disable. `Application/FakeIdempotencyKeyStore` / `Application/FakeIdempotencyResponseStore` (WO-040) is the reference example for this rule.
- Every fake is a **`sealed` class** — no inheritance extension point. Tests compose behavior via constructor parameters and mutable properties (`SimulateFailure`, `IsAuthenticated`, etc.), never by subclassing a fake. Abstract bases (`EntityFaker<TEntity,TId>`, `SingleValueObjectFaker<TValueObject,TValue>`, `AggregateRootFaker<TAggregate,TId>`, `TenantedAggregateFaker<TAggregate,TId>`, `TestSharedKernelDbContext`) are the deliberate, documented exception — they exist specifically to be subclassed by consuming test projects, unlike fakes which are leaf types.
- Any fake holding mutable shared state (caches, recorded message lists) must use a **thread-safe collection** (`ConcurrentDictionary`, `ConcurrentQueue`) — xUnit runs test collections in parallel by default. `FakeCacheInvalidationBus.PublishedInvalidations` and its registered handler list follow the same rule.
- Fakes simulate **behavioral correctness, not timing** — no fake enforces TTL/expiry/sliding-window semantics from `CachePolicy`, retry backoff, or any other time-based production behavior unless a test explicitly drives a `FakeClock`. Real `Task.Delay`/`Thread.Sleep` is forbidden anywhere in this package.
- Container fixtures implement `IAsyncLifetime` **exclusively** — never a synchronous constructor that blocks on `.Result`/`.Wait()` to start a container. Container images are referenced by a **pinned tag**, never `:latest`.
- Container fixtures are scoped per xUnit `ICollectionFixture<T>` — **one instance per test collection**, never started per test method. This is the same pattern already mandated by `02.Caching`, `06.Persistence`, and `07.Messaging`'s Test Rules for Redis/PostgreSQL/RabbitMQ Testcontainers.
- `InMemoryMessageBus`/`InMemoryEventPublisher` record **every** `PublishAsync`/`SendAsync` call, even when the test never asserts on it. Assertion helpers (`ShouldHavePublished<T>()`, `PublishedOf<TEvent>()`, etc.) are read-only queries over the recorded list — they must never mutate it.
- `InMemoryMessageBus.RequestAsync` is **configurable** via `SetResponseHandler<TRequest,TResponse>(Func<TRequest,TResponse>)`, throwing a descriptive `InvalidOperationException` when no handler is registered for the requested type pair — **corrected** by P-183/WO-029; an earlier draft of this rule specified `NotSupportedException`, which is now superseded. `InMemoryMessageBus.ExecuteRoutingSlipAsync` still throws `NotSupportedException` by design — true routing-slip orchestration requires a real broker round-trip; tests needing that fidelity use MassTransit's `TestHarness` (`07.Messaging`'s own Test Rules, or this package's own `TestHarnessFactory`), not the in-memory double.
- `AddInMemoryMessageBus()`/`AddInMemoryEventPublisher()` register their double as a **singleton**, intentionally diverging from `IMessageBus`/`IEventPublisher`'s production scoped lifetime — documented explicitly at the point of registration so no consumer mistakes this for the production DI shape.
- `TestHarnessFactory` is the **only** type in `Messaging/` permitted to carry a `SharedKernel.Messaging.MassTransit` reference — `InMemoryMessageBus`/`InMemoryEventPublisher` remain isolated to `SharedKernel.Messaging.Abstractions` only.
- `FakeDbConnectionFactory` never constructs its own connection substitute — it wraps a caller-supplied `Func<IDbConnection>` so this package never takes a hard dependency on a mocking framework.
- A capability-folder addition may be **design-ahead-of-schedule in two different senses, and they carry different consequences.** The soft sense (established since P-226/WO-036's `ActivityRecorder`): the referenced domain's own capability is merely undesigned/undispatched, but the fake needs no actual compiled type from it (e.g. a literal `ActivitySource` name string) — Core-phase implementation proceeds unblocked. The hard sense (first encountered at P-269/WO-043's `Storage/InMemoryFileStorage`, repeated at P-276/WO-044's `Search/InMemorySearchIndex`, a THIRD time at P-284/WO-045's `Intelligence/` fakes, and now a FOURTH time at P-288/WO-046's `Workflows/` fakes — the pattern is a confirmed recurring category, not a one-off — and all prior occurrences have since cleared within the same or a following Design-phase confirmation pass, so a `⚑ Blocked` marker in this domain has so far always been temporary, not permanent): the fake must `: IInterfaceName` against a real compiled type in another domain's package, and that package is a genuinely empty placeholder `.csproj` with zero source files — verified directly on disk, never assumed from that domain's `CLAUDE.md` narrative alone. **P-284 and P-288 are both variants worth naming explicitly**: `10.Intelligence`'s own Design phase (P-279) and `17.Workflows`'s own Design phase (P-287) are BOTH fully RATIFIED with a complete, zero-drift-sourceable interface contract — a stronger starting position than `08.Storage`/`09.Search` had when their own `Storage/`/`Search/` blockers were first recorded — yet the blocker still applies in full, because prose ratification in a `CLAUDE.md` is not compiled code a fake can `: Implement` against. In the hard case, Design still proceeds (target shape is sourced from the owning domain's fully-authored `CLAUDE.md`, which is authoritative regardless of code-shipped status), and Scaffold may still add the `ProjectReference` (an empty project reference always builds), but the Core/Tests/Docs tasks that require the real type must be marked `⚑` Blocked in `state-map.md`, with an explicit Cross-Domain Dependencies row naming the upstream phase key that must land first (e.g. `SK.08.Core`, `SK.09.Core` for `Search/`, `SK.10.Core` for `Intelligence/`, or `SK.17.Core` for `Workflows/`) — never silently implemented against a guessed-at interface shape, and never left as an undifferentiated `○` that looks identically "not started yet by choice" rather than "cannot start yet by necessity." **Once the upstream phase ships, re-verify directly on disk (never trust the upstream domain's own `CLAUDE.md` prose alone) and correct the blocked tasks straight back to `○` Pending** — never to `●`, since clearing the blocker only means "no longer blocked," not "implemented"; actually writing the code remains a separate future Core/Tests/Docs-phase task.
- **The hard design-ahead-of-schedule sense above assumed a whole-folder blocker through its first four occurrences; P-300/WO-049's `Cryptography/`/`FeatureManagement/` folders were the FIFTH occurrence and the FIRST *partial* one — both have since resolved (2026-07-28).** `01.Core/SharedKernel.Cryptography` was already fully `Published` — 7 of `Cryptography/`'s 8 fakes needed no upstream code to land at all; at the original Design pass only `FakeContentHasher` (and the composite `AddFakeCryptography()`, transitively) were blocked on the one still-unshipped member, `IContentHasher`. Contrast this with `FeatureManagement/`'s single fake, `FakeFeatureManager`: because it must implement `IFeatureManager` in FULL to compile, and `GetVariantAsync`/`GetVariantAsync<TContext>` did not exist on that interface at all yet (not merely "exist but reference a missing type," the way `IContentHasher` was merely absent from a DI registration call), the entire type — and by extension the entire folder — was blocked exactly like a whole-folder occurrence, even though only two of the interface's four eventual members were missing. The lesson generalizes: whether a partially-shipped upstream package produces a partial or a whole-folder block depends on WHERE the gap falls — a missing standalone interface (`IContentHasher`) blocks only the fakes that reference it; a missing member on an interface a fake must implement in full blocks that whole fake, regardless of how many of the interface's other members already exist. Both `01.Core` gaps (`SK.01.P296`, `SK.01.P298`) shipped within 24 hours of each other, and both were re-verified directly on disk during this domain's own `SK.16.Scaffold` pass — every `Cryptography/`/`FeatureManagement/` task is `○` Pending today, none `⚑` Blocked.
- Fakes for an interface with more than one keyed DI registration (e.g. `IAsymmetricSignatureService`'s unkeyed-default/`"Rsa"`-keyed/`"Ecdsa"`-keyed triad) reuse the REAL production package's own keyed-service-key `string` constants (`CryptographyServiceCollectionExtensions.RsaSignatureServiceKey`/`EcdsaSignatureServiceKey`) rather than redeclaring fake-only key strings — first applied at `Cryptography/FakeAsymmetricSignatureService`/`FakeCryptographyServiceCollectionExtensions.AddFakeCryptography()` (P-300/WO-049). This is not a sibling-capability-folder reference (the constants live in the SAME abstraction package, `SharedKernel.Cryptography`, this folder already depends on) — it ensures a test resolving a keyed fake gets the identical key a production resolution would use, with zero call-site change when swapping `AddSharedKernelCryptography()` for `AddFakeCryptography()`.
- **A container fixture almost never carries the hard blocker an in-memory fake does, even for the exact same upstream domain in the exact same work order** — reconfirmed a third time by `Containers/QdrantContainerFixture`/`MilvusContainerFixture` (P-283/WO-045): both are fully unblocked and `○` Pending, taking NO `ProjectReference` to `SharedKernel.AI.Abstractions`, while their sibling `Intelligence/` in-memory fakes (P-284, same work order, same upstream domain) are `⚑` Blocked. A container fixture exposing only flat scalar connection properties has no build-time dependency on the domain whose real provider it stands in for; an in-memory fake implementing that domain's actual interface does. When a work order bundles a container-fixture phase and an in-memory-fake phase for the same upstream domain, expect this asymmetry by default. **`Workflows/` (P-288/WO-046) is the exception that proves the rule has a boundary**: it carries the FOURTH hard blocker, but with no paired `Containers/` fixture at all — `17.Workflows` needs none, since its own testing story is Temporal's in-box `WorkflowEnvironment`/`WorkflowReplayer`, not Testcontainers. The asymmetry above only exists when a work order bundles both a fixture and a fake for the same upstream domain; when a domain's own brain states outright that it needs no container fixture, there is nothing to be unblocked separately from the fake, and the entire folder is blocked as one unit.
- `Containers/MinioContainerFixture` is the only type in `Containers/` permitted to carry an `AWSSDK.S3` reference (bucket-bootstrap only); `Containers/ElasticsearchContainerFixture` is likewise the only type permitted to carry a `Testcontainers.Elasticsearch` reference; `Containers/MeilisearchContainerFixture` is the only type permitted to use the base `Testcontainers` package's generic builder API directly (a `PackageReference`, not merely a transitive one) — `PostgreSqlContainerFixture`/`RedisContainerFixture`/`RabbitMqContainerFixture` remain isolated to their own single `Testcontainers.*` per-engine module, mirroring the existing `TestHarnessFactory`-is-the-only-`MassTransit`-reference-in-`Messaging/` rule. `Containers/QdrantContainerFixture`/`MilvusContainerFixture` (P-283/WO-045) extend this pattern with their own single-carrier `Testcontainers.Qdrant`/`Testcontainers.Milvus` references respectively — both OFFICIAL dedicated modules, so neither needs a `Meilisearch`-style hand-rolled fallback. None of the eight `Containers/` fixtures takes a `ProjectReference` to the owning domain's own abstraction package (`SharedKernel.Storage.Abstractions`, `SharedKernel.Search.Abstractions`, `SharedKernel.AI.Abstractions`), unlike `Storage/InMemoryFileStorage`, `Search/InMemorySearchIndex`, or `Intelligence/InMemoryVectorCollection` — a container fixture exposing only flat scalar connection properties has no build-time dependency on the domain whose real provider it stands in for; an in-memory fake implementing that domain's actual interface does.
- `Search/`'s in-memory `SearchFilter` evaluator is the **first documented use, outside `Domain/SpecificationAssert`, of reflection-based expression/field resolution in this package** — resolving a filter node's `Field` string to a `TDocument` public property by name at runtime. This is explicitly the SAME class of test-only reflection `SpecificationAssert`/`SpecificationTestBuilder<T>` already use via `ISpecification<T>.Criteria.Compile()` (acceptable here, never in production per `03.Domain`'s own constraint) — not a new exception to any rule, a second instance of an already-sanctioned one. `Intelligence/`'s in-memory `VectorFilter` evaluator DOES NOT need this — it resolves a filter node's `Field` string via a direct dictionary lookup into `IVectorRecord.Metadata` instead (see the `Intelligence/` Interface Contracts NOTE), since that contract already exposes fields as a `IReadOnlyDictionary<string, VectorValue>` rather than typed C# properties. Not every closed-AST filter in this package needs reflection — only ones resolving against a caller-defined typed shape do.
- `Intelligence/InMemoryVectorCollection<TRecord>` computes REAL cosine/dot-product/Euclidean similarity math for `VectorHit.Score`/`.Rank`, unlike `Search/InMemorySearchIndex<TDocument>`'s explicit non-replication of BM25/ranking-rule relevance — vector similarity is simple closed-form arithmetic a fake CAN faithfully replicate; full-text relevance ranking is not. Do not assume every in-memory fake in this package defaults to a fixed/simplified score just because `Search/`'s did — check whether the underlying scoring math is actually replicable in-memory before choosing to simplify it away.
- `Workflows/InMemoryWorkflowHandle<TResult>.GetResultAsync` never blocks, spins, or polls — it has no mechanism to ever produce a result the test did not explicitly configure via `InMemoryWorkflowDispatcher.CompleteWorkflow<TResult>`/`.FailWorkflow`, so calling it before either was invoked throws `InvalidOperationException` immediately rather than hanging. This is the domain's starkest application yet of the "no real `Task.Delay`/`Thread.Sleep`" hard rule: most fakes in this package simply have nothing to wait for (every write is immediately visible), but a workflow's eventual result is the one case where the REAL implementation is expected to await something that has not happened yet — the fake makes that impossibility explicit instead of silently deadlocking a test.
- `FakeClock` must default to a **fixed, non-real `DateTimeOffset`** — never `DateTimeOffset.UtcNow` — so any test that forgets to configure it explicitly still runs deterministically across time zones and CI machines. `TestSharedKernelDbContext`'s wired-in `IClock` follows the same rule.
- `SpecificationAssert`/`SpecificationTestBuilder<T>` use `ISpecification<T>.Criteria.Compile()` — reflection-based expression compilation, acceptable in this test-only package, **never** acceptable in production code per `03.Domain`'s own documented constraint on `Specification<T>.IsSatisfiedBy`.
- `FakeTenantResolutionStrategy` (`ServiceDefaults/`) is **structurally compatible** with `13.ServiceDefaults`'s `ITenantResolutionStrategy`, not a direct interface implementation — this package takes no project reference to `SharedKernel.MultiTenancy`. The same "structural, not direct" pattern applies to any future fake whose owning interface lives in a package this domain has chosen not to reference.
- No static mutable state anywhere in this domain, with two documented exceptions: `Bogus.Randomizer.Seed` set via `FakerSeeding.Apply` (a deliberate, opt-in, process-wide determinism convention), and the single static, always-sampling `ActivityListener` registered by `AmbientActivityTestHelper` scoped to its own private `ActivitySource` (required so `ActivitySource.StartActivity` returns a real `Activity` in pure unit tests with no OTel host listening). Both are deliberate, opt-in, process-wide — never incidental shared state.
- `Caching/FakeRedisChannelService` (P-306/WO-050) is the first fake in this package to perform genuine in-process cross-call interaction rather than pure passive recording — `PublishAsync` synchronously invokes every currently-subscribed handler for the same channel, so a publish/subscribe/unsubscribe round trip can be proven in a single process with no real Redis. Every other `Caching/` fake (and most fakes in this package generally) only records what was called; `FakeCacheInvalidationBus.OnInvalidation` comes closest but still requires the CALLER to wire the chained effect manually, whereas `FakeRedisChannelService` performs the fan-out itself.
- `Caching/FakeRedisHashService`/`FakeTypedHashStore<T>`'s `IncrementFieldAsync` throws `InvalidCastException` when the target field currently holds a value that is not a `long` — a fake-only guard standing in for the `WRONGTYPE` error a real Redis `HINCRBY` against a non-numeric field would raise; it is the first place in this domain a fake throws to surface a caller-side type-mismatch bug rather than returning a `default`/no-op.
- `Caching/FakeCacheWarmupStrategy`'s cross-instance execution-order proof is a caller-supplied `ConcurrentQueue<string>` passed to every constructor that should share one — never a static field. This is the domain's established pattern for proving multi-instance ordering without adding a third exception to the `No static mutable state` rule above.
- `Caching/FakeTypedHashStore<T>` is deliberately independent of `Caching/FakeRedisHashService` — both maintain their own separate backing store — continuing this folder's existing convention that its (now six) fakes share no internal state with each other.

---

## DI Registration (expected shape)

```csharp
// Messaging test doubles — singleton by design, see Implementation Rules
services.AddInMemoryMessageBus();
services.AddInMemoryEventPublisher();

// Structured log capture — singleton ILoggerFactory + real ILogger<> resolution via the BCL
// Logger<> adapter, mirroring the singleton rationale above (P-258/WO-041)
services.AddInMemoryLoggerFactory();

// Caching fakes — one call registers all four as singletons
services.AddFakeCachingServices();

// Caching fakes — manual per-fake alternative remains valid for callers who want a subset
services.AddSingleton<ICacheService, FakeCacheService>();
services.AddSingleton<IDistributedLockService, FakeDistributedLockService>();
services.AddSingleton<ITenantCacheKeyProvider, FakeTenantCacheKeyProvider>();
services.AddSingleton<ICacheInvalidationBus, FakeCacheInvalidationBus>();

// Domain test helpers — registers FakeClock as IClock only
services.AddFakeDomainServices();

// Application local-seam doubles — one call satisfies ApplicationBehaviorsBuilder's Build()-time
// missing-dependency guards for Transaction/Authorization/Idempotency behaviors (WO-040)
services.AddFakeApplicationBehaviorServices();

// Replay-capable idempotency store — manual override in place of the bundled non-replay default
services.AddSingleton<IIdempotencyKeyStore, FakeIdempotencyResponseStore>();

// Object storage doubles — singleton lifetime matches the production registration exactly (P-269/WO-043)
services.AddInMemoryFileStorage();

// Search index double — SINGLETON, a deliberate deviation from the real scoped AddIndex<TDocument>
// registration (see Search/ Interface Contracts); call once per TDocument the test needs indexed
services.AddInMemorySearchIndex<MyDocument>(myDefinition);

// Search provisioner + descriptor doubles — one call bundles both, singleton, matches real lifetime
services.AddInMemorySearchProvisioning();

// Intelligence doubles (P-284/WO-045)
services.AddInMemoryEmbeddingGenerator("test-model", dimension: 1536);
services.AddInMemoryVectorCollection<MyRecord>(myDefinition);
services.AddInMemoryVectorProvisioning();
services.AddInMemorySemanticKernel();

// Workflow dispatch double (P-288/WO-046) — SINGLETON, a deliberate deviation from the real
// scoped IWorkflowDispatcher registration, mirroring the Messaging/Search/Intelligence precedent
services.AddInMemoryWorkflowDispatcher();

// Cryptography fakes (P-300/WO-049, implemented C-85–C-93/SK.16.Core) — one call registers all
// eight as singletons (IOneWayHasher, IEncryptionKeyProvider, ISymmetricEncryptionService,
// IAsymmetricKeyProvider, IAsymmetricSignatureService (+ both Rsa/Ecdsa keyed slots), IHmacSigner,
// ISecureRandomGenerator, IContentHasher)
services.AddFakeCryptography();

// Cryptography fakes — manual per-type alternative remains valid, mirroring AddFakeCachingServices()'s
// documented manual-alternative precedent for callers wanting only a subset
services.AddSingleton<IOneWayHasher, FakeOneWayHasher>();
services.AddSingleton<IEncryptionKeyProvider, FakeEncryptionKeyProvider>();
services.AddSingleton<ISymmetricEncryptionService, FakeSymmetricEncryptionService>();
services.AddSingleton<IAsymmetricKeyProvider, FakeAsymmetricKeyProvider>();
services.AddSingleton<IAsymmetricSignatureService, FakeAsymmetricSignatureService>();
services.AddSingleton<IHmacSigner, FakeHmacSigner>();
services.AddSingleton<ISecureRandomGenerator, FakeSecureRandomGenerator>();
services.AddSingleton<IContentHasher, FakeContentHasher>();

// Feature-flag fake (P-300/WO-049, implemented C-94/C-95/SK.16.Core) — registers
// IFeatureManager → FakeFeatureManager (IsEnabledAsync + GetVariantAsync/GetVariantAsync<TContext>)
services.AddFakeFeatureManagement();

// Redis-adjacent caching fakes (P-306/WO-050, implemented C-96–C-100/SK.16.Core) —
// AddFakeCachingServices() extended to also register IRedisChannelService/IRedisHashService as
// singletons (six caching fakes in one call)
services.AddFakeCachingServices();

// Typed hash store fake — call once per T needed, mirrors AddTypedHashStore<T>(JsonTypeInfo<T>)
services.AddFakeTypedHashStore<MyDto>();

// Cache warmup strategy fake — call once per named strategy (plain AddSingleton, CORRECTED from an
// earlier TryAddEnumerable draft — see the Caching/ Interface Contracts block for why TryAddEnumerable
// cannot work here); pass the SAME executionLog queue across calls to observe order
services.AddFakeCacheWarmupStrategy("warm-catalog", order: 0, executionLog);
```

Fakes in `Security/`, `Persistence/`, `Clocks/` (outside `AddFakeDomainServices()`'s narrow `IClock` registration), `Contracts/`, `Communication/`, and `ServiceDefaults/` are intentionally **not** wrapped in `Add*` DI extensions — they are simple `new`-able classes or static helpers with test-controlled constructor parameters, and registering them via DI adds indirection most unit tests don't need. Only doubles that exist specifically to be swapped in for a production DI registration (caching, messaging, the single `IClock` registration in `AddFakeDomainServices()`, `Application/`'s three-fake bundle, `Storage/`'s two-fake bundle, `Search/`'s per-index call plus its provisioning bundle, `Intelligence/`'s per-embedding-generator call, per-collection call, vector-provisioning bundle, and semantic-kernel bundle, `Workflows/`'s single dispatcher call, `Cryptography/`'s eight-fake `AddFakeCryptography()` bundle, `FeatureManagement/`'s single `AddFakeFeatureManagement()` call, and now `Caching/`'s extended `AddFakeCachingServices()` bundle plus the new per-`T` `AddFakeTypedHashStore<T>()` and per-strategy `AddFakeCacheWarmupStrategy()` calls) ship a convenience extension — justified for the two newest folders because their real production counterparts (`AddSharedKernelCryptography()`, `AddSharedKernelFeatureManagement()`) are themselves `Add*`-registered, unlike `Security/`'s `IUserContext`/`ITenantProvider`, which production never registers via a single bundled call either; justified for the three new `Caching/` extensions because their real production counterparts (`AddRedisChannelService`, `AddRedisHashService`, `AddTypedHashStore<T>`, `AddCacheWarmup<TStrategy>`) are likewise all `Add*`-registered in `02.Caching`. `AddFakeContractsServices()` is explicitly **deferred** (P-064/WO-012) — none of the `Contracts/` helpers currently need DI registration; add it only if a concrete need surfaces. `ApplicationPipelineTestHarness` (`Application/`) is deliberately **not** DI-registered — it is a directly `new`-able builder/harness type, consistent with `SpecificationTestBuilder<T>`/`ProjectionSpecificationBuilder<TAggregate,TResult>`'s existing convention for builder-shaped types. `Containers/MinioContainerFixture`/`MeilisearchContainerFixture`/`ElasticsearchContainerFixture` are likewise never DI-registered — container fixtures are consumed via xUnit `ICollectionFixture<T>`, never a DI container, consistent with `PostgreSqlContainerFixture`/`RedisContainerFixture`/`RabbitMqContainerFixture`.

---

## AOT Compatibility

AOT guidance does **not** apply to this domain. `16.Testing` packages are never referenced by production code (root `CLAUDE.md` Hard Rule) and are never published as part of an AOT-compiled service. Fakes, faker conventions, and container fixtures may freely use reflection-based conveniences (e.g., Bogus's expression-tree rule builders, `Activator`-based test object construction) without AOT review. This section exists only for consistency with the other domain brains' structure.

---

## Test Rules

- **This package has no nested `.Tests` project of its own.** A fake's correctness is defined entirely by its conformance to the interface it implements — best verified in the `.Tests` project of the domain that owns that interface, where the real implementation's contract tests already exist as a comparison baseline. Today: `FakeCacheService`/`FakeDistributedLockService`/`FakeRenewableLock`/`FakeTenantCacheKeyProvider` are exercised through `02.Caching`'s own test suites (e.g., `SharedKernel.Caching.Redis.DistributedLocking.Tests` references `FakeRenewableLock` directly via `ProjectReference`), not through a dedicated `SharedKernel.Testing.Tests` project. This is a deliberate, documented exception to the platform's otherwise-universal "every package gets a nested `.Tests` project" rule.
- **`SharedKernel.Testing.SelfTests` (added WO-029) is a narrow, documented exception to the rule above.** It exists exclusively for standalone testing-infrastructure logic that has no consuming-domain-owned interface to anchor against — fluent builders (`SpecificationTestBuilder`, `ProjectionSpecificationBuilder`), assertion-helper classes (`PagedListAssertions`, `DomainVersionAssertions`, `HealthCheckAssertionExtensions`), faker-seeding conventions, and recorder/double self-checks (e.g., `InMemoryMessageBus`'s own `ShouldHavePublished` behavior). Decision rule for every future addition to this domain: does the type implement an interface owned by another numbered domain? If yes, it is still proven via that domain's own contract tests — unchanged, per the rule above. If no — it has no owning domain interface to anchor against — it is proven in `SharedKernel.Testing.SelfTests` instead. This project carries the Standard Test Package Set (xUnit runner, FluentAssertions, NSubstitute) as direct package references; those dependencies must never leak into `SharedKernel.Testing.csproj` itself.
- When a new fake is added to this package, the consuming domain's existing contract/behavioral tests for the real interface are the acceptance bar — the fake must satisfy the same documented pre/post-conditions (e.g., `FakeCacheService.GetManyAsync` must return a dictionary entry for every requested key, exactly like the production `FusionCacheService`/`RedisL2BatchService` behavior documented in `02.Caching`).
- New container fixtures must be proven against the integration test suite of the domain that needs them first (e.g., a new `RedisContainerFixture` proves itself by replacing whatever ad-hoc Testcontainers setup `02.Caching.Redis.Tests` currently rolls inline) before being treated as the canonical shared fixture.
- Any behavioral fix to an existing fake (e.g., correcting `FakeCacheService`'s tag-eviction semantics) must be cross-checked against every domain's test suite that currently consumes it — grep for the fake's type name across `**/*.Tests/` before changing its public behavior, since multiple domains' test suites assert against it without their own copy.
- Determinism is non-negotiable: any test failure traced back to a fake's internal use of real time, real randomness, or real I/O (outside the deliberate `Containers/` fixtures) is a bug in the fake, not a flaky test to retry.
- `StaticTenantProvider` (implements `ITenantProvider` directly) is proven against `12.Security`'s own `ITenantProvider` contract-shape tests where one exists; `SharedKernel.Testing.SelfTests` is the documented fallback only when no such generic contract-shape test exists there.
- `FakeTenantResolutionStrategy` is proven in `SharedKernel.Testing.SelfTests` unconditionally — it is structurally compatible with `ITenantResolutionStrategy`, not a direct implementation, so there is no owning-domain interface to anchor a "prove it there" rule against.
- Cross-cutting `Communication/` test doubles (`MockServiceEndpointResolver` excepted — see below) are proven in `SharedKernel.Testing.SelfTests` because no single `11.Communication` package owns all of them collectively; `MockServiceEndpointResolver` implements `IServiceEndpointResolver` directly and may additionally be exercised by `11.Communication.Internal`'s own suite if a duplicate-removal pass (see `Communication/` SCOPE LOCK note) wires it in.
- **The "prove it in the owning domain's suite" rule has a practical fallback, observed repeatedly during the Tests phase: if no consuming domain's existing `.Tests` project actually exercises the fake/fixture directly, prove it in `SharedKernel.Testing.SelfTests` instead, even when the type implements an interface owned elsewhere.** Confirmed cases: `FakeClock` — `01.Core` can never take a `ProjectReference` to `16.Testing` (it sits below this domain in the layering rules and references nothing), so `SharedKernel.Primitives.Tests` necessarily rolls its own private nested fake rather than referencing this package's `FakeClock`; `FakeCacheService.GetManyAsync`/`SetManyAsync`, `FakeTenantCacheKeyProvider`, `FakeCacheInvalidationBus` — no `02.Caching` test project exercised these three directly (only incidental DI-registration usage existed); `TestSharedKernelDbContext`, `AggregateRootFaker`/`TenantedAggregateFaker`, `EfContextExtensions` — net-new types with no consumer yet in `06.Persistence.EfCore.Tests` despite its `ProjectReference` to this package; `StaticTenantProvider` — `12.Security.Abstractions.Tests` carries no `ProjectReference` to this package and no generic `ITenantProvider` contract-shape test exists there. This is not a workaround — it is the documented fallback the original phase specs (P-181, P-187) anticipated; treat "prove it there" as the default and "no actual consumer exists yet" as the trigger for the `SelfTests` fallback, re-checked at the time each fake is proven rather than assumed from the interface's owning domain alone.
- **Before adding a new fake/fixture for a downstream domain's test need, audit this package's existing surface first — do not assume a gap exists.** Demonstrated by P-226/WO-036: `05.Application`'s `TracingBehavior`/`CacheInvalidationBehavior` (both design-only, WO-036) looked at first glance like they might need two new fakes, but the audit found `FakeCacheService` already fully covers `CacheInvalidationBehavior`'s `ICacheService.RemoveAsync`/`RemoveByTagAsync` assertion need with zero new code, while only `TracingBehavior`'s span-recording need was a genuine, narrow gap (`AmbientActivityTestHelper` is an ambient-context *setter*, not a span-recording *listener* — a different capability, not a duplicate), closed by one small additive type (`ActivityRecorder`). Grep this package's existing types and their documented capabilities before designing a new fake; "the interface is owned elsewhere" does not by itself imply "no fake exists yet."
- `ActivityRecorder` (`Communication/`) is proven in `SharedKernel.Testing.SelfTests` unconditionally — it implements no consuming-domain-owned interface (`ActivitySource`/`ActivityListener` are BCL, not a SharedKernel abstraction), mirroring `AmbientActivityTestHelper`'s own routing in the same folder.
- **A live `ProjectReference` from a consuming domain's `.Tests` project to `SharedKernel.Testing` does not by itself mean a net-new fake should route there.** `SharedKernel.Application.Behaviors.Tests.csproj` already carries a `ProjectReference` to `SharedKernel.Testing` (confirmed by reading the `.csproj` directly, WO-040) — yet `FakeUnitOfWork`/`FakeAuthorizationContext`/`FakeIdempotencyKeyStore`/`FakeIdempotencyResponseStore`/`ApplicationPipelineTestHarness` (`Application/`) still route to `SharedKernel.Testing.SelfTests`, because they are net-new at the time of writing with zero existing consumer — the same reasoning already applied to `TestSharedKernelDbContext`/`AggregateRootFaker`/`EfContextExtensions` (T-19/T-20/T-21) despite `SharedKernel.Persistence.EfCore.Tests` also carrying a live reference. The rule is "is this type actually consumed there today," never "could it theoretically be consumed there." Re-check at implementation time, every time — a fake proven in `SelfTests` today may later be genuinely adopted by its owning domain's suite, at which point that becomes a documented cross-domain follow-up (never a file edit performed by this domain), not a retroactive routing change here.
- `LogRecord`/`InMemoryLogger`/`InMemoryLogger<TCategoryName>`/`InMemoryLoggerFactory`/`LoggerAssertions` (`Logging/`, P-258/WO-041) are proven in `SharedKernel.Testing.SelfTests` unconditionally — this is a net-new capability with zero existing consumer in any domain's own `.Tests` project (no WO-041 domain retrofit to `[LoggerMessage]`-based logging has shipped yet — all ten domain phases are `○` Pending as of this design pass, including `01.Core`'s own `LoggingEventIdRanges` registry), so there is no owning-domain suite to "prove it there" against, consistent with the established no-consumer-yet fallback (`FakeClock`, `TestSharedKernelDbContext`, `Application/`'s six types, etc.). The `SelfTests` coverage for this folder must exercise a REAL `[LoggerMessage]`-attributed test-only call site, never a hand-written `ILogger.Log(...)` call standing in for one — per this phase's explicit acceptance criterion.
- `Containers/MinioContainerFixture` (P-268/WO-043) is proven FIRST in `SharedKernel.Testing.SelfTests` (Docker-gated fixture-mechanics tests — `ServiceUrl` throws before `InitializeAsync`, pinned tag, `DefaultBucket` exists after startup, property shape binds directly to `S3StorageOptions`/`ObsStorageOptions`), mirroring the existing routing for `PostgreSqlContainerFixture`/`RedisContainerFixture`/`RabbitMqContainerFixture` (this package's own Docker-gated `Containers/` lifecycle tests already established that pattern). Adoption by `SharedKernel.Storage.S3.Tests`/`SharedKernel.Storage.Obs.Tests` — the ones that actually "prove it as the canonical shared fixture" per the general container-fixture rule above — happens once those `.Tests` projects exist and land their own provider-round-trip suites; that is an explicit cross-domain follow-up for a future `08.Storage` implementer pass, not performed here (this domain never touches a `.Tests` project, in this domain or any other).
- `Storage/InMemoryFileStorage`/`InMemoryBlobUriGenerator`/`AddInMemoryFileStorage()` (P-269/WO-043) are proven in `SharedKernel.Testing.SelfTests` — net-new capability, zero existing consumer: `08.Storage`'s own test suite exercises its REAL providers against a `MinioContainerFixture`-backed container and never mocks `IFileStorage` itself (per `08.Storage`'s own Test Rules), and no downstream microservice `.Tests` project exists in this mono-repo yet to consume the fake either. **The hard compile-time blocker documented in the `Storage/` Interface Contracts section has CLEARED**, re-verified directly on disk as of this Design-phase confirmation pass (2026-07-17) — `08.Storage/SharedKernel.Storage.Abstractions` now ships real, compiled `IFileStorage`/`IBlobUriGenerator`/model/`StorageErrors` types, and `08.Storage`'s own `state-map.md` confirms `SK.08.Core` is `●` 30/30. C-61–C-63/T-47/DO-18 are corrected from `⚑` Blocked back to `○` Pending in `state-map.md` — actually implementing them remains a future Core/Tests/Docs-phase session's work, out of scope for this Design-confirmation pass. **T-46/T-47 IMPLEMENTED (2026-07-18)**: `Containers/MinioContainerFixtureTests.cs` (6 tests) and `Storage/InMemoryFileStorageTests.cs`/`InMemoryBlobUriGeneratorTests.cs`/`AddInMemoryFileStorageTests.cs` (18 tests) all landed in `SharedKernel.Testing.SelfTests`, 24 tests total. `dotnet test` (excluding `Containers/`, no Docker daemon in this session's environment) passed 332/332 with zero regressions to the 311 pre-existing tests.
- **All four `Containers/` fixture builders validate Docker connectivity eagerly inside the fixture's own constructor, not lazily at `InitializeAsync`** — discovered while writing `MinioContainerFixtureTests.cs` and independently reproduced against the three pre-existing sibling fixtures for confirmation. `MinioBuilder`/`PostgreSqlBuilder`/`RedisBuilder`/`RabbitMqBuilder.Build()` all throw `System.ArgumentException` ("Docker is either not running or misconfigured") the instant `new XyzContainerFixture()` runs if no Docker daemon is reachable — meaning even a fixture's pre-initialize property-throw tests (`ServiceUrl` throws `InvalidOperationException` before `InitializeAsync`) require a live Docker daemon to execute at all, not just the full-lifecycle test. This is Testcontainers' own builder-validation behavior, not something any fixture in this package controls or could change; it is not a defect, just an operational fact worth knowing before assuming a "throws before initialize" test is Docker-independent.
- **`SharedKernel.Testing.SelfTests.csproj` does NOT carry the `HotChocolate.Data`→`GreenDonut.Result<TValue>` transitive-ambiguity landmine that `SharedKernel.Testing.csproj` has** (see the `Result<T>` fully-qualification note on `Storage/InMemoryFileStorage.cs`/`InMemoryBlobUriGenerator.cs` above). Global usings generated for a package reference are scoped to the project that declares the reference — they do not propagate through a `ProjectReference` to a consuming project. Confirmed by a clean build using bare, unqualified `Result<T>` throughout every `Storage/` test file in `SelfTests` — no `SharedKernel.Primitives.Results.Result<T>` fully-qualification workaround is needed there, unlike in the production package's own `Storage/` folder.
- `Containers/MeilisearchContainerFixture`/`ElasticsearchContainerFixture` (P-275/WO-044) **implemented 2026-07-20 (C-64/C-65)**, proven 2026-07-20 (T-48/T-49): `Containers/MeilisearchContainerFixtureTests.cs` (3 tests) and `Containers/ElasticsearchContainerFixtureTests.cs` (3 tests) are now committed in `SharedKernel.Testing.SelfTests`, mirroring the existing `PostgreSqlContainerFixture`/`RedisContainerFixture`/`RabbitMqContainerFixture`/`MinioContainerFixture` Docker-gated pattern exactly — `.Url`/`.ApiKey`/`.Nodes` throw-before-`InitializeAsync`, full-lifecycle smoke tests (Meilisearch: unauthenticated `GET /health` + authenticated `GET /indexes`; Elasticsearch: root-endpoint `version.number` starts with `"9."`, proving the pinned 9.4.2 image over the module's own 8.6.1 default, plus a `/_cluster/health` call proving the explicit post-start poll genuinely resolved before `InitializeAsync` returned), and clean `DisposeAsync` shutdown. Run and passing against a real Docker daemon. Adoption by `SharedKernel.Search.Meilisearch.Tests`/`SharedKernel.Search.ElasticSearch.Tests` — the ones that actually "prove it as the canonical shared fixture" per the general container-fixture rule above — remains an explicit cross-domain follow-up for a future `09.Search` implementer pass, not performed here (this domain never touches a `.Tests` project, in this domain or any other).
- `Search/InMemorySearchIndex<TDocument>`/`InMemorySearchIndexProvisioner`/`InMemorySearchProviderDescriptor`/both `Add*` DI extensions (P-276/WO-044) **implemented 2026-07-20 (C-66–C-69)**, proven 2026-07-20 (T-50/T-51): `Search/InMemorySearchIndexTests.cs` (38 tests), `Search/InMemorySearchIndexProvisionerTests.cs` (16 tests), `Search/InMemorySearchProviderDescriptorTests.cs` (12 tests), and `Search/SearchServiceCollectionExtensionsTests.cs` (9 tests) are now committed in `SharedKernel.Testing.SelfTests` (net-new capability, zero existing consumer — no `SharedKernel.Search.*.Tests` project exists in this mono-repo yet, confirmed on disk via `grep`). Coverage includes the full write/read/corpus-walk contract, `SearchAsync`'s complete 5-step fail-loud validation pipeline with an explicit check-order proof, a dedicated test exercising all 8 `SearchFilter` AST node kinds, tenant-checked `GetAsync`, idempotent/additive-only `EnsureIndexAsync` incl. `IndexDefinitionConflict`, `CutoverAsync`'s `DeleteStagingAfterCutover` semantics both ways, `.Validate`'s pre-flight parity with `SearchAsync`'s own pipeline, and both DI extensions' singleton registration shape. All 75 tests passing.
- `Containers/QdrantContainerFixture`/`MilvusContainerFixture` (P-283/WO-045) **implemented 2026-07-22 (C-70–C-72)**, proven 2026-07-22 (T-52/T-53): both fixtures landed exactly per D-122/D-123/D-125's design, both candidate image tags (`qdrant/qdrant:v1.13.4`, `milvusdb/milvus:v2.3.10`) reconfirmed via `docker manifest inspect` at implementation time with zero drift, and both smoke-tested end-to-end against a real Docker daemon (pre-`InitializeAsync` throw guard, container start, endpoint reachability, clean `DisposeAsync`) — neither module's own built-in wait strategy showed a readiness race, so neither fixture needed an `ElasticsearchContainerFixture`-style post-start poll override. `Containers/QdrantContainerFixtureTests.cs` (4 tests) and `Containers/MilvusContainerFixtureTests.cs` (3 tests) are now committed in `SharedKernel.Testing.SelfTests`, mirroring the established Docker-gated pattern: `.GrpcEndpoint`/`.HttpEndpoint`/`.Endpoint` throw-before-`InitializeAsync`; full-lifecycle smoke tests (Qdrant: unauthenticated `GET /readyz` + `GET /collections` over `.HttpEndpoint`; Milvus: `GET /healthz` over the management port); a **bare gRPC-channel-open probe** against `.GrpcEndpoint`/`.Endpoint` on both, implemented as a plain `System.Net.Sockets.TcpClient` connect (a gRPC channel is fundamentally a TCP connection to an HTTP/2 endpoint, so this proves reachability with zero `Qdrant.Client`/`Milvus.Client`/protobuf dependency — consistent with both fixtures' own "no `ProjectReference` to `SharedKernel.AI.*`" isolation). **Two new test-authoring techniques surfaced, worth recording for future `Containers/` test-writers**: (1) `MilvusContainerFixture` exposes no management-port (9091) property of its own — its host-mapped port has no fixed/derivable relationship to the gRPC port's own mapped port (confirmed empirically: the offset varies run to run) — so `MilvusContainerFixtureTests` reads it via the underlying `Testcontainers.Milvus.MilvusContainer`'s own public `GetMappedPublicPort(int)`, reached through the fixture's private `_container` field via reflection (`BindingFlags.NonPublic | BindingFlags.Instance`) since there is no other way to reach it without changing the already-shipped fixture's public surface — this is the same class of test-only reflection already sanctioned by `Domain/SpecificationAssert` (never acceptable in production). (2) The "no external etcd/MinIO container" acceptance criterion is proven via a `docker ps --format "{{.Image}}"` snapshot taken immediately before `InitializeAsync` and again immediately after, asserting no image containing `"etcd"`/`"minio"` appears in the diff — reading the child process's stdout AND stderr concurrently via `Task.WhenAll` (never sequentially — a sequential read risks a classic pipe-buffer deadlock if the child writes enough to the undrained stream; this was hit and fixed during implementation using `docker logs` before switching to the smaller-output `docker ps` command). This diff check has a documented, narrow residual race if a concurrently running sibling fixture test (e.g. `MinioContainerFixtureTests`) starts within the same ~7-second window, since this project has no test-parallelization override and xUnit parallelizes across test classes by default — accepted as a pragmatic trade-off, not eliminated. Adoption by `SharedKernel.AI.Qdrant.Tests`/`SharedKernel.AI.Milvus.Tests` (`10.Intelligence`'s own T-03/T-05, neither project existing on disk yet) is an explicit cross-domain follow-up for a future `10.Intelligence` implementer pass, not performed here.
- `Intelligence/InMemoryEmbeddingGenerator`/`InMemoryVectorCollection<TRecord>`/`InMemoryVectorCollectionProvisioner`/`InMemoryVectorProviderDescriptor`/`InMemorySemanticKernel`/`InMemoryCompletionProviderDescriptor`/`IntelligenceServiceCollectionExtensions` (P-284/WO-045) **implemented (C-73–C-79, 2026-07-22) and proven (T-54, 2026-07-22)**: `SharedKernel.Testing.SelfTests/Intelligence/` (`IntelligenceTestFixtures.cs` + `InMemoryEmbeddingGeneratorTests.cs`/`InMemoryVectorCollectionTests.cs`/`InMemoryVectorCollectionProvisionerTests.cs`/`InMemoryVectorProviderDescriptorTests.cs`/`InMemorySemanticKernelTests.cs`/`InMemoryCompletionProviderDescriptorTests.cs`/`IntelligenceServiceCollectionExtensionsTests.cs`, 119 tests) — net-new capability, zero existing consumer (no `SharedKernel.AI.*.Tests` project exists in this mono-repo yet, confirmed via `10.Intelligence/state-map.md`'s own Package Board), so this is the only behavioral proof today, per the SelfTests routing rule. Coverage includes the full write/read/scroll contract, all 8 `VectorFilter` AST node kinds, tenant-scope enforcement (incl. `GetAsync`'s tenant-checked-not-fail-closed behavior), fail-loud model-identity/dimension rejection with no-I/O proof, Score/Rank directionality for all three `VectorDistanceMetric` values (incl. a magnitude-sensitive dot-product-vs-cosine divergence case), provisioner/descriptor parity tests, `InMemorySemanticKernel`'s canned-response/streaming/failure-injection paths, and DI-registration/non-coupling proofs. Adoption by `SharedKernel.AI.Qdrant.Tests`/`.Milvus.Tests`/`.SemanticKernel.Tests` (none existing on disk yet) remains a future cross-domain follow-up, not performed here.
- `Workflows/InMemoryWorkflowDispatcher`/`InMemoryWorkflowExecution`/`InMemoryWorkflowHandle`/`InMemoryWorkflowHandle<TResult>`/`WorkflowServiceCollectionExtensions` (P-288/WO-046) **implemented (C-80–C-84, 2026-07-23) and proven (T-55, 2026-07-24)**: against the live `SharedKernel.Workflows.Temporal` source, zero drift except two corrections (`GetHandle` throws `ArgumentException` rather than returning a `Result`; `WorkflowLifecycleStatus` is `public`, not `internal`) — see the `Workflows/` Interface Contracts block above. `SharedKernel.Testing.SelfTests/Workflows/` (`WorkflowsTestFixtures.cs` marker types + `InMemoryWorkflowDispatcherTests.cs`/`InMemoryWorkflowHandleTests.cs`/`InMemoryWorkflowHandleOfTTests.cs`/`WorkflowServiceCollectionExtensionsTests.cs`/`WorkflowsCsprojScopeLockTests.cs`, 85 tests) is the only behavioral proof today, per the same no-consumer-yet fallback as every other net-new capability in this package's history — `SharedKernel.Workflows.Temporal.Tests` (authored concurrently by a separate session, 158/158 passing as of 2026-07-23) tests workflow/activity/replay/propagation behavior against a real `WorkflowEnvironment` per `17.Workflows`'s own Test Rules, not against this fake, and carries no reference to `SharedKernel.Testing`; adoption there remains a future cross-domain follow-up, not automatically assumed. One DI-registration nuance surfaced during test-authoring, worth recording: `AddInMemoryWorkflowDispatcher()` registers `InMemoryWorkflowDispatcher` **only** as `IWorkflowDispatcher` — unlike some sibling `Add*` extensions elsewhere in this package, the concrete type is not separately resolvable from the container; the singleton proof must be made through the interface. Two xUnit `Assert.Throws<T>` exact-type corrections were needed during test-authoring (not fake bugs): `ArgumentException.ThrowIfNullOrWhiteSpace` throws the `ArgumentNullException` subclass specifically for a null argument (`ArgumentException` itself for whitespace), so `GetHandle`/`GetHandle<TResult>`/`TerminateAsync`'s null-argument test cases assert the subclass. Full regression `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` (real Docker daemon) passes 640/640 (614 non-container + 26 Docker-gated `Containers/`). **This closes `SK.16.Tests` (55/55) and, since every other phase was already `●`, the entire `16.Testing` domain end to end.**
- `Cryptography/FakeOneWayHasher`/`FakeSecureRandomGenerator`/`FakeEncryptionKeyProvider`/`FakeAsymmetricKeyProvider`/`FakeSymmetricEncryptionService`/`FakeAsymmetricSignatureService`/`FakeHmacSigner`/`FakeContentHasher`/`FakeCryptographyServiceCollectionExtensions.AddFakeCryptography()` and `FeatureManagement/FakeFeatureManager`/`FakeFeatureManagementServiceCollectionExtensions.AddFakeFeatureManagement()` (P-300/WO-049) are all proven in `SharedKernel.Testing.SelfTests` unconditionally — the SECOND occurrence of `FakeClock`'s exact routing rationale (the first): `01.Core` sits below `16.Testing` in the layering rules and references nothing, so it can never take a `ProjectReference` back to this package, meaning `SharedKernel.Cryptography.Tests`/`SharedKernel.FeatureManagement.Tests` will keep rolling their own private nested fakes (as `SharedKernel.Cryptography.Tests` already does today with its internal `InMemoryEncryptionKeyProvider`/`InMemoryAsymmetricKeyProvider`) regardless of what ships here — there is no "prove it in the owning domain's suite" path available at all for this domain, not merely a "no consumer yet" gap that might later close. **IMPLEMENTED AND PROVEN (T-56–T-59, 2026-07-28)**: `SharedKernel.Testing.SelfTests/Cryptography/` (`FakeOneWayHasherTests.cs`/`FakeSecureRandomGeneratorTests.cs`/`FakeEncryptionKeyProviderTests.cs`/`FakeAsymmetricKeyProviderTests.cs`/`FakeSymmetricEncryptionServiceTests.cs`/`FakeAsymmetricSignatureServiceTests.cs`/`FakeHmacSignerTests.cs`/`FakeContentHasherTests.cs`/`FakeCryptographyServiceCollectionExtensionsTests.cs`) and `SharedKernel.Testing.SelfTests/FeatureManagement/` (`FakeFeatureManagerTests.cs`/`FakeFeatureManagementServiceCollectionExtensionsTests.cs`), 93 tests total, all passing. Coverage includes hash/verify round-trips and rehash-needed simulation via `.Iterations` mutation; non-seeded non-determinism vs. seeded reproducibility for `FakeSecureRandomGenerator`; multi-key rotation/current-key switching/removal for `FakeEncryptionKeyProvider`; RSA/ECDSA per-`keyId` caching, cloned-handle-safe-to-dispose proof, and a genuine `ObjectDisposedException` proof of `Dispose` cleanup for `FakeAsymmetricKeyProvider`; encrypt/decrypt round-trip, unknown-`KeyId` and `SimulateDecryptFailure` failure parity with the real `CryptographyErrorCodes` for `FakeSymmetricEncryptionService`; sign/verify round-trip and tamper detection uniform across the unkeyed default and both real `RsaSignatureServiceKey`/`EcdsaSignatureServiceKey` slot names for `FakeAsymmetricSignatureService`; sign/verify round-trip, tamper detection, and introspection for `FakeHmacSigner`; deterministic-digest/single-byte-divergence/`byte[]`-`Stream`-async parity for `FakeContentHasher`; all eight `AddFakeCryptography()` singleton registrations (incl. all three `IAsymmetricSignatureService` slots) resolving to working fakes, with the manual per-type registration alternative proven standalone; and `FakeFeatureManager`'s boolean/variant override round-trips, unconfigured-feature-defaults-closed/`FeatureVariant.Unassigned`-fallback behavior, `Reset`, and `AddFakeFeatureManagement()`'s singleton registration. **One CLAUDE.md drift corrected during this pass**: the `FeatureManagement/` Interface Contracts block's `GetVariantAsync` description had drafted a fake-only fallback shape (`.Name = "Default"`, `.Configuration = null`) that the shipped code never actually implements — the real `FakeFeatureManager.GetVariantAsync`/`GetVariantAsync<TContext>` fall back to the REAL `FeatureVariant.Unassigned` sentinel (`.Name = "Unassigned"`) via `?? FeatureVariant.Unassigned`, exactly reusing `01.Core`'s own production fallback rather than inventing a fake-only one — corrected in place. `dotnet build`/`dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` (real Docker daemon) passes 733/733 (640 pre-existing + 93 net new), zero regressions. No consuming-domain regression check applies — neither `SharedKernel.Cryptography.Tests` nor `SharedKernel.FeatureManagement.Tests` references `SharedKernel.Testing`'s fakes (both keep their own private internal doubles, per the layering constraint above). Both folders' `[STATUS: Planned]` markers had already been removed from their Interface Contracts blocks during the preceding `SK.16.Core` pass — this Tests-phase pass adds proof, not a further status flip.
- `Caching/FakeRedisChannelService`/`FakeRedisHashService`/`FakeTypedHashStore<T>`/`FakeCacheWarmupStrategy` and their three DI extensions (`AddFakeCachingServices()` extended, `AddFakeTypedHashStore<T>()`, `AddFakeCacheWarmupStrategy()`) (P-306/WO-050) are proven in `SharedKernel.Testing.SelfTests`, following the same no-actual-consumer-yet fallback already established for this folder's existing four fakes (`FakeCacheService.GetManyAsync`/`SetManyAsync`, `FakeTenantCacheKeyProvider`, `FakeCacheInvalidationBus` — "no `02.Caching` test project exercised these three directly"). `FakeCacheWarmupStrategy`'s ordering/failure-isolation coverage must construct at least three instances sharing one `ConcurrentQueue<string>`, run them through a hand-rolled ordering/dispatch loop that mirrors `CacheWarmupHostedService`'s own documented sort-by-`Order`/catch-and-continue contract (this package never references `CacheWarmupHostedService` itself — that type lives in `02.Caching`, outside this package's dependency set — so the proof loop is hand-rolled in `SelfTests`, not borrowed from production), and assert the queue reflects every strategy having run exactly once, in `Order`, including the ones with `SimulateFailure = true`.
- `Persistence/FakeRepository<TAggregate,TId>`/`FakeUnitOfWork`/`FakePersistenceTransaction` and the long-pending `FakeDbConnectionFactory` (P-335/WO-053) are proven in `SharedKernel.Testing.SelfTests` — net-new capability, zero existing consumer, following the IDENTICAL no-actual-consumer-yet fallback already established for this SAME folder's `TestSharedKernelDbContext`/`AggregateRootFaker`/`EfContextExtensions` (`SharedKernel.Persistence.EfCore.Tests` carries a `ProjectReference` to `SharedKernel.Testing` but consumes none of these new types either, exactly as it did not for that earlier trio). `13.ServiceDefaults`'s `DatabaseTenantResolutionStrategyTests` — re-confirmed on disk (2026-08-03) to still hand-roll an `IDbConnection`/`IDbCommand`/`IDbDataParameter` NSubstitute mock rather than use a `16.Testing` fake, exactly as its own Test Rules section states — is a plausible future adopter of `FakeDbConnectionFactory` specifically, but that adoption remains `13.ServiceDefaults`'s own future cross-domain follow-up, never performed here. `FakeRepository<TAggregate,TId>`'s test fixture is a minimal test-only type extending the REAL `SoftDeletableAggregateRoot<Guid>` base (03.Domain), mirroring `AggregateRootFakerTests`'s own existing fixture convention in this same `SelfTests` project, so `ISoftDeletable` conformance is genuine inheritance rather than a hand-rolled interface implementation. **IMPLEMENTED AND PROVEN (T-66–T-71, 2026-08-04)**: `SharedKernel.Testing.SelfTests/Persistence/` gained `FakeRepositoryTestFixtures.cs`, `FakeDbConnectionFactoryTests.cs`, `FakeRepositoryTests.cs`, `FakeRepositorySpecificationPipelineTests.cs`, `FakeRepositoryPagingAndProjectionTests.cs`, `FakeRepositoryKeysetTests.cs`, and `FakeUnitOfWorkTests.cs` (50 tests total). **One naming correction against this bullet's own original phrasing**: the fixture type is named `TestSoftDeletableOrder`, not literally `TestOrder` as the phase text's own example suggested — `TestOrder` was already taken in this same `SharedKernel.Testing.SelfTests.Persistence` namespace by `TestFixtures.cs`'s own plain, non-soft-deletable `AggregateRoot<Guid>` fixture (consumed by the EF Core-backed self-tests), so a distinctly-named type was required to avoid a collision. Full regression `dotnet test` (excluding Docker-gated `Containers/`) passes 844/844 (794 pre-existing + 50 net new), zero regressions.

---

## Changelog

> Maintained by `/sync-brain` and a future testing-arch-planner domain agent. One line per significant change.

- [2026-06-22] Domain brain initialized — packages, technology stack, folder/namespace map, full interface contracts (implemented: `Caching/` fakes; planned: `Clocks/`, `Security/`, `Messaging/`, `Persistence/`, `Containers/`, `Fakers/`), implementation rules, DI registration shape, AOT note, test rules. Derived from the root `CLAUDE.md` folder-map entry for `16.Testing`, the root WO-022 commitment to `InMemoryMessageBus`/`InMemoryEventPublisher`, and the cross-domain expectations already load-bearing in `02.Caching`, `06.Persistence`, `07.Messaging`, `11.Communication`, and `13.ServiceDefaults`'s own `CLAUDE.md` files (Testcontainers PostgreSQL/Redis/RabbitMQ, the `FakeCacheService`/`FakeDistributedLockService`/`FakeRenewableLock`/`FakeTenantCacheKeyProvider` already shipped, and the documented `IDbConnectionFactory` fake gap) — no dedicated testing-arch-planner agent exists yet, so this pass is an arch-lead-equivalent placeholder pending formal phase breakdown in `state-map.md` (arch-lead-equivalent pass)
- [2026-06-22] WO-029: `SharedKernel.Testing.SelfTests` package added — narrow exception to the no-nested-`.Tests` rule for standalone helpers with no owning consuming-domain interface; Test Rules amended with the decision rule; consolidates 14 long-pending phases into 9 (P-179–P-187) (arch-lead)
- [2026-06-22] P-035/WO-008 + P-064/WO-012 + P-179..P-187/WO-029 processed in full (137 tasks written to `state-map.md` across all 6 phases): Folder/Namespace Map gains `Domain/`, `Contracts/`, `Communication/`, `ServiceDefaults/` (new) and documents the `Fakers/`-vs-`Domain/` split (construction-time vs. post-condition helpers); full target-shape interface contracts added for `FakeClock`, `EntityFaker<TEntity,TId>`, `DomainEventAssertions` (incl. P-181 extensions `ContainsEventWithVersion<T>`/`HasRaisedExactlyNEvents`), `BusinessRuleAssertions`, `SpecificationAssert`, `AddFakeDomainServices()` (P-035); `PagedListBuilder<T>`, `EnvelopeAssertions`, `IntegrationEventFaker<TEvent>`, `EventEnvelopeBuilder<TEvent>` (P-064); `FakeCacheInvalidationBus` + unified `AddFakeCachingServices()` added to `Caching/` — `FakeCacheService.GetManyAsync`/`SetManyAsync` and `FakeTenantCacheKeyProvider` verified against spec with zero code drift (P-180); `SingleValueObjectFaker<TValueObject,TValue>`, `DomainVersionAssertions`, `SpecificationTestBuilder<T>`, `FakeDomainNotFoundException` (P-181); `TestSharedKernelDbContext`, `AggregateRootFaker`/`TenantedAggregateFaker`, `EfContextExtensions`, `ProjectionSpecificationBuilder<TAggregate,TResult>`, `PagedListAssertions` (corrected to zero-FluentAssertions per standing hard rule), `BulkAggregateFaker`, `WithDeletedSpecification<TAggregate>`, `PersistenceTestHelpers` (P-182, explicitly scoped out: no Outbox fakers, no PostgreSQL Testcontainer dependency); `InMemoryMessageBus`/`InMemoryEventPublisher`/`TestHarnessFactory` finalized with `RequestAsync` **corrected** from an earlier `NotSupportedException` draft to the dispatched `SetResponseHandler<TRequest,TResponse>`-configurable design (P-183, supersedes P-011/P-124); `PostgreSqlContainerFixture`/`RedisContainerFixture`/`RabbitMqContainerFixture` markers updated `[STATUS: Planned]` with design finalized (P-184); Redis topology alignment confirmed, no code drift (P-185); `MockServiceEndpointResolver`, `FakeHttpContextAccessor`, `HttpClientHandlerTestFactory`, `FakeHttpMessageHandler`, ambient `Activity` helper, gRPC `ServerCallContext` stub, GraphQL test-executor factory added to new `Communication/` section, merging superseded P-158+P-168 (P-168 wins on conflict) (P-186); `StaticTenantProvider`, `FakeTenantResolutionStrategy` (structurally compatible, no `SharedKernel.MultiTenancy` reference), `HealthCheckAssertionExtensions` added to new `ServiceDefaults/` section (P-187, carried forward unchanged from superseded P-174). Implementation Rules, DI Registration, and Test Rules sections updated throughout to reflect all of the above (testing-arch-planner)
- [2026-06-23] `SK.16.Core` implemented in full (C-01–C-43): all 43 target-shape contracts from the Design phase landed as real code across `Clocks/`, `Domain/`, `Contracts/`, `Caching/`, `Persistence/`, `Messaging/`, `Containers/`, `Communication/`, `ServiceDefaults/`. Remaining stale `[STATUS: Planned — P-035/WO-008]` tag on the `Domain/`-section `FakeClock` cross-reference removed (the real implementation lives in `Clocks/FakeClock.cs`, already documented without a status tag). `dotnet build` on `SharedKernel.Testing.csproj` succeeds with 0 errors (pre-existing `NU1903`/`CS1574` warnings only, none originating from this package). `[STATUS: Planned]` tags intentionally left in place for `FakeUserContext`/`FakeTenantProvider` (`Security/`), `FakeDbConnectionFactory` (`Persistence/`), and `FakerSeeding` (`Fakers/`) — none of these were in this Core phase's task list (C-01–C-43); they remain future work. `SK.16.Tests` is next (testing-phase-implementer).
- [2026-06-23] `SK.16.Tests` implemented in full (T-01–T-34); 209/209 tests passing in `SharedKernel.Testing.SelfTests` (container-fixture lifecycle tests excluded from this count, requiring Docker). New Test Rules bullet added documenting the SelfTests fallback observed repeatedly in practice — most interface-implementing fakes had no actual consumer in their owning domain's `.Tests` project yet, so they were proven in `SelfTests` instead of "there," even though the original rule's default was "prove it in the owning domain." Two real bugs found and fixed during implementation, now documented in their respective Interface Contracts blocks: `EfContextExtensions` gained a new public `RegisterOptions(DbContext, DbContextOptions)` API (`TestSharedKernelDbContext` calls it automatically) because `ReloadAsync<T>` could not resolve `DbContextOptions` from a standalone context's internal service provider; `AmbientActivityTestHelper` now registers a single static always-sampling `ActivityListener` scoped to its own `ActivitySource` (a second documented exception to the "no static mutable state" rule, alongside `FakerSeeding.Apply`) because `ActivitySource.StartActivity` returns `null` outside an OTel-instrumented host. Stale "(planned —  ...)" parenthetical notes removed from the DI Registration code samples now that `AddFakeCachingServices()`/`AddFakeDomainServices()`/`AddInMemoryMessageBus()` are all implemented. `SK.16.Docs` is next (testing-phase-implementer).
- [2026-06-24] `SK.16.Docs` completed (DO-01–DO-10) without further code changes — every public type's XML doc-comment coverage was verified (not assumed) against the live `.cs` files via a line-count check against public-member declarations, and every doc-only `CLAUDE.md` claim from prior phases ("done in this pass" for DO-03/04/08/09) was independently re-checked against the current file content rather than trusted from earlier agent summaries. `dotnet build` re-confirmed 0 errors. `SK.16.Published` is next (testing-phase-implementer).
- [2026-06-24] `SK.16.Published` completed (P-01, P-02) — formally records the `ProjectReference`-only consumption model for both packages; new "Publishing / Consumption Model" subsection added under Packages documenting this explicitly. Verified rather than assumed: `SharedKernel.Testing.SelfTests.csproj` already had `<IsPackable>false</IsPackable>`, but `SharedKernel.Testing.csproj` had no `IsPackable` property at all — the SDK default (`true`) meant `dotnet pack` would have attempted to pack it, contradicting the documented decision. Fixed by adding `<IsPackable>false</IsPackable>` explicitly to `SharedKernel.Testing.csproj`. `[STATUS: Planned]` markers on `FakeUserContext`/`FakeTenantProvider` (`Security/`) and `FakerSeeding` (`Fakers/`) confirmed still correctly unimplemented on disk (no `Security/` folder exists; no `FakerSeeding.cs` in `Fakers/`) — out of scope for every phase to date, left unflipped. `dotnet build` succeeds 0 errors. All 6 phases of `16.Testing` (Design→Published) now complete — domain closed (WO-008/WO-012/WO-029) (testing-phase-implementer).
- [2026-07-29] `SK.16.Docs` completed (DO-27–DO-29, P-300/WO-049) — DO-27 added the capitalized `TEST-ONLY — NEVER PRODUCTION-SAFE` `<remarks>` paragraph to the five `Cryptography/` fakes that lacked one (`FakeSecureRandomGenerator`, `FakeEncryptionKeyProvider`, `FakeAsymmetricKeyProvider`, `FakeAsymmetricSignatureService`, `FakeHmacSigner`); DO-28/DO-29 verified existing coverage on `FakeContentHasher`/`AddFakeCryptography()` and `FakeFeatureManager`/`AddFakeFeatureManagement()` with no code change needed. `Cryptography/`/`FeatureManagement/` Interface Contracts blocks annotated with DOCS-PHASE COMPLETION notes. `dotnet build SharedKernel.Testing.csproj -c Release` succeeds 0 errors. All 6 phases of `16.Testing` (Design→Published) are `●` again — WO-049's `16.Testing` contribution closed end to end (testing-phase-implementer).
- [2026-06-24] WO-030 dispatch processed (testing-arch-planner): P-188 (`FakeUserContext`/`FakeTenantProvider` — `Security/`) and P-189 (`FakerSeeding` — `Fakers/`) accepted; 16 new tasks added to `state-map.md` across Design/Scaffold/Core/Tests/Docs (D-49–D-54, S-13/S-14, C-44–C-46, T-35–T-37, DO-11/DO-12), all `○`. `IUserContext`/`ITenantProvider` member signatures re-verified directly against the live source at `12.Security/SharedKernel.Security.Abstractions/Abstractions/{IUserContext,ITenantProvider}.cs` (not from memory) — exact match to the `[STATUS: Planned]` blocks already on file below; zero signature drift. `[STATUS: Planned]` markers on both `Security/` types and `FakerSeeding` are left in place in this pass — they flip only once a future Core-phase implementer lands the code (Design documents target shape, not completion). **P-190 (`OutboxMessageFaker`/`OutboxAssertions` for `06.Persistence`) rejected outright, not deferred** — its premise that `06.Persistence` shipped a real `OutboxInterceptor`/outbox contract is false; that domain's own `CLAUDE.md` states outbox ownership belongs entirely to `07.Messaging` via MassTransit and that introducing any outbox type into `06.Persistence` is a hard violation, confirmed independently in `07.Messaging/CLAUDE.md`. No SharedKernel-owned outbox message contract exists anywhere to mirror in a `Faker<T>`. The P-182/WO-029 scope lock is reaffirmed, not stale — see the updated `Persistence/` SCOPE LOCK note below. No code changes in this pass — Design-only.
- [2026-06-24] `SK.16.Core` (C-44–C-46) implemented: `FakeUserContext`/`FakeTenantProvider` (`Security/FakeUserContext.cs`/`FakeTenantProvider.cs`) and `FakerSeeding` (`Fakers/FakerSeeding.cs`) landed exactly per the D-49/D-50/D-53 target shape — zero signature drift re-confirmed against `12.Security/SharedKernel.Security.Abstractions/Abstractions/{IUserContext,ITenantProvider}.cs`. `[STATUS: Planned]` markers removed from all three Interface Contracts blocks (`Security/` section, `Fakers/` section). No `Add*` DI extension shipped for the two `Security/` fakes, per D-51's documented decision — both remain plain `new`-able classes consistent with the `Security/`/`Persistence/`/`Clocks/` convention. Grepped for an existing `12.Security` consumer of `SharedKernel.Testing` — none found (`SharedKernel.Security.Abstractions.Tests.csproj`/`.Oidc.Tests.csproj` carry no `ProjectReference` to this package), confirming T-35/T-36 will route to `SharedKernel.Testing.SelfTests` per the D-52 fallback when the Tests phase runs. `dotnet build SharedKernel.Testing.csproj -c Release` succeeds, 0 errors (pre-existing NU1903 advisory warnings only). `SK.16.Core` now 46/46 `●`, promoted to root. `SK.16.Tests` (T-35–T-37) and `SK.16.Docs` (DO-11/DO-12) remain pending to close out WO-030 (testing-phase-implementer).
- [2026-06-30] P-226/WO-036 processed (testing-arch-planner) — audit-first phase dispatched against `05.Application`'s freshly-planned, design-only `TracingBehavior` (P-220) and `CacheInvalidationBehavior` (P-224); read `05.Application/CLAUDE.md` and `05.Application/state-map.md` in full for authoritative type names before writing this phase. **Audit 1 (Tracing):** `AmbientActivityTestHelper` found insufficient as-is for span-recording assertions — it is an ambient-context *setter* (sets `Activity.Current`), not a span-recording *listener*; designed a new, small, additive sibling type `ActivityRecorder` (`Communication/ActivityRecorder.cs`, `[STATUS: Planned]`) that registers a scoped `ActivityListener` against a named `ActivitySource` and exposes `RecordedActivities` for post-hoc assertion. Zero new `PackageReference` (BCL only). `AmbientActivityTestHelper` itself unmodified. **Audit 2 (Cache Invalidation):** `FakeCacheService` found **already fully sufficient** — its existing `RemoveAsync`/`RemoveByTagAsync` cover `CacheInvalidationBehavior`'s entire test need; `FakeCacheInvalidationBus` correctly identified as an unrelated abstraction (`ICacheInvalidationBus`, Redis pub/sub signaling) with no bearing here. **No new fake added for cache invalidation** — that half of the phase is documentation-only, the explicit "shrinks to documentation only" outcome the phase itself anticipated as valid. New Test Rules bullets added: a general "audit before adding" principle for future phases, plus `ActivityRecorder`'s unconditional `SelfTests` routing (mirrors `AmbientActivityTestHelper`). 9 new tasks added to `state-map.md` (D-55–D-58, S-15, C-47, T-38, DO-13), all `○`; total task count 165 → 173. Domain remains otherwise closed (WO-008/WO-012/WO-029/WO-030 all `●`) — this is the only in-progress phase pending a future Core-phase implementer session.
- [2026-06-30] `SK.16.Core` closed: C-47 implemented — `ActivityRecorder` (`Communication/ActivityRecorder.cs`) lands per the D-56 target shape exactly: `static StartRecording(string activitySourceName)` registers a process-scoped `ActivityListener` (filtered via `ShouldListenTo`/`Sample = AllDataAndRecorded`) capturing every `Activity` stopped against the named source into a thread-safe `ConcurrentQueue<Activity>`, exposed read-only via `.RecordedActivities`; `.Dispose()` disposes the underlying `ActivityListener` to unregister it. Zero new `PackageReference`/`ProjectReference` (BCL `System.Diagnostics` only), confirming S-15. `AmbientActivityTestHelper` left unmodified — purely additive sibling in the same folder/namespace. `[STATUS: Planned — P-226/WO-036]` marker removed from both the Interface Contracts block and the Test Rules cross-reference. `dotnet build SharedKernel.Testing.csproj -c Release` succeeds, 0 errors (pre-existing NU1903 advisory warnings only). `SK.16.Core` now 47/47 `●`, promoted to root. `T-38` (prove in `SharedKernel.Testing.SelfTests`) and `DO-13` (XML docs — already written inline in this pass) remain pending to fully close WO-036 (testing-phase-implementer).
- [2026-07-07] WO-040 (P-244/P-245) processed (testing-arch-planner): new `Application/` capability folder added — the first `16.Testing` reference to `05.Application`. Read `05.Application/CLAUDE.md` in full for the live `IUnitOfWork`/`IAuthorizationContext` (incl. P-232's `AllOf`/`AnyOf` multi-requirement evolution and its "no-op on empty collection" semantics)/`IIdempotencyKeyStore`/`IIdempotencyResponseStore` (P-242, additive/optional) signatures, and read the live internal `PipelineTestHarness.cs` source directly at `05.Application/SharedKernel.Application.Behaviors/SharedKernel.Application.Behaviors.Tests/TestHarness/PipelineTestHarness.cs` to source `ApplicationPipelineTestHarness`'s promoted design from the already-proven shape rather than a fresh guess. P-244 adds `FakeUnitOfWork` (explicitly disambiguated from `SharedKernel.Persistence.Abstractions.IUnitOfWork`), `FakeAuthorizationContext` (configurable per-requirement pass/fail, `AllOf`/`AnyOf` vacuous-true-on-empty matching the real behavior exactly), and — because C# cannot toggle interface implementation at runtime and `IdempotentCommandBehavior` detects replay support via `is IIdempotencyResponseStore` — TWO separate idempotency-store fakes (`FakeIdempotencyKeyStore` non-replay-only, `FakeIdempotencyResponseStore` implementing both interfaces), plus `AddFakeApplicationBehaviorServices()` bundling all three as singletons. P-245 adds `ApplicationPipelineTestHarness`, a public promotion of the internal `PipelineTestHarness`, renamed to avoid ambiguity with `07.Messaging`'s `TestHarnessFactory`/MassTransit `ITestHarness`; it hand-rolls its own `ActivityListener` wiring rather than referencing `Communication/ActivityRecorder`, per the sibling-capability-folder-isolation hard rule (documented as a new concrete example of that rule, not an exception to it). Both fakes' Tests-phase routing is `SharedKernel.Testing.SelfTests`: `SharedKernel.Application.Behaviors.Tests.csproj` was confirmed to already carry a live `ProjectReference` to `SharedKernel.Testing`, but every one of these six types is net-new with zero existing consumer — same reasoning already applied to `TestSharedKernelDbContext`/`AggregateRootFaker` (T-19/T-20); a new Test Rules bullet generalizes this "live reference ≠ automatic routing there" principle explicitly. Adoption of these fakes (and retirement of the internal `PipelineTestHarness`) into `05.Application.Behaviors.Tests` is tracked as an explicit cross-domain follow-up for a future `05.Application` implementer pass — `16.Testing` never touches a `.Tests` project, in this domain or any other. Folder/Namespace Map, Interface Contracts (new `Application/` section, all `[STATUS: Planned — P-244/P-245/WO-040]`), Implementation Rules (sibling-isolation example + the two-type optional-capability-fake rule + the same-named-interface disambiguation rule), DI Registration, and Test Rules all updated in this pass. 23 new tasks added to `state-map.md` (D-59–D-71, S-16–S-18, C-48–C-53, T-39–T-44, DO-14/DO-15), all `○`.
- [2026-07-07] `SK.16.Design` closed for WO-040 (D-59–D-71 → `●`, 71/71): confirmed the `Application/` Interface Contracts block against the LIVE `05.Application.Behaviors` source (`IUnitOfWork.cs`, `IAuthorizationContext.cs`, `AuthorizationBehavior.cs`, `IIdempotencyKeyStore.cs`, `IIdempotencyResponseStore.cs`, `ApplicationBehaviorsBuilder.cs`, `ApplicationBehaviorsServiceCollectionExtensions.cs`, and the live `PipelineTestHarness.cs`), not from the prior pass's draft alone. **One drift found and corrected**: `FakeAuthorizationContext.AnyOf`'s empty-collection behavior was mis-drafted as "same vacuous-true-on-empty rule" as `AllOf`; the live `IAuthorizationContext.AnyOf` XML doc states the opposite verbatim — `AllOf(empty) → true` (vacuous truth) but `AnyOf(empty) → false` (nothing to satisfy) — corrected in the Interface Contracts block above, with a note that `AuthorizationBehavior<,>` itself never actually calls either method with an empty collection (it guards with `Count > 0` first), so the correction only matters for a test calling the fake directly. All other target shapes (`FakeUnitOfWork`, `FakeIdempotencyKeyStore`/`FakeIdempotencyResponseStore`, `AddFakeApplicationBehaviorServices()`, `ApplicationPipelineTestHarness`) confirmed with zero drift. `[STATUS: Planned — P-244/P-245/WO-040]` markers remain in place — Design confirms target shape, Core (C-48–C-53, still `○`) is what flips them. `SK.16.Scaffold`/`SK.16.Core`/`SK.16.Tests`/`SK.16.Docs` remain pending for WO-040 (testing-phase-implementer).
- [2026-07-07] `SK.16.Core` closed for WO-040 (C-48–C-53 → `●`, 53/53): all six `Application/` types implemented — `FakeUnitOfWork`, `FakeAuthorizationContext`, `FakeIdempotencyKeyStore`, `FakeIdempotencyResponseStore`, `AddFakeApplicationBehaviorServices()`, `ApplicationPipelineTestHarness` — against the live `05.Application.Behaviors` source, zero drift from the locked D-59–D-71 design. `ApplicationPipelineTestHarness` filters its `ActivityListener`/`MeterListener` by the literal string `"SharedKernel.Application"` rather than referencing `ApplicationDiagnostics` directly, since that type is `internal` to `SharedKernel.Application.Behaviors` — confirmed this is the only option, not an oversight. `dotnet build SharedKernel.Testing.csproj -c Release` succeeds, 0 errors (2 pre-existing NU1903 advisory warnings only). `SK.16.Core` now 53/53 `●`, promoted to root. `[STATUS: Planned — P-244/P-245/WO-040]` marker removed from the `Application/` Interface Contracts section header; Packages table row updated to list `SharedKernel.Application.Behaviors` as implemented. No consuming `.Tests` project yet references these six types — behavioral proof deferred to `SK.16.Tests` (T-39–T-44, still pending) (testing-phase-implementer).
- [2026-07-08] `SK.16.Tests` closed for WO-040 (T-39–T-44 → `●`, 44/44): six new test files added under `SharedKernel.Testing.SelfTests/Application/` (`FakeUnitOfWorkTests.cs`, `FakeAuthorizationContextTests.cs`, `FakeIdempotencyKeyStoreTests.cs`, `FakeIdempotencyResponseStoreTests.cs`, `AddFakeApplicationBehaviorServicesTests.cs`, `ApplicationPipelineTestHarnessTests.cs`), 41 tests, all passing. T-40's `AnyOf`-on-empty assertion follows the CLAUDE.md's own already-corrected contract (vacuous-false), not the stale parenthetical in the original T-40 task text. T-44's harness tests declare a minimal test-only `ICommand`/`IRequestHandler<,>` triad inside the test file itself, never a real `05.Application`-owned command. `dotnet build SharedKernel.Testing.SelfTests.csproj -c Release` succeeds, 0 errors (pre-existing NU1903 advisory warnings only). `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` passes 283/283 (242 pre-existing + 41 net new). `SK.16.Tests` now 44/44 `●`, promoted to root. No `[STATUS: Planned]` markers remained on the `Application/` section (already flipped at Core-phase close) — this pass is test-coverage only, no CLAUDE.md contract content changed. `SK.16.Docs` (DO-14/DO-15) is next (testing-phase-implementer).
- [2026-07-08] `SK.16.Docs` closed for WO-040 (DO-14/DO-15 → `●`, 15/15): added an explicit local-seam-only-scope `<remarks>` block — naming all three excluded cross-domain interfaces (`06.Persistence`, `12.Security`, `07.Messaging`) by name — to `FakeUnitOfWork`, `FakeAuthorizationContext`, `FakeIdempotencyKeyStore`, `FakeIdempotencyResponseStore`, and `AddFakeApplicationBehaviorServices()`; the two idempotency-store fakes additionally disambiguate by name against `07.Messaging.Abstractions.IIdempotencyStore` (a naming-pattern collision only, not a shared owning domain). Added an outstanding-cross-domain-follow-up `<remarks>` block to `ApplicationPipelineTestHarness` citing D-70 — repointing `05.Application.Behaviors.Tests`' internal `PipelineTestHarness` call sites to this public type remains a future `05.Application` implementer pass, out of `16.Testing`'s jurisdiction. The two-type idempotency-store split rationale and the harness's promoted-origin/sibling-isolation notes were already fully documented from the Core-phase pass and required no further edit. No `[STATUS: Planned]` markers remained on `Application/` (already flipped at Core-phase close, T-39–T-44 pass reconfirmed this). `dotnet build SharedKernel.Testing.csproj -c Release` succeeds, 0 errors (2 pre-existing NU1903 advisory + 2 pre-existing CS1574 warnings from `01.Core`, none introduced here). `SK.16.Docs` now 15/15 `●`, promoted to root. This closes WO-040 in full — all 6 phases of `16.Testing` (Design/Scaffold/Core/Tests/Docs/Published) are `●` again (testing-phase-implementer).
- [2026-07-14] P-258/WO-041 verification/closeout pass (testing-phase-implementer): all six local phase tables (Design D-72–D-78, Scaffold S-19/S-20, Core C-54–C-59, Tests T-45, Docs DO-16) were already `●` from prior sessions, but the root `state-map.md` `### P-258` header block's `Status` line and acceptance-criteria checkboxes had never been promoted to match. Re-verified the on-disk `Logging/` implementation (`LogRecord`, `InMemoryLogger`, `InMemoryLoggerFactory`, `InMemoryLogger<TCategoryName>`, `LoggerAssertions`, `AddInMemoryLoggerFactory()`) line-by-line against this file's Interface Contracts block — zero drift. `dotnet build` clean (0 errors) on both `SharedKernel.Testing.csproj` and `SharedKernel.Testing.SelfTests.csproj` (Release, only pre-existing NU1903 advisory warnings). `dotnet test SharedKernel.Testing.SelfTests.csproj` passes 320/320, including the 37 `Logging/InMemoryLoggerTests.cs` tests built on a real `[LoggerMessage]`-attributed call site (`TestLogMessages.OrderProcessed`/`.OrderFailed`). Root `state-map.md`'s `### P-258` block corrected: `Status` `◐ Dispatched` → `● Complete`, all five acceptance criteria checked. No code or contract drift found — this pass is a pure verification/promotion close-out, no new implementation.
- [2026-07-16] WO-043 P-268/P-269 processed (testing-arch-planner): read `08.Storage/CLAUDE.md` in full for the live `IFileStorage`/`IBlobUriGenerator` nine/two-member contracts and `S3StorageOptions`/`ObsStorageOptions` property shapes (P-265/P-266/P-267), and separately verified on disk (not assumed from that file's prose) that `08.Storage/SharedKernel.Storage.Abstractions/SharedKernel.Storage.Abstractions.csproj` is a genuinely empty placeholder — zero `.cs` files, no references — confirming `08.Storage`'s own `state-map.md` Package Board note that it is still 100% Design-phase (`○`) (NOTE: this member count and empty-placeholder status were both true at the time of this original WO-043 changelog entry — see the 2026-07-17 entry below for the corrected re-verification: `IFileStorage` is nine members, and `08.Storage.Abstractions` now ships real compiled code). **P-268** adds `Containers/MinioContainerFixture` — a fourth `IAsyncLifetime` Testcontainers fixture alongside `PostgreSqlContainerFixture`/`RedisContainerFixture`/`RabbitMqContainerFixture`, exposing `ServiceUrl`/`AccessKeyId`/`SecretAccessKey`/`ForcePathStyle`/`DefaultBucket` scalar properties named 1:1 against `S3StorageOptions` so `SharedKernel.Storage.S3.Tests` binds with zero renaming (`SharedKernel.Storage.Obs.Tests` maps the same `ServiceUrl` value into `ObsStorageOptions`'s differently-named `Endpoint` field — a straight assignment, never provider branching); bootstraps `DefaultBucket` during `InitializeAsync` via a short-lived `AmazonS3Client`, making it the one type in `Containers/` permitted to carry a new `AWSSDK.S3` `PackageReference` (mirrors the existing `TestHarnessFactory`-carries-`MassTransit` precedent). Deliberately takes **no** `ProjectReference` to `SharedKernel.Storage.Abstractions` — like its three siblings, it exposes only flat scalar connection properties — so P-268 has **zero build-time dependency on `08.Storage`'s own code landing** and is fully unblocked end-to-end. **P-269** adds a new `Storage/` capability folder (`SharedKernel.Testing.Storage` namespace, the first mapped to `08.Storage`) — `InMemoryFileStorage` (implements the full nine-member `IFileStorage`: deterministic incrementing ETags and a fixed non-real `LastModified` instead of a `Clocks/FakeClock` dependency, per sibling-isolation; a single `.SimulateFailure` write-path-only toggle mirroring `FakeDistributedLockService`'s precedent, with `CheckHealthAsync` deliberately EXCLUDED from it — always succeeds unconditionally, per this phase's explicit acceptance criterion; `.UploadedKeys`/`.DeletedKeys`/`.CopiedPairs` recorded-history lists plus `.WasUploaded`/`.WasDeleted`/`.WasCopied` query helpers; a `.Seed(...)` test-setup helper mirroring `FakeIdempotencyKeyStore.MarkAsProcessed`'s pre-seed-without-the-normal-method precedent), `InMemoryBlobUriGenerator` (implements `IBlobUriGenerator`, deterministic inspectable presigned URLs, honors the same `StorageErrors.ExpiryTooLong` validation as the real contract), and `AddInMemoryFileStorage()` (registers both as singletons — matching, not diverging from, `IFileStorage`/`IBlobUriGenerator`'s own production singleton lifetime, unlike the messaging doubles' deliberate scoped-to-singleton deviation). **New Implementation Rules distinction drawn** (first time needed in this domain's history): a "soft" design-ahead-of-schedule situation (P-226/WO-036's `ActivityRecorder`, needing only a literal string, not a real upstream type) is NOT the same as a "hard" compile-time blocker (P-269, needing `: IFileStorage` against a package with zero compiled types) — in the hard case, Design/Scaffold still proceed (target shape sourced from the owning domain's fully-authored `CLAUDE.md`; an empty-project `ProjectReference` always builds) but Core/Tests/Docs tasks requiring the real type are marked `⚑` Blocked in `state-map.md`, not `○`, with an explicit Cross-Domain Dependencies row naming the upstream phase key (`08.Storage`'s `SK.08.Core`) that must land first. 30 new tasks added to `state-map.md` (D-79–D-96, S-21–S-24, C-60–C-63, T-46/T-47, DO-17/DO-18); C-61–C-63/T-47/DO-18 (the five P-269 tasks needing real `IFileStorage`/`IBlobUriGenerator` types) are `⚑` Blocked, all fourteen others (P-268's five plus P-269's Design/Scaffold nine) are `○` Pending and fully actionable today. Folder/Namespace Map, Interface Contracts (`Containers/` gains `MinioContainerFixture`; new `Storage/` section), Technology Stack (`Testcontainers.Minio`, `AWSSDK.S3` rows), Implementation Rules, DI Registration, and Test Rules all updated in this pass.
- [2026-07-17] `SK.16.Design` closed for WO-043 (D-79–D-96 → `●`, 96/96): re-verified every P-268/P-269 target-shape claim directly against LIVE source, not the prior pass's draft — read `08.Storage/CLAUDE.md` in full plus every `.cs` file under `08.Storage/SharedKernel.Storage.Abstractions/` (`Abstractions/IFileStorage.cs`, `Abstractions/IBlobUriGenerator.cs`, all seven `Models/*.cs` records, `Errors/StorageErrors.cs`), `SharedKernel.Storage.S3/Options/S3StorageOptions.cs`, `SharedKernel.Storage.Obs/Options/ObsStorageOptions.cs`, `SharedKernel.Storage.S3.csproj`'s `AWSSDK.S3` pin, and `08.Storage/state-map.md`'s Package Board/Overall Progress. **Two stale claims found and corrected, both flagged explicitly by the dispatching brief rather than discovered cold:** (1) **`IFileStorage` member-count miscount** — this file and `state-map.md` repeatedly said "ten members"; the live interface has exactly **nine** (Upload/Download/Delete/Exists/GetMetadata/Copy/DeleteMany/List/CheckHealth) — corrected in the Cross-Domain Dependencies table, D-85's task text, and annotated (not silently rewritten) in the two prior changelog entries that stated it, mirroring `08.Storage`'s own 2026-07-16 changelog entry that made the identical nine-vs-ten correction on its own side. (2) **The BUILD-TIME BLOCKER has CLEARED** — `08.Storage/SharedKernel.Storage.Abstractions` was a genuinely empty placeholder when P-268/P-269 were originally designed; it now ships real, compiled `IFileStorage`/`IBlobUriGenerator`/all seven `Models/` records/nine-factory-method `StorageErrors`, matching this file's `Storage/` Interface Contracts block with **zero drift** on every member signature, and `08.Storage`'s own `state-map.md` confirms `SK.08.Core` is `●` 30/30. `S3StorageOptions.ServiceUrl`/`.AccessKeyId`/`.SecretAccessKey`/`.ForcePathStyle`/`.DefaultBucket` and `ObsStorageOptions.Endpoint` confirmed matching `MinioContainerFixture`'s D-79 property-naming design 1:1. `AWSSDK.S3` confirmed pinned `4.0.101.1` in `SharedKernel.Storage.S3.csproj`. The BUILD-TIME BLOCKER block (`Storage/` Interface Contracts section) rewritten to a BLOCKER-CLEARANCE VERIFICATION block; `[STATUS: Planned — P-269/WO-043, BLOCKED on 08.Storage's own code]` markers on `InMemoryFileStorage`/`InMemoryBlobUriGenerator`/`AddInMemoryFileStorage()` shortened to `[STATUS: Planned — P-269/WO-043]` (still un-implemented — Core phase has not run — but no longer genuinely blocked); the Packages-table row and the Test-Rules blocker note both corrected to match. **Per this domain's own established rule** (Implementation Rules: "Core/Tests/Docs tasks that require the real type must be marked `⚑` Blocked... never silently implemented against a guessed-at interface shape"), the inverse now applies once the blocker clears: `C-61`/`C-62`/`C-63`/`T-47`/`DO-18` are corrected from `⚑` Blocked back to `○` Pending in `state-map.md` as a state-correction — this is bookkeeping, not implementation; the actual `InMemoryFileStorage`/`InMemoryBlobUriGenerator`/`AddInMemoryFileStorage()` code is NOT written in this Design-confirmation pass and remains a future `SK.16.Core` implementer session's work. Cross-Domain Dependencies table's `08.Storage` row corrected from "Pending (`SK.08.Core` `○`)" to "Available", member count corrected to nine. `SK.16.Design` now 96/96 `●` — phase promoted to root. `dotnet build SharedKernel.Testing.csproj -c Release` reconfirmed 0 errors (pre-existing NU1903/CS1574 advisory warnings only, none introduced by this documentation-only pass — no `.cs` files were added or changed). `SK.16.Scaffold` (S-21–S-24) and `SK.16.Core` (C-60–C-63, now unblocked) remain the next sessions' work (testing-phase-implementer).
- [2026-07-17] `SK.16.Core` closed for WO-043 (C-60–C-63 → `●`, 63/63): implemented `Containers/MinioContainerFixture.cs` (`Testcontainers.Minio` 4.1.0, pinned `minio/minio:RELEASE.2024-01-16T16-07-38Z`, exposes `ServiceUrl`/`AccessKeyId`/`SecretAccessKey`/`ForcePathStyle`/`DefaultBucket`, bootstraps the default bucket in `InitializeAsync` via a short-lived `AmazonS3Client`, mirrors the other three fixtures' `InvalidOperationException`-before-start contract exactly) and a new `Storage/` folder — `InMemoryFileStorage` (full nine-member `IFileStorage`, `ConcurrentDictionary`-backed, deterministic incrementing ETag sequence, fixed non-real `LastModified`, single write-path `.SimulateFailure` toggle, `CheckHealthAsync` unconditionally succeeds, `.UploadedKeys`/`.DeletedKeys`/`.CopiedPairs`/`.WasUploaded`/`.WasDeleted`/`.WasCopied`/`.Seed`/`.Reset`), `InMemoryBlobUriGenerator` (deterministic inspectable presigned URLs, honors `StorageErrors.ExpiryTooLong`), and `StorageServiceCollectionExtensions.AddInMemoryFileStorage()` (singletons, matching — not diverging from — `IFileStorage`/`IBlobUriGenerator`'s own production singleton lifetime). All signatures re-verified directly against the live `08.Storage/SharedKernel.Storage.Abstractions` source during implementation — zero drift from the already-locked target shape. One implementation-time refinement versus the original draft: `DeleteAsync`'s `SimulateFailure` branch returns `StorageErrors.AccessDenied` rather than `UploadFailed` — `StorageErrors` defines no dedicated single-key "delete failed" factory, and `AccessDenied` mirrors the real `S3FileStorage`/`ObsFileStorage` providers' own (and only) `DeleteAsync` failure-mapping path. Hit and resolved a genuine `Result<T>` type-resolution ambiguity: this project's existing `HotChocolate.Data` package reference (for `Communication/GraphQLTestExecutorFactory`) transitively pulls in `GreenDonut.Result<TValue>`, colliding with `SharedKernel.Primitives.Results.Result<T>` under an unqualified `Result<T>` — the first time this collision has surfaced in the package, since no prior fake returned a generic `Result<T>`; resolved by fully qualifying `SharedKernel.Primitives.Results.Result<T>` in both new `Storage/` files rather than adding a type alias (open generic aliases are not supported in C#). `[STATUS: Planned]` markers removed from `MinioContainerFixture`/`InMemoryFileStorage`/`InMemoryBlobUriGenerator`/`AddInMemoryFileStorage()` and the `Storage/` row in the Folder/Namespace Map. `dotnet build SharedKernel.Testing.csproj -c Release` succeeds, 0 errors (2 pre-existing NU1903 advisory warnings only, none introduced). No consuming-domain regression check applies — neither `08.Storage`'s own `.Tests` suites nor any other domain's `.Tests` project yet references `MinioContainerFixture` or the `Storage/` fakes; first behavioral proof is deferred to `SK.16.Tests` (T-46/T-47, `SharedKernel.Testing.SelfTests`, since no consuming domain has adopted either yet). `SK.16.Core` now 63/63 `●` — phase promoted to root; `SK.16.Tests` (T-46/T-47) and `SK.16.Docs` (DO-17/DO-18) remain the next WO-043 sessions' work (testing-phase-implementer).
- [2026-07-18] `SK.16.Tests` closed for WO-043 (T-46/T-47 → `●`, 47/47): `Containers/MinioContainerFixtureTests.cs` (6 tests) and three new `Storage/` test files — `InMemoryFileStorageTests.cs` (8), `InMemoryBlobUriGeneratorTests.cs` (6), `AddInMemoryFileStorageTests.cs` (4) — added to `SharedKernel.Testing.SelfTests`, 24 tests total, all passing. No `[STATUS: Planned]` markers remained (already flipped at Core-phase close). Two new Test Rules bullets added documenting operational facts surfaced during implementation: (1) all four `Containers/` fixture builders validate Docker connectivity eagerly inside the fixture's own constructor, not lazily at `InitializeAsync` — reproduced identically against all three pre-existing siblings, confirming it is Testcontainers' own behavior, not a defect; (2) `SharedKernel.Testing.SelfTests.csproj` does not carry the `HotChocolate.Data`→`GreenDonut.Result<TValue>` transitive-ambiguity landmine that `SharedKernel.Testing.csproj` has, since global usings from a `PackageReference` are scoped to the declaring project, not propagated through a `ProjectReference`. `dotnet build` on both `SharedKernel.Testing.csproj` and `SharedKernel.Testing.SelfTests.csproj` succeeds 0 errors (pre-existing NU1903/CS1574 warnings only). `dotnet test` (excluding the Docker-gated `Containers/` namespace — no Docker daemon in this session's environment) passes 332/332 (314 pre-existing non-`Containers/` tests + 18 net new `Storage/` tests; the 6 new `Containers/` Minio tests were added but not executed this session, mirroring the same Docker-daemon limitation already affecting the 6 pre-existing `Containers/` tests). `SK.16.Tests` now 47/47 `●` — phase promoted to root; only `SK.16.Docs` (DO-17/DO-18) remains to close WO-043 (testing-phase-implementer).
- [2026-07-19] WO-044 (P-275/P-276) processed (testing-arch-planner): read `09.Search/CLAUDE.md` in full (not from memory) for the live target signatures of `ISearchDocument`, `ISearchIndex<TDocument>` (12 members), `ISearchIndexProvisioner` (5 members), `ISearchProviderDescriptor` (4 members + `.Validate`), `TenantScope`, the closed 8-node `SearchFilter` AST, `SearchRequest`/`SearchResults<TDocument>`/`SearchWriteReceipt`/`SearchBulkReceipt`/`IndexCutoverRequest`/`SearchIndexHealth`/`SearchIndexDefinition`, `SearchWellKnown`, `SearchErrors`, and both providers' `MeilisearchOptions`/`ElasticSearchOptions`. Independently re-verified ON DISK (via `09.Search/state-map.md`'s own Package Board and Blocked section, which the search-arch-planner had already reached the identical finding on) that `09.Search/SharedKernel.Search.Abstractions/SharedKernel.Search.Abstractions.csproj` is a genuinely empty placeholder — zero `.cs` files — and that `16.Testing/SharedKernel.Testing/Containers/` today ships exactly four fixtures (Postgres/Redis/RabbitMq/Minio) with zero search-related rows anywhere in this domain's own files, confirming both P-275 and P-276 are genuine greenfield gaps, not merely undocumented plans. **P-275** adds two new `Containers/` fixtures: `MeilisearchContainerFixture` — hand-rolled on the generic `Testcontainers.Builders.ContainerBuilder` API since no `Testcontainers.Meilisearch` NuGet module exists (404 independently reconfirmed), image `getmeili/meilisearch` with `MEILI_MASTER_KEY`/`MEILI_NO_ANALYTICS=true` and an unauthenticated `GET /health` wait strategy, `.Url`/`.ApiKey` matching `MeilisearchOptions` 1:1; `ElasticsearchContainerFixture` — built on the official `Testcontainers.Elasticsearch` module with an EXPLICIT `.WithImage(...)` override to a pinned 9.x server tag (the module's own default, `elasticsearch:8.6.1`, is incompatible with the platform's pinned 9.4.2 client) plus an explicit post-start ping-poll wait closing the documented `testcontainers-dotnet#955` readiness race, `.Nodes`/`.Username`/`.Password`/`.AllowInvalidCertificates` matching `ElasticSearchOptions` 1:1. The `Testcontainers` package version-alignment question is resolved EXPLICITLY: bump the four pre-existing `Testcontainers.*` pins from `4.1.0` to `4.13.0` alongside the new `Testcontainers.Elasticsearch` `4.13.0` and a new direct `Testcontainers` base-package reference (for Meilisearch's hand-rolled builder) — one consistent version across the whole `Containers/` folder. Neither fixture takes a `ProjectReference` to any `SharedKernel.Search.*` package, mirroring `MinioContainerFixture`'s zero-build-time-dependency precedent, so P-275 carries NO blocker — 15 new tasks, all `○`. **P-276** adds a new `Search/` capability folder (`SharedKernel.Testing.Search` namespace, the first mapped to `09.Search`) with THREE deliberately independent fakes (no constructor/type coupling between them, a design choice unique to this folder — every other multi-type folder in this package lets its types compose freely): `InMemorySearchIndex<TDocument>` (constructed with a REQUIRED `SearchIndexDefinition`; full 12-member write/read/corpus-walk contract; a reflection-based in-memory `SearchFilter` evaluator explicitly justified by the existing `Domain/SpecificationAssert` test-only-reflection precedent — the first reuse of that precedent outside `Domain/`; a fail-loud pre-flight validation pipeline mirroring the real contract's field-role/pagination/tenant-scope checks; `TotalHitsAccuracy.Exact` unconditionally as a documented divergence from both real providers; a single write-path `.SimulateFailure` toggle and `.IndexedDocumentIds`/`.DeletedDocumentIds`/`.WasIndexed`/`.WasDeleted`/`.IsSearchable`/`.Seed`/`.Reset` assertion helpers, mirroring `Storage/InMemoryFileStorage`'s established shape); `InMemorySearchIndexProvisioner` (idempotent additive-only `EnsureIndexAsync`, idempotent `DeleteIndexAsync`, `CutoverAsync` via the real contract's own `CutoverFailed` error factory, deterministic `ProbeAsync`); `InMemorySearchProviderDescriptor` (`.ProviderName` defaults to `"in-memory-fake"` — deliberately neither real `SearchWellKnown` provider name — `.RegisterIndex`/`.Validate` reusing the same pre-flight pipeline); plus `AddInMemorySearchIndex<TDocument>()` (singleton — a DELIBERATE deviation from the real `AddIndex<TDocument>`'s scoped lifetime, mirroring `InMemoryMessageBus`'s own documented deviation) and `AddInMemorySearchProvisioning()` (singleton, matches real lifetime, bundles both non-generic fakes in one call mirroring `AddFakeCachingServices()`). **This repeats the P-269/WO-043 HARD design-ahead-of-schedule pattern exactly — the SECOND time this domain has recorded it, confirming it is a recurring category**: Design and Scaffold (an empty-project `ProjectReference` always builds) proceed fully unblocked and are marked `○`; Core/Tests/Docs tasks needing the real `ISearchIndex<TDocument>`/`ISearchIndexProvisioner`/`ISearchProviderDescriptor` types (8 tasks) are marked `⚑` **Blocked**, pending `09.Search`'s `SK.09.Core` phase. 28 new tasks for P-276. Combined WO-044 total: 43 new tasks (D-97–D-121, S-25–S-28, C-64–C-69, T-48–T-51, DO-19–DO-22); domain total 250 → 293 (also corrected a pre-existing arithmetic mismatch in the Overall Progress summary line — previously stated "243," which did not match the sum of the per-phase Total column then on file, 250 — corrected and flagged explicitly rather than silently rewritten). `## Blocked` section, Cross-Domain Dependencies table (two new `09.Search` rows), Package Board, and Overall Progress table all updated. `16.Testing/CLAUDE.md` refreshed in the same pass — `Containers/` interface contract gains both new fixtures plus a version-alignment decision block; new `Search/` Folder/Namespace Map row and full Interface Contracts section (all `[STATUS: Planned]`, the `Search/` types additionally marked HARD-BLOCKED with an explicit verification block mirroring `Storage/`'s own); Technology Stack gains `Testcontainers.Elasticsearch`/base-`Testcontainers` rows; Implementation Rules gains the reflection-based-filter-evaluator justification, restates the hard-blocker pattern now has two precedents, and documents the six-fixture `Containers/` sole-carrier conventions; DI Registration gains both new `Add*` extensions (marked blocked) and corrects a stale "blocked" parenthetical on the now-shipped `AddInMemoryFileStorage()` line; Test Rules gains routing notes for all five new types (SelfTests first for all, `09.Search` adoption tracked as an explicit cross-domain follow-up for both fixtures, mirroring the `MinioContainerFixture` precedent).
- [2026-07-19] `SK.16.Design` closed for WO-044 (D-97–D-121 → `●`, 25/25), same calendar day as dispatch: re-verified every P-275/P-276 target-shape claim directly against LIVE `09.Search` source rather than trusting the prior pass's draft or either domain's prose — read `09.Search/SharedKernel.Search.Abstractions/Abstractions/{ISearchIndex,ISearchIndexProvisioner,ISearchProviderDescriptor,ISearchDocument}.cs`, every `Models/*.cs` type the target shape depends on, `Constants/SearchWellKnown.cs`, `Errors/SearchErrors.cs`, and both `SharedKernel.Search.Meilisearch/Options/MeilisearchOptions.cs`/`SharedKernel.Search.ElasticSearch/Options/ElasticSearchOptions.cs`. **ZERO drift found anywhere** — every member count, property name, and error-factory signature the original design pass drafted from `09.Search/CLAUDE.md` prose turned out to match the eventually-shipped code exactly. **The central finding: D-121's HARD BLOCKER claim went stale within the same WO-044 dispatch window.** `09.Search` shipped its own Design/Scaffold/Core phases in the interim — `09.Search/state-map.md`'s own Package Board now shows `SharedKernel.Search.Abstractions`/`.Meilisearch`/`.ElasticSearch` all `Core` phase `●`, and its own Blocked section confirms the dependency direction is now the OPPOSITE of what D-121 originally recorded: `09.Search`'s own `SK.09.Tests` T-13–T-17/T-21–T-26 are `⚑` Blocked waiting on THIS package's `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` (P-275) — making D-97–D-102's design-lock the critical path for another domain, not just this one. **Per this domain's own established rule** (now made explicit as a standing Implementation Rule: "once the upstream phase ships, re-verify directly on disk and correct the blocked tasks straight back to `○` Pending"), the 8 tasks that were `⚑` Blocked on D-121's stale claim — C-66, C-67, C-68, C-69 (Core), T-50, T-51 (Tests), DO-21, DO-22 (Docs) — are corrected back to `○` Pending in `state-map.md`; this is bookkeeping, not implementation — the actual `Search/` code is NOT written in this Design-confirmation pass and remains a future `SK.16.Core`/`SK.16.Tests`/`SK.16.Docs` implementer session's work. The `Search/` Interface Contracts section's "HARD BLOCKER VERIFICATION" block rewritten to a "BLOCKER-CLEARANCE VERIFICATION" block (mirroring `Storage/`'s own precedent exactly); `[STATUS: Planned — P-276/WO-044, HARD-BLOCKED]` markers on all five `Search/` types shortened to `[STATUS: Planned — P-276/WO-044]` (still un-implemented — Core phase has not run — but no longer genuinely blocked); the Packages-table row and folder-map row corrected to match. `SK.16.Design` now 121/121 `●` — phase promoted to root. `dotnet build SharedKernel.Testing.csproj -c Release` reconfirmed 0 errors (pre-existing NU1903/CS1574 advisory warnings only, none introduced by this documentation-only pass — no `.cs` files were added or changed; no consuming-domain regression check applicable for the same reason). `SK.16.Scaffold` (S-25–S-28) and the now-unblocked `SK.16.Core` (C-64–C-69) remain the next sessions' work (testing-phase-implementer).
- [2026-07-19] sync-brain pass: swept two residual stale HARD-BLOCKED references the D-121 correction had missed — a `Containers/` cross-reference note and both `Add*` DI Registration code-sample comments — plus rewrote the Test Rules `Search/` routing paragraph from present-tense-blocked to past-tense-cleared, matching `Storage/`'s own precedent phrasing exactly (testing-phase-implementer, sync-brain)
- [2026-07-19] `SK.16.Scaffold` closed for WO-044 (S-25–S-28 → `●`, 28/28): added `Testcontainers`/`Testcontainers.Elasticsearch` `PackageReference`s (`4.13.0`) and the `SharedKernel.Search.Abstractions` `ProjectReference`; bumped the four pre-existing `Testcontainers.PostgreSql`/`.Redis`/`.RabbitMq`/`.Minio` pins from `4.1.0` to `4.13.0`. **New finding, not a drop-in**: the version bump obsoleted each existing builder's parameterless constructor (`RedisBuilder()`/`MinioBuilder()`/`RabbitMqBuilder()`/`PostgreSqlBuilder()`) in favor of a `ctor(string image)` overload — confirmed via each package's own shipped XML docs, not assumed — surfacing 4 new `CS0618` warnings; fixed by rewriting all four `Containers/*ContainerFixture.cs` files to construct via the image-string constructor directly, zero behavioral change, reconfirmed via a real-Docker run of `SharedKernel.Testing.SelfTests`' `Containers/` suite (12/12 passing). Independently confirmed the identical `ctor(string image)` overload exists on the base `ContainerBuilder` and `ElasticsearchBuilder` types (the not-yet-implemented `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` targets, C-64/C-65) — Technology Stack gained a CONSTRUCTOR RULE row, the `Containers/` VERSION-ALIGNMENT DECISION block gained a confirmation paragraph, and both fixtures' `.InitializeAsync()` design notes were corrected from `.WithImage(...)` phrasing to the constructor-based pattern so the future Core-phase implementer doesn't write code that immediately warns obsolete. Also corrected S-28's own stale `state-map.md` prose (the "genuinely EMPTY placeholder" claim, true at original WO-044 design time but stale by this Scaffold session — `09.Search/SharedKernel.Search.Abstractions` now ships 35 real compiled `.cs` files). `dotnet build SharedKernel.Testing.csproj -c Release` succeeds, 2 Warning(s) (both pre-existing NU1903 advisories, zero new), 0 Error(s). Downstream compile check on `SharedKernel.Storage.S3.Tests`/`SharedKernel.Presentation.SignalR.Tests` (both consume the bumped fixtures directly) — both clean. No `[STATUS: Planned]` markers flipped — this phase touched only `SharedKernel.Testing.csproj` references, not the fake/fixture implementations themselves (C-64–C-69 remain future Core-phase work). `SK.16.Scaffold` now 28/28 `●` — phase promoted to root (testing-phase-implementer, sync-brain)
- [2026-07-20] SK.16.Core closed (C-64–C-69, 69/69 `●`) — flipped all five remaining `[STATUS: Planned]` markers (`Containers/MeilisearchContainerFixture`/`ElasticsearchContainerFixture`, `Search/InMemorySearchIndex<TDocument>`/`InMemorySearchIndexProvisioner`/`InMemorySearchProviderDescriptor`/both `Add*` extensions); pinned and documented the two container image tags chosen at implementation time (`getmeili/meilisearch:v1.20.0`, `docker.elastic.co/elasticsearch/elasticsearch:9.4.2` to match the 9.4.2 client exactly), both confirmed to exist via `docker manifest inspect`; corrected the Elasticsearch fixture's `.Username`/`.Password` note to the concrete sourcing mechanism (`ElasticsearchBuilder.DefaultUsername`/`.DefaultPassword` public constants, since the module exposes neither `WithUsername` nor `GetPassword()`); documented an implementer judgment call extending `CountAsync`/`DeleteByFilterAsync`/`EnumerateAsync` with the same fail-closed tenant-scope-missing check `SearchAsync` already had, justified by cross-checking both real provider adapters' shared `CompileWithTenantScope`-equivalent helper; corrected `GetAsync`'s NOTE to match the shipped dictionary-lookup-plus-comparison implementation rather than a literal filtered-evaluator route; documented a pre-existing CS8509 "switch not exhaustive" warning shared by the fake and the real `09.Search` filter compilers alike (confirmed via a clean rebuild of `SharedKernel.Search.Meilisearch`); removed stale `(PLANNED — P-276/WO-044)` DI-registration comments; updated both Test Rules bullets to reflect Core-phase completion (SelfTests coverage — T-48–T-51 — still outstanding); annotated (not rewrote) the Search/ BLOCKER-CLEARANCE VERIFICATION note's now-stale forward-looking sentence. Verified via real-Docker smoke tests (both `Containers/` fixtures) and a 65-assertion functional smoke harness (all three `Search/` fakes + DI extensions); 344/344 `SharedKernel.Testing.SelfTests` regression clean; `dotnet build` 0 errors (testing-phase-implementer, sync-brain)
- [2026-07-20] SK.16.Tests closed (T-48–T-51, 51/51 `●`) — committed `Containers/MeilisearchContainerFixtureTests.cs`/`ElasticsearchContainerFixtureTests.cs` (6 tests, Docker-gated) and the new `Search/` self-test suite (`SearchTestFixtures.cs` + `InMemorySearchIndexTests.cs`/`InMemorySearchIndexProvisionerTests.cs`/`InMemorySearchProviderDescriptorTests.cs`/`SearchServiceCollectionExtensionsTests.cs`, 75 tests); rewrote both Test Rules bullets that previously described these as "throwaway, never committed, still need to be written" — corrected to reflect committed, passing coverage. Zero interface/contract drift found while writing assertions (re-verified `SearchIndexDefinition`/`SearchFieldDefinition`/`SearchRequest`/`SearchFilter`/`TenantScope`/`SearchErrors` directly against live `09.Search` source). Full regression 429/429 against a real Docker daemon; `dotnet build` 0 errors on both `SharedKernel.Testing.csproj`/`SharedKernel.Testing.SelfTests.csproj`. Only `SK.16.Docs` (DO-19–DO-22) remains to fully close WO-044 (testing-phase-implementer, sync-brain)
- [2026-07-21] WO-045 (P-283/P-284) processed (testing-arch-planner): read `10.Intelligence/CLAUDE.md` in full (995 lines — not from memory) for the live, RATIFIED (P-279, same work order) target signatures of `IVectorRecord`/`VectorValue`, `IEmbeddingGenerator`+`EmbeddingResult`/`EmbeddingBatchResult`/`TokenUsage`, `IVectorCollection<TRecord>`'s full write/read/provisioning surface, the closed 8-node `VectorFilter` AST, `TenantScope`, `VectorCollectionDefinition`/`VectorFieldDefinition`/`VectorDistanceMetric`+`Fingerprint`, `IVectorCollectionProvisioner`, `IVectorProviderDescriptor`, `ISemanticKernel`+`ChatMessage`/`CompletionRequest`/`CompletionResult`/`CompletionChunk`, `ICompletionProviderDescriptor`, `IntelligenceErrors`, and `IntelligenceWellKnown`. Independently verified ON DISK (via `10.Intelligence/state-map.md`'s own Package Board) that `SharedKernel.AI.Abstractions.csproj` is a genuinely empty placeholder — zero `.cs` files — confirming P-284 is the THIRD hard design-ahead-of-schedule occurrence in this domain's history. **P-283** adds two new `Containers/` fixtures: `QdrantContainerFixture`/`MilvusContainerFixture`. Verified via live WebFetch against nuget.org and the `testcontainers-dotnet` GitHub source (2026-07-21, not assumed): both `Testcontainers.Qdrant` and `Testcontainers.Milvus` EXIST as OFFICIAL dedicated modules at latest stable `4.13.0` — matching this package's existing `Testcontainers.*` floor exactly, so NO version-bump ceremony is required this time (unlike the `Testcontainers.Elasticsearch` addition at WO-044). Confirmed via direct source read: `QdrantContainer` exposes `GetGrpcConnectionString()`/`GetHttpConnectionString()` with a built-in `/readyz` wait strategy; `MilvusContainer` exposes `GetEndpoint()` with a built-in `UntilContainerIsHealthy()` wait strategy AND already runs genuine single-container standalone Milvus via its own `DEPLOY_MODE=STANDALONE`/`ETCD_USE_EMBED=true` defaults (embedded etcd, no external sidecar) — directly satisfying the phase's minimal-standalone-deployment acceptance criterion with zero fixture-level orchestration. Candidate image-tag pins (`qdrant/qdrant:v1.13.4`, `milvusdb/milvus:v2.3.10` — each module's own last-known-good default before its parameterless ctor was obsoleted) are deferred to Core-phase `docker manifest inspect` re-verification, mirroring the `Meilisearch`/`Elasticsearch` precedent. Neither fixture reconciles its property names against a `QdrantOptions`/`MilvusOptions` type 1:1 (unlike `MinioContainerFixture` et al.) because those types are not yet designed in `10.Intelligence` — folded into that domain's own Core-phase tasks C-05/C-08. Neither fixture takes a `ProjectReference` to any `SharedKernel.AI.*` package, so P-283 carries NO blocker — 16 new tasks (D-122–D-127, S-29–S-31, C-70–C-72, T-52/T-53, DO-23/DO-24), all `○`. **P-284** adds a new `Intelligence/` capability folder (`SharedKernel.Testing.Intelligence` namespace, the first mapped to `10.Intelligence`) with SIX deliberately independent fakes (no constructor/type coupling between any pair, the SECOND application of the non-coupling pattern after `Search/`'s original three-type case): `InMemoryEmbeddingGenerator` (deterministic SHA-256-hash-derived vectors of the declared dimension, never real randomness); `InMemoryVectorCollection<TRecord>` (full write/read/scroll contract, fail-loud model-identity/dimension/tenant-scope pre-flight pipelines, and — the one deliberate FIDELITY IMPROVEMENT over the `Search/` precedent — REAL cosine/dot-product/Euclidean `Score`/`Rank` computation, since vector similarity is closed-form arithmetic a fake can faithfully replicate unlike BM25 relevance; its `VectorFilter` evaluator resolves fields via a DIRECT DICTIONARY LOOKUP into `IVectorRecord.Metadata`, a deliberate SIMPLIFICATION relative to `Search/`'s reflection-based evaluator since `Metadata` is already a `IReadOnlyDictionary<string, VectorValue>`); `InMemoryVectorCollectionProvisioner`/`InMemoryVectorProviderDescriptor` (mirroring `InMemorySearchIndexProvisioner`/`InMemorySearchProviderDescriptor`'s exact shape); `InMemorySemanticKernel` (FIFO canned-response/streaming-chunk/streaming-failure queues, `.SentRequests` assertion list — NEVER generates text itself, the phase's own hard acceptance criterion) and `InMemoryCompletionProviderDescriptor`; plus four `Add*` DI extensions (`AddInMemoryVectorCollection<TRecord>` deliberately SINGLETON — a documented deviation from the real SCOPED per-collection registration, mirroring `AddInMemorySearchIndex<TDocument>`'s identical precedent; the other three match their real production lifetimes). **This is the domain's THIRD hard design-ahead-of-schedule occurrence** — a new variant worth naming: `10.Intelligence`'s own Design (P-279) is fully RATIFIED, the strongest starting position of the three occurrences, yet the blocker still applies in full since prose ratification is not compiled code. Design (D-128–D-138) and Scaffold (S-32/S-33, an empty-project `ProjectReference` always builds) proceed fully `○` unblocked; Core (C-73–C-79), Tests (T-54), and Docs (DO-25) — 9 tasks — are marked `⚑` Blocked pending `10.Intelligence`'s own `SK.10.Core`. 22 new tasks for P-284. Combined WO-045 total: 38 new tasks (D-122–D-138, S-29–S-33, C-70–C-79, T-52–T-54, DO-23–DO-25); domain total 293 → 331. `## Blocked` section, Cross-Domain Dependencies table, Package Board, and Overall Progress table all updated in `state-map.md`. `16.Testing/CLAUDE.md` refreshed in the same pass — Packages/Technology Stack gain the `Testcontainers.Qdrant`/`.Milvus`/`SharedKernel.AI.Abstractions` references and blocker notes; `Containers/` Interface Contracts gains both new fixtures plus a no-version-bump-needed note; new `Intelligence/` Folder/Namespace Map row and full Interface Contracts section (all `[STATUS: Planned]`, HARD-BLOCKED); Implementation Rules gains the third-hard-blocker note, the container-fixture-vs-in-memory-fake blocker-asymmetry-confirmed-a-third-time note, the direct-dictionary-lookup-vs-reflection filter-evaluator contrast, and the Score/Rank fidelity-improvement note; DI Registration gains all four new (blocked) extensions; Test Rules gains routing notes for both new capability areas.
- [2026-07-22] SK.16.Core: C-70–C-72 implemented (`Containers/QdrantContainerFixture.cs`, `Containers/MilvusContainerFixture.cs`) — re-verified `10.Intelligence/SharedKernel.AI.Abstractions` directly on disk first (still empty, `SK.10.Core` still `○` 0/11 — C-73–C-79 correctly remain `⚑` Blocked, untouched). Flipped both fixtures' `[STATUS: Planned — P-283/WO-045]` markers. Confirmed via direct reflection against the shipped `Testcontainers.Qdrant`/`.Milvus` 4.13.0 DLLs (the XML docs alone don't state return types) that `QdrantContainer.GetGrpcConnectionString()`/`.GetHttpConnectionString()` return `string` and `MilvusContainer.GetEndpoint()` returns `Uri`, matching the drafted design exactly. Both candidate image tags (`qdrant/qdrant:v1.13.4`, `milvusdb/milvus:v2.3.10`) reconfirmed via `docker manifest inspect` — both resolve, no correction needed. Smoke-tested both fixtures end-to-end against a real Docker daemon via a throwaway console app (built, run, deleted): neither module's own built-in wait strategy (Qdrant's `/readyz`, Milvus's `UntilContainerIsHealthy()`) showed a readiness race, so neither fixture needed an `ElasticsearchContainerFixture`-style post-start poll override — corrected both design notes from "no override anticipated unless..." to the confirmed outcome. `dotnet build SharedKernel.Testing.csproj -c Release` succeeds, 0 errors, 7 pre-existing warnings only. No consuming-domain regression check applies — no `SharedKernel.AI.Qdrant.Tests`/`.Milvus.Tests` project exists yet. `SK.16.Core` Overall Progress corrected to 72/79 done, 0 pending, 7 blocked, state `⚑` (every remaining Core task is now blocked, none merely unstarted). T-52/T-53 (Tests, SelfTests proof) and DO-23/DO-24 (Docs, already-accurate per this pass) remain future-session work; C-73–C-79 remain `⚑` Blocked pending `10.Intelligence`'s `SK.10.Core` (testing-phase-implementer).
- [2026-07-22] SK.16.Tests: T-52/T-53 implemented (`Containers/QdrantContainerFixtureTests.cs`, 4 tests; `Containers/MilvusContainerFixtureTests.cs`, 3 tests) — phase does NOT close, T-54 remains `⚑` Blocked pending `10.Intelligence`'s `SK.10.Core` (re-confirmed still empty on disk, untouched this session per the dispatching brief). Both new test classes mirror the established Docker-gated `Containers/` pattern: throw-before-`InitializeAsync` guards, full-lifecycle smoke tests (Qdrant: `GET /readyz` + `GET /collections` over `HttpEndpoint`; Milvus: `GET /healthz` over the management port), a bare gRPC-channel-open probe via a plain `TcpClient` connect (no `Qdrant.Client`/`Milvus.Client`/protobuf dependency added, preserving both fixtures' own isolation), and clean `DisposeAsync` shutdown. Smoke-probed both Testcontainers modules via a throwaway console app first (built, run, deleted) to nail down exact endpoint-string formats and confirm the Milvus management port (9091) has no fixed/derivable relationship to the gRPC port's own host-mapped port — read instead via `MilvusContainer.GetMappedPublicPort(9091)`, reached through the fixture's private `_container` field via reflection (the same class of test-only reflection already sanctioned by `Domain/SpecificationAssert`). The "no external etcd/MinIO container" acceptance criterion is proven via a `docker ps --format "{{.Image}}"` before/after diff, reading the child process's stdout and stderr CONCURRENTLY via `Task.WhenAll` — a sequential read pattern was tried first (via `docker logs`, for evidence of embedded etcd startup) and deadlocked on pipe backpressure; switched to the smaller-output `docker ps` diff with concurrent stream reads instead, and documented the technique's narrow residual race against a concurrently running sibling fixture test inline. `dotnet build` clean on both `SharedKernel.Testing.csproj`/`SharedKernel.Testing.SelfTests.csproj`, 0 errors. `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` — real Docker daemon — passes 436/436 (429 pre-existing + 7 net new); confirmed zero leftover containers after the run. `Containers/` Test Rules bullet updated from "will be proven" to "proven," with both new techniques (reflection-based private-field access, concurrent-stream-read `docker ps` diffing) recorded for future `Containers/` test-writers. `SK.16.Tests` Overall Progress corrected to 53/54 done, 0 pending, 1 blocked, state `⚑`. No consuming-domain regression check applies — no `SharedKernel.AI.Qdrant.Tests`/`.Milvus.Tests` project exists yet; adoption remains a `10.Intelligence` cross-domain follow-up (testing-phase-implementer).

- [2026-07-22] SK.16.Tests closed (T-54, 54/54 `●`) — implemented and proved all six `Intelligence/` in-memory fakes plus `IntelligenceServiceCollectionExtensions` in `SharedKernel.Testing.SelfTests/Intelligence/` (`IntelligenceTestFixtures.cs` + 7 test files, 119 tests) against the live `10.Intelligence/SharedKernel.AI.Abstractions` source (C-73–C-79), zero drift. Corrected three stale `[STATUS: Planned]`/blocked references left over from the prior Core-phase close that had not been flipped: the `Intelligence/` Folder/Namespace Map row, the DI Registration code-sample comment block, and the Test Rules routing bullet — all now reflect implemented-and-proven status. Full regression `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` (real Docker daemon) passes 555/555 (436 pre-existing + 119 net new); confirmed zero leftover containers. `dotnet build` clean on both `SharedKernel.Testing.csproj`/`SharedKernel.Testing.SelfTests.csproj`. `SK.16.Tests` now 54/54 `●` — phase promoted to root. Only `SK.16.Docs` (DO-25, 24/25 `◐`) remains to fully close WO-045 (testing-phase-implementer).
- [2026-07-22] `SK.16.Design` closed for WO-046 (D-139–D-147 → `●`, 147/147), same calendar day as dispatch (testing-phase-implementer). Pure Design-confirmation pass, no `.cs` code written. Re-read `17.Workflows/CLAUDE.md` in full (519 lines) and re-checked every D-139–D-146 target-shape claim already drafted into this file's `Workflows/` Interface Contracts section against it — **zero drift found** on any `IWorkflowDispatcher`/`IWorkflowHandle`/`IWorkflowHandle<TResult>`/`IWorkflowIdFactory` member signature, the tenant-scope-fail-loud rule, or the singleton-DI-lifetime-deviation rationale; `WorkflowExecutionDescription`'s member-shape-UNCONFIRMED flag (D-144) re-checked and confirmed still accurate (the live brain defines the member's return type but never its member list). **D-147's blocker claim was independently re-verified directly on disk — and, unlike all three prior hard-blocker occurrences (`Storage/`, `Search/`, `Intelligence/`, each of which cleared within its own Design-confirmation pass), it did NOT clear this time**: `17.Workflows/SharedKernel.Workflows.Temporal.csproj` is still a genuinely empty placeholder, and `17.Workflows/state-map.md`'s own Package Board/Phase-Design table confirms that domain has not yet closed even its own `SK.17.Design` phase (D-01–D-16 all still `○`) — a materially earlier state than any prior occurrence had reached. Per this domain's own established rule, no state correction was made: the `Workflows/` Interface Contracts section, the Packages-table blocker note, and the Folder/Namespace Map row all remain exactly as originally written (still `HARD-BLOCKED`, still un-implemented) — nothing to flip. `dotnet build SharedKernel.Testing.csproj -c Release` reconfirmed 0 errors, 7 pre-existing warnings only, none introduced (documentation-only pass, no `.cs` files touched). `sync-brain` judged unnecessary beyond this changelog entry — no contract text or `[STATUS: Planned]` marker changed, only a state-map bookkeeping confirmation. `SK.16.Design` now 147/147 `●`, promoted to root. `SK.16.Scaffold` (S-34–S-36) remains the next actionable work; the `Workflows/` Core/Tests/Docs tasks stay `⚑` Blocked pending `17.Workflows`'s own `SK.17.Core`.
- [2026-07-22] P-288/WO-046 processed (testing-arch-planner): read `17.Workflows/CLAUDE.md` in full (519 lines — not from memory) for the live, RATIFIED (P-287, same work order) target signatures of `IWorkflowDispatcher` (StartAsync x3 overloads, GetHandle x2, DescribeAsync, the mandatory-separate-`TenantScope` rule, the workflow-id-as-durable-idempotency-key rule, the deliberate absence of a `StartAndWaitAsync` convenience), `IWorkflowHandle`/`IWorkflowHandle<TResult>` (Signal/Query/Cancel/Terminate/GetResult, the Cancel-vs-Terminate distinction, the "TerminateAsync always requires a non-empty reason" rule), `IWorkflowIdFactory`, `TenantScope`, `WorkflowErrors`, and `WorkflowWellKnown`. Independently verified ON DISK (not merely trusted from the brain's own prose, per this domain's now four-times-applied discipline) that `17.Workflows/SharedKernel.Workflows.Temporal/SharedKernel.Workflows.Temporal.csproj` is a genuinely empty placeholder — bare `TargetFramework`/`ImplicitUsings`/`Nullable`, zero references, zero `.cs` content beyond generated `obj/` artifacts — confirming P-288 is the FOURTH hard design-ahead-of-schedule occurrence in this domain's history (after `Storage/` P-269/WO-043, `Search/` P-276/WO-044, `Intelligence/` P-284/WO-045). Adds a new `Workflows/` capability folder (`SharedKernel.Testing.Workflows` namespace, the first mapped to `17.Workflows`) — `InMemoryWorkflowDispatcher` (`ConcurrentDictionary`-backed shared execution store, tenant-scope fail-loud on every dispatch member, `AlreadyStarted`-on-duplicate-running-id semantics, `ShouldHaveStarted<TWorkflow>()`/`ShouldHaveStartedOnce<TWorkflow>()`/`ShouldNotHaveStarted<TWorkflow>()`, `CompleteWorkflow<TResult>`/`FailWorkflow` test-setup helpers standing in for real execution, `SimulateFailure`, `Reset()`), `InMemoryWorkflowHandle` (Signal/Query/Cancel/Terminate, idempotent Cancel/Terminate as a documented fake-only simplification, an `ArgumentException` guard on a blank `TerminateAsync` reason, `ShouldHaveSignalled`/`ShouldNotHaveSignalled`/`ShouldHaveBeenCancelled`/`ShouldHaveBeenTerminated`/`ShouldHaveBeenQueried` assertion helpers — the distinguishable cancel-vs-terminate outcomes the phase's own acceptance criteria named explicitly), and `InMemoryWorkflowHandle<TResult>` (composes `InMemoryWorkflowHandle` internally per this package's sealed-no-inheritance rule; `GetResultAsync` never blocks/polls/`Task.Delay`s — it throws `InvalidOperationException` if `CompleteWorkflow`/`FailWorkflow` was never called for that workflow id, the domain's starkest application yet of the "no real waiting" hard rule, since a workflow's eventual result is the one case where the REAL implementation is expected to await something genuinely not-yet-happened). Plus `AddInMemoryWorkflowDispatcher()` — singleton, a deliberate deviation from the real scoped `IWorkflowDispatcher` registration, mirroring the `InMemoryMessageBus`/`InMemorySearchIndex<TDocument>`/`InMemoryVectorCollection<TRecord>` precedent exactly. **Unlike all three prior hard-blocker occurrences, this one pairs with NO `Containers/` fixture** — `17.Workflows`'s own brain states plainly that `WorkflowEnvironment`/`WorkflowReplayer` (Temporal's own in-box time-skipping test server) replaces the container fixture for that domain, so there is nothing for a fixture to stand in for; the entire `Workflows/` folder is blocked as one unit rather than splitting into an unblocked-fixture/blocked-fake pair the way `Storage/`/`Search/`/`Intelligence/` each did. Two upstream record shapes (`WorkflowStartOptions`'s exact business-key/`IdReusePolicy`/`IdConflictPolicy` field names, `WorkflowExecutionDescription`'s member shape) are explicitly flagged as UNCONFIRMED at this Design pass — `17.Workflows` ships no compiled model yet — and deferred to Core-phase re-verification rather than guessed, mirroring this domain's own established discipline for any upstream record type that has not yet compiled. The `TenantScope.None`-always-rejected design choice (no per-index/collection "declares a tenant field" toggle exists for workflows the way it does for `Search/`/`Intelligence/`) is likewise flagged as a deliberate, conservative reading pending reconciliation once `17.Workflows`'s own Core phase ships. 19 new tasks (D-139–D-147, S-34–S-36, C-80–C-84, T-55, DO-26); domain total 331 → 350. Design (D-139–D-147) and Scaffold (S-34–S-36) — 12 tasks — are `○` Pending and fully actionable (an empty-project `ProjectReference` always builds); Core (C-80–C-84), Tests (T-55), and Docs (DO-26) — 7 tasks — are `⚑` Blocked pending `17.Workflows`'s own `SK.17.Core`. `## Blocked` section, Cross-Domain Dependencies table (new `17.Workflows` row plus an informational reverse-direction row), Package Board, and Overall Progress table all updated in `state-map.md`. `16.Testing/CLAUDE.md` refreshed in the same pass — Packages table row gains the `SharedKernel.Workflows.Temporal` blocker note; Folder/Namespace Map gains the `Workflows/` row plus a folder-isolation-paragraph sentence; new full `Workflows/` Interface Contracts section (HARD-BLOCKED); Implementation Rules gains the fourth-hard-blocker note, the no-paired-container-fixture contrast, and the `GetResultAsync` no-poll design-decision note; DI Registration gains the new (blocked) extension; Test Rules gains a routing note flagging that `17.Workflows.Temporal.Tests`'s own primary strategy is `WorkflowEnvironment`, not this fake, so adoption there is not automatically assumed.
- [2026-07-28] `SK.16.Scaffold` closed for WO-049 (S-37–S-41 → `●`, 41/41, promoted to root): added `ProjectReference`s to `SharedKernel.Cryptography` and `SharedKernel.FeatureManagement` (both 01.Core) to `SharedKernel.Testing.csproj`; both `Cryptography/`/`FeatureManagement/` folder rows were already present in the Folder/Namespace Map (no empty directory created, per the established deferred-to-first-Core-file precedent); confirmed no new third-party `PackageReference` was needed. **While verifying the S-37/S-38 claims directly on disk (per this task's own instruction to never trust prose alone), both remaining `01.Core` blockers from the Design pass were found to have cleared**: `01.Core`'s own `SK.01.P296` closed 2026-07-27 (`Hashing/IContentHasher.cs` now real and compiled with its full three-member contract) and `SK.01.P298` closed 2026-07-28 (the live `Abstractions/IFeatureManager.cs` now declares `GetVariantAsync`/`GetVariantAsync<TContext>` alongside the two pre-existing boolean overloads) — both re-read directly from source with zero drift from this file's own target shape. Per this domain's own established blocker-clearance discipline (demonstrated four times previously for `Storage/`/`Search/`/`Intelligence/`/`Workflows/`), corrected the eight formerly-`⚑`-Blocked tasks (C-92–C-95 in Core, T-57–T-59 in Tests, DO-28/DO-29 in Docs) back to `○` Pending in `state-map.md` — a state-correction only; none of the eight fakes are implemented in this pass, that remains a future `SK.16.Core`/`SK.16.Tests`/`SK.16.Docs` implementer session's work. `state-map.md`'s `## Blocked` section, Cross-Domain Dependencies table (both `01.Core` rows corrected to "Available"), Package Board, and Overall Progress table all updated to reflect zero remaining `⚑` entries. `16.Testing/CLAUDE.md` updated in the same pass: both `Cryptography/`/`FeatureManagement/` `BLOCKER` notes rewritten to `BLOCKER-CLEARANCE VERIFICATION` notes (mirroring the `Storage/`/`Search/`/`Intelligence/`/`Workflows/` precedent exactly); `[STATUS: Planned — BLOCKED ...]` markers on `FakeContentHasher`, `AddFakeCryptography()`, `FakeFeatureManager`, and `AddFakeFeatureManagement()` shortened to `[STATUS: Planned]` (still un-implemented, no longer blocked); Packages-table row, Folder/Namespace Map paragraph, Implementation Rules bullets, and DI Registration comments all corrected to past-tense/resolved phrasing. `dotnet build SharedKernel.Testing.csproj -c Release` succeeds, 0 errors, 9 pre-existing warnings only (2 `NU1903`, 2 `CS1574` in `01.Core`, 5 `CS8509` in `Intelligence/`/`Search/`), none introduced by this pass (no new `.cs` files — only `ProjectReference`s, `state-map.md`, and `CLAUDE.md` changed). No consuming-domain regression check applies — no fakes were implemented in this pass. `SK.16.Core`/`SK.16.Tests`/`SK.16.Docs` remain the next sessions' work, now fully unblocked (testing-phase-implementer).
- [2026-07-23] `SK.16.Core` closed for WO-046 (C-80–C-84 → `●`, 84/84): re-verified `17.Workflows/SharedKernel.Workflows.Temporal` directly on disk — the blocker HAS cleared, 43 real `.cs` files now ship (`IWorkflowDispatcher`, `IWorkflowHandle`/`IWorkflowHandle<TResult>`, `IWorkflowIdFactory`, `TenantScope`, `WorkflowStartOptions`, `WorkflowExecutionDescription`, `WorkflowErrors`, `WorkflowWellKnown`, `WorkflowBase`, all re-read directly from source). Implemented all five `Workflows/` files (`InMemoryWorkflowExecution.cs`, `InMemoryWorkflowDispatcher.cs`, `InMemoryWorkflowHandle.cs`, `InMemoryWorkflowHandle{TResult}.cs`, `WorkflowServiceCollectionExtensions.cs`) exactly per the locked design, with **two corrections made against the pre-verification draft, both now the ground truth in the Interface Contracts block above**: (1) the real `IWorkflowDispatcher.GetHandle`/`GetHandle<TResult>` return `IWorkflowHandle`/`IWorkflowHandle<TResult>` directly (never `Task<Result<...>>`) and throw `ArgumentException` for `TenantScope.None`/a null-or-whitespace `workflowId` — the tenant-scope-fail-loud NOTE's "returns `Result.Failure`" claim is now correctly scoped to `StartAsync`/`DescribeAsync` only; (2) `WorkflowLifecycleStatus` is `public`, not `internal` — required for it to be the return type of the public `InMemoryWorkflowHandle.Status`/`InMemoryWorkflowHandle<TResult>.Status` members (an `internal` enum there is `CS0051`); `InMemoryWorkflowExecution` is a mutable `internal sealed class`, not a "record" as originally drafted. Added `InMemoryWorkflowDispatcher.ConfigureQueryHandler<TQueryResult>(workflowId, queryName, handler)` — a necessary test-setup member the design implied via `InMemoryWorkflowExecution.QueryHandlers` but never explicitly named on the dispatcher itself. `Result<T>`/`Error` fully qualified throughout per the known `GreenDonut`/HotChocolate ambiguity in this csproj (identical to the `Storage/`/`Search/`/`Intelligence/` precedent). `dotnet build SharedKernel.Testing.csproj -c Release` succeeds, 0 errors, 7 pre-existing warnings only (2 `NU1903`, 5 `CS8509` in `Intelligence/`/`Search/`), none introduced by the five new files. No consuming-domain regression check applies — no `SharedKernel.Workflows.Temporal.Tests` suite exists on disk yet (a separate session is concurrently authoring it, out of this session's write scope). Packages-table row, Folder/Namespace Map row, and the `Workflows/` Interface Contracts block's blocker banner all corrected from `HARD-BLOCKED` to a `BLOCKER-CLEARANCE VERIFICATION` note, mirroring the `Storage/`/`Search/`/`Intelligence/` precedent exactly. T-55 corrected `⚑`→`○` (its sole blocker cleared); DO-26 corrected `⚑`→`●` in the same pass (every public member across all five files already carries an XML doc comment written during this implementation — verified, not assumed, mirroring the DO-19–DO-25 pattern). `SK.16.Core`/`SK.16.Docs` are now `●` — both promoted to root; `SK.16.Tests` (`◐`, 54/55) is the only remaining phase in the whole domain, with T-55 the only remaining task and zero `⚑` Blocked entries anywhere (testing-phase-implementer).
- [2026-07-24] `SK.16.Tests` closed for WO-046 (T-55 → `●`, 55/55, promoted to root): proved all `Workflows/` fakes via 85 new tests in `SharedKernel.Testing.SelfTests/Workflows/` (`WorkflowsTestFixtures.cs` marker types + `InMemoryWorkflowDispatcherTests.cs`/`InMemoryWorkflowHandleTests.cs`/`InMemoryWorkflowHandleOfTTests.cs`/`WorkflowServiceCollectionExtensionsTests.cs`/`WorkflowsCsprojScopeLockTests.cs`). Updated the `Workflows/` Test Rules bullet to reflect completion (mirroring the `Search/`/`Intelligence/` precedent) and recorded a DI-registration nuance discovered during authoring — `AddInMemoryWorkflowDispatcher()` maps only the interface, not the concrete type, unlike some sibling `Add*` extensions. Full regression `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` against a real Docker daemon passes 640/640 (614 non-container + 26 Docker-gated `Containers/`); `dotnet build` clean on both packages, 0 errors, 0 new warnings. No consuming-domain regression check applies — no `SharedKernel.Workflows.Temporal.Tests` reference to `SharedKernel.Testing` exists. **This closes WO-046/P-288 in full and, since every other phase was already `●`, the entire `16.Testing` domain end to end (Design/Scaffold/Core/Tests/Docs/Published all `●`)** (testing-phase-implementer, sync-brain).
- [2026-07-24] `Containers/QdrantContainerFixture`'s pinned image corrected `qdrant/qdrant:v1.13.4` → `v1.16.0` (external fix, by `intelligence-phase-implementer` during `10.Intelligence`'s own `SK.10.Tests` session — the first real adoption of this fixture, T-03/T-04). Root cause: `v1.13.4` predates Qdrant server's collection-level metadata feature entirely — verified empirically via a throwaway probe console app (`CreateCollectionAsync(..., metadata: ...)` against a live `v1.13.4` server silently drops the metadata; `GetCollectionInfoAsync().Config.Metadata` comes back `Count == 0`, no error) and cross-checked against Qdrant's own GitHub release notes (collection metadata, qdrant/qdrant#7123, shipped in server `v1.16.0`, 2024-11-17). The original pin was confirmed only via `docker manifest inspect` (existence), never smoke-tested for this specific behaviour — a gap this session's real-consumer usage exposed. Re-verified the identical probe against a live `v1.16.0` server: round-trip works. `v1.16.0` re-confirmed to resolve via `docker manifest inspect` before adoption. This is exactly the kind of drift the "re-verify pins at Core-phase implementation time" guidance anticipates once a real consumer lands — `16.Testing`'s own `Containers/QdrantContainerFixtureTests.cs` (4 tests, generic endpoint-reachability smoke tests only, no metadata assertions) re-run clean against the new tag with zero changes needed. `Containers/` Interface Contracts block and the fixture's own XML doc both updated to record the finding.
- [2026-07-27] P-300/WO-049 processed (testing-arch-planner): read `01.Core/CLAUDE.md`/`state-map.md` in full for the live `Published` (WO-034) `SharedKernel.Cryptography` contract and the design-locked P-296 (`IContentHasher`)/P-298 (`IFeatureManager` variant support) additions, then independently verified every claim directly against the real `.cs` files on disk — all seven baseline `Cryptography` interfaces are real, compiled, zero-drift types; `IContentHasher` does not exist anywhere in `Hashing/`; the live `Abstractions/IFeatureManager.cs` declares only the two boolean `IsEnabledAsync` overloads. Also read `SharedKernel.Cryptography.Tests`' existing internal-only `InMemoryEncryptionKeyProvider`/`InMemoryAsymmetricKeyProvider` doubles directly from source. Adds two new capability folders — the SECOND and THIRD ever anchored to a `01.Core` package (`Clocks/` was the first): `Cryptography/` (`FakeOneWayHasher` — reuses `Pbkdf2OneWayHasher`'s exact self-describing format at a tiny settable `.Iterations`; `FakeSecureRandomGenerator` — real `RandomNumberGenerator` by default, an explicit seeded mode on request; `FakeEncryptionKeyProvider`/`FakeAsymmetricKeyProvider` — promoted verbatim from `SharedKernel.Cryptography.Tests`' own internal doubles, mirroring the WO-040 `ApplicationPipelineTestHarness` promotion precedent; `FakeSymmetricEncryptionService` — non-cryptographic reversible XOR transform, constructor-compatible with `AesGcmEncryptionService`; `FakeAsymmetricSignatureService` — deterministic per-`keyId` HMAC pseudo-signature, reuses the REAL `RsaSignatureServiceKey`/`EcdsaSignatureServiceKey` DI-key constants rather than redeclaring fake-only ones; `FakeHmacSigner`; `FakeContentHasher` — mirrors `Sha256ContentHasher`, `[STATUS: Planned — BLOCKED]` pending P-296; `FakeCryptographyServiceCollectionExtensions.AddFakeCryptography()` — bundles all eight, deliberately diverging from `AddSharedKernelCryptography()` by also registering `IEncryptionKeyProvider`/`IAsymmetricKeyProvider`, `⚑` Blocked transitively pending P-296) and `FeatureManagement/` (`FakeFeatureManager` — a deterministic override map for both the boolean and variant paths, explicitly not a weighted-random allocator, `[STATUS: Planned — BLOCKED]` pending P-298 since a missing interface member blocks the whole type; `FakeFeatureManagementServiceCollectionExtensions.AddFakeFeatureManagement()`). This is the FIFTH design-ahead-of-schedule occurrence in this domain's history (after P-269/WO-043, P-276/WO-044, P-284/WO-045, P-288/WO-046) but the FIRST *partial* one — 7 of `Cryptography/`'s 8 fakes are fully unblocked today because `SharedKernel.Cryptography` is already `Published`, contrasted explicitly against `FeatureManagement/`'s whole-folder block (a missing interface member, not a missing standalone type, blocks the entire fake regardless of how many other members already exist). Also the SECOND folder in this domain's history routed to `SharedKernel.Testing.SelfTests` under the `FakeClock` rationale specifically — `01.Core` can never take a `ProjectReference` back to this package. 35 new tasks (D-148–D-159, S-37–S-41, C-85–C-95, T-56–T-59, DO-27–DO-29); domain total 350 → 385. `SK.16.Design` (D-148–D-159, 12/12) is `●`, promoted to root, same calendar day as dispatch. Sections touched in this file: Packages table (new `Cryptography/`/`FeatureManagement/` blocker paragraph appended to the `SharedKernel.Testing` row); Folder/Namespace Map (two new rows plus an explanatory paragraph sentence); two new full Interface Contracts sections (`Cryptography/`, `FeatureManagement/`, both fully `[STATUS: Planned]`, with an explicit BLOCKER/SCOPE LOCK note each); the cross-cutting Implementation Rules bullet list (two new bullets — the partial-vs-whole-folder blocker distinction, and the reuse-real-DI-key-constants convention); DI Registration (both new, partially-blocked calls plus the manual-per-type alternative, and an updated `Add*`-justification sentence); Test Rules (one new bullet). **Out of this domain's jurisdiction, explicitly not actioned:** the phase input's acceptance criteria also names a root `CLAUDE.md` "What Goes Where" entry for these fakes — root `CLAUDE.md` is exclusively arch-lead's and is never edited by this domain agent; flagged as a follow-up for arch-lead/root-sync, not represented anywhere in this file.
- [2026-07-28] `SK.16.Core` closed for WO-049 (C-85–C-95, 95/95, promoted to root): all 8 `Cryptography/` fakes + `AddFakeCryptography()` and the `FeatureManagement/` fake + `AddFakeFeatureManagement()` implemented against the live `01.Core` source, zero drift on any interface signature. Every `[STATUS: Planned]` marker in both folders' Interface Contracts blocks removed; the Folder/Namespace Map rows and the DI Registration code sample updated to reflect shipped status (the manual per-type alternative gained the previously-missing `IContentHasher`→`FakeContentHasher` line). **One implementation-detail correction against the original design draft**: `FakeContentHasher`'s `Stream`/async overloads do not call `SHA256.HashData(Stream)`/`.HashDataAsync(Stream, ...)` directly as originally drafted — they fully buffer the stream into a `byte[]` first (via `SHA256.HashData(byte[])` on the buffer), because the `.HashedContent` introspection list needs the raw payload bytes, which the BCL's `Stream`-based overloads never expose. Documented explicitly as an accepted test-double trade-off against production's genuinely constant-memory streaming. `dotnet build SharedKernel.Testing.csproj -c Release` succeeds, 0 errors, 9 pre-existing warnings only (2 `NU1903`, 2 `CS1574` in `01.Core/SharedKernel.Core`, 5 `CS8509` in `Intelligence/`/`Search/`), none introduced. `SK.16.Tests`/`SK.16.Docs` (T-56–T-59/DO-27–DO-29) remain to prove/document both folders in `SharedKernel.Testing.SelfTests` (testing-phase-implementer, sync-brain).
- [2026-07-29] P-306/WO-050 processed (testing-arch-planner): read `02.Caching/CLAUDE.md` in full (not from memory) for the live, shipped signatures of `IRedisChannelService` (`PublishAsync`/`SubscribeAsync`/`UnsubscribeAsync` plus the Phase 26 `.ConnectionHealth` addition and its "cache-adjacent ephemeral signaling only, never durable messaging" scope constraint), `IRedisHashService` (`GetFieldAsync<T>`/`SetFieldAsync<T>`/`GetAllFieldsAsync<T>`/`DeleteFieldAsync`/`IncrementFieldAsync`, all `JsonTypeInfo<T>`-parameterized for AOT safety), `ITypedHashStore<T>` (the identical five members minus `JsonTypeInfo<T>`, the AOT-safe per-DTO-type wrapper), `ICacheWarmupStrategy` (`Name`/`Order`/`WarmupAsync(ICacheService, CancellationToken)`), `ConnectionHealthState`, and the Cache warmup rules (`CacheWarmupHostedService` catches per-strategy exceptions, logs at `Error`, and continues — a failed strategy must never crash the pod). Confirmed these four interfaces are the ONLY `SharedKernel.Caching.Abstractions` contracts with zero existing `16.Testing` fake. Adds four new fakes inside the EXISTING, already-`**implemented**` `Caching/` folder: `FakeRedisChannelService` (genuine in-process pub/sub fan-out — `PublishAsync` synchronously invokes every currently-subscribed handler for that channel, catching/swallowing handler exceptions per the real `RedisChannelService`'s own rule — the FIRST `Caching/` fake, and one of very few in this whole package, to simulate cross-call interaction rather than pure passive recording; `.ConnectionHealth` a plain settable property, never driven by a real reconnect event; `.SimulateFailure`), `FakeRedisHashService` (`ConcurrentDictionary<(string,string),object?>`-backed, `JsonTypeInfo<T>` accepted for signature parity only and never invoked since there is no wire format to cross in-memory; `IncrementFieldAsync` throws `InvalidCastException` on a non-`long` field — a fake-only WRONGTYPE-equivalent guard, the first place in this domain a fake throws to surface a caller type-mismatch bug rather than returning a default/no-op; `.SimulateFailure`/`.Seed<T>`), `FakeTypedHashStore<T>` (same semantics minus `JsonTypeInfo<T>`, deliberately an INDEPENDENT fake with its own backing store rather than composing `FakeRedisHashService` — continuing this folder's existing every-fake-independent convention), and `FakeCacheWarmupStrategy` (`constructor(name, order, ConcurrentQueue<string>? executionLog)` — a caller-supplied shared queue, never a static field, is the mechanism a consuming test uses to prove `CacheWarmupHostedService`'s ordering/failure-isolation contract, since this package permits only two documented static-mutable-state exceptions and this is not a third; `.SimulateFailure`/`.OnWarmup`/`.CallCount`). Plus three DI registration extensions: `AddFakeCachingServices()` extended to also cover the first two (six fakes in one call once implemented); new `AddFakeTypedHashStore<T>()` (per-`T`, mirrors `AddTypedHashStore<T>(JsonTypeInfo<T>)`'s shape minus the type-info argument); new `AddFakeCacheWarmupStrategy(name, order, executionLog)` (`TryAddEnumerable`, mirrors `AddCacheWarmup<TStrategy>()`'s multi-strategy shape). Carries **zero cross-domain blocker of any kind** — `SharedKernel.Caching.Abstractions` is already referenced and fully shipped — the first phase in this domain's history to extend an already-`**implemented**`-labeled folder rather than stand up a new one. 18 new tasks added to `state-map.md` (D-160–D-164, S-42, C-96–C-100, T-60–T-64, DO-30/DO-31), all `○`; domain total 385 → 403. Sections touched in this file: Folder/Namespace Map (`Caching/` row extended, plus an explanatory paragraph sentence); `Caching/` Interface Contracts block gains all four new fakes plus the three DI extensions (all `[STATUS: Planned — P-306/WO-050]`), inserted alongside the five already-`**implemented**` types with no change to those; Implementation Rules gains four new bullets (genuine cross-call fan-out, the `InvalidCastException` type-mismatch guard, the shared-queue ordering-proof pattern, `FakeTypedHashStore<T>`'s independence from `FakeRedisHashService`); DI Registration section's code sample and prose both extended; Test Rules gains a routing note for all four new types plus their DI extensions, following the established no-actual-consumer-yet fallback. No `.Tests` project outside `16.Testing` was touched or will be touched by this domain.
- [2026-07-29] `SK.16.Core` closed for WO-050 (C-96–C-100, 100/100, promoted to root): implemented all four `Caching/` fakes plus the extended DI registration, against the live `02.Caching/SharedKernel.Caching.Abstractions` source re-read directly on disk, zero drift from the D-160–D-164 design. `Caching/FakeRedisChannelService.cs` — genuine synchronous in-process pub/sub fan-out on `PublishAsync` (subscription order, handler exceptions caught/swallowed); `UnsubscribeAsync` removes a channel's whole handler bag wholesale rather than mutating it in place, avoiding a torn-bag read racing a concurrent publish; `.ConnectionHealth` a plain settable property, never driven by a real reconnect event. `Caching/FakeRedisHashService.cs` — `ConcurrentDictionary<(string Key, string Field), object?>`-backed; `JsonTypeInfo<T>` accepted for signature parity only, never invoked; `GetFieldAsync<T>`/`GetAllFieldsAsync<T>` use a safe pattern-match cast (`is T typed`), mirroring `FakeCacheService.GetAsync<T>`'s existing convention; `IncrementFieldAsync` throws `InvalidCastException` on a non-`long` field via the `ConcurrentDictionary.AddOrUpdate<TArg>` overload. `Caching/FakeTypedHashStore.cs` (`FakeTypedHashStore<T>`) — an independent fake with its own backing store, never a thin wrapper over `FakeRedisHashService`. `Caching/FakeCacheWarmupStrategy.cs` — `WarmupAsync` increments `.CallCount`, invokes `.OnWarmup`, appends to the caller-supplied shared `ConcurrentQueue<string>` unconditionally, then evaluates `.SimulateFailure` — exactly the documented ordering. `Caching/CachingServiceCollectionExtensions.cs` extended with the three registration extensions per D-164. 4 new files + 1 modified file, all `sealed`. `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, 10 pre-existing warnings only, none introduced. No consuming-domain regression check applies — no `02.Caching` `.Tests` project exercises these four fakes directly today; first behavioral proof is deferred to a future `SK.16.Tests` session (`SharedKernel.Testing.SelfTests`, T-60–T-64). `[STATUS: Planned — P-306/WO-050]` markers removed from all four fakes and the three DI extensions in the `Caching/` Interface Contracts block; the `AddFakeCachingServices()` note, the Folder/Namespace Map paragraph, and the DI Registration code-sample comment updated to reflect shipped status. `SK.16.Core` is now `●` 100/100 — phase promoted to root (testing-phase-implementer).
- [2026-07-29] `SK.16.Tests` closed for WO-050 (T-60–T-64, 64/64, promoted to root): proved all four `Caching/` fakes plus their three DI extensions in `SharedKernel.Testing.SelfTests/Caching/` — `FakeRedisChannelServiceTests.cs` (15 tests), `FakeRedisHashServiceTests.cs` (17, backed by a small source-generated `FakeRedisHashServiceTestJsonContext` since `JsonTypeInfo<T>` is accepted for signature parity only and never invoked by the fake), `FakeTypedHashStoreTests.cs` (16, incl. a dedicated independence proof against a separately-constructed `FakeRedisHashService`), `FakeCacheWarmupStrategyTests.cs` (5, incl. a three-instance-sharing-one-`ConcurrentQueue<string>` hand-rolled ordering/dispatch loop mirroring `CacheWarmupHostedService`'s sort-by-`Order`/catch-and-continue contract), `AddFakeTypedHashStoreTests.cs` (3, new file), `AddFakeCacheWarmupStrategyTests.cs` (4, new file), plus 3 tests added to the existing `AddFakeCachingServicesTests.cs`. **Found and fixed a genuine, previously-unproven defect in the shipped C-100 `AddFakeCacheWarmupStrategy()` while writing T-64's own test coverage** — see the `Caching/` Interface Contracts block's `AddFakeCacheWarmupStrategy` entry (corrected in this same pass) for the full root-cause writeup: the shipped `TryAddEnumerable`-based registration threw `ArgumentException` on the very first call, and once corrected to not throw, silently dropped every differently-named call after the first. Fixed `16.Testing/SharedKernel.Testing/Caching/CachingServiceCollectionExtensions.cs` to use plain `services.AddSingleton<ICacheWarmupStrategy>(factory)` instead of `services.TryAddEnumerable(...)` — this is a genuine Tests-phase code fix to already-shipped `SK.16.Core` production code, not a test-only change; the fix's XML doc `<remarks>` on `AddFakeCacheWarmupStrategy` explains the deviation in full, per this file's own "any `Add*` DI extension whose behavior would otherwise surprise a caller must carry an explanatory `<remarks>`" convention. `dotnet build` on both `SharedKernel.Testing.csproj`/`SharedKernel.Testing.SelfTests.csproj` succeeds 0 errors, 7/4 pre-existing warnings respectively, none introduced. Full regression `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` (real Docker daemon) passes 794/794 (733 pre-existing + 61 net new); confirmed zero leftover containers. No consuming-domain regression check applies — no `02.Caching` `.Tests` project exercises these four fakes directly today. `SK.16.Tests` is now `●` 64/64 — phase promoted to root. Only `SK.16.Docs` (DO-30/DO-31) remains to fully close WO-050/P-306's `16.Testing` contribution (testing-phase-implementer, sync-brain).
- [2026-07-29] `SK.16.Docs` closed for WO-050 (DO-30/DO-31, 31/31, promoted to root) — **this closes WO-050/P-306 in full: all six phases of `16.Testing` (Design/Scaffold/Core/Tests/Docs/Published) are `●` again.** DO-30 added an explicit `<b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b>` `<remarks>` paragraph to all four `Caching/` fakes (`FakeRedisChannelService.cs`, `FakeRedisHashService.cs`, `FakeTypedHashStore.cs`, `FakeCacheWarmupStrategy.cs`), matching the `Cryptography/` DO-27 precedent exactly — none of the four previously carried this explicit statement, only an implicit "for use in unit tests" summary line. `FakeRedisChannelService`'s paragraph additionally names the deliberately-missing reconnect/replay machinery; `FakeRedisHashService`'s additionally names the accepted-but-never-invoked `JsonTypeInfo<T>` divergence from real Redis wire behavior. `[STATUS: Planned — P-306/WO-050]` markers were verified already absent from all four fakes' Interface Contracts entries (removed during the `SK.16.Core` pass) — no marker removal needed. DO-31 verified already complete with zero code change: all three DI extensions in `Caching/CachingServiceCollectionExtensions.cs` already carry full `<summary>`/`<remarks>`/`<param>`/`<returns>` XML docs, and this file's DI Registration section already reflects shipped status for all three calls. `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, 10 pre-existing warnings only, none introduced by this pass. No consuming-domain regression check applies — no `02.Caching` `.Tests` project exercises these four fakes directly; `SharedKernel.Testing` carries no nested `.Tests` project of its own per this domain's documented exception. `SK.16.Docs` now 31/31 `●` — phase promoted to root (testing-phase-implementer).
- [2026-07-30] P-330/WO-052 processed (testing-arch-planner) — plans the `04.Contracts` WO-052/P-328 namespace-rename adoption: `Contracts/EnvelopeAssertions.cs`/`EnvelopeAssertionsTests.cs` must drop their `using EnvelopeNs = SharedKernel.Contracts.Envelope;` collision-workaround alias (carried since the original P-064/WO-012 design) once `04.Contracts` relocates `Envelope`/`Envelope<T>` from `SharedKernel.Contracts.Envelope` to `SharedKernel.Contracts.Envelopes`, in favor of a direct `using SharedKernel.Contracts.Envelopes;` and plain unqualified `Envelope`/`Envelope<T>` references — the exact reason the namespace collision existed in the first place goes away once the rename lands. Verified directly on disk (not `04.Contracts/CLAUDE.md` prose alone, which already describes the post-rename state as ratified): `04.Contracts/SharedKernel.Contracts/Envelope/Envelope.cs`/`EnvelopeT.cs` still declare `namespace SharedKernel.Contracts.Envelope;` today — P-328's own root `state-map.md` status is `◐` Dispatched, not `●` Complete. This is a NEW variant of this domain's design-ahead-of-schedule pattern (after the four prior hard-blocker occurrences at P-269/P-276/P-284/P-288, each needing a fake to `: Implement` a brand-new never-before-compiled interface): here `Envelope`/`Envelope<T>` already compile fine today under the OLD namespace via the `EnvelopeNs` alias — the blocker is narrower, this file cannot be rewritten to reference the NEW namespace until it exists, or the build breaks. Added a `PENDING NAMESPACE RENAME` note directly under `EnvelopeAssertions`'s Interface Contracts block (`Contracts/` section) recording this — the block's own extension-method signatures were already written generically as `Envelope`/`Envelope<T>` with no mention of `EnvelopeNs` (an in-code readability device only, never part of the documented public contract), so no signature correction was needed. 5 new tasks added to `16.Testing/state-map.md` (D-165, S-43, C-101, T-65, DO-32) — D-165/S-43 (Design/Scaffold) are `○` Pending and fully actionable now (Design sources the target namespace from `04.Contracts/CLAUDE.md`'s already-ratified prose per this domain's established convention; Scaffold needs no reference change since `SharedKernel.Contracts` is already referenced); C-101/T-65/DO-32 (Core/Tests/Docs) are `⚑` Blocked pending P-328 landing in code. New `04.Contracts` row added to the Cross-Domain Dependencies table; new `## Blocked` table entry; Package Board and Overall Progress table both updated.
- [2026-07-31] P-330/WO-052 closed for `16.Testing` in full (C-101/T-65/DO-32 → `●`, promoted to root; `SK.16.Core`/`SK.16.Tests`/`SK.16.Docs` all `●` again, 101/101 + 65/65 + 32/32): re-verified directly on disk that `04.Contracts` shipped P-328 (`04.Contracts/SharedKernel.Contracts/Envelopes/Envelope.cs`/`EnvelopeT.cs` now declare `namespace SharedKernel.Contracts.Envelopes;`, package published v2.0.0). **DISCOVERED ALREADY IMPLEMENTED, not written this session**: `git log` shows both `Contracts/EnvelopeAssertions.cs` (C-101's target) and `SharedKernel.Testing.SelfTests/Contracts/EnvelopeAssertionsTests.cs` (T-65's target) were already rewritten in commit `77e87c4`, same calendar day prior to this pass — the `EnvelopeNs` alias is gone from both files, replaced by a direct `using SharedKernel.Contracts.Envelopes;`, every method signature/`<see cref>` already plain unqualified `Envelope`/`Envelope<T>`. Independently verified rather than trusted: grep-swept all of `16.Testing/` for `EnvelopeNs`/`SharedKernel.Contracts.Envelope` (singular, old namespace) — zero matches; `dotnet build SharedKernel.Testing.csproj -c Release` succeeds 0 errors, 2 pre-existing `NU1903` warnings only; full regression `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` passes 794/794, zero regressions. The stale `PENDING NAMESPACE RENAME (⚑ Blocked...)` note under `EnvelopeAssertions`'s Interface Contracts block (`Contracts/` section) is replaced with a `NAMESPACE RENAME ADOPTED` note recording the shipped state — no signature change was needed (the block's own signatures were already written generically as `Envelope`/`Envelope<T>`). This closes P-330/WO-052's `16.Testing` contribution in full (testing-phase-implementer, sync-brain).
- [2026-08-03] P-335 (WO-053): design pass for `06.Persistence`'s core write/read/unit-of-work/connection-factory gap-fill. Three new `[STATUS: Planned]` types added to the existing `Persistence/` Interface Contracts block — `FakeRepository<TAggregate,TId>` (a single type deliberately implementing BOTH `IRepository<TAggregate,TId>` and `IReadRepository<TAggregate,TId>`, diverging from production's replica-routing-driven `EfRepository`/`EfReadRepository` two-class split; backed by an in-memory mirror of `06.Persistence`'s canonical specification-evaluator pipeline, including a `ListKeysetAsync<TKey>` cursor/seek-pagination algorithm), `FakeUnitOfWork` (a DELIBERATE namespace-disambiguated naming collision with the already-shipped `Application/FakeUnitOfWork`), `FakePersistenceTransaction`. The long-pending `FakeDbConnectionFactory` design (documented `[STATUS: Planned]` since P-182/WO-029, 2026-06-22, never built across roughly a dozen intervening work orders) was re-verified with zero drift and carried forward unchanged. `IRestorableRepository<TAggregate,TId>` (WO-053/P-337, a sibling `06.Persistence` phase dispatched in the SAME work order) is explicitly scope-locked out — confirmed absent from disk, never named by this phase's own acceptance criteria. Folder/Namespace Map `Persistence/` row and its rationale paragraph updated; a new Test Rules bullet added. Carried zero cross-domain blocker of any kind — the first of this domain's six "design-ahead-of-schedule"-shaped phases to find none at all, since `06.Persistence` is long-`●`-Published. Core/Tests/Docs implementation remains a future session's work (testing-arch-planner, WO-053, P-335).
- [2026-08-04] `SK.16.Tests` closed for WO-053 (T-66–T-71, 71/71) — proved `FakeDbConnectionFactory`/`FakeRepository<TAggregate,TId>`/`FakeUnitOfWork`/`FakePersistenceTransaction` via 50 new tests across 6 files in `SharedKernel.Testing.SelfTests/Persistence/` (`FakeRepositoryTestFixtures.cs`, `FakeDbConnectionFactoryTests.cs`, `FakeRepositoryTests.cs`, `FakeRepositorySpecificationPipelineTests.cs`, `FakeRepositoryPagingAndProjectionTests.cs`, `FakeRepositoryKeysetTests.cs`, `FakeUnitOfWorkTests.cs`). The write/read/keyset/UoW target shapes documented at Core-phase close (C-102–C-105) were re-confirmed with zero drift while writing tests — no further corrections needed. One deliberate deviation from the phase text's own suggested approach: the soft-delete-mix seeding uses `TestSoftDeletableOrderFaker.Generate(n)` plus explicit `.Delete(...)` calls on a known subset (mirroring `SoftDeletableAggregateRoot<TId>`'s own documented usage example) rather than a randomized Bogus `RuleFor(x => x.IsDeleted, ...)` override, since `IsDeleted` is `private set` and a random split cannot be asserted against deterministically. `Persistence/` Interface Contracts and Folder/Namespace Map sections both updated to record Tests-phase completion. `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, 2 pre-existing `NU1903` warnings only; full regression `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` (excluding Docker-gated `Containers/`) passes 844/844 (794 pre-existing + 50 net new), zero regressions. No consuming-domain regression check applies — net-new fakes with no existing consumer (`13.ServiceDefaults`'s `DatabaseTenantResolutionStrategyTests` remains the identified future adopter of `FakeDbConnectionFactory`, adoption not performed here). `SK.16.Docs` (DO-33–DO-35) is the only remaining task to close WO-053's `16.Testing` contribution (testing-phase-implementer).
- [2026-08-04] `SK.16.Core` closed for WO-053 (C-102–C-105 → `●`, 105/105, promoted to root): implemented `FakeDbConnectionFactory.cs`, `FakeRepository.cs` (`FakeRepository<TAggregate,TId>`), `FakeUnitOfWork.cs`, `FakePersistenceTransaction.cs` in `Persistence/`, against the live `06.Persistence/SharedKernel.Persistence.Abstractions` (`IDbConnectionFactory`, `IRepository<TAggregate,TId>`, `IReadRepository<TAggregate,TId>`, `IUnitOfWork`, `ITransactionalUnitOfWork`, `IPersistenceTransaction`, `IProjectionSpecification<TAggregate,TResult>`, `KeysetPage<TAggregate,TKey>`) and `03.Domain/SharedKernel.Domain` (`ISpecification<T>`, `KeysetSpecification<T,TKey>`, `IAggregateRoot<TId>`, `ISoftDeletable`) source, all re-read directly from disk. **One genuine design-doc drift found and corrected in place, not silently coded around**: the live `IReadRepository.ListKeysetAsync<TKey>` constraint is `where TKey : struct, IComparable<TKey>` (matching `KeysetSpecification<T,TKey>`'s own WO-051/C-39-corrected constraint) — this file's `Persistence/` Interface Contracts block had abbreviated it to bare `IComparable<TKey>`, dropping the `struct` half; corrected in place with an inline note. **`IRestorableRepository<TAggregate,TId>` (WO-053/P-337) status flip**: re-verified on disk per the phase's own instruction and found it has SHIPPED since the Design-phase pass's "confirmed absent" finding (D-175) — `06.Persistence/SharedKernel.Persistence.Abstractions/Repositories/IRestorableRepository.cs` now exists; per the phase's own explicit instruction, it was deliberately NOT implemented against even though it now compiles — flagged as a genuine follow-up, not silently absorbed; both the Interface Contracts NOTE and the Folder/Namespace Map narrative paragraph updated to record this. `Persistence/FakeUnitOfWork` and the already-shipped `Application/FakeUnitOfWork` (P-244/WO-040) now cross-reference each other explicitly by full namespace in their XML docs, including a small additive edit to the latter's existing doc comment (justified because the naming collision only becomes a real, doc-discoverable fact once the second same-named type exists). Hit the project's known `HotChocolate.Data`→`GreenDonut` transitive-ambiguity landmine for the first time against a BCL exception type (`System.Collections.Generic.KeyNotFoundException`, previously only seen for `Result<T>`/`Error`) — fixed via full qualification at the throw site and the XML doc `cref`. `[STATUS: Planned]` markers removed from all four types' Interface Contracts entries and the Folder/Namespace Map row; the Packages-table narrative paragraph updated from "design-only" to "Core-phase implemented." `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, 7 pre-existing warnings only (2 `NU1903`, 5 `CS8509` in `Intelligence/`/`Search/`), none introduced by these 4 new files; `SharedKernel.Testing.SelfTests.csproj` also builds clean, confirming no regression from the `Application/FakeUnitOfWork.cs` doc-only edit. No consuming-domain regression check applies — net-new fakes with no existing consumer (`13.ServiceDefaults`'s `DatabaseTenantResolutionStrategyTests` is `FakeDbConnectionFactory`'s identified future adopter, per D-166; adoption there remains that domain's own future follow-up). `SK.16.Tests` (T-66–T-71) and `SK.16.Docs` (DO-33–DO-35) remain `○` Pending, future-session work (testing-phase-implementer).
