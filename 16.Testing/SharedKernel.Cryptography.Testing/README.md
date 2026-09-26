# SharedKernel.Cryptography.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**Doubles for `SharedKernel.Cryptography` that run the real algorithms and add call recording, key rotation and
failure simulation.** A ciphertext produced by the fake decrypts with the fake, a signature verifies, a hash
verifies — nothing is stubbed to "always true", so a test that passes proves the wiring is right.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Cryptography.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Cryptography`.

## Contents

| Type | Stands in for | Notes |
| --- | --- | --- |
| `FakeEncryptionKeyProvider` | `IEncryptionKeyProvider`, `ISynchronousEncryptionKeyProvider` | In-memory AES keys; `AddKey(id)`, `SetCurrentKey(id)`, `RemoveKey(id)`, `CurrentKeyId` — rotate in a test |
| `FakeRemoteEncryptionKeyProvider` | `IEncryptionKeyProvider` (async only) | Like a KMS: no synchronous path; `KeyCallCount`, `CurrentKeyCallCount` |
| `FakeSymmetricEncryptionService` | `ISymmetricEncryptionService`, `ISynchronousSymmetricEncryptionService` | Real AES-256-GCM over the key provider; `EncryptedPayloads` (payload + associated data), `SimulateDecryptFailure` |
| `FakeEnvelopeEncryptionProvider` | `IEnvelopeEncryptionProvider` | Data keys wrapped by a fake master key; `GenerateCallCount`, `UnwrapCallCount`, `SimulateUnwrapFailure` |
| `FakeSigningKeyProvider` / `FakeAsymmetricSignatureService` | `ISigningKeyProvider` / `IAsymmetricSignatureService` | Real RSA/ECDSA keys created on demand (`CreateKeysOnDemand`, `DefaultAlgorithm`); `SignedPayloads` |
| `FakeHmacSigner` | `IHmacSigner` | Real HMAC-SHA256; `SignedPayloads`, `VerifiedPayloads` |
| `FakeOneWayHasher` | `IOneWayHasher` | Real PBKDF2 PHC hashing at 16 iterations so tests stay fast; raise `Iterations` mid-test to exercise `SuccessRehashNeeded` |
| `FakeContentHasher` | `IContentHasher` | Real SHA-256; `HashedContent` |
| `FakeSecureRandomGenerator` | `ISecureRandomGenerator` | Seedable (`new FakeSecureRandomGenerator(seed: 42)`) for reproducible tokens |
| `FakeTotpReplayGuard` | `ITotpReplayGuard` | Atomic last-accepted-time-step per identity; `Reset()` |

## Registration

```csharp
services.AddFakeCryptography();
```

Registers every fake above as a singleton, **removing earlier registrations of those contracts first**, so it works
on top of the service's real `AddSharedKernelCryptography(...)`. One `FakeEncryptionKeyProvider` backs both
encryption services and one `FakeSigningKeyProvider` backs signing; resolve them to rotate keys. The real
`EnvelopeEncryptionService` runs over `FakeEnvelopeEncryptionProvider`.

## Example

```csharp
var keys = new FakeEncryptionKeyProvider("v1");
var encryption = new FakeSymmetricEncryptionService(keys);
var vault = new CustomerSecretVault(encryption);

var stored = await vault.ProtectAsync("secret", customerId, ct);
keys.AddKey("v2");
keys.SetCurrentKey("v2");

(await vault.RevealAsync(stored, customerId, ct)).Value.Should().Be("secret");   // old key still decrypts
(await encryption.IsEncryptedWithCurrentKeyAsync(stored, ct)).Should().BeFalse(); // and needs re-encryption
```

## Related packages

- References `SharedKernel.Cryptography` and `SharedKernel.Testing` (`FakeClock`).
- The Argon2id and Azure Key Vault providers have no fakes; the fakes above stand in for the contracts they implement.
