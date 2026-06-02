---
name: project-wo014-wo016-phases
description: WO-014 (P-081) and WO-016 (P-095) — locked design decisions for SharedKernel.Domain phases
metadata:
  type: project
---

WO-016 (P-095) added 5 tasks (D-31, C-35, T-28, DO-27, P-08) for the `IncludeDeleted` flag on `ISpecification<T>`.
WO-014 (P-081) added 5 tasks (D-32, C-36, T-29, DO-28, P-09) for `IDomainEventDispatcher` interface.

**Why:** WO-016 enables persistence layer to bypass EF Core's global soft-delete query filter via a domain-level specification flag. WO-014 enables `06.Persistence.EfCore.EfUnitOfWork` to hold an optional dispatch hook without coupling to `05.Application` (which would introduce a forbidden cross-layer dependency).

**How to apply:** Treat all decisions below as locked. Increment D/C/T/DO/P IDs from D-32/C-36/T-29/DO-28/P-09.

Key locked decisions for WO-016:

- `ISpecification<T>.IncludeDeleted` defaults to `false`. The safe default never changes existing behavior.
- `IncludeSoftDeleted()` builder call is restricted to admin/audit/export/recovery specification constructors only.
- `IgnoreQueryFilters()` in EF Core bypasses ALL global query filters — including tenant isolation. The XML doc must warn about this and document the workaround: re-apply tenant criterion via `AddCriteria(e => e.TenantId == tenantId)`.
- Composite propagation: `IncludeDeleted = true` when any operand is `true` (more-permissive wins — mirrors `AsNoTracking`).
- SharedKernel.Domain version bumped to 1.4.0 after WO-016.

Key locked decisions for WO-014:

- `IDomainEventDispatcher` lives in `SharedKernel.Domain` namespace under `03.Domain/SharedKernel.Domain/Abstractions/IDomainEventDispatcher.cs`.
- Single method: `Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)`.
- Interface references only `IDomainEvent` which is already in the same package — zero new NuGet dependencies.
- Two mandatory XML-doc contract rules on the interface: (a) empty list is a no-op, (b) handler exceptions propagate unchanged — no swallowing.
- DI registration is opt-in only — consuming services register an implementation alongside their infra builder (e.g., `EfCorePersistenceBuilder`). `IDomainEventDispatcher` is NOT auto-registered by any builder.
- `IDomainEventHandler<TEvent>` remains excluded from `03.Domain` — handler registration is `05.Application` territory.
- MediatR-based implementation (`MediatRDomainEventDispatcher`) is deferred to a future `05.Application` phase.
- Rationale for 03.Domain placement: `06.Persistence` already references `03.Domain`; placing the interface in `05.Application` would force `06.Persistence` to reference `05.Application`, which violates the layering rules (persistence is below application).
- SharedKernel.Domain version bumped to 1.5.0 after WO-014.

Related: [[project-wo010-wo011-phases]] [[project-domain-foundation]]
