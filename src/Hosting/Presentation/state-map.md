# 14.Presentation — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

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
| `SK.14.Design` | Design (D-01–D-86; WO-031, WO-041, WO-042, WO-058, WO-062, WO-063, WO-074, WO-078) | ● |
| `SK.14.Scaffold` | Scaffold (S-01–S-35) | ● |
| `SK.14.Core` | Core (C-01–C-87) | ● |
| `SK.14.Tests` | Tests (T-01–T-80) | ● |
| `SK.14.Docs` | Docs (DO-01–DO-34) | ● |
| `SK.14.Published` | Published (P-01–P-28) | ● |
| `SK.14.P563` | P-563 thin HTTP edge (P1–P4, S1, REN, DOC ●; PUB ⊘ — superseded by the repo-wide release train) | ● |
| `SK.14.P579` | P-579 main's P-562/P-563 redesign on the WO-086 foundation (R1–R9 ●; PUB ⊘ — superseded by the repo-wide release train) | ● |

## Open Work

None — every phase in this domain is complete. (The WO-074/P-468 and WO-078/P-484 rows the old progress table still counted as pending are all ● in their phase tables; the `PUB` "first publish" rows of P-563/P-579 are not domain work — every package publishes together through the release train, root P-577.)

## Blocked

None.

## Cross-Domain Dependencies

None open. (`01.Core`'s `LoggingEventIdRanges`, `WellKnownHeaders`, `Error.Forbidden` and `SharedKernel.Localization` all shipped. `ErrorType.PreconditionFailed` was declined by design: 412 is an HTTP-native outcome.)

## Completed Phases

- P-579 ● main's P-562/P-563 redesign re-applied on WO-086: `Presentation.Core`, correlation id and baggage refusal owned by `ServiceDefaults.Security`, gRPC scope from the pipeline, `SignalR.Redis` deleted (2026-09-26)
- WO-086 ● Every package Host tier; `Presentation.Core` and `.GraphQL` added (P-565–P-567, P-570, P-571, P-573, P-574) (2026-09-26)
- P-563 ● Thin HTTP edge: endpoint modules + generator, one public namespace per package, `Paging`/`CursorPaging`, `RequireEndpointPermission` rename (2026-09-24)
- P-562 ● Gold-standard pre-publish pass: WebApi/SignalR/Grpc rewritten, OpenApi split out (ran without state-map phases; decisions in `docs/p562/`) (2026-09-24)
- WO-078 ● Localized `Error.ToProblemDetails()` (P-484) (2026-08)
- WO-074 ● `SharedKernel.Presentation.Grpc` created (P-468) (2026-08)
- WO-063 ● Resource-exhaustion, OpenAPI security schemes, sunset headers, audit logging (P-411–P-418) (2026-08-21)
- WO-062 ● Multi-field validation problems, security headers, CORS, idempotency key, step-up attributes, ETag/412, 429 shaping (P-402–P-409) (2026-08-20)
- WO-058 ● Declarative role/permission endpoint authorization (P-381) (2026-08-17)
- WO-042 ● Shared `WellKnownHeaders` constants (P-262) (2026-07-16)
- WO-041 ● Explicit `[LoggerMessage]` EventIds (P-256) (2026-07-14)
- Earlier phases (WO-031 Design → Published, P-192–P-198) — archived.

## Changelog

- [2026-09-28] State map rewritten as a living board; stale pending counts and per-domain PUB rows closed.
- [2026-09-26] P-579 recorded as `SK.14.P579`.
- [2026-09-26] WO-086 recorded (P-565/P-566/P-567/P-570/P-571/P-573/P-574).
- [2026-09-24] P-563 recorded as `SK.14.P563` (docs stream D1).
- [2026-09-24] P-562 gold-standard pre-publish pass shipped; `CLAUDE.md` rewritten to rules, old brain moved to `CLAUDE.history.md`.
