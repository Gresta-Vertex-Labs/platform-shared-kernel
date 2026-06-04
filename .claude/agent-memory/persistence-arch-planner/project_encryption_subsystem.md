---
name: project_encryption_subsystem
description: Field-level AES-256-GCM encryption subsystem decisions for SharedKernel.Persistence.EfCore (WO-019, P-111/P-112/P-113)
metadata:
  type: project
---

Field-level transparent encryption lives entirely in `SharedKernel.Persistence.EfCore/Encryption/`. No domain entities carry encryption attributes (SK0302 violation if they do). All configuration via `PropertyBuilder<T>.Encrypt()` inside `IEntityTypeConfiguration<T>`.

**Why:** DDD purity — domain entities must not carry infrastructure concerns. EF Core value converters are the correct idiom for transparent value transformation.

**Key design decisions:**
- `EncryptionOptions` (section `SharedKernel:Encryption`): `Enabled` (bool, default false), `CurrentVersion` (string), `Keys` (Dictionary<string,string>). Eager startup validation via `ValidateOnStart()` — fires at `Build()` time, not lazily.
- `PersistenceServiceOptions` (section `SharedKernel:Persistence`): `ServiceName` (string, default `"system"`). Replaces hardcoded `"system"` literal in `AuditInterceptor.ResolveUserId()`. Registered via `.WithServiceName(string)`.
- `EncryptedValueConverter<string>`: AES-256-GCM, random 12-byte nonce per encrypt, format `"v{version}:{Base64(nonce||ciphertext||16-byte-tag)}"`. Holds `IOptionsMonitor<EncryptionOptions>` for hot-reload. When `Enabled == false`: pure pass-through (no AES). Legacy plaintext (no `"v"` prefix): returned unchanged.
- `EncryptionKeyNotFoundException` extends `SharedKernelException` — thrown when ciphertext version prefix not found in `Keys`. Never remove a key before all rows using it are rotated.
- `.Encrypt(bool? enabled = true)` on `PropertyBuilder<T>` writes annotation `"SharedKernel:Encrypt"`. Only permitted way to mark a property (SK0304 if converter instantiated directly in EntityTypeConfiguration).
- `EncryptionModelConvention` is `IModelFinalizingConvention` — registered automatically in `SharedKernelDbContext.OnModelCreating`. Always wires the converter; pass-through is gated by `Enabled` inside the converter.
- `SharedKernelDbContext` gains nullable optional `IOptionsMonitor<EncryptionOptions>?` constructor param. All existing subclasses remain backward compatible.
- `IEncryptionRotationJob` and `EncryptionRotationService` live in EfCore (NOT Abstractions — references EF types). Registered as scoped by `.Build()` only when `.WithEncryption()` was called. Default batch size 500 (virtual override).
- `EfCorePersistenceBuilder` gains `.WithEncryption(Action<EncryptionOptions>?)` and `.WithServiceName(string)`. Both optional — omitting leaves existing behavior unchanged.

**How to apply:** When planning any encryption-related capability: verify it stays in EfCore package, uses options pattern with eager validation, never leaks into Abstractions or domain layers. `IEncryptionRotationJob` must not be injected in MediatR handlers (SK0303).

Related: [[project_iusercontext_pattern]] (audit string format uses ServiceName as fallback)
