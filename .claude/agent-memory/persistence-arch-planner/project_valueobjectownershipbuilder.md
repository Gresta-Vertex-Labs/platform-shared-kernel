---
name: valueobjectownershipbuilder
description: Class renamed from ValueObjectOwnershipConvention to ValueObjectOwnershipBuilder (P-102); it is NOT an IModelFinalizingConvention and must be called manually from OnModelCreating
metadata:
  type: project
---

`ValueObjectOwnershipConvention` renamed to `ValueObjectOwnershipBuilder` (P-102, WO-017).

**Why:** The name `ValueObjectOwnershipConvention` misleads developers into expecting it auto-applies like EF Core conventions (e.g., `SnakeCaseNamingConvention` which implements `IModelFinalizingConvention`). It does not implement that interface and has no effect when registered via `ConfigureConventions`. Developers either skip calling `Apply` (missing `OwnsOne` configs → runtime model errors) or spend time looking for the convention registration hook.

**How to apply:** Always refer to this class as `ValueObjectOwnershipBuilder`. Call `ValueObjectOwnershipBuilder.Apply(modelBuilder)` manually at the end of `OnModelCreating` after all entity configurations. Never register via `ConfigureConventions`. XML doc on the class explicitly states the static-utility nature and the required manual call.
