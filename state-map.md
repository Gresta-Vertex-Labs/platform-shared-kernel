# Platform.SharedKernel — State Map

> Living cross-domain board: what exists, what is open, and the next free IDs. Each domain's own `state-map.md` holds its package board and open phase keys. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## ID Counters

| Counter | Next free | Note |
| --- | --- | --- |
| Next phase id | P-619 | Highest used: P-618 (WO-087) |
| Next work order | WO-088 | Highest used: WO-087 |

Phases written by `arch-lead` take the next phase id; a user-directed phase without a work order still takes one, so no id is ever reused.

## Domain Summary Board

| # | Domain | Packages | Status | Open items |
| --- | --- | --- | --- | --- |
| 00 | [Governance](tools/Governance/state-map.md) | 3 (Tooling) + dev-only Benchmarks | ○ | Next: `SK.00.ObservabilityEnforce` (WO-087, after Phase 2) |
| 01 | [Core](src/Foundation/state-map.md) | 13 (10 Foundation, 3 Adapter) | ● | None |
| 02 | [Caching](src/Infrastructure/Caching/state-map.md) | 7 (1 Abstractions, 6 Adapter) | ● | None |
| 03 | [Domain](src/Model/Domain/state-map.md) | 1 (Model) | ● | None |
| 04 | [Contracts](src/Model/Contracts/state-map.md) | 1 (Model) | ● | None |
| 05 | [Application](src/Application/state-map.md) | 4 (1 Abstractions, 3 Host) | ● | None |
| 06 | [Persistence](src/Infrastructure/Persistence/state-map.md) | 6 (1 Abstractions, 5 Adapter) | ● | None |
| 07 | [Messaging](src/Infrastructure/Messaging/state-map.md) | 5 (1 Abstractions, 4 Adapter) | ● | None |
| 08 | [Storage](src/Infrastructure/Storage/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 09 | [Search](src/Infrastructure/Search/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 10 | [Intelligence](src/Infrastructure/AI/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 11 | [Communication](src/Infrastructure/Communication/state-map.md) | 3 (Adapter) | ● | None |
| 12 | [Security](src/Hosting/Security/state-map.md) | 5 (1 Abstractions, 4 Host) | ● | None |
| 13 | [ServiceDefaults](src/Hosting/ServiceDefaults/state-map.md) | 7 (Host) | ○ | Next: `SK.13.ServiceDefaultsConformance` (WO-087) |
| 14 | [Presentation](src/Hosting/Presentation/state-map.md) | 6 (Host) + WebApi generator (Tooling) | ○ | Next: `SK.14.PresentationConformance` (WO-087) |
| 15 | [Integration](src/Infrastructure/Integration/state-map.md) | 4 (1 Abstractions, 3 Adapter) | ○ | Next: `SK.15.IntegrationConformance` (WO-087) |
| 16 | [Testing](src/Testing/state-map.md) | 21 (Testing; 20 packable + `Testing.Internal`) | ● | None |
| 17 | [Workflows](src/Infrastructure/Workflows/state-map.md) | 1 (Adapter) | ○ | Next: `SK.17.WorkflowConformance` (WO-087) |
| 18 | [Idempotency](src/Infrastructure/Idempotency/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 19 | [Scheduling](src/Infrastructure/Scheduling/state-map.md) | 1 (Adapter) | ○ | Next: `SK.19.SchedulingConformance` (WO-087) |
| 20 | [Reporting](src/Infrastructure/Reporting/state-map.md) | 5 (1 Abstractions, 4 Adapter) | ○ | Next: `SK.20.ReportingConformance` (WO-087) |
| — | [samples](samples/README.md) | `Shop`: one Aspire platform, 7 services (Catalog, Ordering, Inventory, Billing, Merchant, Notify, Reports) | ● | None |

Every package ships at one repo-wide version through the release train (`CONTRIBUTING.md`, "Versioning and releases"); nothing is released per package.

## Open Work

### WO-087 — Standardise logging, OpenTelemetry, exceptions and error codes across the kernel

**Verdict: accept (upgraded to a four-phase rollout).** A repo-wide audit (HEAD b7daa29c) found 13 defects and wide drift: telemetry silently dropped (the Messaging meter, Temporal tracing and the AI sources are never subscribed or never used), tenant/actor/PII values and exception text reaching logs and ProblemDetails, lost stack traces, exceptions that carry an `Error` but do not derive `SharedKernelException` (so every edge maps them to 500), wrong `ErrorType`s, and 55 bare EventId literals with overlapping sub-blocks. The goal is that any consuming service traces, logs and reports errors identically. User decisions, binding: (1) four phases: Phase 0 foundation (shared helpers, gold standard in root `CLAUDE.md`, governance tests report-only, ServiceDefaults wildcard subscription), Phase 1 the 13 bugs per domain, Phase 2 conformance per domain, Phase 3 governance as errors; (2) wire-visible breaking renames are made now, pre-GA, and carried in release notes (this WO gates the first release, P-577); (3) log templates are one sentence with a trailing period and quoted identifier values; (4) failed spans record the full exception event (`AddException`), because traces share the trust level of logs. Nothing declined; consumer-service migration is not kernel work. The gold standard (rules L1-L8, T1-T8, E1-E4, C1-C3) is written into root `CLAUDE.md` section "Observability, exceptions and error codes", marked planned until Phase 3 ships. Breaking changes, named so the release carries the major-bump review of the `PublicAPI` diff: error-code renames, metric and meter renames and units, meter consolidation, EventId renumbering, exception base-type changes, and `Error`-carrying results replacing `string? Error` in Integration.

Order for `/dispatch-phase`: Phase 0 (P-580 to P-583), then Phase 1 (P-584 to P-599, parallel across domains), then Phase 2 (P-600 to P-617, parallel across domains, each after its own domain's Phase 1), then Phase 3 (P-618). Every phase keeps the capability's `.Testing` double in step with its changes. Type names appear below only where a defect cannot be located otherwise; planners own file-level design. Every Phase 1 and Phase 2 entry also carries these acceptance bullets: wire-visible renames are listed under a "Breaking changes" heading in the commit/PR body for the release notes; `PublicAPI.Unshipped.txt`, package READMEs (error code, EventId, telemetry tables) and the domain `CLAUDE.md` are updated in the same change; tests assert telemetry and error codes through the P-581 capture helpers, not by string search.

#### P-580 — Shared observability helpers in Foundation (Phase 0)
**Status:** `●` Complete
**Domain:** 01.Core
**Depends on:** None
**Phase key:** `SK.01.ObservabilityHelpers`
Provide the single set of helpers every other domain adopts: (a) a telemetry-version helper returning the assembly informational version for ActivitySource, Meter and `service.version`; (b) one span failure recorder: status Error with `error.code`, `error.type` (the `ErrorType` for Result failures, the exception full name for exceptions), the full exception event for exceptions, cancellation never marked as an error; (c) the shared `outcome` vocabulary (`success`, `failure`, `exception`, `canceled`) as named constants; (d) any missing cross-cutting well-known tag keys later phases need; (e) a helper or convention for per-package EventId sub-blocks over `LoggingEventIdRanges`; (f) `ResultTry` aligned with (b). Wire names stay in `WellKnown*`, never in Contracts.
- [ ] No package needs hand-written status/exception/`error.*` code on a span; the recorder is tested for Result failure, exception, cancellation and already-ended spans.
- [ ] Outcome and tag-key constants are public, documented and pinned by tests.
- [ ] No new third-party dependency; Foundation tier rules hold.
- [ ] Helpers recorded in the domain `CLAUDE.md`; a root "What Goes Where" row flagged for `/sync-brain`.

#### P-581 — Telemetry capture helpers for tests (Phase 0)
**Status:** `●` Complete
**Domain:** 16.Testing
**Depends on:** P-580
**Phase key:** `SK.16.TelemetryCapture`
Provide in the shared test package a capture helper for `ActivitySource` and `Meter` output (records started/stopped activities with tags, status and events, and instrument measurements with name, unit and tags) so every later phase asserts telemetry structurally. Follow the double rules (deterministic, no test framework). Add a log-capture assertion for EventId, template and exception-parameter checks if none exists.
- [ ] A test asserts a span's name, kind, tags, status and exception event, and a metric's name, unit and tag set, in a few lines.
- [ ] Self-tests cover several sources, disposed listeners and concurrent recording.
- [ ] Catalogue and rules in the domain `CLAUDE.md` updated.

#### P-582 — Wildcard subscription and resource in ServiceDefaults (Phase 0)
**Status:** `●` Complete
**Domain:** 13.ServiceDefaults
**Depends on:** P-580
**Phase key:** `SK.13.WildcardTelemetry`
`AddServiceDefaults` subscribes to every `SharedKernel.*` ActivitySource and Meter by wildcard, so a capability's telemetry can never again be dropped by a missing hand-copied name. The per-capability `With{X}Telemetry()` extensions keep only third-party instrumentation (MassTransit meter and source, Npgsql, Polly, gRPC, HttpClient metrics, SignalR server source, and the Temporal `Temporalio.*` sources used by the Temporal tracing interceptor). Add HttpClient metrics. `service.version` comes from the assembly informational version via the P-580 helper, with a single resource configuration. This fixes the dropped Messaging meter and the unsubscribed Temporal sources at the host. Report (do not edit) the sample wiring that omits integration telemetry.
- [ ] A host built with only `AddServiceDefaults` exports spans and instruments from a test source/meter named `SharedKernel.Anything`.
- [ ] The Messaging meter and the Temporal client/worker sources are exported; HttpClient metrics are exported.
- [ ] `service.version` is the informational version; one resource configuration.
- [ ] README telemetry tables and domain `CLAUDE.md` updated; the sample gap is reported to the caller.

#### P-583 — Governance rules in report-only mode (Phase 0)
**Status:** `●` Complete
**Domain:** 00.Governance
**Depends on:** P-580
**Phase key:** `SK.00.ObservabilityReportOnly`
Add the checks that will enforce the standard, shipped non-failing so each domain can measure itself before Phase 3: (G1) wire the kernel analyzers into `src/` as warnings and enable CA2200; (G2) a platform-wide EventId test over every packable assembly with per-package 100-wide sub-block ownership and no bare literals; (G3) a telemetry test: every ActivitySource/Meter name is a public constant matching `^SharedKernel\.[A-Z]\w+$`, every instrument declares a unit, tag keys are not raw literals; (G4) an analyzer: an exception type exposing an `Error` must derive `SharedKernelException`. Add log-template (one sentence, trailing period, no free-text placeholders) and error-code format (lowercase `{domain}.{snake_reason}`) checks as tests. Report mode prints per-domain finding counts so Phase 2 can show zero.
- [ ] Each rule has a firing and a passing test and a report-only switch whose default is non-failing; findings are listed per domain/assembly.
- [ ] Analyzer release tracking and README rule tables updated; rule ids take the next free SK ids.
- [ ] Switching to failing is a single, documented change (consumed by P-618).

#### P-584 — Fix observability and exception defects in Core (Phase 1)
**Status:** `●` Complete
**Domain:** 01.Core
**Depends on:** P-580
**Phase key:** `SK.01.CoreObservabilityFixes`
FeatureManagement: stop copying `ex.Message` into the OpenFeature evaluation message (breaks the domain rule on leaking exception text). Execution and FeatureManagement: the exceptions that carry no `Error` (commit outcome unknown, transaction rolled back, feature-flag validation) get an `Error` and derive `SharedKernelException` so edges map them correctly. Check the Foundation packages for other exception text in logs or results.
- [ ] No exception text reaches an evaluation message, log or `Error` in Core packages.
- [ ] The three exceptions map to the intended HTTP/gRPC status through the existing edge mappers (tested).
- [ ] No stack trace is lost on a rethrow.

#### P-585 — Fix exception shape in Caching (Phase 1)
**Status:** `●` Complete
**Domain:** 02.Caching
**Depends on:** P-580
**Phase key:** `SK.02.LockUnavailableError`
The distributed-lock-unavailable exception carries no `Error` and does not derive `SharedKernelException`; give it a code and the Unavailable type so edges map it to 503 instead of 500. Check the Redis packages for caught-exception logs that omit the exception.
- [ ] The exception maps to Unavailable at HTTP and gRPC (tested).
- [ ] Every caught-exception log in the domain passes the exception.

#### P-586 — Fix pipeline telemetry gaps in Application (Phase 1)
**Status:** `●` Complete
**Domain:** 05.Application
**Depends on:** P-580
**Phase key:** `SK.05.PipelineTelemetryGaps`
Stream queries (`IStreamQuery<T>`) get the same observable guarantees as other requests: the pipeline registers only the authorization behavior for streams today, so there is no tracing, logging or metrics. Add them suited to a stream's lifetime (the span covers enumeration; cancellation is not an error). Domain-event dispatch and the transaction behavior get spans and failure logs. The idempotency behavior maps a store outage (a raw Redis/Npgsql exception today) to an Unavailable `Result` instead of letting it escape.
- [ ] A stream query produces a span with outcome and a metric; an aborted enumeration is `canceled`, not failure.
- [ ] Domain-event dispatch and transactions have spans; failures carry `error.code`.
- [ ] A store outage during idempotency begin returns `Error.Unavailable`, tested against a faulting store double.

#### P-587 — Fix data leaks, error types and swallowed detail in Persistence (Phase 1)
**Status:** `●` Complete
**Domain:** 06.Persistence
**Depends on:** P-580
**Phase key:** `SK.06.PersistenceErrorSafety`
Stop logging the ambient tenant id and actor id in the cross-tenant scope diagnostics and the full idempotency key in auditing logs. Remove tenant ids from exception and caller-scope messages (audit chain integrity, caller scope). Log the exception object, not a type-name string. Classify a PostgreSQL query-canceled fault as Timeout, not Conflict. Replace `KeyNotFoundException` for not-found (audit ledger maintenance, chain verifier) with a `Result` or a `SharedKernelException` with NotFound; replace raw `CryptographicException` escaping the kernel (field encryption runtime, tenant key store) with a classified exception. Make "no tenant resolved" one behaviour across Dapper, caller scope and EfCore (fail closed with the Forbidden error).
- [ ] No tenant, actor or idempotency-key value appears in any log or exception message in the domain.
- [ ] Query-canceled maps to Timeout; not-found and crypto faults surface as classified errors, tested.
- [ ] The three "no tenant" paths return the same error.

#### P-588 — Fix header leakage, lost stack traces and swallowed failures in Messaging (Phase 1)
**Status:** `●` Complete
**Domain:** 07.Messaging
**Depends on:** P-580, P-582
**Phase key:** `SK.07.MessagingErrorSafety`
The consumer log scope must not copy every `x-sk-*` header (it includes the actor id) nor use raw prefix literals; copy only an allow-list of non-sensitive well-known headers (tenant and correlation flow ambiently). Replace `throw ex;` in the message bus with a stack-preserving rethrow. The message scheduler's catch-all must not swallow cancellation. The concurrent-delivery and payload-transform-mismatch exceptions get an `Error` and derive `SharedKernelException`. The Messaging meter is exported through P-582; verify with a host test.
- [ ] No actor/tenant header value reaches a log scope (capturing-logger test).
- [ ] The rethrown exception's stack trace includes the original frame (tested).
- [ ] Cancellation propagates from the scheduler; the two exceptions map via the edge mappers.
- [ ] The Messaging meter appears in a ServiceDefaults host test.

#### P-589 — Make StorageException map at the edge (Phase 1)
**Status:** `●` Complete
**Domain:** 08.Storage
**Depends on:** P-580
**Phase key:** `SK.08.StorageExceptionEdge`
The storage exception carries an `Error` but does not derive `SharedKernelException`, so edges map it to 500 and lose the error. Fix, and check logs and results for provider detail (hosts, endpoints).
- [ ] A storage failure maps to its `ErrorType` at HTTP and gRPC (tested).
- [ ] No provider endpoint or host appears in an `Error` or log.

#### P-590 — Fix detail leaks, lost exceptions and swallowed failures in Search (Phase 1)
**Status:** `●` Complete
**Domain:** 09.Search
**Depends on:** P-580
**Phase key:** `SK.09.SearchErrorSafety`
Stop putting provider debug dumps (ElasticSearch response debug information) and raw exception messages into `Error.Message` (they reach ProblemDetails). Stop logging `Error.Message` as a free-text reason. Search log classes must accept and receive the exception on caught-exception paths. The cursor-search catch-all must not swallow cancellation or turn unrelated faults into CursorExpired (domain rule). The stream exception carrying an `Error` derives `SharedKernelException`. Align the catch policy with Messaging's classify-or-rethrow.
- [ ] No provider debug text or exception message in any `Error`, tested with a faulting client.
- [ ] Cancellation propagates through cursor search; only a genuine expired-cursor fault yields CursorExpired.
- [ ] Every caught-exception log passes the exception.

#### P-591 — Make the AI domain observable and stop its leaks (Phase 1)
**Status:** `●` Complete
**Domain:** 10.Intelligence
**Depends on:** P-580, P-582
**Phase key:** `SK.10.IntelligenceObservability`
The Qdrant and Semantic Kernel packages declare a source and meter that are never used: emit spans (embedding, vector operations, orchestration; Client kind) and metrics including token counters, using the already-declared `ai.*` tag keys. Stop embedding endpoints and raw exception messages in `Error`s; stop logging `Error.Message`; pass exceptions to logs; fix ErrorType (unreachable, timeout, write-timeout and rate-limited must not be Unexpected) and the stream exception base type; replace blanket catch-all with classify-or-rethrow, cancellation always propagating.
- [ ] A vector search and an embedding call each produce a span and a duration metric through ServiceDefaults; token usage is a counter with a unit.
- [ ] No endpoint or exception text in any `Error`; ErrorTypes match the condition (tested).
- [ ] Cancellation propagates from every operation.

#### P-592 — Make AccessTokenUnavailableException map at the edge (Phase 1)
**Status:** `●` Complete
**Domain:** 11.Communication
**Depends on:** P-580
**Phase key:** `SK.11.TokenUnavailableEdge`
The access-token-unavailable exception carries an `Error` but does not derive `SharedKernelException`; fix so inbound edges map it to Unavailable. Check outbound clients for hosts or URLs in logs and `Error`s.
- [ ] The exception maps via the edge mappers (tested).
- [ ] No URL or host in logs or `Error`s.

#### P-593 — Pass the exception on token revocation failures (Phase 1)
**Status:** `●` Complete
**Domain:** 12.Security
**Depends on:** P-580
**Phase key:** `SK.12.RevocationFailureLogging`
The token-revocation enforcer logs failures without the exception; pass it. Check the Security packages for logs of certificate subjects, client ids or claim values.
- [ ] The failure log carries the exception (captured-logger test).
- [ ] No subject, client id or claim value is logged in the domain.

#### P-594 — Fix server-span tags, propagation and sensitive logging in ServiceDefaults (Phase 1)
**Status:** `●` Complete
**Domain:** 13.ServiceDefaults
**Depends on:** P-580, P-582
**Phase key:** `SK.13.HostObservabilityFixes`
The inbound request span must be tagged with correlation id and tenant id (they live only in baggage today), and the HTTP host must read the request-context propagation headers via `RequestContextPropagation` (clients write the actor headers, nobody reads them). Remove the certificate subject distinguished name and the remote IP from mTLS logs and stop reusing one EventId for unrelated messages; remove the tenant id placeholder from the multi-tenancy log. The readiness health check must log the probe exception, not only its type name. Coordinate with 14.Presentation so diagnostics suppression does not hide middleware spans for handled exceptions.
- [ ] A request through the host yields a server span with `correlation.id` and `tenant.id`; the actor headers written by clients are read by the host.
- [ ] No certificate subject, IP or tenant id in a log; one EventId per message.
- [ ] A failing probe logs its exception.

#### P-595 — Unify error context and failed-span recording at the edges (Phase 1)
**Status:** `●` Complete
**Domain:** 14.Presentation
**Depends on:** P-580, P-594
**Phase key:** `SK.14.EdgeErrorContext`
GraphQL: the error filter exposes the exception message as title/detail in all environments, hard-codes status and type, has no logging and no EventId sub-block, and does not map `SharedKernelException` or validation exceptions; bring it to ProblemDetails parity. SignalR: hub errors lack trace id and correlation id. The HTTP exception handler and gRPC interceptor must set span status Error, record the exception event and `error.code`. Every client-facing error in HTTP, gRPC, SignalR and GraphQL carries trace id and correlation id.
- [ ] The four edges expose equivalent error information (code, type-derived status, trace id, correlation id); exception text only in development.
- [ ] A handled exception leaves a failed span with `error.code` and the exception event on HTTP and gRPC.
- [ ] GraphQL logs unexpected errors under its own EventId sub-block.

#### P-596 — Fix leaks and fan-out failures in Integration (Phase 1)
**Status:** `●` Complete
**Domain:** 15.Integration
**Depends on:** P-580
**Phase key:** `SK.15.IntegrationLeakFixes`
Webhook dispatch and the SendGrid/Twilio senders flatten `ex.Message` into a log field (may contain hosts) and do not pass the exception; fix. The dispatcher throws inside the parallel fan-out, violating the domain rule that one subscription's failure never aborts the others; fix.
- [ ] One failing subscription never stops delivery to the others (tested).
- [ ] No exception text in any log or result; the exception object is passed.

#### P-597 — Make Workflows observable and stop its leaks (Phase 1)
**Status:** `●` Complete
**Domain:** 17.Workflows
**Depends on:** P-580, P-582
**Phase key:** `SK.17.WorkflowObservabilityFixes`
Workflow telemetry has a dead ActivitySource and an unsubscribed Temporal tracing interceptor: remove the dead source or make it real, and rely on the Temporal tracing interceptor subscribed in P-582. The logged workflow id embeds tenant and business key; log a safe form. Probe failures must log the exception. Fix ErrorTypes (timed-out and service-unavailable must not be Unexpected; map Temporal RPC failures by status instead of all to service-unavailable, so not-found and namespace-not-found are produced). Fix the worker-not-configured message that is handed a reason sentence where a task queue is expected.
- [ ] Temporal client/workflow/activity spans appear through ServiceDefaults; no dead source remains.
- [ ] No tenant or business key in a log; probe logs carry the exception.
- [ ] Failure mapping is status-aware and tested for NotFound, NamespaceNotFound, timeout and unavailable.

#### P-598 — Fix log ordering and leaks in Scheduling (Phase 1)
**Status:** `●` Complete
**Domain:** 19.Scheduling
**Depends on:** P-580
**Phase key:** `SK.19.SchedulerLogContext`
The hosted service logs before the request-context scope begins and again after it is disposed; log inside the scope. Remove error-message placeholders and exception text from span status and logs. Pass exceptions.
- [ ] Every scheduler log is emitted with the system request context ambient (tested).
- [ ] Span status carries `error.code`, never a message; no free-text error placeholder.

#### P-599 — Fix provider detail and exception shape in Reporting (Phase 1)
**Status:** `●` Complete
**Domain:** 20.Reporting
**Depends on:** P-580
**Phase key:** `SK.20.ReportingErrorShape`
The Gotenberg converter puts the host and response body into `Error`s; remove. The report-aborted exception carries no `Error`; give it one and derive `SharedKernelException`.
- [ ] No host or body in any `Error`.
- [ ] The aborted-report exception maps via the edge mappers (tested).

#### P-600 — Conform Core to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 01.Core
**Depends on:** P-580, P-581, P-583, P-584
**Phase key:** `SK.01.CoreConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" (L1-L8, T1-T8, E1-E4, C1-C3) to every Foundation package. Specifics: off-pattern codes are renamed (FluentValidation pass-through codes map to the Validation codes; `unexpected.exception` becomes `unexpected.default`); the FeatureManagement EventId class is renamed; every package owns a 100-wide sub-block recorded in the domain `CLAUDE.md`; Core emits telemetry only under `SharedKernel.Core`.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-601 — Conform Caching to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 02.Caching
**Depends on:** P-580, P-581, P-583, P-585
**Phase key:** `SK.02.ObservabilityConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Caching packages. Specifics: cache metrics gain units and seconds for durations and drop hand-typed tag-key literals (cache operation, error type); source and meter named `SharedKernel.Caching`; sub-blocks for FusionCache and each Redis role package.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-602 — Conform Domain error codes to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 03.Domain
**Depends on:** P-580, P-583
**Phase key:** `SK.03.DomainErrorCodes`
Domain stays logging-free and telemetry-free; only error codes change: `domain.rule.violated` becomes `domain.rule_violated`; the Currency and Money codes move into a public `DomainErrorCodes`; code format and ErrorType per condition follow C1-C3.
- [ ] Codes follow C1-C3; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, README and domain `CLAUDE.md` updated.

#### P-603 — Conform Application to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 05.Application
**Depends on:** P-580, P-581, P-583, P-586
**Phase key:** `SK.05.ApplicationConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Application packages. Specifics: the tracing and metrics behaviors use the T4/T6 constants, the failure recorder and the outcome vocabulary (no raw `reason`/`operation` literals); caching behavior metrics get units and names; sub-blocks per package (Pipeline, Pipeline.Caching, Mediator.MediatR).
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-604 — Conform Persistence to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 06.Persistence
**Depends on:** P-580, P-581, P-583, P-587
**Phase key:** `SK.06.PersistenceConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Persistence packages. Specifics: fold the Auditing and Encryption meters into one `SharedKernel.Persistence` source and meter; audit instruments use seconds and units; repository tracing uses the failure recorder; PascalCase `Persistence.Encryption.*` and `Persistence.BulkMutation.*` codes become lowercase snake; a public `PersistenceErrorCodes` and an internal factory gather the scattered codes; the non-contiguous EventIds (6000-6026 plus 6350-6351) become per-package 100-wide sub-blocks.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-605 — Conform Messaging to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 07.Messaging
**Depends on:** P-580, P-581, P-583, P-588
**Phase key:** `SK.07.ObservabilityConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Messaging packages. Specifics: non-overlapping sub-blocks between MassTransit and AzureServiceBus; `*.count` instruments get unit `{message}` and durations seconds; publish-side propagation goes through `RequestContextPropagation` instead of hand-rolled header writes; spans use Producer/Consumer kinds and the failure recorder; public `MessagingErrorCodes`.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-606 — Conform Storage to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 08.Storage
**Depends on:** P-580, P-581, P-583, P-589
**Phase key:** `SK.08.StorageConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Storage packages. Specifics: source and meter named `SharedKernel.Storage`; operation-duration instrument names and units normalised; public `StorageErrorCodes`; Obs and S3 follow the sub-block rule.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-607 — Conform Search to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 09.Search
**Depends on:** P-580, P-581, P-583, P-590
**Phase key:** `SK.09.ObservabilityConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Search packages. Specifics: public `SearchErrorCodes` (codes are private today); engine-fault, timeout and unavailable use the right ErrorTypes with consistent reason words; instrument names, units and spans per the T-rules; per-package EventId sub-blocks; log classes take exceptions.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-608 — Conform Intelligence to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 10.Intelligence
**Depends on:** P-580, P-581, P-583, P-591
**Phase key:** `SK.10.IntelligenceConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Intelligence packages. Specifics: public `IntelligenceErrorCodes`; unify the two providers' diagnostics classes under `SharedKernel.AI`; instrument names and units; classify-or-rethrow across both providers; per-package sub-blocks; `ai.*` tag keys complete in the well-known class.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-609 — Conform Communication to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 11.Communication
**Depends on:** P-580, P-581, P-583, P-592
**Phase key:** `SK.11.CommunicationConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Communication packages. Specifics: the `money.proto.invalid`, `grpc.*` and `http.` codes that live outside the communication error-code class move into it; sub-blocks for the base, REST and gRPC packages (no bare literals); propagation only via `RequestContextPropagation`.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-610 — Conform Security to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 12.Security
**Depends on:** P-580, P-581, P-583, P-593
**Phase key:** `SK.12.SecurityConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Security packages. Specifics: an EventId class per package with no bare literals; no sub-block shared with ServiceDefaults.Security or Localization; one `SharedKernel.Security` source/meter if the domain emits telemetry; public `SecurityErrorCodes`.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-611 — Conform ServiceDefaults to the gold standard (Phase 2)
**Status:** `◐` Dispatched
**Domain:** 13.ServiceDefaults
**Depends on:** P-580, P-581, P-582, P-583, P-594
**Phase key:** `SK.13.ServiceDefaultsConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all ServiceDefaults packages. Specifics: the shared 13000-13099 range splits into distinct per-package sub-blocks (Security.Mtls, ServiceDefaults base, Localization, request context); single resource configuration; per-capability telemetry extensions keep only third-party instrumentation; request-context and tenant middleware tag spans per the T-rules.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-612 — Conform Presentation to the gold standard (Phase 2)
**Status:** `◐` Dispatched
**Domain:** 14.Presentation
**Depends on:** P-580, P-581, P-583, P-595
**Phase key:** `SK.14.PresentationConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Presentation packages. Specifics: Presentation.Core's EventIds sit inside the WebApi range today, so each package gets its own sub-block; HTTP, gRPC, SignalR and GraphQL status/type tables derive from one mapping; instrument and tag constants per T4; edge exception handlers use the failure recorder.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-613 — Conform Integration to the gold standard (Phase 2)
**Status:** `◐` Dispatched
**Domain:** 15.Integration
**Depends on:** P-580, P-581, P-583, P-596
**Phase key:** `SK.15.IntegrationConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Integration packages. Specifics: notification and webhook results carry a real `Error` instead of `string? Error` (breaking, intended); the `notifications.*` code prefix is normalised; success/failure literals use the shared outcome vocabulary; the webhook dispatcher propagates context only via `RequestContextPropagation` (root `CLAUDE.md` wrongly lists Webhooks as propagating; report for `/sync-brain`); per-package sub-blocks; public `IntegrationErrorCodes`.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-614 — Conform Workflows to the gold standard (Phase 2)
**Status:** `◐` Dispatched
**Domain:** 17.Workflows
**Depends on:** P-580, P-581, P-583, P-597
**Phase key:** `SK.17.WorkflowConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to the Workflows package. Specifics: add public `WorkflowErrorCodes` (none exists today); the sites that throw `InvalidOperationException(error.Message)` become classified exceptions; one dispatch mechanism per API (GetHandle vs StartAsync); EventIds through a named class, no bare literals.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, README and domain `CLAUDE.md` updated.

#### P-615 — Conform Idempotency to the gold standard (Phase 2)
**Status:** `●` Complete
**Domain:** 18.Idempotency
**Depends on:** P-580, P-581, P-583
**Phase key:** `SK.18.IdempotencyConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to the Abstractions, Redis and EfCore packages. Specifics: a store outage maps to Unavailable with `idempotency.*` codes; instruments under `SharedKernel.Idempotency`; per-package sub-blocks; no key values in logs.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-616 — Conform Scheduling to the gold standard (Phase 2)
**Status:** `◐` Dispatched
**Domain:** 19.Scheduling
**Depends on:** P-580, P-581, P-583, P-598
**Phase key:** `SK.19.SchedulingConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to the Scheduling package. Specifics: add a duration histogram and bring the eight counters to units and the shared outcome vocabulary (replacing outcome string literals); rename the `SchedulingLogs` class to the L2 shape; span status per T5; public `SchedulingErrorCodes`.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, README and domain `CLAUDE.md` updated.

#### P-617 — Conform Reporting to the gold standard (Phase 2)
**Status:** `◐` Dispatched
**Domain:** 20.Reporting
**Depends on:** P-580, P-581, P-583, P-599
**Phase key:** `SK.20.ReportingConformance`
Apply every applicable rule of root `CLAUDE.md` "Observability, exceptions and error codes" to all Reporting packages. Specifics: Reporting writes `Error.Code` into `error.type` today, fix with the failure recorder; `*.client.operation.duration` instruments are renamed to `sharedkernel.reporting.*` in seconds; public `ReportingErrorCodes`; per-package sub-blocks; remaining `InvalidOperationException(error.Message)` sites become classified exceptions.
- [ ] Every applicable rule holds; the P-583 report-only tests show zero findings for the domain.
- [ ] Breaking renames listed for the release notes; PublicAPI, READMEs and domain `CLAUDE.md` updated.

#### P-618 — Switch the observability rules to errors (Phase 3)
**Status:** `◐` Dispatched
**Domain:** 00.Governance
**Depends on:** P-583, P-600, P-601, P-602, P-603, P-604, P-605, P-606, P-607, P-608, P-609, P-610, P-611, P-612, P-613, P-614, P-615, P-616, P-617
**Phase key:** `SK.00.ObservabilityEnforce`
Flip the report-only checks from P-583 to failing: analyzers become errors in `src/` (and CA2200), and the platform-wide EventId test, the telemetry name/unit/tag-literal test, the log-template and error-code-format tests and the `Error`-carrying-exception analyzer all fail the build. Remove the report switch. Confirm no domain still needs a suppression; any remaining one is justified in the domain `CLAUDE.md`.
- [ ] The full Unit lane is green with every rule failing on violation; a deliberate violation of each rule is proven to fail (firing tests).
- [ ] README rule tables and the domain `CLAUDE.md` updated.
- [ ] The standard in root `CLAUDE.md` is no longer marked planned (reconcile via `/sync-brain`).

### P-577 — First release train (WO-086)

**Status:** `○` Not started
**Domain:** cross-domain (every packable project)
**Depends on:** P-562–P-576, P-579 (all ●), P-580–P-618 (WO-087: wire-visible renames land before the first release)

**What is needed**

Cut the first release of the WO-086 package set: `git tag vX.Y.Z` on `main`, letting `release.yml` run every gate (tier check, full build, Unit and Integration suites, every consumer-verify harness and sample), pack all packages at that one version, check the set against every packable project, and publish them together to GitHub Packages.

**Acceptance**

- One tag, one version on every published package; no `<Version>` in any `.csproj`.
- The published set equals the set of packable projects (including the per-capability `*.Testing` packages and the Tooling packages).
- `06.Persistence` and the other never-published or re-shaped packages (`SharedKernel.Execution`, `Application.Pipeline`/`.Pipeline.Caching`/`.Mediator.MediatR`, the MassTransit satellites, `Presentation.Core`, `Idempotency.*`) appear on the feed for the first time.
- This supersedes every per-package "publish pending" task left in older records (P-557's P-17, P-563's PUB, P-544's cross-domain follow-ups).

### P-578 — Retire old package IDs (WO-086)

**Status:** `○` Not started — approved by the user 2026-09-26: delete them after the release
**Domain:** cross-domain (feed housekeeping)
**Depends on:** P-577

**What is needed**

After P-577 is on the feed, delete the package IDs that WO-086 and the pre-publish passes replaced (for example `SharedKernel.Application.Abstractions`, `SharedKernel.Application.Behaviors`, `SharedKernel.Application.Behaviors.Caching`, `SharedKernel.Guards`, `SharedKernel.Persistence.PostgreSQL`, the probe-only `SharedKernel.ServiceDefaults.*` packages). Confirm the exact list with the user before deleting anything.

**Acceptance**

- Every retired ID is either deleted from the feed or explicitly kept by the user's decision.
- No retired ID is still referenced by any project, sample or README.

## Blocked

None.
