---
name: project_encryption_version_override
description: IEncryptionVersionOverride scoped seam for rotation-time key version control without mutating EncryptionOptions.CurrentVersion
metadata:
  type: project
---

P-147 introduced `IEncryptionVersionOverride` (mutable `string? OverrideVersion`,
default `null`) plus a no-op `EncryptionVersionOverride` default implementation,
registered scoped via `.WithEncryption()`.

`EncryptedValueConverter`'s constructor gains an optional parameter:
`EncryptedValueConverter(IOptionsMonitor<EncryptionOptions> optionsMonitor, IEncryptionVersionOverride? versionOverride = null)`.
`Encrypt()` resolves the target version as `versionOverride?.OverrideVersion ?? options.CurrentVersion`.
`EncryptionModelConvention` resolves the scoped `IEncryptionVersionOverride` from DI
and passes it through.

`EncryptionRotationService.RotateAsync` sets `OverrideVersion = toVersion` on the
**batch context's own scoped instance only**, and only around the
`SaveChangesAsync` call for that batch — reset to `null` immediately after.
`EncryptionOptions.CurrentVersion` is NEVER mutated by rotation. This guarantees
unrelated concurrent scopes (other requests encrypting new data during a rotation)
continue using `CurrentVersion` and are unaffected by the rotation's override.

**Why:** Original design required callers to manually flip `EncryptionOptions.CurrentVersion`
to `toVersion` before calling `RotateAsync`, which would cause all NEW writes across
the whole service to switch keys mid-rotation — a correctness and blast-radius problem.
The scoped override isolates the version change to the rotation's own batch contexts.

**How to apply:** Any future encryption-related design must preserve this separation —
`EncryptionOptions.CurrentVersion` is the steady-state key version for all normal
read/write traffic; `IEncryptionVersionOverride` is exclusively for rotation's
internal use and must never leak outside a rotation batch's DI scope.

See also [[project_encryption_subsystem]], [[project_loadbatchasync_reflection_free]].
