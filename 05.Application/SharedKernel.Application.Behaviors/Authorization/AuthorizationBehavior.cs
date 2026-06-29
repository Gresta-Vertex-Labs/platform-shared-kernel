using MediatR;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Authorization;

/// <summary>
/// Short-circuits the pipeline with an unauthorized failure when the current caller does not
/// satisfy <see cref="IAuthorizeRequest.Requirement"/>.
/// </summary>
/// <typeparam name="TRequest">
/// The request type, constrained to <see cref="IAuthorizeRequest"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Calls <see cref="IAuthorizationContext.IsAuthorizedAsync"/>. On <see langword="false"/>,
/// short-circuits without calling <c>next()</c> and returns
/// <c>Result.Failure(Error.Unauthorized(...))</c> — for both <see cref="Result"/> and
/// <see cref="Result{T}"/> response shapes — never throws. On <see langword="true"/>, calls
/// <c>next()</c> and returns its result unchanged. Runs after <c>ValidationBehavior</c> (reject
/// malformed input before spending a permission check) and before
/// <c>CachingBehavior</c>/<c>TransactionBehavior</c> (never let an unauthorized request reach a
/// cache lookup or a mutation).
/// </remarks>
public sealed class AuthorizationBehavior<TRequest, TResponse>(IAuthorizationContext authorizationContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IAuthorizeRequest, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var isAuthorized = await authorizationContext
            .IsAuthorizedAsync(request.Requirement, cancellationToken)
            .ConfigureAwait(false);

        if (!isAuthorized)
        {
            var error = Error.Unauthorized(
                "authorization.forbidden",
                $"The current caller is not authorized to satisfy requirement '{request.Requirement}'.");
            return FailureResponseFactory.Create<TResponse>(error);
        }

        return await next().ConfigureAwait(false);
    }
}
