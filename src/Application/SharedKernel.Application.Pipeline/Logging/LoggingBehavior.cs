using SharedKernel.Application.Logging;
using System.Diagnostics;
using SharedKernel.Application.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Pipeline.Diagnostics;
using SharedKernel.Application.Pipeline.Shared;

namespace SharedKernel.Application.Pipeline.Logging;

/// <summary>
/// Logs the start and completion (or fault) of every request that passes through the pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type being logged.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// The category is this behavior (<c>ILogger&lt;LoggingBehavior&lt;,&gt;&gt;</c>), never the request type; the request
/// type is the <c>{RequestType}</c> placeholder. Logs <see cref="LogLevel.Debug"/> at start, timed via
/// <see cref="Stopwatch.GetTimestamp"/>/<see cref="Stopwatch.GetElapsedTime(long)"/> (no
/// <see cref="Stopwatch"/> allocation).
/// </para>
/// <para>
/// On completion:
/// <list type="bullet">
///   <item><description>Success within <see cref="ApplicationLoggingOptions.SlowRequestThreshold"/> — <see cref="LogLevel.Debug"/>.</description></item>
///   <item><description>Success over the threshold — <see cref="LogLevel.Warning"/>, naming the threshold that was exceeded.</description></item>
///   <item><description>A <c>Result</c>/<c>Result&lt;T&gt;</c> failure — <see cref="LogLevel.Warning"/>, naming the error's type and code.</description></item>
///   <item><description>A cancellation (an <see cref="OperationCanceledException"/>) — <see cref="LogLevel.Debug"/>, then rethrown unchanged.</description></item>
///   <item><description>Any other thrown exception — <see cref="LogLevel.Error"/> with the exception, then rethrown unchanged; never swallowed.</description></item>
/// </list>
/// Classification is delegated to <see cref="ResponseOutcome"/>, the same helper
/// <see cref="Metrics.MetricsBehavior{TRequest,TResponse}"/> uses, so the two behaviors' taxonomy
/// can never silently diverge. Never logs request or response payloads by default — see
/// <see cref="ILoggableRequest{TResponse}"/> for the opt-in mechanism.
/// </para>
/// </remarks>
internal sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
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
            PipelineLog.Handling(logger, requestType);
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
                    PipelineLog.HandledFailure(logger, requestType, error.Type.ToString(), error.Code, elapsed.TotalMilliseconds);
                }
                else
                {
                    var threshold = options.Value.SlowRequestThreshold;
                    if (elapsed > threshold)
                        PipelineLog.HandledSlow(logger, requestType, elapsed.TotalMilliseconds, threshold.TotalMilliseconds);
                    else
                        PipelineLog.HandledSuccess(logger, requestType, elapsed.TotalMilliseconds);
                }
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

            using (requestFields is { Count: > 0 } ? logger.BeginScope(requestFields) : null)
            {
                PipelineLog.HandlingCanceled(logger, requestType, elapsed.TotalMilliseconds);
            }

            throw;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

            using (requestFields is { Count: > 0 } ? logger.BeginScope(requestFields) : null)
            {
                PipelineLog.HandlingThrew(logger, requestType, elapsed.TotalMilliseconds, ex);
            }

            throw;
        }
    }
}
