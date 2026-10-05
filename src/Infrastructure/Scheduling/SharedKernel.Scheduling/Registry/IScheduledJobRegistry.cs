using SharedKernel.Application.Messaging;
using SharedKernel.Scheduling.Jobs;

namespace SharedKernel.Scheduling.Registry;

/// <summary>
/// The startup-time registration surface for cron/recurring and one-shot deferred jobs.
/// </summary>
/// <remarks>
/// <para>
/// Registration is a startup-time act. There is no runtime API to mutate, pause, or remove a
/// registered job once the host has started — that is out of scope for this domain unless a future
/// phase explicitly adds it (see <c>src/Infrastructure/Scheduling/CLAUDE.md</c>'s package rules).
/// </para>
/// <para>
/// Both members take a <c>commandFactory</c> — a <see cref="Func{T,TResult}"/> — rather than a bare
/// command instance, because a fired job has no external caller able to supply one the way an HTTP
/// request or a Temporal activity input does. The factory closes over whatever the job needs at
/// registration time and is invoked fresh for every single execution attempt.
/// </para>
/// </remarks>
public interface IScheduledJobRegistry
{
    /// <summary>
    /// Registers a recurring job driven by a cron expression.
    /// </summary>
    /// <typeparam name="TCommand">The void-returning command type this job dispatches.</typeparam>
    /// <param name="jobName">
    /// A unique name for this job within the scheduling registry. Registering a second job under an
    /// already-used name throws.
    /// </param>
    /// <param name="cronExpression">
    /// A standard Quartz cron expression, parsed exclusively via Quartz's standalone
    /// <see cref="Quartz.CronExpression"/> class (never a hand-rolled parser) and interpreted in UTC.
    /// Throws <see cref="ArgumentException"/> immediately if not a valid cron expression.
    /// </param>
    /// <param name="commandFactory">Builds the <typeparamref name="TCommand"/> instance for one execution.</param>
    /// <param name="configure">
    /// Configures this job's <see cref="ScheduledJobOptions"/>. Mandatory, and must set both
    /// <see cref="ScheduledJobOptions.MisfirePolicy"/> and <see cref="ScheduledJobOptions.OverlapPolicy"/>
    /// — registration throws <see cref="ArgumentException"/> immediately if either is left unset.
    /// </param>
    /// <returns>This registry, for fluent chaining.</returns>
    IScheduledJobRegistry AddRecurring<TCommand>(
        string jobName,
        string cronExpression,
        Func<ScheduledJobExecutionContext, TCommand> commandFactory,
        Action<ScheduledJobOptions> configure)
        where TCommand : class, ICommand;

    /// <summary>
    /// Registers a one-shot job that fires once at <paramref name="fireAtUtc"/> and never again.
    /// </summary>
    /// <typeparam name="TCommand">The void-returning command type this job dispatches.</typeparam>
    /// <param name="jobName">
    /// A unique name for this job within the scheduling registry. Registering a second job under an
    /// already-used name throws.
    /// </param>
    /// <param name="fireAtUtc">The single UTC time this job should fire at.</param>
    /// <param name="commandFactory">Builds the <typeparamref name="TCommand"/> instance for this one execution.</param>
    /// <param name="configure">
    /// Configures this job's <see cref="ScheduledJobOptions"/>. Mandatory, and must set both
    /// <see cref="ScheduledJobOptions.MisfirePolicy"/> and <see cref="ScheduledJobOptions.OverlapPolicy"/>
    /// — registration throws <see cref="ArgumentException"/> immediately if either is left unset.
    /// </param>
    /// <returns>This registry, for fluent chaining.</returns>
    IScheduledJobRegistry AddDeferred<TCommand>(
        string jobName,
        DateTimeOffset fireAtUtc,
        Func<ScheduledJobExecutionContext, TCommand> commandFactory,
        Action<ScheduledJobOptions> configure)
        where TCommand : class, ICommand;
}
