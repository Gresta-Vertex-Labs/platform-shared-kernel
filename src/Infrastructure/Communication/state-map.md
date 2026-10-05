# 11.Communication — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

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

Test kit: `src/Testing/SharedKernel.Communication.Testing` (`StubHttpMessageHandler` + `UseStubHttpMessageHandler`, `GrpcCalls`). Worked example: `samples/CheckoutApi` → `samples/InventoryApi`; `consumer-verify` covers 5 surfaces.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |
| `SK.11.Design` | Design (D-01–D-32) | ● |
| `SK.11.Scaffold` | Scaffold (S-01–S-14) | ● |
| `SK.11.Rest` | Rest (R-01–R-26; root P-154) | ● |
| `SK.11.Grpc` | Grpc (G-01–G-20; root P-156) | ● |
| `SK.11.GraphQL` | GraphQL (GQ-01–GQ-10; root P-157) — package since moved to `14.Presentation` | ● |
| `SK.11.Internal` | Internal (I-01–I-13; root P-155) — package since deleted | ● |
| `SK.11.Tests` | Tests (T-01–T-38) | ● |
| `SK.11.Docs` | Docs (DO-01–DO-15) | ● |
| `SK.11.Published` | Published (PB-01–PB-08; PB-06 per-package publish superseded by the release train) | ● |

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. (The old `○` rows for `01.Core` P-249 and `00.Governance` P-250 were stale — both shipped 2026-07-13.)

## Completed Phases

- Pre-publish gold-standard pass ● `.Internal` → `SharedKernel.Communication` base; real service discovery; safe POST/PATCH retries; hedging; outbound auth; mTLS; Result helpers for REST and gRPC; nine defects fixed; InventoryApi/CheckoutApi samples (2026-09-27)
- WO-086 ● `IRequestContext` replaces `IUserContext`; `RequestContextDelegatingHandler`; GraphQL moved to `14.Presentation`; Adapter tier; `Communication.Testing` (P-565, P-566, P-570, P-571, P-574, P-575) (2026-09-26)
- WO-056 ● Gap-fill: canonical correlation GUID, `IClock`, validate-at-consumption, real gRPC deadlines, consumer-verify harness (P-356–P-364) (2026-08-12)
- WO-052 ● `SharedKernel.Contracts.Envelopes` namespace adoption (P-329) (2026-07-31)
- WO-042 ● Header names from `WellKnownHeaders` (P-260) (2026-07-15)
- WO-041 ● `[LoggerMessage]` retrofit for Grpc and Internal (P-255) (2026-07-13)
- WO-026 ● Rest/Grpc/GraphQL/Internal correctness fixes (P-160–P-165) (2026-06-18)
- Earlier phases (WO-025 Design → Tests, P-154–P-157) — archived.

## Changelog

- [2026-09-28] State map rewritten as a living board; completed phase detail archived outside the repository.
- [2026-09-27] Pre-publish gold-standard pass — direct user request.
- [2026-09-26] WO-086 recorded (P-575).
- [2026-08-12] PB-06 per-package NuGet publish retracted by user decision (later superseded by the release train).
- [2026-08-12] WO-056 Tests/Docs/Published closed.
