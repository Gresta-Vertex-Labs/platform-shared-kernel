---
name: tenantedrepository-softdelete-bypass
description: TenantedRepository has two cross-tenant lookup methods with distinct filter semantics; GetByIdForTenantAsync preserves soft-delete, GetByIdForTenantIncludingDeletedAsync bypasses both (P-100)
metadata:
  type: project
---

`TenantedRepository<TAggregate, TId>` has two methods for cross-tenant admin lookups (P-100, WO-017):

1. `GetByIdForTenantAsync(TId id, Guid tenantId, ct)` — bypasses the tenant filter only; preserves the soft-delete filter for `ISoftDeletable` entities. Implementation: `IgnoreQueryFilters()` + manual `Where(e => !EF.Property<bool>(e, "IsDeleted"))` re-application when `typeof(ISoftDeletable).IsAssignableFrom(typeof(TAggregate))`.

2. `GetByIdForTenantIncludingDeletedAsync(TId id, Guid tenantId, ct)` — bypasses BOTH the tenant filter AND the soft-delete filter via `IgnoreQueryFilters()` only. For audit, recovery, and data-export operations only.

**Why:** EF Core's `IgnoreQueryFilters()` cannot selectively bypass one filter — it bypasses all. The workaround for preserving soft-delete while bypassing tenant is to re-add the soft-delete condition as an explicit `Where` clause after `IgnoreQueryFilters()`. The original single `GetByIdForTenantAsync` silently returned soft-deleted records, which is a correctness trap for developers using it in admin panels.

**How to apply:** Non-`ISoftDeletable` entities: both methods behave identically. XML doc on each method must state its filter semantics clearly. The rule is: if the method name does not say "IncludingDeleted", soft-deleted records must be excluded.
