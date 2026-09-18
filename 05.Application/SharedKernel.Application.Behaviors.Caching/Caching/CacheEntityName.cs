using System.Globalization;

namespace SharedKernel.Application.Behaviors.Caching;

/// <summary>
/// Derives the cache <c>entity</c> segment from a query type, so every entry a query writes lives in
/// a namespace of its own.
/// </summary>
/// <remarks>
/// <para>
/// Without this, two query types returning different value types but choosing the same
/// <see cref="ICacheableQuery.CacheKey"/> share one entry. Entries hold the bare value as JSON, and
/// <c>System.Text.Json</c> deserializes a foreign payload into a partially-populated object rather
/// than failing, so the collision surfaces as wrong data, never as an error.
/// </para>
/// <para>
/// The simple type name is used, not the full name, because the entity segment appears in every key
/// and a namespace-qualified name makes keys unreadable in a cache browser. Two
/// <see cref="ICacheableQuery"/> types with the same simple name in different namespaces would
/// therefore collide; <c>SharedKernel.Analyzers</c>' SK0041 reports that at compile time.
/// </para>
/// <para>
/// Generic query types include their type arguments, so <c>GetQuery&lt;Order&gt;</c> and
/// <c>GetQuery&lt;Invoice&gt;</c> do not both reduce to the arity-mangled <c>GetQuery`1</c>.
/// </para>
/// </remarks>
internal static class CacheEntityName
{
    internal static string For(Type queryType)
    {
        ArgumentNullException.ThrowIfNull(queryType);

        if (!queryType.IsGenericType)
            return queryType.Name;

        var name = queryType.Name;
        var arity = name.IndexOf('`', StringComparison.Ordinal);
        if (arity >= 0)
            name = name[..arity];

        var arguments = queryType.GetGenericArguments();
        var parts = new string[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
            parts[i] = For(arguments[i]);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name}({string.Join(",", parts)})");
    }
}
