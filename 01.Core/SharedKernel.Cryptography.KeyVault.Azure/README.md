# SharedKernel.Cryptography.KeyVault.Azure

Azure Key Vault Keys implementation of [`SharedKernel.Cryptography`](../SharedKernel.Cryptography/README.md)'s `IEncryptionKeyProvider`, `IEnvelopeEncryptionProvider`, and (as of P-494/WO-081) `IAsymmetricKeyProvider` (remote sign/verify). The one `01.Core` package with a genuine third-party vendor SDK dependency — `Azure.Security.KeyVault.Keys` + `Azure.Security.KeyVault.Secrets` (added P-496/WO-081, for the durable version registry below) + `Azure.Identity` — kept out of the zero-third-party-dependency `SharedKernel.Cryptography` core — mirrors why `SharedKernel.Storage.S3`/`.Obs` are separate packages from `SharedKernel.Storage.Abstractions`.

## Included

| Type | Purpose |
|---|---|
| `AzureKeyVaultEncryptionKeyProvider` | Implements `IEncryptionKeyProvider` (direct retrieval), `IEnvelopeEncryptionProvider` (wrap/unwrap), `IEncryptionKeyProviderProbe` (readiness), and `MintNewVersionAsync` (explicit rotation, P-496/WO-081) — see design note and "Key rotation" below |
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

Azure Key Vault Keys does not export raw HSM-protected key material by default — the vendor-idiomatic operation is `CryptographyClient.WrapKeyAsync`/`UnwrapKeyAsync`, exactly `IEnvelopeEncryptionProvider`'s shape. `GetCurrentKeyAsync`/`GetKeyAsync` are both built atop `GenerateDataKeyAsync`/`UnwrapDataKeyAsync`, exposing only the already-in-memory plaintext data key as `CryptographicKey.Material`. The vault's own master key material never crosses the process boundary either way. See `AzureKeyVaultEncryptionKeyProvider`'s XML docs for the full reasoning — this is a deliberate design decision, not an implementation shortcut, and must never be "fixed" into two divergent code paths.

## Key rotation and the durable version registry (P-496/WO-081)

Prior to P-496, `GetCurrentKeyAsync` cached a single locally-generated data key for the lifetime of the process — meaning every process/pod/replica of a service silently minted its **own** unique AES-256 data key on first use, with no sharing across replicas. `AzureKeyVaultEncryptionKeyProvider` now maintains a durable, **Key-Vault-Secrets-backed** version registry instead: every version is stored as its own Key Vault Secret (a short opaque tag, `"v1"`, `"v2"`, …), and a single shared `"current version"` pointer secret is read live by every replica — so "current" is a genuinely shared, deliberately-minted concept, never a per-process accident. `CryptographicKey.Id` is now that short tag rather than the previous self-decodable ~470-byte envelope.

```csharp
// Mint the FIRST version once, during initial provisioning — before any traffic reaches this
// service. GetCurrentKeyAsync deliberately never auto-mints one (see its own XML docs for why).
AzureKeyVaultEncryptionKeyProvider provider = serviceProvider.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>();
string firstVersionTag = await provider.MintNewVersionAsync();

// ... later, from an ops script / hosted job / future 19.Scheduling job — never automatic:
string newVersionTag = await provider.MintNewVersionAsync();
// Every previously-minted version (including the one just superseded) remains resolvable via
// GetKeyAsync(oldTag) indefinitely — MintNewVersionAsync never deletes or overwrites anything.
```

**Rotation is deliberately manual, not automatic.** `MintNewVersionAsync` makes rotation possible and cheap to call — it is never scheduled or policy-driven by this package. Automatic/crypto-period-enforced rotation is out of scope here; that is closer to `19.Scheduling` territory, mirroring how `SharedKernel.DataPrivacy` left its cross-service erasure orchestrator out of scope. `MintNewVersionAsync` is **not** part of `IEncryptionKeyProvider`/`IEnvelopeEncryptionProvider` — it is a provider-specific operational method, resolved by depending on the concrete `AzureKeyVaultEncryptionKeyProvider` type (or your own thin wrapper around it), mirroring `06.Persistence`'s `IEncryptionRotationJob` precedent of leaving scheduling to the caller.

**Backward-read compatibility, no forced data migration.** `GetKeyAsync` first tries the new short-tag registry; if the supplied `keyId` does not match that shape, it falls back to parsing the legacy pre-P-496 self-decodable envelope — any row already encrypted under the old shape stays decryptable indefinitely. This is why P-496 ships as an additive/MINOR repack, not a breaking change.

**A bounded `CachedEncryptionKeyProvider` working set.** Because every replica now converges on the same small, deliberately-minted set of live version tags (rather than one unique tag per pod restart over a service's entire operational history), wrapping this provider in `SharedKernel.Cryptography`'s `CachedEncryptionKeyProvider` now has a genuinely bounded cache size.

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
