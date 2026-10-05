using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;
using SharedKernel.Application.Messaging;
using SharedKernel.Scheduling.Extensions;
using SharedKernel.Scheduling.Jobs;

namespace SharedKernel.Scheduling.Registry;

/// <summary>
/// The concrete, single implementation of both <see cref="IScheduledJobRegistry"/> (the public
/// registration surface) and <see cref="ISchedulingBuilder"/> (the fluent builder returned by
/// <c>AddSharedKernelScheduling</c>). Also the internal storage for every registered job's
/// <see cref="IScheduledJobDefinition"/>, read by <c>SchedulingHostedService</c>.
/// </summary>
internal sealed class ScheduledJobRegistry : ISchedulingBuilder
{
    private readonly List<IScheduledJobDefinition> _definitions = [];
    private readonly HashSet<string> _jobNames = new(StringComparer.Ordinal);

    public ScheduledJobRegistry(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <summary>Gets every job registered so far. Read by <c>SchedulingHostedService</c> at startup.</summary>
    internal IReadOnlyList<IScheduledJobDefinition> Definitions => _definitions;

    /// <inheritdoc />
    public IScheduledJobRegistry AddRecurring<TCommand>(
        string jobName,
        string cronExpression,
        Func<ScheduledJobExecutionContext, TCommand> commandFactory,
        Action<ScheduledJobOptions> configure)
        where TCommand : class, ICommand
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(configure);
        EnsureUniqueName(jobName);

        CronExpression cron = ParseCron(cronExpression);
        ScheduledJobOptions options = BuildOptions(configure);

        Services.TryAddTransient<ScheduledCommandJob<TCommand>>();

        _definitions.Add(new ScheduledJobDefinition<TCommand>
        {
            JobName = jobName,
            Cron = cron,
            DeferredFireAtUtc = null,
            Options = options,
            CommandFactory = commandFactory,
        });

        return this;
    }

    /// <inheritdoc />
    public IScheduledJobRegistry AddDeferred<TCommand>(
        string jobName,
        DateTimeOffset fireAtUtc,
        Func<ScheduledJobExecutionContext, TCommand> commandFactory,
        Action<ScheduledJobOptions> configure)
        where TCommand : class, ICommand
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(configure);
        EnsureUniqueName(jobName);

        ScheduledJobOptions options = BuildOptions(configure);

        Services.TryAddTransient<ScheduledCommandJob<TCommand>>();

        _definitions.Add(new ScheduledJobDefinition<TCommand>
        {
            JobName = jobName,
            Cron = null,
            DeferredFireAtUtc = fireAtUtc,
            Options = options,
            CommandFactory = commandFactory,
        });

        return this;
    }

    private void EnsureUniqueName(string jobName)
    {
        if (!_jobNames.Add(jobName))
        {
            throw new ArgumentException($"A job named '{jobName}' is already registered.", nameof(jobName));
        }
    }

    private static CronExpression ParseCron(string cronExpression)
    {
        try
        {
            // Anchored to UTC explicitly: CronExpression defaults to TimeZoneInfo.Local, which would
            // make this domain's scheduling non-deterministic across environments given every other
            // decision here (IClock.UtcNow, ScheduledJobExecutionContext timestamps) is UTC-anchored.
            return new CronExpression(cronExpression) { TimeZone = TimeZoneInfo.Utc };
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"'{cronExpression}' is not a valid cron expression.", nameof(cronExpression), ex);
        }
    }

    private static ScheduledJobOptions BuildOptions(Action<ScheduledJobOptions> configure)
    {
        var options = new ScheduledJobOptions();
        configure(options);

        if (options.MisfirePolicy is null)
        {
            throw new ArgumentException(
                "ScheduledJobOptions.MisfirePolicy must be explicitly set by the configure delegate — "
                    + "it is never defaulted (19.Scheduling Domain Invariant 4).",
                nameof(configure));
        }

        if (options.OverlapPolicy is null)
        {
            throw new ArgumentException(
                "ScheduledJobOptions.OverlapPolicy must be explicitly set by the configure delegate — "
                    + "it is never defaulted (19.Scheduling Domain Invariant 4).",
                nameof(configure));
        }

        return options;
    }
}
