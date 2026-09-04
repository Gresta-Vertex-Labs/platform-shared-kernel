# SharedKernel.Cryptography.KeyVault.Azure

Azure Key Vault Keys implementation of [`SharedKernel.Cryptography`](../SharedKernel.Cryptography/README.md)'s `IEncryptionKeyProvider` and `IEnvelopeEncryptionProvider`. The one `01.Core` package with a genuine third-party vendor SDK dependency (`Azure.Security.KeyVault.Keys` + `Azure.Identity`), kept out of the zero-third-party-dependency `SharedKernel.Cryptography` core — mirrors why `SharedKernel.Storage.S3`/`.Obs` are separate packages from `SharedKernel.Storage.Abstractions`.

## Included

| Type | Purpose |
|---|---|
| `AzureKeyVaultEncryptionKeyProvider` | Implements `IEncryptionKeyProvider` (direct retrieval), `IEnvelopeEncryptionProvider` (wrap/unwrap), and `IEncryptionKeyProviderProbe` (readiness) — see design note below |
| `AzureKeyVaultCryptographyOptions` | `.VaultUri`, `.CurrentKeyId`, `.KeyNames` (keyId → Azure key name), `.Credential` (defaults to `DefaultAzureCredential`) |
| `AddSharedKernelAzureKeyVaultCryptography(configuration)` | Registers the options (validated) and the provider as all three service types — same singleton instance |

## Readiness probe

`AzureKeyVaultEncryptionKeyProvider` implements `SharedKernel.Cryptography`'s `IEncryptionKeyProviderProbe`. `ProbeAsync` performs exactly one read-only, non-cryptographic call — a key-metadata lookup, never a wrap/unwrap/sign/verify — and never throws for an ordinary reachability failure: it reports `EncryptionKeyProviderHealth.IsHealthy = false` with a `Description` instead. This is a deliberate, narrow carve-out from every other member of this class, all of which fail closed via a thrown exception — see `ProbeAsync`'s own XML docs. `01.Core` ships this probe primitive only; wiring it into `AddHealthChecks()` is `13.ServiceDefaults`'s concern.

```csharp
IEncryptionKeyProviderProbe probe = provider.GetRequiredService<IEncryptionKeyProviderProbe>();
EncryptionKeyProviderHealth health = await probe.ProbeAsync();
// health.IsHealthy / health.Description
```

## Design note: direct retrieval is built on envelope wrapping, not a second code path

Azure Key Vault Keys does not export raw HSM-protected key material by default — the vendor-idiomatic operation is `CryptographyClient.WrapKeyAsync`/`UnwrapKeyAsync`, exactly `IEnvelopeEncryptionProvider`'s shape. `GetCurrentKeyAsync` therefore generates (or returns a process-lifetime-cached) local AES-256 data key via `GenerateDataKeyAsync`, exposing only the already-in-memory plaintext data key as `CryptographicKey.Material`. The vault's own master key material never crosses the process boundary either way. See `AzureKeyVaultEncryptionKeyProvider`'s XML docs for the full reasoning — this is a deliberate design decision, not an implementation shortcut, and must never be "fixed" into two divergent code paths.

## Quick Start

```csharp
// appsettings.json
// {
//   "SharedKernel": { "Cryptography": { "KeyVault": { "Azure": {
//     "VaultUri": "https://my-vault.vault.azure.net/",
//     "CurrentKeyId": "primary",
//     "KeyNames": { "primary": "tenant-data-key" }
//   } } } }
// }

builder.Services.AddSharedKernelAzureKeyVaultCryptography(builder.Configuration);

// Optional: bounded-TTL caching, composed externally — this package ships none of its own.
builder.Services.AddSingleton<IEncryptionKeyProvider>(sp =>
    new CachedEncryptionKeyProvider(
        sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>(),
        TimeProvider.System,
        TimeSpan.FromMinutes(5)));
```

## Fail-closed

Every genuine Azure SDK exception (unreachable vault, `RequestFailedException` for permission/auth failure, a wrapped key the vault rejects as tampered) propagates directly from every member — never a silent fallback. The one narrow exception is `UnwrapDataKeyAsync`'s `Result<byte[]>` failure path, returned only when the supplied `masterKeyId` fails local well-formedness validation *before* any call ever reaches Azure.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview, including the "Azure Key Vault Key Provider" section with a complete worked example.
