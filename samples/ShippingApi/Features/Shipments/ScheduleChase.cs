using SharedKernel.Application.Messaging;
using SharedKernel.Messaging.Abstractions.Scheduling;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;

namespace ShippingApi.Features.Shipments;

/// <summary>Schedules a <see cref="ChaseShipment"/> reminder; returns the schedule token.</summary>
/// <param name="ShipmentId">The shipment.</param>
/// <param name="DelayMilliseconds">How long the broker should hold the reminder.</param>
public sealed record ScheduleChase(Guid ShipmentId, int DelayMilliseconds) : ICommand<Guid>;

/// <summary>Schedule: the broker holds the message until its time, so it survives this process exiting.</summary>
public sealed class ScheduleChaseHandler(IMessageScheduler scheduler, IClock clock) : ICommandHandler<ScheduleChase, Guid>
{
    public async Task<Result<Guid>> Handle(ScheduleChase command, CancellationToken cancellationToken)
    {
        Guid token = await scheduler.ScheduleAsync(
            new ChaseShipment(command.ShipmentId),
            clock.UtcNow.AddMilliseconds(command.DelayMilliseconds),
            cancellationToken);

        return Result<Guid>.Success(token);
    }
}
