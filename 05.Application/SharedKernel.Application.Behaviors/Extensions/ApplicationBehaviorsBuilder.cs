using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Polly.Registry;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Caching;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Application.Behaviors.Metrics;
using SharedKernel.Application.Behaviors.Resilience;
using SharedKernel.Application.Behaviors.Tracing;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Behaviors.Validation;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Extensions;

/// <summary>
/// Builds the opt-in MediatR pipeline behavior registration for <c>SharedKernel.Application.Behaviors</c>.
/// </summary>
/// <remarks>
/// Use <c>.AddXBehavior()</c> methods to opt in to individual behaviors, then call
/// <see cref="Build"/> to register them. Registration order is always the fixed canonical
/// ten-named-slot order (Logging → Metrics → Tracing → Validation → Authorization → Caching →
/// Resilience → Idempotency → Transaction → CacheInvalidation) regardless of the order in which
/// <c>.AddXBehavior()</c> methods were called.
/// </remarks>
public sealed class ApplicationBehaviorsBuilder
{
    private readonly IServiceCollection _services;
    private bool _validation;
    private bool _logging;
    private bool _metrics;
    private bool _tracing;
    private bool _authorization;
    private bool _caching;
    private bool _resilience;
    private bool _idempotency;
    private bool _transaction;
    private bool _cacheInvalidation;

    internal ApplicationBehaviorsBuilder(IServiceCollection services)
    {
        _services = services;
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
    /// Registers the opted-into behaviors, always in the fixed canonical order, regardless of
    /// <c>.AddXBehavior()</c> call order.
    /// </summary>
    /// <returns>The underlying <see cref="IServiceCollection"/>, for further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="AddTransactionBehavior"/> was opted into without
    /// <see cref="IUnitOfWork"/> registered, when <see cref="AddCachingBehavior"/> or
    /// <see cref="AddCacheInvalidationBehavior"/> was opted into without
    /// <see cref="ICacheService"/> registered, when <see cref="AddAuthorizationBehavior"/> was
    /// opted into without <see cref="IAuthorizationContext"/> registered, when
    /// <see cref="AddIdempotencyBehavior"/> was opted into without <see cref="IIdempotencyKeyStore"/>
    /// registered, or when <see cref="AddResilienceBehavior"/> was opted into without
    /// <see cref="ResiliencePipelineProvider{TKey}"/> of <see cref="string"/> registered.
    /// </exception>
    /// <remarks>
    /// Does not call <c>services.AddMediatR(...)</c> — the consuming service already registers
    /// MediatR; this builder only appends behaviors via
    /// <c>services.AddTransient(typeof(IPipelineBehavior&lt;,&gt;), ...)</c>.
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

        if (_authorization && !IsRegistered<IAuthorizationContext>())
            throw new InvalidOperationException(
                "AddAuthorizationBehavior() requires SharedKernel.Application.Behaviors.Authorization.IAuthorizationContext " +
                "to be registered in the service collection. Register an implementation before calling Build().");

        if (_idempotency && !IsRegistered<IIdempotencyKeyStore>())
            throw new InvalidOperationException(
                "AddIdempotencyBehavior() requires SharedKernel.Application.Behaviors.Idempotency.IIdempotencyKeyStore " +
                "to be registered in the service collection. Register an implementation before calling Build().");

        if (_resilience && !IsRegistered<ResiliencePipelineProvider<string>>())
            throw new InvalidOperationException(
                "AddResilienceBehavior() requires Polly.Registry.ResiliencePipelineProvider<string> " +
                "to be registered in the service collection. Register one before calling Build().");

        // Fixed canonical order — never configurable:
        // Logging -> Metrics -> Tracing -> Validation -> Authorization -> Caching -> Resilience ->
        // Idempotency -> Transaction -> CacheInvalidation
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

        if (_caching)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));

        if (_resilience)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ResilienceBehavior<,>));

        if (_idempotency)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotentCommandBehavior<,>));

        if (_transaction)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        if (_cacheInvalidation)
            _services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));

        return _services;
    }

    private bool IsRegistered<TService>()
        => _services.Any(descriptor => descriptor.ServiceType == typeof(TService));
}
