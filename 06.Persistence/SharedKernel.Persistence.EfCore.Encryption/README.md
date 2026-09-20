# SharedKernel.Persistence.EfCore.Encryption

Field-level AES-256-GCM transparent encryption for EF Core 10 properties, built on `01.Core/SharedKernel.Cryptography`: `PropertyBuilder<T>.Encrypt()`, an HMAC-SHA256 blind index for equality lookups on encrypted columns without decrypting every row, and a resumable rotation job for moving encrypted data onto a new key. An opt-in sibling of `SharedKernel.Persistence.EfCore` — never referenced unless a service calls `.WithEncryption()`.

## Included types

- `EfCorePersistenceBuilderEncryptionExtensions.WithEncryption()` — opts in; two overloads (code-configured and `IConfiguration`-bound)
- `PropertyBuilderEncryptExtensions.Encrypt<TProperty>(purpose, perTenantKey: false)` — marks a `string` property (or, via the `ComplexTypePropertyBuilder<TProperty>` overload, a value object's own property) for transparent AES-256-GCM encryption
- `PropertyBuilderEncryptExtensions.WithBlindIndex<TProperty>(normalize: null)` — adds the HMAC-SHA256 shadow column an encrypted property needs for equality queries
- `EncryptedPropertyQueryExtensions.WhereBlindIndexEquals()` — the only sanctioned way to query an encrypted property by value
- `EncryptionInterceptor` — the `SaveChanges`-time encrypt-on-write / materialization-time decrypt-on-read interceptor
- `EncryptionModelConvention` — validates every `.Encrypt(...)` property (supported CLR type, unique purpose, a rotatable primary key shape) and adds the blind-index shadow property; wires no `ValueConverter` (see [How it works](#how-it-works))
- `EncryptedColumnEqualityGuardInterceptor` — fails loudly if a query compares an encrypted column with anything other than `WhereBlindIndexEquals`
- `IBlindIndexService` — the HMAC-SHA256 blind-index computation seam
- `IEncryptionRotationJob` / `EncryptionRotationService<TContext>` — re-encrypts every row not already on the current key, resumable across calls via an opaque checkpoint token
- `EncryptionRotationReport` — what one `RotateAsync` call did: rows processed/rotated/failed/skipped, and whether it completed
- `KeyRing.EncryptionKeyRingCache` — bridges an asynchronous-only key provider to the synchronous contract the runtime encrypt/decrypt path needs
- `EncryptionOptions` — `AllowUnencryptedValues`, `KeyRingRetiredKeyIds`, `KeyRingRefreshInterval`; carries no key material
- `EncryptionKeyNotFoundException` — thrown when a row's stored key id cannot be resolved
- `Diagnostics.EncryptionMeter` / `Diagnostics.EncryptionLog` — the package's metrics and structured logging

## Install

```xml
<ProjectReference Include="..\SharedKernel.Persistence.EfCore.Encryption\SharedKernel.Persistence.EfCore.Encryption.csproj" />
```

`.WithEncryption()` requires an `ISynchronousEncryptionKeyProvider` and/or an `IEncryptionKeyProvider` (both from `01.Core/SharedKernel.Cryptography`) already registered before it is called — this package never generates, stores, or defaults any key material itself.

## Quick start

```csharp
// A development/config-backed key. In production, register a KMS-backed provider instead
// (e.g. SharedKernel.Cryptography.KeyVault.Azure, or 13.ServiceDefaults's
// AddSharedKernelKeyVaultKeyProvider()) — .WithEncryption() bridges an asynchronous-only
// provider to the synchronous path automatically.
var keyProvider = new StaticEncryptionKeyProvider(
    currentKeyId: "2026-09",
    keys: [new CryptographicKey("2026-09", currentKeyMaterial)]);
services.AddSingleton(keyProvider);
services.AddSingleton<IEncryptionKeyProvider>(sp => sp.GetRequiredService<StaticEncryptionKeyProvider>());
services.AddSingleton<ISynchronousEncryptionKeyProvider>(sp => sp.GetRequiredService<StaticEncryptionKeyProvider>());

services.AddSharedKernelEfCore<AppDbContext>(options => options.UsePostgreSQL(connectionString))
    .WithEncryption(configuration)  // section "SharedKernel:Persistence:Encryption"; or the parameterless overload
        .Build();
```

```csharp
public sealed class CustomerConfiguration : EntityTypeConfigurationBase<Customer, CustomerId>
{
    public override void Configure(EntityTypeBuilder<Customer> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.Email).HasMaxLength(255).IsRequired()
            .Encrypt("customer.email")
                .WithBlindIndex(static s => s.Trim().ToLowerInvariant());

        builder.Property(c => c.Ssn).HasMaxLength(20).IsRequired()
            .Encrypt("customer.ssn", perTenantKey: true); // no blind index — never searched by value.
    }
}

// Query by an encrypted value:
var blindIndexService = serviceProvider.GetRequiredService<IBlindIndexService>();
var customer = await context.Customers
    .WhereBlindIndexEquals(blindIndexService, c => c.Email, "customer.email", "ada@example.com", normalize: static s => s.Trim().ToLowerInvariant())
        .SingleOrDefaultAsync();
```

Every `.Encrypt(...)` property needs its OWN, UNIQUE purpose label — see [Design decisions](#design-decisions).

## Rotating a key

```csharp
public sealed class RotateCustomerKeysJob(IEncryptionRotationJob rotationJob)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string? checkpoint = null;
        EncryptionRotationReport report;
        do
        {
            report = await rotationJob.RotateAsync(expectedCurrentKeyId: "2026-10", checkpointToken: checkpoint, cancellationToken: cancellationToken);
            checkpoint = report.CheckpointToken;
        }
        while (!report.Completed);

        if (report.RowsSkippedUnparseable > 0)
        {
            // Needs a human, not a retry — see EncryptionRotationReport.RowsSkippedUnparseable's remarks.
        }
    }
}
```

Register the new key with whatever `IEncryptionKeyProvider` your service uses, make it CURRENT there, then call `RotateAsync(expectedCurrentKeyId: "the new key's id")` — it fails fast if the provider's current key does not already match. Do not remove the old key from the provider until every `IEncryptionRotationJob.RotateAsync` call across the whole model reports `Completed: true` with an empty `RowsRemainingByKeyId`.

## How it works

There is **no `ValueConverter`** for an encrypted property — a `ValueConverter` can only see the one property being converted, never the row's primary key or tenant id, both of which the authenticated associated data (AAD) requires. Encryption and decryption instead happen in `EncryptionInterceptor`, which has full entity-graph access: it temporarily overwrites each `.Encrypt(...)` property's tracked value with its ciphertext immediately before the physical save, and restores the plaintext immediately after (success or failure) — the tracked entity graph a caller keeps using never observably holds ciphertext.

AAD binds `purpose` + the row's primary key + (for every `IHasTenant` entity, unconditionally) the tenant id — never the physical table/column/schema name, so renaming any of those never invalidates existing ciphertext. Renaming `purpose` itself does, since it is one of the bound components.

## Reference

| Type | Purpose |
|---|---|
| `PropertyBuilderEncryptExtensions.Encrypt<TProperty>(purpose, perTenantKey)` | Marks a `string` property (direct or complex-type) as encrypted. `perTenantKey` controls KEY DERIVATION only — AAD already binds the tenant id regardless. |
| `PropertyBuilderEncryptExtensions.WithBlindIndex<TProperty>(normalize)` | Adds an HMAC-SHA256 shadow column so the property can be searched by exact value. |
| `IBlindIndexService.Compute(purpose, normalizedValue, tenantId)` | Derives the blind index from whichever key the registered `ISynchronousEncryptionKeyProvider` currently reports as current. |
| `IBlindIndexService.Compute(key, purpose, normalizedValue, tenantId)` | The explicit-key overload — used by `EncryptionRotationService` to pin the blind index to the SAME key it is re-encrypting under, never the ambient synchronous provider's possibly-stale answer. |
| `EncryptedPropertyQueryExtensions.WhereBlindIndexEquals(query, blindIndexService, property, purpose, value, normalize?, tenantId?)` | The only sanctioned equality lookup against an encrypted property. |
| `EncryptedColumnEqualityGuardInterceptor.DisableTagText` | Pass to `.TagWith(...)` to opt one query out of the naive-equality guard. |
| `IEncryptionRotationJob.RotateAsync(expectedCurrentKeyId, checkpointToken?, batchSize = 500, cancellationToken)` | Re-encrypts every row not already on `expectedCurrentKeyId`, resumable via the returned `EncryptionRotationReport.CheckpointToken`. |
| `EncryptionRotationReport` | `RowsProcessed`, `RowsRotated`, `RowsFailed` (concurrent-writer collisions — safely retryable), `RowsSkippedUnparseable` (needs a human), `Completed`, `CheckpointToken`, `RowsRemainingByKeyId`. |
| `EncryptionOptions.AllowUnencryptedValues` | Temporary migration setting: a stored value that fails to parse as an encrypted payload is returned unchanged instead of throwing. Never enable permanently. |
| `EncryptionOptions.KeyRingRetiredKeyIds` / `.KeyRingRefreshInterval` | Historical key ids `EncryptionKeyRingCache` keeps warm, and how often it refreshes — only consulted when no genuine `ISynchronousEncryptionKeyProvider` was registered. |

## Pitfalls

- **A property with no blind index cannot be searched by value.** `.Where(x => x.Email == "...")` compiles but always returns zero rows — `EncryptedColumnEqualityGuardInterceptor` throws instead, for encrypted columns with OR without a blind index. Use `WhereBlindIndexEquals`.
- **A LINQ projection bypasses decryption.** `.Select(x => x.Email)` returns raw ciphertext — decryption happens only on full entity materialization. Always read an encrypted property through its owning entity.
- **The primary key must be client-generated.** It is part of the AAD, computed before the physical save — a store-generated (identity/serial) key is not yet known then and throws at save time.
- **Two encrypted properties on the same entity type must never share a purpose.** AAD would be identical for both on the same row, making their ciphertext freely swappable — rejected at model-build time.
- **A same-table owned entity type's OWN properties cannot be encrypted.** Its primary key is always a shadow property in EF Core 10, with no CLR-backed storage `EncryptionInterceptor` can read at materialization time. Move the property to the owning entity type, or use a complex type instead (which has no primary key of its own and is fully supported).
- **Only `string` properties are supported.** `.Encrypt()` on any other CLR type throws at model build.
- **Rotation supports only single-column primary keys** whose provider type is `Guid`, `long`, `int`, or `string`. A composite key with an encrypted property throws at model build, not only when a rotation happens to run.
- **`.WithEncryption()` must actually be called.** A model with `.Encrypt(...)` annotations but no `.WithEncryption()` call throws at model build (a core-package guard, `EncryptAnnotationRegisteredGuardConvention`) — it never silently persists plaintext.

## Design decisions

- **No `ValueConverter`.** See [How it works](#how-it-works).
- **AAD purpose uniqueness is enforced, not merely documented.** A model-build-time check rejects two `.Encrypt(...)` properties on one entity type sharing a purpose.
- **The blind index is always parameterized, never inlined as a SQL literal.** It is a keyed pseudonym of the plaintext — often PII by derivation — so `WhereBlindIndexEquals` forces it through `EF.Parameter(...)` explicitly, since the predicate is built via `Expression` APIs directly rather than compiled from C# lambda syntax.
- **Rotation is raw ADO.NET, never EF Core's change tracker, LINQ, or platform interceptors.** It reads with a plain `SELECT` (so soft-deleted and cross-tenant rows are included) and writes with a compare-and-swap `UPDATE ... WHERE pk = @pk AND ciphertext = @old` (so a concurrent writer's own save is detected — zero affected rows — rather than silently overwritten, and `AuditInterceptor` never stamps a rotation as a business change).
- **The rotation checkpoint identifies its target by name, never by position.** `BuildTargets` rebuilds the target list from the live model on every call; a purely positional checkpoint would silently point at the wrong target (or skip one entirely) if an `.Encrypt(...)` property was added to or removed from the model between the call that produced the checkpoint and the one resuming from it.
- **An unparseable stored value is counted and surfaced, never silently skipped.** `RowsSkippedUnparseable` is distinct from `RowsFailed` specifically so a clean-looking `Completed: true, RowsFailed: 0` report can never mask rows nobody actually rotated.

## AI quick reference

- Register a `01.Core` key provider (`IEncryptionKeyProvider` and/or `ISynchronousEncryptionKeyProvider`) BEFORE calling `.WithEncryption()` — this package supplies no default.
- `.Encrypt("stable.unique.purpose")` on a `string` property; `.WithBlindIndex()` only if it needs to be searched by exact value.
- Query an encrypted property only via `WhereBlindIndexEquals` — never a direct `==`.
- Rotation: loop `RotateAsync` while `!report.Completed`, passing `report.CheckpointToken` back in; check `RowsSkippedUnparseable` before declaring victory.

## Compatibility

`net10.0`. References `Microsoft.EntityFrameworkCore`/`.Relational`, `01.Core/SharedKernel.Cryptography`, `01.Core/SharedKernel.Configuration`, and `SharedKernel.Persistence.EfCore` — never `Npgsql` directly (PostgreSQL-specific wiring lives in `SharedKernel.Persistence.PostgreSQL`).

## Deliberately not included

- **`byte[]`-typed encrypted properties.** Only `string` is supported today.
- **Crypto-shredding via `perTenantKey`.** It gives genuine cross-tenant key ISOLATION, not per-tenant ERASURE — the subkey is deterministically re-derivable from the still-live root key and the (public) tenant id. Genuine per-tenant erasure needs a per-tenant root key registered under its own key id, deleted independently at offboarding — a key-management policy this package cannot automate.
- **A raw-SQL-parser-grade naive-equality guard.** `EncryptedColumnEqualityGuardInterceptor` is a documented heuristic (regex over generated SQL text) — a safety net for the common mistake, not a proof of absence for every query shape. `.TagWith(EncryptedColumnEqualityGuardInterceptor.DisableTagText)` opts one query out for a genuine false positive.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts and implementation rules.
