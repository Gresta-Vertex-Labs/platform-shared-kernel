using System.Security.Claims;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Abstractions.Claims;

namespace SharedKernel.Security.ApiKey.Validation;

/// <summary>
/// Maps the <see cref="ClaimsPrincipal"/> produced by <see cref="ApiKeyAuthenticationHandler"/> on a
/// successful match to <see cref="IUserContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// A successfully authenticated API key always represents a machine-client identity — never a human
/// subject — so <see cref="IdentityKind"/> is always <see cref="Security.Abstractions.Abstractions.IdentityKind.ServicePrincipal"/>,
/// <see cref="IsAuthenticated"/> is always <see langword="true"/>, and <see cref="UserId"/> is always
/// <see cref="Guid.Empty"/> — the standard shape for a machine-to-machine identity (see the corrected
/// invariant on <see cref="IUserContext"/>, WO-057/P-367).
/// </para>
/// <para>
/// <see cref="Username"/> carries the client identifier (<see cref="ApiKeyValidationResult.ClientId"/>),
/// the closest <see cref="IUserContext"/> concept to "the identifier of who is calling".
/// </para>
/// </remarks>
public sealed class ApiKeyUserContext : IUserContext
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

    /// <summary>
    /// Initialises a new <see cref="ApiKeyUserContext"/> from the <see cref="ClaimsPrincipal"/> produced
    /// by <see cref="ApiKeyAuthenticationHandler"/>.
    /// </summary>
    /// <param name="principal">
    /// The <see cref="ClaimsPrincipal"/> produced on a successful API-key match. Must not be
    /// <see langword="null"/>.
    /// </param>
    public ApiKeyUserContext(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var claimsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var roles = new List<string>();
        var permissions = new List<string>();

        foreach (var claim in principal.Claims)
        {
            if (string.Equals(claim.Type, SecurityClaimTypes.Role, StringComparison.Ordinal))
            {
                roles.Add(claim.Value);
            }
            else if (string.Equals(claim.Type, ApiKeyClaimTypes.Permission, StringComparison.Ordinal))
            {
                permissions.Add(claim.Value);
            }

            claimsDict.TryAdd(claim.Type, claim.Value);
        }

        Claims = claimsDict;
        Roles = roles;
        Permissions = permissions;
        Username = claimsDict.TryGetValue(ApiKeyClaimTypes.ClientId, out var clientId) ? clientId : null;
    }

    /// <inheritdoc/>
    public bool HasRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
