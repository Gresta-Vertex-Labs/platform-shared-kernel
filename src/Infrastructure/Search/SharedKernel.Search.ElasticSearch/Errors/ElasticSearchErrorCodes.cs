namespace SharedKernel.Search.ElasticSearch.Errors;

/// <summary>
/// The stable codes of the failures only the ElasticSearch provider produces, <c>search.elasticsearch.{snake_reason}</c>. The
/// neutral codes (<c>search.unavailable</c>, <c>search.timeout</c>, ...) are in <c>SearchErrorCodes</c> and identical on both
/// providers. A service that branches on a code below has taken a dependency on this engine.
/// </summary>
public static class ElasticSearchErrorCodes
{
    /// <summary>A cursor token is malformed (<c>Validation</c>).</summary>
    public const string InvalidCursor = "search.elasticsearch.invalid_cursor";

    /// <summary>A cursor's point-in-time lapsed or was closed (<c>Conflict</c>).</summary>
    public const string CursorExpired = "search.elasticsearch.cursor_expired";

    /// <summary>The engine rejected an aggregation request (<c>Unexpected</c>).</summary>
    public const string AggregationFailed = "search.elasticsearch.aggregation_failed";

    /// <summary>No source serializer context covers the document type (<c>Unexpected</c>).</summary>
    public const string SourceSerializerContextMissing = "search.elasticsearch.source_serializer_context_missing";
}
