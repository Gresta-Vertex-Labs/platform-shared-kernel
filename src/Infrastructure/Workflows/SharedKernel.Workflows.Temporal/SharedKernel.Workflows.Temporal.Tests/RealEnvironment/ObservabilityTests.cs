using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;
using SharedKernel.Workflows.Temporal.Constants;
using SharedKernel.Workflows.Temporal.Diagnostics;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Hosting;
using SharedKernel.Workflows.Temporal.Interception;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Common;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Worker;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// Telemetry, metrics and log hygiene of the real composition with <c>.WithOpenTelemetry()</c> and
/// <c>.WithMetrics()</c>, against the shared <see cref="Temporalio.Testing.WorkflowEnvironment"/>. Span source names are
/// read from the SDK's own <see cref="TracingInterceptor"/> objects so a rename fails the test.
/// </summary>
/// <remarks>
/// Replay safety: the tracing, propagation and log-signature changes emit no workflow commands, so a history captured
/// with the interceptors composed replays against the same workflow code with them composed; a non-deterministic variant
/// proves the replay has teeth.
/// </remarks>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class ObservabilityTests(TemporalTestFixture fixture) : IAsyncLifetime
{
    private const string ObservedQueue = "sk-workflows-observability-queue";

    private ActivityRecorder _spans = null!;
    private MetricRecorder _metrics = null!;
    private ServiceProvider _provider = null!;
    private InMemoryLoggerFactory _loggers = null!;
    private List<IHostedService> _hosted = [];

    static ObservabilityTests()
    {
        // Without the OpenTelemetry SDK in the process the API's default propagator injects and extracts nothing, so
        // the SDK's workflow and activity spans would not parent to the client span. A host gets the same composite
        // from AddServiceDefaults(); the test installs it explicitly.
        OpenTelemetry.Sdk.SetDefaultTextMapPropagator(
            new OpenTelemetry.Context.Propagation.CompositeTextMapPropagator(
            [
                new OpenTelemetry.Context.Propagation.TraceContextPropagator(),
                new OpenTelemetry.Context.Propagation.BaggagePropagator(),
            ]));
    }

    private static string[] SdkSourceNames =>
    [
        TracingInterceptor.ClientSource.Name,
        TracingInterceptor.WorkflowsSource.Name,
        TracingInterceptor.ActivitiesSource.Name,
    ];

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _spans = ActivityRecorder.StartRecording(SdkSourceNames);
        _metrics = MetricRecorder.StartRecording(WorkflowWellKnown.MeterName);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workflows:Temporal:TargetHost"] = fixture.Environment.Client.Connection.Options.TargetHost,
                ["Workflows:Temporal:Namespace"] = fixture.Environment.Client.Options.Namespace,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSingleton<IClock>(new FakeClock());
        services
            .AddSharedKernelTemporalWorkflows(configuration)
            .AddWorkflow<EchoWorkflow>()
            .AddWorkflow<TimerVsSignalWorkflow>()
            .AddActivities<EchoActivity>()
            .WithWorker(ObservedQueue)
            .WithOpenTelemetry()
            .WithMetrics()
            .Build();

        _provider = services.BuildServiceProvider();
        _loggers = (InMemoryLoggerFactory)_provider.GetRequiredService<ILoggerFactory>();
        _hosted = [.. _provider.GetServices<IHostedService>()];
        foreach (IHostedService hosted in _hosted)
        {
            await hosted.StartAsync(CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        foreach (IHostedService hosted in _hosted)
        {
            await hosted.StopAsync(CancellationToken.None);
        }

        await _provider.DisposeAsync();
        _spans.Dispose();
        _metrics.Dispose();
    }

    private static WorkflowStartOptions Options(string businessKey) => new()
    {
        TaskQueue = ObservedQueue,
        BusinessKey = businessKey,
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    private async Task<IWorkflowHandle<string>> RunEchoAsync(string businessKey)
    {
        using IServiceScope scope = _provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        Result<IWorkflowHandle<string>> started = await dispatcher.StartAsync<EchoWorkflow, string, string>(
            "observed-input", Options(businessKey), TenantScope.For(TestTenants.Observability));
        started.IsSuccess.Should().BeTrue();
        Result<string> result = await started.Value.GetResultAsync();
        result.IsSuccess.Should().BeTrue();
        return started.Value;
    }

    private List<LogRecord> PackageRecords() =>
        [.. _loggers.Loggers.Values
            .SelectMany(logger => logger.Records)
            .Where(record => record.EventId.Id is >= 17000 and <= 17099)];

    [Fact]
    public async Task Spans_AStartAWorkflowAndAnActivitySpan_AreRecordedFromTheSdkSources_ParentLinked_AndLeakNothing()
    {
        string businessKey = $"obs-span-{Guid.NewGuid():N}";

        IWorkflowHandle<string> handle = await RunEchoAsync(businessKey);

        IReadOnlyList<System.Diagnostics.Activity> recorded = _spans.RecordedActivities;
        System.Diagnostics.Activity start = recorded.First(a => a.Source.Name == TracingInterceptor.ClientSource.Name);
        System.Diagnostics.Activity workflow = recorded.First(a => a.Source.Name == TracingInterceptor.WorkflowsSource.Name);
        System.Diagnostics.Activity activity = recorded.First(a => a.Source.Name == TracingInterceptor.ActivitiesSource.Name);

        workflow.TraceId.Should().Be(start.TraceId);
        activity.TraceId.Should().Be(start.TraceId);
        workflow.ParentSpanId.Should().Be(start.SpanId, because: "the workflow span is parented to the client start span through the Temporal header");

        recorded.Should().OnlyContain(a => a.Source.Name.StartsWith("Temporalio.Extensions.OpenTelemetry.", StringComparison.Ordinal));
        recorded
            .Where(a => a.TraceId == start.TraceId)
            .ShouldNotLeak(handle.WorkflowId, TestTenants.Observability.ToString(), businessKey);

        string? workflowIdTag = new TracingInterceptorOptions().TagNameWorkflowId;
        workflowIdTag.Should().NotBeNullOrEmpty();
        recorded.Should().OnlyContain(a => !a.TagObjects.Any(tag => tag.Key == workflowIdTag));
    }

    [Fact]
    public async Task Spans_TheSdkDefaultTracingInterceptor_DoesTagTheWorkflowId_SoTheSuppressionIsTheOnlyReasonItIsAbsent()
    {
        string businessKey = $"obs-default-{Guid.NewGuid():N}";
        ITemporalClient defaultTracingClient = await TemporalClient.ConnectAsync(
            new TemporalClientConnectOptions(fixture.Environment.Client.Connection.Options.TargetHost!)
            {
                Namespace = fixture.Environment.Client.Options.Namespace,
                Interceptors = [new TracingInterceptor()],
            });
        string workflowId = $"{TestTenants.Observability}:EchoWorkflow:{businessKey}";

        WorkflowHandle handle = await defaultTracingClient.StartWorkflowAsync(
            "EchoWorkflow", ["x"], new WorkflowOptions(workflowId, TemporalTestFixture.TaskQueue));
        await handle.GetResultAsync<string>();

        Action act = () => _spans.RecordedActivities.ShouldNotLeak(workflowId);
        act.Should().Throw<InvalidOperationException>(
            because: "the SDK default tags every span with the workflow id, which is {tenant}:{type}:{businessKey}");
    }

    [Fact]
    public async Task Spans_ARecorderOverTheSharedKernelPrefix_SeesNoSpanFromThisPackage()
    {
        using ActivityRecorder kernelSources = ActivityRecorder.StartRecordingPrefix("SharedKernel.");

        await RunEchoAsync($"obs-prefix-{Guid.NewGuid():N}");

        kernelSources.RecordedActivities.Should().BeEmpty(because: "this package owns no ActivitySource");
    }

    [Fact]
    public async Task Metrics_TheSdkInstruments_ArriveOnTheKernelMeter_WhichCarriesTheAssemblyVersion()
    {
        await RunEchoAsync($"obs-metrics-{Guid.NewGuid():N}");
        _metrics.Collect();

        _metrics.Instruments.Should().NotBeEmpty(because: "WithMetrics() hands the meter to the SDK's CustomMetricMeter");
        _metrics.Instruments.Should().OnlyContain(i => i.MeterName == WorkflowWellKnown.MeterName);
        WorkflowWellKnown.MeterName.Should().Be("SharedKernel.Workflows");
        WorkflowDiagnostics.Meter.Name.Should().Be(WorkflowWellKnown.MeterName);
        WorkflowDiagnostics.Meter.Version.Should()
            .NotBeNullOrWhiteSpace()
            .And.Be(TelemetryVersion.Of(typeof(WorkflowDiagnostics).Assembly));
    }

    [Fact]
    public async Task Logs_AStartedWorkflow_IsLoggedWithoutTheWorkflowIdTenantOrBusinessKey()
    {
        string businessKey = $"obs-log-{Guid.NewGuid():N}";

        IWorkflowHandle<string> handle = await RunEchoAsync(businessKey);

        List<LogRecord> records = PackageRecords();
        records.ShouldHaveLogged(new EventId(17000)).Template.Should().NotContain("WorkflowId");
        records.ShouldNotLeak(handle.WorkflowId, TestTenants.Observability.ToString(), businessKey);
    }

    [Fact]
    public async Task Logs_ASignalAndAStart_LeakNeitherTheWorkflowIdNorTheBusinessKey()
    {
        string businessKey = $"obs-signal-{Guid.NewGuid():N}";
        using IServiceScope scope = _provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        Result<IWorkflowHandle> started = await dispatcher.StartAsync<TimerVsSignalWorkflow>(
            Options(businessKey), TenantScope.For(TestTenants.Observability));
        started.IsSuccess.Should().BeTrue();

        Result signalled = await started.Value.SignalAsync("Signal", "go");

        signalled.IsSuccess.Should().BeTrue();
        PackageRecords().ShouldNotLeak(started.Value.WorkflowId, TestTenants.Observability.ToString(), businessKey);
    }

    [Fact]
    public async Task Logs_AWorkflowStartedWithoutTheTenantHeader_LogsEvent17005WithoutTheWorkflowId()
    {
        string businessKey = $"obs-notenant-{Guid.NewGuid():N}";
        string workflowId = $"untenanted:EchoWorkflow:{businessKey}";

        // The shared environment client has no propagation interceptor, so no tenant header is written.
        WorkflowHandle handle = await fixture.Environment.Client.StartWorkflowAsync(
            "EchoWorkflow", ["x"], new WorkflowOptions(workflowId, ObservedQueue));
        await handle.GetResultAsync<string>();

        List<LogRecord> records = PackageRecords();
        LogRecord missing = records.ShouldHaveLogged(new EventId(17005), LogLevel.Warning);
        // Exactly once: the propagation interceptor is registered on the client only; the SDK chains it into the worker,
        // so it runs (and logs) once per workflow.
        records.ShouldHaveLoggedCount(new EventId(17005), 1);
        missing.Template.Should().NotContain("WorkflowId");
        records.ShouldNotLeak(workflowId, businessKey);
    }

    [Fact]
    public async Task Replay_ARealHistory_ReplaysWithTheTracingAndPropagationInterceptorsComposed()
    {
        IWorkflowHandle<string> handle = await RunEchoAsync($"obs-replay-{Guid.NewGuid():N}");
        WorkflowHistory history = await fixture.Environment.Client.GetWorkflowHandle(handle.WorkflowId).FetchHistoryAsync();

        WorkflowReplayResult good = await ReplayAsync<EchoWorkflow>(history);
        WorkflowReplayResult bad = await ReplayAsync<BadEchoWorkflowVariant>(history);

        good.ReplayFailure.Should().BeNull();
        bad.ReplayFailure.Should().NotBeNull(because: "a variant that schedules the activity twice diverges from history");
    }

    private async Task<WorkflowReplayResult> ReplayAsync<TWorkflow>(WorkflowHistory history)
    {
        var options = new WorkflowReplayerOptions
        {
            Namespace = fixture.Environment.Client.Options.Namespace,
            TaskQueue = "sk-workflows-observability-replayer-queue",
            Interceptors =
            [
                new WorkflowPropagationInterceptor(),
                new TracingInterceptor(new TracingInterceptorOptions { TagNameWorkflowId = null }),
            ],
        };
        options.AddWorkflow(typeof(TWorkflow));
        return await new WorkflowReplayer(options).ReplayWorkflowAsync(history, throwOnReplayFailure: false);
    }
}
