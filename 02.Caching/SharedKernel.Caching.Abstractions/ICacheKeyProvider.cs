namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Builds cache keys prefixed with the owning service's name, in the format defined by
/// <see cref="CacheKeyFormat"/>.
/// </summary>
/// <remarks>
/// Build every key through this interface instead of by string interpolation: it prefixes the
/// service name and escapes each part, so two different inputs never produce the same key.
/// To change a cached type's shape across a deployment, change the key, for example
/// <c>BuildKey("invoice-v2", id)</c>.
/// </remarks>
/// <example>
/// <code>
/// // With ServiceName "orders":
/// keys.BuildKey("invoice", "42")            // "orders:invoice:42"
/// keys.BuildKey("invoice", "42", "en-GB")   // "orders:invoice:42:en-GB"
/// </code>
/// </example>
public interface ICacheKeyProvider
{
    /// <summary>Builds a key in the format <c>{service}:{entity}:{id}[:{segment}...]</c>.</summary>
    /// <param name="entity">The entity or resource name, such as <c>"invoice"</c>. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="segments">Optional extra parts, such as a locale. Each must not be null or whitespace.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentException">A part is null or whitespace.</exception>
    string BuildKey(string entity, string id, params string[] segments);
}
