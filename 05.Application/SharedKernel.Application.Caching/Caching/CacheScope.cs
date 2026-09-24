namespace SharedKernel.Application.Caching;

/// <summary>
/// The identity a cache entry is partitioned by, declared per query and per invalidating command.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Tenant"/> is deliberately the zero value, so <c>default(CacheScope)</c> and the
/// interface defaults on <see cref="ICacheableQuery"/>/<c>IInvalidatesCache</c> all land on the
/// fail-closed option. A scope whose identity is absent at request time — <see cref="Tenant"/> with
/// no resolved tenant, <see cref="User"/> with no authenticated user — never silently degrades to a
/// wider scope: the entry is not read and not written, the handler runs, and the behavior logs it.
/// Widening is always an explicit declaration of <see cref="Global"/>, never an accident of a failed
/// resolution.
/// </para>
/// <para>
/// Scope is part of the key, so a query and the command that invalidates it must declare the same
/// scope or the eviction targets a key the query never wrote.
/// </para>
/// </remarks>
public enum CacheScope
{
    /// <summary>
    /// One entry per tenant. The key carries the tenant in <see cref="SharedKernel.Caching.Abstractions.CacheKeyFormat"/>'s
    /// tenant form, so a query can neither read nor invalidate another tenant's entry, and
    /// <c>ITenantCacheService.RemoveTenantAsync</c> removes the tenant's query results with the rest
    /// of its entries. The default, and the safe choice for anything a multi-tenant service caches.
    /// </summary>
    Tenant = 0,

    /// <summary>
    /// One entry per caller. Use it when the value depends on who is asking — anything filtered by
    /// the caller's permissions, ownership, or preferences. The tenant is included as well when one
    /// is resolved. A cached value that varies by caller under <see cref="Tenant"/> scope serves one
    /// user's data to another; that is what this value exists to prevent.
    /// </summary>
    User = 1,

    /// <summary>
    /// One entry for the whole service. Use it only for genuinely tenant-independent and
    /// caller-independent data — reference tables, ISO code lists, published rate cards. An explicit
    /// declaration that no caller identity affects the value.
    /// </summary>
    Global = 2,
}
