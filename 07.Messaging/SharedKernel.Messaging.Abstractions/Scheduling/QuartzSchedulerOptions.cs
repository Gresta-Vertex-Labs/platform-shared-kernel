namespace SharedKernel.Messaging.Abstractions.Scheduling;

/// <summary>
/// Configuration options for the Quartz.NET-backed durable message scheduler.
/// </summary>
/// <remarks>
/// Passed to <c>MessagingBusBuilder.WithQuartzScheduler()</c> in the MassTransit package.
/// <c>Build()</c> throws <see cref="InvalidOperationException"/> at startup if
/// <see cref="ConnectionString"/> is null or empty when <c>WithQuartzScheduler()</c> is called.
/// </remarks>
public sealed class QuartzSchedulerOptions
{
    /// <summary>
    /// The ADO.NET connection string for the Quartz.NET scheduler database.
    /// </summary>
    /// <remarks>
    /// Required when using <c>MessagingBusBuilder.WithQuartzScheduler()</c>.
    /// Source from environment variables, Kubernetes Secrets, or Azure Key Vault —
    /// never embed credentials in committed configuration files.
    /// </remarks>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The database schema name used by Quartz.NET for its internal tables.
    /// </summary>
    /// <remarks>Default: <c>"quartz"</c>.</remarks>
    public string Schema { get; set; } = "quartz";
}
