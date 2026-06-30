using System.Diagnostics;
using System.Diagnostics.Metrics;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Behaviors.Extensions;

namespace SharedKernel.Application.Behaviors.Tests.TestHarness;

/// <summary>
/// Reusable, test-assembly-internal helper that wires a real <see cref="ServiceCollection"/> +
/// <c>AddMediatR</c> + a caller-chosen subset of behaviors via <see cref="ApplicationBehaviorsBuilder"/>,
/// and exposes a minimal fluent surface to send a request and assert on response shape, thrown
/// exceptions, recorded <c>ApplicationDiagnostics</c> metrics, and recorded
/// <see cref="ActivitySource"/> spans.
/// </summary>
/// <remarks>
/// Never packaged, never referenced by <c>16.Testing</c> or production code — internal to
/// <c>SharedKernel.Application.Behaviors.Tests</c> only. Formalizes the "prefer a minimal real
/// <see cref="ServiceCollection"/>" guidance already applied ad hoc across the WO-035 behavior test
/// files into one reusable harness.
/// </remarks>
internal sealed class PipelineTestHarness : IDisposable
{
    private readonly ServiceCollection _services = new();
    private readonly List<Activity> _capturedActivities = [];
    private ActivityListener? _activityListener;
    private ServiceProvider? _provider;
    private readonly MeterListener _meterListener = new();
    private readonly List<(string InstrumentName, double Value, IReadOnlyList<KeyValuePair<string, object?>> Tags)> _capturedMeasurements = [];

    public PipelineTestHarness()
    {
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "SharedKernel.Application")
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            var tagList = new List<KeyValuePair<string, object?>>();
            foreach (var tag in tags)
                tagList.Add(tag);
            _capturedMeasurements.Add((instrument.Name, measurement, tagList));
        });
        _meterListener.Start();
    }

    /// <summary>Exposes the underlying <see cref="ServiceCollection"/> for additional service registration.</summary>
    public ServiceCollection Services => _services;

    /// <summary>Begins building the opt-in behavior pipeline via <see cref="ApplicationBehaviorsBuilder"/>.</summary>
    public ApplicationBehaviorsBuilder AddBehaviors() => _services.AddSharedKernelApplicationBehaviors();

    /// <summary>Enables capturing <see cref="Activity"/> spans started on the <c>"SharedKernel.Application"</c> source.</summary>
    public PipelineTestHarness WithActivityCapture()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "SharedKernel.Application",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => _capturedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(_activityListener);
        return this;
    }

    /// <summary>
    /// Registers MediatR against the assembly containing <typeparamref name="TMarker"/> and builds
    /// the <see cref="ServiceProvider"/>. Must be called after all behaviors and handlers are
    /// registered.
    /// </summary>
    public PipelineTestHarness Build<TMarker>()
    {
        _services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<TMarker>());
        _provider = _services.BuildServiceProvider();
        return this;
    }

    /// <summary>Sends <paramref name="request"/> through the resolved pipeline.</summary>
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        if (_provider is null)
            throw new InvalidOperationException("Call Build<TMarker>() before SendAsync.");

        var sender = _provider.GetRequiredService<ISender>();
        return sender.Send(request, ct);
    }

    /// <summary>Gets every <see cref="Activity"/> captured so far, filtered by tag value for test isolation.</summary>
    public IReadOnlyList<Activity> CapturedActivities => _capturedActivities;

    /// <summary>Gets every duration measurement recorded on <c>sharedkernel.application.request.duration</c>.</summary>
    public IReadOnlyList<(string InstrumentName, double Value, IReadOnlyList<KeyValuePair<string, object?>> Tags)> CapturedMeasurements
    {
        get
        {
            _meterListener.RecordObservableInstruments();
            return _capturedMeasurements;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _activityListener?.Dispose();
        _meterListener.Dispose();
        _provider?.Dispose();
    }
}
