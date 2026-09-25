using System.Security.Claims;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Security.Mtls.Authentication;

internal sealed class MtlsUserContextMapper : IUserContextMapper
{
    public string AuthenticationType => MtlsAuthenticationDefaults.AuthenticationScheme;

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

            // The handler issues no authentication method; a claims transformation may add one, with its time.
            AuthenticationMethods = [.. identity.FindAll(SecurityClaimTypes.AuthenticationMethod).Select(claim => claim.Value)],
            AuthenticationMethodTimes = AuthenticationMethodTimeClaim.Read(identity.Claims),
        };
    }
}
