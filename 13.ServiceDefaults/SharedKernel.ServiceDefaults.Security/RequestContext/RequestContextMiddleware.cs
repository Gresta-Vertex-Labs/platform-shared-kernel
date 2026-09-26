using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>
/// The HTTP inbound adapter: resolves the request's correlation id and runs the rest of the request inside a
/// <see cref="RequestContextScope"/>, so everything downstream — handlers, repositories, outbound REST and gRPC
/// clients, message publishers, log enrichment — reads one caller and one correlation id.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inbound baggage.</b> Unless <see cref="RequestContextOptions.TrustInboundBaggage"/> is set, every W3C baggage
/// item the caller sent is first removed from the request's <see cref="Activity"/> (<see cref="InboundBaggage"/>), so
/// a caller cannot plant a tenant or user id that downstream services and log records would trust. The correlation id
/// is added afterwards, so it is always the platform's own (P-579; this was <c>SharedKernel.Presentation.WebApi</c>'s
/// first pipeline step).
/// </para>
/// <para>
/// <b>Correlation id.</b> The caller's <see cref="WellKnownHeaders.CorrelationId"/> header is kept when
/// <see cref="CorrelationIds.IsValid"/> accepts it; otherwise a new id is created and the rejected value is never
/// logged, echoed or put in baggage. The resolved id is written to the current <see cref="Activity"/>'s baggage
/// under <see cref="WellKnownBaggageKeys.CorrelationId"/> (for log enrichment) and echoed on the response header,
/// through <see cref="HttpResponse.OnStarting(Func{Task})"/> so it is present even on an error response.
/// </para>
/// <para>
/// <b>Caller.</b> The scope holds an <see cref="HttpRequestContext"/>, whose identity members are read lazily from
/// the request's <see cref="SecurityRequestContext"/>; <see cref="HttpRequestContext"/> explains why lazily.
/// </para>
/// <para>
/// <b>Tenant.</b> Without <c>SharedKernel.MultiTenancy</c>, the tenant is the one the caller's credential asserts.
/// With it, <c>TenantResolutionMiddleware</c> (after <c>UseAuthentication()</c>) opens a second, inner scope that
/// replaces only the tenant with the one it resolved and keeps this scope's caller and correlation id. That nesting
/// is intentional: this middleware owns the request's scope and correlation id, tenant resolution owns the tenant.
/// </para>
/// </remarks>
internal sealed partial class RequestContextMiddleware(
    RequestDelegate next,
    ILogger<RequestContextMiddleware> logger,
    IOptions<RequestContextOptions>? options = null)
{
    private readonly bool _trustInboundBaggage = options?.Value.TrustInboundBaggage ?? false;

    /// <summary>Runs the rest of the request inside the request's context scope.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Before the correlation id is added, so the only baggage left on the activity is the platform's own.
        if (!_trustInboundBaggage)
        {
            InboundBaggage.RemoveAll(Activity.Current);
        }

        string correlationId = ResolveCorrelationId(context);

        Activity.Current?.SetBaggage(WellKnownBaggageKeys.CorrelationId, correlationId);
        context.Response.OnStarting(static state =>
        {
            var (response, id) = ((HttpResponse, string))state;
            response.Headers[WellKnownHeaders.CorrelationId] = id;
            return Task.CompletedTask;
        }, (context.Response, correlationId));

        var requestContext = new HttpRequestContext(context, correlationId);
        using (RequestContextScope.Begin(requestContext))
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            finally
            {
                // Something may keep this context after the request (a SignalR connection does): fix its caller now,
                // while the request's services still exist.
                requestContext.CaptureCaller();
            }
        }
    }

    private string ResolveCorrelationId(HttpContext context)
    {
        string? supplied = context.Request.Headers[WellKnownHeaders.CorrelationId].ToString();

        if (CorrelationIds.IsValid(supplied))
            return supplied;

        if (!string.IsNullOrWhiteSpace(supplied))
        {
            // Never log the rejected value itself: an unvalidated caller-controlled string is exactly the
            // log-injection vector this check exists for. Its length is enough to investigate.
            LogCorrelationIdRejected(logger, supplied.Length);
        }
        else
        {
            LogCorrelationIdGenerated(logger);
        }

        return CorrelationIds.New();
    }

    [LoggerMessage(
        EventId = 13006,
        Level = LogLevel.Debug,
        Message = "Created a correlation id for an inbound request that carried none.")]
    private static partial void LogCorrelationIdGenerated(ILogger logger);

    [LoggerMessage(
        EventId = 13007,
        Level = LogLevel.Warning,
        Message = "Rejected a caller-supplied correlation id of length {CorrelationIdLength} that failed validation; created a new one.")]
    private static partial void LogCorrelationIdRejected(ILogger logger, int correlationIdLength);
}
