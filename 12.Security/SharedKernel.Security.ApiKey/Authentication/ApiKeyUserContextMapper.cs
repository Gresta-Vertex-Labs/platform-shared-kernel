using System.Security.Claims;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Security.ApiKey.Authentication;

internal sealed class ApiKeyUserContextMapper : IUserContextMapper
{
    public string AuthenticationType => ApiKeyAuthenticationDefaults.AuthenticationScheme;

    public IUserContext Map(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (identity.FindFirst(SecurityClaimTypes.Subject)?.Value is not { Length: > 0 } clientId)
        {
            return AnonymousUserContext.Instance;
        }

        return new UserContext(IdentityKind.ServicePrincipal, clientId, identity.Claims)
        {
            ClientId = clientId,
            TenantId = Guid.TryParse(identity.FindFirst(SecurityClaimTypes.TenantId)?.Value, out Guid tenantId) && tenantId != Guid.Empty ? tenantId : null,
            Roles = [.. identity.FindAll(SecurityClaimTypes.Roles).Select(claim => claim.Value)],
            Permissions = [.. identity.FindAll(SecurityClaimTypes.Scope).Select(claim => claim.Value)],
        };
    }
}
