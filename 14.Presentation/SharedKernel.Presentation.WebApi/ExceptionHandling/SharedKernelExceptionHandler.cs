using Microsoft.AspNetCore.Builder;
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
/// The platform's exception handling, installed by <c>AddSharedKernelWebApi</c> as the fallback
/// <see cref="ExceptionHandlerOptions.ExceptionHandler"/>: every exception that reaches <c>UseExceptionHandler()</c>
/// and that no <see cref="IExceptionHandler"/> of the service handled becomes the platform's problem response.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Any exception while the client has gone away (<see cref="HttpContext.RequestAborted"/> is cancelled): 499,
///   no body, Debug log. The framework answers an aborted <see cref="OperationCanceledException"/> or
///   <see cref="IOException"/> the same way before any handler runs; this covers every other type.</item>
///   <item><see cref="TimeoutException"/>, or an <see cref="OperationCanceledException"/> the client did not cause (a
///   timeout inside the service): 504 <c>timeout.default</c>.</item>
///   <item><see cref="BadHttpRequestException"/>: its own status. A 400 — a minimal API that could not bind a
///   parameter or read the JSON body — is a validation problem (<c>validation.invalid_format</c>, the JSON path in
///   <c>errors</c> when known) whose text never names a .NET type; other statuses keep the framework's client-safe
///   message, and 413 is coded <c>request.too_large</c>.</item>
///   <item><see cref="ValidationException"/>: 400, with exactly the body a returned <c>Error.Validation</c> of the same
///   errors produces.</item>
///   <item>Any other <see cref="SharedKernelException"/>: its <see cref="Error"/>, presented like a returned one.</item>
///   <item>Anything else: 500 <c>unexpected.exception</c> with a generic message; the exception itself only in
///   Development or when <c>Problems:IncludeExceptionDetails</c> is set.</item>
/// </list>
/// <para>
/// Server errors (5xx) are logged at Error with the exception, client errors at Debug. The exception middleware's
/// own log is suppressed for every exception handled here, so each is logged once. A gRPC call gets the status but
/// no body.
/// </para>
/// </remarks>
internal sealed partial class SharedKernelExceptionHandler
{
    private const string UnexpectedMessage = "An unexpected error occurred.";

    private const string TimeoutMessage = "The operation did not complete in time.";

    private const string ExceptionTypeKey = "type";

    private const string ExceptionMessageKey = "message";

    private const string ExceptionStackTraceKey = "stackTrace";

    /// <summary>The <see cref="HttpContext.Items"/> key marking an exception this handler handled.</summary>
    private static readonly object HandledKey = new();

    private readonly ILogger<SharedKernelExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IOptions<SharedKernelWebApiOptions> _options;

    public SharedKernelExceptionHandler(
        ILogger<SharedKernelExceptionHandler> logger,
        IHostEnvironment environment,
        IOptions<SharedKernelWebApiOptions> options)
    {
        _logger = logger;
        _environment = environment;
        _options = options;
    }

    /// <summary>
    /// Installs this handler as the fallback of <paramref name="options"/>, unless the service configured its own
    /// <see cref="ExceptionHandlerOptions.ExceptionHandler"/> or <see cref="ExceptionHandlerOptions.ExceptionHandlingPath"/>,
    /// and suppresses the middleware's own diagnostics for the exceptions it handles.
    /// </summary>
    public void Install(ExceptionHandlerOptions options)
    {
        if (options.ExceptionHandler is null && !options.ExceptionHandlingPath.HasValue)
        {
            options.ExceptionHandler = HandleAsync;

            // A NotFoundException is a legitimate 404, and a gRPC call gets its status without a body, which the
            // middleware would otherwise read as a misconfigured handler.
            options.AllowStatusCode404Response = true;
        }

        var serviceCallback = options.SuppressDiagnosticsCallback;
        options.SuppressDiagnosticsCallback = context =>
            IsHandledHere(context.HttpContext)
            || (serviceCallback?.Invoke(context) ?? context.ExceptionHandledBy == ExceptionHandledType.ExceptionHandlerService);
    }

    /// <summary>The <see cref="RequestDelegate"/> the exception middleware runs: handles the exception it recorded.</summary>
    public Task HandleAsync(HttpContext httpContext)
    {
        var exception = httpContext.Features.Get<IExceptionHandlerFeature>()?.Error;
        return exception is null ? Task.CompletedTask : HandleAsync(httpContext, exception);
    }

    /// <summary>Writes the response for <paramref name="exception"/> and logs it.</summary>
    public async Task HandleAsync(HttpContext httpContext, Exception exception)
    {
        httpContext.Items[HandledKey] = true;

        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            Log.RequestAborted(_logger, RequestFacts.GetEndpointDisplayName(httpContext), exception);

            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            return;
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
            return;
        }

        await ProblemResponseWriter.WriteAsync(httpContext, problem).ConfigureAwait(false);
    }

    private static bool IsHandledHere(HttpContext httpContext) => httpContext.Items.ContainsKey(HandledKey);

    private ProblemDetails BuildProblem(HttpContext httpContext, Exception exception)
    {
        switch (exception)
        {
            case BadHttpRequestException { StatusCode: StatusCodes.Status400BadRequest } badRequest:
                // A minimal API that could not bind a parameter. The framework's message names the parameter's .NET
                // type, so the client gets the platform's validation shape instead.
                return ProblemFactory.ForError(RequestValidationErrors.FromBadRequest(badRequest), httpContext, StatusCodes.Status400BadRequest);

            case BadHttpRequestException badRequest:
                // The framework wrote this message for the client: it names the limit or the media type, never internals.
                return ProblemFactory.Create(
                    httpContext,
                    badRequest.StatusCode,
                    ProblemFactory.CodeForStatus(badRequest.StatusCode),
                    badRequest.Message);

            case ValidationException validation:
                // Exactly what a returned error produces: one error is itself, several are Error.Validation(errors).
                var error = validation.Errors.Count == 1 ? validation.Errors[0] : Error.Validation(validation.Errors);
                return ProblemFactory.ForError(error, httpContext, StatusCodes.Status400BadRequest);

            case SharedKernelException sharedKernelException:
                return ProblemFactory.ForError(sharedKernelException.Error, httpContext);

            case TimeoutException:
            case OperationCanceledException:
                // Not the client: the request is still open, so something inside the service ran out of time.
                return ProblemFactory.ForError(Error.Timeout(ErrorCodes.Timeout.Default, TimeoutMessage), httpContext);

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
