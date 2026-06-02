using System.Security.Claims;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Abstractions.Claims;

namespace SharedKernel.Security.Oidc.Mapping;

/// <summary>
/// Maps a <see cref="ClaimsPrincipal"/> to <see cref="IUserContext"/> for OIDC/JWT requests.
/// </summary>
/// <remarks>
/// <para>
/// Constructed per-request from <c>IHttpContextAccessor.HttpContext?.User</c>.
/// Never cached across requests.
/// </para>
/// <para>
/// If the <c>sub</c> claim is absent or cannot be parsed as a <see cref="Guid"/>,
/// <see cref="IsAuthenticated"/> is forced to <see langword="false"/> and
/// <see cref="UserId"/> is set to <see cref="Guid.Empty"/>. This upholds the invariant
/// that <see cref="UserId"/> is never <see cref="Guid.Empty"/> when authenticated.
/// </para>
/// <para>
/// AOT note: <see cref="ClaimsPrincipal.Claims"/> iteration does not use reflection on user types.
/// </para>
/// </remarks>
public sealed class OidcUserContext : IUserContext
{
    /// <inheritdoc/>
    public Guid UserId { get; }

    /// <inheritdoc/>
    public string? Email { get; }

    /// <inheritdoc/>
    public string? Username { get; }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Roles { get; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Claims { get; }

    /// <inheritdoc/>
    public bool IsAuthenticated { get; }

    /// <summary>
    /// Initialises a new <see cref="OidcUserContext"/> from the supplied <paramref name="principal"/>.
    /// </summary>
    /// <param name="principal">
    /// The <see cref="ClaimsPrincipal"/> representing the current request's identity.
    /// Must not be <see langword="null"/>.
    /// </param>
    public OidcUserContext(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // Build Claims dictionary first — first value per claim type wins.
        var claimsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var roles = new List<string>();

        foreach (var claim in principal.Claims)
        {
            if (claim.Type == SecurityClaimTypes.Role)
            {
                roles.Add(claim.Value);
            }

            claimsDict.TryAdd(claim.Type, claim.Value);
        }

        Claims = claimsDict;
        Roles = roles.AsReadOnly();

        // Resolve email and username from their dedicated claim types.
        Email = claimsDict.TryGetValue(SecurityClaimTypes.Email, out var email) ? email : null;
        Username = claimsDict.TryGetValue(ClaimTypes.Name, out var name) ? name : null;

        // Resolve UserId from the 'sub' claim. If absent or unparseable, force IsAuthenticated = false.
        var baseAuthenticated = principal.Identity?.IsAuthenticated ?? false;

        if (baseAuthenticated
            && claimsDict.TryGetValue(SecurityClaimTypes.UserId, out var sub)
            && Guid.TryParse(sub, out var userId)
            && userId != Guid.Empty)
        {
            UserId = userId;
            IsAuthenticated = true;
        }
        else
        {
            UserId = Guid.Empty;
            IsAuthenticated = false;
        }
    }

    /// <inheritdoc/>
    public bool HasRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
