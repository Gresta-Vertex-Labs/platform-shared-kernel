namespace SharedKernel.Security.Abstractions.Abstractions;

/// <summary>
/// Discriminates the kind of identity represented by an <see cref="IUserContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Introduced (WO-057, P-367) to correct an earlier, over-strong invariant that conflated a genuinely
/// rejected/unauthenticated caller with a legitimate client-credentials (machine-to-machine) token that
/// simply carries no human subject claim. The corrected invariant is: <see cref="IUserContext.UserId"/>
/// is never <see cref="Guid.Empty"/> only when <see cref="IUserContext.IdentityKind"/> is <see cref="User"/>.
/// <see cref="ServicePrincipal"/> and <see cref="System"/> legitimately carry
/// <see cref="IUserContext.IsAuthenticated"/> = <see langword="true"/> with
/// <see cref="IUserContext.UserId"/> == <see cref="Guid.Empty"/>.
/// </para>
/// <para>
/// Detection of <see cref="ServicePrincipal"/> must always be IdP-agnostic — never keyed off a single
/// identity provider's proprietary claim names (e.g. Microsoft Entra's <c>idtyp</c>/<c>azp</c> claims) as
/// the sole detection mechanism.
/// </para>
/// </remarks>
public enum IdentityKind
{
    /// <summary>
    /// No valid authenticated principal is present. This is <c>default(IdentityKind)</c>, so an
    /// uninitialized or default-constructed value fails safe.
    /// </summary>
    Anonymous = 0,

    /// <summary>
    /// A human subject — a parseable, non-empty <see cref="Guid"/> subject (<c>sub</c>) claim was present
    /// on an authenticated principal.
    /// </summary>
    User,

    /// <summary>
    /// A valid, authenticated token that carries no human subject claim — the standard shape of a
    /// client-credentials / machine-to-machine (M2M) OAuth2 token, or a successfully matched API key
    /// (see <c>SharedKernel.Security.ApiKey</c>).
    /// </summary>
    ServicePrincipal,

    /// <summary>
    /// A trusted, non-HTTP execution context — e.g. a Temporal activity, a MassTransit consumer, or a
    /// hosted-service startup path. See <see cref="SystemUserContext"/>.
    /// </summary>
    System,
}
