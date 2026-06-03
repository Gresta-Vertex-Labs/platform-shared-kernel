using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Domain;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions.Abstractions;
#pragma warning disable IDE0130 // Namespace does not match folder structure

namespace SharedKernel.Persistence.EfCore.Extensions;

/// <summary>
/// Entry-point DI extension for wiring the SharedKernel EF Core persistence layer.
/// </summary>
public static class EfCorePersistenceExtensions
{
    /// <summary>
    /// Registers the SharedKernel EF Core persistence services and returns a
    /// <see cref="EfCorePersistenceBuilder{TContext}"/> for fluent configuration.
    /// </summary>
    /// <typeparam name="TContext">
    /// The concrete <see cref="SharedKernelDbContext"/> subclass for this service.
    /// </typeparam>
    /// <param name="services">The service collection to register services into.</param>
    /// <param name="configureDb">
    /// Action that configures the <see cref="DbContextOptionsBuilder"/> (e.g., sets the provider
    /// and connection string). Interceptors are registered by
    /// <see cref="EfCorePersistenceBuilder{TContext}.Build"/> — do not add them here.
    /// </param>
    /// <returns>A fluent builder for optional multi-tenancy configuration.</returns>
    public static EfCorePersistenceBuilder<TContext> AddSharedKernelEfCore<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
        where TContext : SharedKernelDbContext
    {
        return new EfCorePersistenceBuilder<TContext>(services, configureDb);
    }
}

/// <summary>
/// Fluent builder for SharedKernel EF Core persistence DI registration.
/// </summary>
/// <typeparam name="TContext">
/// The concrete <see cref="SharedKernelDbContext"/> subclass for this service.
/// </typeparam>
/// <remarks>
/// <para>
/// Typical single-tenant usage:
/// <code>
/// services.AddSharedKernelEfCore&lt;OrderDbContext&gt;(options =>
///     options.UseNpgsql(connectionString))
///     .Build();
/// </code>
/// </para>
/// <para>
/// Multi-tenant usage:
/// <code>
/// services.AddSharedKernelEfCore&lt;OrderDbContext&gt;(options =>
///     options.UseNpgsql(connectionString))
///     .WithMultiTenancy()
///     .Build();
/// </code>
/// </para>
/// <para>
/// With explicit transaction support:
/// <code>
/// services.AddSharedKernelEfCore&lt;OrderDbContext&gt;(options =>
///     options.UseNpgsql(connectionString))
///     .WithTransactionalUnitOfWork()
///     .Build();
/// </code>
/// </para>
/// </remarks>
public sealed class EfCorePersistenceBuilder<TContext>
    where TContext : SharedKernelDbContext
{
    private readonly IServiceCollection _services;
    private readonly Action<DbContextOptionsBuilder> _configureDb;
    private bool _multiTenancyEnabled;
    private bool _transactionalUnitOfWorkEnabled;

    internal EfCorePersistenceBuilder(
        IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
    {
        _services = services;
        _configureDb = configureDb;
    }

    /// <summary>
    /// Opts in to multi-tenancy support.
    /// Registers a no-op <see cref="ITenantProvider"/> placeholder (<see cref="NoOpTenantProvider"/>)
    /// that returns <see cref="Guid.Empty"/> until overridden by the consuming service.
    /// At <see cref="Build"/> time, asserts that <typeparamref name="TContext"/> extends
    /// <see cref="TenantedDbContext"/>; throws <see cref="InvalidOperationException"/> with an
    /// actionable message if the assertion fails.
    /// </summary>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithMultiTenancy()
    {
        _multiTenancyEnabled = true;

        _services.AddScoped<ITenantProvider, NoOpTenantProvider>();

        return this;
    }

    /// <summary>
    /// Opts in to explicit transaction support by registering
    /// <see cref="ITransactionalUnitOfWork"/> → <see cref="EfTransactionalUnitOfWork"/> (scoped).
    /// </summary>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <para>
    /// Optional — call only for services that require explicit transaction boundaries (e.g., saga
    /// compensation, two-phase read-then-write operations). Services that do not need explicit
    /// transactions can omit this call and use <see cref="IUnitOfWork"/> directly.
    /// </para>
    /// <para>
    /// When called, both <c>IUnitOfWork</c> and <c>ITransactionalUnitOfWork</c> resolve the same
    /// scoped <see cref="EfTransactionalUnitOfWork"/> instance.
    /// </para>
    /// <para>
    /// <strong>Hard violation:</strong> Application-layer code must inject
    /// <c>ITransactionalUnitOfWork</c> — never <c>IDbContextTransaction</c> directly.
    /// </para>
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> WithTransactionalUnitOfWork()
    {
        _transactionalUnitOfWorkEnabled = true;
        return this;
    }

    /// <summary>
    /// Finalises the DI registration:
    /// <list type="bullet">
    ///   <item><description>Registers <typeparamref name="TContext"/> as <see cref="DbContext"/> (scoped).</description></item>
    ///   <item><description>Registers <see cref="IUnitOfWork"/> → <see cref="EfUnitOfWork"/> (scoped), or <see cref="EfTransactionalUnitOfWork"/> when <see cref="WithTransactionalUnitOfWork"/> was called.</description></item>
    ///   <item><description>Registers <see cref="ITransactionalUnitOfWork"/> → <see cref="EfTransactionalUnitOfWork"/> (scoped) when <see cref="WithTransactionalUnitOfWork"/> was called.</description></item>
    ///   <item><description>Registers <see cref="ISpecificationEvaluator{T}"/> → <see cref="SpecificationEvaluator{T}"/> (singleton — stateless).</description></item>
    ///   <item><description>Registers <see cref="AuditInterceptor"/>, <see cref="SoftDeleteInterceptor"/>, <see cref="ConcurrencyInterceptor"/> (scoped).</description></item>
    ///   <item><description>Registers a no-op <see cref="IUserContext"/> placeholder (scoped) if none is already registered.</description></item>
    /// </list>
    /// </summary>
    /// <returns>The <see cref="IServiceCollection"/> for further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup when <see cref="WithMultiTenancy"/> was called but
    /// <typeparamref name="TContext"/> does not extend <see cref="TenantedDbContext"/>.
    /// Fix: change your DbContext to extend <c>TenantedDbContext</c> instead of
    /// <c>SharedKernelDbContext</c>, or remove the <c>.WithMultiTenancy()</c> call.
    /// </exception>
    public IServiceCollection Build()
    {
        if (_multiTenancyEnabled && !typeof(TenantedDbContext).IsAssignableFrom(typeof(TContext)))
        {
            throw new InvalidOperationException(
                $"Multi-tenancy was enabled via '.WithMultiTenancy()' but the context type " +
                $"'{typeof(TContext).FullName}' does not extend '{typeof(TenantedDbContext).FullName}'. " +
                $"Either change '{typeof(TContext).Name}' to extend 'TenantedDbContext', " +
                $"or remove the '.WithMultiTenancy()' call from the DI registration.");
        }

        // Register interceptors as scoped so they receive per-request IUserContext / IClock.
        _services.AddScoped<AuditInterceptor>();
        _services.AddScoped<SoftDeleteInterceptor>();
        _services.AddScoped<ConcurrencyInterceptor>();

        // Register DbContext using the caller-supplied options action.
        // Interceptors are wired via SharedKernelDbContext.OnConfiguring.
        _services.AddDbContext<TContext>(_configureDb);

        // Register TContext also as the base SharedKernelDbContext so EfUnitOfWork resolves it.
        _services.AddScoped<SharedKernelDbContext>(sp => sp.GetRequiredService<TContext>());

        if (_transactionalUnitOfWorkEnabled)
        {
            // When transactional UoW is enabled, EfTransactionalUnitOfWork serves as both
            // IUnitOfWork and ITransactionalUnitOfWork — same scoped instance.
            _services.AddScoped<EfTransactionalUnitOfWork>();
            _services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EfTransactionalUnitOfWork>());
            _services.AddScoped<ITransactionalUnitOfWork>(sp => sp.GetRequiredService<EfTransactionalUnitOfWork>());
        }
        else
        {
            // Standard non-transactional path.
            _services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        }

        // ISpecificationEvaluator<T> — singleton because SpecificationEvaluator<T> is stateless.
        _services.AddSingleton(typeof(ISpecificationEvaluator<>), typeof(SpecificationEvaluator<>));

        // No-op IUserContext placeholder — registered only when no other IUserContext is present.
        if (!_services.Any(sd => sd.ServiceType == typeof(IUserContext)))
        {
            _services.AddScoped<IUserContext, NoOpUserContext>();
        }

        // IDomainEventDispatcher is optional — consuming services opt in by registering it.
        // EfUnitOfWork resolves it as a nullable IDomainEventDispatcher? via DI.

        // IClock — registered as singleton only when not already present.
        if (!_services.Any(sd => sd.ServiceType == typeof(IClock)))
        {
            _services.AddSingleton<IClock, SystemClock>();
        }

        return _services;
    }
}
