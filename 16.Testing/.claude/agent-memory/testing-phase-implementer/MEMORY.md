# Memory Index

- [GreenDonut Result<T> collision](greendonut_result_collision.md) — SharedKernel.Testing.csproj CS0104: always fully-qualify Result<T>/Error, never alias.
- [Blocker reconciliation workflow](blocker_reconciliation_workflow.md) — always re-verify ⚑ blockers on disk before trusting state-map.md prose; clearing ≠ implementing.
- [Cross-domain shared fake migration](cross_domain_shared_fake_migration.md) — another domain's breaking-change phase can silently fix SharedKernel.Testing but leave SelfTests broken.
- [Coordinated breaking-wave verification](coordinated_breaking_wave_verification.md) — SharedKernel.Testing.csproj references concrete (non-Abstractions) production packages directly; a multi-domain wave can leave the real build red for reasons entirely outside 16.Testing — how to attribute errors and get a genuine stale-reference sanity signal via `-p:BuildProjectReferences=false`.
