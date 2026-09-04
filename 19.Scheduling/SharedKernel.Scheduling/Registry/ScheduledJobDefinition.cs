using Microsoft.Extensions.DependencyInjection;
using Quartz;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Jobs;

namespace SharedKernel.Scheduling.Registry;

/// <summary>
/// The closed-generic-per-command storage record for one registered job. See
/// <see cref="IScheduledJobDefinition"/> for the non-generic contract the hosted loop consumes.
/// </summary>
internal sealed class ScheduledJobDefinition<TCommand> : IScheduledJobDefinition
    where TCommand : class, ICommand
{
    /// <inheritdoc />
    public required string JobName { get; init; }

    /// <inheritdoc />
    public required CronExpression? Cron { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset? DeferredFireAtUtc { get; init; }

    /// <inheritdoc />
    public required ScheduledJobOptions Options { get; init; }

    /// <summary>Gets the registration-time factory that builds a <typeparamref name="TCommand"/> for one execution.</summary>
    public required Func<ScheduledJobExecutionContext, TCommand> CommandFactory { get; init; }

    /// <inheritdoc />
    public bool IsRecurring => Cron is not null;

    /// <inheritdoc />
    public Task<Result> ExecuteAsync(IServiceProvider scopedServiceProvider, ScheduledJobExecutionContext context, CancellationToken cancellationToken)
    {
        var job = scopedServiceProvider.GetRequiredService<ScheduledCommandJob<TCommand>>();
        return job.ExecuteAsync(CommandFactory, context, cancellationToken);
    }
}
