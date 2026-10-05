using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Exceptions;

namespace SharedKernel.Workflows.Temporal.Failures;

/// <summary>
/// The single mandatory bridge between <see cref="Result"/>/<see cref="Result{T}"/> and Temporal's
/// own exception-based failure model.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ToFailure(Error)"/>/<see cref="ToFailure(Result)"/> map an expected <see cref="Error"/>
/// raised inside an activity or workflow body onto an <see cref="ApplicationFailureException"/> so
/// Temporal's retry, timeout, and compensation machinery is driven correctly, always with
/// <c>errorType</c> set to <see cref="Error.Code"/>. <see cref="ErrorType.Unexpected"/>,
/// <see cref="ErrorType.Unavailable"/> and <see cref="ErrorType.Timeout"/> map to
/// <c>nonRetryable: false</c> — a transient fault, a dependency that is down, and one that ran out
/// of time are exactly what the retry policy exists for. Every other type
/// (<see cref="ErrorType.Validation"/>/<see cref="ErrorType.NotFound"/>/<see cref="ErrorType.Conflict"/>/
/// <see cref="ErrorType.Unauthorized"/>/<see cref="ErrorType.Forbidden"/>/<see cref="ErrorType.BusinessRule"/>)
/// maps to <c>nonRetryable: true</c>, because repeating the same call cannot change the answer. An
/// unmapped exception escaping an activity stays retryable, matching Temporal's own semantics — this
/// package never blanket-catches.
/// </para>
/// <para>
/// <see cref="ToError(Exception)"/> is the inverse, used at the dispatch site (e.g.
/// <c>IWorkflowHandle{TResult}.GetResultAsync</c>) to turn a thrown failure back into a
/// <see cref="Result{T}"/>. The original <see cref="Error.Code"/> string always survives the round
/// trip via <see cref="ApplicationFailureException.ErrorType"/>, so a code set inside an activity is
/// branchable at the dispatch site. The original <see cref="ErrorType"/> discriminator additionally
/// survives when the failure originated from this same mapper, because it is stashed as a structured
/// failure detail — a defensive best-effort decode, not a guarantee for failures Temporal itself
/// produced from an unmapped exception. That detail is the enum's numeric value, persisted in
/// workflow history, which is why <see cref="ErrorType"/> members are never renumbered.
/// </para>
/// </remarks>
internal static class WorkflowFailureMapper
{
    /// <summary>
    /// Maps an expected <see cref="Error"/> to the <see cref="ApplicationFailureException"/> that
    /// should be thrown from inside an activity or workflow body.
    /// </summary>
    /// <remarks>
    /// Retryable (<c>nonRetryable: false</c>) only for <see cref="ErrorType.Unexpected"/>,
    /// <see cref="ErrorType.Unavailable"/> and <see cref="ErrorType.Timeout"/>; non-retryable for every
    /// other type.
    /// </remarks>
    public static ApplicationFailureException ToFailure(Error error)
    {
        bool nonRetryable = !IsRetryable(error.Type);
        return new ApplicationFailureException(
            message: error.Message,
            errorType: error.Code,
            nonRetryable: nonRetryable,
            details: [(int)error.Type]);
    }

    /// <summary>
    /// Maps a failed <see cref="Result"/> to the <see cref="ApplicationFailureException"/> that
    /// should be thrown from inside an activity or workflow body.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> represents a success.</exception>
    public static ApplicationFailureException ToFailure(Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException(
                "Cannot map a successful Result to a Temporal failure — check IsFailure before calling ToFailure.");
        }

        return ToFailure(result.Error);
    }

    /// <summary>
    /// Maps a Temporal exception (thrown from <c>GetResultAsync</c>, <c>SignalAsync</c>,
    /// <c>QueryAsync</c>, or a client-level RPC call) back onto an <see cref="Error"/>.
    /// </summary>
    public static Error ToError(Exception exception) => exception switch
    {
        WorkflowFailedException { InnerException: { } inner } => ToError(inner),
        ActivityFailureException { InnerException: { } inner } => ToError(inner),
        ChildWorkflowFailureException { InnerException: { } inner } => ToError(inner),
        ApplicationFailureException applicationFailure => FromApplicationFailure(applicationFailure),
        WorkflowAlreadyStartedException alreadyStarted => WorkflowErrors.AlreadyStarted(alreadyStarted.WorkflowId),
        CanceledFailureException => WorkflowErrors.Cancelled(ExtractWorkflowId(exception)),
        TerminatedFailureException => WorkflowErrors.Terminated(ExtractWorkflowId(exception)),
        TimeoutFailureException => WorkflowErrors.TimedOut(ExtractWorkflowId(exception)),
        WorkflowQueryRejectedException rejected => WorkflowErrors.QueryFailed(
            $"query rejected — workflow status is {rejected.WorkflowStatus}"),
        RpcException rpcException => WorkflowErrors.ServiceUnavailable(rpcException.Message),
        _ => Error.Unexpected("workflow.unmapped_failure", exception.Message),
    };

    private static Error FromApplicationFailure(ApplicationFailureException applicationFailure)
    {
        ErrorType originalType = ReadOriginalErrorType(applicationFailure);
        string code = applicationFailure.ErrorType ?? "workflow.application_failure";
        string message = applicationFailure.Message;

        return originalType switch
        {
            ErrorType.Validation => Error.Validation(code, message),
            ErrorType.NotFound => Error.NotFound(code, message),
            ErrorType.Conflict => Error.Conflict(code, message),
            ErrorType.Unauthorized => Error.Unauthorized(code, message),
            ErrorType.Forbidden => Error.Forbidden(code, message),
            ErrorType.BusinessRule => Error.BusinessRule(code, message),
            ErrorType.Unavailable => Error.Unavailable(code, message),
            ErrorType.Timeout => Error.Timeout(code, message),
            _ => Error.Unexpected(code, message),
        };
    }

    /// <summary>
    /// Whether Temporal should retry a failure of <paramref name="type"/>: only when the same call
    /// can succeed unchanged later — an unclassified fault, a dependency that is down, or one that ran
    /// out of time.
    /// </summary>
    private static bool IsRetryable(ErrorType type) =>
        type is ErrorType.Unexpected or ErrorType.Unavailable or ErrorType.Timeout;

    /// <summary>
    /// Best-effort decode of the original <see cref="ErrorType"/> stashed by
    /// <see cref="ToFailure(Error)"/> as the failure's first structured detail. Never throws — a
    /// failure Temporal itself produced from an unmapped exception carries no such detail, and falls
    /// back to a type inferred from <see cref="ApplicationFailureException.NonRetryable"/>.
    /// </summary>
    private static ErrorType ReadOriginalErrorType(ApplicationFailureException applicationFailure)
    {
        try
        {
            if (applicationFailure.Details.Count > 0)
            {
                return (ErrorType)applicationFailure.Details.ElementAt<int>(0);
            }
        }
        catch (Exception)
        {
            // The detail was not produced by ToFailure(Error) (e.g. a raw exception Temporal itself
            // converted) or could not be decoded as the expected int — fall through to the
            // NonRetryable-derived default below. This is a defensive decode of optional round-trip
            // metadata, not a swallow of a business Result.Failure.
        }

        return applicationFailure.NonRetryable ? ErrorType.Validation : ErrorType.Unexpected;
    }

    private static string ExtractWorkflowId(Exception exception) => exception.Message;
}
