using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.Extensions;

/// <summary>
/// No-op <see cref="ICurrentTenantService"/> placeholder registered by
/// <see cref="EfCorePersistenceBuilder{TContext}.WithMultiTenancy"/>.
/// Always returns <see langword="null"/>, meaning no tenant filter is applied until a real
/// implementation is registered by the consuming service.
/// </summary>
/// <remarks>
/// Override this by registering a scoped <see cref="ICurrentTenantService"/> implementation
/// (e.g., from <c>13.ServiceDefaults.MultiTenancy</c>) before or after calling <c>.Build()</c>.
/// </remarks>
internal sealed class NoOpCurrentTenantService : ICurrentTenantService
{
    /// <inheritdoc />
    public Guid? TenantId => null;
}
