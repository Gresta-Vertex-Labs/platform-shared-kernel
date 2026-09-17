using SharedKernel.Security.Abstractions;

namespace SharedKernel.Testing.Security;

/// <summary>A settable <see cref="IUserContext"/> for unit tests.</summary>
/// <remarks>
/// Defaults to an authenticated <see cref="IdentityKind.User"/> with <see cref="DefaultSubjectId"/>, so most tests
/// need no setup. <see cref="IsAuthenticated"/> follows <see cref="IdentityKind"/>, as on every real context; set
/// <see cref="IdentityKind"/> to <see cref="IdentityKind.Anonymous"/> for an unauthenticated caller.
/// </remarks>
public sealed class FakeUserContext : IUserContext
{
    /// <summary>The default subject id.</summary>
    public const string DefaultSubjectId = "11111111-1111-1111-1111-111111111111";

    /// <inheritdoc/>
    public IdentityKind IdentityKind { get; set; } = IdentityKind.User;

    /// <inheritdoc/>
    public bool IsAuthenticated => IdentityKind != IdentityKind.Anonymous;

    /// <inheritdoc/>
    /// <remarks>Defaults to <see cref="DefaultSubjectId"/>.</remarks>
    public string? SubjectId { get; set; } = DefaultSubjectId;

    /// <inheritdoc/>
    public string? ClientId { get; set; }

    /// <inheritdoc/>
    public Guid? TenantId { get; set; }

    /// <inheritdoc/>
    public string? SessionId { get; set; }

    /// <inheritdoc/>
    public string? Name { get; set; }

    /// <inheritdoc/>
    public string? Email { get; set; }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Roles { get; set; } = [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Permissions { get; set; } = [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> AuthenticationMethods { get; set; } = [];

    /// <inheritdoc/>
    public string? AuthContextClassReference { get; set; }

    /// <inheritdoc/>
    public DateTimeOffset? AuthTime { get; set; }

    /// <inheritdoc/>
    public bool IsSenderConstrained { get; set; }

    /// <summary>Gets or sets the claims searched by <see cref="FindClaim"/> and <see cref="FindClaims"/>, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Claims { get; set; } = [];

    /// <inheritdoc/>
    public string? FindClaim(string claimType)
    {
        ArgumentNullException.ThrowIfNull(claimType);
        return Claims.FirstOrDefault(claim => string.Equals(claim.Key, claimType, StringComparison.Ordinal)).Value;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> FindClaims(string claimType)
    {
        ArgumentNullException.ThrowIfNull(claimType);
        return [.. Claims.Where(claim => string.Equals(claim.Key, claimType, StringComparison.Ordinal)).Select(claim => claim.Value)];
    }

    /// <inheritdoc/>
    public bool HasRole(string role) => Roles.Contains(role, StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool WasAuthenticatedWith(string method) => AuthenticationMethods.Contains(method, StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) =>
        AuthTime is { } authTime && authTime - now <= UserContext.MaxFutureAuthTime && now - authTime <= maxAge;
}
