using SharedKernel.Primitives.Errors;

namespace SharedKernel.Search.ElasticSearch.Errors;

/// <summary>The ElasticSearch-exclusive <see cref="Error"/> factory, for failure modes with no Meilisearch analogue.</summary>
internal static class ElasticSearchErrors
{
    /// <summary>A <see cref="ElasticSearch.Cursors.SearchCursor"/> token is malformed.</summary>
    /// <param name="reason">Kernel-authored text only: never the decoder exception message or any part of the token.</param>
    public static Error InvalidCursor(string reason) =>
        Error.Validation(ElasticSearchErrorCodes.InvalidCursor, $"The search cursor is invalid: {reason}");

    /// <summary>
    /// The point-in-time backing a <see cref="ElasticSearch.Cursors.SearchCursor"/> lapsed. The token
    /// was well-formed and the caller did nothing wrong — the world moved — so this is a
    /// <see cref="ErrorType.Conflict"/>, not a <see cref="ErrorType.Validation"/>.
    /// </summary>
    public static Error CursorExpired(DateTimeOffset expiredAt) =>
        Error.Conflict(ElasticSearchErrorCodes.CursorExpired, $"The search cursor's point-in-time expired at {expiredAt:O}.");

    /// <summary>An aggregation request failed.</summary>
    /// <param name="aggregationName">The aggregation or index name.</param>
    /// <param name="reason">Kernel-authored text, never engine, response-body or exception text.</param>
    public static Error AggregationFailed(string aggregationName, string reason) =>
        Error.Unexpected(ElasticSearchErrorCodes.AggregationFailed, $"Aggregation '{aggregationName}' failed: {reason}");

    /// <summary>
    /// No source-generated <c>JsonSerializerContext</c> was registered via
    /// <c>.WithSourceSerializerContext(...)</c> for <paramref name="documentTypeName"/>. Required for
    /// trimmed/AOT consumers, since the client disables reflection-based STJ by default.
    /// </summary>
    public static Error SourceSerializerContextMissing(string documentTypeName) =>
        Error.Unexpected(
            ElasticSearchErrorCodes.SourceSerializerContextMissing,
            $"No JsonSerializerContext was registered via WithSourceSerializerContext(...) for document type '{documentTypeName}'.");
}
