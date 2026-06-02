---
name: project_iusercontext_pattern
description: IUserContext injection pattern — approved Security.Abstractions reference, GUID audit string format, no-op placeholder rules
metadata:
  type: project
---

`AuditInterceptor` and `SoftDeleteInterceptor` need `IUserContext` from `SharedKernel.Security.Abstractions`.

**P-078 decision (WO-014, 2026-06-02):** `SharedKernel.Persistence.EfCore` holds a deliberate project reference to `SharedKernel.Security.Abstractions`. This is an approved layering exception — `12.Security.Abstractions` is a zero-dependency interface library. The prior workaround (local `IUserContext.cs` copy in EfCore) was removed. All other `12.Security.*` packages remain forbidden in `06.Persistence`.

**`IUserContext` shape (Security.Abstractions):** `UserId` is `Guid`; `IsAuthenticated` is `bool`.

**No-op placeholder:** `EfCorePersistenceBuilder.Build()` registers a scoped no-op with `UserId = Guid.Empty`, `IsAuthenticated = false` when no `IUserContext` is already registered.

**Audit string format rule (P-091, WO-016, 2026-06-02):** Audit columns (`CreatedBy`, `ModifiedBy`, `DeletedBy`) are `string HasMaxLength(256)`. The value is produced as:
- `IsAuthenticated == true && UserId != Guid.Empty` → `userId.ToString("D")` (lowercase hyphenated GUID, 36 chars)
- Otherwise → `"system"`

Only `"D"` format is permitted. `"N"`, `"B"`, `"P"`, `"X"` are all violations.

**Why:** `UserId` changed from `string` to `Guid` when migrating to `Security.Abstractions`. Audit columns remain `string`. The `"D"` format is stable, unique, human-readable, and fits within 256 chars. `"system"` fallback prevents null audit records in background-service or test contexts.

**How to apply:** When planning any audit interceptor task, always include the `ToString("D")` vs `"system"` branch logic. Never store raw `Guid.ToString()` without the `"D"` specifier.

See also: [[project_icurrenttenantservice_location]]
