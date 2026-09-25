# SharedKernel.Security.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

Test helpers for the SharedKernel security packages: `FakeUserContext` and `SecurityTestContextBuilder` for `IUserContext`, `InMemoryApiKeyStore`, `InMemoryDpopReplayCache`, the in-memory TOTP step-up and recovery-code stores, and builders for DPoP proofs (`DpopTestProofBuilder`) and mTLS test certificates (`MtlsTestCertificateBuilder`).

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
`SharedKernel.*.Testing` package.

```xml
<PackageReference Include="SharedKernel.Security.Testing" />
```

## Contents

- **Namespaces:** `SharedKernel.Testing.Security`
- **Types:** `DpopTestProof`, `DpopTestProofBuilder`, `FakeUserContext`, `InMemoryApiKeyStore`, `InMemoryDpopReplayCache`, `InMemoryRecoveryCodeStore`, `InMemoryTotpStepUpStore`, `MtlsTestCertificate`, `MtlsTestCertificateBuilder`, `SecurityTestContextBuilder`

## Dependencies

References `SharedKernel.Security.Abstractions` and, for the stores the authentication packages define, `SharedKernel.Security.ApiKey`, `.Oidc` and `.Totp` (which bring ASP.NET Core). No test framework is referenced: the doubles work under xUnit, NUnit or MSTest. The lightweight
`SharedKernel.Testing` package supplies the shared basics (`FakeClock`, `InMemoryLogger`, `TestRequestContext`,
fakers and assertions).
