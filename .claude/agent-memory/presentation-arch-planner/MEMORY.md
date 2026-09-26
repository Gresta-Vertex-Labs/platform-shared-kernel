# Memory Index

- [WO-031 six-phase dispatch P-192–P-198](project_wo031_phasing.md) — last task IDs issued: D-11→D-13, S-10, C-13→C-18, T-10→T-13, DO-05→DO-07, P-05→P-06 (see update note)
- [Interface Contracts were pre-drafted in CLAUDE.md before Design phase ran](project_interface_contracts_predrafted.md) — check brain first, don't re-derive from scratch
- [Correlation id is owned by ServiceDefaults.Security since WO-086](feedback_correlation_id_ownership.md) — read IRequestContextAccessor; never reintroduce a correlation-id reader here
- [P-256 EventId sub-block allocation](project_p256_eventid_allocation.md) — WebApi 14000-14099, SignalR 14100-14199 (Grpc +200 since); check CLAUDE.md for current table
- [13.ServiceDefaults's P-251 test hardcodes the wrong CorrelationId baggage-key literal](project_correlationid_baggage_key_mismatch.md) — discovered in P-256, fixed by WO-042; middleware since deleted (WO-086)
- [Cross-domain gating pattern for phases blocked on another domain's unshipped constant](project_cross_domain_gating_pattern.md) — design now/code gated later shape, used by P-256 and P-262/WO-042
- [01.Core has no Error.Forbidden/ErrorType.Forbidden](project_p381_forbidden_gap.md) — (since shipped, P-384) blocked WO-058/P-381; also fixed a stale pre-existing CLAUDE.md doc defect (fake "Forbidden→403/Failure→500" mapping)
- [Shared WebApi/Grpc types live in Presentation.Core](project_grpc_webapi_reference_decision.md) — Grpc no longer references WebApi (WO-086); GrpcStatusCodeMap stays a sibling of ErrorTypeStatusCodeMap
