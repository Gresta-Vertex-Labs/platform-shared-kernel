using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Api.ErrorDetails.V1;
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
                "A successful Result cannot be mapped to a Temporal failure; check IsFailure before calling ToFailure.");
        }

        return ToFailure(result.Error);
    }

    /// <summary>
    /// Maps a Temporal exception (thrown from <c>GetResultAsync</c>, <c>SignalAsync</c>,
    /// <c>QueryAsync</c>, or a client-level RPC call) back onto an <see cref="Error"/>. A failure with no mapping
    /// row becomes <see cref="WorkflowErrorCodes.UnmappedFailure"/> with a fixed message and no exception text.
    /// </summary>
    public static Error ToError(Exception exception)
    {
        TryToError(exception, out Error error);
        return error;
    }

    /// <summary>
    /// Maps a Temporal exception onto an <see cref="Error"/> and reports whether a mapping row matched, so a caller
    /// can log an unmapped failure with its exception.
    /// </summary>
    /// <param name="exception">The Temporal exception.</param>
    /// <param name="error">The mapped error; <see cref="WorkflowErrorCodes.UnmappedFailure"/> when no row matched.</param>
    /// <returns><see langword="true"/> when a mapping row matched.</returns>
    public static bool TryToError(Exception exception, out Error error)
    {
        switch (exception)
        {
            case WorkflowFailedException or ActivityFailureException or ChildWorkflowFailureException
                when exception.InnerException is { } inner:
                return TryToError(inner, out error);
            case ApplicationFailureException applicationFailure:
                error = FromApplicationFailure(applicationFailure);
                return true;
            case WorkflowAlreadyStartedException:
                error = WorkflowErrors.AlreadyStarted();
                return true;
            case CanceledFailureException:
                error = WorkflowErrors.Cancelled();
                return true;
            case TerminatedFailureException:
                error = WorkflowErrors.Terminated();
                return true;
            case TimeoutFailureException:
                error = WorkflowErrors.TimedOut();
                return true;
            case WorkflowQueryRejectedException rejected:
                error = WorkflowErrors.QueryFailed($"the query was rejected for a workflow with status {rejected.WorkflowStatus}.");
                return true;
            case InvalidOperationException when WorkflowConnectFailure.Is(exception):
                error = WorkflowErrors.ServiceUnavailable();
                return true;
            case RpcException rpcException:
                error = FromRpcException(rpcException);
                return true;
            default:
                error = WorkflowErrors.UnmappedFailure();
                return false;
        }
    }

    /// <summary>
    /// Maps an <see cref="RpcException"/> by its gRPC status — never wholesale. A deployment or contract fault
    /// (missing namespace, credential, permission, internal error) stays <see cref="ErrorType.Unexpected"/>: a
    /// service-to-Temporal fault is not the end user's 401, 403 or 404. No message carries the RPC text, the host or
    /// a workflow id.
    /// </summary>
    private static Error FromRpcException(RpcException rpcException) => rpcException.Code switch
    {
        RpcException.StatusCode.NotFound => TryReadNamespaceNotFound(rpcException, out string? missingNamespace)
            ? WorkflowErrors.NamespaceNotFound(missingNamespace)
            : WorkflowErrors.NotFound(),
        RpcException.StatusCode.DeadlineExceeded => WorkflowErrors.RequestTimedOut(),
        RpcException.StatusCode.Unavailable
            or RpcException.StatusCode.ResourceExhausted
            or RpcException.StatusCode.Aborted => WorkflowErrors.ServiceUnavailable(),
        _ => WorkflowErrors.RpcFailed(),
    };

    /// <summary>
    /// Whether the gRPC status carries the server's <c>NamespaceNotFoundFailure</c> detail. Both a missing workflow and
    /// a missing namespace answer gRPC <c>NOT_FOUND</c>; only the detail tells them apart.
    /// </summary>
    private static bool TryReadNamespaceNotFound(RpcException rpcException, [NotNullWhen(true)] out string? missingNamespace)
    {
        missingNamespace = null;
        if (rpcException.GrpcStatus.Value is not { } status)
        {
            return false;
        }

        foreach (Any detail in status.Details)
        {
            if (detail.Is(NamespaceNotFoundFailure.Descriptor))
            {
                missingNamespace = detail.Unpack<NamespaceNotFoundFailure>().Namespace;
                return true;
            }
        }

        return false;
    }

    private static Error FromApplicationFailure(ApplicationFailureException applicationFailure)
    {
        ErrorType originalType = ReadOriginalErrorType(applicationFailure);
        string code = applicationFailure.ErrorType ?? WorkflowErrorCodes.ApplicationFailure;
        string message = applicationFailure.Message;
        // The failure message is the activity's own Error.Message, set deliberately by the author (ToFailure);
        // it is carried as-is. Exception text from anywhere else never reaches this arm.

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
    /// Reads the original <see cref="ErrorType"/> stashed by <see cref="ToFailure(Error)"/> as the failure's first
    /// structured detail. A failure Temporal itself produced from an unmapped exception carries no such detail, or one
    /// of another shape, and falls back to a type inferred from <see cref="ApplicationFailureException.NonRetryable"/>.
    /// </summary>
    /// <remarks>
    /// The detail read is a cast for a failure built in this process and a conversion for one decoded from history, so
    /// only the three exceptions those two paths raise for a detail of another shape are caught; any other exception
    /// propagates.
    /// </remarks>
    private static ErrorType ReadOriginalErrorType(ApplicationFailureException applicationFailure)
    {
        if (applicationFailure.Details.Count > 0)
        {
            try
            {
                return (ErrorType)applicationFailure.Details.ElementAt<int>(0);
            }
            catch (Exception exception) when (exception is InvalidCastException or JsonException or InvalidOperationException)
            {
                // The first detail is not the int ToFailure(Error) stashes: fall through to the NonRetryable default.
            }
        }

        return applicationFailure.NonRetryable ? ErrorType.Validation : ErrorType.Unexpected;
    }
}
