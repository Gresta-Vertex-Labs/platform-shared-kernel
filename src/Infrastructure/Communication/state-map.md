# 11.Communication — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Communication` | Adapter | ● | Shared base (2026-09-27): `AddSharedKernelCommunication(configuration)`, per-client settings from `SharedKernel:Communication:Clients:{name}` (validated on start), service discovery through `Microsoft.Extensions.ServiceDiscovery` (`Services` section, `Dns`, `DnsSrv`; round-robin per request), outbound auth (client credentials, API key, `IAccessTokenProvider`), mutual TLS, `communication.*` error codes. |
| `SharedKernel.Communication.Rest` | Adapter | ● | `AddRestClient<TClient, TImplementation>(name)`; Microsoft.Extensions.Http.Resilience (retry, timeout, breaker, hedging) that never repeats a POST/PATCH without an `Idempotency-Key`; `RequestContextDelegatingHandler` writes the caller headers from `IRequestContextAccessor`; `GetResultAsync`/`PostResultAsync`/… → `Result<T>`. Declared edge → `Communication`. |
| `SharedKernel.Communication.Grpc` | Adapter | ● | `AddGrpcClient<T>(name)`: deadline, retry policy, keepalive; one `RequestContextInterceptor`; `ToResultAsync()` over the rich status; `google.type.Money` ↔ `Money`. Never references `SharedKernel.Contracts`. Declared edge → `Communication`. |
| `SharedKernel.Communication.Internal` | — | ⊘ | Deleted 2026-09-27; replaced by the `SharedKernel.Communication` base. |
| `SharedKernel.Communication.GraphQL` | — | ⊘ | Moved to `14.Presentation` as `SharedKernel.Presentation.GraphQL` (WO-086, P-570). |

Test kit: `src/Infrastructure/Communication/SharedKernel.Communication.Testing` (`StubHttpMessageHandler` + `UseStubHttpMessageHandler`, `GrpcCalls`). Worked example: the Shop (`samples/Shop`), Ordering → Inventory over gRPC with mutual TLS and Ordering → Billing over REST with an API key; `consumer-verify` covers 5 surfaces.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

Inbound (this domain waits on):
- **01.Core** `SK.01.ObservabilityHelpers` (P-580): `IHasError`, `LoggingEventIdRanges.SubBlockStart/IsInSubBlock`. `SK.01.CoreObservabilityFixes` (P-584): `CommitOutcomeUnknownException` -> `execution.commit_outcome_unknown` / Timeout (504). `SK.01.CoreConformance` (P-600): `unexpected.exception` -> `unexpected.default`; this domain holds no literal of either, the reverse maps read `errorCode`, and T-06 pins both.
- **16.Testing** `SK.16.TelemetryCapture` (P-581): span, metric and log capture; `ActivityRecorder` moves from `SharedKernel.Testing.Communication` to `SharedKernel.Testing.Telemetry` (README of `SharedKernel.Communication.Testing` follows, C-05).
- **00.Governance** `SK.00.ObservabilityReportOnly` (P-583): report-only rules the conformance phase must clear; SK0044 bans bare `EventId` literals.
- **13.ServiceDefaults** `SK.13.WildcardTelemetry` (P-582): `WithCommunicationTelemetry()` keeps the gRPC client instrumentation and the Polly meter; no code change here.

Outbound (notes for other domains, nothing planned here):
- **14.Presentation:** assert at the three edges (WebApi exception handler, gRPC server interceptor, SignalR hub filter) that an `AccessTokenUnavailableException` maps to 503 / `Unavailable` / `communication.access_token_unavailable`; `grpc.{status}` and `http.{status}` names in `GrpcErrorCodes.ForStatus` and the framework-problem codes are mirrored by hand here, so a rename there must be mirrored in `CommunicationErrorCodes`.
- **13.ServiceDefaults:** `ServiceDefaults.Security`'s `EndToEndPropagationTests` may add a raw typed-client call with a failing token provider expecting 503 (the end-to-end proof of the P-592 edge mapping).
- **All consumers:** `communication.unreachable` -> `communication.unavailable`, `money.proto.invalid` -> `communication.grpc.invalid_money`, the authentication handler's logger category, and `AccessTokenUnavailableException` no longer being an `HttpRequestException` are release-note items (WO-087 decision 2).
- **samples/Shop and `consumer-verify`:** compile-only checks (T-06 / T-08).
