using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Health;
using SharedKernel.Workflows.Temporal.Hosting;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Workflows;

namespace SharedKernel.Workflows.Temporal.Tests.Hosting;

/// <summary>
/// T-07 — DI registration lifetimes. <see cref="IWorkflowDispatcher"/> resolves scoped;
/// <see cref="ITemporalClient"/>/<see cref="IWorkflowIdFactory"/>/<see cref="IWorkflowServiceProbe"/>
/// resolve as singletons; activities resolve scoped;
/// <see cref="ITemporalRawClientAccessor"/> does not resolve without
/// <see cref="ITemporalWorkflowsBuilder.AllowRawClientAccess"/> and its <c>Warning</c> 17012 is
/// asserted. This package deliberately registers neither <c>ILogger&lt;T&gt;</c> nor <c>IClock</c> —
/// both are registered explicitly here.
/// </summary>
public sealed class DiRegistrationTests
{
    [Workflow]
    private sealed class DiTestWorkflow : WorkflowBase
    {
        [WorkflowRun]
        public Task<string> RunAsync() => Task.FromResult("done");
    }

    private sealed class DiTestActivity : ActivityBase
    {
        public DiTestActivity(ILogger<DiTestActivity> logger, IClock clock) : base(logger, clock)
        {
        }

        [Activity(nameof(DiTestActivity))]
        public Task<string> RunAsync() => Task.FromResult("done");
    }

    private static IConfiguration ValidConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:Temporal:TargetHost"] = "localhost:59999",
            ["Workflows:Temporal:Namespace"] = "default",
        })
        .Build();

    private static IServiceCollection ClientOnlyServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FixedClock());
        services.AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AsClientOnly()
            .Build();
        return services;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.DateTime);
    }

    [Fact]
    public void IWorkflowDispatcher_ResolvesScoped()
    {
        using ServiceProvider provider = ClientOnlyServices().BuildServiceProvider();

        using IServiceScope scopeA = provider.CreateScope();
        var dispatcherA1 = scopeA.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        var dispatcherA2 = scopeA.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        using IServiceScope scopeB = provider.CreateScope();
        var dispatcherB1 = scopeB.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        ReferenceEquals(dispatcherA1, dispatcherA2).Should().BeTrue(because: "scoped resolves the same instance within one scope");
        ReferenceEquals(dispatcherA1, dispatcherB1).Should().BeFalse(because: "scoped must not share an instance across scopes");
    }

    [Fact]
    public void ITemporalClient_ResolvesSingleton()
    {
        using ServiceProvider provider = ClientOnlyServices().BuildServiceProvider();

        var clientFromRoot = provider.GetRequiredService<ITemporalClient>();
        using IServiceScope scope = provider.CreateScope();
        var clientFromScope = scope.ServiceProvider.GetRequiredService<ITemporalClient>();

        ReferenceEquals(clientFromRoot, clientFromScope).Should().BeTrue();
    }

    [Fact]
    public void IWorkflowIdFactory_ResolvesSingleton()
    {
        using ServiceProvider provider = ClientOnlyServices().BuildServiceProvider();

        var a = provider.GetRequiredService<IWorkflowIdFactory>();
        using IServiceScope scope = provider.CreateScope();
        var b = scope.ServiceProvider.GetRequiredService<IWorkflowIdFactory>();

        ReferenceEquals(a, b).Should().BeTrue();
    }

    [Fact]
    public void IWorkflowServiceProbe_ResolvesSingleton()
    {
        using ServiceProvider provider = ClientOnlyServices().BuildServiceProvider();

        var a = provider.GetRequiredService<IWorkflowServiceProbe>();
        using IServiceScope scope = provider.CreateScope();
        var b = scope.ServiceProvider.GetRequiredService<IWorkflowServiceProbe>();

        ReferenceEquals(a, b).Should().BeTrue();
    }

    [Fact]
    public void Activities_ResolveScoped()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FixedClock());
        services.AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AddWorkflow<DiTestWorkflow>()
            .AddActivities<DiTestActivity>()
            .WithWorker("di-test-queue")
            .Build();

        using ServiceProvider provider = services.BuildServiceProvider();

        using IServiceScope scopeA = provider.CreateScope();
        var activityA1 = scopeA.ServiceProvider.GetRequiredService<DiTestActivity>();
        var activityA2 = scopeA.ServiceProvider.GetRequiredService<DiTestActivity>();

        using IServiceScope scopeB = provider.CreateScope();
        var activityB1 = scopeB.ServiceProvider.GetRequiredService<DiTestActivity>();

        ReferenceEquals(activityA1, activityA2).Should().BeTrue();
        ReferenceEquals(activityA1, activityB1).Should().BeFalse();
    }

    [Fact]
    public void ITemporalRawClientAccessor_DoesNotResolve_WithoutAllowRawClientAccess()
    {
        using ServiceProvider provider = ClientOnlyServices().BuildServiceProvider();

        var accessor = provider.GetService<ITemporalRawClientAccessor>();

        accessor.Should().BeNull();
    }

    [Fact]
    public void ITemporalRawClientAccessor_Resolves_WithAllowRawClientAccess_AndLogsWarning17012()
    {
        var loggerFactory = new SharedKernel.Testing.Logging.InMemoryLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<IClock>(new FixedClock());
        services.AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AsClientOnly()
            .AllowRawClientAccess()
            .Build();

        using ServiceProvider provider = services.BuildServiceProvider();

        var accessor = provider.GetService<ITemporalRawClientAccessor>();

        accessor.Should().NotBeNull();
        loggerFactory.Loggers.Values
            .SelectMany(logger => logger.Records)
            .Should().Contain(record => record.EventId.Id == 17012 && record.LogLevel == LogLevel.Warning);
    }
}
