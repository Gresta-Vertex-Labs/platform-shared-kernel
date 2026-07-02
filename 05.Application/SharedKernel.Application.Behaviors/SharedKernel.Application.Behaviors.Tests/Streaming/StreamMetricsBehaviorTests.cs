using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Behaviors.Streaming;
using SharedKernel.Application.Streaming;

namespace SharedKernel.Application.Behaviors.Tests.Streaming;

/// <summary>
/// Verifies <see cref="StreamMetricsBehavior{TRequest,TResponse}"/> (T-28):
/// <list type="bullet">
///   <item>Records exactly one measurement per stream with outcome tag <c>"streamed"</c> on success.</item>
///   <item>Records exactly one measurement with outcome tag <c>"faulted"</c> when the stream throws.</item>
/// </list>
/// </summary>
public sealed class StreamMetricsBehaviorTests
{
    private sealed record MetricsNormalStreamQuery : IStreamQuery<int>;
    private sealed record MetricsFaultStreamQuery : IStreamQuery<int>;

    private sealed class MetricsNormalStreamHandler : IStreamRequestHandler<MetricsNormalStreamQuery, int>
    {
        public async IAsyncEnumerable<int> Handle(MetricsNormalStreamQuery request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return 1;
            yield return 2;
            await Task.CompletedTask;
        }
    }

    private sealed class MetricsFaultStreamHandler : IStreamRequestHandler<MetricsFaultStreamQuery, int>
    {
        public async IAsyncEnumerable<int> Handle(MetricsFaultStreamQuery request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return 1;
            await Task.CompletedTask;
            throw new InvalidOperationException("metrics stream fault");
        }
    }

    private sealed class MeasurementCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<(double Value, string? Outcome, string? RequestName)> Measurements { get; } = [];

        public MeasurementCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "SharedKernel.Application"
                    && instrument.Name == "sharedkernel.application.request.duration")
                    listener.EnableMeasurementEvents(instrument);
            };

            _listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            {
                string? outcome = null;
                string? name = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "outcome") outcome = tag.Value?.ToString();
                    if (tag.Key == "request.name") name = tag.Value?.ToString();
                }
                Measurements.Add((value, outcome, name));
            });

            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public async Task Handle_NormalStream_RecordsExactlyOneMeasurementWithOutcomeStreamed()
    {
        using var capture = new MeasurementCapture();

        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IStreamRequestHandler<MetricsNormalStreamQuery, int>, MetricsNormalStreamHandler>();
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamMetricsBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamMetricsBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var sender = provider.GetRequiredService<ISender>();
        await foreach (var _ in sender.CreateStream(new MetricsNormalStreamQuery())) { }

        capture.Measurements.Should().ContainSingle(
            m => m.Outcome == "streamed" && m.RequestName!.Contains("MetricsNormalStreamQuery"),
            "one measurement with outcome='streamed' must be recorded after normal stream completion");
    }

    [Fact]
    public async Task Handle_FaultedStream_RecordsExactlyOneMeasurementWithOutcomeFaulted()
    {
        using var capture = new MeasurementCapture();

        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IStreamRequestHandler<MetricsFaultStreamQuery, int>, MetricsFaultStreamHandler>();
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamMetricsBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamMetricsBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var sender = provider.GetRequiredService<ISender>();
        var act = async () =>
        {
            await foreach (var _ in sender.CreateStream(new MetricsFaultStreamQuery())) { }
        };

        await act.Should().ThrowAsync<InvalidOperationException>();

        capture.Measurements.Should().ContainSingle(
            m => m.Outcome == "faulted" && m.RequestName!.Contains("MetricsFaultStreamQuery"),
            "one measurement with outcome='faulted' must be recorded when stream throws");
    }
}
