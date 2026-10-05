using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Execution.Context;

namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// The default <see cref="ICrossTenantScope"/>: the active state belongs to this instance — one per
/// dependency-injection scope — and the actor is taken from that scope's <see cref="IRequestContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// The state is a counter on the instance, not a flow-local (<see cref="AsyncLocal{T}"/>) value, so an entry
/// made anywhere in the scope — including inside an awaited helper method — is visible to every component of
/// that scope (repositories, the EF Core context, Dapper sessions, the audit services) until the handle is
/// disposed, and never to another scope. The former flow-local design lost an entry made inside an
/// <see langword="async"/> method as soon as that method returned.
/// </para>
/// <para>
/// The container creates one per scope (<c>AddSharedKernelCrossTenantScope</c>, called by
/// <c>AddSharedKernelPostgres</c> and <c>AddSharedKernelDapper</c>). Constructing one yourself creates an
/// independent bypass that only what you hand it to observes; a context created through
/// <c>ICallerDbContextFactory</c> already carries its own (<c>SharedKernelDbContext.CrossTenantScope</c>).
/// </para>
/// <para>
/// Each <see cref="Enter(string)"/> is logged at Information level (EventId 6150) with the actor, its
/// kind, the tenant it acted from and the reason, and counted on the <c>"SharedKernel.Persistence"</c>
/// meter (<c>persistence.cross_tenant_scope_entries</c>, tagged with the actor kind only, which keeps the
/// cardinality bounded). Thread-safe.
/// </para>
/// </remarks>
public sealed class CrossTenantScope : ICrossTenantScope
{
    private readonly IRequestContext _requestContext;
    private readonly ILogger _logger;
    private int _depth;

    /// <summary>Initialises a new, inactive <see cref="CrossTenantScope"/>.</summary>
    /// <param name="requestContext">The caller that enters the bypass.</param>
    /// <param name="logger">Optional logger for the entry record.</param>
    /// <remarks>The container creates one per scope (<c>AddSharedKernelCrossTenantScope</c>); construct one yourself only in tests.</remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public CrossTenantScope(IRequestContext requestContext, ILogger<CrossTenantScope>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(requestContext);

        _requestContext = requestContext;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public bool IsActive => Volatile.Read(ref _depth) > 0;

    /// <inheritdoc />
    public IDisposable Enter(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var actorId = _requestContext.UserId is { Length: > 0 } userId ? userId : "anonymous";
        var actorKind = _requestContext.ActorKind;

        Interlocked.Increment(ref _depth);

        CrossTenantScopeLog.Entered(_logger, actorId, actorKind, _requestContext.TenantId, reason);
        CrossTenantScopeDiagnostics.ScopeEntries.Add(
            1, new KeyValuePair<string, object?>(CrossTenantScopeDiagnostics.ActorKindTag, actorKind.ToString()));

        return new ScopeHandle(this);
    }

    private sealed class ScopeHandle(CrossTenantScope owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref owner._depth);
        }
    }
}
