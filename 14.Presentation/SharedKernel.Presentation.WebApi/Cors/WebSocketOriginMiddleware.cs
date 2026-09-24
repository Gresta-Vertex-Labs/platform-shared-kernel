using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.WebApi.Cors;

/// <summary>
/// Refuses a WebSocket request whose <c>Origin</c> the platform's CORS policy does not allow, with 403
/// <c>forbidden.origin_not_allowed</c>. Added after <c>UseCors</c> only when origins are configured.
/// </summary>
/// <remarks>
/// Browsers enforce CORS on fetch and XHR but not on WebSockets: any page can open a WebSocket to the service, and
/// the browser sends the user's cookies with it (cross-site WebSocket hijacking). A SignalR hub's WebSocket transport
/// is covered this way, while its negotiate request and other transports are ordinary CORS requests. A request
/// without <c>Origin</c> does not come from a browser page and is let through, as CORS itself would.
/// </remarks>
internal sealed partial class WebSocketOriginMiddleware
{
    private const string WebSocketProtocol = "websocket";

    private const string OriginNotAllowedMessage = "WebSocket connections from this origin are not allowed.";

    private readonly RequestDelegate _next;
    private readonly ICorsService _corsService;
    private readonly ICorsPolicyProvider _policyProvider;
    private readonly ILogger<WebSocketOriginMiddleware> _logger;

    public WebSocketOriginMiddleware(
        RequestDelegate next,
        ICorsService corsService,
        ICorsPolicyProvider policyProvider,
        ILogger<WebSocketOriginMiddleware> logger)
    {
        _next = next;
        _corsService = corsService;
        _policyProvider = policyProvider;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsWebSocketRequest(context) || StringValues.IsNullOrEmpty(context.Request.Headers.Origin))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var policy = await _policyProvider.GetPolicyAsync(context, CorsPolicyConfiguration.PolicyName).ConfigureAwait(false);
        if (policy is null || _corsService.EvaluatePolicy(context, policy).IsOriginAllowed)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        Log.WebSocketOriginRefused(_logger, RequestFacts.GetEndpointDisplayName(context));

        await ProblemResponseWriter.WriteAsync(
                context,
                ProblemFactory.ForPresentation(context, StatusCodes.Status403Forbidden, PresentationErrorCodes.OriginNotAllowed, OriginNotAllowedMessage))
            .ConfigureAwait(false);
    }

    // The WebSockets middleware, which would answer this, runs later (SignalR adds it inside the hub endpoint), so the
    // handshake is recognized here from the request itself: an HTTP/1.1 upgrade or an HTTP/2 and HTTP/3 extended CONNECT.
    private static bool IsWebSocketRequest(HttpContext context)
    {
        if (context.Features.Get<IHttpExtendedConnectFeature>() is { IsExtendedConnect: true } extendedConnect)
        {
            return string.Equals(extendedConnect.Protocol, WebSocketProtocol, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var upgrade in context.Request.Headers.Upgrade)
        {
            if (upgrade?.Contains(WebSocketProtocol, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 13,
            Level = LogLevel.Warning,
            Message = "Refused a WebSocket request to {EndpointDisplayName} from an origin the CORS policy does not allow.")]
        public static partial void WebSocketOriginRefused(ILogger logger, string endpointDisplayName);
    }
}
