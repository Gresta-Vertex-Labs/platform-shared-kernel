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

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
