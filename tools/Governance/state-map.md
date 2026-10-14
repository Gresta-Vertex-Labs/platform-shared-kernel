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

- **01.Core, 13.ServiceDefaults (inbound):** the `## Logging` ownership tables are parsed by the EventId assertion; keep one machine-readable shape (family base at 1-49, Adapters at 50-99) and tell this domain if it changes.
- **11.Communication, 14.Presentation, 12.Security (inbound note):** protocol-mirror prefixes (`grpc`, `http`) and any protocol value reaching an `Error` constructor are exempted by caller-supplied entries that cite the domain `CLAUDE.md`; keep those entries written down there.
