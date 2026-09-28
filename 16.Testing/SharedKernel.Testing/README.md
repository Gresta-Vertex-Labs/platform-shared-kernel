# SharedKernel.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **The lightweight core of the SharedKernel test packages: a controllable clock, structured-log capture, a
> configurable caller, deterministic fakers and framework-free assertions for the domain and contract types — with
> no infrastructure and no test-framework dependency.**

| You get | So that |
| --- | --- |
| `FakeClock` | Time-dependent code is tested at a fixed instant, then moved forward on demand |
| `InMemoryLogger` / `InMemoryLoggerFactory` / `LoggerAssertions` | `[LoggerMessage]` output is asserted by `EventId`, level and structured property — never by rendered text |
| `TestRequestContext` / `FakeRequestContext` | Any caller (user, tenant, service, system, anonymous) is one line, mutable between steps |
| Bogus fakers (`EntityFaker`, `SingleValueObjectFaker`, `AggregateRootFaker`, `IntegrationEventFaker`, `MoneyFaker`) + `FakerSeeding` | Test data is realistic and reproducible across CI runs |
| Domain, specification and paging assertions | Domain events, business rules, specifications and `PagedList<T>` are checked without a database |
| `ValidationSampleGenerator` | Checksum-correct valid and invalid IBANs, cards, VAT numbers and national ids on demand |
| `PiiMaskingAssertions`, `RecordingDataSubjectRequestHandler`, `CultureScope`, `FakeHttpMessageHandler`, `ActivityRecorder` | Privacy, localization, HTTP and tracing code is tested with plain BCL doubles |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
  - [1. Assert a structured log event](#1-assert-a-structured-log-event)
  - [2. Act as different callers](#2-act-as-different-callers)
  - [3. Assert the domain events an aggregate raised](#3-assert-the-domain-events-an-aggregate-raised)
  - [4. Test a specification in memory](#4-test-a-specification-in-memory)
  - [5. Generate reproducible test data](#5-generate-reproducible-test-data)
  - [6. Build envelopes and paged results](#6-build-envelopes-and-paged-results)
  - [7. Feed validators valid and invalid identifiers](#7-feed-validators-valid-and-invalid-identifiers)
  - [8. Prove masking and data-subject requests](#8-prove-masking-and-data-subject-requests)
  - [9. Pin the culture](#9-pin-the-culture)
  - [10. Fake an HTTP endpoint and record spans](#10-fake-an-http-endpoint-and-record-spans)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only**. `TestingNeverReferencedByProduction` fails any production project that
references a testing package. Every capability testing package (`SharedKernel.*.Testing`) that needs these basics
brings this one with it.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Primitives`, `SharedKernel.Execution`, `SharedKernel.DataPrivacy`, `SharedKernel.Validation`, `SharedKernel.Domain`, `SharedKernel.Contracts`, `Bogus`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| Namespaces | `SharedKernel.Testing.Clocks`, `.Logging`, `.Execution`, `.Application`, `.Fakers`, `.Domain`, `.Persistence`, `.Contracts`, `.Validation`, `.DataPrivacy`, `.Localization`, `.Communication` |

## Quick start

```csharp
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.Logging;
using Xunit;

public sealed class InvoiceServiceTests
{
    [Fact]
    public async Task An_unpaid_invoice_becomes_overdue_after_30_days()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));
        var caller = TestRequestContext.ForTenant(new TenantId(Guid.NewGuid())).WithPermissions("invoices.write");
        var logger = new InMemoryLogger<InvoiceService>();
        var service = new InvoiceService(clock, caller, logger);

        var invoice = await service.IssueAsync(amount: 100m, CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(31));
        await service.MarkOverdueAsync(CancellationToken.None);

        Assert.True(invoice.IsOverdue);
        logger.Records.ShouldHaveLoggedWithProperty(new EventId(42010), "InvoiceId", invoice.Id);
    }
}
```

`InvoiceService` stands for your own class; `42010` for its `[LoggerMessage]` event id.

## How it works

- **No test framework.** Every assertion throws `InvalidOperationException` with a readable message, so the package
  works under xUnit, NUnit or MSTest alike.
- **Deterministic.** `FakeClock` starts at `2024-01-01T00:00:00Z` when no instant is given, never the real time.
  `MoneyFaker` and `ValidationSampleGenerator` use their own fixed-seed randomizer (`8675309`); your own Bogus fakers
  become reproducible after `FakerSeeding.Apply()`, which sets the process-wide `Randomizer.Seed`.
- **Thread-safe.** `FakeClock`, `InMemoryLogger`, `InMemoryLoggerFactory`, `FakeExchangeRateProvider`,
  `RecordingDataSubjectRequestHandler`, `FakeHttpMessageHandler` and `ActivityRecorder` tolerate parallel test
  collections. `InMemoryLogger` keeps its scope stack in an `AsyncLocal`, so scopes survive `await` and never leak
  between collections, and copies the log state when it is written (some generators reuse a pooled state object).
- **Fail-closed callers.** `TestRequestContext` grants no permission until told, and has no tenant unless given one —
  like the platform's authorization and tenant filters.
- **Faithful builders.** `EventEnvelopeBuilder` builds through `EventEnvelope.Wrap` and `PagedListBuilder` through
  `PagedList<T>.Create`, so invalid values fail exactly as in production.
- **Simplifications.** `InMemoryLoggerFactory` is the whole logging pipeline (`AddProvider` is a no-op).
  `SpecificationAssert` and `SpecificationTestBuilder` evaluate `Criteria` only — no ordering, paging or soft-delete
  filter (for those, use `FakeRepository` in
  [`SharedKernel.Persistence.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Persistence.Testing/README.md)).
  `HttpClientHandlerTestFactory`'s header handlers are stand-ins, not the platform's propagation handlers.
- **Lifetimes.** `AddInMemoryLoggerFactory()` and `AddFakeDomainServices()` register singletons, so captured state
  outlives any DI scope.

## Recipes

### 1. Assert a structured log event

Through DI — register before `AddLogging()`, because `AddInMemoryLoggerFactory()` uses `TryAdd`:

```csharp
var services = new ServiceCollection();
services.AddInMemoryLoggerFactory();                    // ILoggerFactory + ILogger<T>
services.AddScoped<InvoiceService>();
await using var provider = services.BuildServiceProvider();

await provider.GetRequiredService<InvoiceService>().MarkOverdueAsync(ct);

var loggers = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
var records = loggers.GetLogger(typeof(InvoiceService).FullName!).Records;
records.ShouldHaveLogged(new EventId(42011), LogLevel.Warning);
records.ShouldNotHaveLogged(new EventId(42012));
```

`LogRecord` carries `EventId`, `LogLevel`, `Message`, `State`, `Exception` and `Scopes` (outer to inner);
`TryGetProperty(name, out value)` reads one structured property by exact name. `Clear()` empties a logger between
phases of one test.

### 2. Act as different callers

```csharp
var caller = TestRequestContext.ForTenant(tenantA).WithPermissions("orders.read");
caller.TenantId = tenantB;                                   // now act as tenant B
var job = TestRequestContext.System("nightly-close", tenantA);
var client = TestRequestContext.Service("billing-api");      // ActorKind.Service
var guest = TestRequestContext.Anonymous();                  // no subject, not authenticated
```

Permissions compare ordinally. `FakeRequestContext` (`SharedKernel.Testing.Application`) is the pipeline flavour:
authenticated, a fixed GUID-shaped `UserId`, case-insensitive permissions.

### 3. Assert the domain events an aggregate raised

```csharp
order.Pay(payment, clock);

var paid = order.DomainEvents.ContainsEventOfType<OrderPaid>();
order.DomainEvents.HasRaisedExactlyNEvents(1);
order.DomainEvents.HasNoEventsOfType<OrderCancelled>();
DomainVersionAssertions.ShouldHaveVersion<OrderPaid>(2);     // [DomainEventVersion(2)]
new OrderMustBePaidRule(order).ShouldNotBeBroken();          // IBusinessRule
```

### 4. Test a specification in memory

```csharp
var spec = Spec.For<Order>().Where(o => o.Total > 100m);

SpecificationAssert.Satisfies(spec, bigOrder);
SpecificationAssert.DoesNotSatisfy(spec, smallOrder);
SpecificationTestBuilder<Order>.For(spec)
    .Against([bigOrder, smallOrder])
    .ExpectCount(1)
    .ExpectMatch(o => o.Total > 100m)
    .Assert();

var withDeleted = WithDeletedSpecification<Order>.Wrap(spec);          // same spec, IncludeDeleted
var projection = new ProjectionSpecificationBuilder<Order, decimal>()
    .WithCriteria(o => o.Total > 0).WithSelector(o => o.Total).Build();
```

### 5. Generate reproducible test data

```csharp
FakerSeeding.Apply();                                    // once per test assembly, e.g. a module initializer

public sealed class OrderFaker : AggregateRootFaker<Order, Guid>
{
    public OrderFaker() => CustomInstantiator(f => new Order(f.Random.Guid(), f.Person.FullName, new FakeClock()));
}

List<Order> orders = new BulkAggregateFaker<Order, Guid>(new OrderFaker()).Generate(500);
Money price = new MoneyFaker().Generate(Currency.Eur);   // omit the currency: USD, EUR, JPY or BHD
```

`EntityFaker<TEntity, TId>` adds `WithClock(IClock)` (read `Clock` inside your rules);
`SingleValueObjectFaker<TValueObject, TValue>` adds `WithValue(value)` / `WithRandomValue(f => …)` and calls the
single-argument constructor (override `CreateFrom` otherwise). `IntegrationEventFaker<TEvent>` subclasses call
`RuleForEventId()` and `RuleForOccurredOn()` in their constructor. The faker bases declare no rules of their own —
your domain invariants stay in your faker.

### 6. Build envelopes and paged results

```csharp
EventEnvelope<OrderPlaced> envelope = new EventEnvelopeBuilder<OrderPlaced>()
    .WithData(new OrderPlaced(Guid.NewGuid(), DateTimeOffset.UtcNow, orderId))
    .WithTenantId(tenantId.Value)
    .WithSubject($"order/{orderId}")
    .Build();                              // defaults: source "test-service", a new correlation id

PagedList<OrderDto> page = new PagedListBuilder<OrderDto>().WithItems(dtos).WithPage(2).WithPageSize(10)
    .WithTotalCount(35).Build();

page.ShouldHaveTotalCount(35);
cursorPage.ShouldHaveItems(a, b);
string next = cursorPage.ShouldHaveNextPage();
```

`TEvent` must be a `sealed` `IIntegrationEvent` with a valid `[IntegrationEvent("name", Version = n)]`.

### 7. Feed validators valid and invalid identifiers

```csharp
Assert.True(Iban.Create(ValidationSampleGenerator.ValidIban("NL")).IsSuccess);
Assert.False(Iban.IsValid(ValidationSampleGenerator.InvalidIban("NL")));   // only the checksum is wrong
```

Each `Invalid*` sample changes exactly one character of a valid one, so only the intended check fails.

### 8. Prove masking and data-subject requests

```csharp
PiiMaskingAssertions.ShouldBeMasked("ada@example.com", observedInAuditLog, PiiMasking.Email);

var handler = new RecordingDataSubjectRequestHandler(clock);
await sut.EraseCustomerAsync(request, ct);
handler.ShouldHaveErased(request.SubjectId);
```

Unconfigured subjects get an empty export and an empty erasure receipt; `SetExportResult`/`SetErasureResult` return a
chosen `Result` per subject, and a repeated `RequestId` returns the first outcome again, as the contract requires.

### 9. Pin the culture

```csharp
using (new CultureScope("de-DE"))
{
    Assert.Equal("1.234,50", 1234.5m.ToString("N2"));   // code under test formats with the current culture
}                                                   // CurrentCulture and CurrentUICulture restored, even on throw
```

### 10. Fake an HTTP endpoint and record spans

```csharp
var http = new FakeHttpMessageHandler();
http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK));        // then DefaultResponse (200)
var client = new HttpClient(http) { BaseAddress = new Uri("https://inventory.test") };

using var spans = ActivityRecorder.StartRecording("SharedKernel.Application");
// … exercise the code …
Assert.Equal(2, http.Requests.Count);
Assert.NotEmpty(spans.RecordedActivities);
```

`AmbientActivityTestHelper.Start(traceId)` sets `Activity.Current` for a block and restores it on dispose. For a typed
REST client with the platform's resilience and propagation, use `StubHttpMessageHandler` from
[`SharedKernel.Communication.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Communication.Testing/README.md).

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddInMemoryLoggerFactory(this IServiceCollection)` | `InMemoryLoggerFactory` as `ILoggerFactory` and `Logger<T>` as `ILogger<T>`, singletons, `TryAdd` |
| `AddFakeDomainServices(this IServiceCollection)` | `FakeClock` as `IClock`, singleton |

### Types by namespace (`SharedKernel.Testing.*`)

| Namespace | Type | Implements / surface |
| --- | --- | --- |
| `.Clocks` | `FakeClock(DateTimeOffset? initial = null)` | `IClock`; `UtcNow` (settable), `Today`, `Set`, `SetUtcNow`, `Advance(TimeSpan)` |
| `.Logging` | `InMemoryLogger`, `InMemoryLogger<T>` | `ILogger` / `ILogger<T>`; `Records`, `MinLevel` (default `Trace`), `Clear()` |
| | `InMemoryLoggerFactory` | `ILoggerFactory`; `GetLogger(category)`, `Loggers` |
| | `LogRecord` | `EventId`, `LogLevel`, `Message`, `State`, `Exception`, `Scopes`, `TryGetProperty` |
| | `LoggerAssertions` | `ShouldHaveLogged(eventId[, level])`, `ShouldHaveLoggedWithProperty(eventId, name, value)`, `ShouldNotHaveLogged`, `ShouldHaveLoggedCount` |
| `.Execution` | `TestRequestContext` | `IRequestContext`; `ForUser`, `ForTenant`, `Service`, `System`, `Anonymous`, `WithTenant`, `WithPermissions`, `WithSession`, `WithImpersonator`, settable properties, `DefaultUserId = "test-user"` |
| `.Application` | `FakeRequestContext` | `TestRequestContext` with a fixed user id and case-insensitive `HasPermission` |
| `.Fakers` | `EntityFaker<TEntity, TId>`, `SingleValueObjectFaker<TValueObject, TValue>` | Abstract `Faker<T>` bases |
| | `FakerSeeding` | `Apply(int seed = 8675309)` |
| `.Domain` | `DomainEventAssertions` | `ContainsEventOfType<T>`, `ContainsExactly<T>(n)`, `ContainsEventWithVersion<T>(v)`, `HasNoEvents`, `HasNoEventsOfType<T>`, `HasRaisedExactlyNEvents(n)` |
| | `BusinessRuleAssertions`, `DomainVersionAssertions` | `ShouldBeBroken`, `ShouldNotBeBroken`; `ShouldHaveVersion<T>(v)`, `ShouldBeVersioned<T>()` |
| | `SpecificationAssert`, `SpecificationTestBuilder<T>` | `Satisfies`, `DoesNotSatisfy`; `For`, `Against`, `ExpectCount`, `ExpectMatch`, `Assert` |
| | `MoneyFaker`, `FakeExchangeRateProvider`, `FakeDomainNotFoundException` | `Generate`, `GenerateMany`; `IExchangeRateProvider` with `SeedRate`, `SimulateFailure`; `For<TAggregate>(id)` |
| `.Persistence` | `AggregateRootFaker<,>`, `TenantedAggregateFaker<,>`, `BulkAggregateFaker<,>` | Abstract faker bases; `Generate(count)` |
| | `WithDeletedSpecification<T>`, `ProjectionSpecificationBuilder<TAggregate, TResult>` | `Wrap(spec)`; `WithCriteria`, `WithSelector`, `Build` |
| `.Contracts` | `EventEnvelopeBuilder<TEvent>`, `IntegrationEventFaker<TEvent>` | `WithData`, `WithSource`, `WithSubject`, `WithTenantId`, `WithCorrelationId`, `WithCausationId`, `Build` |
| | `PagedListBuilder<T>`, `PagedListAssertions` | `WithItems`, `WithPage`, `WithPageSize`, `WithRequest`, `WithTotalCount`, `Build`, `Empty()`; `ShouldHaveTotalCount`, `ShouldHaveItems`, `ShouldBeEmpty`, `ShouldHaveNextPage`, `ShouldBeLastPage` |
| `.Validation` | `ValidationSampleGenerator` | `Valid`/`Invalid` × `Iban`, `Bic`, `Pan`, `CurrencyCode`, `CountryCode`, `E164Phone`, `Vat`, `NationalId` (TR only) |
| `.DataPrivacy` | `PiiMaskingAssertions`, `RecordingDataSubjectRequestHandler` | `ShouldBeMasked`; `IDataSubjectRequestHandler` with `ExportRequests`, `ErasureRequests`, `Set*Result`, `ShouldHaveExported`, `ShouldHaveErased` |
| `.Localization` | `CultureScope` | `IDisposable`; `(string cultureName)` or `(CultureInfo)` |
| `.Communication` | `FakeHttpMessageHandler` | `HttpMessageHandler`; `EnqueueResponse`, `DefaultResponse`, `Requests` |
| | `HttpClientHandlerTestFactory`, `ActivityRecorder`, `AmbientActivityTestHelper` | `WithInnerHandler`, `WithCorrelationIdHandler`, `WithTenantIdHandler`, `Build`; `StartRecording(source)`, `RecordedActivities`; `Start(traceId, parentSpanId?)` |

### Errors returned by the doubles

| Double | Code | Type | When |
| --- | --- | --- | --- |
| `FakeExchangeRateProvider` | `exchange_rate.not_found` | NotFound | No rate seeded for the pair |
| `FakeExchangeRateProvider` | `exchange_rate.simulated_failure` | Unexpected | `SimulateFailure = true` |

## Testing

This package is the test toolkit; its self-tests live in
[`SharedKernel.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/16.Testing/SharedKernel.Testing/SharedKernel.Testing.Tests)
and prove each helper against the real domain, contract, validation and privacy types. For a capability's fakes add
its own package — `SharedKernel.Application.Testing`, `SharedKernel.Persistence.Testing`,
`SharedKernel.Messaging.Testing`, `SharedKernel.Caching.Testing` and the rest, listed in the
[16.Testing overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/README.md).

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference it from production code | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build |
| Assert on `LogRecord.Message` text | `ShouldHaveLoggedWithProperty(eventId, name, value)` | Templates change; event ids and property names are the contract |
| Call `AddInMemoryLoggerFactory()` after `AddLogging()` | Call it first | It uses `TryAdd`, so an earlier `ILoggerFactory` wins |
| Leave Bogus unseeded | `FakerSeeding.Apply()` once per test assembly | Unseeded fakers produce different data on every run |
| Expect `SpecificationAssert` to apply ordering, paging or soft delete | Use `FakeRepository` or PostgreSQL | It evaluates `Criteria` only |
| Expect permissions on a fresh `TestRequestContext` | `WithPermissions(…)` | It grants none, like production authorization |
| Use `HttpClientHandlerTestFactory` to test the platform's header propagation | Test typed clients with `SharedKernel.Communication.Testing` | Its handlers are simple stand-ins |
| Keep an `ActivityRecorder` or `CultureScope` alive past the test | `using` them | Listeners and culture are process- or flow-wide |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
