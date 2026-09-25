using SharedKernel.Application.Mediator.MediatR;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Pipeline;
using SharedKernel.Application.Pipeline.Extensions;

namespace SharedKernel.Testing.Application;

/// <summary>
/// Reusable helper that wires a real <see cref="ServiceCollection"/> + the kernel request pipeline
/// (<see cref="RequestPipeline{TRequest,TResponse}"/>) + a caller-chosen subset of
/// <c>SharedKernel.Application.Pipeline</c> behaviors via <see cref="ApplicationBehaviorsBuilder"/>,
/// and exposes a minimal fluent surface to send a request and assert on response shape, thrown
/// exceptions, recorded application-pipeline metrics, and recorded tracing spans.
/// </summary>
/// <remarks>
/// <para>
/// Two ways to build it. <see cref="Build"/> needs no mediator at all: register each handler on
/// <see cref="Services"/> and send with <see cref="SendThroughPipelineAsync{TRequest,TResponse}"/>,
/// which resolves the pipeline directly. <see cref="Build{TMarker}"/> adds the MediatR adapter
/// (<c>AddSharedKernelMediatR</c>) over the marker's assembly, so <see cref="SendAsync{TResponse}"/>
/// goes through the kernel <see cref="ISender"/> exactly as a service would. Both run the same
/// behaviors in the same order.
/// </para>
/// <para>
/// Implements its OWN local <see cref="ActivityListener"/>/<see cref="MeterListener"/> wiring
/// (self-contained BCL <c>System.Diagnostics</c> code), filtered by the literal string
/// <c>"SharedKernel.Application"</c>: the source Meter/ActivitySource pair
/// (<c>ApplicationDiagnostics</c> in <c>SharedKernel.Application.Pipeline</c>) is declared
/// <see langword="internal"/> to that assembly, so this harness filters by the well-known name.
/// </para>
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
    /// Builds the <see cref="ServiceProvider"/> without any mediator. Handlers are whatever the test
    /// registered on <see cref="Services"/>; send with
    /// <see cref="SendThroughPipelineAsync{TRequest,TResponse}"/>. Must be called after all
    /// behavior/handler registration.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApplicationPipelineTestHarness Build()
    {
        _services.AddSharedKernelRequestPipeline();
        _provider = _services.BuildServiceProvider();
        return this;
    }

    /// <summary>
    /// Registers the MediatR adapter over the assembly containing <typeparamref name="TMarker"/>
    /// (discovering its handlers) and builds the <see cref="ServiceProvider"/>. Must be called after
    /// all behavior/handler registration.
    /// </summary>
    /// <typeparam name="TMarker">Any type in the assembly that declares the handlers.</typeparam>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApplicationPipelineTestHarness Build<TMarker>()
    {
        _services.AddSharedKernelMediatR(typeof(TMarker).Assembly);
        _provider = _services.BuildServiceProvider();
        return this;
    }

    /// <summary>Sends <paramref name="request"/> through the kernel <see cref="ISender"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called before <see cref="Build{TMarker}"/>, or after <see cref="Build"/> (which
    /// registers no <see cref="ISender"/>).
    /// </exception>
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        if (_provider is null)
            throw new InvalidOperationException("Call Build<TMarker>() before SendAsync.");

        var sender = _provider.GetService<ISender>()
            ?? throw new InvalidOperationException(
                "No ISender is registered: Build() builds without a mediator. Use SendThroughPipelineAsync, or Build<TMarker>().");
        return sender.Send(request, ct);
    }

    /// <summary>
    /// Runs <paramref name="request"/> through <see cref="RequestPipeline{TRequest,TResponse}"/>
    /// directly — every applicable behavior, then the registered handler — with no mediator.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when called before <see cref="Build"/> or <see cref="Build{TMarker}"/>.</exception>
    public Task<TResponse> SendThroughPipelineAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
    {
        if (_provider is null)
            throw new InvalidOperationException("Call Build() or Build<TMarker>() before SendThroughPipelineAsync.");

        return _provider.GetRequiredService<RequestPipeline<TRequest, TResponse>>().HandleAsync(request, ct);
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
