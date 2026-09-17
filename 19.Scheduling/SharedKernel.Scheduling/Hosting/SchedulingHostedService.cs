using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Diagnostics;
using SharedKernel.Scheduling.Jobs;
using SharedKernel.Scheduling.Options;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Registry;

namespace SharedKernel.Scheduling.Hosting;

/// <summary>
/// The <see cref="BackgroundService"/> that owns the scheduling tick loop: computing next-fire times
/// via Quartz's standalone <see cref="Quartz.CronExpression"/>, enforcing <see cref="MisfirePolicy"/>
/// and <see cref="OverlapPolicy"/>, acquiring the optional per-tick distributed lock, and dispatching
/// each due job through its <see cref="IScheduledJobDefinition"/>.
/// </summary>
/// <remarks>
/// This is this package's own hosted loop — never Quartz's <c>IScheduler</c> (Domain Invariant 2).
/// Registered as a singleton by <c>AddSharedKernelScheduling</c>, both directly (so
/// <c>SchedulerServiceProbe</c> can read its in-process state) and as the process's
/// <see cref="IHostedService"/>.
/// </remarks>
internal sealed class SchedulingHostedService : BackgroundService
{
    private readonly ScheduledJobRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockService? _lockService;
    private readonly IClock _clock;
    private readonly IOptions<SchedulingOptions> _options;
    private readonly ILogger<SchedulingHostedService> _logger;

    private readonly Dictionary<string, JobRuntimeState> _states = new(StringComparer.Ordinal);
    private readonly object _inFlightGate = new();
    private readonly List<Task> _inFlightTasks = [];

    private volatile bool _isRunning;
    private long _lastTickUtcTicks = -1;

    public SchedulingHostedService(
        ScheduledJobRegistry registry,
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<SchedulingOptions> options,
        ILogger<SchedulingHostedService> logger,
        IDistributedLockService? lockService = null)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options;
        _logger = logger;
        _lockService = lockService;
    }

    /// <summary>Gets a value indicating whether the tick loop is currently running.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>Gets the number of jobs registered at startup.</summary>
    public int RegisteredJobCount => _registry.Definitions.Count;

    /// <summary>Gets the UTC time of the most recent tick, or <see langword="null"/> before the first tick.</summary>
    public DateTimeOffset? LastTickUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastTickUtcTicks);
            return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_lockService is null)
        {
            Log.SingleReplicaWarning(_logger);
        }

        DateTimeOffset startupNowUtc = _clock.UtcNow;
        foreach (IScheduledJobDefinition definition in _registry.Definitions)
        {
            JobRuntimeState state = GetOrCreateState(definition.JobName);
            state.NextFireTimeUtc = definition.IsRecurring
                ? definition.Cron!.GetTimeAfter(startupNowUtc)
                : definition.DeferredFireAtUtc;
        }

        _isRunning = true;
        Log.HostedServiceStarted(_logger, _registry.Definitions.Count);

        try
        {
            using var timer = new PeriodicTimer(_options.Value.TickInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                DateTimeOffset tickTimeUtc = _clock.UtcNow;
                Interlocked.Exchange(ref _lastTickUtcTicks, tickTimeUtc.UtcTicks);

                foreach (IScheduledJobDefinition definition in _registry.Definitions)
                {
                    ProcessJobTick(definition, tickTimeUtc, stoppingToken);
                }

                PruneCompletedInFlightTasks();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — PeriodicTimer.WaitForNextTickAsync observed the stopping token.
        }
        catch (Exception ex)
        {
            // A fault here (e.g. IOptions<SchedulingOptions>.Value re-validating and throwing) would
            // otherwise die silently: BackgroundService's own StartAsync schedules ExecuteAsync via
            // Task.Run and never awaits it, so only the real generic host's crash-supervision
            // machinery (HostOptions.BackgroundServiceExceptionBehavior) ever observes this Task's
            // fault — never this method's direct caller. Logging here guarantees a diagnosable trail
            // exists regardless of which host is supervising this service, before rethrowing so that
            // supervision still applies.
            Log.HostedServiceFaulted(_logger, ex);
            throw;
        }
        finally
        {
            _isRunning = false;
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Log.HostedServiceStopping(_logger);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        Task[] inFlight;
        lock (_inFlightGate)
        {
            inFlight = [.. _inFlightTasks.Where(t => !t.IsCompleted)];
        }

        if (inFlight.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(inFlight).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.ShutdownDrainTimedOut(_logger, inFlight.Length);
        }
        catch (Exception)
        {
            // Individual execution failures are already logged by ExecuteOneAsync/ScheduledCommandJob —
            // Task.WhenAll surfacing the first one here must not fault host shutdown itself.
        }
    }

    private JobRuntimeState GetOrCreateState(string jobName)
    {
        lock (_states)
        {
            if (!_states.TryGetValue(jobName, out JobRuntimeState? state))
            {
                state = new JobRuntimeState();
                _states.Add(jobName, state);
            }

            return state;
        }
    }

    private void ProcessJobTick(IScheduledJobDefinition definition, DateTimeOffset tickTimeUtc, CancellationToken stoppingToken)
    {
        JobRuntimeState state = GetOrCreateState(definition.JobName);

        if (state.Terminal)
        {
            return;
        }

        DateTimeOffset? dueAtUtc = state.NextFireTimeUtc;
        if (dueAtUtc is null || tickTimeUtc < dueAtUtc.Value)
        {
            return;
        }

        TimeSpan misfireThreshold = _options.Value.TickInterval;
        if (tickTimeUtc - dueAtUtc.Value > misfireThreshold)
        {
            HandleMisfire(definition, state, dueAtUtc.Value, tickTimeUtc, stoppingToken);
            return;
        }

        ScheduleFire(definition, state, scheduledFireTimeUtc: dueAtUtc.Value, actualFireTimeUtc: tickTimeUtc, stoppingToken);
        AdvancePastNow(definition, state, dueAtUtc.Value);
    }

    private void HandleMisfire(
        IScheduledJobDefinition definition,
        JobRuntimeState state,
        DateTimeOffset missedFireTimeUtc,
        DateTimeOffset observedAtUtc,
        CancellationToken stoppingToken)
    {
        MisfirePolicy policy = definition.Options.MisfirePolicy!.Value;
        SchedulingTelemetry.MisfireCount.Add(1, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, definition.JobName));
        Log.MisfireDetected(_logger, definition.JobName, missedFireTimeUtc, observedAtUtc, policy);

        switch (policy)
        {
            case MisfirePolicy.Skip:
                Log.MisfireSkipped(_logger, definition.JobName, missedFireTimeUtc);
                AdvancePastNow(definition, state, observedAtUtc);
                break;

            case MisfirePolicy.FireOnce:
                ScheduleFire(definition, state, missedFireTimeUtc, observedAtUtc, stoppingToken);
                // Collapse every remaining missed occurrence into this single catch-up run — jump
                // straight to the next occurrence after "now" rather than replaying the rest.
                AdvancePastNow(definition, state, observedAtUtc);
                break;

            case MisfirePolicy.RunImmediatelyThenReschedule:
                ScheduleFire(definition, state, missedFireTimeUtc, observedAtUtc, stoppingToken);
                // Preserve the original cadence anchor: compute the next occurrence from the missed
                // one itself, even if still in the past — a later tick will catch that one up too.
                // This is what makes every missed occurrence eventually replay, one per tick, instead
                // of collapsing into a single run the way FireOnce does.
                if (definition.IsRecurring)
                {
                    state.NextFireTimeUtc = definition.Cron!.GetTimeAfter(missedFireTimeUtc);
                }
                else
                {
                    state.Terminal = true;
                    state.NextFireTimeUtc = null;
                }

                break;
        }
    }

    private static void AdvancePastNow(IScheduledJobDefinition definition, JobRuntimeState state, DateTimeOffset afterUtc)
    {
        if (definition.IsRecurring)
        {
            state.NextFireTimeUtc = definition.Cron!.GetTimeAfter(afterUtc);
        }
        else
        {
            state.Terminal = true;
            state.NextFireTimeUtc = null;
        }
    }

    private void ScheduleFire(
        IScheduledJobDefinition definition,
        JobRuntimeState state,
        DateTimeOffset scheduledFireTimeUtc,
        DateTimeOffset actualFireTimeUtc,
        CancellationToken stoppingToken)
    {
        OverlapPolicy overlapPolicy = definition.Options.OverlapPolicy!.Value;

        if (overlapPolicy == OverlapPolicy.Allow)
        {
            Interlocked.Increment(ref state.RunningCount);
            Task allowTask = RunOneAndDecrementAsync(definition, state, scheduledFireTimeUtc, actualFireTimeUtc, stoppingToken);
            TrackInFlight(allowTask);
            return;
        }

        // Skip / Queue: single-concurrency per job, guarded by the same lock protecting the queue.
        bool startNow = false;
        lock (state.Gate)
        {
            if (state.RunningCount == 0)
            {
                state.RunningCount = 1;
                startNow = true;
            }
            else if (overlapPolicy == OverlapPolicy.Queue)
            {
                state.PendingQueue.Enqueue(new PendingFire(scheduledFireTimeUtc, actualFireTimeUtc));
                SchedulingTelemetry.QueuedOverlapCount.Add(1, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, definition.JobName));
                Log.JobQueuedOverlap(_logger, definition.JobName, state.PendingQueue.Count);
            }
            else
            {
                SchedulingTelemetry.SkippedOverlapCount.Add(1, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, definition.JobName));
                Log.JobSkippedOverlap(_logger, definition.JobName);
            }
        }

        if (startNow)
        {
            Task drainTask = RunAndDrainQueueAsync(definition, state, scheduledFireTimeUtc, actualFireTimeUtc, stoppingToken);
            TrackInFlight(drainTask);
        }
    }

    private async Task RunOneAndDecrementAsync(
        IScheduledJobDefinition definition,
        JobRuntimeState state,
        DateTimeOffset scheduledFireTimeUtc,
        DateTimeOffset actualFireTimeUtc,
        CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteOneAsync(definition, scheduledFireTimeUtc, actualFireTimeUtc, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref state.RunningCount);
        }
    }

    private async Task RunAndDrainQueueAsync(
        IScheduledJobDefinition definition,
        JobRuntimeState state,
        DateTimeOffset scheduledFireTimeUtc,
        DateTimeOffset actualFireTimeUtc,
        CancellationToken stoppingToken)
    {
        DateTimeOffset currentScheduled = scheduledFireTimeUtc;
        DateTimeOffset currentActual = actualFireTimeUtc;

        while (true)
        {
            await ExecuteOneAsync(definition, currentScheduled, currentActual, stoppingToken).ConfigureAwait(false);

            lock (state.Gate)
            {
                if (!state.PendingQueue.TryDequeue(out PendingFire next))
                {
                    state.RunningCount = 0;
                    return;
                }

                currentScheduled = next.ScheduledFireTimeUtc;
                currentActual = next.ActualFireTimeUtc;
            }
        }
    }

    private async Task ExecuteOneAsync(
        IScheduledJobDefinition definition,
        DateTimeOffset scheduledFireTimeUtc,
        DateTimeOffset actualFireTimeUtc,
        CancellationToken stoppingToken)
    {
        using Activity? activity = SchedulingTelemetry.ActivitySource.StartActivity("Scheduling.Job.Fire", ActivityKind.Internal);
        activity?.SetTag(SchedulingTagKeys.JobName, definition.JobName);
        activity?.SetTag(SchedulingTagKeys.MisfirePolicy, definition.Options.MisfirePolicy!.Value.ToString());
        activity?.SetTag(SchedulingTagKeys.OverlapPolicy, definition.Options.OverlapPolicy!.Value.ToString());

        SchedulingTelemetry.FireCount.Add(1, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, definition.JobName));
        Log.JobFireStarted(_logger, definition.JobName, scheduledFireTimeUtc, actualFireTimeUtc);

        try
        {
            long? fencingToken = null;

            if (_lockService is not null)
            {
                string resource = SchedulingLockKeys.ForOccurrence(definition.JobName, scheduledFireTimeUtc);
                TimeSpan lockExpiry = definition.Options.LockExpiry ?? _options.Value.DefaultLockExpiry;

                DistributedLease? lease;
                try
                {
                    lease = await _lockService.TryAcquireLeaseAsync(resource, lockExpiry, stoppingToken).ConfigureAwait(false);
                }
                catch (DistributedLockUnavailableException ex)
                {
                    // Without the lock store no replica can prove it owns this occurrence, so none
                    // runs it. Visible as an error, never mistaken for another replica's claim.
                    SchedulingTelemetry.LockStoreUnavailableCount.Add(1, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, definition.JobName));
                    Log.LockStoreUnavailable(_logger, definition.JobName, ex);
                    activity?.SetTag(SchedulingTagKeys.Outcome, "lock-store-unavailable");
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    return;
                }

                if (lease is null)
                {
                    // Another replica already claimed this exact occurrence — this IS the
                    // cross-replica single-execution mechanism (Domain Invariant 3), not an error.
                    SchedulingTelemetry.LockAcquisitionFailedCount.Add(1, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, definition.JobName));
                    Log.LockAcquisitionFailed(_logger, definition.JobName);
                    activity?.SetTag(SchedulingTagKeys.Outcome, "lock-not-acquired");
                    return;
                }

                fencingToken = lease.FencingToken;
                Log.LockAcquired(_logger, definition.JobName, fencingToken);

                // A lease, not a lock: it is never released and simply expires after lockExpiry — see
                // SchedulingLockKeys' remarks. Releasing it when this execution finishes would reopen
                // the cross-replica duplicate-execution window the per-occurrence key exists to close.
            }

            var context = new ScheduledJobExecutionContext
            {
                JobName = definition.JobName,
                ScheduledFireTimeUtc = scheduledFireTimeUtc,
                ActualFireTimeUtc = actualFireTimeUtc,
                TenantScope = definition.Options.TenantScope,
                FencingToken = fencingToken,
            };

            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            Result result = await definition.ExecuteAsync(scope.ServiceProvider, context, stoppingToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                SchedulingTelemetry.SucceededCount.Add(1, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, definition.JobName));
                activity?.SetTag(SchedulingTagKeys.Outcome, "succeeded");
            }
            else
            {
                SchedulingTelemetry.FailedCount.Add(1, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, definition.JobName));
                activity?.SetTag(SchedulingTagKeys.Outcome, "failed");
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Message);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            activity?.SetTag(SchedulingTagKeys.Outcome, "cancelled");
            Log.JobCancelled(_logger, definition.JobName);
        }
        catch (Exception ex)
        {
            SchedulingTelemetry.FailedCount.Add(1, new KeyValuePair<string, object?>(SchedulingTagKeys.JobName, definition.JobName));
            activity?.SetTag(SchedulingTagKeys.Outcome, "threw");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Log.JobFireThrew(_logger, definition.JobName, ex);
        }
    }

    private void TrackInFlight(Task task)
    {
        lock (_inFlightGate)
        {
            _inFlightTasks.Add(task);
        }
    }

    private void PruneCompletedInFlightTasks()
    {
        lock (_inFlightGate)
        {
            _inFlightTasks.RemoveAll(t => t.IsCompleted);
        }
    }
}
