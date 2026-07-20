# 16.Testing — Shared Test Infrastructure Brain

## What This Domain Is

The shared test-infrastructure layer. Every `.Tests` project in this repository — and every downstream microservice's test suite — references `SharedKernel.Testing` for fakes, in-memory test doubles, and container fixtures instead of hand-rolling them per project. This domain is exempt from the normal layering direction: it may reference **any** numbered layer, because it is test-only and is never shipped inside a production artifact.

Philosophy: **Deterministic, dependency-light, conformance-first.** A fake's job is to satisfy the exact interface contract of the thing it replaces — nothing more. No fake here may introduce flakiness (real clocks, real sleeps, unseeded randomness) or silently diverge from the production implementation's documented behavior.

> **Why this package exists:** without it, every `.Tests` project across `02`–`14` independently reinvents `FakeCacheService`-shaped classes, container bootstrapping, and auth stand-ins — with subtle behavioral drift between copies. `16.Testing` is the single source of truth for "what does a fake `ICacheService` look like," so a behavioral fix only has to happen once.

---

## Packages

| Package | Role | References |
|---------|------|------------|
| `SharedKernel.Testing` | Fakes, in-memory test doubles, Testcontainers fixtures, and Bogus faker conventions consumed by every `.Tests` project | Any layer's `.Abstractions` package (and, where a planning pass has justified it, a non-`.Abstractions` package — e.g. `SharedKernel.Persistence.EfCore`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Application.Behaviors` — on demand, as each capability area is added). Implemented: `SharedKernel.Caching.Abstractions`, `SharedKernel.Domain`, `SharedKernel.Primitives`, `SharedKernel.Contracts`, `SharedKernel.Security.Abstractions`, `SharedKernel.Messaging.Abstractions`, `SharedKernel.Messaging.MassTransit` (test-only), `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.EfCore`, `SharedKernel.Communication.Internal`, `SharedKernel.Application.Behaviors` (WO-040 — the first `16.Testing` reference to `05.Application`; local-seam fakes + a promoted MediatR pipeline test harness, both in the `Application/` folder). Also references `Microsoft.Extensions.Logging.Abstractions` directly (P-258/WO-041, `Logging/`) — a NuGet `PackageReference` to a cross-cutting BCL logging contract, not a `SharedKernel.*` `ProjectReference`; the first capability folder in this package anchored to a foundational BCL package rather than a numbered domain's own abstraction. **Design-ahead-of-Core, blocker now CLEARED (P-268/P-269/WO-043):** `SharedKernel.Storage.Abstractions` (08.Storage) — a `ProjectReference` is added in Scaffold (S-23). As of the original WO-043 design pass, `08.Storage.Abstractions.csproj` was a genuinely empty placeholder (zero `.cs` files), a harder blocker than this package's usual "design documents target shape ahead of upstream code" convention (e.g. P-226/WO-036's `ActivityRecorder`, which needed only a literal `ActivitySource` name string, never an actual type reference) — `Storage/InMemoryFileStorage`/`InMemoryBlobUriGenerator` could not compile as `: IFileStorage`/`: IBlobUriGenerator` until `08.Storage` shipped real code. **Re-verified directly on disk during this Design-phase pass (2026-07-17): the blocker has cleared.** `08.Storage`'s `SK.08.Core` phase is now `●` 30/30 — `IFileStorage` (nine members), `IBlobUriGenerator` (two members), all seven `Models/` records, and the nine-factory-method `StorageErrors` class are fully implemented and compile clean, with zero drift from the target shape documented below. Core-phase tasks C-61–C-63 are now `●` Complete (`Storage/InMemoryFileStorage.cs`, `Storage/InMemoryBlobUriGenerator.cs`, `Storage/StorageServiceCollectionExtensions.cs`) — their Tests/Docs follow-ons T-47/DO-18 remain a future session's work. Also references `AWSSDK.S3` directly (P-268/WO-043, `Containers/`) — a NuGet `PackageReference` used exclusively by `MinioContainerFixture`'s bucket-bootstrap step (mirrors the existing `TestHarnessFactory`-carries-`MassTransit`-in-`Messaging/` precedent: one file in the folder carries a heavier third-party reference, the rest of the folder stays isolated from it). **Design-ahead-of-Core, blocker now CLEARED (P-276/WO-044):** `SharedKernel.Search.Abstractions` (09.Search) — a `ProjectReference` is planned in Scaffold (S-28). At the original WO-044 dispatch pass (2026-07-19), `09.Search/SharedKernel.Search.Abstractions.csproj` was a genuinely empty placeholder (zero `.cs` files, verified directly on disk via `09.Search`'s own `state-map.md` Package Board, not merely undispatched) — the same hard-blocker shape `08.Storage/SharedKernel.Storage.Abstractions` was in when P-269/WO-043 was originally designed. **Re-verified directly on disk during this Design-phase confirmation pass (2026-07-19, same calendar day): the blocker has cleared.** `09.Search`'s `SK.09.Core` phase — and its two provider packages' — are now `●`: `ISearchIndex<TDocument>` (12 members), `ISearchIndexProvisioner` (5 members), `ISearchProviderDescriptor` (4 members + `Validate`), and every `Models`/`Errors`/`Constants` type the target shape below depends on are fully implemented and compile clean, with zero drift from the target shape documented below. `09.Search`'s own `state-map.md` Blocked section confirms the dependency direction is now the OPPOSITE of what this note originally recorded — `09.Search`'s own `SK.09.Tests` T-13–T-17/T-21–T-26 are `⚑` Blocked waiting on THIS package's `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` (P-275). `Search/InMemorySearchIndex`/`InMemorySearchIndexProvisioner`/`InMemorySearchProviderDescriptor` can now compile as `: ISearchIndex<TDocument>`/`: ISearchIndexProvisioner`/`: ISearchProviderDescriptor` — Core-phase implementation (C-66–C-69, plus T-50/T-51/DO-21/DO-22) is corrected from `⚑` Blocked back to `○` Pending in `state-map.md`; actually writing that code remains a future Core-phase implementer session, not performed in this Design-confirmation pass. Also references `Testcontainers.Elasticsearch` and the base `Testcontainers` package directly (P-275/WO-044, `Containers/`) — used exclusively by `ElasticsearchContainerFixture` and `MeilisearchContainerFixture` respectively, the fifth and sixth `Containers/` fixtures; carried NO blocker at any point, since both fixtures expose only flat scalar connection properties with zero `SharedKernel.Search.*` reference. |
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

`SharedKernel.Testing` deliberately does **not** reference `FluentAssertions`, `NSubstitute`, or any xUnit runner package. Those belong to the **Standard Test Package Set** added directly by each `.Tests` project (see root `CLAUDE.md` Test Project Rules and each domain's own Test Rules section for the pinned versions). Mixing assertion/mocking libraries into a shared production-shaped dependency would force every consumer onto this package's framework choices.

---

## Folder / Namespace Map

```text
SharedKernel.Testing/
  Clocks/          — SharedKernel.Testing.Clocks         — IClock fake (01.Core) — FakeClock
  Caching/         — SharedKernel.Testing.Caching         — ICacheService / IDistributedLockService / ITenantCacheKeyProvider / ICacheInvalidationBus fakes (02.Caching)
  Domain/          — SharedKernel.Testing.Domain          — assertion helpers over 03.Domain primitives — DomainEventAssertions, BusinessRuleAssertions, SpecificationAssert, DomainVersionAssertions, SpecificationTestBuilder<T>, FakeDomainNotFoundException
  Contracts/       — SharedKernel.Testing.Contracts       — DTO test helpers (04.Contracts) — PagedListBuilder<T>, EnvelopeAssertions, IntegrationEventFaker<TEvent>, EventEnvelopeBuilder<TEvent>, PagedListAssertions
  Security/        — SharedKernel.Testing.Security        — IUserContext / ITenantProvider fakes (12.Security)
  Messaging/       — SharedKernel.Testing.Messaging       — IMessageBus / IEventPublisher in-memory doubles (07.Messaging), TestHarnessFactory
  Persistence/     — SharedKernel.Testing.Persistence     — IDbConnectionFactory fake + EF Core test helpers (06.Persistence) — TestSharedKernelDbContext, AggregateRootFaker, EfContextExtensions, ProjectionSpecificationBuilder, BulkAggregateFaker, WithDeletedSpecification, PersistenceTestHelpers
  Containers/      — SharedKernel.Testing.Containers      — Testcontainers IAsyncLifetime fixtures (PostgreSQL / Redis / RabbitMQ / MinIO / Meilisearch / Elasticsearch)
  Communication/   — SharedKernel.Testing.Communication   — cross-cutting Communication test doubles (11.Communication) — MockServiceEndpointResolver, FakeHttpContextAccessor, HttpClientHandlerTestFactory, FakeHttpMessageHandler, ambient Activity helper, gRPC ServerCallContext stub, GraphQL test-executor factory
  ServiceDefaults/ — SharedKernel.Testing.ServiceDefaults  — tenant resolution and health check test doubles (13.ServiceDefaults) — StaticTenantProvider, FakeTenantResolutionStrategy, HealthCheckAssertionExtensions
  Fakers/          — SharedKernel.Testing.Fakers          — Bogus deterministic-seeding convention + abstract Faker<T> bases — FakerSeeding, EntityFaker<TEntity,TId>, SingleValueObjectFaker<TValueObject,TValue>
  Application/     — SharedKernel.Testing.Application     — local-seam test doubles + MediatR pipeline test harness (05.Application.Behaviors) — FakeUnitOfWork, FakeAuthorizationContext, FakeIdempotencyKeyStore, FakeIdempotencyResponseStore, ApplicationPipelineTestHarness
  Logging/         — SharedKernel.Testing.Logging          — structured log capture double (Microsoft.Extensions.Logging.Abstractions, cross-cutting — not owned by any single numbered domain) — LogRecord, InMemoryLogger, InMemoryLogger<TCategoryName>, InMemoryLoggerFactory, LoggerAssertions
  Storage/         — SharedKernel.Testing.Storage          — IFileStorage / IBlobUriGenerator in-memory doubles (08.Storage) — InMemoryFileStorage, InMemoryBlobUriGenerator (implemented P-269/WO-043)
  Search/          — SharedKernel.Testing.Search            — ISearchIndex<TDocument> / ISearchIndexProvisioner / ISearchProviderDescriptor in-memory doubles (09.Search) — InMemorySearchIndex<TDocument>, InMemorySearchIndexProvisioner, InMemorySearchProviderDescriptor (implemented P-276/WO-044, Core phase C-64–C-69, 2026-07-20)
```

Each capability folder maps 1:1 to the numbered domain whose abstraction it fakes. A new capability folder is added only when a concrete consumer needs it — this map is aspirational scaffolding, not a commitment to build every row immediately (see per-type `STATUS` markers below). `Domain/` and `Fakers/` are deliberately split: `Fakers/` holds abstract `Bogus.Faker<T>` base classes (construction-time concerns); `Domain/` holds assertion/verification helpers (post-condition concerns) — both fake sibling-isolation from each other since neither references the other's types. `Application/` (added WO-040) fakes `05.Application.Behaviors`' own LOCAL seam interfaces (`IUnitOfWork`, `IAuthorizationContext`, `IIdempotencyKeyStore`/`IIdempotencyResponseStore`), never the real cross-domain interfaces those seams bridge to in production (`06.Persistence`, `12.Security`, `07.Messaging` are never referenced by anything in this folder) — see its Interface Contracts block below for the full rationale. `Logging/` (added P-258/WO-041) is the first folder anchored to a cross-cutting BCL contract (`Microsoft.Extensions.Logging.Abstractions`) rather than a numbered domain's own `.Abstractions` package — `[LoggerMessage]`-based structured logging (root `CLAUDE.md`'s WO-041 "Logging Conventions" section) is consumed by every domain and owned by none of them, so there is no single owning domain to model the folder after; it still obeys the sibling-isolation rule (never references `Caching/`, `Messaging/`, `Application/`, or any other capability folder). `Storage/` (added P-269/WO-043) is the newest folder — the first one mapped to `08.Storage` — and references `SharedKernel.Storage.Abstractions` only, never `SharedKernel.Storage.S3`/`.Obs` (the concrete provider packages) nor any sibling capability folder in this package (in particular, never `Containers/MinioContainerFixture` — the in-memory fake and the real-provider-integration fixture are deliberately independent test paths, mirroring the existing split between e.g. `Caching/FakeCacheService` and `Containers/RedisContainerFixture`). `Containers/` itself gains a fourth fixture, `MinioContainerFixture` (P-268/WO-043), which — like its three siblings — exposes only flat scalar connection properties and takes **no** dependency on `SharedKernel.Storage.Abstractions`; its bucket-bootstrap step is the one place in this entire package that references `AWSSDK.S3` (a third-party NuGet, not a `SharedKernel.*` type), scoped to that single file. `Containers/` gains a fifth and sixth fixture, `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` (P-275/WO-044) — same "flat scalar connection properties only, zero `SharedKernel.Search.*` dependency" shape as `MinioContainerFixture`, so this pair carries no build-time dependency on `09.Search`'s own code landing. `Search/` (P-276/WO-044) is the newest folder — the second one built against a package that was a genuinely empty placeholder on disk at design time (the first was `Storage/` at P-269/WO-043) — and references `SharedKernel.Search.Abstractions` only, never `SharedKernel.Search.Meilisearch`/`.ElasticSearch` (the concrete provider packages) nor any sibling capability folder in this package (in particular, never `Containers/MeilisearchContainerFixture`/`.ElasticsearchContainerFixture` — the in-memory fakes and the real-provider-integration fixtures are deliberately independent test paths, mirroring the existing `Storage/`-vs-`Containers/MinioContainerFixture` split exactly). Unlike every other folder in this package, the three `Search/` fakes are additionally independent OF EACH OTHER — see the SCOPE LOCK note in the `Search/` Interface Contracts block below for why.

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

AddFakeCachingServices(this IServiceCollection)
    NOTE: First DI convenience extension for Caching/ — registers all four caching fakes
          (FakeCacheService, FakeDistributedLockService, FakeTenantCacheKeyProvider,
          FakeCacheInvalidationBus) as singletons in one call. Supersedes the prior manual
          per-fake AddSingleton<T> pattern shown in DI Registration below (that pattern remains
          valid for callers who want only a subset of the four fakes).
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
FakeDbConnectionFactory  (sealed class, implements IDbConnectionFactory)            [STATUS: Planned — documented gap, see 13.ServiceDefaults Test Rules]
    constructor(Func<IDbConnection> connectionFactory)
    .CreateConnectionAsync(CancellationToken ct)                → Task<IDbConnection>
        Returns connectionFactory() wrapped in Task.FromResult — no real ADO.NET connection opened.
    NOTE: Deliberately takes a caller-supplied Func<IDbConnection> rather than constructing its own
          substitute — this package must never take a hard dependency on a mocking framework
          (NSubstitute, Moq, etc.). The consuming .Tests project builds its own IDbConnection
          substitute (typically via NSubstitute, per the platform's Standard Test Package Set) and
          passes it in. Closes the gap noted in 13.ServiceDefaults's Test Rules ("16.Testing has no
          IDbConnectionFactory fake yet").

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

- `SharedKernel.Testing` ships **no test runner, no assertion library, and no mocking framework** as a dependency of its own `.csproj` — only the minimal `Testcontainers.*`, `Bogus`, and `xunit.core` packages strictly required to implement fixtures and faker conventions. The Standard Test Package Set (xUnit runner, `FluentAssertions`, `NSubstitute`) is added per-`.Tests`-project, never transitively through this package. This includes the new `PagedListAssertions` (`Contracts/`) and `DomainVersionAssertions` (`Domain/`) — both are plain exception-throwing helpers, never FluentAssertions-backed, correcting an earlier superseded-phase draft for `PagedListAssertions`.
- Sibling capability folders (`Caching/`, `Domain/`, `Contracts/`, `Security/`, `Messaging/`, `Persistence/`, `Clocks/`, `Containers/`, `Communication/`, `ServiceDefaults/`, `Fakers/`, `Application/`, `Logging/`, `Storage/`, `Search/`) must **never reference each other**. Each fake depends only on the single abstraction package it implements (e.g., `FakeCacheService` → `SharedKernel.Caching.Abstractions` only). Mirrors the platform's sibling-package-isolation rule already enforced in `02.Caching`. Standalone helpers with no owning abstraction (`SpecificationTestBuilder<T>`, `ProjectionSpecificationBuilder<TAggregate,TResult>`, etc.) depend only on the domain types they operate over, never on a sibling folder's fake types. Demonstrated concretely by `Application/ApplicationPipelineTestHarness` (WO-040): it needs `ActivityListener`-based span capture, functionally similar to `Communication/ActivityRecorder`, but the rule forbids reusing it across folders — it hand-rolls its own small, self-contained `ActivityListener` wiring instead. A little duplicated boilerplate across sibling folders is the accepted cost of this rule; it is not a bug to "fix" by punching a hole in the isolation rule.
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
- A capability-folder addition may be **design-ahead-of-schedule in two different senses, and they carry different consequences.** The soft sense (established since P-226/WO-036's `ActivityRecorder`): the referenced domain's own capability is merely undesigned/undispatched, but the fake needs no actual compiled type from it (e.g. a literal `ActivitySource` name string) — Core-phase implementation proceeds unblocked. The hard sense (first encountered at P-269/WO-043's `Storage/InMemoryFileStorage`, repeated at P-276/WO-044's `Search/InMemorySearchIndex` — the pattern now has TWO precedents, confirming it is a recurring category, not a one-off — and BOTH have since cleared within the same or a following Design-phase confirmation pass, so a `⚑ Blocked` marker in this domain has so far always been temporary, not permanent): the fake must `: IInterfaceName` against a real compiled type in another domain's package, and that package is a genuinely empty placeholder `.csproj` with zero source files — verified directly on disk, never assumed from that domain's `CLAUDE.md` narrative alone. In the hard case, Design still proceeds (target shape is sourced from the owning domain's fully-authored `CLAUDE.md`, which is authoritative regardless of code-shipped status), and Scaffold may still add the `ProjectReference` (an empty project reference always builds), but the Core/Tests/Docs tasks that require the real type must be marked `⚑` Blocked in `state-map.md`, with an explicit Cross-Domain Dependencies row naming the upstream phase key that must land first (e.g. `SK.08.Core`, or `SK.09.Core` for `Search/`) — never silently implemented against a guessed-at interface shape, and never left as an undifferentiated `○` that looks identically "not started yet by choice" rather than "cannot start yet by necessity." **Once the upstream phase ships, re-verify directly on disk (never trust the upstream domain's own `CLAUDE.md` prose alone) and correct the blocked tasks straight back to `○` Pending** — never to `●`, since clearing the blocker only means "no longer blocked," not "implemented"; actually writing the code remains a separate future Core/Tests/Docs-phase task.
- `Containers/MinioContainerFixture` is the only type in `Containers/` permitted to carry an `AWSSDK.S3` reference (bucket-bootstrap only); `Containers/ElasticsearchContainerFixture` is likewise the only type permitted to carry a `Testcontainers.Elasticsearch` reference; `Containers/MeilisearchContainerFixture` is the only type permitted to use the base `Testcontainers` package's generic builder API directly (a `PackageReference`, not merely a transitive one) — `PostgreSqlContainerFixture`/`RedisContainerFixture`/`RabbitMqContainerFixture` remain isolated to their own single `Testcontainers.*` per-engine module, mirroring the existing `TestHarnessFactory`-is-the-only-`MassTransit`-reference-in-`Messaging/` rule. None of the six `Containers/` fixtures takes a `ProjectReference` to the owning domain's own abstraction package (`SharedKernel.Storage.Abstractions`, `SharedKernel.Search.Abstractions`), unlike `Storage/InMemoryFileStorage` or `Search/InMemorySearchIndex` — a container fixture exposing only flat scalar connection properties has no build-time dependency on the domain whose real provider it stands in for; an in-memory fake implementing that domain's actual interface does.
- `Search/`'s in-memory `SearchFilter` evaluator is the **first documented use, outside `Domain/SpecificationAssert`, of reflection-based expression/field resolution in this package** — resolving a filter node's `Field` string to a `TDocument` public property by name at runtime. This is explicitly the SAME class of test-only reflection `SpecificationAssert`/`SpecificationTestBuilder<T>` already use via `ISpecification<T>.Criteria.Compile()` (acceptable here, never in production per `03.Domain`'s own constraint) — not a new exception to any rule, a second instance of an already-sanctioned one.
- `FakeClock` must default to a **fixed, non-real `DateTimeOffset`** — never `DateTimeOffset.UtcNow` — so any test that forgets to configure it explicitly still runs deterministically across time zones and CI machines. `TestSharedKernelDbContext`'s wired-in `IClock` follows the same rule.
- `SpecificationAssert`/`SpecificationTestBuilder<T>` use `ISpecification<T>.Criteria.Compile()` — reflection-based expression compilation, acceptable in this test-only package, **never** acceptable in production code per `03.Domain`'s own documented constraint on `Specification<T>.IsSatisfiedBy`.
- `FakeTenantResolutionStrategy` (`ServiceDefaults/`) is **structurally compatible** with `13.ServiceDefaults`'s `ITenantResolutionStrategy`, not a direct interface implementation — this package takes no project reference to `SharedKernel.MultiTenancy`. The same "structural, not direct" pattern applies to any future fake whose owning interface lives in a package this domain has chosen not to reference.
- No static mutable state anywhere in this domain, with two documented exceptions: `Bogus.Randomizer.Seed` set via `FakerSeeding.Apply` (a deliberate, opt-in, process-wide determinism convention), and the single static, always-sampling `ActivityListener` registered by `AmbientActivityTestHelper` scoped to its own private `ActivitySource` (required so `ActivitySource.StartActivity` returns a real `Activity` in pure unit tests with no OTel host listening). Both are deliberate, opt-in, process-wide — never incidental shared state.

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
```

Fakes in `Security/`, `Persistence/`, `Clocks/` (outside `AddFakeDomainServices()`'s narrow `IClock` registration), `Contracts/`, `Communication/`, and `ServiceDefaults/` are intentionally **not** wrapped in `Add*` DI extensions — they are simple `new`-able classes or static helpers with test-controlled constructor parameters, and registering them via DI adds indirection most unit tests don't need. Only doubles that exist specifically to be swapped in for a production DI registration (caching, messaging, the single `IClock` registration in `AddFakeDomainServices()`, `Application/`'s three-fake bundle, `Storage/`'s two-fake bundle, and now `Search/`'s per-index call plus its provisioning bundle) ship a convenience extension. `AddFakeContractsServices()` is explicitly **deferred** (P-064/WO-012) — none of the `Contracts/` helpers currently need DI registration; add it only if a concrete need surfaces. `ApplicationPipelineTestHarness` (`Application/`) is deliberately **not** DI-registered — it is a directly `new`-able builder/harness type, consistent with `SpecificationTestBuilder<T>`/`ProjectionSpecificationBuilder<TAggregate,TResult>`'s existing convention for builder-shaped types. `Containers/MinioContainerFixture`/`MeilisearchContainerFixture`/`ElasticsearchContainerFixture` are likewise never DI-registered — container fixtures are consumed via xUnit `ICollectionFixture<T>`, never a DI container, consistent with `PostgreSqlContainerFixture`/`RedisContainerFixture`/`RabbitMqContainerFixture`.

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
