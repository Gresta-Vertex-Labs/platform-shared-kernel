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

## Runs

| Date | Skill version | Packages | Placement | Build | Notes |
| --- | --- | ---: | ---: | ---: | --- |
| 2026-10-14 | 1.0.0-rc.4 | 4/6 | 4/6 | 6/6 | Ordering, tasks 4, 6, 8, 11, 14, 19 only, headless `claude -p`. 6 and 8 stopped to ask (the prompts name shipments; Ordering has none). Details on the root board, P-625. |
