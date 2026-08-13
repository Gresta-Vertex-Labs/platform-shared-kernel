namespace SharedKernel.Security.Abstractions.Abstractions;

/// <summary>
/// Sentinel implementation of <see cref="IUserContext"/> for trusted, non-HTTP execution contexts.
/// </summary>
/// <remarks>
/// <para>
/// Represents a trusted background-execution authority — a Temporal activity, a MassTransit consumer, a
/// Hangfire job, a startup seeder — as opposed to a rejected or genuinely unauthenticated caller. A peer
/// of <see cref="AnonymousUserContext"/>, not a replacement for it (WO-057, P-369).
/// </para>
/// <para>
/// <see cref="IdentityKind"/> is <see cref="Security.Abstractions.Abstractions.IdentityKind.System"/>;
/// <see cref="IsAuthenticated"/> is <see langword="true"/>; <see cref="UserId"/> is
/// <see cref="Guid.Empty"/> — this is the standard shape for a trusted background context, not a bug
/// (see the corrected invariant on <see cref="IUserContext"/>). <see cref="Roles"/>,
/// <see cref="Permissions"/>, and <see cref="Claims"/> are empty; <see cref="HasRole"/> and
/// <see cref="HasPermission"/> always return <see langword="false"/> — a system context asserts trust by
/// <see cref="IdentityKind"/>, not by inheriting role/permission membership it was never granted. A
/// consuming service's own authorization bridge decides what <see cref="Security.Abstractions.Abstractions.IdentityKind.System"/>
/// means for its authorization rules.
/// </para>
/// <para>
/// <b>No DI wiring is shipped for this type in <c>SharedKernel.Security.Abstractions</c>.</b> A
/// background-execution composition root is responsible for registering it in place of the
/// HTTP-derived <see cref="IUserContext"/> factory, e.g.:
/// <code>
/// services.AddScoped&lt;IUserContext&gt;(_ => SystemUserContext.Instance);
/// </code>
/// This domain does not itself decide whether <see cref="Security.Abstractions.Abstractions.IdentityKind.System"/>
/// bypasses authorization — that is the consuming service's own explicit choice.
/// </para>
/// </remarks>
public sealed class SystemUserContext : IUserContext
{
    /// <summary>Gets the singleton instance of <see cref="SystemUserContext"/>.</summary>
    public static readonly SystemUserContext Instance = new();

    private SystemUserContext()
    {
    }

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
    public bool IsAuthenticated => true;

    /// <inheritdoc/>
    public IdentityKind IdentityKind => IdentityKind.System;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool HasRole(string role) => false;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool HasPermission(string permission) => false;
}
