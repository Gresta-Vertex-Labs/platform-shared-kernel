using SharedKernel.Application.Context;

namespace SharedKernel.Persistence.Testing;

/// <summary>
/// A configurable <see cref="IRequestContext"/> for tests: the caller the persistence layer attributes writes to,
/// filters tenant data by and audits.
/// </summary>
/// <remarks>
/// <para>
/// Start from a factory — <see cref="ForUser"/>, <see cref="ForTenant"/>, <see cref="Service"/>, <see cref="System"/>,
/// <see cref="Anonymous"/> — and adjust with the <c>With*</c> methods or the settable properties. The instance is
/// mutable on purpose: register it as a singleton (or scoped, per test) and change the tenant between steps to act as
/// another caller.
/// </para>
/// <para>
/// Permissions are granted explicitly (<see cref="WithPermissions"/>); an unconfigured context grants none, like
/// the platform's fail-closed authorization. Comparison is ordinal, as for real OAuth2 scopes.
/// </para>
/// </remarks>
public class TestRequestContext : IRequestContext
{
    /// <summary>The user id of <see cref="ForUser"/> and <see cref="ForTenant"/> when none is given.</summary>
    public const string DefaultUserId = "test-user";

    private ActorKind? _actorKind;

    /// <summary>Initializes an authenticated user context (<see cref="DefaultUserId"/>, no tenant, no permissions).</summary>
    public TestRequestContext()
    {
    }

    /// <summary>Gets or sets whether the caller is authenticated. Defaults to <see langword="true"/>.</summary>
    public bool IsAuthenticated { get; set; } = true;

    /// <summary>Gets or sets the caller's subject id. Defaults to <see cref="DefaultUserId"/>.</summary>
    public string? UserId { get; set; } = DefaultUserId;

    /// <summary>Gets or sets the caller's tenant. Defaults to <see langword="null"/> (no tenant: tenant data is hidden).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the kind of actor. Until set: <see cref="ActorKind.User"/> when authenticated, otherwise
    /// <see cref="ActorKind.Anonymous"/>.
    /// </summary>
    public ActorKind ActorKind
    {
        get => _actorKind ?? (IsAuthenticated ? ActorKind.User : ActorKind.Anonymous);
        set => _actorKind = value;
    }

    /// <summary>Gets or sets the OAuth2 client id of the caller.</summary>
    public string? ClientId { get; set; }

    /// <summary>Gets or sets the caller's session id.</summary>
    public string? SessionId { get; set; }

    /// <summary>Gets or sets the identity acting on the caller's behalf.</summary>
    public string? ImpersonatorId { get; set; }

    /// <summary>Gets or sets the permissions the caller holds. Defaults to none.</summary>
    public IReadOnlyCollection<string> Permissions { get; set; } = [];

    /// <summary>An authenticated user, optionally in a tenant.</summary>
    /// <param name="userId">The subject id.</param>
    /// <param name="tenantId">The tenant, or <see langword="null"/>.</param>
    /// <returns>A new context.</returns>
    public static TestRequestContext ForUser(string userId = DefaultUserId, Guid? tenantId = null) =>
        new() { UserId = userId, TenantId = tenantId };

    /// <summary>An authenticated user of <paramref name="tenantId"/>.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="userId">The subject id.</param>
    /// <returns>A new context.</returns>
    public static TestRequestContext ForTenant(Guid tenantId, string userId = DefaultUserId) =>
        new() { UserId = userId, TenantId = tenantId };

    /// <summary>A machine client (client-credentials token): <see cref="ActorKind.Service"/>.</summary>
    /// <param name="clientId">The client id, also reported as the subject.</param>
    /// <param name="tenantId">The tenant, or <see langword="null"/>.</param>
    /// <returns>A new context.</returns>
    public static TestRequestContext Service(string clientId, Guid? tenantId = null) =>
        new() { UserId = clientId, ClientId = clientId, TenantId = tenantId, ActorKind = ActorKind.Service };

    /// <summary>
    /// The system itself (a background job, a seeder): <see cref="ActorKind.System"/>. Like
    /// <c>SystemRequestContext</c>, but mutable.
    /// </summary>
    /// <param name="identity">The system identity.</param>
    /// <param name="tenantId">The tenant, or <see langword="null"/>.</param>
    /// <returns>A new context.</returns>
    public static TestRequestContext System(string identity = "system", Guid? tenantId = null) =>
        new() { UserId = identity, TenantId = tenantId, ActorKind = ActorKind.System };

    /// <summary>An unauthenticated caller: <see cref="ActorKind.Anonymous"/>, no subject.</summary>
    /// <param name="tenantId">A tenant resolved without authentication (e.g. from the host), or <see langword="null"/>.</param>
    /// <returns>A new context.</returns>
    public static TestRequestContext Anonymous(Guid? tenantId = null) =>
        new() { IsAuthenticated = false, UserId = null, TenantId = tenantId };

    /// <summary>Sets <see cref="TenantId"/>.</summary>
    /// <param name="tenantId">The tenant, or <see langword="null"/>.</param>
    /// <returns>This context.</returns>
    public TestRequestContext WithTenant(Guid? tenantId)
    {
        TenantId = tenantId;
        return this;
    }

    /// <summary>Adds <paramref name="permissions"/> to <see cref="Permissions"/>.</summary>
    /// <param name="permissions">The permissions to grant.</param>
    /// <returns>This context.</returns>
    public TestRequestContext WithPermissions(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        Permissions = [.. Permissions, .. permissions];
        return this;
    }

    /// <summary>Sets <see cref="SessionId"/>.</summary>
    /// <param name="sessionId">The session id.</param>
    /// <returns>This context.</returns>
    public TestRequestContext WithSession(string? sessionId)
    {
        SessionId = sessionId;
        return this;
    }

    /// <summary>Sets <see cref="ImpersonatorId"/>.</summary>
    /// <param name="impersonatorId">The impersonating identity.</param>
    /// <returns>This context.</returns>
    public TestRequestContext WithImpersonator(string? impersonatorId)
    {
        ImpersonatorId = impersonatorId;
        return this;
    }

    /// <summary>Whether <see cref="Permissions"/> contains <paramref name="permission"/> (ordinal).</summary>
    /// <param name="permission">The permission.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    public virtual bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return ValueTask.FromResult(HasPermission(permission));
    }
}
