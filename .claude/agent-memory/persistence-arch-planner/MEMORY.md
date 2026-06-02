# Persistence Architecture Planner — Memory Index

- [Outbox scope: owned by 07.Messaging only](project_outbox_scope.md) — No OutboxMessage/IOutboxWriter/OutboxInterceptor in 06.Persistence; SharedKernelDbContext has exactly three interceptors
- [IUserContext injection pattern](project_iusercontext_pattern.md) — Security.Abstractions ref approved (P-078); audit string is userId.ToString("D") or "system"; no-op has Guid.Empty/IsAuthenticated=false
- [ICurrentTenantService removed — ITenantProvider + Guid.Empty sentinel](project_icurrenttenantservice_location.md) — TenantedDbContext uses ITenantProvider; Guid.Empty = zero rows (safe); expression-tree filter (no reflection)
- [SpecificationEvaluator canonical order](project_specification_evaluator_order.md) — Criteria→Includes→OrderBy→ThenBys→Distinct→AsNoTracking→Skip/Take (paging always last)
- [IProjectionSpecification in Abstractions](project_projection_specification.md) — Extends ISpecification; adds Selector expression; prerequisite for projection reads; IDbConnectionFactory doc restriction removed
- [EfRepository.ExistsAsync and GetByIdsAsync](project_repository_extensions.md) — ExistsAsync uses AnyAsync (no materialization); GetByIdsAsync uses IN clause; warn >1000 IDs
- [EfRepository.UpdateAsync tracking optimization](project_efrepository_update_optimization.md) — Skip .Update() for tracked entities; only call for Detached; MarkAsModifiedIfDetached helper
- [Audit/SoftDelete interceptors use ChangeTracker only](project_ef_interceptor_changetracker_rule.md) — CurrentValues[propertyName] indexer; never direct property setters on aggregates
