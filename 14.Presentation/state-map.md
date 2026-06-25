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
| `SharedKernel.Presentation.WebApi` | Published | `●` | Published complete (P-01–P-05) — NuGet packaging metadata added, `dotnet pack` produces `.nupkg`+`.snupkg` with 0 warnings, consumer-verify harness confirms zero DI exceptions across ProblemDetails/exception-handler/versioning/OpenAPI+Scalar. 38/38 tests still passing |
| `SharedKernel.Presentation.SignalR` | Published | `●` | Published complete (P-01–P-05) — NuGet packaging metadata added, `dotnet pack` produces `.nupkg`+`.snupkg` with 0 warnings, consumer-verify harness confirms zero DI exceptions for `AddSharedKernelSignalR` with and without `WithRedisBackplane`. 10/10 tests still passing |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.14.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Error`, `ErrorType`) | Available |
| `SK.14.Scaffold` | `04.Contracts` | `SharedKernel.Contracts` ProjectReference (cross-reference only — `Envelope<T>` is a sibling concern to `ProblemDetails`, not a dependency of it) | Available |
| `SK.14.Core` | `12.Security` | `SharedKernel.Security.Abstractions` ProjectReference (`IUserContext`, `ITenantProvider`) — consumed by the OpenAPI Bearer scheme metadata and by `TenantContextHubFilter` | Available |

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

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | --- | --- | --- | --- |
| `SK.14.Design` | Design | 11 | 11 | 0 | `●` |
| `SK.14.Scaffold` | Scaffold | 10 | 10 | 0 | `●` |
| `SK.14.Core` | Core | 13 | 13 | 0 | `●` |
| `SK.14.Tests` | Tests | 10 | 10 | 0 | `●` |
| `SK.14.Docs` | Docs | 5 | 5 | 0 | `●` |
| `SK.14.Published` | Published | 5 | 5 | 0 | `●` |

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
