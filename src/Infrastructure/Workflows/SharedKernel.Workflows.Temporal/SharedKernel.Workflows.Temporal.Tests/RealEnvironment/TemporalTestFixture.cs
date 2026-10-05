using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Workflows.Temporal.Hosting;
using Temporalio.Client;
using Temporalio.Testing;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// Shared <see cref="WorkflowEnvironment"/> (time-skipping) plus one worker-hosting DI composition,
/// reused across the entire real-environment test collection (T-09..T-15). Starting a fresh
/// time-skipping test server per test class would be wasteful; xUnit collection fixtures share one
/// instance across every test class in the collection.
/// </summary>
public sealed class TemporalTestFixture : IAsyncLifetime
{
    /// <summary>The task queue every sample workflow/activity in this fixture is registered against.</summary>
    public const string TaskQueue = "sk-workflows-tests-queue";

    private List<IHostedService> _hostedServices = [];
    private ServiceProvider? _provider;

    /// <summary>The shared time-skipping <see cref="WorkflowEnvironment"/>.</summary>
    public WorkflowEnvironment Environment { get; private set; } = null!;

    /// <summary>The DI root built from a worker-hosting <c>AddSharedKernelTemporalWorkflows(...)</c> composition.</summary>
    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Fixture not initialised.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        Environment = await WorkflowEnvironment.StartTimeSkippingAsync();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workflows:Temporal:TargetHost"] = Environment.Client.Connection.Options.TargetHost,
                ["Workflows:Temporal:Namespace"] = Environment.Client.Options.Namespace,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FakeClock());
        services
            .AddSharedKernelTemporalWorkflows(configuration)
            .AddWorkflow<EchoWorkflow>()
            .AddWorkflow<DelayWorkflow>()
            .AddWorkflow<TimerVsSignalWorkflow>()
            .AddWorkflow<RetryExhaustionWorkflow>()
            .AddWorkflow<ScheduleToCloseTimeoutWorkflow>()
            .AddWorkflow<CompensatingWorkflow>()
            .AddWorkflow<PropagationParentWorkflow>()
            .AddWorkflow<PropagationChildWorkflow>()
            .AddActivities<EchoActivity>()
            .AddActivities<FlakyActivity>()
            .AddActivities<NeverCompletingActivity>()
            .AddActivities<CompensationActivity>()
            .AddActivities<PropagationActivity>()
            .WithWorker(TaskQueue)
            .Build();

        _provider = services.BuildServiceProvider();
        _hostedServices = [.. _provider.GetServices<IHostedService>()];
        foreach (IHostedService hostedService in _hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        foreach (IHostedService hostedService in _hostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        await Environment.DisposeAsync();
    }

    /// <summary>Creates a DI scope for resolving <c>IWorkflowDispatcher</c> and other scoped services.</summary>
    public IServiceScope CreateScope() => Services.CreateScope();
}

/// <summary>xUnit collection definition sharing one <see cref="TemporalTestFixture"/> across the whole real-environment test collection.</summary>
[CollectionDefinition(Name)]
public sealed class TemporalEnvironmentCollection : ICollectionFixture<TemporalTestFixture>
{
    /// <summary>The collection name every real-environment test class opts into via <c>[Collection(Name)]</c>.</summary>
    public const string Name = "TemporalEnvironment";
}
