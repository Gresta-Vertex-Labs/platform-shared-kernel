# 00.Governance — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Analyzers` | Tooling | ● | Roslyn rules (`SK0001`…): `[LoggerMessage]`-only logging, `Result` never discarded, magic strings, raw SDK clients, workflow determinism, pipeline-marker response shape, parameterized Dapper SQL, … Targets `netstandard2.0`. |
| `SharedKernel.ArchitectureTests` | Tooling | ● | NetArchTest purity rules the tiers cannot express, `DependencyGraphRulesTests` over every csproj, `RuleExecutionCoverageTests` (every public rule needs a test). |
| `SharedKernel.Linter` | Tooling | ● | Content-only package: EditorConfig + CSharpier enforcement. |
| `SharedKernel.Benchmarks` | — (not packable) | ● | Dev-only BenchmarkDotNet harness; never published. |

The tier check itself is MSBuild (`eng/SharedKernelTiers.targets`, `SKTIER000`–`SKTIER006`) — see `eng/README.md`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **devops-lead (outbound):** one import of `tools/Governance/SelfAnalysis/SharedKernel.SelfAnalysis.props` into the root build files for projects under `src/` (this domain edits only `tools/Governance/`); confirm the `WarningsNotAsErrors` interplay with each project's `TreatWarningsAsErrors`; optionally a script that sums the per-project SARIF files per domain for CI logs (the test report covers local runs).
- **All domains (outbound, notes only):** each Phase 1/2 reads its counts from the report and drives them to zero; suspended "warning-free" expectation for SK ids and CA2200 until P-618.
- **P-618 / SK.00.ObservabilityEnforce (this domain, later):** consumes the switch, removes the report plumbing and audits suppressions; root `CLAUDE.md` is reconciled via `/sync-brain` afterwards. Entry gate: every Phase 2 conformance phase done and the platform report at zero.
- **devops-lead (outbound, P-618):** after the flip the root build import composes with each project's `TreatWarningsAsErrors` without `WarningsNotAsErrors`; drop any CI step reading `SHAREDKERNEL_OBSERVABILITY_MODE` or the per-project SARIF files.
- **01.Core, 13.ServiceDefaults (inbound, P-583 D-07):** the `## Logging` ownership tables are parsed by the EventId assertion; keep one machine-readable shape (family base at 1-49, Adapters at 50-99) and tell this domain if it changes.
- **16.Testing (outbound note):** the Temporal SDK `CustomMetricMeter` instruments on `SharedKernel.Workflows` need an allow-list in `ShouldFollowMetricConvention`; this domain's IL check does not see them.
- **11.Communication, 14.Presentation, 12.Security (inbound note, P-583 D-09):** protocol-mirror prefixes (`grpc`, `http`) and any protocol value reaching an `Error` constructor are exempted by caller-supplied entries that cite the domain `CLAUDE.md`; keep those entries written down there.
- **14.Presentation (inbound, P-618 T-05):** GraphQL → WebApi/Core references; this domain re-runs the graph and layering tests.
