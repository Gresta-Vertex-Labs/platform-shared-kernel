# Memory Index

- [WO-031 six-phase dispatch P-192–P-198](project_wo031_phasing.md) — last task IDs issued: D-11→D-13, S-10, C-13→C-18, T-10→T-13, DO-05→DO-07, P-05→P-06 (see update note)
- [Interface Contracts were pre-drafted in CLAUDE.md before Design phase ran](project_interface_contracts_predrafted.md) — check brain first, don't re-derive from scratch
- [CorrelationIdMiddleware baggage/Items keys are this domain's own contract](feedback_correlation_id_ownership.md) — never list as a Pending dependency on 13.ServiceDefaults
- [P-256 EventId sub-block allocation](project_p256_eventid_allocation.md) — WebApi 14000-14099, SignalR 14100-14199; gated on 01.Core's P-249 landing
- [13.ServiceDefaults's P-251 test hardcodes the wrong CorrelationId baggage-key literal](project_correlationid_baggage_key_mismatch.md) — discovered in P-256, flagged not fixed (out of jurisdiction)
