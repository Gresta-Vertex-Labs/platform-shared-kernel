# SharedKernel.ServiceDefaults.Configuration.KeyVault

Azure Key Vault **secrets** as an additional `IConfiguration` source. One of the
`SharedKernel.ServiceDefaults.*` integration packages.

## Usage

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Configuration.KeyVault" />
```

```csharp
using SharedKernel.ServiceDefaults.Configuration;

builder.AddServiceDefaults();
builder.AddSharedKernelKeyVaultConfiguration(new Uri("https://my-vault.vault.azure.net/"));
```

Authenticates with `DefaultAzureCredential` unless you pass a `TokenCredential` as the second argument.
Opt-in — `AddServiceDefaults()` never calls it.

## Not to be confused with the key provider

| Package | Uses Key Vault for | Call |
| --- | --- | --- |
| **This package** | **Secrets**, read as configuration values | `AddSharedKernelKeyVaultConfiguration(vaultUri)` |
| `SharedKernel.ServiceDefaults.Cryptography.KeyVault` | **Keys**, as the platform's encryption-key provider | `AddSharedKernelKeyVaultKeyProvider()` |

A service may use either, both, or neither. They are separate packages so that a service using Key Vault
only for configuration does not restore `Azure.Security.KeyVault.Keys` and `SharedKernel.Cryptography`.

## Why a separate package

It brings `Azure.Extensions.AspNetCore.Configuration.Secrets` and `Azure.Identity`, which a service
reading configuration from anywhere else has no use for. It carries no SharedKernel dependency beyond the
composition base. The types keep their `SharedKernel.ServiceDefaults.Configuration` namespace from before
the WO-084 split, so moving to this package changes a `PackageReference` and no source.

The package name has no `.Azure` segment, unlike `SharedKernel.Cryptography.KeyVault.Azure`. "Key Vault"
already names the vendor, and with the segment this package's test assembly would be written to a
261-character path — past Windows' 260-character `MAX_PATH`. Measured: the compiler wrote no assembly and the build
failed with `MSB3030`.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition base
and the full list of integration packages.
