namespace SharedKernel.Messaging.Abstractions.Scheduling;

/// <summary>
/// Configuration options for the deferred message scheduling feature.
/// Bound from the <c>"SharedKernel:Messaging:Scheduling"</c> configuration section.
/// </summary>
/// <remarks>
/// Consumed by <c>MessagingBusBuilder.WithInMemoryScheduler()</c> and
/// <c>MessagingBusBuilder.WithQuartzScheduler()</c> in the MassTransit package.
/// Choose <see cref="SchedulingProvider.Quartz"/> for production workloads that require
/// durable scheduling across process restarts.
/// </remarks>
public sealed class SchedulingOptions
{
    /// <summary>
    /// The DI configuration section name for <see cref="SchedulingOptions"/>.
    /// </summary>
    public const string SectionName = "SharedKernel:Messaging:Scheduling";

    /// <summary>
    /// Gets or sets the scheduler backend to use.
    /// </summary>
    /// <remarks>Default: <see cref="SchedulingProvider.InMemory"/>.</remarks>
    public SchedulingProvider Provider { get; set; } = SchedulingProvider.InMemory;
}
