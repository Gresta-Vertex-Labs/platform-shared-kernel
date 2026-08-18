using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Global endpoint filter that enforces <see cref="RequireRoleAttribute"/>/
/// <see cref="RequirePermissionAttribute"/> metadata attached to an endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Safe to register on every route — this filter reads endpoint metadata via
/// <see cref="EndpointHttpContextExtensions.GetEndpoint"/> and no-ops (calls
/// <c>next(context)</c> immediately, resolving nothing) when neither attribute is present.
/// Mirrors the "global registration, no-op when inapplicable" pattern already used by
/// <c>TenantContextHubFilter</c>/<c>HubExceptionMappingFilter</c> in
/// <c>SharedKernel.Presentation.SignalR</c>.
/// </para>
/// <para>
/// When at least one attribute is present, resolves <see cref="IUserContext"/> from
/// <see cref="HttpContext.RequestServices"/> and evaluates each attached attribute in order:
/// roles/permissions listed within one attribute instance are OR'd (any one satisfies it);
/// multiple attributes stacked on the same endpoint are AND'd (every attached attribute must
/// pass). On the first failing attribute, short-circuits — <c>next()</c> is never called — and
/// returns <see cref="Error.Forbidden(string, string)"/> converted via
/// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails"/> through
/// <see cref="Microsoft.AspNetCore.Http.Results.Problem(Microsoft.AspNetCore.Mvc.ProblemDetails)"/> —
/// never a bare, body-less 403. This produces the identical response shape a handler-level
/// <c>AuthorizationBehavior</c> rejection (<c>05.Application</c>) would produce, so an API
/// consumer sees one consistent error contract regardless of which layer rejected the request.
/// </para>
/// <para>
/// An anonymous/unauthenticated caller is correctly rejected through the ordinary
/// <see cref="IUserContext.HasRole"/>/<see cref="IUserContext.HasPermission"/> false path — every
/// unauthenticated/non-human <see cref="IUserContext"/> implementation shipped in
/// <c>12.Security</c> (<c>AnonymousUserContext</c>, and <c>SystemUserContext</c>'s own
/// <c>HasRole</c>/<c>HasPermission</c>) hardcodes those members to <see langword="false"/>, so no
/// dedicated <c>IsAuthenticated</c> branch is needed here.
/// </para>
/// </remarks>
public sealed class AuthorizationRequirementEndpointFilter : IEndpointFilter
{
    private const string ForbiddenErrorCode = "Authorization.Forbidden";

    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var metadata = context.HttpContext.GetEndpoint()?.Metadata;
        var roleRequirements = metadata?.GetOrderedMetadata<RequireRoleAttribute>() ?? [];
        var permissionRequirements = metadata?.GetOrderedMetadata<RequirePermissionAttribute>() ?? [];

        if (roleRequirements.Count == 0 && permissionRequirements.Count == 0)
        {
            return await next(context).ConfigureAwait(false);
        }

        var userContext = context.HttpContext.RequestServices.GetRequiredService<IUserContext>();

        foreach (var requirement in roleRequirements)
        {
            if (!requirement.Roles.Any(userContext.HasRole))
            {
                return BuildForbiddenResult(context.HttpContext, "role", requirement.Roles);
            }
        }

        foreach (var requirement in permissionRequirements)
        {
            if (!requirement.Permissions.Any(userContext.HasPermission))
            {
                return BuildForbiddenResult(context.HttpContext, "permission", requirement.Permissions);
            }
        }

        return await next(context).ConfigureAwait(false);
    }

    private static IResult BuildForbiddenResult(
        HttpContext httpContext,
        string requirementKind,
        IReadOnlyCollection<string> requiredValues)
    {
        var error = Error.Forbidden(
            ForbiddenErrorCode,
            $"The caller does not hold any of the required {requirementKind}(s): {string.Join(", ", requiredValues)}.");

        return Microsoft.AspNetCore.Http.Results.Problem(error.ToProblemDetails(httpContext));
    }
}
