using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography;
using SharedKernel.Security.ApiKey.Logging;
using SharedKernel.Security.ApiKey.Options;

namespace SharedKernel.Security.ApiKey.Validation;

/// <summary>
/// Authenticates a request presenting a pre-shared API key, delegating the actual credential check to a
/// consumer-supplied <see cref="IApiKeyValidator"/>.
/// </summary>
/// <remarks>
/// <para>
/// The key is read from <see cref="ApiKeyAuthenticationOptions.HeaderName"/> and, when configured, the
/// <see cref="ApiKeyAuthenticationOptions.QueryParameterName"/> query-string parameter. If both are
/// present and their values differ, authentication fails outright — a possible credential-confusion
/// attack — detected with <c>SharedKernel.Cryptography</c>'s <see cref="FixedTimeComparison.AreEqual(string, string)"/>,
/// never <c>string.Equals</c>/<c>==</c>.
/// </para>
/// <para>
/// On a successful <see cref="IApiKeyValidator.ValidateAsync"/> match, produces an authenticated
/// <see cref="ClaimsPrincipal"/> (mappable to <see cref="ApiKeyUserContext"/>,
/// <c>IdentityKind.ServicePrincipal</c>, <c>IsAuthenticated = true</c>). On an invalid, absent, or
/// ambiguous key, authentication fails — it never falls through to an authenticated context.
/// </para>
/// </remarks>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IApiKeyValidator validator,
    ILogger<ApiKeyAuthenticationHandler> auditLogger)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, loggerFactory, encoder)
{
    /// <inheritdoc/>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headerValue = Request.Headers.TryGetValue(Options.HeaderName, out var headerValues)
            ? headerValues.ToString()
            : null;

        string? queryValue = null;
        if (!string.IsNullOrEmpty(Options.QueryParameterName)
            && Request.Query.TryGetValue(Options.QueryParameterName, out var queryValues))
        {
            queryValue = queryValues.ToString();
        }

        if (!TrySelectPresentedKey(headerValue, queryValue, out var presentedKey))
        {
            SecurityLogEvents.ApiKeyValidationFailed(auditLogger, "AmbiguousCredential");
            return AuthenticateResult.Fail("Ambiguous API key credential.");
        }

        if (string.IsNullOrEmpty(presentedKey))
        {
            // No credential presented at all — not our scheme's concern for this request.
            return AuthenticateResult.NoResult();
        }

        var result = await validator.ValidateAsync(presentedKey, Context.RequestAborted).ConfigureAwait(false);
        if (!result.IsValid)
        {
            SecurityLogEvents.ApiKeyValidationFailed(auditLogger, "Rejected");
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var identity = new ClaimsIdentity(BuildClaims(result), Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private static bool TrySelectPresentedKey(string? headerValue, string? queryValue, out string? presentedKey)
    {
        if (!string.IsNullOrEmpty(headerValue) && !string.IsNullOrEmpty(queryValue))
        {
            if (!FixedTimeComparison.AreEqual(headerValue, queryValue))
            {
                presentedKey = null;
                return false;
            }

            presentedKey = headerValue;
            return true;
        }

        presentedKey = !string.IsNullOrEmpty(headerValue) ? headerValue : queryValue;
        return true;
    }

    private static IEnumerable<Claim> BuildClaims(ApiKeyValidationResult result)
    {
        if (!string.IsNullOrEmpty(result.ClientId))
        {
            yield return new Claim(ApiKeyClaimTypes.ClientId, result.ClientId);
        }

        if (result.Roles is not null)
        {
            foreach (var role in result.Roles)
            {
                yield return new Claim(SharedKernel.Security.Abstractions.Claims.SecurityClaimTypes.Role, role);
            }
        }

        if (result.Permissions is not null)
        {
            foreach (var permission in result.Permissions)
            {
                yield return new Claim(ApiKeyClaimTypes.Permission, permission);
            }
        }
    }
}
