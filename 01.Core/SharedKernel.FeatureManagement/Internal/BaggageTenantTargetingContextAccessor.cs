using System.Diagnostics;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// The default <see cref="IFeatureTargetingContextAccessor"/>: the tenant from the current activity's
/// <see cref="WellKnownBaggageKeys.TenantId"/> baggage item, and no user.
/// </summary>
internal sealed class BaggageTenantTargetingContextAccessor : IFeatureTargetingContextAccessor
{
    public FeatureTargetingContext? GetTargetingContext() =>
        Activity.Current?.GetBaggageItem(WellKnownBaggageKeys.TenantId) is { Length: > 0 } tenantId
            ? FeatureTargetingContext.ForTenant(tenantId)
            : null;
}
