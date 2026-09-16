using System.Diagnostics.Metrics;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Metrics;

/// <summary>
/// <see cref="Behaviors.Metrics.MetricsBehavior{TRequest,TResponse}"/> and
/// <see cref="Behaviors.Metrics.ApplicationMetrics"/> are internal, so this test drives them through
/// a real composed <c>ServiceCollection</c> + <c>AddMediatR</c> + <see cref="ApplicationBehaviorsBuilder"/>
/// dispatch and observes the published <c>"SharedKernel.Application"</c> meter directly via
/// <see cref="MeterListener"/> — the same documented instrument name <c>13.ServiceDefaults</c>
/// subscribes to at the host level.
/// </summary>
public sealed class MetricsBehaviorTests
{
    private const string MeterName = "SharedKernel.Application";
    private const string InstrumentName = "sharedkernel.application.request.duration";

    private sealed record SucceedingCommand : ICommand;

    private sealed class SucceedingCommandHandler : ICommandHandler<SucceedingCommand>
    {
        public Task<Result> Handle(SucceedingCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed record FailingQuery : IQuery<string>;

    private sealed class FailingQueryHandler : IQueryHandler<FailingQuery, string>
    {
        public Task<Result<string>> Handle(FailingQuery request, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Failure(Error.NotFound("test.not_found", "missing")));
    }

    private sealed record ThrowingCommand : ICommand;

    private sealed class ThrowingCommandHandler : ICommandHandler<ThrowingCommand>
    {
        public Task<Result> Handle(ThrowingCommand request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    private sealed record RecordedMeasurement(double Value, Dictionary<string, object?> Tags);

    private static (MeterListener Listener, List<RecordedMeasurement> Records) Listen()
    {
        var records = new List<RecordedMeasurement>();
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MeterName && instrument.Name == InstrumentName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var tag in tags)
                dict[tag.Key] = tag.Value;
            records.Add(new RecordedMeasurement(value, dict));
        });
        listener.Start();
        return (listener, records);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MetricsBehaviorTests>());
        services.AddSharedKernelApplicationBehaviors().AddMetricsBehavior().Build();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_Success_RecordsExactlyOneMeasurementWithSuccessOutcome()
    {
        var (listener, records) = Listen();
        using var _ = listener;
        using var provider = BuildProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new SucceedingCommand());

        records.Should().ContainSingle();
        records[0].Tags["outcome"].Should().Be("success");
        records[0].Tags["request.kind"].Should().Be("command");
        records[0].Tags.Should().NotContainKey("error.type");
    }

    [Fact]
    public async Task Handle_ResultFailure_RecordsFailureOutcomeWithErrorType()
    {
        var (listener, records) = Listen();
        using var _ = listener;
        using var provider = BuildProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new FailingQuery());

        records.Should().ContainSingle();
        records[0].Tags["outcome"].Should().Be("failure");
        records[0].Tags["error.type"].Should().Be("NotFound");
        records[0].Tags["request.kind"].Should().Be("query");
    }

    [Fact]
    public async Task Handle_ThrownException_RecordsExceptionOutcomeAndRethrows()
    {
        var (listener, records) = Listen();
        using var _ = listener;
        using var provider = BuildProvider();
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new ThrowingCommand());

        await act.Should().ThrowAsync<InvalidOperationException>();
        records.Should().ContainSingle();
        records[0].Tags["outcome"].Should().Be("exception");
        records[0].Tags["error.type"].Should().Be(typeof(InvalidOperationException).FullName);
    }
}
