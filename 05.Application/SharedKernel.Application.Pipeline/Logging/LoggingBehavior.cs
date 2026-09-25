using SharedKernel.Application.Logging;
using System.Diagnostics;
using SharedKernel.Application.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Pipeline.Shared;

namespace SharedKernel.Application.Pipeline.Logging;

/// <summary>
/// Logs the start and completion (or fault) of every request that passes through the pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type being logged.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Logs <see cref="LogLevel.Debug"/> at start ("Handling {RequestType}"), timed via
/// <see cref="Stopwatch.GetTimestamp"/>/<see cref="Stopwatch.GetElapsedTime(long)"/> (no
/// <see cref="Stopwatch"/> allocation).
/// </para>
/// <para>
/// On completion:
/// <list type="bullet">
///   <item><description>Success within <see cref="ApplicationLoggingOptions.SlowRequestThreshold"/> — <see cref="LogLevel.Information"/>.</description></item>
///   <item><description>Success over the threshold — <see cref="LogLevel.Warning"/>, naming the threshold that was exceeded.</description></item>
///   <item><description>A <c>Result</c>/<c>Result&lt;T&gt;</c> failure — <see cref="LogLevel.Warning"/>, naming the error's type and code.</description></item>
///   <item><description>A thrown exception — <see cref="LogLevel.Error"/> with the exception, then rethrown unchanged; never swallowed.</description></item>
/// </list>
/// Classification is delegated to <see cref="ResponseOutcome"/>, the same helper
/// <see cref="Metrics.MetricsBehavior{TRequest,TResponse}"/> uses, so the two behaviors' taxonomy
/// can never silently diverge. Never logs request or response payloads by default — see
/// <see cref="ILoggableRequest{TResponse}"/> for the opt-in mechanism.
/// </para>
/// </remarks>
public sealed partial class LoggingBehavior<TRequest, TResponse>(
    ILogger<TRequest> logger,
    IOptions<ApplicationLoggingOptions> options)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestType = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var startTimestamp = Stopwatch.GetTimestamp();

        var loggable = request as ILoggableRequest<TResponse>;
        var requestFields = loggable?.LoggableRequestFields;

        using (requestFields is { Count: > 0 } ? logger.BeginScope(requestFields) : null)
        {
            LogHandling(logger, requestType);
        }

        try
        {
            var response = await next().ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var responseFields = loggable?.GetLoggableResponseFields(response);

            using (responseFields is { Count: > 0 } ? logger.BeginScope(responseFields) : null)
            {
                var error = ResponseOutcome.TryGetError(response);
                if (error is not null)
                {
                    LogHandledFailure(logger, requestType, error.Type.ToString(), error.Code, elapsed.TotalMilliseconds);
                }
                else
                {
                    var threshold = options.Value.SlowRequestThreshold;
                    if (elapsed > threshold)
                        LogHandledSuccessSlow(logger, requestType, elapsed.TotalMilliseconds, threshold.TotalMilliseconds);
                    else
                        LogHandledSuccess(logger, requestType, elapsed.TotalMilliseconds);
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

            using (requestFields is { Count: > 0 } ? logger.BeginScope(requestFields) : null)
            {
                LogHandlingFailed(logger, requestType, elapsed.TotalMilliseconds, ex);
            }

            throw;
        }
    }

    /// <summary>Entry log (Debug) — emitted once per request before <c>next()</c> runs.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogHandling,
        Level = LogLevel.Debug,
        Message = "Handling {RequestType}")]
    private static partial void LogHandling(ILogger logger, string requestType);

    /// <summary>Completion log, success path within threshold (Information).</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogHandledSuccess,
        Level = LogLevel.Information,
        Message = "Handled {RequestType} in {ElapsedMilliseconds}ms")]
    private static partial void LogHandledSuccess(ILogger logger, string requestType, double elapsedMilliseconds);

    /// <summary>Completion log, success path over the configured slow-request threshold (Warning).</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogHandledSuccessSlow,
        Level = LogLevel.Warning,
        Message = "Handled {RequestType} in {ElapsedMilliseconds}ms, over the {ThresholdMilliseconds}ms threshold")]
    private static partial void LogHandledSuccessSlow(ILogger logger, string requestType, double elapsedMilliseconds, double thresholdMilliseconds);

    /// <summary>Completion log, failure path — <c>next()</c> returned a <c>Result</c>/<c>Result&lt;T&gt;</c> failure (Warning).</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogHandledFailure,
        Level = LogLevel.Warning,
        Message = "Handled {RequestType} with failure {ErrorType} {ErrorCode} in {ElapsedMilliseconds}ms")]
    private static partial void LogHandledFailure(ILogger logger, string requestType, string errorType, string errorCode, double elapsedMilliseconds);

    /// <summary>Fault log (Error) — <c>next()</c> threw; the exception is rethrown unchanged after this log call.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogHandlingFailed,
        Level = LogLevel.Error,
        Message = "Handling {RequestType} failed after {ElapsedMilliseconds}ms")]
    private static partial void LogHandlingFailed(ILogger logger, string requestType, double elapsedMilliseconds, Exception exception);
}
