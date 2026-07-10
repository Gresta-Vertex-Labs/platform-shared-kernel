using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Behaviors.Shared;

namespace SharedKernel.Application.Behaviors.Logging;

/// <summary>
/// Logs the start and completion (or fault) of every request that passes through the pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type being logged.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Logs <see cref="LogLevel.Information"/> at start ("Handling {RequestName}"), timed via
/// <see cref="Stopwatch.GetTimestamp"/>/<see cref="Stopwatch.GetElapsedTime(long)"/> (no
/// <see cref="Stopwatch"/> allocation).
/// </para>
/// <para>
/// <b>Post-handler log level (WO-038, P-232, depends on P-230/IHasSuccessFlag):</b>
/// <list type="bullet">
///   <item>
///     <term>Response implements <c>IHasSuccessFlag</c> with <c>IsSuccess == false</c></term>
///     <description>Logs at <see cref="LogLevel.Warning"/> ("Handled {RequestName} with failure
///     in {ElapsedMilliseconds}ms") so operations teams can alert on failure rates without
///     sifting through <c>Information</c> noise.</description>
///   </item>
///   <item>
///     <term>Response implements <c>IHasSuccessFlag</c> with <c>IsSuccess == true</c>,
///     or response does not implement <c>IHasSuccessFlag</c></term>
///     <description>Logs at <see cref="LogLevel.Information"/> ("Handled {RequestName} in
///     {ElapsedMilliseconds}ms").</description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Shared classification helper (WO-039, P-239):</b> the success/failure decision above is
/// delegated to <see cref="ResponseOutcomeClassifier"/> — the same helper
/// <see cref="Metrics.MetricsBehavior{TRequest,TResponse}"/> uses for its <c>outcome</c> tag, so the
/// two behaviors' classification of a response can never silently diverge.
/// </para>
/// <para>
/// On exception: logs <see cref="LogLevel.Error"/> with the exception and elapsed time, then
/// rethrows unchanged — never swallows. Does not log request or response payloads by default
/// (PII risk in command/query parameters).
/// </para>
/// <para>
/// The <c>request.name</c> tag value uses <c>typeof(TRequest).FullName ?? typeof(TRequest).Name</c>
/// to prevent log key collisions when two assemblies in the same host define a request type with
/// the same short name.
/// </para>
/// <para>
/// <b>Opt-in structured payload logging (WO-040, P-246):</b> when <c>TRequest</c> implements
/// <see cref="ILoggableRequest{TResponse}"/>, the entry log line additionally opens an
/// <see cref="ILogger.BeginScope{TState}"/> scope over <c>LoggableRequestFields</c> (skipped when
/// null/empty), and the completion log line additionally opens a scope over
/// <c>GetLoggableResponseFields(response)</c> — only when <c>next()</c> returns normally, never on
/// a thrown exception. The request-side scope also wraps the fault-path <see cref="LogLevel.Error"/>
/// log line. This is a pure additive branch: a <c>TRequest</c> not implementing
/// <see cref="ILoggableRequest{TResponse}"/> produces byte-for-byte identical logging behavior to
/// before this capability existed.
/// </para>
/// </remarks>
public sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<TRequest> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var startTimestamp = Stopwatch.GetTimestamp();

        // WO-040 (P-246): pure additive opt-in — a TRequest not implementing ILoggableRequest<TResponse>
        // takes the `loggable is null` path below and produces identical logging behavior to before
        // this capability existed.
        var loggable = request as ILoggableRequest<TResponse>;
        var requestFields = loggable?.LoggableRequestFields;

        using (requestFields is { Count: > 0 } ? logger.BeginScope(requestFields) : null)
        {
            LogHandling(logger, requestName);
        }

        try
        {
            var response = await next().ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var responseFields = loggable?.GetLoggableResponseFields(response);

            using (responseFields is { Count: > 0 } ? logger.BeginScope(responseFields) : null)
            {
                // Emit at Warning when the response signals a business-rule failure (IHasSuccessFlag);
                // emit at Information for success or for response types that do not participate in the
                // Result railway (e.g. raw T responses from streaming handlers).
                if (!ResponseOutcomeClassifier.IsSuccess(response))
                {
                    LogHandledFailure(logger, requestName, elapsed.TotalMilliseconds);
                }
                else
                {
                    LogHandledSuccess(logger, requestName, elapsed.TotalMilliseconds);
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

            using (requestFields is { Count: > 0 } ? logger.BeginScope(requestFields) : null)
            {
                LogHandlingFailed(logger, requestName, elapsed.TotalMilliseconds, ex);
            }

            throw;
        }
    }

    /// <summary>Entry log (EventId 5100, Information) — emitted once per request before <c>next()</c> runs.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogHandling,
        Level = LogLevel.Information,
        Message = "Handling {RequestName}")]
    private static partial void LogHandling(ILogger logger, string requestName);

    /// <summary>Completion log, success path (EventId 5101, Information) — <c>next()</c> returned a successful response.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogHandledSuccess,
        Level = LogLevel.Information,
        Message = "Handled {RequestName} in {ElapsedMilliseconds}ms")]
    private static partial void LogHandledSuccess(ILogger logger, string requestName, double elapsedMilliseconds);

    /// <summary>Completion log, failure path (EventId 5102, Warning) — <c>next()</c> returned a response classified as failed by <see cref="ResponseOutcomeClassifier"/>.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogHandledFailure,
        Level = LogLevel.Warning,
        Message = "Handled {RequestName} with failure in {ElapsedMilliseconds}ms")]
    private static partial void LogHandledFailure(ILogger logger, string requestName, double elapsedMilliseconds);

    /// <summary>Fault log (EventId 5103, Error) — <c>next()</c> threw; the exception is rethrown unchanged after this log call.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogHandlingFailed,
        Level = LogLevel.Error,
        Message = "Handling {RequestName} failed after {ElapsedMilliseconds}ms")]
    private static partial void LogHandlingFailed(ILogger logger, string requestName, double elapsedMilliseconds, Exception exception);
}
