# 12.Security

Identity and access abstractions for Platform.SharedKernel microservices — the current user's identity, the current tenant, and the machinery to authenticate a request via JWT/OIDC or a pre-shared API key.

Philosophy: **Thin abstractions. Claims-first. No domain coupling. Request-scoped identity.**

- **`SharedKernel.Security.Abstractions`** — `IUserContext`, `ITenantProvider`, `IdentityKind`, `AnonymousUserContext`/`SystemUserContext` sentinels, `SecurityClaimTypes`. Zero NuGet dependencies — the only types application and domain-adjacent code should ever inject.
- **`SharedKernel.Security.Oidc`** — the concrete JWT/OIDC implementation: `OidcUserContext`/`OidcTenantProvider`, configurable claims-to-context mapping, Azure B2C / Microsoft Entra External ID wiring, structured security-audit logging, and the `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` DI entry points.
- **`SharedKernel.Security.ApiKey`** — pre-shared-key / machine-client authentication. A sibling provider to `.Oidc`, never a dependent of it — composes alongside JWT Bearer via a policy/forwarding scheme so a host can accept either credential type on the same endpoints.
- **`SharedKernel.Security.Mtls`** — mutual-TLS client-certificate authentication for regulated Open Banking/PSD2-style external APIs. A fourth sibling provider, never a dependent of `.Oidc`/`.ApiKey` and never referenced by them — composes alongside either via the same policy/forwarding-scheme pattern `.ApiKey` established, and additionally enforces RFC 8705 certificate-bound (`cnf.x5t#S256`) access tokens when a bearer token accompanies the certificate.
- **`SharedKernel.Security.Totp`** — second-factor (TOTP) enrollment/challenge orchestration plus an `IClaimsTransformation`-based step-up wiring that makes a successful TOTP verification observable through `IUserContext.WasAuthenticatedWith`/`AuthenticationMethods` with zero changes to `.Oidc`. A fifth sibling provider, never a dependent of `.Oidc`/`.ApiKey`/`.Mtls` and never referenced by them — delegates every RFC 6238/4226 primitive to `01.Core/SharedKernel.Cryptography`, never reimplementing the algorithm itself.

Every microservice in the platform depends on `SharedKernel.Security.Abstractions` to read the current user and tenant — never on `.Oidc`, `.ApiKey`, or `.Mtls` directly. Only the host's composition root (`Program.cs`) references the concrete provider packages.

## Quick Start

```csharp
// Standard JWT / Microsoft Entra ID (non-B2C):
services.AddSharedKernelSecurity(configuration);

// Azure B2C / Microsoft Entra External ID:
services.AddAzureB2CAuthentication(configuration);

// Machine-client / pre-shared-key callers — composes alongside either call above:
services.AddApiKeyAuthentication<MyDatabaseBackedApiKeyValidator>();

// Certificate-based (mTLS) machine clients, e.g. regulated Open Banking TPPs — also composes
// alongside the calls above:
services.AddMtlsAuthentication<MyCertificateWhitelistValidator>();

// TOTP second-factor step-up — augments the OIDC-authenticated principal with an "otp" AMR claim
// after a fresh successful TOTP challenge. Chains onto 01.Core's cryptography builder and needs your own
// ITotpReplayGuard:
services.AddSingleton<ITotpReplayGuard, MyRedisTotpReplayGuard>();
services.AddSharedKernelCryptography(configuration).AddTotpStepUp<MyTotpChallengeStore>();

// In application code, inject the abstractions — never a concrete OIDC/ApiKey/Mtls/Totp type:
public sealed class MyCommandHandler(IUserContext user, ITenantProvider tenant)
{
    public Task Handle(MyCommand command, CancellationToken ct)
    {
        if (!user.IsAuthenticated)
        {
            return Task.FromException(new UnauthorizedAccessException());
        }

        var tenantId = tenant.TenantId; // pass as a Guid primitive to domain constructors
        // ...
    }
}
```

`IUserContext` and `ITenantProvider` are both **scoped** — one instance per HTTP request. Both are always resolvable: `AnonymousUserContext` is the registered fallback when no HTTP context is present (background workers, console hosts, unit-test DI containers). Background-execution hosts (Temporal activities, MassTransit consumers, hosted services) register `SystemUserContext` explicitly in place of the HTTP-derived factory — see the "Background-execution host" recipe in `SharedKernel.Security.Oidc/README.md`.

## Ordering rules

1. Call `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` first — it registers JWT Bearer authentication plus the scoped `IUserContext`/`ITenantProvider` factories. Both return a `SecurityAuthenticationBuilder` that can additionally chain `.RequireDpop<TReplayCache>()`/`.WithRevocationCheck<TCheck>()` — see `SharedKernel.Security.Oidc/README.md`'s Quick Starts.
2. `AddApiKeyAuthentication<TValidator>()` and `AddMtlsAuthentication<TValidator>()` are both optional and additive, and may be combined. Call each *after* step 1 so its scheme-aware `IUserContext` factory can correctly delegate to whichever factory was already registered for a request authenticated on a different scheme.
3. `AddTotpStepUp<TChallengeStore>()` is also optional and additive — call it after step 1 (so an authenticated JWT Bearer principal exists to transform) and chain it onto `01.Core`'s `AddSharedKernelCryptography(configuration)` builder, which supplies `ITotpGenerator`/`IRecoveryCodeGenerator`; it registers `ITotpVerifier` itself, and your service registers the `ITotpReplayGuard`. Unlike `.ApiKey`/`.Mtls`, it never authenticates a new primary identity — it augments an already-authenticated principal's `AuthenticationMethods`, and composes only with `.Oidc`'s `OidcUserContext`.
4. Application code injects `IUserContext`/`ITenantProvider` — never `IHttpContextAccessor`, `ClaimsPrincipal`, or `HttpContext` directly. Those are infrastructure details this domain exists to hide.

## Packages

| Package | README |
| --- | --- |
| `SharedKernel.Security.Abstractions` | [`SharedKernel.Security.Abstractions/README.md`](SharedKernel.Security.Abstractions/README.md) |
| `SharedKernel.Security.Oidc` | [`SharedKernel.Security.Oidc/README.md`](SharedKernel.Security.Oidc/README.md) — also holds the five end-to-end cross-domain recipes (`05.Application`, `06.Persistence`, `07.Messaging`, background-execution hosts, step-up authorization) plus the DPoP and token-revocation Quick Starts |
| `SharedKernel.Security.ApiKey` | [`SharedKernel.Security.ApiKey/README.md`](SharedKernel.Security.ApiKey/README.md) |
| `SharedKernel.Security.Mtls` | [`SharedKernel.Security.Mtls/README.md`](SharedKernel.Security.Mtls/README.md) |
| `SharedKernel.Security.Totp` | [`SharedKernel.Security.Totp/README.md`](SharedKernel.Security.Totp/README.md) |

See `12.Security/CLAUDE.md` for the full interface contracts, implementation rules, and AOT notes.
