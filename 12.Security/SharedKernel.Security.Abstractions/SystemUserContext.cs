using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Context;

namespace SharedKernel.Security.Abstractions;

/// <summary>The <see cref="IUserContext"/> of trusted code running without a caller.</summary>
/// <remarks>
/// <para>
/// For scheduled jobs, message consumers and workflow activities. <see cref="IsAuthenticated"/> is
/// <see langword="true"/>, but the context holds no roles or permissions: what a system context may do is decided
/// by the service's own authorization rules, keyed on <see cref="ActorKind"/>.
/// </para>
/// <para>
/// Not registered by any package. A worker host registers it itself; the authentication packages add their
/// <see cref="IUserContext"/> only when none is registered, and replace only an <see cref="AnonymousUserContext"/>
/// placeholder:
/// <code>services.AddScoped&lt;IUserContext&gt;(_ =&gt; SystemUserContext.Instance);</code>
/// </para>
/// </remarks>
public sealed class SystemUserContext : IUserContext
{
    /// <summary>Gets the shared instance.</summary>
    public static SystemUserContext Instance { get; } = new();

    private SystemUserContext()
    {
    }

    /// <inheritdoc/>
    public ActorKind ActorKind => ActorKind.System;

    /// <inheritdoc/>
    public bool IsAuthenticated => true;

    /// <inheritdoc/>
    public string? SubjectId => null;

    /// <inheritdoc/>
    public string? ClientId => null;

    /// <inheritdoc/>
    public TenantId? TenantId => null;

    /// <inheritdoc/>
    public string? SessionId => null;

    /// <inheritdoc/>
    public string? Name => null;

    /// <inheritdoc/>
    public string? Email => null;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Roles => [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Permissions => [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> AuthenticationMethods => [];

    /// <inheritdoc/>
    public string? AuthContextClassReference => null;

    /// <inheritdoc/>
    public DateTimeOffset? AuthTime => null;

    /// <inheritdoc/>
    public bool IsSenderConstrained => false;

    /// <inheritdoc/>
    public string? FindClaim(string claimType) => null;

    /// <inheritdoc/>
    public IReadOnlyList<string> FindClaims(string claimType) => [];

    /// <inheritdoc/>
    public bool HasRole(string role) => false;

    /// <inheritdoc/>
    public bool HasPermission(string permission) => false;

    /// <inheritdoc/>
    public bool WasAuthenticatedWith(string method) => false;

    /// <inheritdoc/>
    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) => false;
}
