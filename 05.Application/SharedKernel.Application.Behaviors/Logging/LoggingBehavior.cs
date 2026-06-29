using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Application.Behaviors.Logging;

/// <summary>
/// Logs the start and completion (or fault) of every request that passes through the pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type being logged.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Logs <see cref="LogLevel.Information"/> at start ("Handling {RequestName}") and at successful
/// completion ("Handled {RequestName} in {ElapsedMilliseconds}ms"), timed via
/// <see cref="Stopwatch.GetTimestamp"/>/<see cref="Stopwatch.GetElapsedTime(long)"/> (no
/// <see cref="Stopwatch"/> allocation). On exception: logs <see cref="LogLevel.Error"/> with the
/// exception and elapsed time, then rethrows unchanged — never swallows. Does not log request or
/// response payloads by default (PII risk in command/query parameters).
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
        var requestName = typeof(TRequest).Name;
        var startTimestamp = Stopwatch.GetTimestamp();

        logger.LogInformation("Handling {RequestName}", requestName);

        try
        {
            var response = await next().ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMilliseconds}ms",
                requestName,
                elapsed.TotalMilliseconds);

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
