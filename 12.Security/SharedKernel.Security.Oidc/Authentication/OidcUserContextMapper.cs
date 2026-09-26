using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Context;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc.Internal;
using SharedKernel.Security.Oidc.Logging;
using SharedKernel.Security.Oidc.Options;

namespace SharedKernel.Security.Oidc.Authentication;

internal sealed class OidcUserContextMapper(
    IOptionsMonitor<OidcAuthenticationOptions> options,
    ILogger<OidcUserContextMapper> logger) : IUserContextMapper
{
    public string AuthenticationType => OidcAuthenticationDefaults.AuthenticationScheme;

    public IUserContext Map(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        OidcClaimOptions claims = options.CurrentValue.Claims;
        string? subject = First(identity, claims.SubjectClaimType);
        string? clientId = FirstOf(identity, OidcDefaults.ClientIds(claims));

        bool applicationToken =
            OidcDefaults.ApplicationClaims(claims).Any(pair => identity.HasClaim(pair.Key, pair.Value))
            || (subject is not null && string.Equals(subject, clientId, StringComparison.Ordinal))
            || (subject is null && clientId is not null);

        string? subjectId = applicationToken ? subject ?? clientId : subject;
        if (subjectId is null)
        {
            OidcLog.SubjectMissing(logger);
            return AnonymousUserContext.Instance;
        }

        return new UserContext(applicationToken ? ActorKind.Service : ActorKind.User, subjectId, identity.Claims)
        {
            ClientId = clientId,
            TenantId = ReadTenant(identity, claims.TenantClaimType),
            SessionId = FirstOf(identity, OidcDefaults.SessionIds(claims)),
            Name = First(identity, claims.NameClaimType),
            Email = First(identity, claims.EmailClaimType),
            Roles = All(identity, claims.RoleClaimType),
            Permissions = SpaceDelimited(identity, OidcDefaults.Permissions(claims)),
            AuthenticationMethods = All(identity, claims.AuthenticationMethodClaimType),
            AuthenticationMethodTimes = AuthenticationMethodTimeClaim.Read(identity.Claims),
            AuthContextClassReference = First(identity, claims.AuthContextClassReferenceClaimType),
            AuthTime = ReadUnixTime(identity, claims.AuthTimeClaimType),
            IsSenderConstrained = TokenConfirmation.Read(identity).IsSenderConstrained,
        };
    }

    private TenantId? ReadTenant(ClaimsIdentity identity, string claimType)
    {
        string? value = First(identity, claimType);
        if (value is null)
        {
            return null;
        }

        if (TenantId.TryParse(value, out TenantId tenantId))
        {
            return tenantId;
        }

        OidcLog.TenantClaimInvalid(logger, claimType);
        return null;
    }

    private static string? First(ClaimsIdentity identity, string claimType)
    {
        string? value = identity.FindFirst(claimType)?.Value;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? FirstOf(ClaimsIdentity identity, IReadOnlyList<string> claimTypes)
    {
        foreach (string claimType in claimTypes)
        {
            if (First(identity, claimType) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static string[] All(ClaimsIdentity identity, string claimType) =>
        [.. identity.FindAll(claimType).Select(claim => claim.Value).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal)];

    private static string[] SpaceDelimited(ClaimsIdentity identity, IReadOnlyList<string> claimTypes) =>
        [.. claimTypes
            .SelectMany(identity.FindAll)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)];

    private static DateTimeOffset? ReadUnixTime(ClaimsIdentity identity, string claimType) =>
        long.TryParse(First(identity, claimType), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long seconds)
        && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
