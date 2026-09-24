using System.Reflection;
using MediatR;
using SharedKernel.Application.Context;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Short-circuits the pipeline with an unauthorized or forbidden failure when the current caller
/// does not hold the permissions a request declares with <see cref="RequirePermissionAttribute"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Runs before <c>ValidationBehavior</c> in the canonical pipeline — an unauthorized caller must
/// never learn a request's validation rules, and a validator may itself hit the database, which an
/// unauthorized caller should never trigger.
/// </para>
/// <para>
/// A request without the attribute passes unchecked. Otherwise: not authenticated
/// (<see cref="IRequestContext.IsAuthenticated"/> is <see langword="false"/>) →
/// <c>Error.Unauthorized(ErrorCodes.Unauthorized.Default, ...)</c>; one attribute whose values the
/// caller holds none of → <c>Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission, ...)</c>,
/// with a message that never names the permission. The values of one attribute are alternatives,
/// several attributes all apply.
/// </para>
/// <para>
/// The attributes are read once per request type (<see cref="PermissionRequirements{TRequest}"/>),
/// never per call. Every failure is returned as a failed <c>Result</c>/<c>Result&lt;T&gt;</c> — never
/// thrown.
/// </para>
/// </remarks>
internal sealed class AuthorizationBehavior<TRequest, TResponse>(IRequestContext requestContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>The error code for an unauthenticated caller of a protected request.</summary>
    internal const string UnauthenticatedCode = ErrorCodes.Unauthorized.Default;

    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requirements = PermissionRequirements<TRequest>.AllOf;
        if (requirements.Length == 0)
            return await next().ConfigureAwait(false);

        if (!requestContext.IsAuthenticated)
        {
            return FailureResponse.Create<TResponse>(
                Error.Unauthorized(UnauthenticatedCode, "Authentication is required."));
        }

        foreach (var anyOf in requirements)
        {
            if (!await HoldsAnyAsync(anyOf, cancellationToken).ConfigureAwait(false))
            {
                return FailureResponse.Create<TResponse>(
                    Error.Forbidden(
                        ErrorCodes.Forbidden.InsufficientPermission,
                        "The caller does not have the required permission."));
            }
        }

        return await next().ConfigureAwait(false);
    }

    private async Task<bool> HoldsAnyAsync(IReadOnlyList<string> permissions, CancellationToken cancellationToken)
    {
        foreach (var permission in permissions)
        {
            if (await requestContext.HasPermissionAsync(permission, cancellationToken).ConfigureAwait(false))
                return true;
        }

        return false;
    }
}

/// <summary>
/// The <see cref="RequirePermissionAttribute"/> declarations of <typeparamref name="TRequest"/>, read
/// once per closed request type.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <remarks>
/// Each element is one attribute's values (any of them satisfies it); every element must be
/// satisfied. Empty when the request declares none. An invalid attribute (no value, or a blank one)
/// makes the type initializer throw, so every send of that request fails instead of running
/// unchecked.
/// </remarks>
internal static class PermissionRequirements<TRequest>
{
    /// <summary>Gets the requirements; every one must be met.</summary>
    internal static readonly IReadOnlyList<string>[] AllOf =
    [
        .. typeof(TRequest)
            .GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
            .Select(attribute => attribute.Permissions),
    ];
}
