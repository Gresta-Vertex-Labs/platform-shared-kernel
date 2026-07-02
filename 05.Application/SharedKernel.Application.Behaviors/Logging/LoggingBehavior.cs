using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Results;

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
///     <term>Response implements <see cref="IHasSuccessFlag"/> with <c>IsSuccess == false</c></term>
///     <description>Logs at <see cref="LogLevel.Warning"/> ("Handled {RequestName} with failure
///     in {ElapsedMilliseconds}ms") so operations teams can alert on failure rates without
///     sifting through <c>Information</c> noise.</description>
///   </item>
///   <item>
///     <term>Response implements <see cref="IHasSuccessFlag"/> with <c>IsSuccess == true</c>,
///     or response does not implement <see cref="IHasSuccessFlag"/></term>
///     <description>Logs at <see cref="LogLevel.Information"/> ("Handled {RequestName} in
///     {ElapsedMilliseconds}ms").</description>
///   </item>
/// </list>
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
            if (response is IHasSuccessFlag flag && !flag.IsSuccess)
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
