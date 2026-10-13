# 14.Presentation — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Presentation.Core` | Host | ● | Shared by WebApi and gRPC (WO-086 P-570, reshaped by P-579): `[RequireEndpointPermission]`/`[RequireRole]`/`[RequireFreshAuthentication]`/`[RequireAuthenticationMethod]` (`SharedKernel.Presentation.Authorization`) over native policies evaluated against `IUserContext`; policy machinery, error presentation and the `ErrorType` → HTTP map (internal). |
| `SharedKernel.Presentation.WebApi` | Host | ● | `AddSharedKernelWebApi()` + `UseSharedKernelWebApi(p => …)`: one RFC 9457 problem shape (localized, redacted outside Development), typed results, `IEndpointModule` + generated `MapEndpoints()`, `Paging`/`CursorPaging`, `IdempotencyKey`/`IfMatch<T>` (412), security headers, CORS, rate-limit 429 shaping. No MediatR. |
| `SharedKernel.Presentation.WebApi.Generators` | Tooling | ● | Source generator for `MapEndpoints()` (netstandard2.0, SKEP001–SKEP004); not packable on its own — packed under `analyzers/dotnet/cs` of WebApi. |
| `SharedKernel.Presentation.OpenApi` | Host | ● | Asp.Versioning, one OpenAPI document per version, Scalar, sunset/deprecation policies, security schemes; documents unpublished outside Development unless `ExposeInProduction` (P-562). |
| `SharedKernel.Presentation.Grpc` | Host | ● | Rich `google.rpc.Status` mapping (`GrpcStatusCodeMap`, internal), foreign statuses sanitized; calls run through the HTTP pipeline so the request context and `Core` attributes apply. References Core, never WebApi or Contracts. |
| `SharedKernel.Presentation.SignalR` | Host | ● | Error contract (`HubErrorMessage`), `RequestContextHubFilter` (connection's `RequestContextScope`), partitioned invocation rate limit, `HubGroupNaming` (incl. `TenantGroup(TenantId)`). |
| `SharedKernel.Presentation.GraphQL` | Host | ● | HotChocolate conventions (`FilterBase<T>`/`SortBase<T>`, `PagedResponseType<T>.FromPagedList`); moved from `11.Communication` (WO-086 P-570). |
| `SharedKernel.Presentation.SignalR.Redis` | — | ⊘ | Created by WO-086 (P-570), deleted by P-579; use SignalR's own `AddStackExchangeRedis(…)`. |

Test helpers: `src/Hosting/Presentation/SharedKernel.Presentation.Testing`. Configuration reference: `CONFIGURATION.md`. `consumer-verify` composes the request context first and exercises a hub method and a gRPC method.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **Inbound, 01.Core:** `SK.01.ObservabilityHelpers` (`ActivityFailureExtensions`, `IHasError`, `WellKnownOutcomes`, `LoggingEventIdRanges.SubBlockStart`/`IsInSubBlock`/`PackageSubBlockWidth`, `WellKnownTagKeys.ErrorCode`) for both phases; `SK.01.CoreConformance` renames `ErrorCodes.Unexpected.Default` to `unexpected.default` (this domain uses the constant everywhere, P-612 updates docs and tests).
- **Inbound, 13.ServiceDefaults:** `SK.13.HostObservabilityFixes` tags the server span (`correlation.id`, `tenant.id`) before `UseExceptionHandler()` and keeps instrumentation `RecordException` off; this domain records `error.code` and the exception event on that same span exactly once (D-02). Inbound actor/tenant headers are never applied by HTTP or gRPC servers; nothing here reads them. `SK.13.WildcardTelemetry` subscribes the framework SignalR sources; this domain creates no hub source (D-03).
- **Inbound, 16.Testing:** `SK.16.TelemetryCapture` (`ActivityRecorder`, `ShouldBeRecordedFailure`/`ShouldBeRecordedException`, log convention assertions); an `IHttpContextAccessor` double for the GraphQL tests belongs to `SharedKernel.Presentation.Testing` and is added in P-595 if the filter tests need one (note to `16.Testing` for the catalogue).
- **Inbound, 00.Governance:** `SK.00.ObservabilityReportOnly` (P-583) shows zero findings at the end of P-612; `PresentationLayeringRules` and `DependencyGraphRulesTests` are re-run after GraphQL references Core and WebApi (T-13); G4 (an exception exposing an `Error` must derive `SharedKernelException`) is the guard behind the T-09 negative control.
- **Inbound, owners of the matrix rows (ordering only):** SK.01.CoreObservabilityFixes, SK.02.LockUnavailableError, SK.05.PipelineTelemetryGaps, SK.06.PersistenceErrorSafety, SK.07.MessagingErrorSafety, SK.08.StorageExceptionEdge, SK.09.SearchErrorSafety, SK.10.IntelligenceObservability, SK.11.TokenUnavailableEdge, SK.17.WorkflowObservabilityFixes, SK.20.ReportingErrorShape, SK.20.ReportingConformance; and `SK.06.PersistenceConformance`/`SK.08.StorageConformance` for the public `…ErrorCodes` constants that pin the precondition defaults.
- **Outbound, 11.Communication:** no rename of `grpc.{status}`, `http.{status}`, `request.too_large` or any `PresentationErrorCodes` value; `unexpected.exception` to `unexpected.default` follows 01. A SignalR or GraphQL consumer is not an outbound caller of this kernel, so no change there.
- **Outbound, 01.Core (note for the core planner, optional):** a tag-only form of the recorder (`error.code` and `error.type` without touching status) would replace this domain's client-category tagging helper with a shared call; this domain works without it.
- **Outbound, 05.Application, 02/06/07/08/09/10/17/18/20 and the Shop samples:** none planned here beyond the matrix rows above; sample and README updates for the SignalR trailer and GraphQL members are notes for the sample owners.
