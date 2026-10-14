using SharedKernel.Primitives.Logging;

namespace SharedKernel.Search.Meilisearch.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of <c>SharedKernel.Search.Meilisearch</c>: the second 100-wide sub-block (9100-9199) of
/// <c>09.Search</c>'s <see cref="LoggingEventIdRanges.Search"/> block. The abstractions package logs nothing (9000-9099 stay
/// reserved) and the ElasticSearch provider owns 9200-9299. Ids are never renumbered.
/// </summary>
/// <remarks>Every field is a <see langword="const"/>, as <c>[LoggerMessage(EventId = ...)]</c> requires.</remarks>
internal static class MeilisearchLoggingEventIds
{
    /// <summary>The client was configured (Information).</summary>
    internal const int ClientConfigured = LoggingEventIdRanges.Search + 100;

    /// <summary>An index was ensured (Information).</summary>
    internal const int IndexEnsured = LoggingEventIdRanges.Search + 101;

    /// <summary>An index's settings were applied (Information).</summary>
    internal const int IndexSettingsApplied = LoggingEventIdRanges.Search + 102;

    /// <summary>Documents were enqueued for indexing (Debug).</summary>
    internal const int DocumentsEnqueued = LoggingEventIdRanges.Search + 103;

    /// <summary>A deletion was enqueued (Debug).</summary>
    internal const int DocumentsDeleted = LoggingEventIdRanges.Search + 104;

    /// <summary>An engine task completed (Debug).</summary>
    internal const int TaskCompleted = LoggingEventIdRanges.Search + 105;

    /// <summary>Waiting for an engine task timed out (Warning).</summary>
    internal const int TaskWaitTimedOut = LoggingEventIdRanges.Search + 106;

    /// <summary>An engine task failed (Error).</summary>
    internal const int TaskFailed = LoggingEventIdRanges.Search + 107;

    /// <summary>A bulk operation partially failed (Warning).</summary>
    internal const int BulkPartialFailure = LoggingEventIdRanges.Search + 108;

    /// <summary>A search was executed (Debug).</summary>
    internal const int SearchExecuted = LoggingEventIdRanges.Search + 109;

    /// <summary>A request was rejected before any I/O (Warning).</summary>
    internal const int RequestRejected = LoggingEventIdRanges.Search + 110;

    /// <summary>A tenanted index was called with the global tenant scope (Warning).</summary>
    internal const int TenantScopeMissing = LoggingEventIdRanges.Search + 111;

    /// <summary>A staging index was swapped live (Information).</summary>
    internal const int IndexesSwapped = LoggingEventIdRanges.Search + 112;

    /// <summary>A staging index was retained after the cutover (Warning).</summary>
    internal const int StagingIndexRetained = LoggingEventIdRanges.Search + 113;

    /// <summary>A tenant search token was issued (Debug).</summary>
    internal const int TenantTokenIssued = LoggingEventIdRanges.Search + 114;

    /// <summary>Raw client access was enabled (Warning).</summary>
    internal const int RawClientAccessEnabled = LoggingEventIdRanges.Search + 115;

    /// <summary>An index probe was degraded (Warning).</summary>
    internal const int ProbeDegraded = LoggingEventIdRanges.Search + 116;

    /// <summary>The write backlog is deep (Warning).</summary>
    internal const int WriteBacklogDeep = LoggingEventIdRanges.Search + 117;

    /// <summary>An index's schema fingerprint does not match its definition (Warning).</summary>
    internal const int SchemaFingerprintMismatch = LoggingEventIdRanges.Search + 118;

    /// <summary>The engine answered with a fault the kernel has no mapping for (Warning).</summary>
    internal const int EngineFault = LoggingEventIdRanges.Search + 119;

    /// <summary>A document walk started (Debug).</summary>
    internal const int DocumentWalkStarted = LoggingEventIdRanges.Search + 120;

    /// <summary>A bulk operation waited between batches (Debug).</summary>
    internal const int BulkThrottled = LoggingEventIdRanges.Search + 121;

    /// <summary>A count reached the pagination ceiling (Debug).</summary>
    internal const int CountReachedCeiling = LoggingEventIdRanges.Search + 122;

    /// <summary>An operation faulted and was mapped to an error code (Warning).</summary>
    internal const int OperationFaulted = LoggingEventIdRanges.Search + 123;

    /// <summary>An index's text analysis was applied (Information).</summary>
    internal const int TextAnalysisApplied = LoggingEventIdRanges.Search + 124;

    /// <summary>An index drifted from its registered definition at startup (Warning).</summary>
    internal const int IndexSettingsDrifted = LoggingEventIdRanges.Search + 125;

    /// <summary>An index was verified against its registered definition (Information).</summary>
    internal const int IndexSettingsVerified = LoggingEventIdRanges.Search + 126;

    /// <summary>An index's ranking rules were applied (Information).</summary>
    internal const int RankingRulesApplied = LoggingEventIdRanges.Search + 127;

    /// <summary>Issuing a tenant search token failed (Warning).</summary>
    internal const int TenantTokenIssuanceFailed = LoggingEventIdRanges.Search + 128;

    /// <summary>A cutover step failed (Warning).</summary>
    internal const int CutoverStepFailed = LoggingEventIdRanges.Search + 129;
}
