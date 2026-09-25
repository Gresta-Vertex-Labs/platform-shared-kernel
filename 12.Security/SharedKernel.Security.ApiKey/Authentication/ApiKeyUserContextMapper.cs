using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Context;
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

        return new UserContext(ActorKind.Service, clientId, identity.Claims)
        {
            ClientId = clientId,
            TenantId = TenantId.TryParse(identity.FindFirst(SecurityClaimTypes.TenantId)?.Value, out TenantId tenantId) ? tenantId : null,
            Roles = [.. identity.FindAll(SecurityClaimTypes.Roles).Select(claim => claim.Value)],
            Permissions = [.. identity.FindAll(SecurityClaimTypes.Scope).Select(claim => claim.Value)],
        };
    }
}
