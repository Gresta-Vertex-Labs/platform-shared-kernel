using SharedKernel.Application.Context;

namespace SharedKernel.Messaging.MassTransit.Context;

/// <summary>
/// Holds the <c>IRequestContext</c> the service had registered before messaging replaced the
/// registration, so <see cref="MessageAwareRequestContext"/> can still fall back to it outside a
/// consume.
/// </summary>
/// <remarks>
/// <para>
/// A distinct service type rather than the interface itself, because the composite <em>is</em> the
/// registered <c>IRequestContext</c> — injecting that interface into it would resolve the composite
/// again and stack-overflow on first use.
/// </para>
/// <para>
/// Registered with the original registration's own lifetime, so a scoped HTTP-backed context is
/// still created once per request and a singleton is still created once.
/// </para>
/// </remarks>
internal sealed class HostRequestContextSource
{
    /// <summary>Initialises the holder.</summary>
    /// <param name="requestContext">The context produced by the service's original registration.</param>
    public HostRequestContextSource(IRequestContext requestContext)
    {
        RequestContext = requestContext;
    }

    /// <summary>Gets the service's own request context.</summary>
    public IRequestContext RequestContext { get; }
}
