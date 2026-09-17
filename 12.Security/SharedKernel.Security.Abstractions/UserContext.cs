using System.Security.Claims;

namespace SharedKernel.Security.Abstractions;

/// <summary>
/// An immutable <see cref="IUserContext"/> for an authenticated user or service principal, built by an
/// <see cref="IUserContextMapper"/>.
/// </summary>
public sealed class UserContext : IUserContext
{
    private readonly IReadOnlyList<Claim> _claims;

    /// <summary>Creates a context for an authenticated caller.</summary>
    /// <param name="identityKind">
    /// <see cref="Abstractions.IdentityKind.User"/> or <see cref="Abstractions.IdentityKind.ServicePrincipal"/>.
    /// Use <see cref="AnonymousUserContext"/> and <see cref="SystemUserContext"/> for the other kinds.
    /// </param>
    /// <param name="subjectId">The subject identifier. Must not be empty.</param>
    /// <param name="claims">The claims <see cref="FindClaim"/> and <see cref="FindClaims"/> search.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="identityKind"/> is not User or ServicePrincipal.</exception>
    /// <exception cref="ArgumentException"><paramref name="subjectId"/> is null, empty or whitespace.</exception>
    public UserContext(IdentityKind identityKind, string subjectId, IEnumerable<Claim>? claims = null)
    {
        if (identityKind is not (IdentityKind.User or IdentityKind.ServicePrincipal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(identityKind),
                identityKind,
                "Only User and ServicePrincipal contexts carry a subject; use AnonymousUserContext or SystemUserContext.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        IdentityKind = identityKind;
        SubjectId = subjectId;
        _claims = claims is null ? [] : [.. claims];
    }

    /// <inheritdoc/>
    public IdentityKind IdentityKind { get; }

    /// <inheritdoc/>
    public bool IsAuthenticated => true;

    /// <inheritdoc/>
    public string? SubjectId { get; }

    /// <inheritdoc/>
    public string? ClientId { get; init; }

    /// <inheritdoc/>
    public Guid? TenantId { get; init; }

    /// <inheritdoc/>
    public string? SessionId { get; init; }

    /// <inheritdoc/>
    public string? Name { get; init; }

    /// <inheritdoc/>
    public string? Email { get; init; }

    /// <inheritdoc/>
    /// <remarks>The collection is copied when set.</remarks>
    public IReadOnlyCollection<string> Roles { get; init => field = Copy(value); } = [];

    /// <inheritdoc/>
    /// <remarks>The collection is copied when set.</remarks>
    public IReadOnlyCollection<string> Permissions { get; init => field = Copy(value); } = [];

    /// <inheritdoc/>
    /// <remarks>The collection is copied when set.</remarks>
    public IReadOnlyCollection<string> AuthenticationMethods { get; init => field = Copy(value); } = [];

    /// <inheritdoc/>
    public string? AuthContextClassReference { get; init; }

    /// <inheritdoc/>
    public DateTimeOffset? AuthTime { get; init; }

    /// <inheritdoc/>
    public bool IsSenderConstrained { get; init; }

    /// <inheritdoc/>
    public string? FindClaim(string claimType)
    {
        ArgumentNullException.ThrowIfNull(claimType);

        foreach (Claim claim in _claims)
        {
            if (string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            {
                return claim.Value;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> FindClaims(string claimType)
    {
        ArgumentNullException.ThrowIfNull(claimType);

        List<string>? values = null;
        foreach (Claim claim in _claims)
        {
            if (string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            {
                (values ??= []).Add(claim.Value);
            }
        }

        return values is null ? [] : values;
    }

    /// <inheritdoc/>
    public bool HasRole(string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return Roles.Contains(role, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public bool HasPermission(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return Permissions.Contains(permission, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public bool WasAuthenticatedWith(string method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return AuthenticationMethods.Contains(method, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An <see cref="AuthTime"/> more than <see cref="MaxFutureAuthTime"/> after <paramref name="now"/> is not fresh:
    /// a clock that far ahead, or a forged value, must not pass a step-up check for ever.
    /// </remarks>
    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) =>
        AuthTime is { } authTime && authTime - now <= MaxFutureAuthTime && now - authTime <= maxAge;

    /// <summary>The largest clock difference tolerated for an <see cref="AuthTime"/> after the current time: five minutes.</summary>
    public static TimeSpan MaxFutureAuthTime { get; } = TimeSpan.FromMinutes(5);

    private static string[] Copy(IReadOnlyCollection<string> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return [.. value];
    }
}
