# Governance Arch Planner — Memory Index

- [Guard Purity Phase](project_guard_purity_phase.md) — SK.00.GuardPurity design decisions: Mono.Cecil IL inspection approach, Guard+Throw exclusion, cross-domain dependency on P-003
- [SK Diagnostic Registry](project_sk_diagnostic_registry.md) — all assigned SK IDs with block conventions; sequential block SK0001–SK0012 (next: SK0013), multi-tenancy block SK0201–SK0202 (next: SK0203), encryption block SK0301–SK0304 (next: SK0305), messaging block SK0701–SK0708 (next: SK0709); SK0012 is a NetArchTest ICustomRule (not Roslyn) — first such case in the sequential block
- [Redis Topology Phase](project_redis_topology_phase.md) — SK.00.RedisTopology design: RedisTopologyRules (5 ConditionList predicates, no new SK IDs), 5-package Redis topology enforcement, exemption list
