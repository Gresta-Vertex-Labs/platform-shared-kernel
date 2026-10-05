using System.Diagnostics;
using FluentAssertions;
using SharedKernel.Application.Pipeline.Tracing;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Pipeline.Tests.Tracing;

public sealed class TracingBehaviorTests
{
    // The documented public ActivitySource name TracingBehavior emits under — 13.ServiceDefaults
    // subscribes to this same literal at the host level. ApplicationDiagnostics itself is internal.
    private const string ActivitySourceName = "SharedKernel.Application";

    // A name no other test uses: the listener sees every span of the "SharedKernel.Application" source, and test classes
    // in this assembly run in parallel, so it keeps only this class's spans.
    private sealed record TracingProbeCommand : ICommand;

    private static ActivityListener Listen(List<Activity> captured)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName != nameof(TracingProbeCommand))
                    return;
                lock (captured)
                    captured.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public async Task Handle_Success_RecordsSpanWithRequestTags()
    {
        var captured = new List<Activity>();
        using var listener = Listen(captured);
        var behavior = new TracingBehavior<TracingProbeCommand, Result>();

        await behavior.Handle(new TracingProbeCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        captured.Should().ContainSingle();
        var activity = captured[0];
        activity.OperationName.Should().Be(nameof(TracingProbeCommand));
        activity.GetTagItem("request.kind").Should().Be("command");
        activity.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Handle_ResultFailure_SetsErrorStatusAndErrorTags()
    {
        var captured = new List<Activity>();
        using var listener = Listen(captured);
        var behavior = new TracingBehavior<TracingProbeCommand, Result>();
        var error = Error.Conflict("test.conflict", "conflict");

        await behavior.Handle(new TracingProbeCommand(), () => Task.FromResult(Result.Failure(error)), CancellationToken.None);

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
        var behavior = new TracingBehavior<TracingProbeCommand, Result>();

        var act = async () => await behavior.Handle(new TracingProbeCommand(), () => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var activity = captured.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }
}
