# SharedKernel.Persistence.EfCore.Encryption

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![EF Core 10](https://img.shields.io/badge/EF%20Core-10-512BD4)](https://learn.microsoft.com/ef/core/)
![AES-256-GCM](https://img.shields.io/badge/cipher-AES--256--GCM-success)
![FIPS-approved algorithms](https://img.shields.io/badge/algorithms-FIPS%20approved-success)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)

> **Column-level encryption for EF Core on PostgreSQL: mark a property with `.Encrypt("purpose")` and it is stored as
> AES-256-GCM ciphertext, searchable by value, rotatable without downtime, and erasable per tenant.**

A database backup, a replica, a support engineer with read access or a leaked SQL dump should not expose a customer's
email, national id or IBAN. Encrypting in the application keeps those values ciphertext everywhere below it. This
package does that transparently for EF Core — your domain type keeps a plain `string` — and adds what a real system
needs around it: lookups by value, a query guard that refuses leaking queries, key rotation, migration of existing
plaintext, and **crypto-shredding**: destroy one tenant's key and its encrypted data is gone, backups included.

| 🔐 Transparent | 🔎 Searchable | 🔄 Rotatable | 🗑️ Erasable |
| --- | --- | --- | --- |
| `.Encrypt("purpose")` in the entity configuration | `.WithBlindIndex()` + `WhereEncryptedEquals` | Versioned keys, a maintenance job with a safe-to-retire report | Per-tenant data keys wrapped by a KMS |
| One derived key per column | Case- and space-insensitive options | Plaintext migration for existing columns | `ShredTenantAsync`: one call, checked for completeness |
| Ciphertext bound to row, column and tenant | Leaking LINQ refused before it runs | KMS keys refreshed in the background | Blind indexes cleared with the key |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Marking properties](#marking-properties)
- [Reading and querying](#reading-and-querying)
- [Keys](#keys)
- [Maintenance job](#maintenance-job)
- [Per-tenant data keys and crypto-shredding](#per-tenant-data-keys-and-crypto-shredding)
- [Security model](#security-model)
- [Pitfalls](#pitfalls)
- [AI quick reference](#ai-quick-reference)

## Install

```shell
dotnet add package SharedKernel.Persistence.EfCore.Encryption
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Builds on | `SharedKernel.Persistence.EfCore`, `SharedKernel.Cryptography` |
| Key source | any `IEncryptionKeyProvider` — Azure Key Vault (`SharedKernel.Cryptography.KeyVault.Azure`), configuration, or your own |
| Namespaces | `SharedKernel.Persistence` (`UseFieldEncryption`), `SharedKernel.Persistence.EfCore` (`Encrypt`, `WithBlindIndex`, `WhereEncryptedEquals`) |

## Quick start

**1. Register** — encryption plugs into the EF Core registration:

```csharp
using SharedKernel.Persistence;

// A KMS (Azure Key Vault): the key source and, for tenant data keys, the envelope provider that wraps them.
builder.Services.AddSharedKernelCryptography(builder.Configuration)
    .AddAzureKeyVaultEncryption(builder.Configuration);

builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p
    .UseMultiTenancy(rowLevelSecurity: true)
    .UseFieldEncryption(k => k.UseTenantDataKeys()));   // optional: per-tenant keys and crypto-shredding
```

**2. Mark properties** — in the entity configuration, never with attributes on domain types:

```csharp
using SharedKernel.Persistence.EfCore;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(x => x.Email).HasMaxLength(320).Encrypt("customer.email")
            .WithBlindIndex(BlindIndexNormalization.Trim | BlindIndexNormalization.CaseFold);
        b.Property(x => x.NationalId).Encrypt("customer.national_id");
    }
}
```

**3. Use it** — reads decrypt; lookups go through the blind index:

```csharp
var customer = await db.Customers.WhereEncryptedEquals(c => c.Email, "  Ada@Example.com ").SingleOrDefaultAsync(ct);
```

Without a key-source call, the `IEncryptionKeyProvider` already in the container is used. `k.FromConfiguration()`
reads keys from `SharedKernel:Persistence:Encryption:Keys` instead; `k.UseKeyProvider<TProvider>()` names a provider
type. Options bind from `SharedKernel:Persistence:Encryption` and are validated at host start with the key source, so a
missing provider fails the start, not the first request.

```json
"SharedKernel": { "Persistence": { "Encryption": {
  "Keys": { "CurrentKeyId": "k2", "Keys": { "k1": "<base64 32 bytes>", "k2": "<base64 32 bytes>" } },
  "BlindIndexKeys": { "CurrentVersion": "v1", "Keys": { "v1": "<base64 >= 32 bytes>" } }
} } }
```

`Keys` is read only by `FromConfiguration()`. Keep key material in a secret store (Key Vault configuration, a mounted
secret), never in a checked-in file.

## How it works

```text
 SaveChanges                                             Materialization
 ───────────                                             ───────────────
 Email = "ada@example.com"                               column: ciphertext (opaque)
   │  purpose "customer.email"                              │
   ▼                                                        ▼
 HKDF(root key or tenant key, purpose) ─► column key     same column key
   │                                                        │
 AES-256-GCM(value, nonce, AD = purpose‖row key‖tenant)  decrypt, verify AD ─► "ada@example.com"
   │
 email = ciphertext      email_blind_index = HMAC(blind key v1, normalize(value))
```

- **Interceptor-based**, never a `ValueConverter`: the entity keeps plaintext in memory, the column holds ciphertext.
- **One key per column**, derived with HKDF-SHA256 from the root key (or the tenant's data key) and the purpose.
- **Associated data** binds purpose, primary key and tenant: a value copied to another row, column or tenant fails to
  decrypt.
- **Blind indexes use their own versioned keys** (`v1:3fa9…`), so rotating the encryption key never touches lookups.
- **The model stores only strings, flags and names**, so `dotnet ef migrations add` and compiled models work — with a
  design-time factory that calls `UseFieldEncryption()` in `ConfigurePersistence`.

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

| Rule | Detail |
| --- | --- |
| Purpose | Stable, lowercase, dotted, unique across the model (checked at model build). Bound into every value — renaming tables or columns never breaks data; renaming a purpose does |
| Supported | `string` properties on entities and on complex types at any depth |
| Rejected at model build | non-`string` (store another representation as a string), complex collections, JSON-mapped and struct complex types, composite or shadow keys, keys other than `Guid`/`long`/`int`/`string` |
| Keys | Assigned on the client (UUID v7 or a strongly-typed id), because the key is bound into the value |
| Length | A declared `HasMaxLength(n)` is the plaintext length; the column is widened to fit the ciphertext |
| Custom normalization | `k.AddBlindIndexNormalizer<TNormalizer>()` (`IBlindIndexNormalizer`), referenced by name |

## Reading and querying

Entities are decrypted when materialized. Everything else about an encrypted column is refused **before the query
runs**: filtering, sorting, grouping, joining on it, projecting it (`Select(x => x.Email)`, or a complex value holding
it) and `ExecuteUpdate` setters that write or read it. Only `== null` / `!= null` are allowed. Hand-written SQL
(`FromSql`, `ExecuteSql`, Dapper) is not checked.

To find rows by value, use the blind index:

```csharp
var customer = await db.Customers.WhereEncryptedEquals(x => x.Email, input).SingleOrDefaultAsync(ct);
```

Purpose, normalization and the caller's tenant come from the model and the context; the index travels as a query
parameter. Blind indexes are tenant-bound: a tenanted entity is searched within one tenant (pass `tenantId:` to the
`IQueryable` overload for a cross-tenant job). An index reveals which rows hold equal values — do not index
low-cardinality properties.

## Keys

| Topic | Behavior |
| --- | --- |
| Algorithm | AES-256-GCM, a random 96-bit nonce per value |
| Column keys | HKDF-SHA256 from the root key (or tenant key) and the purpose |
| Blind-index keys | `IBlindIndexKeyProvider`, versioned; lookups match every configured version |
| KMS providers | Asynchronous providers are bridged: keys loaded at startup, refreshed every `KeyRefreshInterval` (5 min) |
| Unknown key id | Fetched in the background; that one read throws `EncryptionKeyNotFoundException`, later reads succeed. List ids that must work from the first request in `AdditionalDecryptionKeyIds` |
| Health | The keyed `IEncryptionKeyProviderProbe` (`FieldEncryptionServiceKeys.KeyRingProbe`) turns unhealthy when keys were not refreshed within `MaxKeyStaleness` (30 min); `AddFieldEncryptionReadinessCheck()` wires it |

### Key rotation protocol

1. Add the new key to the key source, not yet current. Wait at least `KeyRefreshInterval` (every process can now
   decrypt with it).
2. Make it current. Wait `KeyRefreshInterval` again (every process now encrypts with it).
3. Run the maintenance job with `ReEncrypt` and `ExpectedCurrentKeyId` = the new key.
4. Run `VerifyOnly`; retire the old key only when `report.IsSafeToRetire(oldKeyId)` is true.

**Blind-index key:** add the new version and make it current (lookups match every configured version), run
`RecomputeBlindIndexes`, confirm `VerifyOnly` reports `StaleBlindIndexes == 0`, then remove the old version.

## Maintenance job

`IEncryptionRotationJob` reads and writes every tenant's rows, so it requires a cross-tenant scope **the caller**
entered, in a scope whose `IRequestContext` identifies the job. It never enters the scope itself.

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
| --- | --- |
| `VerifyOnly` | Decrypts every value and counts it by key; writes nothing |
| `ReEncrypt` | Moves values not on the current key (or the tenant's data key) onto it |
| `RecomputeBlindIndexes` | Rewrites indexes under the current blind-index version and normalization |
| `EncryptPlaintext` | Encrypts values still stored as plaintext (a column that was just marked `.Encrypt`) |

The job walks every encrypted column in primary-key order in short transactions, with plain SQL (no query filter
hides soft-deleted or other tenants' rows, no interceptor stamps audit columns), and writes with a batched
compare-and-swap, so a value changed concurrently is left alone and counted. Cancelling returns a `CheckpointToken`
to resume. Run it from a hosted service, scheduled job or workflow activity — never from request handling (SK0303).

A stored plaintext value is never read silently: materializing it throws. To encrypt an existing column, deploy the
`.Encrypt(...)` model, then run `EncryptPlaintext` before serving reads.

**Row-level security.** Each maintenance transaction runs with `row_security = off`, so a policy that would hide rows
fails the run instead of letting it report completion over the visible rows only. Maintenance therefore needs a role
that bypasses row-level security: the cross-tenant data source is used automatically, or pass one with
`k.UseMaintenanceDataSource(sp => …)`. A role that sees every row through a role-specific policy instead needs
`RequireRowSecurityBypass = false`.

## Per-tenant data keys and crypto-shredding

With `UseTenantDataKeys()` every encrypted value of a tenanted entity is encrypted under **its tenant's own data
key**, generated and wrapped by the registered `IEnvelopeEncryptionProvider` (a KMS master key) and stored wrapped in
`sk_tenant_encryption_keys` (create it with `migrationBuilder.CreateTenantEncryptionKeyTable()`). Keys are created on
a tenant's first encrypted write; existing values move with `ReEncrypt`.

```csharp
using (crossTenantScope.Enter("GDPR erasure request 2026-114"))   // required, as for the maintenance job
{
    TenantShredResult result = await keys.ShredTenantAsync(tenantId, cancellationToken: ct); // ITenantEncryptionKeyManager
}
```

Shredding deletes the wrapped key (keeping a tombstone, so the tenant id never gets a new key) and clears the tenant's
blind indexes in the same transaction. Afterwards the tenant's values under its key cannot be decrypted by anyone:
reading or writing one throws `TenantKeyShreddedException` (`Persistence.Encryption.TenantKeyShredded`, NotFound).
Columns that are not encrypted are not touched — delete or anonymize them yourself.

**Only values under the tenant's key are erased.** Values written before `UseTenantDataKeys()` stay under a root key
until `ReEncrypt` moves them, and a column marked `.Encrypt()` late may still hold plaintext. `ShredTenantAsync`
therefore checks the tenant's rows first and, when any remain, throws `TenantShredIncompleteException`
(`Persistence.Encryption.TenantShredIncomplete`, Conflict, with the counts) and changes nothing. Run
`ReEncrypt | EncryptPlaintext`, then shred. To shred anyway, pass `new TenantShredOptions { AllowIncompleteErasure = true }`:
`result.IsComplete` is then `false` and `RootKeyValues`/`PlaintextValues` say how many remain.

| After a shred | Guarantee |
| --- | --- |
| EF Core writes in a transaction | Every save that encrypts a value of a tenant re-reads the tombstone and locks the tenant's key row (`FOR SHARE`): no value is written under the key by a transaction that commits after the shred |
| EF Core writes without a transaction | The tombstone is re-read just before EF Core's own transaction starts: a shred committing inside that window is missed for that one save |
| Reads | This process forgets the key at once; another process can still decrypt from its cache for up to `TenantKeyCacheDuration` (5 min) |
| Dapper, raw SQL | Not checked |

Wrapped keys in database backups keep the data recoverable until those backups expire or the master key is destroyed.
Record the erasure in your audit trail; the log entry deliberately carries no tenant id. Revoke `DELETE` on
`sk_tenant_encryption_keys` from the application roles so a tombstone cannot be removed (see the role script in
[SharedKernel.Persistence.Npgsql](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence/SharedKernel.Persistence.Npgsql#roles-the-one-canonical-script)).

## Security model

**Protects against:** reading personal data from backups, replicas, dumps, logs of SQL parameters and direct
database access; moving a ciphertext to another row, column or tenant; a query that would compare, sort or project an
encrypted column in SQL; recovering a shredded tenant's data without the master key.

**Does not protect against:** a compromised application process (it holds the keys), equality inference through
blind indexes (by design — index only high-cardinality values), or a writer that bypasses EF Core.

**Diagnostics.** Meter `SharedKernel.Persistence.EfCore.Encryption`: encrypt/decrypt failures by reason, maintenance
values written and skipped, tenant keys shredded. Logs use EventIds 6500–6699. No metric or log ever carries key
material, a value, a primary key or a tenant id.

## Pitfalls

| Symptom | Cause and fix |
| --- | --- |
| `InvalidOperationException`: query uses an encrypted member | LINQ filters, orders or projects an encrypted column. Use `WhereEncryptedEquals`, or load and filter in memory |
| `dotnet ef migrations add` fails with "never wired in" | The design-time factory lacks `ConfigurePersistence(p => p.UseFieldEncryption(...))` |
| Startup fails: no key provider | Register `IEncryptionKeyProvider` (Key Vault, `k.FromConfiguration()` or `k.UseKeyProvider<T>()`) |
| Materialization throws on an old row | The column still holds plaintext: run `EncryptPlaintext` |
| `TenantShredIncompleteException` | Values under a root key or in plaintext remain: run `ReEncrypt` and `EncryptPlaintext`, then shred |
| The maintenance job throws at once | No cross-tenant scope entered, or its role cannot bypass row-level security |

## AI quick reference

```text
REGISTER     .UseFieldEncryption(k => ...) on AddSharedKernelPostgres; key source = the registered
             IEncryptionKeyProvider, k.FromConfiguration() or k.UseKeyProvider<T>(); per-tenant keys:
             k.UseTenantDataKeys() + an IEnvelopeEncryptionProvider (KMS).
MARK         IEntityTypeConfiguration<T>: b.Property(x => x.P).Encrypt("area.entity.field")[.WithBlindIndex(
             BlindIndexNormalization.Trim | .CaseFold)]. string properties only; never attributes on domain types (SK0302).
QUERY        db.Set<T>().WhereEncryptedEquals(x => x.P, value). No Where/OrderBy/Select/GroupBy on an encrypted
             member; only == null / != null.
MIGRATIONS   Design-time factory ConfigurePersistence must call UseFieldEncryption(). Tenant keys:
             migrationBuilder.CreateTenantEncryptionKeyTable() + REVOKE DELETE, TRUNCATE ON sk_tenant_encryption_keys.
MAINTENANCE  In a background job: using (crossTenantScope.Enter("reason")) { await job.RunAsync(new
             EncryptionMaintenanceRequest { Mode = ReEncrypt | RecomputeBlindIndexes | EncryptPlaintext | VerifyOnly,
             ExpectedCurrentKeyId = "k2" }, null, ct); }. Retire a key only when report.IsSafeToRetire(id).
SHRED        using (crossTenantScope.Enter("reason")) await keys.ShredTenantAsync(tenantId, cancellationToken: ct);
             TenantShredIncompleteException -> migrate first. Reads afterwards throw TenantKeyShreddedException (404).
FORBIDDEN    Encrypted keys; ValueConverters for encryption; maintenance from request handling (SK0303);
             blind indexes on low-cardinality values; key material in appsettings checked into source control.
```

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · start at the
[persistence overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence).
