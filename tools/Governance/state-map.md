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
| `SK.00.ObservabilityEnforce` | Phase 3 flip: every observability rule fails the build or test, report plumbing removed, suppressions audited (WO-087 / P-618) | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.00.ObservabilityEnforce — WO-087 Phase 3: the observability rules fail `○`

**Work order:** WO-087 / P-618 · **Depends on:** SK.00.ObservabilityReportOnly, and every Phase 2 conformance phase (SK.01.CoreConformance, SK.02.ObservabilityConformance, SK.03.DomainErrorCodes, SK.05.ApplicationConformance, SK.06.PersistenceConformance, SK.07.ObservabilityConformance, SK.08.StorageConformance, SK.09.ObservabilityConformance, SK.10.IntelligenceConformance, SK.11.CommunicationConformance, SK.12.SecurityConformance, SK.13.ServiceDefaultsConformance, SK.14.PresentationConformance, SK.15.IntegrationConformance, SK.17.WorkflowConformance, SK.18.IdempotencyConformance, SK.19.SchedulingConformance, SK.20.ReportingConformance)

The flip. Verdict: accept; reshaped in two places. First, the flip changes only the **kernel self-analysis** severity and the platform assertions; analyzers keep their default Warning for consumers (Decisions). Second, "remove the report switch" means deleting the plumbing, not only the property: after this phase there is one behaviour. Entry conditions are on disk, not assumed: the first task re-reads the platform report and refuses to flip while any domain still shows a finding.

The seven items the Phase 2 planners raised: items 1-4 and 7 are designed into P-583 (addenda on D-03, D-07, D-08, D-09, C-01) because the rules must accommodate them before they can become errors; item 5 (warnings in every `src/` build) is the report-mode cost already recorded in D-01 and ends with this flip; item 6 (GraphQL → WebApi/Core) is a re-run here (T-05).

**Where each constraint is enforced after the flip:** kernel analyzers and CA2200 by `kernel-enforce.globalconfig` through the self-analysis props (build error); EventId ownership, telemetry, log template and error-code format by assertions that throw (test failure); no id added, no tier edge changed.

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Entry gate and flip procedure. Run the platform report (P-583 T-08) and record that every domain shows zero findings and zero `unscanned` before the edit; the flip is a no-op for the code and fails for any regression. Define the end state: no `ObservabilityMode.props`, no `kernel-report.globalconfig`, no `WarningsNotAsErrors`, no SARIF `ErrorLog`, no `AssemblyMetadata` stamp, no `SHAREDKERNEL_OBSERVABILITY_MODE` override, no mode parameter on any assertion; `kernel-enforce.globalconfig` is renamed `kernel.globalconfig` and `SharedKernel.SelfAnalysis.props` imports it unconditionally. The finding model and the per-domain grouping survive inside the aggregate exception message (one throw per assertion, as `LoggingEventIdIntegrityAssertion`) | `SharedKernel.Analyzers` | `○` |
| D-02 | Scope and severity decisions to ratify. (a) SK0034 stays `suggestion` (advisory, Decisions); every other SK id and CA2200 is `error` for the kernel. (b) Default severity of every SK id for consumers stays Warning; SK0044 stays off by default. (c) `unscanned` (missing DLL) fails; `unverifiable` (telemetry unit from a field) stays listed, never a failure. (d) Testing-tier packages: P-583 D-01 started them included; decide from the final report whether they stay, and record the decision. (e) The self-analysis still excludes Tooling, test projects, `samples/` and `_verification/` | `SharedKernel.Analyzers` | `○` |
| D-03 | Suppression audit design. In `src/`, any `NoWarn`/`WarningsNotAsErrors` naming an SK id or `CA2200` (csproj, props, `.editorconfig`, globalconfig), any `#pragma warning disable` of them and any `SuppressMessage` for them must be justified: the owning domain's `CLAUDE.md` names the file and the reason (`## Known Limitations`). The owning domain is the longest folder prefix in the root domain table (P-583 C-05 registry). An unjustified suppression fails; the expected count at the flip is zero | `SharedKernel.ArchitectureTests` | `○` |
| C-01 | Self-analysis files: delete the report plumbing per D-01, keep one globalconfig and one props import, SK0034 at `suggestion`. Ratify with devops-lead that the root build import and each project's `TreatWarningsAsErrors` still compose (a project without it now fails on the SK errors regardless) | `SharedKernel.Analyzers` | `○` |
| C-02 | Assertions: remove the mode parameter from `PackageEventIdBlockAssertion`, `TelemetryNamingAssertion`, `LogTemplateAssertion`, `ErrorCodeFormatAssertion`; `ObservabilityEnforcement` reduces to "throw one aggregated `InvalidOperationException` when the report is non-empty". Remove `Configured`, the metadata stamp and the `ArchitectureTests.Tests` import of the mode props | `SharedKernel.ArchitectureTests` | `○` |
| C-03 | Suppression audit assertion per D-03 | `SharedKernel.ArchitectureTests` | `○` |
| T-01 | Firing proof for every rule, one deliberate violation each, all failing: each analyzer (SK0043, SK0044, SK0022 fifth shape, and the existing SK ids the kernel now enforces) compiled with the real `kernel.globalconfig` as the analyzer-config source and reported as `Error`; each assertion throws on a violating fixture (collision-proof assembly names, rule 15) and not on a conforming one | `SharedKernel.Analyzers.Tests`, `SharedKernel.ArchitectureTests.Tests` | `○` |
| T-02 | Config completeness: every `SharedKernel.Analyzers` descriptor id appears in `kernel.globalconfig`, `error` except SK0034 (`suggestion`), SK0044 enabled, `CA2200` `error`. Replaces P-583 T-07's `WarningsNotAsErrors` comparison, so a new rule still cannot be forgotten | `SharedKernel.ArchitectureTests.Tests` | `○` |
| T-03 | Platform-wide tests over every packable assembly now assert **no findings** (EventId ownership including the family tables of 01 and 13, telemetry, template, error code) and no `unscanned`; remove the report-only variants. Every public assertion stays called by a test (`RuleExecutionCoverageTests`) | `SharedKernel.ArchitectureTests.Tests` | `○` |
| T-04 | Suppression audit: fires on a `NoWarn` of SK0043, on a `#pragma` of CA2200, on a `SuppressMessage`; passes when the owning domain `CLAUDE.md` names the file; passes on the real tree (zero suppressions) | `SharedKernel.ArchitectureTests.Tests` | `○` |
| T-05 | Re-run the graph and layering tests after 14.Presentation's GraphQL → WebApi/Core references: `DependencyGraphRulesTests`, `OptionalDependencySatelliteRulesTests` (`Presentation.Core` depends on no sibling stays true; GraphQL is Host → Host, legal in the matrix) and `PresentationLayeringRules`. If an expectation must change, it follows the csproj, never the reverse (rule 1) | `SharedKernel.ArchitectureTests.Tests` | `○` |
| T-06 | `_verification/AnalyzerConsumer`: still reports `SK0001` and the consumer-default severities are unchanged (Warning), proving the flip did not leak into packages; a packed `.nuspec` of one self-analysed package still shows no analyzer dependency | `_verification` | `○` |
| T-07 | Full build of `src/` in Release with no SK or CA2200 diagnostic, and the full Unit lane green | `SharedKernel.ArchitectureTests.Tests` | `○` |
| DO-01 | `tools/Governance/README.md`: replace the "Observability report mode" section with the enforced rules and how to read a failure; rule tables show the enforced set; `SharedKernel.Analyzers` and `SharedKernel.ArchitectureTests` READMEs drop the override variable and report-table text | `SharedKernel.Analyzers` | `○` |
| DO-02 | Domain `CLAUDE.md`: rule 17 rewritten as the enforced end state, planned markers dropped when shipped; the "Adding an analyzer rule" list gains the `kernel.globalconfig` row | `SharedKernel.Analyzers` | `○` |

**Acceptance:**

- [ ] The full Unit lane is green and `src/` builds with no SK or CA2200 diagnostic; every rule fails on a deliberate violation (firing tests).
- [ ] No report mode, override variable, `WarningsNotAsErrors` list or mode parameter remains; the severity of every rule is fixed in `kernel.globalconfig`, SK0034 at `suggestion`.
- [ ] Zero unjustified suppressions in `src/`; any remaining one is named in the owning domain's `CLAUDE.md`.
- [ ] Consumers' default severities and the packed analyzer are unchanged.
- [ ] Root `CLAUDE.md` "Observability, exceptions and error codes" is un-marked as planned through `/sync-brain` after this phase ships (root file, not edited here).

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
