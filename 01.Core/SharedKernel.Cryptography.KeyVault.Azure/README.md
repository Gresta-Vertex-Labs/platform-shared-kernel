# SharedKernel.Cryptography.KeyVault.Azure

Azure Key Vault Keys implementation of [`SharedKernel.Cryptography`](../SharedKernel.Cryptography/README.md)'s `IEncryptionKeyProvider`, `IEnvelopeEncryptionProvider`, and (as of P-494/WO-081) `IAsymmetricKeyProvider` (remote sign/verify). The one `01.Core` package with a genuine third-party vendor SDK dependency (`Azure.Security.KeyVault.Keys` + `Azure.Identity`), kept out of the zero-third-party-dependency `SharedKernel.Cryptography` core — mirrors why `SharedKernel.Storage.S3`/`.Obs` are separate packages from `SharedKernel.Storage.Abstractions`.

## Included

| Type | Purpose |
|---|---|
| `AzureKeyVaultEncryptionKeyProvider` | Implements `IEncryptionKeyProvider` (direct retrieval), `IEnvelopeEncryptionProvider` (wrap/unwrap), and `IEncryptionKeyProviderProbe` (readiness) — see design note below |
| `AzureKeyVaultAsymmetricKeyProvider` | Implements `IAsymmetricKeyProvider` — **remote** RSA/ECDSA sign/verify against Azure Key Vault Keys; a deliberately separate class/singleton from `AzureKeyVaultEncryptionKeyProvider` — see "Remote Signing" below |
| `AzureKeyVaultCryptographyOptions` | `.VaultUri`, `.CurrentKeyId`, `.KeyNames` (keyId → Azure key name), `.Credential` (defaults to `DefaultAzureCredential`) — shared by both providers above |
| `AddSharedKernelAzureKeyVaultCryptography(configuration)` | Registers the options (validated); the encryption provider as all three of `IEncryptionKeyProvider`/`IEnvelopeEncryptionProvider`/`IEncryptionKeyProviderProbe` (same singleton instance); and the asymmetric provider as `IAsymmetricKeyProvider` (a **distinct** singleton) |

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

## Remote Signing (P-494/WO-081)

`AzureKeyVaultAsymmetricKeyProvider` implements `SharedKernel.Cryptography`'s `IAsymmetricKeyProvider` — it is registered by the same `AddSharedKernelAzureKeyVaultCryptography(configuration)` call above, reusing the identical `AzureKeyVaultCryptographyOptions.KeyNames` map (any entry may now double as a signing `keyId`, not just `CurrentKeyId`). `RsaSignatureService`/`EcdsaSignatureService` (`SharedKernel.Cryptography`) need **zero code changes** to consume it — `GetRsaKeyAsync`/`GetEcdsaKeyAsync` return a thin `RSA`/`ECDsa` subclass whose signing overrides delegate to Azure's genuine `CryptographyClient.Sign`/`Verify` calls.

```csharp
builder.Services.AddSharedKernelAzureKeyVaultCryptography(builder.Configuration);
builder.Services.AddSharedKernelCryptography(builder.Configuration); // RsaSignatureService/EcdsaSignatureService

// keyId "primary" (or any other SharedKernel:Cryptography:KeyVault:Azure:KeyNames entry) now
// resolves through AzureKeyVaultAsymmetricKeyProvider — no separate registration needed.
IAsymmetricSignatureService rsa = provider.GetRequiredKeyedService<IAsymmetricSignatureService>(
    CryptographyServiceCollectionExtensions.RsaSignatureServiceKey);
byte[] signature = await rsa.SignAsync(data, "primary", ct);
bool isValid = await rsa.VerifyAsync(data, signature, "primary", ct);
```

**Private key material never crosses the process boundary.** The returned `RSA`/`ECDsa` instance's `ExportParameters`/`ImportParameters` always throw `NotSupportedException` — this is a remote-signing handle, not a local key. Only the fixed algorithm combination `RsaSignatureService`/`EcdsaSignatureService` already use is supported (SHA-256+PSS → Azure's `PS256`; SHA-256 on P-256 → Azure's `ES256`); anything else throws `NotSupportedException` rather than silently guessing.

**Every sign/verify call is a real, unavoidable blocking network round trip to Key Vault** — `AzureKeyVaultAsymmetricKeyProvider` never implements `ISynchronousAsymmetricKeyProvider`, so `IAsymmetricSignatureService`'s retained synchronous `Sign`/`Verify` members throw `NotSupportedException` against it; always use `SignAsync`/`VerifyAsync`. Only *key resolution* is genuinely asynchronous (a BCL limitation: `RSA`/`ECDsa` expose no async `SignHash`/`VerifyHash`) — the cryptographic call itself still blocks the calling thread for the duration of the Key Vault round trip.

**Connection reuse is built in from the start**: one `CryptographyClient` per distinct Azure key name, cached in a `ConcurrentDictionary`, never constructed per call.

## Fail-closed

Every genuine Azure SDK exception (unreachable vault, `RequestFailedException` for permission/auth failure, a wrapped key the vault rejects as tampered) propagates directly from every member — never a silent fallback. The one narrow exception is `UnwrapDataKeyAsync`'s `Result<byte[]>` failure path, returned only when the supplied `masterKeyId` fails local well-formedness validation *before* any call ever reaches Azure.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview, including the "Azure Key Vault Key Provider" section with a complete worked example.
