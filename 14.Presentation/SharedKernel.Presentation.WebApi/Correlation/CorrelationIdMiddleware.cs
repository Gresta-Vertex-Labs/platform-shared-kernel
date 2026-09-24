using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.WebApi.Correlation;

/// <summary>
/// Resolves the correlation id of every request — REST, SignalR and gRPC alike, since they share the pipeline — and
/// makes it available to logs, traces, outgoing calls and the response.
/// </summary>
/// <remarks>
/// A valid inbound <c>X-Correlation-Id</c> is kept. A missing or invalid one is replaced by the current trace id, so
/// logs and traces share one id, or by a new identifier when there is no trace. The id is stored for
/// <see cref="CorrelationIdHttpContextExtensions.GetCorrelationId"/>, added to <see cref="Activity"/> baggage under
/// <see cref="WellKnownBaggageKeys.CorrelationId"/> and written back as a response header on every response,
/// error responses included. An invalid inbound value is never logged, only its length.
/// </remarks>
internal sealed partial class CorrelationIdMiddleware
{
    private static readonly TimeSpan CustomPatternTimeout = TimeSpan.FromMilliseconds(100);

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    private readonly int _maxLength;
    private readonly Regex _pattern;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger, IOptions<SharedKernelWebApiOptions> options)
    {
        _next = next;
        _logger = logger;

        var settings = options.Value.CorrelationId;
        _maxLength = settings.MaxLength;
        _pattern = string.Equals(settings.AllowedCharacterPattern, WebApiCorrelationIdOptions.DefaultAllowedCharacterPattern, StringComparison.Ordinal)
            ? DefaultPattern()
            : new Regex(settings.AllowedCharacterPattern, RegexOptions.CultureInvariant | RegexOptions.Compiled, CustomPatternTimeout);
    }

    public Task InvokeAsync(HttpContext context)
    {
        var correlationId = Resolve(context);

        CorrelationIdHttpContextExtensions.Set(context, correlationId);
        Activity.Current?.SetBaggage(WellKnownBaggageKeys.CorrelationId, correlationId);

        // Registered before the rest of the pipeline runs, so the header is written even when an exception handler or
        // a short-circuiting middleware produces the response.
        context.Response.OnStarting(
            static state =>
            {
                var (httpContext, id) = ((HttpContext, string))state;
                httpContext.Response.Headers[WellKnownHeaders.CorrelationId] = id;
                return Task.CompletedTask;
            },
            (context, correlationId));

        return _next(context);
    }

    private string Resolve(HttpContext context)
    {
        var values = context.Request.Headers[WellKnownHeaders.CorrelationId];

        if (values.Count == 1 && values[0] is { Length: > 0 } inbound)
        {
            if (IsValid(inbound))
            {
                return inbound;
            }

            Log.CorrelationIdRejected(_logger, inbound.Length);
        }
        else if (values.Count > 1)
        {
            Log.CorrelationIdRejected(_logger, values.ToString().Length);
        }

        var assigned = NewCorrelationId();
        Log.CorrelationIdAssigned(_logger, assigned);
        return assigned;
    }

    private bool IsValid(string value)
    {
        // The length bound comes first so the pattern only ever sees short input; control characters are refused
        // outright because '$' in a pattern also matches before a trailing line feed.
        if (value.Length > _maxLength || value.AsSpan().ContainsAnyInRange('\0', '\u001f') || value.Contains('\u007f', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return _pattern.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string NewCorrelationId()
    {
        var activity = Activity.Current;

        return activity is { IdFormat: ActivityIdFormat.W3C } && activity.TraceId != default
            ? activity.TraceId.ToHexString()
            : Guid.NewGuid().ToString("N");
    }

    [GeneratedRegex(WebApiCorrelationIdOptions.DefaultAllowedCharacterPattern, RegexOptions.CultureInvariant)]
    private static partial Regex DefaultPattern();

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 0,
            Level = LogLevel.Debug,
            Message = "Assigned correlation id {CorrelationId} to a request that did not send a valid one.")]
        public static partial void CorrelationIdAssigned(ILogger logger, string correlationId);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 6,
            Level = LogLevel.Warning,
            Message = "Rejected an inbound correlation id of length {CorrelationIdLength} that failed validation.")]
        public static partial void CorrelationIdRejected(ILogger logger, int correlationIdLength);
    }
}
