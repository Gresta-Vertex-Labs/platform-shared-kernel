# Consumer skill evaluation set

Task prompts for scoring the `sharedkernel` skill. Run them as described in
[`../README.md`](../README.md#evaluation): a copy of a Shop service with the packed skill in
`.claude/skills/sharedkernel/`, one fresh session per prompt.

Score each task on three points (0 or 1 each):

- **Packages:** the assistant adds the expected packages, and no package for something the kernel already covers.
- **Placement:** each package goes into the expected project.
- **Build:** the service still builds with no new analyzer warning.

Packages are named without the `SharedKernel.` prefix. Projects: **D** Domain · **A** Application ·
**I** Infrastructure · **H** Api/Worker · **T** Tests.

| # | Task prompt | Expected packages → project | Recipe |
| ---: | --- | --- | --- |
| 1 | "Create a new `Shipping` service with the standard four projects, health checks and telemetry." | `Domain` → D; `Application` → A; `Persistence.EfCore` → I; `ServiceDefaults`, `Application.Pipeline`, `Application.Mediator.MediatR`, `Presentation.WebApi` → H | new-service.md |
| 2 | "Store `Shipment` aggregates in PostgreSQL and load them by id." | `Persistence.Abstractions` → A; `Persistence.EfCore` → I; `ServiceDefaults.Persistence` → H | add-persistence.md |
| 3 | "Add `POST /shipments` that creates a shipment and returns 201 with its location." | `Presentation.WebApi` → H (endpoint module sends a command through `ISender`) | add-http-endpoint.md |
| 4 | "Publish `OrderPlaced` when an order is confirmed, without losing it if the broker is down." | `Messaging.Abstractions` → A; `Messaging.MassTransit`, `Messaging.MassTransit.RabbitMq`, `Messaging.MassTransit.EfCore` → I | add-messaging.md |
| 5 | "Consume `OrderPlaced` from the Ordering service and create a shipment, processing each message only once." | `Messaging.MassTransit` (+ `.RabbitMq`) → I; `Idempotency.EfCore` or `Idempotency.Redis` → I | add-messaging.md |
| 6 | "Cache `GetShipment` for five minutes and clear it when the shipment changes." | `Application` markers → A; `Application.Pipeline.Caching` → H; `Caching.FusionCache` → I | add-caching.md |
| 7 | "Share the cache between all replicas using Redis." | `Caching.Redis` (with `Caching.Redis.Core`) → I | add-caching.md |
| 8 | "Protect the API with our OpenID Connect provider; only callers with `shipments.write` may create shipments." | `Security.Oidc`, `ServiceDefaults.Security` → H; `[RequirePermission]` from `Application` → A | add-authentication.md |
| 9 | "Each tenant must only see its own shipments, enforced in the database too." | `MultiTenancy` → H; `Persistence.EfCore` (row-level security) → I; tenanted aggregate base from `Domain` → D | add-multi-tenancy.md |
| 10 | "A client may retry `POST /shipments` with the same `Idempotency-Key`; never create two shipments." | `Idempotency.Abstractions` → A; `Idempotency.Redis` → I; `app.WithIdempotency()` in `Application.Pipeline` → H | add-idempotency.md |
| 11 | "Run the stock reconciliation every night at 02:00 on one replica only." | `Scheduling` → I (with `Caching.Redis.DistributedLocking` → I) | add-background-job.md |
| 12 | "Fulfil an order in steps (reserve stock, charge, ship) and undo the earlier steps if a later one fails." | `Workflows.Temporal` → I | add-long-running-workflow.md |
| 13 | "Call the Billing service's REST API to charge a card and handle its errors as results." | `Communication`, `Communication.Rest` → I | add-outbound-call.md |
| 14 | "Let users upload a shipment label PDF directly to storage, per tenant." | `Storage.Abstractions` → A; `Storage.S3` → I | add-file-storage.md |
| 15 | "Add typo-tolerant search over shipments by address and recipient name." | `Search.Abstractions` → A; `Search.Meilisearch` → I | add-search.md |
| 16 | "Suggest similar past shipments using embeddings." | `AI.Abstractions` → A; `AI.Qdrant`, `AI.SemanticKernel` → I | add-ai.md |
| 17 | "Email the customer when the shipment is dispatched, and notify the merchant's system with a signed webhook." | `Integration.Notifications.Abstractions` → A; `Integration.Notifications.Email.SendGrid`, `Integration.Webhooks` → I | add-notifications-webhooks.md |
| 18 | "Export all shipments of a month as an Excel file that the user downloads from a link." | `Reporting.Abstractions` → A; `Reporting.Spreadsheet`, `Storage.S3` → I | add-report-export.md |
| 19 | "Unit-test the create-shipment command without a database or broker, then add one integration test against real PostgreSQL." | `Testing`, `Application.Testing`, `Persistence.Testing`, `Messaging.Testing` → T | write-tests.md |

## Prompts for a staged service

The prompts above are written for a new "Shipping" service. A run on a staged Shop service uses the variant below,
phrased in that service's own vocabulary and asking for something it does not do yet; a task without a variant uses
its generic prompt. A prompt that names a concept the service lacks, or asks for something it already does, scores
the eval set, not the skill.

Headless runs (`claude -p`) cannot answer a question, so every prompt ends with: *"Implement it in this service. If
something is ambiguous, make the most reasonable assumption, state it, and proceed; then make sure the solution
builds."*

### Ordering

| # | Variant | Expected packages → project |
| ---: | --- | --- |
| 4 | "When an order is cancelled, also publish a new `OrderRefundRequested` integration event for Billing, without losing it if the broker is down." | contract in the contracts project; `Messaging.Abstractions` → A (already referenced); the outbox (`Messaging.MassTransit.EfCore`) is already wired → I |
| 6 | "Cache `GetOrderQuery` for five minutes per tenant and clear it whenever the order changes." | `Application` markers and `Caching.Abstractions` (`CachePolicy`) → A; `Application.Pipeline.Caching` → H; `Caching.FusionCache` (+ `Caching.Redis`) → I |
| 8 | "Let the warehouse system read an order (`GET /orders/{id}`) with an API key instead of an OIDC token; keys must be revocable." | `Security.ApiKey` → H |
| 11 | the generic prompt | `Scheduling`, `Caching.Redis.DistributedLocking` → I |
| 14 | "Let clients upload an order's invoice PDF directly to storage with a presigned URL, without the file passing through this API, per tenant." | `Storage.Abstractions` → A; `Storage.S3` → I; `CreateUploadUrlAsync` on a tenant store |
| 19 | "Unit-test the cancel-order command without a database or broker, then add one integration test against real PostgreSQL." | `Testing`, `Persistence.Testing` (already referenced) → T; `FakeRepository`, `PostgresTestServer` |

## Greenfield

The service template's real situation: an empty folder with only the skill, a `Directory.Packages.props` pinning
`SharedKernelVersion` to a published version, and a `NuGet.Config` on GitHub Packages. Stage it with
`eng/stage-consumer-skill-eval.sh --empty <out-dir>`. The four steps run in order, one fresh session each, and every
step builds on the previous one's output (no reset between steps). Each prompt ends with the assume-and-proceed
sentence above.

Besides packages, placement and build, score the analyzer warnings left and whether the projects would pass the
reference platform's service-shape rules: Domain → no project and only Foundation/Model packages; Application →
Domain only, no Adapter or Host package and no MediatR; Infrastructure → Application only, direct kernel packages
Adapter or Foundation; Api → Application and Infrastructure, direct kernel packages Host only; no production
project reaches a Testing package.

| Step | Prompt | Expected packages → project |
| --- | --- | --- |
| a | "Create a new service named Shipping using SharedKernel: Domain, Application, Infrastructure and Api projects, a Shipment aggregate, health checks and telemetry." | `Domain` → D; `Application` → A; `ServiceDefaults`, `Application.Pipeline`, `Application.Mediator.MediatR` → H; `Analyzers` in every project; new-service.md |
| b | "Add EF Core on PostgreSQL for Shipment, with multi-tenancy enforced by row-level security." | `Persistence.Abstractions` → A; `Persistence.EfCore` → I; `ServiceDefaults.Persistence`, `ServiceDefaults.Security` and an authentication package (the tenant claim's source, e.g. `Security.Oidc`) → H, `MultiTenancy` optional; tenanted aggregate base in D; add-persistence.md, add-multi-tenancy.md |
| c | "Add HTTP endpoints to create a shipment and to get one by id; they send through ISender." | `Presentation.WebApi` → H; commands and queries in A; `IEndpointModule`; add-http-endpoint.md |
| d | "Add one unit test for creating a shipment and one integration test against real PostgreSQL." | `Testing`, `Application.Testing`, `Persistence.Testing` → T; the integration test keeps row-level security on (`EnableRowLevelSecurityAsync`); write-tests.md |

## Runs

| Date | Skill version | Packages | Placement | Build | Notes |
| --- | --- | ---: | ---: | ---: | --- |
| 2026-10-14 | 1.0.0-rc.4 | 4/6 | 4/6 | 6/6 | Ordering, tasks 4, 6, 8, 11, 14, 19 only, headless `claude -p`. 6 and 8 stopped to ask (the prompts name shipments; Ordering has none). Per-task table and fault analysis: commit f9e60ea0 (root `state-map.md`, P-625). |
| 2026-10-14 | 1.0.0-rc.4 + fixes | 6/6 | 6/6 | 6/6 | Second run, same six tasks with the Ordering variants and the assume-and-proceed suffix, after the recipe-folder wording in SKILL.md and the proven presigned upload in add-file-storage.md. Every run opened the right recipe first; 14 now uses `CreateUploadUrlAsync`. The other 13 tasks have not been run headless yet. |
| 2026-10-14 | 1.0.0-rc.4 (feed) + main skill | 3/4 | 4/4 | 4/4 | Greenfield, run 1 (empty folder, packages restored from GitHub Packages, one `claude -p` per step). a pass. b: EF Core with `TenantedDbContext` and RLS on, but no tenant source (no authentication), so every request would fail closed; recipe fault. c pass. d: the integration test turned RLS off; recipe fault (`write-tests.md` had no proven PostgreSQL step). ServiceShape 11/11. Warnings left: 8 NU1903 in the test project (SSH.NET via `Persistence.Testing` → Testcontainers; packaging fault). |
| 2026-10-14 | 1.0.0-rc.4 (feed) + fixes | 3/4 | 4/4 | 4/4 | Greenfield, b–d re-run twice from step a's output after the fixes: `add-multi-tenancy.md`/`add-persistence.md` say tenancy needs authentication; `write-tests.md` gains a proven RLS-on `PostgresTestServer` step (Shop: `CatalogTenantIsolationPostgresTests`). d now keeps RLS on (`EnableRowLevelSecurityAsync`); c pass. b still adds no authentication: it quotes the recipe and defers authentication as out of scope (not a routing, recipe or README fault any more). ServiceShape 11/11; the same 8 NU1903 warnings. |
