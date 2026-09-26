using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Scheduling.Extensions;
using SharedKernel.Scheduling.Hosting;
using SharedKernel.Scheduling.Options;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Scheduling.Tests.TestSupport;

/// <summary>
/// Builds a minimal, fully-composed DI container around <c>AddSharedKernelScheduling</c> for
/// integration-shaped tests, with direct access to the internal <see cref="SchedulingHostedService"/>
/// (via this test project's <c>InternalsVisibleTo</c> grant) so tests can start/stop it precisely
/// without pulling in the full generic <c>IHost</c> machinery.
/// </summary>
internal sealed class SchedulingTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private SchedulingTestHarness(
        ServiceProvider provider,
        FakeClock clock,
        InMemoryLoggerFactory loggerFactory,
        RecordingCommandRecorder recorder,
        SchedulingHostedService hostedService)
    {
        _provider = provider;
        Clock = clock;
        LoggerFactory = loggerFactory;
        Recorder = recorder;
        HostedService = hostedService;
    }

    public FakeClock Clock { get; }

    public InMemoryLoggerFactory LoggerFactory { get; }

    public RecordingCommandRecorder Recorder { get; }

    public SchedulingHostedService HostedService { get; }

    public IServiceProvider Services => _provider;

    /// <summary>Records captured by <see cref="SchedulingHostedService"/>'s own log category.</summary>
    public IReadOnlyList<LogRecord> HostedServiceLogRecords =>
        LoggerFactory.GetLogger(typeof(SchedulingHostedService).FullName!).Records;

    public static SchedulingTestHarness Build(
        Action<ISchedulingBuilder> registerJobs,
        Action<SchedulingOptions>? configureOptions = null,
        DateTimeOffset? initialClock = null,
        IDistributedLockService? lockService = null)
    {
        ArgumentNullException.ThrowIfNull(registerJobs);

        var services = new ServiceCollection();
        // AddSharedKernelScheduling binds SchedulingOptions via OptionsBuilder.BindConfiguration,
        // which resolves IConfiguration lazily from the container the first time the options value
        // is materialized (e.g. when the hosted loop starts) — a bare ServiceCollection-based test
        // harness needs an IConfiguration registered up front, even an empty one, exactly as the
        // real generic host always provides automatically.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var clock = new FakeClock(initialClock);
        services.AddSingleton<IClock>(clock);
        services.AddInMemoryLoggerFactory();
        services.AddSharedKernelApplication(typeof(RecordingCommand).Assembly, app => app.UseMediatR());
        services.AddSingleton<RecordingCommandRecorder>();

        if (lockService is not null)
        {
            services.AddSingleton(lockService);
        }

        // 150ms — comfortably inside SchedulingOptions.TickInterval's [Range] floor of 100ms
        // (below-floor values fail DataAnnotations validation the first time IOptions<T>.Value is
        // read, which happens inside the hosted loop's background task where nothing in this bypass
        // harness observes the fault — it silently stops without ever ticking).
        ISchedulingBuilder builder = services.AddSharedKernelScheduling(configureOptions ??= o => o.TickInterval = TimeSpan.FromMilliseconds(150));
        registerJobs(builder);

        ServiceProvider provider = services.BuildServiceProvider();
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var recorder = provider.GetRequiredService<RecordingCommandRecorder>();
        var hostedService = provider.GetRequiredService<SchedulingHostedService>();

        return new SchedulingTestHarness(provider, clock, loggerFactory, recorder, hostedService);
    }

    /// <summary>
    /// Starts the hosted loop and waits for its startup prefix to have genuinely run before returning.
    /// </summary>
    /// <remarks>
    /// The real <c>BackgroundService.StartAsync</c> (verified by decompiling
    /// <c>Microsoft.Extensions.Hosting.Abstractions</c> 10.0.0) schedules <c>ExecuteAsync</c> via
    /// <c>Task.Run(...)</c> and returns <c>Task.CompletedTask</c> immediately — it does NOT wait for
    /// even the synchronous prefix (computing every job's initial <c>NextFireTimeUtc</c> from
    /// <c>IClock.UtcNow</c>, then setting <c>IsRunning = true</c>) to have executed. A caller that
    /// manipulates <see cref="Clock"/> immediately after awaiting a naive <c>StartAsync</c> call races
    /// that prefix: if the clock advances before the prefix reads <c>IClock.UtcNow</c>, the very first
    /// <c>NextFireTimeUtc</c> is computed from the wrong instant and the job silently never becomes due
    /// on the occurrence the test expects. This method closes that race by polling for
    /// <see cref="Hosting.SchedulingHostedService.IsRunning"/> — set at the very end of that prefix —
    /// before returning control to the caller.
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await HostedService.StartAsync(cancellationToken).ConfigureAwait(false);

        bool started = await Eventually.UntilAsync(() => HostedService.IsRunning, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        if (!started)
        {
            throw new InvalidOperationException(
                "SchedulingHostedService did not report IsRunning within the startup wait window.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => HostedService.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}
