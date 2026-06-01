# Persistence Architecture Planner — Memory Index

- [Outbox scope: owned by 07.Messaging only](project_outbox_scope.md) — No OutboxMessage/IOutboxWriter/OutboxInterceptor in 06.Persistence; SharedKernelDbContext has exactly three interceptors
- [IUserContext injection pattern](project_iusercontext_pattern.md) — No 12.Security project ref in Persistence; no-op placeholder in EfCorePersistenceBuilder; scoped interceptors
- [SpecificationEvaluator canonical order](project_specification_evaluator_order.md) — Criteria→Includes→OrderBy→ThenBys→Distinct→AsNoTracking→Skip/Take (paging always last)
- [ICurrentTenantService lives in EfCore, not Abstractions](project_icurrenttenantservice_location.md) — DbContext concern; concrete impl in 13.ServiceDefaults.MultiTenancy
- [Audit/SoftDelete interceptors use ChangeTracker only](project_ef_interceptor_changetracker_rule.md) — CurrentValues[propertyName] indexer; never direct property setters on aggregates
