# SharedKernel.Cryptography

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Third-party dependencies: 0](https://img.shields.io/badge/third--party%20dependencies-0-brightgreen)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**Encryption, signing, password hashing, secure randomness and one-time passwords for .NET services, built only on
the .NET base class library.**

Every primitive picks a safe algorithm for you and hides the parameters that are easy to get wrong: nonces, salts,
iteration counts, padding, digest sizes and comparison timing. Keys come from providers you register, so the same
code runs against in-memory keys in tests and a key management service in production.

| You get | So that |
| --- | --- |
| AES-256-GCM with required associated data | A ciphertext cannot be decrypted after being altered or copied into another row, tenant or message |
| Async and synchronous encryption services over key providers | Key management services never block a thread, and EF Core converters still work |
| Key rotation helpers | Old payloads keep decrypting and a background job moves them to the current key |
| Envelope encryption and HKDF subkeys | One leaked data key or subkey exposes one value or one purpose, not everything |
| Signing keys that carry their algorithm | A signature can never be checked with an algorithm the key was not issued for |
| PHC-format password hashes with pepper and rehash-on-verify | Costs, algorithms and peppers change without a migration |
| Fixed-time comparison and secure random values | Secrets are not leaked through timing or predictable randomness |
| RFC 4226/6238 HOTP and TOTP with replay protection | A one-time code is accepted once, and never from a wrong or older time step |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Which type do I need?](#which-type-do-i-need)
- [Reference](#reference)
  - [Registration](#registration)
  - [Symmetric encryption](#symmetric-encryption)
  - [Key providers](#key-providers)
  - [Key rotation](#key-rotation)
  - [Envelope encryption](#envelope-encryption)
  - [Subkey derivation](#subkey-derivation)
  - [Signing](#signing)
  - [HMAC](#hmac)
  - [Password and secret hashing](#password-and-secret-hashing)
  - [Content hashing](#content-hashing)
  - [Fixed-time comparison](#fixed-time-comparison)
  - [Secure random values](#secure-random-values)
  - [One-time passwords](#one-time-passwords)
  - [Error codes](#error-codes)
  - [Configuration](#configuration)
- [Pitfalls](#pitfalls)
- [AI quick reference](#ai-quick-reference)
- [FIPS 140-3](#fips-140-3)
- [Compatibility and guarantees](#compatibility-and-guarantees)
- [Deliberately not included](#deliberately-not-included)

## Install

```shell
dotnet add package SharedKernel.Cryptography
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Dependencies | `SharedKernel.Primitives`, `SharedKernel.Configuration` |
| Optional companions | [`SharedKernel.Cryptography.Argon2`](../SharedKernel.Cryptography.Argon2/README.md) (Argon2id hashing), [`SharedKernel.Cryptography.KeyVault.Azure`](../SharedKernel.Cryptography.KeyVault.Azure/README.md) (Azure Key Vault keys) |

## Quick start

```csharp
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Symmetric;

// Program.cs: keys loaded at startup from a secret store.
builder.Services.AddSingleton(new StaticEncryptionKeyProvider(
    currentKeyId: "2026-09",
    [new CryptographicKey("2026-09", Convert.FromBase64String(builder.Configuration["Keys:2026-09"]!))]));
builder.Services.AddSingleton<IEncryptionKeyProvider>(sp => sp.GetRequiredService<StaticEncryptionKeyProvider>());

builder.Services.AddSharedKernelCryptography(builder.Configuration)
    .AddSymmetricEncryption();
```

```csharp
public sealed class CustomerNotes(ISymmetricEncryptionService encryption)
{
    public ValueTask<string> ProtectAsync(Guid customerId, string note, CancellationToken ct) =>
        // Bind the ciphertext to its owner: it will not decrypt for any other customer.
        encryption.EncryptToStringAsync(note, customerId.ToByteArray(), ct);

    public async Task<string?> RevealAsync(Guid customerId, string stored, CancellationToken ct)
    {
        Result<string> note = await encryption.DecryptToStringAsync(stored, customerId.ToByteArray(), ct);
        return note.IsSuccess ? note.Value : null;
    }
}
```

## Which type do I need?

| I need to… | Use |
| --- | --- |
| Encrypt a value I will decrypt later | `ISymmetricEncryptionService` (async) or `ISynchronousSymmetricEncryptionService` (EF Core converters, serializers) |
| Encrypt large files, one key per file | `IEnvelopeEncryptionService` |
| Separate keys per tenant or feature from one root key | `SubkeyDerivation`, or `provider.ForPurpose(...)` |
| Prove data came from me, verifiable by anyone with my public key | `IAsymmetricSignatureService` |
| Authenticate data between parties sharing a secret | `IHmacSigner` |
| Store a password, API key or recovery code | `IOneWayHasher` |
| Fingerprint content for ETags, deduplication or hash chains | `IContentHasher` |
| Compare a presented token with the expected one | `FixedTimeComparison` |
| Generate a token, salt, key or random code | `ISecureRandomGenerator` |
| Add authenticator-app second factor | `TotpSecret`, `TotpProvisioningUri`, `ITotpVerifier`, `IRecoveryCodeGenerator` |

## Reference

### Registration

`AddSharedKernelCryptography` registers everything that needs no keys and returns a builder for the rest. Each opt-in
service requires a provider that only your service can supply.

```csharp
services.AddSharedKernelCryptography(configuration)   // hashing, random, content hash, HMAC, HOTP/TOTP, recovery codes
    .AddSymmetricEncryption()                         // requires IEncryptionKeyProvider
    .AddSynchronousSymmetricEncryption()              // requires ISynchronousEncryptionKeyProvider
    .AddEnvelopeEncryption()                          // requires IEnvelopeEncryptionProvider
    .AddAsymmetricSigning()                           // requires ISigningKeyProvider
    .AddTotpVerification();                           // requires ITotpReplayGuard
```

Every registration uses `TryAdd`: calling a method twice is harmless, and a registration you make earlier wins.
Nothing that needs a missing provider is registered by default, so container validation at startup passes.

### Symmetric encryption

AES-256-GCM with a random 96-bit nonce per call. Keys must be exactly 32 bytes; any other length throws
`CryptographicException`.

```csharp
EncryptedPayload payload = await encryption.EncryptAsync(plaintext, associatedData: orderId.ToByteArray(), ct);
byte[] stored = payload.ToBytes();                    // or payload.ToString() for Base64Url text

if (EncryptedPayload.TryParse(stored, out EncryptedPayload? parsed))
{
    Result<byte[]> result = await encryption.DecryptAsync(parsed, orderId.ToByteArray(), ct);
}
```

| Topic | Behaviour |
| --- | --- |
| Associated data | Required on every call; authenticated, never stored. Pass the same bytes to decrypt. Use `ReadOnlyMemory<byte>.Empty` only when nothing identifies the context. |
| Storage format | `[0x01][key id length][key id][nonce 12][tag 16][ciphertext]`; `ToString()` is that layout in Base64Url |
| Sync vs async | Identical payloads and results; each service decrypts what the other encrypts |
| Failures | Decryption returns `Result` failures and never throws for bad input; a wrong key length or unreachable provider throws |
| Strings | `EncryptToString(Async)` encodes UTF-8 and returns Base64Url text |

### Key providers

| Provider | Use |
| --- | --- |
| `IEncryptionKeyProvider` | Async lookup, for key management services |
| `ISynchronousEncryptionKeyProvider` | Lookup from memory, for synchronous code |
| `StaticEncryptionKeyProvider` | Implements both over keys loaded at startup |
| `CachedEncryptionKeyProvider` | Caches a remote provider: time to live, single flight, bounded size, unknown ids not cached, expired keys never served |
| `PurposeBoundEncryptionKeyProvider` / `PurposeBoundSynchronousEncryptionKeyProvider` | Derives purpose- and context-specific keys from another provider |
| `IEncryptionKeyProviderProbe` | Optional readiness check for remote providers |

`GetKeyAsync` receives key ids read from payloads, which may be forged. Providers must not let arbitrary ids cause
unbounded work or remote calls; `CachedEncryptionKeyProvider` and the Azure Key Vault provider already guard this.

### Key rotation

```csharp
// 1. Add the new key and make it current. Old payloads still decrypt with their own key.
// 2. In a background job, move stored payloads to the current key:
if (!await encryption.IsEncryptedWithCurrentKeyAsync(payload, ct))
{
    Result<EncryptedPayload> rotated = await encryption.ReEncryptAsync(payload, associatedData, ct);
    if (rotated.IsSuccess) await store.SaveAsync(rotated.Value.ToBytes(), ct);
}
// 3. Retire the old key once nothing uses it.
```

### Envelope encryption

Each value gets a fresh data key; the data key is wrapped by a master key in a key management service and stored
inside the payload. Requires an `IEnvelopeEncryptionProvider`, for example `AzureKeyVaultEncryptionKeyProvider`.

```csharp
EnvelopePayload payload = await envelope.EncryptAsync(fileBytes, associatedData: fileId.ToByteArray(), ct);
await blobs.UploadAsync(fileId, payload.ToBytes(), ct);

// Later
if (EnvelopePayload.TryParse(await blobs.DownloadAsync(fileId, ct), out EnvelopePayload? stored))
{
    Result<byte[]> file = await envelope.DecryptAsync(stored, fileId.ToByteArray(), ct);
}
```

| Topic | Behaviour |
| --- | --- |
| Cost | One key service call per encryption and per decryption; suits files and exports, not millions of small values |
| Format | `[0x02][master key id][wrapped key][nonce][tag][ciphertext]`; the header is authenticated with your associated data |
| Unwrap failures | `cryptography.data_key_unwrap_failed` |
| RSA master keys | Anyone with the public key can wrap a data key and create payloads that decrypt. Use a symmetric master key (managed HSM) or sign payloads when writers are untrusted. |

### Subkey derivation

HKDF-SHA256 (RFC 5869). The root key must be at least 32 random bytes; the purpose and context are length-prefixed
so no two pairs collide.

```csharp
byte[] subkey = SubkeyDerivation.DeriveKey(rootKey, purpose: "documents", context: Encoding.UTF8.GetBytes(tenantId));

// Or wrap a provider: every key it returns becomes a tenant-specific subkey with the same id.
IEncryptionKeyProvider tenantKeys = rootProvider.ForPurpose("documents", Encoding.UTF8.GetBytes(tenantId));
var tenantEncryption = new AesGcmEncryptionService(tenantKeys);
```

### Signing

Keys carry their algorithm; callers name only the key. Data is hashed locally with the key's digest algorithm, so a
remote key never receives the data.

```csharp
services.AddSingleton<ISigningKeyProvider>(new InMemorySigningKeyProvider(
[
    SigningKey.FromECDsa("receipts-2026", ECDsa.Create(ECCurve.NamedCurves.nistP256)),     // ES256
    SigningKey.FromRsa("partner-jwt", RSA.Create(3072), SignatureAlgorithm.RS256),         // RSASSA-PKCS1-v1_5
]));
services.AddSharedKernelCryptography(configuration).AddAsymmetricSigning();

byte[] signature = await signer.SignAsync(receiptJson, "receipts-2026", ct);
bool valid = await signer.VerifyAsync(receiptJson, signature, "receipts-2026", ct);
```

| Algorithm | Key | Digest | Notes |
| --- | --- | --- | --- |
| `PS256`, `PS384`, `PS512` | RSA ≥ 2048 bits | SHA-256/384/512 | RSASSA-PSS; preferred for RSA |
| `RS256`, `RS384`, `RS512` | RSA ≥ 2048 bits | SHA-256/384/512 | PKCS #1 v1.5; for systems that require it |
| `ES256`, `ES384`, `ES512` | P-256, P-384, P-521 | SHA-256/384/512 | Chosen from the curve; IEEE P1363 signatures by default, DER optional |

Signing with an unknown key throws `KeyNotFoundException`. Verifying with an unknown key or a malformed signature
returns `false`. `GetAlgorithmAsync` gives the value for a JWS `alg` header. Stream overloads hash without buffering.

### HMAC

`IHmacSigner` computes and verifies HMAC-SHA256 in fixed time. Keys must be at least 32 bytes.

### Password and secret hashing

```csharp
string stored = hasher.Hash(password);          // $pbkdf2-sha256$i=600000$<salt>$<hash>

switch (hasher.Verify(stored, attempt))
{
    case HashVerificationResult.SuccessRehashNeeded:
        await users.UpdatePasswordHashAsync(userId, hasher.Hash(attempt), ct);
        goto case HashVerificationResult.Success;
    case HashVerificationResult.Success:
        return SignIn(userId);
    default:
        return InvalidCredentials();
}
```

| Topic | Behaviour |
| --- | --- |
| Format | PHC string naming its algorithm and parameters; a pepper adds `k=<pepper id>` |
| Default | PBKDF2-HMAC-SHA256, 600,000 iterations, 16-byte salt (FIPS 140-3 approved) |
| Argon2id | Add `SharedKernel.Cryptography.Argon2` and set `OneWayHashing:Algorithm` to `argon2id` |
| Rehash | `SuccessRehashNeeded` when the algorithm, cost or pepper changed; hash again and store |
| Pepper | HMAC-SHA256 of the secret with a key kept outside the database; rotate by adding a pepper and changing `CurrentPepperId` |
| Input | Normalized to Unicode NFKC; empty secrets cannot be hashed |
| Hostile hashes | Stored costs are bounded before any work (PBKDF2: at most 2,000,000 iterations) |
| Custom algorithms | Implement `IOneWayHashAlgorithm` and call `AddOneWayHashAlgorithm<T>()` |

### Content hashing

`IContentHasher` computes SHA-256 over spans or streams, with `ComputeHashHex` and `ComputeHashBase64` extensions.
It is fast and unsalted by design: never use it for secrets.

### Fixed-time comparison

```csharp
bool ok = FixedTimeComparison.AreEqual(presentedApiKey, expectedApiKey);            // hides length and position
bool okDuringRotation = FixedTimeComparison.AreEqualToAny(presented, [current, previous]);
```

### Secure random values

`ISecureRandomGenerator` (`SecureRandomGenerator`): `GetBytes`, `Fill`, `GetInt32(toExclusive)`,
`GetString(alphabet, length)` and `GetToken(byteCount = 32)`, which returns unpadded Base64Url (43 characters for 32 bytes).

### One-time passwords

```csharp
// Enrollment
byte[] secret = TotpSecret.Generate(random);                                  // 20 bytes; store it encrypted
Uri qr = TotpProvisioningUri.Build("Contoso", user.Email, secret);            // render as a QR code
IReadOnlyList<string> recovery = recoveryCodes.GenerateCodes();              // show once; store each hashed:
                                                                             // hasher.Hash(RecoveryCodeGenerator.Normalize(code))

// Sign-in (after checking your attempt throttle)
TotpVerificationResult result = await totp.VerifyAsync(userId.ToString(), secret, submittedCode, cancellationToken: ct);
```

| Topic | Behaviour |
| --- | --- |
| Parameters | `TotpParameters`: 6–8 digits (default 6), 15–300 second steps (default 30), 0–5 drift steps (default 1), SHA-1/256/512 |
| Secrets | At least 16 bytes; `TotpSecret.Generate` defaults to 20 |
| Input | Spaces and hyphens are ignored; other non-digits fail |
| Replay | `ITotpReplayGuard` records the last accepted time step per identity; the same or an older step returns `Replayed` |
| Throttling | Not built in. `ITotpAttemptThrottle` is the seam; RFC 4226 requires limiting attempts |
| Recovery codes | `XXXXX-XXXXX` from the Base32 alphabet, 50 bits each |

### Error codes

| Code | Type | Raised by |
| --- | --- | --- |
| `cryptography.malformed_payload` | Validation | Text or bytes that are not a payload; decrypted text that is not UTF-8 |
| `cryptography.decryption_failed` | Validation | Altered payload, different associated data, or different key material |
| `cryptography.unknown_key_id` | Unexpected | A payload names a key the provider does not have |
| `cryptography.data_key_unwrap_failed` | Validation | Envelope data key rejected by the provider |
| `cryptography.invalid_base32_encoding` | Validation | `Base32.Decode` |

### Configuration

```json
{
  "SharedKernel": {
    "Cryptography": {
      "OneWayHashing": {
        "Algorithm": "pbkdf2-sha256",
        "CurrentPepperId": "p1",
        "Peppers": { "p1": "<base64 of at least 32 random bytes, from a secret store>" }
      },
      "Pbkdf2": { "Iterations": 600000 }
    }
  }
}
```

All values are validated when the host starts: iterations 100,000–2,000,000, a registered algorithm, pepper ids of
letters, digits and hyphens, peppers of at least 32 bytes, and a `CurrentPepperId` that exists. Each hash algorithm
binds its own options (`Pbkdf2Options`, `Argon2Options`); a custom `IOneWayHashAlgorithm` must not depend on
`CryptographyOptions`, which is validated against the registered algorithms.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Pass `ReadOnlyMemory<byte>.Empty` as associated data by habit | Pass the row, tenant or message id | Without it, a ciphertext can be moved to another record and still decrypt |
| Call the async service with `.Result` from a converter | Register `ISynchronousSymmetricEncryptionService` over an in-memory provider | Blocking on a key service call starves the thread pool |
| Wrap a remote provider without a cache | Use `CachedEncryptionKeyProvider` or a provider that caches | Every encryption would call the key service |
| Delete an old key right after rotating | Re-encrypt with `ReEncryptAsync` first, then retire it | Payloads using the old key fail with `unknown_key_id` |
| Hash passwords with `IContentHasher` or SHA-256 | Use `IOneWayHasher` | Fast hashes let attackers test billions of guesses per second |
| Ignore `SuccessRehashNeeded` | Store the new hash | Old costs and algorithms stay in the database forever |
| Remove a pepper while hashes use it | Keep it until every hash is rehashed | Those hashes fail to verify |
| Compare tokens with `==` or `SequenceEqual` | Use `FixedTimeComparison` | Early exit leaks how much of the guess was right |
| Use `System.Random` or `Guid.NewGuid()` for tokens | Use `ISecureRandomGenerator` | They are predictable or not guaranteed unpredictable |
| Store TOTP secrets in plain text | Encrypt them with `ISymmetricEncryptionService` | Anyone reading the secret can generate codes |
| Verify TOTP codes without a throttle | Limit attempts per identity | Three valid values in a million are guessable without limits |
| Trust an RSA-wrapped envelope from untrusted writers | Use a symmetric master key or sign payloads | Anyone with the public key can wrap a data key |

## AI quick reference

Conventions for generating code with this package. Each line is a rule.

```text
REGISTER     services.AddSharedKernelCryptography(configuration) then opt in: .AddSymmetricEncryption(),
             .AddSynchronousSymmetricEncryption(), .AddEnvelopeEncryption(), .AddAsymmetricSigning(),
             .AddTotpVerification(). Register the matching provider yourself.
ENCRYPT      Async: ISymmetricEncryptionService.EncryptAsync(plaintext, associatedData, ct).
             Sync (EF Core converters, serializers): ISynchronousSymmetricEncryptionService over an
             ISynchronousEncryptionKeyProvider. Never block on the async service.
             Always pass meaningful associatedData (row id, tenant id, message type).
STORE        payload.ToBytes() / EncryptedPayload.TryParse(bytes, out p); payload.ToString() / TryParse(text, out p).
DECRYPT      Returns Result<byte[]>; check IsSuccess. Codes: malformed_payload, decryption_failed (Validation),
             unknown_key_id (Unexpected).
KEYS         In-memory: StaticEncryptionKeyProvider (both interfaces). Remote: IEncryptionKeyProvider +
             CachedEncryptionKeyProvider. Per tenant/purpose: provider.ForPurpose("purpose", contextBytes).
ROTATE       IsEncryptedWithCurrentKeyAsync + ReEncryptAsync in a background job before retiring keys.
SIGN         ISigningKeyProvider returns SigningKey (FromRsa(keyId, rsa, PS256..RS512) / FromECDsa(keyId, ecdsa)).
             IAsymmetricSignatureService.SignAsync(data, keyId) / VerifyAsync(data, signature, keyId).
             Never pass an algorithm at call sites.
HMAC         IHmacSigner.Sign(data, key) with key >= 32 bytes.
HASH SECRET  IOneWayHasher.Hash / Verify; on SuccessRehashNeeded store Hash(secret) again.
HASH CONTENT IContentHasher (SHA-256), never for secrets.
COMPARE      FixedTimeComparison.AreEqual(a, b) / AreEqualToAny(candidate, expected).
RANDOM       ISecureRandomGenerator.GetBytes / GetToken / GetString(alphabet, length) / GetInt32.
TOTP         TotpSecret.Generate(random); TotpProvisioningUri.Build(issuer, account, secret);
             ITotpVerifier.VerifyAsync(identityKey, secret, code) -> Valid | Invalid | Replayed;
             implement ITotpReplayGuard.TryAcceptTimeStepAsync atomically; throttle attempts.
FORBIDDEN    System.Random, Guid.NewGuid() for secrets, == on secrets, raw Aes/AesGcm/RSA outside this package,
             SHA-256 for passwords, .Result/.GetAwaiter().GetResult() on crypto calls.
```

## FIPS 140-3

Every algorithm in this package is FIPS 140-3 approved when the platform's cryptographic provider runs in FIPS mode:
AES-256-GCM, HKDF-SHA256, PBKDF2-HMAC-SHA256, RSA (PSS and PKCS #1 v1.5), ECDSA on P-256/P-384/P-521, HMAC-SHA256,
SHA-256/384/512 and the operating system random generator. HOTP/TOTP use HMAC-SHA1 by default as the RFCs require;
HMAC-SHA1 remains approved for this use. Argon2id (companion package) is not approved; keep PBKDF2 where FIPS
compliance is required. The package itself is not a validated module: validation belongs to the operating system
provider .NET calls into.

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`; any change fails the build until it is
  recorded.
- **Every public member is documented**, including the exceptions it throws.
- **No third-party dependencies.** Only the .NET base class library and two SharedKernel packages.
- **Versioned formats.** Payloads, envelopes and hashes carry a version or algorithm identifier, so future formats can
  be read alongside current ones.
- **Thread-safe.** Every service and provider in the package can be registered as a singleton.

## Deliberately not included

- **No key storage.** The package holds no keys and reads none from configuration, except pepper secrets bound
  through validated options.
- **No sync-over-async.** Synchronous services require a synchronous provider; nothing blocks on a remote call.
- **No streaming encryption.** Encrypt large content with `IEnvelopeEncryptionService` or in chunks at the call site.
- **No post-quantum algorithms yet.** ML-DSA and ML-KEM will fit the algorithm-carrying signing design when platform
  support and interoperability mature.
- **No attempt throttling or replay store implementation.** Both need a shared store your service owns;
  `ITotpAttemptThrottle` and `ITotpReplayGuard` are the seams.
