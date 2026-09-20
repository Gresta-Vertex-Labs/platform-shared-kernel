# SharedKernel.Persistence.EfCore.Encryption

Field-level AES-256-GCM transparent encryption for EF Core 10 properties, built on `01.Core/SharedKernel.Cryptography`: `PropertyBuilder<T>.Encrypt()`, an HMAC-SHA256 blind index for equality lookups on encrypted columns without decrypting every row, and a rotation job for moving encrypted data onto a new key. An opt-in sibling of `SharedKernel.Persistence.EfCore` — never referenced unless a service calls `WithEncryption()`.

## Included types

- `EfCorePersistenceBuilderEncryptionExtensions.WithEncryption()` — opts in; two overloads (code-configured and `IConfiguration`-bound)
- `PropertyBuilderEncryptExtensions.Encrypt<TProperty>()` — marks a property (or, via the complex-type overload, a value object's own property) for transparent AES-256-GCM encryption; `perTenantKey: true` derives a per-tenant subkey
- `PropertyBuilderEncryptExtensions.WithBlindIndex<TProperty>()` — adds the HMAC-SHA256 shadow column an encrypted property needs for equality queries
- `EncryptedPropertyQueryExtensions.WhereBlindIndexEquals()` — the only sanctioned way to query an encrypted property by value
- `EncryptionInterceptor` — the `SaveChanges`-time encrypt-on-write / materialization-time decrypt-on-read interceptor
- `EncryptionModelConvention` — applies `EncryptedValueConverter<string>` wherever `.Encrypt()` was called
- `EncryptedColumnEqualityGuardInterceptor` — fails loudly if a query compares an encrypted column with anything other than `WhereBlindIndexEquals`
- `IBlindIndexService` — the HMAC-SHA256 blind-index computation seam
- `IEncryptionRotationJob` / `EncryptionRotationService<TContext>` — rotates already-encrypted rows onto a newly-added key, batch by batch
- `EncryptionKeyRingCache` — bounded in-memory cache over `01.Core`'s `IEncryptionKeyProvider`
- `EncryptionOptions` — the key ring / current-key-id configuration, validated at startup
- `EncryptionKeyNotFoundException` — thrown when a row's stored key id has no matching entry in the ring

## Install

```xml
<ProjectReference Include="..\SharedKernel.Persistence.EfCore.Encryption\SharedKernel.Persistence.EfCore.Encryption.csproj" />
```

Requires `01.Core/SharedKernel.Cryptography`'s `AddSharedKernelCryptography()` (and a registered `IEncryptionKeyProvider`) to have been called first.

## Quick start

```csharp
services.AddSharedKernelCryptography(configuration);           // IEncryptionKeyProvider, etc.
services.AddSharedKernelEfCore<AppDbContext>(options => ...)
    .WithEncryption(configuration)                              // section "SharedKernel:Persistence:Encryption"
    .Build();
```

```csharp
public sealed class CustomerConfiguration : EntityTypeConfigurationBase<Customer, CustomerId>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Email).HasMaxLength(255).Encrypt("customer.email").WithBlindIndex().IsRequired();
        builder.Property(c => c.Ssn).HasMaxLength(20).Encrypt("customer.ssn", perTenantKey: true).IsRequired();
    }
}

// Query by an encrypted value:
var customer = await context.Customers.WhereBlindIndexEquals(c => c.Email, "ada@example.com").SingleOrDefaultAsync();
```

## Rotating a key

```csharp
public sealed class RotateCustomerKeysJob(IEncryptionRotationJob rotationJob) : IEncryptionRotationJob
{
    public Task<EncryptionRotationReport> RotateAsync(string fromKeyId, string toKeyId, CancellationToken ct) =>
        rotationJob.RotateAsync(fromKeyId, toKeyId, ct);
}
```

Add the new key to `EncryptionOptions.Keys` before rotating; do not remove the old key until rotation completes.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts and implementation rules.
