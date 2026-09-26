using SharedKernel.Execution.Context;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// The default <see cref="IFeatureTargetingContextAccessor"/>: the caller of the open <see cref="RequestContextScope"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every inbound adapter opens a scope — HTTP, gRPC, message consume, workflow activity, scheduled job — from an
/// identity it has verified, so the scope is the caller: its <see cref="IRequestContext.UserId"/> (for an
/// authenticated caller) and its <see cref="IRequestContext.TenantId"/>. A caller with neither targets as anonymous,
/// and so does code running with no scope open.
/// </para>
/// <para>
/// <see cref="System.Diagnostics.Activity"/> baggage is never read: a caller sets baggage itself (the W3C
/// <c>baggage</c> header), so a tenant or user taken from it would let anyone choose another tenant's flags
/// (P-562 X2, merged from main in P-579).
/// </para>
/// </remarks>
internal sealed class AmbientTenantTargetingContextAccessor : IFeatureTargetingContextAccessor
{
    public FeatureTargetingContext? GetTargetingContext()
    {
        if (RequestContextScope.Current is not { } caller)
            return null;

        var userId = caller.IsAuthenticated && !string.IsNullOrWhiteSpace(caller.UserId) ? caller.UserId : null;

        return userId is null && caller.TenantId is null
            ? null
            : new FeatureTargetingContext(userId, caller.TenantId);
    }
}
