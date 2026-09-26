using System.Diagnostics;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// The default <see cref="IFeatureTargetingContextAccessor"/>: the ambient caller's tenant, and no user.
/// </summary>
/// <remarks>
/// <para>
/// When a <see cref="RequestContextScope"/> is open — every inbound adapter opens one: HTTP, gRPC, message
/// consume, workflow activity, scheduled job — the tenant is that caller's <see cref="IRequestContext.TenantId"/>,
/// and a caller without a tenant targets as anonymous. The scope is authoritative, so baggage is not consulted then.
/// </para>
/// <para>
/// Without an open scope, the tenant comes from the current activity's <see cref="WellKnownBaggageKeys.TenantId"/>
/// baggage item when it is a valid <see cref="TenantId"/>.
/// </para>
/// </remarks>
internal sealed class AmbientTenantTargetingContextAccessor : IFeatureTargetingContextAccessor
{
    public FeatureTargetingContext? GetTargetingContext()
    {
        if (RequestContextScope.Current is { } caller)
        {
            return caller.TenantId is { } callerTenant ? FeatureTargetingContext.ForTenant(callerTenant) : null;
        }

        return TenantId.TryParse(Activity.Current?.GetBaggageItem(WellKnownBaggageKeys.TenantId), out TenantId tenantId)
            ? FeatureTargetingContext.ForTenant(tenantId)
            : null;
    }
}
