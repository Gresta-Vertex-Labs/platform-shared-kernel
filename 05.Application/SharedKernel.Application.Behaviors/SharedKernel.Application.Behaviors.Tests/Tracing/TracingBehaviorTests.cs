using System.Diagnostics;
using FluentAssertions;
using SharedKernel.Application.Behaviors.Tracing;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Tracing;

public sealed class TracingBehaviorTests
{
    // The documented public ActivitySource name TracingBehavior emits under — 13.ServiceDefaults
    // subscribes to this same literal at the host level. ApplicationDiagnostics itself is internal.
    private const string ActivitySourceName = "SharedKernel.Application";

    private sealed record TestCommand : ICommand;

    private static ActivityListener Listen(List<Activity> captured)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = captured.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public async Task Handle_Success_RecordsSpanWithRequestTags()
    {
        var captured = new List<Activity>();
        using var listener = Listen(captured);
        var behavior = new TracingBehavior<TestCommand, Result>();

        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        captured.Should().ContainSingle();
        var activity = captured[0];
        activity.OperationName.Should().Be(nameof(TestCommand));
        activity.GetTagItem("request.kind").Should().Be("command");
        activity.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Handle_ResultFailure_SetsErrorStatusAndErrorTags()
    {
        var captured = new List<Activity>();
        using var listener = Listen(captured);
        var behavior = new TracingBehavior<TestCommand, Result>();
        var error = Error.Conflict("test.conflict", "conflict");

        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Failure(error)), CancellationToken.None);

        var activity = captured.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem("error.type").Should().Be("Conflict");
        activity.GetTagItem("error.code").Should().Be("test.conflict");
    }

    [Fact]
    public async Task Handle_ThrownException_SetsErrorStatusAndRethrows()
    {
        var captured = new List<Activity>();
        using var listener = Listen(captured);
        var behavior = new TracingBehavior<TestCommand, Result>();

        var act = async () => await behavior.Handle(new TestCommand(), () => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var activity = captured.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }
}
