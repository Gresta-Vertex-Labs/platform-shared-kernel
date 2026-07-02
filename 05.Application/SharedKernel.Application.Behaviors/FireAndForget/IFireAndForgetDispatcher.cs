using SharedKernel.Application.Messaging;

namespace SharedKernel.Application.Behaviors.FireAndForget;

/// <summary>
/// Enqueues fire-and-forget commands for background execution without awaiting their result.
/// </summary>
/// <remarks>
/// <para>
/// This is the caller-facing entry point for <see cref="IFireAndForgetCommand"/> dispatch.
/// Callers enqueue a command and continue — they never observe the command's
/// <c>Result</c> directly. Handlers still run in a background context managed by
/// <c>FireAndForgetBackgroundConsumer</c>.
/// </para>
/// <para>
/// Backed by a bounded <see cref="System.Threading.Channels.Channel{T}"/> whose capacity is
/// configured via <see cref="FireAndForgetOptions.Capacity"/>. If the channel is full,
/// <see cref="EnqueueAsync"/> blocks until space is available or the token is cancelled.
/// </para>
/// </remarks>
public interface IFireAndForgetDispatcher
{
    /// <summary>
    /// Enqueues <paramref name="command"/> for background execution.
    /// </summary>
    /// <param name="command">The fire-and-forget command to enqueue.</param>
    /// <param name="cancellationToken">
    /// A token to observe while waiting for channel capacity. If cancelled, the method
    /// returns without enqueuing the command.
    /// </param>
    ValueTask EnqueueAsync(IFireAndForgetCommand command, CancellationToken cancellationToken = default);
}
