namespace SharedKernel.Messaging.Abstractions.Scheduling;

/// <summary>
/// Identifies the scheduler backend used by <c>IMessageScheduler</c>.
/// </summary>
public enum SchedulingProvider
{
    /// <summary>
    /// MassTransit in-memory scheduler. Schedule tokens do not survive process restart.
    /// Suitable for development and testing only.
    /// </summary>
    InMemory,

    /// <summary>
    /// MassTransit Quartz.NET integration. Durable scheduling — tokens survive process restarts.
    /// Requires a relational database configured via <see cref="QuartzSchedulerOptions.ConnectionString"/>.
    /// </summary>
    Quartz,

    /// <summary>
    /// Hangfire integration. Reserved for future use — not wired in this release.
    /// </summary>
    Hangfire,
}
