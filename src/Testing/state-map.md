# 16.Testing — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

Every package is Testing tier and referenced only by test projects (`TestingNeverReferencedByProduction`). Each `SharedKernel.{Capability}.Testing` references only its capability's contracts. Split out of the former monolithic `SharedKernel.Testing` by WO-086 (P-571).

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Testing` | Testing | ● | Lightweight packable core (Foundation + Model references only): `FakeClock`, in-memory `ILogger`/`ILoggerFactory`, `TestRequestContext`, fakers, assertions, `CultureScope`, `ActivityRecorder`. |
| `SharedKernel.Testing.Internal` | Testing | ● | Not packable. Testcontainers fixtures (PostgreSQL, Redis, RabbitMQ, MinIO, Meilisearch, Elasticsearch, Qdrant), EF Core/Npgsql/audit helpers, MassTransit `TestHarnessFactory` — this repo's own tests only. |
| `SharedKernel.AI.Testing` | Testing | ● | In-memory embedding generator, vector collection and kernel doubles. |
| `SharedKernel.Application.Testing` | Testing | ● | `ApplicationPipelineTestHarness.Build()` and local-seam doubles for the pipeline. |
| `SharedKernel.Caching.Testing` | Testing | ● | `FakeCacheService`, `FakeTenantCacheService`, fake lock service, `AddFakeCachingServices()`. |
| `SharedKernel.Caching.Redis.Testing` | Testing | ● | Fake Redis channel/hash/typed-hash services, `AddFakeRedisServices()`. |
| `SharedKernel.Communication.Testing` | Testing | ● | `StubHttpMessageHandler` + `UseStubHttpMessageHandler(clientName, stub)`, `GrpcCalls`. |
| `SharedKernel.Cryptography.Testing` | Testing | ● | Fake symmetric (AAD-enforcing), async key provider, envelope provider, remote key provider, hasher, TOTP doubles; `AddFakeCryptography()`. |
| `SharedKernel.FeatureManagement.Testing` | Testing | ● | `FakeFeatureClient`. |
| `SharedKernel.Idempotency.Testing` | Testing | ● | `FakeIdempotencyStore` + `AddFakeIdempotencyStore(purposes)`. |
| `SharedKernel.Integration.Testing` | Testing | ● | `InMemoryWebhookDispatcher`, `InMemoryNotificationSender`. |
| `SharedKernel.Messaging.Testing` | Testing | ● | `InMemoryMessageBus`, `InMemoryEventPublisher` and their DI extension. |
| `SharedKernel.Persistence.Testing` | Testing | ● | `AddFakeRepository<T,TId>()`, `AddFakeUnitOfWork()` (`TransientFailures`), `AddFakeAuditTrailWriter()`, `AddFakeCrossTenantScope()`, `FakeDbConnectionFactory`, `PostgresTestServer`/`PostgresTestDatabase`. |
| `SharedKernel.Presentation.Testing` | Testing | ● | HTTP, gRPC and GraphQL test helpers. |
| `SharedKernel.Reporting.Testing` | Testing | ● | `InMemoryReportExporter`/`InMemoryReportExporterFactory`/`InMemoryHtmlToPdfConverter` + `AddInMemoryReporting()`. |
| `SharedKernel.Scheduling.Testing` | Testing | ● | `InMemoryScheduledJobRegistry`. |
| `SharedKernel.Search.Testing` | Testing | ● | `InMemorySearchIndex<T>` (incl. bulk-write throttle), provisioner and descriptor doubles. |
| `SharedKernel.Security.Testing` | Testing | ● | `FakeUserContext`, `SecurityTestContextBuilder`, test certificates, DPoP proofs. |
| `SharedKernel.ServiceDefaults.Testing` | Testing | ● | `FakeTenantResolutionStrategy`, `InMemoryTenantCatalog`. |
| `SharedKernel.Storage.Testing` | Testing | ● | `InMemoryFileStorage` via `AddInMemoryStore(name)`. |
| `SharedKernel.Workflows.Testing` | Testing | ● | `InMemoryWorkflowDispatcher`. |

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. (The last rows still marked open — D-217, D-218, S-56, T-91, DO-49, the P-450/WO-068 async `FakeEncryptionKeyProvider` + `FakeEnvelopeEncryptionProvider` work — were verified on disk 2026-09-28: both types, their async members and their tests ship in `SharedKernel.Cryptography.Testing`.)

## Blocked

None. Every historical "fake designed ahead of its upstream interface" blocker has cleared; each upstream type now exists.

## Cross-Domain Dependencies

None open.
