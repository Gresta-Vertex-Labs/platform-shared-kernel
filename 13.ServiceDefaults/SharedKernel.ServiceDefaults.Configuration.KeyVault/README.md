# SharedKernel.ServiceDefaults.Configuration.KeyVault

Azure Key Vault **secrets** as an additional `IConfiguration` source. One of the
`SharedKernel.ServiceDefaults.*` integration packages. **Tier:** Host.

## Usage

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Configuration.KeyVault" />
```

Versions come from the consumer's single `SharedKernelVersion` property.

```csharp
using SharedKernel.ServiceDefaults.Configuration;

builder.AddSharedKernelKeyVaultConfiguration(new Uri("https://my-vault.vault.azure.net/"));
builder.AddServiceDefaults();
```

Authenticates with `DefaultAzureCredential` unless you pass a `TokenCredential` as the second argument.
Opt-in — `AddServiceDefaults()` never calls it. `ConfigurationManager` loads the source eagerly, so an
unreachable vault throws from this call at startup rather than on the first configuration read.

## Not to be confused with the key provider

| Package | Uses Key Vault for | Call |
| --- | --- | --- |
| **This package** | **Secrets**, read as configuration values | `builder.AddSharedKernelKeyVaultConfiguration(vaultUri)` |
| `SharedKernel.Cryptography.KeyVault.Azure` (`01.Core`) | **Keys**, as the platform's encryption and signing key provider | `AddSharedKernelCryptography(configuration).AddAzureKeyVaultEncryption(configuration)` / `.AddAzureKeyVaultSigning(configuration)` |

A service may use either, both, or neither. The key provider registers its own `encryption-key-provider`
`IReadinessProbe`, which `AddSharedKernelReadiness()` (composition base) maps to a `ready` health check —
no ServiceDefaults package is involved.

## Why a separate package

It brings `Azure.Extensions.AspNetCore.Configuration.Secrets` and `Azure.Identity`, which a service
reading configuration from anywhere else has no use for. It carries no SharedKernel dependency beyond the
composition base.

The package name has no `.Azure` segment, unlike `SharedKernel.Cryptography.KeyVault.Azure`. "Key Vault"
already names the vendor, and with the segment this package's test assembly would be written to a
261-character path — past Windows' 260-character `MAX_PATH`.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition base
and the full list of integration packages.
