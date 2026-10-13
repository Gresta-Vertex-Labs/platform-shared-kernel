using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi.ExceptionHandling;

/// <summary>How an exception that reached an edge is classified.</summary>
internal enum ExceptionKind
{
    /// <summary>A <see cref="ValidationException"/>: answered like a returned validation error (400).</summary>
    Validation,

    /// <summary>A <see cref="SharedKernelException"/>: its own <see cref="Error"/>, presented like a returned one.</summary>
    Classified,

    /// <summary>A <see cref="TimeoutException"/> or an <see cref="OperationCanceledException"/> the client did not cause.</summary>
    Timeout,

    /// <summary>Anything else: a defect, answered with the generic unexpected error.</summary>
    Unclassified,
}

/// <summary>An exception together with the <see cref="Error"/> an edge answers it with.</summary>
/// <param name="Kind">The classification.</param>
/// <param name="Error">The error to present.</param>
/// <param name="StatusCode">
/// The HTTP status to force, or <see langword="null"/> to derive it from <paramref name="Error"/>. Set for
/// <see cref="ExceptionKind.Validation"/> only: a validation exception is a 400 whatever the type of its single error.
/// </param>
internal sealed record ClassifiedException(ExceptionKind Kind, Error Error, int? StatusCode);

/// <summary>
/// The one classification of a non-client exception, shared by the HTTP exception handler and GraphQL's error filter
/// so the two never disagree about what an exception means.
/// </summary>
/// <remarks>
/// Cancellation by the client and the framework's own <c>BadHttpRequestException</c> are HTTP matters the caller
/// decides first; everything else lands here. The generic sentences are the ones
/// <see cref="Presentation.ErrorPresentation.GetClientMessage"/> shows for the same error types.
/// </remarks>
internal static class ExceptionClassification
{
    private const int ValidationStatusCode = 400;

    private const string ExceptionTypeKey = "type";

    private const string ExceptionMessageKey = "message";

    private const string ExceptionStackTraceKey = "stackTrace";

    /// <summary>Classifies <paramref name="exception"/>.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The classification and the error to present.</returns>
    public static ClassifiedException Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (exception)
        {
            case ValidationException validation:
                // Exactly what a returned error produces: one error is itself, several are Error.Validation(errors).
                var error = validation.Errors.Count == 1 ? validation.Errors[0] : Error.Validation(validation.Errors);
                return new ClassifiedException(ExceptionKind.Validation, error, ValidationStatusCode);

            case SharedKernelException sharedKernelException:
                return new ClassifiedException(ExceptionKind.Classified, sharedKernelException.Error, StatusCode: null);

            case TimeoutException:
            case OperationCanceledException:
                // Not the client: the request is still open, so something inside the service ran out of time.
                return new ClassifiedException(
                    ExceptionKind.Timeout,
                    EdgeErrors.Timeout,
                    StatusCode: null);

            default:
                return new ClassifiedException(
                    ExceptionKind.Unclassified,
                    EdgeErrors.Unexpected,
                    StatusCode: null);
        }
    }

    /// <summary>
    /// The <c>exception</c> member of a Development response: the exception's type, message and stack trace. Never
    /// written outside Development.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The members.</returns>
    public static Dictionary<string, string?> Describe(Exception exception) =>
        new(StringComparer.Ordinal)
        {
            [ExceptionTypeKey] = exception.GetType().FullName,
            [ExceptionMessageKey] = exception.Message,
            [ExceptionStackTraceKey] = exception.StackTrace,
        };
}
