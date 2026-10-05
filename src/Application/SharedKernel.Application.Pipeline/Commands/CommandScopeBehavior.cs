using SharedKernel.Application.Commands;
using SharedKernel.Application.Messaging;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Pipeline.Shared;

namespace SharedKernel.Application.Pipeline.Commands;

/// <summary>
/// Tracks command nesting depth and runs <see cref="ICommandScope.OnCompleted"/> callbacks after
/// the outermost command in the current DI scope succeeds.
/// </summary>
/// <typeparam name="TRequest">The command type, constrained to <see cref="ICommandBase"/>.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// The first behavior in the command stage — registered outermost among the command-stage
/// behaviors, so its post-<c>next()</c> code (running the queued callbacks) executes last, after
/// every inner command-stage behavior — including <c>TransactionBehavior</c>'s commit — has already
/// unwound. This is how <c>SharedKernel.Application.Pipeline.Caching</c>'s
/// <c>CacheInvalidationBehavior</c> achieves "evict only after a confirmed commit" without any
/// registration-order trickery of its own: it simply calls <see cref="ICommandScope.OnCompleted"/>
/// instead of evicting directly.
/// </para>
/// <para>
/// Automatically registered by <c>AddSharedKernelApplication</c> whenever any command-stage
/// behavior (idempotency, transaction, auditing, or a custom <c>PipelineStage.Command</c> behavior)
/// is active — never opted into directly.
/// </para>
/// <para>
/// A nested command (sent via <c>ISender.Send</c> from inside another command's handler) that
/// succeeds has its callbacks merged into the outer command's frame rather than run immediately; a
/// nested command that fails or throws has its callbacks discarded. The outermost command runs its
/// callbacks in registration order only on success; a callback that throws is logged and does not
/// change the response — the work it was meant to follow up on is already committed. Depth is
/// always reset by exactly one, regardless of outcome.
/// </para>
/// </remarks>
internal sealed partial class CommandScopeBehavior<TRequest, TResponse>(
    CommandScope scope,
    ILogger<CommandScopeBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        scope.Enter();
        var exited = false;

        try
        {
            var response = await next().ConfigureAwait(false);
            var callbacks = scope.Exit(succeeded: ResponseOutcome.IsSuccess(response));
            exited = true;

            foreach (var callback in callbacks)
            {
                try
                {
                    await callback(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogCallbackFailed(logger, ex);
                }
            }

            return response;
        }
        finally
        {
            if (!exited)
                scope.Exit(succeeded: false);
        }
    }

    /// <summary>A post-commit <see cref="ICommandScope.OnCompleted"/> callback threw (Error) — the response is unaffected.</summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogCommandScopeCallbackFailed,
        Level = LogLevel.Error,
        Message = "A post-commit ICommandScope.OnCompleted callback threw")]
    private static partial void LogCallbackFailed(ILogger logger, Exception exception);
}
