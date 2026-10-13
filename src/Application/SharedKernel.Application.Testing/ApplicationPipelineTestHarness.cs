using System.Diagnostics;
using SharedKernel.Testing.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline;
using SharedKernel.Application.Streaming;

namespace SharedKernel.Testing.Application;

/// <summary>
/// Reusable helper that wires a real <see cref="ServiceCollection"/> through
/// <c>AddSharedKernelApplication</c> with a caller-chosen set of opt-in behaviors
/// (<see cref="Configure"/>), and exposes a minimal fluent surface to send a request
/// and assert on response shape, thrown exceptions, recorded application-pipeline metrics, and
/// recorded tracing spans.
/// </summary>
/// <remarks>
/// <para>
/// Two ways to build it, both running the same behaviors in the same order. <see cref="Build"/> needs no
/// mediator: register each handler on <see cref="Services"/>, and <see cref="SendAsync{TResponse}"/> goes
/// through a harness sender that runs <see cref="RequestPipeline{TRequest,TResponse}"/> directly.
/// <see cref="Build{TMarker}"/> registers the application layer over the marker's assembly — its handlers,
/// validators and domain-event handlers — with the MediatR adapter (<c>UseMediatR()</c>), so
/// <see cref="SendAsync{TResponse}"/> goes through the kernel <see cref="ISender"/> exactly as a service's does.
/// </para>
/// <para>
/// Both run the start-time checks a host runs (<see cref="IStartupValidator"/>), so a seam the chosen behaviors
/// need and the test did not register fails the build with <see cref="OptionsValidationException"/>, exactly as it
/// would fail the host. Authorization is always part of the pipeline: when the assembly passed to
/// <see cref="Build{TMarker}"/> declares a <c>[RequirePermission]</c> request, register an <c>IRequestContext</c>
/// (for example with <c>AddFakeApplicationBehaviorServices()</c>) before building.
/// </para>
/// <para>
/// Span and metric capture run through the shared <c>ActivityRecorder</c> and <c>MetricRecorder</c> of <c>SharedKernel.Testing</c>,
/// filtered by <see cref="ApplicationDiagnostics.ActivitySourceName"/> and <see cref="ApplicationDiagnostics.MeterName"/>.
/// </para>
/// </remarks>
public sealed class ApplicationPipelineTestHarness : IDisposable
{
    private readonly ServiceCollection _services = new();
    private readonly MetricRecorder _metricRecorder = MetricRecorder.StartRecording(ApplicationDiagnostics.MeterName);
    private ActivityRecorder? _activityRecorder;
    private ServiceProvider? _provider;
    private Action<ApplicationPipelineBuilder> _configure = static _ => { };

    /// <summary>Initializes a new instance of <see cref="ApplicationPipelineTestHarness"/>.</summary>
    public ApplicationPipelineTestHarness()
    {
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    /// <summary>Exposes the underlying <see cref="ServiceCollection"/> for additional test-specific registration.</summary>
    public ServiceCollection Services => _services;

    /// <summary>
    /// Chooses the opt-in behaviors (<c>WithIdempotency()</c>, <c>WithTransactions()</c>, …) that
    /// <see cref="Build"/> and <see cref="Build{TMarker}"/> register; tracing, logging, metrics, authorization and
    /// validation are always on.
    /// </summary>
    /// <param name="configure">The opt-in choice, as passed to <c>AddSharedKernelApplication</c>.</param>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApplicationPipelineTestHarness Configure(Action<ApplicationPipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure = configure;
        return this;
    }

    /// <summary>
    /// Starts recording the spans of the
    /// <see cref="ApplicationDiagnostics.ActivitySourceName"/> <see cref="ActivitySource"/> (through <c>ActivityRecorder</c>).
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApplicationPipelineTestHarness WithActivityCapture()
    {
        _activityRecorder ??= ActivityRecorder.StartRecording(ApplicationDiagnostics.ActivitySourceName);
        return this;
    }

    /// <summary>
    /// Registers the application layer (<c>AddSharedKernelApplication</c>) with the behaviors chosen by
    /// <see cref="Configure"/> and no mediator, and builds the <see cref="ServiceProvider"/>. Handlers are whatever
    /// the test registered on <see cref="Services"/>; <see cref="SendAsync{TResponse}"/> runs each request's
    /// <see cref="RequestPipeline{TRequest,TResponse}"/> directly. Must be called after all test-specific
    /// registration. Runs the start-time checks a host runs, so a missing seam fails here with
    /// <see cref="OptionsValidationException"/>.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApplicationPipelineTestHarness Build()
    {
        // This assembly declares no handler, validator or [RequirePermission] request, so the scan adds nothing.
        _services.AddSharedKernelApplication(typeof(ApplicationPipelineTestHarness).Assembly, _configure);
        _services.TryAddTransient<ISender, PipelineSender>();
        return BuildProvider();
    }

    /// <summary>
    /// Registers the application layer (<c>AddSharedKernelApplication</c>) over the assembly containing
    /// <typeparamref name="TMarker"/> — its handlers, validators and domain-event handlers — with the behaviors
    /// chosen by <see cref="Configure"/> and the MediatR adapter, and builds the <see cref="ServiceProvider"/>. Must
    /// be called after all test-specific registration. Runs the start-time checks a host runs, so a missing seam
    /// fails here with <see cref="OptionsValidationException"/>.
    /// </summary>
    /// <typeparam name="TMarker">Any type in the assembly that declares the handlers.</typeparam>
    /// <returns>This instance, for fluent chaining.</returns>
    public ApplicationPipelineTestHarness Build<TMarker>()
    {
        var configure = _configure;
        _services.AddSharedKernelApplication(typeof(TMarker).Assembly, app =>
        {
            configure(app);
            app.UseMediatR();
        });
        return BuildProvider();
    }

    /// <summary>Sends <paramref name="request"/> through the kernel <see cref="ISender"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when called before <see cref="Build"/> or <see cref="Build{TMarker}"/>.</exception>
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        if (_provider is null)
            throw new InvalidOperationException("Call Build() or Build<TMarker>() before SendAsync.");

        return _provider.GetRequiredService<ISender>().Send(request, ct);
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
    public IReadOnlyList<Activity> CapturedActivities => _activityRecorder?.StartedActivities ?? [];

    /// <summary>
    /// Gets every duration measurement recorded on the <see cref="ApplicationDiagnostics.MeterName"/> meter.
    /// Always captured (no opt-in gate needed).
    /// </summary>
    public IReadOnlyList<(string InstrumentName, double Value, IReadOnlyList<KeyValuePair<string, object?>> Tags)> CapturedMeasurements
    {
        get
        {
            _metricRecorder.Collect();
            return [.. _metricRecorder.Instruments
                .Where(static instrument => instrument.ValueType == typeof(double))
                .SelectMany(instrument => _metricRecorder.MeasurementsOf(instrument.Name))
                .Select(static m => (m.InstrumentName, m.Value, m.Tags))];
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _activityRecorder?.Dispose();
        _metricRecorder.Dispose();
        _provider?.Dispose();
    }

    private ApplicationPipelineTestHarness BuildProvider()
    {
        _provider = _services.BuildServiceProvider();
        _provider.GetRequiredService<IStartupValidator>().Validate();
        return this;
    }

    /// <summary>
    /// The <see cref="ISender"/> of <see cref="Build"/>: runs each request's pipeline directly, with no mediator.
    /// </summary>
    private sealed class PipelineSender(IServiceProvider services) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var dispatcher = (RequestDispatcher<TResponse>)Activator.CreateInstance(
                typeof(RequestDispatcher<,>).MakeGenericType(request.GetType(), typeof(TResponse)))!;
            return dispatcher.SendAsync(services, request, cancellationToken);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamQuery<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var dispatcher = (StreamDispatcher<TResponse>)Activator.CreateInstance(
                typeof(StreamDispatcher<,>).MakeGenericType(request.GetType(), typeof(TResponse)))!;
            return dispatcher.CreateStream(services, request, cancellationToken);
        }
    }

    private abstract class RequestDispatcher<TResponse>
    {
        public abstract Task<TResponse> SendAsync(IServiceProvider services, IRequest<TResponse> request, CancellationToken cancellationToken);
    }

    private sealed class RequestDispatcher<TRequest, TResponse> : RequestDispatcher<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> SendAsync(IServiceProvider services, IRequest<TResponse> request, CancellationToken cancellationToken)
            => services.GetRequiredService<RequestPipeline<TRequest, TResponse>>().HandleAsync((TRequest)request, cancellationToken);
    }

    private abstract class StreamDispatcher<TResponse>
    {
        public abstract IAsyncEnumerable<TResponse> CreateStream(IServiceProvider services, IStreamQuery<TResponse> request, CancellationToken cancellationToken);
    }

    private sealed class StreamDispatcher<TRequest, TResponse> : StreamDispatcher<TResponse>
        where TRequest : IStreamQuery<TResponse>
    {
        public override IAsyncEnumerable<TResponse> CreateStream(IServiceProvider services, IStreamQuery<TResponse> request, CancellationToken cancellationToken)
            => services.GetRequiredService<StreamRequestPipeline<TRequest, TResponse>>().Handle((TRequest)request, cancellationToken);
    }
}
