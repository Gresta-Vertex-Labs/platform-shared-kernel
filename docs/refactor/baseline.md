# WO-086 baseline (P-562)

Recorded 2026-09-25 on `refactor/wo-086-foundation`, branched from `main` at `3a296bb5`. Every later WO-086 step is compared against these numbers.

## Build

`dotnet build Platform.SharedKernel.slnx -c Release`

| Measure | Value |
|---|---|
| Errors | 0 |
| Warnings | 34 (31 unique) |
| Duration | 2m 40s |

Warnings by code (all pre-existing, all in test projects or known NU1701 compat notices):

| Code | Count |
|---|---|
| CS8509 | 18 |
| NU1701 | 12 |
| RS0026 | 8 |
| CS8764 | 6 |
| CS8765 | 6 |
| xUnit2031 | 4 |
| CS8524 | 4 |
| xUnit2013 | 2 |
| ASPDEPR008 | 2 |
| CS0184 | 2 |

**Rule:** no WO-086 step may add a warning code. The count may only go down.

## Unit tests

`dotnet test Platform.SharedKernel.Unit.slnf -c Release`

| Measure | Value |
|---|---|
| Test assemblies | 66 |
| Tests | 7,557 |
| Passed | 7,557 |
| Failed | 0 |
| Skipped | 0 |

Test counts will change as types move between packages. What must hold is **0 failed**, and every behavior test that existed here still exists somewhere afterwards. Moved tests are recorded in each step's state-map entry.

## Integration tests

`dotnet test Platform.SharedKernel.Integration.slnf -c Release --settings eng/testsettings/integration.runsettings` (Docker Desktop 28.4.0)

| Measure | Value |
|---|---|
| Test assemblies | 22 |
| Tests | 2,913 |
| Passed | 2,913 |
| Failed | 0 |
| Skipped | 0 |

## Packages

86 packable projects (see the P-563 tier assignment). The tier checks cover these plus `SharedKernel.Testing` (not packable).

## Tier violations present at the start

These are the only edges that break the target tier matrix today. Every other current edge is legal under the tiers, including the four numbered-layer "grants". So Step 1's baseline holds exactly these:

| From | To | Rule |
|---|---|---|
| `SharedKernel.Application` (Abstractions) | `MediatR` package | SKTIER003: third-party package in the Abstractions tier |
| `SharedKernel.Idempotency.EfCore` (Adapter) | `SharedKernel.Application.Behaviors` (Host) | SKTIER001 |
| `SharedKernel.Idempotency.Redis` (Adapter) | `SharedKernel.Application.Behaviors` (Host) | SKTIER001 |

The other structural problems WO-086 fixes (contracts filed in the wrong package, MediatR in public contracts, duplicated tenant/probe/idempotency concepts, forced optional dependencies) are not tier violations. Each is tracked by its own step, not by this baseline.
