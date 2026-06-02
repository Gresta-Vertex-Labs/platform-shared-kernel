using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Persistence.EfCore.Extensions;

/// <summary>
/// No-op <see cref="ITenantProvider"/> placeholder registered by
/// <see cref="EfCorePersistenceBuilder{TContext}.WithMultiTenancy"/> when no real
/// <see cref="ITenantProvider"/> implementation is present in the DI container.
/// </summary>
/// <remarks>
/// Always returns <see cref="Guid.Empty"/> explicitly (not <c>default(Guid)</c>). When this
/// provider is active the global tenant filter becomes <c>e.TenantId == Guid.Empty</c>, which
/// returns <strong>zero rows</strong> — no production entity should ever carry
/// <c>TenantId == Guid.Empty</c>. This is intentional: teams that forget to register a real
/// provider see an empty result set immediately rather than a cross-tenant data leak.
/// Override by registering a scoped <see cref="ITenantProvider"/> implementation (e.g., from
/// <c>SharedKernel.Security.Oidc</c>) before or after calling <c>.Build()</c>.
/// </remarks>
internal sealed class NoOpTenantProvider : ITenantProvider
{
    /// <inheritdoc />
    public Guid TenantId => Guid.Empty;
}
