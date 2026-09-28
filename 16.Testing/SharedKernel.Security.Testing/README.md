# SharedKernel.Security.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Test doubles for the SharedKernel security packages: a settable `IUserContext`, a builder for the principal an
> OIDC token produces, in-memory API-key, DPoP-replay, TOTP step-up and recovery-code stores, and builders for real
> signed DPoP proofs and mTLS client certificates.** Authorization, step-up and authentication code is tested without
> an identity provider, a database or a certificate authority.

| You get | So that |
| --- | --- |
| `FakeUserContext` | Any caller — user, service, anonymous, with roles, permissions, tenant, `amr`/`acr`/`auth_time` — in one object initializer |
| `SecurityTestContextBuilder` | The same caller as the `ClaimsPrincipal` a bearer token maps to, or as a `FakeUserContext` with matching claims |
| `WithAuthenticationMethodTime` | `[RequireAuthenticationMethod(…, MaxAgeSeconds = n)]` is driven fresh and expired from a fake clock |
| `InMemoryApiKeyStore`, `InMemoryDpopReplayCache`, `InMemoryTotpStepUpStore`, `InMemoryRecoveryCodeStore` | The stores the security packages ask you to implement work in a test host with no database |
| `DpopTestProofBuilder` | A real ES256 DPoP proof, plus the malformed variants a validator must reject |
| `MtlsTestCertificateBuilder` | Self-signed, CA-chained and revoked (with a CRL) client certificates, generated in memory |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

Add it to a **test project** only — never to production code. `TestingNeverReferencedByProduction` (an architecture
rule you can run against your own assemblies) fails any production project that references a testing package.

```xml
<PackageReference Include="SharedKernel.Security.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Security.Abstractions`, `SharedKernel.Security.ApiKey`, `SharedKernel.Security.Oidc`, `SharedKernel.Security.Totp` (the last three are Host tier, so this package brings ASP.NET Core into the test project) |
| Namespaces | `SharedKernel.Testing.Security` |

## Quick start

```csharp
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Security;
using Xunit;

// The code under test: a policy that reads the caller.
public sealed class RefundPolicy(IUserContext user)
{
    public bool CanRefund(decimal amount) =>
        user.HasPermission("refunds.write") && (amount <= 500m || user.HasRole("finance-lead"));
}

public sealed class RefundPolicyTests
{
    [Fact]
    public void Large_refund_needs_the_finance_lead_role()
    {
        var clerk = new FakeUserContext
        {
            TenantId = new TenantId(Guid.NewGuid()),
            Permissions = ["refunds.write"],
        };
        var lead = new SecurityTestContextBuilder()
            .WithPermissions("refunds.write")
            .WithRoles("finance-lead")
            .BuildUserContext();

        Assert.False(new RefundPolicy(clerk).CanRefund(2_000m));
        Assert.True(new RefundPolicy(lead).CanRefund(2_000m));
    }

    [Fact]
    public void Anonymous_caller_is_not_authenticated()
    {
        var anonymous = new FakeUserContext { ActorKind = ActorKind.Anonymous };

        Assert.False(anonymous.IsAuthenticated);
    }
}
```

In a DI-based test, register the double as the service the code resolves:
`services.AddSingleton<IUserContext>(user)`. This package has no `Add*` helpers.

## How it works

- **`FakeUserContext` answers as the real `UserContext` does.** It defaults to an authenticated `ActorKind.User`
  with `SubjectId = FakeUserContext.DefaultSubjectId`; `IsAuthenticated` is `ActorKind != Anonymous`. `HasRole`,
  `HasPermission`, `WasAuthenticatedWith`, `FindClaim` and `FindClaims` compare ordinally.
  `IsAuthenticationFresherThan(maxAge, now)` applies the same future-skew limit as `UserContext`.
  `GetAuthenticationMethodTime(method)` returns `null` unless the method is in `AuthenticationMethods`, then its time
  from `AuthenticationMethodTimes`, or `AuthTime` when it has none.
- **`SecurityTestContextBuilder.Build()`** emits the short claim names of `SecurityClaimTypes` (`sub`, `azp`,
  `tenant_id`, `sid`, `name`, `email`, `acr`, `auth_time`, `scope`, `roles`, `amr`, `amr_time`) under the `Bearer`
  authentication type — what `SharedKernel.Security.Oidc` produces with default claim settings. A service actor gets
  `idtyp=app`; `Unauthenticated()` gives an identity without an authentication type. `BuildUserContext()` returns a
  `FakeUserContext` whose `Claims` are exactly those claims; an anonymous or system caller gets no `SubjectId`.
- **Stores are thread-safe** (`ConcurrentDictionary`), so one instance can be shared across parallel requests of an
  in-process host. `FakeUserContext` and the builders are plain mutable objects: one per test.
- **Documented simplifications:**
  - `InMemoryDpopReplayCache` never expires an entry; `expiresAt` is ignored.
  - `InMemoryTotpStepUpStore` keeps the last `verifiedAt` per subject **and** session and ignores `expiresAt`; step-up
    freshness is decided by the code reading it.
  - `DpopTestProofBuilder` defaults are fixed — `iat` 2024-01-01T00:00:00Z, access token `dpop-test-access-token`, a
    sequential `jti` — so pair the proof with a fake clock or set `WithIssuedAt`.
  - `MtlsTestCertificateBuilder` defaults to a fixed validity window (2024-01-01 to 2034-01-01). A revoked
    certificate's CRL is returned as bytes; no CRL distribution point is reachable, so feed
    `RevocationList` into your own chain policy — online revocation fetching is not reproduced.

## Recipes

### 1. Test a step-up with a maximum age

```csharp
var clock = new FakeClock(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));   // SharedKernel.Testing.Clocks

var fresh = new FakeUserContext { AuthenticationMethods = ["pwd", "otp"] }
    .WithAuthenticationMethodTime("otp", clock.UtcNow.AddMinutes(-2));
var expired = new FakeUserContext { AuthenticationMethods = ["pwd", "otp"] }
    .WithAuthenticationMethodTime("otp", clock.UtcNow.AddMinutes(-6));
```

Against `[RequireAuthenticationMethod("otp", MaxAgeSeconds = 300)]`, `fresh` passes and `expired` is refused. A time
only dates a method — list it in `AuthenticationMethods` too, or it is ignored.

### 2. Authenticate a request in a test host with a principal

```csharp
ClaimsPrincipal principal = new SecurityTestContextBuilder()
    .WithTenantId(tenantId)
    .WithPermissions("orders.read", "orders.write")
    .WithAuthenticationMethods("pwd")
    .WithAuthTime(clock.UtcNow.AddMinutes(-5))
    .Build();
```

Return it from a test authentication handler so the registered `IUserContextMapper` maps it as it would a real token.

### 3. Issue managed API keys against an in-memory store

```csharp
var store = new InMemoryApiKeyStore();
services.AddSingleton<IApiKeyStore>(store);   // before AddManagedApiKeyAuthentication, which only TryAdds the store
services.AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(keys => keys.Prefix = "acme_test");

// after the host is built
GeneratedApiKey key = provider.GetRequiredService<ApiKeyGenerator>().Generate();
store.Add(key, "billing-service", tenantId, permissions: ["invoices.read"]);
// send key.Key in the API-key header; later: store.Revoke(key.KeyId, clock.UtcNow)
```

### 4. Send a DPoP-bound request

```csharp
var proof = new DpopTestProofBuilder()
    .WithHttpMethod("POST")
    .WithHttpUri("https://api.example.test/orders")
    .WithAccessToken(accessToken)          // its cnf.jkt must be proof.JwkThumbprint
    .WithIssuedAt(clock.UtcNow)
    .Build();

request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", proof.AccessToken);
request.Headers.Add("DPoP", proof.ProofJwt);
```

Register `services.AddSingleton<IDpopReplayCache>(new InMemoryDpopReplayCache())` before `AddDpop<…>()`: `AddDpop`
only TryAdds the cache as scoped, which would give every request an empty cache and hide replays. Negative cases:
`WithMissingAth()`, `WithMismatchedAth()`, `WithMalformedAth(raw)`, `WithType("jwt")`; share a key across proofs with
`WithKey(ecdsa)`.

### 5. Present an mTLS client certificate

```csharp
MtlsTestCertificate chained = new MtlsTestCertificateBuilder().AsChainedFromEphemeralCa().Build();
MtlsTestCertificate revoked = new MtlsTestCertificateBuilder().AsRevoked().Build();
MtlsTestCertificate selfSigned = new MtlsTestCertificateBuilder().WithSubjectName("CN=rogue").Build();
```

Trust `chained.IssuingCertificate` through your validator's custom trust store; `revoked.RevocationList` is a
DER-encoded CRL listing `revoked.Certificate`'s serial.

## Reference

### Types

| Type | Implements / builds | Members worth knowing |
| --- | --- | --- |
| `FakeUserContext` | `IUserContext` | Settable `ActorKind`, `SubjectId`, `ClientId`, `TenantId` (`TenantId?`), `SessionId`, `Name`, `Email`, `Roles`, `Permissions`, `AuthenticationMethods`, `AuthenticationMethodTimes`, `AuthContextClassReference`, `AuthTime`, `IsSenderConstrained`, `Claims`; `WithAuthenticationMethodTime(method, verifiedAt)`; `const DefaultSubjectId` |
| `SecurityTestContextBuilder` | `ClaimsPrincipal` / `FakeUserContext` | `WithSubjectId`, `WithClientId`, `WithTenantId`, `WithSessionId`, `WithName`, `WithEmail`, `WithRoles(params)`, `WithPermissions(params)`, `WithAuthenticationMethods(params)`, `WithAuthenticationMethodTime`, `WithAuthContextClassReference`, `WithAuthTime`, `WithActorKind`, `Unauthenticated()`, `WithClaim(type, value)`; `Build()`, `BuildUserContext()` |
| `InMemoryApiKeyStore` | `IApiKeyStore` (`SharedKernel.Security.ApiKey.Keys`) | `Add(ApiKeyRecord)` (adds or replaces), `Add(GeneratedApiKey, clientId, tenantId?, permissions?, expiresAt?)` → the stored `ApiKeyRecord`, `Revoke(keyId, revokedAt)` (throws `KeyNotFoundException` for an unknown id) |
| `InMemoryDpopReplayCache` | `IDpopReplayCache` (`SharedKernel.Security.Oidc.Dpop`) | `TryAddAsync` → `false` for a proof id seen before; `Count` |
| `InMemoryTotpStepUpStore` | `ITotpStepUpStore` (`SharedKernel.Security.Totp`) | Keyed by subject and session; `Clear()` |
| `InMemoryRecoveryCodeStore` | `IRecoveryCodeStore` (`SharedKernel.Security.Totp`) | `Save(subjectId, codes)` replaces a user's codes; `TryMarkUsedAsync` removes a code atomically and returns `false` the second time |
| `DpopTestProofBuilder` → `DpopTestProof` | a client's DPoP proof | `WithKey(ECDsa)`, `WithHttpMethod(htm = "POST")`, `WithHttpUri`, `WithIssuedAt`, `WithJti`, `WithAccessToken`, `WithNonce`, `WithType`, `WithMissingAth()`, `WithMismatchedAth()`, `WithMalformedAth(raw)`; result: `ProofJwt`, `AccessToken`, `PublicJwk`, `JwkThumbprint` |
| `MtlsTestCertificateBuilder` → `MtlsTestCertificate` | an X.509 client certificate (ECDsa P-256, client-authentication EKU) | `AsSelfSigned()` (default), `AsChainedFromEphemeralCa()`, `AsRevoked()`, `WithSubjectName(= "CN=mtls-test-client")`, `WithValidityPeriod`; result: `Certificate` (private key included), `IssuingCertificate`, `RevocationList`, `IsSelfSigned` |

The doubles return no `Error` codes of their own: the production handlers that consume them do.

## Testing

This package is the test double; its self-tests live in
[`SharedKernel.Security.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Security.Testing/SharedKernel.Security.Testing.Tests),
which prove each double against the production contract — including a step-up evaluated by the platform's own
`[RequireAuthenticationMethod]` authorization handler. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Testing/README.md):
`FakeClock` for `auth_time` and step-up ages, and `TestRequestContext` when the code under test reads
`IRequestContext` rather than `IUserContext` (application code should).

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Give application code a `FakeUserContext` to read the tenant | Use `TestRequestContext` from `SharedKernel.Testing` | Application code reads `IRequestContext`; `IUserContext` is the authentication layer |
| Call `WithAuthenticationMethodTime("otp", …)` without listing `otp` | Also set `AuthenticationMethods = [..., "otp"]` | A time only dates a method, as on the real `UserContext`; an unlisted method reads as never used |
| Let `AddDpop` register the replay cache | `AddSingleton<IDpopReplayCache>(new InMemoryDpopReplayCache())` first | The default registration is scoped, so each request gets an empty cache and a replay passes |
| Assert on step-up expiry through `InMemoryTotpStepUpStore` | Drive the age with `FakeClock` and the reader's own freshness window | The store ignores `expiresAt` |
| Use `DateTimeOffset.UtcNow` with DPoP proofs or certificates | Use a `FakeClock` and pass its time to `WithIssuedAt`/`WithValidityPeriod` | The defaults are fixed dates; mixing them with wall-clock time makes tests flaky |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
