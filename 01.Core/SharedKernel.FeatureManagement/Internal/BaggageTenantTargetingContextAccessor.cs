using System.Diagnostics;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// The default <see cref="IFeatureTargetingContextAccessor"/>: the tenant from the current activity's
/// <see cref="WellKnownBaggageKeys.TenantId"/> baggage item when it is a valid <see cref="TenantId"/>, and no user.
/// </summary>
internal sealed class BaggageTenantTargetingContextAccessor : IFeatureTargetingContextAccessor
{
    public FeatureTargetingContext? GetTargetingContext() =>
        TenantId.TryParse(Activity.Current?.GetBaggageItem(WellKnownBaggageKeys.TenantId), out TenantId tenantId)
            ? FeatureTargetingContext.ForTenant(tenantId)
            : null;
}
