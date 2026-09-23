using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.WebApi.ExceptionHandling;

/// <summary>
/// The exception handler registered by <c>AddSharedKernelWebApi</c>: turns every exception that reaches
/// <c>UseExceptionHandler()</c> into the platform's problem response, and is the only place such exceptions are
/// logged (.NET 10's exception middleware does not log exceptions a handler handles).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>A client that went away (<see cref="OperationCanceledException"/> while the request is aborted): 499, no body, Debug log.</item>
///   <item><see cref="BadHttpRequestException"/> (a body over the size limit, a malformed request): its own status and message; 413 is coded <c>request.too_large</c>.</item>
///   <item><see cref="ValidationException"/>: 400 with every field error.</item>
///   <item>Any other <see cref="SharedKernelException"/>: its <see cref="Error"/>, presented like a returned one.</item>
///   <item>Anything else: 500 <c>unexpected.exception</c> with a generic message; the exception itself only in Development or when <c>Problems:IncludeExceptionDetails</c> is set.</item>
/// </list>
/// Server errors (5xx) are logged at Error with the exception, client errors at Debug. A gRPC call gets the status
/// but no body.
/// </remarks>
internal sealed partial class SharedKernelExceptionHandler : IExceptionHandler
{
    private const string UnexpectedMessage = "An unexpected error occurred.";

    private const string ExceptionTypeKey = "type";

    private const string ExceptionMessageKey = "message";

    private const string ExceptionStackTraceKey = "stackTrace";

    private readonly ILogger<SharedKernelExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IOptions<WebApiOptions> _options;

    public SharedKernelExceptionHandler(
        ILogger<SharedKernelExceptionHandler> logger,
        IHostEnvironment environment,
        IOptions<WebApiOptions> options)
    {
        _logger = logger;
        _environment = environment;
        _options = options;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            Log.RequestAborted(_logger, RequestFacts.GetEndpointDisplayName(httpContext), exception);

            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            return true;
        }

        var problem = BuildProblem(httpContext, exception);
        var statusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        var errorCode = problem.Extensions[ProblemDetailsExtensionNames.ErrorCode] as string ?? string.Empty;

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            Log.ServerError(_logger, statusCode, errorCode, RequestFacts.GetEndpointDisplayName(httpContext), exception);
        }
        else
        {
            Log.ClientError(_logger, statusCode, errorCode, RequestFacts.GetEndpointDisplayName(httpContext), exception);
        }

        if (RequestFacts.IsGrpcRequest(httpContext))
        {
            httpContext.Response.StatusCode = statusCode;
            return true;
        }

        await ProblemResponseWriter.WriteAsync(httpContext, problem).ConfigureAwait(false);
        return true;
    }

    private ProblemDetails BuildProblem(HttpContext httpContext, Exception exception)
    {
        switch (exception)
        {
            case BadHttpRequestException badRequest:
                // The framework wrote this message for the client: it names the limit or the malformed part, never internals.
                return ProblemFactory.Create(
                    httpContext,
                    badRequest.StatusCode,
                    ProblemFactory.CodeForStatus(badRequest.StatusCode),
                    badRequest.Message);

            case ValidationException validation:
                // One field error keeps its own code and message; several are reported like a Result carrying
                // Error.Validation(errors). Either way every field error is listed.
                var error = validation.Errors.Count == 1 ? validation.Errors[0] : Error.Validation(validation.Errors);
                var fieldErrors = error.Details.Count > 0 ? error.Details : validation.Errors;
                return ProblemFactory.ForError(error, httpContext, StatusCodes.Status400BadRequest, fieldErrors);

            case SharedKernelException sharedKernelException:
                return ProblemFactory.ForError(sharedKernelException.Error, httpContext);

            default:
                var problem = ProblemFactory.ForError(
                    Error.Unexpected(ErrorCodes.Unexpected.Default, UnexpectedMessage),
                    httpContext);

                if (_options.Value.Problems.IncludeExceptionDetails ?? _environment.IsDevelopment())
                {
                    problem.Extensions[ProblemDetailsExtensionNames.Exception] = new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [ExceptionTypeKey] = exception.GetType().FullName,
                        [ExceptionMessageKey] = exception.Message,
                        [ExceptionStackTraceKey] = exception.StackTrace,
                    };
                }

                return problem;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 1,
            Level = LogLevel.Error,
            Message = "Request to {EndpointDisplayName} failed with {StatusCode} {ErrorCode}.")]
        public static partial void ServerError(ILogger logger, int statusCode, string errorCode, string endpointDisplayName, Exception exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 7,
            Level = LogLevel.Debug,
            Message = "Request to {EndpointDisplayName} was rejected with {StatusCode} {ErrorCode}.")]
        public static partial void ClientError(ILogger logger, int statusCode, string errorCode, string endpointDisplayName, Exception exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 8,
            Level = LogLevel.Debug,
            Message = "The client closed the request to {EndpointDisplayName} before it completed.")]
        public static partial void RequestAborted(ILogger logger, string endpointDisplayName, Exception exception);
    }
}
