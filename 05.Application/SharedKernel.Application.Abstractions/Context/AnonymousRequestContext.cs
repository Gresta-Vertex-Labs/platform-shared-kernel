namespace SharedKernel.Application.Context;

/// <summary>
/// An <see cref="IRequestContext"/> for a command or query dispatched with no caller identity at
/// all: not authenticated, no user, no tenant, and no permissions.
/// </summary>
/// <remarks>
/// Immutable and stateless, so <see cref="Instance"/> is safe to share across every caller —
/// there is no per-request state to isolate.
/// </remarks>
public sealed class AnonymousRequestContext : IRequestContext
{
    /// <summary>Gets the shared, stateless singleton instance.</summary>
    public static AnonymousRequestContext Instance { get; } = new();

    private AnonymousRequestContext()
    {
    }

    /// <inheritdoc/>
    public bool IsAuthenticated => false;

    /// <inheritdoc/>
    public string? UserId => null;

    /// <inheritdoc/>
    public Guid? TenantId => null;

    /// <inheritdoc/>
    /// <remarks>
    /// Always <see cref="Context.ActorKind.Anonymous"/>: nobody was identified. Persisted audit columns still name
    /// the service (its configured name), but the caller is never presented as the platform's own work.
    /// </remarks>
    public ActorKind ActorKind => ActorKind.Anonymous;

    /// <inheritdoc/>
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(false);
}
