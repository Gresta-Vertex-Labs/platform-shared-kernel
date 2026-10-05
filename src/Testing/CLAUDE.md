# 16.Testing — Domain Brain

> The platform's test infrastructure: one lightweight core (`SharedKernel.Testing`), nineteen per-capability `SharedKernel.{Capability}.Testing` packages that fake a capability's contracts, and the non-packable `SharedKernel.Testing.Internal` with Testcontainers fixtures for this repository's own suites. Consuming services' test projects and this repo's `.Tests` projects use the same doubles, so a behavioural fix happens once. Philosophy: **deterministic, dependency-light, conformance-first** — a double satisfies the exact contract it replaces, including failure modes and mandatory tenant scope, and nothing more. This domain owns no production code, no business logic and no mocking framework.

## Packages

Every project declares `<SharedKernelTier>Testing</SharedKernelTier>`; every package except `Testing.Internal` is packable and tracks `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`. Namespaces are `SharedKernel.Testing.{Capability}` regardless of package id (exceptions noted).

**Location.** Only `SharedKernel.Testing` and `SharedKernel.Testing.Internal` live in this folder. Each `SharedKernel.{Capability}.Testing` lives in its capability's folder, next to the contract it fakes (for example `src/Infrastructure/Caching/SharedKernel.Caching.Testing`), so a contract change and its double change in one place. This domain keeps the rules every double follows (below) and this catalogue; a new `.Testing` package is created in the capability folder of the contract it fakes.

| Package | Tier | Purpose |
|---|---|---|
| `SharedKernel.Testing` (core) | Testing | Foundation/Model helpers only: `FakeClock`, `InMemoryLogger`/`InMemoryLoggerFactory`/`LoggerAssertions`/`AddInMemoryLoggerFactory()`, `TestRequestContext` (`.Execution`), `FakeRequestContext` (`.Application`), `EntityFaker<,>`/`SingleValueObjectFaker<,>`/`FakerSeeding`, domain assertions + `SpecificationTestBuilder<T>`/`MoneyFaker`/`FakeExchangeRateProvider`/`AddFakeDomainServices()`, aggregate fakers and spec builders (`.Persistence`: `AggregateRootFaker<,>`, `TenantedAggregateFaker<,>`, `BulkAggregateFaker<,>`, `WithDeletedSpecification<T>`, `ProjectionSpecificationBuilder<,>`), `PagedListBuilder<T>`/`PagedListAssertions`/`EventEnvelopeBuilder<TEvent>`/`IntegrationEventFaker<TEvent>`, `ValidationSampleGenerator`, `PiiMaskingAssertions`/`RecordingDataSubjectRequestHandler`, `CultureScope`, `FakeHttpMessageHandler`/`ActivityRecorder` (BCL only). |
| `SharedKernel.Application.Testing` | Testing | `ApplicationPipelineTestHarness` (runs the kernel pipeline with its host-start seam checks), `AddFakeApplicationBehaviorServices()`. |
| `SharedKernel.Persistence.Testing` | Testing | Namespace `SharedKernel.Persistence.Testing`. `FakeRepository<,>`/`AddFakeRepository<TAggregate,TId>()`, `FakeUnitOfWork` (`TransientFailures`), `FakeAuditTrailWriter`, `FakeCrossTenantScope`, `FakeDbConnectionFactory`, `AddTestRequestContext()`, `PostgresTestServer`/`PostgresTestDatabase`/`PostgresTestRoles` (production role split). |
| `SharedKernel.Caching.Testing` | Testing | `FakeCacheService`, `FakeTenantCacheService`, `FakeDistributedLockService`/`FakeDistributedLock`, `FakeCacheWarmupStrategy`, `FakeTenantCacheKeyProvider`; `AddFakeCachingServices()`, `AddFakeTenantCacheService()`, `AddFakeCacheWarmupStrategy()`. |
| `SharedKernel.Caching.Redis.Testing` | Testing | `FakeRedisChannelService`, `FakeRedisHashService`, `FakeTypedHashStore<T>`; `AddFakeRedisServices()`, `AddFakeTypedHashStore<T>()`. |
| `SharedKernel.Communication.Testing` | Testing | `StubHttpMessageHandler` + `UseStubHttpMessageHandler(clientName, stub)`, `RecordedHttpRequest`, `GrpcCalls` (fake unary calls, rich-status failures), `TestServerCallContext` (client-side stub). |
| `SharedKernel.Cryptography.Testing` | Testing | Fakes over the real algorithms (`FakeSymmetricEncryptionService`, `FakeOneWayHasher`, `FakeHmacSigner`, key providers, `FakeTotpReplayGuard`, …); `AddFakeCryptography()`. |
| `SharedKernel.FeatureManagement.Testing` | Testing | `FakeFeatureClient`; `AddFakeFeatureFlags()`. |
| `SharedKernel.Idempotency.Testing` | Testing | `FakeIdempotencyStore` (`RecordedCall`); `AddFakeIdempotencyStore(purposes)`. |
| `SharedKernel.Integration.Testing` | Testing | `InMemoryWebhookDispatcher`/`InMemoryWebhookDeliveryObserver` (`.Integration`), `InMemoryNotificationSender`/`InMemoryNotificationDeliveryObserver` (`.Notifications`); matching `AddInMemory*()`. |
| `SharedKernel.Messaging.Testing` | Testing | `InMemoryMessageBus`, `InMemoryEventPublisher`; `AddInMemoryMessageBus()`, `AddInMemoryEventPublisher()`. |
| `SharedKernel.Storage.Testing` | Testing | `InMemoryFileStorage`/`InMemoryStorage`; `AddInMemoryStore(name)`, `AddInMemoryTenantStore(name)` on the real storage builder. |
| `SharedKernel.Search.Testing` | Testing | `InMemorySearchIndex<TDocument>`, provisioner, descriptor; `AddInMemorySearchIndex<TDocument>()`, `AddInMemorySearchProvisioning()`. |
| `SharedKernel.AI.Testing` | Testing | Namespace `.Intelligence`. `InMemoryEmbeddingGenerator` (hash-derived vectors), `InMemoryVectorCollection<TRecord>`, provisioner, provider descriptors, `InMemorySemanticKernel`; `AddInMemoryEmbeddingGenerator()`, `AddInMemoryVectorCollection<TRecord>()`, `AddInMemoryVectorProvisioning()`, `AddInMemorySemanticKernel()`. |
| `SharedKernel.Security.Testing` | Testing | `FakeUserContext`, `SecurityTestContextBuilder`, `InMemoryApiKeyStore`, `InMemoryDpopReplayCache`, `InMemoryTotpStepUpStore`, `InMemoryRecoveryCodeStore`, `DpopTestProofBuilder`, `MtlsTestCertificateBuilder`. References Host-tier Security packages (brings ASP.NET Core). |
| `SharedKernel.Workflows.Testing` | Testing | `InMemoryWorkflowDispatcher`, `InMemoryWorkflowHandle<TResult>`, `InMemoryWorkflowStartRecord`; `AddInMemoryWorkflowDispatcher()`. |
| `SharedKernel.Scheduling.Testing` | Testing | `InMemoryScheduledJobRegistry(ISender)` — records registrations; a test fires a tick with `TriggerAsync` (optionally a simulated misfire). |
| `SharedKernel.Reporting.Testing` | Testing | `InMemoryReportExporter<TRow>`, `InMemoryReportExporterFactory`, `InMemoryHtmlToPdfConverter`; `AddInMemoryReporting()`. |
| `SharedKernel.Presentation.Testing` | Testing | `TestServerCallContext` (`.Grpc`, server side with an `HttpContext`), `GraphQLTestExecutorFactory`, `FakeHttpContextAccessor` (`.Communication`). |
| `SharedKernel.ServiceDefaults.Testing` | Testing | `InMemoryTenantCatalog`, `FakeTenantResolutionStrategy`, `HealthCheckAssertionExtensions`. |
| `SharedKernel.Testing.Internal` (not packable) | Testing | Namespaces `SharedKernel.Testing.{Containers, Persistence, Messaging}`. Testcontainers fixtures (`PostgreSqlContainerFixture`, `RedisContainerFixture`, `RabbitMqContainerFixture`, `MinioContainerFixture`, `ElasticsearchContainerFixture`, `MeilisearchContainerFixture`, `QdrantContainerFixture`), EF Core/Npgsql/audit-ledger helpers, MassTransit `TestHarnessFactory`. This repository's suites only. |

## Public Entry Points

- **Caller:** `TestRequestContext` (settable tenant, actor, permissions, correlation id); `AddTestRequestContext()` (Persistence.Testing) registers one.
- **Time and logs:** `new FakeClock(...)` for `IClock`; `services.AddInMemoryLoggerFactory()` then `LoggerAssertions` on `InMemoryLogger` records (EventId, level, structured properties).
- **Replace one dependency in a real host:** the Cryptography, Idempotency, Persistence and Reporting `AddFake*`/`AddInMemory*` extensions remove an existing registration first; the others only add, so call them before (or instead of) the production registration.
- **Storage:** `services.AddSharedKernelStorage().AddInMemoryStore("invoices")` — named stores and tenant views resolve as in production.
- **Pipeline:** `new ApplicationPipelineTestHarness().Configure(app => app.WithTransactions()…).Build<TMarker>()` — `Configure` takes the same `ApplicationPipelineBuilder` a service passes to `AddSharedKernelApplication`, `Build<TMarker>()` (or `Build()`) runs the real registration and seam checks, then `SendAsync`; `WithActivityCapture()` records spans and metrics. Authorization is always on.
- **REST/gRPC clients:** `UseStubHttpMessageHandler(clientName, stub)` runs a typed client's whole pipeline against canned answers; `GrpcCalls` fakes unary calls.
- **PostgreSQL for consumers:** `PostgresTestServer`/`PostgresTestDatabase` (Persistence.Testing) — never `Testing.Internal`.
- No configuration sections: doubles take options objects or constructor arguments.

## Rules & Invariants

1. **Testing packages are referenced by test projects only.** `SharedKernelLayeringRules.TestingNeverReferencedByProduction` (usable against a consumer's assemblies) and `TestingPackagesNeverReferencedByProductionTests` (by tier and by `SharedKernel.*.Testing` name) enforce it; the tier check forbids Testing below Testing.
2. **The core stays lightweight:** Foundation and Model packages, Bogus and the two `Microsoft.Extensions.*.Abstractions` only (`CoreTestingPackage_DependsOnlyOnFoundationAndModelPackages`). A capability double goes in that capability's package, never the core.
3. **A capability package references the abstraction it fakes, not a provider** — unless the contract only exists in a concrete package (Workflows.Temporal, Scheduling, the Security Host packages, Integration.Webhooks, Communication). It may reference the core, and another Testing package only to compose doubles (`Application.Testing` → `Idempotency.Testing` + `Persistence.Testing`).
4. **No test framework in a packable package** — no xUnit, NUnit, MSTest, FluentAssertions or NSubstitute; assertions throw `InvalidOperationException` with a readable message. Only `Testing.Internal` references `xunit.core` (its `IAsyncLifetime` fixtures).
5. **No mocking framework inside a double.** Implement the interface directly.
6. **Docker-bound infrastructure lives only in `Testing.Internal`** (`IsPackable=false`), except `Persistence.Testing`'s `PostgresTestServer` for consumers.
7. **Deterministic:** time from `FakeClock` or a caller-supplied `TimeProvider`/`IClock`, seeded randomness (`FakerSeeding`), no real I/O outside container fixtures. Flakiness traced into a double is a bug in the double.
8. **Thread-safe (target):** a stateful double should tolerate parallel test collections. `InMemoryScheduledJobRegistry` and `InMemoryReportExporter` still use unsynchronized collections — keep a test that uses them in one collection until they are fixed.
9. **Faithful failure modes:** a double fails where production fails (definition validation, tenant fail-closed for `TenantScope.Global` against a tenant-declaring index/collection, fingerprint mismatch, conditional-write conflicts) and returns the owning domain's real `Error` codes. Simplifications (exact `TotalHits`, substring free-text, …) are documented in the package README.
10. **Tenants are `TenantId`, scopes are `SharedKernel.Execution.Tenancy.TenantScope`, callers are `IRequestContext`.** There is no fake tenant provider.
11. **Registered lifetime:** a double whose history is asserted after the SUT's scope ends is a **singleton** even when production is scoped (message bus, event publisher, search index, vector collection, workflow dispatcher); say so in the XML doc and README.
12. **Extensions never register `ILogger<T>` or `IClock`** for the caller.
13. **Magic strings follow SK0022:** headers and baggage/tag keys come from `WellKnownHeaders`/`WellKnownBaggageKeys`/`WellKnownTagKeys`.
14. **Keep each package README current** with its public API (contents, registration, example).

## Decisions

| Decision | Why |
|---|---|
| One package per capability instead of one monolithic testing package | A consumer's test project takes only the dependencies of the capabilities it uses; the core never drags in providers. |
| `FakeRepository<,>` ignores `expectedVersion` | A real `EntityVersion` is an opaque token only the real repository issues; pass `EntityVersion.None` and test concurrency/ETags against PostgreSQL. |
| `FakeRepository<,>` with `FakeUnitOfWork` restores membership on rollback, not in-place aggregate changes | Snapshotting arbitrary aggregates would need reflection or cloning. |
| `FakeIdempotencyStore` records the 64-hex tenant/caller digest, not the raw key | That is what `IdempotencyBehavior` passes to a real store; `.WithIdempotency()` also needs a registered `IRequestContext`. |
| `FakeUserContext` dates methods via `AuthenticationMethodTimes`/`WithAuthenticationMethodTime`; `GetAuthenticationMethodTime` answers exactly as the real `UserContext` | Lets a consumer drive `[RequireAuthenticationMethod(…, MaxAgeSeconds = n)]` fresh and expired from a `FakeClock`. A time only dates a method — list it in `AuthenticationMethods` too. |
| `Presentation.Testing`'s `TestServerCallContext` does not simulate authorization or the exception interceptor | Both run in the ASP.NET Core pipeline before the method; test them against an in-process host. |
| `InMemoryScheduledJobRegistry` fires ticks manually via `TriggerAsync` | Misfire/overlap policies are asserted without a clock, hosted loop or lock store. |
| Container fixtures pin images (`qdrant/qdrant:v1.16.0`, `docker.elastic.co/elasticsearch/elasticsearch:9.4.2`, `getmeili/meilisearch:v1.20.0`, `redis:7.4`, `rabbitmq:3.13-management`, …) | Reproducible CI; a pin must support what the owning domain relies on (Qdrant collection metadata needs 1.16+). |

## Logging

Block **16000–16999** (`LoggingEventIdRanges.Testing`) is reserved but unused: test doubles emit no `[LoggerMessage]` logs. `InMemoryLogger` only records the EventIds production code emits.

## Cross-Domain Couplings

- Each `{Capability}.Testing` package references its capability's contract package (see the table) and implements its interfaces; a contract change in that domain must update the double, its README and its self-tests in the same change.
- Contract sources of truth: `19.Scheduling`'s job execution model for `InMemoryScheduledJobRegistry`; `05.Application`'s `ApplicationPipelineBuilder` for the harness; `12.Security`'s `UserContext` for `FakeUserContext`; `18.Idempotency`'s `IIdempotencyStore` semantics for `FakeIdempotencyStore`.
- `00.Governance` enforces the no-production-reference rule and the core's dependency lock.
- Every domain's `.Tests` projects consume the core, their capability's `.Testing`, and `Testing.Internal` for containers.

## Testing

- Every package has a nested `{Name}.Tests` project (namespace `SharedKernel.Testing.SelfTests.{Capability}`) proving each double against the documented behaviour of the production contract. All run in the **Unit lane** (`Platform.SharedKernel.Unit.slnf`) except `SharedKernel.Persistence.Testing.Tests` and `SharedKernel.Testing.Internal.Tests`, which need Docker (**Integration lane**).
- A behavioural change to an existing double must be cross-checked against every suite that consumes it — grep the type name across `**/*.Tests/` first.
- Container fixtures are shared per collection via `[CollectionDefinition]` + `ICollectionFixture<T>`; never hand-roll a competing container setup inside a `.Tests` project.
- Event types passed to `EventEnvelopeBuilder<TEvent>`, `InMemoryEventPublisher` or `InMemoryWebhookDispatcher` must be `sealed` `IIntegrationEvent`s with a valid `[IntegrationEvent("name", Version = n)]`.
- **Adding a double:** a contract of capability X → `SharedKernel.X.Testing` (create it if absent: Testing tier, packable, `PublicAPI.*.txt`, README, nested `.Tests`, added to `Platform.SharedKernel.slnx` and the Unit `.slnf`, a `PackageVersion` for any new dependency in `Directory.Packages.props`); a Foundation/Model-only helper → the core; a container fixture, EF Core helper or MassTransit harness → `Testing.Internal`. Audit the existing surface first, and check the package id against the 245-character test-assembly path limit before scaffolding.

## Known Limitations

- Doubles simplify where documented in each README (e.g. in-memory search counts are exact and free-text is substring match; in-memory embeddings are hash-derived, not semantic).
- `FakeRepository<,>` cannot test optimistic concurrency or ETags; use PostgreSQL.
- `Presentation.Testing`'s `TestServerCallContext` does not run endpoint authorization or the platform exception interceptor.
- `Security.Testing` references Host-tier packages, so it brings ASP.NET Core into any test project that uses it.
