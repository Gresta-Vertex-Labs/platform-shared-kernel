using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Commands;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Auditing;
using SharedKernel.Application.Pipeline.Authorization;
using SharedKernel.Application.Pipeline.Commands;
using SharedKernel.Application.Pipeline.Idempotency;
using SharedKernel.Application.Pipeline.Logging;
using SharedKernel.Application.Pipeline.Metrics;
using SharedKernel.Application.Pipeline.Tracing;
using SharedKernel.Application.Pipeline.Transaction;
using SharedKernel.Application.Pipeline.Validation;
using SharedKernel.Application.Streaming;
using SharedKernel.Execution.Auditing;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Transactions;
using SharedKernel.Idempotency.Abstractions;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Chooses the opt-in parts of the application layer that
/// <see cref="ApplicationServiceCollectionExtensions.AddSharedKernelApplication(IServiceCollection, Assembly, Action{ApplicationPipelineBuilder})"/>
/// registers: the mediator that sends requests, and the opt-in behaviors.
/// </summary>
/// <remarks>
/// <para>
/// Tracing, logging, metrics, authorization (<c>[RequirePermission]</c>) and validation are always on. The
/// <c>With…</c> methods add the rest, and every behavior lands in the canonical order (Observability → Authorization
/// → Validation → Query → Command) whatever order the calls are made in.
/// </para>
/// <para>
/// A mediator plugs in through an extension method on this builder, such as <c>UseMediatR()</c>
/// (<c>SharedKernel.Application.Mediator.MediatR</c>): it registers the <see cref="ISender"/> for the handlers of
/// <see cref="Assemblies"/>. Without one, the host start fails naming <see cref="ISender"/>.
/// </para>
/// <para>
/// A behavior that needs a service (a seam such as <see cref="IUnitOfWork"/>) does not check for it here: the check
/// runs when the host starts, so the service may be registered before or after <c>AddSharedKernelApplication</c>. A
/// missing one fails the start with one message naming every missing service.
/// </para>
/// </remarks>
public sealed class ApplicationPipelineBuilder
{
    private readonly Dictionary<PipelineStage, List<Type>> _customBehaviors = new()
    {
        [PipelineStage.Observability] = [],
        [PipelineStage.Authorization] = [],
        [PipelineStage.Validation] = [],
        [PipelineStage.Query] = [],
        [PipelineStage.Command] = [],
    };

    private readonly List<PipelineRequirement> _requirements = [];

    private bool _idempotency;
    private Action<IdempotencyBehaviorOptions>? _configureIdempotency;
    private bool _transactions;
    private bool _auditing;

    internal ApplicationPipelineBuilder(IServiceCollection services, IReadOnlyList<Assembly> assemblies)
    {
        Services = services;
        Assemblies = assemblies;
    }

    /// <summary>
    /// Gets the service collection, so a package that plugs into the pipeline — a mediator, or a behavior added through
    /// <see cref="WithBehavior"/> — can also register what it needs.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Gets the assemblies passed to <c>AddSharedKernelApplication</c>, whose handlers, validators and domain-event
    /// handlers it registers. A mediator adapter reads them to route each request type to its pipeline.
    /// </summary>
    public IReadOnlyList<Assembly> Assemblies { get; }

    /// <summary>
    /// Deduplicates commands that implement <see cref="IIdempotentRequest"/>, reserving each key per tenant and
    /// caller. Needs an <see cref="IIdempotencyStore"/> registered for <see cref="IdempotencyPurpose.Request"/> (for
    /// example <c>AddRedisIdempotency(p =&gt; p.ForRequests())</c>) and <see cref="IRequestContext"/> (a host with no
    /// caller identity registers <see cref="AnonymousRequestContext.Instance"/> or a <see cref="SystemRequestContext"/>
    /// on purpose).
    /// </summary>
    /// <param name="configure">
    /// Optional delegate to set <see cref="IdempotencyBehaviorOptions"/> (reservation lease and retention window);
    /// validated when the host starts.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationPipelineBuilder WithIdempotency(Action<IdempotencyBehaviorOptions>? configure = null)
    {
        if (configure is not null)
            _configureIdempotency += configure;

        if (!_idempotency)
        {
            _idempotency = true;
            Require(nameof(WithIdempotency) + "()", typeof(IIdempotencyStore), IdempotencyPurpose.Request);
            Require(nameof(WithIdempotency) + "()", typeof(IRequestContext));
        }

        return this;
    }

    /// <summary>
    /// Runs every outermost command inside <c>IUnitOfWork.ExecuteInTransactionAsync</c>, so a failed result commits
    /// nothing. Needs <see cref="IUnitOfWork"/>.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationPipelineBuilder WithTransactions()
    {
        if (!_transactions)
        {
            _transactions = true;
            Require(nameof(WithTransactions) + "()", typeof(IUnitOfWork));
        }

        return this;
    }

    /// <summary>
    /// Records an audit entry for every command that implements <see cref="SharedKernel.Application.Auditing.IAuditableRequest{TResponse}"/>: a
    /// success inside the transaction, a failure after the rollback. Needs <see cref="IAuditTrailWriter"/>.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationPipelineBuilder WithAuditing()
    {
        if (!_auditing)
        {
            _auditing = true;
            Require(nameof(WithAuditing) + "()", typeof(IAuditTrailWriter));
        }

        return this;
    }

    /// <summary>
    /// Adds an open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/> to <paramref name="stage"/>, after that
    /// stage's built-in behaviors and after any behavior already added to it.
    /// </summary>
    /// <param name="openGenericBehaviorType">
    /// An open generic type definition implementing <see cref="IPipelineBehavior{TRequest,TResponse}"/>, for example
    /// <c>typeof(MyBehavior&lt;,&gt;)</c>. Adding the same type twice adds it once.
    /// </param>
    /// <param name="stage">The stage to add the behavior to.</param>
    /// <param name="requiredServices">Services the behavior needs; checked when the host starts, like the built-in seams.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="openGenericBehaviorType"/> is not an open generic type implementing
    /// <see cref="IPipelineBehavior{TRequest,TResponse}"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is not a defined stage.</exception>
    public ApplicationPipelineBuilder WithBehavior(
        Type openGenericBehaviorType,
        PipelineStage stage,
        params Type[] requiredServices)
    {
        ArgumentNullException.ThrowIfNull(openGenericBehaviorType);
        ArgumentNullException.ThrowIfNull(requiredServices);

        if (!_customBehaviors.TryGetValue(stage, out var entries))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                $"'{stage}' is not a defined {nameof(PipelineStage)} value. Use one of: " +
                string.Join(", ", Enum.GetNames<PipelineStage>()) + ".");
        }

        if (!openGenericBehaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"'{openGenericBehaviorType.FullName}' must be an open generic type definition " +
                "implementing IPipelineBehavior<,>, for example typeof(MyBehavior<,>).",
                nameof(openGenericBehaviorType));
        }

        var implementsPipelineBehavior = openGenericBehaviorType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

        if (!implementsPipelineBehavior)
        {
            throw new ArgumentException(
                $"'{openGenericBehaviorType.FullName}' does not implement IPipelineBehavior<,>.",
                nameof(openGenericBehaviorType));
        }

        if (entries.Contains(openGenericBehaviorType))
            return this;

        entries.Add(openGenericBehaviorType);

        foreach (var required in requiredServices)
        {
            ArgumentNullException.ThrowIfNull(required, nameof(requiredServices));
            Require($"WithBehavior(typeof({BehaviorName(openGenericBehaviorType)}))", required);
        }

        return this;
    }

    /// <summary>Registers the pipeline in canonical order. Called once, by the registration call.</summary>
    internal void Register(IReadOnlyList<Type> scannedTypes)
    {
        // ICommandScope is always available for a handler to inject, whether or not any
        // command-stage behavior is active.
        Services.TryAddScoped<CommandScope>();
        Services.TryAddScoped<ICommandScope>(sp => sp.GetRequiredService<CommandScope>());

        Services.TryAddTransient(typeof(RequestPipeline<,>));
        Services.TryAddTransient(typeof(StreamRequestPipeline<,>));

        // ---- Observability stage: Tracing, Logging, Metrics, then any custom entries. ----
        Services.AddOptions<ApplicationLoggingOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();
        Services.TryAddSingleton<ApplicationMetrics>();

        AddBehavior(typeof(TracingBehavior<,>));
        AddBehavior(typeof(LoggingBehavior<,>));
        AddBehavior(typeof(MetricsBehavior<,>));
        AddCustom(PipelineStage.Observability);

        // ---- Authorization stage: always on, so [RequirePermission] can never go unenforced. It resolves
        // IRequestContext only for a request that declares a permission. Streams get the same check. ----
        AddBehavior(typeof(AuthorizationBehavior<,>));
        Services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamAuthorizationBehavior<,>));
        AddCustom(PipelineStage.Authorization);

        // ---- Validation stage. ----
        AddBehavior(typeof(ValidationBehavior<,>));
        AddCustom(PipelineStage.Validation);

        // ---- Query stage: no built-in of its own (e.g. CachingBehavior). ----
        AddCustom(PipelineStage.Query);

        // ---- Command stage: CommandScope, Idempotency, Auditing (failures — outside the transaction), Transaction,
        // AuditingCommit (success — inside the transaction, via the unit of work's pre-commit hook), then custom
        // entries (e.g. CacheInvalidationBehavior). CommandScopeBehavior is registered only when the command stage is
        // genuinely active — with nothing in it, there is nothing to sequence.
        var commandStageActive = _idempotency || _transactions || _auditing
            || _customBehaviors[PipelineStage.Command].Count > 0;

        if (commandStageActive)
            AddBehavior(typeof(CommandScopeBehavior<,>));

        if (_idempotency)
        {
            var configureIdempotency = _configureIdempotency;
            Services.AddOptions<IdempotencyBehaviorOptions>()
                .Configure(o => configureIdempotency?.Invoke(o))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            AddBehavior(typeof(IdempotencyBehavior<,>));
        }

        if (_auditing)
            AddBehavior(typeof(AuditingBehavior<,>));

        if (_transactions)
            AddBehavior(typeof(TransactionBehavior<,>));

        if (_auditing)
            AddBehavior(typeof(AuditingCommitBehavior<,>));

        AddCustom(PipelineStage.Command);

        // [RequirePermission] anywhere in the scanned assemblies demands a caller identity at host start.
        var marked = PermissionRequirementScan.FindMarkedRequestTypes(scannedTypes);
        if (marked.Count > 0)
            Require(PermissionRequirementScan.Describe(marked), typeof(IRequestContext));

        // Sending needs a mediator.
        Require(
            "AddSharedKernelApplication (commands and queries are sent through a mediator: add one on the builder, " +
            "for example app => app.UseMediatR() from SharedKernel.Application.Mediator.MediatR)",
            typeof(ISender));

        // The seams are checked when the host starts, so they may be registered after this call.
        Services.AddSingleton(new PipelineRequirements(_requirements));
        Services.AddSingleton<IValidateOptions<PipelineRequirementsOptions>, PipelineRequirementsValidator>();
        Services.AddOptions<PipelineRequirementsOptions>().ValidateOnStart();
    }

    private void Require(string feature, Type service, object? serviceKey = null)
        => _requirements.Add(new PipelineRequirement(feature, service, serviceKey));

    // "CachingBehavior`2" → "CachingBehavior<,>", as the type is written in a typeof expression.
    private static string BehaviorName(Type openGenericType)
    {
        var name = openGenericType.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0
            ? name
            : name[..tick] + "<" + new string(',', openGenericType.GetGenericArguments().Length - 1) + ">";
    }

    private void AddBehavior(Type openGenericBehaviorType)
        => Services.AddTransient(typeof(IPipelineBehavior<,>), openGenericBehaviorType);

    private void AddCustom(PipelineStage stage)
    {
        foreach (var behaviorType in _customBehaviors[stage])
            AddBehavior(behaviorType);
    }
}
