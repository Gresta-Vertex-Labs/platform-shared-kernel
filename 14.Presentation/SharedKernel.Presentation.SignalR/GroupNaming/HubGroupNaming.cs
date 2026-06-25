namespace SharedKernel.Presentation.SignalR.GroupNaming;

/// <summary>
/// Single source of truth for SignalR group-name formatting conventions.
/// </summary>
/// <remarks>
/// Mirrors the <c>02.Caching</c> <c>ICacheKeyProvider</c> discipline ("one formatter, many
/// callers") applied to group names instead of cache keys. Inline group-name string formatting
/// elsewhere in a consuming service is a platform violation.
/// </remarks>
public static class HubGroupNaming
{
    /// <summary>
    /// Builds the canonical SignalR group name for the specified tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The group name in the format <c>"tenant:{tenantId:D}"</c>.</returns>
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:D}";
}
