using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.Commands;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Application.Behaviors.Metrics;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Behaviors.Validation;
using SharedKernel.Application.Context;

namespace SharedKernel.Application.Behaviors.Extensions;

/// <summary>
/// Builds the opt-in MediatR pipeline behavior registration for <c>SharedKernel.Application.Behaviors</c>.
/// </summary>
/// <remarks>
/// <para>
/// Use the <c>.AddXBehavior()</c> methods to opt in to a built-in behavior, and
/// <see cref="AddBehavior"/> to append a custom behavior into one of the five
/// <see cref="PipelineStage"/> slots. Call <see cref="Build"/> to register everything opted into —
/// always in the fixed canonical order (Observability → Authorization → Validation → Query →
/// Command), regardless of the order the <c>.AddXBehavior()</c>/<see cref="AddBehavior"/> calls were
/// made in.
/// </para>
/// <para>
/// <see cref="ICommandScope"/> is always registered by <see cref="Build"/>, independent of whether
/// any command-stage behavior is active, so a handler may always inject it. The pipeline-level
/// <c>CommandScopeBehavior</c> itself is registered only when at least one command-stage behavior
/// (idempotency, transaction, auditing, or a custom <see cref="PipelineStage.Command"/> behavior) is
/// active — with nothing in the command stage, there is nothing for it to sequence.
/// </para>
/// </remarks>
public sealed class ApplicationBehaviorsBuilder
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<PipelineStage, List<(Type BehaviorType, Type[] RequiredServices)>> _customBehaviors = new()
    {
        [PipelineStage.Observability] = [],
        [PipelineStage.Authorization] = [],
        [PipelineStage.Validation] = [],
        [PipelineStage.Query] = [],
        [PipelineStage.Command] = [],
    };

    private bool _tracing;
    private bool _logging;
    private bool _metrics;
    private bool _authorization;
    private bool _validation;
    private bool _idempotency;
    private bool _transaction;
    private bool _auditing;
    private bool _built;

    internal ApplicationBehaviorsBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Opts in to the zero-prerequisite onboarding preset: <c>TracingBehavior</c>,
    /// <c>LoggingBehavior</c>, <c>MetricsBehavior</c>, and <c>ValidationBehavior</c>.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Delegates to the four individual <c>.AddXBehavior()</c> methods below — provably equivalent,
    /// not a reimplementation. These four are the only behaviors in this domain that carry zero
    /// <see cref="Build"/>-time missing-dependency guard; every infrastructure-gated behavior
    /// (Authorization, Idempotency, Transaction, Auditing, and anything registered via
    /// <see cref="AddBehavior"/>) remains a deliberate, individual opt-in.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddDefaultBehaviors()
    {
        AddTracingBehavior();
        AddLoggingBehavior();
        AddMetricsBehavior();
        AddValidationBehavior();
        return this;
    }

    /// <summary>Opts in to <c>TracingBehavior</c>.</summary>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationBehaviorsBuilder AddTracingBehavior()
    {
        _tracing = true;
        return this;
    }

    /// <summary>Opts in to <c>LoggingBehavior</c>.</summary>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationBehaviorsBuilder AddLoggingBehavior()
    {
        _logging = true;
        return this;
    }

    /// <summary>Opts in to <c>MetricsBehavior</c>.</summary>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationBehaviorsBuilder AddMetricsBehavior()
    {
        _metrics = true;
        return this;
    }

    /// <summary>Opts in to <c>AuthorizationBehavior</c>.</summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="IRequestContext"/> is not registered in the service collection when this was
    /// called.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddAuthorizationBehavior()
    {
        _authorization = true;
        return this;
    }

    /// <summary>Opts in to <c>ValidationBehavior</c>.</summary>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationBehaviorsBuilder AddValidationBehavior()
    {
        _validation = true;
        return this;
    }

    /// <summary>Opts in to <c>IdempotencyBehavior</c>.</summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="IRequestIdempotencyStore"/> is not registered in the service collection when this
    /// was called.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddIdempotencyBehavior()
    {
        _idempotency = true;
        return this;
    }

    /// <summary>Opts in to <c>TransactionBehavior</c>.</summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="IUnitOfWork"/> is not registered in the service collection when this was called.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddTransactionBehavior()
    {
        _transaction = true;
        return this;
    }

    /// <summary>Opts in to <c>AuditingBehavior</c>.</summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="IAuditTrailWriter"/> is not registered in the service collection when this was
    /// called.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddAuditingBehavior()
    {
        _auditing = true;
        return this;
    }

    /// <summary>
    /// Appends a custom open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/>
    /// implementation into <paramref name="stage"/>, running after that stage's built-in behaviors
    /// (if any) and after any other custom behavior already added to the same stage.
    /// </summary>
    /// <param name="openGenericBehaviorType">
    /// An open generic type definition (e.g. <c>typeof(CachingBehavior&lt;,&gt;)</c>) implementing
    /// <see cref="IPipelineBehavior{TRequest,TResponse}"/>.
    /// </param>
    /// <param name="stage">The pipeline stage to append the behavior into.</param>
    /// <param name="requiredServices">
    /// Service types that must already be registered in the service collection for this behavior to
    /// function. Checked by <see cref="Build"/>, not by this method.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationBehaviorsBuilder AddBehavior(
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

        entries.Add((openGenericBehaviorType, requiredServices));
        return this;
    }

    /// <summary>
    /// Registers the opted-into behaviors, always in the fixed canonical order, regardless of
    /// <c>.AddXBehavior()</c>/<see cref="AddBehavior"/> call order.
    /// </summary>
    /// <returns>The underlying <see cref="IServiceCollection"/>, for further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Build"/> was already called once on this builder instance, or a built-in behavior
    /// was opted into without its required service registered, or a custom behavior added via
    /// <see cref="AddBehavior"/> is not an open generic type implementing
    /// <see cref="IPipelineBehavior{TRequest,TResponse}"/>, or one of its declared
    /// <c>requiredServices</c> is not registered.
    /// </exception>
    /// <remarks>
    /// Does not call <c>services.AddMediatR(...)</c> — the consuming service already registers
    /// MediatR; this builder only appends behaviors via
    /// <c>services.AddTransient(typeof(IPipelineBehavior&lt;,&gt;), ...)</c>.
    /// </remarks>
    /// <remarks>
    /// Always calls <c>services.AddLogging()</c> (idempotent — safe even if the consumer already
    /// called it, and a no-op default logging pipeline if nothing else configures one). Several
    /// behaviors this builder can register (<c>CommandScopeBehavior</c>, <c>IdempotencyBehavior</c>)
    /// constructor-inject an <c>ILogger&lt;T&gt;</c>; without this call, a bare
    /// <see cref="IServiceCollection"/> with no prior <c>AddLogging()</c> would fail to resolve those
    /// loggers at the first dispatch rather than at this deterministic <see cref="Build"/> call.
    /// </remarks>
    public IServiceCollection Build()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "Build() has already been called on this ApplicationBehaviorsBuilder instance. " +
                "Calling it again would register every opted-in behavior a second time. Start a new " +
                "pipeline with a fresh AddSharedKernelApplicationBehaviors() call instead.");
        }

        _built = true;

        _services.AddLogging();

        if (_authorization && !IsRegistered(typeof(IRequestContext)))
        {
            throw new InvalidOperationException(
                "AddAuthorizationBehavior() requires SharedKernel.Application.Context.IRequestContext " +
                "to be registered in the service collection. Register an implementation before calling Build().");
        }

        if (_idempotency && !IsRegistered(typeof(IRequestIdempotencyStore)))
        {
            throw new InvalidOperationException(
                "AddIdempotencyBehavior() requires SharedKernel.Application.Behaviors.Idempotency.IRequestIdempotencyStore " +
                "to be registered in the service collection. Register an implementation before calling Build().");
        }

        if (_transaction && !IsRegistered(typeof(IUnitOfWork)))
        {
            throw new InvalidOperationException(
                "AddTransactionBehavior() requires SharedKernel.Application.Behaviors.Transaction.IUnitOfWork " +
                "to be registered in the service collection. Register an implementation before calling Build().");
        }

        if (_auditing && !IsRegistered(typeof(IAuditTrailWriter)))
        {
            throw new InvalidOperationException(
                "AddAuditingBehavior() requires SharedKernel.Application.Behaviors.Auditing.IAuditTrailWriter " +
                "to be registered in the service collection. Register an implementation before calling Build().");
        }

        foreach (var entries in _customBehaviors.Values)
        {
            foreach (var entry in entries)
                ValidateCustomBehavior(entry);
        }

        // ICommandScope is always available for a handler to inject, whether or not any
        // command-stage behavior is active.
        _services.TryAddScoped<CommandScope>();
        _services.TryAddScoped<ICommandScope>(sp => sp.GetRequiredService<CommandScope>());

        // ---- Observability stage: Tracing, Logging, Metrics, then any custom entries. ----
        if (_tracing)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Tracing.TracingBehavior<,>));

        if (_logging)
        {
            _services.AddOptions<ApplicationLoggingOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        }

        if (_metrics)
        {
            _services.AddMetrics();
            _services.TryAddSingleton<ApplicationMetrics>();
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Metrics.MetricsBehavior<,>));
        }

        RegisterCustom(PipelineStage.Observability);

        // ---- Authorization stage. ----
        if (_authorization)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));

        RegisterCustom(PipelineStage.Authorization);

        // ---- Validation stage. ----
        if (_validation)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        RegisterCustom(PipelineStage.Validation);

        // ---- Query stage: no built-in of its own — custom entries only (e.g. CachingBehavior). ----
        RegisterCustom(PipelineStage.Query);

        // ---- Command stage: CommandScope, then Idempotency, Transaction, Auditing, then custom
        // entries (e.g. CacheInvalidationBehavior). CommandScopeBehavior is registered only when
        // the command stage is genuinely active — with nothing in it, there is nothing to sequence.
        var commandStageActive = _idempotency || _transaction || _auditing
            || _customBehaviors[PipelineStage.Command].Count > 0;

        if (commandStageActive)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CommandScopeBehavior<,>));

        if (_idempotency)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));

        if (_transaction)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        if (_auditing)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditingBehavior<,>));

        RegisterCustom(PipelineStage.Command);

        return _services;
    }

    private void RegisterCustom(PipelineStage stage)
    {
        foreach (var entry in _customBehaviors[stage])
            _services.AddTransient(typeof(IPipelineBehavior<,>), entry.BehaviorType);
    }

    private void ValidateCustomBehavior((Type BehaviorType, Type[] RequiredServices) entry)
    {
        var type = entry.BehaviorType;

        if (!type.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException(
                $"AddBehavior: '{type.FullName}' must be an open generic type definition " +
                "implementing IPipelineBehavior<,>.");
        }

        var implementsPipelineBehavior = type.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

        if (!implementsPipelineBehavior)
        {
            throw new InvalidOperationException(
                $"AddBehavior: '{type.FullName}' does not implement IPipelineBehavior<,>.");
        }

        foreach (var required in entry.RequiredServices)
        {
            if (!IsRegistered(required))
            {
                throw new InvalidOperationException(
                    $"AddBehavior: '{type.FullName}' requires '{required.FullName}' to be registered " +
                    "in the service collection. Register it before calling Build().");
            }
        }
    }

    private bool IsRegistered(Type serviceType)
        => _services.Any(descriptor => descriptor.ServiceType == serviceType);
}
