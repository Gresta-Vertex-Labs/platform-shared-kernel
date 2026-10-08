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

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. (The old `○` rows for `01.Core` P-249 and `00.Governance` P-250 were stale — both shipped 2026-07-13.)
