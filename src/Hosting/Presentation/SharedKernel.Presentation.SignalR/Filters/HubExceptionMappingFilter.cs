using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using SharedKernel.Presentation.SignalR.Diagnostics;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>
/// The outermost platform hub filter: every error of a hub method invocation leaves it as a <see cref="HubException"/>
/// with the message <c>{code}: {message} [traceId=…; correlationId=…]</c> (<see cref="HubErrorMessage"/>, the ids a
/// client also reads from an HTTP problem), the one exception type whose message SignalR delivers to clients. SignalR puts its own sentence in front of it; <see cref="HubErrorMessage.TryParse(string?, out string?, out string?)"/>
/// reads the code back.
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
///   <item>A successful <see cref="Result{T}"/> whose value is a stream (<see cref="IAsyncEnumerable{T}"/>,
///   <see cref="System.Threading.Channels.ChannelReader{T}"/>) is refused as <c>unexpected.default</c> (in
///   Development, with the fix) and logged at Error. SignalR streams only a hub method declared to return a stream, so
///   a <c>Result&lt;IAsyncEnumerable&lt;T&gt;&gt;</c> method is an ordinary invocation, and its stream as the value of
///   that invocation would close the connection (the JSON protocol cannot write it) or reach the client as a
///   meaningless object (a channel reader). The stream is not read.</item>
///   <item>A <see cref="TimeoutException"/>, or an <see cref="OperationCanceledException"/> while the connection is
///   open (a timeout inside the service), becomes <c>timeout.default: The operation did not complete in time.</c> and is
///   logged at Error — the error an HTTP request gets for it (504).</item>
///   <item>Any other exception becomes <c>unexpected.default: An unexpected error occurred.</c> (in Development, the
///   exception's message) and is logged at Error.</item>
///   <item>Cancellation because the connection closed is logged at Debug and rethrown: there is no client to answer.</item>
/// </list>
/// <para>
/// Server errors (<see cref="ErrorPresentation.IsServerError"/>) are logged at Error, client errors at Debug.
/// </para>
/// <para>
/// A filter wraps the invocation of a hub method, not the reading of the stream a streaming hub method returns:
/// SignalR reads the stream after this filter has returned. A streaming hub method that fails before it returns its
/// stream (<c>return result.GetValueOrThrow();</c>) gets the coded <see cref="HubException"/>; an exception thrown while
/// the stream is read reaches the client as SignalR's uncoded
/// <c>An error occurred on the server while streaming results.</c> and is logged by SignalR, not here.
/// </para>
/// </remarks>
internal sealed class HubExceptionMappingFilter : IHubFilter
{
    private readonly ILogger<HubExceptionMappingFilter> _logger;

    /// <summary>Initializes a new instance of the <see cref="HubExceptionMappingFilter"/> class.</summary>
    /// <param name="logger">The logger.</param>
    public HubExceptionMappingFilter(ILogger<HubExceptionMappingFilter> logger)
    {
        _logger = logger;
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

            if (error is not null)
            {
                EdgeFailureRecording.Record(error);
                throw CreateException(invocationContext, error, exception: null);
            }

            if (HubMethodResult.IsStream(value))
            {
                throw CreateStreamInResultException(invocationContext);
            }

            return value;
        }
        catch (HubException)
        {
            // Already written for the client: by the hub method, by another filter, or by this filter just above.
            throw;
        }
        catch (OperationCanceledException exception) when (invocationContext.Context.ConnectionAborted.IsCancellationRequested)
        {
            SignalRLog.ConnectionAborted(_logger, GetHubName(invocationContext), invocationContext.HubMethodName, exception);
            throw;
        }
        catch (ValidationException exception)
        {
            var error = exception.Errors.Count == 1 ? exception.Errors[0] : Error.Validation(exception.Errors);
            // A validation exception is a client error whatever the type of its single error, as over HTTP.
            EdgeFailureRecording.RecordClientCode(error.Code);
            throw CreateException(invocationContext, error, exception);
        }
        catch (SharedKernelException exception)
        {
            EdgeFailureRecording.Record(exception.Error, exception);
            throw CreateException(invocationContext, exception.Error, exception);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            // Not the client: the connection is still open, so something inside the service ran out of time — the
            // error an HTTP request gets for the same exception (504 timeout.default).
            // A real timeout of the service: recorded as a failure, not as an exception event.
            EdgeFailureRecording.Record(EdgeErrors.Timeout);
            throw CreateException(invocationContext, EdgeErrors.Timeout, exception);
        }
        catch (Exception exception)
        {
            EdgeFailureRecording.Record(EdgeErrors.Unexpected, exception);
            SignalRLog.ServerError(_logger, GetHubName(invocationContext), invocationContext.HubMethodName, EdgeErrors.Unexpected.Code, exception);
            throw new HubException(GetUnexpectedMessage(invocationContext.Context, exception.Message), exception);
        }
    }

    private HubException CreateException(HubInvocationContext invocationContext, Error error, Exception? exception)
    {
        var hubName = GetHubName(invocationContext);

        if (ErrorPresentation.IsServerError(error.Type))
        {
            if (exception is null)
            {
                // A returned failure is an expected outcome of the method, not an unexpected exception.
                SignalRLog.ServerFailure(_logger, hubName, invocationContext.HubMethodName, error.Code);
            }
            else
            {
                SignalRLog.ServerError(_logger, hubName, invocationContext.HubMethodName, error.Code, exception);
            }
        }
        else
        {
            SignalRLog.ClientError(_logger, hubName, invocationContext.HubMethodName, error.Code, exception);
        }

        var message = HubErrorMessage.For(error, invocationContext.Context);

        // The inner exception stays on the server: SignalR sends a HubException's own message and nothing else.
        return exception is null ? new HubException(message) : new HubException(message, exception);
    }

    // A stream as the value of an ordinary invocation. SignalR decides whether a hub method streams from its declared
    // return type, and Result<IAsyncEnumerable<T>> is not a stream type, so the stream would be written as the
    // invocation's single result: the JSON protocol fails on it and closes the connection. Refused instead, as the
    // server-side mistake it is: the fix in Development and in the log, the generic sentence anywhere else.
    private HubException CreateStreamInResultException(HubInvocationContext invocationContext)
    {
        EdgeFailureRecording.Record(EdgeErrors.Unexpected);
        SignalRLog.StreamInResult(
            _logger,
            GetHubName(invocationContext),
            invocationContext.HubMethodName,
            new InvalidOperationException("A hub method returned a stream inside a Result."));

        var fix = $"Hub method '{invocationContext.HubMethodName}' returned a stream inside a Result, which SignalR "
            + "cannot stream: it streams only a hub method declared to return IAsyncEnumerable<T> or ChannelReader<T>. "
            + "Declare the method that way and throw a failure before returning the stream: "
            + "'return result.GetValueOrThrow();'.";

        return new HubException(GetUnexpectedMessage(invocationContext.Context, fix));
    }

    // Development shows what went wrong, as the HTTP exception handler's "exception" member does; anywhere else the
    // generic sentence, translated like every other client message. Without an HTTP context the environment is
    // unknown and treated as production, the same rule ErrorPresentation applies.
    private static string GetUnexpectedMessage(HubCallerContext context, string developmentMessage)
    {
        var httpContext = context.GetHttpContext();

        var message = IsDevelopment(httpContext)
            ? developmentMessage
            : ErrorPresentation.GetClientMessage(EdgeErrors.Unexpected, httpContext);

        return HubErrorMessage.Format(EdgeErrors.Unexpected.Code, message, context);
    }

    private static bool IsDevelopment(HttpContext? httpContext) =>
        httpContext?.RequestServices?.GetService<IHostEnvironment>()?.IsDevelopment() == true;

    private static string GetHubName(HubInvocationContext invocationContext) => invocationContext.Hub.GetType().Name;
}
