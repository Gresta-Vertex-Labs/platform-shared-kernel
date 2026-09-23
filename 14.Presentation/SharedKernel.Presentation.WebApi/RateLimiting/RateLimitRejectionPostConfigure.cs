using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.WebApi.RateLimiting;

/// <summary>
/// Gives rate-limit rejections the platform's body: when a service configures rate limiting without its own
/// <see cref="RateLimiterOptions.OnRejected"/>, a rejected request is answered 429 <c>rate_limit.exceeded</c> with
/// <c>Retry-After</c> when the limiter suggests one.
/// </summary>
internal sealed partial class RateLimitRejectionPostConfigure : IPostConfigureOptions<RateLimiterOptions>
{
    private const string TooManyRequestsMessage = "Too many requests. Retry later.";

    private readonly ILogger<RateLimitRejectionPostConfigure> _logger;

    public RateLimitRejectionPostConfigure(ILogger<RateLimitRejectionPostConfigure> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A service that wrote its own OnRejected keeps it.
        options.OnRejected ??= WriteRejectionAsync;
    }

    private async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        Log.RateLimitRejected(_logger, RequestFacts.GetEndpointDisplayName(httpContext));

        // The lease is null when the request was cancelled while queued.
        if (context.Lease is { } lease && lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers[HeaderNames.RetryAfter] = ProblemResponseWriter.ToSeconds(retryAfter);
        }

        if (httpContext.Response.HasStarted)
        {
            return;
        }

        if (RequestFacts.IsGrpcRequest(httpContext))
        {
            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        await ProblemResponseWriter.WriteAsync(
                httpContext,
                ProblemFactory.ForPresentation(
                    httpContext,
                    StatusCodes.Status429TooManyRequests,
                    PresentationErrorCodes.RateLimitExceeded,
                    TooManyRequestsMessage))
            .ConfigureAwait(false);
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 5,
            Level = LogLevel.Warning,
            Message = "Rate limiting rejected a request to {EndpointDisplayName}.")]
        public static partial void RateLimitRejected(ILogger logger, string endpointDisplayName);
    }
}
