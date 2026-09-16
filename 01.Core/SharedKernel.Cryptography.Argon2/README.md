# SharedKernel.Cryptography.Argon2

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**Argon2id password hashing for [`SharedKernel.Cryptography`](../SharedKernel.Cryptography/README.md)'s
`IOneWayHasher`.**

Argon2id (RFC 9106) is memory-hard: each guess costs an attacker memory as well as time, which makes GPU and ASIC
cracking far more expensive than with PBKDF2. It is OWASP's first recommendation for password storage.

| You get | So that |
| --- | --- |
| An `argon2id` algorithm for `OneWayHasher` | Switching algorithms is one configuration value |
| Standard PHC strings (`$argon2id$v=19$m=...,t=...,p=...$salt$hash`) | Hashes interoperate with Argon2 libraries in other languages |
| Rehash-on-verify from PBKDF2 or older costs | Stored hashes upgrade as users sign in, with no migration |
| Cost ceilings checked before hashing | A hash written by an attacker cannot exhaust memory or CPU |

## Install

```shell
dotnet add package SharedKernel.Cryptography.Argon2
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Dependencies | `SharedKernel.Cryptography`, `SharedKernel.Configuration`, `Konscious.Security.Cryptography.Argon2` (managed, no native code) |

## Quick start

```csharp
builder.Services.AddSharedKernelCryptography(builder.Configuration)
    .AddArgon2id(builder.Configuration);
```

```json
{
  "SharedKernel": {
    "Cryptography": {
      "OneWayHashing": { "Algorithm": "argon2id" },
      "Argon2": { "MemorySizeKb": 19456, "Iterations": 2, "DegreeOfParallelism": 1 }
    }
  }
}
```

Inject `IOneWayHasher` as usual. New hashes use Argon2id; PBKDF2 hashes keep verifying and return
`HashVerificationResult.SuccessRehashNeeded`, so store `Hash(password)` again at that point. A configured pepper
applies to Argon2id hashes too.

## Configuration

| Setting | Default | Range | Notes |
| --- | --- | --- | --- |
| `MemorySizeKb` | 19,456 (19 MiB) | 7,168 – 1,048,576 | Memory per hash. Each concurrent sign-in uses this much. |
| `Iterations` | 2 | 2 – 10 | Passes over memory |
| `DegreeOfParallelism` | 1 | 1 – 16 | Lanes; each uses a thread-pool thread while hashing |

The defaults are the OWASP minimum. Raise memory first; measure sign-in latency and peak memory under concurrent
load. Changing a value never invalidates stored hashes; they report `SuccessRehashNeeded`.

## Verification bounds

A stored hash is data an attacker may control, and its parameters decide the work verification does. Verification
returns `Failed` without hashing when the hash is not version 19, or its memory exceeds 1 GiB, its iterations exceed
10, its lanes exceed 16, its memory is under 8 KiB per lane, its salt is outside 8–64 bytes, or its output is outside
16–64 bytes.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Set `Algorithm` to `argon2id` without calling `AddArgon2id` | Call both | Startup validation rejects an unregistered algorithm |
| Use Argon2id where FIPS 140-3 is required | Keep the PBKDF2 default there | Argon2id is not FIPS approved |
| Raise `MemorySizeKb` without load testing | Measure concurrent sign-ins | Memory use multiplies with concurrency |
| Remove PBKDF2 support after switching | Leave it registered (it always is) | Unmigrated users could not sign in |

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`.
- **Interoperable output.** PHC strings with unpadded Base64 salt and hash, readable by standard Argon2 libraries
  (hashes without a pepper).
- **Not FIPS 140-3 approved.** Choose PBKDF2 where FIPS compliance is required.
