# SharedKernel.ServiceDefaults.Cryptography.KeyVault

Azure Key Vault **keys** as the platform's encryption-key provider, plus a readiness check for the vault.
One of the `SharedKernel.ServiceDefaults.*` integration packages.

## Usage

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Cryptography.KeyVault" />
```

```csharp
using SharedKernel.Cryptography.Extensions;
using SharedKernel.ServiceDefaults.Cryptography;
using SharedKernel.ServiceDefaults.HealthChecks;

builder.AddServiceDefaults();
builder.AddSharedKernelKeyVaultKeyProvider()
    .AddSymmetricEncryption()     // ISymmetricEncryptionService over the Key Vault data keys
    .AddEnvelopeEncryption();     // IEnvelopeEncryptionService, a fresh wrapped data key per payload

builder.Services.AddHealthChecks()
    .AddKeyVaultKeyProviderReadinessCheck();
```

Chain the readiness check onto `AddHealthChecks()`, **not** `AddSharedKernelHealthChecks()`.
`AddServiceDefaults()` already calls `AddSharedKernelHealthChecks()`; a second call registers the
`"startup"` check twice and the application throws `ArgumentException: Duplicate health checks were
registered with the name(s): startup` when it starts.

`AddSharedKernelKeyVaultKeyProvider()` is a host-builder shorthand for
`builder.Services.AddSharedKernelCryptography(builder.Configuration).AddAzureKeyVaultEncryption(builder.Configuration)`
from `01.Core`'s `SharedKernel.Cryptography` and `SharedKernel.Cryptography.KeyVault.Azure`. It returns the
`ICryptographyBuilder`, so chain the services that consume the provider onto it. It registers
`AzureKeyVaultEncryptionKeyProvider` as `IEncryptionKeyProvider`, `IEnvelopeEncryptionProvider` and
`IEncryptionKeyProviderProbe` — one singleton behind all three. Every registration uses `TryAdd`, so calling it
more than once registers the provider once.

Configure `AzureKeyVaultEncryptionOptions` under `SharedKernel:Cryptography:KeyVault:Azure:Encryption`:

```json
{
  "SharedKernel": { "Cryptography": { "KeyVault": { "Azure": { "Encryption": {
    "VaultUri": "https://contoso-prod.vault.azure.net/",
    "MasterKeyName": "orders-kek",
    "DataKeySecretName": "orders-data-keys",
    "RefreshInterval": "00:05:00"
  } } } } }
}
```

See the `SharedKernel.Cryptography.KeyVault.Azure` README for the key and secret layout and for rotation.

## Key caching

The provider caches by itself: it reads the list of data-key versions at most once per `RefreshInterval` and
unwraps each data key once. Do **not** wrap it in `CachedEncryptionKeyProvider`. The readiness probe is the same
singleton and always reads live vault state.

The provider is asynchronous only — it does not implement `ISynchronousEncryptionKeyProvider`. Synchronous code
paths (EF Core value converters, message serializers) cannot use it: `ISynchronousSymmetricEncryptionService`
fails to resolve against it instead of blocking a thread. Give those paths their own synchronous provider.

## Rules

| Rule | Why |
| --- | --- |
| Registering the provider does **not** make persistence use it | `06.Persistence`'s encryption is configured in its own builder chain, so it and this method can never silently collide on the one unkeyed `IEncryptionKeyProvider` slot. |
| **Never** point synchronous encryption — `06.Persistence` value converters, `07.Messaging` payload serializers — at this provider | Those paths are synchronous and need an `ISynchronousEncryptionKeyProvider`, which a Key Vault–backed provider never is. Keep them on a separately configured provider. |
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
