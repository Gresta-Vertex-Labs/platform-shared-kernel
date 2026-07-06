using MediatR;
using SharedKernel.Application.Messaging;

namespace SharedKernel.Application.Behaviors.FireAndForget;

/// <summary>
/// Guards against accidental routing of <see cref="IFireAndForgetCommand"/> instances through
/// the normal <c>ISender.Send</c> MediatR pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type being dispatched.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Fire-and-forget commands must be dispatched via <see cref="IFireAndForgetDispatcher.EnqueueAsync"/>,
/// not via <c>ISender.Send</c>. If a caller mistakenly calls <c>ISender.Send</c> with an
/// <see cref="IFireAndForgetCommand"/>, this behavior short-circuits and throws
/// <see cref="InvalidOperationException"/> before the handler runs.
/// </para>
/// <para>
/// Register this behavior as the outermost behavior (before <c>LoggingBehavior</c>) or at minimum
/// before the handler, so the error is surfaced before any cross-cutting work is performed.
/// In practice, <c>ApplicationBehaviorsBuilder.AddFireAndForgetDispatch()</c> registers it
/// before all other behaviors.
/// </para>
/// <para>
/// <b>Trusted-dispatch exemption (WO-039, P-238):</b> before throwing, this behavior checks the
/// internal, unspoofable <see cref="FireAndForgetDispatchContext.IsTrusted"/> ambient marker. When
/// set — which is only ever true inside <see cref="FireAndForgetBackgroundConsumer"/>'s own internal
/// dispatch call — this behavior calls <c>next()</c> instead of throwing, letting the rest of the
/// registered pipeline (Logging/Metrics/Validation/etc.) run for the internally-dispatched command.
/// The marker has no public surface and cannot be set by consuming-service code, so the external-
/// misuse guard below is never weakened by this exemption.
/// </para>
/// </remarks>
public sealed class FireAndForgetGuardBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IFireAndForgetCommand)
        {
            if (FireAndForgetDispatchContext.IsTrusted)
                return next();

            throw new InvalidOperationException(
                $"The command '{typeof(TRequest).FullName ?? typeof(TRequest).Name}' implements " +
                $"IFireAndForgetCommand and must not be dispatched through ISender.Send. " +
                $"Route it through IFireAndForgetDispatcher.EnqueueAsync instead.");
        }

        return next();
    }
}
