using SharedKernel.Execution.Context;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.Faults;
using SharedKernel.Messaging.MassTransit.Consumers;

namespace ShippingApi;

/// <summary>
/// Consumes the dispatched event and writes the read model.
/// </summary>
/// <remarks>
/// <para>
/// The message type is <see cref="EventEnvelope{TEvent}"/>, not the event: <c>IEventPublisher</c>
/// publishes the CloudEvents envelope, so that is what arrives. <c>envelope.Data</c> is the event.
/// </para>
/// <para>
/// Note what this consumer does <em>not</em> do: it never reads a tenant out of the message body,
/// and it never takes the actor as a parameter. It injects <c>IRequestContext</c> exactly as an
/// HTTP handler would, and <c>WithInboundRequestContext()</c> makes that resolve to the caller that
/// published. That is the whole feature.
/// </para>
/// </remarks>
public sealed class ShipmentDispatchedConsumer : ConsumerBase<EventEnvelope<ShipmentDispatched>>
{
    private readonly ShipmentProjection _projection;
    private readonly IRequestContext _caller;

    /// <summary>Initialises the consumer.</summary>
    /// <param name="projection">The read model this consumer writes.</param>
    /// <param name="caller">
    /// Who published the message. Message-aware inside a consume; the host's own caller elsewhere.
    /// </param>
    /// <param name="logger">The consumer's logger.</param>
    public ShipmentDispatchedConsumer(
        ShipmentProjection projection,
        IRequestContext caller,
        ILogger<ShipmentDispatchedConsumer> logger)
        : base(logger)
    {
        _projection = projection;
        _caller = caller;
    }

    /// <inheritdoc />
    protected override Task ConsumeAsync(EventEnvelope<ShipmentDispatched> message, CancellationToken ct)
    {
        ShipmentDispatched dispatched = message.Data;

        _projection.RecordDispatch(
            dispatched.ShipmentId,
            dispatched.Carrier,
            dispatched.TrackingNumber,
            _caller);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Consumes the hold command, which reaches exactly one endpoint rather than every subscriber.
/// </summary>
public sealed class HoldShipmentConsumer : ConsumerBase<HoldShipment>
{
    private readonly ShipmentProjection _projection;

    /// <summary>Initialises the consumer.</summary>
    /// <param name="projection">The read model this consumer updates.</param>
    /// <param name="logger">The consumer's logger.</param>
    public HoldShipmentConsumer(ShipmentProjection projection, ILogger<HoldShipmentConsumer> logger)
        : base(logger)
    {
        _projection = projection;
    }

    /// <inheritdoc />
    protected override Task ConsumeAsync(HoldShipment message, CancellationToken ct)
    {
        _projection.RecordHold(message.ShipmentId, message.Reason);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Consumes the delayed reminder. Reaching this consumer at all is the proof that deferral went
/// through the broker rather than through a timer in this process.
/// </summary>
public sealed class ChaseShipmentConsumer : ConsumerBase<ChaseShipment>
{
    private readonly ShipmentProjection _projection;

    /// <summary>Initialises the consumer.</summary>
    /// <param name="projection">The read model this consumer updates.</param>
    /// <param name="logger">The consumer's logger.</param>
    public ChaseShipmentConsumer(ShipmentProjection projection, ILogger<ChaseShipmentConsumer> logger)
        : base(logger)
    {
        _projection = projection;
    }

    /// <inheritdoc />
    protected override Task ConsumeAsync(ChaseShipment message, CancellationToken ct)
    {
        _projection.RecordHold(message.ShipmentId, "chased");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Always throws, so the retry policy, the fault message and the dead-letter path are exercised
/// end to end against a real broker.
/// </summary>
public sealed class FailingShipmentCheckConsumer : ConsumerBase<FailingShipmentCheck>
{
    /// <summary>Initialises the consumer.</summary>
    /// <param name="logger">The consumer's logger.</param>
    public FailingShipmentCheckConsumer(ILogger<FailingShipmentCheckConsumer> logger)
        : base(logger)
    {
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Always.</exception>
    protected override Task ConsumeAsync(FailingShipmentCheck message, CancellationToken ct) =>
        throw new InvalidOperationException($"Carrier lookup failed for shipment {message.ShipmentId}.");
}

/// <summary>
/// Observes the faults <see cref="FailingShipmentCheckConsumer"/> produces once retries are
/// exhausted, and records them so a test can assert on them.
/// </summary>
/// <remarks>
/// A fault consumer is an observer, not a recovery mechanism: by the time it runs, the message has
/// already been through every retry the policy allows. Its job is to make the failure visible.
/// </remarks>
public sealed class ShipmentCheckFaultConsumer : IFaultConsumer<FailingShipmentCheck>
{
    private readonly FaultLog _faults;

    /// <summary>Initialises the fault consumer.</summary>
    /// <param name="faults">Where observed faults are recorded.</param>
    public ShipmentCheckFaultConsumer(FaultLog faults)
    {
        _faults = faults;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        Guid faultId,
        DateTimeOffset faultTimestamp,
        FailingShipmentCheck faultedMessage,
        IReadOnlyList<FaultExceptionInfo> exceptions,
        CancellationToken ct)
    {
        _faults.Record(
            faultedMessage.ShipmentId,
            exceptions.Count > 0 ? exceptions[0].Message : "no exception recorded");

        return Task.CompletedTask;
    }
}

/// <summary>Records the faults <see cref="ShipmentCheckFaultConsumer"/> observed.</summary>
public sealed class FaultLog
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> _faults = new();

    /// <summary>Records one observed fault.</summary>
    /// <param name="shipmentId">The shipment the failing message referred to.</param>
    /// <param name="message">The exception message the consumer failed with.</param>
    public void Record(Guid shipmentId, string message) => _faults[shipmentId] = message;

    /// <summary>Returns the recorded fault for a shipment, or <see langword="null"/>.</summary>
    /// <param name="shipmentId">The shipment.</param>
    /// <returns>The exception message, or <see langword="null"/> when no fault was observed.</returns>
    public string? Find(Guid shipmentId) => _faults.TryGetValue(shipmentId, out string? message) ? message : null;
}
