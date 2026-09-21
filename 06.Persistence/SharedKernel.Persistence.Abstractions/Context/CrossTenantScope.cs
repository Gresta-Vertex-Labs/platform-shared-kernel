using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Context;

namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// The default <see cref="ICrossTenantScope"/>: the active state lives in the current logical call flow,
/// the actor is taken from the scope's <see cref="IRequestContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every instance shares one flow-local depth counter, so an instance created anywhere (including with
/// <see langword="new"/> in a test) enters the same bypass every other component observes. The former
/// design kept the counter per instance, which made a hand-constructed scope a silent no-op.
/// </para>
/// <para>
/// Each <see cref="Enter(string)"/> is logged at Information level (EventId 6150) with the actor, its
/// kind, the tenant it acted from and the reason, and counted on the <c>"SharedKernel.Persistence"</c>
/// meter (<c>persistence.cross_tenant_scope_entries</c>, tagged with the actor kind only, which keeps the
/// cardinality bounded).
/// </para>
/// </remarks>
public sealed class CrossTenantScope : ICrossTenantScope
{
    private static readonly AsyncLocal<int> Depth = new();

    private readonly IRequestContext _requestContext;
    private readonly ILogger<CrossTenantScope> _logger;

    /// <summary>Initialises a new <see cref="CrossTenantScope"/>.</summary>
    /// <param name="requestContext">The caller that enters the bypass.</param>
    /// <param name="logger">Optional logger for the entry record.</param>
    public CrossTenantScope(IRequestContext requestContext, ILogger<CrossTenantScope>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(requestContext);

        _requestContext = requestContext;
        _logger = logger ?? NullLogger<CrossTenantScope>.Instance;
    }

    /// <summary>
    /// Gets whether a cross-tenant bypass is active in the current logical call flow: the value every
    /// <see cref="ICrossTenantScope.IsActive"/> reports. For singleton infrastructure (EF Core
    /// interceptors, connection hooks) that cannot inject the scoped <see cref="ICrossTenantScope"/>.
    /// </summary>
    public static bool IsActiveInCurrentFlow => Depth.Value > 0;

    /// <inheritdoc />
    public bool IsActive => IsActiveInCurrentFlow;

    /// <inheritdoc />
    public IDisposable Enter(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var actorId = _requestContext.UserId is { Length: > 0 } userId ? userId : "anonymous";
        var actorKind = _requestContext.ActorKind;

        Depth.Value++;

        CrossTenantScopeLog.Entered(_logger, actorId, actorKind, _requestContext.TenantId, reason);
        CrossTenantScopeDiagnostics.ScopeEntries.Add(
            1, new KeyValuePair<string, object?>(CrossTenantScopeDiagnostics.ActorKindTag, actorKind.ToString()));

        return new ScopeHandle();
    }

    private sealed class ScopeHandle : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (Depth.Value > 0)
                Depth.Value--;
        }
    }
}
