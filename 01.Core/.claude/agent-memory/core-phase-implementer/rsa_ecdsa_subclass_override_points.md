---
name: rsa-ecdsa-subclass-override-points
description: When subclassing System.Security.Cryptography.RSA/ECDsa (test doubles or real providers like a future Key Vault remote-signer), override SignHash/VerifyHash, not SignData/VerifyData
type: feedback
---

`RSA.SignData`/`VerifyData` and `ECDsa.SignData`/`VerifyData` are **non-virtual convenience methods** on the BCL abstract base classes — they hash the input internally and then delegate to `SignHash`/`VerifyHash`, which are the actual overridable extension points. Attempting `public override byte[] SignData(...)` on a subclass fails to compile with CS0506 ("cannot override inherited member ... because it is not marked virtual/abstract/override").

**How to subclass correctly:**
- `RSA`: override `SignHash(byte[] hash, HashAlgorithmName, RSASignaturePadding)` / `VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName, RSASignaturePadding)`, plus the genuinely-abstract `ExportParameters`/`ImportParameters`.
- `ECDsa`: override `SignHash(byte[] hash)` / `VerifyHash(byte[] hash, byte[] signature)`, plus abstract `ExportParameters`/`ImportParameters`/`GenerateKey`.
- `KeySize` is a virtual property on `AsymmetricAlgorithm` — override it too if the subclass needs to report a different value than whatever it wraps (e.g. a test double simulating an undersized key).
- Let the compiler enumerate the real abstract-member list for you — write the subclass, build, and fix whatever CS0534 lists. Don't guess the full member set from memory.

**Why this matters beyond test doubles:** any future provider that subclasses `RSA`/`ECDsa` to back a remote-signing operation (e.g. Azure Key Vault Keys' `CryptographyClient.SignData`/`VerifyData` — see `01.Core`'s P-494 design notes for `KeyVaultRsaKey`/`KeyVaultEcdsaKey`) hits the exact same override-point requirement. `01.Core/CLAUDE.md`'s P-494 design section was corrected during P-493 to say `SignHash`/`VerifyHash`, not `SignData`/`VerifyData`, specifically so that future implementer doesn't repeat the mistake.

**Testing technique this unlocked (P-493/WO-081, T-70):** to prove a service never disposes a provider-returned crypto-key instance, subclass `RSA`/`ECDsa` directly and make `Dispose(bool)` throw — then have the test double return the SAME cached instance across repeated calls (not a fresh clone per call, which would silently mask the regression). Verify the double's genuineness empirically: temporarily reintroduce the bug (`using` around the returned instance), confirm the test fails with the expected exception, then revert. A `KeySize`-override subclass (real key, faked `KeySize` getter) is the equivalent trick for testing a minimum-key-size gate without constructing a genuinely undersized/non-standard key.
