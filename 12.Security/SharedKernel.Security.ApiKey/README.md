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

## Rotation-window recipe (WO-060)

Rotating an API key without an outage requires a window during which **both** the old and the new key are simultaneously valid for the same client: issue the new key, keep the old key accepted for a bounded overlap period, then revoke the old key once every caller has migrated. A single-key `IApiKeyValidator` (comparing the presented key against exactly one stored value) cannot express this — there are multiple simultaneously-valid candidates, not one.

`ApiKeyRotationComparer.AnyMatch(string presented, IReadOnlyList<string> candidates)` is the sanctioned way to validate against more than one active key per client. It is a **new public type**, deliberately distinct from the package-internal `ConstantTimeKeyComparer` used by `ApiKeyAuthenticationHandler`'s own header-vs-query ambiguity check — your `IApiKeyValidator` implementation lives in your own assembly and cannot reach an internal type, so this comparer is public specifically so it compiles there. It is built on the same `IHmacSigner`-based constant-time technique, and it **always evaluates every candidate — never short-circuits on the first match** — so elapsed comparison time never correlates with which key, or how many keys, matched.

```csharp
public sealed class RotationAwareApiKeyValidator(IActiveApiKeyStore store) : IApiKeyValidator
{
    public async Task<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken)
    {
        // Your own storage supplies every currently-active key for a client — typically the
        // current key plus, during a bounded rotation grace window, one not-yet-retired previous
        // key. Narrow to a single client's candidates wherever your key shape allows it (e.g. a
        // "{clientId}.{secret}" prefix) rather than scanning every client, as shown here for clarity.
        foreach (var (clientId, candidates) in await store.GetAllActiveKeysAsync(cancellationToken))
        {
            if (ApiKeyRotationComparer.AnyMatch(presentedKey, candidates))
            {
                return ApiKeyValidationResult.Valid(clientId: clientId);
            }
        }

        return ApiKeyValidationResult.Invalid;
    }
}
```

The full rotation flow, entirely your own storage's concern — this package never dictates it:

1. **Issue** a new key for the client; your store now returns `[oldKey, newKey]` for that client's active set. Both are accepted immediately — no coordinated cutover, no downtime for callers still using the old key.
2. **Dual-valid window**: callers migrate to the new key at their own pace, bounded by whatever grace period your service's key-rotation policy defines. `AnyMatch` accepts either during this window.
3. **Revoke** the old key by removing it from the store's active set for that client. Only the new key is accepted from that point on.

A worked, non-production reference implementation of this exact pattern — backed by an in-memory stand-in for "your own storage" — ships inside this package at `Samples/RotationWindowApiKeyValidatorSample.cs`, exercised by this package's own test suite. It is not a shipped production type (it is `internal`); read it alongside this recipe rather than as a drop-in replacement for your own `IApiKeyValidator`.

As with every other extensibility point in this package, `ApiKeyRotationComparer` **never dictates key storage** — it only closes the "every consuming team reinvents constant-time multi-candidate comparison and likely gets the timing side-channel wrong" gap. Where the keys themselves live (a two-row table, a JSON array column, a secret manager with versioned entries) remains entirely your own choice.

## Security notes

- **Constant-time comparison** is used wherever this package genuinely holds both sides of a comparison: when a key is presented via *both* the configured header and query parameter on the same request, the two *presented* values are compared against each other (never a stored secret, which this package never holds) via a constant-time comparer built on `01.Core/SharedKernel.Cryptography`'s `IHmacSigner` — never `string.Equals`/`==`. A mismatch fails authentication as a possible credential-confusion attack.
- The actual presented-key-vs-stored-secret comparison is entirely `IApiKeyValidator`'s own concern, invisible to this package. Prefer a hashed lookup over plaintext key storage.
- An invalid, absent, or ambiguous key never resolves to an authenticated context — authentication fails outright, it never falls through to `AnonymousUserContext` treated as "maybe authenticated."
