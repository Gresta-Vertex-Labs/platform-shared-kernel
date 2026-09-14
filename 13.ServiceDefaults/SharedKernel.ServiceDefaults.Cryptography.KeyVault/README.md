# SharedKernel.ServiceDefaults.Cryptography.KeyVault

Azure Key Vault **keys** as the platform's encryption-key provider, plus a readiness check for the vault.
One of the `SharedKernel.ServiceDefaults.*` integration packages.

## Usage

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Cryptography.KeyVault" />
```

```csharp
using SharedKernel.ServiceDefaults.Cryptography;
using SharedKernel.ServiceDefaults.HealthChecks;

builder.AddServiceDefaults();
builder.AddSharedKernelKeyVaultKeyProvider();

builder.Services.AddHealthChecks()
    .AddKeyVaultKeyProviderReadinessCheck();
```

Chain the readiness check onto `AddHealthChecks()`, **not** `AddSharedKernelHealthChecks()`.
`AddServiceDefaults()` already calls `AddSharedKernelHealthChecks()`; a second call registers the
`"startup"` check twice and the application throws `ArgumentException: Duplicate health checks were
registered with the name(s): startup` when it starts.

`AddSharedKernelKeyVaultKeyProvider()` calls through to `01.Core`'s
`SharedKernel.Cryptography.KeyVault.Azure`. Configure `AzureKeyVaultCryptographyOptions` under the
`SharedKernel:Cryptography:KeyVault:Azure` section — `VaultUri`, `CurrentKeyId`, `KeyNames`; see that
package's README for the full shape. It is idempotent: calling it more than once registers the provider
once.

## Key caching

By default — `cacheTtl` left `null` — `IEncryptionKeyProvider`, and only `IEncryptionKeyProvider`, is wrapped
in `01.Core`'s bounded-TTL `CachedEncryptionKeyProvider` with a 5-minute TTL. `IEnvelopeEncryptionProvider`
and `IEncryptionKeyProviderProbe` always stay on the raw, uncached provider: envelope wrap and unwrap is a
real per-call vault operation rather than a cacheable lookup, and a readiness probe must observe live vault
state.

```csharp
builder.AddSharedKernelKeyVaultKeyProvider();                                 // cached, 5-minute TTL
builder.AddSharedKernelKeyVaultKeyProvider(cacheTtl: TimeSpan.FromMinutes(10)); // custom TTL
builder.AddSharedKernelKeyVaultKeyProvider(cacheTtl: TimeSpan.Zero);          // uncached
```

The cache never unlocks a synchronous path. `CachedEncryptionKeyProvider` does not implement `01.Core`'s
`ISynchronousEncryptionKeyProvider` marker, so the synchronous `Encrypt`/`Decrypt`/`EncryptToString`/
`DecryptToString` members still throw `NotSupportedException` against it. The cache helps async callers
only.

Both variants are resolvable as their own concrete types, so a service using `06.Persistence`'s encryption
builder can target either explicitly:

```csharp
efCorePersistenceBuilder.WithExternalEncryptionKeyProvider<CachedEncryptionKeyProvider>();
// or
efCorePersistenceBuilder.WithExternalEncryptionKeyProvider<AzureKeyVaultEncryptionKeyProvider>();
```

## Rules

| Rule | Why |
| --- | --- |
| Registering the provider does **not** make persistence use it | `06.Persistence` requires its own explicit `.WithExternalEncryptionKeyProvider<TProvider>()`, so its `.WithEncryption()` and this method can never silently collide on the one unkeyed `IEncryptionKeyProvider` slot. |
| **Never** point `07.Messaging` payload encryption at this provider | Messaging's payload-encryption serializer is hard-synchronous with no async overload, so against a Key Vault–backed provider — cached or not — every message fails with `NotSupportedException`. Keep messaging payload encryption on a separately configured, config-backed provider, never the ambient slot this method registers. |
| Register the provider before the readiness check | `AddKeyVaultKeyProviderReadinessCheck()` resolves `IEncryptionKeyProviderProbe`, which `AddSharedKernelKeyVaultKeyProvider()` registers. |

## Readiness check

| | |
| --- | --- |
| Method | `AddKeyVaultKeyProviderReadinessCheck()` |
| Default name | `HealthCheckNames.EncryptionKeyProvider` (`"encryption-key-provider"`) |
| Tags | `ready`, `encryption-key-provider` — never `live` |
| On failure | `Unhealthy`, never `Degraded` — no fail-safe layer sits in front of key-provider connectivity |
| Probe | A cheap, non-cryptographic metadata read; never a wrap, unwrap, sign, or verify |

## Not to be confused with the configuration source

| Package | Uses Key Vault for | Call |
| --- | --- | --- |
| **This package** | **Keys**, as the encryption-key provider | `AddSharedKernelKeyVaultKeyProvider()` |
| `SharedKernel.ServiceDefaults.Configuration.KeyVault` | **Secrets**, read as configuration values | `AddSharedKernelKeyVaultConfiguration(vaultUri)` |

## Why a separate package

It brings `SharedKernel.Cryptography.KeyVault.Azure` and, with it, `Azure.Security.KeyVault.Keys`,
`Azure.Security.KeyVault.Secrets`, and `Azure.Identity`. The types keep their
`SharedKernel.ServiceDefaults.Cryptography` and `SharedKernel.ServiceDefaults.HealthChecks` namespaces
from before the WO-084 split, so moving to this package changes a `PackageReference` and no source.

The package name has no `.Azure` segment, unlike the `01.Core` package it wraps: "Key Vault" already names
the vendor, and the shorter name keeps this package's test-assembly path within the length the repository
already reaches elsewhere, well clear of Windows' 260-character `MAX_PATH`.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition base
and the full list of integration packages.
