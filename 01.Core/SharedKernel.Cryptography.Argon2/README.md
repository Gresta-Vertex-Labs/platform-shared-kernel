# SharedKernel.Cryptography.Argon2

Argon2id implementation of [`SharedKernel.Cryptography`](../SharedKernel.Cryptography/README.md)'s `IOneWayHasher`. The one `01.Core` package with a genuine third-party Argon2id dependency (`Konscious.Security.Cryptography.Argon2`, a pure-managed .NET implementation — no native/P-Invoke binding), kept out of the zero-third-party-dependency `SharedKernel.Cryptography` core — mirrors why `SharedKernel.Cryptography.KeyVault.Azure` and `SharedKernel.Validation.FluentValidation` are separate sibling packages.

## Argon2id vs. PBKDF2 — which one to use

`SharedKernel.Cryptography`'s `Pbkdf2OneWayHasher` remains the **unkeyed default** `IOneWayHasher`, and this package's `Argon2idOneWayHasher` is registered as a **keyed alternative** ("Argon2id") alongside it — never replacing it.

**FIPS-mode is the deciding factor:**

| | PBKDF2 (`Pbkdf2OneWayHasher`) | Argon2id (`Argon2idOneWayHasher`) |
|---|---|---|
| FIPS 140-3 approved | **Yes** | No |
| OWASP's current top recommendation | No (an accepted fallback when FIPS applies) | **Yes** |
| Resistance to GPU/ASIC cracking | Weaker (compute-bound only) | Stronger (memory-hard) |

If your deployment target runs in FIPS mode (a hard regulatory/platform constraint — e.g. certain US federal, healthcare, or financial environments locking the OS crypto provider to FIPS-validated algorithms only), keep using the unkeyed `IOneWayHasher` default (PBKDF2). Everywhere FIPS-mode is not a hard constraint, prefer this package's keyed Argon2id hasher — it is OWASP's current top recommendation for new password-storage designs.

Both implement the identical `IOneWayHasher` contract and self-describing rehash-on-parameter-change behavior — switching between them (or running both side by side, keyed by algorithm, during a migration) requires no change to calling code beyond which registration/key you resolve.

## Included

| Type | Purpose |
|---|---|
| `Argon2idOneWayHasher` | Implements `IOneWayHasher` — Argon2id hash/verify producing the real, interoperable PHC string format |
| `Argon2CryptographyOptions` | `.MemorySizeKb` (default 19456), `.Iterations` (default 2), `.DegreeOfParallelism` (default 1) — OWASP's current default Argon2id row, each with a real (not nominal) `[Range]` floor and ceiling |
| `AddSharedKernelArgon2Cryptography(configuration)` | Registers the validated options and `Argon2idOneWayHasher` as the `"Argon2id"`-keyed `IOneWayHasher` singleton only |

## Quick Start

```csharp
// appsettings.json — omit entirely to use the OWASP-default values above.
// { "SharedKernel": { "Cryptography": { "Argon2": {
//   "MemorySizeKb": 19456, "Iterations": 2, "DegreeOfParallelism": 1
// } } } }

builder.Services.AddSharedKernelCryptography(builder.Configuration);       // unkeyed default: Pbkdf2OneWayHasher
builder.Services.AddSharedKernelArgon2Cryptography(builder.Configuration); // keyed "Argon2id": Argon2idOneWayHasher

// Resolve explicitly by key — the unkeyed IOneWayHasher stays Pbkdf2OneWayHasher regardless.
IOneWayHasher argon2 = provider.GetRequiredKeyedService<IOneWayHasher>(
    Argon2CryptographyServiceCollectionExtensions.Argon2idOneWayHasherKey);

string hash = argon2.Hash("correct-horse-battery-staple");
HashVerificationResult result = argon2.Verify(hash, "correct-horse-battery-staple");
```

## Self-describing output — the real PHC string format

`Hash` produces the industry-standard Argon2 **PHC string format**, not a bespoke encoding:

```
$argon2id$v=19$m=19456,t=2,p=1$<base64 salt>$<base64 subkey>
```

This is deliberately the real, interoperable format (unlike `Pbkdf2OneWayHasher`'s bespoke binary-then-Base64 encoding — PBKDF2 has no equivalently universal standard string format, so departing from a de facto standard cost nothing there; Argon2 has one, so inventing a second encoding here would forfeit interoperability for no benefit). `Verify` parses the stored string, recomputes under the exact same embedded parameters, and constant-time-compares the subkey. Raising `Argon2CryptographyOptions` later never invalidates hashes already stored — `Verify` returns `HashVerificationResult.SuccessRehashNeeded` whenever the parsed `m`/`t`/`p` differ from the currently configured values, exactly like `Pbkdf2OneWayHasher`'s own rehash-on-parameter-change contract.

A malformed string, a foreign algorithm marker (e.g. `argon2i`/`argon2d`), an unsupported version, or a structurally valid string whose embedded parameters Argon2id itself cannot compute — all return `HashVerificationResult.Failed`. `Verify` never throws.

## A real floor, not a nominal one

The audit that started this work order (WO-081) found `CryptographyOptions.Pbkdf2Iterations` carried `[Range(1, int.MaxValue)]` — a floor in name only, since `Pbkdf2Iterations: 1` passed startup validation cleanly. `Argon2CryptographyOptions` does not repeat that mistake: `MemorySizeKb`, `Iterations`, and `DegreeOfParallelism` each carry a real, OWASP-cited `[Range]` — the lower bound of each is the smallest value appearing in any row of OWASP's own Argon2id acceptable-configurations table, not an arbitrary technical minimum. A misconfiguration below that floor fails fast at host startup (`ValidateOnStart()`), never silently.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
