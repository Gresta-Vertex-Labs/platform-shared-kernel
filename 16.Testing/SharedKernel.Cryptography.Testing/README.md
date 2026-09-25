# SharedKernel.Cryptography.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

Doubles for `SharedKernel.Cryptography` that run the real algorithms and add call recording and failure simulation: symmetric and envelope encryption, rotatable key providers (synchronous and remote), signing, HMAC, one-way hashing, content hashing, a seedable secure random generator and a TOTP replay guard. `AddFakeCryptography()` registers them all.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Cryptography.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Cryptography`
- **Types:** `FakeAsymmetricSignatureService`, `FakeContentHasher`, `FakeCryptographyServiceCollectionExtensions`, `FakeEncryptionKeyProvider`, `FakeEnvelopeEncryptionProvider`, `FakeHmacSigner`, `FakeOneWayHasher`, `FakeRemoteEncryptionKeyProvider`, `FakeSecureRandomGenerator`, `FakeSigningKeyProvider`, `FakeSymmetricEncryptionService`, `FakeTotpReplayGuard`

## Dependencies

References `SharedKernel.Cryptography` and `SharedKernel.Testing` (`FakeClock`). No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
