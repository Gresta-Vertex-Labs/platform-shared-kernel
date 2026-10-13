using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Health;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Codec;
using SharedKernel.Workflows.Temporal.Configuration;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Diagnostics;
using SharedKernel.Workflows.Temporal.Health;
using SharedKernel.Workflows.Temporal.Interception;
using Temporalio.Client;
using Temporalio.Client.Interceptors;
using Temporalio.Converters;
using Temporalio.Extensions.Hosting;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Runtime;
using Temporalio.Workflows;

namespace SharedKernel.Workflows.Temporal.Hosting;

/// <summary>The default <see cref="ITemporalWorkflowsBuilder"/>.</summary>
internal sealed class TemporalWorkflowsBuilder : ITemporalWorkflowsBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfigurationSection _section;
    private readonly List<Type> _workflowTypes = [];
    private readonly List<Type> _activityTypes = [];
    private string? _taskQueue;
    private Func<WorkerTuningOptions, WorkerTuningOptions>? _tune;
    private bool _clientOnly;
    private bool _payloadEncryption;
    private bool _openTelemetry;
    private bool _metrics;
    private bool _rawClientAccess;
    private bool _built;

    public TemporalWorkflowsBuilder(IServiceCollection services, IConfiguration configuration)
    {
        _services = services;
        _section = configuration.GetSection(TemporalOptions.SectionName);

        services.AddValidatedOptions<TemporalOptions>(_section);
        // The SDK metrics sink is a Meter created from the host IMeterFactory; AddMetrics is a no-op when the host already added it.
        services.AddMetrics();
        services.TryAddSingleton<IWorkflowIdFactory, WorkflowIdFactory>();
        services.TryAddScoped<IWorkflowDispatcher, WorkflowDispatcher>();
        services.AddReadinessProbe<WorkflowServiceProbe>();
    }

    /// <inheritdoc />
    public ITemporalWorkflowsBuilder AddWorkflow<TWorkflow>()
        where TWorkflow : WorkflowBase
    {
        EnsureNotClientOnly();
        _workflowTypes.Add(typeof(TWorkflow));
        return this;
    }

    /// <inheritdoc />
    public ITemporalWorkflowsBuilder AddActivities<TActivities>()
        where TActivities : class
    {
        EnsureNotClientOnly();
        _activityTypes.Add(typeof(TActivities));
        _services.AddScoped<TActivities>();
        return this;
    }

    /// <inheritdoc />
    public ITemporalWorkflowsBuilder WithWorker(string taskQueue, Func<WorkerTuningOptions, WorkerTuningOptions>? tune = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskQueue);
        EnsureNotClientOnly();
        if (_taskQueue is not null)
        {
            throw new InvalidOperationException(
                $"WithWorker was already called for task queue '{_taskQueue}'. Only one worker composition is supported per builder, so remove the second WithWorker call.");
        }

        _taskQueue = taskQueue;
        _tune = tune;
        return this;
    }

    /// <inheritdoc />
    public ITemporalWorkflowsBuilder AsClientOnly()
    {
        if (_workflowTypes.Count > 0 || _activityTypes.Count > 0 || _taskQueue is not null)
        {
            throw new InvalidOperationException(
                "AsClientOnly() cannot be combined with AddWorkflow<T>(), AddActivities<T>() or WithWorker(...). Remove AsClientOnly() to host a worker, or remove the worker registrations for a dispatch-only service.");
        }

        _clientOnly = true;
        return this;
    }

    /// <inheritdoc />
    public ITemporalWorkflowsBuilder WithPayloadEncryption()
    {
        _payloadEncryption = true;
        return this;
    }

    /// <inheritdoc />
    public ITemporalWorkflowsBuilder WithOpenTelemetry()
    {
        _openTelemetry = true;
        return this;
    }

    /// <inheritdoc />
    public ITemporalWorkflowsBuilder WithMetrics()
    {
        _metrics = true;
        return this;
    }

    /// <inheritdoc />
    public ITemporalWorkflowsBuilder AllowRawClientAccess()
    {
        _rawClientAccess = true;
        return this;
    }

    /// <inheritdoc />
    public IServiceCollection Build()
    {
        if (_built)
        {
            throw new InvalidOperationException("Build() was already called on this builder. Call Build() once per composition.");
        }

        _built = true;

        return _clientOnly ? BuildClientOnly() : BuildWorkerHosting();
    }

    private void EnsureNotClientOnly()
    {
        if (_clientOnly)
        {
            throw new InvalidOperationException(
                "AddWorkflow<T>(), AddActivities<T>() and WithWorker(...) cannot be called after AsClientOnly(). Remove AsClientOnly() to host a worker.");
        }
    }

    private IServiceCollection BuildClientOnly()
    {
        ConfigureClient();
        return _services;
    }

    private IServiceCollection BuildWorkerHosting()
    {
        if (_taskQueue is null)
        {
            throw new InvalidOperationException(
                "No task queue is configured. Call WithWorker(taskQueue) to host a worker, or AsClientOnly() for a dispatch-only service.");
        }

        if (_workflowTypes.Count == 0 && _activityTypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"The worker for task queue '{_taskQueue}' has no workflows and no activities. Register them with AddWorkflow<T>() or AddActivities<T>(), or call AsClientOnly().");
        }

        ConfigureClient();

        // The (taskQueue, buildId) overload is obsolete in favor of a WorkerDeploymentOptions overload
        // introducing Temporal's worker-versioning/deployment feature — a genuinely separate SDK
        // concept this domain's locked D-01..D-16 contract does not model. Suppressed narrowly at
        // this single call site rather than adopting an unplanned new concept; revisit if/when worker
        // versioning is designed as its own capability.
#pragma warning disable CS0618
        ITemporalWorkerServiceOptionsBuilder workerBuilder = _services.AddHostedTemporalWorker(_taskQueue, buildId: "1.0.0");
#pragma warning restore CS0618

        foreach (Type workflowType in _workflowTypes)
        {
            try
            {
                // Temporalio.Extensions.Hosting's ITemporalWorkerServiceOptionsBuilder.AddWorkflow(Type)
                // defers [Workflow]-attribute validation until the hosted worker actually starts — it
                // does NOT validate eagerly the way the core SDK's WorkflowDefinition.Create(Type) (and
                // TemporalWorkerOptions.AddWorkflow(Type)) does. Verified empirically against the real
                // 1.17.0 assembly at Tests phase (T-08): calling the hosting builder's AddWorkflow with a
                // non-[Workflow] type does not throw, which would leave "Build() validates composition
                // eagerly" silently false for this specific case. Calling the SDK's own validating
                // factory here surfaces the error at Build() time instead of at first worker poll.
                WorkflowDefinition.Create(workflowType);
                workerBuilder.AddWorkflow(workflowType);
            }
            catch (ArgumentException exception)
            {
                // The SDK's definition factory rejects a type without [Workflow] or with an invalid run method.
                throw new InvalidOperationException(
                    $"'{workflowType.Name}' could not be registered as a workflow. Mark the type with [Workflow] and give it a valid [WorkflowRun] method.",
                    exception);
            }
        }

        foreach (Type activityType in _activityTypes)
        {
            workerBuilder.AddScopedActivities(activityType);
        }

        WorkerTuningOptions tuning = _tune?.Invoke(WorkerTuningOptions.Default) ?? WorkerTuningOptions.Default;
        workerBuilder.Services.Configure<TemporalWorkerServiceOptions>(options =>
        {
            // No worker-level interceptors: TemporalWorkerOptions.Interceptors documents that the worker includes every
            // client interceptor that implements IWorkerInterceptor (WorkflowPropagationInterceptor, TracingInterceptor)
            // and says those should not be specified again here.
            options.MaxConcurrentWorkflowTasks = tuning.MaxConcurrentWorkflowTasks;
            options.MaxConcurrentActivities = tuning.MaxConcurrentActivities;
            options.MaxConcurrentLocalActivities = tuning.MaxConcurrentLocalActivities;
            options.MaxCachedWorkflows = tuning.MaxCachedWorkflows;
            options.GracefulShutdownTimeout = tuning.GracefulShutdownTimeout;
        });

        // AsClientOnly() registers no IHostedService (hard rule) — this composition-summary logger is
        // therefore only ever added on the worker-hosting path, never in BuildClientOnly().
        _services.AddSingleton(new TemporalWorkflowsCompositionSummary(
            TaskQueue: _taskQueue,
            WorkflowCount: _workflowTypes.Count,
            ActivityCount: _activityTypes.Count));
        _services.AddHostedService<TemporalWorkflowsCompositionLogger>();

        return _services;
    }

    private void ConfigureClient()
    {
        string targetHost = _section[nameof(TemporalOptions.TargetHost)]
            ?? throw new InvalidOperationException(
                $"The configuration key '{TemporalOptions.SectionName}:{nameof(TemporalOptions.TargetHost)}' is not set. Set it to the Temporal service address.");
        string @namespace = _section[nameof(TemporalOptions.Namespace)]
            ?? throw new InvalidOperationException(
                $"The configuration key '{TemporalOptions.SectionName}:{nameof(TemporalOptions.Namespace)}' is not set. Set it to the Temporal namespace.");

        OptionsBuilder<TemporalClientConnectOptions> connectBuilder = _services.AddTemporalClient(targetHost, @namespace);

        bool enableOpenTelemetry = _openTelemetry;
        bool enableMetrics = _metrics;
        bool clientOnly = _clientOnly;
        connectBuilder.Configure<IOptions<TemporalOptions>, IMeterFactory, ILogger<TemporalWorkflowsBuilder>>((connectOptions, temporalOptions, meterFactory, logger) =>
        {
            TemporalOptions opts = temporalOptions.Value;
            if (opts.Tls)
            {
                connectOptions.Tls = new TlsOptions();
            }

            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
            {
                connectOptions.ApiKey = opts.ApiKey;
            }

            if (!string.IsNullOrWhiteSpace(opts.IdentityPrefix))
            {
                connectOptions.Identity = $"{opts.IdentityPrefix}-{Environment.MachineName}";
            }

            List<IClientInterceptor> interceptors = [new WorkflowPropagationInterceptor()];
            if (enableOpenTelemetry)
            {
                // The workflow id is {tenant}:{type}:{businessKey}; the SDK would tag every span with it. Spans carry
                // no tenant or business key, so the tag is suppressed (null disables it). The sources are unchanged.
                interceptors.Add(new TracingInterceptor(new TracingInterceptorOptions { TagNameWorkflowId = null }));
            }

            connectOptions.Interceptors = interceptors;

            if (enableMetrics)
            {
                connectOptions.Runtime = new TemporalRuntime(new TemporalRuntimeOptions
                {
                    Telemetry = new TelemetryOptions
                    {
                        Metrics = new MetricsOptions
                        {
                            CustomMetricMeter = new Temporalio.Extensions.DiagnosticSource.CustomMetricMeter(
                                WorkflowDiagnostics.CreateMeter(meterFactory),
                                disableWorkflowTracingEventListener: false),
                        },
                    },
                });
            }

            if (clientOnly)
            {
                // A client-only composition hosts nothing to log at startup, so its summary is logged once, when the client is first built.
                WorkflowLog.ClientOnlyBuilt(logger);
            }
        });

        if (_payloadEncryption)
        {
            connectBuilder.Configure<ISymmetricEncryptionService, ILogger<EncryptionPayloadCodec>>((connectOptions, encryptionService, codecLogger) =>
            {
                connectOptions.DataConverter = DataConverter.Default with
                {
                    PayloadCodec = new EncryptionPayloadCodec(encryptionService, codecLogger),
                };
            });
        }

        if (_rawClientAccess)
        {
            _services.TryAddSingleton<ITemporalRawClientAccessor, TemporalRawClientAccessor>();
        }
    }
}
