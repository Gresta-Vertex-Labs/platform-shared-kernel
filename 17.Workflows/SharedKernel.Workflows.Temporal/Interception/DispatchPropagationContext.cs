using SharedKernel.Workflows.Temporal.Dispatch;

namespace SharedKernel.Workflows.Temporal.Interception;

/// <summary>
/// Carries the <see cref="TenantScope"/> a client-side dispatch call is being made under, read by
/// <see cref="WorkflowPropagationInterceptor"/>'s client half when it writes Temporal headers on a
/// start/signal/query call.
/// </summary>
/// <remarks>
/// <see cref="Dispatch.WorkflowDispatcher"/> and <see cref="Dispatch.WorkflowHandleAdapter"/> set this
/// immediately before issuing the underlying Temporal client call, since neither
/// <c>ITemporalClient</c>'s call shape nor the Temporal SDK's interceptor input types carry a
/// tenant-scope parameter directly. Per-logical-call-context <see cref="AsyncLocal{T}"/> state,
/// isolated per async flow — not shared mutable state across concurrent dispatch calls.
/// </remarks>
internal static class DispatchPropagationContext
{
    private static readonly AsyncLocal<TenantScope?> TenantScopeLocal = new();

    /// <summary>Gets the tenant scope for the dispatch call currently in flight on this async flow.</summary>
    public static TenantScope CurrentTenantScope => TenantScopeLocal.Value ?? TenantScope.None;

    /// <summary>Sets the tenant scope for the dispatch call about to be issued on this async flow.</summary>
    public static void SetTenantScope(TenantScope tenantScope) => TenantScopeLocal.Value = tenantScope;
}
