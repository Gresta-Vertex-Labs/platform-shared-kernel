using System.Diagnostics;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>Removes the W3C baggage a caller sent from the request's <see cref="Activity"/>.</summary>
/// <remarks>
/// ASP.NET Core copies the <c>baggage</c> request header onto the request activity before any middleware runs. From
/// there it flows onward with every outgoing call and onto log records, so an unauthenticated caller could plant
/// values — a tenant id, a user id — that downstream code trusts. Only the activity's own items are removed; the
/// request header itself is left as sent. <see cref="RequestContextMiddleware"/> calls this before it adds the
/// correlation id, unless <see cref="RequestContextOptions.TrustInboundBaggage"/> is set.
/// </remarks>
internal static class InboundBaggage
{
    /// <summary>Removes every baggage item of <paramref name="activity"/>.</summary>
    /// <param name="activity">The request's activity, or <see langword="null"/>.</param>
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
