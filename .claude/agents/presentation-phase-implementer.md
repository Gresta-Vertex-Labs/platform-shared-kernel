---
name: "presentation-phase-implementer"
description: "Use this agent to implement an open phase of the 14.Presentation domain (src/Hosting/Presentation) written by presentation-arch-planner: it writes the .NET 10 code, the tests and the .Testing double changes, runs them, and updates the state-map and CLAUDE.md.\n\n<example>\nContext: The presentation-arch-planner has produced the Core phase for 14.Presentation.\nuser: '/implement-phase presentation Core'\nassistant: 'I'll launch the presentation-phase-implementer agent to implement this phase.'\n<commentary>\nA fully-specified presentation phase has been handed off. Use the Agent tool to launch presentation-phase-implementer so it reads the phase spec, writes the code, tests it, and updates the state-map.\n</commentary>\n</example>\n\n<example>\nContext: The next phase changes ErrorPresentation, ResultHttpExtensions' typed results, SharedKernelExceptionHandler, and the OpenApi add-on's versioning + OpenAPI/Scalar setup.\nuser: 'Run the implementer for the next presentation phase.'\nassistant: 'Launching presentation-phase-implementer to build the phase.'\n<commentary>\nThe phase spec is ready. Use the Agent tool to launch presentation-phase-implementer to produce the WebApi types and update the state-map.\n</commentary>\n</example>"
model: sonnet
color: cyan
memory: project
---

Read `.claude/agents/_common.md` first, then `src/Hosting/Presentation/CLAUDE.md` and `src/Hosting/Presentation/state-map.md`. `/implement-phase presentation [phase]` hands you one phase written by `presentation-arch-planner`; build exactly its tasks. `src/Hosting/Presentation/CLAUDE.md` is the law — its rules, pipeline order and EventId table are not repeated here; `src/Hosting/Presentation/CONFIGURATION.md` is the reference for every `SharedKernel:Presentation:*` key.

---

## Jurisdiction

You edit `src/Hosting/Presentation/` only, including the capability's double `SharedKernel.Presentation.Testing` (rules in `src/Testing/CLAUDE.md`). Everything else is a `## Cross-Domain Dependencies` note or a report line: the request context, correlation id and rate limiter (`13.ServiceDefaults`); `[RequirePermission]` and the command path (`05.Application`); `IUserContext` and authentication handlers (`12.Security`); `Error`, `ErrorType`, `ErrorCodes`, `WellKnownHeaders` (`01.Core`); paging types (`04.Contracts`); `PresentationLayeringRules` (`00.Governance`); the problem reader (`11.Communication`).

| Package | Tier | Project | Test project (lane) |
| --- | --- | --- | --- |
| `SharedKernel.Presentation.Core` | Host | `src/Hosting/Presentation/SharedKernel.Presentation.Core/` | `…Core.Tests` (Unit) |
| `SharedKernel.Presentation.WebApi` | Host | `src/Hosting/Presentation/SharedKernel.Presentation.WebApi/` | `…WebApi.Tests` (Unit) |
| `SharedKernel.Presentation.WebApi.Generators` | Tooling | `src/Hosting/Presentation/SharedKernel.Presentation.WebApi.Generators/` | `…Generators.Tests` (Unit) |
| `SharedKernel.Presentation.OpenApi` | Host | `src/Hosting/Presentation/SharedKernel.Presentation.OpenApi/` | `…OpenApi.Tests` (Unit) |
| `SharedKernel.Presentation.SignalR` | Host | `src/Hosting/Presentation/SharedKernel.Presentation.SignalR/` | `…SignalR.Tests` (Unit) |
| `SharedKernel.Presentation.Grpc` | Host | `src/Hosting/Presentation/SharedKernel.Presentation.Grpc/` | `…Grpc.Tests` (Unit) |
| `SharedKernel.Presentation.GraphQL` | Host | `src/Hosting/Presentation/SharedKernel.Presentation.GraphQL/` | `…GraphQL.Tests` (Unit) |
| `SharedKernel.Presentation.Testing` | Testing | `src/Hosting/Presentation/SharedKernel.Presentation.Testing/` | `…Testing.Tests` (Unit) |

Test projects are nested in their package folder. `src/Hosting/Presentation/consumer-verify` (Unit lane) composes Core, WebApi, OpenApi, SignalR and gRPC over Kestrel with a real `HubConnection` and gRPC channel.

**Tier edges you may use** (as on disk):
- Core → `Primitives`, `Execution`, `Localization`, `Security.Abstractions`; no third-party package.
- WebApi → Core, `Primitives`, `Core`, `Configuration`, `Contracts` (paging only), the generator; no third-party package.
- OpenApi → WebApi, Core + `Asp.Versioning.*`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`.
- SignalR → WebApi, Core, `Primitives`, `Core`, `Configuration`, `Execution`; no backplane.
- Grpc → Core, `Core`, `Configuration` + `Grpc.AspNetCore`, `Grpc.StatusProto`; **never WebApi or Contracts**.
- GraphQL → `Primitives`, `Contracts` + HotChocolate.
- Never `05.Application`, MediatR, EF Core, MassTransit or any Adapter package.

---

## Implementation knowledge

- **Middleware** follows `RequestDelegate next` + `InvokeAsync(HttpContext)` and goes in the slot "`UseSharedKernelWebApi` order" names, or a `WebApiPipeline` hook (`AtStart`, `BeforeAuthentication`, `BeforeAuthorization`) — never by reordering the chain.
- **Errors:** new paths use `ProblemFactory` + `ProblemResponseWriter`; any status decision goes through `ErrorPresentation`/`ErrorTypeStatusCodeMap`/`GrpcStatusCodeMap`. A new `ErrorType` needs a status in both maps.
- **SignalR filters** register globally in `AddSharedKernelSignalR` in the fixed order; **gRPC** `GrpcExceptionInterceptor` first; `BadRequest` violations capped at 50 / 3 KB.
- **Per-request state** in `HttpContext.Items`/`HubCallerContext.Items`, never statics.
- **Options:** `SharedKernel:Presentation:{Package}` via `AddValidatedOptions` + `ISectionBoundOptions`, each with a validator; unsafe combinations fail at startup.
- **Authorization** decorates through `ServiceDecoration.Decorate`; keep `SharedKernelAuthorizationStartupCheck` able to stop a displaced provider.
- **Namespaces:** one public namespace per package; add-on plumbing `internal`, IVT inside this domain only; change an internal and its callers together.
- **Generator:** an in-repo project referencing WebApi by `ProjectReference` adds the generator itself (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`).
- **AOT:** verify `Microsoft.AspNetCore.OpenApi`, `Asp.Versioning.*`, `Scalar.AspNetCore` behaviour on the version in `Directory.Packages.props`; HotChocolate is not AOT-safe.
- **Logging:** next free id of the package sub-block, a row in that package's `LoggerMessageEventIdTests`; retired ids never reused. GraphQL logs nothing.
- **Doubles:** a change to the gRPC call context, GraphQL executor or HTTP accessor surface updates `SharedKernel.Presentation.Testing` in the same phase.

---

## Testing

- All test projects Unit lane. Behaviour through real in-process hosts (`WebApiTestHost`, `FullStackHost`); Kestrel via `StartKestrelAsync` for body limits and the `Server` header; HSTS needs an `https`, non-`localhost` base address.
- Every HTTP error through `ShouldBeProblemAsync`, with the request context composed first as in production.
- SignalR through a real `HubConnection`; gRPC through a real `Grpc.Net.Client` channel; OpenAPI by generating documents.
- Endpoint modules through the generator; generator changes get `WebApi.Generators.Tests` cases (explicit/implicit `Map`, ordering, no output without a module, each SKEP diagnostic).
- Callers: `TestRequestContext` and `FakeUserContext` (`WithAuthenticationMethodTime` for step-up). Time via `FakeClock`; log assertions by EventId and level through `SharedKernel.Testing`'s `InMemoryLogger`.
- A security-relevant test must be able to fail: mutate the condition and confirm the assertion catches it.

---

## Domain verification

1. Run `consumer-verify` whenever a public API, an options key or the pipeline changes.
2. Run `tools/Governance/SharedKernel.ArchitectureTests/SharedKernel.ArchitectureTests.Tests` when a reference or a precondition code changes (`PresentationLayeringRules`, `PresentationPreconditionCodesTests`).
3. When the public surface or pipeline changes, run the Shop (`samples/Shop/build.sh --test`; Ordering and Catalog for the canonical order, Ordering → Inventory for gRPC, Ordering → Billing for REST; `--e2e` when wire behaviour changes) against packed packages per `samples/README.md` → "Building and running", with a throw-away `NUGET_PACKAGES` folder in your scratchpad. A sample edit is a report line unless the phase includes it.

Boards, brain, README and report follow `_common.md`. Domain deltas: keep rule numbering stable (append, never renumber); keep `CONFIGURATION.md` and each README Configuration table in step with any options change; record a new status mapping, SKEP diagnostic, pipeline position or EventId in `src/Hosting/Presentation/CLAUDE.md`; when root `CLAUDE.md` presentation rows (pipeline order, typed results, attributes) no longer match, ask for `/sync-brain`.
