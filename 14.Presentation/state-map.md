# 14.Presentation — State Map

> **What this file is:** Phase and task tracker for all work within `14.Presentation`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.14.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
| --- | --- | --- |
| `SK.14.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.14.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.14.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.14.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.14.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.14.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Define ErrorTypeStatusCodeMap | SK.14.Design | SharedKernel.Presentation.WebApi | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.14.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Presentation.WebApi` | Published | `●` | WO-041/P-256 closed: `CorrelationIdMiddleware`/`SharedKernelExceptionHandler` `[LoggerMessage]` methods now carry explicit `EventId`s (14000/14001) derived from `LoggingEventIdRanges.Presentation`; `CorrelationIdMiddleware.BaggageKey` constant confirmed already extracted and now regression-pinned by tests; new correlation-on-log-record integration test proves compatibility with `13.ServiceDefaults`'s `BaggageLogRecordProcessor` contract with zero cross-domain `ProjectReference`. Re-packed to `1.0.1`, `dotnet pack` 0 warnings, `consumer-verify` re-run confirms all 6 surfaces still PASS with zero DI exceptions. 42/42 tests passing (+4: 2 `EventId` regression pins, 1 `BaggageKey` literal pin, 1 correlation-log-record integration test) |
| `SharedKernel.Presentation.SignalR` | Published | `●` | WO-041/P-256 closed: `HubExceptionMappingFilter`'s `[LoggerMessage]` method now carries explicit `EventId` (14100) derived from `LoggingEventIdRanges.Presentation`. Re-packed to `1.0.1`, `dotnet pack` 0 warnings, `consumer-verify` re-run confirms all 6 surfaces still PASS with zero DI exceptions. 11/11 tests passing (+1 `EventId` regression pin) |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.14.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Error`, `ErrorType`) | Available |
| `SK.14.Scaffold` | `04.Contracts` | `SharedKernel.Contracts` ProjectReference (cross-reference only — `Envelope<T>` is a sibling concern to `ProblemDetails`, not a dependency of it) | Available |
| `SK.14.Core` | `12.Security` | `SharedKernel.Security.Abstractions` ProjectReference (`IUserContext`, `ITenantProvider`) — consumed by the OpenAPI Bearer scheme metadata and by `TenantContextHubFilter` | Available |
| `SK.14.Core` (C-14–C-16, P-256, WO-041) | `01.Core` | `SharedKernel.Primitives.Logging.LoggingEventIdRanges.Presentation` compile-time `const int` (P-249, C-42) — must exist before these tasks can compile a `[LoggerMessage(EventId = LoggingEventIdRanges.Presentation + N)]` reference | **Resolved** — `01.Core`'s P-249 landed (`SharedKernel.Primitives/Logging/LoggingEventIdRanges.cs`, `Presentation = 14000`); C-14–C-16 implemented against it |
| `SK.14.Core` (C-18, P-256, WO-041) | `00.Governance` | `SK0020`/`SK0021` (`LoggingAuthoringStyleAnalyzer`, P-250) producing zero diagnostics against this domain's existing `[LoggerMessage]`-only authoring | Informational only — not a functional blocker; this domain's code already conforms to the platform logging standard (verified via clean Release builds of both packages), so this task is a confirmation step, not a fix |

> **Resolved (P-192, WO-031):** The prior `SK.14.Core` → `13.ServiceDefaults` row ("OTel baggage conventions") is removed. `CorrelationIdMiddleware`'s `Activity` baggage key (`correlation.id`) is this domain's own contract — `14.Presentation` does not borrow or wait on a `13.ServiceDefaults` tracing convention, and takes no `ProjectReference` on it. `13.ServiceDefaults` may itself align with this domain's baggage key if it chooses, but the dependency direction is one-way and does not flow back into `14.Presentation`.

---

## Phase: Design <!-- phase-key: SK.14.Design -->

> Finalize all interface contracts, mapping rules, and DI extension signatures before any implementation begins.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |
| D-01 | Confirm `ErrorTypeStatusCodeMap` / `ErrorProblemDetailsExtensions` contract: status-code table, `Type`/`Title`/`Detail`/`Extensions["errorCode"]`/`Extensions["traceId"]` construction, unmapped-`ErrorType`-falls-back-to-500 rule | `SharedKernel.Presentation.WebApi` | `●` |
| D-02 | Confirm `ResultHttpExtensions` as two non-overlapping overload families (Minimal API `IResult` incl. `onSuccess` projection; MVC `ActionResult`/`ActionResult<T>`) with no auto-detecting third form; confirm distinctness from `04.Contracts` `ResultEnvelopeExtensions` | `SharedKernel.Presentation.WebApi` | `●` |
| D-03 | Confirm `SharedKernelExceptionHandler` contract: known-`SharedKernelException`-via-`Error.ToProblemDetails()` branch vs. unknown-exception redacted-500 branch, `IsDevelopment()` gate, mandatory `LogLevel.Error` logging before response | `SharedKernel.Presentation.WebApi` | `●` |
| D-04 | Confirm API versioning defaults: `DefaultApiVersion = 1.0`, combined URL-segment-primary + header-secondary reader, `AssumeDefaultVersionWhenUnspecified = true`, `ReportApiVersions = true`, `GroupNameFormat = "'v'VVV"` | `SharedKernel.Presentation.WebApi` | `●` |
| D-05 | Confirm OpenAPI + Scalar registration contract: one native `Microsoft.AspNetCore.OpenApi` document per discovered version group, Bearer-scheme document transformer (metadata only), `Scalar.AspNetCore` UI listing all versions, no Swashbuckle/NSwag | `SharedKernel.Presentation.WebApi` | `●` |
| D-06 | Confirm `CorrelationIdMiddleware` contract as self-contained: `X-Correlation-Id` header read/generate, `HttpContext.Items["CorrelationId"]` storage key, `Activity.SetBaggage("correlation.id", ...)` — own contract, no `13.ServiceDefaults` `ProjectReference` | `SharedKernel.Presentation.WebApi` | `●` |
| D-07 | Remove the `SK.14.Core` → `13.ServiceDefaults` "Pending" Cross-Domain Dependencies row; record resolution note confirming the correlation-id baggage key is this domain's own contract | `SharedKernel.Presentation.WebApi` | `●` |
| D-08 | Confirm `TenantContextHubFilter` and `HubExceptionMappingFilter` contracts: connect-time tenant attachment (non-rejecting), invocation-wrapping exception-to-`HubException` redaction for known vs. unknown exception types | `SharedKernel.Presentation.SignalR` | `●` |
| D-09 | Confirm `HubGroupNaming.TenantGroup(Guid)` format (`tenant:{tenantId:D}`) as the single source of truth for tenant-scoped SignalR groups | `SharedKernel.Presentation.SignalR` | `●` |
| D-10 | Confirm `AddSharedKernelSignalR` / `WithRedisBackplane` DI extension contracts: global filter registration via `HubOptions.AddFilter<T>()` with opt-out callback; `WithRedisBackplane` as a pure pass-through over `AddStackExchangeRedis` with explicit `IConnectionMultiplexer` isolation from `02.Caching.Redis.Core` | `SharedKernel.Presentation.SignalR` | `●` |
| D-11 | Verify no confirmed contract across either package requires a `ProjectReference` outside `01.Core`, `04.Contracts`, `12.Security.Abstractions` | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |
| D-12 | Lock 14.Presentation's `EventId` sub-block allocation within `LoggingEventIdRanges.Presentation` (14000): `SharedKernel.Presentation.WebApi` = 14000–14099 (declared first), `SharedKernel.Presentation.SignalR` = 14100–14199 (declared second); assign exact `EventId`s — `CorrelationIdMiddleware` = 14000, `SharedKernelExceptionHandler` = 14001, `HubExceptionMappingFilter` = 14100 | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |
| D-13 | Promote `CorrelationIdMiddleware`'s inline `"correlation.id"` `Activity` baggage-key string literal to a public named constant (`CorrelationIdMiddleware.BaggageKey`) — motivated by discovering `13.ServiceDefaults`'s P-251 test design (T-27) currently hardcodes a _different_ literal (`"CorrelationId"`) for "the exact mechanism 14.Presentation's middleware uses," a mismatch invisible to either domain's own test suite because neither takes a `ProjectReference` on the other; a shared, discoverable constant closes this class of drift for any future cross-domain consumer | `SharedKernel.Presentation.WebApi` | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.14.Scaffold -->

> Wire up `.csproj` NuGet references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |
| S-01 | Add NuGet references: `Asp.Versioning.Http`, `Asp.Versioning.Mvc.ApiExplorer`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` | `SharedKernel.Presentation.WebApi` | `●` |
| S-02 | Add `ProjectReference`s: `SharedKernel.Primitives`, `SharedKernel.Contracts`, `SharedKernel.Security.Abstractions` | `SharedKernel.Presentation.WebApi` | `●` |
| S-03 | Create folder structure: `Errors/`, `Results/`, `ExceptionHandling/`, `Versioning/`, `OpenApi/`, `Middleware/` | `SharedKernel.Presentation.WebApi` | `●` |
| S-04 | Add NuGet reference: `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | `SharedKernel.Presentation.SignalR` | `●` |
| S-05 | Add `ProjectReference`s: `SharedKernel.Primitives`, `SharedKernel.Security.Abstractions` | `SharedKernel.Presentation.SignalR` | `●` |
| S-06 | Create folder structure: `Filters/`, `GroupNaming/`, `Extensions/` | `SharedKernel.Presentation.SignalR` | `●` |
| S-07 | Register both packages under the `14.Presentation` solution folder in `Platform.SharedKernel.slnx` | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |
| S-08 | Create empty nested test project `SharedKernel.Presentation.WebApi.Tests` referencing `SharedKernel.Testing` | `SharedKernel.Presentation.WebApi` | `●` |
| S-09 | Create empty nested test project `SharedKernel.Presentation.SignalR.Tests` referencing `SharedKernel.Testing` | `SharedKernel.Presentation.SignalR` | `●` |
| S-10 | Confirm solution builds with zero warnings after scaffold (no implementation yet) | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |

---

## Phase: Core <!-- phase-key: SK.14.Core -->

> Full implementation of all interfaces, middleware, hub filters, and DI registration.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |
| C-01 | Implement `ErrorTypeStatusCodeMap.Resolve` covering every `ErrorType` member plus a 500 fallback for unmapped types | `SharedKernel.Presentation.WebApi` | `●` |
| C-02 | Implement `ErrorProblemDetailsExtensions.ToProblemDetails()`: `Title`/`Detail`/`Status`/RFC 9457 `Type` URI/`Extensions["errorCode"]`/`Extensions["traceId"]` | `SharedKernel.Presentation.WebApi` | `●` |
| C-03 | Implement `ResultHttpExtensions` — Minimal API (`ToProblemDetailsResult`, `onSuccess` projection overload) and MVC (`ToActionResult`, `ToActionResult<T>`) | `SharedKernel.Presentation.WebApi` | `●` |
| C-04 | Implement `SharedKernelExceptionHandler` (`IExceptionHandler`): known-`SharedKernelException` mapping via carried `Error`, unknown-exception redacted 500 outside `IsDevelopment()`, mandatory `LogLevel.Error` logging | `SharedKernel.Presentation.WebApi` | `●` |
| C-05 | Implement `AddSharedKernelApiVersioning`: `DefaultApiVersion = 1.0`, combined reader, `AssumeDefaultVersionWhenUnspecified = true`, `ReportApiVersions = true`, `GroupNameFormat = "'v'VVV"` | `SharedKernel.Presentation.WebApi` | `●` |
| C-06 | Implement `AddSharedKernelOpenApi` / `MapSharedKernelOpenApi`: one document per version group, Bearer-scheme document transformer, `MapScalarApiReference` wiring, zero Swashbuckle/NSwag | `SharedKernel.Presentation.WebApi` | `●` |
| C-07 | Implement `CorrelationIdMiddleware` + `AddSharedKernelCorrelationId`/`UseSharedKernelCorrelationId`: header read/generate, `HttpContext.Items` storage, `Activity.SetBaggage`, always-set response header including on short-circuit | `SharedKernel.Presentation.WebApi` | `●` |
| C-08 | Add XML doc stubs on all public WebApi types/members (prevent CS1591; full prose deferred to Docs phase) | `SharedKernel.Presentation.WebApi` | `●` |
| C-09 | Implement `TenantContextHubFilter.OnConnectedAsync`: resolve `ITenantProvider`, store `Context.Items["TenantId"]`, non-rejecting on unresolved tenant | `SharedKernel.Presentation.SignalR` | `●` |
| C-10 | Implement `HubExceptionMappingFilter.InvokeMethodAsync`: known `SharedKernelException` → safe `HubException` message, unknown → logged + redacted `HubException`, no non-`HubException` ever crosses the boundary | `SharedKernel.Presentation.SignalR` | `●` |
| C-11 | Implement `HubGroupNaming.TenantGroup(Guid)` — `tenant:{tenantId:D}` format | `SharedKernel.Presentation.SignalR` | `●` |
| C-12 | Implement `AddSharedKernelSignalR` (global filter registration via `HubOptions.AddFilter<T>()`, opt-out `configureHubOptions` callback) and `WithRedisBackplane` (thin pass-through over `AddStackExchangeRedis`, isolated `IConnectionMultiplexer` from `02.Caching.Redis.Core`) | `SharedKernel.Presentation.SignalR` | `●` |
| C-13 | Add XML doc stubs on all public SignalR types/members | `SharedKernel.Presentation.SignalR` | `●` |
| C-14 | Assign `EventId = LoggingEventIdRanges.Presentation + 0` (14000) to `CorrelationIdMiddleware`'s `[LoggerMessage]` method | `SharedKernel.Presentation.WebApi` | `●` |
| C-15 | Assign `EventId = LoggingEventIdRanges.Presentation + 1` (14001) to `SharedKernelExceptionHandler`'s `[LoggerMessage]` method | `SharedKernel.Presentation.WebApi` | `●` |
| C-16 | Assign `EventId = LoggingEventIdRanges.Presentation + 100` (14100) to `HubExceptionMappingFilter`'s `[LoggerMessage]` method | `SharedKernel.Presentation.SignalR` | `●` |
| C-17 | Extract the `"correlation.id"` baggage-key literal into the public constant defined in D-13 (`CorrelationIdMiddleware.BaggageKey`); replace the inline literal at the `Activity.SetBaggage(...)` call site | `SharedKernel.Presentation.WebApi` | `●` |
| C-18 | Confirm both `.csproj` files build with zero `SK0020`/`SK0021` diagnostics (`00.Governance`'s `LoggingAuthoringStyleAnalyzer`, P-250) after C-14–C-17 — this domain already authors exclusively via `[LoggerMessage]`, so zero violations are expected; this task records the confirmation, it is not a code fix | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |

---

## Phase: Tests <!-- phase-key: SK.14.Tests -->

> Unit and integration test coverage. SignalR Redis backplane tests must use Testcontainers.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |
| T-01 | Unit tests: `ErrorTypeStatusCodeMap` / `ErrorProblemDetailsExtensions` — every `ErrorType` mapping, plus unmapped-falls-back-to-500 | `SharedKernel.Presentation.WebApi` | `●` |
| T-02 | Unit tests: `ResultHttpExtensions` — all four overloads (Minimal API plain, Minimal API `onSuccess`, MVC `ActionResult`, MVC `ActionResult<T>`), success and failure paths | `SharedKernel.Presentation.WebApi` | `●` |
| T-03 | Unit tests: `SharedKernelExceptionHandler` — known-exception status mapping, unknown-exception 500 fallback, `Detail` suppression outside `IsDevelopment()` | `SharedKernel.Presentation.WebApi` | `●` |
| T-04 | Integration tests (`WebApplicationFactory`): API versioning — unversioned-falls-back-to-default, URL-segment reader, header reader, `api-supported-versions` response header present | `SharedKernel.Presentation.WebApi` | `●` |
| T-05 | Integration tests (`WebApplicationFactory`): OpenAPI/Scalar — valid document per version group, Scalar route responds successfully | `SharedKernel.Presentation.WebApi` | `●` |
| T-06 | Unit tests: `CorrelationIdMiddleware` — header-present, header-absent-generates-new, response-header-always-set-including-on-short-circuit | `SharedKernel.Presentation.WebApi` | `●` |
| T-07 | Unit tests: `TenantContextHubFilter` / `HubExceptionMappingFilter` using SignalR's hub-testing harness — tenant attachment on connect, safe redaction for known and unknown exception types | `SharedKernel.Presentation.SignalR` | `●` |
| T-08 | Unit test: `HubGroupNaming.TenantGroup` format | `SharedKernel.Presentation.SignalR` | `●` |
| T-09 | Testcontainers-backed integration test (via `16.Testing`): message sent through one `IHubContext` instance reaches a client connected through a second, independently-configured instance sharing the same Redis backplane | `SharedKernel.Presentation.SignalR` | `●` |
| T-10 | Confirm all tests green; raise any `16.Testing` capability gap discovered during authoring back to arch-lead rather than working around it locally | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |
| T-11 | Unit tests: the three `[LoggerMessage]`-attributed methods carry their exact assigned `EventId` values (14000, 14001, 14100) — regression-proof against future accidental renumbering from method reordering/addition in the same class | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |
| T-12 | Unit test: `CorrelationIdMiddleware.BaggageKey` equals the literal `"correlation.id"` exactly — locks the contract value itself, not merely its existence | `SharedKernel.Presentation.WebApi` | `●` |
| T-13 | Integration test: exercise a request through `CorrelationIdMiddleware`, emit a log record downstream via `ILogger`, and — using a test-local minimal `BaseProcessor<LogRecord>` mirroring `13.ServiceDefaults`'s documented `BaggageLogRecordProcessor` contract (generic `Activity.Baggage` → `LogRecord.Attributes` copy, never overwriting an existing attribute) — assert the resulting `LogRecord.Attributes` contains the correlation id under the `CorrelationIdMiddleware.BaggageKey` key. Proves this domain's middleware output is compatible with `13.ServiceDefaults`'s P-251 ambient-log-enrichment mechanism with zero `ProjectReference` to `SharedKernel.ServiceDefaults`, mirroring the reciprocal technique `13.ServiceDefaults`'s own T-27 already uses in the opposite direction | `SharedKernel.Presentation.WebApi` | `●` |

---

## Phase: Docs <!-- phase-key: SK.14.Docs -->

> XML doc comments on all public APIs, README with usage examples, configuration reference.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |
| DO-01 | Complete XML doc comments on every public type/member, replacing Core-phase stubs — zero CS1591 warnings | `SharedKernel.Presentation.WebApi` | `●` |
| DO-02 | Complete XML doc comments on every public type/member, replacing Core-phase stubs — zero CS1591 warnings | `SharedKernel.Presentation.SignalR` | `●` |
| DO-03 | README: minimal ProblemDetails-only setup, full versioning+OpenAPI+Scalar setup, `Result<T>` boundary usage (Minimal API and MVC) | `SharedKernel.Presentation.WebApi` | `●` |
| DO-04 | README: minimal SignalR setup, Redis backplane setup, tenant-scoped group broadcast example | `SharedKernel.Presentation.SignalR` | `●` |
| DO-05 | Configuration reference documenting every DI extension method's options and defaults (e.g. `DefaultApiVersion`, `GroupNameFormat`, hub filter opt-out callback) | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |
| DO-06 | XML doc the new `CorrelationIdMiddleware.BaggageKey` constant and the three explicit `EventId` assignments; update this domain's Interface Contracts entry for `CorrelationIdMiddleware` to reference the constant instead of the inline literal | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |
| DO-07 | Record the discovered cross-domain literal mismatch (`13.ServiceDefaults`'s P-251 test design hardcodes `"CorrelationId"` instead of this domain's actual `"correlation.id"` key) in this domain's changelog as a flagged correction for `servicedefaults-arch-planner`/`servicedefaults-phase-implementer` — out of this domain's jurisdiction to fix directly | `SharedKernel.Presentation.WebApi` | `●` |

---

## Phase: Published <!-- phase-key: SK.14.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Package(s) | State |
| --- | --- | --- | --- |
| P-01 | Add NuGet packaging metadata (package id, version, description, license, repository URL matching platform convention) | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |
| P-02 | `dotnet pack` producing `.nupkg` + `.snupkg` for both packages with no warnings | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |
| P-03 | Consumer-verify harness: full WebApi stack (ProblemDetails, exception handler, versioning, OpenAPI/Scalar) wires up with zero DI exceptions | `SharedKernel.Presentation.WebApi` | `●` |
| P-04 | Consumer-verify harness: `AddSharedKernelSignalR` with and without `WithRedisBackplane` wires up with zero DI exceptions | `SharedKernel.Presentation.SignalR` | `●` |
| P-05 | Update Package Board to reflect both packages at `Published`/`●` | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |
| P-06 | Re-pack both packages reflecting the additive `EventId`/constant changes (patch version bump); re-run `consumer-verify` to confirm zero DI/build regressions; update Package Board notes | `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.SignalR` | `●` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | --- | --- | --- | --- |
| `SK.14.Design` | Design | 13 | 13 | 0 | `●` |
| `SK.14.Scaffold` | Scaffold | 10 | 10 | 0 | `●` |
| `SK.14.Core` | Core | 18 | 18 | 0 | `●` |
| `SK.14.Tests` | Tests | 13 | 13 | 0 | `●` |
| `SK.14.Docs` | Docs | 7 | 7 | 0 | `●` |
| `SK.14.Published` | Published | 6 | 6 | 0 | `●` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-25] Sub state-map initialized — phase key registry, 6 phases scaffolded at `○`, no tasks yet (claude)
- [2026-06-25] WO-031 P-192–P-198 dispatched: Design phase signed off (D-01–D-11, all `●`) — `SK.14.Core` → `13.ServiceDefaults` Cross-Domain Dependencies row removed and resolution noted (correlation-id baggage key is this domain's own contract); Scaffold (S-01–S-10), Core (C-01–C-13), Tests (T-01–T-10), Docs (DO-01–DO-05), Published (P-01–P-05) tasks added at `○`; Package Board advanced to Scaffold for both packages (presentation-arch-planner, WO-031)
- [2026-06-25] SK.14.Scaffold complete (S-01–S-10, all `●`): NuGet refs and ProjectReferences added to both `.csproj` files, folder structures created (`Errors/`, `Results/`, `ExceptionHandling/`, `Versioning/`, `OpenApi/`, `Middleware/` for WebApi; `Filters/`, `GroupNaming/`, `Extensions/` for SignalR), both packages confirmed already registered in `Platform.SharedKernel.slnx`, nested `.Tests` projects created referencing `SharedKernel.Testing`, solution builds 0 warnings/0 errors for all four 14.Presentation projects (presentation-phase-implementer)
- [2026-06-25] SK.14.Core complete (C-01–C-13, all `●`): full implementation of `ErrorTypeStatusCodeMap`/`ErrorProblemDetailsExtensions`/`ResultHttpExtensions`/`SharedKernelExceptionHandler`/API versioning/OpenAPI+Scalar/`CorrelationIdMiddleware` (WebApi) and `TenantContextHubFilter`/`HubExceptionMappingFilter`/`HubGroupNaming`/`AddSharedKernelSignalR`/`WithRedisBackplane` (SignalR). Both `.csproj` files gained a `SharedKernel.Core` ProjectReference (within the 01.Core layering allowance) for `SharedKernelException`. Both projects build 0 warnings/0 errors. 39 unit tests added and passing (30 WebApi, 9 SignalR); a standalone smoke test confirmed `AddSharedKernelApiVersioning`/`AddSharedKernelOpenApi`/`MapSharedKernelOpenApi` serve `/openapi/v1.json` and `/scalar/v1` end-to-end with HTTP 200. `Microsoft.OpenApi` 2.0.0's types live under namespace `Microsoft.OpenApi` (not `.Models`); SignalR's stock builder type is `ISignalRServerBuilder` and its Redis options type is `RedisOptions` — both confirmed via reflection against the installed packages, design-doc naming corrected to match. SK.14.Tests is next (presentation-phase-implementer)
- [2026-06-25] SK.14.Tests complete (T-01–T-10, all `●`): T-01/T-02/T-03/T-06/T-07/T-08 were already fully covered by the Core-phase unit tests (30 WebApi + 9 SignalR) — verified, not re-implemented. Three integration tests added to close the remaining gaps: `ApiVersioningIntegrationTests` (T-04, `WebApplicationFactory` over a real Minimal API host built via `WebApplicationFactory<T>.CreateHost` override — unversioned fallback, URL-segment reader, header reader, `api-supported-versions` response header); `OpenApiIntegrationTests` (T-05, with-versioning and without-versioning variants asserting `/openapi/v1.json` and `/scalar/v1` both return HTTP 200); `RedisBackplaneIntegrationTests` (T-09, `SharedKernel.Presentation.SignalR.Tests`, Testcontainers-backed via `SharedKernel.Testing.Containers.RedisContainerFixture` — two independent in-memory `TestServer` SignalR hosts both calling `WithRedisBackplane` against the same Redis container; a message sent via one instance's `IHubContext` is received by a `HubConnection` client connected through the other instance's `TestServer.CreateHandler()`). No `16.Testing` capability gap found — `RedisContainerFixture` was sufficient as-is (T-10). Two `Asp.Versioning.Http` 10.0.0 API-shape corrections discovered via reflection probe (not assumed from training data): `HttpContext` has no callable `GetRequestedApiVersion()` method — the correct usage is the extension property `HttpContext.RequestedApiVersion` (`Microsoft.AspNetCore.Http.HttpContextExtensions`, compiled as a property getter, not a method); `OpenApiExtensions.MapSharedKernelOpenApi` requires a `WebApplication` receiver, not `IApplicationBuilder` — test hosts must override `WebApplicationFactory<T>.CreateHost` and build a `WebApplication` directly rather than going through `IWebHostBuilder.Configure(IApplicationBuilder)`. SignalR Tests project gained `Microsoft.AspNetCore.SignalR.Client` 10.0.5 (pinned lower than the 10.0.9 WebApi-stack version — that is the latest available for this package) and `Microsoft.AspNetCore.TestHost` 10.0.9 package references. 38 WebApi + 10 SignalR = 48/48 tests passing, 0 failed. SK.14.Docs is next (presentation-phase-implementer)
- [2026-06-25] SK.14.Docs complete (DO-01–DO-05, all `●`): all public XML doc comments were already complete prose from the Core phase (no stubs found) — added `<GenerateDocumentationFile>true</GenerateDocumentationFile>` to both `.csproj` files to actually enforce CS1591/doc-comment warnings, then fixed 4 unresolved `cref` warnings surfaced by that flag (`IHostEnvironment.IsDevelopment()` and `MapOpenApi(IEndpointRouteBuilder, string)` in WebApi; `HubOptions.HubFilters` and ambiguous `HttpContext` in SignalR) by switching to `<c>` plain-text or fully-qualified crefs. Both packages now build 0 warnings/0 errors with doc generation on. Added `SharedKernel.Presentation.WebApi/README.md` (minimal ProblemDetails-only setup, full versioning+OpenAPI+Scalar setup, `Result<T>` boundary usage for Minimal API and MVC) and `SharedKernel.Presentation.SignalR/README.md` (minimal in-memory setup, Redis backplane setup, tenant-scoped group broadcast example). Added `14.Presentation/CONFIGURATION.md` documenting every DI extension method's parameters/options/defaults for both packages, linked from both READMEs. 48/48 tests (38 WebApi + 10 SignalR) still passing after the csproj/source edits. SK.14.Published is next (presentation-phase-implementer)
- [2026-06-25] SK.14.Published complete (P-01–P-05, all `●`): both `.csproj` files gained full NuGet packaging metadata (`PackageId`, `Version=1.0.0`, `Authors`/`Company`/`Product=Gresta-Vertex-Labs`/`Platform.SharedKernel`, `Description`, `PackageTags`, `PackageLicenseExpression=MIT`, `PackageReadmeFile`, `RepositoryUrl`/`PackageProjectUrl`, `Copyright`, `IncludeSymbols`+`SymbolPackageFormat=snupkg`) matching the `12.Security`/`13.ServiceDefaults` convention exactly, plus a `<None Include="README.md" Pack="true" PackagePath="\" />` item since both READMEs already existed from the Docs phase. `dotnet pack --configuration Release` produced `SharedKernel.Presentation.WebApi.1.0.0.nupkg`+`.snupkg` and `SharedKernel.Presentation.SignalR.1.0.0.nupkg`+`.snupkg` with 0 warnings for both. Added a new shared `14.Presentation/consumer-verify/consumer-verify.csproj` (non-packable exe, `TreatWarningsAsErrors=true`, mirroring `13.ServiceDefaults/consumer-verify`) registered in `Platform.SharedKernel.slnx`: Surfaces 1–4 compose the full WebApi stack (`AddProblemDetails`/`AddExceptionHandler<SharedKernelExceptionHandler>`/`AddSharedKernelCorrelationId`/`AddSharedKernelApiVersioning`/`AddSharedKernelOpenApi`) and resolve `IExceptionHandler`/`IApiVersionDescriptionProvider` plus build the request pipeline (`UseSharedKernelCorrelationId`/`UseExceptionHandler`/`MapSharedKernelOpenApi`) with zero DI exceptions; Surfaces 5–6 resolve `AddSharedKernelSignalR()` with and without `.WithRedisBackplane(...)` by constructing a `HubConnectionHandler<ConsumerVerifyHub>` (a minimal harness-only test `Hub`) from the composed container — proven to require no live Redis instance, since `AddStackExchangeRedis` defers connection until first use. **Correction discovered while writing the harness:** `HubOptions.HubFilters` is not a publicly accessible member shape suitable for a DI-resolution assertion (confirmed via build error, consistent with the existing Docs-phase note that this member name doesn't resolve cleanly under `cref`) — the harness instead asserts hub-filter wiring indirectly by resolving a constructed `HubConnectionHandler<THub>`, which transitively proves `HubOptions`/filter configuration succeeded. All 48 existing tests (38 WebApi + 10 SignalR) still passing; full `14.Presentation` solution scope (both packages + both test projects + consumer-verify) builds 0 warnings/0 errors. Six pre-existing NU1605 errors in unrelated `02.Caching.Redis.*`/`11.Communication.GraphQL` test projects were confirmed present on `main` before this session's changes (verified via `git stash`) — out of scope for this phase. Package Board updated to `Published`/`●` for both packages (presentation-phase-implementer)
- [2026-07-14] WO-041 P-256 closed — D-12/D-13 confirmed (BaggageKey constant was already present in code), then continued through C-14–C-18, T-11–T-13, DO-06/DO-07, and P-06 in the same session now that `01.Core`'s P-249 `LoggingEventIdRanges.Presentation` constant landed. Added explicit `EventId`s to all three `[LoggerMessage]` methods (`CorrelationIdMiddleware` = 14000, `SharedKernelExceptionHandler` = 14001, `HubExceptionMappingFilter` = 14100), all `LoggingEventIdRanges.Presentation + N`-derived. Added 4 new tests to `SharedKernel.Presentation.WebApi.Tests` (`LoggerMessageEventIdTests` ×2 reflection-based EventId pins, `CorrelationIdMiddlewareTests.BaggageKey_EqualsExpectedLiteral`, `CorrelationLogRecordIntegrationTests` — a test-local `BaseProcessor<LogRecord>` mirroring `13.ServiceDefaults`'s `BaggageLogRecordProcessor` contract, zero `ProjectReference` to `SharedKernel.ServiceDefaults`) and 1 new test to `SharedKernel.Presentation.SignalR.Tests` (`HubExceptionMappingFilterEventIdTests`). Both packages re-packed to `1.0.1`; `consumer-verify` re-run confirms all 6 surfaces PASS with zero DI exceptions (pre-existing, unrelated `NU1903`-as-error on `Microsoft.OpenApi` 2.0.0 confirmed present identically on `main` before this session via `git stash` — not a regression). 42/42 WebApi + 11/11 SignalR tests passing. All six phases (`SK.14.Design` through `SK.14.Published`) now fully `●` — `14.Presentation` domain complete end to end (presentation-phase-implementer)
- [2026-07-09] WO-041 P-256 dispatched — explicit `EventId` assignment and correlation verification. Design: D-12 locks the `LoggingEventIdRanges.Presentation` (14000) sub-block split (`WebApi` = 14000–14099, `SignalR` = 14100–14199) and the three exact `EventId`s (`CorrelationIdMiddleware` = 14000, `SharedKernelExceptionHandler` = 14001, `HubExceptionMappingFilter` = 14100); D-13 promotes the inline `"correlation.id"` baggage-key literal to a public `CorrelationIdMiddleware.BaggageKey` constant, motivated by discovering `13.ServiceDefaults`'s own P-251 test design (T-27) hardcodes a _different_, incorrect literal (`"CorrelationId"`) for "the exact mechanism this middleware uses" — a mismatch invisible to either domain's test suite since neither takes a `ProjectReference` on the other; flagged for `servicedefaults-arch-planner` to correct, out of this domain's jurisdiction to fix directly (DO-07). Core: C-14–C-16 assign the three `EventId`s (blocked on `01.Core`'s P-249 `LoggingEventIdRanges.Presentation` constant landing — new Cross-Domain Dependencies row added, `Pending`); C-17 extracts the baggage-key constant; C-18 confirms zero `SK0020`/`SK0021` diagnostics (informational — this domain already conforms). Tests: T-11 pins the three `EventId`s against future accidental renumbering; T-12 pins the baggage-key constant's literal value; T-13 is a self-contained integration test using a test-local minimal `BaseProcessor<LogRecord>` mirroring `13.ServiceDefaults`'s documented `BaggageLogRecordProcessor` contract to prove the correlation id lands on `LogRecord.Attributes` — deliberately avoids any `ProjectReference` to `SharedKernel.ServiceDefaults`, mirroring the reciprocal, jurisdiction-respecting technique `13.ServiceDefaults`'s own T-27 already uses in the opposite direction. Docs: DO-06/DO-07. Published: P-06 (re-pack, patch bump, consumer-verify re-run). Overall Progress counts updated (Design 11/13, Core 13/18, Tests 10/13, Docs 5/7, Published 5/6, all now `◐`); Package Board notes updated on both packages noting the pending sub-phase (presentation-arch-planner, WO-041)
