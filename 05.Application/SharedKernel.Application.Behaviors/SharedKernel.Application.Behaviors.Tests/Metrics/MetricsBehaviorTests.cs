using System.Diagnostics.Metrics;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Metrics;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Metrics;

/// <summary>
/// Verifies <see cref="MetricsBehavior{TRequest,TResponse}"/> records exactly one measurement per
/// request, tagged with the request type name, whether the inner pipeline succeeds or throws.
/// </summary>
public sealed class MetricsBehaviorTests
{
    private sealed record TestCommand : IRequest<Result>;

    private sealed class SucceedingHandler : IRequestHandler<TestCommand, Result>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
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
        public List<(double Value, string? RequestName)> Measurements { get; } = [];

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
                foreach (var tag in tags)
                {
                    if (tag.Key == "request.name")
                        requestName = tag.Value?.ToString();
                }

                Measurements.Add((measurement, requestName));
            });

            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task Handle_OnSuccess_RecordsExactlyOneMeasurementTaggedWithRequestName()
    {
        using var capture = new MeasurementCapture();
        var provider = BuildProvider<SucceedingHandler>();
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new TestCommand());

        capture.Measurements.Should().ContainSingle();
        capture.Measurements[0].RequestName.Should().Be(nameof(TestCommand));
    }

    [Fact]
    public async Task Handle_WhenHandlerThrows_StillRecordsExactlyOneMeasurement()
    {
        using var capture = new MeasurementCapture();
        var provider = BuildProvider<ThrowingHandler>();
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TestCommand());
        await act.Should().ThrowAsync<InvalidOperationException>();

        capture.Measurements.Should().ContainSingle();
        capture.Measurements[0].RequestName.Should().Be(nameof(TestCommand));
    }
}
