using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.WebApi.Middleware;

/// <summary>
/// Resolves and propagates the inbound correlation identifier for the current request.
/// </summary>
/// <remarks>
/// <para>
/// Reads the <see cref="HeaderName"/> request header; generates <c>Guid.NewGuid("N")</c> when
/// absent, whitespace, or failing the <see cref="CorrelationIdOptions.MaxLength"/>/
/// <see cref="CorrelationIdOptions.AllowedCharacterPattern"/> shape check — the caller-supplied
/// value never reaches <c>HttpContext.Items</c>, <see cref="Activity"/> baggage, or the response
/// header until it has passed this validation. Stores the resolved value in
/// <c>HttpContext.Items["CorrelationId"]</c> and calls
/// <c>Activity.Current?.SetBaggage(BaggageKey, value)</c> so OTel spans and
/// <c>11.Communication</c>'s outbound correlation-id delegating handler can propagate the same
/// identifier end-to-end. Always writes the resolved value back as a response header, including
/// on early pipeline short-circuits — this requires the middleware to be registered first, before
/// exception handling.
/// </para>
/// <para>
/// The <see cref="BaggageKey"/> value and the <c>HttpContext.Items["CorrelationId"]</c>
/// storage key are this domain's own contract (confirmed P-192, WO-031) — not borrowed from
/// <c>13.ServiceDefaults</c>. <see cref="HeaderName"/> and <see cref="BaggageKey"/> are, as of
/// WO-042/P-262, documented forwarding aliases over <c>01.Core</c>'s
/// <see cref="WellKnownHeaders.CorrelationId"/> / <see cref="WellKnownBaggageKeys.CorrelationId"/>
/// — the constants remain here for call-site ergonomics and backward compatibility, but their
/// literal value now originates from <c>01.Core</c> rather than being independently retyped in
/// this domain, closing the drift class flagged by the prior <c>13.ServiceDefaults</c>
/// baggage-key mismatch (DO-07). <see cref="ItemsKey"/> is presentation-local — a
/// <see cref="HttpContext.Items"/> storage key with no cross-service wire meaning — and is
/// explicitly excluded from this sourcing rule.
/// </para>
/// </remarks>
public sealed partial class CorrelationIdMiddleware
{
    /// <summary>
    /// The request/response header name carrying the correlation identifier. Forwards
    /// <c>01.Core</c>'s <see cref="WellKnownHeaders.CorrelationId"/> (WO-042, P-262, D-14) —
    /// never an independently-owned literal.
    /// </summary>
    public const string HeaderName = WellKnownHeaders.CorrelationId;

    /// <summary>The <see cref="HttpContext.Items"/> key the resolved correlation identifier is stored under.</summary>
    /// <remarks>
    /// Presentation-local — not a cross-service wire concept, so it has no <c>01.Core</c>
    /// equivalent and is explicitly out of scope for the WO-042/P-262 forwarding-alias sourcing
    /// rule applied to <see cref="HeaderName"/> and <see cref="BaggageKey"/>.
    /// </remarks>
    public const string ItemsKey = "CorrelationId";

    /// <summary>
    /// The <see cref="Activity"/> baggage key the resolved correlation identifier is propagated
    /// under. Forwards <c>01.Core</c>'s <see cref="WellKnownBaggageKeys.CorrelationId"/>
    /// (WO-042, P-262, D-14) — never an independently-owned literal.
    /// </summary>
    public const string BaggageKey = WellKnownBaggageKeys.CorrelationId;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    private readonly CorrelationIdOptions _options;
    private readonly Regex _allowedCharacterRegex;

    /// <summary>
    /// Initialises a new <see cref="CorrelationIdMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    /// <param name="logger">The logger used for correlation-id resolution diagnostics.</param>
    /// <param name="options">
    /// The caller-supplied-value shape validation configuration. When no
    /// <see cref="CorrelationIdOptions"/> is registered in the container (e.g. a host that calls
    /// <see cref="CorrelationIdExtensions.UseSharedKernelCorrelationId"/> without ever calling
    /// <see cref="CorrelationIdExtensions.AddSharedKernelCorrelationId"/>), the platform-default
    /// options are used instead — this validation is never silently skipped.
    /// </param>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger, CorrelationIdOptions? options = null)
    {
        _next = next;
        _logger = logger;
        _options = options ?? new CorrelationIdOptions();
        _allowedCharacterRegex = new Regex(_options.AllowedCharacterPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
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
            {
                if (IsValidFormat(value))
                {
                    return value;
                }

                // Never log the raw rejected value itself — an unvalidated, caller-controlled
                // string is exactly the log-injection vector this validation defends against.
                // Length alone is sufficient audit context.
                Log.CorrelationIdRejected(_logger, value.Length);
            }
        }

        var generated = Guid.NewGuid().ToString("N");
        Log.CorrelationIdGenerated(_logger, generated);
        return generated;
    }

    private bool IsValidFormat(string value)
        => value.Length <= _options.MaxLength && _allowedCharacterRegex.IsMatch(value);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 0,
            Level = LogLevel.Debug,
            Message = "Generated new correlation id {CorrelationId} for inbound request.")]
        public static partial void CorrelationIdGenerated(ILogger logger, string correlationId);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 6,
            Level = LogLevel.Warning,
            Message = "Rejected a caller-supplied correlation id of length {CorrelationIdLength} failing format validation; generating a new one instead.")]
        public static partial void CorrelationIdRejected(ILogger logger, int correlationIdLength);
    }
}
