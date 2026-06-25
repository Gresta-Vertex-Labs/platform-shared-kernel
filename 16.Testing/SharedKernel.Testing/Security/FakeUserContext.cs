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

    /// <inheritdoc />
    /// <remarks>Comparison against <see cref="Roles"/> is case-insensitive.</remarks>
    public bool HasRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
