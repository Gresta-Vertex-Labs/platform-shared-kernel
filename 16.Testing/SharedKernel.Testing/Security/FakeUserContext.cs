using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Testing.Security;

/// <summary>
/// In-memory fake implementation of <see cref="IUserContext"/> for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <c>12.Security</c>'s <c>AnonymousUserContext</c>, which is an immutable
/// production fallback sentinel (always <see cref="IsAuthenticated"/> == <see langword="false"/>).
/// <see cref="FakeUserContext"/> defaults to an <em>authenticated</em> user so most test setups
/// need zero configuration — call the property setters to exercise unauthenticated or
/// role-restricted paths explicitly.
/// </para>
/// </remarks>
public sealed class FakeUserContext : IUserContext
{
    private static readonly Guid DefaultUserId = new("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// Gets or sets the unique identifier of the authenticated user.
    /// </summary>
    /// <remarks>Defaults to a fixed, non-empty test <see cref="Guid"/>.</remarks>
    public Guid UserId { get; set; } = DefaultUserId;

    /// <summary>Gets or sets the email address of the authenticated user.</summary>
    public string? Email { get; set; }

    /// <summary>Gets or sets the username of the authenticated user.</summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the roles assigned to the authenticated user.
    /// </summary>
    /// <remarks>Defaults to an empty collection.</remarks>
    public IReadOnlyCollection<string> Roles { get; set; } = [];

    /// <summary>
    /// Gets or sets the permissions (fine-grained scopes) granted to the authenticated identity.
    /// </summary>
    /// <remarks>Defaults to an empty collection. Mirrors <see cref="Roles"/>'s shape exactly (WO-057, P-368).</remarks>
    public IReadOnlyCollection<string> Permissions { get; set; } = [];

    /// <summary>
    /// Gets or sets all claims carried by the current principal, keyed by claim type.
    /// </summary>
    /// <remarks>Defaults to an empty dictionary.</remarks>
    public IReadOnlyDictionary<string, string> Claims { get; set; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets a value indicating whether the current request is authenticated.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/> — the deliberate inverse of
    /// <c>AnonymousUserContext</c>'s always-<see langword="false"/> production sentinel — so most
    /// test setups need zero configuration to exercise the authenticated path.
    /// </remarks>
    public bool IsAuthenticated { get; set; } = true;

    /// <summary>
    /// Gets or sets the kind of identity represented by this context.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="IdentityKind.User"/>. This fake represents every <see cref="IdentityKind"/>
    /// value — including <see cref="IdentityKind.ServicePrincipal"/> and <see cref="IdentityKind.System"/>
    /// — through this one settable property rather than a dedicated sentinel type (WO-057, P-374). A test
    /// exercising a "system/background execution context" path constructs
    /// <c>new FakeUserContext { IdentityKind = IdentityKind.System }</c> (optionally with
    /// <c>UserId = Guid.Empty</c>, mirroring the production <c>SystemUserContext</c> singleton's own
    /// invariant) rather than reaching for a second fake type.
    /// </remarks>
    public IdentityKind IdentityKind { get; set; } = IdentityKind.User;

    /// <inheritdoc />
    /// <remarks>Comparison against <see cref="Roles"/> is case-insensitive.</remarks>
    public bool HasRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    /// <remarks>Comparison against <see cref="Permissions"/> is case-insensitive. Mirrors <see cref="HasRole"/> exactly (WO-057, P-368).</remarks>
    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the OIDC Authentication Method Reference (<c>amr</c>) values for the current session.
    /// </summary>
    /// <remarks>Defaults to an empty collection (WO-058, P-375).</remarks>
    public IReadOnlyCollection<string> AuthenticationMethods { get; set; } = [];

    /// <summary>Gets or sets the OIDC Authentication Context Class Reference (<c>acr</c>) claim.</summary>
    /// <remarks>Defaults to <see langword="null"/> (WO-058, P-375).</remarks>
    public string? AuthContextClassReference { get; set; }

    /// <summary>Gets or sets the UTC instant the authentication event actually occurred.</summary>
    /// <remarks>Defaults to <see langword="null"/> (WO-058, P-375).</remarks>
    public DateTimeOffset? AuthTime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current request's access token is DPoP-bound.
    /// </summary>
    /// <remarks>Defaults to <see langword="false"/> (WO-058, P-376).</remarks>
    public bool IsSenderConstrained { get; set; }

    /// <inheritdoc />
    /// <remarks>Comparison against <see cref="AuthenticationMethods"/> is case-insensitive. Mirrors <see cref="HasRole"/> exactly (WO-058, P-375).</remarks>
    public bool WasAuthenticatedWith(string method) =>
        AuthenticationMethods.Contains(method, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    /// <remarks>
    /// A pure function mirroring the real <see cref="IUserContext.IsAuthenticationFresherThan"/>
    /// contract verbatim — never calls <see cref="DateTimeOffset.UtcNow"/> internally (WO-058, P-375).
    /// </remarks>
    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) =>
        AuthTime.HasValue && (now - AuthTime.Value) <= maxAge;
}
