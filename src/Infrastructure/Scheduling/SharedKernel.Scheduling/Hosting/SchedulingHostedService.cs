using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;
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
/// This is this package's own hosted loop — never Quartz's <c>IScheduler</c>.
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
    private readonly SchedulingMetrics _metrics;
    private readonly ILogger<SchedulingHostedService> _logger;

    private readonly Dictionary<string, JobRuntimeState> _states = new(StringComparer.Ordinal);
    private readonly object _inFlightGate = new();
    private readonly List<Task> _inFlightTasks = [];

    // One correlation id per service instance, shared by the start and stop scopes of the loop caller.
    private readonly string _loopCorrelationId = CorrelationIds.New();

    private volatile bool _isRunning;
    private long _lastTickUtcTicks = -1;

    public SchedulingHostedService(
        ScheduledJobRegistry registry,
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<SchedulingOptions> options,
        SchedulingMetrics metrics,
        ILogger<SchedulingHostedService> logger,
        IDistributedLockService? lockService = null)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options;
        _metrics = metrics;
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
        // Every loop-level log (start, faulted, misfire and overlap decisions) runs under the loop
        // caller. The synchronous prefix of this method runs on the caller of StartAsync; the async
        // method builder restores that caller's ambient context when the method first yields.
        using IDisposable loopScope = RequestContextScope.Begin(SchedulerCallers.ForLoop(_loopCorrelationId));

        if (_lockService is null)
        {
            SchedulingLog.SingleReplicaWarning(_logger);
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
        SchedulingLog.HostedServiceStarted(_logger, _registry.Definitions.Count);

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
            SchedulingLog.HostedServiceFaulted(_logger, ex);
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
        using IDisposable loopScope = RequestContextScope.Begin(SchedulerCallers.ForLoop(_loopCorrelationId));

        SchedulingLog.HostedServiceStopping(_logger);
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
            SchedulingLog.ShutdownDrainTimedOut(_logger, inFlight.Length);
        }
        catch (Exception ex)
        {
            // Individual execution failures are already logged by ExecuteOneAsync and ScheduledCommandJob.
            // Task.WhenAll surfacing the first one here must not fault host shutdown itself, but the drain
            // fault is logged so a swallowed failure stays diagnosable.
            SchedulingLog.ShutdownDrainFaulted(_logger, ex);
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
        _metrics.RecordMisfire(definition.JobName);
        SchedulingLog.MisfireDetected(_logger, definition.JobName, missedFireTimeUtc, observedAtUtc, policy);

        switch (policy)
        {
            case MisfirePolicy.Skip:
                SchedulingLog.MisfireSkipped(_logger, definition.JobName, missedFireTimeUtc);
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
                _metrics.RecordOverlapQueued(definition.JobName);
                SchedulingLog.JobQueuedOverlap(_logger, definition.JobName, state.PendingQueue.Count);
            }
            else
            {
                _metrics.RecordOverlapSkipped(definition.JobName);
                SchedulingLog.JobSkippedOverlap(_logger, definition.JobName);
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
        // The occurrence's system caller (job identity, the job's tenant, a new correlation id) is
        // ambient from the first statement until after the catch blocks, so the lease claim, the span
        // and every log of this occurrence carry the same caller as the command handler does. It holds
        // no permissions: a job whose command is gated by [RequirePermission] registers its own identity.
        using IDisposable callerScope = RequestContextScope.Begin(SchedulerCallers.ForOccurrence(definition));

        using Activity? activity = SchedulingTelemetry.ActivitySource.StartActivity("scheduling fire", ActivityKind.Internal);
        activity?.SetTag(SchedulingTagKeys.JobName, definition.JobName);
        activity?.SetTag(SchedulingTagKeys.MisfirePolicy, definition.Options.MisfirePolicy!.Value.ToString());
        activity?.SetTag(SchedulingTagKeys.OverlapPolicy, definition.Options.OverlapPolicy!.Value.ToString());

        SchedulingLog.JobFireStarted(_logger, definition.JobName, scheduledFireTimeUtc, actualFireTimeUtc);

        // Set when the occurrence is ours to run (the claim is held, or no lock service exists); the
        // duration of the execution is measured from here with the injected clock.
        DateTimeOffset? startedAtUtc = null;

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
                    _metrics.RecordLockStoreUnavailable(definition.JobName);
                    SchedulingLog.LockStoreUnavailable(_logger, definition.JobName, ex);
                    activity?.SetTag(SchedulingTagKeys.Claim, SchedulingClaimResults.StoreUnavailable);
                    activity?.SetTag(WellKnownTagKeys.Outcome, activity.RecordException(ex));
                    return;
                }

                if (lease is null)
                {
                    // Another replica already claimed this exact occurrence. This IS the cross-replica
                    // single-execution mechanism, not an error: it has no outcome of its own.
                    _metrics.RecordLockNotAcquired(definition.JobName);
                    SchedulingLog.LockAcquisitionFailed(_logger, definition.JobName);
                    activity?.SetTag(SchedulingTagKeys.Claim, SchedulingClaimResults.NotAcquired);
                    return;
                }

                fencingToken = lease.FencingToken;
                activity?.SetTag(SchedulingTagKeys.Claim, SchedulingClaimResults.Acquired);
                SchedulingLog.LockAcquired(_logger, definition.JobName, fencingToken);

                // A lease, not a lock: it is never released and simply expires after lockExpiry, see
                // SchedulingLockKeys' remarks. Releasing it when this execution finishes would reopen
                // the cross-replica duplicate-execution window the per-occurrence key exists to close.
            }

            startedAtUtc = _clock.UtcNow;

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

            string outcome = activity.RecordResult(result);
            activity?.SetTag(WellKnownTagKeys.Outcome, outcome);
            RecordExecution(definition.JobName, outcome, startedAtUtc);
        }
        catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
        {
            // Cancellation is not a failure: the recorder leaves the status unset and returns "canceled".
            string outcome = activity.RecordException(ex);
            activity?.SetTag(WellKnownTagKeys.Outcome, outcome);
            RecordExecution(definition.JobName, outcome, startedAtUtc);
            SchedulingLog.JobCancelled(_logger, definition.JobName);
        }
        catch (Exception ex)
        {
            // Job-isolation boundary: one job's exception must not stop the loop. It is logged, recorded
            // on the span and counted; the loop carries on with the next occurrence.
            string outcome = activity.RecordException(ex);
            activity?.SetTag(WellKnownTagKeys.Outcome, outcome);
            RecordExecution(definition.JobName, outcome, startedAtUtc);
            SchedulingLog.JobFireThrew(_logger, definition.JobName, ex);
        }
    }

    private void RecordExecution(string jobName, string outcome, DateTimeOffset? startedAtUtc)
    {
        TimeSpan elapsed = startedAtUtc is { } started ? _clock.UtcNow - started : TimeSpan.Zero;
        _metrics.RecordExecution(jobName, outcome, elapsed.TotalSeconds);
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
