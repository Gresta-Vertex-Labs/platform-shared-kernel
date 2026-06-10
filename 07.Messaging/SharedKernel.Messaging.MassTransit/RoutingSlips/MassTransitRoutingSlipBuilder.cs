using MassTransit;

// Alias to disambiguate from MassTransit.IRoutingSlipBuilder
using ISkRoutingSlipBuilder = SharedKernel.Messaging.Abstractions.RoutingSlips.IRoutingSlipBuilder;

namespace SharedKernel.Messaging.MassTransit.RoutingSlips;

/// <summary>
/// MassTransit implementation of <see cref="ISkRoutingSlipBuilder"/>.
/// Wraps a <c>MassTransit.RoutingSlipBuilder</c> instance and returns the resulting
/// <c>MassTransit.Courier.Contracts.RoutingSlip</c> typed as <see cref="object"/>.
/// </summary>
internal sealed class MassTransitRoutingSlipBuilder : ISkRoutingSlipBuilder
{
    private readonly global::MassTransit.RoutingSlipBuilder _builder = new(NewId.NextGuid());

    /// <inheritdoc />
    public ISkRoutingSlipBuilder AddActivity(string activityName, Uri executeAddress, object arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);
        ArgumentNullException.ThrowIfNull(executeAddress);
        ArgumentNullException.ThrowIfNull(arguments);

        _builder.AddActivity(activityName, executeAddress, arguments);
        return this;
    }

    /// <inheritdoc />
    public object Build() => _builder.Build();
}
