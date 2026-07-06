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
/// </remarks>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<TRequest> logger)
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

        logger.LogInformation("Handling {RequestName}", requestName);

        try
        {
            var response = await next().ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

            // Emit at Warning when the response signals a business-rule failure (IHasSuccessFlag);
            // emit at Information for success or for response types that do not participate in the
            // Result railway (e.g. raw T responses from streaming handlers).
            if (!ResponseOutcomeClassifier.IsSuccess(response))
            {
                logger.LogWarning(
                    "Handled {RequestName} with failure in {ElapsedMilliseconds}ms",
                    requestName,
                    elapsed.TotalMilliseconds);
            }
            else
            {
                logger.LogInformation(
                    "Handled {RequestName} in {ElapsedMilliseconds}ms",
                    requestName,
                    elapsed.TotalMilliseconds);
            }

            return response;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            logger.LogError(
                ex,
                "Handling {RequestName} failed after {ElapsedMilliseconds}ms",
                requestName,
                elapsed.TotalMilliseconds);
            throw;
        }
    }
}
