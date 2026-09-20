namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// <see cref="System.Threading.AsyncLocal{T}"/>-backed default implementation of
/// <see cref="ICrossTenantScope"/>.
/// </summary>
/// <remarks>
/// BCL-only, zero reflection. <see cref="Enter(string?)"/> is the sole way to activate the scope —
/// the returned <see cref="IDisposable"/> deactivates it on disposal, and nested calls compose
/// (the innermost disposal only deactivates the scope once every entry has been disposed).
/// Registered as a singleton by <c>EfCorePersistenceBuilder.Build()</c> unless the consuming service
/// already registered its own <see cref="ICrossTenantScope"/>.
/// </remarks>
public sealed class CrossTenantScope : ICrossTenantScope
{
    private readonly AsyncLocal<int> _depth = new();

    /// <inheritdoc />
    public bool IsActive => _depth.Value > 0;

    /// <summary>
    /// Activates the cross-tenant bypass for the scope of the returned <see cref="IDisposable"/>.
    /// </summary>
    /// <param name="actorId">
    /// The identity of the caller requesting the bypass (e.g. an admin user id, or a background job's
    /// name), recorded on the <c>"SharedKernel.Persistence"</c> meter's
    /// <c>persistence.cross_tenant_scope_entries</c> counter as the
    /// <c>persistence.actor_id</c> tag. <see langword="null"/> is recorded as <c>"unknown"</c> — still
    /// counted, but with no attribution, so a caller that skips this parameter is visible as a gap in
    /// its own audit trail rather than silently uncounted.
    /// </param>
    /// <returns>A handle that deactivates the bypass when disposed.</returns>
    /// <remarks>
    /// Every call is recorded — including a nested/re-entrant call while a scope is already active —
    /// because each call site represents an independent decision to request the bypass, not merely a
    /// depth-counter increment.
    /// </remarks>
    public IDisposable Enter(string? actorId = null)
    {
        _depth.Value++;
        CrossTenantScopeDiagnostics.ScopeEntries.Add(
            1, new KeyValuePair<string, object?>(CrossTenantScopeDiagnostics.ActorIdTag, actorId ?? "unknown"));
        return new ScopeHandle(this);
    }

    private sealed class ScopeHandle(CrossTenantScope owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner._depth.Value--;
        }
    }
}
