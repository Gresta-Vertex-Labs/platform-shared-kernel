using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>
/// The <see cref="IRequestContext"/> of a service that authenticates callers with <c>12.Security</c>:
/// reads the caller, and the tenant its credential asserts, from <see cref="IUserContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// One implementation for every layer that asks who is calling — <c>AuthorizationBehavior</c> and the
/// caching behaviors in <c>05.Application</c>, and audit columns, tenant filters and the audit trail in
/// <c>06.Persistence</c>. Registered by
/// <see cref="RequestContextServiceCollectionExtensions.AddSharedKernelRequestContext"/>.
/// </para>
/// <para>
/// <strong>Mapping.</strong> <see cref="UserId"/> is the subject id (falling back to the client id for
/// a client-credentials caller without one) and <see langword="null"/> when unauthenticated.
/// <see cref="TenantId"/> is <see cref="IUserContext.TenantId"/>; <see langword="null"/> fails closed.
/// <see cref="ActorKind"/> is <see cref="IUserContext.ActorKind"/> for an authenticated caller, and
/// <see cref="ActorKind.Anonymous"/> for every unauthenticated one — never <c>System</c>, so an audit trail cannot
/// mistake an anonymous request for the platform's own background work.
/// Permissions are checked with <see cref="IUserContext.HasPermission"/> (ordinal).
/// </para>
/// </remarks>
internal sealed class SecurityRequestContext : IRequestContext
{
    private readonly IUserContext _userContext;

    /// <summary>Initialises a new <see cref="SecurityRequestContext"/>.</summary>
    /// <param name="userContext">The current scope's authenticated caller.</param>
    public SecurityRequestContext(IUserContext userContext)
    {
        ArgumentNullException.ThrowIfNull(userContext);
        _userContext = userContext;
    }

    /// <inheritdoc />
    public bool IsAuthenticated => _userContext.IsAuthenticated;

    /// <inheritdoc />
    public string? UserId => _userContext.IsAuthenticated
        ? _userContext.SubjectId ?? _userContext.ClientId
        : null;

    /// <inheritdoc />
    public TenantId? TenantId => _userContext.TenantId;

    /// <inheritdoc />
    public ActorKind ActorKind => _userContext.IsAuthenticated ? _userContext.ActorKind : ActorKind.Anonymous;

    /// <inheritdoc />
    public string? ClientId => _userContext.ClientId;

    /// <inheritdoc />
    public string? SessionId => _userContext.SessionId;

    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return ValueTask.FromResult(_userContext.HasPermission(permission));
    }
}
