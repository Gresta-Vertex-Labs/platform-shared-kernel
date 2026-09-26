# 16.Testing — Shared Test Infrastructure Brain

## What This Domain Is

The test infrastructure of the platform, split into **twenty packable Testing-tier packages** plus one
non-packable project. Consuming services' test projects and this repository's own `.Tests` projects take the same
doubles, so a behavioural fix to a fake happens once.

Philosophy: **Deterministic, dependency-light, conformance-first.** A double satisfies the exact contract of the
thing it replaces — including its failure modes and the mandatory tenant scope — and nothing more. No double may
introduce flakiness (real clocks, real sleeps, unseeded randomness) or silently diverge from the production
behaviour it stands in for.

---

## Packages

Every project here declares `<SharedKernelTier>Testing</SharedKernelTier>`. A Testing package may reference any
production tier (a double must implement the contract it stands in for); **no production project may reference a
Testing package** — see Hard rules.

| Package | Namespace(s) | References | Doubles for |
| --- | --- | --- | --- |
| `SharedKernel.Testing` (**core**, packable) | `SharedKernel.Testing.{Clocks, Logging, Execution, Application, Fakers, Domain, Persistence, Contracts, Validation, DataPrivacy, Localization, Communication}` | Foundation/Model only: `Primitives`, `Execution`, `DataPrivacy`, `Validation`, `Domain`, `Contracts`; `Bogus`; `Microsoft.Extensions.Logging.Abstractions`, `.DependencyInjection.Abstractions` | `FakeClock`, `InMemoryLogger`/`InMemoryLoggerFactory`/`LoggerAssertions`/`AddInMemoryLoggerFactory()`, `TestRequestContext`, `FakeRequestContext`, `EntityFaker`/`SingleValueObjectFaker`/`FakerSeeding`, domain assertions + `SpecificationTestBuilder`/`MoneyFaker`/`FakeExchangeRateProvider`/`AddFakeDomainServices()`, domain-only aggregate fakers, `PagedListBuilder`/`EventEnvelopeBuilder`/`IntegrationEventFaker`, `ValidationSampleGenerator`, `PiiMaskingAssertions`/`RecordingDataSubjectRequestHandler`, `CultureScope`, `FakeHttpMessageHandler`/`ActivityRecorder` (BCL only) |
| `SharedKernel.Application.Testing` | `SharedKernel.Testing.Application` | core, `Idempotency.Testing`, `Persistence.Testing`, `Application.Pipeline`, `Application.Mediator.MediatR` | `ApplicationPipelineTestHarness`, `AddFakeApplicationBehaviorServices()` |
| `SharedKernel.Persistence.Testing` | `SharedKernel.Persistence.Testing` | core, the persistence packages, `Testcontainers.PostgreSql` | `FakeRepository<,>`/`AddFakeRepository`, `FakeUnitOfWork`, `FakeAuditTrailWriter`, `FakeCrossTenantScope`, `FakeDbConnectionFactory`, `AddTestRequestContext`, `PostgresTestServer`/`PostgresTestDatabase` (production role split) |
| `SharedKernel.Caching.Testing` | `SharedKernel.Testing.Caching` | `Caching.Abstractions` | `FakeCacheService`, `FakeDistributedLockService`, `FakeTenantCacheService`, key providers; `AddFakeCachingServices()`, `AddFakeTenantCacheService()`, `AddFakeCacheWarmupStrategy()` |
| `SharedKernel.Caching.Redis.Testing` | `SharedKernel.Testing.Caching` | the Redis HashStore/PubSub packages | `FakeRedisChannelService`, `FakeRedisHashService`, `FakeTypedHashStore<T>`; `AddFakeRedisServices()`, `AddFakeTypedHashStore<T>()` |
| `SharedKernel.Cryptography.Testing` | `SharedKernel.Testing.Cryptography` | `Cryptography` | Fakes over the real algorithms; `AddFakeCryptography()` |
| `SharedKernel.FeatureManagement.Testing` | `SharedKernel.Testing.FeatureManagement` | `FeatureManagement` | `FakeFeatureClient`; `AddFakeFeatureFlags()` |
| `SharedKernel.Idempotency.Testing` | `SharedKernel.Testing.Idempotency` | `Idempotency.Abstractions` | `FakeIdempotencyStore`; `AddFakeIdempotencyStore(purposes)` |
| `SharedKernel.Messaging.Testing` | `SharedKernel.Testing.Messaging` | `Messaging.Abstractions` | `InMemoryMessageBus`, `InMemoryEventPublisher`; `AddInMemoryMessageBus()`, `AddInMemoryEventPublisher()` |
| `SharedKernel.Storage.Testing` | `SharedKernel.Testing.Storage` | `Storage.Abstractions` | `InMemoryFileStorage`; `AddInMemoryStore(name)`, `AddInMemoryTenantStore(name)` on the storage builder |
| `SharedKernel.Search.Testing` | `SharedKernel.Testing.Search` | `Search.Abstractions` | `InMemorySearchIndex<T>`, provisioner, descriptor; `AddInMemorySearchIndex<T>()`, `AddInMemorySearchProvisioning()` |
| `SharedKernel.AI.Testing` | `SharedKernel.Testing.Intelligence` | `AI.Abstractions` | Embedding generator (hash-derived vectors), vector collection, provisioner, descriptors, semantic kernel; `AddInMemory*()` |
| `SharedKernel.Security.Testing` | `SharedKernel.Testing.Security` | `Security.Abstractions`, `.ApiKey`, `.Oidc`, `.Totp` (Host — brings ASP.NET Core) | `FakeUserContext`, `SecurityTestContextBuilder`, in-memory API-key/DPoP-replay/TOTP stores, DPoP proof and mTLS certificate builders |
| `SharedKernel.Workflows.Testing` | `SharedKernel.Testing.Workflows` | `Workflows.Temporal` | `InMemoryWorkflowDispatcher`/`InMemoryWorkflowHandle`; `AddInMemoryWorkflowDispatcher()` |
| `SharedKernel.Scheduling.Testing` | `SharedKernel.Testing.Scheduling` | `Scheduling` | `InMemoryScheduledJobRegistry` (manual `TriggerAsync`) |
| `SharedKernel.Integration.Testing` | `SharedKernel.Testing.Integration`, `.Notifications` | `Integration.Webhooks`, `.Notifications.Abstractions` | Webhook dispatcher/observer, notification sender/observer; `AddInMemory*()` |
| `SharedKernel.Reporting.Testing` | `SharedKernel.Testing.Reporting` | `Reporting.Abstractions` | `InMemoryReportExporter<TRow>` |
| `SharedKernel.Communication.Testing` | `SharedKernel.Testing.Communication` | `Communication.Internal`, `Grpc.Core.Testing` | `MockServiceEndpointResolver`, gRPC `ServerCallContext` stub |
| `SharedKernel.Presentation.Testing` | `SharedKernel.Testing.Communication`, `.Grpc` | `Execution`, `Presentation.Grpc`, `Grpc.Core.Testing`, HotChocolate | `TestServerCallContext` (with `HttpContext`), `GraphQLTestExecutorFactory`, `FakeHttpContextAccessor` |
| `SharedKernel.ServiceDefaults.Testing` | `SharedKernel.Testing.ServiceDefaults` | `MultiTenancy`, health-check abstractions | `InMemoryTenantCatalog`, `FakeTenantResolutionStrategy`, `HealthCheckAssertionExtensions` |
| `SharedKernel.Testing.Internal` (**not packable**) | `SharedKernel.Testing.{Containers, Persistence, Messaging}` | core, `Persistence.Testing`, EF Core, MassTransit, Testcontainers, `xunit.core`, `AWSSDK.S3` | Testcontainers fixtures (PostgreSQL, Redis, RabbitMQ, MinIO, Elasticsearch, Meilisearch, Qdrant), EF Core/Npgsql/audit-ledger helpers, `TestHarnessFactory` — this repository's own suites only |

Namespaces are `SharedKernel.Testing.{Capability}` regardless of the package id (the AI package uses
`.Intelligence`; Presentation shares `.Communication`); `SharedKernel.Persistence.Testing` is the one package whose
namespace is its id. Each package has a README with contents, registration and an example — keep it current with
the public API (`PublicAPI.Shipped.txt`/`Unshipped.txt` are tracked on every packable package).

---

## Hard rules

- **Testing packages are referenced by test projects only.** `TestingNeverReferencedByProduction` (also usable against a consuming service's assemblies) and `TestingPackagesNeverReferencedByProductionTests` (matches by tier and by `SharedKernel.*.Testing` name) fail the build otherwise.
- **The core stays lightweight.** `SharedKernel.Testing` references Foundation and Model packages, Bogus and the two `Microsoft.Extensions.*.Abstractions` packages only — locked by `CoreTestingPackage_DependsOnlyOnFoundationAndModelPackages`. A double for a capability goes in that capability's package, never in the core.
- **A capability package references the abstraction it fakes, not a provider** — unless the contract only exists in a concrete package (Workflows.Temporal, Scheduling, the Security Host packages, Integration.Webhooks). It may reference the core, and another Testing package only to compose its doubles (`Application.Testing` composes `Idempotency.Testing` and `Persistence.Testing` for the pipeline seams).
- **No test framework in a packable package.** No xUnit, NUnit, MSTest, FluentAssertions or NSubstitute; assertions throw `InvalidOperationException` with a readable message. Only `SharedKernel.Testing.Internal` (xUnit `IAsyncLifetime` fixtures) references `xunit.core`.
- **Heavy or Docker-bound infrastructure lives in `SharedKernel.Testing.Internal`**, which is `IsPackable=false`. A consuming service that needs PostgreSQL uses `SharedKernel.Persistence.Testing`'s `PostgresTestServer`.
- **No mocking framework inside a double.** Implement the interface directly in C#.
- **Deterministic.** Time comes from `FakeClock` or a caller-supplied `TimeProvider`/`IClock`; randomness is seeded; no real I/O outside `Internal`'s container fixtures. A test failure traced to real time or randomness inside a double is a bug in the double.
- **Thread-safe.** Every stateful double tolerates parallel test collections (`ConcurrentDictionary`/locks).
- **Faithful failure modes.** A double fails where production fails: definition validation, tenant fail-closed (`TenantScope.Global` against a tenant-declaring index/collection), fingerprint mismatch, conditional-write conflicts. It returns the owning domain's real `Error` codes, never invented ones. Simplifications (e.g. exact `TotalHits`, substring free-text) are documented in the package README.
- **Tenants are `TenantId`, scopes are `SharedKernel.Execution.Tenancy.TenantScope`, callers are `IRequestContext`.** Use `TestRequestContext` (core) for any caller; there is no fake tenant provider.
- **Logging in production code is asserted through the core's `InMemoryLogger`** (EventId, level, structured properties), never on rendered text and never through a hand-rolled `ILogger` mock.
- **Magic strings follow the platform rule** (`SK0022`): header names, baggage keys and tag keys come from `WellKnownHeaders`/`WellKnownBaggageKeys`/`WellKnownTagKeys`.

---

## Registration conventions

- `Add{Fake|InMemory}*()` extensions **replace** any existing registration of the same service, so a test can
  compose the real host and swap one dependency.
- A double whose recorded history is asserted after the system under test's scope ends is registered as a
  **singleton**, even when production is scoped (message bus, event publisher, search index, vector collection,
  workflow dispatcher). Each such deviation is stated in the extension's XML doc and the package README.
- A double with no `Add*` helper is constructed directly (`new InMemoryScheduledJobRegistry(sender)`,
  `new FakeUserContext { … }`) or registered by the test.
- Storage doubles register through the real builder (`AddSharedKernelStorage().AddInMemoryStore(name)`), so named
  stores and tenant views resolve exactly as in production.
- These extensions never register `ILogger<T>` or `IClock` for the caller; use `AddInMemoryLoggerFactory()` and
  `FakeClock`.

---

## Where a new double belongs

| You need… | Put it in… |
| --- | --- |
| A double for a contract in capability X | `SharedKernel.X.Testing` (create it if absent: Testing tier, packable, `PublicAPI.*.txt`, README, nested `{Name}.Tests`, added to `Platform.SharedKernel.slnx` and `Platform.SharedKernel.Unit.slnf`) |
| A helper over Foundation/Model types only (clock, logger, request context, fakers, domain/contract assertions) | `SharedKernel.Testing` (core) |
| A Testcontainers fixture, an EF Core helper, a MassTransit harness | `SharedKernel.Testing.Internal` |
| A persistence contract double or PostgreSQL fixture a consuming service needs | `SharedKernel.Persistence.Testing` |

Before adding one, audit the existing surface — the gap often does not exist. Check the package id against the
Windows path limit (`…\{Name}\{Name}.Tests\obj\Release\net10.0\{Name}.Tests.dll` ≤ 245 characters at
`C:\Github\platform-shared-kernel`) before scaffolding. Every new `PackageReference` needs a `PackageVersion` in
`Directory.Packages.props`.

---

## Test rules

- Every package has its own nested `{Name}.Tests` project (namespace `SharedKernel.Testing.SelfTests.{Capability}`)
  proving each double against the documented behaviour of the production contract it replaces. The unit lane runs
  them all; `SharedKernel.Testing.Internal.Tests` (Docker) runs in the Integration lane.
- A behavioural change to an existing double must be cross-checked against every suite that consumes it — grep the
  type name across `**/*.Tests/` first.
- Container fixtures pin their image (`qdrant/qdrant:v1.16.0`, `elasticsearch:9.4.2`,
  `getmeili/meilisearch:v1.20.0`, …) and are shared per collection via `[CollectionDefinition]` +
  `ICollectionFixture<T>`; never hand-roll a competing container setup inside a `.Tests` project. A fixture image
  must support what the owning domain actually relies on (Qdrant collection metadata needs 1.16+).
- Test event types passed to `EventEnvelopeBuilder<TEvent>`, `InMemoryEventPublisher` or
  `InMemoryWebhookDispatcher` must be `sealed` `IIntegrationEvent`s with a valid `[IntegrationEvent("name",
  Version = n)]`.

---

## Changelog

History — every double's design notes, the per-fake routing decisions, and the WO-086 split into per-capability
packages (P-571) — is in `state-map.md` ("Domain-Brain Changelog" and the phase tables). The detailed pre-split
interface notes are in git history (this file before P-575).
