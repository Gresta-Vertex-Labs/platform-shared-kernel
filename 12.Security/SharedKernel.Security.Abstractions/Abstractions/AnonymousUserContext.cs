namespace SharedKernel.Security.Abstractions.Abstractions;

/// <summary>
/// Sentinel implementation of <see cref="IUserContext"/> for unauthenticated requests.
/// </summary>
/// <remarks>
/// <para>
/// Registered by the OIDC package as the DI fallback so that <see cref="IUserContext"/> is
/// always resolvable regardless of authentication state. The scoped factory returns this
/// instance when no <c>HttpContext</c> is present.
/// </para>
/// <para>
/// All string properties are <see langword="null"/>; <see cref="Roles"/>, <see cref="Permissions"/>, and
/// <see cref="Claims"/> are empty read-only collections; <see cref="UserId"/> is <see cref="Guid.Empty"/>;
/// <see cref="IsAuthenticated"/> is <see langword="false"/>; <see cref="IdentityKind"/> is
/// <see cref="Security.Abstractions.Abstractions.IdentityKind.Anonymous"/>; <see cref="HasRole"/> and
/// <see cref="HasPermission"/> always return <see langword="false"/>.
/// </para>
/// <para>
/// A peer of <see cref="SystemUserContext"/>, not a replacement for it — this sentinel represents a
/// genuinely unauthenticated/rejected caller, while <see cref="SystemUserContext"/> represents a
/// trusted, non-HTTP execution authority.
/// </para>
/// </remarks>
public sealed class AnonymousUserContext : IUserContext
{
    /// <summary>Gets the singleton instance of <see cref="AnonymousUserContext"/>.</summary>
    public static readonly AnonymousUserContext Instance = new();

    /// <inheritdoc/>
    public Guid UserId => Guid.Empty;

    /// <inheritdoc/>
    public string? Email => null;

    /// <inheritdoc/>
    public string? Username => null;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Roles => [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Permissions => [];

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Claims => EmptyClaimsDictionary.Instance;

    /// <inheritdoc/>
    public bool IsAuthenticated => false;

    /// <inheritdoc/>
    public IdentityKind IdentityKind => IdentityKind.Anonymous;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool HasRole(string role) => false;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool HasPermission(string permission) => false;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> AuthenticationMethods => [];

    /// <inheritdoc/>
    public string? AuthContextClassReference => null;

    /// <inheritdoc/>
    public DateTimeOffset? AuthTime => null;

    /// <inheritdoc/>
    public bool IsSenderConstrained => false;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool WasAuthenticatedWith(string method) => false;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) => false;
}
