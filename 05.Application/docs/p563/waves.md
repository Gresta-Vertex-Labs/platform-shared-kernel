# P-563 — how the work ran

The record of the "one application model with a thin HTTP edge" pass: what each stream owned, where it landed, and
how it was checked. The decisions are in [design.md](design.md) (A1–A6, P1–P4, S1). Branch `p563-application-model`,
from the P-562 branch at `824eb0da`.

## Steps

| Step | Scope | Landed |
| --- | --- | --- |
| Design | Owner decisions A1–A6 (05.Application), P1–P4 (14.Presentation), S1 (samples); target registration, public surfaces and namespaces | `24ad6654` |
| Stream **B** | 14.Presentation P3 and P4: one public namespace in WebApi (`.Errors`, `.Http`, `.Idempotency`, `.Options` folded into the root), the plumbing the OpenApi, SignalR and gRPC add-ons share made internal and visible to them (`ErrorPresentation`, `ErrorTypeStatusCodeMap`, `PresentationErrorCodes.ForStatus`, `AddSharedKernelAuthorization`, the header metadata interfaces), the add-ons' own maps and options folded the same way (`GrpcStatusCodeMap` internal, options classes in each root namespace), `GetIfMatchTags` removed, `Paging`/`CursorPaging` parameters validated by the header-requirements middleware and documented by OpenApi | `3174cebb`, merged `1c139dfa` |
| Stream **A** | 05.Application A1–A6: `.Behaviors` merged into `SharedKernel.Application`, `.Behaviors.Caching` renamed `SharedKernel.Application.Caching`, one `AddSharedKernelApplication` call with seams checked at host start, `[RequirePermission]` on the use case, behaviors internal in `SharedKernel.Application.Pipeline`, type forwarders removed, `ErrorCodes.Idempotency` in 01.Core; SK0040 and the architecture rules (00.Governance), 16.Testing's harness, 18.Idempotency, 19.Scheduling, 17.Workflows, 13.ServiceDefaults, 06.Persistence, the samples' registrations and CI follow | `594f76eb`, merged `ddf549ba` |
| Fix | One 401 code on every path: the pipeline's `authorization.unauthenticated` became `unauthorized.default`, the code the HTTP edge answers; `PresentationErrorCodes`' idempotency constants take their values from `ErrorCodes.Idempotency` | `7275b0da` |
| Stream **C** | 14.Presentation P2: `IEndpointModule` and the `MapEndpoints()` source generator (`SharedKernel.Presentation.WebApi.Generators`, diagnostics SKEP001–SKEP004), packed under `analyzers/dotnet/cs` of the WebApi package; consumer-verify uses a module | `69f7f1be`, merged `4b2a8df3` |
| Stream **S** | S1: all five samples use endpoint modules and send commands and queries through `ISender`; permissions on the use cases; `Features/` folders in vertical-slice shape | `906b78f3`, merged `a7dde581` |
| Rename | Owner decision after the streams: 14.Presentation's endpoint attribute and convention renamed `RequireEndpointPermission`, so they no longer share a name (and a CS0104 clash) with 05.Application's `[RequirePermission]` on use cases | `52975eed` |
| Docs | **D1** 05.Application and 14.Presentation: READMEs, `CONFIGURATION.md`, `CLAUDE.md` rewritten as rules with the superseded brain archived, state maps, this record. **D2** the root brain and the other domains' docs | this pass |
| Fix | Authorization fails closed: `AuthorizationBehavior` always registered, `WithAuthorization()` removed, a marked request with no `IRequestContext` refused and a marked request type in a scanned assembly checked at host start (the docs review found a service could mark its use cases and never enforce them) | `2848f827` |
| Fix | SK0015 exempts no call site: the method-name exemption for `AddStreamingBehaviors` hid the bug it exists to catch | `ea7d854b` |
| Verification | CI's three jobs on the finished branch: the Release build and unit lane (every unit test project, 8,732 tests) with both consumer-verify harnesses; the packaging job (86 packages at one version, every consumer harness, BillingApi 17/17, DocumentsApi 57/57, ShippingApi 9/9, the OrderApi smoke test); the container integration lane (21 projects; one RabbitMQ dead-letter test timed out under the parallel lane and passed 2/2 alone) | this record |

## How the work was run

- Streams ran in parallel in their own git worktrees under `C:\wt` (the agent tool's own worktrees exceed the
  Windows path limit). Each owned a fixed set of folders, never committed, and finished with its projects building and
  its tests passing.
- The orchestrator reviewed each result, committed it in its worktree and merged it into the branch with `--no-ff`.
  Nothing was pushed.
- B and A ran first because C and S build on them: C needs the flattened WebApi namespace, S needs both the new
  registration and the endpoint modules.
- Where the design and the code differ, the code is the record: the builder is `ApplicationPipelineBuilder`, the
  internal types live in `SharedKernel.Application.Pipeline`, and the pipeline's 401 code is `unauthorized.default`.
