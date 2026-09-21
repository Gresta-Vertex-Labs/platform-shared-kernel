using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.EfCore.Encryption.Maintenance;

/// <summary>Enters the platform's cross-tenant scope for a maintenance operation.</summary>
/// <remarks>
/// The one place this package enters <see cref="ICrossTenantScope"/>. The platform contract is
/// <c>ICrossTenantScope.Enter(string reason)</c>; until that member exists on the interface this falls back to the
/// concrete <see cref="CrossTenantScope"/>. When the scope is replaced by the interface member, only this method
/// changes.
/// </remarks>
internal static class CrossTenantScopeBridge
{
    public static IDisposable? Enter(IServiceProvider services, string reason) =>
        services.GetService<ICrossTenantScope>() is CrossTenantScope scope ? scope.Enter(reason) : null;
}
