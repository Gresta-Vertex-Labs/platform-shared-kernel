namespace SharedKernel.Primitives.Propagation;

/// <summary>
/// The closed vocabulary of the <see cref="WellKnownTagKeys.Outcome"/> tag (<c>"outcome"</c>), carried by spans
/// and by metric dimensions across domains.
/// </summary>
/// <remarks>
/// <para>
/// <b>Wire names.</b> These four values are what dashboards and alert rules group by. Never change one in place and
/// never add a spelling variant: a new outcome is a new constant and a cross-domain decision.
/// </para>
/// <para>
/// <b>One call yields both.</b> <c>Activity.RecordResult</c>, <c>RecordFailure</c> and <c>RecordException</c>
/// (<c>SharedKernel.Primitives.Telemetry</c>) return one of these values, so a span and its metric tag are produced
/// by the same call.
/// </para>
/// </remarks>
public static class WellKnownOutcomes
{
    /// <summary>The operation succeeded (<c>"success"</c>).</summary>
    public const string Success = "success";

    /// <summary>The operation completed with an expected failure, a failed <c>Result</c> (<c>"failure"</c>).</summary>
    public const string Failure = "failure";

    /// <summary>The operation ended with an exception other than cancellation (<c>"exception"</c>).</summary>
    public const string Exception = "exception";

    /// <summary>The operation was cancelled by its caller, which is not an error (<c>"canceled"</c>).</summary>
    public const string Canceled = "canceled";
}
