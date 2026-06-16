using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Domain;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Seeding;
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
/// With field-level encryption:
/// <code>
/// services.AddSharedKernelEfCore&lt;OrderDbContext&gt;(options =>
///     options.UseNpgsql(connectionString))
///     .WithEncryption(enc => { enc.Enabled = true; enc.CurrentVersion = "v1"; enc.Keys["v1"] = "..."; })
///     .WithServiceName("order-service")
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
    private bool _registerFactory;
    private bool _registerEncryption;
    private bool _migrationsOnStartup;
    private IModel? _compiledModel;
    private readonly List<Type> _additionalInterceptorTypes = [];
    private readonly List<Func<IServiceProvider, TContext, CancellationToken, Task>> _seedSteps = [];

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
    /// Opts in to <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/> registration
    /// for background services and hosted workers.
    /// </summary>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithDbContextFactory()
    {
        _registerFactory = true;
        return this;
    }

    /// <summary>
    /// Registers an additional service-specific <see cref="ISaveChangesInterceptor"/> that fires
    /// after the platform three (Audit, SoftDelete, Concurrency).
    /// </summary>
    /// <typeparam name="TInterceptor">
    /// The concrete interceptor type. Must be a class implementing <see cref="ISaveChangesInterceptor"/>.
    /// </typeparam>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> AddInterceptor<TInterceptor>()
        where TInterceptor : class, ISaveChangesInterceptor
    {
        _additionalInterceptorTypes.Add(typeof(TInterceptor));
        return this;
    }

    /// <summary>
    /// Configures the <see cref="DbContext"/> to use a pre-built compiled model for AOT and
    /// cold-start performance improvements.
    /// </summary>
    /// <param name="compiledModel">
    /// The compiled model produced via <c>dotnet ef dbcontext optimize</c>.
    /// </param>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithCompiledModel(IModel compiledModel)
    {
        _compiledModel = compiledModel;
        return this;
    }

    /// <summary>
    /// Opts in to explicit transaction support by registering
    /// <see cref="ITransactionalUnitOfWork"/> → <see cref="EfTransactionalUnitOfWork"/> (scoped).
    /// </summary>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithTransactionalUnitOfWork()
    {
        _transactionalUnitOfWorkEnabled = true;
        return this;
    }

    /// <summary>
    /// Opts in to field-level AES-256-GCM transparent encryption.
    /// </summary>
    /// <param name="configure">
    /// Optional action to configure <see cref="EncryptionOptions"/> (keys, current version, enabled flag).
    /// Pass <see langword="null"/> to use defaults (disabled).
    /// </param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registers <see cref="EncryptionOptions"/> via the Options system and registers eager startup
    /// validation. When <see cref="Build"/> is called, also registers
    /// <see cref="IEncryptionRotationJob"/> → <see cref="EncryptionRotationService{TContext}"/> (scoped).
    /// </para>
    /// <para>
    /// Omitting this call leaves all existing behavior unchanged — encryption is disabled by default.
    /// </para>
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> WithEncryption(Action<EncryptionOptions>? configure = null)
    {
        _registerEncryption = true;

        var optionsBuilder = _services.AddOptions<EncryptionOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        _services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>();
        _services.AddOptions<EncryptionOptions>().ValidateOnStart();

        // Singleton, AsyncLocal-backed rotation-target-version accessor — resolved once by
        // EncryptionModelConvention during model finalization (EF Core caches the compiled model,
        // including converters, across DbContext instances of the same context type, so a scoped
        // registration would only be observed by the very first context's converters). Mutated by
        // EncryptionRotationService<TContext> for the duration of each batch's SaveChangesAsync.
        _services.AddSingleton<IEncryptionVersionOverride, EncryptionVersionOverride>();

        return this;
    }

    /// <summary>
    /// Configures the unauthenticated audit fallback string written to <c>CreatedBy</c>,
    /// <c>ModifiedBy</c>, and <c>DeletedBy</c> columns when no authenticated user is present.
    /// </summary>
    /// <param name="serviceName">
    /// The service identity string. Must be non-null, non-empty, and ≤ 256 characters.
    /// </param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// Omitting this call keeps the default <c>"system"</c> fallback.
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> WithServiceName(string serviceName)
    {
        _services.AddOptions<PersistenceServiceOptions>()
            .Configure(o => o.ServiceName = serviceName);

        _services.AddSingleton<IValidateOptions<PersistenceServiceOptions>, PersistenceServiceOptionsValidator>();
        _services.AddOptions<PersistenceServiceOptions>().ValidateOnStart();

        return this;
    }

    /// <summary>
    /// Opts in to applying pending EF Core migrations during host startup via
    /// <c>Database.MigrateAsync</c>.
    /// </summary>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// Does not by itself register a hosted service — <see cref="Build"/> registers
    /// <c>MigrationAndSeedHostedService&lt;TContext&gt;</c> only when this method was called, or at
    /// least one seeder was registered via <see cref="AddSeeder{TSeeder}"/>. Compatible with
    /// <see cref="WithCompiledModel"/>: <c>MigrateAsync</c> still applies pending SQL migrations
    /// independently of the runtime model.
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> WithMigrationsOnStartup()
    {
        _migrationsOnStartup = true;
        return this;
    }

    /// <summary>
    /// Registers a startup data seeder for <typeparamref name="TContext"/>.
    /// </summary>
    /// <typeparam name="TSeeder">
    /// The seeder type. Must implement <see cref="IDataSeeder{TContext}"/> for the same
    /// <typeparamref name="TContext"/> as this builder.
    /// </typeparam>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <typeparamref name="TSeeder"/> is registered as scoped. Multiple calls accumulate into an
    /// ordered list, executed in call order during startup, each in its own DI scope with its own
    /// <typeparamref name="TContext"/> instance resolved via
    /// <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>. Seeders are
    /// idempotent by contract — see <see cref="IDataSeeder{TContext}"/>.
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> AddSeeder<TSeeder>()
        where TSeeder : class, IDataSeeder<TContext>
    {
        _services.AddScoped<TSeeder>();
        _seedSteps.Add(static (sp, context, ct) =>
            sp.GetRequiredService<TSeeder>().SeedAsync(context, ct));
        return this;
    }

    /// <summary>
    /// Finalises the DI registration.
    /// </summary>
    /// <returns>The <see cref="IServiceCollection"/> for further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup when <see cref="WithMultiTenancy"/> was called but
    /// <typeparamref name="TContext"/> does not extend <see cref="TenantedDbContext"/>.
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

        // Register PersistenceServiceOptions default if not already configured by WithServiceName().
        // This ensures AuditInterceptor and SoftDeleteInterceptor can always resolve it.
        if (!_services.Any(sd => sd.ServiceType == typeof(IOptions<PersistenceServiceOptions>))
            && !_services.Any(sd => sd.ServiceType == typeof(IConfigureOptions<PersistenceServiceOptions>)))
        {
            _services.AddOptions<PersistenceServiceOptions>();
        }

        // Register interceptors as scoped so they receive per-request IUserContext / IClock.
        _services.AddScoped<AuditInterceptor>();
        _services.AddScoped<SoftDeleteInterceptor>();
        _services.AddScoped<ConcurrencyInterceptor>();

        // Register any additional consumer-supplied interceptors as scoped.
        foreach (var interceptorType in _additionalInterceptorTypes)
        {
            _services.AddScoped(interceptorType);
            _services.AddScoped(typeof(ISaveChangesInterceptor), sp =>
                sp.GetRequiredService(interceptorType) as ISaveChangesInterceptor
                    ?? throw new InvalidOperationException(
                        $"Type '{interceptorType.Name}' does not implement ISaveChangesInterceptor."));
        }

        // Build effective configureDb action — wrap with compiled model if supplied.
        Action<DbContextOptionsBuilder> effectiveConfigureDb = _compiledModel is not null
            ? options =>
            {
                _configureDb(options);
                options.UseModel(_compiledModel);
            }
            : _configureDb;

        // Register DbContext using the caller-supplied options action.
        // Interceptors are wired via SharedKernelDbContext.OnConfiguring.
        _services.AddDbContext<TContext>(effectiveConfigureDb);

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

        // Register IDbContextFactory<TContext> when WithDbContextFactory() was called.
        if (_registerFactory)
        {
            _services.AddDbContextFactory<TContext>(effectiveConfigureDb);
        }

        // When .WithEncryption() was called, register the IOptionsMonitor<EncryptionOptions> so
        // SharedKernelDbContext can resolve it for EncryptionModelConvention.
        // IEncryptionRotationJob registration is the consumer's responsibility — they must provide
        // a concrete EncryptionRotationService<TContext> subclass via services.AddScoped<IEncryptionRotationJob, MyRotationService>().
        // We intentionally do not register the abstract base class here.
        if (_registerEncryption)
        {
            // Ensure IOptionsMonitor<EncryptionOptions> is available in the DI container.
            // AddOptions() is idempotent and does not duplicate registrations.
            _services.AddOptions<EncryptionOptions>();

            // EncryptedEntityBatchProcessorRegistry<TContext> and EncryptionRotationService<TContext>
            // both require IDbContextFactory<TContext> — register it if WithDbContextFactory() was
            // not already called.
            if (!_registerFactory)
            {
                _services.AddDbContextFactory<TContext>(effectiveConfigureDb);
                _registerFactory = true;
            }

            // Singleton registry of reflection-free batch processors, one per encrypted entity
            // type, populated lazily on first use from the model.
            _services.AddSingleton<EncryptedEntityBatchProcessorRegistry<TContext>>();
        }

        // MigrationAndSeedHostedService<TContext> is registered only when migrations-on-startup
        // was requested, or at least one seeder was registered — fully opt-in, zero overhead
        // otherwise. Both paths require IDbContextFactory<TContext>; register it here if not
        // already registered by .WithDbContextFactory() or .WithEncryption().
        if (_migrationsOnStartup || _seedSteps.Count > 0)
        {
            if (!_registerFactory)
            {
                _services.AddDbContextFactory<TContext>(effectiveConfigureDb);
                _registerFactory = true;
            }

            var migrationsOnStartup = _migrationsOnStartup;
            var seedSteps = _seedSteps.ToArray();

            _services.AddHostedService<MigrationAndSeedHostedService<TContext>>(sp =>
                new MigrationAndSeedHostedService<TContext>(sp, migrationsOnStartup, seedSteps));
        }

        return _services;
    }
}
