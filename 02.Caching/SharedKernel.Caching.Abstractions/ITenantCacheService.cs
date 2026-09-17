namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// A tenant-isolated cache: every call takes an explicit tenant, builds the key itself and scopes
/// every tag to that tenant. Use it for any data that belongs to a tenant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <see cref="ICacheService"/> with a tenant in the key.</b> Hand-built keys and tags are
/// easy to get wrong, and tags live in one global namespace: two tenants tagging entries
/// <c>"orders"</c> would evict each other's data. This interface takes <c>(tenantId, entity, id)</c>,
/// never a pre-built key, so tenant scoping cannot be skipped.
/// </para>
/// <para>
/// <b>Isolation guarantee.</b> Keys are <c>{service}:@{tenant}:{entity}:{id}</c>, tags are
/// <c>@{tenant}:{tag}</c>, and every part is escaped (<see cref="CacheKeyFormat"/>). No tenant's key
/// or tag can equal another tenant's or a global one. Every entry also carries the tenant-wide tag,
/// which <see cref="RemoveTenantAsync"/> uses.
/// </para>
/// <para>
/// <b>Tenant identity</b> is always the caller's argument, never read from ambient state. Resolve it
/// at the edge (for example from <c>ITenantProvider</c>) and pass it down.
/// </para>
/// <para>
/// <b>Policies</b> are passed unscoped; the service applies <see cref="CachePolicy.ForTenant"/>. The
/// same fail-safe, eager refresh and distribution behaviour as <see cref="ICacheService"/> applies.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class InvoiceReader(ITenantCacheService cache, IInvoiceRepository invoices)
/// {
///     public ValueTask&lt;Invoice?&gt; GetAsync(string tenantId, string invoiceId, CancellationToken ct) =&gt;
///         cache.GetOrSetAsync(
///             tenantId, "invoice", invoiceId,
///             token =&gt; invoices.FindAsync(tenantId, invoiceId, token),
///             CachePolicy.Default.WithTags("invoices"),
///             ct);
///
///     public ValueTask InvalidateAllAsync(string tenantId, CancellationToken ct) =&gt;
///         cache.RemoveByTagAsync(tenantId, "invoices", ct); // this tenant only
/// }
/// </code>
/// </example>
public interface ITenantCacheService
{
    /// <summary>Reads a tenant entry without computing it.</summary>
    /// <typeparam name="T">The type the value was stored as.</typeparam>
    /// <param name="tenantId">The tenant identifier. Must not be null or whitespace.</param>
    /// <param name="entity">The entity or resource name, such as <c>"invoice"</c>. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A hit carrying the value (which may be <see langword="null"/>), or <see cref="CacheLookup{T}.Miss"/>.</returns>
    /// <exception cref="ArgumentException">A part is null or whitespace.</exception>
    ValueTask<CacheLookup<T>> TryGetAsync<T>(string tenantId, string entity, string id, CancellationToken ct = default);

    /// <summary>Returns a tenant entry, or computes, stores and returns it; the factory runs once per key at a time.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="tenantId">The tenant identifier. Must not be null or whitespace.</param>
    /// <param name="entity">The entity or resource name. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="factory">Computes the value on a miss.</param>
    /// <param name="policy">How the value is stored, with unscoped tags. Must not already be tenant-scoped.</param>
    /// <param name="ct">Cancellation token, also passed to the factory.</param>
    /// <returns>The cached or freshly computed value.</returns>
    /// <exception cref="ArgumentException">A part is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> or <paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="policy"/> is already tenant-scoped.</exception>
    ValueTask<T> GetOrSetAsync<T>(
        string tenantId,
        string entity,
        string id,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a tenant entry, or computes it with a factory that decides through its
    /// <see cref="CacheFactoryContext"/> whether and how long the value is stored.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="tenantId">The tenant identifier. Must not be null or whitespace.</param>
    /// <param name="entity">The entity or resource name. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="factory">Computes the value on a miss and records its caching decision.</param>
    /// <param name="policy">How the value is stored unless the factory overrides it, with unscoped tags. Must not already be tenant-scoped.</param>
    /// <param name="ct">Cancellation token, also passed to the factory.</param>
    /// <returns>The cached or freshly computed value.</returns>
    /// <exception cref="ArgumentException">A part is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> or <paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="policy"/> is already tenant-scoped.</exception>
    ValueTask<T> GetOrSetAsync<T>(
        string tenantId,
        string entity,
        string id,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default);

    /// <summary>Stores a tenant entry, replacing any existing one.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="tenantId">The tenant identifier. Must not be null or whitespace.</param>
    /// <param name="entity">The entity or resource name. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="value">The value to store; may be <see langword="null"/>.</param>
    /// <param name="policy">How the value is stored, with unscoped tags. Must not already be tenant-scoped.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">A part is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="policy"/> is already tenant-scoped.</exception>
    ValueTask SetAsync<T>(string tenantId, string entity, string id, T value, CachePolicy policy, CancellationToken ct = default);

    /// <summary>Removes a tenant entry from every layer. Does nothing when it is absent.</summary>
    /// <param name="tenantId">The tenant identifier. Must not be null or whitespace.</param>
    /// <param name="entity">The entity or resource name. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">A part is null or whitespace.</exception>
    ValueTask RemoveAsync(string tenantId, string entity, string id, CancellationToken ct = default);

    /// <summary>
    /// Marks a tenant entry as expired: the next <c>GetOrSetAsync</c> recomputes it, but fail-safe can
    /// still serve the old value if that fails.
    /// </summary>
    /// <param name="tenantId">The tenant identifier. Must not be null or whitespace.</param>
    /// <param name="entity">The entity or resource name. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">A part is null or whitespace.</exception>
    ValueTask ExpireAsync(string tenantId, string entity, string id, CancellationToken ct = default);

    /// <summary>Removes the tenant's entries that carry <paramref name="tag"/>. Other tenants are never affected.</summary>
    /// <param name="tenantId">The tenant identifier. Must not be null or whitespace.</param>
    /// <param name="tag">The unscoped tag, as passed to <see cref="CachePolicy.WithTags"/>. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">A part is null or whitespace.</exception>
    ValueTask RemoveByTagAsync(string tenantId, string tag, CancellationToken ct = default);

    /// <summary>
    /// Removes every entry of the tenant: everything written through this service, and any other entry
    /// stored with a <see cref="CachePolicy.ForTenant"/> policy. Use it when a tenant is suspended or offboarded.
    /// </summary>
    /// <param name="tenantId">The tenant identifier. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is null or whitespace.</exception>
    ValueTask RemoveTenantAsync(string tenantId, CancellationToken ct = default);
}
