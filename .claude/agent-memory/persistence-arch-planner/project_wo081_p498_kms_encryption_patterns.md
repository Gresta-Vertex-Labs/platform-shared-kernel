---
name: project_wo081_p498_kms_encryption_patterns
description: P-498/WO-081 SEVERE fix design — why SavingChangesAsync-only pre-warming is structurally unsound against KMS-backed IEncryptionKeyProvider, and the corrected dual-hook + keyed-DI design
type: project
---

**Context:** WO-081 is a coordinated, `01.Core`-first breaking wave. `01.Core`'s `core-arch-planner`
design-locked `SK.01.P491` (associated-data required on `ISymmetricEncryptionService`, breaking),
`SK.01.P492` (`ISynchronousEncryptionKeyProvider` marker + `EncryptionKeyProviderCapabilities.
IsGenuinelySynchronous`, replacing the silent sync-bridge blocking hazard with a structural
`NotSupportedException`), and `SK.01.P496` (Azure Key Vault provider hardening) in the same session
`06.Persistence`'s own P-498 was dispatched. P-498 closes F1 SEVERE — thread-pool starvation when
`EncryptedValueConverter` runs against a KMS-backed `IEncryptionKeyProvider`.

**Why the phase input's own premise ("pre-warm via a `SavingChangesAsync` hook") is UNSOUND, not just
incomplete — record this if a future phase touches encryption-provider gating again:**
1. `SavingChangesAsync` never fires for a query. `EncryptedValueConverter`'s DECRYPT path runs during
   row materialization on every read (`ToListAsync`/etc.), which never calls `SaveChangesAsync` at all.
   A write-only hook does literally nothing for reads — and the motivating defect scenario is explicitly
   about READS ("100 rows means 200 blocking Key Vault calls").
2. Even for writes, wrapping a KMS provider in `01.Core`'s `CachedEncryptionKeyProvider` does NOT make
   `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous` return `true` — that check recursively
   unwraps to the true leaf provider via `CachedEncryptionKeyProvider.Inner`, and a KMS provider can
   never honestly implement `ISynchronousEncryptionKeyProvider` (real network I/O on a cache miss). The
   gate is a STATIC, provider-IDENTITY check computed ONCE at `AesGcmEncryptionService` construction —
   NOT a per-call "is this particular call going to block" check. So a bare cached KMS provider makes
   EVERY sync `Encrypt`/`Decrypt` call throw `NotSupportedException`, unconditionally, forever — cache
   warmth is irrelevant to whether the call is even LEGAL, not just whether it's fast.

**Corrected design (see `06.Persistence/state-map.md` D-126..D-134, all design-locked `●` 2026-09-08):**
- `EncryptionOptionsKeyProvider`/`NullEncryptionKeyProvider` (the config-backed default, already
  source-verified zero-I/O) gain `ISynchronousEncryptionKeyProvider` directly. **This is the single most
  load-bearing task in the whole phase** — without it, EVERY existing config-backed `.WithEncryption()`
  user breaks (sync `Encrypt`/`Decrypt` start throwing) the moment `01.Core`'s P-492 repack lands.
- New `PreWarmedEncryptionKeyProvider` (06.Persistence-owned) HONESTLY earns the marker: it NEVER
  touches its wrapped inner provider from its own `IEncryptionKeyProvider` members — serves exclusively
  from a `ConcurrentDictionary<string,byte[]>` populated only by explicit async `WarmCurrentAsync`/
  `WarmVersionAsync` calls. A cache miss on `GetKeyAsync` returns `null` (non-blocking, matches the
  EXISTING "unknown key version" → `EncryptionKeyNotFoundException` path — no new failure shape); a
  never-warmed `GetCurrentKeyAsync` throws `InvalidOperationException` SYNCHRONOUSLY before constructing
  any `ValueTask` (mirrors `NullEncryptionKeyProvider`'s existing precedent).
- TWO warming hooks, not one, on a new `EncryptionKeyPreWarmingInterceptor` (a 5th interceptor, opt-in):
  `ISaveChangesInterceptor.SavingChangesAsync` for writes (before `ConvertToProviderExpression` runs),
  AND `IDbCommandInterceptor.ReaderExecutingAsync` for reads — EF Core's genuine ASYNC
  pre-materialization extension point (fires before `ExecuteReaderAsync` returns a `DbDataReader`,
  strictly before any row's `ConvertFromProviderExpression`). This is the piece that actually closes
  the F1 defect's literal scenario; a write-only hook cannot.
- Structural DI isolation (the actual root-cause fix, not just detection): `.WithEncryption()` STOPS
  registering `IEncryptionKeyProvider`/`ISymmetricEncryptionService` UNKEYED. It always constructs its
  own persistence-scoped instance under a package-internal keyed-DI slot. Root cause confirmed:
  `.WithEncryption()`'s `EncryptionOptionsKeyProvider` and e.g. `13.ServiceDefaults`'s
  `AddSharedKernelKeyVaultKeyProvider` (`AzureKeyVaultEncryptionKeyProvider`) both register the SAME
  unkeyed `IEncryptionKeyProvider` — whichever wins by registration ORDER in `Program.cs` becomes what
  `AesGcmEncryptionService` (a container-wide singleton) uses for EVERYTHING, including persistence
  column encryption, with zero domain boundary. New `.WithExternalEncryptionKeyProvider<TProvider>()`
  is the sanctioned opt-in for KMS-backed column encryption (wraps `TProvider` in
  `PreWarmedEncryptionKeyProvider`, registers the interceptor, blocks host readiness on a one-time boot
  warm-up).
- AAD derivation (closes `01.Core`'s own D-67 "hardest of the six" open question): bound to the
  property's stable `{schema}.{table}.{column}` storage identity, computed ONCE per property inside
  `EncryptionModelConvention` at model-finalization time (no row-PK access needed — one converter
  instance already exists per annotated property). Optional `.Encrypt(associatedDataOverride:)` escapes
  the documented rename hazard (renaming a table/column changes its AAD, breaks decrypting old rows).
- Explicit non-goal: `EncryptionRotationService` stays config-backed-only (`EncryptionOptions.Keys`)
  in this phase; KMS-backed rotation orchestration (coordinating with `01.Core`'s
  `AzureKeyVaultEncryptionKeyProvider.MintNewVersionAsync`, `SK.01.P496`) is left to a future phase.
- Known, accepted residual gap: the read-path hook only warms the CURRENT version (no way to enumerate
  all historical versions via `IEncryptionKeyProvider`). An unwarmed historical version on a cold
  process surfaces as the existing `EncryptionKeyNotFoundException`, never a block or corruption.

**General lesson for future phases in this domain:** when a phase input proposes wiring a KMS/async
provider through a fundamentally-SYNCHRONOUS EF Core extension point (`ValueConverter`, no async path
in EF Core 10), do not trust "add a pre-warm hook" at face value — check (a) whether the hook's trigger
point (`SaveChanges`) is even reachable from the code path being fixed (reads often aren't), and (b)
whether the actual gating mechanism (a capability marker, here) is a per-call check or a static,
provider-identity check that cache warmth cannot influence at all.
