---
name: phase-numbering-state
description: Last known phase and work order numbers in the root state-map Phase Backlog
metadata:
  type: project
---

As of 2026-06-04, the last phase written to `state-map.md` Phase Backlog is **P-114** under **WO-019**.

Next new phase must be **P-115**. Next new Work Order must be **WO-020**.

**How to apply:** Always read the current Phase Backlog before assigning new IDs — this memory is a starting point, not a substitute for reading the file.

**WO-019 context:** DB-level field encryption for EfCore persistence layer — 4 phases across 2 domains:
- P-111 06.Persistence: EncryptionOptions + PersistenceServiceOptions + AuditInterceptor service-name fallback + EfCorePersistenceBuilder.WithEncryption() / .WithServiceName()
- P-112 06.Persistence: EncryptedValueConverter<string> (AES-256-GCM, versioned ciphertext `v{ver}:{B64}`), .Encrypt() PropertyBuilder extension, EncryptionModelConvention (IModelFinalizingConvention), IEncryptionRotationJob + EncryptionRotationService base, EncryptionKeyNotFoundException — depends P-111
- P-113 06.Persistence: Tests — converter round-trip, tamper detection, hot-reload, legacy plaintext, convention integration, rotation idempotency, service-name audit, builder wiring — depends P-111, P-112
- P-114 00.Governance: Architecture rules SK0301 (no AES in Domain/Application), SK0302 (no Encrypt attributes on domain types), SK0303 (IEncryptionRotationJob not in Domain/Application), SK0304 (no direct EncryptedValueConverter instantiation in configs) — depends P-112

**Domains touched:** 06.Persistence (already ●), 00.Governance (already ●) — no `state-map-phase` calls needed.

**Key architectural decisions made in WO-019:**
1. AES-256-GCM chosen over AES-CBC+HMAC — single authenticated primitive, no separate MAC step
2. Versioned ciphertext format `v{version}:{Base64(nonce||ciphertext||tag)}` is the minimum metadata for key rotation transparency — version prefix in stored value identifies the decryption key
3. IModelFinalizingConvention is the correct EF Core extension point — runs after all IEntityTypeConfiguration implementations, has full model visibility, zero domain leakage
4. `.Encrypt()` annotation-only on PropertyBuilder — writes `SharedKernel:Encrypt` annotation; convention wires the converter; entity types carry no encryption attributes
5. IOptionsMonitor<EncryptionOptions> (not IOptionsSnapshot) — required for singleton converter hot-reload without restart
6. EncryptionOptions.Enabled == false makes converter a pass-through — toggling Enabled at runtime takes effect without model rebuild
7. IEncryptionRotationJob abstraction in EfCore package (not Abstractions) — references EF Core types (IDbContextFactory); registered only when .WithEncryption() is called
8. PersistenceServiceOptions.ServiceName replaces hardcoded "system" in AuditInterceptor.ResolveUserId() — configurable per-service via .WithServiceName()
9. SharedKernelDbContext gains optional IOptionsMonitor<EncryptionOptions>? constructor parameter — nullable to preserve backward compat for all existing subclasses
