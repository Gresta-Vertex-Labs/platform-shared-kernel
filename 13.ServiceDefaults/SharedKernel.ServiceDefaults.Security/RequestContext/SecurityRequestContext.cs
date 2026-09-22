using SharedKernel.Application.Context;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>
/// The <see cref="IRequestContext"/> of a service that authenticates callers with <c>12.Security</c>:
/// reads the caller from <see cref="IUserContext"/> and the tenant from <see cref="ITenantProvider"/>.
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
/// <see cref="TenantId"/> maps <see cref="ITenantProvider"/>'s <see cref="Guid.Empty"/> "no tenant"
/// sentinel to <see langword="null"/>, which fails closed. <see cref="ActorKind"/> maps
/// <see cref="IdentityKind.User"/> to <see cref="Application.Context.ActorKind.User"/>,
/// <see cref="IdentityKind.ServicePrincipal"/> to <see cref="Application.Context.ActorKind.Service"/>,
/// an authenticated <see cref="IdentityKind.System"/> identity to <see cref="Application.Context.ActorKind.System"/>,
/// and every unauthenticated caller to <see cref="Application.Context.ActorKind.Anonymous"/> — never to
/// <c>System</c>, so an audit trail cannot mistake an anonymous request for the platform's own background work.
/// Permissions are checked with <see cref="IUserContext.HasPermission"/> (ordinal).
/// </para>
/// </remarks>
internal sealed class SecurityRequestContext : IRequestContext
{
    private readonly IUserContext _userContext;
    private readonly ITenantProvider _tenantProvider;

    /// <summary>Initialises a new <see cref="SecurityRequestContext"/>.</summary>
    /// <param name="userContext">The current scope's authenticated caller.</param>
    /// <param name="tenantProvider">The current scope's tenant.</param>
    public SecurityRequestContext(IUserContext userContext, ITenantProvider tenantProvider)
    {
        ArgumentNullException.ThrowIfNull(userContext);
        ArgumentNullException.ThrowIfNull(tenantProvider);

        _userContext = userContext;
        _tenantProvider = tenantProvider;
    }

    /// <inheritdoc />
    public bool IsAuthenticated => _userContext.IsAuthenticated;

    /// <inheritdoc />
    public string? UserId => _userContext.IsAuthenticated
        ? _userContext.SubjectId ?? _userContext.ClientId
        : null;

    /// <inheritdoc />
    public Guid? TenantId => _tenantProvider.TenantId == Guid.Empty ? null : _tenantProvider.TenantId;

    /// <inheritdoc />
    public ActorKind ActorKind => !_userContext.IsAuthenticated
        ? ActorKind.Anonymous
        : _userContext.IdentityKind switch
        {
            IdentityKind.User => ActorKind.User,
            IdentityKind.ServicePrincipal => ActorKind.Service,
            IdentityKind.System => ActorKind.System,
            _ => ActorKind.Anonymous,
        };

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
