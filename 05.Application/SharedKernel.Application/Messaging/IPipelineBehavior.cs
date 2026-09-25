namespace SharedKernel.Application.Messaging;

/// <summary>
/// Continues a request pipeline: calling it runs the next behavior, or the handler when this is the
/// innermost behavior.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <returns>The response produced by the rest of the pipeline.</returns>
public delegate Task<TResponse> RequestHandlerContinuation<TResponse>();

/// <summary>
/// Wraps the handling of a request with cross-cutting work — logging, authorization, validation,
/// transactions and the like.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// <para>
/// A behavior is registered open-generic against this interface, and narrows the requests it
/// applies to through its generic constraints (<c>where TRequest : ICommandBase</c>, a marker
/// interface, …). The pipeline resolves every registration whose constraints the request satisfies,
/// in registration order, the first one outermost. <c>SharedKernel.Application.Pipeline</c>'s
/// <c>ApplicationBehaviorsBuilder</c> fixes that order by stage, so the order is a property of the
/// platform rather than of the calls a service happens to make.
/// </para>
/// <para>
/// A behavior short-circuits by returning without calling its <c>next</c> continuation in
/// <see cref="Handle"/>; for a <c>Result</c> response that means returning a failed result, never
/// throwing for an expected failure.
/// </para>
/// </remarks>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Handles <paramref name="request"/>, calling <paramref name="next"/> to continue the pipeline.</summary>
    /// <param name="request">The request.</param>
    /// <param name="next">Continues the pipeline.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The response to return to the caller.</returns>
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken);
}
