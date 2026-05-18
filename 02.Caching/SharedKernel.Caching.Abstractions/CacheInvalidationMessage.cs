using System.Diagnostics;
using System.Text.Json.Serialization;

namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Classifies the scope of a cache invalidation broadcast.
/// </summary>
public enum CacheInvalidationType
{
    /// <summary>Invalidate specific cache keys listed in <see cref="CacheInvalidationMessage.Keys"/>.</summary>
    Key,

    /// <summary>Invalidate all entries carrying any of the tags in <see cref="CacheInvalidationMessage.Tags"/>.</summary>
    Tag,

    /// <summary>
    /// Broadcast full-cache invalidation. Receivers log a warning and rely on TTL expiry;
    /// no key enumeration is attempted.
    /// </summary>
    All,
}

/// <summary>
/// Wire payload for a cross-service cache invalidation signal transmitted over Redis Pub/Sub.
/// </summary>
/// <remarks>
/// <para>
/// Serialized to/from JSON using the <see cref="CacheInvalidationMessageJsonContext"/> STJ
/// source-generated context — no reflection is used, ensuring AOT compatibility.
/// </para>
/// <para>
/// <see cref="CorrelationId"/> defaults to <c>Activity.Current?.Id</c> if an active trace is
/// present, otherwise a compact GUID. Receivers use this value to link OTel spans back to the
/// originating trace for end-to-end distributed tracing continuity.
/// </para>
/// </remarks>
/// <param name="SourceService">The service name that originated the invalidation (e.g., <c>"order-svc"</c>).</param>
/// <param name="InvalidationType">The scope of invalidation: specific keys, tags, or full broadcast.</param>
/// <param name="Keys">Cache keys to invalidate. Required when <see cref="InvalidationType"/> is <see cref="CacheInvalidationType.Key"/>.</param>
/// <param name="Tags">Cache tags to invalidate. Required when <see cref="InvalidationType"/> is <see cref="CacheInvalidationType.Tag"/>.</param>
/// <param name="CorrelationId">
/// Distributed trace correlation identifier. Defaults to <c>Activity.Current?.Id</c> if an
/// active OTel trace is present; otherwise a compact GUID (<c>Guid.NewGuid().ToString("N")</c>).
/// </param>
/// <param name="TimestampUtc">The UTC timestamp at which the invalidation was published.</param>
[method: JsonConstructor]
public sealed record CacheInvalidationMessage(
    string SourceService,
    CacheInvalidationType InvalidationType,
    string[]? Keys,
    string[]? Tags,
    string CorrelationId,
    DateTimeOffset TimestampUtc)
{
    /// <summary>
    /// Creates a <see cref="CacheInvalidationMessage"/> with <see cref="CorrelationId"/>
    /// automatically derived from the active OTel trace, and <see cref="TimestampUtc"/>
    /// set to <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    /// <param name="sourceService">The originating service name.</param>
    /// <param name="invalidationType">The invalidation scope.</param>
    /// <param name="keys">Cache keys to invalidate (for <see cref="CacheInvalidationType.Key"/>).</param>
    /// <param name="tags">Cache tags to invalidate (for <see cref="CacheInvalidationType.Tag"/>).</param>
    public CacheInvalidationMessage(
        string sourceService,
        CacheInvalidationType invalidationType,
        string[]? keys = null,
        string[]? tags = null)
        : this(
            sourceService,
            invalidationType,
            keys,
            tags,
            Activity.Current?.Id ?? Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow)
    {
    }
}

/// <summary>
/// STJ source-generated serializer context for <see cref="CacheInvalidationMessage"/>.
/// Use <see cref="Default"/> to obtain the <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/>
/// required by <c>IRedisHashService</c> and <c>ICacheInvalidationBus</c> implementations.
/// </summary>
[JsonSerializable(typeof(CacheInvalidationMessage))]
[JsonSerializable(typeof(CacheInvalidationType))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
public sealed partial class CacheInvalidationMessageJsonContext : JsonSerializerContext;
