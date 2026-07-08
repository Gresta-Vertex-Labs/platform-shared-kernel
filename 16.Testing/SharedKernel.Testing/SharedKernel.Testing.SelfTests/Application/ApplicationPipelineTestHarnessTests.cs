using System.Linq;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Application;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Application;

public sealed class ApplicationPipelineTestHarnessTests
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
            => Task.FromResult(Result.Failure(Error.Validation("x", "bad")));
    }

    private sealed record ThrowingCommand : ICommand;

    private sealed class ThrowingCommandHandler : IRequestHandler<ThrowingCommand, Result>
    {
        public Task<Result> Handle(ThrowingCommand request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task SendAsync_ReturnsExpectedResponse_ThroughComposedBehaviors()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.AddBehaviors().AddLoggingBehavior().AddMetricsBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new SucceedingCommand());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SendAsync_ReturnsFailureResponse_ThroughComposedBehaviors()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<FailingResultCommand, Result>, FailingResultCommandHandler>();
        harness.AddBehaviors().AddLoggingBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var result = await harness.SendAsync(new FailingResultCommand());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SendAsync_ThrownHandlerException_PropagatesUnchanged()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<ThrowingCommand, Result>, ThrowingCommandHandler>();
        harness.AddBehaviors().AddLoggingBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.SendAsync(new ThrowingCommand()));

        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task WithActivityCapture_AddTracingBehavior_RecordsSpanTaggedWithRequestName()
    {
        using var harness = new ApplicationPipelineTestHarness().WithActivityCapture();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.AddBehaviors().AddTracingBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        await harness.SendAsync(new SucceedingCommand());

        var expectedTag = typeof(SucceedingCommand).FullName ?? nameof(SucceedingCommand);
        var spans = harness.CapturedActivities
            .Where(a => Equals(a.GetTagItem("request.name"), expectedTag))
            .ToList();
        Assert.Single(spans);
    }

    [Fact]
    public async Task CapturedMeasurements_AddMetricsBehavior_RecordsRequestDurationEntry()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.AddBehaviors().AddMetricsBehavior().Build();
        harness.Build<ApplicationPipelineTestHarnessTests>();

        await harness.SendAsync(new SucceedingCommand());

        Assert.Contains(
            harness.CapturedMeasurements,
            measurement => measurement.InstrumentName == "sharedkernel.application.request.duration");
    }

    [Fact]
    public async Task SendAsync_BeforeBuild_ThrowsInvalidOperationException()
    {
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddSingleton<IRequestHandler<SucceedingCommand, Result>, SucceedingCommandHandler>();
        harness.AddBehaviors().Build();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.SendAsync(new SucceedingCommand()));
    }
}
