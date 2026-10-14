using System.Diagnostics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Presentation;

/// <summary>
/// The one place that decides what a failure the edge handled leaves on the ambient server span, shared by the HTTP
/// exception handler, a returned error's HTTP result and the gRPC interceptor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Server category</b> (<see cref="ErrorPresentation.IsServerError"/>: 5xx, or gRPC <c>Unknown</c>/<c>Internal</c>/
/// <c>DataLoss</c>/<c>Unavailable</c>/<c>DeadlineExceeded</c>): the span fails. An exception goes through
/// <see cref="ActivityFailureExtensions.RecordException"/> (status <c>Error</c>, <c>error.type</c>, one exception
/// event) and a failure without an exception through <see cref="ActivityFailureExtensions.RecordFailure"/> (status
/// <c>Error</c>, <c>error.type</c>, <c>error.code</c>).
/// </para>
/// <para>
/// <b>Client category</b>: only the <c>error.code</c> tag, so a trace search by code works. The status stays
/// <c>Unset</c>: OpenTelemetry's HTTP and gRPC server conventions do not make a 4xx or a gRPC client code a span
/// error, and marking every validation failure as failed would flood error dashboards. To fail client-category spans
/// as well, change <see cref="FailsSpan"/> only.
/// </para>
/// <para>
/// This domain starts no span: it enriches the framework's server span (<see cref="Activity.Current"/>). A
/// <see langword="null"/>, stopped or non-sampled span is never touched. Nothing else in the domain writes
/// <c>SetStatus</c>, <c>AddException</c> or an <c>error.*</c> tag.
/// </para>
/// </remarks>
internal static class EdgeFailureRecording
{
    /// <summary>Returns <see langword="true"/> when a failure of <paramref name="type"/> fails the span (sets its status to <c>Error</c>).</summary>
    /// <param name="type">The error type.</param>
    /// <returns><see langword="true"/> for the server category.</returns>
    public static bool FailsSpan(ErrorType type) => ErrorPresentation.IsServerError(type);

    /// <summary>
    /// Records a handled failure on <see cref="Activity.Current"/>.
    /// </summary>
    /// <param name="error">The error the edge answers with.</param>
    /// <param name="exception">
    /// The exception that carried it, or <see langword="null"/> for a returned error or a failure with no exception
    /// worth an event (a timeout, answered 504, is recorded as a failure: an exception event for a cancellation is
    /// never added).
    /// </param>
    public static void Record(Error error, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        var activity = Activity.Current;

        if (!FailsSpan(error.Type))
        {
            TagErrorCode(activity, error.Code);
            return;
        }

        if (exception is null)
        {
            activity.RecordFailure(error);
            return;
        }

        // A cancellation is never a failure of the service (and records nothing, not even a code).
        if (activity.RecordException(exception) == WellKnownOutcomes.Canceled)
        {
            return;
        }

        // An exception that carries no error of its own (an unclassified one) is still answered with a code, which a
        // trace search by code finds.
        TagErrorCode(activity, error.Code);
    }

    /// <summary>
    /// Records a client-category outcome known only by its code (a foreign gRPC status such as <c>grpc.not_found</c>):
    /// the <c>error.code</c> tag and nothing else.
    /// </summary>
    /// <param name="errorCode">The code the edge answers with.</param>
    public static void RecordClientCode(string errorCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(errorCode);

        TagErrorCode(Activity.Current, errorCode);
    }

    private static void TagErrorCode(Activity? activity, string errorCode)
    {
        if (activity is { IsStopped: false, IsAllDataRequested: true })
        {
            activity.SetTag(WellKnownTagKeys.ErrorCode, errorCode);
        }
    }
}
