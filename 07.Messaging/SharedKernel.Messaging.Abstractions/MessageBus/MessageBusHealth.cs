namespace SharedKernel.Messaging.Abstractions.MessageBus;

/// <summary>
/// Reports the health of the message bus, as returned by <see cref="IMessageBusProbe.ProbeAsync"/>.
/// </summary>
/// <remarks>
/// Value equality is provided by record semantics. This type is AOT-safe: no reflection is used
/// in its construction or equality path.
/// </remarks>
/// <param name="IsHealthy">
/// <see langword="true"/> when the message bus is fully healthy and ready to publish/consume;
/// <see langword="false"/> for any degraded or unhealthy state.
/// </param>
/// <param name="Description">
/// Human-readable detail about the bus's current state. <see langword="null"/> when
/// <paramref name="IsHealthy"/> is <see langword="true"/>; populated with a diagnostic message
/// (e.g. the reason the bus is not ready) when <see langword="false"/>.
/// </param>
public sealed record MessageBusHealth(bool IsHealthy, string? Description);
