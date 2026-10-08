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

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. `SharedKernel.Analyzers`, `.ArchitectureTests` and `.Linter` ship with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.
