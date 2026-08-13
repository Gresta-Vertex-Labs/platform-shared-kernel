namespace SharedKernel.Security.Abstractions.Abstractions;

/// <summary>
/// Represents the identity of the current request's user.
/// </summary>
/// <remarks>
/// <para>
/// Registered as <b>Scoped</b> — one instance per HTTP request. Never inject into singleton services.
/// The concrete implementation is constructed from the incoming JWT claims by the OIDC package, or from
/// a matched API key by the <c>SharedKernel.Security.ApiKey</c> package.
/// </para>
/// <para>
/// <see cref="AnonymousUserContext"/> is registered as the fallback when no HTTP context is present,
/// so this interface is always resolvable. Callers must check <see cref="IsAuthenticated"/> before
/// consuming <see cref="UserId"/>.
/// </para>
/// <para>
/// <b>Invariant (corrected WO-057, P-367):</b> <see cref="UserId"/> is never <see cref="Guid.Empty"/>
/// only when <see cref="IdentityKind"/> is <see cref="Security.Abstractions.Abstractions.IdentityKind.User"/>.
/// <see cref="Security.Abstractions.Abstractions.IdentityKind.ServicePrincipal"/> and
/// <see cref="Security.Abstractions.Abstractions.IdentityKind.System"/> legitimately carry
/// <see cref="IsAuthenticated"/> = <see langword="true"/> with <see cref="UserId"/> ==
/// <see cref="Guid.Empty"/> — this is the standard shape of a client-credentials/machine-to-machine
/// token or a trusted background-execution context, not a bug. The prior, stronger rule ("<see cref="UserId"/>
/// is never <see cref="Guid.Empty"/> when <see cref="IsAuthenticated"/> is <see langword="true"/>") could
/// not distinguish those two legitimate states from a rejected token and has been superseded.
/// </para>
/// </remarks>
public interface IUserContext
{
    /// <summary>Gets the unique identifier of the authenticated human user.</summary>
    /// <remarks>
    /// Always <see cref="Guid.Empty"/> unless <see cref="IdentityKind"/> is
    /// <see cref="Security.Abstractions.Abstractions.IdentityKind.User"/>.
    /// </remarks>
    Guid UserId { get; }

    /// <summary>Gets the email address of the authenticated user, or <see langword="null"/> when absent.</summary>
    string? Email { get; }

    /// <summary>Gets the username of the authenticated user, or <see langword="null"/> when absent.</summary>
    string? Username { get; }

    /// <summary>Gets the roles assigned to the authenticated identity.</summary>
    /// <remarks>
    /// Empty when the identity is unauthenticated or when no role claims are present.
    /// Use this collection for role checks — do not parse <see cref="Claims"/> for roles.
    /// </remarks>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Gets the permissions (fine-grained scopes) granted to the authenticated identity.</summary>
    /// <remarks>
    /// <para>
    /// Empty when the identity is unauthenticated or carries no permission/scope claim. Mirrors
    /// <see cref="Roles"/>'s shape exactly (WO-057, P-368). Use <see cref="Roles"/>/<see cref="HasRole"/>
    /// for coarse role-membership checks; use this collection/<see cref="HasPermission"/> for
    /// fine-grained per-action/per-resource checks (e.g. <c>"orders:write"</c>), typically sourced from
    /// an OAuth2 <c>scope</c> claim. Both are legitimate and not mutually exclusive.
    /// </para>
    /// </remarks>
    IReadOnlyCollection<string> Permissions { get; }

    /// <summary>
    /// Gets all claims carried by the current principal as a dictionary keyed by claim type.
    /// </summary>
    /// <remarks>
    /// First value wins for multi-value claims (e.g. multiple <c>role</c> claims).
    /// For role checks, always use <see cref="HasRole"/> or <see cref="Roles"/> — not this dictionary.
    /// For permission checks, always use <see cref="HasPermission"/> or <see cref="Permissions"/>.
    /// </remarks>
    IReadOnlyDictionary<string, string> Claims { get; }

    /// <summary>
    /// Gets a value indicating whether the current request is authenticated.
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> for <see cref="Security.Abstractions.Abstractions.IdentityKind.User"/>,
    /// <see cref="Security.Abstractions.Abstractions.IdentityKind.ServicePrincipal"/>, and
    /// <see cref="Security.Abstractions.Abstractions.IdentityKind.System"/>; <see langword="false"/> only
    /// for <see cref="Security.Abstractions.Abstractions.IdentityKind.Anonymous"/>.
    /// </remarks>
    bool IsAuthenticated { get; }

    /// <summary>Gets the kind of identity represented by this context.</summary>
    /// <remarks>Added WO-057 (P-367). See <see cref="Security.Abstractions.Abstractions.IdentityKind"/>.</remarks>
    IdentityKind IdentityKind { get; }

    /// <summary>
    /// Returns <see langword="true"/> if the identity holds the specified role.
    /// </summary>
    /// <param name="role">The role name to check. Comparison is case-insensitive.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="role"/> is found in <see cref="Roles"/>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    bool HasRole(string role);

    /// <summary>
    /// Returns <see langword="true"/> if the identity holds the specified permission.
    /// </summary>
    /// <param name="permission">The permission name to check. Comparison is case-insensitive.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="permission"/> is found in <see cref="Permissions"/>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>Mirrors <see cref="HasRole"/> exactly (WO-057, P-368).</remarks>
    bool HasPermission(string permission);
}
