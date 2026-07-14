using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.WebApi.Middleware;

/// <summary>
/// Resolves and propagates the inbound correlation identifier for the current request.
/// </summary>
/// <remarks>
/// <para>
/// Reads the <c>"X-Correlation-Id"</c> request header; generates <c>Guid.NewGuid("N")</c> when
/// absent or whitespace. Stores the resolved value in
/// <c>HttpContext.Items["CorrelationId"]</c> and calls
/// <c>Activity.Current?.SetBaggage("correlation.id", value)</c> so OTel spans and
/// <c>11.Communication</c>'s outbound correlation-id delegating handler can propagate the same
/// identifier end-to-end. Always writes the resolved value back as a response header, including
/// on early pipeline short-circuits — this requires the middleware to be registered first, before
/// exception handling.
/// </para>
/// <para>
/// The <c>correlation.id</c> baggage key and the <c>HttpContext.Items["CorrelationId"]</c>
/// storage key are this domain's own contract (confirmed P-192, WO-031) — not borrowed from
/// <c>13.ServiceDefaults</c>.
/// </para>
/// </remarks>
public sealed partial class CorrelationIdMiddleware
{
    /// <summary>The request/response header name carrying the correlation identifier.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>The <see cref="HttpContext.Items"/> key the resolved correlation identifier is stored under.</summary>
    public const string ItemsKey = "CorrelationId";

    /// <summary>The <see cref="Activity"/> baggage key the resolved correlation identifier is propagated under.</summary>
    public const string BaggageKey = "correlation.id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    /// <summary>
    /// Initialises a new <see cref="CorrelationIdMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    /// <param name="logger">The logger used for correlation-id resolution diagnostics.</param>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the correlation identifier for <paramref name="context"/>, propagates it via
    /// <see cref="HttpContext.Items"/> and <see cref="Activity"/> baggage, invokes the rest of the
    /// pipeline, and always writes the resolved value back as a response header.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the request has been fully processed.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[ItemsKey] = correlationId;
        Activity.Current?.SetBaggage(BaggageKey, correlationId);

        // Register before next(): OnStarting fires even when downstream middleware short-circuits
        // the pipeline (e.g., the exception handler writing a response directly), guaranteeing the
        // response header is always set.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var headerValue))
        {
            var value = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        var generated = Guid.NewGuid().ToString("N");
        Log.CorrelationIdGenerated(_logger, generated);
        return generated;
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 0,
            Level = LogLevel.Debug,
            Message = "Generated new correlation id {CorrelationId} for inbound request.")]
        public static partial void CorrelationIdGenerated(ILogger logger, string correlationId);
    }
}
