using MediatR;
using SharedKernel.Application.Messaging;
using SharedKernel.Scheduling.Jobs;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Registry;

namespace SharedKernel.Testing.Scheduling;

/// <summary>
/// In-memory fake implementation of <see cref="IScheduledJobRegistry"/> for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>TICKS ARE TRIGGERED MANUALLY BY THE TEST, NEVER BY REAL ELAPSED WALL-CLOCK TIME.</b> This
/// fake starts no timer, no hosted loop, and no Quartz-style scheduler — <c>AddRecurring</c>/
/// <c>AddDeferred</c> merely record the registration; a test drives execution explicitly via
/// <see cref="TriggerAsync"/>, mirroring the <c>InMemoryWorkflowDispatcher</c>/
/// <c>InMemorySearchIndex{TDocument}</c> precedent of a controllable, non-wall-clock-driven double.
/// </para>
/// <para>
/// Each registration is stored as a closed generic per command type (zero reflection — no
/// <c>Type.GetMethod</c>/<c>MakeGenericMethod</c>/<c>Invoke</c>, forbidden platform-wide), mirroring
/// the real package's own internal <c>IScheduledJobDefinition</c>/<c>ScheduledJobDefinition&lt;TCommand&gt;</c>
/// split. <see cref="TriggerAsync"/> dispatches the built command directly through a
/// constructor-injected <see cref="ISender"/> — it does not construct a real
/// <see cref="ScheduledCommandJob{TCommand}"/> (which additionally requires an
/// <see cref="Microsoft.Extensions.Logging.ILogger{TCategoryName}"/>), since this fake's job is to
/// prove "the registry dispatched the right command," not to re-exercise
/// <see cref="ScheduledCommandJob{TCommand}"/>'s own already-real, already-pure logging wrapper.
/// </para>
/// <para>
/// References only <c>SharedKernel.Scheduling</c> (plus <c>SharedKernel.Application.Messaging</c>
/// for <see cref="ICommand"/>/<see cref="ISender"/>, which <c>SharedKernel.Scheduling</c> itself
/// depends on for the identical reason — see that package's own reference-justification comment).
/// </para>
/// </remarks>
public sealed class InMemoryScheduledJobRegistry : IScheduledJobRegistry
{
    private readonly ISender _sender;
    private readonly Dictionary<string, IRegisteredJob> _jobs = [];
    private readonly List<string> _fired = [];
    private readonly List<string> _skipped = [];
    private readonly List<string> _misfired = [];
    private readonly HashSet<string> _inFlight = [];

    /// <summary>Creates a new <see cref="InMemoryScheduledJobRegistry"/>.</summary>
    /// <param name="sender">
    /// The <see cref="ISender"/> that <see cref="TriggerAsync"/> dispatches a fired job's built
    /// command through.
    /// </param>
    public InMemoryScheduledJobRegistry(ISender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;
    }

    /// <summary>The names of every job that has fired (a real execution — including a misfire catch-up run), in order, one entry per fire.</summary>
    public IReadOnlyList<string> Fired => _fired;

    /// <summary>The names of every job whose tick was discarded because <see cref="OverlapPolicy"/> was <see cref="OverlapPolicy.Skip"/> while a prior simulated fire was still in flight, in order.</summary>
    public IReadOnlyList<string> Skipped => _skipped;

    /// <summary>The names of every job for which a simulated missed tick was signaled via <see cref="TriggerAsync"/>'s <c>simulatedMisfire</c> parameter, in order — regardless of whether <see cref="MisfirePolicy"/> then discarded it or caught it up.</summary>
    public IReadOnlyList<string> Misfired => _misfired;

    /// <inheritdoc />
    public IScheduledJobRegistry AddRecurring<TCommand>(
        string jobName,
        string cronExpression,
        Func<ScheduledJobExecutionContext, TCommand> commandFactory,
        Action<ScheduledJobOptions> configure)
        where TCommand : class, ICommand
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        Register(jobName, isRecurring: true, cronExpression, deferredFireAtUtc: null, commandFactory, configure);
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
        Register(jobName, isRecurring: false, cronExpression: null, fireAtUtc, commandFactory, configure);
        return this;
    }

    /// <summary>
    /// Marks <paramref name="jobName"/> as "in flight" until the returned handle is disposed, so a
    /// subsequent <see cref="TriggerAsync"/> call for the SAME job exercises its
    /// <see cref="OverlapPolicy"/> exactly as a real overlapping tick would.
    /// </summary>
    /// <param name="jobName">The registered job to mark in flight.</param>
    /// <exception cref="InvalidOperationException">No job named <paramref name="jobName"/> is registered.</exception>
    public IDisposable BeginInFlight(string jobName)
    {
        GetJobOrThrow(jobName);
        _inFlight.Add(jobName);
        return new InFlightHandle(this, jobName);
    }

    /// <summary>
    /// Simulates one tick for <paramref name="jobName"/>: builds its command via the registered
    /// factory and dispatches it through the constructor-injected <see cref="ISender"/> — unless
    /// <see cref="OverlapPolicy"/>/<see cref="MisfirePolicy"/> says otherwise (see remarks).
    /// </summary>
    /// <param name="jobName">The registered job to trigger.</param>
    /// <param name="simulatedNowUtc">
    /// The simulated scheduled/actual fire time passed to the job's <see cref="ScheduledJobExecutionContext"/>.
    /// Never real wall-clock time.
    /// </param>
    /// <param name="fencingToken">
    /// An optional caller-suppliable fencing token, propagated onto <see cref="ScheduledJobExecutionContext.FencingToken"/>
    /// exactly like a real acquired <c>IFencedLock</c> claim would.
    /// </param>
    /// <param name="simulatedMisfire">
    /// When <see langword="true"/>, this tick is treated as a caught-up missed occurrence rather than
    /// an on-time fire: the job is recorded into <see cref="Misfired"/>, and its
    /// <see cref="MisfirePolicy"/> decides whether it then actually dispatches
    /// (<see cref="MisfirePolicy.FireOnce"/>/<see cref="MisfirePolicy.RunImmediatelyThenReschedule"/>)
    /// or is discarded entirely (<see cref="MisfirePolicy.Skip"/>).
    /// </param>
    /// <param name="cancellationToken">The token observed by the dispatched command.</param>
    /// <returns>
    /// The dispatched command's <see cref="SharedKernel.Primitives.Results.Result"/>, or <see langword="null"/>
    /// when the tick was discarded (an <see cref="OverlapPolicy.Skip"/> collision, or a
    /// <see cref="MisfirePolicy.Skip"/> missed occurrence) — nothing was dispatched.
    /// </returns>
    /// <exception cref="InvalidOperationException">No job named <paramref name="jobName"/> is registered.</exception>
    public async Task<SharedKernel.Primitives.Results.Result?> TriggerAsync(
        string jobName,
        DateTimeOffset simulatedNowUtc,
        long? fencingToken = null,
        bool simulatedMisfire = false,
        CancellationToken cancellationToken = default)
    {
        var job = GetJobOrThrow(jobName);

        if (simulatedMisfire)
        {
            _misfired.Add(jobName);

            if (job.Options.MisfirePolicy is MisfirePolicy.Skip)
            {
                return null;
            }
        }

        if (_inFlight.Contains(jobName) && job.Options.OverlapPolicy is OverlapPolicy.Skip)
        {
            _skipped.Add(jobName);
            return null;
        }

        var context = new ScheduledJobExecutionContext
        {
            JobName = jobName,
            ScheduledFireTimeUtc = simulatedNowUtc,
            ActualFireTimeUtc = simulatedNowUtc,
            TenantScope = job.Options.TenantScope,
            FencingToken = fencingToken,
        };

        var result = await job.ExecuteAsync(_sender, context, cancellationToken).ConfigureAwait(false);
        _fired.Add(jobName);
        return result;
    }

    /// <summary>Asserts that <paramref name="jobName"/> fired exactly <paramref name="times"/> times.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="jobName"/> fired a different number of times.</exception>
    public void ShouldHaveFired(string jobName, int times = 1)
    {
        var actual = _fired.Count(f => f == jobName);
        if (actual != times)
        {
            throw new InvalidOperationException(
                $"Expected job '{jobName}' to have fired {times} time(s), but it fired {actual} time(s).");
        }
    }

    /// <summary>Asserts that <paramref name="jobName"/> was skipped at least once due to <see cref="OverlapPolicy.Skip"/>.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="jobName"/> was never skipped.</exception>
    public void ShouldHaveSkipped(string jobName)
    {
        if (!_skipped.Contains(jobName))
        {
            throw new InvalidOperationException($"Expected job '{jobName}' to have been skipped (overlap), but it was not.");
        }
    }

    /// <summary>Asserts that <paramref name="jobName"/> had a simulated misfire recorded at least once.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="jobName"/> never misfired.</exception>
    public void ShouldHaveMisfired(string jobName)
    {
        if (!_misfired.Contains(jobName))
        {
            throw new InvalidOperationException($"Expected job '{jobName}' to have misfired, but it did not.");
        }
    }

    /// <summary>Clears every registration, in-flight marker, and recorded fire/skip/misfire — as if this fake were newly constructed.</summary>
    public void Reset()
    {
        _jobs.Clear();
        _fired.Clear();
        _skipped.Clear();
        _misfired.Clear();
        _inFlight.Clear();
    }

    private void Register<TCommand>(
        string jobName,
        bool isRecurring,
        string? cronExpression,
        DateTimeOffset? deferredFireAtUtc,
        Func<ScheduledJobExecutionContext, TCommand> commandFactory,
        Action<ScheduledJobOptions> configure)
        where TCommand : class, ICommand
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(configure);

        if (_jobs.ContainsKey(jobName))
        {
            throw new ArgumentException($"A job named '{jobName}' is already registered.", nameof(jobName));
        }

        var options = new ScheduledJobOptions();
        configure(options);

        if (options.MisfirePolicy is null || options.OverlapPolicy is null)
        {
            throw new ArgumentException(
                $"The configure delegate for job '{jobName}' must set both {nameof(ScheduledJobOptions.MisfirePolicy)} and {nameof(ScheduledJobOptions.OverlapPolicy)}.",
                nameof(configure));
        }

        _jobs[jobName] = new RegisteredJob<TCommand>
        {
            JobName = jobName,
            IsRecurring = isRecurring,
            CronExpression = cronExpression,
            DeferredFireAtUtc = deferredFireAtUtc,
            Options = options,
            CommandFactory = commandFactory,
        };
    }

    private IRegisteredJob GetJobOrThrow(string jobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        if (!_jobs.TryGetValue(jobName, out var job))
        {
            throw new InvalidOperationException($"No job named '{jobName}' is registered.");
        }

        return job;
    }

    /// <summary>The internal, non-generic storage shape for one registered job — mirrors the real package's own <c>IScheduledJobDefinition</c> split, closed per command type, zero reflection.</summary>
    private interface IRegisteredJob
    {
        ScheduledJobOptions Options { get; }

        Task<SharedKernel.Primitives.Results.Result> ExecuteAsync(ISender sender, ScheduledJobExecutionContext context, CancellationToken cancellationToken);
    }

    private sealed class RegisteredJob<TCommand> : IRegisteredJob
        where TCommand : class, ICommand
    {
        public required string JobName { get; init; }

        public required bool IsRecurring { get; init; }

        public string? CronExpression { get; init; }

        public DateTimeOffset? DeferredFireAtUtc { get; init; }

        public required ScheduledJobOptions Options { get; init; }

        public required Func<ScheduledJobExecutionContext, TCommand> CommandFactory { get; init; }

        public Task<SharedKernel.Primitives.Results.Result> ExecuteAsync(ISender sender, ScheduledJobExecutionContext context, CancellationToken cancellationToken)
        {
            var command = CommandFactory(context);
            return sender.Send(command, cancellationToken);
        }
    }

    private sealed class InFlightHandle(InMemoryScheduledJobRegistry owner, string jobName) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner._inFlight.Remove(jobName);
        }
    }
}
