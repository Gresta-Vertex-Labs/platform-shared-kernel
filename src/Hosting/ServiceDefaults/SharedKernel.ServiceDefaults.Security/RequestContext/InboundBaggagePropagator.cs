using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>
/// Decorates the <see cref="DistributedContextPropagator"/> ASP.NET Core hosting reads requests with, so it takes no
/// baggage from the caller unless <see cref="RequestContextOptions.TrustInboundBaggage"/> is set; trace context is read
/// as before.
/// </summary>
/// <remarks>
/// Hosting copies the <c>baggage</c> header onto the request activity and logs "Request starting" before any
/// middleware runs, so removing the items in the pipeline (<see cref="InboundBaggage"/>) comes too late for that first
/// log record. Refusing them here closes that gap; the middleware still removes anything that reached the activity
/// another way. Outgoing propagation (<see cref="Inject"/>) is unchanged.
/// </remarks>
internal sealed class InboundBaggagePropagator : DistributedContextPropagator
{
    private readonly DistributedContextPropagator _inner;
    private readonly IOptions<RequestContextOptions> _options;

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    /// <param name="inner">The propagator hosting would otherwise use.</param>
    /// <param name="options">The request context settings.</param>
    public InboundBaggagePropagator(DistributedContextPropagator inner, IOptions<RequestContextOptions> options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);

        _inner = inner;
        _options = options;
    }

    /// <summary>Gets the propagator this one decorates.</summary>
    public DistributedContextPropagator Inner => _inner;

    /// <inheritdoc />
    public override IReadOnlyCollection<string> Fields => _inner.Fields;

    /// <inheritdoc />
    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter) =>
        _inner.Inject(activity, carrier, setter);

    /// <inheritdoc />
    public override void ExtractTraceIdAndState(object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState) =>
        _inner.ExtractTraceIdAndState(carrier, getter, out traceId, out traceState);

    /// <inheritdoc />
    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter) =>
        _options.Value.TrustInboundBaggage ? _inner.ExtractBaggage(carrier, getter) : null;
}
