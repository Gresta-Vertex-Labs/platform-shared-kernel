using System.Diagnostics;
using System.Diagnostics.Metrics;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Behaviors.Extensions;

namespace SharedKernel.Testing.Application;

/// <summary>
/// Reusable helper that wires a real <see cref="ServiceCollection"/> + MediatR + a caller-chosen
/// subset of <c>SharedKernel.Application.Behaviors</c> pipeline behaviors via
/// <see cref="ApplicationBehaviorsBuilder"/>, and exposes a minimal fluent surface to send a request
/// and assert on response shape, thrown exceptions, recorded application-pipeline metrics, and
/// recorded tracing spans.
/// </summary>
/// <remarks>
/// Public promotion of the internal-only <c>PipelineTestHarness</c> already proven in
/// <c>SharedKernel.Application.Behaviors.Tests/TestHarness/PipelineTestHarness.cs</c> — same design,
/// renamed to avoid ambiguity with <c>07.Messaging</c>'s <c>TestHarnessFactory</c>/MassTransit
/// <c>ITestHarness</c> in the sibling <c>Messaging/</c> folder. Implements its OWN local
/// <see cref="ActivityListener"/>/<see cref="MeterListener"/> wiring (self-contained BCL
/// <c>System.Diagnostics</c> code), filtered by the literal string <c>"SharedKernel.Application"</c>,
/// rather than referencing <c>Communication/ActivityRecorder</c> — even though the two are
/// functionally similar, the sibling-capability-folder-isolation hard rule forbids <c>Application/</c>
/// from referencing <c>Communication/</c>. This is also the only option: the source Meter/ActivitySource
/// pair (<c>ApplicationDiagnostics</c> in <c>SharedKernel.Application.Behaviors</c>) is declared
/// <see langword="internal"/> to that assembly, so this harness cannot reference the instrument
/// instances directly — it filters by the well-known name/version instead, exactly mirroring the
/// internal harness's own approach.
/// </remarks>
/// <remarks>
/// Outstanding cross-domain follow-up (tracked, not performed by this package): the original
/// <c>internal sealed class PipelineTestHarness</c> at
/// <c>05.Application.Behaviors.Tests/TestHarness/PipelineTestHarness.cs</c> still exists unchanged.
/// A future <c>05.Application</c> implementer pass should repoint that project's call sites to this
/// public <see cref="ApplicationPipelineTestHarness"/> and then retire (or thin-wrap) the internal
/// type. <c>16.Testing</c> never edits another domain's <c>.Tests</c> project, so this package's
/// obligation is satisfied by shipping this type with zero duplicated wiring logic versus the
/// internal harness's already-proven shape — not by performing that repointing itself.
/// </remarks>
public sealed class ApplicationPipelineTestHarness : IDisposable
{
    private const string ApplicationDiagnosticsName = "SharedKernel.Application";

    private readonly ServiceCollection _services = new();
    private readonly List<Activity> _capturedActivities = [];
    private readonly MeterListener _meterListener = new();
    private readonly List<(string InstrumentName, double Value, IReadOnlyList<KeyValuePair<string, object?>> Tags)> _capturedMeasurements = [];
    private ActivityListener? _activityListener;
    private ServiceProvider? _provider;

    /// <summary>Initializes a new instance of <see cref="ApplicationPipelineTestHarness"/>.</summary>
    public ApplicationPipelineTestHarness()
    {
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ApplicationDiagnosticsName)
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

    /// <summary>Exposes the underlying <see cref="ServiceCollection"/> for additional test-specific registration.</summary>
    public ServiceCollection Services => _services;

    /// <summary>Begins building the opt-in behavior pipeline via <see cref="ApplicationBehaviorsBuilder"/>.</summary>
    /// <remarks>Delegates to <c>Services.AddSharedKernelApplicationBehaviors()</c>.</remarks>
    public ApplicationBehaviorsBuilder AddBehaviors() => _services.AddSharedKernelApplicationBehaviors();

    /// <summary>
    /// Registers an opt-in <see cref="ActivityListener"/> filtered to the
    /// <c>"SharedKernel.Application"</c> <see cref="ActivitySource"/>.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApplicationPipelineTestHarness WithActivityCapture()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationDiagnosticsName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => _capturedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(_activityListener);
        return this;
    }

    /// <summary>
    /// Registers MediatR from the assembly containing <typeparamref name="TMarker"/> and builds the
    /// <see cref="ServiceProvider"/>. Must be called after all behavior/handler registration.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApplicationPipelineTestHarness Build<TMarker>()
    {
        _services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<TMarker>());
        _provider = _services.BuildServiceProvider();
        return this;
    }

    /// <summary>Sends <paramref name="request"/> through the resolved pipeline.</summary>
    /// <exception cref="InvalidOperationException">Thrown when called before <see cref="Build{TMarker}"/>.</exception>
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        if (_provider is null)
            throw new InvalidOperationException("Call Build<TMarker>() before SendAsync.");

        var sender = _provider.GetRequiredService<ISender>();
        return sender.Send(request, ct);
    }

    /// <summary>
    /// Gets every <see cref="Activity"/> captured so far. Populated only when
    /// <see cref="WithActivityCapture"/> was called first.
    /// </summary>
    public IReadOnlyList<Activity> CapturedActivities => _capturedActivities;

    /// <summary>
    /// Gets every duration measurement recorded on the <c>"SharedKernel.Application"</c> meter.
    /// Always captured (no opt-in gate needed).
    /// </summary>
    public IReadOnlyList<(string InstrumentName, double Value, IReadOnlyList<KeyValuePair<string, object?>> Tags)> CapturedMeasurements
    {
        get
        {
            _meterListener.RecordObservableInstruments();
            return _capturedMeasurements;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activityListener?.Dispose();
        _meterListener.Dispose();
        _provider?.Dispose();
    }
}
