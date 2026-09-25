using MediatR;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Behaviors.Authorization;

/// <summary>
/// Short-circuits the pipeline with an unauthorized or forbidden failure when the current caller
/// does not satisfy the requirements declared by <see cref="IAuthorizeRequest"/>.
/// </summary>
/// <typeparam name="TRequest">The request type, constrained to <see cref="IAuthorizeRequest"/>.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Runs before <c>ValidationBehavior</c> in the canonical pipeline — an unauthorized caller must
/// never learn a request's validation rules, and a validator may itself hit the database, which an
/// unauthorized caller should never trigger.
/// </para>
/// <para>
/// Not authenticated (<see cref="IRequestContext.IsAuthenticated"/> is <see langword="false"/>) →
/// <c>Error.Unauthorized("authorization.unauthenticated", ...)</c>.
/// </para>
/// <para>
/// <b>Fail closed on an empty declaration.</b> <see cref="IAuthorizeRequest.RequiredPermissions"/>
/// being empty is treated as a misconfiguration, not "no check needed" — it returns
/// <c>Error.Forbidden("authorization.no_permissions_declared", ...)</c>. A request that genuinely
/// needs no permission check should not implement <see cref="IAuthorizeRequest"/> at all; opting in
/// with nothing declared is refused rather than silently allowed.
/// </para>
/// <para>
/// Otherwise, <see cref="IAuthorizeRequest.RequiredPermissions"/> is evaluated against
/// <see cref="IRequestContext.HasPermissionAsync"/> per <see cref="IAuthorizeRequest.PermissionMatch"/>:
/// <see cref="PermissionMatch.All"/> requires every permission (short-circuits on the first miss),
/// <see cref="PermissionMatch.Any"/> requires at least one (short-circuits on the first hit). A
/// denial returns <c>Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission, ...)</c> — the
/// message never echoes which permission was missing.
/// </para>
/// <para>
/// Every failure is returned as a <c>Result.Failure</c>/<c>Result&lt;T&gt;.Failure</c> — never
/// thrown. An unauthorized or forbidden caller is an expected, foreseeable outcome, not a fault.
/// </para>
/// </remarks>
public sealed class AuthorizationBehavior<TRequest, TResponse>(IRequestContext requestContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IAuthorizeRequest, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!requestContext.IsAuthenticated)
        {
            return FailureResponse.Create<TResponse>(
                Error.Unauthorized("authorization.unauthenticated", "Authentication is required."));
        }

        var requiredPermissions = request.RequiredPermissions;
        if (requiredPermissions.Count == 0)
        {
            return FailureResponse.Create<TResponse>(
                Error.Forbidden(
                    "authorization.no_permissions_declared",
                    "The request declares no required permissions."));
        }

        var satisfied = request.PermissionMatch == PermissionMatch.All
            ? await AllOf(requiredPermissions, cancellationToken).ConfigureAwait(false)
            : await AnyOf(requiredPermissions, cancellationToken).ConfigureAwait(false);

        if (!satisfied)
        {
            return FailureResponse.Create<TResponse>(
                Error.Forbidden(
                    ErrorCodes.Forbidden.InsufficientPermission,
                    "The caller does not have the required permission."));
        }

        return await next().ConfigureAwait(false);
    }

    private async Task<bool> AllOf(IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        foreach (var permission in permissions)
        {
            if (!await requestContext.HasPermissionAsync(permission, cancellationToken).ConfigureAwait(false))
                return false;
        }

        return true;
    }

    private async Task<bool> AnyOf(IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        foreach (var permission in permissions)
        {
            if (await requestContext.HasPermissionAsync(permission, cancellationToken).ConfigureAwait(false))
                return true;
        }

        return false;
    }
}
