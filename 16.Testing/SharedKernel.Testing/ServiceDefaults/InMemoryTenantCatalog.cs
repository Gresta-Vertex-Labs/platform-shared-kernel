using System.Collections.Concurrent;
using SharedKernel.MultiTenancy.Catalog;

namespace SharedKernel.Testing.ServiceDefaults;

/// <summary>
/// In-memory fake implementation of <see cref="ITenantCatalog"/> for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>SCOPE-LOCK REVISION (P-473/WO-075), not silently overridden:</b> this domain's existing
/// scope lock (P-187/WO-029) forbids this package from ever referencing
/// <c>SharedKernel.ServiceDefaults</c>/<c>SharedKernel.MultiTenancy</c> — <c>StaticTenantProvider</c>/
/// <c>FakeTenantResolutionStrategy</c> in this same folder are deliberately duck-typed/reference-free
/// against that package. <see cref="InMemoryTenantCatalog"/> is the ONE named exception: it takes a
/// genuine <c>ProjectReference</c> to <c>SharedKernel.MultiTenancy</c> because <see cref="ITenantCatalog"/>
/// must be a real interface implementation, not a duck-typed stand-in, per this phase's own
/// acceptance criteria. <c>StaticTenantProvider</c>/<c>FakeTenantResolutionStrategy</c> are
/// unaffected and remain reference-free.
/// </para>
/// <para>
/// Dictionary-backed: a primary store keyed by <see cref="TenantDescriptor.TenantId"/>, plus a
/// secondary index mapping an arbitrary caller-supplied resolution key (host/claim/header value) to
/// a tenant id. <see cref="GetByIdAsync"/>/<see cref="GetByResolutionKeyAsync"/> return
/// <see langword="null"/> for an unseeded lookup — never throw, matching <see cref="ITenantCatalog"/>'s
/// own documented "never throws for an absent tenant" contract.
/// </para>
/// <para>
/// <see cref="MutateStatus"/> lets a test flip a seeded tenant to <see cref="TenantStatus.Suspended"/>/
/// <see cref="TenantStatus.Offboarded"/> mid-test without re-seeding — composes with the real (once
/// wired) <see cref="CatalogTenantStatusValidator"/> with zero code changes on either side, since
/// that type depends only on the <see cref="ITenantCatalog"/> interface, never a concrete
/// implementation type.
/// </para>
/// <para>
/// <b>No cross-instance invalidation surface</b> — unlike <c>CachedTenantCatalog</c> (which exposes
/// a consumer-wired <c>HandleCrossInstanceInvalidationSignal(Guid)</c> entry point, since
/// <c>02.Caching.Abstractions</c>'s <c>ICacheInvalidationBus</c> is publish-only with no subscribe
/// surface), this fake is a single-instance, single-process in-memory store with no caching layer
/// to invalidate in the first place — a test simply calls <see cref="SeedTenant"/>/
/// <see cref="MutateStatus"/> directly.
/// </para>
/// </remarks>
public sealed class InMemoryTenantCatalog : ITenantCatalog
{
    private readonly ConcurrentDictionary<Guid, TenantDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, Guid> _byResolutionKey = new();

    /// <summary>
    /// Seeds <paramref name="descriptor"/> into the catalog, optionally indexing it under
    /// <paramref name="resolutionKey"/> too.
    /// </summary>
    /// <param name="descriptor">The tenant descriptor to seed.</param>
    /// <param name="resolutionKey">
    /// An optional raw resolution value (host/claim/header value) this tenant should also be
    /// reachable by via <see cref="GetByResolutionKeyAsync"/>.
    /// </param>
    public void SeedTenant(TenantDescriptor descriptor, string? resolutionKey = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        _byId[descriptor.TenantId] = descriptor;

        if (resolutionKey is not null)
        {
            _byResolutionKey[resolutionKey] = descriptor.TenantId;
        }
    }

    /// <summary>
    /// Mutates a seeded tenant's <see cref="TenantDescriptor.Status"/> in place, without requiring
    /// the caller to re-seed the full descriptor.
    /// </summary>
    /// <param name="tenantId">The tenant to mutate.</param>
    /// <param name="status">The new status.</param>
    /// <remarks>A no-op when <paramref name="tenantId"/> was never seeded.</remarks>
    public void MutateStatus(Guid tenantId, TenantStatus status)
    {
        if (_byId.TryGetValue(tenantId, out var existing))
        {
            _byId[tenantId] = existing with { Status = status };
        }
    }

    /// <inheritdoc />
    public Task<TenantDescriptor?> GetByIdAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult(_byId.TryGetValue(tenantId, out var descriptor) ? descriptor : null);

    /// <inheritdoc />
    public Task<TenantDescriptor?> GetByResolutionKeyAsync(string resolutionKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolutionKey);

        if (_byResolutionKey.TryGetValue(resolutionKey, out var tenantId) && _byId.TryGetValue(tenantId, out var descriptor))
        {
            return Task.FromResult<TenantDescriptor?>(descriptor);
        }

        return Task.FromResult<TenantDescriptor?>(null);
    }

    /// <summary>Clears every seeded tenant and resolution-key index entry.</summary>
    public void Reset()
    {
        _byId.Clear();
        _byResolutionKey.Clear();
    }
}
