# SharedKernel.Cryptography

Dependency-free cryptographic primitives for the Platform.SharedKernel ecosystem. Pure BCL `System.Security.Cryptography` — zero third-party NuGet dependencies, AOT-compatible. Depends on `SharedKernel.Primitives` and `SharedKernel.Configuration`.

Deliberately decoupled from identity/OIDC concerns, so worker and non-web services can consume crypto without pulling in an authentication stack.

## Included

| Contract | Implementation | Purpose |
|---|---|---|
| `IOneWayHasher` | `Pbkdf2OneWayHasher` | One-way hashing of **secrets** — passwords, API keys, recovery codes. PBKDF2-HMACSHA256, deliberately slow. |
| `ISymmetricEncryptionService` | `AesGcmEncryptionService` | AES-256-GCM authenticated encryption (AEAD). |
| `IAsymmetricSignatureService` | `RsaSignatureService`, `EcdsaSignatureService` | RSA / ECDSA sign and verify. |
| `IHmacSigner` | `HmacSha256Signer` | HMAC-SHA256 message signing. |
| `ISecureRandomGenerator` | `CryptoRandomGenerator` | Cryptographically secure bytes and tokens. |
| `IContentHasher` | `Sha256ContentHasher` | Fast, streaming **non-secret** fingerprinting — ETags, dedup keys, cache keys. |

`IEncryptionKeyProvider` and `IAsymmetricKeyProvider` are the key-supply seams — bridge them to your own key store (Key Vault, KMS, config) at the composition root.

> **`IOneWayHasher` vs `IContentHasher`.** `IOneWayHasher` is for secrets and is intentionally expensive. `IContentHasher` is for content fingerprints and is fast. Never substitute one for the other.

## Quick Start

```csharp
// Register (Program.cs)
builder.Services.AddSharedKernelCryptography(builder.Configuration);

// Hash and verify a secret
public class ApiKeyService(IOneWayHasher hasher)
{
    public string Issue(string rawKey) => hasher.Hash(rawKey);

    public bool IsValid(string storedHash, string presentedKey) =>
        hasher.Verify(storedHash, presentedKey) == HashVerificationResult.Success;
}

// Encrypt / decrypt — Decrypt returns Result<T>, it does not throw on tampered input
public class DocumentService(ISymmetricEncryptionService crypto)
{
    public EncryptedPayload Protect(byte[] plaintext) => crypto.Encrypt(plaintext);

    public Result<byte[]> Unprotect(EncryptedPayload payload) => crypto.Decrypt(payload);
}

// Secure tokens
public class InviteService(ISecureRandomGenerator random)
{
    public string NewInviteCode() => random.NextToken(32);
}
```

## Rules

Prohibited platform-wide — use these primitives instead:

- Hand-rolled password hashing (raw `SHA256`/`MD5`)
- `System.Random` or `Guid.NewGuid()` for security tokens or keys
- Unauthenticated symmetric encryption (AES-CBC without a MAC) — this package is AEAD-only

`Decrypt` returns `Result<byte[]>` rather than throwing, so a tampered or truncated payload is an ordinary failure branch, not an exception.

For transparent EF Core **column** encryption, use `SharedKernel.Persistence.EfCore`'s `PropertyBuilder.Encrypt()` instead — do not route column encryption through this package directly.

## Key-Material Zeroization

Every plaintext/subkey/comparison `byte[]` buffer this package **genuinely owns and controls the
lifetime of** is zeroed via `CryptographicOperations.ZeroMemory` — inside a `try`/`finally` so it
happens even when the buffer's own computation throws — the instant it is no longer needed:

| Class | Buffer(s) zeroed | Zeroed when |
|---|---|---|
| `Pbkdf2OneWayHasher.Hash` | `salt`, `subkey` | Immediately after `Encode` has copied their contents into the returned string |
| `Pbkdf2OneWayHasher.Verify` | `salt`, `expectedSubkey`, `actualSubkey` | Once the `CryptographicOperations.FixedTimeEquals` comparison has run — every exit path, not just the successful one |
| `Argon2idOneWayHasher.Hash` (and its shared `ComputeSubkey` helper) | `secretBytes`, `salt`, `subkey` | `secretBytes` once Argon2id has finished reading it; `salt`/`subkey` immediately after `Encode` has copied their contents into the returned string |
| `Argon2idOneWayHasher.Verify` (and its shared `ComputeSubkey` helper) | `secretBytes`, `salt`, `expectedSubkey`, `actualSubkey` | Same pattern as `Pbkdf2OneWayHasher.Verify` |
| `HmacSha256Signer.Verify` | the internal `expected` comparison buffer | Once the `FixedTimeEquals` comparison has run |
| `AesGcmEncryptionService.EncryptToString`/`EncryptToStringAsync` | the intermediate UTF-8 plaintext `byte[]` (`Encoding.UTF8.GetBytes(plaintext)`) | Immediately after that buffer has been encrypted |
| `AesGcmEncryptionService.DecryptToString`/`DecryptToStringAsync` | the intermediate decrypted plaintext `byte[]` (this method's own internal call into `Decrypt`/`DecryptAsync`) | Immediately after its contents have been copied into the returned `string` |

**What is deliberately *never* zeroed, and why:** the `byte[]` returned **directly** to a caller by
the primary `Encrypt`/`EncryptAsync`/`Decrypt`/`DecryptAsync` `byte[]`-based overloads is **never**
zeroed by this package — it is the caller's own needed output, still to be read after the call
returns. Zeroing it would silently corrupt the very result the caller asked for. `IHmacSigner.Sign`'s
return value is likewise never zeroed — it *is* the signature the caller needs, not an internal
scratch buffer. `HashVerificationResult`, `bool`, and every other non-`byte[]` return value carries
no key material to zero in the first place.

**Two broader redesigns were considered and explicitly declined (P-524/WO-083) — do not re-propose
either:**

- **An `IDisposable CryptographicKey`.** `CryptographicKey` instances typically flow through
  `CachedEncryptionKeyProvider` — a shared, multi-reader, process-lifetime cache. The point a key's
  lifetime genuinely ends is the *cache's own eviction*, not any individual caller's read; an
  `IDisposable` shape would create a use-after-dispose hazard the moment two callers hold the same
  cached instance and one of them disposes it.
- **`ReadOnlySpan<char>`/`char[]` secret overloads on `IOneWayHasher`.** The realistic caller — a
  request DTO field, an `IConfiguration` value — already holds the secret as an immutable,
  already-allocated `string` by the time it reaches this API. A `Span`/`char[]` overload cannot
  un-happen that upstream allocation; it would add API surface without removing the one allocation
  that actually matters.

**Upstream guidance — minimizing a secret's lifetime *before* it ever reaches this package:** this
package can only zero buffers *it* allocates. If a secret string arrives already-allocated (from
model binding, `IConfiguration`, a database read), that original `string` is immutable and outside
this package's control — .NET strings cannot be zeroed in place. To minimize exposure upstream:

- Avoid holding a secret in a long-lived field or static — read it, use it, let it go out of scope
  as quickly as possible so the GC can reclaim it.
- Prefer passing a secret straight from its source (a request DTO property, `IConfiguration`) into
  `Hash`/`Verify`/`Sign`/`EncryptToString` rather than copying it into an intermediate variable that
  outlives the call.
- If a secret is read from a stream or buffer *you* control before it becomes a `string` (e.g. a
  raw upload), zero that buffer yourself with `CryptographicOperations.ZeroMemory` once the `string`
  conversion is complete — this package cannot do that on your behalf, since it never sees the
  pre-`string` bytes.

## FIPS 140-3 / Approved-Algorithm Posture

A posture statement for a service operating under a FIPS-enforced-mode compliance requirement (certain US federal, healthcare, or financial environments locking the OS crypto provider to FIPS-validated algorithms only). This is a documentation-only statement — no code in this package changes based on it, and there is no runtime FIPS-mode switch.

**FIPS-approved:**

| Primitive | Type | Standard |
|---|---|---|
| PBKDF2-HMACSHA256 | `Pbkdf2OneWayHasher` | SP 800-132 |
| AES-256-GCM | `AesGcmEncryptionService` | SP 800-38D |
| RSA (2048-bit minimum) | `RsaSignatureService` | FIPS 186 |
| ECDSA (256-bit minimum, NIST curves) | `EcdsaSignatureService` | FIPS 186 |
| HMAC-SHA256 | `HmacSha256Signer` | FIPS 198-1 |
| SHA-256 | `Sha256ContentHasher` / `IContentHasher` | FIPS 180-4 |

Each is approved only at the key size / configuration this package actually enforces (e.g. RSA's and ECDSA's documented minimums, above) — the table lists the shipped default posture, not a general endorsement of every possible parameterization of the underlying algorithm.

**NOT FIPS-approved:**

- **Argon2id** (`SharedKernel.Cryptography.Argon2`'s `Argon2idOneWayHasher`) — no FIPS 140-3 validated status exists for Argon2 as of this posture statement. A FIPS-constrained consumer must use the unkeyed `Pbkdf2OneWayHasher` default instead; see that package's own README for the full PBKDF2-vs-Argon2id comparison.

**The gap this statement exists specifically to surface:** RFC 4226/6238's **default** `HotpAlgorithm.Sha1` (`HotpGenerator`, `TotpGenerator`, `TotpVerifier` all default to it unless a caller passes a different value) carries restricted/deprecated status under FIPS-enforced mode. A FIPS-constrained consumer must explicitly pass `HotpAlgorithm.Sha256` or `HotpAlgorithm.Sha512` to every `GenerateCode`/`ValidateCode`/`VerifyAsync` call — never rely on the RFC-default parameter value.

**Summary guidance for FIPS-constrained services:** use PBKDF2 (never Argon2id) for one-way secret hashing, and explicitly pass `HotpAlgorithm.Sha256`/`.Sha512` to every TOTP/HOTP call (never the default `Sha1`).

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
