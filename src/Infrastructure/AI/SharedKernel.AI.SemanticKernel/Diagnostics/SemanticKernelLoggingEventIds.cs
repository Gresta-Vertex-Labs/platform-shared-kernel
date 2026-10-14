using SharedKernel.Primitives.Logging;

namespace SharedKernel.AI.SemanticKernel.Diagnostics;

/// <summary>
/// The <c>EventId</c> constants of <c>SharedKernel.AI.SemanticKernel</c>, the 100-wide sub-block 10300-10399
/// (<see cref="LoggingEventIdRanges.Intelligence"/> + 300..399).
/// </summary>
internal static class SemanticKernelLoggingEventIds
{
    private const int Base = LoggingEventIdRanges.Intelligence + (3 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>The provider was configured (Information).</summary>
    internal const int ClientConfigured = Base + 0;

    /// <summary>An embedding call completed (Debug).</summary>
    internal const int EmbeddingCompleted = Base + 1;

    /// <summary>An embedding batch exceeded the ceiling (Warning).</summary>
    internal const int BatchSizeExceeded = Base + 2;

    /// <summary>A completion call completed (Debug).</summary>
    internal const int CompletionCompleted = Base + 3;

    /// <summary>A streaming completion started (Debug).</summary>
    internal const int StreamingStarted = Base + 4;

    /// <summary>A request was rejected before any I/O (Warning).</summary>
    internal const int RequestRejected = Base + 5;

    /// <summary>The estimated prompt exceeded the context window (Warning).</summary>
    internal const int ContextWindowExceeded = Base + 6;

    /// <summary>A recognised provider fault was classified (Warning).</summary>
    internal const int EngineFault = Base + 7;

    /// <summary>Raw client access was enabled (Warning).</summary>
    internal const int RawClientAccessEnabled = Base + 8;

    /// <summary>A bounded retry of a completion call started (Warning).</summary>
    internal const int CompletionRetrying = Base + 9;
}
