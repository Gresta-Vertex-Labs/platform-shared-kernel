---
name: project_17_workflows_published_phase
description: 17.Workflows SK.17.Published phase status (2026-07-27) — domain complete end to end (81/81), consumer-verify harness added, genuine two-tier config-validation finding.
type: project
---

`SK.17.Published` (P-01–P-07) reached `●` on 2026-07-27 — 81/81 tasks complete. All six phase keys
(Design/Scaffold/Core/Tests/Docs/Published) are now `●`. **17.Workflows (WO-046) is done end to end.**
Root Phase Backlog P-287 closed. No production `.cs` files were touched this phase — only NuGet
metadata re-verification and a new standalone `consumer-verify` console project.

**What shipped:**
- P-01/P-02: re-verified the Docs-phase NuGet metadata block (`PackageReadmeFile` + packed
  `README.md` + full metadata) was already complete — unlike `08.Storage`, no gap existed here.
  `dotnet pack` → `.nupkg`+`.snupkg`, zero `NU5039`/`NU5128`.
- P-03: `17.Workflows/consumer-verify/consumer-verify.csproj` — `OutputType=Exe`, `IsPackable=false`,
  `TreatWarningsAsErrors=true`, one `PackageReference` (`Microsoft.Extensions.Hosting` 10.0.9), one
  `ProjectReference` (`SharedKernel.Workflows.Temporal.csproj`). `Temporalio`/`.Extensions.Hosting`/
  `.Testing` all flow transitively through the `ProjectReference` — confirmed this works exactly like
  `SharedKernel.Workflows.Temporal.Tests` already relies on (that project also has zero explicit
  `Temporalio` `PackageReference`). Registered in `Platform.SharedKernel.slnx` under `/17.Workflows/`.
- P-04–P-06: four surfaces in `Program.cs`, all passing against real compiled code through
  `Host.CreateApplicationBuilder()` → `IHost.StartAsync()` (never `BuildServiceProvider()`):
  1. `.AsClientOnly()` resolves `IWorkflowDispatcher`/`IWorkflowIdFactory`/`IWorkflowServiceProbe` with
     zero DI exceptions, no `IHostedService` registered, `ITemporalRawClientAccessor` unreachable
     without `.AllowRawClientAccess()`.
  2. Worker-hosting against a real `WorkflowEnvironment.StartTimeSkippingAsync()` (never a live
     cluster) — hosted worker service registered, `ITemporalClient` resolves singleton, and a full
     `StartAsync` → activity → `GetResultAsync` round trip actually completes (minimal
     `ConsumerVerifyEchoWorkflow`/`ConsumerVerifyEchoActivity` pair, no timers — see gotcha note below).
  3/4. Config validation — see the two-tier finding, the highest-value discovery this phase.

**GENUINE FINDING — `TemporalWorkflowsBuilder`'s config-validation is a two-tier mechanism, not the
single tier prior docs implied.** Confirmed via a scratch console repro *before* writing the harness,
then proven again inside it:
- **Tier 1 — a config KEY entirely absent** (e.g. no `Workflows:Temporal:TargetHost` at all).
  `ConfigureClient()` reads `_section[nameof(TemporalOptions.TargetHost)]` directly off the raw
  `IConfigurationSection` indexer (returns `null` for a missing key) and does
  `?? throw new InvalidOperationException(...)`. This fires **synchronously the instant consumer code
  calls `.Build()`** on the fluent builder — during service registration, i.e. **before the `IHost` is
  even constructed** (`HostApplicationBuilder.Build()` never runs), let alone before
  `IHost.StartAsync()`. Stronger/eagerer than any existing doc claimed.
- **Tier 2 — a config key PRESENT but invalid** (e.g. `TargetHost = ""` or whitespace). The Tier-1
  check only guards a *null* read, so a non-null empty/whitespace string passes it — `.Build()`
  succeeds. `TemporalOptions.TargetHost`'s `[Required]` + `AddValidatedOptions`'s `.ValidateOnStart()`
  (registered unconditionally in the builder's constructor) then throws
  `Microsoft.Extensions.Options.OptionsValidationException` at the real `IHost.StartAsync()`, naming
  `"TargetHost"` in `.Failures` — exactly what prior docs already claimed.
Recorded in `17.Workflows/CLAUDE.md`'s Technology Stack "Configuration" row and a new NOTE beside the
`TemporalOptions` Interface Contract block. **General lesson: when a phase spec asserts a single
exception-type/timing for "misconfiguration fails at startup," test the entirely-absent-key case
separately from the present-but-invalid-value case — they can (and did, here) take different paths
through the same builder.**

**Also reconfirmed:** resolving `ITemporalClient` via DI never eagerly connects — even against a
syntactically-valid-but-unreachable target host (`"localhost:59999"`), both `GetRequiredService`
and `IHost.StartAsync()` succeed; the gRPC connection attempt is fully deferred to first real RPC.
Previously only shown via `BuildServiceProvider()` in `DiRegistrationTests`; now also proven through a
real `IHost`.

**Also reconfirmed:** a workflow round trip with no timer involved does *not* hit the Tests-phase
"genuine time-skipping needs the exact `WorkflowEnvironment.Client` instance" gotcha (see
[[reference_temporalio_1170_sdk_shapes]]) — a separately-built client pointed at
`environment.Client.Connection.Options.TargetHost`/`.Client.Options.Namespace` dispatches and
completes a real (non-timer) workflow just fine. That gotcha is specific to tests needing a *natural
timer* to elapse under time-skipping, not to ordinary dispatch.

**Verification:** 158/158 tests still passing (unchanged — no production code touched).
`dotnet pack` clean. Consumer-verify harness runs green end to end (`dotnet run` against
`17.Workflows/consumer-verify/consumer-verify.csproj`).

Root `state-map.md`: Domain Summary Board row 17 → `Published`/`●`; Overall Progress `● Published`
11→12, `● Docs` 3→2; Phase Backlog `P-287` → `●` Complete. Root `CLAUDE.md` was evaluated and
deliberately **not** changed — nothing here is a new cross-domain rule, layering exception, or
package-naming decision; P-287's closure and the WO-046 folder-map/layering entries already fully
cover this domain at the root level.

See also [[project_17_workflows_core_implementation]], [[project_17_workflows_tests_phase]],
[[project_17_workflows_docs_phase]], and [[reference_temporalio_1170_sdk_shapes]] for earlier phases.
