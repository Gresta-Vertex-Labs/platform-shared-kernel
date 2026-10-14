using SharedKernel.Primitives.Logging;

namespace SharedKernel.Application.Pipeline.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of <c>SharedKernel.Application.Pipeline</c>: sub-block 1 (<c>5100</c>-<c>5199</c>) of the
/// <see cref="LoggingEventIdRanges.Application"/> domain range.
/// </summary>
/// <remarks>
/// Sub-block 0 (<c>5000</c>) belongs to <c>SharedKernel.Application</c> and sub-block 3 (<c>5300</c>) to
/// <c>SharedKernel.Application.Mediator.MediatR</c>; neither logs. Every value is a <see langword="const"/>
/// because <c>[LoggerMessage(EventId = ...)]</c> needs a compile-time constant. The numbers are grouped by behavior in
/// tens, and a published number is never reused for another event.
/// </remarks>
internal static class PipelineLoggingEventIds
{
    /// <summary>The first id of the sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Application + 1 * LoggingEventIdRanges.PackageSubBlockWidth;

    // ---- LoggingBehavior (5100-5109) ----

    /// <summary>A request entered the pipeline (Debug).</summary>
    internal const int Handling = Base + 0;

    /// <summary>A request succeeded within the slow-request threshold (Debug).</summary>
    internal const int HandledSuccess = Base + 1;

    /// <summary>A request succeeded but crossed the slow-request threshold (Warning).</summary>
    internal const int HandledSlow = Base + 2;

    /// <summary>A request returned a failed <c>Result</c> (Warning).</summary>
    internal const int HandledFailure = Base + 3;

    /// <summary>A request threw (Error).</summary>
    internal const int HandlingThrew = Base + 4;

    /// <summary>A request was canceled by its caller (Debug).</summary>
    internal const int HandlingCanceled = Base + 5;

    // ---- CommandScopeBehavior (5110-5119) ----

    /// <summary>A post-commit <c>ICommandScope.OnCompleted</c> callback threw (Error).</summary>
    internal const int CommandCallbackFailed = Base + 10;

    // ---- IdempotencyBehavior (5120-5129) ----

    /// <summary>The reservation was lost before <c>CompleteAsync</c> could confirm it (Warning).</summary>
    internal const int IdempotencyReservationLost = Base + 20;

    /// <summary><c>TryBeginAsync</c> of the idempotency store threw, so the request was not handled (Error; Warning when classified).</summary>
    internal const int IdempotencyStoreUnavailable = Base + 21;

    /// <summary><c>CompleteAsync</c> threw after the handler succeeded (Error).</summary>
    internal const int IdempotencyCompleteFailed = Base + 22;

    /// <summary><c>ReleaseAsync</c> threw (Warning).</summary>
    internal const int IdempotencyReleaseFailed = Base + 23;

    // ---- AuditingBehavior (5130-5139) ----

    /// <summary>Recording the fault entry of an audited command threw while the handler's exception was propagating (Error).</summary>
    internal const int AuditWriteFailedDuringException = Base + 30;

    // ---- StreamObservabilityBehavior (5140-5149) ----

    /// <summary>A stream ran to the end (Debug).</summary>
    internal const int StreamCompleted = Base + 40;

    /// <summary>A stream was canceled or disposed before the end (Debug).</summary>
    internal const int StreamCanceled = Base + 41;

    /// <summary>A stream failed with a classified exception (Warning).</summary>
    internal const int StreamFailedClassified = Base + 42;

    /// <summary>A stream failed with an unexpected exception (Error).</summary>
    internal const int StreamFailed = Base + 43;

    // ---- DomainEventDispatcher (5150-5159) ----

    /// <summary>A domain-event handler threw (Error; Warning when classified).</summary>
    internal const int DomainEventHandlerFailed = Base + 50;

    // ---- TransactionBehavior (5160-5169) ----

    /// <summary>The transaction was rolled back because the command returned a failed <c>Result</c> (Warning).</summary>
    internal const int TransactionRolledBackByResult = Base + 60;

    /// <summary>The transaction was rolled back because the command threw (Error).</summary>
    internal const int TransactionRolledBackByException = Base + 61;

    /// <summary>The unit of work ran the transaction delegate more than once (Warning).</summary>
    internal const int TransactionReplayed = Base + 62;
}
