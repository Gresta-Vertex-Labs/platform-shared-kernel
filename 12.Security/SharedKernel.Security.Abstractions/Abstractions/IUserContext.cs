namespace SharedKernel.Security.Abstractions.Abstractions;

/// <summary>
/// Represents the identity of the current request's user.
/// </summary>
/// <remarks>
/// <para>
/// Registered as <b>Scoped</b> — one instance per HTTP request. Never inject into singleton services.
/// The concrete implementation is constructed from the incoming JWT claims by the OIDC package.
/// </para>
/// <para>
/// <see cref="AnonymousUserContext"/> is registered as the fallback when no HTTP context is present,
/// so this interface is always resolvable. Callers must check <see cref="IsAuthenticated"/> before
/// consuming <see cref="UserId"/>.
/// </para>
/// <para>
/// <b>Invariant:</b> <see cref="UserId"/> must never equal <see cref="Guid.Empty"/> when
/// <see cref="IsAuthenticated"/> is <see langword="true"/>. If the <c>sub</c> claim is absent or
/// cannot be parsed as a <see cref="Guid"/>, the implementation must set
/// <see cref="IsAuthenticated"/> to <see langword="false"/>.
/// </para>
/// </remarks>
public interface IUserContext
{
    /// <summary>Gets the unique identifier of the authenticated user.</summary>
    /// <remarks>
    /// Always <see cref="Guid.Empty"/> when <see cref="IsAuthenticated"/> is <see langword="false"/>.
    /// </remarks>
    Guid UserId { get; }

    /// <summary>Gets the email address of the authenticated user, or <see langword="null"/> when absent.</summary>
    string? Email { get; }

    /// <summary>Gets the username of the authenticated user, or <see langword="null"/> when absent.</summary>
    string? Username { get; }

    /// <summary>Gets the roles assigned to the authenticated user.</summary>
    /// <remarks>
    /// Empty when the user is unauthenticated or when no role claims are present.
    /// Use this collection for role checks — do not parse <see cref="Claims"/> for roles.
    /// </remarks>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>
    /// Gets all claims carried by the current principal as a dictionary keyed by claim type.
    /// </summary>
    /// <remarks>
    /// First value wins for multi-value claims (e.g. multiple <c>role</c> claims).
    /// For role checks, always use <see cref="HasRole"/> or <see cref="Roles"/> — not this dictionary.
    /// </remarks>
    IReadOnlyDictionary<string, string> Claims { get; }

    /// <summary>
    /// Gets a value indicating whether the current request is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Returns <see langword="true"/> if the user holds the specified role.
    /// </summary>
    /// <param name="role">The role name to check. Comparison is case-insensitive.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="role"/> is found in <see cref="Roles"/>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    bool HasRole(string role);
}
