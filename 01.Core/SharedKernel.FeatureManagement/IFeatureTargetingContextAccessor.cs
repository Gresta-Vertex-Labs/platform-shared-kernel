namespace SharedKernel.FeatureManagement;

/// <summary>
/// Supplies the current caller's <see cref="FeatureTargetingContext"/>, so every flag evaluation targets the
/// right user, tenant and groups without each call site passing them.
/// </summary>
/// <remarks>
/// <para>
/// A service registers its own when it targets by more than the user and tenant (groups, for example), mapping its
/// identity source; any lifetime works, because it is resolved from the scope that creates the <c>IFeatureClient</c>.
/// It is read once per scope. Return <see langword="null"/> for an anonymous caller.
/// </para>
/// <para>
/// Without a registration, the target is the caller of the open <c>RequestContextScope</c> (<c>SharedKernel.Execution</c>),
/// which every inbound adapter (HTTP, gRPC, message consume, workflow activity, scheduled job) opens: its
/// <c>UserId</c> when it is authenticated, and its <c>TenantId</c>. With no scope open the caller is anonymous.
/// </para>
/// <para>
/// Nothing is read from <see cref="System.Diagnostics.Activity"/> baggage: a caller can send baggage itself, and a
/// tenant taken from it would let anyone choose another tenant's flags (P-562 X2). Work without a request that opens
/// no scope passes its caller to the call instead, for example
/// <c>FeatureTargetingContext.ForTenant(tenantId).ToEvaluationContext()</c>.
/// </para>
/// </remarks>
public interface IFeatureTargetingContextAccessor
{
    /// <summary>Returns the current caller, or <see langword="null"/> when there is none.</summary>
    /// <returns>The targeting context, or <see langword="null"/>.</returns>
    FeatureTargetingContext? GetTargetingContext();
}
