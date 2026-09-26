using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.ApiKey.Logging;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Security.ApiKey.Validation;

namespace SharedKernel.Security.ApiKey.Authentication;

internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IApiKeyValidator validator)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out StringValues values) || StringValues.IsNullOrEmpty(values))
        {
            return AuthenticateResult.NoResult();
        }

        if (values.Count != 1)
        {
            ApiKeyLog.AmbiguousKey(Logger, Options.HeaderName);
            return AuthenticateResult.Fail("Invalid API key.");
        }

        string key = values[0]!.Trim();
        if (key.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        ApiKeyValidationResult result = await validator.ValidateAsync(key, Context.RequestAborted).ConfigureAwait(false);
        if (!result.IsValid)
        {
            ApiKeyLog.KeyRejected(Logger, result.FailureReason ?? "Rejected", result.KeyId);
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var claims = new List<Claim>
        {
            new(SecurityClaimTypes.Subject, result.ClientId!),
            new(SecurityClaimTypes.ClientId, result.ClientId!),
        };

        if (result.TenantId is { } tenantId)
        {
            claims.Add(new Claim(SecurityClaimTypes.TenantId, tenantId.ToString()));
        }

        if (result.KeyId is { } keyId)
        {
            claims.Add(new Claim(ApiKeyAuthenticationDefaults.KeyIdClaimType, keyId));
        }

        claims.AddRange(result.Roles.Select(role => new Claim(SecurityClaimTypes.Roles, role)));
        claims.AddRange(result.Permissions.Select(permission => new Claim(SecurityClaimTypes.Scope, permission)));

        var identity = new ClaimsIdentity(claims, Scheme.Name, SecurityClaimTypes.Subject, SecurityClaimTypes.Roles);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
