using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Decorates OpenTelemetry's default <see cref="TextMapPropagator"/> so that it takes no baggage from an incoming HTTP
/// request. OpenTelemetry's own baggage store, <see cref="Baggage.Current"/>, is then never filled from the
/// <c>baggage</c> header a caller sent. Trace context is still read from the request, every other carrier is read as
/// before, and injection — the service's own outgoing baggage — is unchanged.
/// </summary>
/// <remarks>
/// <para>
/// <b>The path this closes (P-562 X2).</b> OpenTelemetry's ASP.NET Core instrumentation extracts every request with
/// <see cref="Propagators.DefaultTextMapPropagator"/> and assigns the result to <see cref="Baggage.Current"/>, before
/// any middleware runs; the HttpClient and gRPC client instrumentations inject <see cref="Baggage.Current"/> into
/// every outgoing call. With the SDK's default propagator (W3C trace context and W3C baggage), an anonymous caller's
/// <c>baggage: TenantId=…,SubjectId=…</c> therefore reached every downstream service, and — because the outgoing
/// <c>baggage</c> header was already set — displaced the platform's own <see cref="System.Diagnostics.Activity"/>
/// baggage, the correlation id, which .NET would otherwise have sent. <c>SharedKernel.ServiceDefaults.Security</c>'
/// <c>RequestContextOptions.TrustInboundBaggage</c> governs the other store, the request's <see cref="System.Diagnostics.Activity"/>, and
/// does not reach this one.
/// </para>
/// <para>
/// <b>Why only requests.</b> The carrier of an incoming HTTP request (gRPC and SignalR included) is an
/// <see cref="HttpRequest"/>, and only that carrier comes from outside. Other carriers — the workflow headers
/// Temporal's tracing interceptor reads, for one — are written by the platform's own services, so their baggage is
/// kept.
/// </para>
/// <para>
/// <b>Not configurable, not coupled to <c>14.Presentation</c>.</b> Even with <c>TrustInboundBaggage</c> on, a caller's
/// baggage stays out of <see cref="Baggage.Current"/>: <c>14.Presentation</c> then keeps it on the request's
/// <see cref="System.Diagnostics.Activity"/>, which is where the platform reads baggage and what .NET propagates to
/// outgoing HTTP calls.
/// </para>
/// </remarks>
internal sealed class RequestBaggageRefusingPropagator : TextMapPropagator
{
    private readonly TextMapPropagator _inner;

    /// <summary>Wraps <paramref name="inner"/>, which still reads trace context and every non-request carrier.</summary>
    /// <param name="inner">The propagator to decorate.</param>
    public RequestBaggageRefusingPropagator(TextMapPropagator inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Gets the propagator this one decorates.</summary>
    public TextMapPropagator Inner => _inner;

    /// <inheritdoc />
    public override ISet<string>? Fields => _inner.Fields;

    /// <summary>
    /// Makes <see cref="Propagators.DefaultTextMapPropagator"/> a <see cref="RequestBaggageRefusingPropagator"/> over
    /// the propagator in place, unless it already is one or is a plain <see cref="TraceContextPropagator"/>.
    /// </summary>
    /// <remarks>
    /// Runs when the tracer provider is built — before any instrumentation exists — so a propagator the service set
    /// earlier, B3 or Jaeger for example, is decorated rather than replaced. A propagator set after the host started
    /// replaces this protection. A plain <see cref="TraceContextPropagator"/> is left alone: it reads no baggage, and
    /// the instrumentations skip their own extraction and injection for it.
    /// </remarks>
    public static void Install()
    {
        // The SDK assigns its default composite in Sdk's static constructor. Run it now, or it would replace the
        // decorator the first time anything touches Sdk.
        RuntimeHelpers.RunClassConstructor(typeof(Sdk).TypeHandle);

        var current = Propagators.DefaultTextMapPropagator;
        if (current is RequestBaggageRefusingPropagator or TraceContextPropagator)
        {
            return;
        }

        Sdk.SetDefaultTextMapPropagator(new RequestBaggageRefusingPropagator(current));
    }

    /// <inheritdoc />
    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter) =>
        _inner.Inject(context, carrier, setter);

    /// <summary>
    /// Extracts with the decorated propagator; for an incoming <see cref="HttpRequest"/>, keeps the trace context it
    /// read and discards the baggage, returning <paramref name="context"/>'s baggage instead.
    /// </summary>
    /// <inheritdoc />
    public override PropagationContext Extract<T>(PropagationContext context, T carrier, Func<T, string, IEnumerable<string>?> getter)
    {
        var extracted = _inner.Extract(context, carrier, getter);

        return carrier is HttpRequest
            ? new PropagationContext(extracted.ActivityContext, context.Baggage)
            : extracted;
    }
}
