using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly.Registry;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Caching;
using SharedKernel.Application.Behaviors.DualApproval;
using SharedKernel.Application.Behaviors.FireAndForget;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Application.Behaviors.Metrics;
using SharedKernel.Application.Behaviors.Resilience;
using SharedKernel.Application.Behaviors.Streaming;
using SharedKernel.Application.Behaviors.Tracing;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Behaviors.Validation;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Streaming;
using SharedKernel.Caching.Abstractions;
using System.Threading.Channels;

namespace SharedKernel.Application.Behaviors.Extensions;

/// <summary>
/// Builds the opt-in MediatR pipeline behavior registration for <c>SharedKernel.Application.Behaviors</c>.
/// </summary>
/// <remarks>
/// Use <c>.AddXBehavior()</c> methods to opt in to individual behaviors, then call
/// <see cref="Build"/> to register them. Registration order is always the fixed canonical
/// twelve-named-slot order (Logging → Metrics → Tracing → Validation → Authorization →
/// DualApproval → Caching → Resilience → Idempotency → Auditing → Transaction →
/// CacheInvalidation) regardless of the order in which <c>.AddXBehavior()</c> methods were called.
/// </remarks>
public sealed class ApplicationBehaviorsBuilder
{
    private readonly IServiceCollection _services;
    private bool _validation;
    private bool _logging;
    private bool _metrics;
    private bool _tracing;
    private bool _authorization;
    private bool _dualApproval;
    private bool _caching;
    private bool _resilience;
    private bool _idempotency;
    private bool _auditing;
    private bool _transaction;
    private bool _cacheInvalidation;
    private bool _fireAndForget;
    private Action<FireAndForgetOptions>? _fireAndForgetConfigure;
    private bool _streaming;

    internal ApplicationBehaviorsBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Opts in to the zero-prerequisite onboarding preset: <see cref="LoggingBehavior{TRequest,TResponse}"/>,
    /// <see cref="MetricsBehavior{TRequest,TResponse}"/>, <see cref="TracingBehavior{TRequest,TResponse}"/>,
    /// and <see cref="ValidationBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Delegates to the four individual <c>.AddXBehavior()</c> methods below — provably equivalent,
    /// not a reimplementation. These four are the only behaviors in this domain that carry zero
    /// <see cref="Build"/>-time missing-dependency guard: every other behavior requires a registered
    /// local-seam or infrastructure bridge (<see cref="IUnitOfWork"/>, <see cref="ICacheService"/>,
    /// <see cref="IAuthorizationContext"/>, <see cref="IIdempotencyKeyStore"/>, a Polly resilience
    /// pipeline) and must remain a deliberate, individual opt-in — no other behavior is ever eligible
    /// for this preset.
    /// </para>
    /// <para>
    /// <b>Composable, not exclusive (WO-039, P-243):</b> each underlying <c>.AddXBehavior()</c> call
    /// only sets a <see langword="bool"/> opt-in flag, so calling <see cref="AddDefaultBehaviors"/>
    /// alongside any individual call to <see cref="AddLoggingBehavior"/>/<see cref="AddMetricsBehavior"/>/
    /// <see cref="AddTracingBehavior"/>/<see cref="AddValidationBehavior"/> for the same behavior is
    /// idempotent — <see cref="Build"/> still registers exactly one <see cref="IPipelineBehavior{TRequest,TResponse}"/>
    /// per behavior, in the unchanged fixed canonical order. This preset never throws
    /// <see cref="InvalidOperationException"/> from <see cref="Build"/> on its own.
    /// </para>
    /// </remarks>
    public ApplicationBehaviorsBuilder AddDefaultBehaviors()
    {
        AddLoggingBehavior();
        AddMetricsBehavior();
        AddTracingBehavior();
        AddValidationBehavior();
        return this;
    }

    /// <summary>Opts in to <see cref="ValidationBehavior{TRequest,TResponse}"/>.</summary>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationBehaviorsBuilder AddValidationBehavior()
    {
        _validation = true;
        return this;
    }

    /// <summary>Opts in to <see cref="LoggingBehavior{TRequest,TResponse}"/>.</summary>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationBehaviorsBuilder AddLoggingBehavior()
    {
        _logging = true;
        return this;
    }

    /// <summary>Opts in to <see cref="MetricsBehavior{TRequest,TResponse}"/>.</summary>
    /// <returns>This builder, for chaining.</returns>
    public ApplicationBehaviorsBuilder AddMetricsBehavior()
    {
        _metrics = true;
        return this;
    }

    /// <summary>Opts in to <see cref="TracingBehavior{TRequest,TResponse}"/>.</summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// No missing-dependency guard needed: <c>ApplicationDiagnostics.ActivitySource</c> is always
    /// available (a BCL static instance), exactly like <see cref="AddMetricsBehavior"/>/
    /// <see cref="AddLoggingBehavior"/> carry no guard.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddTracingBehavior()
    {
        _tracing = true;
        return this;
    }

    /// <summary>
    /// Opts in to <see cref="AuthorizationBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="IAuthorizationContext"/> is not registered in the service collection when this
    /// was called.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddAuthorizationBehavior()
    {
        _authorization = true;
        return this;
    }

    /// <summary>
    /// Opts in to <see cref="DualApprovalBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// This domain's FIRST two-dependency <see cref="Build"/>-time guard: throws
    /// <see cref="InvalidOperationException"/> naming <see cref="IAuthorizationContext"/> if it is
    /// not registered, and a distinct <see cref="InvalidOperationException"/> naming
    /// <see cref="IDualApprovalStore"/> if that is not registered — both are required for
    /// <see cref="DualApprovalBehavior{TRequest,TResponse}"/> to function (identity resolution and
    /// approval-record lookup respectively).
    /// </remarks>
    public ApplicationBehaviorsBuilder AddDualApprovalBehavior()
    {
        _dualApproval = true;
        return this;
    }

    /// <summary>Opts in to <see cref="CachingBehavior{TRequest,TResponse}"/>.</summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="ICacheService"/> is not registered in the service collection when this was
    /// called.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddCachingBehavior()
    {
        _caching = true;
        return this;
    }

    /// <summary>Opts in to <see cref="ResilienceBehavior{TRequest,TResponse}"/>.</summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="ResiliencePipelineProvider{TKey}"/> of <see cref="string"/> is not registered in
    /// the service collection when this was called.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddResilienceBehavior()
    {
        _resilience = true;
        return this;
    }

    /// <summary>
    /// Opts in to <see cref="IdempotentCommandBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="IIdempotencyKeyStore"/> is not registered in the service collection when this
    /// was called.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddIdempotencyBehavior()
    {
        _idempotency = true;
        return this;
    }

    /// <summary>
    /// Opts in to <see cref="AuditingBehavior{TRequest,TResponse}"/>.
    /// </summary>
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

    /// <summary>Opts in to <see cref="TransactionBehavior{TRequest,TResponse}"/>.</summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="IUnitOfWork"/> is not registered in the service collection when this was
    /// called.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddTransactionBehavior()
    {
        _transaction = true;
        return this;
    }

    /// <summary>Opts in to <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/>.</summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> reuses the exact same <see cref="ICacheService"/> missing-dependency
    /// guard already enforced for <see cref="AddCachingBehavior"/> — not a duplicated guard.
    /// Calling only <see cref="AddCacheInvalidationBehavior"/> without
    /// <see cref="AddCachingBehavior"/> still requires <see cref="ICacheService"/> to be
    /// registered; the check is keyed on the dependency, not on which <c>.AddXBehavior()</c> call
    /// requested it.
    /// </remarks>
    public ApplicationBehaviorsBuilder AddCacheInvalidationBehavior()
    {
        _cacheInvalidation = true;
        return this;
    }

    /// <summary>
    /// Opts in to fire-and-forget command dispatch infrastructure: <see cref="IFireAndForgetDispatcher"/>,
    /// <see cref="FireAndForgetBackgroundConsumer"/>, and <see cref="FireAndForgetGuardBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <param name="configure">
    /// An optional delegate to configure <see cref="FireAndForgetOptions"/> (channel capacity,
    /// rejection policy). Pass <see langword="null"/> to use the defaults (capacity 1000,
    /// <see cref="FireAndForgetRejectionPolicy.DropAndLog"/>).
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registers the following on <see cref="Build"/>:
    /// <list type="bullet">
    ///   <item><description><see cref="IFireAndForgetDispatcher"/> → <see cref="ChannelFireAndForgetDispatcher"/> (singleton).</description></item>
    ///   <item><description><see cref="FireAndForgetBackgroundConsumer"/> as <see cref="IHostedService"/> (singleton).</description></item>
    ///   <item><description><see cref="FireAndForgetGuardBehavior{TRequest,TResponse}"/> as an open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/>.</description></item>
    /// </list>
    /// None of these are registered unless this method is called — entirely opt-in.
    /// </para>
    /// <para>
    /// <b>Note:</b> <see cref="FireAndForgetBackgroundConsumer"/> uses <c>ISender.Send</c>
    /// internally to dispatch commands through the MediatR pipeline. That internal dispatch is
    /// marked as trusted (WO-039, P-238) via an unspoofable ambient marker, so
    /// <see cref="FireAndForgetGuardBehavior{TRequest,TResponse}"/> — registered by this same method
    /// as a global behavior — permits the consumer's own dispatch through while still rejecting any
    /// external caller's direct <c>ISender.Send</c> attempt for the same command types. Calling this
    /// method once, exactly as documented, wires a fully functional unit — no special composition-root
    /// ordering or separation is required.
    /// </para>
    /// </remarks>
    public ApplicationBehaviorsBuilder AddFireAndForgetDispatch(Action<FireAndForgetOptions>? configure = null)
    {
        _fireAndForget = true;
        _fireAndForgetConfigure = configure;
        return this;
    }

    /// <summary>
    /// Opts in to streaming pipeline behaviors for <see cref="IStreamQuery{TResponse}"/> requests:
    /// <see cref="StreamLoggingBehavior{TRequest,TResponse}"/>,
    /// <see cref="StreamMetricsBehavior{TRequest,TResponse}"/>,
    /// <see cref="StreamTracingBehavior{TRequest,TResponse}"/>,
    /// <see cref="StreamValidationBehavior{TRequest,TResponse}"/>, and
    /// <see cref="StreamAuthorizationBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registers all five streaming behaviors against the open-generic
    /// <see cref="IStreamPipelineBehavior{TRequest,TResponse}"/> in the canonical streaming order
    /// (Logging → Metrics → Tracing → Validation → Authorization) — the same positional logic as
    /// the first five unary pipeline steps.
    /// </para>
    /// <para>
    /// <see cref="Build"/> throws <see cref="InvalidOperationException"/> if
    /// <see cref="IAuthorizationContext"/> is not registered (required by
    /// <see cref="StreamAuthorizationBehavior{TRequest,TResponse}"/>).
    /// </para>
    /// <para>
    /// This method is distinct from the unary <c>.AddXBehavior()</c> methods — calling neither is
    /// valid; calling both registers both unary and streaming behaviors; calling only one registers
    /// only that path. <b>Non-applicable streaming behaviors</b> (Transaction, Caching,
    /// CacheInvalidation, Idempotency, Resilience) are intentionally excluded — see
    /// <c>05.Application/CLAUDE.md</c> for the rationale.
    /// </para>
    /// </remarks>
    public ApplicationBehaviorsBuilder AddStreamingBehaviors()
    {
        _streaming = true;
        return this;
    }

    /// <summary>
    /// Registers the opted-into behaviors, always in the fixed canonical order, regardless of
    /// <c>.AddXBehavior()</c> call order.
    /// </summary>
    /// <returns>The underlying <see cref="IServiceCollection"/>, for further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="AddTransactionBehavior"/> was opted into without
    /// <see cref="IUnitOfWork"/> registered, when <see cref="AddCachingBehavior"/> or
    /// <see cref="AddCacheInvalidationBehavior"/> was opted into without
    /// <see cref="ICacheService"/> registered, when <see cref="AddAuthorizationBehavior"/>,
    /// <see cref="AddDualApprovalBehavior"/>, or <see cref="AddStreamingBehaviors"/> was opted into
    /// without <see cref="IAuthorizationContext"/> registered, when
    /// <see cref="AddDualApprovalBehavior"/> was opted into without <see cref="IDualApprovalStore"/>
    /// registered, when <see cref="AddIdempotencyBehavior"/> was opted into without
    /// <see cref="IIdempotencyKeyStore"/> registered, when <see cref="AddAuditingBehavior"/> was
    /// opted into without <see cref="IAuditTrailWriter"/> registered, or when
    /// <see cref="AddResilienceBehavior"/> was opted into without
    /// <see cref="ResiliencePipelineProvider{TKey}"/> of <see cref="string"/> registered.
    /// </exception>
    /// <remarks>
    /// Does not call <c>services.AddMediatR(...)</c> — the consuming service already registers
    /// MediatR; this builder only appends behaviors via
    /// <c>services.AddTransient(typeof(IPipelineBehavior&lt;,&gt;), ...)</c> and
    /// <c>services.AddTransient(typeof(IStreamPipelineBehavior&lt;,&gt;), ...)</c>.
    /// </remarks>
    public IServiceCollection Build()
    {
        if (_transaction && !IsRegistered<IUnitOfWork>())
            throw new InvalidOperationException(
                "AddTransactionBehavior() requires SharedKernel.Application.Behaviors.Transaction.IUnitOfWork " +
                "to be registered in the service collection. Register an implementation before calling Build().");

        if ((_caching || _cacheInvalidation) && !IsRegistered<ICacheService>())
            throw new InvalidOperationException(
                "AddCachingBehavior()/AddCacheInvalidationBehavior() require SharedKernel.Caching.Abstractions.ICacheService " +
                "to be registered in the service collection. Register an implementation before calling Build().");

        if ((_authorization || _dualApproval || _streaming) && !IsRegistered<IAuthorizationContext>())
            throw new InvalidOperationException(
                "AddAuthorizationBehavior()/AddDualApprovalBehavior()/AddStreamingBehaviors() require " +
                "SharedKernel.Application.Behaviors.Authorization.IAuthorizationContext to be registered " +
                "in the service collection. Register an implementation before calling Build().");

        if (_dualApproval && !IsRegistered<IDualApprovalStore>())
            throw new InvalidOperationException(
                "AddDualApprovalBehavior() requires SharedKernel.Application.Behaviors.DualApproval.IDualApprovalStore " +
                "to be registered in the service collection. Register an implementation before calling Build().");

        if (_idempotency && !IsRegistered<IIdempotencyKeyStore>())
            throw new InvalidOperationException(
                "AddIdempotencyBehavior() requires SharedKernel.Application.Behaviors.Idempotency.IIdempotencyKeyStore " +
                "to be registered in the service collection. Register an implementation before calling Build().");

        if (_resilience && !IsRegistered<ResiliencePipelineProvider<string>>())
            throw new InvalidOperationException(
                "AddResilienceBehavior() requires Polly.Registry.ResiliencePipelineProvider<string> " +
                "to be registered in the service collection. Register one before calling Build().");

        if (_auditing && !IsRegistered<IAuditTrailWriter>())
            throw new InvalidOperationException(
                "AddAuditingBehavior() requires SharedKernel.Application.Behaviors.Auditing.IAuditTrailWriter " +
                "to be registered in the service collection. Register an implementation before calling Build().");

        // Fixed canonical unary order — never configurable:
        // Logging -> Metrics -> Tracing -> Validation -> Authorization -> DualApproval -> Caching ->
        // Resilience -> Idempotency -> Auditing -> Transaction -> CacheInvalidation
        //
        // NOTE ON PHYSICAL REGISTRATION ORDER vs. the CANONICAL STEP ORDER ABOVE: MediatR wraps
        // IPipelineBehavior<,> instances so that the FIRST-registered behavior is OUTERMOST (its
        // post-`next()` code runs LAST, after every later-registered/more-inner behavior's
        // post-`next()` code has already run). Auditing's write must observably complete BEFORE
        // Transaction's own commit executes ("just inside Transaction" — see AuditingBehavior's
        // remarks), which requires AuditingBehavior to be registered AFTER (closer to the handler
        // than) TransactionBehavior below, even though Auditing is step 10 and Transaction is step
        // 11 in the canonical numbering above. This is the same inverted-registration-order
        // technique already applied for CacheInvalidationBehavior's post-commit-only positioning,
        // used here in the opposite temporal direction. Verified via a real, empirical pipeline
        // dispatch (state-map.md T-77) — never assume registration-list order equals execution
        // order for a post-`next()` side effect.
        if (_logging)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        if (_metrics)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(MetricsBehavior<,>));

        if (_tracing)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>));

        if (_validation)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        if (_authorization)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));

        if (_dualApproval)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DualApprovalBehavior<,>));

        if (_caching)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));

        if (_resilience)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ResilienceBehavior<,>));

        if (_idempotency)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotentCommandBehavior<,>));

        if (_transaction)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // Registered AFTER TransactionBehavior (physically inner to it) so RecordAsync fires
        // before SaveChangesAsync — see the "NOTE ON PHYSICAL REGISTRATION ORDER" comment above.
        if (_auditing)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditingBehavior<,>));

        if (_cacheInvalidation)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));

        // Fire-and-forget infrastructure (opt-in — registered as a unit).
        if (_fireAndForget)
        {
            _services.AddOptions<FireAndForgetOptions>();
            if (_fireAndForgetConfigure is not null)
                _services.Configure(_fireAndForgetConfigure);

            // The bounded channel is the singleton shared between the dispatcher and the consumer.
            _services.AddSingleton(static sp =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FireAndForgetOptions>>().Value;
                return Channel.CreateBounded<IFireAndForgetCommand>(
                    new BoundedChannelOptions(opts.Capacity)
                    {
                        FullMode = opts.RejectionPolicy == FireAndForgetRejectionPolicy.Block
                            ? BoundedChannelFullMode.Wait
                            : BoundedChannelFullMode.DropOldest, // DropAndLog uses TryWrite, so this fallback is never hit
                        SingleReader = true
                    });
            });

            _services.AddSingleton<ChannelWriter<IFireAndForgetCommand>>(
                static sp => sp.GetRequiredService<Channel<IFireAndForgetCommand>>().Writer);

            _services.AddSingleton<ChannelReader<IFireAndForgetCommand>>(
                static sp => sp.GetRequiredService<Channel<IFireAndForgetCommand>>().Reader);

            _services.AddSingleton<IFireAndForgetDispatcher, ChannelFireAndForgetDispatcher>();
            _services.AddSingleton<IHostedService, FireAndForgetBackgroundConsumer>();

            // Guard behavior: intercepts ISender.Send attempts for IFireAndForgetCommand types.
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FireAndForgetGuardBehavior<,>));
        }

        // Streaming behaviors — canonical order: Logging -> Metrics -> Tracing -> Validation -> Authorization.
        if (_streaming)
        {
            _services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamLoggingBehavior<,>));
            _services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamMetricsBehavior<,>));
            _services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamTracingBehavior<,>));
            _services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamValidationBehavior<,>));
            _services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamAuthorizationBehavior<,>));
        }

        return _services;
    }

    private bool IsRegistered<TService>()
        => _services.Any(descriptor => descriptor.ServiceType == typeof(TService));
}
