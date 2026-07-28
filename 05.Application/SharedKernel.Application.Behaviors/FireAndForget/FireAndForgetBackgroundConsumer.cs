using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Application.Messaging;
using System.Threading.Channels;

namespace SharedKernel.Application.Behaviors.FireAndForget;

/// <summary>
/// Background service that reads <see cref="IFireAndForgetCommand"/> instances from the shared
/// bounded channel and dispatches each through a scoped MediatR pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Each command is dispatched inside its own DI scope (via
/// <see cref="IServiceScopeFactory"/>) so scoped dependencies (repositories, unit-of-work,
/// per-request services) resolve correctly even though the consumer runs as a
/// <see cref="BackgroundService"/> (singleton lifetime).
/// </para>
/// <para>
/// Handler exceptions are caught, logged at <c>Error</c> level, and discarded — the caller
/// already relinquished result observation by choosing the fire-and-forget path. The background
/// loop continues consuming subsequent commands.
/// </para>
/// <para>
/// A handler that returns <see cref="SharedKernel.Primitives.Results.Result.Failure(SharedKernel.Primitives.Errors.Error)"/> — a deliberate business-rule failure,
/// not a thrown exception — is likewise discarded per the fire-and-forget contract, but is
/// additionally logged at <c>Warning</c> level (EventId 5112) so the outcome remains observable
/// instead of vanishing silently. This is distinct from the <c>Error</c>-level handler-fault log
/// above: a thrown exception is an unexpected fault the handler never anticipated, while a
/// <c>Result.Failure</c> is an expected, foreseeable outcome the handler evaluated and returned
/// normally.
/// </para>
/// <para>
/// <b>Trusted dispatch marker (WO-039, P-238):</b> the internal <c>ISender.Send</c> call is wrapped
/// in <see cref="FireAndForgetDispatchContext.EnterTrustedDispatch"/>'s disposable scope so
/// <see cref="FireAndForgetGuardBehavior{TRequest,TResponse}"/> permits this dispatch through
/// instead of rejecting it as external misuse. The scope's <see cref="IDisposable.Dispose"/> resets
/// the marker unconditionally — even if <c>Send</c> throws — because the <see langword="using"/>
/// block guarantees disposal on exit via any path (normal return or exception).
/// </para>
/// </remarks>
public sealed partial class FireAndForgetBackgroundConsumer(
    ChannelReader<IFireAndForgetCommand> reader,
    IServiceScopeFactory scopeFactory,
    ILogger<FireAndForgetBackgroundConsumer> logger)
    : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var command in reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                // Mark this dispatch as trusted so FireAndForgetGuardBehavior<,> permits it through
                // instead of rejecting it as external misuse (WO-039, P-238). The using block
                // guarantees the marker is reset on scope exit even if Send throws.
                using (FireAndForgetDispatchContext.EnterTrustedDispatch())
                {
                    var result = await sender.Send(command, stoppingToken).ConfigureAwait(false);
                    if (result.IsFailure)
                    {
                        LogCommandFailed(logger, command.GetType().FullName ?? command.GetType().Name, result.Error.Code);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down — stop processing.
                break;
            }
            catch (Exception ex)
            {
                // Log and swallow: the caller chose fire-and-forget, so result is not observable.
                LogCommandFaulted(logger, ex, command.GetType().FullName ?? command.GetType().Name);
            }
        }
    }

    /// <summary>Handler-fault log (EventId 5111, Error) — the dispatched command's handler threw; the result is discarded (fire-and-forget contract), the background loop continues.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogCommandFaulted,
        Level = LogLevel.Error,
        Message = "Fire-and-forget command {CommandType} faulted and its result was discarded.")]
    private static partial void LogCommandFaulted(ILogger logger, Exception exception, string commandType);

    /// <summary>Handler-failure log (EventId 5112, Warning) — the dispatched command's handler returned a <c>Result.Failure</c> outcome; the result is discarded (fire-and-forget contract), the background loop continues.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogCommandFailed,
        Level = LogLevel.Warning,
        Message = "Fire-and-forget command {CommandType} completed with a failure result ({ErrorCode}) and the result was discarded.")]
    private static partial void LogCommandFailed(ILogger logger, string commandType, string errorCode);
}
