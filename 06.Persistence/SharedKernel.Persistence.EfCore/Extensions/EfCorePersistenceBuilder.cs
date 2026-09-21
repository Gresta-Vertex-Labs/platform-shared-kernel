using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Application.Context;
using SharedKernel.Application.Transactions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Exceptions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Identifiers;

namespace SharedKernel.Persistence.EfCore.Extensions;

/// <summary>
/// Options for one context registered with <c>AddSharedKernelPostgres&lt;TContext&gt;(name, p =&gt; ...)</c>.
/// Everything is optional; the defaults are the platform's production settings.
/// </summary>
/// <typeparam name="TContext">The registered context.</typeparam>
/// <remarks>
/// Capability packages add their own methods to this builder (<c>UseAuditTrail</c>, <c>UseFieldEncryption</c>,
/// <c>UseRowLevelSecurity</c>); <c>UseMultiTenancy</c> is an extension method so only a
/// <see cref="TenantedDbContext"/> can call it.
/// </remarks>
public sealed class EfCorePersistenceBuilder<TContext>
    where TContext : SharedKernelDbContext
{
    private readonly List<Type> _interceptorTypes = [];
    private readonly List<(string SeederTypeName, Func<IServiceProvider, TContext, CancellationToken, Task> Invoke)> _seedSteps = [];
    private readonly List<Action<PostgreSqlProviderOptions>> _providerConfigurations = [];
    private readonly List<Action<IServiceProvider, DbContextOptionsBuilder>> _dbContextConfigurations = [];
    private Action<IServiceProvider, NpgsqlDataSourceBuilder>? _configureDataSource;
    private NpgsqlDataSource? _dataSource;
    private Action<IServiceProvider, DbContextOptionsBuilder>? _providerOverride;
    private IModel? _compiledModel;
    private TimeSpan? _commandTimeout;
    private bool _pooling;
    private int _poolSize = 1024;
    private string? _serviceName;
    private bool _uuidV7Keys;
    private bool _migrateOnStartup;
    private TimeSpan _startupLockTimeout = TimeSpan.FromMinutes(2);

    internal EfCorePersistenceBuilder(IServiceCollection services, IConfiguration? configuration, string connectionName)
    {
        Services = services;
        Configuration = configuration;
        ConnectionName = connectionName;
    }

    /// <summary>The service collection the registration writes to (for capability packages' extension methods).</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection Services { get; }

    /// <summary>The connection name: <c>ConnectionStrings:{name}</c> and <c>SharedKernel:Persistence:{name}</c>.</summary>
    public string ConnectionName { get; }

    /// <summary>Whether multi-tenancy was requested with <c>UseMultiTenancy()</c>.</summary>
    internal bool MultiTenancyRequested { get; set; }

    internal IConfiguration? Configuration { get; }

    /// <summary>Changes the PostgreSQL provider settings (retry — on by default —, pgvector).</summary>
    /// <param name="configure">Mutates the provider options.</param>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> ConfigureProvider(Action<PostgreSqlProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _providerConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Extends the shared <see cref="NpgsqlDataSourceBuilder"/> (<c>MapEnum</c>, a periodic password provider,
    /// Npgsql's OpenTelemetry hooks, ...). Applies only when this registration creates the data source.
    /// </summary>
    /// <param name="configure">Receives the root service provider and the data-source builder.</param>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> ConfigureDataSource(Action<IServiceProvider, NpgsqlDataSourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureDataSource += configure;
        return this;
    }

    /// <summary>
    /// Uses an existing data source instead of the one built from configuration. The caller owns and disposes it;
    /// no connection factory, migration lock or tenant session binder is registered for it.
    /// </summary>
    /// <param name="dataSource">One data source per database, kept for the process lifetime.</param>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> UseDataSource(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
        return this;
    }

    /// <summary>Adds EF Core options (warnings, logging, ...), applied after the platform's PostgreSQL setup.</summary>
    /// <param name="configure">Receives the root service provider and the options builder.</param>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> ConfigureDbContext(Action<IServiceProvider, DbContextOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _dbContextConfigurations.Add(configure);
        return this;
    }

    /// <summary>Uses a compiled model (<c>dotnet ef dbcontext optimize</c>) for faster startup.</summary>
    /// <param name="compiledModel">The compiled model.</param>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> UseCompiledModel(IModel compiledModel)
    {
        ArgumentNullException.ThrowIfNull(compiledModel);
        _compiledModel = compiledModel;
        return this;
    }

    /// <summary>Sets the command timeout of every command the context issues.</summary>
    /// <param name="timeout">A positive timeout.</param>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> UseCommandTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _commandTimeout = timeout;
        return this;
    }

    /// <summary>Pools context instances (<c>AddPooledDbContextFactory</c>) for high request throughput.</summary>
    /// <param name="poolSize">Maximum number of pooled instances.</param>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> UseDbContextPooling(int poolSize = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(poolSize, 1);
        _pooling = true;
        _poolSize = poolSize;
        return this;
    }

    /// <summary>
    /// Sets the actor written to <c>CreatedBy</c>/<c>ModifiedBy</c>/<c>DeletedBy</c> when the caller has no user
    /// id (background work). Default: <c>SharedKernel:Persistence:ServiceName</c>, else <c>"system"</c>.
    /// </summary>
    /// <param name="serviceName">1–256 characters.</param>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> UseServiceName(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        _serviceName = serviceName;
        return this;
    }

    /// <summary>
    /// Generates unset <see cref="Guid"/> / <c>StronglyTypedId&lt;Guid&gt;</c> primary keys with the registered
    /// <see cref="IIdGenerator"/> (UUID v7 unless another one is registered) — index-friendly, time-ordered keys.
    /// </summary>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> UseUuidV7Keys()
    {
        _uuidV7Keys = true;
        return this;
    }

    /// <summary>Applies pending migrations at startup, serialized across replicas by the migration lock.</summary>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> MigrateOnStartup()
    {
        _migrateOnStartup = true;
        return this;
    }

    /// <summary>How long startup migration/seeding waits for the cross-replica lock. Default 2 minutes.</summary>
    /// <param name="timeout">A positive timeout.</param>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> UseStartupLockTimeout(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _startupLockTimeout = timeout;
        return this;
    }

    /// <summary>Runs a data seeder at startup, after migrations, under the migration lock.</summary>
    /// <typeparam name="TSeeder">The seeder; resolved from a fresh scope.</typeparam>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> AddSeeder<TSeeder>()
        where TSeeder : class, IDataSeeder<TContext>
    {
        Services.TryAddScoped<TSeeder>();
        _seedSteps.Add((typeof(TSeeder).Name, static (sp, context, ct) => sp.GetRequiredService<TSeeder>().SeedAsync(context, ct)));
        return this;
    }

    /// <summary>
    /// Adds an interceptor after the platform's own. Registered as a <b>singleton</b> and shared by every
    /// context: read per-request state from the event data's context, never from constructor dependencies.
    /// </summary>
    /// <typeparam name="TInterceptor">The interceptor type.</typeparam>
    /// <returns>This builder.</returns>
    public EfCorePersistenceBuilder<TContext> AddInterceptor<TInterceptor>()
        where TInterceptor : class, IInterceptor
    {
        if (!_interceptorTypes.Contains(typeof(TInterceptor)))
        {
            _interceptorTypes.Add(typeof(TInterceptor));
            Services.TryAddSingleton<TInterceptor>();
            Services.AddSingleton<IInterceptor>(sp => sp.GetRequiredService<TInterceptor>());
            if (typeof(ISaveChangesInterceptor).IsAssignableFrom(typeof(TInterceptor)))
                Services.AddSingleton(typeof(ISaveChangesInterceptor), sp => sp.GetRequiredService<TInterceptor>());
        }

        return this;
    }

    /// <summary>Test seam: replaces the PostgreSQL provider setup (e.g. with SQLite) for unit tests of this package.</summary>
    internal EfCorePersistenceBuilder<TContext> UseProviderForTesting(Action<IServiceProvider, DbContextOptionsBuilder> configure)
    {
        _providerOverride = configure;
        return this;
    }

    /// <summary>Performs the registration. Called once by <c>AddSharedKernelPostgres</c> after the configure callback.</summary>
    internal void Register()
    {
        var services = Services;

        if (services.Any(sd => sd.ServiceType == typeof(RegistrationMarker)))
        {
            throw new InvalidOperationException(
                $"'{typeof(TContext).Name}' is already registered. Call AddSharedKernelPostgres<{typeof(TContext).Name}> once.");
        }

        services.AddSingleton<RegistrationMarker>();

        if (MultiTenancyRequested && !typeof(TenantedDbContext).IsAssignableFrom(typeof(TContext)))
        {
            throw new InvalidOperationException(
                $"UseMultiTenancy() requires '{typeof(TContext).Name}' to derive from TenantedDbContext.");
        }

        RegisterShared(services);
        var provider = BuildProviderSetup(services);

        void ConfigureOptions(IServiceProvider sp, DbContextOptionsBuilder options)
        {
            provider(sp, options);

            foreach (var configure in _dbContextConfigurations)
                configure(sp, options);

            if (_compiledModel is not null)
                options.UseModel(_compiledModel);

            if (_commandTimeout is { } timeout
                && options.Options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault() is { } relational)
            {
                ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
                    relational.WithCommandTimeout((int)Math.Ceiling(timeout.TotalSeconds)));
            }
        }

        // The inner (pooled or plain) factory is a singleton: every constructor dependency of a context is one.
        // It is registered under a private key and wrapped by the scoped, identity-attaching public factory.
        if (_pooling)
        {
            services.AddPooledDbContextFactory<TContext>((sp, options) =>
            {
                ConfigureOptions(sp, options);
                // A pooled context's options are frozen before OnConfiguring runs.
                sp.GetRequiredService<PersistenceContextDependencies>().ApplyTo(options);
            }, _poolSize);
        }
        else
        {
            services.AddDbContextFactory<TContext>(ConfigureOptions, ServiceLifetime.Singleton);
        }

        RekeyLastRegistration(services, typeof(IDbContextFactory<TContext>), InnerFactoryKey.Instance);

        services.AddScoped<IDbContextFactory<TContext>>(sp => new TenantAwareDbContextFactory<TContext>(
            sp.GetRequiredKeyedService<IDbContextFactory<TContext>>(InnerFactoryKey.Instance),
            sp.GetRequiredService<IRequestContext>(),
            sp.GetService<IDomainEventDispatcher>()));
        services.AddSingleton<ICallerDbContextFactory<TContext>>(sp => new CallerDbContextFactory<TContext>(
            sp.GetRequiredKeyedService<IDbContextFactory<TContext>>(InnerFactoryKey.Instance)));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext());

        // The first registered context is the unkeyed default; every context is also keyed by its type.
        services.TryAddScoped<SharedKernelDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddKeyedScoped<SharedKernelDbContext>(typeof(TContext), (sp, _) => sp.GetRequiredService<TContext>());

        services.AddScoped<EfUnitOfWork<TContext>>();
        services.AddScoped<IUnitOfWork<TContext>>(sp => sp.GetRequiredService<EfUnitOfWork<TContext>>());
        services.AddKeyedScoped<IUnitOfWork>(typeof(TContext), (sp, _) => sp.GetRequiredService<EfUnitOfWork<TContext>>());
        services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EfUnitOfWork<TContext>>());

        RepositoryRegistration.Register<TContext>(services);

        if (_migrateOnStartup || _seedSteps.Count > 0)
        {
            var startup = new MigrationAndSeedHostedService<TContext>.StartupOptions(_migrateOnStartup, _startupLockTimeout);
            var seedSteps = _seedSteps.ToArray();
            services.AddHostedService(sp => new MigrationAndSeedHostedService<TContext>(
                sp, startup, seedSteps, sp.GetService<ILogger<MigrationAndSeedHostedService<TContext>>>()));
        }

        // Startup validation: builds and validates the model, warns when domain events have no dispatcher.
        services.AddOptions<PersistenceStartupCheck<TContext>>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<PersistenceStartupCheck<TContext>>, PersistenceStartupValidator<TContext>>();
    }

    private void RegisterShared(IServiceCollection services)
    {
        // SQLSTATE classification first, once, so it is consulted before any consumer classifier.
        if (!services.Any(sd => sd.ServiceType == typeof(IDbUpdateExceptionClassifier)
                && sd.ImplementationType == typeof(PostgreSqlDbUpdateExceptionClassifier)))
        {
            services.Insert(0, ServiceDescriptor.Singleton<IDbUpdateExceptionClassifier, PostgreSqlDbUpdateExceptionClassifier>());
        }

        var serviceOptions = services.AddOptions<PersistenceServiceOptions>();
        if (!services.Any(sd => sd.ServiceType == typeof(PersistenceServiceOptionsValidator)))
        {
            services.AddSingleton<PersistenceServiceOptionsValidator>();
            services.AddSingleton<IValidateOptions<PersistenceServiceOptions>>(sp => sp.GetRequiredService<PersistenceServiceOptionsValidator>());
            serviceOptions.ValidateOnStart();
            if (Configuration is not null)
                serviceOptions.Bind(Configuration.GetSection(PersistenceServiceOptions.SectionName));
        }

        if (_serviceName is { } serviceName)
            serviceOptions.Configure(o => o.ServiceName = serviceName);

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSharedKernelCrossTenantScope();

        if (_uuidV7Keys)
        {
            services.TryAddSingleton<IIdGenerator, UuidV7IdGenerator>();
            services.TryAddSingleton<ClientKeyGenerationMarker>();
        }

        services.TryAddSingleton(sp => new PersistenceContextDependencies(
            sp.GetRequiredService<IClock>(),
            AnonymousRequestContext.Instance,
            sp.GetRequiredService<IOptions<PersistenceServiceOptions>>().Value.ServiceName,
            defaultDomainEventDispatcher: null,
            sp.GetServices<IInterceptor>(),
            sp.GetServices<IPersistenceModelConventionFactory>(),
            sp.GetServices<IPersistenceModelConfigurator>(),
            sp.GetServices<IPersistenceOptionsExtension>(),
            sp.GetServices<IDbUpdateExceptionClassifier>(),
            sp.GetService<ClientKeyGenerationMarker>() is null ? null : sp.GetRequiredService<IIdGenerator>(),
            sp.GetService<ILoggerFactory>()));

        services.TryAddScoped<AmbientDbTransactionAccessor>();
        services.TryAddScoped<IAmbientDbTransaction>(sp => sp.GetRequiredService<AmbientDbTransactionAccessor>());
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ISpecificationEvaluator<>), typeof(SpecificationEvaluator<>)));
    }

    // Returns the provider setup for the context's options: the test override, an explicit data source, or the
    // data source registered for this connection name.
    private Action<IServiceProvider, DbContextOptionsBuilder> BuildProviderSetup(IServiceCollection services)
    {
        if (_providerOverride is { } overrideSetup)
            return overrideSetup;

        void Configure(PostgreSqlProviderOptions o)
        {
            foreach (var configure in _providerConfigurations)
                configure(o);
        }

        if (_dataSource is { } dataSource)
            return (_, options) => options.UsePostgreSQL(dataSource, Configure);

        var dataSourceName = PostgresDataSources.Register(services, Configuration, ConnectionName, _configureDataSource);
        return (sp, options) => options.UsePostgreSQL(sp, dataSourceName, Configure);
    }

    // Moves the last registration of serviceType to a keyed slot, using only the public ServiceDescriptor shape.
    private static void RekeyLastRegistration(IServiceCollection services, Type serviceType, object key)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var original = services[i];
            if (original.ServiceType != serviceType || original.IsKeyedService)
                continue;

            services.RemoveAt(i);
            services.Add(original switch
            {
                { ImplementationType: { } type } => ServiceDescriptor.DescribeKeyed(serviceType, key, type, original.Lifetime),
                { ImplementationFactory: { } factory } => ServiceDescriptor.DescribeKeyed(serviceType, key, (sp, _) => factory(sp), original.Lifetime),
                { ImplementationInstance: { } instance } => ServiceDescriptor.DescribeKeyed(serviceType, key, (_, _) => instance, ServiceLifetime.Singleton),
                _ => throw new InvalidOperationException($"Unsupported registration shape for '{serviceType}'."),
            });
            return;
        }

        throw new InvalidOperationException($"No registration of '{serviceType}' to re-key (internal invariant).");
    }

    private sealed class InnerFactoryKey
    {
        public static readonly InnerFactoryKey Instance = new();
    }

    private sealed class RegistrationMarker;
}

/// <summary>Marks that client-side key generation (<c>UseUuidV7Keys()</c>) is on for the process.</summary>
internal sealed class ClientKeyGenerationMarker;
