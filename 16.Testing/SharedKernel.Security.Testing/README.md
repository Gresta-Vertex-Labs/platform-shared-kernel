# SharedKernel.Security.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**Test helpers for the SharedKernel security packages: a settable `IUserContext`, a claims-principal builder,
in-memory stores for API keys, DPoP replay, TOTP step-up and recovery codes, and builders for DPoP proofs and mTLS
client certificates.** Authorization, step-up and authentication code can be tested without an identity provider,
a database or a certificate authority.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Security.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Security`.

## Contents

| Type | Stands in for | Notes |
| --- | --- | --- |
| `FakeUserContext` | `IUserContext` | Every member settable: `SubjectId` (default `FakeUserContext.DefaultSubjectId`), `ActorKind`, `TenantId` (`TenantId?`), `ClientId`, `SessionId`, `Roles`, `Permissions`, `AuthenticationMethods`, `AuthContextClassReference`, `AuthTime`, `IsSenderConstrained`, `Claims`. `HasRole`/`HasPermission` compare ordinally, as production does |
| `SecurityTestContextBuilder` | a `ClaimsPrincipal` | Fluent `WithSubjectId`/`WithTenantId`/`WithRoles`/`WithPermissions`/`WithActorKind`/`WithAuthenticationMethods`/`WithAuthTime`/`WithClaim`/`Unauthenticated()`; `Build()` returns the principal, `BuildUserContext()` the matching `FakeUserContext` |
| `InMemoryApiKeyStore` | `IApiKeyStore` (`.ApiKey`) | `Add(GeneratedApiKey, clientId, tenantId, permissions, …)` or `Add(ApiKeyRecord)`, `Revoke(keyId, at)` |
| `InMemoryDpopReplayCache` | `IDpopReplayCache` (`.Oidc`) | `TryAddAsync` returns `false` for a replayed proof id; `Count` |
| `InMemoryTotpStepUpStore` | `ITotpStepUpStore` (`.Totp`) | Step-ups keyed by subject **and** session; `Clear()` |
| `InMemoryRecoveryCodeStore` | `IRecoveryCodeStore` (`.Totp`) | `Save(subjectId, codes)`, atomic `TryMarkUsedAsync` |
| `DpopTestProofBuilder` → `DpopTestProof` | a client's DPoP proof | Signs a real ES256 proof: `WithHttpMethod`, `WithHttpUri`, `WithAccessToken`, `WithNonce`, `WithIssuedAt`, `WithJti`, and the negative cases `WithMissingAth`, `WithMismatchedAth`, `WithMalformedAth`, `WithType` |
| `MtlsTestCertificateBuilder` → `MtlsTestCertificate` | a client certificate | `AsSelfSigned()`, `AsChainedFromEphemeralCa()`, `AsRevoked()` (with a CRL), `WithSubjectName`, `WithValidityPeriod` |

## Registration

There are no `Add*` helpers; register what the code under test needs:

```csharp
services.AddSingleton<IUserContext>(new SecurityTestContextBuilder()
    .WithTenantId(tenantId)
    .WithPermissions("orders.write")
    .BuildUserContext());
```

## Example

```csharp
var user = new FakeUserContext
{
    TenantId = tenantId,
    AuthenticationMethods = ["pwd"],
    AuthTime = clock.UtcNow.AddMinutes(-30),
};

var result = await transferService.ApproveAsync(transferId, user, ct);

result.Error.Type.Should().Be(ErrorType.Forbidden);   // no recent MFA: step-up required
```

```csharp
var proof = new DpopTestProofBuilder()
    .WithHttpMethod("POST")
    .WithHttpUri("https://api.example.test/orders")
    .WithAccessToken(accessToken)
    .Build();

request.Headers.Authorization = new("DPoP", proof.AccessToken);
request.Headers.Add("DPoP", proof.ProofJwt);
```

## Related packages

- References `SharedKernel.Security.Abstractions` and, for the stores they define, `SharedKernel.Security.ApiKey`,
  `.Oidc` and `.Totp` (Host tier, so this package brings ASP.NET Core).
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `TestRequestContext` when the code under test reads
  `IRequestContext` rather than `IUserContext`.
