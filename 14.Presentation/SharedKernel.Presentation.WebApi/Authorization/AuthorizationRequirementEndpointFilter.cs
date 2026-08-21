using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Global endpoint filter that enforces <see cref="RequireRoleAttribute"/>/
/// <see cref="RequirePermissionAttribute"/>/<see cref="RequireFreshAuthenticationAttribute"/>/
/// <see cref="RequireAuthenticationMethodAttribute"/> metadata attached to an endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Safe to register on every route — this filter reads endpoint metadata via
/// <see cref="EndpointHttpContextExtensions.GetEndpoint"/> and no-ops (calls
/// <c>next(context)</c> immediately, resolving nothing) when none of the four attributes are
/// present. Mirrors the "global registration, no-op when inapplicable" pattern already used by
/// <c>TenantContextHubFilter</c>/<c>HubExceptionMappingFilter</c> in
/// <c>SharedKernel.Presentation.SignalR</c>.
/// </para>
/// <para>
/// When at least one attribute is present, resolves <see cref="IUserContext"/> from
/// <see cref="HttpContext.RequestServices"/> and evaluates each attached attribute in order:
/// roles/permissions/authentication-methods listed within one attribute instance are OR'd (any one
/// satisfies it); every attribute type stacked on the same endpoint is AND'd (every attached
/// attribute must pass). On the first failing attribute, short-circuits — <c>next()</c> is never
/// called — and returns <see cref="Error.Forbidden(string, string)"/> converted via
/// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails"/> through
/// <see cref="Microsoft.AspNetCore.Http.Results.Problem(Microsoft.AspNetCore.Mvc.ProblemDetails)"/> —
/// never a bare, body-less 403. This produces the identical response shape a handler-level
/// <c>AuthorizationBehavior</c> rejection (<c>05.Application</c>) would produce, so an API
/// consumer sees one consistent error contract regardless of which layer rejected the request.
/// </para>
/// <para>
/// An anonymous/unauthenticated caller is correctly rejected through the ordinary
/// <see cref="IUserContext.HasRole"/>/<see cref="IUserContext.HasPermission"/>/absent-<c>AuthTime</c>/
/// empty-<c>AuthenticationMethods</c> false paths — every unauthenticated/non-human
/// <see cref="IUserContext"/> implementation shipped in <c>12.Security</c>
/// (<c>AnonymousUserContext</c>, and <c>SystemUserContext</c>'s own <c>HasRole</c>/
/// <c>HasPermission</c>) hardcodes those members to <see langword="false"/>, so no dedicated
/// <c>IsAuthenticated</c> branch is needed here.
/// </para>
/// <para>
/// <see cref="IClock"/> is resolved from <see cref="HttpContext.RequestServices"/> lazily — only
/// when a <see cref="RequireFreshAuthenticationAttribute"/> is actually present on the endpoint —
/// so an endpoint using only <see cref="RequireRoleAttribute"/>/<see cref="RequirePermissionAttribute"/>/
/// <see cref="RequireAuthenticationMethodAttribute"/> never requires <see cref="IClock"/> to be
/// registered in the consumer's container.
/// </para>
/// </remarks>
public sealed partial class AuthorizationRequirementEndpointFilter : IEndpointFilter
{
    private const string ForbiddenErrorCode = "Authorization.Forbidden";
    private const string AuthenticationNotFreshErrorCode = "Authorization.AuthenticationNotFresh";
    private const string AuthenticationMethodNotSatisfiedErrorCode = "Authorization.AuthenticationMethodNotSatisfied";

    private readonly ILogger<AuthorizationRequirementEndpointFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationRequirementEndpointFilter"/> class.
    /// </summary>
    /// <param name="logger">
    /// The logger used to record rejection-path security audit events. When no
    /// <see cref="ILogger{TCategoryName}"/> is registered in the container, a no-op
    /// <see cref="NullLogger{T}"/> is used instead — this filter never fails to construct merely
    /// because logging was not configured.
    /// </param>
    public AuthorizationRequirementEndpointFilter(ILogger<AuthorizationRequirementEndpointFilter>? logger = null)
    {
        _logger = logger ?? NullLogger<AuthorizationRequirementEndpointFilter>.Instance;
    }

    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var metadata = context.HttpContext.GetEndpoint()?.Metadata;
        var roleRequirements = metadata?.GetOrderedMetadata<RequireRoleAttribute>() ?? [];
        var permissionRequirements = metadata?.GetOrderedMetadata<RequirePermissionAttribute>() ?? [];
        var freshAuthenticationRequirement = metadata?.GetMetadata<RequireFreshAuthenticationAttribute>();
        var authenticationMethodRequirement = metadata?.GetMetadata<RequireAuthenticationMethodAttribute>();

        if (roleRequirements.Count == 0
            && permissionRequirements.Count == 0
            && freshAuthenticationRequirement is null
            && authenticationMethodRequirement is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        var userContext = context.HttpContext.RequestServices.GetRequiredService<IUserContext>();

        foreach (var requirement in roleRequirements)
        {
            if (!requirement.Roles.Any(userContext.HasRole))
            {
                return BuildForbiddenResult(
                    context.HttpContext,
                    ForbiddenErrorCode,
                    $"The caller does not hold any of the required role(s): {string.Join(", ", requirement.Roles)}.");
            }
        }

        foreach (var requirement in permissionRequirements)
        {
            if (!requirement.Permissions.Any(userContext.HasPermission))
            {
                return BuildForbiddenResult(
                    context.HttpContext,
                    ForbiddenErrorCode,
                    $"The caller does not hold any of the required permission(s): {string.Join(", ", requirement.Permissions)}.");
            }
        }

        if (freshAuthenticationRequirement is not null)
        {
            var clock = context.HttpContext.RequestServices.GetRequiredService<IClock>();

            if (!userContext.IsAuthenticationFresherThan(freshAuthenticationRequirement.MaxAge, clock.UtcNow))
            {
                return BuildForbiddenResult(
                    context.HttpContext,
                    AuthenticationNotFreshErrorCode,
                    $"The caller's authentication must be no older than {freshAuthenticationRequirement.MaxAge.TotalSeconds} seconds.");
            }
        }

        if (authenticationMethodRequirement is not null
            && !authenticationMethodRequirement.Methods.Any(userContext.WasAuthenticatedWith))
        {
            return BuildForbiddenResult(
                context.HttpContext,
                AuthenticationMethodNotSatisfiedErrorCode,
                $"The caller's authentication must have used one of the required method(s): {string.Join(", ", authenticationMethodRequirement.Methods)}.");
        }

        return await next(context).ConfigureAwait(false);
    }

    private IResult BuildForbiddenResult(HttpContext httpContext, string code, string message)
    {
        var error = Error.Forbidden(code, message);
        var endpointDisplayName = httpContext.GetEndpoint()?.DisplayName ?? "(unknown endpoint)";

        Log.AuthorizationRequirementRejected(_logger, endpointDisplayName, code);

        return Microsoft.AspNetCore.Http.Results.Problem(error.ToProblemDetails(httpContext));
    }

    /// <summary>
    /// Source-generated log messages for <see cref="AuthorizationRequirementEndpointFilter"/>.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 2,
            Level = LogLevel.Warning,
            Message = "Authorization requirement {RequirementCode} rejected the request to endpoint {EndpointDisplayName}.")]
        public static partial void AuthorizationRequirementRejected(ILogger logger, string endpointDisplayName, string requirementCode);
    }
}
