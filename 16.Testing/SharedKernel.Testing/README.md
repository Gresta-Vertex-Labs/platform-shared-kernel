# SharedKernel.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Consumption: ProjectReference](https://img.shields.io/badge/consumption-ProjectReference-informational)
![Runner-agnostic](https://img.shields.io/badge/ships%20no-test%20runner-brightgreen)

**The shared test library for this repository: a test double for every SharedKernel abstraction, container
fixtures for the real dependencies, and assertion helpers that check outcomes rather than rendered strings.**

Each package here implements another domain's *published contract*, so a test writes against the same
interface production code does. A fake never reaches for a provider: `FakeCacheService` implements
`ICacheService` and knows nothing about Redis, so a test that uses it needs no server.

| You get | So that |
| --- | --- |
| In-memory doubles for the platform's abstractions | A unit test exercises real wiring with no infrastructure |
| Testcontainers fixtures for Postgres, Redis, RabbitMQ, MinIO, Elasticsearch, Meilisearch and Qdrant | An integration test runs against the real engine, not a mock of it |
| `InMemoryLogger` and `LoggerAssertions` | Log assertions key off `EventId` and structured properties, never a formatted message |
| Deterministic fakers (Bogus) seeded once per assembly | The same test data every run, on every machine and in CI |
| `ApplicationPipelineTestHarness` | A service asserts its own MediatR behavior composition end to end |

## Not a published package

`IsPackable=false`. This library is consumed **by `ProjectReference` inside this repository only** — it is
never pushed to a feed, and production code never references it (an architecture test enforces that). It is
also not a test project: it ships no runner, and references `xunit.core` solely for the `IAsyncLifetime`
contract the container fixtures implement, so your own suite picks its own runner.

```xml
<ProjectReference Include="..\..\..\16.Testing\SharedKernel.Testing\SharedKernel.Testing.csproj" />
```

## Quick start

### A controllable clock

```csharp
var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

var subscription = Subscription.Start(clock);
clock.Advance(TimeSpan.FromDays(31));

Assert.True(subscription.IsExpired(clock));
```

`FakeClock` implements `IClock`, so nothing under test reads the machine clock. `Set`/`SetUtcNow` jump to an
instant, `Advance` moves relative to the current one.

### Asserting what was published

```csharp
var bus = new InMemoryMessageBus();
await new PlaceOrderHandler(bus).Handle(command, CancellationToken.None);

var published = bus.ShouldHavePublishedOnce<OrderPlaced>();
Assert.Equal(orderId, published.OrderId);
bus.ShouldNotHavePublished<OrderCancelled>();
```

`ShouldHavePublished<T>()`, `ShouldHaveSent<T>()`, `ShouldHavePublishedOnce<T>()` and
`ShouldNotHavePublished<T>()` fail with the recorded messages in the failure text. `SetResponseHandler` scripts
a reply for `RequestAsync`. `InMemoryEventPublisher` does the same for `IEventPublisher`.

### Asserting what was logged

```csharp
var logger = new InMemoryLogger<OrderService>();

await new OrderService(logger).PlaceAsync(command, CancellationToken.None);

logger.Records.ShouldHaveLogged(new EventId(5101), LogLevel.Information);
logger.Records.ShouldHaveLoggedWithProperty(new EventId(5101), "RequestType", "PlaceOrderCommand");
logger.Records.ShouldNotHaveLogged(new EventId(5103));
```

Assertions key off the `EventId` and the structured properties — never the rendered message — so rewording a
message template never breaks a test. `AddInMemoryLoggerFactory()` registers the factory for a full host.

### A command through the real pipeline

```csharp
using var harness = new ApplicationPipelineTestHarness();

harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext { Permissions = ["orders.place"] });
harness.Services.AddScoped<IUnitOfWork, FakeUnitOfWork>();
harness.AddBehaviors().AddDefaultBehaviors().AddAuthorizationBehavior().AddTransactionBehavior().Build();
harness.Build<PlaceOrderCommand>();

var result = await harness.SendAsync(new PlaceOrderCommand("ada", 10m));

Assert.True(result.IsSuccess);
```

`WithActivityCapture()` additionally records the spans and metric measurements the pipeline emitted, exposed
as `CapturedActivities` and `CapturedMeasurements`. `AddFakeApplicationBehaviorServices()` registers the
whole seam set (`IRequestContext`, `IUnitOfWork`, `IRequestIdempotencyStore`) in one call.

### A real dependency, in a container

```csharp
public sealed class OrderRepositoryTests : IClassFixture<PostgreSqlContainerFixture>
{
    private readonly PostgreSqlContainerFixture _postgres;

    public OrderRepositoryTests(PostgreSqlContainerFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Saves_and_reads_back()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;
        // ...
    }
}
```

Each fixture starts its container in `InitializeAsync` and exposes a `ConnectionString`, which throws if read
before the container is up. **Docker must be running**, so keep container-backed tests in their own suite — a
machine without Docker fails every one of them.

For a test that needs a `SharedKernelDbContext` without a container, derive from `TestSharedKernelDbContext`
and build options with `TestSharedKernelDbContext.BuildOptions<TContext>(databaseName)`.

### Deterministic data

```csharp
FakerSeeding.Apply();   // once per test assembly, before anything generates

internal sealed class OrderFaker : AggregateRootFaker<Order, OrderId>
{
    public OrderFaker() =>
        CustomInstantiator(f => Order.Place(f.Person.FullName, f.Random.Int(1, 5)));
}

var order = new OrderFaker().Generate();
```

`EntityFaker<TEntity,TId>`, `AggregateRootFaker<TAggregate,TId>`, `TenantedAggregateFaker<,>` and
`SingleValueObjectFaker<,>` are abstract Bogus `Faker<T>` bases: derive one per type and supply a
`CustomInstantiator`, since these types have no public parameterless constructor. `WithClock(clock)` hands
the faker a clock for timestamped construction. `MoneyFaker` is concrete — call `Generate(currency, amount)`.

`FakerSeeding.Apply()` fixes Bogus's global seed, so generated data is identical on every run. Call it from an
assembly fixture, not per test.

## What is in here

| Area | Types |
| --- | --- |
| `Application/` | `FakeRequestContext`, `FakeUnitOfWork`, `FakeRequestIdempotencyStore`, `FakeAuditTrailWriter`, `ApplicationPipelineTestHarness`, `AddFakeApplicationBehaviorServices()` |
| `Caching/` | `FakeCacheService`, `FakeTenantCacheService`, `FakeDistributedLockService`, `FakeRenewableLock`, `FakeRedisChannelService`, `FakeRedisHashService`, `FakeTypedHashStore<T>`, `FakeCacheInvalidationBus`, `FakeCacheWarmupStrategy`, `FakeTenantCacheKeyProvider` |
| `Clocks/` | `FakeClock` |
| `Communication/` | `FakeHttpMessageHandler`, `HttpClientHandlerTestFactory`, `MockServiceEndpointResolver`, `ActivityRecorder`, `AmbientActivityTestHelper`, `GraphQLTestExecutorFactory`, `FakeHttpContextAccessor` |
| `Containers/` | `PostgreSqlContainerFixture`, `RedisContainerFixture`, `RabbitMqContainerFixture`, `MinioContainerFixture`, `ElasticsearchContainerFixture`, `MeilisearchContainerFixture`, `QdrantContainerFixture` |
| `Contracts/` | `IntegrationEventFaker<TEvent>`, `EventEnvelopeBuilder<TEvent>`, `PagedListBuilder<T>`, `PagedListAssertions` |
| `Cryptography/` | Fakes for every `SharedKernel.Cryptography` contract — hashing, symmetric and asymmetric encryption, signing, key providers, envelope encryption, secure random, TOTP replay — plus `AddFakeCryptography()` |
| `DataPrivacy/` | `PiiMaskingAssertions`, `RecordingDataSubjectRequestHandler` |
| `Domain/` | `DomainEventAssertions`, `BusinessRuleAssertions`, `DomainVersionAssertions`, `SpecificationAssert`, `SpecificationTestBuilder<T>`, `MoneyFaker`, `FakeExchangeRateProvider` |
| `Fakers/` | `FakerSeeding`, `EntityFaker<TEntity,TId>`, `SingleValueObjectFaker<TValueObject,TValue>` |
| `FeatureManagement/` | `FakeFeatureManager`, `AddFakeFeatureManagement()` |
| `Integration/` | `InMemoryWebhookDispatcher`, `InMemoryWebhookDeliveryObserver` |
| `Intelligence/` | `InMemoryEmbeddingGenerator` (deterministic, hash-derived vectors), `InMemoryVectorCollection<TRecord>`, `InMemorySemanticKernel`, provisioners and descriptors |
| `Localization/` | `CultureScope` |
| `Logging/` | `InMemoryLogger`, `InMemoryLogger<T>`, `InMemoryLoggerFactory`, `LogRecord`, `LoggerAssertions` |
| `Messaging/` | `InMemoryMessageBus`, `InMemoryEventPublisher`, `TestHarnessFactory` |
| `Notifications/` | `InMemoryNotificationSender`, `InMemoryNotificationDeliveryObserver` |
| `Persistence/` | `FakeRepository<TAggregate,TId>` (evaluates real specifications in memory), `FakeUnitOfWork`, `FakePersistenceTransaction`, `FakeDbConnectionFactory`, `FakeAuditTrailWriter`, `FakeAuditQueryService`, `TestSharedKernelDbContext`, aggregate fakers, `ProjectionSpecificationBuilder<,>` |
| `Reporting/` | `InMemoryReportExporter<TRow>` |
| `Scheduling/` | `InMemoryScheduledJobRegistry` |
| `Search/` | `InMemorySearchIndex<TDocument>`, `InMemorySearchIndexProvisioner`, `InMemorySearchProviderDescriptor` |
| `Security/` | `FakeUserContext`, `FakeTenantProvider`, `SecurityTestContextBuilder`, `DpopTestProofBuilder`, `MtlsTestCertificateBuilder`, `InMemoryApiKeyStore`, `InMemoryDpopReplayCache`, `InMemoryTotpStepUpStore`, `InMemoryRecoveryCodeStore` |
| `ServiceDefaults/` | `InMemoryTenantCatalog`, `StaticTenantProvider`, `FakeTenantResolutionStrategy`, `HealthCheckAssertionExtensions` |
| `Storage/` | `InMemoryFileStorage`, `InMemoryBlobUriGenerator` |
| `Validation/` | `ValidationSampleGenerator` — checksum-correct valid and invalid IBAN, PAN, national-id and similar samples |
| `Workflows/` | `InMemoryWorkflowDispatcher`, `InMemoryWorkflowHandle`, `InMemoryWorkflowHandle<TResult>`, `InMemoryWorkflowStartRecord` |

Most areas also ship an `Add…` extension that registers their doubles in one call — `AddInMemoryMessageBus()`,
`AddInMemoryFileStorage()`, `AddFakeCachingServices()`, `AddInMemoryWorkflowDispatcher()` and the rest.

## The rules these doubles follow

- **A double implements the owning domain's interface, and nothing more.** It is substitutable for the real
  implementation because it satisfies the same contract, so a test never needs a mocking framework for a
  SharedKernel abstraction.
- **No provider dependency ever leaks in.** A fake references only the abstraction package it implements.
- **Behaviour matches the contract, including its failure modes.** `FakeRepository` evaluates a real
  `ISpecification<T>`; `FakeRequestIdempotencyStore` reserves a key atomically and rejects a stale
  reservation token, exactly as the Redis and EF Core stores do.
- **Deterministic by construction.** Fakers are seeded, the embedding generator derives vectors from a hash,
  and the clock only moves when a test moves it.
- **Assertions read state, not text.** Log assertions match an `EventId` and structured properties; message
  assertions match the recorded message instances.

## Adding a double

A double for another domain's interface belongs in the folder for that domain and proves itself through that
domain's own contract tests. A standalone helper with no interface behind it — a builder, an assertion class,
a faker convention — is proven in `SharedKernel.Testing.SelfTests`, which is this library's own suite.

Maintainer rules, the folder-to-domain map and the reference policy live in
[`16.Testing/CLAUDE.md`](../CLAUDE.md).
