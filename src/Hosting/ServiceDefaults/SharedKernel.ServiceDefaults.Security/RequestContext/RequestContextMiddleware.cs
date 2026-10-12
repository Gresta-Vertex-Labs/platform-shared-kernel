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
/// <b>Headers.</b> The request-context headers are parsed once, through
/// <see cref="RequestContextPropagation.ReadHeaders{TCarrier}"/>, and only the correlation id is used. The parsed
/// tenant, actor and client are never applied to the context, a span tag, a log or baggage: an inbound HTTP header is
/// forgeable by any external caller.
/// </para>
/// <para>
/// <b>Server span.</b> The ambient ASP.NET Core server span (when something listens to it) is tagged with
/// <see cref="WellKnownTagKeys.CorrelationId"/> here, and with <see cref="WellKnownTagKeys.TenantId"/> from the
/// credential-asserted tenant unless tenant resolution already tagged the tenant it resolved. This middleware starts
/// no span and runs before <c>UseExceptionHandler()</c>, so a handled exception cannot remove the tags.
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

        // The ambient server span (the ASP.NET Core instrumentation's activity, when something listens). Tagged here,
        // before UseExceptionHandler() runs, with the validated, platform-made id only, never the raw inbound header.
        Activity? serverSpan = Activity.Current;
        serverSpan?.SetBaggage(WellKnownBaggageKeys.CorrelationId, correlationId);
        if (serverSpan is { IsAllDataRequested: true })
            serverSpan.SetTag(WellKnownTagKeys.CorrelationId, correlationId);
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

                // The credential-asserted tenant, only when tenant resolution (SharedKernel.MultiTenancy) did not
                // already tag the span with the tenant it resolved. Without that package this is the only writer.
                if (serverSpan is { IsAllDataRequested: true }
                    && requestContext.TenantId is { } assertedTenant
                    && serverSpan.GetTagItem(WellKnownTagKeys.TenantId) is null)
                {
                    serverSpan.SetTag(WellKnownTagKeys.TenantId, assertedTenant.ToString());
                }
            }
        }
    }

    private string ResolveCorrelationId(HttpContext context)
    {
        // The one parser of the request-context headers. Only the correlation id is taken from it: the tenant, actor
        // and client it also parses come from a forgeable inbound header and are deliberately never applied.
        PropagatedRequestContext inbound = RequestContextPropagation.ReadHeaders(
            context.Request.Headers,
            static (headers, name) => headers.TryGetValue(name, out var values) ? values.ToString() : null,
            createCorrelationId: false);

        if (inbound.CorrelationId is { } valid)
            return valid;

        // Not accepted: absent or invalid. Only the raw header's presence and length select the message.
        string supplied = context.Request.Headers[WellKnownHeaders.CorrelationId].ToString();
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
