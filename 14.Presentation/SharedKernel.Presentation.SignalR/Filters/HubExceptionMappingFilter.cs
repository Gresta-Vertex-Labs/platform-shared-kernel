using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>
/// The outermost platform hub filter: every error of a hub method invocation leaves it as a <see cref="HubException"/>
/// with the message <c>{code}: {message}</c>, the one exception type SignalR reliably delivers to clients.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>A <see cref="HubException"/> (thrown by the hub, another filter or this one) passes unchanged.</item>
///   <item>A failed <see cref="Result"/> or <see cref="Result{T}"/> the hub method returned, and a thrown
///   <see cref="SharedKernelException"/>, become <c>{error.Code}: {client message}</c> via
///   <see cref="ErrorPresentation.GetClientMessage"/> — localized, and redacted for server errors outside Development,
///   exactly as HTTP presents the same error. A <see cref="ValidationException"/> with several errors is presented as
///   <see cref="Error.Validation(IReadOnlyList{Error})"/>, like the HTTP exception handler does.</item>
///   <item>A successful <see cref="Result{T}"/> returns its value, a successful <see cref="Result"/> nothing.</item>
///   <item>Any other exception becomes <c>unexpected.exception: An unexpected error occurred.</c> (in Development, the
///   exception's message) and is logged at Error.</item>
///   <item>Cancellation because the connection closed is logged at Debug and rethrown: there is no client to answer.</item>
/// </list>
/// Server errors (<see cref="ErrorPresentation.IsServerError"/>) are logged at Error, client errors at Debug.
/// </remarks>
internal sealed partial class HubExceptionMappingFilter : IHubFilter
{
    private const string UnexpectedMessage = "An unexpected error occurred.";

    private readonly ILogger<HubExceptionMappingFilter> _logger;

    /// <summary>Initializes a new instance of the <see cref="HubExceptionMappingFilter"/> class.</summary>
    /// <param name="logger">The logger; a missing logging registration never breaks error mapping.</param>
    public HubExceptionMappingFilter(ILogger<HubExceptionMappingFilter>? logger = null)
    {
        _logger = logger ?? NullLogger<HubExceptionMappingFilter>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            var returned = await next(invocationContext).ConfigureAwait(false);

            if (!HubMethodResult.TryRead(returned, out var value, out var error))
            {
                return returned;
            }

            if (error is null)
            {
                return value;
            }

            throw CreateException(invocationContext, error, exception: null);
        }
        catch (HubException)
        {
            // Already written for the client: by the hub method, by another filter, or by this filter just above.
            throw;
        }
        catch (OperationCanceledException exception) when (invocationContext.Context.ConnectionAborted.IsCancellationRequested)
        {
            Log.ConnectionAborted(_logger, GetHubName(invocationContext), invocationContext.HubMethodName, exception);
            throw;
        }
        catch (ValidationException exception)
        {
            var error = exception.Errors.Count == 1 ? exception.Errors[0] : Error.Validation(exception.Errors);
            throw CreateException(invocationContext, error, exception);
        }
        catch (SharedKernelException exception)
        {
            throw CreateException(invocationContext, exception.Error, exception);
        }
        catch (Exception exception)
        {
            Log.UnhandledHubException(_logger, GetHubName(invocationContext), invocationContext.HubMethodName, exception);
            throw new HubException(GetUnexpectedMessage(invocationContext.Context, exception), exception);
        }
    }

    private HubException CreateException(HubInvocationContext invocationContext, Error error, Exception? exception)
    {
        var hubName = GetHubName(invocationContext);

        if (ErrorPresentation.IsServerError(error.Type))
        {
            Log.ServerError(_logger, hubName, invocationContext.HubMethodName, error.Code, exception);
        }
        else
        {
            Log.ClientError(_logger, hubName, invocationContext.HubMethodName, error.Code, exception);
        }

        var message = HubErrorMessage.For(error, invocationContext.Context);

        // The inner exception stays on the server: SignalR sends a HubException's own message and nothing else.
        return exception is null ? new HubException(message) : new HubException(message, exception);
    }

    // Development shows what went wrong, as the HTTP exception handler's "exception" member does; anywhere else the
    // generic sentence, translated like every other client message. Without an HTTP context the environment is
    // unknown and treated as production, the same rule ErrorPresentation applies.
    private static string GetUnexpectedMessage(HubCallerContext context, Exception exception)
    {
        var httpContext = context.GetHttpContext();

        var message = IsDevelopment(httpContext)
            ? exception.Message
            : ErrorPresentation.GetClientMessage(Error.Unexpected(ErrorCodes.Unexpected.Default, UnexpectedMessage), httpContext);

        return HubErrorMessage.Format(ErrorCodes.Unexpected.Default, message);
    }

    private static bool IsDevelopment(HttpContext? httpContext) =>
        httpContext?.RequestServices?.GetService<IHostEnvironment>()?.IsDevelopment() == true;

    private static string GetHubName(HubInvocationContext invocationContext) => invocationContext.Hub.GetType().Name;

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 100,
            Level = LogLevel.Error,
            Message = "Unhandled exception in hub method {HubName}.{HubMethodName}.")]
        public static partial void UnhandledHubException(ILogger logger, string hubName, string hubMethodName, Exception exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 103,
            Level = LogLevel.Error,
            Message = "Hub method {HubName}.{HubMethodName} failed with {ErrorCode}.")]
        public static partial void ServerError(ILogger logger, string hubName, string hubMethodName, string errorCode, Exception? exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 104,
            Level = LogLevel.Debug,
            Message = "Hub method {HubName}.{HubMethodName} was rejected with {ErrorCode}.")]
        public static partial void ClientError(ILogger logger, string hubName, string hubMethodName, string errorCode, Exception? exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 106,
            Level = LogLevel.Debug,
            Message = "The connection closed before hub method {HubName}.{HubMethodName} completed.")]
        public static partial void ConnectionAborted(ILogger logger, string hubName, string hubMethodName, Exception exception);
    }
}
