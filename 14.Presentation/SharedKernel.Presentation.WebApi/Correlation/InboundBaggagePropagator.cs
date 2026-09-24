using System.Diagnostics;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.WebApi.Options;

namespace SharedKernel.Presentation.WebApi.Correlation;

/// <summary>
/// Decorates the <see cref="DistributedContextPropagator"/> ASP.NET Core hosting reads requests with, so it takes no
/// baggage from the caller unless <c>TrustInboundBaggage</c> is set; trace context is read as before.
/// </summary>
/// <remarks>
/// Hosting copies the <c>baggage</c> header onto the request activity and logs "Request starting" before any
/// middleware runs, so removing the items in the pipeline (<see cref="InboundBaggage"/>) comes too late for that
/// first log record. Refusing them here closes that gap; the pipeline step still removes anything that reached the
/// activity another way. Outgoing propagation (<see cref="Inject"/>) is unchanged.
/// </remarks>
internal sealed class InboundBaggagePropagator : DistributedContextPropagator
{
    private readonly DistributedContextPropagator _inner;
    private readonly IOptions<SharedKernelWebApiOptions> _options;

    public InboundBaggagePropagator(DistributedContextPropagator inner, IOptions<SharedKernelWebApiOptions> options)
    {
        _inner = inner;
        _options = options;
    }

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
