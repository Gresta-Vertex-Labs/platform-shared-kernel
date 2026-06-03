# Persistence Architecture Planner — Memory Index

- [Outbox scope: owned by 07.Messaging only](project_outbox_scope.md) — No OutboxMessage/IOutboxWriter/OutboxInterceptor in 06.Persistence; SharedKernelDbContext has exactly three interceptors
- [IUserContext injection pattern](project_iusercontext_pattern.md) — Security.Abstractions ref approved (P-078); audit string is userId.ToString("D") or "system"; no-op has Guid.Empty/IsAuthenticated=false
- [ICurrentTenantService removed — ITenantProvider + Guid.Empty sentinel](project_icurrenttenantservice_location.md) — TenantedDbContext uses ITenantProvider; Guid.Empty = zero rows (safe); expression-tree filter (no reflection)
- [SpecificationEvaluator canonical order](project_specification_evaluator_order.md) — Criteria→Includes→OrderBy→ThenBys→Distinct→AsNoTracking→Skip/Take (paging always last)
- [IProjectionSpecification in Abstractions](project_projection_specification.md) — Extends ISpecification; adds Selector expression; prerequisite for projection reads; IDbConnectionFactory doc restriction removed
- [EfRepository.ExistsAsync and GetByIdsAsync](project_repository_extensions.md) — ExistsAsync uses AnyAsync (no materialization); GetByIdsAsync uses IN clause; warn >1000 IDs
- [EfRepository.UpdateAsync tracking optimization](project_efrepository_update_optimization.md) — Skip .Update() for tracked entities; only call for Detached; MarkAsModifiedIfDetached helper
- [Audit/SoftDelete interceptors use ChangeTracker only](project_ef_interceptor_changetracker_rule.md) — CurrentValues[propertyName] indexer; never direct property setters on aggregates
- [ISpecificationEvaluator GetProjectedQuery on interface](project_ispecificationevaluator_projection.md) — GetProjectedQuery promoted to interface (P-097); EfReadRepository field is ISpecificationEvaluator not concrete; downcast is hard violation
- [EfUnitOfWork single constructor rule](project_efunitofwork_single_constructor.md) — One constructor only: (SharedKernelDbContext, IDomainEventDispatcher? = null); second constructor = DI ambiguity hard violation (P-098)
- [ITransactionalUnitOfWork + IPersistenceTransaction](project_itransactionalunitofwork.md) — BCL-only interfaces in Abstractions; EfPersistenceTransaction wraps IDbContextTransaction in EfCore; WithTransactionalUnitOfWork() optional (P-099)
- [TenantedRepository soft-delete bypass split](project_tenantedrepository_softdelete_bypass.md) — GetByIdForTenantAsync preserves soft-delete; GetByIdForTenantIncludingDeletedAsync bypasses both; IgnoreQueryFilters + re-apply Where workaround (P-100)
- [ListPagedProjectedAsync two-round-trip pattern](project_listpagedprojectedasync.md) — Count via GetQuery (no projection), data via GetProjectedQuery; depends on P-097 interface promotion (P-101)
- [ValueObjectOwnershipBuilder rename from Convention](project_valueobjectownershipbuilder.md) — Not IModelFinalizingConvention; call Apply() manually from OnModelCreating; ConfigureConventions has no effect (P-102)
