namespace SharedKernel.FeatureManagement;

/// <summary>
/// Supplies the current caller's <see cref="FeatureTargetingContext"/>, so every flag evaluation targets the
/// right user, tenant and groups without each call site passing them.
/// </summary>
/// <remarks>
/// <para>
/// Implement this once per service over its own authenticated identity — for example <c>IUserContext</c> and
/// <c>ITenantProvider</c> from <c>SharedKernel.Security.Abstractions</c>, or <c>IRequestContext</c> from
/// <c>SharedKernel.Application.Abstractions</c>, which this package cannot reference — and register it; any lifetime
/// works, because it is resolved from the scope that creates the <c>IFeatureClient</c>. It is read once per scope.
/// Return <see langword="null"/> for an anonymous caller.
/// </para>
/// <para>
/// <b>Without a registration there is no targeting identity</b>: every caller is anonymous to user, group and tenant
/// targeting and shares one percentage bucket. Nothing is read from <see cref="System.Diagnostics.Activity"/>
/// baggage: a caller can send baggage itself, and a tenant taken from it would let anyone choose another tenant's
/// flags (P-562 X2). Work without a request, such as a background job, passes its caller to the call instead, for
/// example <c>FeatureTargetingContext.ForTenant(tenantId).ToEvaluationContext()</c>.
/// </para>
/// </remarks>
public interface IFeatureTargetingContextAccessor
{
    /// <summary>Returns the current caller, or <see langword="null"/> when there is none.</summary>
    /// <returns>The targeting context, or <see langword="null"/>.</returns>
    FeatureTargetingContext? GetTargetingContext();
}
