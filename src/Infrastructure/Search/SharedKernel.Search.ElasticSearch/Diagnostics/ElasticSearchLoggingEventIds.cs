using SharedKernel.Primitives.Logging;

namespace SharedKernel.Search.ElasticSearch.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of <c>SharedKernel.Search.ElasticSearch</c>: the third 100-wide sub-block (9200-9299) of
/// <c>09.Search</c>'s <see cref="LoggingEventIdRanges.Search"/> block. The abstractions package logs nothing (9000-9099 stay
/// reserved) and the Meilisearch provider owns 9100-9199. Ids are never renumbered.
/// </summary>
/// <remarks>Every field is a <see langword="const"/>, as <c>[LoggerMessage(EventId = ...)]</c> requires.</remarks>
internal static class ElasticSearchLoggingEventIds
{
    /// <summary>The client was configured (Information).</summary>
    internal const int ClientConfigured = LoggingEventIdRanges.Search + 200;

    /// <summary>The engine version was verified (Information).</summary>
    internal const int EngineVersionVerified = LoggingEventIdRanges.Search + 201;

    /// <summary>The engine version is not supported (Error).</summary>
    internal const int EngineVersionNotSupported = LoggingEventIdRanges.Search + 202;

    /// <summary>An index was ensured (Information).</summary>
    internal const int IndexEnsured = LoggingEventIdRanges.Search + 203;

    /// <summary>Documents were indexed (Debug).</summary>
    internal const int DocumentsIndexed = LoggingEventIdRanges.Search + 204;

    /// <summary>A bulk operation completed (Debug).</summary>
    internal const int BulkCompleted = LoggingEventIdRanges.Search + 205;

    /// <summary>A bulk operation partially failed (Warning).</summary>
    internal const int BulkPartialFailure = LoggingEventIdRanges.Search + 206;

    /// <summary>Waiting for a refresh timed out (Warning).</summary>
    internal const int RefreshWaitTimedOut = LoggingEventIdRanges.Search + 207;

    /// <summary>A search was executed (Debug).</summary>
    internal const int SearchExecuted = LoggingEventIdRanges.Search + 208;

    /// <summary>A request was rejected before any I/O (Warning).</summary>
    internal const int RequestRejected = LoggingEventIdRanges.Search + 209;

    /// <summary>A tenanted index was called with the global tenant scope (Warning).</summary>
    internal const int TenantScopeMissing = LoggingEventIdRanges.Search + 210;

    /// <summary>An alias cutover completed (Information).</summary>
    internal const int AliasCutoverCompleted = LoggingEventIdRanges.Search + 211;

    /// <summary>A staging index was deleted after the cutover (Information).</summary>
    internal const int StagingIndexDeleted = LoggingEventIdRanges.Search + 212;

    /// <summary>A point-in-time was opened (Debug).</summary>
    internal const int PointInTimeOpened = LoggingEventIdRanges.Search + 213;

    /// <summary>A point-in-time was closed (Debug).</summary>
    internal const int PointInTimeClosed = LoggingEventIdRanges.Search + 214;

    /// <summary>Closing a point-in-time failed (Warning).</summary>
    internal const int PointInTimeCloseFailed = LoggingEventIdRanges.Search + 215;

    /// <summary>Raw client access was enabled (Warning).</summary>
    internal const int RawClientAccessEnabled = LoggingEventIdRanges.Search + 216;

    /// <summary>TLS certificate validation is disabled (Warning).</summary>
    internal const int CertificateValidationDisabled = LoggingEventIdRanges.Search + 217;

    /// <summary>A yellow cluster was treated as healthy (Information).</summary>
    internal const int ClusterYellowAccepted = LoggingEventIdRanges.Search + 218;

    /// <summary>An index probe was degraded (Warning).</summary>
    internal const int ProbeDegraded = LoggingEventIdRanges.Search + 219;

    /// <summary>An index's schema fingerprint does not match its definition (Warning).</summary>
    internal const int SchemaFingerprintMismatch = LoggingEventIdRanges.Search + 220;

    /// <summary>No source serializer context was registered (Warning).</summary>
    internal const int SourceSerializerContextMissing = LoggingEventIdRanges.Search + 221;

    /// <summary>The engine answered with a fault the kernel has no mapping for (Warning).</summary>
    internal const int EngineFault = LoggingEventIdRanges.Search + 222;

    /// <summary>An aggregation was executed (Debug).</summary>
    internal const int AggregationExecuted = LoggingEventIdRanges.Search + 223;

    /// <summary>A bulk operation waited between batches (Debug).</summary>
    internal const int BulkThrottled = LoggingEventIdRanges.Search + 224;

    /// <summary>An operation faulted and was mapped to an error code (Warning).</summary>
    internal const int OperationFaulted = LoggingEventIdRanges.Search + 225;

    /// <summary>An index's text analysis was applied (Information).</summary>
    internal const int TextAnalysisApplied = LoggingEventIdRanges.Search + 226;

    /// <summary>An index drifted from its registered definition (Warning).</summary>
    internal const int IndexDrifted = LoggingEventIdRanges.Search + 227;

    /// <summary>An index was verified against its registered definition (Information).</summary>
    internal const int IndexVerified = LoggingEventIdRanges.Search + 228;

    /// <summary>A completion suggester query was executed (Debug).</summary>
    internal const int SuggestExecuted = LoggingEventIdRanges.Search + 229;

    /// <summary>A cursor read faulted and was mapped to an error code (Warning).</summary>
    internal const int CursorReadFaulted = LoggingEventIdRanges.Search + 230;
}
