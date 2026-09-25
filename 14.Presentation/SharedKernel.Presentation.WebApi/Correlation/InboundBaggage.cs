using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace SharedKernel.Presentation.WebApi.Correlation;

/// <summary>
/// Removes the W3C baggage a caller sent from the request's <see cref="Activity"/>, the first step of the pipeline
/// unless <c>TrustInboundBaggage</c> is set.
/// </summary>
/// <remarks>
/// ASP.NET Core copies the <c>baggage</c> request header onto the request activity before any middleware runs. From
/// there it flows onward with every outgoing call and onto log records, so an unauthenticated caller could plant
/// values — a tenant id, a user id — that downstream code trusts. Only the activity's own items are removed; the
/// request header itself is left as sent, and baggage the service adds later, the correlation id included, is kept.
/// </remarks>
internal static class InboundBaggage
{
    /// <summary>The middleware: removes the inbound baggage, then runs the rest of the pipeline.</summary>
    public static Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        RemoveAll(Activity.Current);
        return next(context);
    }

    /// <summary>Removes every baggage item of <paramref name="activity"/>.</summary>
    public static void RemoveAll(Activity? activity)
    {
        if (activity is null)
        {
            return;
        }

        // One removal per item: a key may appear more than once, and SetBaggage(key, null) removes one occurrence.
        foreach (var item in activity.Baggage.ToArray())
        {
            activity.SetBaggage(item.Key, null);
        }
    }
}
