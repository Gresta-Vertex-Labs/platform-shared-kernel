using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Presentation.SignalR;

/// <summary>The one place SignalR group names are built, so every hub and every sender agrees on them.</summary>
/// <remarks>Never format a group name inline; add a method here instead.</remarks>
public static class HubGroupNaming
{
    /// <summary>Returns the group name of a tenant: <c>tenant:{tenantId:D}</c>.</summary>
    /// <param name="tenantId">The tenant, such as <c>Context.GetTenantId()</c>.</param>
    /// <returns>The group name, for <c>Groups.AddToGroupAsync</c> and <c>Clients.Group</c>.</returns>
    /// <remarks>
    /// A connection without a tenant has no tenant group: <c>Context.GetTenantId()</c> is then <see langword="null"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is <c>default</c>, whose value is <see cref="Guid.Empty"/>, for the reason the
    /// <see cref="TenantGroup(Guid)"/> overload gives.
    /// </exception>
    public static string TenantGroup(TenantId tenantId) => TenantGroup(tenantId.Value);

    /// <summary>Returns the group name of a tenant: <c>tenant:{tenantId:D}</c>.</summary>
    /// <param name="tenantId">The tenant's identifier.</param>
    /// <returns>The group name, for <c>Groups.AddToGroupAsync</c> and <c>Clients.Group</c>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is <see cref="Guid.Empty"/>. A connection without a tenant has no tenant group:
    /// a group for <see cref="Guid.Empty"/> would put every tenantless connection, from any caller, into one group
    /// that receives each other's messages.
    /// </exception>
    public static string TenantGroup(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "A tenant group needs a tenant. Guid.Empty would put every tenantless connection into one shared group.",
                nameof(tenantId));
        }

        return $"tenant:{tenantId:D}";
    }
}
