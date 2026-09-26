---
name: project-communication
description: 11.Communication domain status, NuGet version pins, and key cross-phase implementation patterns discovered during SK.11 implementation
metadata:
  type: project
---

> WO-086 (2026-09): `CorrelationIdDelegatingHandler` + `TenantIdDelegatingHandler` are now `RequestContextDelegatingHandler`; propagation reads `IRequestContextAccessor` (no `IHttpContextAccessor`, no ASP.NET Core package, no `ITenantProvider`); the three packages are Adapter tier with declared edges Rest/Grpc → Communication.Internal; `SharedKernel.Communication.GraphQL` moved to `14.Presentation` as `SharedKernel.Presentation.GraphQL`. Status sections below are history.

## Phase completion status (as of 2026-08-12, WO-056 Tests phase closed)

**UPDATE 2026-08-12:** `SK.11.Tests` (T-31–T-38) fully shipped and closed — all 8 deferred
regression/DI-resolution tests from the WO-056 review landed in one session. Full regression:
`.Rest.Tests` 77/77 (was 66), `.Grpc.Tests` 64/64 (was 60), `.GraphQL.Tests` 45/45 (was 43),
`.Internal.Tests` 57/57 (was 52) — 243/243, zero regressions. Root Domain Summary Board row for
Communication stayed at Docs/`●` per the established "never regress the row" precedent (Tests is
earlier in the pipeline than the already-reached Docs/Published milestones). Only `SK.11.Docs`
(DO-09–DO-15) and `SK.11.Published` (PB-07/PB-08) remain to close WO-056 in full — see
[[feedback_grpc_deadline_test_technique]] and the TTL-cache-reflection-seeding note below for the
new test techniques this pass established.

## Phase completion status (as of 2026-08-11, mid-WO-056) — historical, superseded by the update above

WO-056 (P-356–P-364, dispatched 2026-08-11) added 34 new tasks across all nine phase keys on top of
the previously-all-● baseline below. As of this entry:

- SK.11.Design: ● (32 tasks — D-26–D-32, WO-056's 7 gap-fill design contracts, all landed in the same
  arch-lead session with zero CLAUDE.md edits needed since it was authored design-locked already)
- SK.11.Scaffold: ● (14 tasks — S-14/P-362 `consumer-verify` harness scaffold, `communication-phase-implementer`, 2026-08-11)
- SK.11.Rest: ● (26/26, closed 2026-08-11 — R-22–R-26 shipped: SK0011 GUID-fallback fix, `RestClientOptionsValidator`
  validate-at-point-of-consumption + `RestResilienceOptions` range validation, `EnsureSuccessOrErrorAsync<T>` retired
  for a non-generic `Task<Result>` helper, `IdempotencyKeyDelegatingHandler`. See "Handler pipeline order (REST)"
  below — implementing R-26 surfaced and fixed a real pre-existing ordering defect, not just an addition.)
- SK.11.Grpc: ● (20/20, closed 2026-08-11 — G-18–G-20 shipped: SK0011 GUID-fallback fix, real per-call
  `DeadlineSeconds` enforcement via `GrpcClientFactoryOptions.CallOptionsActions` (see the
  "Grpc.Net.ClientFactory real deadline mechanism" entry below — NOT `ConfigureDefaultCallOptions`,
  which doesn't exist in 2.80.0), `GrpcClientOptionsValidator`. T-31/T-32/T-35 deferred to Tests phase.)
- SK.11.GraphQL: ◐ (9/10 — GQ-10 pending: `GraphQLOptionsValidator` validate-at-point-of-consumption)
- SK.11.Internal: ◐ (11/13 — I-12/I-13 pending: `IClock` injection into `KubernetesServiceEndpointResolver`, symmetric `AddK8sServiceDiscovery` registration guard)
- SK.11.Tests: ◐ (30/38 — T-31–T-38 pending, one per above)
- SK.11.Docs: ◐ (8/15 — DO-09–DO-15 pending)
- SK.11.Published: ◐ (4/8 — PB-01–PB-04 already ●; PB-05/PB-06 pack/publish gated on the correctness
  fixes above; PB-07 is the actual `consumer-verify` verification content — DI resolution + the two
  P-358 `OptionsValidationException` regression proofs — gated on P-358/R-23/GQ-10; PB-08 pending)

**Why:** WO-025/WO-026 built the domain; WO-041 (P-255) retrofitted `.Grpc`/`.Internal` onto the
platform `[LoggerMessage]` logging standard; WO-042 (P-260) consolidated correlation/tenant
header-name literals onto `01.Core`'s `WellKnownHeaders`; WO-052 (P-329) adopted `04.Contracts`'
`Envelope`→`Envelopes` namespace rename; WO-056 (P-356–P-365) is a fresh gold-standard review fixing
a duplicated SK0011 GUID-format defect, two structurally-dead options validators, a dead
`GrpcClientOptions.DeadlineSeconds` knob, an asymmetric service-discovery registration guard, a
misleading `EnsureSuccessOrErrorAsync<T>`, a missing `consumer-verify` harness, this domain's
never-been-published status, and a gap in the outbound idempotency story — plus one sibling phase in
`13.ServiceDefaults` (`WithCommunicationTelemetry`, P-365, not this domain's jurisdiction).

**How to apply:** When resuming, start from the first non-● phase in sub state-map at
`11.Communication/state-map.md`. R-22–R-26 (Rest, closed 2026-08-11) had no cross-phase dependency;
GQ-10/I-12/I-13/G-18–G-20 are similarly independent of each other and of Rest. PB-05/PB-06/PB-07
are the only phases with real dependencies (gated on the correctness fixes landing first) — do not
attempt pack/publish or the consumer-verify content before then. T-31–T-38 (regression/format/DI-
resolution test coverage for R-22–R-26/G-18–G-20/GQ-10/I-12/I-13) were deliberately deferred to a
separate future Tests-phase session by explicit phase-input scoping when Rest landed — do not assume
"phase implemented" also means "T-NN for that phase is done"; check the Tests phase table directly.

## Root Domain Summary Board row is never regressed to an earlier-in-pipeline phase key (WO-056 precedent, 2026-08-11)

This domain tracks nine concurrent phase keys (Design/Scaffold/Rest/Grpc/GraphQL/Internal/Tests/Docs/
Published), not one linear pipeline — a later WO (e.g. WO-056) can add new tasks to an *earlier*
phase key (Scaffold, Design) after the domain's root Domain Summary Board row already advanced past
it to a later one (Docs). When that earlier phase key's promotion condition fires (e.g. `SK.11.Scaffold`
reaching 14/14 ●), do **not** let Root mode Step R3 overwrite the Domain Summary Board's "Current
Phase"/State columns with "Scaffold"/●  — that would visually regress a domain that has already
reached Docs/Published. Instead: append a changelog line recording the promotion (`Communication →
Scaffold (●) — promoted from SK.11.Scaffold (...)`), but leave the Domain Summary Board row's Current
Phase/State/Summary columns exactly as they already stood (most-advanced-phase-reached), and leave
Overall Progress counts (the phase-name histogram at the bottom of root `state-map.md`) unchanged too.
This is the exact same call the previous session made for the `SK.11.Design` promotion earlier in the
same WO-056 run — its changelog entry explicitly says "Domain remains at Docs/Published overall." Only
`state-map-phase`'s sub-map-side updates (task row, phase-key Overall Progress, sub-map changelog) are
mechanical; the root Domain-Summary-Board-row decision requires this judgment call for any domain with
more than one active WO/phase-key track. Also apply Step S8a's Case-3 skip correctly here: Scaffold's
`Root Backlog ID` is `—` and its `Maps to Root Phase` is a standard lifecycle name, so no individual
`### P-NNN` Phase Backlog entry gets auto-closed by this promotion — P-362 stays `◐ Dispatched` since
its acceptance criteria (DI-entry-point resolution + the two P-358 regression proofs) are PB-07's job,
explicitly deferred, not S-14's.

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
- `Microsoft.Extensions.Http` 10.0.9 (confirmed against `.csproj` 2026-08-11 — prior "10.0.0" note was stale)
- `Microsoft.Extensions.Http.Resilience` 10.7.0 (confirmed against `.csproj` 2026-08-11 — prior "9.8.0" note was stale)
- (`Microsoft.AspNetCore.Http` was removed in WO-086 — no ASP.NET Core below the Host tier)

### SharedKernel.Communication.Grpc
- `Grpc.Net.Client` 2.80.0
- `Grpc.Net.ClientFactory` 2.80.0
- `Google.Protobuf` 3.35.1
- `Google.Api.CommonProtos` 2.17.0
- `OpenTelemetry.Instrumentation.GrpcNetClient` 1.15.1-beta.1

### SharedKernel.Communication.Internal

- `Microsoft.Extensions.ServiceDiscovery` (pin TBD when SK.11.Internal I-07/I-08 complete)

## Handler pipeline order (REST) — empirically verified 2026-08-11 (R-26/WO-056)

Target/correct order (outer → inner): `RequestContextDelegatingHandler` →
`IdempotencyKeyDelegatingHandler` (conditional) → `StandardResilienceHandler` → transport. (Until WO-086 the
first slot was two handlers, correlation-id then tenant-id; the history below uses those names.)

**The rule:** on `IHttpClientBuilder`, registration *call order* is outer-to-inner — the first
`AddHttpMessageHandler<T>()`/`AddStandardResilienceHandler()` call registered becomes the OUTERMOST
handler (its `SendAsync` runs first on the way out, last on the way back), and each subsequent call
nests one layer further in, toward the primary/transport handler. Confirmed empirically via a
throwaway console harness against the real `Microsoft.Extensions.Http`/`Microsoft.Extensions.Http.Resilience`
packages (not assumed from BCL source memory) — see the trace-marker technique below, reusable for
any future ordering question in this domain.

**Why order matters here specifically:** `StandardResilienceHandler`'s retries re-send the *same*
`HttpRequestMessage` instance repeatedly. If it is registered OUTERMOST (before the header-injecting
handlers), those handlers sit INSIDE the retry loop and get re-entered on every attempt — still
functionally harmless here only because Correlation/Tenant/Idempotency all check
"header already present" before writing (so the value stays stable regardless), but it contradicts
the documented pipeline shape and wastes a few no-op invocations per retry. Registering the header
handlers BEFORE `AddStandardResilienceHandler` makes Resilience innermost, so they run exactly once
per logical call, matching the documented shape and the "before Polly's first attempt" framing used
in `RestClientOptions.EnableIdempotencyKeyPropagation`'s XML doc.

**Real defect found and fixed under R-26:** the shipped `RestCommunicationBuilder.AddRestClient<TClient>`
had `builder.AddStandardResilienceHandler(...)` called BEFORE
`builder.AddHttpMessageHandler<CorrelationIdDelegatingHandler>()`/`<TenantIdDelegatingHandler>()` —
the wrong order per the rule above — despite an existing code comment claiming the opposite
("Fixed pipeline order: CorrelationId → TenantId → StandardResilienceHandler → transport"). This had
shipped undetected since the original Rest phase because no test exercises retry+header interaction
together (`ResiliencePipelineTests` bypasses `AddRestClient` entirely; DI smoke tests never assert
handler order). Implementing R-26's literal instruction ("position the idempotency handler after
TenantIdDelegatingHandler, before StandardResilienceHandler") was impossible to honor without also
reordering the pre-existing two calls — fixed as a direct corollary of R-26, not a separately
numbered task. No existing test broke (order doesn't affect any currently-asserted behavior).

**Reusable verification technique:** a scratch console app (`dotnet new console`, package refs
`Microsoft.Extensions.Http` + `Microsoft.Extensions.Http.Resilience`) registering marker
`DelegatingHandler`s that append `"Enter-{name}"`/`"Exit-{name}"` to a shared `List<string>`, plus a
flaky primary handler that fails N times before succeeding, run through `IHttpClientFactory` —
the printed trace directly shows enter/exit nesting and whether a handler is re-entered per retry.
Faster and more trustworthy than reasoning about `HttpMessageHandlerBuilder.CreateHandlerPipeline`
from memory, and this domain has since drifted from its own documented convention once already.

## Grpc.Net.ClientFactory real deadline mechanism — CallOptionsActions, not ConfigureDefaultCallOptions (2026-08-11, G-19/P-359)

`Grpc.Net.ClientFactory` 2.80.0 has **no** `IHttpClientBuilder.ConfigureDefaultCallOptions(...)` method —
that name was an unverified assumption baked into the original phase-spec/design text (D-29). Verified
empirically by reflecting over the installed NuGet DLL (`~/.nuget/packages/grpc.net.clientfactory/2.80.0/
lib/net10.0/Grpc.Net.ClientFactory.dll`, loaded via a scratch console app referencing the real
`Grpc.Net.Client`/`Grpc.Net.ClientFactory` PackageReferences so transitive deps resolve — a bare
`Assembly.LoadFrom` without the PackageReferences throws `FileNotFoundException` on `Grpc.Core.Api`).

**The real mechanism:** `GrpcClientFactoryOptions.CallOptionsActions` — an `IList<Action<CallOptionsContext>>`
populated inside the same `Action<IServiceProvider, GrpcClientFactoryOptions>` configure delegate already
passed to `services.AddGrpcClient<TClient>((sp, o) => ...)`. `CallOptionsContext` exposes both a mutable
`CallOptions` property (starts as whatever the caller/generated-client-method supplied) and a `ServiceProvider`
property — but its constructor is `internal`, so it cannot be `new`'d from outside the `Grpc.Net.ClientFactory`
assembly (relevant if a future Tests-phase session wants to unit-test the per-call action directly; would
need `BindingFlags.NonPublic` reflection to construct one, or an integration-style DI/host test instead).
`Grpc.Core.CallOptions.Deadline` is `DateTime?` (not `DateTimeOffset?`); `WithDeadline(DateTime)` returns a
new `CallOptions` (immutable-builder pattern, same as `WithHeaders`).

**Design choice made here (not explicit in the original P-359 text):** only set the deadline when
`context.CallOptions.Deadline is null` — never overwrite a per-call deadline the caller already supplied
via the generated client's own `CallOptions` overload. Mirrors this domain's existing "caller-supplied
value always wins" convention (x-correlation-id/x-tenant-id header injection). No task/design doc
explicitly required this; treated it as the obviously-correct extension of an established domain pattern
and documented the reasoning inline (XML doc + CLAUDE.md) rather than silently deciding it.

**IClock sourcing:** resolved once via `sp.GetRequiredService<IClock>()` inside the same registration-time
closure the address-resolution logic already uses `sp` for (both the `hasAddress` and resolver branches —
switched `hasAddress`'s configure delegate from the single-arg `Action<GrpcClientFactoryOptions>` overload
to the two-arg `(sp, o)` overload solely to reach `sp`). The *instant* is still computed fresh at call time
since `clock.UtcNow` is read inside the per-call `CallOptionsActions` lambda, not captured ahead of time —
only the `IClock` *instance* is resolved once. This satisfies both "no direct `DateTime.UtcNow`" (SK0001)
and "deadline reflects actual call time," and avoids the internal-constructor `CallOptionsContext` testing
problem entirely (no test needs to construct one — DI-registration-level assertions on
`GrpcClientFactoryOptions.CallOptionsActions.Count` suffice for structural proof; a live deadline-exceeded
behavioral test, T-35, needs a real/fake gRPC server, deferred).

**Reusable technique:** reflecting over an installed NuGet package's real DLL beats trusting a phase-spec's
assumed API name — same lesson as the R-26 `Microsoft.Extensions.Http` handler-ordering scratch-harness
entry below. When a design doc says "the verified X mechanism" or "empirically verify the shape" for a
third-party API, always actually load the real assembly (via a scratch project referencing the same
PackageReference version pinned in the production `.csproj`) and reflect over its public surface before
writing any wiring code — do not pattern-match a plausible-sounding method name from memory.

## gRPC interceptor singleton registration pattern

Both `CorrelationTracingInterceptor` and `TenantIdInterceptor` are registered as **singletons** via `TryAddSingleton`, together with `TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>()`. They are safe as singletons because `IRequestContextAccessor.Current` is AsyncLocal and read at call time, never captured at construction.

## Address resolution at gRPC channel creation

When `IServiceEndpointResolver` is registered and `Address` is omitted, the address is resolved via `.GetAwaiter().GetResult()` inside the `AddGrpcClient<T>((sp, o) => ...)` factory action. Safe because:
1. `KubernetesServiceEndpointResolver.ResolveAsync` never throws per its contract
2. Channel is a singleton — factory fires once, not per-call

## Money Protobuf conversion formula

`decimal → Money`: `Units = (long)Truncate(value)`, `Nanos = (int)Round((value - units) * 1_000_000_000, 0)`
`Money → decimal`: `Units + (decimal)Nanos / 1_000_000_000`
