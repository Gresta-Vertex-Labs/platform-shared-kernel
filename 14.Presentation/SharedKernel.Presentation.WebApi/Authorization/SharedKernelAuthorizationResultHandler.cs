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
/// Wraps ASP.NET Core's authorization result handling so every refusal carries the platform's problem body:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Not signed in: the scheme's own challenge (its <c>WWW-Authenticate</c> header is kept) and 401 <c>unauthorized.default</c>.</item>
///   <item>Signed in, but the only unmet requirements are freshness or authentication-method ones: 401 with an RFC 9470
///   <c>insufficient_user_authentication</c> challenge (plus <c>max_age</c> for freshness) and <c>unauthorized.step_up_required</c>.</item>
///   <item>Signed in but not permitted: 403 <c>forbidden.insufficient_permission</c>. The message never names the
///   roles or permissions required.</item>
/// </list>
/// A gRPC call gets the framework's behavior unchanged — gRPC maps HTTP 401 and 403 to its own status codes — and
/// no body. Every refusal is logged at Warning with the endpoint name and code, never with principal data.
/// </remarks>
internal sealed partial class SharedKernelAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private const string DefaultChallengeScheme = "Bearer";

    private const string UnauthorizedMessage = "Authentication is required to access this resource.";

    private const string ForbiddenMessage = "You are not permitted to perform this operation.";

    private const string StepUpMessage = "This operation requires a more recent or stronger authentication.";

    private const string FreshnessDescription = "More recent authentication is required";

    private const string MethodDescription = "A stronger authentication method is required";

    private readonly AuthorizationMiddlewareResultHandler _inner = new();
    private readonly ILogger<SharedKernelAuthorizationResultHandler> _logger;

    public SharedKernelAuthorizationResultHandler(ILogger<SharedKernelAuthorizationResultHandler>? logger = null)
    {
        _logger = logger ?? NullLogger<SharedKernelAuthorizationResultHandler>.Instance;
    }

    /// <inheritdoc />
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded || (!authorizeResult.Challenged && !authorizeResult.Forbidden) || RequestFacts.IsGrpcRequest(context))
        {
            await _inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
            return;
        }

        if (authorizeResult.Forbidden && TryGetStepUp(authorizeResult.AuthorizationFailure, out var maxAge))
        {
            await WriteStepUpAsync(context, maxAge).ConfigureAwait(false);
            return;
        }

        if (authorizeResult.Challenged)
        {
            await ChallengeAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
            return;
        }

        await ForbidAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
    }

    private async Task ChallengeAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (await HasSchemeAsync(context, policy, forbid: false).ConfigureAwait(false))
        {
            // The scheme's challenge sets 401 and its WWW-Authenticate header, or redirects (a cookie scheme).
            await _inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.Append(HeaderNames.WWWAuthenticate, DefaultChallengeScheme);
        }

        await WriteProblemAsync(
                context,
                StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthorized.Default,
                UnauthorizedMessage)
            .ConfigureAwait(false);
    }

    private async Task ForbidAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (await HasSchemeAsync(context, policy, forbid: true).ConfigureAwait(false))
        {
            await _inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        }

        await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden.InsufficientPermission,
                ForbiddenMessage)
            .ConfigureAwait(false);
    }

    private async Task WriteStepUpAsync(HttpContext context, TimeSpan? maxAge)
    {
        var challenge = $"{GetRequestScheme(context)} error=\"insufficient_user_authentication\", error_description=\"{(maxAge is null ? MethodDescription : FreshnessDescription)}\"";
        if (maxAge is { } age)
        {
            challenge += string.Create(CultureInfo.InvariantCulture, $", max_age=\"{(long)age.TotalSeconds}\"");
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.Append(HeaderNames.WWWAuthenticate, challenge);

        await WriteProblemAsync(
                context,
                StatusCodes.Status401Unauthorized,
                PresentationErrorCodes.StepUpRequired,
                StepUpMessage)
            .ConfigureAwait(false);
    }

    private Task WriteProblemAsync(HttpContext context, int statusCode, string code, string message)
    {
        Log.AuthorizationRejected(_logger, RequestFacts.GetEndpointDisplayName(context), code);

        // The scheme may have answered differently (a cookie scheme redirects to its sign-in page) or written the
        // response itself; only a plain 401 or 403 gets the problem body.
        if (context.Response.HasStarted || context.Response.StatusCode != statusCode)
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
    // (a handler called Fail) or any other unmet requirement is a refusal instead.
    private static bool TryGetStepUp(AuthorizationFailure? failure, out TimeSpan? maxAge)
    {
        maxAge = null;

        if (failure is null || failure.FailCalled)
        {
            return false;
        }

        var failed = failure.FailedRequirements.ToArray();
        if (failed.Length == 0 || !failed.All(requirement => requirement is SharedKernelRequirement { IsStepUp: true }))
        {
            return false;
        }

        var ages = failed.OfType<FreshAuthenticationRequirement>().Select(requirement => requirement.MaxAge).ToArray();
        maxAge = ages.Length == 0 ? null : ages.Min();
        return true;
    }

    // RFC 9470 answers in the scheme the client used (DPoP or Bearer); Bearer when the request carried no credentials.
    private static string GetRequestScheme(HttpContext context)
    {
        var authorization = context.Request.Headers[HeaderNames.Authorization].ToString();
        var end = authorization.IndexOf(' ', StringComparison.Ordinal);
        var scheme = end > 0 ? authorization[..end] : authorization;

        return scheme.Length > 0 && scheme.All(IsTokenCharacter) ? scheme : DefaultChallengeScheme;
    }

    private static bool IsTokenCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 2,
            Level = LogLevel.Warning,
            Message = "Authorization refused the request to {EndpointDisplayName} with {ErrorCode}.")]
        public static partial void AuthorizationRejected(ILogger logger, string endpointDisplayName, string errorCode);
    }
}
