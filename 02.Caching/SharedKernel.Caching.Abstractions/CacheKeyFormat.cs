using System.Text;

namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// The platform's canonical cache key and tag format. Every <see cref="ICacheKeyProvider"/>
/// implementation must produce exactly these strings.
/// </summary>
/// <remarks>
/// <para>
/// Formats:
/// <list type="bullet">
///   <item><description>Key: <c>{service}:{entity}:{id}[:{segment}...]</c></description></item>
///   <item><description>Tenant key: <c>{service}:@{tenant}:{entity}:{id}[:{segment}...]</c></description></item>
///   <item><description>Tenant tag: <c>@{tenant}:{tag}</c></description></item>
///   <item><description>Tenant-wide tag (carried by every tenant entry): <c>@{tenant}</c></description></item>
/// </list>
/// </para>
/// <para>
/// Every caller-supplied part is escaped: <c>%</c>, <c>:</c> and <c>@</c> become <c>%25</c>,
/// <c>%3A</c> and <c>%40</c>. A part can therefore never introduce a separator or the tenant
/// marker, so two different inputs never produce the same key or tag, a global key never equals a
/// tenant key, and one tenant's tag never equals another tenant's tag or a global tag. Global tags
/// are not escaped but may not start with <c>@</c> (see <see cref="CachePolicy.WithTags"/>).
/// </para>
/// <para>
/// Application code normally goes through <see cref="ICacheKeyProvider"/> and
/// <see cref="ITenantCacheService"/>; call this class directly when implementing a provider or
/// scoping keys for a pipeline.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// CacheKeyFormat.BuildKey("orders", "invoice", "42")                     // "orders:invoice:42"
/// CacheKeyFormat.BuildTenantKey("orders", "tenant-a", "invoice", "42")   // "orders:@tenant-a:invoice:42"
/// CacheKeyFormat.BuildKey("orders", "report", "2026:Q3")                 // "orders:report:2026%3AQ3"
/// CacheKeyFormat.BuildTenantTag("tenant-a", "invoices")                  // "@tenant-a:invoices"
/// </code>
/// </example>
public static class CacheKeyFormat
{
    /// <summary>The separator between key and tag parts.</summary>
    public const char Separator = ':';

    /// <summary>The marker that starts a tenant scope in a key or tag.</summary>
    public const char TenantMarker = '@';

    /// <summary>
    /// Returns whether <paramref name="serviceName"/> is a valid key prefix: 1 to 64 characters of
    /// lowercase ASCII letters, digits, <c>.</c>, <c>_</c> or <c>-</c>, starting with a letter or digit.
    /// </summary>
    /// <param name="serviceName">The candidate service name.</param>
    /// <returns><see langword="true"/> when the name is valid.</returns>
    public static bool IsValidServiceName(string? serviceName)
    {
        if (string.IsNullOrEmpty(serviceName) || serviceName.Length > 64 || !IsLowerAlphanumeric(serviceName[0]))
        {
            return false;
        }

        foreach (char c in serviceName)
        {
            if (!IsLowerAlphanumeric(c) && c is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Escapes one caller-supplied key or tag part.</summary>
    /// <param name="part">The part to escape. Must not be null or whitespace.</param>
    /// <returns><paramref name="part"/> with <c>%</c>, <c>:</c> and <c>@</c> percent-encoded.</returns>
    /// <exception cref="ArgumentException"><paramref name="part"/> is null or whitespace.</exception>
    public static string Escape(string part)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(part);

        if (part.AsSpan().IndexOfAny('%', Separator, TenantMarker) < 0)
        {
            return part;
        }

        var builder = new StringBuilder(part.Length + 8);
        foreach (char c in part)
        {
            builder.Append(c switch
            {
                '%' => "%25",
                Separator => "%3A",
                TenantMarker => "%40",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    /// <summary>Builds a key in the format <c>{service}:{entity}:{id}[:{segment}...]</c>.</summary>
    /// <param name="serviceName">The owning service; see <see cref="IsValidServiceName"/>.</param>
    /// <param name="entity">The entity or resource name.</param>
    /// <param name="id">The entity identifier.</param>
    /// <param name="segments">Optional extra parts, such as a locale.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentException">The service name is invalid, or a part is null or whitespace.</exception>
    public static string BuildKey(string serviceName, string entity, string id, params ReadOnlySpan<string> segments)
    {
        ValidateServiceName(serviceName);

        var builder = new StringBuilder(serviceName);
        AppendParts(builder, entity, id, segments);
        return builder.ToString();
    }

    /// <summary>
    /// Builds a tenant key in the format <c>{service}:@{tenant}:{entity}:{id}[:{segment}...]</c>.
    /// </summary>
    /// <param name="serviceName">The owning service; see <see cref="IsValidServiceName"/>.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="entity">The entity or resource name.</param>
    /// <param name="id">The entity identifier.</param>
    /// <param name="segments">Optional extra parts, such as a locale.</param>
    /// <returns>The tenant key.</returns>
    /// <exception cref="ArgumentException">The service name is invalid, or a part is null or whitespace.</exception>
    public static string BuildTenantKey(
        string serviceName,
        string tenantId,
        string entity,
        string id,
        params ReadOnlySpan<string> segments)
    {
        ValidateServiceName(serviceName);

        var builder = new StringBuilder(serviceName)
            .Append(Separator)
            .Append(TenantMarker)
            .Append(Escape(tenantId));
        AppendParts(builder, entity, id, segments);
        return builder.ToString();
    }

    /// <summary>Builds a tenant tag in the format <c>@{tenant}:{tag}</c>.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="tag">The tag name.</param>
    /// <returns>The tenant tag.</returns>
    /// <exception cref="ArgumentException">A part is null or whitespace.</exception>
    public static string BuildTenantTag(string tenantId, string tag) =>
        string.Concat(TenantMarker.ToString(), Escape(tenantId), Separator.ToString(), Escape(tag));

    /// <summary>
    /// Builds the tag every tenant entry carries, in the format <c>@{tenant}</c>. Removing it removes
    /// every entry of that tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The tenant-wide tag.</returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is null or whitespace.</exception>
    public static string BuildTenantWideTag(string tenantId) => TenantMarker + Escape(tenantId);

    private static void AppendParts(StringBuilder builder, string entity, string id, ReadOnlySpan<string> segments)
    {
        builder.Append(Separator).Append(Escape(entity)).Append(Separator).Append(Escape(id));
        foreach (string segment in segments)
        {
            builder.Append(Separator).Append(Escape(segment));
        }
    }

    private static void ValidateServiceName(string serviceName)
    {
        if (!IsValidServiceName(serviceName))
        {
            throw new ArgumentException(
                "The service name must be 1 to 64 lowercase ASCII letters, digits, '.', '_' or '-', starting with a letter or digit.",
                nameof(serviceName));
        }
    }

    private static bool IsLowerAlphanumeric(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9');
}
