namespace SharedKernel.Idempotency.Abstractions;

/// <summary>
/// The names of the idempotency telemetry every provider emits: one <c>ActivitySource</c> and one <c>Meter</c> named
/// <see cref="SourceName"/>, distinguished per provider by the <see cref="IdempotencyTagKeys.Provider"/> tag.
/// </summary>
/// <remarks>
/// <para>
/// This type holds constants only; each provider owns its instances. A host subscribes through the
/// <c>SharedKernel.*</c> wildcard, so no per-domain registration is needed.
/// </para>
/// <para>
/// <b>Spans</b> (kind Client): <see cref="BeginSpanName"/>, <see cref="CompleteSpanName"/>,
/// <see cref="ReleaseSpanName"/>. <b>Instruments:</b> the histogram <see cref="OperationDurationInstrument"/>
/// (unit <c>s</c>; tags operation, purpose, provider, <c>outcome</c>, result) and the counter
/// <see cref="FailOpenInstrument"/> (unit <c>{operation}</c>; tags operation, purpose, provider), the alert signal
/// for duplicate-execution risk.
/// </para>
/// </remarks>
public static class IdempotencyTelemetry
{
    /// <summary>The <c>ActivitySource</c> and <c>Meter</c> name (<c>"SharedKernel.Idempotency"</c>).</summary>
    public const string SourceName = "SharedKernel.Idempotency";

    /// <summary>The <c>Meter</c> name; the same as <see cref="SourceName"/>.</summary>
    public const string MeterName = SourceName;

    /// <summary>The name of the span around <c>TryBeginAsync</c>.</summary>
    public const string BeginSpanName = "idempotency begin";

    /// <summary>The name of the span around <c>CompleteAsync</c>.</summary>
    public const string CompleteSpanName = "idempotency complete";

    /// <summary>The name of the span around <c>ReleaseAsync</c>.</summary>
    public const string ReleaseSpanName = "idempotency release";

    /// <summary>The duration histogram, in seconds (<c>"sharedkernel.idempotency.operation.duration"</c>).</summary>
    public const string OperationDurationInstrument = "sharedkernel.idempotency.operation.duration";

    /// <summary>The counter of operations let through by fail-open (<c>"sharedkernel.idempotency.fail_open"</c>).</summary>
    public const string FailOpenInstrument = "sharedkernel.idempotency.fail_open";

    /// <summary>The <see cref="IdempotencyTagKeys.Operation"/> value of <c>TryBeginAsync</c> (<c>"begin"</c>).</summary>
    public const string OperationBegin = "begin";

    /// <summary>The <see cref="IdempotencyTagKeys.Operation"/> value of <c>CompleteAsync</c> (<c>"complete"</c>).</summary>
    public const string OperationComplete = "complete";

    /// <summary>The <see cref="IdempotencyTagKeys.Operation"/> value of <c>ReleaseAsync</c> (<c>"release"</c>).</summary>
    public const string OperationRelease = "release";

    /// <summary>The <see cref="IdempotencyTagKeys.Purpose"/> value of <see cref="IdempotencyPurpose.Request"/> (<c>"request"</c>).</summary>
    public const string PurposeRequest = "request";

    /// <summary>The <see cref="IdempotencyTagKeys.Purpose"/> value of <see cref="IdempotencyPurpose.Message"/> (<c>"message"</c>).</summary>
    public const string PurposeMessage = "message";

    /// <summary>Result of a begin that reserved the key (<c>"started"</c>).</summary>
    public const string ResultStarted = "started";

    /// <summary>Result of a begin that found the key in flight (<c>"in_progress"</c>).</summary>
    public const string ResultInProgress = "in_progress";

    /// <summary>Result of a begin that found the key completed (<c>"completed"</c>).</summary>
    public const string ResultCompleted = "completed";

    /// <summary>Result of a begin that found the key reserved for another fingerprint (<c>"fingerprint_mismatch"</c>).</summary>
    public const string ResultFingerprintMismatch = "fingerprint_mismatch";

    /// <summary>Result of a complete or release whose token still owned the reservation (<c>"applied"</c>).</summary>
    public const string ResultApplied = "applied";

    /// <summary>Result of a complete or release whose token no longer owned the reservation (<c>"lost"</c>).</summary>
    public const string ResultLost = "lost";

    /// <summary>Result of an operation let through by fail-open after a store outage (<c>"fail_open"</c>).</summary>
    public const string ResultFailOpen = "fail_open";
}
