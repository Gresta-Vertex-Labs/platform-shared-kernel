using Quartz;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Jobs;

namespace SharedKernel.Scheduling.Registry;

/// <summary>
/// The internal, non-generic storage shape for a single registered job — recurring or deferred.
/// </summary>
/// <remarks>
/// Backed by a closed-generic <see cref="ScheduledJobDefinition{TCommand}"/> per command type
/// (zero reflection): <see cref="ExecuteAsync"/> resolves the matching
/// <see cref="ScheduledCommandJob{TCommand}"/> from the supplied per-execution DI scope via ordinary
/// generic service resolution — never <c>Type.GetMethod</c>/<c>MakeGenericMethod</c>/<c>Invoke</c>.
/// </remarks>
internal interface IScheduledJobDefinition
{
    /// <summary>Gets the unique, registration-time job name.</summary>
    string JobName { get; }

    /// <summary>Gets a value indicating whether this is a recurring (cron) job as opposed to a one-shot deferred job.</summary>
    bool IsRecurring { get; }

    /// <summary>Gets the parsed cron expression when <see cref="IsRecurring"/> is <see langword="true"/>; otherwise <see langword="null"/>.</summary>
    CronExpression? Cron { get; }

    /// <summary>Gets the fire-at time when <see cref="IsRecurring"/> is <see langword="false"/>; otherwise <see langword="null"/>.</summary>
    DateTimeOffset? DeferredFireAtUtc { get; }

    /// <summary>Gets the registration-time options for this job.</summary>
    ScheduledJobOptions Options { get; }

    /// <summary>
    /// Executes this job once, resolving its <see cref="ScheduledCommandJob{TCommand}"/> from
    /// <paramref name="scopedServiceProvider"/>.
    /// </summary>
    /// <param name="scopedServiceProvider">
    /// A per-execution DI scope's service provider — never the application's root provider.
    /// </param>
    /// <param name="context">The execution context for this single fire attempt.</param>
    /// <param name="cancellationToken">The host's stopping token.</param>
    Task<Result> ExecuteAsync(IServiceProvider scopedServiceProvider, ScheduledJobExecutionContext context, CancellationToken cancellationToken);
}
