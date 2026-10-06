# SharedKernel.ServiceDefaults.Configuration.KeyVault

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)

> **Azure Key Vault secrets as an `IConfiguration` source in one call — so connection strings, client secrets and API
> keys come from the vault instead of `appsettings.json`, and an unreachable vault stops the host at startup.**

| You get | So that |
| --- | --- |
| `builder.AddSharedKernelKeyVaultConfiguration(vaultUri)` | Vault secrets are ordinary configuration values, read by `AddValidatedOptions` like any other |
| `DefaultAzureCredential` by default | Managed identity in Azure, your developer login locally — no secret to bootstrap the secret store |
| An optional `TokenCredential` | Workload identity, a specific managed identity, or a test credential |
| Eager load | A wrong URI or missing permission fails at startup, not on the first configuration read |
| A package of its own | Only services that use Key Vault restore `Azure.Identity` and the Key Vault configuration provider |

## Install

```xml
<PackageReference Include="SharedKernel.ServiceDefaults.Configuration.KeyVault" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** / **Worker** project |
| Depends on | `SharedKernel.ServiceDefaults`, `Azure.Extensions.AspNetCore.Configuration.Secrets`, `Azure.Identity` |
| Namespaces | `SharedKernel.ServiceDefaults.Configuration` |

## Quick start

```csharp
using SharedKernel.ServiceDefaults.Configuration;
using SharedKernel.ServiceDefaults.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedKernelKeyVaultConfiguration(new Uri(builder.Configuration["KeyVault:Uri"]!));
builder.AddServiceDefaults();
// Everything registered after this reads vault secrets as configuration.
```

A secret named `ConnectionStrings--orders` becomes the configuration key `ConnectionStrings:orders`; a secret named
`SharedKernel--Communication--Clients--inventory--Authentication--ClientCredentials--ClientSecret` fills that client's
secret. (`--` is the Key Vault provider's section separator, since secret names cannot contain `:`.)

The identity the service runs as needs permission to list and read secrets (the *Key Vault Secrets User* role).

## How it works

- The call adds the vault through `AddAzureKeyVault(vaultUri, credential)` on the host's `ConfigurationManager`. A
  `ConfigurationManager` loads a source as soon as it is added, so the vault's secrets are enumerated during this call:
  a DNS failure, refused connection or authentication failure throws from it, before the host is built.
- It is added after the sources the builder already has (appsettings files, environment variables, command line), so
  a vault secret overrides an equally named key from those.
- It is opt-in: `AddServiceDefaults()` never calls it.

### Not to be confused with the key provider

| Package | Uses Key Vault for | Call |
| --- | --- | --- |
| **This package** | **Secrets**, read as configuration values | `builder.AddSharedKernelKeyVaultConfiguration(vaultUri)` |
| [`SharedKernel.Cryptography.KeyVault.Azure`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Foundation/SharedKernel.Cryptography.KeyVault.Azure/README.md) | **Keys**, as the encryption and signing key provider | `AddSharedKernelCryptography(configuration).AddAzureKeyVaultEncryption(configuration)` |

A service may use either, both, or neither. The key provider registers its own `encryption-key-provider` readiness
probe; this package registers none — a vault that fails is caught at startup instead.

## Recipes

### 1. Use a specific managed identity

```csharp
using Azure.Identity;

builder.AddSharedKernelKeyVaultConfiguration(
    vaultUri,
    new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId)));
```

### 2. Skip the vault in local development

```csharp
if (!builder.Environment.IsDevelopment())
{
    builder.AddSharedKernelKeyVaultConfiguration(vaultUri);
}
```

Use user secrets (`dotnet user-secrets`) for the same keys locally.

## Reference

| Method | Does |
| --- | --- |
| `IHostApplicationBuilder.AddSharedKernelKeyVaultConfiguration(Uri vaultUri, TokenCredential? credential = null)` | Adds Key Vault as a configuration source; `DefaultAzureCredential` when `credential` is `null`. Throws `ArgumentNullException` for a null builder or URI, and the Azure SDK's exception when the vault cannot be read |
| `IHostApplicationBuilder.AddSharedKernelKeyVaultConfiguration(Uri vaultUri, TokenCredential? credential, SecretClientOptions clientOptions)` | The same, reading the vault with a `SecretClient` built from `clientOptions` (transport, retries, diagnostics). Use it to reach a local emulator such as Lowkey Vault: trust its TLS certificate through `Transport` and set `DisableChallengeResourceVerification = true` |

The package logs nothing of its own and registers no readiness probe.

## Testing

There is no fake: in tests, do not call `AddSharedKernelKeyVaultConfiguration` — supply the same keys with
`builder.Configuration.AddInMemoryCollection(...)` or through `WebApplicationFactory<Program>`'s
`ConfigureAppConfiguration`. Guard the call on an environment or configuration flag (Recipe 2) so the test host
never reaches a vault.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Call it after services read their options | Call it before `AddServiceDefaults()` and every `Add…(configuration)` that binds options | Values read before the source is added never see the vault |
| Name secrets with `:` | Use `--` as the section separator | Key Vault secret names cannot contain `:` |
| Grant the service identity *Key Vault Administrator* | Grant *Key Vault Secrets User* on that vault | It only needs to list and read secrets |
| Use this package for encryption keys | Use `SharedKernel.Cryptography.KeyVault.Azure` | Keys never leave the vault there; here secrets become plain configuration |
| Hard-code the vault URI | Read it from configuration or an environment variable | The URI differs per environment |

## Design decisions

**Why fail at startup?** A service that starts without its secrets fails later and less clearly — a first request with
an empty connection string. Loading eagerly turns that into a startup error the orchestrator reports.

**Why no `.Azure` segment in the name?** "Key Vault" already names the vendor, and with the segment the test assembly
path would exceed Windows' 260-character `MAX_PATH`.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[ServiceDefaults domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/ServiceDefaults/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
