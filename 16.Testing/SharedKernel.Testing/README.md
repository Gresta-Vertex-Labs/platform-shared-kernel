# SharedKernel.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**The lightweight core of the SharedKernel testing packages: the basics every test suite needs, with no
infrastructure dependency.** It depends only on the Foundation and Model packages (Primitives, Execution,
DataPrivacy, Validation, Domain, Contracts), Bogus and the `Microsoft.Extensions.*.Abstractions` packages, and on
no test framework.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Testing" />
```

## Contents

| Namespace | What it gives you |
| --- | --- |
| `SharedKernel.Testing.Clocks` | `FakeClock`, a controllable `IClock` |
| `SharedKernel.Testing.Logging` | `InMemoryLogger`, `InMemoryLoggerFactory`, `LogRecord`, `LoggerAssertions`, `AddInMemoryLoggerFactory()`: assert on `EventId`, level and structured properties, never on rendered text |
| `SharedKernel.Testing.Execution` | `TestRequestContext`, a configurable `IRequestContext` (`ForUser`, `ForTenant`, `Service`, `System`, `Anonymous`, `With*`) |
| `SharedKernel.Testing.Application` | `FakeRequestContext`, the pipeline-test flavour (fixed user id, case-insensitive permissions) |
| `SharedKernel.Testing.Fakers` | `EntityFaker`, `SingleValueObjectFaker`, `FakerSeeding` (one seed per assembly) |
| `SharedKernel.Testing.Domain` | Domain-event, business-rule, version and specification assertions, `SpecificationTestBuilder`, `MoneyFaker`, `FakeExchangeRateProvider`, `AddFakeDomainServices()` |
| `SharedKernel.Testing.Persistence` | `AggregateRootFaker`, `TenantedAggregateFaker`, `BulkAggregateFaker`, `ProjectionSpecificationBuilder`, `WithDeletedSpecification` (domain-only; no EF Core) |
| `SharedKernel.Testing.Contracts` | `PagedListBuilder`, `PagedListAssertions`, `IntegrationEventFaker`, `EventEnvelopeBuilder` |
| `SharedKernel.Testing.Validation` | `ValidationSampleGenerator`: checksum-correct valid and invalid IBANs, cards, VAT numbers and other identifiers |
| `SharedKernel.Testing.DataPrivacy` | `PiiMaskingAssertions`, `RecordingDataSubjectRequestHandler` |
| `SharedKernel.Testing.Localization` | `CultureScope`, which sets the current (UI) culture for a block |
| `SharedKernel.Testing.Communication` | `FakeHttpMessageHandler`, `HttpClientHandlerTestFactory`, `ActivityRecorder`, `AmbientActivityTestHelper` (BCL only) |

## The other testing packages

Each capability has its own package, depending on that capability's abstractions (plus this package where it
needs the basics), so a test project takes only what it tests:

| Package | Doubles for |
| --- | --- |
| `SharedKernel.Application.Testing` | The application pipeline (`ApplicationPipelineTestHarness`) |
| `SharedKernel.Persistence.Testing` | Repositories, unit of work, audit trail, cross-tenant scope, PostgreSQL with the production roles |
| `SharedKernel.Caching.Testing` / `SharedKernel.Caching.Redis.Testing` | `ICacheService`, locks, key providers / Redis hashes and pub/sub |
| `SharedKernel.Cryptography.Testing` | Encryption, signing, hashing, random, TOTP |
| `SharedKernel.FeatureManagement.Testing` | OpenFeature `IFeatureClient` |
| `SharedKernel.Messaging.Testing` | `IMessageBus`, `IEventPublisher` |
| `SharedKernel.Storage.Testing` | `IFileStorage` (in-memory stores) |
| `SharedKernel.Search.Testing` | `ISearchIndex<T>`, provisioner, descriptor |
| `SharedKernel.AI.Testing` | Embeddings, vector collections, semantic kernel |
| `SharedKernel.Security.Testing` | `IUserContext`, API-key, DPoP and TOTP stores, certificate builders |
| `SharedKernel.Workflows.Testing` | `IWorkflowDispatcher`, `IWorkflowHandle` |
| `SharedKernel.Scheduling.Testing` | `IScheduledJobRegistry` |
| `SharedKernel.Integration.Testing` | Webhooks and notifications |
| `SharedKernel.Reporting.Testing` | `IReportExporter<TRow>` |
| `SharedKernel.Idempotency.Testing` | `IIdempotencyStore` |
| `SharedKernel.Communication.Testing` | `IServiceEndpointResolver`, gRPC `ServerCallContext` |
| `SharedKernel.Presentation.Testing` | gRPC server context with `HttpContext`, HotChocolate executor, `IHttpContextAccessor` |
| `SharedKernel.ServiceDefaults.Testing` | `ITenantCatalog`, tenant resolution, health checks |

Testcontainers fixtures and the EF Core helpers used by this repository's own suites live in
`SharedKernel.Testing.Internal`, which is never packed.
