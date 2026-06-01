---
name: project_ef_interceptor_changetracker_rule
description: Audit/SoftDelete interceptors must write via EF ChangeTracker CurrentValues — never via direct property setters
metadata:
  type: project
---

`AuditInterceptor` and `SoftDeleteInterceptor` must set field values exclusively via EF Core's ChangeTracker:

```csharp
context.Entry(entity).CurrentValues[nameof(IHasCreatedAudit.CreatedBy)] = value;
```

Direct property setter calls on the aggregate (`entity.CreatedBy = value`) are a hard violation.

**Why:** Domain aggregates declare audit properties with `private set` or no setter at all, enforcing that infrastructure cannot bypass domain invariants. EF Core's ChangeTracker can write these values directly at the database layer without the domain object needing a public setter. This maintains the domain model's encapsulation while still allowing persistence-layer population of cross-cutting fields.

**How to apply:** Any implementation task for AuditInterceptor or SoftDeleteInterceptor must use `CurrentValues[propertyName]` indexer syntax. Shadow property approach is also acceptable. Review any code review that shows direct property assignment.
