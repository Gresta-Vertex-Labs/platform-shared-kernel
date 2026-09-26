using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Context;

namespace SharedKernel.Security.Abstractions;

/// <summary>The caller of the current operation.</summary>
/// <remarks>
/// <para>
/// Registered as a scoped service by the authentication packages. Outside an HTTP request it resolves to
/// <see cref="AnonymousUserContext"/> unless the host registers <see cref="SystemUserContext"/> or its own
/// implementation.
/// </para>
/// <para>
/// <see cref="SubjectId"/> is never <see langword="null"/> when <see cref="ActorKind"/> is
/// <see cref="ActorKind.User"/> or <see cref="ActorKind.Service"/>,
/// and always <see langword="null"/> otherwise. Identifiers are strings because identity providers issue
/// arbitrary subject formats (<c>auth0|…</c>, pairwise identifiers, GUIDs).
/// </para>
/// </remarks>
public interface IUserContext
{
    /// <summary>Gets the kind of caller.</summary>
    ActorKind ActorKind { get; }

    /// <summary>
    /// Gets a value indicating whether the caller is authenticated: <see langword="true"/> for every
    /// <see cref="ActorKind"/> except <see cref="ActorKind.Anonymous"/>.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the stable identifier of the user or service principal, as issued by the authority that
    /// authenticated it (the <c>sub</c> claim, an API key's client id, a certificate's client id).
    /// </summary>
    /// <remarks>Unique per issuer only. Store it together with the issuer if a service accepts several.</remarks>
    string? SubjectId { get; }

    /// <summary>
    /// Gets the application the call came through: the OAuth client id (<c>azp</c>/<c>client_id</c>), or the
    /// client id of an API key or certificate. <see langword="null"/> when the credential does not carry one.
    /// </summary>
    string? ClientId { get; }

    /// <summary>
    /// Gets the tenant asserted by the credential, or <see langword="null"/> when it carries none or the value is
    /// not a GUID.
    /// </summary>
    TenantId? TenantId { get; }

    /// <summary>
    /// Gets the identifier of the sign-in session (<c>sid</c>), or of the individual token when the identity
    /// provider issues no session id. <see langword="null"/> for credentials without either.
    /// </summary>
    string? SessionId { get; }

    /// <summary>Gets the display name, or <see langword="null"/> when absent.</summary>
    string? Name { get; }

    /// <summary>Gets the email address, or <see langword="null"/> when absent. Not verified unless the issuer says so.</summary>
    string? Email { get; }

    /// <summary>Gets the roles granted to the caller. Empty when there are none.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Gets the permissions (OAuth scopes) granted to the caller. Empty when there are none.</summary>
    IReadOnlyCollection<string> Permissions { get; }

    /// <summary>
    /// Gets the methods used to authenticate the session (the OIDC <c>amr</c> claim, e.g. <c>pwd</c>, <c>otp</c>,
    /// <c>mfa</c>). Empty for credentials without an authentication context.
    /// </summary>
    IReadOnlyCollection<string> AuthenticationMethods { get; }

    /// <summary>Gets the authentication context class (the OIDC <c>acr</c> claim), or <see langword="null"/>.</summary>
    string? AuthContextClassReference { get; }

    /// <summary>
    /// Gets when the user actually authenticated (the OIDC <c>auth_time</c> claim), which is earlier than the
    /// token's issue time when the token was refreshed. <see langword="null"/> when absent.
    /// </summary>
    DateTimeOffset? AuthTime { get; }

    /// <summary>
    /// Gets a value indicating whether the credential is bound to a key the caller proved it holds on this
    /// request: a DPoP-bound (RFC 9449) or certificate-bound (RFC 8705) access token.
    /// </summary>
    bool IsSenderConstrained { get; }

    /// <summary>Returns the first value of a claim, or <see langword="null"/> when the caller has no such claim.</summary>
    /// <param name="claimType">The claim type, compared ordinally.</param>
    /// <returns>The first value, or <see langword="null"/>.</returns>
    string? FindClaim(string claimType);

    /// <summary>Returns every value of a claim. Empty when the caller has no such claim.</summary>
    /// <param name="claimType">The claim type, compared ordinally.</param>
    /// <returns>The values in the order the credential carried them.</returns>
    IReadOnlyList<string> FindClaims(string claimType);

    /// <summary>Returns whether the caller holds a role.</summary>
    /// <param name="role">The role, compared ordinally.</param>
    /// <returns><see langword="true"/> when <see cref="Roles"/> contains <paramref name="role"/>.</returns>
    bool HasRole(string role);

    /// <summary>Returns whether the caller holds a permission.</summary>
    /// <param name="permission">The permission, compared ordinally.</param>
    /// <returns><see langword="true"/> when <see cref="Permissions"/> contains <paramref name="permission"/>.</returns>
    bool HasPermission(string permission);

    /// <summary>Returns whether the session was authenticated with a method.</summary>
    /// <param name="method">The authentication method reference, compared ordinally.</param>
    /// <returns><see langword="true"/> when <see cref="AuthenticationMethods"/> contains <paramref name="method"/>.</returns>
    bool WasAuthenticatedWith(string method);

    /// <summary>Returns whether the user authenticated no longer ago than <paramref name="maxAge"/>.</summary>
    /// <param name="maxAge">The longest acceptable time since authentication.</param>
    /// <param name="now">The current time, from the caller's <c>IClock</c>.</param>
    /// <returns>
    /// <see langword="true"/> when <see cref="AuthTime"/> is known and within <paramref name="maxAge"/> of
    /// <paramref name="now"/>; <see langword="false"/> when it is unknown or older.
    /// </returns>
    bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now);

    /// <summary>Returns when the caller verified an authentication method.</summary>
    /// <param name="method">The authentication method reference, such as <c>otp</c>, compared ordinally.</param>
    /// <returns>
    /// When <paramref name="method"/> was verified; <see langword="null"/> when <see cref="WasAuthenticatedWith"/> is
    /// <see langword="false"/> for it, or its time is unknown.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A method added after sign-in, such as a step-up, carries its own time in a
    /// <see cref="SecurityClaimTypes.AuthenticationMethodTime"/> claim; a method the credential carried was verified at
    /// sign-in, <see cref="AuthTime"/>.
    /// </para>
    /// <para>
    /// Compare it with the current time when a method must be recent, not only present. That matters on long-lived
    /// connections: a SignalR connection keeps the principal it connected with, and a gRPC streaming call the principal
    /// it started with, so an <c>amr</c> value on them stays however old it gets, while a plain request re-authenticates
    /// every time.
    /// </para>
    /// <para>
    /// The default implementation returns <see langword="null"/>, so a requirement with a maximum age refuses the callers
    /// of an implementation that does not override it.
    /// </para>
    /// </remarks>
    DateTimeOffset? GetAuthenticationMethodTime(string method) => null;
}
