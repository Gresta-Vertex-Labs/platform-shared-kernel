using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Decorates the authorization result handler registered before the platform's — the service's own, or ASP.NET
/// Core's default — so every refusal carries the platform's answer:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Not signed in: the scheme's own challenge (its <c>WWW-Authenticate</c> header is kept) and 401
///   <c>unauthorized.default</c>. With no authentication scheme registered at all: 401 with
///   <c>WWW-Authenticate: Bearer</c>.</item>
///   <item>Signed in, but the only unmet requirements are freshness or authentication-method ones: 401 with an RFC 9470
///   <c>insufficient_user_authentication</c> challenge (plus <c>max_age</c>, the smallest maximum age among them, when
///   a freshness requirement or an authentication-method requirement with a maximum age is unmet) and
///   <c>unauthorized.step_up_required</c>. The challenge names <c>DPoP</c> when the request used that scheme, otherwise
///   <c>Bearer</c>.</item>
///   <item>Signed in but not permitted: 403 <c>forbidden.insufficient_permission</c>. The message never names the
///   roles or permissions required.</item>
/// </list>
/// <para>
/// A gRPC call gets the same statuses and headers but never a body: gRPC maps 401 to <c>Unauthenticated</c> and 403
/// to <c>PermissionDenied</c> itself. Successful and unrelated results, and the challenge or forbid of a registered
/// scheme, are passed to the decorated handler. Every refusal is logged at Warning with the endpoint name and code,
/// never with principal data.
/// </para>
/// </remarks>
internal sealed partial class SharedKernelAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private const string BearerScheme = "Bearer";

    private const string DPoPScheme = "DPoP";

    private const string UnauthorizedMessage = "Authentication is required to access this resource.";

    private const string ForbiddenMessage = "You are not permitted to perform this operation.";

    private const string StepUpMessage = "This operation requires a more recent or stronger authentication.";

    private const string FreshnessDescription = "More recent authentication is required";

    private const string MethodDescription = "A stronger authentication method is required";

    private const string RecentMethodDescription = "A recent authentication with a stronger method is required";

    private readonly IAuthorizationMiddlewareResultHandler _inner;
    private readonly ILogger<SharedKernelAuthorizationResultHandler> _logger;

    public SharedKernelAuthorizationResultHandler(
        IAuthorizationMiddlewareResultHandler inner,
        ILogger<SharedKernelAuthorizationResultHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _logger = logger ?? NullLogger<SharedKernelAuthorizationResultHandler>.Instance;
    }

    /// <inheritdoc />
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded || (!authorizeResult.Challenged && !authorizeResult.Forbidden))
        {
            await _inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
            return;
        }

        var writeBody = !RequestFacts.IsGrpcRequest(context);

        if (authorizeResult.Forbidden && TryGetStepUp(authorizeResult.AuthorizationFailure, out var maxAge, out var needsMethod))
        {
            await WriteStepUpAsync(context, maxAge, needsMethod, writeBody).ConfigureAwait(false);
            return;
        }

        if (authorizeResult.Challenged)
        {
            await ChallengeAsync(next, context, policy, authorizeResult, writeBody).ConfigureAwait(false);
            return;
        }

        await ForbidAsync(next, context, policy, authorizeResult, writeBody).ConfigureAwait(false);
    }

    private async Task ChallengeAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult,
        bool writeBody)
    {
        if (await HasSchemeAsync(context, policy, forbid: false).ConfigureAwait(false))
        {
            // The scheme's challenge sets 401 and its WWW-Authenticate header, or redirects (a cookie scheme).
            await _inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
        }
        else
        {
            // Without a scheme the framework's challenge would throw; answer the way a bearer scheme would.
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.Append(HeaderNames.WWWAuthenticate, BearerScheme);
        }

        await RefuseAsync(context, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized.Default, UnauthorizedMessage, writeBody)
            .ConfigureAwait(false);
    }

    private async Task ForbidAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult,
        bool writeBody)
    {
        if (await HasSchemeAsync(context, policy, forbid: true).ConfigureAwait(false))
        {
            await _inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        }

        await RefuseAsync(context, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission, ForbiddenMessage, writeBody)
            .ConfigureAwait(false);
    }

    private Task WriteStepUpAsync(HttpContext context, TimeSpan? maxAge, bool needsMethod, bool writeBody)
    {
        var description = !needsMethod ? FreshnessDescription : maxAge is null ? MethodDescription : RecentMethodDescription;
        var challenge = $"{GetChallengeScheme(context)} error=\"insufficient_user_authentication\", error_description=\"{description}\"";
        if (maxAge is { } age)
        {
            challenge += string.Create(CultureInfo.InvariantCulture, $", max_age=\"{(long)age.TotalSeconds}\"");
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.Append(HeaderNames.WWWAuthenticate, challenge);

        return RefuseAsync(context, StatusCodes.Status401Unauthorized, PresentationErrorCodes.StepUpRequired, StepUpMessage, writeBody);
    }

    private Task RefuseAsync(HttpContext context, int statusCode, string code, string message, bool writeBody)
    {
        Log.AuthorizationRejected(_logger, RequestFacts.GetEndpointDisplayName(context), code);

        // gRPC: the status and headers are the whole answer. Otherwise the scheme may have answered differently (a
        // cookie scheme redirects to its sign-in page) or written the response itself; only a plain 401 or 403 gets
        // the problem body.
        if (!writeBody || context.Response.HasStarted || context.Response.StatusCode != statusCode)
        {
            return Task.CompletedTask;
        }

        return ProblemResponseWriter.WriteAsync(context, ProblemFactory.ForPresentation(context, statusCode, code, message));
    }

    private static async Task<bool> HasSchemeAsync(HttpContext context, AuthorizationPolicy policy, bool forbid)
    {
        if (policy.AuthenticationSchemes.Count > 0)
        {
            return true;
        }

        var schemes = context.RequestServices.GetService<IAuthenticationSchemeProvider>();
        if (schemes is null)
        {
            return false;
        }

        var scheme = forbid
            ? await schemes.GetDefaultForbidSchemeAsync().ConfigureAwait(false)
            : await schemes.GetDefaultChallengeSchemeAsync().ConfigureAwait(false);

        return scheme is not null;
    }

    // Step-up applies when every unmet requirement is a freshness or authentication-method one. An explicit failure
    // (a handler called Fail) or any other unmet requirement is a refusal instead. The challenge asks for the smallest
    // maximum age among them, and says whether a method is missing.
    private static bool TryGetStepUp(AuthorizationFailure? failure, out TimeSpan? maxAge, out bool needsMethod)
    {
        maxAge = null;
        needsMethod = false;

        if (failure is null || failure.FailCalled)
        {
            return false;
        }

        var failed = failure.FailedRequirements.ToArray();
        if (failed.Length == 0 || !failed.All(requirement => requirement is SharedKernelRequirement { IsStepUp: true }))
        {
            return false;
        }

        foreach (var requirement in failed.Cast<SharedKernelRequirement>())
        {
            needsMethod |= requirement is AuthenticationMethodRequirement;
            if (requirement.StepUpMaxAge is { } age && (maxAge is null || age < maxAge))
            {
                maxAge = age;
            }
        }

        return true;
    }

    // RFC 9470 answers in the token scheme the client used. Only the two schemes a step-up can apply to are echoed —
    // DPoP when the request used it, otherwise Bearer — never an arbitrary value taken from the request.
    private static string GetChallengeScheme(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString().AsSpan().TrimStart();

        return authorization.StartsWith(DPoPScheme, StringComparison.OrdinalIgnoreCase)
            && (authorization.Length == DPoPScheme.Length || authorization[DPoPScheme.Length] is ' ' or '\t')
                ? DPoPScheme
                : BearerScheme;
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 2,
            Level = LogLevel.Warning,
            Message = "Authorization refused the request to {EndpointDisplayName} with {ErrorCode}.")]
        public static partial void AuthorizationRejected(ILogger logger, string endpointDisplayName, string errorCode);
    }
}
