using SharedKernel.Application.Commands;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Application.Pipeline.Shared;

/// <summary>
/// Reserves this package's <c>EventId</c> sub-block within <c>01.Core</c>'s
/// <see cref="LoggingEventIdRanges.Application"/> domain range (5000-5999), per the platform's
/// <c>[LoggerMessage]</c> logging-authoring standard.
/// </summary>
/// <remarks>
/// <c>SharedKernel.Application.Pipeline</c> is the second package declared in the
/// <c>05.Application</c> domain, so it owns the second 100-wide sub-block, <c>5100</c>-<c>5199</c>,
/// subdivided further by authoring file. Every field is <see langword="const int"/> —
/// <c>[LoggerMessage(EventId = ...)]</c> requires a compile-time constant expression.
/// </remarks>
internal static class ApplicationBehaviorsLoggingEventIds
{
    // ---- Logging/LoggingBehavior.cs (5100-5109) ----

    /// <summary>Entry log — "Handling {RequestType}" (Debug).</summary>
    internal const int LogHandling = LoggingEventIdRanges.Application + 100;

    /// <summary>Completion log, success path — "Handled {RequestType} in {ElapsedMilliseconds}ms" (Information).</summary>
    internal const int LogHandledSuccess = LoggingEventIdRanges.Application + 101;

    /// <summary>Completion log, slow success path — over the configured threshold (Warning).</summary>
    internal const int LogHandledSuccessSlow = LoggingEventIdRanges.Application + 102;

    /// <summary>Completion log, failure path — "Handled {RequestType} with failure ..." (Warning).</summary>
    internal const int LogHandledFailure = LoggingEventIdRanges.Application + 103;

    /// <summary>Fault log — "Handling {RequestType} failed after {ElapsedMilliseconds}ms" (Error).</summary>
    internal const int LogHandlingFailed = LoggingEventIdRanges.Application + 104;

    // ---- Commands/CommandScopeBehavior.cs (5110-5119) ----

    /// <summary>A post-commit <c>ICommandScope.OnCompleted</c> callback threw (Error).</summary>
    internal const int LogCommandScopeCallbackFailed = LoggingEventIdRanges.Application + 110;

    // ---- Idempotency/IdempotencyBehavior.cs (5120-5129) ----

    /// <summary>
    /// <c>CompleteAsync</c> returned <see langword="false"/> after the handler already ran
    /// successfully — the reservation was lost (expired and reclaimed, or already
    /// completed/released) before it could be confirmed (Warning). The response is still returned.
    /// </summary>
    internal const int LogCompleteReservationLost = LoggingEventIdRanges.Application + 120;

    // ---- Auditing/AuditingBehavior.cs (5130-5139) ----

    /// <summary>
    /// <c>IAuditTrailWriter.RecordAsync</c> itself threw while recording the fault entry for a
    /// command whose handler had already thrown (Error). The original handler exception is still
    /// the one that propagates — this only logs the otherwise-silent audit write failure.
    /// </summary>
    internal const int LogAuditWriteFailedDuringException = LoggingEventIdRanges.Application + 130;
}
