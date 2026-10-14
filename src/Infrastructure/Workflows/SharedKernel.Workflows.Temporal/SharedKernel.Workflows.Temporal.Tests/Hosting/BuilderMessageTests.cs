using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Failures;
using SharedKernel.Workflows.Temporal.Hosting;
using Temporalio.Workflows;

namespace SharedKernel.Workflows.Temporal.Tests.Hosting;

/// <summary>
/// The composition errors of the builder stay <see cref="InvalidOperationException"/>: each message is one sentence that
/// names the remedy, carries no <c>Error</c> code, em-dash or cause text, and the registration failure keeps the SDK
/// exception as the inner exception.
/// </summary>
public sealed class BuilderMessageTests
{
    [Workflow]
    private sealed class ValidWorkflow : WorkflowBase
    {
        [WorkflowRun]
        public Task<string> RunAsync() => Task.FromResult("done");
    }

    private sealed class NotAWorkflow : WorkflowBase
    {
        public Task<string> RunAsync() => Task.FromResult("done");
    }

    private static IConfiguration Configuration(bool targetHost = true, bool @namespace = true)
    {
        var values = new Dictionary<string, string?>();
        if (targetHost)
        {
            values["Workflows:Temporal:TargetHost"] = "localhost:59999";
        }

        if (@namespace)
        {
            values["Workflows:Temporal:Namespace"] = "default";
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new SharedKernel.Testing.Clocks.FakeClock());
        return services;
    }

    private static void AssertRemedySentence(InvalidOperationException exception, params string[] remedyFragments)
    {
        string message = exception.Message;
        message.Should().MatchRegex("^[A-Z'].*\\.$", because: "a message is a sentence ending in a period");
        message.Should().NotMatchRegex("workflow\\.[a-z]", because: "no Error code is embedded").And.NotContain("—");
        message.Should().ContainAny(remedyFragments, because: "a message names the remedy");
    }

    [Fact]
    public void WithWorker_CalledTwice_NamesTheQueueAndTheRemedy()
    {
        ITemporalWorkflowsBuilder builder = NewServices().AddSharedKernelTemporalWorkflows(Configuration())
            .AddWorkflow<ValidWorkflow>().WithWorker("queue-a");

        InvalidOperationException exception = FluentActions.Invoking(() => builder.WithWorker("queue-b"))
            .Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("'queue-a'");
        AssertRemedySentence(exception, "remove the second WithWorker call");
    }

    [Fact]
    public void AsClientOnly_AfterWorkerRegistrations_NamesTheRemedy()
    {
        ITemporalWorkflowsBuilder builder = NewServices().AddSharedKernelTemporalWorkflows(Configuration()).AddWorkflow<ValidWorkflow>();

        InvalidOperationException exception = FluentActions.Invoking(() => builder.AsClientOnly())
            .Should().Throw<InvalidOperationException>().Which;

        AssertRemedySentence(exception, "Remove AsClientOnly()");
    }

    [Fact]
    public void WorkerRegistration_AfterAsClientOnly_NamesTheRemedy()
    {
        ITemporalWorkflowsBuilder builder = NewServices().AddSharedKernelTemporalWorkflows(Configuration()).AsClientOnly();

        InvalidOperationException exception = FluentActions.Invoking(() => builder.AddWorkflow<ValidWorkflow>())
            .Should().Throw<InvalidOperationException>().Which;

        AssertRemedySentence(exception, "Remove AsClientOnly()");
    }

    [Fact]
    public void Build_Twice_NamesTheRemedy()
    {
        ITemporalWorkflowsBuilder builder = NewServices().AddSharedKernelTemporalWorkflows(Configuration()).AsClientOnly();
        builder.Build();

        InvalidOperationException exception = FluentActions.Invoking(() => builder.Build())
            .Should().Throw<InvalidOperationException>().Which;

        AssertRemedySentence(exception, "Call Build() once");
    }

    [Fact]
    public void Build_WithoutATaskQueue_NamesBothRemedies()
    {
        ITemporalWorkflowsBuilder builder = NewServices().AddSharedKernelTemporalWorkflows(Configuration()).AddWorkflow<ValidWorkflow>();

        InvalidOperationException exception = FluentActions.Invoking(() => builder.Build())
            .Should().Throw<InvalidOperationException>().Which;

        AssertRemedySentence(exception, "Call WithWorker(taskQueue)");
        exception.Message.Should().Contain("AsClientOnly()");
    }

    [Fact]
    public void Build_AnEmptyWorker_NamesTheQueueAndTheRemedy()
    {
        ITemporalWorkflowsBuilder builder = NewServices().AddSharedKernelTemporalWorkflows(Configuration()).WithWorker("empty-queue");

        InvalidOperationException exception = FluentActions.Invoking(() => builder.Build())
            .Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("'empty-queue'");
        AssertRemedySentence(exception, "Register them with AddWorkflow<T>()");
    }

    [Fact]
    public void Build_ATypeWithoutTheWorkflowAttribute_KeepsTheSdkExceptionAsInner_AndNeverItsText()
    {
        ITemporalWorkflowsBuilder builder = NewServices().AddSharedKernelTemporalWorkflows(Configuration())
            .AddWorkflow<NotAWorkflow>().WithWorker("bad-queue");

        InvalidOperationException exception = FluentActions.Invoking(() => builder.Build())
            .Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("'NotAWorkflow'");
        AssertRemedySentence(exception, "Mark the type with [Workflow]");
        exception.InnerException.Should().BeOfType<ArgumentException>();
        exception.Message.Should().NotContain(exception.InnerException!.Message);
    }

    [Fact]
    public void Build_WithoutTargetHost_NamesTheKey()
    {
        ITemporalWorkflowsBuilder builder = NewServices().AddSharedKernelTemporalWorkflows(Configuration(targetHost: false)).AsClientOnly();

        InvalidOperationException exception = FluentActions.Invoking(() => builder.Build())
            .Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("'Workflows:Temporal:TargetHost'");
        AssertRemedySentence(exception, "Set it to the Temporal service address");
    }

    [Fact]
    public void Build_WithoutNamespace_NamesTheKey()
    {
        ITemporalWorkflowsBuilder builder = NewServices().AddSharedKernelTemporalWorkflows(Configuration(@namespace: false)).AsClientOnly();

        InvalidOperationException exception = FluentActions.Invoking(() => builder.Build())
            .Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("'Workflows:Temporal:Namespace'");
        AssertRemedySentence(exception, "Set it to the Temporal namespace");
    }

    [Fact]
    public void ToFailure_OfASuccessfulResult_IsAProgrammingErrorSentence()
    {
        InvalidOperationException exception = FluentActions.Invoking(() => WorkflowFailureMapper.ToFailure(Result.Success()))
            .Should().Throw<InvalidOperationException>().Which;

        AssertRemedySentence(exception, "check IsFailure");
    }
}
