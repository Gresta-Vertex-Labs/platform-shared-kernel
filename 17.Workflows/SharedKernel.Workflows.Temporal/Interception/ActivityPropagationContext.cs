using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Workflows.Temporal.Interception;

/// <summary>
/// Carries the ambient <see cref="TenantScope"/> and correlation id into
/// <see cref="Authoring.ActivityBase"/>, set by <see cref="WorkflowPropagationInterceptor"/>'s worker
/// half at the start of each activity task and read back by <see cref="Authoring.ActivityBase"/>.
/// </summary>
/// <remarks>
/// <see cref="Temporalio.Activities.ActivityInfo"/> exposes no <c>Headers</c> member — unlike
/// <see cref="Temporalio.Workflows.WorkflowInfo"/>, which does — so an activity has no direct route
/// to the Temporal headers its invocation carried. This ambient <see cref="AsyncLocal{T}"/>-backed
/// holder is the mechanism that bridges the interceptor's <c>ExecuteActivityInput.Headers</c> to
/// <see cref="Authoring.ActivityBase.TenantScope"/>. It is per-logical-call-context state (isolated
/// per async flow, exactly like <c>IHttpContextAccessor</c>), not shared mutable state across
/// concurrent activity invocations.
/// </remarks>
internal static class ActivityPropagationContext
{
    private static readonly AsyncLocal<TenantScope?> TenantScopeLocal = new();
    private static readonly AsyncLocal<string?> CorrelationIdLocal = new();

    /// <summary>
    /// Gets the tenant scope propagated into the current activity invocation, or
    /// <see cref="TenantScope.Global"/> if none was set (e.g. the interceptor found no tenant header).
    /// </summary>
    public static TenantScope CurrentTenantScope => TenantScopeLocal.Value ?? TenantScope.Global;

    /// <summary>
    /// Gets the correlation id propagated into the current activity invocation, or
    /// <see cref="string.Empty"/> if none was set.
    /// </summary>
    public static string CurrentCorrelationId => CorrelationIdLocal.Value ?? string.Empty;

    /// <summary>Sets the ambient tenant scope and correlation id for the current async flow.</summary>
    public static void Set(TenantScope tenantScope, string correlationId)
    {
        TenantScopeLocal.Value = tenantScope;
        CorrelationIdLocal.Value = correlationId;
    }
}
