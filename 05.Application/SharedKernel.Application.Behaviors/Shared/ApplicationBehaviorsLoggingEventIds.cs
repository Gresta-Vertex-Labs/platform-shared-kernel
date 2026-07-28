using SharedKernel.Primitives.Logging;

namespace SharedKernel.Application.Behaviors.Shared;

/// <summary>
/// Reserves this package's <c>EventId</c> sub-block within <c>01.Core</c>'s
/// <see cref="LoggingEventIdRanges.Application"/> domain range (5000-5999), per the platform's
/// <c>[LoggerMessage]</c> logging-authoring standard (WO-041, P-249/P-250/P-253).
/// </summary>
/// <remarks>
/// <para>
/// <c>SharedKernel.Application.Behaviors</c> is the second package declared in the
/// <c>05.Application</c> domain (after <c>SharedKernel.Application</c>, which currently has zero
/// <c>ILogger</c> call sites and so reserves but does not yet use <c>5000</c>-<c>5099</c>) — this
/// package therefore owns the second 100-wide sub-block, <c>5100</c>-<c>5199</c>, subdivided into
/// three named sub-ranges, one per authoring file:
/// </para>
/// <list type="bullet">
///   <item><description><c>Logging/LoggingBehavior.cs</c> — <c>5100</c>-<c>5109</c>.</description></item>
///   <item><description><c>FireAndForget/</c> — <c>5110</c>-<c>5119</c>.</description></item>
///   <item><description><c>Streaming/StreamLoggingBehavior.cs</c> — <c>5120</c>-<c>5129</c>.</description></item>
/// </list>
/// <para>
/// Every field is <see langword="const int"/> — <c>[LoggerMessage(EventId = ...)]</c> requires a
/// compile-time constant expression, so anything short of <see langword="const"/> (e.g.
/// <see langword="static readonly"/>) would fail to compile at every call site. Unused headroom
/// within each sub-range (<c>5104</c>-<c>5109</c>, <c>5113</c>-<c>5119</c>, <c>5124</c>-<c>5129</c>)
/// is reserved for future log statements in the same file without renumbering anything already
/// shipped.
/// </para>
/// </remarks>
internal static class ApplicationBehaviorsLoggingEventIds
{
    // ---- Logging/LoggingBehavior.cs (5100-5109) ----

    /// <summary>Entry log — "Handling {RequestName}" (Information).</summary>
    internal const int LogHandling = LoggingEventIdRanges.Application + 100;

    /// <summary>Completion log, success path — "Handled {RequestName} in {ElapsedMilliseconds}ms" (Information).</summary>
    internal const int LogHandledSuccess = LoggingEventIdRanges.Application + 101;

    /// <summary>Completion log, failure path — "Handled {RequestName} with failure in {ElapsedMilliseconds}ms" (Warning).</summary>
    internal const int LogHandledFailure = LoggingEventIdRanges.Application + 102;

    /// <summary>Fault log — "Handling {RequestName} failed after {ElapsedMilliseconds}ms" (Error).</summary>
    internal const int LogHandlingFailed = LoggingEventIdRanges.Application + 103;

    // ---- FireAndForget/ (5110-5119) ----

    /// <summary><see cref="FireAndForget.ChannelFireAndForgetDispatcher"/>'s full-channel drop log (Warning).</summary>
    internal const int LogChannelFull = LoggingEventIdRanges.Application + 110;

    /// <summary><see cref="FireAndForget.FireAndForgetBackgroundConsumer"/>'s handler-fault log (Error).</summary>
    internal const int LogCommandFaulted = LoggingEventIdRanges.Application + 111;

    /// <summary>
    /// <see cref="FireAndForget.FireAndForgetBackgroundConsumer"/>'s handler-failure log (Warning) —
    /// the dispatched command's handler returned <c>Result.Failure</c> (a deliberate business-rule
    /// outcome, not a thrown exception). Added to close the SK0030 real-source-audit finding that this
    /// outcome previously produced zero telemetry (WO-049, P-299 candidate follow-up).
    /// </summary>
    internal const int LogCommandFailed = LoggingEventIdRanges.Application + 112;

    // ---- Streaming/StreamLoggingBehavior.cs (5120-5129) ----

    /// <summary>Stream-opened entry log — "Streaming {RequestName} started." (Information).</summary>
    internal const int LogStreamStarted = LoggingEventIdRanges.Application + 120;

    /// <summary>First-item latency log — "Streaming {RequestName} produced first item in {ElapsedMilliseconds}ms." (Debug).</summary>
    internal const int LogFirstItem = LoggingEventIdRanges.Application + 121;

    /// <summary>Stream-completed log — "Streaming {RequestName} completed in {ElapsedMilliseconds}ms." (Information).</summary>
    internal const int LogStreamCompleted = LoggingEventIdRanges.Application + 122;

    /// <summary>Stream-faulted log — "Streaming {RequestName} faulted after {ElapsedMilliseconds}ms." (Warning).</summary>
    internal const int LogStreamFaulted = LoggingEventIdRanges.Application + 123;
}
