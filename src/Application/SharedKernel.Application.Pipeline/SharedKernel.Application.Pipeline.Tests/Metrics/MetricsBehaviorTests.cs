using SharedKernel.Application.Mediator.MediatR;
using System.Diagnostics.Metrics;
using SharedKernel.Application.Pipeline.Metrics;
using SharedKernel.Application.Pipeline.Tests.Support;
using SharedKernel.Application.Pipeline.Tracing;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Telemetry;
using FluentAssertions;
using SharedKernel.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Pipeline.Tests.Metrics;

/// <summary>
/// <c>MetricsBehavior&lt;TRequest, TResponse&gt;</c> and
/// <c>ApplicationMetrics</c> are internal, so this test drives them through
/// a real composed <c>ServiceCollection</c> + <c>AddSharedKernelApplication(..., app =&gt; app.UseMediatR())</c>
/// dispatch and observes the published <c>"SharedKernel.Application"</c> meter through the shared <c>MetricRecorder</c> —
/// the same documented instrument name <c>13.ServiceDefaults</c>
/// subscribes to at the host level.
/// </summary>
public sealed class MetricsBehaviorTests
{
    private const string MeterName = ApplicationDiagnostics.MeterName;
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

    private sealed record CancelingCommand : ICommand;

    private sealed class CancelingCommandHandler : ICommandHandler<CancelingCommand>
    {
        public Task<Result> Handle(CancelingCommand request, CancellationToken cancellationToken)
            => throw new OperationCanceledException(cancellationToken);
    }

    private static IReadOnlyList<RecordedMeasurement> DurationsOf(MetricRecorder recorder, Type requestType)
        => [.. recorder.MeasurementsOf(InstrumentName)
            .Where(m => m.TryGetTag("application.request_type", out var value) && Equals(value, requestType.FullName))];

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(typeof(MetricsBehaviorTests).Assembly, app => app.UseMediatR());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_Success_RecordsExactlyOneMeasurementWithSuccessOutcome()
    {
        using var recorder = MetricRecorder.StartRecording(MeterName);
        using var provider = BuildProvider();

        await provider.GetRequiredService<ISender>().Send(new SucceedingCommand());

        var records = DurationsOf(recorder, typeof(SucceedingCommand));
        records.Should().ContainSingle();
        records[0].TryGetTag("outcome", out var outcome).Should().BeTrue();
        outcome.Should().Be(WellKnownOutcomes.Success);
        records[0].TryGetTag("application.request_kind", out var kind).Should().BeTrue();
        kind.Should().Be("command");
        records[0].TryGetTag("error.type", out _).Should().BeFalse();
        recorder.ShouldHaveInstrument(InstrumentName, "s", InstrumentKind.Histogram)
            .ShouldUseOutcomeVocabulary(InstrumentName)
            .ShouldFollowMetricConvention();
    }

    [Fact]
    public async Task Handle_ResultFailure_RecordsFailureOutcomeWithErrorType()
    {
        using var recorder = MetricRecorder.StartRecording(MeterName);
        using var provider = BuildProvider();

        await provider.GetRequiredService<ISender>().Send(new FailingQuery());

        var records = DurationsOf(recorder, typeof(FailingQuery));
        records.Should().ContainSingle();
        records[0].TryGetTag("outcome", out var outcome);
        outcome.Should().Be(WellKnownOutcomes.Failure);
        records[0].TryGetTag("error.type", out var errorType);
        errorType.Should().Be("NotFound");
        records[0].TryGetTag("application.request_kind", out var kind);
        kind.Should().Be("query");
    }

    [Fact]
    public async Task Handle_ThrownException_RecordsExceptionOutcomeAndRethrows()
    {
        using var recorder = MetricRecorder.StartRecording(MeterName);
        using var provider = BuildProvider();

        var act = async () => await provider.GetRequiredService<ISender>().Send(new ThrowingCommand());

        await act.Should().ThrowAsync<InvalidOperationException>();
        var records = DurationsOf(recorder, typeof(ThrowingCommand));
        records.Should().ContainSingle();
        records[0].TryGetTag("outcome", out var outcome);
        outcome.Should().Be(WellKnownOutcomes.Exception);
        records[0].TryGetTag("error.type", out var errorType);
        errorType.Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task Handle_Cancellation_RecordsCanceledOutcomeWithoutAnErrorType()
    {
        using var recorder = MetricRecorder.StartRecording(MeterName);
        using var provider = BuildProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await provider.GetRequiredService<ISender>().Send(new CancelingCommand(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var records = DurationsOf(recorder, typeof(CancelingCommand));
        records.Should().ContainSingle();
        records[0].TryGetTag("outcome", out var outcome);
        outcome.Should().Be(WellKnownOutcomes.Canceled);
        records[0].TryGetTag("error.type", out _).Should().BeFalse();
    }

    public static TheoryData<ErrorType> AllErrorTypes()
    {
        var data = new TheoryData<ErrorType>();
        foreach (var type in Enum.GetValues<ErrorType>().Where(static t => t != ErrorType.None))
            data.Add(type);
        return data;
    }

    private sealed record ParityCommand : ICommand;

    /// <summary>The metric <c>error.type</c> is the same text the span carries, for every <see cref="ErrorType"/>.</summary>
    [Theory]
    [MemberData(nameof(AllErrorTypes))]
    public async Task Handle_ResultFailure_MetricAndSpanCarryTheSameErrorType(ErrorType type)
    {
        using var probe = new TelemetryProbe();
        var error = new Error("test.parity", "failure", type);
        var metrics = new ApplicationMetrics(new TestMeterFactory());
        var tracing = new TracingBehavior<ParityCommand, Result>();
        var measuring = new MetricsBehavior<ParityCommand, Result>(metrics);

        await tracing.Handle(
            new ParityCommand(),
            () => measuring.Handle(new ParityCommand(), () => Task.FromResult(Result.Failure(error)), CancellationToken.None),
            CancellationToken.None);

        var span = probe.Spans.Single(s => s.OperationName == ApplicationSpanNames.Send);
        span.GetTagItem("error.type").Should().Be(type.ToString());
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
                meter.Dispose();
        }
    }
}
