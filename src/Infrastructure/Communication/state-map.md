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
| `SK.11.ServiceContractsRecipe` | Consumer-skill recipe: publish and consume a service's contracts and client | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.11.ServiceContractsRecipe — Recipe: publish and consume a service's contracts and client `○`

**Work order:** WO-090 / P-631 · **Depends on:** P-629 (SK.02.RecipeRequiredSettings; it touches no needs-table row, so no conflict). P-633 (19.Scheduling) edits the needs table after this phase.

Consumer-skill documentation only: no package, API, option or error-code change, so a 1.0.0 consumer is unaffected. The skill gains one recipe for the pattern every service follows: a contracts project (integration events, DTOs, error codes, JSON context), the typed REST client convention, and consuming another service's client or event type behind the consumer's own port in Infrastructure. The skill also states, once in the entry file and once in the new-service recipe, that recipes decide packages and APIs while the service's own `CLAUDE.md` decides folders. Verdict: accept, reshaped to what the Shop does on disk. The P-entry's "never referenced from Application" is narrowed to the transport client: Ordering's Application project references the contracts project for its event and DTO types (legal, contracts are Model tier), while every client class and registration stays in Infrastructure.

**Proof matrix (verified on disk):**

| Claim | Evidence | Proof |
| --- | --- | --- |
| Publish contracts: events with `[IntegrationEvent("{context}.{name}", Version = n)]`, DTOs, error codes, source-generated JSON context | `samples/Shop/Shop.Contracts/Billing/BillingContracts.cs`, `.../Ordering/OrderEvents.cs`, `Shop.Contracts.csproj`; one shared `Shop.Contracts` project for all services, not one per service | `proven-by` (shape: services reference a contracts project, never another service's projects) |
| Additive-only evolution, bumping an event `Version` | every Shop event is `Version = 1`; no v2 exists | `unproven` (rule from the contracts package README, no snippet) |
| One contracts package per service, published to a feed | Shop shares one project and packs nothing | `unproven` |
| Publish a typed `Client` for others to reference | Shop has no client project; callers write their own class over the REST client package | `unproven` (prose only, no snippet) |
| Consume another service's REST client behind a local port | `Ordering/Shop.Ordering.Application/OrderingPorts.cs` (`IPayments`), `Ordering/Shop.Ordering.Infrastructure/Billing/RestPayments.cs`, `.../OrderingInfrastructure.cs` | `proven-by` |
| Consume another service's gRPC client behind a local port | `.../Inventory/GrpcInventoryReservations.cs` | `proven-by` |
| Consume another service's event type | `samples/Shop/Notify/Shop.Notify.Worker/Receipts.cs` (`ReceiptDueConsumer` over `EventEnvelope<ReceiptDue>`); Notify references `Shop.Contracts` only. The consumer is a Worker class, not behind a port | `proven-by` |
| Application keeps the transport out | `Shop.Ordering.Application.csproj`: kernel Abstractions plus `Shop.Contracts`, no Communication package | `proven-by` |

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify recipe name `add-service-contracts.md`, its scope (contracts project, client convention, consuming behind a port), the proof matrix and the 80-250 line budget; cross-check naming and version rules against the contracts package README so the recipe states nothing the README does not | consumer skill | `○` |
| D-02 | Ratify exactly two new needs-table rows, no other row edited. "Publish my service's contracts and client": `SharedKernel.Contracts`, `SharedKernel.Communication.Rest`; reference from a shared contracts project and Infrastructure; entry points `[IntegrationEvent("{context}.{name}", Version = n)]`, `AddRestClient<IClient, Client>("name")`. "Call or consume another service": `SharedKernel.Communication.Rest`, `SharedKernel.Communication.Grpc`, `SharedKernel.Messaging.Abstractions`; Application (port) and Infrastructure (client, `ConsumerBase<T>`). Both route to `add-service-contracts.md`; the existing "An integration event contract or paging DTO" row stays | consumer skill | `○` |
| D-03 | Ratify the folder line: "Recipes decide packages and APIs; the service's own `CLAUDE.md` decides folders and file placement." One bullet in the skill's non-negotiables, one sentence in the new-service recipe | consumer skill | `○` |
| DO-01 | Write `recipes/add-service-contracts.md` per the recipe standard: `proven-by`/`unproven` frontmatter from the matrix; Packages table; steps (contracts project, port in Application, client and registration in Infrastructure, event consumer); unproven parts as prose without snippets; Test it, Check, Read next. Snippets mirror the cited Shop files | consumer skill | `○` |
| DO-02 | Add the two rows and the recipe-list entry to `SKILL.md`, plus the folder bullet; add a "Not when" pointer to the new recipe in `add-outbound-call.md` | consumer skill | `○` |
| DO-03 | Add the folder sentence to `new-service.md`; the file is already 250 lines (the recipe limit), so trim one line elsewhere in it | consumer skill | `○` |
| T-01 | `dotnet run eng/generate-package-index.cs`, then `-- --check`: catalog regenerated, no unrouted package, every `proven-by` path exists, no maintainer vocabulary; `bash eng/pack-consumer-skill.sh 0.0.0-local artifacts/skill` leaks no repository path | eng | `○` |
| T-02 | Confirm each snippet matches its cited Shop file and `samples/Shop` still builds | samples | `○` |

**Acceptance:**
- [ ] The recipe covers publishing contracts (event naming and version rule, additive-only evolution), publishing a client, and consuming a foreign client or event type behind a local port; code snippets compile against the public API.
- [ ] The needs table gains exactly the two rows in D-02 and no other row changes.
- [ ] Every proof claim is a `proven-by` Shop path that exists, or an `unproven` entry with a reason (per-service contracts package, typed client package, event version bump).
- [ ] The folder line is present in the skill entry file and in the new-service recipe; no recipe exceeds 250 lines.
- [ ] Consumer vocabulary only; the catalog regenerates and `--check` passes.

## Blocked

None.

## Cross-Domain Dependencies

- **Consumer skill (WO-090):** SK.11.ServiceContractsRecipe lands after P-629 (SK.02.RecipeRequiredSettings) and before P-633 (19.Scheduling), which edits the needs table too; this phase adds only the two rows named in its D-02.
- **14.Presentation:** the `grpc.{status}` and `http.{status}` names in `GrpcErrorCodes.ForStatus` and the framework-problem codes are mirrored by hand in `CommunicationErrorCodes`, so a rename there must be mirrored here.
