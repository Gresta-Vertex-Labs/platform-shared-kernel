# Memory Index

- [Phase sequencing](phase_sequencing.md) — Scaffold before Core; foundation before dependency-specific checks; C-19 gated on 07.Messaging P-172
- [Health check tag calibration](health_check_tag_calibration.md) — canonical tag/HealthStatus table for every check in this domain; Degraded vs Unhealthy rule of thumb
- [WO-028 audit corrections](wo028_audit_corrections.md) — don't document defects as "accepted limitations"; check async signatures actually await; cross-check CLAUDE.md contracts have state-map task rows
- [Ambient logging enrichment](ambient_logging_enrichment.md) — WO-041 Activity.Baggage→LogRecord mechanism; generic (no hardcoded keys); cross-domain test-without-reference pattern; Guid.Empty sentinel set explicitly; WO-042 confirmed baggage-key mismatch fix
- [Verify dependency claims](feedback_verify_dependency_claims.md) — always check the target domain's own state-map before marking a cross-domain dependency Available; a phase input's "already implemented" claim can be stale
- [Provider-agnostic adapter pattern](provider_agnostic_adapter_pattern.md) — for multi-provider domains (08.Storage's .S3/.Obs), take config as an explicit param, never read a concrete provider's options type
