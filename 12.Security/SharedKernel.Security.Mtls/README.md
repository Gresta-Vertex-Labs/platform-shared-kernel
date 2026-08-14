# SharedKernel.Security.Mtls

Mutual-TLS client-certificate authentication for the SharedKernel, targeting regulated Open Banking/PSD2-style external APIs (QWAC/QSEAL certificate-based mTLS demanded regardless of whatever internal service-mesh mTLS already authenticates pod-to-pod traffic). A sibling provider package to `SharedKernel.Security.Oidc` and `SharedKernel.Security.ApiKey` — never a dependent of either, and never referenced by them.

> **This package performs no certificate issuance, CA management, or revocation checking (CRL/OCSP) of its own.** The consuming service supplies an `IMtlsCertificateValidator` that decides trust-store and revocation policy entirely; this package only wires the certificate into the ASP.NET Core authentication pipeline and maps a successful match onto `IUserContext` — identical in spirit to `.ApiKey`'s key-issuance/rotation/storage disclaimer.

## Public Surface

| Type | Kind | Purpose |
| --- | --- | --- |
| `IMtlsCertificateValidator` | Interface | The sole consumer-supplied extensibility point — validates a presented client certificate |
| `MtlsValidationResult` | Sealed class | Outcome of a validation attempt — valid/invalid, optional client id/roles/permissions (mirrors `ApiKeyValidationResult` exactly) |
| `MtlsUserContext` | Sealed class | Maps a successfully-authenticated certificate `ClaimsPrincipal` to `IUserContext` (`IdentityKind.ServicePrincipal`, `IsAuthenticated = true`, `UserId = Guid.Empty`) |
| `MtlsAuthenticationOptions` | Sealed class | `AllowedCertificateTypes`/`RevocationMode` passed to the underlying `Microsoft.AspNetCore.Authentication.Certificate` handler |
| `MtlsServiceCollectionExtensions` | Static class | `AddMtlsAuthentication<TValidator>` DI extension method |

## Usage

Implement `IMtlsCertificateValidator` against whatever trust-store/allowlist your service already uses for client certificates — a database table of allowed thumbprints, a CA-issued-subject allowlist, or a secret-store-backed registry:

```csharp
public sealed class WhitelistedCertificateValidator(ITppCertificateRepository repository) : IMtlsCertificateValidator
{
    public async Task<MtlsValidationResult> ValidateAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        // This package performs NO CA/chain/revocation validation of its own — that trust decision
        // (thumbprint allowlist, CA-issued-subject match, CRL/OCSP check, etc.) is entirely yours.
        var record = await repository.FindByThumbprintAsync(certificate.Thumbprint, cancellationToken);
        if (record is null)
        {
            return MtlsValidationResult.Invalid;
        }

        return MtlsValidationResult.Valid(
            clientId: record.ClientId,
            roles: record.Roles,
            permissions: record.Permissions);
    }
}
```

## DI Registration

Register **after** `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` (and, if used, `AddApiKeyAuthentication`) so the scheme-aware `IUserContext` factory this package installs can correctly delegate to whichever factory was already registered for non-certificate-authenticated requests:

```csharp
services.AddSharedKernelSecurity(configuration);
services.AddMtlsAuthentication<WhitelistedCertificateValidator>(options =>
{
    options.AllowedCertificateTypes = CertificateTypes.All;   // default — full trust delegated to the validator
    options.RevocationMode = X509RevocationMode.NoCheck;      // default — this package does no CRL/OCSP of its own
});
```

A request presenting a certificate the validator accepts resolves `IUserContext.IdentityKind == IdentityKind.ServicePrincipal`, `IsAuthenticated == true`, `UserId == Guid.Empty` — the identical machine-to-machine identity shape `SharedKernel.Security.ApiKey`'s `ApiKeyUserContext` produces. A request presenting a valid JWT (with no certificate, or a certificate the validator rejects) still resolves via whichever `IUserContext` factory was registered before this call — the certificate-aware factory only takes over for requests actually authenticated on the `Certificate` scheme.

## RFC 8705 certificate-bound access tokens (`cnf.x5t#S256`)

Several Open Banking/PSD2 regimes additionally require binding a bearer access token to the presenting client certificate (RFC 8705) — proving the caller holding the token is the same caller that terminated the TLS connection, on top of (or instead of) `SharedKernel.Security.Oidc`'s DPoP mechanism. When **both** a client certificate and a bearer token are presented on the same request:

1. This package computes the SHA-256 thumbprint of the *actually-presented* certificate via `01.Core/SharedKernel.Cryptography`'s `IContentHasher`.
2. It compares that thumbprint, constant-time, against the bearer principal's `cnf.x5t#S256` confirmation claim — read generically off the `ClaimsPrincipal` produced by whichever scheme validated the token. **This package never references `SharedKernel.Security.Oidc`** — the binding check is decoupled entirely through claims inspection, mirroring this domain's sibling-packages-never-reference-each-other rule.
3. A mismatch rejects the request even when the underlying bearer token is otherwise fully valid. A request presenting *only* a certificate (no accompanying bearer token/`cnf` claim) skips this check entirely — there is nothing to bind against.

This is the FAPI 1.0-era sender-constraining mechanism several regional Open Banking regimes still mandate today, alongside or instead of DPoP (`SharedKernel.Security.Oidc`, RFC 9449) — `.Oidc`'s DPoP is this domain's *primary* recommended mechanism for its lower developer-integration burden, but a consumer may require either or both depending on its regulatory regime.

## Security notes

- **Constant-time comparison** is used for the `cnf.x5t#S256` binding check — a `ConstantTimeThumbprintComparer` built on `01.Core/SharedKernel.Cryptography`'s `IHmacSigner`, never `string.Equals`/`==`/`SequenceEqual`, mirroring `.ApiKey`'s `ConstantTimeKeyComparer` technique exactly.
- `AllowedCertificateTypes`/`RevocationMode` default to `CertificateTypes.All`/`X509RevocationMode.NoCheck` — this package deliberately relaxes the ASP.NET Core Certificate handler's own default chained-only/revocation-checked policy so that full trust and revocation control genuinely land with your `IMtlsCertificateValidator`, not a framework default you might not expect.
- A rejected, absent, or validator-invalid certificate never resolves to an authenticated context — authentication fails outright.
- This package does not attempt certificate issuance, CA management, or revocation checking (CRL/OCSP) — those remain the consuming service's own concern.
