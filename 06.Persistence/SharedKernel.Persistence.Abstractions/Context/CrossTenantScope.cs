namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// <see cref="System.Threading.AsyncLocal{T}"/>-backed default implementation of
/// <see cref="ICrossTenantScope"/>.
/// </summary>
/// <remarks>
/// BCL-only, zero reflection. <see cref="Enter"/> is the sole way to activate the scope —
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
    /// <returns>A handle that deactivates the bypass when disposed.</returns>
    public IDisposable Enter()
    {
        _depth.Value++;
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
