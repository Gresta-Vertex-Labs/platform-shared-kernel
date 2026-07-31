---
name: project-communication
description: 11.Communication domain status, NuGet version pins, and key cross-phase implementation patterns discovered during SK.11 implementation
metadata:
  type: project
---

## Phase completion status (as of 2026-07-31)

- SK.11.Design: ● (25 tasks, incl. D-23 EventId allocation, D-24 WellKnownHeaders consumption, D-25 Envelopes-namespace adoption contract)
- SK.11.Scaffold: ● (13 tasks)
- SK.11.Rest: ● (21 tasks, 66/66 tests) — includes R-19/R-20 (WellKnownHeaders) and R-21 (Envelope→Envelopes namespace swap)
- SK.11.Grpc: ● (17 tasks, 60/60 tests) — includes G-14/G-15 [LoggerMessage] retrofit (EventId 11100/11101), G-16/G-17 WellKnownHeaders thin-alias retrofit
- SK.11.GraphQL: ● (9 tasks, 43/43 tests)
- SK.11.Internal: ● (11 tasks, 52/52 tests) — includes I-09/I-10/I-11 [LoggerMessage] retrofit (EventId 11300-11308)
- SK.11.Tests: ● (30 tasks) — T-27/T-28 (WO-041 P-255) + T-29 (WO-042 P-260) + T-30 (WO-052 P-329) all complete
- SK.11.Docs: ● (8 tasks) — DO-01..DO-08 all complete; DO-07 (P-260) and DO-08 (P-329) closed 2026-07-31
- SK.11.Published: ○ (6 tasks pending) — PB-01..PB-06, the **only** remaining phase in the domain

**Why:** WO-025/WO-026 built the domain; WO-041 (P-255) retrofitted `.Grpc`/`.Internal` onto the platform `[LoggerMessage]` logging standard; WO-042 (P-260) consolidated correlation/tenant header-name literals onto `01.Core`'s `WellKnownHeaders`; WO-052 (P-329) adopted `04.Contracts`' `Envelope`→`Envelopes` namespace rename (single-file blast radius: `HttpResponseMessageExtensions.cs`).

**How to apply:** When resuming, start from the first non-● phase in sub state-map at `11.Communication/state-map.md`. Every phase key is now ● except Published — the domain's entire remaining backlog is NuGet packaging metadata, pack, manifest verification, and publish to the internal feed (PB-01..PB-06). No design/implementation work is queued unless a new WO arrives from `communication-arch-planner`.

## Cross-cutting root Phase Backlog entries aren't auto-closed by `state-map-phase` (P-260/P-329 precedent)

Some root `## Phase Backlog` entries (e.g. `P-260`, `P-329`) span **multiple** sub-map phase keys at once — a WO-042/WO-052-style task adds one task to Design, one to Rest, one to Tests, one to Docs, all tagged `**[P-nnn]**` in their Task column. The sub-map's own `Phase Key Registry` "Root Backlog ID" column is `—` for these (it's only populated for the four single-package lifecycle mappings: Rest→P-154, Internal→P-155, Grpc→P-156, GraphQL→P-157), and "Maps to Root Phase" is a standard lifecycle name (`Design`/`Rest`/`Tests`/`Docs`), so `state-map-phase`'s own Step S8a Case 3 explicitly **skips** auto-closing the cross-cutting entry.

**How to apply:** When the *last* task of a cross-cutting P-nnn phase lands (recognizable because it's the last `**[P-nnn]**`-tagged row still `○` across every phase-key table in the sub-map), manually flip that root `### P-nnn` entry's `**Status:**` line from `◐ Dispatched` to `● Complete`, check off its acceptance criteria, and append one root changelog line — same mechanics as Root mode Steps R3-R7, just not triggered automatically by the sub-map promotion. This is exactly what happened for P-260 (closed 2026-07-15 after DO-07) and P-329 (closed 2026-07-31 after DO-08) — both by a prior/this session's manual step, not the mechanical S8a path. Don't assume "phase key promoted to ●" alone closes everything; check the Task-column `**[P-nnn]**` tags across *all* phase-key tables for the domain before treating the WO as done.

## Docs-phase task text can go stale mid-flight — verify against real source before trusting the phase spec

DO-08 (P-329) was authored while P-328 (`04.Contracts`) was still `◐ Dispatched`/blocked, so its literal instruction said "record the queued... adoption (blocked on 04.Contracts)". By the time DO-08 was actually picked up, D-25/R-21/T-30 had already shipped in prior sessions and the phase was fully done — the task text no longer matched reality. Always re-verify current state directly (grep the real `.cs` files, re-read the state-map's own Overall Progress/changelog) rather than trusting a task's literal wording, especially for the last task in a multi-session WO. Also caught in the same pass: a "12-test suite" figure for `ReadEnvelopeAsyncTests.cs` had propagated through multiple WO-052 changelog lines without ever being verified against the file — the real count is 10 `[Fact]`s (5 `JsonTypeInfo<T>` + 5 `JsonSerializerOptions?`). Grep-count claims like this directly before restating them.

## ProjectReference vs packed-.nupkg build proofs — say which one you mean

This repo wires cross-domain packages (`04.Contracts` ↔ `11.Communication`, etc.) via `ProjectReference`, not a consumed `.nupkg`. When a phase task's acceptance criteria says something like "confirm `dotnet build` is clean against the `SharedKernel.Contracts` version published by P-328," a clean build only proves the `ProjectReference`-level contract holds — it does NOT prove a packed-package consumption path (i.e., it says nothing about what happens once `04.Contracts` is actually `dotnet pack`ed and restored as a `.nupkg` by a downstream consumer). State explicitly which level was validated; don't let "clean build" imply more than it does.

## Docs-phase lesson (2026-07-14)

Every `11.Communication` production `.csproj` already sets `<GenerateDocumentationFile>true</GenerateDocumentationFile>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. This means CS1591 (missing XML doc on a public member) has been a **build-breaking error** since Scaffold — so by the time a Docs phase is reached, XML doc coverage on the public API surface is already guaranteed by every prior phase's own build passing. When picking up a Docs phase task, first run `dotnet build -c Release` on each affected `.csproj`: 0 warnings/0 errors is a fast, mechanical proof that the "add XML doc comments to all public types" tasks are already satisfied, and the real remaining work is usually confined to (a) CLAUDE.md prose updates (deviations, lessons, version pins) and (b) package-level `<Description>` wording — not new `.cs` edits. Don't assume a Docs phase requires touching source files; verify via build first.

**Specific lesson captured (DO-05):** `RestCommunicationBuilder.AddRestClient<TClient>` has always silently enforced Polly v8's `CircuitBreaker.SamplingDuration >= 2 × AttemptTimeout.Timeout` constraint (computes a floor and raises `SamplingDurationSec` to it when needed) — this was implemented since the original Rest phase but never called out in CLAUDE.md's REST client rules until this Docs pass. When auditing a Docs phase for "lessons from X configuration," grep the builder/implementation source for validation constraints or auto-adjustment logic that isn't mirrored in the domain brain — that's exactly the kind of undocumented lesson these tasks are meant to surface.

## NuGet version pins (confirmed working)

### SharedKernel.Communication.Rest
- `Microsoft.Extensions.Http` 10.0.0
- `Microsoft.Extensions.Http.Resilience` 9.8.0
- `Microsoft.AspNetCore.Http` 2.3.0 (for HttpContextAccessor concrete class)

### SharedKernel.Communication.Grpc
- `Grpc.Net.Client` 2.80.0
- `Grpc.Net.ClientFactory` 2.80.0
- `Google.Protobuf` 3.35.1
- `Google.Api.CommonProtos` 2.17.0
- `OpenTelemetry.Instrumentation.GrpcNetClient` 1.15.1-beta.1

### SharedKernel.Communication.GraphQL

- `HotChocolate.Data` 16.1.4
- `HotChocolate.AspNetCore` 16.1.4
- Note: architecture originally specified v14 but v14 is not available for net10.0; v16.1.4 used

### SharedKernel.Communication.Internal

- `Microsoft.Extensions.ServiceDiscovery` (pin TBD when SK.11.Internal I-07/I-08 complete)

## Handler pipeline order (REST)

CorrelationIdDelegatingHandler → TenantIdDelegatingHandler → StandardResilienceHandler → transport

Registration order in `AddRestClient<TClient>()` determines pipeline: add innermost (StandardResilienceHandler) last.

## gRPC interceptor singleton registration pattern

Both `CorrelationTracingInterceptor` and `TenantIdInterceptor` are registered as **singletons** via `TryAddSingleton`. Despite resolving request-scoped `ITenantProvider`, they are safe as singletons because they access the scope dynamically via `IHttpContextAccessor` at call time (not via constructor injection).

## Address resolution at gRPC channel creation

When `IServiceEndpointResolver` is registered and `Address` is omitted, the address is resolved via `.GetAwaiter().GetResult()` inside the `AddGrpcClient<T>((sp, o) => ...)` factory action. Safe because:
1. `KubernetesServiceEndpointResolver.ResolveAsync` never throws per its contract
2. Channel is a singleton — factory fires once, not per-call

## Money Protobuf conversion formula

`decimal → Money`: `Units = (long)Truncate(value)`, `Nanos = (int)Round((value - units) * 1_000_000_000, 0)`
`Money → decimal`: `Units + (decimal)Nanos / 1_000_000_000`

## PagedResponseType factory method summary

- `FromPage(IPage)` — offset paging source (HC v16 IPage, not `CollectionSegment<T>`)
- `FromConnection(Connection<T>)` — cursor paging source
- `From(IReadOnlyList<T>, int)` — manual assembly
- `FromPagedList(PagedList<T>)` — bridge from 04.Contracts application layer result (GQ-09/P-165)

`FromPagedList` requires the `SharedKernel.Contracts` (04.Contracts) project reference in the GraphQL csproj — already present in the original scaffold.
