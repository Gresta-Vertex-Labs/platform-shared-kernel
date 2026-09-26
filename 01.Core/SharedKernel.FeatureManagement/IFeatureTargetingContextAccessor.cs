namespace SharedKernel.FeatureManagement;

/// <summary>
/// Supplies the current caller's <see cref="FeatureTargetingContext"/>, so every flag evaluation targets the
/// right user, tenant and groups without each call site passing them.
/// </summary>
/// <remarks>
/// <para>
/// Implement this once per service over its own identity source (for example <c>IRequestContext</c> from
/// <c>SharedKernel.Execution</c>, whose <c>UserId</c> and <c>TenantId</c> it maps) and register it; any lifetime works,
/// because it is resolved from the scope that creates the <c>IFeatureClient</c>. It is read once per scope.
/// </para>
/// <para>
/// Without a registration, the target is the ambient caller's tenant and there is no user: the
/// <c>IRequestContext.TenantId</c> of the open <c>RequestContextScope</c>, which every inbound adapter (HTTP, gRPC,
/// message consume, workflow activity, scheduled job) opens; or, when no scope is open, the <c>TenantId</c>
/// <see cref="System.Diagnostics.Activity"/> baggage item (<c>WellKnownBaggageKeys.TenantId</c>) when it parses as a
/// <c>TenantId</c>.
/// </para>
/// </remarks>
public interface IFeatureTargetingContextAccessor
{
    /// <summary>Returns the current caller, or <see langword="null"/> when there is none.</summary>
    /// <returns>The targeting context, or <see langword="null"/>.</returns>
    FeatureTargetingContext? GetTargetingContext();
}
