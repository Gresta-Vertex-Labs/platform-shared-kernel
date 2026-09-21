namespace SharedKernel.Application.Context;

/// <summary>
/// An <see cref="IRequestContext"/> for a command or query dispatched with no HTTP request behind
/// it — a Temporal activity (<c>17.Workflows</c>) or a scheduled job (<c>19.Scheduling</c>).
/// </summary>
/// <remarks>
/// <para>
/// Always <see cref="IsAuthenticated"/>, under a caller-supplied identity string
/// (<see cref="UserId"/>). The permission set is supplied explicitly by the caller and checked
/// exactly as given — this type never grants "all permissions"; an empty set means the identity
/// holds none, which is exactly what a system actor with no business calling
/// <c>AuthorizationBehavior</c>-gated commands should get.
/// </para>
/// </remarks>
public sealed class SystemRequestContext : IRequestContext
{
    private readonly IReadOnlySet<string> _permissions;

    /// <summary>
    /// Creates a <see cref="SystemRequestContext"/> holding exactly <paramref name="permissions"/>.
    /// </summary>
    /// <param name="permissions">
    /// The exact permissions this system identity holds. An empty collection means no permissions —
    /// never interpreted as "all permissions".
    /// </param>
    /// <param name="identity">
    /// The value reported as <see cref="UserId"/>, identifying which system actor is executing.
    /// Defaults to <c>"system"</c>.
    /// </param>
    /// <param name="tenantId">
    /// The tenant this execution is scoped to, or <see langword="null"/> when the work is not
    /// tenant-scoped.
    /// </param>
    public SystemRequestContext(
        IEnumerable<string> permissions,
        string identity = "system",
        Guid? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        _permissions = permissions.ToHashSet(StringComparer.Ordinal);
        UserId = identity;
        TenantId = tenantId;
    }

    /// <inheritdoc/>
    public bool IsAuthenticated => true;

    /// <inheritdoc/>
    public string? UserId { get; }

    /// <inheritdoc/>
    public Guid? TenantId { get; }

    /// <inheritdoc/>
    /// <remarks>Always <see cref="Context.ActorKind.System"/>.</remarks>
    public ActorKind ActorKind => ActorKind.System;

    /// <inheritdoc/>
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(_permissions.Contains(permission));
}
