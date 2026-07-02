using MediatR;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Behaviors.Authorization;

/// <summary>
/// Short-circuits the pipeline with an unauthorized failure when the current caller does not
/// satisfy the authorization requirements declared by <see cref="IAuthorizeRequest"/>.
/// </summary>
/// <typeparam name="TRequest">
/// The request type, constrained to <see cref="IAuthorizeRequest"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Evaluates <see cref="IAuthorizationContext.AllOf"/> over
/// <see cref="IAuthorizeRequest.AllOfRequirements"/> first (short-circuit on first failure), then
/// <see cref="IAuthorizationContext.AnyOf"/> over <see cref="IAuthorizeRequest.AnyOfRequirements"/>
/// (short-circuit on first pass). Both are no-ops on empty collections. Additionally, when
/// <see cref="IAuthorizeRequest.Requirement"/> is non-empty and <see cref="IAuthorizeRequest.AllOfRequirements"/>
/// is empty, the single-requirement convenience property is evaluated via
/// <see cref="IAuthorizationContext.IsAuthorizedAsync"/> for backward compatibility.
/// </para>
/// <para>
/// On any authorization failure, short-circuits without calling <c>next()</c> and returns
/// <c>Result.Failure(Error.Unauthorized(...))</c> — for both <see cref="SharedKernel.Primitives.Results.Result"/>
/// and <see cref="SharedKernel.Primitives.Results.Result{T}"/> response shapes — <b>never throws</b>.
/// An unauthorized caller is an expected, foreseeable outcome, not a fault; exceptions stay
/// reserved for <c>ValidationException</c> and genuinely unexpected faults.
/// </para>
/// <para>
/// Runs after <c>ValidationBehavior</c> (reject malformed input before spending a permission
/// check) and before <c>CachingBehavior</c>/<c>TransactionBehavior</c> (never let an unauthorized
/// request reach a cache lookup or a mutation).
/// </para>
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
        // Step 1: AllOf — all listed requirements must pass (short-circuit on first failure).
        // Also evaluates the single-string Requirement convenience property when AllOfRequirements
        // is empty but Requirement is set, for backward compatibility.
        var allOfRequirements = request.AllOfRequirements;
        if (allOfRequirements.Count > 0)
        {
            if (!await authorizationContext.AllOf(allOfRequirements, cancellationToken).ConfigureAwait(false))
            {
                return FailureResponseFactory.Create<TResponse>(
                    Error.Unauthorized(
                        "authorization.forbidden",
                        "The current caller does not satisfy all required AllOf authorization requirements."));
            }
        }
        else if (!string.IsNullOrEmpty(request.Requirement))
        {
            // Backward-compatibility path for the single-string Requirement convenience property.
            if (!await authorizationContext.IsAuthorizedAsync(request.Requirement, cancellationToken).ConfigureAwait(false))
            {
                return FailureResponseFactory.Create<TResponse>(
                    Error.Unauthorized(
                        "authorization.forbidden",
                        $"The current caller is not authorized to satisfy requirement '{request.Requirement}'."));
            }
        }

        // Step 2: AnyOf — at least one listed requirement must pass (short-circuit on first pass).
        var anyOfRequirements = request.AnyOfRequirements;
        if (anyOfRequirements.Count > 0)
        {
            if (!await authorizationContext.AnyOf(anyOfRequirements, cancellationToken).ConfigureAwait(false))
            {
                return FailureResponseFactory.Create<TResponse>(
                    Error.Unauthorized(
                        "authorization.forbidden",
                        "The current caller does not satisfy any of the required AnyOf authorization requirements."));
            }
        }

        return await next().ConfigureAwait(false);
    }
}
