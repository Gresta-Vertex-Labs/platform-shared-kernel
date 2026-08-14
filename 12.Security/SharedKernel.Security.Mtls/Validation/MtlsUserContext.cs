using System.Security.Claims;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Security.Mtls.Validation;

/// <summary>
/// Maps the <see cref="ClaimsPrincipal"/> produced on a successful <see cref="IMtlsCertificateValidator"/>
/// match to <see cref="IUserContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// A successfully authenticated client certificate always represents a machine-client identity — never
/// a human subject — so <see cref="IdentityKind"/> is always
/// <see cref="Security.Abstractions.Abstractions.IdentityKind.ServicePrincipal"/>,
/// <see cref="IsAuthenticated"/> is always <see langword="true"/>, and <see cref="UserId"/> is always
/// <see cref="Guid.Empty"/> — the identical shape to <c>SharedKernel.Security.ApiKey.Validation.ApiKeyUserContext</c>
/// (WO-058, P-377).
/// </para>
/// </remarks>
public sealed class MtlsUserContext : IUserContext
{
    /// <inheritdoc/>
    public Guid UserId => Guid.Empty;

    /// <inheritdoc/>
    public string? Email => null;

    /// <inheritdoc/>
    public string? Username { get; }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Roles { get; }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Permissions { get; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Claims { get; }

    /// <inheritdoc/>
    public bool IsAuthenticated => true;

    /// <inheritdoc/>
    public IdentityKind IdentityKind => IdentityKind.ServicePrincipal;

    /// <inheritdoc/>
    /// <remarks>Empty — no OIDC authentication-context concept for a certificate-based credential.</remarks>
    public IReadOnlyCollection<string> AuthenticationMethods => [];

    /// <inheritdoc/>
    /// <remarks>Always <see langword="null"/> — no OIDC authentication-context concept for a certificate-based credential.</remarks>
    public string? AuthContextClassReference => null;

    /// <inheritdoc/>
    /// <remarks>Always <see langword="null"/> — no OIDC authentication-context concept for a certificate-based credential.</remarks>
    public DateTimeOffset? AuthTime => null;

    /// <inheritdoc/>
    /// <remarks>Always <see langword="false"/> — mTLS is its own sender-constraining mechanism, distinct from DPoP.</remarks>
    public bool IsSenderConstrained => false;

    /// <summary>
    /// Initialises a new <see cref="MtlsUserContext"/> from the <see cref="ClaimsPrincipal"/> produced
    /// on a successful <see cref="IMtlsCertificateValidator"/> match.
    /// </summary>
    /// <param name="principal">
    /// The <see cref="ClaimsPrincipal"/> produced on a successful certificate match. Must not be
    /// <see langword="null"/>.
    /// </param>
    public MtlsUserContext(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claimsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var roles = new List<string>();
        var permissions = new List<string>();

        foreach (var claim in principal.Claims)
        {
            if (string.Equals(claim.Type, MtlsClaimTypes.Role, StringComparison.Ordinal))
            {
                roles.Add(claim.Value);
            }
            else if (string.Equals(claim.Type, MtlsClaimTypes.Permission, StringComparison.Ordinal))
            {
                permissions.Add(claim.Value);
            }

            claimsDict.TryAdd(claim.Type, claim.Value);
        }

        Claims = claimsDict;
        Roles = roles;
        Permissions = permissions;
        Username = claimsDict.TryGetValue(MtlsClaimTypes.ClientId, out var clientId) ? clientId : null;
    }

    /// <inheritdoc/>
    public bool HasRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool WasAuthenticatedWith(string method) => false;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) => false;
}
