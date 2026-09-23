using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>
/// Enforces the authorization attributes on a hub method that SignalR itself skips — <see cref="RequirePermissionAttribute"/>,
/// <see cref="RequireRoleAttribute"/>, <see cref="RequireFreshAuthenticationAttribute"/>,
/// <see cref="RequireAuthenticationMethodAttribute"/> and any other <see cref="IAuthorizeData"/> that is not an
/// <see cref="AuthorizeAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// SignalR authorizes a hub method only by the <see cref="AuthorizeAttribute"/>s on it (its hub dispatcher collects
/// <c>GetCustomAttributes&lt;AuthorizeAttribute&gt;()</c>, verified against ASP.NET Core 10.0.11), so the WebApi
/// attributes, which implement <see cref="IAuthorizeData"/> without deriving from it, would be ignored there and the
/// method would run for anyone who may connect. This filter evaluates them the way SignalR evaluates its own: the
/// policies combined through the registered <see cref="IAuthorizationPolicyProvider"/>, authorized by
/// <see cref="IAuthorizationService"/> for the connection's user with the <see cref="HubInvocationContext"/> as the
/// resource. <see cref="AuthorizeAttribute"/>s are left to SignalR, which checks them before any filter runs.
/// Attributes on the hub class need no filter: <c>MapHub&lt;T&gt;()</c> copies them onto the endpoint, so
/// <c>UseAuthorization()</c> checks them when the connection is opened.
/// </para>
/// <para>
/// A refused invocation never reaches the hub method. Like an HTTP refusal it carries
/// <c>unauthorized.default</c> (no authenticated user), <c>unauthorized.step_up_required</c> (only freshness or
/// authentication-method requirements failed) or <c>forbidden.insufficient_permission</c>, whose message never names
/// what was required, and is logged at Warning without principal data.
/// </para>
/// </remarks>
internal sealed partial class HubMethodAuthorizationFilter : IHubFilter
{
    private const string UnauthorizedMessage = "Authentication is required to access this resource.";

    private const string ForbiddenMessage = "You are not permitted to perform this operation.";

    private const string StepUpMessage = "This operation requires a more recent or stronger authentication.";

    private static readonly Error Unauthenticated = Error.Unauthorized(ErrorCodes.Unauthorized.Default, UnauthorizedMessage);

    private static readonly Error Forbidden = Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission, ForbiddenMessage);

    private static readonly Error StepUpRequired = Error.Unauthorized(PresentationErrorCodes.StepUpRequired, StepUpMessage);

    private readonly ConcurrentDictionary<MethodInfo, IAuthorizeData[]> _requirements = new();
    private readonly ILogger<HubMethodAuthorizationFilter> _logger;

    /// <summary>Initializes a new instance of the <see cref="HubMethodAuthorizationFilter"/> class.</summary>
    /// <param name="logger">The logger; a missing logging registration never breaks authorization.</param>
    public HubMethodAuthorizationFilter(ILogger<HubMethodAuthorizationFilter>? logger = null)
    {
        _logger = logger ?? NullLogger<HubMethodAuthorizationFilter>.Instance;
    }

    /// <inheritdoc />
    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var authorizeData = _requirements.GetOrAdd(invocationContext.HubMethod, FindRequirementsSignalRSkips);

        return authorizeData.Length == 0
            ? next(invocationContext)
            : AuthorizeAndInvokeAsync(invocationContext, next, authorizeData);
    }

    private static IAuthorizeData[] FindRequirementsSignalRSkips(MethodInfo hubMethod) =>
        [.. hubMethod.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Where(data => data is not AuthorizeAttribute)];

    private async ValueTask<object?> AuthorizeAndInvokeAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next,
        IAuthorizeData[] authorizeData)
    {
        var services = invocationContext.ServiceProvider;
        var policyProvider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var user = invocationContext.Context.User ?? new ClaimsPrincipal(new ClaimsIdentity());

        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData).ConfigureAwait(false);
        if (policy is null)
        {
            return await next(invocationContext).ConfigureAwait(false);
        }

        var result = await authorization.AuthorizeAsync(user, invocationContext, policy).ConfigureAwait(false);
        if (result.Succeeded)
        {
            return await next(invocationContext).ConfigureAwait(false);
        }

        var error = await ClassifyAsync(user, result, invocationContext, authorizeData, policyProvider, authorization).ConfigureAwait(false);

        Log.AuthorizationRefused(_logger, invocationContext.Hub.GetType().Name, invocationContext.HubMethodName, error.Code);
        throw new HubException(HubErrorMessage.For(error, invocationContext.Context));
    }

    // The same answers the HTTP result handler gives: no authenticated identity is a challenge; a signed-in caller is
    // asked to authenticate again only when every attribute that failed is a freshness or authentication-method one,
    // and never when a handler failed the caller outright (Fail() — for instance no IUserContextMapper understands
    // it), which authenticating again could not fix. The per-attribute evaluation runs only on this refusal path.
    private static async Task<Error> ClassifyAsync(
        ClaimsPrincipal user,
        AuthorizationResult result,
        HubInvocationContext resource,
        IAuthorizeData[] authorizeData,
        IAuthorizationPolicyProvider policyProvider,
        IAuthorizationService authorization)
    {
        if (!user.Identities.Any(identity => identity.IsAuthenticated))
        {
            return Unauthenticated;
        }

        if (result.Failure is null || result.Failure.FailCalled)
        {
            return Forbidden;
        }

        var anyFailed = false;

        foreach (var data in authorizeData)
        {
            var single = await AuthorizationPolicy.CombineAsync(policyProvider, [data]).ConfigureAwait(false);
            if (single is null || (await authorization.AuthorizeAsync(user, resource, single).ConfigureAwait(false)).Succeeded)
            {
                continue;
            }

            if (data is not (RequireFreshAuthenticationAttribute or RequireAuthenticationMethodAttribute))
            {
                return Forbidden;
            }

            anyFailed = true;
        }

        return anyFailed ? StepUpRequired : Forbidden;
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 105,
            Level = LogLevel.Warning,
            Message = "Authorization refused an invocation of hub method {HubName}.{HubMethodName} with {ErrorCode}.")]
        public static partial void AuthorizationRefused(ILogger logger, string hubName, string hubMethodName, string errorCode);
    }
}
