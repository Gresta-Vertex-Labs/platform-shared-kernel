using System.Diagnostics.Metrics;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Metrics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Metrics;

/// <summary>
/// Verifies <see cref="MetricsBehavior{TRequest,TResponse}"/> records exactly one measurement per
/// request, tagged with the request type name and — per WO-039 P-239 (T-38) — an <c>outcome</c>
/// tag of <c>"success"</c>, <c>"failure"</c>, or <c>"exception"</c> reflecting how the inner
/// pipeline terminated.
/// </summary>
public sealed class MetricsBehaviorTests
{
    private sealed record TestCommand : IRequest<Result>;

    private sealed class SucceedingHandler : IRequestHandler<TestCommand, Result>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed class FailingHandler : IRequestHandler<TestCommand, Result>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure(Error.Conflict("x", "failed")));
    }

    private sealed class ThrowingHandler : IRequestHandler<TestCommand, Result>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("handler blew up");
    }

    private static ServiceProvider BuildProvider<THandler>()
        where THandler : class, IRequestHandler<TestCommand, Result>
    {
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<TestCommand, Result>, THandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MetricsBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MetricsBehaviorTests>());

        return services.BuildServiceProvider();
    }

    private sealed class MeasurementCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<(double Value, string? RequestName, string? Outcome)> Measurements { get; } = [];

        public MeasurementCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "SharedKernel.Application"
                    && instrument.Name == "sharedkernel.application.request.duration")
                    listener.EnableMeasurementEvents(instrument);
            };

            _listener.SetMeasurementEventCallback<double>((_, measurement, tags, _) =>
            {
                string? requestName = null;
                string? outcome = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "request.name")
                        requestName = tag.Value?.ToString();
                    else if (tag.Key == "outcome")
                        outcome = tag.Value?.ToString();
                }

                Measurements.Add((measurement, requestName, outcome));
            });

            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task Handle_OnSuccess_RecordsExactlyOneMeasurementTaggedWithRequestName()
    {
        var expectedName = typeof(TestCommand).FullName ?? typeof(TestCommand).Name;
        using var capture = new MeasurementCapture();
        var provider = BuildProvider<SucceedingHandler>();
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new TestCommand());

        // Filter by request name to be resilient to concurrent tests recording to the same shared Meter.
        var myMeasurements = capture.Measurements
            .Where(m => m.RequestName == expectedName)
            .ToList();

        myMeasurements.Should().ContainSingle("exactly one measurement must be recorded for this request type");
        myMeasurements[0].RequestName.Should().Be(expectedName);
        myMeasurements[0].Outcome.Should().Be("success", "a successful response must be tagged outcome=\"success\" (WO-039 P-239)");
    }

    [Fact]
    public async Task Handle_OnResultFailure_RecordsMeasurementTaggedOutcomeFailure()
    {
        var expectedName = typeof(TestCommand).FullName ?? typeof(TestCommand).Name;
        using var capture = new MeasurementCapture();
        var provider = BuildProvider<FailingHandler>();
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new TestCommand());

        var myMeasurements = capture.Measurements
            .Where(m => m.RequestName == expectedName)
            .ToList();

        myMeasurements.Should().ContainSingle("exactly one measurement must be recorded for a Result.Failure response");
        myMeasurements[0].Outcome.Should().Be("failure",
            "a Result.Failure response must be tagged outcome=\"failure\" (WO-039 P-239)");
    }

    [Fact]
    public async Task Handle_WhenHandlerThrows_StillRecordsExactlyOneMeasurement()
    {
        var expectedName = typeof(TestCommand).FullName ?? typeof(TestCommand).Name;
        using var capture = new MeasurementCapture();
        var provider = BuildProvider<ThrowingHandler>();
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TestCommand());
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Filter by request name to be resilient to concurrent tests recording to the same shared Meter.
        var myMeasurements = capture.Measurements
            .Where(m => m.RequestName == expectedName)
            .ToList();

        myMeasurements.Should().ContainSingle("exactly one measurement must be recorded even when the handler throws");
        myMeasurements[0].RequestName.Should().Be(expectedName);
        myMeasurements[0].Outcome.Should().Be("exception",
            "a thrown exception must be tagged outcome=\"exception\" (WO-039 P-239)");
    }
}
