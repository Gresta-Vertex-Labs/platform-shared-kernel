# Memory Index

- [Phase sequencing](phase_sequencing.md) — Scaffold before Core; foundation before dependency-specific checks; C-19 gated on 07.Messaging P-172
- [Health check tag calibration](health_check_tag_calibration.md) — current tag/HealthStatus table (IReadinessProbe + AddSharedKernelReadiness, WO-086); Degraded vs Unhealthy rule of thumb
- [WO-028 audit corrections](wo028_audit_corrections.md) — don't document defects as "accepted limitations"; check async signatures actually await; cross-check CLAUDE.md contracts have state-map task rows
- [Ambient logging enrichment](ambient_logging_enrichment.md) — WO-041 Activity.Baggage→LogRecord mechanism; generic (no hardcoded keys); cross-domain test-without-reference pattern; WO-042 baggage-key mismatch fix (pre-WO-086 names)
- [Verify dependency claims](feedback_verify_dependency_claims.md) — always check the target domain's own state-map before marking a cross-domain dependency Available; a phase input's "already implemented" claim can be stale
- [Provider-agnostic adapter pattern](provider_agnostic_adapter_pattern.md) — for multi-provider domains (08.Storage's .S3/.Obs), take config as an explicit param, never read a concrete provider's options type
- [Upstream contract-definition gap](upstream_contract_definition_gap.md) — 10.Intelligence's own brain self-contradicts on ICompletionProviderDescriptor.ProbeAsync; how to lock only the verified part and flag the rest
- [Messaging health check probe pattern](messaging_health_check_probe_pattern.md) — WO-054/P-351 (probe since replaced by IReadinessProbe): never build a second connection; how to plan a breaking-change removal while blocked on a cross-domain dependency
- [WO-075/068/073/078 batch](wo075_068_073_078_batch.md) — tenant catalog (zero-gate), KeyVault probe design-gap (not a retraction), "no .csproj on disk" blocker tier, WithSchedulingTelemetry wires both signals, (deleted) 13→19 grant, P-483's P-482 dependency was a false blocker, first intra-domain ServiceDefaults→MultiTenancy reference
