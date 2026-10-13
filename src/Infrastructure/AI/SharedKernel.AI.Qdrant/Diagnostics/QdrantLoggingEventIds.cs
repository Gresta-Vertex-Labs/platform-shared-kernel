using SharedKernel.Primitives.Logging;

namespace SharedKernel.AI.Qdrant.Diagnostics;

/// <summary>
/// The <c>EventId</c> constants of <c>SharedKernel.AI.Qdrant</c>, the 100-wide sub-block 10100-10199
/// (<see cref="LoggingEventIdRanges.Intelligence"/> + 100..199). Block 10200-10299 is reserved for the next vector
/// provider.
/// </summary>
internal static class QdrantLoggingEventIds
{
    private const int Base = LoggingEventIdRanges.Intelligence + (1 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>The client was configured (Information).</summary>
    internal const int ClientConfigured = Base + 0;

    /// <summary>A collection was ensured (Information).</summary>
    internal const int CollectionEnsured = Base + 1;

    /// <summary>Records were upserted (Debug).</summary>
    internal const int RecordsUpserted = Base + 2;

    /// <summary>Records were deleted (Debug).</summary>
    internal const int RecordsDeleted = Base + 3;

    /// <summary>A bulk operation partially failed (Warning).</summary>
    internal const int BulkPartialFailure = Base + 4;

    /// <summary>A query ran (Debug).</summary>
    internal const int QueryExecuted = Base + 5;

    /// <summary>A request was rejected before any I/O (Warning).</summary>
    internal const int RequestRejected = Base + 6;

    /// <summary>A tenant-declaring collection was used with the global scope (Warning).</summary>
    internal const int TenantScopeMissing = Base + 7;

    /// <summary>A live alias was cut over (Information).</summary>
    internal const int CollectionsCutOver = Base + 8;

    /// <summary>The previous collection was retained after cutover (Warning).</summary>
    internal const int StagingCollectionRetained = Base + 9;

    /// <summary>Raw client access was enabled (Warning).</summary>
    internal const int RawClientAccessEnabled = Base + 10;

    /// <summary>A readiness probe degraded (Warning).</summary>
    internal const int ProbeDegraded = Base + 11;

    /// <summary>A collection's schema fingerprint did not match (Warning).</summary>
    internal const int SchemaFingerprintMismatch = Base + 12;

    /// <summary>A recognised provider fault was classified (Warning).</summary>
    internal const int EngineFault = Base + 13;

    /// <summary>A record walk started (Debug).</summary>
    internal const int RecordWalkStarted = Base + 14;
}
