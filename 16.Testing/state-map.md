# 16.Testing — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

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

| Phase key | Phase | Status |
| --- | --- | :---: |
| `SK.16.Design` | Design (D-01–D-243) | ● |
| `SK.16.Scaffold` | Scaffold (S-01–S-66) | ● |
| `SK.16.Core` | Core (C-01–C-150) | ● |
| `SK.16.Tests` | Tests (T-01–T-105) | ● |
| `SK.16.Docs` | Docs (DO-01–DO-59) | ● |
| `SK.16.Published` | Published | ● |

## Open Work

None — every phase in this domain is complete. (The last rows still marked open — D-217, D-218, S-56, T-91, DO-49, the P-450/WO-068 async `FakeEncryptionKeyProvider` + `FakeEnvelopeEncryptionProvider` work — were verified on disk 2026-09-28: both types, their async members and their tests ship in `SharedKernel.Cryptography.Testing`.)

## Blocked

None. Every historical "fake designed ahead of its upstream interface" blocker has cleared; each upstream type now exists.

## Cross-Domain Dependencies

None open.

## Completed Phases

- WO-086 ● Monolithic `SharedKernel.Testing` split into a lightweight core + nineteen packable capability packages + non-packable `Testing.Internal` (P-565, P-567, P-568, P-569, P-571, P-574, P-575) (2026-09-26)
- P-527 ● `ITotpReplayGuard` fake follows `01.Core`'s P-514 break (WO-083) (2026-09-09)
- P-502 ● AAD-enforcing symmetric fake, synchronous key-provider gate — gate for the WO-081 wave (2026-09-08)
- P-441, P-445, P-450, P-453, P-459, P-463, P-467, P-470, P-473, P-475, P-481, P-485 ● Twelve-phase batch: Money, Validation, async key provider/envelope, TOTP, auditing, notifications, scheduling, gRPC, tenant catalog, data privacy, reporting, `CultureScope` (WO-066–WO-078) (2026-09-04)
- P-438 ● `FakeTenantCacheService` (WO-065) (2026-08-24)
- P-431 ● `Integration/` fakes (WO-064) (2026-08-21)
- P-391 ● Security fixture builders (WO-060) (2026-08-17)
- P-382 ● `SecurityTestContextBuilder`, step-up `FakeUserContext` members (WO-058) (2026-08-17)
- P-374 ● `FakeUserContext` permissions (WO-057) (2026-08-13)
- P-355 ● Search bulk-write throttle (WO-055) (2026-08-11)
- P-352 ● Messaging publish-context doubles (WO-054) (2026-08-07)
- P-335 ● Persistence fakes (WO-053) (2026-08-03)
- P-330 ● Envelope assertions namespace (WO-052) (2026-07-31)
- P-306 ● Redis channel/hash fakes (WO-050) (2026-07-29)
- P-300 ● Cryptography and FeatureManagement fakes (WO-049) (2026-07-28)
- P-288 ● Workflows dispatcher double (WO-046) (2026-07-23)
- P-283 / P-284 ● Qdrant fixture, AI doubles (WO-045; Milvus fixture retracted with WO-048) (2026-07-22)
- P-275 / P-276 ● Meilisearch/Elasticsearch fixtures, search doubles (WO-044) (2026-07-19)
- P-268 / P-269 ● MinIO fixture, storage doubles (WO-043) (2026-07-18)
- P-258 ● `Logging/` in-memory logger (WO-041) (2026-07)
- P-244 / P-245 ● Application pipeline doubles (WO-040) (2026-07)
- P-226 ● `ActivityRecorder` (WO-036) (2026-07)
- Earlier phases (P-035, P-064, P-179–P-189; WO-029, WO-030) — archived. P-190 was rejected at design (its premise did not exist).

## Changelog

- [2026-09-28] State map rewritten as a living board; the P-450 rows (D-217, D-218, S-56, T-91, DO-49) closed after on-disk verification.
- [2026-09-26] WO-086 foundation refactor recorded — twenty packable Testing-tier packages.
- [2026-09-08] P-502 (WO-081) implemented end to end; `SK.16.Core` 150/150.
- [2026-09-08] P-502 (WO-081) processed — the gate for the `01.Core`-first breaking wave.
- [2026-08-26] Twelve-phase batch dispatch processed (WO-066–WO-078).
