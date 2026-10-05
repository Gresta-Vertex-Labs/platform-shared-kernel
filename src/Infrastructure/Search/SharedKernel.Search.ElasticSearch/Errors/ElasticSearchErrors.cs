using SharedKernel.Primitives.Errors;

namespace SharedKernel.Search.ElasticSearch.Errors;

/// <summary>The ElasticSearch-exclusive <see cref="Error"/> factory, for failure modes with no Meilisearch analogue.</summary>
public static class ElasticSearchErrors
{
    private const string InvalidCursorCode = "search.elasticsearch.invalid_cursor";
    private const string CursorExpiredCode = "search.elasticsearch.cursor_expired";
    private const string AggregationFailedCode = "search.elasticsearch.aggregation_failed";
    private const string SourceSerializerContextMissingCode = "search.elasticsearch.source_serializer_context_missing";

    /// <summary>A <see cref="ElasticSearch.Cursors.SearchCursor"/> token is malformed.</summary>
    public static Error InvalidCursor(string reason) =>
        Error.Validation(InvalidCursorCode, $"The search cursor is invalid: {reason}");

    /// <summary>
    /// The point-in-time backing a <see cref="ElasticSearch.Cursors.SearchCursor"/> lapsed. The token
    /// was well-formed and the caller did nothing wrong — the world moved — so this is a
    /// <see cref="ErrorType.Conflict"/>, not a <see cref="ErrorType.Validation"/>.
    /// </summary>
    public static Error CursorExpired(DateTimeOffset expiredAt) =>
        Error.Conflict(CursorExpiredCode, $"The search cursor's point-in-time expired at {expiredAt:O}.");

    /// <summary>An aggregation request failed.</summary>
    public static Error AggregationFailed(string aggregationName, string reason) =>
        Error.Unexpected(AggregationFailedCode, $"Aggregation '{aggregationName}' failed: {reason}");

    /// <summary>
    /// No source-generated <c>JsonSerializerContext</c> was registered via
    /// <c>.WithSourceSerializerContext(...)</c> for <paramref name="documentTypeName"/>. Required for
    /// trimmed/AOT consumers, since the client disables reflection-based STJ by default.
    /// </summary>
    public static Error SourceSerializerContextMissing(string documentTypeName) =>
        Error.Unexpected(
            SourceSerializerContextMissingCode,
            $"No JsonSerializerContext was registered via WithSourceSerializerContext(...) for document type '{documentTypeName}'.");
}
