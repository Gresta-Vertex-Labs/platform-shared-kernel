namespace SharedKernel.Application.Messaging;

/// <summary>
/// A request with a response of type <typeparamref name="TResponse"/>, sent through
/// <see cref="ISender"/> and handled by exactly one <see cref="IRequestHandler{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// <para>
/// The kernel's own request contract. Application code declares requests against it — normally
/// through <see cref="ICommand"/>, <see cref="ICommand{TResponse}"/> or <see cref="IQuery{TResponse}"/>
/// — and never against a mediator library's type, so the mediator can be replaced without touching
/// a command, a query, a handler or a behavior. <c>SharedKernel.Application.Mediator.MediatR</c> is the
/// shipped implementation.
/// </para>
/// <para>
/// The response of a request that uses a pipeline marker (<c>IAuthorizeRequest</c>,
/// <c>IIdempotentRequest</c>) must be <c>Result</c> or <c>Result&lt;T&gt;</c>, because the pipeline
/// short-circuits by constructing a failed response. <c>SharedKernel.Analyzers</c>' SK0040 reports the
/// mistake.
/// </para>
/// </remarks>
public interface IRequest<out TResponse>;
