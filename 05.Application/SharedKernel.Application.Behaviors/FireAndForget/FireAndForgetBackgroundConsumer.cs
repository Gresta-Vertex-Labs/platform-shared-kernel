using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
/// </remarks>
public sealed class FireAndForgetBackgroundConsumer(
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
                await sender.Send(command, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down — stop processing.
                break;
            }
            catch (Exception ex)
            {
                // Log and swallow: the caller chose fire-and-forget, so result is not observable.
                logger.LogError(
                    ex,
                    "Fire-and-forget command {CommandType} faulted and its result was discarded.",
                    command.GetType().FullName ?? command.GetType().Name);
            }
        }
    }
}
