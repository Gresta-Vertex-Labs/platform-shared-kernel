# 16.Testing — Shared Test Infrastructure Brain

## What This Domain Is

The shared test-infrastructure layer. Every `.Tests` project in this repository — and every downstream microservice's test suite — references `SharedKernel.Testing` for fakes, in-memory test doubles, and container fixtures instead of hand-rolling them per project. This domain is exempt from the normal layering direction: it may reference **any** numbered layer, because it is test-only and is never shipped inside a production artifact.

Philosophy: **Deterministic, dependency-light, conformance-first.** A fake's job is to satisfy the exact interface contract of the thing it replaces — nothing more. No fake here may introduce flakiness (real clocks, real sleeps, unseeded randomness) or silently diverge from the production implementation's documented behavior.

> **Why this package exists:** without it, every `.Tests` project across `02`–`14` independently reinvents `FakeCacheService`-shaped classes, container bootstrapping, and auth stand-ins — with subtle behavioral drift between copies. `16.Testing` is the single source of truth for "what does a fake `ICacheService` look like," so a behavioral fix only has to happen once.

---

## Packages

| Package | Role | References |
|---------|------|------------|
| `SharedKernel.Testing` | Fakes, in-memory test doubles, Testcontainers fixtures, and Bogus faker conventions consumed by every `.Tests` project | Any layer's `.Abstractions` package (and, where a planning pass has justified it, a non-`.Abstractions` package — e.g. `SharedKernel.Persistence.EfCore`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Application.Behaviors`, `SharedKernel.Integration.Webhooks` — on demand, as each capability area is added). Implemented: `SharedKernel.Caching.Abstractions`, `SharedKernel.Domain`, `SharedKernel.Primitives`, `SharedKernel.Contracts`, `SharedKernel.Security.Abstractions`, `SharedKernel.Messaging.Abstractions`, `SharedKernel.Messaging.MassTransit` (test-only), `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.EfCore`, `SharedKernel.Communication.Internal`, `SharedKernel.Application.Behaviors`, `SharedKernel.Integration.Webhooks` (design-locked, queued P-431/WO-064 — `15.Integration`'s own package, which unlike most of this table's `.Abstractions`-only entries carries both the interfaces AND their concrete pure-DTO companions in one non-split package, per that domain's own single-package-per-role design) (WO-040 — the first `16.Testing` reference to `05.Application`; local-seam fakes + a promoted MediatR pipeline test harness, both in the `Application/` folder). Also references `Microsoft.Extensions.Logging.Abstractions` directly (P-258/WO-041, `Logging/`) — a NuGet `PackageReference` to a cross-cutting BCL logging contract, not a `SharedKernel.*` `ProjectReference`; the first capability folder in this package anchored to a foundational BCL package rather than a numbered domain's own abstraction. **Design-ahead-of-Core, blocker now CLEARED (P-268/P-269/WO-043):** `SharedKernel.Storage.Abstractions` (08.Storage) — a `ProjectReference` is added in Scaffold (S-23). As of the original WO-043 design pass, `08.Storage.Abstractions.csproj` was a genuinely empty placeholder (zero `.cs` files), a harder blocker than this package's usual "design documents target shape ahead of upstream code" convention (e.g. P-226/WO-036's `ActivityRecorder`, which needed only a literal `ActivitySource` name string, never an actual type reference) — `Storage/InMemoryFileStorage`/`InMemoryBlobUriGenerator` could not compile as `: IFileStorage`/`: IBlobUriGenerator` until `08.Storage` shipped real code. **Re-verified directly on disk during this Design-phase pass (2026-07-17): the blocker has cleared.** `08.Storage`'s `SK.08.Core` phase is now `●` 30/30 — `IFileStorage` (nine members), `IBlobUriGenerator` (two members), all seven `Models/` records, and the nine-factory-method `StorageErrors` class are fully implemented and compile clean, with zero drift from the target shape documented below. Core-phase tasks C-61–C-63 are now `●` Complete (`Storage/InMemoryFileStorage.cs`, `Storage/InMemoryBlobUriGenerator.cs`, `Storage/StorageServiceCollectionExtensions.cs`) — their Tests/Docs follow-ons T-47/DO-18 remain a future session's work. Also references `AWSSDK.S3` directly (P-268/WO-043, `Containers/`) — a NuGet `PackageReference` used exclusively by `MinioContainerFixture`'s bucket-bootstrap step (mirrors the existing `TestHarnessFactory`-carries-`MassTransit`-in-`Messaging/` precedent: one file in the folder carries a heavier third-party reference, the rest of the folder stays isolated from it). **Design-ahead-of-Core, blocker now CLEARED (P-276/WO-044):** `SharedKernel.Search.Abstractions` (09.Search) — a `ProjectReference` is planned in Scaffold (S-28). At the original WO-044 dispatch pass (2026-07-19), `09.Search/SharedKernel.Search.Abstractions.csproj` was a genuinely empty placeholder (zero `.cs` files, verified directly on disk via `09.Search`'s own `state-map.md` Package Board, not merely undispatched) — the same hard-blocker shape `08.Storage/SharedKernel.Storage.Abstractions` was in when P-269/WO-043 was originally designed. **Re-verified directly on disk during this Design-phase confirmation pass (2026-07-19, same calendar day): the blocker has cleared.** `09.Search`'s `SK.09.Core` phase — and its two provider packages' — are now `●`: `ISearchIndex<TDocument>` (12 members), `ISearchIndexProvisioner` (5 members), `ISearchProviderDescriptor` (4 members + `Validate`), and every `Models`/`Errors`/`Constants` type the target shape below depends on are fully implemented and compile clean, with zero drift from the target shape documented below. `09.Search`'s own `state-map.md` Blocked section confirms the dependency direction is now the OPPOSITE of what this note originally recorded — `09.Search`'s own `SK.09.Tests` T-13–T-17/T-21–T-26 are `⚑` Blocked waiting on THIS package's `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` (P-275). `Search/InMemorySearchIndex`/`InMemorySearchIndexProvisioner`/`InMemorySearchProviderDescriptor` can now compile as `: ISearchIndex<TDocument>`/`: ISearchIndexProvisioner`/`: ISearchProviderDescriptor` — Core-phase implementation (C-66–C-69, plus T-50/T-51/DO-21/DO-22) is corrected from `⚑` Blocked back to `○` Pending in `state-map.md`; actually writing that code remains a future Core-phase implementer session, not performed in this Design-confirmation pass. Also references `Testcontainers.Elasticsearch` and the base `Testcontainers` package directly (P-275/WO-044, `Containers/`) — used exclusively by `ElasticsearchContainerFixture` and `MeilisearchContainerFixture` respectively, the fifth and sixth `Containers/` fixtures; carried NO blocker at any point, since both fixtures expose only flat scalar connection properties with zero `SharedKernel.Search.*` reference. Also references `Testcontainers.Qdrant`/`Testcontainers.Milvus` directly (P-283/WO-045, `Containers/`) — both CONFIRMED to exist on nuget.org (2026-07-21), latest stable `4.13.0`, matching this package's EXISTING `Testcontainers.*` floor exactly (no version-bump ceremony required, unlike the Elasticsearch addition); used exclusively by `QdrantContainerFixture`/`MilvusContainerFixture` (the seventh and eighth `Containers/` fixtures), which carry NO blocker for the same flat-scalar-connection-properties reason as their six siblings. **Design-ahead-of-Core, HARD-BLOCKED (P-284/WO-045) — the THIRD occurrence of this pattern (after P-269/WO-043 and P-276/WO-044):** `SharedKernel.AI.Abstractions` (10.Intelligence) — a `ProjectReference` was added in Scaffold (S-32, 2026-07-21). Re-verified directly on disk immediately before adding: `SharedKernel.AI.Abstractions.csproj` is still a genuinely empty placeholder, zero `.cs` files — even though `10.Intelligence`'s own Design phase (P-279) is fully RATIFIED and its `CLAUDE.md` documents a complete, zero-drift-verifiable 995-line interface contract, prose ratification is not compiled code. The reference builds cleanly (`dotnet build` 0 errors) but resolves to zero usable types. `Intelligence/InMemoryEmbeddingGenerator`/`InMemoryVectorCollection<TRecord>`/`InMemoryVectorCollectionProvisioner`/`InMemoryVectorProviderDescriptor`/`InMemorySemanticKernel`/`InMemoryCompletionProviderDescriptor` cannot compile as `: IEmbeddingGenerator`/`: IVectorCollection<TRecord>`/etc. until `10.Intelligence`'s own `SK.10.Core` (C-01) ships real code — Core/Tests/Docs tasks (C-73–C-79, T-54, DO-25) remain `⚑` Blocked in `state-map.md`; Design (D-128–D-138) and Scaffold (S-29–S-33) are both `●` complete. **Design-ahead-of-Core, blocker now CLEARED (P-288/WO-046 — the FOURTH such occurrence, and the fourth to resolve):** `SharedKernel.Workflows.Temporal` (17.Workflows) — a `ProjectReference` was added in Scaffold (S-34, 2026-07-23). As of the original design pass, `SharedKernel.Workflows.Temporal.csproj` was a genuinely empty placeholder (bare `TargetFramework`/`ImplicitUsings`/`Nullable`, zero references, zero `.cs` content beyond generated `obj/` files) even though `17.Workflows`'s own Design phase (P-287) was already fully ratified with a zero-drift-sourceable 519-line `CLAUDE.md`. **Re-verified directly on disk 2026-07-23: the blocker has cleared.** `17.Workflows/SharedKernel.Workflows.Temporal/` now ships 43 real `.cs` files — `IWorkflowDispatcher`, `IWorkflowHandle`/`IWorkflowHandle<TResult>`, `IWorkflowIdFactory`, `TenantScope`, `WorkflowStartOptions`, `WorkflowExecutionDescription`, `WorkflowErrors`, `WorkflowWellKnown`, `WorkflowBase` all compiled and re-read directly from source. Core-phase tasks C-80–C-84 are now `●` Complete (`Workflows/InMemoryWorkflowExecution.cs`, `InMemoryWorkflowDispatcher.cs`, `InMemoryWorkflowHandle.cs`, `InMemoryWorkflowHandle{TResult}.cs`, `WorkflowServiceCollectionExtensions.cs`) — their Tests follow-on (T-55) remains a future session's work; DO-26 is `●` (verified complete in the same pass). Unlike the three prior occurrences, this one carries no paired `Containers/` fixture — `17.Workflows`'s own brain states no Testcontainers dependency exists for that domain (`WorkflowEnvironment` replaces it), so the entire `Workflows/` folder cleared as a single unit rather than splitting into an unblocked-fixture/blocked-fake pair. **Design-ahead-of-Core, blocker now CLEARED (P-300/WO-049) — the FIFTH such occurrence, and the FIRST that started as only PARTIAL rather than whole-folder:** `SharedKernel.Cryptography` (01.Core) — a `ProjectReference` was added in Scaffold (S-37). Unlike every prior occurrence, `SharedKernel.Cryptography` was ALREADY fully `Published` (WO-034) at the original Design pass, with all seven baseline interfaces (`IOneWayHasher`, `ISymmetricEncryptionService`, `IEncryptionKeyProvider`, `IAsymmetricSignatureService`, `IAsymmetricKeyProvider`, `IHmacSigner`, `ISecureRandomGenerator`) shipped as real, zero-drift-verified `.cs` files — so `Cryptography/FakeOneWayHasher`/`FakeSecureRandomGenerator`/`FakeEncryptionKeyProvider`/`FakeAsymmetricKeyProvider`/`FakeSymmetricEncryptionService`/`FakeAsymmetricSignatureService`/`FakeHmacSigner` were fully unblocked from the start. Only `IContentHasher` (P-296) was unavailable at Design time, blocking `Cryptography/FakeContentHasher` and the composite `FakeCryptographyServiceCollectionExtensions.AddFakeCryptography()`. Also references `SharedKernel.FeatureManagement` (01.Core) — a `ProjectReference` was added in Scaffold (S-38); at Design time the existing boolean `IFeatureManager.IsEnabledAsync`/`IsEnabledAsync<TContext>` members were shipped, but `GetVariantAsync`/`GetVariantAsync<TContext>` (P-298) did not yet exist on the live `Abstractions/IFeatureManager.cs`, blocking the entire `FeatureManagement/` folder (`FakeFeatureManager`, `FakeFeatureManagementServiceCollectionExtensions.AddFakeFeatureManagement()`) since a fake must implement the FULL interface to compile. **Re-verified directly on disk during the Scaffold-phase implementation pass (2026-07-28, while confirming the S-37/S-38 `ProjectReference` claims): both blockers have cleared.** `01.Core`'s own `SK.01.P296` closed 2026-07-27 (`Hashing/IContentHasher.cs` now real and compiled) and `SK.01.P298` closed 2026-07-28 (`GetVariantAsync`/`GetVariantAsync<TContext>` now declared on the live interface) — both re-verified with zero drift from the target shape documented below. `Cryptography/`'s `FakeContentHasher`/`AddFakeCryptography()` (C-92/C-93) and the entire `FeatureManagement/` folder (C-94/C-95) were corrected from `⚑` Blocked back to `○` Pending in `state-map.md` during the Scaffold-phase pass, then **implemented in full during the following Core-phase pass (2026-07-28, C-85–C-95, 95/95)** — all 8 `Cryptography/` fakes plus `AddFakeCryptography()` and the `FeatureManagement/` fake plus `AddFakeFeatureManagement()` are now real, shipped code; proving/documenting both folders in `SharedKernel.Testing.SelfTests` remains a future Tests/Docs-phase session's work. **Twelve-phase batch dispatch (2026-08-26, WO-066/067/068/069/071/072/073/074/075/076/077/078) — all twelve `[STATUS: Planned]`, none yet on disk:** `SharedKernel.Validation` (01.Core, no `.csproj` yet), `SharedKernel.Security.Totp` (12.Security, no `.csproj` yet), `SharedKernel.Integration.Notifications.Abstractions` (15.Integration, no `.csproj` yet), `SharedKernel.Presentation.Grpc` (14.Presentation, no `.csproj` yet) plus `Grpc.Core.Api`, `SharedKernel.Scheduling` (19.Scheduling, whole domain unscaffolded), `SharedKernel.DataPrivacy` (01.Core, no `.csproj` yet), `SharedKernel.Reporting.Abstractions` (20.Reporting, whole domain unscaffolded) — each planned reference blocked until its own domain's Scaffold phase creates the `.csproj`; plus `SharedKernel.MultiTenancy` (13.ServiceDefaults) — a NARROW, NAMED REVISION to the pre-existing P-187/WO-029 scope lock (see the `ServiceDefaults/` Interface Contracts block), needed solely for `ServiceDefaults/InMemoryTenantCatalog`. No new reference needed for `Domain/MoneyFaker`/`FakeExchangeRateProvider` (03.Domain), the `Cryptography/` async/envelope/TOTP additions (01.Core), or `Persistence/`/`Application/`'s new `FakeAuditTrailWriter` types (06.Persistence/05.Application) — all four target packages are already referenced and mature; only the specific new members/types are unshipped. `Localization/CultureScope` (01.Core/SharedKernel.Localization) needs NO reference at all — pure BCL. |
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
  Caching/         — SharedKernel.Testing.Caching         — ICacheService / IDistributedLockService / ITenantCacheKeyProvider / ICacheInvalidationBus fakes (02.Caching), plus IRedisChannelService / IRedisHashService / ITypedHashStore<T> / ICacheWarmupStrategy fakes (02.Caching, P-306/WO-050); FakeTenantCacheService / AddFakeTenantCacheService() implementing ITenantCacheService (02.Caching Phase 44/P-435/WO-065)
  Domain/          — SharedKernel.Testing.Domain          — assertion helpers over 03.Domain primitives — DomainEventAssertions, BusinessRuleAssertions, SpecificationAssert, DomainVersionAssertions, SpecificationTestBuilder<T>, FakeDomainNotFoundException; MoneyFaker / FakeExchangeRateProvider implementing IExchangeRateProvider [STATUS: Planned — P-441/WO-066, 03.Domain's Money not yet on disk]
  Contracts/       — SharedKernel.Testing.Contracts       — DTO test helpers (04.Contracts) — PagedListBuilder<T>, EnvelopeAssertions, IntegrationEventFaker<TEvent>, EventEnvelopeBuilder<TEvent>, PagedListAssertions
  Security/        — SharedKernel.Testing.Security        — IUserContext / ITenantProvider fakes (12.Security), plus SecurityTestContextBuilder (fluent ClaimsPrincipal/IUserContext test-fixture builder, P-382/WO-058) and FakeUserContext's AuthenticationMethods/AuthContextClassReference/AuthTime/WasAuthenticatedWith/IsAuthenticationFresherThan surface tracking 12.Security's step-up-authentication expansion (P-375/WO-058, implemented) — plus DpopTestProofBuilder, MtlsTestCertificateBuilder/MtlsTestCertificateAuthority, and ApiKeyRotationScenarioBuilder (P-391/WO-060, implemented), BCL-only test-fixture builders for 12.Security's WO-060 hardening (DPoP ath binding, mTLS default posture, API-key rotation), taking zero ProjectReference to SharedKernel.Security.Oidc/.Mtls/.ApiKey; FakeTotpChallengeStore implementing ITotpChallengeStore [STATUS: Planned — P-453/WO-069, SharedKernel.Security.Totp not yet on disk]
  Messaging/       — SharedKernel.Testing.Messaging       — IMessageBus / IEventPublisher in-memory doubles (07.Messaging), TestHarnessFactory
  Persistence/     — SharedKernel.Testing.Persistence     — IDbConnectionFactory / IRepository<TAggregate,TId> / IReadRepository<TAggregate,TId> / IUnitOfWork / ITransactionalUnitOfWork fakes + EF Core test helpers (06.Persistence) — FakeDbConnectionFactory, FakeRepository<TAggregate,TId>, FakeUnitOfWork, FakePersistenceTransaction (all P-335/WO-053, implemented C-102–C-105/SK.16.Core), TestSharedKernelDbContext, AggregateRootFaker, EfContextExtensions, ProjectionSpecificationBuilder, BulkAggregateFaker, WithDeletedSpecification, PersistenceTestHelpers; FakeAuditTrailWriter / FakeAuditQueryService implementing IAuditTrailWriter/IAuditQueryService (the RICH 06.Persistence.Abstractions contract — see Application/ below for the DIFFERENT, smaller same-named local-seam contract) [STATUS: Planned — P-459/WO-071, 06.Persistence.Abstractions Auditing surface not yet on disk]
  Containers/      — SharedKernel.Testing.Containers      — Testcontainers IAsyncLifetime fixtures (PostgreSQL / Redis / RabbitMQ / MinIO / Meilisearch / Elasticsearch / Qdrant / Milvus)
  Communication/   — SharedKernel.Testing.Communication   — cross-cutting Communication test doubles (11.Communication) — MockServiceEndpointResolver, FakeHttpContextAccessor, HttpClientHandlerTestFactory, FakeHttpMessageHandler, ambient Activity helper, gRPC ServerCallContext stub, GraphQL test-executor factory
  ServiceDefaults/ — SharedKernel.Testing.ServiceDefaults  — tenant resolution and health check test doubles (13.ServiceDefaults) — StaticTenantProvider, FakeTenantResolutionStrategy, HealthCheckAssertionExtensions; InMemoryTenantCatalog implementing ITenantCatalog [STATUS: Planned — P-473/WO-075, SharedKernel.MultiTenancy's Catalog/ sub-surface not yet on disk]
  Fakers/          — SharedKernel.Testing.Fakers          — Bogus deterministic-seeding convention + abstract Faker<T> bases — FakerSeeding, EntityFaker<TEntity,TId>, SingleValueObjectFaker<TValueObject,TValue>
  Application/     — SharedKernel.Testing.Application     — local-seam test doubles + MediatR pipeline test harness (05.Application.Behaviors) — FakeUnitOfWork, FakeAuthorizationContext, FakeIdempotencyKeyStore, FakeIdempotencyResponseStore, ApplicationPipelineTestHarness, FakeDualApprovalStore (companion to WO-058/P-380, implemented) implementing the local IDualApprovalStore seam; FakeAuditTrailWriter implementing 05.Application.Behaviors' OWN, deliberately smaller local-seam IAuditTrailWriter (SAME NAME as, but a DIFFERENT type from, Persistence/FakeAuditTrailWriter above — mirrors the FakeUnitOfWork naming-collision precedent) [STATUS: Planned — P-459/WO-071, 05.Application.Behaviors' local Auditing seam not yet on disk]
  Logging/         — SharedKernel.Testing.Logging          — structured log capture double (Microsoft.Extensions.Logging.Abstractions, cross-cutting — not owned by any single numbered domain) — LogRecord, InMemoryLogger, InMemoryLogger<TCategoryName>, InMemoryLoggerFactory, LoggerAssertions
  Storage/         — SharedKernel.Testing.Storage          — IFileStorage / IBlobUriGenerator in-memory doubles (08.Storage) — InMemoryFileStorage, InMemoryBlobUriGenerator (implemented P-269/WO-043)
  Search/          — SharedKernel.Testing.Search            — ISearchIndex<TDocument> / ISearchIndexProvisioner / ISearchProviderDescriptor in-memory doubles (09.Search) — InMemorySearchIndex<TDocument>, InMemorySearchIndexProvisioner, InMemorySearchProviderDescriptor (implemented P-276/WO-044, Core phase C-64–C-69, 2026-07-20); gained an opt-in bulk-write throttle surface (SearchBulkWriteOptions-carrying 4-arg IndexManyAsync/DeleteManyAsync overloads, plus LastBulkWriteOptions) — implemented P-355/WO-055, Core phase C-108–C-110, 2026-08-11, once 09.Search's own SearchBulkWriteOptions/ISearchIndex<TDocument> overloads shipped in SK.09.Core; proven Tests phase T-75–T-77, 2026-08-11
  Intelligence/    — SharedKernel.Testing.Intelligence      — IEmbeddingGenerator / IVectorCollection<TRecord> / IVectorCollectionProvisioner / IVectorProviderDescriptor / ISemanticKernel / ICompletionProviderDescriptor in-memory doubles (10.Intelligence) — InMemoryEmbeddingGenerator, InMemoryVectorCollection<TRecord>, InMemoryVectorCollectionProvisioner, InMemoryVectorProviderDescriptor, InMemorySemanticKernel, InMemoryCompletionProviderDescriptor (implemented P-284/WO-045, Core phase C-73–C-79, proven Tests phase T-54, 2026-07-22)
  Workflows/       — SharedKernel.Testing.Workflows          — IWorkflowDispatcher / IWorkflowHandle / IWorkflowHandle<TResult> in-memory doubles (17.Workflows) — InMemoryWorkflowDispatcher, InMemoryWorkflowHandle, InMemoryWorkflowHandle<TResult> (implemented P-288/WO-046, Core phase C-80–C-84, 2026-07-23; no paired Containers/ fixture, since 17.Workflows needs none)
  Cryptography/    — SharedKernel.Testing.Cryptography      — IOneWayHasher / ISymmetricEncryptionService / IEncryptionKeyProvider / IAsymmetricSignatureService / IAsymmetricKeyProvider / IHmacSigner / ISecureRandomGenerator / IContentHasher fakes (01.Core/SharedKernel.Cryptography) — FakeOneWayHasher, FakeSecureRandomGenerator, FakeEncryptionKeyProvider, FakeAsymmetricKeyProvider, FakeSymmetricEncryptionService, FakeAsymmetricSignatureService, FakeHmacSigner, FakeContentHasher (P-300/WO-049 — implemented 2026-07-28, all 8 fakes + AddFakeCryptography() shipped; SK.16.Core complete, proven/documented in a future SK.16.Tests/SK.16.Docs session); FakeEncryptionKeyProvider TARGET async migration (GetCurrentKeyAsync/GetKeyAsync) + FakeEnvelopeEncryptionProvider implementing IEnvelopeEncryptionProvider [STATUS: Planned, BREAKING — P-450/WO-068, 01.Core's async IEncryptionKeyProvider contract not yet shipped]; FakeTotpReplayGuard implementing ITotpReplayGuard [STATUS: Planned — P-453/WO-069, 01.Core's TOTP/HOTP surface not yet on disk]

  Validation/      — SharedKernel.Testing.Validation        — checksum-correct valid/invalid sample generators (01.Core/SharedKernel.Validation) — ValidationSampleGenerator [STATUS: Planned — P-445/WO-067, SharedKernel.Validation not yet on disk]
  Notifications/   — SharedKernel.Testing.Notifications      — INotificationSender / INotificationDeliveryObserver in-memory doubles (15.Integration/SharedKernel.Integration.Notifications.Abstractions) — InMemoryNotificationSender, InMemoryNotificationDeliveryObserver, AddInMemoryNotificationSender()/AddInMemoryNotificationDeliveryObserver() [STATUS: Planned — P-463/WO-072, SharedKernel.Integration.Notifications.Abstractions not yet on disk]
  Scheduling/      — SharedKernel.Testing.Scheduling         — IScheduledJobRegistry in-memory double (19.Scheduling/SharedKernel.Scheduling) — InMemoryScheduledJobRegistry (manual TriggerAsync test driver, never a real timer/hosted loop) [STATUS: Planned — P-467/WO-073, SharedKernel.Scheduling not yet on disk — whole domain unscaffolded]
  Grpc/            — SharedKernel.Testing.Grpc                — server-side gRPC interceptor test harness (14.Presentation/SharedKernel.Presentation.Grpc) — TestServerCallContext (a concrete Grpc.Core.ServerCallContext subclass with settable fields, plus a fluent .Create(...) builder for inbound metadata) [STATUS: Planned — P-470/WO-074, SharedKernel.Presentation.Grpc not yet on disk]
  DataPrivacy/     — SharedKernel.Testing.DataPrivacy         — IDataSubjectRequestHandler fake + masking-assertion helpers (01.Core/SharedKernel.DataPrivacy) — RecordingDataSubjectRequestHandler, PiiMaskingAssertions (re-invokes the REAL PiiMasking.* function, never reimplements masking) [STATUS: Planned — P-475/WO-076, SharedKernel.DataPrivacy not yet on disk]
  Reporting/       — SharedKernel.Testing.Reporting           — IReportExporter<TRow> in-memory double (20.Reporting/SharedKernel.Reporting.Abstractions) — InMemoryReportExporter<TRow> (fully drains the supplied IAsyncEnumerable<TRow> for assertion — a test-double convenience, NEVER implying .Spreadsheet/.Pdf are memory-bounded, which they are verified NOT to be) [STATUS: Planned — P-481/WO-077, SharedKernel.Reporting.Abstractions not yet on disk — whole domain unscaffolded]
  Localization/    — SharedKernel.Testing.Localization        — culture-context test helper (01.Core/SharedKernel.Localization) — CultureScope (IDisposable CultureInfo.CurrentCulture/.CurrentUICulture scoping, zero dependency on SharedKernel.Localization itself — see the AUDIT FINDING note below for why no InMemoryLocalizationCatalog fake is added here) [STATUS: Planned — P-485/WO-078; CultureScope itself carries NO blocker, pure BCL]
  FeatureManagement/ — SharedKernel.Testing.FeatureManagement — IFeatureManager fake, incl. variant support (01.Core/SharedKernel.FeatureManagement) — FakeFeatureManager (P-300/WO-049 — implemented 2026-07-28, FakeFeatureManager + AddFakeFeatureManagement() shipped; SK.16.Core complete, proven/documented in a future SK.16.Tests/SK.16.Docs session)
  Integration/     — SharedKernel.Testing.Integration       — IWebhookDispatcher / IWebhookDeliveryObserver in-memory doubles (15.Integration) — InMemoryWebhookDispatcher, InMemoryWebhookDeliveryObserver, AddInMemoryWebhookDispatcher()/AddInMemoryWebhookDeliveryObserver() (P-431/WO-064, implemented 2026-08-21 — the first capability folder ever mapped to 15.Integration)
```

Each capability folder maps 1:1 to the numbered domain whose abstraction it fakes. A new capability folder is added only when a concrete consumer needs it — this map is aspirational scaffolding, not a commitment to build every row immediately (see per-type `STATUS` markers below). `Domain/` and `Fakers/` are deliberately split: `Fakers/` holds abstract `Bogus.Faker<T>` base classes (construction-time concerns); `Domain/` holds assertion/verification helpers (post-condition concerns) — both fake sibling-isolation from each other since neither references the other's types. `Application/` (added WO-040) fakes `05.Application.Behaviors`' own LOCAL seam interfaces (`IUnitOfWork`, `IAuthorizationContext`, `IIdempotencyKeyStore`/`IIdempotencyResponseStore`), never the real cross-domain interfaces those seams bridge to in production (`06.Persistence`, `12.Security`, `07.Messaging` are never referenced by anything in this folder) — see its Interface Contracts block below for the full rationale. `Logging/` (added P-258/WO-041) is the first folder anchored to a cross-cutting BCL contract (`Microsoft.Extensions.Logging.Abstractions`) rather than a numbered domain's own `.Abstractions` package — `[LoggerMessage]`-based structured logging (root `CLAUDE.md`'s WO-041 "Logging Conventions" section) is consumed by every domain and owned by none of them, so there is no single owning domain to model the folder after; it still obeys the sibling-isolation rule (never references `Caching/`, `Messaging/`, `Application/`, or any other capability folder). `Storage/` (added P-269/WO-043) is the newest folder — the first one mapped to `08.Storage` — and references `SharedKernel.Storage.Abstractions` only, never `SharedKernel.Storage.S3`/`.Obs` (the concrete provider packages) nor any sibling capability folder in this package (in particular, never `Containers/MinioContainerFixture` — the in-memory fake and the real-provider-integration fixture are deliberately independent test paths, mirroring the existing split between e.g. `Caching/FakeCacheService` and `Containers/RedisContainerFixture`). `Containers/` itself gains a fourth fixture, `MinioContainerFixture` (P-268/WO-043), which — like its three siblings — exposes only flat scalar connection properties and takes **no** dependency on `SharedKernel.Storage.Abstractions`; its bucket-bootstrap step is the one place in this entire package that references `AWSSDK.S3` (a third-party NuGet, not a `SharedKernel.*` type), scoped to that single file. `Containers/` gains a fifth and sixth fixture, `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` (P-275/WO-044) — same "flat scalar connection properties only, zero `SharedKernel.Search.*` dependency" shape as `MinioContainerFixture`, so this pair carries no build-time dependency on `09.Search`'s own code landing. `Search/` (P-276/WO-044) is the newest folder — the second one built against a package that was a genuinely empty placeholder on disk at design time (the first was `Storage/` at P-269/WO-043) — and references `SharedKernel.Search.Abstractions` only, never `SharedKernel.Search.Meilisearch`/`.ElasticSearch` (the concrete provider packages) nor any sibling capability folder in this package (in particular, never `Containers/MeilisearchContainerFixture`/`.ElasticsearchContainerFixture` — the in-memory fakes and the real-provider-integration fixtures are deliberately independent test paths, mirroring the existing `Storage/`-vs-`Containers/MinioContainerFixture` split exactly). Unlike every other folder in this package, the three `Search/` fakes are additionally independent OF EACH OTHER — see the SCOPE LOCK note in the `Search/` Interface Contracts block below for why. `Containers/` gains a seventh and eighth fixture, `QdrantContainerFixture`/`MilvusContainerFixture` (P-283/WO-045) — same "flat scalar connection properties only, zero `SharedKernel.AI.*` dependency" shape as their six siblings, so this pair carries no build-time dependency on `10.Intelligence`'s own code landing, and — unlike `MeilisearchContainerFixture` — both are built on OFFICIAL dedicated Testcontainers modules with no hand-rolled generic-builder fallback needed. `Intelligence/` (P-284/WO-045) is the newest folder — the third one built against a package that was a genuinely empty placeholder on disk at design time (after `Storage/` at P-269/WO-043 and `Search/` at P-276/WO-044) — and references `SharedKernel.AI.Abstractions` only, never `SharedKernel.AI.Qdrant`/`.Milvus`/`.SemanticKernel` (the concrete provider packages) nor any sibling capability folder in this package (in particular, never `Containers/QdrantContainerFixture`/`.MilvusContainerFixture` — the in-memory fakes and the real-provider-integration fixtures are deliberately independent test paths, mirroring the `Storage/`-vs-`Containers/MinioContainerFixture` / `Search/`-vs-`Containers/MeilisearchContainerFixture` split exactly). Like `Search/`, ALL SIX `Intelligence/` fakes are additionally independent of each other — see the SCOPE LOCK note in the `Intelligence/` Interface Contracts block below, the pattern's SECOND application, confirming it generalizes beyond `Search/`'s original three-type case. `Workflows/` (P-288/WO-046) is the newest folder — the fourth one built against a package that was a genuinely empty placeholder on disk at design time (after `Storage/` at P-269/WO-043, `Search/` at P-276/WO-044, and `Intelligence/` at P-284/WO-045) — and references `SharedKernel.Workflows.Temporal` only (the domain's single package; there is no `.Abstractions` split in `17.Workflows` to narrow against). Unlike its three predecessors, `Workflows/` gains **no paired `Containers/` fixture** — `17.Workflows`'s own brain states its testing story is `WorkflowEnvironment`/`WorkflowReplayer` (Temporal's own in-box time-skipping test server), not Testcontainers, so there is no sibling fixture to keep isolated from it. `InMemoryWorkflowDispatcher` and `InMemoryWorkflowHandle`/`InMemoryWorkflowHandle<TResult>` are additionally independent of `Containers/` and every other sibling folder — see the `Workflows/` Interface Contracts block below. `Cryptography/` and `FeatureManagement/` (both P-300/WO-049) are the newest folders — the SECOND and THIRD ever anchored to a `01.Core` package (`Clocks/`'s `IClock`/`SharedKernel.Primitives` was the first) and the FIFTH/SIXTH occasion this package's Design phase has run ahead of an upstream `01.Core` addition still shipping — but the FIRST time that "ahead of schedule" was only PARTIAL rather than whole-folder: `SharedKernel.Cryptography` is already fully `Published` (WO-034), so 7 of `Cryptography/`'s 8 fakes needed no upstream code to land at all; at the original Design pass, only `FakeContentHasher` (needed `IContentHasher`, P-296) and the composite `FakeCryptographyServiceCollectionExtensions.AddFakeCryptography()` (transitively) were blocked, while the entire `FeatureManagement/` folder (`FakeFeatureManager`, needed `IFeatureManager.GetVariantAsync`/`GetVariantAsync<TContext>`, P-298) was blocked as a whole, mirroring the earlier whole-folder pattern exactly since a missing interface member prevents even a partial implementation. **Both blockers cleared 2026-07-28** (re-verified during the Scaffold-phase S-37/S-38 pass) — `01.Core`'s `SK.01.P296`/`SK.01.P298` both shipped. **All 11 fakes/DI extensions across both folders were implemented in the same-day `SK.16.Core` pass (C-85–C-95, 2026-07-28)** — proving/documenting them in `SharedKernel.Testing.SelfTests` remains a future `SK.16.Tests`/`SK.16.Docs` session's work. `Cryptography/` references `SharedKernel.Cryptography` only, never `SharedKernel.FeatureManagement` or any sibling capability folder in this package; `FeatureManagement/` references `SharedKernel.FeatureManagement` only, never `SharedKernel.Cryptography` or any sibling folder — neither folder references `Clocks/`, `Security/`, or any other existing folder despite superficial thematic overlap (e.g., neither a fake token generator nor a fake feature flag needs a fake clock). Both folders' fakes are proven exclusively in `SharedKernel.Testing.SelfTests`, never a `01.Core`-owned `.Tests` project — see Test Rules below for why (the same rationale `FakeClock` already established). `Caching/` (P-306/WO-050 — implemented 2026-07-29) gains four more fakes for `02.Caching`'s last four un-faked abstractions — `IRedisChannelService`, `IRedisHashService`, `ITypedHashStore<T>`, `ICacheWarmupStrategy` — the first extension to an already-`**implemented**`-labeled folder in this domain's history rather than a brand-new folder; `SharedKernel.Caching.Abstractions` is already referenced, so this phase carried zero cross-domain blocker of any kind, hard or soft. `Caching/` now ships nine fakes total, proven/documented in a future `SK.16.Tests`/`SK.16.Docs` session. `Persistence/` (P-335/WO-053, Core-phase implemented 2026-08-04) is the SECOND folder in this domain's history to gain net-new fakes for an already-`**implemented**`-labeled folder (after `Caching/`'s own P-306/WO-050 extension) rather than stand up a new one — three brand-new types (`FakeRepository<TAggregate,TId>`, `FakeUnitOfWork`, `FakePersistenceTransaction`) plus the long-pending `FakeDbConnectionFactory` (documented `[STATUS: Planned]` since P-182/WO-029, 2026-06-22, finally built at C-102) join the existing EF Core test-helper set. Carried zero cross-domain blocker of any kind — the FIRST of this domain's six "design-ahead-of-schedule"-shaped phases to find none at all, since `06.Persistence` is long-`●`-Published, unlike the five prior occurrences at P-269/P-276/P-284/P-288/P-300. `FakeRepository<TAggregate,TId>` deliberately implements BOTH `IRepository<TAggregate,TId>` and `IReadRepository<TAggregate,TId>` in ONE type — a divergence from production's `EfRepository`/`EfReadRepository` two-class split (which exists to support WO-053/P-338's new read-replica routing, meaningless for an in-memory collection) — guaranteeing trivial read-after-write consistency. `06.Persistence`'s WO-053/P-337 `IRestorableRepository<TAggregate,TId>` (a sibling phase dispatched in this SAME work order) is explicitly scope-locked out of `FakeRepository<TAggregate,TId>` — confirmed absent from disk at the original Design pass, but re-verified during Core-phase implementation (2026-08-04) to have SHIPPED in the interim; deliberately still NOT implemented against, per the phase's own explicit instruction, and flagged as a genuine future follow-up rather than silently absorbed. `Persistence/` continues to reference `SharedKernel.Persistence.Abstractions` only, never `.EfCore`/`.PostgreSQL`/`.Dapper` (the concrete provider packages, already excluded from every fake in this folder), nor any sibling capability folder in this package. **Proven Tests-phase implemented (T-66–T-71, 2026-08-04)** — all four types are now covered by 50 tests in `SharedKernel.Testing.SelfTests/Persistence/`. **Docs-phase implemented (DO-33–DO-35, 2026-08-04)** — closing WO-053/P-335 end to end: DO-33/DO-35 were found already fully satisfied from the Core-phase pass on re-verification; DO-34 closed two genuine documentation gaps in `FakeRepository<TAggregate,TId>` — the shared pipeline's `TagWith` no-op step had never been named alongside its Includes/StringIncludes/AsSplitQuery/AsNoTracking siblings, and `AddAsync`/`UpdateAsync`'s throw-vs-silent-correction rationale was undocumented — both closed with new `<remarks>` in code, mirrored here. `Messaging/` (P-352/WO-054, Core-phase implemented 2026-08-04) is the THIRD folder in this domain's history to gain net-new surface for an already-`**implemented**`-labeled folder (after `Caching/`'s P-306/WO-050 and `Persistence/`'s P-335/WO-053 extensions) — `InMemoryMessageBus`/`InMemoryEventPublisher` now implement a `PublishContext`-capture mechanism plus `IMessageHeaderPropagator`-application support across all dispatch verbs (C-106/C-107), closing a pre-existing gap (neither fake had ever captured `CorrelationId`/`CausationId`/`Headers`, only discarded them) while also modeling `07.Messaging`'s still-design-only P-340/P-341/P-344 target contract. The SEVENTH design-ahead-of-schedule occurrence in this domain's history, and the first where the fake's own capture design — storing a REFERENCE to the real `PublishContext` instance rather than copying named scalar fields — converted what would otherwise be a hard compile blocker into a non-blocker for the fake itself: C-106/C-107 compiled and shipped with zero dependency on `07.Messaging`'s then-unshipped `PublishContext.TenantId`/`.PartitionKey`. The TenantId/PartitionKey-specific test proof (T-74) was `⚑` Blocked pending `07.Messaging`'s P-340/P-344 shipping those members in code — **resolved 2026-08-07** once they shipped (2026-08-05/06), re-verified directly on disk before implementing: 12 more tests added to `InMemoryMessageBusTests.cs` and 7 more to `InMemoryEventPublisherTests.cs`, all pre-existing tests untouched, closing `SK.16.Tests` and the whole `16.Testing` domain end to end. `Messaging/` continues to reference `SharedKernel.Messaging.Abstractions` (and, test-only, `.MassTransit` via `TestHarnessFactory`) only, no sibling capability folder in this package. `Search/` (P-355/WO-055) is the SECOND folder in this domain's history (after `Messaging/`'s own P-352/WO-054) to gain net-new surface for an already-`**implemented**`-labeled folder rather than stand up a new one — `InMemorySearchIndex<TDocument>` gains an opt-in bulk-write throttle surface (two new 4-arg `IndexManyAsync`/`DeleteManyAsync` overloads carrying a new `SearchBulkWriteOptions` type, plus a `LastBulkWriteOptions` audit property), narrowed down from a stale phase-input premise (per-document bulk-write outcome reporting, which already shipped in WO-044/P-276 and needed no new work — see D-180). This is the EIGHTH design-ahead-of-schedule occurrence in this domain's history and, unlike the six whole-folder occurrences (`Storage/`, original `Search/`, `Intelligence/`, `Workflows/`, `Cryptography/`+`FeatureManagement/` partial, `Persistence/` unblocked), the blocker is a MEMBER-level gap in an already-shipped folder: `09.Search/SharedKernel.Search.Abstractions/Abstractions/ISearchIndex.cs` declares only the pre-existing 3-arg overloads as of this design pass, and no `SearchBulkWriteOptions` type exists on disk yet, confirmed directly (not from `09.Search/CLAUDE.md` prose). Design/Scaffold (D-180–D-183/S-46) closed 2026-08-10 — target overload signatures sourced directly from `09.Search`'s own live Design-phase state-map rows (D-32–D-35), fully locked prose, not yet coded at that time. **Core (C-108–C-110) implemented 2026-08-11** — re-verified directly on disk that `09.Search`'s own `SK.09.Core` (C-51/C-52, P-353/P-354) shipped `SearchBulkWriteOptions` and the two new `ISearchIndex<TDocument>` overloads as real compiled types, then implemented both 4-arg overloads plus `LastBulkWriteOptions` with zero drift from the locked design. This clears the transitive inbound compilation failure `09.Search`'s own `SK.09.Tests` T-27–T-34 were blocked on (`SharedKernel.Testing.dll` previously failed to build with two `CS0535` errors since `InMemorySearchIndex<TDocument>` no longer satisfied its own `ISearchIndex<TDocument>` interface). **Tests (T-75–T-77) implemented 2026-08-11** — see the `Search/` Test Rules bullet below for the full writeup. **Docs (DO-37) implemented 2026-08-11** — XML docs on both 4-arg overloads and `LastBulkWriteOptions` confirmed present, verified rather than assumed (written at C-108/C-110 implementation time); the `[STATUS: Blocked — pending 09.Search P-354]` marker removal was likewise already completed during the Core-phase pass, re-confirmed via a grep sweep to be absent from this `Search/` Interface Contracts block. This closes WO-055/P-355's entire `16.Testing` contribution, and since every other phase was already `●`, the whole `16.Testing` domain end to end. `Security/` (P-374/WO-057) is the THIRD folder in this domain's history (after `Messaging/`'s P-352/WO-054 and `Search/`'s P-355/WO-055) to gain net-new surface for an already-`**implemented**`-labeled folder rather than stand up a new one — `FakeUserContext` gains `IdentityKind`, `Permissions`, and `HasPermission(string)` tracking `12.Security`'s WO-057 `IUserContext` expansion (P-367/P-368); `FakeTenantProvider` is unaffected. This is the NINTH design-ahead-of-schedule occurrence in this domain's history and, like `Search/`'s own P-355, the blocker is a MEMBER-level gap in an already-shipped folder: `12.Security/SharedKernel.Security.Abstractions/Abstractions/IUserContext.cs` declares only the pre-WO-057 seven members as of this design pass, confirmed directly (not from `12.Security/CLAUDE.md` prose). Design/Scaffold (D-184–D-187/S-47) closed 2026-08-13 — `SharedKernel.Security.Abstractions` is already referenced (S-04), and no separate `FakeSystemUserContext` sentinel type is designed since `FakeUserContext`'s existing mutable-property shape already covers every `IdentityKind` value directly. **Core (C-111–C-113) closed 2026-08-13, same day** — the recorded blocker had gone stale before this pass began: a concurrent `12.Security` WO-057 Core-phase session had already shipped `IUserContext.IdentityKind`/`.Permissions`/`.HasPermission` as real compiled members and, in the same pass, had already patched `Security/FakeUserContext.cs` in-place (an uncommitted working-tree change) to keep this package compiling against the expanded interface. Each member was independently re-verified against its exact acceptance shape rather than assumed satisfied because the build passed — `IdentityKind` is settable and defaults to `IdentityKind.User`; `Permissions`/`HasPermission` use the identical `StringComparer.OrdinalIgnoreCase` pattern `HasRole` already uses; every pre-existing member is byte-for-byte unchanged. Zero new `.cs` code was written by this Core-phase pass. **Tests (T-78) implemented 2026-08-13** — 14 new tests added to the EXISTING `FakeUserContextTests.cs` (all 15 pre-existing tests untouched), mirroring the pre-existing `HasRole`/`Roles` test shape exactly; full regression `dotnet test SharedKernel.Testing.SelfTests.csproj --configuration Release` (real Docker daemon, incl. `Containers/`) passes 930/930 (916 pre-existing + 14 net new), zero regressions. This closes `SK.16.Tests` (78/78) and WO-057/P-374's `SK.16.Tests` contribution; only Docs (DO-38) remains `○` Pending — the only outstanding task in the whole `16.Testing` domain. `Security/` continues to reference `SharedKernel.Security.Abstractions` only, no sibling capability folder in this package. `Security/` (P-382/WO-058) gains a fourth net-new addition to an already-`**implemented**`-labeled folder — a new fluent `SecurityTestContextBuilder` producing BOTH a `ClaimsPrincipal` (`.Build()`) and an `IUserContext` (`.BuildUserContext()`, a `FakeUserContext` instance) from one construction session, directly answering `12.Security/CLAUDE.md`'s own Test Rules prose about hand-assembled `ClaimsIdentity` construction — plus a further `FakeUserContext` extension for `12.Security`'s WO-058 step-up-authentication surface (`AuthenticationMethods`/`AuthContextClassReference`/`AuthTime`/`WasAuthenticatedWith`/`IsAuthenticationFresherThan`, P-375). This is the TENTH design-ahead-of-schedule occurrence in this domain's history and the SECOND against `Security/` specifically (after P-374/WO-057 itself) — the first time the sub-flavor has recurred against the SAME owning domain twice. Unlike every prior member-level occurrence, this one splits cleanly at the member boundary rather than blocking the whole addition: the builder's identity-basics half (`.WithUserId`/`.WithEmail`/`.WithUsername`/`.WithRoles`/`.WithPermissions`/`.WithIdentityKind`/`.WithClaim(s)`/`.Unauthenticated()`, `.Build()`, `.BuildUserContext()`) needs only members `FakeUserContext` already ships (WO-057), so it was UNBLOCKED from the start; the AMR/ACR/AuthTime fluent methods (`.WithAuthenticationMethods`/`.WithAuthContextClassReference`/`.WithAuthTime`) and `FakeUserContext`'s matching new members were `⚑` Blocked at design time, pending `12.Security`'s own P-375 — **both cleared and this folder's Core phase closed 2026-08-17**: `IUserContext.cs` now declares the full WO-058 surface, `FakeUserContext.cs` was found already extended (committed prior to this session), and `SecurityTestContextBuilder`'s three AMR/ACR/AuthTime methods are now implemented, wired into both `.Build()` and `.BuildUserContext()`. Deliberately excludes WO-058/P-376's `IsSenderConstrained`/DPoP surface and WO-058/P-377's `SharedKernel.Security.Mtls` package (neither named by this phase's own acceptance criteria) and a `.WithTenantId(...)` method (never asked for) — both explicit scope locks, not oversights. `SecurityTestContextBuilder` references `SharedKernel.Security.Abstractions` only — never `SharedKernel.Security.Oidc`/`.ApiKey`/`.Mtls` — its default claim-type literals ("sub"/"email"/"name"/"roles"/"scope"/"amr"/"acr"/"auth_time") are PRIVATE constants mirroring `12.Security.Oidc`'s `SecurityOptions.ClaimMapping` defaults, independently maintained rather than sourced via a `ProjectReference` to the concrete provider package, mirroring `FakeTenantCacheKeyProvider`'s established "independently mirrors production's key format" precedent.

`Security/` (P-391/WO-060) gains a fifth net-new addition to an already-`**implemented**`-labeled folder — three fluent, `new`-able test-fixture builders answering `security-arch-planner`'s own WO-060 hardening review of the shipped WO-058 output: `DpopTestProofBuilder` (a genuinely well-formed, real-ES256-signed RFC 9449 DPoP proof + companion access token, with `.WithMismatchedAth()`/`.WithMissingAth()`/`.WithMalformedAth(string)` negative-path seams, for P-385's `ath`-binding fix — `12.Security`'s own `DpopProofValidator` is `internal` and unreachable regardless of any `ProjectReference`, so this builder produces a presentable input fixture for a CONSUMING SERVICE's own integration test, never a fake of the validator itself), `MtlsTestCertificateBuilder`/`MtlsTestCertificateAuthority`/`MtlsTestCertificate` (ephemeral self-signed/ephemeral-CA-chained/revoked X.509 test certificates via BCL `CertificateRequest`/`CertificateRevocationListBuilder`, for P-386's corrected `AllowedCertificateTypes = Chained`/`RevocationMode = Offline` defaults), and `ApiKeyRotationScenarioBuilder`/`ApiKeyRotationScenario` (a pure old-key/new-key/extra-candidate scenario builder, for P-389's new public `ApiKeyRotationComparer.AnyMatch`). This is the TWELFTH design-ahead-of-schedule occurrence in this domain's history and the THIRD against `Security/` specifically (after P-374/WO-057 and P-382/WO-058) — but the FIRST of the twelve to carry ZERO cross-domain compile-time blocker of any kind: all three new types are deliberately built entirely on BCL `System.Security.Cryptography`/`System.Security.Cryptography.X509Certificates` primitives, taking NO `ProjectReference` to `SharedKernel.Security.Oidc`/`.Mtls`/`.ApiKey`. This PRESERVES, rather than widens, the folder's pre-existing "`SharedKernel.Security.Abstractions` only, never a concrete provider package" scope lock (first stated at P-382/WO-058's `SecurityTestContextBuilder` NOTE) via a distinct-but-converging rationale: `FakeUserContext`/`FakeTenantProvider`/`SecurityTestContextBuilder` fake the ABSTRACTIONS and must stay provider-agnostic to do so honestly; these three new types instead construct INPUT FIXTURES exercising concrete-provider-specific validation logic that has no `SharedKernel.Security.Abstractions` surface to fake against at all (DPoP/mTLS/API-key rotation are exclusively `.Oidc`/`.Mtls`/`.ApiKey` concerns). `ApiKeyRotationScenarioBuilder` never calls the real `ApiKeyRotationComparer.AnyMatch` itself — a consuming test's own `.ApiKey`-referencing project performs that call directly against the built `.Candidates` list, keeping this builder abstraction/concrete-provider-free too. Design/Scaffold (D-199–D-203/S-50/S-51) closed `●` same day as dispatch — pure design/state-verification work. **Core (C-119–C-122) closed 2026-08-17** — all three builders implemented exactly as designed, with zero cross-domain blocker; `MtlsTestCertificateBuilder`'s CRL-signing path needed one real API-surface correction discovered only via a throwaway smoke test (the ECDsa-capable `CertificateRevocationListBuilder.Build` overload, plus a Subject Key Identifier extension on the ephemeral CA — see the `MtlsTestCertificateBuilder` NOTE below for the full writeup). Tests/Docs (T-82–T-85/DO-41–DO-44) remain `○` Pending, future-session implementation work.

`Application/` (companion to P-380/WO-058, no root `P`-number of its own — flagged by `application-arch-planner`, out of that domain's own jurisdiction to build) gains a net-new type for an already-`**implemented**`-labeled folder — `FakeDualApprovalStore`, the local-seam fake for `05.Application.Behaviors`'s new `IDualApprovalStore` interface (`DualApprovalBehavior`'s dual-control/maker-checker seam, WO-058/P-380), mirroring `FakeIdempotencyKeyStore`/`FakeIdempotencyResponseStore`'s exact local-seam-fake precedent (never a cross-domain `12.Security`/`06.Persistence`/`07.Messaging` type) — the ELEVENTH design-ahead-of-schedule occurrence in this domain's history and the first where the blocker was TWO layers deep (`05.Application` itself blocked on `01.Core` shipping `Error.Forbidden(...)`). **Both layers shipped in the interim and this folder's Core phase closed 2026-08-17** — `05.Application/SharedKernel.Application.Behaviors/DualApproval/IDualApprovalStore.cs` is real, compiled code, and `FakeDualApprovalStore`/the standalone `AddFakeDualApprovalStore()` are now implemented. Deliberately excludes extending `Application/FakeAuthorizationContext` with the new sibling `IAuthorizationContextIdentity` capability (needed for `DualApprovalBehavior`'s self-approval-prevention check, but never named by this phase's own acceptance criteria and itself equally unshipped as of this pass) — flagged as a natural future follow-up, not built here. `FakeDualApprovalStore` references `SharedKernel.Application.Behaviors` only, no sibling capability folder in this package.

`Integration/` (P-431/WO-064) is the newest folder — the first ever mapped to `15.Integration`, and the THIRTEENTH design-ahead-of-schedule-shaped occurrence in this domain's history but the FIRST against `15.Integration` — and, unlike the majority of prior first-time-folder occurrences (`Storage/`, `Search/`, `Intelligence/`), carries **zero cross-domain compile-time blocker**: `15.Integration/SharedKernel.Integration.Webhooks` is long past its own Core phase, with `IWebhookDispatcher`, `IWebhookDeliveryObserver`, `WebhookSubscription`, and `WebhookDeliveryResult` all real, compiled, already-tested types, mirroring P-335/WO-053's precedent of landing against an already-mature owning domain. **The original D-204 design DID drift, however** — re-verification at implementation time (2026-08-21, not the design pass's own prior check) found `15.Integration`'s own WO-064 hardening pass (`SK.15.WO064`, P-421–P-429) had shipped in the interim, adding a third `IWebhookDispatcher` member (`SendTestDeliveryAsync`, P-429) and a second `WebhookDeliveryResult` constructor parameter (`DeliveryId`, P-423, now 6-arg not 5) that the original design text did not cover — corrected in place before implementing rather than shipped as drift; see `16.Testing/state-map.md`'s "DESIGN-PHASE DRIFT CORRECTION" note and this folder's own Interface Contracts block above for the full writeup. `InMemoryWebhookDispatcher` (`IWebhookDispatcher`) implements all three members with independent recording lists and independent configurable-result mechanisms — `DispatchAsync`/`DispatchToSubscriptionAsync` default to an honest empty fan-out / synthetic 2xx success respectively (unchanged from the original design), and the corrected-in `SendTestDeliveryAsync` records into its own `TestDeliveries` list, defaulting to the same synthetic-success shape — it deliberately performs no real subscription-store lookup, HTTP delivery, or HMAC signing, since it exists to answer "did my command handler correctly trigger a webhook dispatch" for APPLICATION-layer tests, not to re-prove `WebhookDispatcher`'s own real fan-out/routing fidelity (already covered by `15.Integration`'s own `WebhookDispatcherFanOutTests.cs`). `InMemoryWebhookDeliveryObserver` (`IWebhookDeliveryObserver`) records every `OnAttemptAsync`/`OnCompletedAsync` call and never throws — unaffected by the WO-064 drift. `Integration/` references `SharedKernel.Integration.Webhooks` only, never a sibling capability folder in this package. **Design/Scaffold/Core (D-204–D-208/S-52/C-123–C-125) implemented 2026-08-21** — `dotnet build SharedKernel.Testing.csproj -c Release` succeeds 0 errors, 9 pre-existing warnings only. **Tests (T-86) implemented 2026-08-21** — 30 new tests in `SharedKernel.Testing.SelfTests/Integration/` (`InMemoryWebhookDispatcherTests.cs`, `InMemoryWebhookDeliveryObserverTests.cs`, `IntegrationServiceCollectionExtensionsTests.cs`); full-suite regression `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` passes 997/997 non-Docker-dependent tests (26 pre-existing `Containers/` failures are Docker-daemon-unavailable in this environment, unrelated to this phase); `15.Integration/SharedKernel.Integration.Webhooks.Tests.csproj` (the consuming project transitively referencing `SharedKernel.Testing`) still builds clean with 0 errors — no `.cs` file there references either new type yet, so first behavioral proof stays in `SharedKernel.Testing.SelfTests` per this domain's "actual consumer today, not theoretical" rule, and adoption into `15.Integration`'s own suite remains a future cross-domain follow-up.

`Caching/` (P-438/WO-065) gained a sixth net-new addition to the already-`**implemented**` folder — `FakeTenantCacheService`/`AddFakeTenantCacheService()`, implementing `02.Caching`'s `ITenantCacheService` (Phase 44/P-435). At original dispatch this was the FOURTEENTH design-ahead-of-schedule occurrence in this domain's history and the first HARD (compile-time) blocker since P-431/WO-064 found none, but by the Core-phase implementation pass (same calendar day), `02.Caching` had shipped `ITenantCacheService` in the interim — re-verified directly on disk with zero drift from the design. `FakeTenantCacheService`'s implementation structurally prevents cross-tenant collisions via a composite `(TenantId, Entity, Id)` backing-store key, and additionally mirrors `02.Caching`'s own documented cross-tenant TAG-INVALIDATION-vector fix (a second internal tag-tracking structure keyed `(TenantId, Tag)`, never a bare global tag) — proving the stronger property `02.Caching`'s own Phase 44 design work calls "strictly worse than a read leak" if left unguarded. Deliberately an INDEPENDENT fake with its own backing store, continuing this folder's established "fakes share no internal state with each other" convention. **This phase's second requirement needed ZERO new code**: a fake encryption seam letting a test compose `02.Caching`'s `AddCacheEncryption()` (Phase 42/P-433, also shipped) without real AES-GCM key material is fully satisfied by the existing, shipped `Cryptography/FakeSymmetricEncryptionService` (`ISymmetricEncryptionService`, P-300/WO-049) — `AddCacheEncryption()`'s only documented prerequisite is a registered `ISymmetricEncryptionService`, regardless of implementation. This is a DOCUMENTATION-ONLY cross-reference between `Caching/` and `Cryptography/` — no code coupling: a consuming test calls `services.AddFakeCryptography()` (or the narrower `services.AddSingleton<ISymmetricEncryptionService, FakeSymmetricEncryptionService>()`) plus `services.AddLogging()` before composing `.AddCacheEncryption()` on its `ICachingBuilder` — see the Implementation Rules bullet below for the worked recipe. The interop-proof test (T-88) is now shipped, proving this composition round-trips a value through the real `AddCacheEncryption()` extension.

**Twelve-phase batch dispatch (2026-08-26, WO-066/067/068/069/071/072/073/074/075/076/077/078)** — the final domain processed in a thirteen-domain, one-session cross-domain dispatch run: every contract faked below was designed by its OWNING domain's own planner THIS SAME SESSION, so every target shape quoted in the Interface Contracts blocks below is sourced verbatim from that domain's freshly-ratified `CLAUDE.md` prose, never guessed — but, in every case but one, not yet realized as compiled code anywhere on disk (independently re-verified via direct `Glob` sweeps before any `[STATUS]` marker below was written, never trusted from a "design-locked"/"`○` Pending" self-report alone). Six brand-new capability folders are added: `Validation/` (P-445/WO-067, `01.Core`), `Notifications/` (P-463/WO-072, `15.Integration`), `Scheduling/` (P-467/WO-073, `19.Scheduling`), `Grpc/` (P-470/WO-074, `14.Presentation`), `DataPrivacy/` (P-475/WO-076, `01.Core`), and `Localization/` (P-485/WO-078, `01.Core`). Six existing folders are extended: `Domain/` gains `MoneyFaker`/`FakeExchangeRateProvider` (P-441/WO-066); `Cryptography/` gains the async `FakeEncryptionKeyProvider` migration + `FakeEnvelopeEncryptionProvider` (P-450/WO-068, a BREAKING change to this fake's own public surface, cascading from `01.Core`'s own breaking async migration) plus `FakeTotpReplayGuard` (P-453/WO-069); `Security/` gains `FakeTotpChallengeStore` (P-453/WO-069); `Persistence/` and `Application/` each gain their OWN, differently-scoped `FakeAuditTrailWriter` (P-459/WO-071) — SAME NAME, DIFFERENT type, targeting the two genuinely distinct `IAuditTrailWriter` contracts `06.Persistence.Abstractions` and `05.Application.Behaviors` independently declare, cross-referenced by full namespace in both types' planned XML docs, mirroring the already-shipped `FakeUnitOfWork`/`FakeUnitOfWork` naming-collision precedent exactly; `ServiceDefaults/` gains `InMemoryTenantCatalog` implementing `ITenantCatalog` (P-473/WO-075). Every new type in `Persistence/FakeAuditTrailWriter` honors the sibling-capability-folder-isolation rule explicitly: its hash-chain implementation is a deliberately non-cryptographic, self-contained deterministic hash (a plain `HashCode.Combine`-derived hex string) rather than a dependency on the already-shipped `Cryptography/FakeContentHasher` — two capability folders within this package must never reference each other, even when one already ships exactly the algorithm the other could reuse. Two blocker shapes emerged across the twelve phases: **whole-package-not-yet-scaffolded** (`SharedKernel.Validation`, `SharedKernel.Security.Totp`, `SharedKernel.Integration.Notifications.Abstractions`, `SharedKernel.Presentation.Grpc`, `SharedKernel.Scheduling`, `SharedKernel.Reporting.Abstractions`, `SharedKernel.DataPrivacy` — no `.csproj` exists at all, a harder blocker than this domain's historical "empty placeholder csproj already exists" pattern, so even the Scaffold `ProjectReference` task is blocked, not just Core/Tests/Docs) and **member-level-on-an-already-referenced-package** (`SharedKernel.Domain`, `SharedKernel.Cryptography`, `SharedKernel.Persistence.Abstractions`, `SharedKernel.Application.Behaviors`, `SharedKernel.MultiTenancy` — Scaffold needs no new reference, only Core/Tests/Docs wait on the owning domain's own Core phase, mirroring the established P-374/P-382/P-438 pattern). The ONE exception among all twelve: **P-485's `Localization/CultureScope` carries ZERO blocker of any kind** — an AUDIT FINDING (not a fresh-fake design) established that `01.Core/SharedKernel.Localization`'s own planned default catalog, `InMemoryLocalizationCatalog`, already IS the seedable, no-resx-files test double this phase's first requirement describes, so this package does not duplicate a second, colliding-named type; the genuinely net-new deliverable, `CultureScope`, is pure BCL `System.Globalization.CultureInfo` scoping with zero dependency on `SharedKernel.Localization` at all, mirroring P-391/WO-060's zero-blocker precedent — the first fully-unblocked-from-day-one occurrence among this batch's twelve phases. See `16.Testing/state-map.md`'s `## Blocked` section and Cross-Domain Dependencies table for the full task-by-task and per-domain blocker citations.

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

FakeTenantCacheService  (sealed class, implements ITenantCacheService)
    .GetAsync<T>(string tenantId, string entity, string id, CancellationToken ct)            → ValueTask<T?>
    .SetAsync<T>(string tenantId, string entity, string id, T value, CachePolicy policy,
                 CancellationToken ct)                                                        → ValueTask
    .GetOrSetAsync<T>(string tenantId, string entity, string id,
                       Func<CancellationToken, ValueTask<T>> factory, CachePolicy policy,
                       CancellationToken ct)                                                  → ValueTask<T>
    .RemoveAsync(string tenantId, string entity, string id, CancellationToken ct)             → ValueTask
    .RemoveByTagAsync(string tenantId, string tag, CancellationToken ct)                      → ValueTask
    .Count                                                                                    → int
    .Reset()                                                                                  → void
    NOTE: Target shape sourced from 02.Caching's real, shipped ITenantCacheService.cs — read in full
          at implementation time and confirmed ZERO drift from the Design-phase draft (5 members,
          each with a mandatory non-defaulted tenantId first parameter, (entity, id) never a
          pre-built key). Backing store is
          ConcurrentDictionary<(string TenantId, string Entity, string Id), object?> — the composite
          tuple key structurally guarantees two different tenantId values sharing the identical
          (entity, id) pair can NEVER collide, by construction rather than a runtime check. A second
          internal structure, ConcurrentDictionary<(string TenantId, string Tag),
          ConcurrentDictionary<(string Entity, string Id), byte>>, mirrors 02.Caching's own
          documented cross-tenant INVALIDATION-vector fix — SetAsync populates it whenever
          CachePolicy.Tags is non-null/non-empty, and RemoveByTagAsync consults it to remove ONLY
          the calling tenant's tagged entries, so two tenants sharing one raw tag name (e.g. both
          calling .WithTags("orders")) can never cross-invalidate each other's entries. A third
          reverse-map, _keyTags (per-key current tag set), lets a re-SetAsync with a changed/empty
          tag set correctly retire the key's PRIOR tag associations — a case the original design
          prose did not spell out but the cross-tenant-isolation acceptance criterion requires for
          correctness. Deliberately an INDEPENDENT fake with its own backing store — no internal
          composition of Caching/FakeCacheService or Caching/FakeTenantCacheKeyProvider, continuing
          this folder's established "fakes share no internal state with each other" convention.
          GetOrSetAsync invokes factory at most once per (tenantId, entity, id) on a miss, never on
          a hit.

AddFakeTenantCacheService(this IServiceCollection)  → IServiceCollection
    NOTE: Registers FakeTenantCacheService as a singleton ITenantCacheService. A STANDALONE call —
          NOT bundled into AddFakeCachingServices() — mirroring AddFakeTypedHashStore<T>()/
          AddFakeCacheWarmupStrategy()'s existing standalone-registration precedent, and mirroring
          the real AddTenantCacheService(this ICachingBuilder)'s own documented "additive
          to AddTenantCacheKeyProvider(), never replacing it" relationship to the base caching
          registration.
```

> **AUDIT FINDING (D-211, P-438/WO-065) — the "fake encryption seam" this same work order also asked for needed NO new type in `Caching/` or anywhere else in this package.** `02.Caching`'s `AddCacheEncryption(this ICachingBuilder)` (`SharedKernel.Caching.FusionCache`, Phase 42/P-433, shipped) requires only that an `ISymmetricEncryptionService` already be registered — its documented prerequisite check throws `InvalidOperationException` naming `AddSharedKernelCryptography()` as the missing dependency ONLY when nothing is registered, with no further requirement on WHICH implementation satisfies it. `Cryptography/FakeSymmetricEncryptionService` (`ISymmetricEncryptionService`, already shipped — P-300/WO-049, C-89/SK.16.Core) already IS a deterministic, zero-real-AES-GCM-key-material stand-in. A consuming test satisfies this requirement by registering `services.AddFakeCryptography()` (or the narrower `services.AddSingleton<ISymmetricEncryptionService, FakeSymmetricEncryptionService>()`) BEFORE composing its `ICachingBuilder` with `.AddCacheEncryption()`, plus `services.AddLogging()` — required transitively by `FusionCacheService`'s constructor, discovered at implementation/test time, not called out in the original design text:
>
> ```csharp
> // Fake encryption seam for 02.Caching's AddCacheEncryption() — needs ZERO new
> // SharedKernel.Testing code. Cryptography/FakeSymmetricEncryptionService already
> // satisfies AddCacheEncryption()'s sole prerequisite (a registered ISymmetricEncryptionService).
> services.AddFakeCryptography();                 // Cryptography/ — already shipped, P-300/WO-049
> services.AddLogging();                           // required transitively by FusionCacheService's ctor
> cachingBuilder.AddCacheEncryption();             // resolves the fake ISymmetricEncryptionService
>                                                   // above — zero real AES-GCM key material needed
> ```
>
> This is a DOCUMENTATION-ONLY cross-reference between two independent, already-isolated capability
> folders — `Caching/FakeTenantCacheService` (above) takes no dependency on `Cryptography/`, and
> `Cryptography/FakeSymmetricEncryptionService` takes no dependency on `Caching/`; a CONSUMING TEST
> composes both independently, exactly as it would compose two independent real production packages.
> The interop-proof test (`SharedKernel.Testing.SelfTests`' `Caching/CacheEncryptionFakeCryptographyInteropTests.cs`,
> T-88) proves this composition round-trips a value correctly through the real, shipped
> `AddCacheEncryption()` extension, plus a negative-path proof that omitting `ISymmetricEncryptionService`
> throws `InvalidOperationException`.

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

**[STATUS: Planned — P-441/WO-066]** target shape for `Money`/`IExchangeRateProvider`, sourced from `03.Domain/CLAUDE.md`'s ratified "Money system" section — `03.Domain` has not shipped `Money`/`Currency`/`RoundingPolicy`/`IExchangeRateProvider` yet:

```text
MoneyFaker  (sealed class)
    .Generate(Currency? currency = null, decimal? amount = null)   → Money
    .GenerateMany(int count)                                        → IReadOnlyList<Money>
    NOTE: Money has no public parameterless constructor — created only via Money.Create(...)/
          Money.Zero(...). Default currency pool spans at least one 2-digit (USD), one zero-decimal
          (JPY), and one three-decimal (BHD) currency. Deterministic via Bogus.Faker's fixed-seed
          convention (FakerSeeding).

FakeExchangeRateProvider  (sealed class, implements IExchangeRateProvider)
    .SeedRate(Currency source, Currency target, decimal rate)      → void
    GetExchangeRateAsync(Currency source, Currency target, ct)     → Task<Result<decimal>>
    .SimulateFailure                                                → bool
    NOTE: Returns Result<decimal>.Failure for an unseeded pair — never throws, matching the port's
          own Result<decimal>-returning contract.
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
    .IdentityKind                                               → IdentityKind              (settable; default: IdentityKind.User)
    .Permissions                                                → IReadOnlyCollection<string> (settable; default: empty)
    .HasPermission(string permission)                           → bool  (case-insensitive Permissions.Contains, mirrors HasRole exactly)
    .AuthenticationMethods                                      → IReadOnlyCollection<string> (settable; default: empty)
    .AuthContextClassReference                                  → string?                   (settable; default: null)
    .AuthTime                                                    → DateTimeOffset?           (settable; default: null)
    .WasAuthenticatedWith(string method)                        → bool  (case-insensitive AuthenticationMethods.Contains, mirrors HasRole/HasPermission exactly)
    .IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) → bool
        Returns AuthTime.HasValue && (now - AuthTime.Value) <= maxAge — a PURE function, mirroring the real
        IUserContext.IsAuthenticationFresherThan contract verbatim: NEVER calls DateTimeOffset.UtcNow
        internally. A test proving both the fresh-accept and stale-reject branches must pass `now` explicitly.
    NOTE: Distinct from 12.Security's AnonymousUserContext, which is an immutable production
          fallback sentinel (always IsAuthenticated == false). FakeUserContext defaults to an
          authenticated user so most test setups need zero configuration; call the mutators to
          exercise unauthenticated or role-restricted paths explicitly.
    NOTE: FakeUserContext represents EVERY IdentityKind value — including ServicePrincipal and
          System — through the one settable IdentityKind property above, not a dedicated sentinel
          type. Unlike production's immutable SystemUserContext singleton (12.Security), this fake
          is already a directly-constructible, fully-mutable test fixture, so a separate
          FakeSystemUserContext would only duplicate UserId/Roles/Claims/IsAuthenticated for no
          behavioral gain. A test exercising a "system/background execution context" path
          constructs `new FakeUserContext { IdentityKind = IdentityKind.System }` (optionally with
          `UserId = Guid.Empty`, mirroring SystemUserContext's own invariant) rather than reaching
          for a second fake type. This is a deliberate design decision (WO-057, P-374), not an
          oversight.

FakeTenantProvider  (sealed class, implements ITenantProvider)
    .TenantId                                                  → Guid  (settable)
    constructor(Guid? tenantId = null)                         — defaults to a fixed non-empty test Guid, NOT Guid.Empty
    NOTE: Defaulting to a real tenant id (rather than Guid.Empty) means tenant-scoped code under
          test exercises the tenanted path by default. Set TenantId = Guid.Empty explicitly to
          test the no-tenant-resolved path.

SecurityTestContextBuilder  (sealed class — fluent, new-able, NOT DI-registered)                                              [P-382/WO-058]
    .WithUserId(Guid)                                           → SecurityTestContextBuilder (fluent)
    .WithEmail(string?)                                         → SecurityTestContextBuilder (fluent)
    .WithUsername(string?)                                      → SecurityTestContextBuilder (fluent)
    .WithRoles(params string[])                                 → SecurityTestContextBuilder (fluent)
    .WithPermissions(params string[])                           → SecurityTestContextBuilder (fluent)
    .WithIdentityKind(IdentityKind)                             → SecurityTestContextBuilder (fluent)
    .WithClaim(string type, string value)                       → SecurityTestContextBuilder (fluent; additive — appends, never replaces)
    .WithClaims(IReadOnlyDictionary<string,string>)              → SecurityTestContextBuilder (fluent; bulk additive)
    .Unauthenticated()                                          → SecurityTestContextBuilder (fluent; the fluent equivalent of
                                                                    12.Security's own "omit authenticationType" ClaimsIdentity pattern)
    .WithAuthenticationMethods(params string[])                 → SecurityTestContextBuilder (fluent)
    .WithAuthContextClassReference(string?)                     → SecurityTestContextBuilder (fluent)
    .WithAuthTime(DateTimeOffset?)                               → SecurityTestContextBuilder (fluent)
    .Build()                                                     → ClaimsPrincipal
        Assembles a List<Claim> using short-name claim-type literals mirroring 12.Security.Oidc's
        SecurityOptions.ClaimMapping defaults ("sub"/"email"/"name"/"roles"/"scope"/"amr"/"acr"/"auth_time")
        as PRIVATE constants local to this builder — never sourced via a reference to
        SharedKernel.Security.Oidc (this folder's abstraction-only rule). Roles emit ONE Claim per role
        (never a JSON-array-valued single claim). Permissions emit ONE space-delimited "scope" claim.
        AuthenticationMethods (once unblocked) emit ONE Claim per method — discrete, matching the realistic
        default shape 12.Security's own WO-057 claim-shape-realism precedent established for roles.
        AuthTime (once unblocked) emits as the OIDC NumericDate (Unix-seconds) string via
        DateTimeOffset.ToUnixTimeSeconds(). Every WithClaim/WithClaims value is appended verbatim after the
        standard claims. Wraps into `new ClaimsIdentity(claims, authenticationType: IsAuthenticated ?
        "Bearer" : null)` then `new ClaimsPrincipal(identity)` — passing a non-null authenticationType is
        exactly what makes ClaimsPrincipal.Identity.IsAuthenticated return true, per 12.Security/CLAUDE.md's
        own documented "Test construction pattern" prose; Unauthenticated() state passes null instead,
        reproducing the "omit authenticationType" pattern this class exists to eliminate at every call site.
    .BuildUserContext()                                          → IUserContext
        Returns a `new FakeUserContext { ... }` populated by DIRECT property-to-property projection of the
        SAME fluent state .Build() reads — NEVER by constructing a ClaimsPrincipal first and parsing it back
        through OidcUserContext's claim-mapping logic. .Build() and .BuildUserContext() are two INDEPENDENT,
        PARALLEL projections of one shared fluent state, mirroring how production's OidcUserContext (parses
        a ClaimsPrincipal) and this package's own FakeUserContext (direct property assignment) are already
        two independent, deliberately-non-derived mechanisms for satisfying IUserContext.
    NOTE: References SharedKernel.Security.Abstractions only — never SharedKernel.Security.Oidc/.ApiKey/
          .Mtls (the concrete provider packages), mirroring this folder's established abstraction-only rule.
          SCOPE LOCK: no .WithTenantId(...) method exists — never asked for by this phase's own acceptance
          criteria, and adding one would silently expand scope beyond it (05.Application never expected).
          SCOPE LOCK: no fluent support for WO-058/P-376's IsSenderConstrained/DPoP surface or WO-058/P-377's
          SharedKernel.Security.Mtls package — neither was named by this phase's own acceptance criteria and
          neither is even fully design-locked in 12.Security yet; adding fluent support ahead of a locked
          target shape risks baking in a guessed contract.

DpopTestProofBuilder  (sealed class — fluent, new-able, NOT DI-registered)                                                     [P-391/WO-060]
    .WithHttpMethod(string htm = "POST")                        → DpopTestProofBuilder (fluent)
    .WithHttpUri(string htu)                                     → DpopTestProofBuilder (fluent)
    .WithIssuedAt(DateTimeOffset iat)                            → DpopTestProofBuilder (fluent; default a FIXED
                                                                    non-real baseline instant, never DateTimeOffset.UtcNow)
    .WithJti(string jti)                                         → DpopTestProofBuilder (fluent; default a deterministic
                                                                    per-instance incrementing sequence, never Guid.NewGuid())
    .WithAccessToken(string accessToken)                         → DpopTestProofBuilder (fluent; default a fixed
                                                                    deterministic test-token string)
    .WithMismatchedAth()                                         → DpopTestProofBuilder (fluent; negative-path — emits an
                                                                    ath claim provably NOT equal to
                                                                    base64url(SHA-256(AccessToken)))
    .WithMissingAth()                                            → DpopTestProofBuilder (fluent; negative-path — omits the
                                                                    ath claim from the payload entirely)
    .WithMalformedAth(string rawValue)                           → DpopTestProofBuilder (fluent; negative-path — sets ath to
                                                                    a caller-supplied non-base64url string)
    .Build()                                                     → DpopTestProof
        Constructs a GENUINELY well-formed RFC 9449 DPoP proof JWT — real ES256 signature over a freshly
        generated BCL ECDsa P-256 key pair, hand-rolled base64url header.payload.signature (zero third-party
        JWT library dependency). Only the negative-path methods above ever corrupt the ath claim; every other
        binding (htm/htu/iat/jkt/jti) is always correctly formed, so a consuming integration test proves
        GENUINE rejection logic against 12.Security's real (internal, unreachable from this package regardless
        of any ProjectReference) DpopProofValidator, never a strawman.
    NOTE: References ZERO SharedKernel.Security.Oidc types — DpopProofValidator is internal to that assembly
          and cannot be invoked directly from this package under any circumstance, so this builder's entire
          job is producing a presentable INPUT FIXTURE (proof JWT + companion access token) for a consuming
          service's own integration test to exercise its real, wired-up DPoP validation pipeline against —
          never a fake OF the validator. Built entirely on BCL System.Security.Cryptography/
          System.Text.Json — no new PackageReference.

DpopTestProof  (sealed record — return type of DpopTestProofBuilder.Build())                                                   [P-391/WO-060]
    .ProofJwt                                                    → string   (the full DPoP proof, "header.payload.signature")
    .AccessToken                                                 → string   (the companion bearer access-token string the
                                                                    proof is bound to)
    .PublicJwk                                                   → string   (the JWK JSON embedded in the proof header — lets
                                                                    a consuming test independently verify jkt binding)

MtlsTestCertificateBuilder  (sealed class — fluent, new-able, NOT DI-registered)                                               [P-391/WO-060]
    .WithSubjectName(string subjectName = "CN=mtls-test-client") → MtlsTestCertificateBuilder (fluent)
    .WithValidityPeriod(DateTimeOffset notBefore, DateTimeOffset notAfter) → MtlsTestCertificateBuilder (fluent; default a
                                                                    FIXED non-real window, never real UtcNow)
    .AsSelfSigned()                                               → MtlsTestCertificateBuilder (fluent; DEFAULT mode — the
                                                                    shape the corrected AllowedCertificateTypes = Chained
                                                                    default MUST reject, T-32's negative-path target)
    .AsChainedFromEphemeralCa()                                   → MtlsTestCertificateBuilder (fluent; issues a leaf
                                                                    certificate from a freshly generated in-memory root CA —
                                                                    the shape the explicit opt-in path MUST still accept,
                                                                    T-33's positive-path parity target)
    .AsRevoked()                                                  → MtlsTestCertificateBuilder (fluent; chains from an
                                                                    ephemeral CA by necessity and additionally records the
                                                                    issued certificate's serial number into that CA's
                                                                    revocation list)
    .Build()                                                      → MtlsTestCertificate
    NOTE: Built entirely on BCL System.Security.Cryptography.X509Certificates (CertificateRequest,
          X509Certificate2, CertificateRevocationListBuilder) — zero SharedKernel.Security.Mtls reference;
          IMtlsCertificateValidator/MtlsAuthenticationOptions are CONSUMED by a test using this builder's
          output, never faked BY this builder. DOCUMENTED SIMPLIFICATION: cannot fabricate a live,
          network-reachable CRL Distribution Point a real X509Chain.Build() would fetch from automatically —
          a consuming test must feed .RevocationList into its own X509Chain.ChainPolicy (ExtraStore/a local
          CRL cache seed) manually; RevocationMode.Online/automatic-Offline-cache-population fetch behavior
          is NOT reproduced. CertificateRevocationListBuilder's availability at the pinned net10.0 TFM
          (introduced in the BCL at .NET 9) was RE-CONFIRMED at Core-phase implementation time (2026-08-17,
          via reflection against the installed SDK) — but its real overload set diverges from a
          single-call RSA-or-ECDsa-agnostic shape: the RSA-only overload
          (X509Certificate2, BigInteger, DateTimeOffset, HashAlgorithmName, RSASignaturePadding?,
          DateTimeOffset?) cannot sign an ECDsa-keyed CA, so the ephemeral CA's revocation list is built via
          the second, ECDsa-capable overload — Build(X500DistinguishedName, X509SignatureGenerator,
          BigInteger, DateTimeOffset, HashAlgorithmName, X509AuthorityKeyIdentifierExtension,
          DateTimeOffset?) — using X509SignatureGenerator.CreateForECDsa(...) and
          X509AuthorityKeyIdentifierExtension.CreateFromCertificate(...). The latter throws
          CryptographicException unless the issuing CA certificate itself carries a Subject Key Identifier
          extension, so MtlsTestCertificateAuthority.CreateEphemeral adds an X509SubjectKeyIdentifierExtension
          to the CA's own CertificateRequest before self-signing — a genuine runtime defect surfaced only by
          a real, end-to-end smoke test (self-signed / chained-validates-via-X509Chain / revoked-CRL-parses),
          not by a clean compile.

MtlsTestCertificateAuthority  (internal to MtlsTestCertificateBuilder's own construction — the ephemeral root
                               CA .AsChainedFromEphemeralCa()/.AsRevoked() generate and issue against)         [P-391/WO-060]

MtlsTestCertificate  (sealed record — return type of MtlsTestCertificateBuilder.Build())                                       [P-391/WO-060]
    .Certificate                                                  → X509Certificate2   (private key included, for client-cert
                                                                     presentation scenarios)
    .IssuingCertificate                                           → X509Certificate2?  (null for self-signed; the ephemeral
                                                                     CA cert for .AsChainedFromEphemeralCa()/.AsRevoked())
    .RevocationList                                                → byte[]?  (DER-encoded CRL bytes built via
                                                                     CertificateRevocationListBuilder, populated only when
                                                                     .AsRevoked() was used, listing the returned
                                                                     certificate's serial number; null otherwise)
    .IsSelfSigned                                                  → bool

ApiKeyRotationScenarioBuilder  (sealed class — fluent, new-able, NOT DI-registered)                                            [P-391/WO-060]
    .WithOldKey(string? key = null)                               → ApiKeyRotationScenarioBuilder (fluent; default a
                                                                     deterministic per-instance incrementing test-key
                                                                     string, never Guid.NewGuid()/real randomness)
    .WithNewKey(string? key = null)                               → ApiKeyRotationScenarioBuilder (fluent; same
                                                                     determinism convention)
    .WithExtraCandidate(string key)                                → ApiKeyRotationScenarioBuilder (fluent, additive —
                                                                     supports more-than-two-simultaneously-valid-key
                                                                     scenarios)
    .Build()                                                       → ApiKeyRotationScenario
    NOTE: A PURE scenario/data builder — NEVER calls the real SharedKernel.Security.ApiKey.ApiKeyRotationComparer
          .AnyMatch itself, so this type takes ZERO ProjectReference to SharedKernel.Security.ApiKey. A
          consuming test's own .ApiKey-referencing test project calls AnyMatch(scenario.NewKey,
          scenario.Candidates) (or similar) directly against this builder's output.

ApiKeyRotationScenario  (sealed record — return type of ApiKeyRotationScenarioBuilder.Build())                                 [P-391/WO-060]
    .OldKey                                                        → string
    .NewKey                                                        → string
    .Candidates                                                     → IReadOnlyList<string>  ([OldKey, NewKey,
                                                                      ...ExtraCandidates] in call order — the exact shape
                                                                      ApiKeyRotationComparer.AnyMatch's second parameter
                                                                      expects)
    .NeverValidKey                                                  → string  (a deterministic key guaranteed absent from
                                                                      .Candidates, for the negative "presented key matches
                                                                      nothing" case)

SCOPE LOCK (P-391/WO-060): all three new types above take ZERO ProjectReference to SharedKernel.Security.Oidc/
    .Mtls/.ApiKey — preserving, not widening, this folder's pre-existing "SharedKernel.Security.Abstractions
    only" scope lock via a distinct-but-converging rationale (see the Folder/Namespace Map narrative
    paragraph above for the full writeup: these three types construct INPUT FIXTURES exercising
    concrete-provider-specific validation logic with no SharedKernel.Security.Abstractions surface to fake
    against, rather than faking the abstractions themselves).
```

**[STATUS: Planned — P-453/WO-069]** target shape for `ITotpChallengeStore`, sourced from `12.Security/CLAUDE.md`'s ratified `SharedKernel.Security.Totp` section — that package has no `.csproj` on disk yet:

```text
FakeTotpChallengeStore  (sealed class, implements ITotpChallengeStore)
    ctor(IClock? clock = null)
    NOTE: Records the last successful challenge timestamp per identityKey string (the same shared
          Guid-to-string formatting helper TotpChallengeService/TotpStepUpClaimsTransformation use),
          letting a test seed a fresh-vs-stale challenge state deterministically. Composable with
          Clocks/FakeClock, same pattern as Cryptography/FakeTotpReplayGuard below.
```

### `Messaging/` — in-process bus/publisher doubles (07.Messaging)

```text
InMemoryMessageBus  (sealed class, implements IMessageBus)
    constructor(IEnumerable<IMessageHeaderPropagator>? propagators = null)
        Materialized once via `propagators?.ToArray() ?? []` (not stored as a raw IEnumerable) — avoids
        re-enumerating a possibly-lazy/side-effecting sequence on every dispatch call. Behaviorally
        equivalent to the design's literal `?? []` normalization for the common DI-resolved case. The
        existing parameterless `new InMemoryMessageBus()` construction pattern remains valid — this is
        an additive optional parameter, not a breaking change.
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
    .ShouldHavePublishedContext<T>()                            → PublishContext  (throws if none found)
    .ShouldHaveSentContext<T>()                                 → PublishContext  (throws if none found)
    .ShouldHaveRequestedContext<TRequest,TResponse>()           → PublishContext  (throws if none found)
    NOTE: Records every PublishAsync/SendAsync call (message type + instance) into a thread-safe
          list even when no assertion is ever made. Assertion helpers are read-only queries over
          that list — they never mutate state, and return the matched message for further assertion
          chaining. Root CLAUDE.md WO-022 commitment; carried forward unchanged by P-183/WO-029
          (the canonical spec — supersedes the earlier, less complete P-011/P-124 drafts).
    NOTE (P-352/WO-054, IMPLEMENTED): PublishAsync/SendAsync/RequestAsync each build a fresh
          PublishContext, run every registered IMessageHeaderPropagator's Propagate(context) in
          enumeration order, THEN invoke the caller-supplied configure callback where one exists —
          explicit-callback-wins on any key/value it also sets. This precedence is copied verbatim from
          the real, already-shipped MassTransitMessageBus.BuildContextFromPropagators helper
          (07.Messaging.MassTransit; confirmed its `MessagingPublishContext` alias IS
          SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext, not a distinct type).
          SendAsync/RequestAsync have no configure parameter on the real interface at all — propagators
          still run for them (an internally-constructed, uncustomizable-by-the-caller context), matching
          P-341's target contract that propagator application is symmetric across all three dispatch
          verbs. RequestAsync records its context (and enqueues it into the request-context queue) BEFORE
          checking whether a response handler is registered — so ShouldHaveRequestedContext can prove a
          request was attempted even when RequestAsync itself is about to throw InvalidOperationException
          for a missing handler; this ordering was an implementation-time decision, not explicit in the
          original design text. ShouldHave*Context accessors return a REFERENCE to the actual captured
          PublishContext instance — never a copy of named scalar fields — so PublishContext.TenantId/
          .PartitionKey (P-340/P-344, shipped 2026-08-05/06) became visible through the SAME accessors
          with zero further 16.Testing code changes once 07.Messaging shipped them — confirmed by T-74
          (2026-08-07), which proved the round-trip with no change needed to InMemoryMessageBus.cs itself.
          A propagator registered as a production-shaped SCOPED service will throw a captive-dependency
          error if resolved into this fake while DI scope validation is enabled, because this fake
          remains a SINGLETON (see the singleton-lifetime NOTE below) — register propagator test doubles
          as Singleton or Transient, never Scoped, when composing the DI container for this fake.

InMemoryEventPublisher  (sealed class, implements IEventPublisher)
    constructor(IEnumerable<IMessageHeaderPropagator>? propagators = null)
        Materialized once via `propagators?.ToArray() ?? []`, mirroring InMemoryMessageBus's constructor
        exactly (including the re-enumeration-avoidance rationale).
    .PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)             → Task
    .PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct) → Task
    .Published                                                 → IReadOnlyList<object>
    .PublishedOf<TEvent>()                                     → IReadOnlyList<TEvent>
    .ShouldHavePublishedContext<TEvent>()                       → PublishContext  (throws if none found)
    NOTE: Does not wrap events in EventEnvelope<TEvent> — that's a MassTransitEventPublisher-specific
          transport concern (07.Messaging). This double records the raw TEvent instances only.
          Thread-safe under concurrent publish. SCOPE LOCK (P-352/WO-054): this divergence means P-340's
          EventEnvelope.Wrap<TEvent>()-construction-path fix and its TenantId-into-envelope propagation
          need no mirror here — both remain a MassTransitEventPublisher-only transport concern.
    NOTE (P-352/WO-054, IMPLEMENTED): both PublishAsync overloads build a fresh PublishContext, run
          registered propagators, then invoke the explicit configure callback last — identical precedence
          and the identical reference-capture design principle documented on InMemoryMessageBus above.

AddInMemoryMessageBus(this IServiceCollection)
AddInMemoryEventPublisher(this IServiceCollection)
    NOTE: Both register their respective double as a SINGLETON — a deliberate, documented deviation
          from IMessageBus/IEventPublisher's production "scoped" lifetime rule (07.Messaging). Tests
          need the same recorder instance to outlive the DI scope used by the system under test so
          assertions can run after the action completes.
    NOTE (P-352/WO-054): neither registration method needs any code change for the new optional
          IEnumerable<IMessageHeaderPropagator> constructor parameter to be honored — both already use
          implicit constructor activation (`services.AddSingleton<InMemoryMessageBus>()`), and .NET DI's
          built-in IEnumerable<T> support resolves an empty sequence (not an error) when zero
          IMessageHeaderPropagator registrations exist, so the common no-propagators test setup is
          unaffected.

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
        -1. TagWith — N/A, deliberate in-memory no-op (an in-memory LINQ query has no SQL query-tagging
           concept for a diagnostic comment to attach to; named explicitly, per DO-34/WO-053, so its
           absence is never mistaken for an oversight rather than a considered simplification)
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
          WRITE-SEMANTICS ASYMMETRY, BY DESIGN (DO-34/WO-053): AddAsync fails FAST on a duplicate derived
          key — catching a test-authoring bug immediately rather than deferring the eventual real
          unique-constraint violation to a phantom later SaveChangesAsync this fake does not model.
          UpdateAsync fails FAST on a missing derived key rather than silently upserting — a silent
          upsert would mask a genuine "this aggregate was never AddAsync-ed" test bug. DeleteAsync, by
          contrast, is ALWAYS a hard removal AND idempotent (a missing key is a silent no-op) — three
          different failure postures for three different mistakes: Add/Update both fail loudly because a
          silent auto-correction would hide a real test-authoring error, while Delete succeeds silently
          because "delete something already gone" is not, on its own, evidence of a bug.
          IRestorableRepository<TAggregate,TId> (WO-053/P-337, a sibling 06.Persistence phase in the SAME
          work order) is explicitly NOT implemented — scope-locked out of P-335. CORRECTED at Core-phase
          implementation time (2026-08-04): re-verified on disk per the phase's own instruction and found
          the interface HAS shipped since the original Design-phase "confirmed absent" finding (D-175) —
          06.Persistence/SharedKernel.Persistence.Abstractions/Repositories/IRestorableRepository.cs now
          exists with its single RestoreAsync(TAggregate, ct) member. Per the phase's own explicit
          instruction, it was deliberately NOT implemented against even though it now compiles — flagged
          as a genuine future follow-up, not silently absorbed into this pass. RE-CONFIRMED at Docs-phase
          implementation time (2026-08-04, DO-34): re-verified this interface is still shipped, compiled
          code — this scope lock remains a deliberate, active omission, not a stale note describing a
          since-vanished interface.

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

**[STATUS: Planned — P-459/WO-071]** target shape for `IAuditTrailWriter`/`IAuditQueryService` (the RICH `06.Persistence.Abstractions` contract), sourced from `06.Persistence/CLAUDE.md`'s ratified Auditing section — not yet on disk:

```text
FakeAuditTrailWriter  (sealed class, implements 06.Persistence.Abstractions.IAuditTrailWriter)
    ctor(IAuditActorContext actorContext, IClock? clock = null)
    RecordAsync(AuditEntry entry, ct)                               → Task<AuditRecord>
    NOTE: Append-only List<AuditRecord> behind a lock. Assigns a fresh Guid.CreateVersion7() id;
          resolves ActorId/TenantId via the constructor-injected IAuditActorContext (a simple
          test-supplied stub, NEVER 12.Security.Abstractions); stamps OccurredOn via the injected
          IClock (composable with Clocks/FakeClock). Computes a hash chain scoped per
          (TenantId, ResourceType) via a deterministic, NON-cryptographic hash (a plain
          HashCode.Combine-derived hex string) — deliberately NOT Cryptography/FakeContentHasher,
          honoring this package's sibling-capability-folder-isolation rule. No member on
          IAuditTrailWriter/IAuditQueryService allows updating/overwriting a stored AuditRecord —
          immutability is structural, not a runtime guard this fake adds.

FakeAuditQueryService  (sealed class, implements 06.Persistence.Abstractions.IAuditQueryService)
    GetResourceHistoryAsync(AuditResourceHistorySpecification spec, ct)  → Task<IReadOnlyList<AuditRecord>>
    GetActorActionsAsync(AuditActorActionsSpecification spec, ct)        → Task<IReadOnlyList<AuditRecord>>
    VerifyChainIntegrityAsync(tenantId, resourceType, from, to, ct)      → Task<AuditChainVerificationResult>
    NOTE: Evaluates the two named specifications in-memory, mirroring Persistence/FakeRepository's
          existing in-memory ISpecification<T> evaluation precedent. VerifyChainIntegrityAsync walks
          the chain for a (tenantId, resourceType, from, to) range and reports the first broken link.
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

SCOPE LOCK (P-187/WO-029, NARROWED by P-473/WO-069 — see revision below): SharedKernel.Testing must
    never take a project reference to SharedKernel.ServiceDefaults. StaticTenantProvider and
    FakeTenantResolutionStrategy reference only SharedKernel.Security.Abstractions.

**[STATUS: Planned — P-473/WO-075]** target shape for `ITenantCatalog`, sourced from `13.ServiceDefaults/CLAUDE.md`'s ratified "Tenant catalog" section — `SharedKernel.MultiTenancy`'s `Catalog/` sub-surface is not yet on disk:

```text
InMemoryTenantCatalog  (sealed class, implements SharedKernel.MultiTenancy.ITenantCatalog)
    .SeedTenant(TenantDescriptor descriptor, string? resolutionKey = null)   → void
    GetByIdAsync(Guid tenantId, ct)                                         → Task<TenantDescriptor?>
    GetByResolutionKeyAsync(string resolutionKey, ct)                       → Task<TenantDescriptor?>
    .MutateStatus(Guid tenantId, TenantStatus status)                       → void
    NOTE: Dictionary-backed store keyed by TenantId plus a secondary Dictionary<string, Guid> index
          for resolution-key lookup; GetByIdAsync/GetByResolutionKeyAsync return null for an
          unseeded tenant, never throw. .MutateStatus lets a test flip a seeded tenant to
          Suspended/Offboarded mid-test to exercise CatalogTenantStatusValidator's rejection path
          without re-seeding. Composes with the REAL (once shipped) CatalogTenantStatusValidator
          with zero code changes on either side — it depends only on the ITenantCatalog interface.

SCOPE-LOCK REVISION (P-473/WO-075, narrow and named — read this before touching the SCOPE LOCK
    above): the original P-187/WO-029 SCOPE LOCK forbade this package from EVER referencing
    SharedKernel.MultiTenancy at all. InMemoryTenantCatalog is the ONE exception: it alone may take
    a ProjectReference to SharedKernel.MultiTenancy (solely for ITenantCatalog/TenantDescriptor/
    TenantStatus/TenantIsolationMode) — a genuine, deliberate implementation of the real interface,
    per this phase's own acceptance criteria ("references only SharedKernel.MultiTenancy"), unlike
    StaticTenantProvider/FakeTenantResolutionStrategy's deliberate duck-typed-only shape above.
    StaticTenantProvider/FakeTenantResolutionStrategy are UNCHANGED by this revision and remain
    reference-free — the original P-187 rationale (a lightweight fake needs no real dependency on a
    still-evolving resolution-strategy interface) does not apply to a fake explicitly designed, this
    session, to exercise the REAL contract.
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

FakeDualApprovalStore  (sealed class, implements IDualApprovalStore from 05.Application.Behaviors ONLY)
    .TryGetApprovalAsync(string approvalKey, CancellationToken ct)              → Task<string?>
        Returns the recorded approver identity, or null if none recorded for approvalKey.
    .RecordApprovalAsync(string approvalKey, string approverIdentity, CancellationToken ct) → Task
        Upserts — lets a TEST play the role of the consuming service's own separate approval-recording
        workflow, since DualApprovalBehavior itself never calls this member (it only ever reads), per
        05.Application's own explicit "only ever reads" rule for IDualApprovalStore.
    .SimulateFailure                                            → bool  (settable, default false; forces both
                                                                    members to throw InvalidOperationException)
    .Reset()                                                    → void  (clears the store only — does NOT
                                                                    reset SimulateFailure, mirroring this
                                                                    package's universal convention)
    NOTE: Mirrors FakeIdempotencyKeyStore/FakeIdempotencyResponseStore's exact local-seam-fake precedent —
          this package ships a fake for a 05.Application.Behaviors-OWNED local interface, never a
          cross-domain 12.Security/06.Persistence/07.Messaging type. Backed by a thread-safe
          ConcurrentDictionary<string,string> keyed by approvalKey. No .Seed(...) alias — RecordApprovalAsync
          already IS the seeding mechanism per the real contract's own documented single-writer-path design.
          SCOPE LOCK: FakeAuthorizationContext (above) is NOT extended with IAuthorizationContextIdentity
          in this phase — that sibling optional-capability interface (needed for DualApprovalBehavior's
          self-approval-prevention check) was never named by this phase's own acceptance criteria and is
          itself equally unshipped as of this design pass; flagged as a natural future follow-up.

AddFakeDualApprovalStore(this IServiceCollection)
    NOTE: Registers FakeDualApprovalStore as a singleton IDualApprovalStore — a STANDALONE call, NOT
          bundled into AddFakeApplicationBehaviorServices() above, mirroring FakeIdempotencyResponseStore's
          own existing precedent of requiring manual/separate registration rather than being folded into
          the default bundle, since dual-control (IRequiresDualApproval) is an opt-in capability exactly
          like idempotency-response-replay is, not one of AddFakeApplicationBehaviorServices()'s three
          always-bundled fakes.

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

**[STATUS: Planned — P-459/WO-071]** target shape for `05.Application.Behaviors`'s OWN, deliberately smaller local-seam `IAuditTrailWriter` — SAME NAME as, but a DIFFERENT type from, `Persistence/FakeAuditTrailWriter` above, honoring this folder's own SCOPE LOCK (never a `06.Persistence.Abstractions` reference):

```text
FakeAuditTrailWriter  (sealed class, implements 05.Application.Behaviors's local IAuditTrailWriter)
    .RecordAsync(AuditEntry entry, ct)                              → Task
    .SimulateFailure                                                 → bool
    .ShouldHaveAudited(string action, string resourceType, string resourceId)   → void  (throws on no match)
    NOTE: A simple recording List<AuditEntry> using the LOCAL seam's own smaller AuditEntry shape
          (Action/ResourceType/ResourceId/BeforeSnapshot/AfterSnapshot/ApprovalId only — no
          Id/ActorId/TenantId/hash fields, those are the REAL 06.Persistence writer's job). Cross-
          referenced in both this type's and Persistence/FakeAuditTrailWriter's XML docs by full
          namespace, mirroring the already-shipped FakeUnitOfWork/FakeUnitOfWork cross-reference
          precedent exactly.
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
    .IndexManyAsync(IReadOnlyCollection<TDocument> documents, SearchWriteConsistency consistency,
                    SearchBulkWriteOptions bulkOptions, CancellationToken ct)
                                                                → Task<Result<SearchBulkReceipt>>
        [implemented P-355/WO-055, Core phase C-108, 2026-08-11] identical body to the 3-arg overload
        above, with bulkOptions threaded ONLY into .LastBulkWriteOptions below — no behavioral
        branching on its value, no simulated delay.
    .DeleteAsync(string documentId, SearchWriteConsistency consistency, CancellationToken ct)
                                                                → Task<Result<SearchWriteReceipt>>
    .DeleteManyAsync(IReadOnlyCollection<string> documentIds, SearchWriteConsistency consistency,
                     CancellationToken ct)                      → Task<Result<SearchBulkReceipt>>
    .DeleteManyAsync(IReadOnlyCollection<string> documentIds, SearchWriteConsistency consistency,
                      SearchBulkWriteOptions bulkOptions, CancellationToken ct)
                                                                → Task<Result<SearchBulkReceipt>>
        [implemented P-355/WO-055, Core phase C-108, 2026-08-11] same as the IndexManyAsync 4-arg
        overload above.
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
    .LastBulkWriteOptions                                      → SearchBulkWriteOptions?  (nullable, initially null;
                                                                   updated by BOTH IndexManyAsync and DeleteManyAsync on
                                                                   EVERY call, including via the 3-arg overloads' own
                                                                   .Default delegation, so it is always populated after
                                                                   ANY bulk call, never only after an explicit 4-arg one)
        [implemented P-355/WO-055, Core phase C-110, 2026-08-11] see the NOTE (BULK-WRITE THROTTLE)
        below.
    .Reset()                                                   → void  (clears the backing store AND all recorded-history
                                                                   lists, incl. LastBulkWriteOptions)

    NOTE (BULK-WRITE THROTTLE — P-355/WO-055, implemented Core phase C-108–C-110, 2026-08-11): the two
          new 4-arg IndexManyAsync/DeleteManyAsync overloads above accept a SearchBulkWriteOptions carrying
          .MaxBatchesPerSecond (09.Search's own P-354 design, D-32) but apply NO real Task.Delay/pacing —
          an in-memory ConcurrentDictionary write has no batch-dispatch loop to throttle, mirroring this
          fake's own "documented simplification, no real latency" precedent already established for
          WaitUntilSearchableAsync. To avoid SILENTLY IGNORING the caller's configuration (this fake's own
          documented rule: never a discarded parameter), the fake threads bulkOptions into
          .LastBulkWriteOptions above so a test can assert it was genuinely received. The EXISTING 3-arg
          overloads become one-line delegations to the new 4-arg ones passing SearchBulkWriteOptions.Default
          once this ships — mirroring BOTH real 09.Search providers' own locked delegation pattern (D-33)
          exactly, never two independently-maintained copies of the same bulk-write logic. This fake does
          NOT re-validate .MaxBatchesPerSecond — SearchBulkWriteOptions' own constructor already performs
          the non-positive-value ArgumentException guard (09.Search's own C-51), so a SearchBulkWriteOptions
          instance reaching this fake is already guaranteed valid by construction. Per-document bulk-write
          OUTCOME REPORTING (SearchBulkReceipt.Failures/.SucceededCount/.HasFailures) is UNCHANGED by this
          phase — it already shipped in P-276/WO-044 (see the existing NOTE (WRITE PATH) below) and needed
          no new work; the original P-355 phase-input premise assuming otherwise was corrected at Design
          time (D-180), mirroring 09.Search's own D-31 ratification of the identical finding.

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

**[STATUS: Planned, BREAKING — P-450/WO-068]** target shape for the ASYNC migration of `FakeEncryptionKeyProvider` plus the new `FakeEnvelopeEncryptionProvider`, sourced from `01.Core/CLAUDE.md`'s ratified "TARGET SHAPE (P-446/WO-068, BREAKING)" prose — `01.Core`'s async `IEncryptionKeyProvider` contract has not shipped yet:

```text
FakeEncryptionKeyProvider  (sealed class, implements the TARGET async IEncryptionKeyProvider)
    .SeedKey(CryptographicKey key)                                  → void  (unchanged from today)
    GetCurrentKeyAsync(CancellationToken ct = default)              → ValueTask<CryptographicKey>
    GetKeyAsync(string keyId, CancellationToken ct = default)       → ValueTask<CryptographicKey?>
    .SimulateFailure                                                 → bool  (unchanged from today)
    NOTE: BREAKING change to THIS FAKE's own public surface — GetCurrentKey()/GetKey(string) are
          REMOVED, not kept as an additive overload, mirroring the real interface's own breaking
          shape. Every existing consumer across 06.Persistence/15.Integration/17.Workflows test
          suites must update call sites to `await GetCurrentKeyAsync()`/`await GetKeyAsync(...)` —
          that migration is each consuming domain's own future work. Both members complete
          synchronously via ValueTask.FromResult since this fake performs no real I/O.

FakeEnvelopeEncryptionProvider  (sealed class, implements IEnvelopeEncryptionProvider)
    GenerateDataKeyAsync(ct = default)                              → ValueTask<EnvelopeDataKey>
    UnwrapDataKeyAsync(byte[] wrappedDataKey, string masterKeyId, ct = default)  → ValueTask<Result<byte[]>>
    .SimulateFailure                                                 → bool
    NOTE: A deterministic, NON-cryptographic 32-byte data key — "wrap" is a trivial reversible
          transform (e.g. XOR against a fixed test key), mirroring FakeSymmetricEncryptionService's
          own non-cryptographic-internals precedent. UnwrapDataKeyAsync fails for a
          wrappedDataKey/masterKeyId pair this fake did not itself produce — never a
          plausible-looking-but-wrong unwrap. TEST-ONLY — never real envelope encryption.
```

**[STATUS: Planned — P-453/WO-069]** target shape for `FakeTotpReplayGuard`, sourced from `01.Core/CLAUDE.md`'s ratified TOTP/HOTP Interface Contracts — not yet on disk:

```text
FakeTotpReplayGuard  (sealed class, implements ITotpReplayGuard)
    ctor(IClock? clock = null)
    HasBeenUsedAsync(string identityKey, string code, ct = default)                              → ValueTask<bool>
    MarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, ct = default)         → ValueTask
    .Reset()                                                        → void
    NOTE: An in-memory Dictionary<(string IdentityKey, string Code), DateTimeOffset> recording each
          MarkUsedAsync's expiry (now + validityWindow), where now is sourced from the injected
          IClock (composable with Clocks/FakeClock — never DateTime.UtcNow internally, mirroring the
          real TotpVerifier's own clock discipline). HasBeenUsedAsync returns true only while the
          stored expiry has not yet elapsed.
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

### `Integration/` — `IWebhookDispatcher` / `IWebhookDeliveryObserver` in-memory doubles (15.Integration)

```text
InMemoryWebhookDispatcher  (sealed class, implements IWebhookDispatcher — 15.Integration/SharedKernel.Integration.Webhooks)
    .DispatchAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)   → Task<IReadOnlyList<WebhookDeliveryResult>>
        where TEvent : IIntegrationEvent. Records the dispatched event instance into a thread-safe
        list. Returns the result list configured for typeof(TEvent) via
        SetDispatchResult<TEvent>(Func<TEvent,IReadOnlyList<WebhookDeliveryResult>>), or an EMPTY list
        by default — the honest default for a fake that performs no real subscription-store lookup,
        mirroring "zero matching subscriptions" rather than fabricating a fan-out this type cannot see.
        ArgumentNullException.ThrowIfNull(integrationEvent), matching production's own guard shape.
    .DispatchToSubscriptionAsync<TEvent>(WebhookSubscription subscription, TEvent integrationEvent, CancellationToken ct)
        → Task<WebhookDeliveryResult>
        where TEvent : IIntegrationEvent. Records the (subscription.SubscriptionId, event) pair.
        Returns the result configured for that subscription via SetDispatchResult(Guid subscriptionId,
        WebhookDeliveryResult result), or a synthetic 2xx success by default
        (new WebhookDeliveryResult(subscription.SubscriptionId, Guid.NewGuid(), true, 200, 1, null)) —
        a DELIBERATE DIVERGENCE from InMemoryMessageBus.RequestAsync's "throw InvalidOperationException
        when unconfigured" shape: IWebhookDispatcher's own documented contract states a delivery failure
        is ALWAYS expressed as this record, never a thrown exception, so an unconfigured fake must not
        throw either — it mirrors THIS interface's own contract, not a different interface's unrelated
        "missing handler" rule. ArgumentNullException.ThrowIfNull on both subscription/integrationEvent.
    .SendTestDeliveryAsync(WebhookSubscription subscription, CancellationToken ct)   → Task<WebhookDeliveryResult>
        Records subscription into TestDeliveries — a list independent of DispatchedTo, since a ping
        delivery is observably distinct from a real event delivery. Returns the result configured via
        SetTestDeliveryResult(Guid subscriptionId, WebhookDeliveryResult result), or the same synthetic
        2xx-success shape by default. ArgumentNullException.ThrowIfNull(subscription). Deliberately does
        NOT construct a real WebhookPingEvent and delegate through DispatchToSubscriptionAsync
        internally, unlike production's own documented "constructs a WebhookPingEvent and calls
        DispatchToSubscriptionAsync<TEvent> verbatim" behavior — this fake proves each interface
        member's OWN observable contract independently, not production's internal call graph; doing so
        would also require either an unseeded Guid.NewGuid()/non-fixed DateTimeOffset inside the ping
        event's own construction or coupling the two members' result-configuration together, neither of
        which serves a fake whose only job is per-member recording+configurable-result.
    .SetDispatchResult<TEvent>(Func<TEvent, IReadOnlyList<WebhookDeliveryResult>> resultFactory)   → void
    .SetDispatchResult(Guid subscriptionId, WebhookDeliveryResult result)                          → void
    .SetTestDeliveryResult(Guid subscriptionId, WebhookDeliveryResult result)                      → void
    .Dispatched                                                 → IReadOnlyList<object>  (every DispatchAsync-recorded event, in call order)
    .DispatchedTo                                               → IReadOnlyList<(Guid SubscriptionId, object Event)>  (every DispatchToSubscriptionAsync call, in call order)
    .TestDeliveries                                             → IReadOnlyList<WebhookSubscription>  (every SendTestDeliveryAsync call, in call order)
    .ShouldHaveDispatched<TEvent>()                             → TEvent  (returns the recorded event; throws if none found)
    .ShouldNotHaveDispatched<TEvent>()                          → void  (throws if any recorded)
    .ShouldHaveDispatchedTo(Guid subscriptionId)                → void  (throws if no DispatchToSubscriptionAsync call was recorded for that subscription)
    .ShouldHaveSentTestDelivery(Guid subscriptionId)            → void  (throws if no SendTestDeliveryAsync call was recorded for that subscription)
    NOTE: Thread-safe via ConcurrentQueue/ConcurrentDictionary backing collections, mirroring
          InMemoryMessageBus. This fake deliberately performs NO real subscription-store lookup, HTTP
          delivery, or HMAC signing — it is a recorder+configurable-result double for APPLICATION-layer
          tests asserting "did my command handler correctly trigger a webhook dispatch," never a
          fan-out/routing-fidelity double for testing WebhookDispatcher's OWN real routing logic
          (already covered by 15.Integration's own WebhookDispatcherFanOutTests.cs — this fake does not
          need to, and does not, reproduce it).
    DRIFT CORRECTION (2026-08-21, C-123): the original D-204 design predates 15.Integration's own
          WO-064 hardening pass (SK.15.WO064, P-421–P-429), which shipped AFTER D-204 was drafted and
          changed the target contract: IWebhookDispatcher gained SendTestDeliveryAsync (P-429, a third
          interface member the original design never covered) and WebhookDeliveryResult's positional
          constructor gained a second parameter, DeliveryId (P-423, now a 6-arg record, not 5) — a
          5-arg construction call as originally specified would not compile. Re-verified directly
          against the live 15.Integration/SharedKernel.Integration.Webhooks .cs source before
          implementing (not that domain's CLAUDE.md prose, not the prior D-204 text); see
          16.Testing/state-map.md's own "DESIGN-PHASE DRIFT CORRECTION" note (after D-208) for the
          full writeup, including the WebhookSubscription Secrets/Headers re-check this correction
          also performed.

InMemoryWebhookDeliveryObserver  (sealed class, implements IWebhookDeliveryObserver — 15.Integration/SharedKernel.Integration.Webhooks)
    .OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct)     → Task
    .OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct) → Task
        Both record their call into a thread-safe list and complete synchronously (Task.CompletedTask).
        NEVER throws — honestly models production's own documented contract ("an observer's exception
        is caught and logged... never allowed to fault the delivery pipeline") BY CONSTRUCTION, rather
        than requiring a separate try/catch proof.
    .Attempts                                                   → IReadOnlyList<(WebhookSubscription Subscription, int AttemptNumber)>
    .Completions                                                → IReadOnlyList<(WebhookSubscription Subscription, WebhookDeliveryResult Result)>
    .ShouldHaveObservedAttempt(Guid subscriptionId)             → void  (throws if no matching attempt recorded)
    .ShouldHaveObservedCompletion(Guid subscriptionId)          → WebhookDeliveryResult  (returns the recorded result; throws if none found)
    NOTE: This interface (IWebhookDeliveryObserver, OnAttemptAsync/OnCompletedAsync) was unaffected by
          15.Integration's WO-064 drift — no correction needed here beyond the InMemoryWebhookDispatcher
          block above.

AddInMemoryWebhookDispatcher(this IServiceCollection)
AddInMemoryWebhookDeliveryObserver(this IServiceCollection)
    NOTE: Both register their respective double as a SINGLETON — a deliberate, documented deviation
          from IWebhookDispatcher/IWebhookDeliveryObserver's production "scoped" lifetime
          (AddSharedKernelWebhooks/WithDeliveryObserver<TObserver>, 15.Integration), mirroring
          AddInMemoryMessageBus()/AddInMemoryEventPublisher()'s exact precedent and rationale — a test
          needs the same recorder instance to outlive the DI scope of the system under test so
          assertions can run after the action completes.

SCOPE LOCK (P-431/WO-064): Integration/ references SharedKernel.Integration.Webhooks only — the two
    interfaces (IWebhookDispatcher, IWebhookDeliveryObserver) plus the pure-DTO types they traffic in
    (WebhookSubscription, WebhookDeliveryResult) — never a sibling capability folder in this package.
    IIntegrationEvent (SharedKernel.Contracts.Events, 04.Contracts) is consumed transitively via the
    already-existing SharedKernel.Contracts reference (S-03), never a new direct reference.
```

### `Validation/` — checksum-correct sample generators (01.Core/SharedKernel.Validation) — added P-445/WO-067

**[STATUS: Planned — P-445/WO-067]** — `SharedKernel.Validation` has no `.csproj` on disk yet:

```text
ValidationSampleGenerator  (static class)
    ValidIban(string countryCode = "DE")            → string        InvalidIban()             → string
    ValidBic()                                       → string        InvalidBic()              → string
    ValidPan(CardNetwork network = Visa)             → string        InvalidPan()              → string
    ValidCurrencyCode()                              → string        InvalidCurrencyCode()     → string
    ValidCountryCode()                               → string        InvalidCountryCode()      → string
    ValidE164Phone()                                 → string        InvalidE164Phone()        → string
    ValidVat(string countryCode = "DE")              → string        InvalidVat()              → string
    ValidNationalId(string countryCode = "TR")       → string        InvalidNationalId(string countryCode = "TR")  → string
    NOTE: Every Valid* computes a genuinely correct check digit/checksum (mod-97 for IBAN, Luhn for
          PAN, TCKN's own algorithm) — never a hardcoded literal, so a future check-digit-table
          change cannot silently desynchronize the generator from the real validator. Every Invalid*
          deliberately corrupts exactly one character of a valid sample so it fails ONLY the intended
          check, never an unrelated format rule. Deterministic via Bogus.Faker's fixed-seed
          convention (FakerSeeding).

SCOPE LOCK (P-445/WO-067): Validation/ references SharedKernel.Validation only — never
    SharedKernel.Validation.FluentValidation (the FluentValidation rule-adapter sibling package).
```

### `Notifications/` — `INotificationSender` / `INotificationDeliveryObserver` in-memory doubles (15.Integration/SharedKernel.Integration.Notifications.Abstractions) — added P-463/WO-072

**[STATUS: Planned — P-463/WO-072]** — `SharedKernel.Integration.Notifications.Abstractions` has no `.csproj` on disk yet:

```text
InMemoryNotificationSender  (sealed class, implements INotificationSender)
    ctor(NotificationChannel supportedChannel)
    .SupportedChannel                                               → NotificationChannel
    .Sent                                                            → IReadOnlyList<object>  (raw messages, any TTemplateModel)
    .SetSendResult<TTemplateModel>(Func<NotificationMessage<TTemplateModel>, NotificationDeliveryResult>)  → void
    SendAsync<TTemplateModel>(NotificationMessage<TTemplateModel> message, ct)   → Task<NotificationDeliveryResult>
    .ShouldHaveSent<TTemplateModel>(Predicate<NotificationMessage<TTemplateModel>>? filter = null)  → void
    NOTE: Constructed per-channel so a test registers two instances (one per NotificationChannel),
          matching the real keyed-DI registration convention. Defaults to a synthetic success echoing
          the caller-supplied NotificationDeliveryId — never throws for a "provider failure," matching
          INotificationSender's own never-throws contract.

InMemoryNotificationDeliveryObserver  (sealed class, implements INotificationDeliveryObserver)
    OnAttemptAsync(NotificationDeliveryContext context, int attemptNumber, ct)                        → Task
    OnCompletedAsync(NotificationDeliveryContext context, NotificationDeliveryResult result, ct)       → Task
    NOTE: Records every call; never throws.

NotificationsServiceCollectionExtensions
    AddInMemoryNotificationSender(this IServiceCollection, NotificationChannel channel)      — keyed registration
    AddInMemoryNotificationDeliveryObserver(this IServiceCollection)

SCOPE LOCK (P-463/WO-072): Notifications/ references SharedKernel.Integration.Notifications.Abstractions
    only — never a concrete provider package (.Email.SendGrid/.Sms.Twilio) nor SharedKernel.Storage.Abstractions
    (NotificationAttachment.FileReference is consumed transitively, never a direct new reference).
```

### `Scheduling/` — `IScheduledJobRegistry` in-memory double (19.Scheduling/SharedKernel.Scheduling) — added P-467/WO-073

**[STATUS: Planned — P-467/WO-073]** — the WHOLE `19.Scheduling` domain has no `.csproj` on disk yet:

```text
InMemoryScheduledJobRegistry  (sealed class, implements IScheduledJobRegistry)
    ctor(MediatR.ISender sender)
    AddRecurring<TCommand>(jobName, cronExpression, commandFactory, configure)   → void
    AddDeferred<TCommand>(jobName, fireAtUtc, commandFactory, configure)         → void
    TriggerAsync(string jobName, DateTimeOffset simulatedNowUtc, ct = default)   → Task  (test-only manual driver)
    .Fired / .Skipped / .Misfired                                               → IReadOnlyList<string>  (job names)
    .ShouldHaveFired(jobName, int times = 1) / .ShouldHaveSkipped(jobName) / .ShouldHaveMisfired(jobName)  → void
    .Reset()                                                                    → void
    NOTE: A manual test driver, NEVER a real timer/hosted loop — starts nothing on construction.
          TriggerAsync constructs a ScheduledJobExecutionContext (caller-suppliable FencingToken,
          mirroring the real propagation-only seam), invokes the job's command factory, and dispatches
          via the injected ISender (a real MediatR type, not a 16.Testing type — no sibling-isolation
          concern). Honors OverlapPolicy/MisfirePolicy purely against caller-driven simulated state,
          never real elapsed wall-clock time.

SCOPE LOCK (P-467/WO-073): Scheduling/ references SharedKernel.Scheduling only.
```

### `Grpc/` — server-side gRPC interceptor test harness (14.Presentation/SharedKernel.Presentation.Grpc) — added P-470/WO-074

**[STATUS: Planned — P-470/WO-074]** — `SharedKernel.Presentation.Grpc` has no `.csproj` on disk yet:

```text
TestServerCallContext  (sealed class : Grpc.Core.ServerCallContext)
    static .Create(Metadata? requestHeaders = null, CancellationToken ct = default, ...)  → TestServerCallContext
    (every abstract ServerCallContext member — Method/Host/Peer/Deadline/CancellationToken/
     RequestHeaders/WriteOptions/AuthContext/PeerContinuationToken/ResponseTrailers/Status/
     PeerCertificate — backed by a settable field)
    NOTE: A concrete, minimal subclass of the abstract BCL Grpc.Core.ServerCallContext
    (Grpc.Core.Api — already a transitive necessity for any gRPC-hosting consumer). After a
    consuming test runs an interceptor's UnaryServerHandler (or any of the three streaming handler
    shapes — GrpcExceptionInterceptor must handle all four call shapes) against the constructed
    context, ResponseTrailers/Status are readable back for assertion. No real network/
    Grpc.AspNetCore TestServer bootstrap required.

SCOPE LOCK (P-470/WO-074): Grpc/ references SharedKernel.Presentation.Grpc and Grpc.Core.Api only.
```

### `DataPrivacy/` — `IDataSubjectRequestHandler` fake + masking-assertion helpers (01.Core/SharedKernel.DataPrivacy) — added P-475/WO-076

**[STATUS: Planned — P-475/WO-076]** — `SharedKernel.DataPrivacy` has no `.csproj` on disk yet:

```text
RecordingDataSubjectRequestHandler  (sealed class, implements IDataSubjectRequestHandler)
    ctor(IClock? clock = null)
    .SetExportResult(string subjectId, Result<DataSubjectExportBundle> result)   → void
    .SetErasureResult(string subjectId, Result<DataSubjectErasureReceipt> result) → void
    ExportDataAsync(string subjectId, ct = default)                             → Task<Result<DataSubjectExportBundle>>
    RequestErasureAsync(string subjectId, ct = default)                         → Task<Result<DataSubjectErasureReceipt>>
    NOTE: Records every call (subjectId + timestamp via the injected IClock) into two separate lists;
          defaults to a synthetic success, supporting BOTH success and failure outcomes per this
          phase's own acceptance criterion.

PiiMaskingAssertions  (static class)
    .ShouldBeMasked(string? original, string masked, Func<string?, string> maskingFunction)  → void
    NOTE: Re-invokes the SAME real PiiMasking.* function against `original` and asserts the result
          equals `masked` — NEVER reimplements the masking algorithm itself, avoiding a duplicated,
          driftable masking implementation inside this test-only helper.

SCOPE LOCK (P-475/WO-076): DataPrivacy/ references SharedKernel.DataPrivacy only.
```

### `Reporting/` — `IReportExporter<TRow>` in-memory double (20.Reporting/SharedKernel.Reporting.Abstractions) — added P-481/WO-077

**[STATUS: Planned — P-481/WO-077]** — the WHOLE `20.Reporting` domain has no `.csproj` on disk yet:

```text
InMemoryReportExporter<TRow>  (sealed class, implements IReportExporter<TRow>)
    ExportAsync(IAsyncEnumerable<TRow> rows, ReportDefinition<TRow> definition, ReportDestination destination, ct)  → Task<ReportExportOutcome>
    ExportToStreamAsync(IAsyncEnumerable<TRow> rows, ReportDefinition<TRow> definition, Stream destination, ct)     → Task
    .SimulateFailure                                                            → bool
    .LastDefinition / .LastDestination                                          → ReportDefinition<TRow>? / ReportDestination?
    .ShouldHaveExported<TRow>(Predicate<IReadOnlyList<TRow>>? rowsPredicate = null)  → void
    NOTE: FULLY DRAINS the caller-supplied IAsyncEnumerable<TRow> into an internal List<TRow> FOR
          ASSERTION PURPOSES ONLY. THIS IS A TEST-DOUBLE CONVENIENCE — IT MUST NEVER BE READ AS
          IMPLYING THE REAL .Csv/.Spreadsheet/.Pdf PROVIDERS ARE ALL MEMORY-BOUNDED. .Csv genuinely
          is; .Spreadsheet (ClosedXML) and .Pdf (MigraDoc/PdfSharp) are VERIFIED NOT TO BE — both
          third-party libraries build their full document object model in memory before writing a
          byte, a permanent, documented characteristic of those dependencies (20.Reporting/CLAUDE.md's
          own Domain Invariants). Fabricates a synthetic delivered FileReference without touching any
          real IFileStorage.

SCOPE LOCK (P-481/WO-077): Reporting/ references SharedKernel.Reporting.Abstractions only — never
    .Csv/.Spreadsheet/.Pdf (the concrete provider packages).
```

### `Localization/` — culture-context test helper (01.Core/SharedKernel.Localization) — added P-485/WO-078

**AUDIT FINDING (D-235), not a new-fake design:** `01.Core/SharedKernel.Localization`'s own planned default catalog, `InMemoryLocalizationCatalog` (`.AddTranslation(code, culture, value)` chained builder), IS ALREADY the deterministic, seedable, no-resx-files test double this phase's first requirement describes — a consuming test seeds translations directly against `01.Core`'s own shipped default catalog (`new InMemoryLocalizationCatalog().AddTranslation(...)`), never a second, colliding-named type duplicated in this package. Only `CultureScope` below is genuinely net-new, and it carries **NO blocker of any kind** — pure BCL, zero dependency on `SharedKernel.Localization`:

```text
CultureScope  (sealed class : IDisposable)
    ctor(CultureInfo culture)
    ctor(string cultureName)
    NOTE: Saves CultureInfo.CurrentCulture/.CurrentUICulture, sets both to the supplied culture for
          the scope's duration; Dispose() restores BOTH original values — even when the scope's body
          throws (a plain try/finally-safe IDisposable). Zero dependency on SharedKernel.Localization
          — a consuming test references that package directly for InMemoryLocalizationCatalog itself
          (see the AUDIT FINDING above).

SCOPE LOCK (P-485/WO-078): Localization/ references NOTHING beyond System.Globalization (BCL).
```

- Sibling capability folders (`Caching/`, `Domain/`, `Contracts/`, `Security/`, `Messaging/`, `Persistence/`, `Clocks/`, `Containers/`, `Communication/`, `ServiceDefaults/`, `Fakers/`, `Application/`, `Logging/`, `Storage/`, `Search/`, `Intelligence/`, `Workflows/`, `Cryptography/`, `FeatureManagement/`, `Integration/`, `Validation/`, `Notifications/`, `Scheduling/`, `Grpc/`, `DataPrivacy/`, `Reporting/`, `Localization/`) must **never reference each other** — reconfirmed explicitly at the twelve-phase P-441/P-445/P-450/P-453/P-459/P-463/P-467/P-470/P-473/P-475/P-481/P-485 dispatch (2026-08-26): `Persistence/FakeAuditTrailWriter`'s hash chain is a deliberately non-cryptographic, self-contained deterministic hash rather than a dependency on the already-shipped `Cryptography/FakeContentHasher`, even though the latter ships exactly the algorithm the former could reuse. Each fake depends only on the single abstraction package it implements (e.g., `FakeCacheService` → `SharedKernel.Caching.Abstractions` only). Mirrors the platform's sibling-package-isolation rule already enforced in `02.Caching`. Standalone helpers with no owning abstraction (`SpecificationTestBuilder<T>`, `ProjectionSpecificationBuilder<TAggregate,TResult>`, etc.) depend only on the domain types they operate over, never on a sibling folder's fake types. Demonstrated concretely by `Application/ApplicationPipelineTestHarness` (WO-040): it needs `ActivityListener`-based span capture, functionally similar to `Communication/ActivityRecorder`, but the rule forbids reusing it across folders — it hand-rolls its own small, self-contained `ActivityListener` wiring instead. A little duplicated boilerplate across sibling folders is the accepted cost of this rule; it is not a bug to "fix" by punching a hole in the isolation rule.
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
- `FakeTenantResolutionStrategy` (`ServiceDefaults/`) is **structurally compatible** with `13.ServiceDefaults`'s `ITenantResolutionStrategy`, not a direct interface implementation — this package takes no project reference to `SharedKernel.MultiTenancy`. The same "structural, not direct" pattern applies to any future fake whose owning interface lives in a package this domain has chosen not to reference. **REVISED, narrowly and by name, at P-473/WO-075**: `ServiceDefaults/InMemoryTenantCatalog` is the ONE exception — it genuinely implements the real `ITenantCatalog` interface via a real `ProjectReference` to `SharedKernel.MultiTenancy`, per that phase's own explicit acceptance criteria. `StaticTenantProvider`/`FakeTenantResolutionStrategy` are UNCHANGED by this revision and remain duck-typed/reference-free — see the `ServiceDefaults/` Interface Contracts block's own SCOPE-LOCK REVISION note for the full rationale.
- No static mutable state anywhere in this domain, with two documented exceptions: `Bogus.Randomizer.Seed` set via `FakerSeeding.Apply` (a deliberate, opt-in, process-wide determinism convention), and the single static, always-sampling `ActivityListener` registered by `AmbientActivityTestHelper` scoped to its own private `ActivitySource` (required so `ActivitySource.StartActivity` returns a real `Activity` in pure unit tests with no OTel host listening). Both are deliberate, opt-in, process-wide — never incidental shared state.
- `Caching/FakeRedisChannelService` (P-306/WO-050) is the first fake in this package to perform genuine in-process cross-call interaction rather than pure passive recording — `PublishAsync` synchronously invokes every currently-subscribed handler for the same channel, so a publish/subscribe/unsubscribe round trip can be proven in a single process with no real Redis. Every other `Caching/` fake (and most fakes in this package generally) only records what was called; `FakeCacheInvalidationBus.OnInvalidation` comes closest but still requires the CALLER to wire the chained effect manually, whereas `FakeRedisChannelService` performs the fan-out itself.
- `Caching/FakeRedisHashService`/`FakeTypedHashStore<T>`'s `IncrementFieldAsync` throws `InvalidCastException` when the target field currently holds a value that is not a `long` — a fake-only guard standing in for the `WRONGTYPE` error a real Redis `HINCRBY` against a non-numeric field would raise; it is the first place in this domain a fake throws to surface a caller-side type-mismatch bug rather than returning a `default`/no-op.
- `Caching/FakeCacheWarmupStrategy`'s cross-instance execution-order proof is a caller-supplied `ConcurrentQueue<string>` passed to every constructor that should share one — never a static field. This is the domain's established pattern for proving multi-instance ordering without adding a third exception to the `No static mutable state` rule above.
- `Caching/FakeTypedHashStore<T>` is deliberately independent of `Caching/FakeRedisHashService` — both maintain their own separate backing store — continuing this folder's existing convention that its (now six) fakes share no internal state with each other.
- **A "test-fixture builder" is a distinct category from a "fake," and the two justify different cross-package reference footprints for the identical target folder.** `Security/DpopTestProofBuilder`/`MtlsTestCertificateBuilder`/`ApiKeyRotationScenarioBuilder` (P-391/WO-060) construct INPUT DATA (a proof JWT, an X.509 certificate, a set of rotation-window key strings) that a CONSUMING SERVICE's own test feeds into real, concrete-provider-owned validation logic (`SharedKernel.Security.Oidc`'s internal `DpopProofValidator`, `SharedKernel.Security.Mtls`'s `IMtlsCertificateValidator`/`MtlsAuthenticationOptions`, `SharedKernel.Security.ApiKey`'s public `ApiKeyRotationComparer`) — this is fundamentally different from `FakeUserContext`/`SecurityTestContextBuilder`, which fake the ABSTRACTION (`IUserContext`) a consuming service depends on directly. Because a test-fixture builder never implements or invokes the concrete provider's own types, it can (and, per `Security/`'s own scope lock, must) stay built entirely on BCL primitives — no `ProjectReference` to the concrete provider package is ever needed, even when the builder's whole purpose is to exercise that provider's hardened behavior.
- Constructing a real, cryptographically valid test fixture (a genuinely ES256-signed DPoP proof, a genuinely chained-or-self-signed X.509 certificate) is preferred over a syntactically-plausible stand-in whenever the underlying BCL primitive makes it no harder to do so — `DpopTestProofBuilder`/`MtlsTestCertificateBuilder` (P-391/WO-060) both generate real key material and real signatures/chains, corrupting ONLY the specific claim/property a negative-path test needs broken, so a consuming integration test proves genuine rejection logic against the real, unmodified validator rather than a strawman that happens to look wrong.
- `InMemoryWebhookDispatcher`'s three interface methods deliberately default to **two distinct** unconfigured-outcome shapes — `DispatchAsync<TEvent>` defaults to an EMPTY result list, while `DispatchToSubscriptionAsync<TEvent>` AND `SendTestDeliveryAsync` (the latter added at C-123 correcting a design-phase drift, see the `Integration/` Interface Contracts block above) both default to a SYNTHETIC SUCCESS record — because each mirrors what the corresponding real production behavior would honestly look like with no test-supplied configuration (no subscriptions ever "just happen" to exist; a delivery a test didn't configure to fail didn't fail). No method ever throws for an unconfigured case — this is a DELIBERATE divergence from `InMemoryMessageBus.RequestAsync`'s "throw when unconfigured" shape, since `IWebhookDispatcher`'s own contract states delivery failure is always a returned record, never an exception; do not copy `RequestAsync`'s throwing pattern onto a fake whose real interface documents the opposite contract. `DispatchToSubscriptionAsync` and `SendTestDeliveryAsync` maintain INDEPENDENT recording lists (`DispatchedTo` vs. `TestDeliveries`) and independent configurable-result dictionaries — a ping delivery is observably distinct from a real event delivery, and this fake does not internally delegate one method through the other the way production does.
- `InMemoryWebhookDeliveryObserver` never throws from `OnAttemptAsync`/`OnCompletedAsync` — it models production's own "an observer's exception is caught and logged, never allowed to fault the pipeline" contract BY CONSTRUCTION (simply never faulting) rather than by wrapping a try/catch this fake would then need to separately prove works.
- `Caching/FakeTenantCacheService` (P-438/WO-065) guarantees cross-tenant isolation STRUCTURALLY, not by a runtime guard — its backing store is keyed by the FULL `(TenantId, Entity, Id)` tuple, so two different `tenantId` values sharing the identical `(entity, id)` physically occupy different dictionary slots and can never collide, mirroring `Caching/FakeRedisHashService`'s established tuple-key pattern. It additionally tracks tags per `(TenantId, Tag)` rather than per bare `Tag`, mirroring `02.Caching`'s own documented fix for a cross-tenant TAG-INVALIDATION vector (two tenants sharing one tag name must never be able to invalidate each other's entries via `RemoveByTagAsync`) — this is the first `Caching/` fake to encode a security-relevant isolation property directly into its data-structure shape rather than merely recording calls for later assertion.
- **A "fake encryption seam" requirement can be satisfied entirely by DOCUMENTATION when an existing fake already implements the exact interface a new capability's own prerequisite check demands — no new type is warranted just because the requesting phase names a new capability.** First applied at D-211 (P-438/WO-065): `02.Caching`'s planned `AddCacheEncryption()` needs only a REGISTERED `ISymmetricEncryptionService`, and `Cryptography/FakeSymmetricEncryptionService` (P-300/WO-049) already is exactly that — deterministic, zero-real-key-material, and already shipped. Before designing a new fake, always audit whether an existing fake in ANY folder — not just the folder the new capability superficially seems to belong to — already satisfies the target interface's literal prerequisite; when it does, the correct response is a cross-reference (in the DI Registration section and, if warranted, an Implementation Rules bullet like this one), never a duplicate type. This never creates a code-level reference between the two folders — sibling-isolation is preserved because the CONSUMING TEST, not either fake, is what composes the two independently.

---

## DI Registration (expected shape)

```csharp
// Messaging test doubles — singleton by design (see the Messaging/ Interface Contracts NOTE above).
// Register IMessageHeaderPropagator test doubles as Singleton or Transient (never Scoped) BEFORE
// this call if propagator-application coverage is needed — resolved via optional constructor
// injection, so this call itself needs no change either way (P-352/WO-054).
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

// Dual-approval local-seam fake (P-380/WO-058 companion, STANDALONE call — not bundled into
// AddFakeApplicationBehaviorServices(), mirroring the FakeIdempotencyResponseStore manual-registration
// precedent)
services.AddFakeDualApprovalStore();

// Webhook dispatch/observer doubles (P-431/WO-064) — SINGLETON, a deliberate deviation from the real
// scoped IWebhookDispatcher/IWebhookDeliveryObserver registrations, mirroring the
// Messaging/Search/Intelligence/Workflows singleton-fake precedent
services.AddInMemoryWebhookDispatcher();
services.AddInMemoryWebhookDeliveryObserver();

// Tenant-scoped cache fake (P-438/WO-065) — STANDALONE call, not bundled into
// AddFakeCachingServices(), mirroring AddFakeTypedHashStore<T>()/AddFakeCacheWarmupStrategy()'s
// existing standalone precedent
services.AddFakeTenantCacheService();

// Fake encryption seam for 02.Caching's AddCacheEncryption() — needs ZERO new SharedKernel.Testing
// code; Cryptography/FakeSymmetricEncryptionService (already shipped, P-300/WO-049) already
// satisfies AddCacheEncryption()'s sole prerequisite (a registered ISymmetricEncryptionService),
// with zero real AES-GCM key material
services.AddFakeCryptography();
services.AddLogging();                    // required transitively by FusionCacheService's ctor
// cachingBuilder.AddCacheEncryption();

// [STATUS: Planned] Async migration + envelope encryption fake (P-450/WO-068) — same AddFakeCryptography()
// bundle, once 01.Core ships the async IEncryptionKeyProvider/IEnvelopeEncryptionProvider contract
// services.AddFakeCryptography();   // will also register FakeEnvelopeEncryptionProvider as IEnvelopeEncryptionProvider

// [STATUS: Planned] Notification sender/observer doubles (P-463/WO-072) — KEYED per channel, mirroring
// the real AddKeyedScoped<INotificationSender, TSender>(channel) production shape
// services.AddInMemoryNotificationSender(NotificationChannel.Email);
// services.AddInMemoryNotificationSender(NotificationChannel.Sms);
// services.AddInMemoryNotificationDeliveryObserver();
```

Fakes in `Security/` (outside `SecurityTestContextBuilder`, which is a directly `new`-able builder — see below), `Persistence/`, `Clocks/` (outside `AddFakeDomainServices()`'s narrow `IClock` registration), `Contracts/`, `Communication/`, and `ServiceDefaults/` are intentionally **not** wrapped in `Add*` DI extensions — they are simple `new`-able classes or static helpers with test-controlled constructor parameters, and registering them via DI adds indirection most unit tests don't need. Only doubles that exist specifically to be swapped in for a production DI registration (caching, messaging, the single `IClock` registration in `AddFakeDomainServices()`, `Application/`'s three-fake bundle plus the new standalone `AddFakeDualApprovalStore()` call, `Storage/`'s two-fake bundle, `Search/`'s per-index call plus its provisioning bundle, `Intelligence/`'s per-embedding-generator call, per-collection call, vector-provisioning bundle, and semantic-kernel bundle, `Workflows/`'s single dispatcher call, `Cryptography/`'s eight-fake `AddFakeCryptography()` bundle, `FeatureManagement/`'s single `AddFakeFeatureManagement()` call, `Caching/`'s extended `AddFakeCachingServices()` bundle plus the per-`T` `AddFakeTypedHashStore<T>()` and per-strategy `AddFakeCacheWarmupStrategy()` calls, and now the new standalone `AddFakeTenantCacheService()` call) ship a convenience extension — justified for the two newest folders because their real production counterparts (`AddSharedKernelCryptography()`, `AddSharedKernelFeatureManagement()`) are themselves `Add*`-registered, unlike `Security/`'s `IUserContext`/`ITenantProvider`, which production never registers via a single bundled call either; justified for the three new `Caching/` extensions because their real production counterparts (`AddRedisChannelService`, `AddRedisHashService`, `AddTypedHashStore<T>`, `AddCacheWarmup<TStrategy>`) are likewise all `Add*`-registered in `02.Caching`; justified for `AddFakeDualApprovalStore()` because dual-control is an opt-in capability, exactly mirroring why `FakeIdempotencyResponseStore` requires manual registration rather than bundling; justified for `AddInMemoryWebhookDispatcher()`/`AddInMemoryWebhookDeliveryObserver()` (P-431/WO-064) because their real production counterparts (`AddSharedKernelWebhooks()`, `WithDeliveryObserver<TObserver>()`) are themselves `Add*`-registered in `15.Integration`; justified for `AddFakeTenantCacheService()` (P-438/WO-065) because its real production counterpart, `AddTenantCacheService(this ICachingBuilder)` (`02.Caching` Phase 44/P-435), is itself `Add*`-registered and explicitly additive to `AddTenantCacheKeyProvider()` rather than folded into it — this fake's standalone shape mirrors that exact relationship. `AddFakeContractsServices()` is explicitly **deferred** (P-064/WO-012) — none of the `Contracts/` helpers currently need DI registration; add it only if a concrete need surfaces. `SecurityTestContextBuilder` (`Security/`, P-382/WO-058) is deliberately **not** DI-registered — a directly `new`-able fluent builder, consistent with `SpecificationTestBuilder<T>`/`ProjectionSpecificationBuilder<TAggregate,TResult>`/`ApplicationPipelineTestHarness`'s existing convention for builder-shaped types. `ApplicationPipelineTestHarness` (`Application/`) is deliberately **not** DI-registered — it is a directly `new`-able builder/harness type, consistent with `SpecificationTestBuilder<T>`/`ProjectionSpecificationBuilder<TAggregate,TResult>`'s existing convention for builder-shaped types. `Containers/MinioContainerFixture`/`MeilisearchContainerFixture`/`ElasticsearchContainerFixture` are likewise never DI-registered — container fixtures are consumed via xUnit `ICollectionFixture<T>`, never a DI container, consistent with `PostgreSqlContainerFixture`/`RedisContainerFixture`/`RabbitMqContainerFixture`. **`[STATUS: Planned]` additions from the twelve-phase batch (2026-08-26):** `Domain/MoneyFaker`/`FakeExchangeRateProvider`, `Validation/ValidationSampleGenerator`, `Scheduling/InMemoryScheduledJobRegistry`, `Grpc/TestServerCallContext`, `DataPrivacy/RecordingDataSubjectRequestHandler`/`PiiMaskingAssertions`, `Reporting/InMemoryReportExporter<TRow>`, `Localization/CultureScope`, `Persistence/FakeAuditTrailWriter`/`FakeAuditQueryService`, and `Application/FakeAuditTrailWriter` are ALL directly `new`-able — none of the twelve new fakes swap in for a single-call production DI registration the way `Notifications/`'s keyed `INotificationSender`/`INotificationDeliveryObserver` do (see the `Notifications/` `Add*` entries above), so none needs an `Add*` extension, continuing this section's established convention.

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
- `InMemoryWebhookDispatcher`/`InMemoryWebhookDeliveryObserver`/`AddInMemoryWebhookDispatcher()`/`AddInMemoryWebhookDeliveryObserver()` (`Integration/`, P-431/WO-064) are proven in `SharedKernel.Testing.SelfTests` (T-86, implemented 2026-08-21 — 30 tests across `InMemoryWebhookDispatcherTests.cs`/`InMemoryWebhookDeliveryObserverTests.cs`/`IntegrationServiceCollectionExtensionsTests.cs`) — per the "actual consumer today, not theoretical" rule above: `15.Integration/SharedKernel.Integration.Webhooks.Tests.csproj` already carries a live `ProjectReference` to `SharedKernel.Testing`, but net-new types with zero existing consumer route to `SelfTests` regardless, mirroring `TestSharedKernelDbContext`/`Application/`'s established precedent. Adopting these doubles into `15.Integration`'s own suite remains a future cross-domain follow-up, not performed here.
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
- `Messaging/InMemoryMessageBus`/`InMemoryEventPublisher`'s new `PublishContext`-capture + `IMessageHeaderPropagator`-application surface (P-352/WO-054) is proven by EXTENDING the two EXISTING files `SharedKernel.Testing.SelfTests/Messaging/InMemoryMessageBusTests.cs`/`InMemoryEventPublisherTests.cs` (T-72/T-73) — additive only, every pre-existing test in both files must keep passing unmodified, per this phase's own "purely additive" acceptance criterion. This is the first `Messaging/` Tests-phase task in this domain's history that is an EXTENSION rather than a net-new file, since `InMemoryMessageBus`/`InMemoryEventPublisher` were already `●` Published (P-183/WO-029). **IMPLEMENTED AND PROVEN (T-72/T-73, 2026-08-04)**: `InMemoryMessageBusTests.cs` gained 12 new tests (all 13 pre-existing tests untouched) — 4 propagator-registration-order tests (one per dispatch shape: `PublishAsync` no-configure, `PublishAsync` with-configure, `SendAsync`, `RequestAsync`), 1 explicit-configure-wins-on-key-conflict test, 3 `CorrelationId`/`CausationId`/`Headers` round-trip tests, 1 zero-propagators-default-constructor test, 3 throw-on-no-match tests (one per `ShouldHave*Context` accessor) — via three new private test-double `IMessageHeaderPropagator` classes (`OrderTrackingPropagator`, `HeaderSettingPropagator`, `FullContextPropagator`). `InMemoryEventPublisherTests.cs` gained 7 new tests (all 9 pre-existing tests untouched) mirroring the same coverage shape for both `PublishAsync` overloads, plus a dedicated regression guard proving `.Published`/`.PublishedOf<TEvent>()` keep their exact pre-existing `IReadOnlyList<object>`/`IReadOnlyList<TEvent>` return types and publish-order semantics after the internal `_published` tuple-shape change. `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` (real Docker daemon, incl. `Containers/`) passes 889/889 (870 pre-existing + 19 net new), zero regressions. **The TenantId/PartitionKey-specific slice (T-74) IMPLEMENTED 2026-08-07**, once `07.Messaging`'s P-340/P-344 shipped `PublishContext.TenantId`/`.WithTenantId`/`.PartitionKey`/`.WithPartitionKey` in code (re-verified directly on disk before writing any test code, not trusted from prose alone): `InMemoryMessageBusTests.cs` gained 12 more tests and `InMemoryEventPublisherTests.cs` gained 7 more — a `TenantHeaderPropagator`-shaped `TenantPropagator` local test double (plus a mirrored `PartitionKeyPropagator`) proving both fields round-trip through every `ShouldHave*Context` accessor across all four/two dispatch shapes, an explicit-override-wins precedence proof for each field, and a default-null-state proof — exactly mirroring T-72/T-73's `CorrelationId`/`Headers` coverage shape, with zero `SharedKernel.Testing` production code change needed. All 32 pre-existing tests across both files (13+12 and 9+7) remained untouched. `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` (real Docker daemon, incl. `Containers/`) passes 908/908 (889 pre-existing + 19 net new), zero regressions. This closes `SK.16.Tests` (74/74) and, since every other phase was already `●`, the entire `16.Testing` domain end to end.
- `Search/InMemorySearchIndex<TDocument>`'s new bulk-write throttle overloads/`LastBulkWriteOptions` (P-355/WO-055, implemented Core phase C-108–C-110, 2026-08-11) are proven by EXTENDING the EXISTING `SharedKernel.Testing.SelfTests/Search/InMemorySearchIndexTests.cs` — additive only, every pre-existing test in that file kept passing unmodified — mirroring `Messaging/`'s own P-352/WO-054 "extend, don't replace" precedent for a second occurrence in this domain's history. **IMPLEMENTED AND PROVEN (T-75–T-77, 2026-08-11)**: 8 new tests added. T-75 (parity, deliberately scoped INTRA-PACKAGE — 3-arg vs. 4-arg-with-`SearchBulkWriteOptions.Default` on this fake, never against the REAL Meilisearch/ElasticSearch providers, since this package takes no `ProjectReference` to `SharedKernel.Search.Meilisearch`/`.ElasticSearch` and that proof is `09.Search`'s own T-32–T-34 against real Testcontainers-hosted engines): `IndexManyAsync_FourArgOverloadWithDefaultOptions_ProducesIdenticalReceipt_ToThreeArgOverload`/`DeleteManyAsync_FourArgOverloadWithDefaultOptions_ProducesIdenticalReceipt_ToThreeArgOverload`, each re-running its named pre-existing 3-arg scenario on one fresh fake instance against the 4-arg-with-`Default` path on a second fresh instance and asserting `SucceededCount`/`HasFailures`/`Failures` (`SequenceEqual`, since `SearchBulkReceipt`'s record-generated `Equals` does not deep-compare its `IReadOnlyList<SearchItemFailure>` field — a worthwhile equality pitfall to note for future record-based receipt assertions in this package) and the full `SearchWriteReceipt` (direct record equality) are identical. T-76 (`LastBulkWriteOptions` audit surface, 6 tests): null-before-first-call; `Assert.Same` (not just value-equality) against a caller-supplied throttle instance on both members' 4-arg overloads; a revert-to-`Default`-on-subsequent-3-arg-call sequencing proof (ruling out a "sticky first value" bug); a cross-member shared-property proof (`IndexManyAsync`'s throttle is overwritten by a subsequent 3-arg `DeleteManyAsync` call); and a `Stopwatch`-timed 50-document bulk write with `MaxBatchesPerSecond = 1` completing well under one second (the no-real-pacing proof). T-77 (regression): full suite re-run. `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` (real Docker daemon, incl. `Containers/`) passes 916/916 (908 pre-existing + 8 net new), zero regressions.
- `Security/FakeUserContext`'s new `IdentityKind`/`Permissions`/`HasPermission(string)` members (P-374/WO-057) are proven in `SharedKernel.Testing.SelfTests` — a repo-wide grep for `FakeUserContext` confirms the ONLY consumer today is this domain's own `SharedKernel.Testing.SelfTests/Security/FakeUserContextTests.cs`; neither `SharedKernel.Security.Abstractions.Tests` nor `SharedKernel.Security.Oidc.Tests` carries a `ProjectReference` to `SharedKernel.Testing`, unchanged since the original P-188/WO-030 D-52 finding. The recorded `⚑` Blocked state cleared before any Core-phase code was written — `12.Security`'s own `SK.12.Core` (P-367/P-368) had already shipped `IUserContext.IdentityKind`/`.Permissions`/`.HasPermission` and `Security/FakeUserContext.cs` had already been patched in-place by that concurrent session. **IMPLEMENTED 2026-08-13 (T-78)**: 14 new tests added to the EXISTING `FakeUserContextTests.cs` (all 15 pre-existing tests untouched) — `IdentityKind` default/settable-across-all-four-values coverage, `Permissions` default-empty/settable coverage, and `HasPermission` matching/case-insensitive/no-match/empty-collection/never-throws coverage, mirroring the pre-existing `HasRole`/`Roles` test shape exactly. Full regression `dotnet test SharedKernel.Testing.SelfTests.csproj --configuration Release` (real Docker daemon, incl. `Containers/`) passes 930/930 (916 pre-existing + 14 net new), zero regressions. `FakeTenantProvider` needed no change and carried no new test obligation, confirmed via `FakeTenantProviderTests.cs`'s unmodified pass. This closes `SK.16.Tests` (78/78) and WO-057/P-374's `SK.16.Tests` contribution — only `SK.16.Docs` (DO-38) remains open in the whole `16.Testing` domain.
- `Security/SecurityTestContextBuilder` (P-382/WO-058) is proven in `SharedKernel.Testing.SelfTests` — a net-new type with no owning-domain interface of its own (it composes `IUserContext`/`ClaimsPrincipal`, it does not implement either), so it routes to `SelfTests` unconditionally, mirroring `SpecificationTestBuilder<T>`/`ProjectionSpecificationBuilder<TAggregate,TResult>`'s existing routing. **IMPLEMENTED AND PROVEN (T-79/T-80, 2026-08-17)**: `Security/SecurityTestContextBuilderTests.cs` (23 tests) proves both the identity-basics half (default authenticated state, `Unauthenticated()`, one-`Claim`-per-role/space-delimited-scope/one-`Claim`-per-authentication-method emission, additive `WithClaim`/`WithClaims`) and the AMR/ACR/AuthTime step-up-authentication half, including a dedicated anchor test proving `.BuildUserContext()` is never derived by constructing a `ClaimsPrincipal` first and parsing it back — the two projections are independently populated from the same fluent state, exactly as designed.
- `Application/FakeDualApprovalStore` (companion to P-380/WO-058, no root `P`-number) is proven in `SharedKernel.Testing.SelfTests` — mirrors `FakeIdempotencyKeyStore`/`FakeIdempotencyResponseStore`'s exact routing precedent (a `05.Application.Behaviors`-owned local seam, net-new, zero existing consumer). `SharedKernel.Application.Behaviors.Tests` already carries a live `ProjectReference` to `SharedKernel.Testing` (confirmed WO-040), but per this package's own established rule ("a live reference does not by itself mean a net-new fake should route there — the rule is whether the type is ACTUALLY consumed there today, never whether it could theoretically be"), this fake still routes to `SelfTests`. **IMPLEMENTED AND PROVEN (T-81, 2026-08-17)**: `Application/FakeDualApprovalStoreTests.cs` (11 tests across two test classes) proves the store's round-trip/upsert/`SimulateFailure`/`Reset` behavior and `AddFakeDualApprovalStore()`'s standalone-not-bundled singleton registration.
- `Security/DpopTestProofBuilder`/`MtlsTestCertificateBuilder`/`MtlsTestCertificateAuthority`/`ApiKeyRotationScenarioBuilder` (P-391/WO-060) are proven in `SharedKernel.Testing.SelfTests` — net-new, no owning-domain-implementable interface (these are scenario/data builders, not fakes of an abstraction), and `12.Security` sits below `16.Testing` in the layering rules so `SharedKernel.Security.Oidc.Tests`/`.Mtls.Tests`/`.ApiKey.Tests` can never take a `ProjectReference` back to this package — mirroring `FakeClock`/`Cryptography/`/`FeatureManagement/`'s established no-owning-suite-possible fallback, the fourth folder area to invoke this specific rationale. **IMPLEMENTED AND PROVEN (T-82–T-84, 2026-08-17)**: `Security/DpopTestProofBuilderTests.cs` (13 tests) independently re-verifies the ES256 signature via a fresh `ECDsa` public-key import (not merely round-tripping through the builder's own signing code) and proves all three negative-path `ath` corruption methods; `Security/MtlsTestCertificateBuilderTests.cs` (10 tests) proves self-signed/chained/revoked certificate construction via a REAL `X509Chain` validation (`AsChainedFromEphemeralCa` builds and validates against the returned `IssuingCertificate`) plus a hand-parsed CRL proof — writing the CRL-parsing helper surfaced a genuine `CertificateRevocationListBuilder.AddEntry` DER sign-padding quirk (see the `MtlsTestCertificateBuilder` NOTE above), worked around in the TEST's own parsing helper, never in the production builder; `Security/ApiKeyRotationScenarioBuilderTests.cs` (9 tests) proves the scenario-generation half only — its class-level `<remarks>` documents that the end-to-end interop half (calling the real `SharedKernel.Security.ApiKey.ApiKeyRotationComparer.AnyMatch` against this builder's output) is DEFERRED, since that type does not exist yet as of this pass (re-confirmed absent from every `.cs` file under `12.Security/SharedKernel.Security.ApiKey/`; root `P-389` still `◐` Dispatched), mirroring the `Search/` T-75 intra-package-only precedent (P-355/WO-055). Both crypto-fixture builders were additionally proven via throwaway smoke-test console apps (built, run, then deleted) before their final `SelfTests` files were written, per this domain's established crypto-fixture-verification discipline. Full regression `dotnet test SharedKernel.Testing.SelfTests.csproj --configuration Release` (real Docker daemon, incl. `Containers/`) passes 996/996 (930 pre-existing + 66 net new across all three bullets above), zero regressions.
- `Caching/FakeTenantCacheService`/`AddFakeTenantCacheService()` (P-438/WO-065) are proven in `SharedKernel.Testing.SelfTests` (`Caching/FakeTenantCacheServiceTests.cs`, 9 facts), per the same no-actual-consumer-yet rationale already established for this same folder's earlier fakes — `02.Caching`'s own `.FusionCache.Tests`/`.Redis.Tests` projects reference `SharedKernel.Testing` but do not yet reference `FakeTenantCacheService` itself. The companion "fake encryption seam" interop proof (`Caching/CacheEncryptionFakeCryptographyInteropTests.cs`, T-88) is a SEPARATE test file taking a test-only `ProjectReference` to `SharedKernel.Caching.FusionCache` — mirroring `Security/ApiKeyRotationScenarioBuilderTests.cs`'s T-84 precedent of referencing a real production package under test — since it proves composition with the real `AddCacheEncryption()` extension, not merely this package's own `Cryptography/FakeSymmetricEncryptionService` in isolation (already proven by its own pre-existing test file).
- **All twelve fakes from the 2026-08-26 batch dispatch (P-441/P-445/P-450/P-453/P-459/P-463/P-467/P-470/P-473/P-475/P-481/P-485) route to `SharedKernel.Testing.SelfTests` unconditionally — none has any possible owning-domain suite to "prove it there" against.** Every target interface is genuinely new (not yet even compiled) as of this dispatch, so no owning domain's own `.Tests` project can reference a `16.Testing` fake for an interface that does not exist yet in its own package — the "actual consumer today, not theoretical" rule (established at `Application/`/`Integration/`) applies at its most extreme: there is no theoretical consumer either. Routing, once each fake ships: `Domain/MoneyFaker`/`FakeExchangeRateProvider` (T-89), `Validation/ValidationSampleGenerator` (T-90), the migrated `Cryptography/FakeEncryptionKeyProvider` + `FakeEnvelopeEncryptionProvider` (T-91), `Cryptography/FakeTotpReplayGuard` + `Security/FakeTotpChallengeStore` (T-92), `Persistence/FakeAuditTrailWriter`/`FakeAuditQueryService` + `Application/FakeAuditTrailWriter` (T-93), `Notifications/InMemoryNotificationSender`/`InMemoryNotificationDeliveryObserver` (T-94), `Scheduling/InMemoryScheduledJobRegistry` (T-95), `Grpc/TestServerCallContext` (T-96), `ServiceDefaults/InMemoryTenantCatalog` (T-97), `DataPrivacy/RecordingDataSubjectRequestHandler`/`PiiMaskingAssertions` (T-98), `Reporting/InMemoryReportExporter<TRow>` (T-99), and `Localization/CultureScope` (T-100, the one fully-unblocked-from-day-one type in this batch — its restore-after-exception proof needs no upstream package at all) plus the D-235 audit-finding integration proof against `01.Core`'s own `InMemoryLocalizationCatalog` (T-101). See `16.Testing/state-map.md`'s Cross-Domain Dependencies table for each fake's specific upstream blocker.

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
- [2026-08-04] `SK.16.Docs` closed for WO-053 (DO-33–DO-35 → `●`, 35/35, promoted to root) — **this closes WO-053/P-335 in full: all six phases of `16.Testing` (Design/Scaffold/Core/Tests/Docs/Published) are `●` again.** DO-33 (`FakeDbConnectionFactory`) and DO-35 (`FakeUnitOfWork`/`FakePersistenceTransaction`, incl. the `Application/FakeUnitOfWork` reciprocal cross-reference) were both found already fully satisfied from the Core-phase pass on independent re-verification — grep-confirmed the `[STATUS: Planned — documented gap...]` marker text is absent anywhere in this file; re-read the live `13.ServiceDefaults/SharedKernel.MultiTenancy/SharedKernel.MultiTenancy.Tests/Resolution/DatabaseTenantResolutionStrategyTests.cs` directly and confirmed it still hand-rolls a raw `IDbConnection`/`IDbCommand`/`IDbDataParameter` NSubstitute mock, so `FakeDbConnectionFactory.cs`'s own cross-reference claim remains accurate today, not stale. DO-34 (`FakeRepository<TAggregate,TId>`) found and closed two genuine documentation gaps: (1) step -1 (`TagWith`) was never named among the shared pipeline's documented ORM-only in-memory no-ops (Includes/StringIncludes/AsSplitQuery/AsNoTracking were already named) — added to both the `ApplyFilterOrderDistinct` method's XML `<remarks>`/inline comment and this file's SHARED SPECIFICATION PIPELINE listing above; (2) `AddAsync`/`UpdateAsync` documented WHAT they throw but never WHY — added a class-level "write-semantics asymmetry, by design" rationale (Add fails fast on a duplicate key rather than deferring to a phantom `SaveChangesAsync`; Update fails fast on a missing key rather than masking a "never Added" bug via silent upsert; Delete stays idempotent because deleting something already gone is not evidence of a bug) to both the code and this file's `FakeRepository` NOTE block. Also re-verified `IRestorableRepository<TAggregate,TId>` directly on disk per this task's own instruction — confirmed still shipped, with the scope-lock note updated to record the fresh re-confirmation. `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, pre-existing warnings only (2 `NU1903`; remaining `CS1574`/`CS8509` all traced to unrelated files), none introduced by the doc-only edit to `FakeRepository.cs`. No consuming-domain regression check applies beyond this domain's own `SelfTests` suite — `dotnet test` (Persistence-filtered) 106/106, full non-container suite 844/844, zero regressions. `Persistence/` Interface Contracts and Folder/Namespace Map sections both updated to record Docs-phase completion (testing-phase-implementer).
- [2026-08-04] P-352/WO-054 processed (testing-arch-planner): design pass extending the EXISTING `Messaging/` folder — read `07.Messaging/CLAUDE.md` in full for the target shape of P-340 (`PublishContext.TenantId`)/P-341 (header-propagator symmetry across all dispatch verbs)/P-344 (`PublishContext.PartitionKey`)/P-345 (`ITenantContextAccessor`/`AmbientCorrelationHeaderPropagator`/`TenantHeaderPropagator`), then independently re-verified all four against the LIVE `07.Messaging.Abstractions`/`.MassTransit` source rather than trusting that domain's own ratified-but-unshipped prose: `PublishContext.cs` declares only `CorrelationId`/`CausationId`/`Headers`; `IMessageHeaderPropagator.cs` IS real, shipped code; `MassTransitMessageBus.SendAsync`/`.RequestAsync` genuinely call the transport directly with zero propagator involvement (the P-341 asymmetry is real, not hypothetical); no `TenantContext/`/`HeaderPropagation/{Ambient,Tenant}*.cs` files exist yet. Also read the live `Messaging/{InMemoryMessageBus,InMemoryEventPublisher,MessagingServiceCollectionExtensions}.cs` directly and found a pre-existing gap this phase closes as a side effect: both fakes call `configure(new PublishContext())` and discard the result — not even the already-real `CorrelationId`/`CausationId`/`Headers` had ever been captured. **Key design decision**: both fakes' new `ShouldHavePublishedContext<T>()`/`ShouldHaveSentContext<T>()`/`ShouldHaveRequestedContext<TRequest,TResponse>()`/`ShouldHavePublishedContext<TEvent>()` accessors return a REFERENCE to the real, actually-configured `PublishContext` instance — never a copy of individually-named scalar fields — so `.TenantId`/`.PartitionKey` exposure needs ZERO further `16.Testing` code once `07.Messaging` ships them. This is the SEVENTH design-ahead-of-schedule occurrence in this domain's history (after P-269/P-276/P-284/P-288/P-300/P-335) and the first to introduce this specific "capture by reference" technique, which converts what would otherwise be a hard compile blocker into a non-blocker for the fake's own Core-phase code (C-106/C-107) and most of its Tests-phase proof (T-72/T-73) — only T-74 (which must write the literal member names `.TenantId`/`.WithTenantId`/`.PartitionKey`/`.WithPartitionKey` in test source) is `⚑` Blocked. Both fakes' constructors gain an optional `IEnumerable<IMessageHeaderPropagator>? propagators = null` parameter, applied identically before every dispatch shape, mirroring the real `MassTransitMessageBus.BuildContextFromPropagators`'s propagators-first/explicit-configure-wins precedence exactly (models P-341's TARGET contract, not production's current defect). A singleton-fake-vs-scoped-propagator DI caveat is documented explicitly, since both fakes remain registered as singletons (an unchanged, already-existing deviation) while production `IMessageHeaderPropagator` implementations are documented as scoped. Three things explicitly scope-locked OUT: no fake for `ITenantContextAccessor`/`TenantHeaderPropagator`/`AmbientCorrelationHeaderPropagator` (P-345 — concrete implementations a real service composes, not this phase's job — a test registers its own trivial `IMessageHeaderPropagator` test double instead); `InMemoryEventPublisher` still does not wrap events in `EventEnvelope<TEvent>` (P-340's envelope-construction fix remains `MassTransitEventPublisher`-specific, unaffected divergence); `TestHarnessFactory` needs no change (bootstraps the real bus, which gains the fix automatically once `07.Messaging` ships it). `Messaging/` Interface Contracts block updated with the new constructor signatures/accessor methods (marked `[STATUS: Planned — P-352/WO-054]`, T-74's slice separately marked `[STATUS: Blocked — pending 07.Messaging P-340/P-344]`); Folder/Namespace Map narrative paragraph gains a `Messaging/` entry (the THIRD folder in this domain's history to gain net-new surface for an already-`**implemented**`-labeled folder, after `Caching/`'s P-306/WO-050 and `Persistence/`'s P-335/WO-053); DI Registration section's Messaging code sample gains a propagator-lifetime caveat comment; Test Rules gains a routing bullet noting T-72/T-73 EXTEND the two existing test files rather than adding new ones — the first `Messaging/` Tests-phase task in this domain's history to be an extension rather than a net-new file. 11 new tasks added to `state-map.md` (D-176–D-179, S-45, C-106/C-107, T-72–T-74, DO-36); total task count 432 → 443. `SK.16.Design`/`SK.16.Scaffold` closed `●` same-day (pure design/verification, zero code written); Core/Tests(-partial)/Docs remain future-session implementation work.
- [2026-08-04] `SK.16.Core` closed for WO-054 (C-106/C-107 → `●`, 107/107, promoted to root): implemented D-176–D-178 on `Messaging/InMemoryMessageBus.cs`/`InMemoryEventPublisher.cs` against the live `07.Messaging.Abstractions` source (`PublishContext.cs`, `IMessageHeaderPropagator.cs`) and the real `MassTransitMessageBus.BuildContextFromPropagators` helper — confirmed its `MessagingPublishContext` alias IS `SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext` itself, not a distinct type. Both fakes gained an optional `IEnumerable<IMessageHeaderPropagator>? propagators = null` constructor parameter, materialized once via `.ToArray()` (a deliberate deviation from the design text's literal `?? []` — avoids re-enumerating a possibly-lazy/side-effecting sequence on every dispatch call, behaviorally identical for the common DI-resolved case). `InMemoryMessageBus._recorded` extended to a 4-tuple `(Type, object, bool, PublishContext)`; new `_requestContexts` queue `(Type RequestType, Type ResponseType, PublishContext Context)` populated by `RequestAsync` BEFORE the handler-existence check — a second implementation-time decision not explicit in the design text, so `ShouldHaveRequestedContext` can prove a request was attempted even when the call itself throws for a missing handler. `InMemoryEventPublisher._published` extended from `ConcurrentQueue<object>` to `ConcurrentQueue<(object Event, PublishContext Context)>`, with `.Published`/`.PublishedOf<TEvent>()` re-derived via `.Select(p => p.Event)` — exact pre-existing return types/ordering unchanged. New accessors implemented exactly as designed: `ShouldHavePublishedContext<T>()`/`ShouldHaveSentContext<T>()`/`ShouldHaveRequestedContext<TRequest,TResponse>()` on `InMemoryMessageBus`, `ShouldHavePublishedContext<TEvent>()` on `InMemoryEventPublisher` — each throws `InvalidOperationException` naming the missing type(s), mirroring `FindFirst<T>`'s existing message shape. Zero changes needed to `MessagingServiceCollectionExtensions.cs` — confirmed both `Add*` methods already use implicit-constructor-activation singleton registration, which honors the new optional parameter automatically. `[STATUS: Planned — P-352/WO-054]` markers removed from both constructors and all four new accessor methods; T-74's `[STATUS: Blocked — pending 07.Messaging P-340/P-344]` marker left untouched (still genuinely blocked — a Tests-phase concern, unaffected by this Core-phase pass). Folder/Namespace Map `Messaging/` paragraph updated from "Design/Scaffold-phase locked" to "Core-phase implemented." `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, 35 pre-existing warnings only (NU1903/CS1574/CS1734/CS8509), none introduced. Consuming-domain regression check: the pre-existing `SharedKernel.Testing.SelfTests/Messaging/InMemoryMessageBusTests.cs`/`InMemoryEventPublisherTests.cs` (written against the pre-P-352 shape) pass unchanged, 24/24; full suite `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` passes 870/870, zero regressions — confirming every pre-existing public member's signature/behavior is genuinely unchanged. `SK.16.Tests` (T-72–T-74, one `⚑` Blocked) and `SK.16.Docs` (DO-36) remain `○`/`⚑` Pending, future-session work (testing-phase-implementer).
- [2026-08-04] `SK.16.Tests` for WO-054 (T-72/T-73 → `●`, 73/74 — `SK.16.Tests` promoted to `⚑`, not `●`, since T-74 remains blocked): extended `Messaging/InMemoryMessageBusTests.cs` (13 pre-existing tests untouched) with 12 new tests and `Messaging/InMemoryEventPublisherTests.cs` (9 pre-existing tests untouched) with 7 new tests — full coverage detail in the Test Rules bullet above. Re-verified T-74's blocker directly on disk before starting: `07.Messaging.Abstractions/EventPublisher/PublishContext.cs` still declares only `CorrelationId`/`CausationId`/`Headers` — the blocker has not cleared, T-74 correctly left `⚑` Blocked, not implemented. `dotnet build` on both `SharedKernel.Testing.csproj`/`SharedKernel.Testing.SelfTests.csproj` succeeds 0 errors (35/4 pre-existing warnings respectively, none introduced). Full regression `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` (real Docker daemon, incl. `Containers/`) passes 889/889 (870 pre-existing + 19 net new), zero regressions. No consuming-domain regression check applies — `InMemoryMessageBus`/`InMemoryEventPublisher` have no external `.Tests`-project consumer of this new surface yet. Only DO-36 (Docs) and the blocked T-74 remain to fully close WO-054's `16.Testing` contribution (testing-phase-implementer).
- [2026-08-07] `SK.16.Tests` T-74 blocker re-verified and resolved, T-74 implemented (→ `●`, 74/74, promoted to root) — closes WO-054/P-352's entire `16.Testing` contribution and, since every other phase was already `●`, the whole `16.Testing` domain end to end. Per this domain's own standing blocker-clearance discipline, re-read the live `07.Messaging.Abstractions/EventPublisher/PublishContext.cs` directly on disk before writing any test code rather than trusting the dispatching brief's own re-verification claim on faith — confirmed all four required members are real, shipped code: `TenantId` (`Guid?`, line 43), `PartitionKey` (`string?`, line 56), `WithTenantId(Guid)` (line 91), `WithPartitionKey(string)` (line 105); `07.Messaging/CLAUDE.md`'s own changelog corroborates P-340 shipped 2026-08-05 and P-344 shipped 2026-08-06. Extended the two EXISTING test files additively, per P-352's own "purely additive" acceptance criterion — no `SharedKernel.Testing` production code change was needed, confirming D-176's capture-by-reference design worked exactly as intended: the `ShouldHave*Context` accessors already returned a reference to the real `PublishContext` instance, so the newly-shipped `TenantId`/`PartitionKey` members became visible automatically with zero further code. `InMemoryMessageBusTests.cs` gained two new private test-double propagators (`TenantPropagator`, shaped like the real `07.Messaging` `TenantHeaderPropagator`, populating only `TenantId`; `PartitionKeyPropagator`, populating only `PartitionKey`) and 12 new tests covering TenantId/PartitionKey round-trip across all four dispatch shapes, explicit-callback-wins-over-propagator precedence for each field, a combined no-propagators/explicit-configure-alone case, and a default-null-state sanity check; all 25 pre-existing tests (13 original + 12 from T-72) untouched. `InMemoryEventPublisherTests.cs` gained the same two propagator doubles and 7 new tests mirroring the same coverage shape for the event publisher's two `PublishAsync` overloads; all 16 pre-existing tests (9 original + 7 from T-73) untouched. `dotnet build` on both `SharedKernel.Testing.csproj`/`SharedKernel.Testing.SelfTests.csproj` succeeds 0 errors (pre-existing NU1903/CS1574/CS8509 advisory warnings only, none introduced — no production `.cs` file was touched). Full regression `dotnet test SharedKernel.Testing.SelfTests.csproj --configuration Release` (real Docker daemon, incl. `Containers/`) passes **908/908** (889 pre-existing + 19 net new: 12 + 7), zero regressions. `[STATUS: Blocked — pending 07.Messaging P-340/P-344]` marker removed from the `Messaging/` Interface Contracts block; the stale "not yet shipped as of this writing"/"T-74 remains ⚑ Blocked" forward-looking language in the reference-capture NOTE, the Folder/Namespace Map `Messaging/` narrative paragraph, and the Test Rules `Messaging/` routing bullet all corrected to past tense with the resolution recorded. `SK.16.Tests` is now `●` (74/74) — phase promoted to root. All six phases of `16.Testing` (Design/Scaffold/Core/Tests/Docs/Published) are `●` again (testing-phase-implementer).
- [2026-08-10] P-355/WO-055 processed (testing-arch-planner): arch-lead's own dispatch note flagged that this phase's body was written against a premise `09.Search`'s own planner had already found stale — that per-document bulk-write outcome reporting was a NEW P-354 capability. Independently re-verified against the real, live `09.Search` source before writing any task (not trusted from the dispatch note alone): `SharedKernel.Search.Abstractions/Models/SearchBulkReceipt.cs`/`SearchItemFailure.cs` already ship `.Failures`/`.SucceededCount`/`.HasFailures`, and `16.Testing`'s own already-shipped `Search/InMemorySearchIndex<TDocument>.IndexManyAsync`/`DeleteManyAsync` (P-276/WO-044) already populate them faithfully — confirmed by reading the existing `IndexManyAsync_PartialInvalidIds_ReturnsSuccess_WithPerItemFailures` test directly, which already proves the exact per-document-failure scenario the phase input asked for. `09.Search`'s own `state-map.md` independently corroborates via its D-31 ratification ("per-document bulk-write outcome reporting already exists and is shipped — no code change from this task"). Scope narrowed accordingly (D-180): this phase's real, actionable surface is `SearchBulkWriteOptions` (a new sealed record carrying `.MaxBatchesPerSecond : double?`/`.Default`) plus two additive 4-arg `IndexManyAsync`/`DeleteManyAsync` overloads on `ISearchIndex<TDocument>` — `09.Search`'s own D-32/D-33, sourced directly from that domain's live Design-phase state-map prose since neither type is compiled code yet. Re-verified directly on disk (not `09.Search/CLAUDE.md` prose): `Abstractions/ISearchIndex.cs` declares only the pre-existing 3-arg overloads; no `Models/SearchBulkWriteOptions.cs` file exists; `09.Search`'s own P-354 Scaffold (S-14)/Core (C-51–C-55) are `○` Pending. This is the EIGHTH design-ahead-of-schedule occurrence in this domain's history (after P-269/P-276/P-284/P-288/P-300/P-335/P-352) and the SECOND (after `Messaging/`'s own P-352/WO-054) to hit a MEMBER-level gap in an already-`**implemented**` folder rather than a whole empty placeholder package — `Search/` (P-276/WO-044) is fully shipped and Published; only the two new overloads and their carrier type are missing. Design locks the fake's throttle treatment as a documented no-op (D-182): `SearchBulkWriteOptions.MaxBatchesPerSecond` is accepted but produces no real `Task.Delay`, mirroring this fake's own established "no real latency" precedent for `WaitUntilSearchableAsync`; to avoid silently discarding the parameter, a new `LastBulkWriteOptions : SearchBulkWriteOptions?` audit property records the most recently supplied options on every bulk call (via the 3-arg overloads' own `.Default` delegation too, so it is never left unpopulated). No re-validation is added — `SearchBulkWriteOptions`'s own constructor already guards the non-positive-value case (`09.Search`'s C-51), so a value reaching this fake is already valid. 12 new tasks added to `state-map.md` (D-180–D-183, S-46, C-108–C-110, T-75–T-77, DO-37); total task count 443 → 455. `SK.16.Design`/`SK.16.Scaffold` (D-180–D-183/S-46, 5/5) closed `●` same day — pure design/verification work needing no compiled upstream type; C-108–C-110 (Core)/T-75–T-77 (Tests)/DO-37 (Docs) — 7 tasks — marked `⚑` Blocked pending `09.Search`'s own `SK.09.Core` (C-51/C-52). `16.Testing/CLAUDE.md` refreshed in the same pass: `Search/` Folder/Namespace Map row and Folder Map narrative paragraph both gain a P-355 forward-reference; `Search/` Interface Contracts block gains the two new 4-arg overload signatures and `.LastBulkWriteOptions`, both marked `[STATUS: Blocked — pending 09.Search P-354]`, plus a new NOTE (BULK-WRITE THROTTLE) block explaining the no-op-throttle/audit-property/no-re-validation design and explicitly cross-referencing the pre-existing NOTE (WRITE PATH) block to record that outcome reporting itself is unchanged; Test Rules gains a routing bullet noting T-75/T-76 EXTEND the existing `InMemorySearchIndexTests.cs` (mirroring `Messaging/`'s own P-352 "extend, don't replace" precedent, the second occurrence) and explicitly scoping the parity proof as intra-package only, since `09.Search`'s own T-32–T-34 own the real-provider-parity proof this package cannot reproduce (no `ProjectReference` to `.Meilisearch`/`.ElasticSearch`).
- [2026-08-11] `SK.16.Core` closed for WO-055 (C-108–C-110 → `●`, 110/110, promoted to root): re-verified the `09.Search` blocker directly on disk before writing any code, per this domain's own standing blocker-clearance discipline (never trust another domain's own prose alone) — `09.Search/SharedKernel.Search.Abstractions/Models/SearchBulkWriteOptions.cs` exists as real, compiled code and `Abstractions/ISearchIndex.cs` declares both new 4-arg `IndexManyAsync`/`DeleteManyAsync` overloads as abstract interface members, matching D-181's target shape with zero drift; both the root `CLAUDE.md` and `09.Search/CLAUDE.md` changelogs confirm P-353/P-354 shipped in `09.Search` on 2026-08-10 (`SK.09.Core`, C-49–C-55). Implemented `Search/InMemorySearchIndex.cs`: both 4-arg overloads added with identical body logic to the pre-existing 3-arg implementations, each recording `bulkOptions` into the new `LastBulkWriteOptions` audit property (before performing the write, so a `SimulateFailure` call still records what was received) and each guarded by `ArgumentNullException.ThrowIfNull(bulkOptions)`; the existing 3-arg overloads refactored into one-line expression-bodied delegations to the 4-arg siblings passing `SearchBulkWriteOptions.Default`; `Reset()` extended to clear `LastBulkWriteOptions`. No real `Task.Delay`/pacing applied, per D-182's locked design. `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, 35 pre-existing warnings only (NU1903/CS1574/CS8509), none introduced by the changed file. Consuming-domain regression check: the pre-existing `SharedKernel.Testing.SelfTests` `Search/` suite (85 tests, incl. the Docker-gated `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` tests) re-ran unmodified and green, confirming C-109's "must continue to pass unmodified against the refactored path" criterion; full regression `dotnet test SharedKernel.Testing.SelfTests.csproj --configuration Release` passes 908/908, zero regressions. This clears the transitive inbound compilation failure `09.Search`'s own `SK.09.Tests` T-27–T-34 were blocked on (`SharedKernel.Testing.dll` previously failed to build with two `CS0535` errors since `InMemorySearchIndex<TDocument>` no longer satisfied its own `ISearchIndex<TDocument>` interface) — `09.Search` is now unblocked to resume that phase, though acting on it remains that domain's own future session's work. `[STATUS: Blocked — pending 09.Search P-354]` markers removed from both 4-arg overloads and `.LastBulkWriteOptions` in the `Search/` Interface Contracts block; the NOTE (BULK-WRITE THROTTLE) block, the Folder/Namespace Map `Search/` row and narrative paragraph, and the Test Rules `Search/` routing bullet all corrected to past tense with the resolution recorded. T-75–T-77 (`SK.16.Tests`) and DO-37 (`SK.16.Docs`) corrected `⚑`→`○` in `state-map.md` — genuinely unblocked but not implemented in this Core-phase pass, per this session's own explicit scope instruction; they remain future `SK.16.Tests`/`SK.16.Docs` session work (testing-phase-implementer).
- [2026-08-11] `SK.16.Tests` closed for WO-055 (T-75–T-77 → `●`, 77/77, promoted to root): extended `SharedKernel.Testing.SelfTests/Search/InMemorySearchIndexTests.cs` additively (every pre-existing test unmodified) with 8 new tests — 2 intra-package 3-arg-vs-4-arg parity tests (T-75) re-running the existing `IndexManyAsync_PartialInvalidIds_ReturnsSuccess_WithPerItemFailures`/`DeleteManyAsync_MixedPresence_CountsEveryRequestedIdAsSucceeded` scenarios through the new `SearchBulkWriteOptions.Default`-carrying 4-arg path on a separate fresh fake instance, asserting `SucceededCount`/`HasFailures`/`Failures` (via `SequenceEqual`, since `SearchBulkReceipt`'s record-generated `Equals` does not deep-compare its `IReadOnlyList<SearchItemFailure>` field) and the full `SearchWriteReceipt` are identical between the two paths; 6 `LastBulkWriteOptions` audit-surface tests (T-76) proving `Assert.Same`-provable caller-supplied-throttle capture on both 4-arg overloads, reversion to `SearchBulkWriteOptions.Default` on a subsequent 3-arg call, the property being shared across `IndexManyAsync`/`DeleteManyAsync` rather than tracked independently, and a `Stopwatch`-timed 50-document bulk write under `MaxBatchesPerSecond = 1` completing in well under one second (no real pacing applied). `dotnet build` clean on both `SharedKernel.Testing.csproj`/`SharedKernel.Testing.SelfTests.csproj` (0 errors, only the pre-existing `NU1903` advisory warning). Full regression `dotnet test SharedKernel.Testing.SelfTests.csproj --configuration Release` (real Docker daemon, incl. `Containers/`) passes 916/916 (908 pre-existing + 8 net new), zero regressions (T-77). No consuming-domain regression check applies — `16.Testing` takes no `ProjectReference` to `SharedKernel.Search.Meilisearch`/`.ElasticSearch`, and no other domain's `.Tests` project consumes `InMemorySearchIndex<TDocument>` yet. Folder/Namespace Map `Search/` row, the narrative paragraph, and the Test Rules `Search/` routing bullet all updated to record the Tests-phase closure. Only DO-37 (`SK.16.Docs`) remains to fully close WO-055/P-355's `16.Testing` contribution (testing-phase-implementer).
- [2026-08-11] `SK.16.Docs` closed for WO-055 (DO-37 → `●`, 37/37, promoted to root) — **this closes WO-055/P-355 in full: all six phases of `16.Testing` (Design/Scaffold/Core/Tests/Docs/Published) are `●` again.** Re-verified rather than trusted the DO-37 task row's own carried-forward note (which claimed the `CLAUDE.md` marker removal/changelog entry "remains a future `SK.16.Docs` session's own task") against the live file: the `[STATUS: Blocked — pending 09.Search P-354]` marker was in fact ALREADY removed from the `Search/` Interface Contracts block during the `SK.16.Core` pass (2026-08-11) — grep-confirmed zero live occurrences of that marker text anywhere outside historical changelog narrative, both 4-arg overload signatures and `.LastBulkWriteOptions` already carry `[implemented P-355/WO-055, ...]` annotations instead. Read `Search/InMemorySearchIndex.cs` directly and confirmed full XML doc coverage on both `IndexManyAsync`/`DeleteManyAsync` 4-arg overloads (`<inheritdoc/>` plus a `<remarks>` documenting the `LastBulkWriteOptions`-recording-before-write behavior and the no-real-pacing rationale) and on `LastBulkWriteOptions` itself (a full `<summary>` covering the null-until-first-bulk-call state, the 3-arg-delegation-also-populates-it behavior, and why no real throttling is applied) — all written at C-108/C-110 implementation time, nothing missing. The only genuine outstanding work this pass performed: corrected the stale forward-looking sentence in the Folder/Namespace Map `Search/` narrative paragraph ("only Docs (DO-37) remains, a future `SK.16.Docs` session's work" → recording DO-37's actual completion) and appended this changelog entry — both mechanical documentation-accuracy fixes, no code change. `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, pre-existing advisory warnings only (`NU1903`/`CS8509`), none introduced. No consuming-domain regression check applies — no other domain's `.Tests` project consumes `InMemorySearchIndex<TDocument>`'s new bulk-write throttle surface yet (testing-phase-implementer).
- [2026-08-13] P-374/WO-057 processed (testing-arch-planner): design pass extending the ALREADY-`●`-implemented `Security/` folder — read `12.Security/CLAUDE.md` in full for the target shape of P-367 (`IUserContext.IdentityKind`, an `Anonymous`/`User`/`ServicePrincipal`/`System` enum) and P-368 (`IUserContext.Permissions`/`.HasPermission(string)`, mirroring `Roles`/`HasRole`), then independently re-verified both against the LIVE `12.Security/SharedKernel.Security.Abstractions/Abstractions/IUserContext.cs` rather than trusting that domain's own ratified-but-unshipped prose: the live interface still declares only the pre-WO-057 seven members — `IdentityKind`/`Permissions`/`HasPermission` are NOT yet compiled code; `12.Security/state-map.md` corroborates via its own D-13/D-14/D-16 (Design) and C-14/C-15/C-17 (Core) rows, all `○` Pending. `ITenantProvider`/`FakeTenantProvider` are confirmed unaffected by WO-057 — that work order's own task list names no `ITenantProvider` change, only `IUserContext` plus the new, out-of-scope-for-this-package `SharedKernel.Security.ApiKey` provider. **Key design decision**: no dedicated `FakeSystemUserContext`/`FakeServicePrincipalUserContext` sentinel type is introduced to mirror `12.Security`'s new immutable `SystemUserContext` singleton — `FakeUserContext` already represents every `IdentityKind` value through its existing settable-property convention, so a test wanting a "system/background execution context" fixture constructs `new FakeUserContext { IdentityKind = IdentityKind.System }` directly. This is the NINTH design-ahead-of-schedule occurrence in this domain's history (after P-269/P-276/P-284/P-288/P-300/P-335/P-352/P-355) and the SECOND member-level-on-an-already-implemented-folder occurrence after `Search/`'s own P-355/WO-055 — the first time this exact sub-flavor has recurred against a different owning domain (`12.Security` rather than `09.Search`). 10 new tasks added to `state-map.md` (D-184–D-187, S-47, C-111–C-113, T-78, DO-38); total task count 455 → 465. `SK.16.Design`/`SK.16.Scaffold` (D-184–D-187/S-47, 5/5) closed `●` same day — pure design/state-verification work needing no compiled upstream type (`SharedKernel.Security.Abstractions` is already referenced since S-04); C-111–C-113 (Core)/T-78 (Tests)/DO-38 (Docs) — 5 tasks — marked `⚑` Blocked pending `12.Security`'s own `SK.12.Core` (C-14/C-17). `16.Testing/CLAUDE.md` refreshed in the same pass: `Security/` Interface Contracts block gains `IdentityKind`/`Permissions`/`HasPermission(string)` signatures plus the "no dedicated system-context sentinel type" design-decision NOTE, both marked `[STATUS: Planned — P-367/P-368/WO-057]`; Test Rules gains a routing bullet confirming `SharedKernel.Testing.SelfTests` as the future proving ground (repo-wide grep: zero external consumers of `FakeUserContext`, unchanged since P-188/WO-030); `state-map.md`'s `## Blocked` table and Cross-Domain Dependencies table both gain a new `12.Security` entry, Overall Progress updated, Package Board row annotated (testing-arch-planner).
- [2026-08-13] `SK.16.Core` closed for WO-057 (C-111–C-113 → `●`, 113/113, promoted to root): the blocker recorded in the entry immediately above had gone stale by the time this Core-phase pass began. Re-read `12.Security/SharedKernel.Security.Abstractions/Abstractions/IUserContext.cs` (plus `IdentityKind.cs`/`AnonymousUserContext.cs`/`SystemUserContext.cs`) directly on disk per this domain's own blocker-clearance discipline (never trust another domain's own prose alone) — `IdentityKind`/`Permissions`/`HasPermission(string)` are now real, compiled interface members, shipped by a concurrent `12.Security` WO-057 Core-phase session running in the same working tree. That same session had, per this domain's own documented cross-domain pattern (a domain shipping a breaking interface change patches this package's fake in-place so the mono-repo keeps compiling), already added all three matching members to `Security/FakeUserContext.cs` — an uncommitted working-tree change. Rather than assume the compile-fix satisfied this phase's specific acceptance shapes, each was independently re-verified: `IdentityKind` is a SETTABLE property defaulting to `IdentityKind.User` (C-111's exact requirement — a minimal compile-fix could have used a hardcoded/read-only shape instead, and did not); `Permissions`/`HasPermission(string)` use the IDENTICAL `StringComparer.OrdinalIgnoreCase` pattern `HasRole` already uses (C-112's "mirror `HasRole` exactly" requirement); `git diff HEAD` on the file confirms the change is purely additive (25 insertions, 0 deletions) with every pre-existing member (`UserId`/`Email`/`Username`/`Roles`/`Claims`/`IsAuthenticated`/`HasRole`) byte-for-byte unchanged (C-113). `FakeTenantProvider.cs` re-read and confirmed to need — and to have received — zero change. Zero new `.cs` code was written by this session. `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, 27 pre-existing warnings only (`NU1903`/`CS1574`/`CS1734`/`CS8509`), none introduced. No consuming-domain regression check applies — no external `.Tests` project references `FakeUserContext`'s new members yet; the sole existing consumer, `SharedKernel.Testing.SelfTests/Security/FakeUserContextTests.cs`, does not yet exercise them (T-78's future job). `[STATUS: Planned — P-367/P-368/WO-057]` markers removed from the `Security/` Interface Contracts block; the Folder/Namespace Map narrative paragraph and the Test Rules routing bullet both corrected to record Core-phase closure. `state-map.md`'s `## Blocked` table cleared to zero live entries; the `12.Security` Cross-Domain Dependencies row corrected to `Available`; T-78/DO-38 corrected `⚑`→`○` Pending (unblocked, not implemented in this pass); Overall Progress and Package Board rows updated. `SK.16.Core` is now `●` (113/113) — this closes the Core phase in full; T-78 (`SK.16.Tests`) and DO-38 (`SK.16.Docs`) remain the next sessions' work (testing-phase-implementer).

- [2026-08-13] `SK.16.Tests` closed for WO-057 (T-78 -> `●`, 78/78, promoted to root): extended the EXISTING `SharedKernel.Testing.SelfTests/Security/FakeUserContextTests.cs` additively (all 15 pre-existing tests untouched) with 14 new tests proving `FakeUserContext.IdentityKind`/`.Permissions`/`.HasPermission` against the live `12.Security` `IUserContext` contract, mirroring the existing `HasRole`/`Roles` test shape exactly per this task's own acceptance criterion. `FakeTenantProviderTests.cs` needed no change, confirmed unaffected (D-186). `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, 5 pre-existing warnings only, none introduced. Full regression `dotnet test SharedKernel.Testing.SelfTests.csproj --configuration Release` (real Docker daemon, incl. `Containers/`) passes 930/930 (916 pre-existing + 14 net new), zero regressions. This closes WO-057/P-374's entire `16.Testing` contribution except `SK.16.Docs` (DO-38, 37/38), which remains the only outstanding task in the whole domain (testing-phase-implementer).
- [2026-08-13] `SK.16.Docs` closed for WO-057 (DO-38 → `●`, 38/38, promoted to root) — **this closes WO-057/P-374 in full and, since every other phase was already `●`, the entire `16.Testing` domain end to end (Design/Scaffold/Core/Tests/Docs/Published all `●`).** Re-verified rather than trusted the task row's own carried-forward "not implemented in this Core-phase pass" note, per this domain's own established discipline of re-checking a phase brief's blocked/pending claim against the live files rather than the prior pass's own prose: read `Security/FakeUserContext.cs` directly and found `IdentityKind`/`.Permissions`/`.HasPermission` already carry full XML doc coverage — including the exact "no dedicated `FakeSystemUserContext` type" rationale from D-184 — because the `12.Security` Core-phase session that patched this file in-place at C-111–C-113 wrote the documentation alongside the code, not merely a bare compile-fix; no doc edit was needed. Grep-swept `16.Testing/CLAUDE.md` for `STATUS: Planned — P-367/P-368/WO-057` and confirmed zero live occurrences — the marker had already been removed during the `SK.16.Core` pass (2026-08-13), not this one. The only genuine outstanding work this pass performed was appending this changelog entry, plus the `state-map.md` bookkeeping (DO-38 row, Overall Progress, Package Board, and the P-374 Overall-Progress summary paragraph all corrected to record Docs-phase closure). `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, 27 pre-existing warnings only (`NU1903`×2, `CS1574`×3, `CS1734`×1, `CS8509`×5, plus their duplicate second-pass emissions), none introduced — a documentation-only pass, no `.cs` file changed. No consuming-domain regression check applies — no external `.Tests` project references `FakeUserContext`'s new members yet; the sole existing consumer, `SharedKernel.Testing.SelfTests/Security/FakeUserContextTests.cs`, already exercises them fully per T-78, unaffected by this Docs-only pass (testing-phase-implementer).
- [2026-08-13] P-382/WO-058 processed (testing-arch-planner), together with a companion no-`P`-number item flagged by `application-arch-planner` during the same WO-058 dispatch run: 23 new tasks added to `state-map.md` across all five non-Published phases (D-188–D-198, S-48/S-49, C-114–C-118, T-79–T-81, DO-39/DO-40; total task count 465 → 488). Read `16.Testing/CLAUDE.md` in full, then read `12.Security/CLAUDE.md` and `05.Application/CLAUDE.md` in full for the two target contracts this dispatch touches, then independently re-verified both against their LIVE source. **P-382**: `12.Security/SharedKernel.Security.Abstractions/Abstractions/IUserContext.cs` re-read directly — declares only the WO-057 surface (`IdentityKind`/`Permissions`/`HasPermission`, both already shipped); none of P-375's five new members (`AuthenticationMethods`/`AuthContextClassReference`/`AuthTime`/`WasAuthenticatedWith`/`IsAuthenticationFresherThan`) exist yet, and `12.Security/state-map.md` confirms its own WO-058 tasks are still design-only. Extends the ALREADY-`●`-implemented `Security/` folder with a new fluent `SecurityTestContextBuilder` (`.Build() → ClaimsPrincipal`, `.BuildUserContext() → IUserContext`, two independent parallel projections of one shared fluent state, never one derived from the other — mirrors how production's `OidcUserContext` and this package's own `FakeUserContext` are already independent mechanisms) directly answering `12.Security/CLAUDE.md`'s own Test Rules prose about hand-assembled `ClaimsIdentity` construction, plus a further `FakeUserContext` extension tracking P-375. Split cleanly at the member boundary rather than blocking the whole addition: the identity-basics half (`C-114`/`T-79`, needing only members `FakeUserContext` already ships) is genuinely UNBLOCKED; only the AMR/ACR/AuthTime half (`C-115`/`C-116`/`T-80`) is `⚑` Blocked. Deliberately scope-locked OUT: WO-058/P-376's `IsSenderConstrained`/DPoP surface and WO-058/P-377's `SharedKernel.Security.Mtls` package (neither named by this phase's own acceptance criteria, neither even fully design-locked in `12.Security` yet) and a `.WithTenantId(...)` method (never asked for). This is the TENTH design-ahead-of-schedule occurrence in this domain's history and the FOURTH member-level occurrence — the SECOND against `Security/` specifically, the first time the sub-flavor has recurred against the SAME owning domain twice. **The companion item**: evaluated whether to fold `Application/FakeDualApprovalStore` (the local-seam fake `05.Application`'s own P-380/`DualApprovalBehavior` needs, flagged by `application-arch-planner` as out of that domain's jurisdiction, no root `P`-number of its own) into P-382 or record it separately — chose SEPARATE, since the two touch unrelated capability folders (`Security/` vs. `Application/`) and unrelated owning domains, and bundling them would blur two independent cross-domain dependency chains into one entry. Re-verified `SharedKernel.Application.Behaviors` directly on disk: no `DualApproval/` folder, no `IDualApprovalStore.cs`/`IRequiresDualApproval.cs`/`IAuthorizationContextIdentity.cs` file exists; `05.Application/state-map.md` confirms its own WO-058 Design phase (D-72–D-80) is itself `○` 0/9, genuinely blocked on `01.Core` shipping `Error.Forbidden(...)` — a TWO-LAYER-DEEP upstream dependency, the first occurrence of this shape in this domain's history (the ELEVENTH design-ahead-of-schedule occurrence overall). Mirrors `FakeIdempotencyKeyStore`/`FakeIdempotencyResponseStore`'s exact local-seam-fake precedent; `AddFakeDualApprovalStore()` designed as a STANDALONE call, not bundled into `AddFakeApplicationBehaviorServices()`, mirroring `FakeIdempotencyResponseStore`'s own manual-registration precedent since dual-control is opt-in. Deliberately scope-locked OUT: extending `Application/FakeAuthorizationContext` with the new sibling `IAuthorizationContextIdentity` capability (needed for `DualApprovalBehavior`'s self-approval-prevention check, but never named by this phase's own acceptance criteria and itself equally unshipped) — flagged as a natural future follow-up. Design (D-188–D-198) and Scaffold (S-48/S-49) for BOTH items closed `●` same day — pure design/state-verification work needing no compiled upstream type, since `SharedKernel.Security.Abstractions` (S-04) and `SharedKernel.Application.Behaviors` (S-16) are both already referenced; Core/Tests/Docs for the blocked halves recorded `⚑` in `state-map.md`'s `## Blocked` table, with two new Cross-Domain Dependencies rows (`12.Security`, `05.Application`). `Security/` and `Application/` Interface Contracts blocks, both folders' Folder/Namespace Map rows and narrative paragraph, DI Registration section, and Test Rules all updated in this pass; **out of this domain's jurisdiction, flagged not actioned**: updating `12.Security/CLAUDE.md`'s and `05.Application/CLAUDE.md`'s own Test Rules sections to reference the new builder/fake belongs exclusively to `security-arch-planner`/`application-arch-planner`, never edited by this domain agent (testing-arch-planner).
- [2026-08-17] P-391/WO-060 processed (testing-arch-planner) — extends the ALREADY-`●`-implemented `Security/` folder with three new fluent, BCL-only test-fixture builders, dispatched alongside `12.Security`'s own WO-060 hardening review (P-385 DPoP `ath`-binding fix, P-386 corrected mTLS default posture, P-389 API-key rotation comparator) — all three `○` Pending/design-only in `12.Security`'s own state-map as of this dispatch, read directly (D-35/D-36/D-41, C-38/C-39/C-45/C-46, T-29–T-33/T-38/T-39), not from memory. `DpopTestProofBuilder`/`DpopTestProof` constructs a genuinely well-formed, real-ES256-signed RFC 9449 DPoP proof plus a companion access token entirely via BCL `ECDsa`/`System.Text.Json`, with `.WithMismatchedAth()`/`.WithMissingAth()`/`.WithMalformedAth(string)` negative-path seams — `12.Security`'s own `DpopProofValidator` is `internal` and unreachable regardless of any `ProjectReference`, so this builder produces a presentable input fixture for a consuming service's own integration test, never a fake of the validator itself. `MtlsTestCertificateBuilder`/`MtlsTestCertificateAuthority`/`MtlsTestCertificate` constructs ephemeral self-signed/ephemeral-CA-chained/revoked X.509 test certificates via BCL `CertificateRequest`/`X509Certificate2`/`CertificateRevocationListBuilder`, with an explicit documented-simplification note that a consuming test must feed the CRL bytes into its own `X509Chain` manually. `ApiKeyRotationScenarioBuilder`/`ApiKeyRotationScenario` is a pure scenario/data builder that never calls the real `ApiKeyRotationComparer.AnyMatch` itself — a consuming test's own `.ApiKey`-referencing project performs that call directly. **This is the domain's TWELFTH design-ahead-of-schedule-shaped occurrence and the FIRST to carry ZERO cross-domain compile-time blocker of any kind** — all three types are deliberately built entirely on BCL cryptography/X.509 primitives, taking NO `ProjectReference` to `SharedKernel.Security.Oidc`/`.Mtls`/`.ApiKey`, preserving (via a distinct-but-converging rationale, D-203) the `Security/` folder's pre-existing "abstractions-only" scope lock (first stated at P-382/WO-058). 19 new tasks (D-199–D-203, S-50/S-51, C-119–C-122, T-82–T-85, DO-41–DO-44), all in `Security/` — no new capability folder. Design (D-199–D-203) and Scaffold (S-50/S-51) close `●` same day — pure design/state-verification work needing no compiled upstream type and no new `PackageReference`/`ProjectReference`. Core/Tests/Docs recorded `○` Pending, fully actionable — future-session implementation work; none `⚑` Blocked. `Security/` Interface Contracts block, Folder/Namespace Map narrative paragraph, Implementation Rules, and Test Rules all updated in this pass (testing-arch-planner).
- [2026-08-17] `SK.16.Core` closed for the combined remainder of WO-058 (P-382 + companion) and WO-060 (P-391) — C-114–C-122 → `●`, 122/122, promoted to root. Both recorded `⚑` blockers were re-verified directly on disk before writing anything and found STALE: `12.Security/SharedKernel.Security.Abstractions/Abstractions/IUserContext.cs` already declared the full WO-058 AMR/ACR/AuthTime surface, and `Security/FakeUserContext.cs` was found already carrying all five matching members (committed prior to this session, git commit `3c9b5ab`) — each verified against its exact acceptance shape rather than assumed satisfied because the package still compiled; `05.Application/SharedKernel.Application.Behaviors/DualApproval/IDualApprovalStore.cs` was likewise found already shipped, with its own upstream `01.Core` blocker (`Error.Forbidden(...)`, P-384/WO-059) also confirmed shipped. Implemented: `Security/SecurityTestContextBuilder.cs` (net-new — C-114's identity-basics surface plus C-116's AMR/ACR/AuthTime fluent methods, written together since both were unblocked in this pass; `.Build()` assembles a `ClaimsPrincipal` via private claim-type constants mirroring `12.Security.Oidc`'s `ClaimMappingOptions` defaults, `.BuildUserContext()` independently projects the same fluent state onto a `FakeUserContext`); `Application/FakeDualApprovalStore.cs` (net-new, C-117) plus a new standalone `AddFakeDualApprovalStore()` on `ApplicationServiceCollectionExtensions.cs` (C-118); `Security/DpopTestProofBuilder.cs` (net-new, C-119 — real ES256-signed RFC 9449 DPoP proof construction, hand-rolled base64url, zero JWT library); `Security/MtlsTestCertificateBuilder.cs` (net-new, C-120, `MtlsTestCertificateAuthority` as a `private sealed` nested class); `Security/ApiKeyRotationScenarioBuilder.cs` (net-new, C-121) — all three C-119–C-121 types built entirely on BCL `System.Security.Cryptography`/`System.Security.Cryptography.X509Certificates`, zero `ProjectReference` to `SharedKernel.Security.Oidc`/`.Mtls`/`.ApiKey`. A throwaway smoke-test console program (built, run against every new type including independent ES256-signature re-verification and real `X509Chain` validation, then deleted) surfaced and drove the fix for one genuine runtime defect: `CertificateRevocationListBuilder`'s real overload set has no single RSA-or-ECDsa-agnostic `Build` call — the ECDsa-capable overload's `X509AuthorityKeyIdentifierExtension.CreateFromCertificate(includeKeyIdentifier: true)` throws unless the CA certificate carries a Subject Key Identifier extension, so `MtlsTestCertificateAuthority.CreateEphemeral` now adds one before self-signing (see the `MtlsTestCertificateBuilder` NOTE for the full API-surface writeup). C-122's regression check confirmed `FakeUserContext.cs`/`FakeTenantProvider.cs` untouched by this pass and `SecurityTestContextBuilder.cs` unmodified since its own creation earlier in the same phase. `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors, 0 new warnings (verified via a `--no-incremental` full rebuild). No consuming-domain regression check applies to five of the six new/changed types — no external `.Tests` project references them yet (future `SK.16.Tests` work, T-79–T-85); `AddFakeDualApprovalStore()` was proven registering/resolving against a real `ServiceProvider` in the same smoke test. Full `dotnet test SharedKernel.Testing.SelfTests.csproj --configuration Release` (real Docker daemon, incl. `Containers/`) re-run for regression safety: passes **930/930**, unchanged from before this pass, zero regressions. `[STATUS: Blocked]`/`[STATUS: Planned — P-391/WO-060]` markers removed from the `Security/`/`Application/` Folder/Namespace Map rows, the `FakeUserContext`/`SecurityTestContextBuilder`/`FakeDualApprovalStore`/`AddFakeDualApprovalStore()` Interface Contracts blocks (incl. the DI Registration code sample), and the Folder/Namespace Map narrative paragraphs for both phases; Test Rules routing bullets updated to record implemented status; `MtlsTestCertificateBuilder`'s NOTE gained the real `CertificateRevocationListBuilder` API-surface correction writeup. `state-map.md`'s `## Blocked` table cleared to zero live entries; both `12.Security`/`05.Application` Cross-Domain Dependencies rows corrected to `Available`; T-80/T-81 corrected `⚑`→`○` Pending; Overall Progress and Package Board updated (testing-phase-implementer).
- [2026-08-17] `SK.16.Tests` closed for the combined remainder of WO-058 (P-382 + companion) and WO-060 (P-391) — T-79–T-85 → `●`, 85/85, promoted to root: `Security/SecurityTestContextBuilderTests.cs` (23 tests), `Application/FakeDualApprovalStoreTests.cs` (11 tests), `Security/DpopTestProofBuilderTests.cs` (13 tests), `Security/MtlsTestCertificateBuilderTests.cs` (10 tests), `Security/ApiKeyRotationScenarioBuilderTests.cs` (9 tests, intra-package half only — the `ApiKeyRotationComparer` interop half remains deferred, re-confirmed absent from `12.Security/SharedKernel.Security.ApiKey/`, root P-389 still `◐` Dispatched). Both crypto-fixture builders proven via throwaway smoke-test console apps first; the `MtlsTestCertificateBuilder` CRL test surfaced a genuine `CertificateRevocationListBuilder.AddEntry` DER sign-padding quirk, worked around in the test's own parsing helper. Full regression 996/996 (930 pre-existing + 66 net new), zero regressions (testing-phase-implementer).
- [2026-08-17] `SK.16.Docs` closed for the combined remainder of WO-058 (P-382 + companion) and WO-060 (P-391) — DO-39–DO-44 → `●`, 44/44, promoted to root: all six code-level XML doc requirements (`SecurityTestContextBuilder`, `FakeUserContext`'s AMR/ACR/AuthTime members, `FakeDualApprovalStore`/`AddFakeDualApprovalStore()`, `DpopTestProofBuilder`/`DpopTestProof`, `MtlsTestCertificateBuilder`/`MtlsTestCertificateAuthority`/`MtlsTestCertificate`, `ApiKeyRotationScenarioBuilder`/`ApiKeyRotationScenario`) verified already fully satisfied on direct re-read, member by member, matching this domain's own repeatedly-confirmed "Core-phase implementer writes the docs alongside the code" pattern — no code changes needed. The `[STATUS: Planned]`/`[STATUS: Blocked]` marker sweep (DO-44) found zero live markers remaining anywhere in the `Security/` block. The one genuine gap: the Test Rules section's three P-391/P-382/companion routing bullets were still future-tense ("will be proven") despite T-79–T-85 having shipped 66 tests the same day — rewritten to past tense with the actual shipped file/test-count detail, and one inaccurate forward-looking claim corrected (the original `ApiKeyRotationScenarioBuilder` bullet asserted its proof references the real `ApiKeyRotationComparer` "for a genuine end-to-end interop proof" — the shipped test intra-package-only-with-deferral shape contradicts that; corrected to match). Two out-of-jurisdiction acceptance-criteria items (12.Security's own Test Rules section for DO-39/P-382; 05.Application's own Test Rules section for DO-40/the companion item) remain genuinely un-actioned, flagged for `security-arch-planner`/`application-arch-planner`, never edited here. `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj --configuration Release` succeeds, 0 errors (documentation-only pass, no `.cs` file changed). This closes `SK.16.Docs` in full and, since every other `SK.16.*` phase key was already `●`, **all six phases of `16.Testing` (Design/Scaffold/Core/Tests/Docs/Published) are `●` again.** Root Phase Backlog `P-382`/`P-391` deliberately NOT closed by this pass — `P-382` carries a genuinely unmet, out-of-jurisdiction `12.Security/CLAUDE.md` Test Rules criterion; `P-391`'s T-84 acceptance criterion is only partially met (the end-to-end `ApiKeyRotationComparer` interop half is deferred pending root `P-389`) — flagged explicitly for review by an agent with root `state-map.md` write scope rather than closed by hand (testing-phase-implementer).
- [2026-08-21] P-431/WO-064 processed (testing-arch-planner): a brand-new capability folder, `Integration/`, the first ever mapped to `15.Integration` — `InMemoryWebhookDispatcher` (`IWebhookDispatcher`), `InMemoryWebhookDeliveryObserver` (`IWebhookDeliveryObserver`), plus `AddInMemoryWebhookDispatcher()`/`AddInMemoryWebhookDeliveryObserver()`. Read `16.Testing/CLAUDE.md` in full, then `15.Integration/CLAUDE.md`/`state-map.md` for context, and independently re-verified the exact target contract against the LIVE `.cs` source (`IWebhookDispatcher.cs`, `IWebhookDeliveryObserver.cs`, `WebhookSubscription.cs`, `WebhookDeliveryResult.cs`, `WebhookDeliveryOptions.cs`, `WebhookDispatcher.cs`, `ServiceCollectionExtensions.cs`) rather than trusting prose alone — `15.Integration/SharedKernel.Integration.Webhooks` is long past its own Core phase with a fully passing test suite, so this phase carries **zero cross-domain compile-time blocker of any kind**, the THIRTEENTH design-ahead-of-schedule-shaped occurrence in this domain's history and the first check against `15.Integration` to find no blocker at all (mirroring P-335/WO-053's precedent of an already-mature owning domain). `InMemoryWebhookDispatcher.DispatchAsync<TEvent>` records the event and returns a caller-configurable result list (default: empty, the honest "no matching subscriptions" shape); `.DispatchToSubscriptionAsync<TEvent>` records the `(subscriptionId, event)` pair and returns a caller-configurable result (default: synthetic 2xx success) — a deliberate divergence from `InMemoryMessageBus.RequestAsync`'s "throw when unconfigured" shape, since `IWebhookDispatcher`'s own contract states delivery failure is always a returned record, never a thrown exception. `InMemoryWebhookDeliveryObserver` records every `OnAttemptAsync`/`OnCompletedAsync` call and never throws, modeling production's "observer exceptions never fault the pipeline" contract by construction. Both new `Add*` DI extensions register SINGLETONS, mirroring `AddInMemoryMessageBus()`/`AddInMemoryEventPublisher()`'s established deviation from the interfaces' production scoped lifetime. Tests-phase routing (T-86) follows the "actual consumer today, not theoretical" rule: `15.Integration/SharedKernel.Integration.Webhooks.Tests.csproj` already carries a live `ProjectReference` to `SharedKernel.Testing`, but zero `.cs` file there references either new type yet, so first-proof routes to `SharedKernel.Testing.SelfTests`, with adoption into `15.Integration`'s own suite flagged as a future cross-domain follow-up. Folder/Namespace Map (new `Integration/` row + narrative paragraph), a new `Integration/` Interface Contracts block (`[STATUS: Planned — P-431/WO-064]`), two new implementation-rule bullets (the opposite-default-unconfigured-outcome rule, the never-throws observer rule), the sibling-folder-isolation list, the DI Registration code sample + prose, and a new Test Rules routing bullet all updated in this pass. Overall Progress table updated (Design 208 total/203 done/5 pending `◐`, Scaffold 52 total/51 done/1 pending `◐`, Core 125 total/122 done/3 pending `◐`, Tests 86 total/85 done/1 pending `◐`, Docs 45 total/44 done/1 pending `◐`, Published unchanged 2/2 `●`; total tasks 507→518). Package Board gains a new WO-064 paragraph (testing-arch-planner).
- [2026-08-21] P-431/WO-064 (`Integration/`) implemented end to end by testing-phase-implementer, all five phase keys D-204–D-208/S-52/C-123–C-125/T-86/DO-45 in one session. **Found and corrected a real design-phase drift before writing any code**: `15.Integration`'s own WO-064 hardening pass (`SK.15.WO064`, P-421–P-429) shipped AFTER the original D-204 design was drafted, adding a third `IWebhookDispatcher` member (`SendTestDeliveryAsync`, P-429) and changing `WebhookDeliveryResult`'s positional constructor from 5 to 6 args (`DeliveryId` inserted second, P-423) — both would have been hard compile errors against the original design text. Re-verified directly against the live `.cs` source (`IWebhookDispatcher.cs`, `WebhookDeliveryResult.cs`, `WebhookSubscription.cs`) before implementing, corrected the design in place (recorded as a dedicated "DESIGN-PHASE DRIFT CORRECTION" blockquote in `16.Testing/state-map.md` after D-208, and reflected in this file's `Integration/` Interface Contracts block and Folder/Namespace Map narrative), then implemented against the corrected shape: `SendTestDeliveryAsync` tracked independently (`TestDeliveries` list, `SetTestDeliveryResult`, `ShouldHaveSentTestDelivery`), deliberately NOT delegating internally through `DispatchToSubscriptionAsync` the way production does (a fake proves each member's own contract, not production's call graph). Also re-checked `WebhookSubscription` for further WO-064 drift (found `Secrets`/`Headers` additions, P-425/P-426) — no code-shape consequence since neither fake constructs or transforms a `WebhookSubscription`. Implemented `Integration/InMemoryWebhookDispatcher.cs`, `Integration/InMemoryWebhookDeliveryObserver.cs`, `Integration/IntegrationServiceCollectionExtensions.cs`; added the `SharedKernel.Integration.Webhooks` `ProjectReference` to `SharedKernel.Testing.csproj` (S-52). `dotnet build SharedKernel.Testing.csproj -c Release` succeeds 0 errors, 9 pre-existing warnings only. 30 new tests in `SharedKernel.Testing.SelfTests/Integration/` (T-86) — full-suite regression `dotnet test SharedKernel.Testing.SelfTests.csproj -c Release` passes 997/997 (26 pre-existing `Containers/` failures are Docker-daemon-unavailable in this environment, unrelated). `15.Integration/SharedKernel.Integration.Webhooks.Tests.csproj` (transitively consuming `SharedKernel.Testing`) rebuilt clean, 0 errors — confirms no compile-time regression in the one consuming project, though no `.cs` file there references either new type yet, so first behavioral proof stays in `SelfTests` per this domain's established routing rule. `[STATUS: Planned — P-431/WO-064]` marker removed from the `Integration/` Interface Contracts block heading. State-map: all 11 tasks (D-204–D-208/S-52/C-123–C-125/T-86/DO-45) marked `●`; since every other `SK.16.*` phase key was already `●`, **all six phases of `16.Testing` (Design/Scaffold/Core/Tests/Docs/Published) are `●` again.** Root Phase Backlog `P-431` left as-is for a future root-scoped pass, per this domain's established precedent (testing-phase-implementer).
- [2026-08-24] P-438/WO-065 processed (testing-arch-planner): design pass extending the ALREADY-implemented `Caching/` folder with `FakeTenantCacheService`/`AddFakeTenantCacheService()`, dispatched together with `02.Caching`'s own Phase 42 (P-433, opt-in AES-GCM cache-value encryption at rest) and Phase 44 (P-435, `ITenantCacheService`) — both "planned" (not yet implemented) per `02.Caching/state-map.md` as of this dispatch. Read `16.Testing/CLAUDE.md` in full, then `02.Caching/CLAUDE.md` for the exact planned `ITenantCacheService`/`AddCacheEncryption()` target shapes, then independently re-verified both against the LIVE `.cs` source before writing any task: `02.Caching/SharedKernel.Caching.Abstractions` has no `ITenantCacheService.cs` file, and `02.Caching/SharedKernel.Caching.FusionCache` has no `CacheEncryptionSerializer.cs`/`CacheEncryptionCachingBuilderExtensions.cs` file. This is the FOURTEENTH design-ahead-of-schedule-shaped occurrence in this domain's history and the first HARD blocker since P-431/WO-064 found none — `ITenantCacheService` is an entirely new interface, so `FakeTenantCacheService` cannot compile against it yet; C-126/C-127/T-87 are marked BLOCKED pending `02.Caching`'s own `SK.02.TenantCacheService` (Phase 44/P-435). **Key audit finding**: this phase's second requirement — a fake encryption seam letting a test compose `AddCacheEncryption()` with zero real AES-GCM key material — was found, per this domain's own "audit existing surface before assuming a gap" convention, to be ALREADY fully satisfied by existing, shipped code: `Cryptography/FakeSymmetricEncryptionService` (`ISymmetricEncryptionService`, C-89/SK.16.Core, P-300/WO-049) already is a deterministic, zero-real-key-material stand-in, and `AddCacheEncryption()`'s own documented prerequisite is merely "an `ISymmetricEncryptionService` is registered" — regardless of implementation. Zero new Core-phase code is designed for this half; only a documentation cross-reference (D-211, DO-46) and one interop-proof test (T-88, itself BLOCKED pending `02.Caching`'s Phase 42/P-433 shipping the real `AddCacheEncryption()` extension to compose against) are added. `FakeTenantCacheService`'s design structurally guarantees cross-tenant read/write isolation via a composite `(TenantId, Entity, Id)` backing-store key, and additionally mirrors `02.Caching`'s own documented cross-tenant TAG-INVALIDATION-vector fix (tags tracked per `(TenantId, Tag)`, never a bare global tag) — proving the stronger property `02.Caching`'s own Phase 44 design work calls "strictly worse than a read leak" if left unguarded. Deliberately an INDEPENDENT fake with its own backing store, continuing this folder's established "fakes share no internal state with each other" convention (no composition of `Caching/FakeCacheService`/`FakeTenantCacheKeyProvider`). `AddFakeTenantCacheService()` designed as a STANDALONE call, not bundled into `AddFakeCachingServices()`, mirroring `AddFakeTypedHashStore<T>()`/`AddFakeCacheWarmupStrategy()`'s existing standalone-registration precedent and the real (planned) `AddTenantCacheService()`'s own documented "additive to `AddTenantCacheKeyProvider()`" relationship. 10 new tasks added to `state-map.md` (D-209–D-212, S-53, C-126/C-127, T-87/T-88, DO-46); total task count 518 → 528. `SK.16.Design`/`SK.16.Scaffold` (5 tasks) are Pending and fully actionable today — pure design/documentation work needing no compiled upstream type (`SharedKernel.Caching.Abstractions` is already referenced). `Caching/` Interface Contracts block, DI Registration section, Implementation Rules, Test Rules, and Folder/Namespace Map (row + narrative paragraph) all updated in this pass; `state-map.md`'s `## Blocked` table, Cross-Domain Dependencies table (two new `02.Caching` rows), Package Board, and Overall Progress all updated (testing-arch-planner).
- [2026-08-24] `SK.16.Scaffold` (S-53) and `SK.16.Docs` (DO-46) closed for P-438/WO-065 by testing-phase-implementer. S-53 (state-verification only): re-verified `16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj` directly on disk a second time — the `SharedKernel.Caching.Abstractions` `ProjectReference` is already present; no edit made, none required; `dotnet build` clean, 0 errors. DO-46 (documentation-only): audited this file directly before writing anything and found every sub-requirement already present, written by `testing-arch-planner` at the same Design-phase dispatch pass that added D-209–D-212 — the `Caching/` Interface Contracts block already carries `FakeTenantCacheService`/`AddFakeTenantCacheService()` marked `[STATUS: Planned — P-435/WO-065]`, the D-211 audit-finding blockquote already records the `services.AddFakeCryptography()`-before-`.AddCacheEncryption()` composition recipe as a documentation-only cross-reference, the `## DI Registration` code sample already includes both the `AddFakeTenantCacheService()` call and the fake-encryption-seam comment plus a narrative justification, and the Folder/Namespace Map `Caching/` row already names both new members and their blocked status. Zero drift found; no content changed beyond this changelog entry. Both `[STATUS: Planned]` markers deliberately left in place — `FakeTenantCacheService`/`AddFakeTenantCacheService()` remain unimplemented, still genuinely `⚑` Blocked on `02.Caching`'s planned `ITenantCacheService` (Phase 44/P-435); only `SK.16.Core`/`SK.16.Tests` (C-126/C-127, T-87/T-88) remain open in the whole `16.Testing` domain as of this entry.
- [2026-08-24] `SK.16.Core`/`SK.16.Tests` closed for P-438/WO-065 by testing-phase-implementer — the blocker recorded above was re-verified directly on disk (not `02.Caching/CLAUDE.md` prose) and found STALE: `02.Caching/SharedKernel.Caching.Abstractions/ITenantCacheService.cs` and `02.Caching/SharedKernel.Caching.FusionCache/Extensions/CacheEncryptionCachingBuilderExtensions.cs` both now exist as real, compiled code with zero drift from the designed shapes. Implemented `Caching/FakeTenantCacheService.cs`/`AddFakeTenantCacheService()` and their proof suites (`Caching/FakeTenantCacheServiceTests.cs`, 9 facts; `Caching/CacheEncryptionFakeCryptographyInteropTests.cs`, 2 facts — the latter needing a new scoped `ProjectReference` from `SharedKernel.Testing.SelfTests.csproj` to `SharedKernel.Caching.FusionCache`, this project only). Both `[STATUS: Planned]` markers removed throughout this file (Interface Contracts block, Folder/Namespace Map row, DI Registration section, Implementation Rules bullet, Test Rules bullet); the D-211 audit-finding blockquote updated to name the shipped `AddCacheEncryption()` and the discovered `services.AddLogging()` transitive requirement (needed by `FusionCacheService`'s constructor, not called out in the original design). Also checked, per this pass's own brief: `Caching/FakeDistributedLockService.cs`'s `FakeRenewableLock.FencingToken` shim (added out-of-jurisdiction alongside `02.Caching` Phase 43/P-434) remains a settable `long` with no auto-increment on `RenewAsync` — adequate for compile-time `IFencedLock`/`IRenewableLock` conformance but not behaviorally faithful; left as-is (no task tracks fixing it), flagged for a future phase. `dotnet build` clean on both projects; full regression `dotnet test SharedKernel.Testing.SelfTests.csproj --configuration Release` passes 1034/1034, zero regressions. This closes WO-065 in full — every `SK.16.*` phase key is `●` again.
- [2026-08-26] Twelve-phase batch dispatch processed in one pass (testing-arch-planner) — the final domain processed in a thirteen-domain, one-session cross-domain dispatch run: P-441/WO-066 (`03.Domain` Money), P-445/WO-067 (`01.Core` Validation), P-450/WO-068 (`01.Core` async `IEncryptionKeyProvider` + envelope encryption, BREAKING), P-453/WO-069 (`01.Core`+`12.Security` TOTP/HOTP + replay guard + challenge store), P-459/WO-071 (`06.Persistence`+`05.Application` Auditing), P-463/WO-072 (`15.Integration` Notifications), P-467/WO-073 (`19.Scheduling`), P-470/WO-074 (`14.Presentation` gRPC), P-473/WO-075 (`13.ServiceDefaults` `ITenantCatalog`), P-475/WO-076 (`01.Core` DataPrivacy), P-481/WO-077 (`20.Reporting`), P-485/WO-078 (`01.Core` Localization). Every one of the twelve target contracts was designed by its OWNING domain's own planner THIS SAME SESSION — read `16.Testing/CLAUDE.md` in full first, then read all twelve owning domains' `CLAUDE.md` files in full before designing anything; every target interface signature written into the new Interface Contracts blocks below is sourced verbatim from the owning domain's own freshly-ratified prose, never guessed. Independently re-verified every target package's actual on-disk state via direct `Glob` sweeps before marking any `[STATUS]` — never trusted a "design-locked"/"`○` Pending" self-report alone. Six brand-new capability folders added: `Validation/`, `Notifications/`, `Scheduling/`, `Grpc/`, `DataPrivacy/`, `Localization/`. Six existing folders extended: `Domain/` (`MoneyFaker`/`FakeExchangeRateProvider`), `Cryptography/` (async `FakeEncryptionKeyProvider` migration + `FakeEnvelopeEncryptionProvider`, BREAKING to this fake's own surface; `FakeTotpReplayGuard`), `Security/` (`FakeTotpChallengeStore`), `Persistence/` and `Application/` (each gaining its OWN, differently-scoped `FakeAuditTrailWriter` — same name, different type, targeting the two genuinely distinct `IAuditTrailWriter` contracts `06.Persistence.Abstractions`/`05.Application.Behaviors` independently declare, cross-referenced by full namespace, mirroring the `FakeUnitOfWork`/`FakeUnitOfWork` naming-collision precedent), `ServiceDefaults/` (`InMemoryTenantCatalog`). Explicitly honored sibling-capability-folder isolation: `Persistence/FakeAuditTrailWriter`'s hash chain is a deliberately non-cryptographic, self-contained deterministic hash, NOT a dependency on the already-shipped `Cryptography/FakeContentHasher`, even though the latter ships exactly the algorithm the former could reuse. **A genuine scope-lock conflict was found and resolved, not silently overridden**: the pre-existing P-187/WO-029 SCOPE LOCK forbids this package from ever referencing `SharedKernel.MultiTenancy`, but P-473's own acceptance criteria explicitly require `InMemoryTenantCatalog` to implement the REAL `ITenantCatalog` interface. Resolved via a narrow, named revision recorded in both `state-map.md` (D-230) and this file (the `ServiceDefaults/` Interface Contracts block's SCOPE-LOCK REVISION note): `InMemoryTenantCatalog` alone may take the new `ProjectReference`; `StaticTenantProvider`/`FakeTenantResolutionStrategy` are unaffected and remain duck-typed/reference-free. One phase (P-485) resolved as an AUDIT FINDING rather than a fresh fake: `01.Core/SharedKernel.Localization`'s own planned default catalog, `InMemoryLocalizationCatalog`, already IS the seedable test double this phase's first requirement describes, so this package does not duplicate a second, colliding-named type — the genuinely net-new deliverable, `CultureScope`, is pure BCL with zero dependency on `SharedKernel.Localization` at all, making it the ONE fully-unblocked-from-day-one phase among all twelve (mirroring P-391/WO-060's zero-blocker precedent). Blocker taxonomy: seven target packages (`SharedKernel.Validation`, `.Security.Totp`, `.Integration.Notifications.Abstractions`, `.Presentation.Grpc`, `.Scheduling`, `.DataPrivacy`, `.Reporting.Abstractions`) have NO `.csproj` on disk at all — a harder blocker than this domain's historical "empty placeholder csproj" pattern, so even the Scaffold `ProjectReference` task is blocked; four (`SharedKernel.Domain`, `.Cryptography`, `.Persistence.Abstractions`, `.Application.Behaviors`) are member-level gaps on already-mature, already-referenced packages (Scaffold unblocked, Core/Tests/Docs wait); `SharedKernel.MultiTenancy` is the scope-lock-revision case above. 79 new tasks added to `state-map.md` (D-213–D-236, S-54–S-65, C-128–C-145, T-89–T-101, DO-47–DO-58; total 528→607). This file updated in full: Folder/Namespace Map (six new rows, six extended-folder narrative updates, one consolidated twelve-phase batch paragraph), six new Interface Contracts blocks plus additions to `Domain/`/`Cryptography/`/`Security/`/`Persistence/`/`Application/`/`ServiceDefaults/`'s existing blocks, two Implementation Rules bullets updated (sibling-isolation folder list, the `ServiceDefaults/` structural-compatibility rule), DI Registration section (forward-looking `[STATUS: Planned]` entries, a new narrative sentence naming which of the twelve new types are directly `new`-able), Test Rules (one consolidated routing bullet for all twelve fakes, since none has any possible owning-domain suite to anchor against), and the `SharedKernel.Testing` Packages-table row. `state-map.md`'s `## Blocked` section, Package Board, Cross-Domain Dependencies table (twelve new rows), and Overall Progress all updated in the same pass (testing-arch-planner).
