using Microsoft.Extensions.Options;
using SharedKernel.Security.Oidc.Authentication;

namespace SharedKernel.Security.Oidc.Options;

// dpopRegistration is present when AddDpop was called; DPoP settings that need it fail closed at startup otherwise.
internal sealed class OidcAuthenticationOptionsValidator(DpopRegistration? dpopRegistration = null)
    : IValidateOptions<OidcAuthenticationOptions>
{
    internal static readonly HashSet<string> AsymmetricAlgorithms = new(StringComparer.Ordinal)
    {
        "RS256", "RS384", "RS512", "PS256", "PS384", "PS512", "ES256", "ES384", "ES512",
    };

    public ValidateOptionsResult Validate(string? name, OidcAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        // A rooted path parses as an absolute file URI on Linux, so the scheme is checked as well.
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out Uri? authority)
            || (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Authority must be an absolute URL.");
        }
        else if (options.RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("Authority must use https unless RequireHttpsMetadata is false.");
        }

        if (options.Audiences is null || options.Audiences.Count == 0 || options.Audiences.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Audiences must contain at least one value and no empty values.");
        }

        if (options.ValidIssuers?.Any(string.IsNullOrWhiteSpace) == true)
        {
            failures.Add("ValidIssuers must not contain empty values.");
        }

        if (options.ValidTokenTypes?.Any(string.IsNullOrWhiteSpace) == true)
        {
            failures.Add("ValidTokenTypes must not contain empty values.");
        }

        ValidateAlgorithms(options.ValidAlgorithms, "ValidAlgorithms", failures);
        ValidateRange(options.ClockSkew, TimeSpan.Zero, TimeSpan.FromMinutes(5), "ClockSkew", failures);
        ValidateClaims(options.Claims, failures);
        ValidateDpop(options.Dpop, failures);

        if (options.Revocation is null)
        {
            failures.Add("Revocation must not be null.");
        }
        else
        {
            ValidateRange(
                options.Revocation.NotRevokedCacheDuration, TimeSpan.Zero, TimeSpan.FromMinutes(5), "Revocation.NotRevokedCacheDuration", failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateClaims(OidcClaimOptions? claims, List<string> failures)
    {
        if (claims is null)
        {
            failures.Add("Claims must not be null.");
            return;
        }

        (string Value, string Name)[] single =
        [
            (claims.SubjectClaimType, nameof(claims.SubjectClaimType)),
            (claims.NameClaimType, nameof(claims.NameClaimType)),
            (claims.EmailClaimType, nameof(claims.EmailClaimType)),
            (claims.RoleClaimType, nameof(claims.RoleClaimType)),
            (claims.TenantClaimType, nameof(claims.TenantClaimType)),
            (claims.AuthenticationMethodClaimType, nameof(claims.AuthenticationMethodClaimType)),
            (claims.AuthContextClassReferenceClaimType, nameof(claims.AuthContextClassReferenceClaimType)),
            (claims.AuthTimeClaimType, nameof(claims.AuthTimeClaimType)),
        ];

        foreach ((string value, string claimName) in single)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                failures.Add($"Claims.{claimName} must not be empty.");
            }
        }

        (List<string> Values, string Name)[] lists =
        [
            (claims.PermissionClaimTypes, nameof(claims.PermissionClaimTypes)),
            (claims.ClientIdClaimTypes, nameof(claims.ClientIdClaimTypes)),
            (claims.SessionIdClaimTypes, nameof(claims.SessionIdClaimTypes)),
        ];

        foreach ((List<string> values, string listName) in lists)
        {
            if (values is null || values.Any(string.IsNullOrWhiteSpace))
            {
                failures.Add($"Claims.{listName} must not be null or contain empty values.");
            }
        }

        if (claims.ApplicationTokenClaims is null
            || claims.ApplicationTokenClaims.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrEmpty(pair.Value)))
        {
            failures.Add("Claims.ApplicationTokenClaims must not be null or contain empty types or values.");
        }
    }

    private void ValidateDpop(DpopOptions? dpop, List<string> failures)
    {
        if (dpop is null)
        {
            failures.Add("Dpop must not be null.");
            return;
        }

        if (!Enum.IsDefined(dpop.Mode))
        {
            failures.Add($"Dpop.Mode '{dpop.Mode}' is not defined.");
        }
        else if (dpopRegistration is null && (dpop.Mode == DpopMode.Required || dpop.RequireNonce))
        {
            failures.Add("Dpop.Mode Required and Dpop.RequireNonce need AddDpop; without it DPoP tokens are rejected and plain bearer tokens accepted.");
        }

        ValidateAlgorithms(dpop.ValidAlgorithms, "Dpop.ValidAlgorithms", failures);
        ValidateRange(dpop.ProofLifetime, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), "Dpop.ProofLifetime", failures);
        ValidateRange(dpop.ClockSkew, TimeSpan.Zero, TimeSpan.FromMinutes(1), "Dpop.ClockSkew", failures);
        ValidateRange(dpop.NonceLifetime, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1), "Dpop.NonceLifetime", failures);
    }

    private static void ValidateAlgorithms(List<string>? algorithms, string name, List<string> failures)
    {
        if (algorithms is null)
        {
            failures.Add($"{name} must not be null.");
            return;
        }

        foreach (string algorithm in algorithms)
        {
            if (!AsymmetricAlgorithms.Contains(algorithm))
            {
                failures.Add(
                    $"{name} contains '{algorithm}'. Allowed: {string.Join(", ", AsymmetricAlgorithms)}.");
            }
        }
    }

    private static void ValidateRange(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string name, List<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{name} must be between {minimum} and {maximum}.");
        }
    }
}
