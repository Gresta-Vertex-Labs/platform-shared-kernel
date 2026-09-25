using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
internal sealed partial class RequestContextMiddleware(RequestDelegate next, ILogger<RequestContextMiddleware> logger)
{
    /// <summary>Runs the rest of the request inside the request's context scope.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string correlationId = ResolveCorrelationId(context);

        Activity.Current?.SetBaggage(WellKnownBaggageKeys.CorrelationId, correlationId);
        context.Response.OnStarting(static state =>
        {
            var (response, id) = ((HttpResponse, string))state;
            response.Headers[WellKnownHeaders.CorrelationId] = id;
            return Task.CompletedTask;
        }, (context.Response, correlationId));

        using (RequestContextScope.Begin(new HttpRequestContext(context, correlationId)))
        {
            await next(context).ConfigureAwait(false);
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
