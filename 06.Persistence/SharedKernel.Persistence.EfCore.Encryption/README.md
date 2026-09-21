# SharedKernel.Persistence.EfCore.Encryption

Field-level AES-256-GCM encryption for EF Core 10 on PostgreSQL. Mark a `string` property with `.Encrypt("purpose")`
and it is stored encrypted and read back as plaintext. Add `.WithBlindIndex()` to find rows by an encrypted value.
Keys come from a KMS or configuration; rotation, plaintext migration and per-tenant crypto-shredding are built in.

## Setup

```csharp
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Encryption.Extensions;
using SharedKernel.Persistence.EfCore.Extensions;

// A KMS (Azure Key Vault): the key source and, for tenant data keys, the envelope provider that wraps them.
builder.Services.AddSharedKernelCryptography(builder.Configuration)
    .AddAzureKeyVaultEncryption(builder.Configuration);

builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p
    .UseMultiTenancy(rowLevelSecurity: true)
    .UseFieldEncryption(k => k.UseTenantDataKeys()));   // optional: per-tenant keys and crypto-shredding

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> o, PersistenceContextDependencies d)
    : TenantedDbContext(o, d);
```

Without a key-source call, the `IEncryptionKeyProvider` already in the container is used (above: the Key Vault
provider). `k.FromConfiguration()` reads keys from `SharedKernel:Persistence:Encryption:Keys` instead,
`k.UseKeyProvider<TProvider>()` names a provider type. Register a provider once; nothing needs a second synchronous
registration. Options bind from `SharedKernel:Persistence:Encryption` and are validated at host start together with
the key source, so a missing provider fails startup rather than the first request. These samples are compiled by the
test suite (`ReadmeSampleTests`).

```json
"SharedKernel": { "Persistence": { "Encryption": {
  "Keys": { "CurrentKeyId": "k2", "Keys": { "k1": "<base64 32 bytes>", "k2": "<base64 32 bytes>" } },
  "BlindIndexKeys": { "CurrentVersion": "v1", "Keys": { "v1": "<base64 >= 32 bytes>" } }
} } }
```

`Keys` is read only by `FromConfiguration()`. Keep key material in a secret store (Key Vault configuration, a
mounted secret), never in a checked-in file.

## Marking properties

```csharp
modelBuilder.Entity<Customer>(b =>
{
    b.Property(x => x.Email).HasMaxLength(320).Encrypt("customer.email")
        .WithBlindIndex(BlindIndexNormalization.Trim | BlindIndexNormalization.CaseFold);
    b.Property(x => x.NationalId).Encrypt("customer.national_id");
    b.ComplexProperty(x => x.Billing, a => a.ComplexProperty(x => x.Bank, bank =>
        bank.Property(x => x.Iban).Encrypt("customer.billing.iban")
            .WithBlindIndex(BlindIndexNormalization.RemoveWhitespace, normalizer: "iban")));
});
```

- The **purpose** is a stable, lowercase, dotted label, unique across the whole model (checked at model build). It is
  bound into every value and selects the column's own key. Renaming tables or columns never breaks stored data;
  renaming a purpose does.
- Works on entity properties and on complex-type properties at any depth. Rejected at model build: non-`string`
  properties (byte arrays included; store another representation as a string), complex collections, JSON-mapped and
  struct complex types, composite or shadow primary keys, and keys other than `Guid`, `long`, `int` or `string`.
  Primary keys must be assigned on the client (a UUID v7 or strongly-typed id), because the key is bound into the value.
- A declared `HasMaxLength(n)` is the plaintext length; the column is widened to fit the ciphertext.
- The model stores only strings, flags and names, so `dotnet ef migrations add` and compiled models work.
- A named normalizer is registered with `k.AddBlindIndexNormalizer<TNormalizer>()` (`IBlindIndexNormalizer`).

## Reading and querying

Entities are decrypted when materialized. Everything else about an encrypted column is refused **before the query
runs**: filtering, sorting, grouping, joining on it, projecting it (`Select(x => x.Email)`, or a complex value holding
it), and `ExecuteUpdate` setters that write or read it. Only `== null` / `!= null` are allowed. Unrelated SQL and
columns elsewhere with the same name are never affected. Hand-written SQL (`FromSql`, `ExecuteSql`) is not checked.

To find rows by value, use the blind index:

```csharp
var customer = await db.Customers.WhereEncryptedEquals(x => x.Email, input).SingleOrDefaultAsync(ct);
```

Purpose, normalization and the caller's tenant come from the model and the context; the index travels as a query
parameter. Blind indexes are tenant-bound: a tenanted entity is searched within one tenant (pass `tenantId:` to the
`IQueryable` overload for a cross-tenant job). An index reveals which rows hold equal values; do not index
low-cardinality properties.

## Keys and crypto

- **AES-256-GCM**, a random 96-bit nonce per value, and **one key per column**: HKDF-SHA256 derives it from the root
  key (or the tenant's data key) and the purpose. Associated data binds purpose, primary key and tenant, so a value
  copied to another row, column or tenant fails to decrypt.
- **Blind indexes use their own versioned keys** (`IBlindIndexKeyProvider`, stored values look like `v1:3fa9…`), so
  rotating the encryption key never touches lookups.
- **Asynchronous-only providers** (a KMS) are bridged: keys are loaded at startup and refreshed every
  `KeyRefreshInterval`. A key id the process has not loaded is fetched in the background the first time a value needs
  it; that one read fails with `EncryptionKeyNotFoundException`, later reads succeed. List ids that must work from
  the first request in `AdditionalDecryptionKeyIds`. The keyed `IEncryptionKeyProviderProbe`
  (`FieldEncryptionServiceKeys.KeyRingProbe`) turns unhealthy when the keys were not refreshed within
  `MaxKeyStaleness`, and otherwise reports the provider's own probe; wire it into readiness.

### Key rotation protocol

1. Add the new key to the key source, not yet current. Wait at least `KeyRefreshInterval` (every process can now
   decrypt with it).
2. Make it current. Wait `KeyRefreshInterval` again (every process now encrypts with it).
3. Run the maintenance job with `ReEncrypt` and `ExpectedCurrentKeyId` = the new key.
4. Run `VerifyOnly`; retire the old key only when `report.IsSafeToRetire(oldKeyId)` is true.

Rotating the **blind-index key**: add the new version and make it current (lookups match every configured version),
run `RecomputeBlindIndexes`, confirm `VerifyOnly` reports `StaleBlindIndexes == 0`, then remove the old version.

## Maintenance job (`IEncryptionRotationJob`)

A run reads and writes every tenant's rows, so it requires a cross-tenant scope the caller entered around it, in a
scope whose `IRequestContext` identifies the job — the entry is logged under that identity. The job never enters the
scope itself and throws `InvalidOperationException` without one.

```csharp
public sealed class KeyRotationJob(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        // services resolve IRequestContext; register it for jobs as e.g. new SystemRequestContext([], "key-rotation")
        using (services.GetRequiredService<ICrossTenantScope>().Enter("rotate field encryption to k2"))
        {
            var report = await services.GetRequiredService<IEncryptionRotationJob>().RunAsync(new EncryptionMaintenanceRequest
            {
                Mode = EncryptionMaintenanceMode.ReEncrypt | EncryptionMaintenanceMode.RecomputeBlindIndexes,
                ExpectedCurrentKeyId = "k2",
            }, progress: null, stoppingToken);
        }
    }
}
```

| Mode | Does |
|---|---|
| `VerifyOnly` | Decrypts every value and counts it by key; writes nothing |
| `ReEncrypt` | Moves values not on the current key (or the tenant's data key) onto it |
| `RecomputeBlindIndexes` | Rewrites indexes under the current blind-index version and normalization |
| `EncryptPlaintext` | Encrypts values still stored as plaintext (a column that was just marked `.Encrypt`) |

It walks every encrypted column in primary-key order in short transactions, with plain SQL (no query filter hides
soft-deleted or other tenants' rows, no interceptor stamps audit columns), and writes with a batched
compare-and-swap, so a value changed concurrently is left alone and counted. TPH columns shared by sibling types
are processed once, TPT/TPC columns in the table that holds them. Cancelling returns a checkpoint token to resume.
Run it from a hosted service, scheduled job or workflow activity, never from request handling (SK0303).

A stored plaintext value is never read silently: materializing it throws. To encrypt an existing column, deploy the
`.Encrypt(...)` model, then run `EncryptPlaintext` before serving reads.

**Row-level security.** Each maintenance transaction runs with `row_security = off`, so a policy that would hide
rows fails the run instead of letting it report completion over the visible rows only. Maintenance therefore needs
a role that bypasses row-level security: the cross-tenant data source `AddSharedKernelNpgsql` registers for RLS is
used automatically, or pass one with `k.UseMaintenanceDataSource(sp => …)`. A role that sees every row through a
role-specific policy instead needs `RequireRowSecurityBypass = false`; the job then refuses a table that looks empty
while PostgreSQL's statistics say it is not.

## Per-tenant data keys and crypto-shredding

With `UseTenantDataKeys()` every encrypted value of a tenanted entity is encrypted under its tenant's own data key,
generated and wrapped by the registered `IEnvelopeEncryptionProvider` (a KMS master key) and stored wrapped in
`sk_tenant_encryption_keys` (create it with `migrationBuilder.CreateTenantEncryptionKeyTable()`). Keys are created on
a tenant's first encrypted write; existing values move with `ReEncrypt`.

```csharp
using (crossTenantScope.Enter("GDPR erasure request 2026-114"))   // required, as for the maintenance job
{
    TenantShredResult result = await keys.ShredTenantAsync(tenantId, cancellationToken: ct); // ITenantEncryptionKeyManager
}
```

Shredding deletes the wrapped key (keeping a tombstone, so the tenant id never gets a new key) and clears the
tenant's blind indexes in the same transaction (a keyed hash of the plaintext would otherwise still let guesses be
checked). Afterwards the tenant's values under its key cannot be decrypted by anyone: reading one throws
`TenantKeyShreddedException` (`Persistence.Encryption.TenantKeyShredded`, NotFound), writing for the tenant too.
Columns that are not encrypted are not touched: delete or anonymize them yourself.

**Only values under the tenant's key are erased.** Values written before `UseTenantDataKeys()` stay under a root key
until the maintenance job moves them (`ReEncrypt`), and a column marked `.Encrypt()` late may still hold plaintext
(`EncryptPlaintext`). Destroying the tenant key does not erase those, so `ShredTenantAsync` checks the tenant's rows
first and, when any remain, throws `TenantShredIncompleteException` (`Persistence.Encryption.TenantShredIncomplete`,
Conflict, with the counts) and changes nothing. Migrate them and shred again — the order is: `ReEncrypt |
EncryptPlaintext` run, then shred. To shred anyway (and delete those rows afterwards), pass
`new TenantShredOptions { AllowIncompleteErasure = true }`: `result.IsComplete` is then `false` and
`RootKeyValues`/`PlaintextValues` say how many remain. The platform also refuses to read a root-key value of a
shredded tenant (`TenantKeyShreddedException`), but anyone holding the root key still can until the rows are deleted;
the maintenance job counts them as `ShreddedTenantValuesNotErased` (never as `ShreddedValues`) and never re-encrypts
or re-indexes a shredded tenant's rows.

**What other processes do after a shred:**

| | Guarantee |
|---|---|
| Writes through EF Core in a transaction (the platform unit of work, `Database.BeginTransaction`) | Every save that encrypts a value of a tenant re-reads the tombstone inside its transaction and locks the tenant's key row (`FOR SHARE`). A shred that committed first is always seen (`TenantKeyShreddedException`); a shred that starts during the transaction waits for it, then clears what it wrote. No value is written under the key by a transaction that commits after the shred. |
| Writes through EF Core without a transaction | The tombstone is re-read just before EF Core's own transaction starts: a shred committing inside that window is missed for that one save. |
| Reads | This process forgets the key at once. Another process can still decrypt from its cached copy for up to `TenantKeyCacheDuration` (default 5 minutes). |
| Dapper, raw SQL | Not checked. |

Wrapped keys in database backups keep the data recoverable until those backups expire or the master key is destroyed.
Record the erasure in your audit trail; the log entry deliberately carries no tenant id. Revoke `DELETE` on
`sk_tenant_encryption_keys` from the application roles, so a tombstone cannot be removed (see the
`SharedKernel.Persistence.Npgsql` README's role script).

## Diagnostics

Meter `SharedKernel.Persistence.EfCore.Encryption` (`EncryptionMeter`): encrypt/decrypt failures by reason,
maintenance values written and skipped, tenant keys shredded. Logs use EventIds 6500-6699. No metric or log ever
carries key material, a value, a primary key or a tenant id.
