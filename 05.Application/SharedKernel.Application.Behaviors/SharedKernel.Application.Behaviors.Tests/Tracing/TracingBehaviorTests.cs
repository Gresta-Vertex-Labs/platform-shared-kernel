using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Tests.TestHarness;
using SharedKernel.Application.Behaviors.Tracing;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Tracing;

/// <summary>
/// Verifies <see cref="TracingBehavior{TRequest,TResponse}"/> records exactly one span per request
/// on success, on <see cref="Result.Failure"/>, and on a thrown exception, and that the span
/// carries the <c>request.name</c> tag.
/// </summary>
public sealed class TracingBehaviorTests
{
    private sealed record SucceedingCommand : ICommand;

    private sealed class SucceedingCommandHandler : IRequestHandler<SucceedingCommand, Result>
    {
        public Task<Result> Handle(SucceedingCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed record FailingResultCommand : ICommand;

    private sealed class FailingResultCommandHandler : IRequestHandler<FailingResultCommand, Result>
    {
        public Task<Result> Handle(FailingResultCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure(SharedKernel.Primitives.Errors.Error.Validation("x", "bad")));
    }

    private sealed record ThrowingCommand : ICommand;

    private sealed class ThrowingCommandHandler : IRequestHandler<ThrowingCommand, Result>
    {
        public Task<Result> Handle(ThrowingCommand request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Handle_SuccessfulRequest_RecordsExactlyOneSpanTaggedWithRequestName()
    {
        using var harness = new PipelineTestHarness().WithActivityCapture();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.AddBehaviors().AddTracingBehavior().Build();
        harness.Build<TracingBehaviorTests>();

        await harness.SendAsync(new SucceedingCommand());

        var spans = harness.CapturedActivities
            .Where(a => Equals(a.GetTagItem("request.name"), nameof(SucceedingCommand)))
            .ToList();
        spans.Should().HaveCount(1);
        spans[0].GetTagItem("request.name").Should().Be(nameof(SucceedingCommand));
    }

    [Fact]
    public async Task Handle_ResultFailureRequest_StillRecordsSpan()
    {
        using var harness = new PipelineTestHarness().WithActivityCapture();
        harness.Services.AddSingleton<IRequestHandler<FailingResultCommand, Result>, FailingResultCommandHandler>();
        harness.AddBehaviors().AddTracingBehavior().Build();
        harness.Build<TracingBehaviorTests>();

        await harness.SendAsync(new FailingResultCommand());

        var spans = harness.CapturedActivities
            .Where(a => Equals(a.GetTagItem("request.name"), nameof(FailingResultCommand)))
            .ToList();
        spans.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_ThrowingHandler_StillRecordsSpanAndRethrows()
    {
        using var harness = new PipelineTestHarness().WithActivityCapture();
        harness.Services.AddSingleton<IRequestHandler<ThrowingCommand, Result>, ThrowingCommandHandler>();
        harness.AddBehaviors().AddTracingBehavior().Build();
        harness.Build<TracingBehaviorTests>();

        var act = async () => await harness.SendAsync(new ThrowingCommand());

        await act.Should().ThrowAsync<InvalidOperationException>();
        var spans = harness.CapturedActivities
            .Where(a => Equals(a.GetTagItem("request.name"), nameof(ThrowingCommand)))
            .ToList();
        spans.Should().HaveCount(1);
    }
}
