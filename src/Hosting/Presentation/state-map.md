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

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. (The WO-074/P-468 and WO-078/P-484 rows the old progress table still counted as pending are all ● in their phase tables; the `PUB` "first publish" rows of P-563/P-579 are not domain work — every package publishes together through the release train, root P-577.)

## Blocked

None.

## Cross-Domain Dependencies

None open. (`01.Core`'s `LoggingEventIdRanges`, `WellKnownHeaders`, `Error.Forbidden` and `SharedKernel.Localization` all shipped. `ErrorType.PreconditionFailed` was declined by design: 412 is an HTTP-native outcome.)
