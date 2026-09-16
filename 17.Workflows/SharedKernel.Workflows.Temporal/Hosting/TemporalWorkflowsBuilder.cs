using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Codec;
using SharedKernel.Workflows.Temporal.Configuration;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Diagnostics;
using SharedKernel.Workflows.Temporal.Errors;
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
        services.TryAddSingleton<IWorkflowIdFactory, WorkflowIdFactory>();
        services.TryAddScoped<IWorkflowDispatcher, WorkflowDispatcher>();
        services.TryAddSingleton<IWorkflowServiceProbe, WorkflowServiceProbe>();
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
                WorkflowErrors.InvalidWorkflowRegistration(
                    $"WithWorker was already called for task queue '{_taskQueue}' — only one worker composition is supported per builder").Message);
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
                "AsClientOnly() cannot be combined with AddWorkflow<T>(), AddActivities<T>(), or WithWorker(...).");
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
            throw new InvalidOperationException("Build() was already called on this builder.");
        }

        _built = true;

        return _clientOnly ? BuildClientOnly() : BuildWorkerHosting();
    }

    private void EnsureNotClientOnly()
    {
        if (_clientOnly)
        {
            throw new InvalidOperationException(
                "AddWorkflow<T>()/AddActivities<T>()/WithWorker(...) cannot be called after AsClientOnly().");
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
                WorkflowErrors.WorkerNotConfigured("no task queue configured — call WithWorker(taskQueue) or AsClientOnly()").Message);
        }

        if (_workflowTypes.Count == 0 && _activityTypes.Count == 0)
        {
            throw new InvalidOperationException(
                WorkflowErrors.InvalidWorkflowRegistration(
                    $"a worker was configured for task queue '{_taskQueue}' with zero workflows and zero activities").Message);
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
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    WorkflowErrors.InvalidWorkflowRegistration(
                        $"'{workflowType.Name}' could not be registered as a workflow: {exception.Message}").Message,
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
            options.Interceptors = [new WorkflowPropagationInterceptor()];
            options.MaxConcurrentWorkflowTasks = tuning.MaxConcurrentWorkflowTasks;
            options.MaxConcurrentActivities = tuning.MaxConcurrentActivities;
            options.MaxConcurrentLocalActivities = tuning.MaxConcurrentLocalActivities;
            options.MaxCachedWorkflows = tuning.MaxCachedWorkflows;
            options.GracefulShutdownTimeout = tuning.GracefulShutdownTimeout;
        });

        // AsClientOnly() registers no IHostedService (hard rule) — this composition-summary logger is
        // therefore only ever added on the worker-hosting path, never in BuildClientOnly().
        _services.AddSingleton(new TemporalWorkflowsCompositionSummary(
            ClientOnly: false,
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
                WorkflowErrors.InvalidWorkflowRegistration($"{TemporalOptions.SectionName}:{nameof(TemporalOptions.TargetHost)} is not configured").Message);
        string @namespace = _section[nameof(TemporalOptions.Namespace)]
            ?? throw new InvalidOperationException(
                WorkflowErrors.InvalidWorkflowRegistration($"{TemporalOptions.SectionName}:{nameof(TemporalOptions.Namespace)} is not configured").Message);

        OptionsBuilder<TemporalClientConnectOptions> connectBuilder = _services.AddTemporalClient(targetHost, @namespace);

        bool enableOpenTelemetry = _openTelemetry;
        bool enableMetrics = _metrics;
        connectBuilder.Configure<IOptions<TemporalOptions>>((connectOptions, temporalOptions) =>
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
                interceptors.Add(new TracingInterceptor());
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
                                WorkflowDiagnostics.Meter,
                                disableWorkflowTracingEventListener: false),
                        },
                    },
                });
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
