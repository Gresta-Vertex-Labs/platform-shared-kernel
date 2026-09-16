# SharedKernel.Cryptography.KeyVault.Azure

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**Azure Key Vault keys for [`SharedKernel.Cryptography`](../SharedKernel.Cryptography/README.md): encryption keys
wrapped by a Key Vault master key, and signing keys that never leave Key Vault.**

| You get | So that |
| --- | --- |
| AES-256 data keys wrapped by a Key Vault key and stored as secret versions | Every replica shares the same current key, and rotation is one call |
| In-memory caching with bounded, rate-limited lookups | Encryption does not call Key Vault, and forged key ids cannot flood it |
| Envelope encryption restricted to configured master keys | A payload cannot make the service unwrap with an arbitrary key |
| Remote asynchronous signing with local verification | Private keys stay in Key Vault, and verification makes no network call |
| A readiness probe | Kubernetes stops routing traffic when the vault is unreachable |

## Install

```shell
dotnet add package SharedKernel.Cryptography.KeyVault.Azure
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Dependencies | `SharedKernel.Cryptography`, `SharedKernel.Configuration`, `Azure.Security.KeyVault.Keys`, `Azure.Security.KeyVault.Secrets`, `Azure.Identity` |

## Quick start

```csharp
builder.Services.AddSingleton<TokenCredential>(new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));

builder.Services.AddSharedKernelCryptography(builder.Configuration)
    .AddAzureKeyVaultEncryption(builder.Configuration)   // IEncryptionKeyProvider, IEnvelopeEncryptionProvider, probe
    .AddSymmetricEncryption()
    .AddEnvelopeEncryption()
    .AddAzureKeyVaultSigning(builder.Configuration)      // ISigningKeyProvider
    .AddAsymmetricSigning();
```

```json
{
  "SharedKernel": {
    "Cryptography": {
      "KeyVault": {
        "Azure": {
          "Encryption": {
            "VaultUri": "https://contoso-prod.vault.azure.net/",
            "MasterKeyName": "orders-kek",
            "DataKeySecretName": "orders-data-keys",
            "RefreshInterval": "00:05:00"
          },
          "Signing": {
            "VaultUri": "https://contoso-prod.vault.azure.net/",
            "Keys": {
              "receipts": { "KeyName": "receipt-signing", "Algorithm": "ES256" }
            }
          }
        }
      }
    }
  }
}
```

Provision the first data key once, from a deployment job or an admin endpoint:

```csharp
string keyId = await provider.RotateDataKeyAsync(ct);   // AzureKeyVaultEncryptionKeyProvider
```

## Encryption keys

| Topic | Behaviour |
| --- | --- |
| Storage | Each version of the secret `DataKeySecretName` holds one data key wrapped by `MasterKeyName`, as JSON. The secret version id is the key id in every payload. |
| Current key | The newest enabled version |
| Rotation | `RotateDataKeyAsync` adds a version. Concurrent rotations add separate versions; nothing is overwritten. Other replicas switch within `RefreshInterval`. |
| Retirement | Disable a secret version once no payload uses it (re-encrypt first with `ReEncryptAsync`) |
| Caching | The version list is read once per `RefreshInterval`; each data key is unwrapped once and kept in memory. Do not add `CachedEncryptionKeyProvider`. |
| Untrusted key ids | Ids that are not 32 lowercase hex characters return `null` immediately. Unknown versions force a fresh version list at most once every ten seconds. |
| Master key | RSA (RSA-OAEP-256) or, in a managed HSM, AES (A256KW). Moving to a new master key: set `MasterKeyName` to it and list the old name in `PreviousMasterKeyNames`. |
| Envelope unwrap | Only enabled versions of `MasterKeyName` and `PreviousMasterKeyNames` are used. Each key's versions are listed once per `RefreshInterval`; an unknown version forces a re-list at most once every ten seconds. Any other master key id or version returns `cryptography.data_key_unwrap_failed` without a cryptographic call, as does a wrapped key Key Vault rejects. |
| Synchronous code | This provider is asynchronous. EF Core converters and serializers need an in-memory `ISynchronousEncryptionKeyProvider` loaded from it ahead of time. |

### Permissions

| Identity needs | On |
| --- | --- |
| `keys/get`, `keys/list`, `keys/wrapKey`, `keys/unwrapKey` | The master key, and each name in `PreviousMasterKeyNames` (Key Vault Crypto User covers these) |
| `secrets/get`, `secrets/list`, `secrets/set` | The data key secret (`set` only for the identity that rotates) |

## Signing keys

| Topic | Behaviour |
| --- | --- |
| Lookup | Only key ids under `Signing:Keys` are resolved; any other id returns `null` without a call |
| Validation | The Key Vault key must match the configured algorithm: RSA ≥ 2048 bits for `PS*`/`RS*`, the matching curve for `ES*`. A mismatch throws `InvalidOperationException`. |
| Signing | Asynchronous Key Vault `sign` over the locally computed digest |
| Verification | Local, with the cached public key; no network call |
| Versions | Without `KeyVersion` the latest version is used and refreshed every `RefreshInterval`. Pin `KeyVersion` when signatures must stay verifiable after a rotation, and give each version its own key id. |

Permissions: `keys/get` and `keys/sign` on each signing key (Key Vault Crypto User).

## Authentication

Both registrations use the `TokenCredential` registered in the container, or a shared `DefaultAzureCredential` when
none is. In production register a specific credential, such as `ManagedIdentityCredential` or
`WorkloadIdentityCredential`, before calling them: it starts faster and cannot pick up a developer's local identity.

## Readiness

`AzureKeyVaultEncryptionKeyProvider` implements `IEncryptionKeyProviderProbe` with a key metadata read, never a
cryptographic operation. Failures are reported as unhealthy with the HTTP status or exception type only, never the
exception message. `SharedKernel.ServiceDefaults.Cryptography.KeyVault` wires it into health checks.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Call `GetCurrentKeyAsync` before provisioning | Call `RotateDataKeyAsync` once at deployment | With no version it throws `InvalidOperationException` |
| Delete a data key secret version | Disable it, after re-encrypting | Deleting loses the key permanently |
| Remove an old master key name right after moving | Keep it in `PreviousMasterKeyNames` until data keys are re-wrapped | Its data keys can no longer be unwrapped |
| Use an RSA master key for envelopes written by untrusted parties | Use a managed-HSM AES key, or sign payloads | Anyone with the public key can wrap a data key |
| Rely on `DefaultAzureCredential` in production | Register a specific `TokenCredential` | It probes several sources and can use the wrong identity |
| Rotate a signing key in Key Vault while old signatures must verify | Pin `KeyVersion` and add the new version under a new key id | Verification uses the configured version |

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`.
- **Master and signing keys never leave Key Vault.** Only data keys, generated locally, are unwrapped into memory.
- **Fail closed.** An unreachable vault, missing permission or corrupt data key record throws; only the readiness
  probe reports failures as data.
