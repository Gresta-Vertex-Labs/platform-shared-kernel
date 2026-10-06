# SharedKernel.Cryptography.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Doubles for `SharedKernel.Cryptography` that run the real algorithms and add call recording, in-test key
> rotation and failure simulation — nothing is stubbed to "always true", so a passing test proves the wiring.**

| You get | So that |
| --- | --- |
| `FakeSymmetricEncryptionService` — real AES-256-GCM with enforced associated data | A ciphertext that decrypts in the test decrypts in production, and a wrong context fails the same way |
| `FakeEncryptionKeyProvider` with `AddKey`/`SetCurrentKey`/`RemoveKey` | Key rotation and key retirement are tested in a few lines |
| `SimulateDecryptFailure`, `SimulateUnwrapFailure` | The failure branches return the real `cryptography.*` error codes |
| `FakeOneWayHasher` at 16 PBKDF2 iterations | Password tests stay fast and still exercise `SuccessRehashNeeded` |
| `FakeSecureRandomGenerator(seed: 42)` | Tokens and codes are reproducible across runs |
| `EncryptedPayloads`, `SignedPayloads`, `HashedContent` | You assert what was protected, with which key and context |
| `AddFakeCryptography()` | One call replaces every cryptography contract, even on top of the real registration |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Cryptography.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only**. Production code must never reference a Testing package;
`TestingNeverReferencedByProduction` fails the build's architecture tests when it does.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Cryptography`, `SharedKernel.Testing` (`FakeClock`) |
| Namespaces | `SharedKernel.Testing.Cryptography` |

## Quick start

```csharp
using System.Text;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Testing.Cryptography;
using Xunit;

public sealed class CustomerSecretTests
{
    [Fact]
    public async Task Old_ciphertext_still_decrypts_after_rotation_and_needs_re_encryption()
    {
        var keys = new FakeEncryptionKeyProvider("v1");
        var encryption = new FakeSymmetricEncryptionService(keys);
        byte[] context = Encoding.UTF8.GetBytes("customer:42");

        EncryptedPayload stored = await encryption.EncryptAsync(Encoding.UTF8.GetBytes("secret"), context);

        keys.AddKey("v2");
        keys.SetCurrentKey("v2");

        var decrypted = await encryption.DecryptAsync(stored, context);
        Assert.True(decrypted.IsSuccess);
        Assert.Equal("secret", Encoding.UTF8.GetString(decrypted.Value));
        Assert.False(await encryption.IsEncryptedWithCurrentKeyAsync(stored));
        Assert.Equal("v1", stored.KeyId);
    }
}
```

In a real host or `WebApplicationFactory`, replace the cryptography registrations:

```csharp
services.AddFakeCryptography();   // works after AddSharedKernelCryptography(...): earlier registrations are removed
```

## How it works

- **Real algorithms, not stubs.** Encryption delegates to the production `AesGcmEncryptionService` and
  `SynchronousAesGcmEncryptionService`; signing to the production signature service over real ECDSA/RSA keys;
  `FakeHmacSigner` computes HMAC-SHA256 (and enforces the same 32-byte minimum key length); `FakeContentHasher`
  computes SHA-256; `FakeOneWayHasher` is the production `OneWayHasher` and PHC format with PBKDF2. Payloads
  round-trip through the production parsers.
- **Simplified:** `FakeOneWayHasher` uses 16 PBKDF2 iterations — far below the production minimum. Keys are random
  in-memory bytes created per instance, so a ciphertext from one fake instance never decrypts with another.
  `FakeEnvelopeEncryptionProvider` wraps data keys with AES-256-GCM under an in-memory master key; unwrapping checks
  the master key id and authenticates the wrapped key, so tampering fails as in a real key service.
- **Async-only providers:** `FakeRemoteEncryptionKeyProvider` implements `IEncryptionKeyProvider` only and always
  completes asynchronously (it yields before answering), like a KMS. Use it to prove code does not depend on
  `ISynchronousEncryptionKeyProvider`.
- **Clock:** `FakeTotpReplayGuard` keeps the last accepted time step per identity until its retention expires on the
  injected `IClock` (a new `FakeClock` when none is given).
- **Lifetimes and threading:** `AddFakeCryptography()` registers singletons; every fake is thread-safe.

## Recipes

### 1. Test the "decryption failed" branch

```csharp
var encryption = new FakeSymmetricEncryptionService { SimulateDecryptFailure = true };
var payload = encryption.Encrypt("data"u8, context);

var result = encryption.Decrypt(payload, context);

Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);   // SharedKernel.Cryptography
```

`SimulateDecryptFailure` fails every decrypting member, including `ReEncrypt`/`ReEncryptAsync`.

### 2. Retire a key and prove old data is unreadable

```csharp
var keys = new FakeEncryptionKeyProvider("v1");
var encryption = new FakeSymmetricEncryptionService(keys);
var payload = encryption.Encrypt("data"u8, context);

keys.AddKey("v2");
keys.SetCurrentKey("v2");
keys.RemoveKey("v1");

Assert.Equal(CryptographyErrorCodes.UnknownKeyId, encryption.Decrypt(payload, context).Error.Code);
```

### 3. Exercise password rehashing

```csharp
var hasher = new FakeOneWayHasher();
string hash = hasher.Hash("correct horse");

hasher.Iterations = 32;

Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(hash, "correct horse"));
```

### 4. Get reproducible tokens

```csharp
var a = new FakeSecureRandomGenerator(seed: 42).GetToken();
var b = new FakeSecureRandomGenerator(seed: 42).GetToken();
Assert.Equal(a, b);
```

Without a seed the generator uses `RandomNumberGenerator`.

### 5. Prove code works with a KMS-style async provider

```csharp
var provider = new FakeRemoteEncryptionKeyProvider();
var encryption = new AesGcmEncryptionService(provider);   // the production service

await encryption.EncryptAsync("data"u8.ToArray(), context);
Assert.Equal(1, provider.CurrentKeyCallCount);
```

## Reference

### Registration

`AddFakeCryptography(this IServiceCollection)` removes any earlier registration of each contract, then registers
singletons:

| Contract | Resolves to |
| --- | --- |
| `IOneWayHasher` | `FakeOneWayHasher` |
| `ISecureRandomGenerator` | `FakeSecureRandomGenerator` (unseeded) |
| `IContentHasher` | `FakeContentHasher` |
| `IHmacSigner` | `FakeHmacSigner` |
| `IEncryptionKeyProvider`, `ISynchronousEncryptionKeyProvider` | one `FakeEncryptionKeyProvider` (also resolvable as itself) |
| `ISymmetricEncryptionService`, `ISynchronousSymmetricEncryptionService` | one `FakeSymmetricEncryptionService` over that key provider |
| `IEnvelopeEncryptionProvider` | `FakeEnvelopeEncryptionProvider` |
| `IEnvelopeEncryptionService` | the production `EnvelopeEncryptionService` over the fake provider |
| `ISigningKeyProvider` | one `FakeSigningKeyProvider` |
| `IAsymmetricSignatureService` | `FakeAsymmetricSignatureService` over that key provider |
| `ITotpReplayGuard` | `FakeTotpReplayGuard` (uses a registered `IClock`, else its own `FakeClock`) |

Resolve `FakeEncryptionKeyProvider` or `FakeSigningKeyProvider` from the container to rotate keys.
`FakeRemoteEncryptionKeyProvider` is not registered.

### Types

| Type | Implements | Inspection and control |
| --- | --- | --- |
| `FakeEncryptionKeyProvider(string currentKeyId = "v1")` | `IEncryptionKeyProvider`, `ISynchronousEncryptionKeyProvider` | `CurrentKeyId`, `AddKey(id)`, `SetCurrentKey(id)` (unknown id → `KeyNotFoundException`), `RemoveKey(id)` |
| `FakeRemoteEncryptionKeyProvider(string currentKeyId = "v1")` | `IEncryptionKeyProvider` | Same key methods; `CurrentKeyCallCount`, `KeyCallCount` |
| `FakeSymmetricEncryptionService(FakeEncryptionKeyProvider? keyProvider = null)` | `ISymmetricEncryptionService`, `ISynchronousSymmetricEncryptionService` | `KeyProvider`, `EncryptedPayloads` (`(Payload, AssociatedData)`), `SimulateDecryptFailure` |
| `FakeEnvelopeEncryptionProvider(string masterKeyId = "fake-master/v1")` | `IEnvelopeEncryptionProvider` | `MasterKeyId`, `GenerateCallCount`, `UnwrapCallCount`, `SimulateUnwrapFailure` |
| `FakeSigningKeyProvider` | `ISigningKeyProvider`, `IDisposable` | `AddKey(id, SignatureAlgorithm)`, `CreateKeysOnDemand` (default `true`), `DefaultAlgorithm` (default `ES256`) |
| `FakeAsymmetricSignatureService(FakeSigningKeyProvider? keyProvider = null)` | `IAsymmetricSignatureService` | `KeyProvider`, `SignedPayloads` (`(Data, KeyId)`; the in-memory `SignAsync` overload only, successful calls only) |
| `FakeHmacSigner` | `IHmacSigner` | `SignedPayloads`, `VerifiedPayloads` (`(Data, Key)`) |
| `FakeOneWayHasher` | `IOneWayHasher` | `Iterations` (default 16) |
| `FakeContentHasher` | `IContentHasher` | `HashedContent` (streams included) |
| `FakeSecureRandomGenerator(int? seed = null)` | `ISecureRandomGenerator` | Seeded instances are predictable by design |
| `FakeTotpReplayGuard(IClock? clock = null)` | `ITotpReplayGuard` | `Reset()` |

### Error codes the fakes return

| Code | Type | When |
| --- | --- | --- |
| `cryptography.decryption_failed` | Validation | `SimulateDecryptFailure`, wrong associated data, tampered ciphertext |
| `cryptography.unknown_key_id` | Unexpected | The payload's key was removed from the provider |
| `cryptography.data_key_unwrap_failed` | Validation | `SimulateUnwrapFailure`, a different master key id, or a tampered wrapped key |

Other failures (for example `cryptography.malformed_payload`) come from the production code the fakes run.

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Cryptography.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Foundation/SharedKernel.Cryptography.Testing/SharedKernel.Cryptography.Testing.Tests)
and prove each fake against the `SharedKernel.Cryptography` contracts, including composition with the production
services. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
for `FakeClock` (drive `FakeTotpReplayGuard` retention) and `TestRequestContext`. The Argon2id and Azure Key Vault
providers have no fakes; the doubles above stand in for the contracts they implement.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | 16 PBKDF2 iterations and seeded randomness are insecure by design |
| Encrypt with one `FakeSymmetricEncryptionService` and decrypt with a new one | Share one instance, or one `FakeEncryptionKeyProvider` | Each provider creates its own random key material |
| Assume `AddFakeCryptography()` keeps a real key provider you registered | Resolve `FakeEncryptionKeyProvider` from the container | Every cryptography contract registration is removed first |
| Assert `SignedPayloads` after signing a `Stream` | Assert on the signature, or sign `ReadOnlyMemory<byte>` | Only the in-memory overload is recorded |
| Hand a `FakeRemoteEncryptionKeyProvider` to `FakeSymmetricEncryptionService` | Use the production `AesGcmEncryptionService` over it | The fake service takes a `FakeEncryptionKeyProvider` only |
| Rely on real-world password hash cost in tests | Test cost settings against the production registration | The fake hasher is deliberately cheap |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
