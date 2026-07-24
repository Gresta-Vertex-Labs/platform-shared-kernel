using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Hosting;
using Temporalio.Activities;
using Temporalio.Workflows;

namespace SharedKernel.Workflows.Temporal.Tests.Hosting;

/// <summary>
/// T-08 — <see cref="ITemporalWorkflowsBuilder.Build"/> composition-validation tests. A worker with
/// zero workflows and zero activities, a duplicate task queue, a non-<c>[Workflow]</c> type,
/// <see cref="ITemporalWorkflowsBuilder.WithPayloadEncryption"/> with no key, and
/// <see cref="ITemporalWorkflowsBuilder.AddWorkflow{TWorkflow}"/> after
/// <see cref="ITemporalWorkflowsBuilder.AsClientOnly"/> each fail at <c>Build()</c>/startup with the
/// named error. <see cref="ITemporalWorkflowsBuilder.AsClientOnly"/> registers no
/// <see cref="IHostedService"/>.
/// </summary>
public sealed class BuildCompositionValidationTests
{
    [Workflow]
    private sealed class ValidWorkflow : WorkflowBase
    {
        [WorkflowRun]
        public Task<string> RunAsync() => Task.FromResult("done");
    }

    // Deliberately NOT [Workflow]-attributed — used to prove AddWorkflow<T>() rejects a non-workflow type.
    private sealed class NotAWorkflow : WorkflowBase
    {
        public Task<string> RunAsync() => Task.FromResult("done");
    }

    private sealed class ValidActivity : ActivityBase
    {
        public ValidActivity(ILogger<ValidActivity> logger, IClock clock) : base(logger, clock)
        {
        }

        [Activity(nameof(ValidActivity))]
        public Task<string> RunAsync() => Task.FromResult("done");
    }

    private static IConfiguration ValidConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:Temporal:TargetHost"] = "localhost:59999",
            ["Workflows:Temporal:Namespace"] = "default",
        })
        .Build();

    private static IConfiguration ValidConfigurationWithoutEncryptionKey() => ValidConfiguration();

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FixedClock());
        return services;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.DateTime);
    }

    [Fact]
    public void Build_WorkerWithZeroWorkflowsAndZeroActivities_Throws()
    {
        ServiceCollection services = NewServices();
        ITemporalWorkflowsBuilder builder = services
            .AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .WithWorker("empty-queue");

        Action act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void WithWorker_CalledTwice_ThrowsBeforeBuildEvenRuns()
    {
        ServiceCollection services = NewServices();
        ITemporalWorkflowsBuilder builder = services
            .AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AddWorkflow<ValidWorkflow>()
            .WithWorker("queue-a");

        Action act = () => builder.WithWorker("queue-b");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Build_NonWorkflowTypePassedToAddWorkflow_Throws()
    {
        ServiceCollection services = NewServices();
        ITemporalWorkflowsBuilder builder = services
            .AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AddWorkflow<NotAWorkflow>()
            .WithWorker("bad-workflow-queue");

        Action act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Build_WithPayloadEncryption_NoConfiguredKey_Throws()
    {
        ServiceCollection services = NewServices();
        ITemporalWorkflowsBuilder builder = services
            .AddSharedKernelTemporalWorkflows(ValidConfigurationWithoutEncryptionKey())
            .AsClientOnly()
            .WithPayloadEncryption();

        Action act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddWorkflow_AfterAsClientOnly_Throws()
    {
        ServiceCollection services = NewServices();
        ITemporalWorkflowsBuilder builder = services
            .AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AsClientOnly();

        Action act = () => builder.AddWorkflow<ValidWorkflow>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddActivities_AfterAsClientOnly_Throws()
    {
        ServiceCollection services = NewServices();
        ITemporalWorkflowsBuilder builder = services
            .AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AsClientOnly();

        Action act = () => builder.AddActivities<ValidActivity>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void WithWorker_AfterAsClientOnly_Throws()
    {
        ServiceCollection services = NewServices();
        ITemporalWorkflowsBuilder builder = services
            .AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AsClientOnly();

        Action act = () => builder.WithWorker("some-queue");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AsClientOnly_AfterAddWorkflow_Throws()
    {
        ServiceCollection services = NewServices();
        ITemporalWorkflowsBuilder builder = services
            .AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AddWorkflow<ValidWorkflow>();

        Action act = () => builder.AsClientOnly();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Build_ValidWorkerComposition_Succeeds()
    {
        ServiceCollection services = NewServices();

        Action act = () => services
            .AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AddWorkflow<ValidWorkflow>()
            .AddActivities<ValidActivity>()
            .WithWorker("valid-queue")
            .Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void AsClientOnly_RegistersNoHostedService()
    {
        ServiceCollection services = NewServices();
        services
            .AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AsClientOnly()
            .Build();

        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void WorkerHosting_RegistersAtLeastOneHostedService()
    {
        ServiceCollection services = NewServices();
        services
            .AddSharedKernelTemporalWorkflows(ValidConfiguration())
            .AddWorkflow<ValidWorkflow>()
            .WithWorker("hosted-queue")
            .Build();

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IHostedService));
    }
}
