using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.WebApi.ExceptionHandling;

/// <summary>
/// Terminal <see cref="IExceptionHandler"/> that converts unhandled exceptions into RFC 9457
/// <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/> responses.
/// </summary>
/// <remarks>
/// <para>
/// Register via <c>services.AddExceptionHandler&lt;SharedKernelExceptionHandler&gt;()</c> together
/// with <c>services.AddProblemDetails()</c>. <see cref="ValidationException"/> — which carries
/// every failing field's <see cref="Error"/>, not just one — is checked FIRST and mapped via
/// <see cref="ValidationProblemDetailsExtensions.ToProblemDetails"/> so no field error is silently
/// dropped. Every other known <see cref="SharedKernelException"/> subtype (<c>01.Core</c>) that
/// carries an <see cref="Error"/> is mapped via
/// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails(Error, HttpContext?)"/>; unknown
/// exceptions fall back to a generic 500 <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/> with
/// <c>Detail</c> suppressed outside <c>IHostEnvironment.IsDevelopment()</c>.
/// </para>
/// <para>
/// Always logs the full exception at <see cref="LogLevel.Error"/> before writing the response, and
/// always returns <see langword="true"/> — this is the terminal handler in the exception-handling
/// chain.
/// </para>
/// </remarks>
public sealed partial class SharedKernelExceptionHandler : IExceptionHandler
{
    private const string UnexpectedErrorCode = "error.unexpected";
    private const string UnexpectedErrorMessage = "An unexpected error occurred.";

    private readonly ILogger<SharedKernelExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    /// <summary>
    /// Initialises a new <see cref="SharedKernelExceptionHandler"/>.
    /// </summary>
    /// <param name="logger">The logger used to record the full exception before responding.</param>
    /// <param name="environment">
    /// The hosting environment, used to gate exception detail exposure to
    /// <c>IHostEnvironment.IsDevelopment()</c> only.
    /// </param>
    public SharedKernelExceptionHandler(ILogger<SharedKernelExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Attempts to handle the specified <paramref name="exception"/> by writing an RFC 9457
    /// <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/> response to <paramref name="httpContext"/>.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that always resolves to <see langword="true"/>.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        Log.UnhandledException(_logger, exception);

        var problemDetails = BuildProblemDetails(exception, httpContext);

        if (!_environment.IsDevelopment() && exception is not SharedKernelException)
        {
            problemDetails.Detail = UnexpectedErrorMessage;
        }

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static Microsoft.AspNetCore.Mvc.ProblemDetails BuildProblemDetails(Exception exception, HttpContext httpContext) => exception switch
    {
        // Checked BEFORE the generic SharedKernelException branch: ValidationException carries
        // every failing field's Error, not just Errors[0] (which base.Error is set to).
        ValidationException validationException => validationException.ToProblemDetails(httpContext),
        SharedKernelException sharedKernelException => sharedKernelException.Error.ToProblemDetails(httpContext),
        _ => Error.Unexpected(UnexpectedErrorCode, UnexpectedErrorMessage).ToProblemDetails(httpContext),
    };

    /// <summary>
    /// Source-generated log messages for <see cref="SharedKernelExceptionHandler"/>.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 1,
            Level = LogLevel.Error,
            Message = "Unhandled exception caught by SharedKernelExceptionHandler.")]
        public static partial void UnhandledException(ILogger logger, Exception exception);
    }
}
