using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Application.Messaging;
using System.Threading.Channels;

namespace SharedKernel.Application.Behaviors.FireAndForget;

/// <summary>
/// <see cref="IFireAndForgetDispatcher"/> implementation backed by a bounded
/// <see cref="Channel{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Writes commands to the channel's writer end. The channel is shared (by DI singleton
/// registration) with <see cref="FireAndForgetBackgroundConsumer"/>, which reads and dispatches
/// commands on a background loop.
/// </para>
/// <para>
/// When the channel is full, the behavior is controlled by
/// <see cref="FireAndForgetOptions.RejectionPolicy"/>:
/// <list type="bullet">
///   <item>
///     <term><see cref="FireAndForgetRejectionPolicy.DropAndLog"/></term>
///     <description>Drops the command and logs a <c>Warning</c> — the caller receives no exception.
///     This is the default.</description>
///   </item>
///   <item>
///     <term><see cref="FireAndForgetRejectionPolicy.Block"/></term>
///     <description>Blocks until capacity is available or the <see cref="CancellationToken"/> is
///     cancelled.</description>
///   </item>
/// </list>
/// </para>
/// </remarks>
internal sealed partial class ChannelFireAndForgetDispatcher(
    ChannelWriter<IFireAndForgetCommand> writer,
    IOptions<FireAndForgetOptions> options,
    ILogger<ChannelFireAndForgetDispatcher> logger)
    : IFireAndForgetDispatcher
{
    private readonly FireAndForgetOptions _options = options.Value;

    /// <inheritdoc/>
    public ValueTask EnqueueAsync(IFireAndForgetCommand command, CancellationToken cancellationToken = default)
    {
        if (_options.RejectionPolicy == FireAndForgetRejectionPolicy.Block)
        {
            // Blocking path: wait for capacity.
            return writer.WriteAsync(command, cancellationToken);
        }

        // DropAndLog path: try to write without blocking; drop and log if the channel is full.
        if (writer.TryWrite(command))
            return ValueTask.CompletedTask;

        LogChannelFull(logger, _options.Capacity, command.GetType().FullName ?? command.GetType().Name);

        return ValueTask.CompletedTask;
    }

    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogChannelFull,
        Level = LogLevel.Warning,
        Message = "Fire-and-forget channel is full (capacity={Capacity}). Command {CommandType} was dropped and will not be executed.")]
    private static partial void LogChannelFull(ILogger logger, int capacity, string commandType);
}
