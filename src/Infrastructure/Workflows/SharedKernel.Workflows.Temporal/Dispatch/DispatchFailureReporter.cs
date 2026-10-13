using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Workflows.Temporal.Diagnostics;
using SharedKernel.Workflows.Temporal.Failures;
using Temporalio.Exceptions;

namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// Turns a Temporal failure, or the SDK failed-connect exception, caught at a dispatch site into the <see cref="Error"/> of the returned
/// result and logs it: event 17014 for a mapped failure, event 17015 (with the exception) for one the mapper has no
/// row for. Only exceptions accepted by <see cref="CanHandle"/> reach it; every other exception propagates from the dispatch site.
/// </summary>
internal static class DispatchFailureReporter
{
    /// <summary>Whether a dispatch site turns <paramref name="exception"/> into a failed result: a Temporal failure or the SDK's failed-connect exception.</summary>
    public static bool CanHandle(Exception exception) =>
        exception is TemporalException || WorkflowConnectFailure.Is(exception);

    /// <summary>Maps and logs <paramref name="exception"/>.</summary>
    /// <param name="logger">The dispatcher's logger.</param>
    /// <param name="operation">A <see cref="WorkflowOperations"/> constant.</param>
    /// <param name="exception">The caught Temporal exception.</param>
    /// <returns>The error to return in the failed result.</returns>
    public static Error Report(ILogger logger, string operation, Exception exception)
    {
        if (WorkflowFailureMapper.TryToError(exception, out Error error))
        {
            WorkflowLog.DispatchFailed(logger, operation, error.Code, error.Type);
        }
        else
        {
            WorkflowLog.DispatchFaulted(logger, operation, exception);
        }

        return error;
    }
}
