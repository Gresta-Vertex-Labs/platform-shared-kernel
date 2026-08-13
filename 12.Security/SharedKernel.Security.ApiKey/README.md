# SharedKernel.Security.ApiKey

Pre-shared-key / machine-client authentication for the SharedKernel. A sibling provider package to `SharedKernel.Security.Oidc`, never a dependent of it — API-key authentication is not an OIDC concept, so this package never references `.Oidc` and composes alongside JWT Bearer via a policy/forwarding scheme instead.

> **This package is for pre-shared-key/machine-client scenarios only.** It explicitly does **not** attempt key issuance, rotation, or storage — those remain the consuming service's own concern. The consuming service supplies a database-, configuration-, or secret-store-backed `IApiKeyValidator`; this package never dictates which.

## Public Surface

| Type | Kind | Purpose |
| --- | --- | --- |
| `IApiKeyValidator` | Interface | The sole consumer-supplied extensibility point — validates a presented key |
| `ApiKeyValidationResult` | Sealed class | Outcome of a validation attempt — valid/invalid, optional client id/roles/permissions |
| `ApiKeyUserContext` | Sealed class | Maps a successfully-authenticated API-key `ClaimsPrincipal` to `IUserContext` |
| `ApiKeyAuthenticationOptions` | Sealed class | Header/query-parameter names, scheme names |
| `ApiKeyAuthenticationHandler` | `AuthenticationHandler<ApiKeyAuthenticationOptions>` | Reads the presented key, delegates validation, produces the authenticated `ClaimsPrincipal` |
| `ApiKeyServiceCollectionExtensions` | Static class | `AddApiKeyAuthentication<TValidator>` DI extension method |

## Usage

Implement `IApiKeyValidator` against whatever storage your service already uses for API keys — a database table, a secret store, or (for a small, static fleet) configuration:

```csharp
public sealed class DatabaseApiKeyValidator(IApiKeyRepository repository) : IApiKeyValidator
{
    public async Task<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken)
    {
        // Prefer a hashed lookup over storing keys in plaintext — see the interface's own XML docs.
        var record = await repository.FindByHashAsync(Hash(presentedKey), cancellationToken);
        if (record is null)
        {
            return ApiKeyValidationResult.Invalid;
        }

        return ApiKeyValidationResult.Valid(
            clientId: record.ClientId,
            roles: record.Roles,
            permissions: record.Permissions);
    }

    private static string Hash(string key) => /* e.g. IOneWayHasher from 01.Core/SharedKernel.Cryptography */;
}
```

## DI Registration

Register **after** `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` so the scheme-aware `IUserContext` factory this package installs can correctly delegate to the already-registered OIDC-backed factory for non-API-key-authenticated requests:

```csharp
services.AddSharedKernelSecurity(configuration);
services.AddApiKeyAuthentication<DatabaseApiKeyValidator>(options =>
{
    options.HeaderName = "X-Api-Key";          // default
    options.QueryParameterName = "api_key";    // optional fallback; null disables it
});
```

A request presenting a valid API key resolves `IUserContext.IdentityKind == IdentityKind.ServicePrincipal`, `IsAuthenticated == true`, `UserId == Guid.Empty` (the standard machine-to-machine identity shape). A request presenting a valid JWT still resolves via `OidcUserContext` as before — the policy/forwarding scheme selector picks whichever credential the request actually presented, so a host can accept either type on the same set of endpoints without one scheme silently shadowing the other.

If `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` registered the JWT Bearer scheme under a non-default name, pass it explicitly:

```csharp
services.AddApiKeyAuthentication<DatabaseApiKeyValidator>(fallbackAuthenticationScheme: "MyCustomBearerScheme");
```

## Security notes

- **Constant-time comparison** is used wherever this package genuinely holds both sides of a comparison: when a key is presented via *both* the configured header and query parameter on the same request, the two *presented* values are compared against each other (never a stored secret, which this package never holds) via a constant-time comparer built on `01.Core/SharedKernel.Cryptography`'s `IHmacSigner` — never `string.Equals`/`==`. A mismatch fails authentication as a possible credential-confusion attack.
- The actual presented-key-vs-stored-secret comparison is entirely `IApiKeyValidator`'s own concern, invisible to this package. Prefer a hashed lookup over plaintext key storage.
- An invalid, absent, or ambiguous key never resolves to an authenticated context — authentication fails outright, it never falls through to `AnonymousUserContext` treated as "maybe authenticated."
