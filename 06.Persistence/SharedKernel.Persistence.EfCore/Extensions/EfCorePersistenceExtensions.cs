using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.ReadReplica;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Primitives.Clocks;
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
        ArgumentNullException.ThrowIfNull(configureDb);
        return new EfCorePersistenceBuilder<TContext>(services, (_, options) => configureDb(options));
    }

    /// <summary>
    /// Registers the SharedKernel EF Core persistence services and returns a
    /// <see cref="EfCorePersistenceBuilder{TContext}"/> for fluent configuration, with the DI
    /// <see cref="IServiceProvider"/> available inside <paramref name="configureDb"/>.
    /// </summary>
    /// <typeparam name="TContext">
    /// The concrete <see cref="SharedKernelDbContext"/> subclass for this service.
    /// </typeparam>
    /// <param name="services">The service collection to register services into.</param>
    /// <param name="configureDb">
    /// Action that configures the <see cref="DbContextOptionsBuilder"/>, with access to the resolving
    /// <see cref="IServiceProvider"/>. This is the overload
    /// <c>SharedKernel.Persistence.PostgreSQL</c>'s data-source-resolving <c>UsePostgreSQL(DbContextOptionsBuilder,
    /// IServiceProvider,...)</c> overload is designed to be called from, so EF Core and Dapper share
    /// exactly one <c>NpgsqlDataSource</c> connection pool per database:
    /// <code>
    /// services.AddSharedKernelNpgsql(configuration);
    /// services.AddSharedKernelEfCore&lt;OrderDbContext&gt;((sp, options) =&gt;
    /// options.UsePostgreSQL(sp))
    ///     .Build();
    /// </code>
    /// </param>
    /// <returns>A fluent builder for optional multi-tenancy configuration.</returns>
    public static EfCorePersistenceBuilder<TContext> AddSharedKernelEfCore<TContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configureDb)
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
/// options.UseNpgsql(connectionString))
///     .Build();
/// </code>
/// </para>
/// <para>
/// Multi-tenant usage:
/// <code>
/// services.AddSharedKernelEfCore&lt;OrderDbContext&gt;(options =>
/// options.UseNpgsql(connectionString))
///     .WithMultiTenancy()
///     .Build();
/// </code>
/// </para>
/// <para>
/// <strong>Extensibility:</strong> field-level encryption (<c>SharedKernel.Persistence.EfCore.Encryption</c>)
/// and the audit trail (<c>SharedKernel.Persistence.EfCore.Auditing</c>) are no longer part of this
/// package — their <c>.WithEncryption()</c>/<c>.WithAuditTrail()</c> methods are extension methods on this builder shipped by those sibling
/// packages. They reach into this builder through <see cref="Services"/>, <see cref="AddInterceptor{TInterceptor}"/>,
/// <see cref="IsDbContextPoolingEnabled"/>, <see cref="RequireDbContextFactory"/>, and
/// <see cref="AddBuildAction"/> — the same public surface any future opt-in capability package uses.
/// </para>
/// </remarks>
public sealed class EfCorePersistenceBuilder<TContext>
    where TContext : SharedKernelDbContext
{
    private readonly IServiceCollection _services;
    private readonly Action<IServiceProvider, DbContextOptionsBuilder> _configureDb;
    private static readonly object InnerFactoryKey = new();

    private bool _multiTenancyEnabled;
    private bool _transactionalUnitOfWorkEnabled;
    private bool _migrationsOnStartup;
    private bool _dbContextPoolingEnabled;
    private int _poolSize = 1024;
    private IModel? _compiledModel;
    private TransientFaultRetryOptions? _transientFaultRetryOptions;
    private int? _commandTimeoutSeconds;
    private Action<DbContextOptionsBuilder>? _readReplicaConfigureDb;
    private bool _serviceNameValidationRegistered;
    private readonly List<Type> _additionalInterceptorTypes = [];
    private readonly List<Action> _buildActions = [];
    private readonly List<(string SeederTypeName, Func<IServiceProvider, TContext, CancellationToken, Task> Invoke)> _seedSteps = [];

    internal EfCorePersistenceBuilder(
        IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configureDb)
    {
        _services = services;
        _configureDb = configureDb;
    }

    /// <summary>
    /// Gets the underlying <see cref="IServiceCollection"/> this builder registers into.
    /// </summary>
    /// <remarks>
    /// The extensibility seam a sibling capability package's own <c>.WithX()</c> extension
    /// method uses to register its own services against this builder's DI container. Never mutate a
    /// service already registered by this builder's own methods — add to or replace via the normal
    /// <see cref="IServiceCollection"/> APIs (<c>TryAdd</c>, keyed registrations, etc.).
    /// </remarks>
    public IServiceCollection Services => _services;

    /// <summary>
    /// Gets whether <see cref="WithDbContextPooling"/> has been called on this builder.
    /// </summary>
    /// <remarks>
    /// Lets a capability extension whose own feature is unsafe to combine with pooling
    /// (today: field-level encryption's rotation-scoped key-version override) enforce that guard
    /// itself via <see cref="AddBuildAction"/>, without this core builder needing to know the
    /// capability exists.
    /// </remarks>
    public bool IsDbContextPoolingEnabled => _dbContextPoolingEnabled;

    /// <summary>
    /// No-op — kept for source compatibility. <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>
    /// is now unconditionally registered by <see cref="Build"/> (<c>TContext</c> direct injection
    /// itself rides on it, via <c>TenantAwareDbContextFactory{TContext}</c>), so no capability needs
    /// to request it separately any more.
    /// </summary>
    /// <remarks>
    /// Sibling capability packages (migrations/seeding, the encryption
    /// package's key-rotation registry) still call this to declare their dependency on
    /// <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/> existing — harmless to
    /// keep calling, and documents intent, even though it no longer changes registration behavior.
    /// </remarks>
    public void RequireDbContextFactory()
    {
        // Intentionally empty — see summary above.
    }

    /// <summary>
    /// Registers an action to run once, near the end of <see cref="Build"/>, after this builder's own
    /// core registrations (DbContext, unit of work, specification evaluator) are complete.
    /// </summary>
    /// <param name="action">
    /// The action to run. Typically a capability extension's own opt-in-flag validation guard (e.g.
    /// "this capability cannot combine with DbContext pooling") or a piece of registration that must
    /// see this builder's final, fully-configured state.
    /// </param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// Actions run in registration order. A guard that must throw reflects the state of
    /// <em>every</em> builder method call made before <see cref="Build"/> executes, regardless of the
    /// order those calls were made in relative to the method that registered the guard.
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> AddBuildAction(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _buildActions.Add(action);
        return this;
    }

    /// <summary>
    /// Opts in to multi-tenancy support. At <see cref="Build"/> time, asserts that
    /// <typeparamref name="TContext"/> extends <see cref="TenantedDbContext"/>; throws
    /// <see cref="InvalidOperationException"/> with an actionable message if the assertion fails.
    /// </summary>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registers the default <see cref="ICurrentTenantContext"/>
    /// (<see cref="MultiTenancy.NullCurrentTenantContext"/>, fail-closed — see its remarks) when
    /// nothing else has already registered one, and always registers
    /// <see cref="Interceptors.TenantWriteGuardInterceptor"/> as an additional interceptor (H-A1) — the
    /// write-side half of tenant isolation, the tenant global query filter being the read-side half.
    /// A consuming service bridges <see cref="ICurrentTenantContext"/> to its real tenant source at
    /// its own composition root — <c>13.ServiceDefaults/SharedKernel.ServiceDefaults.Persistence</c>
    /// ships that bridge for services already using <c>12.Security</c>.
    /// </para>
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> WithMultiTenancy()
    {
        _multiTenancyEnabled = true;
        AddInterceptor<TenantWriteGuardInterceptor>();
        return this;
    }

    /// <summary>
    /// Opts in to <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/> registration
    /// for background services and hosted workers.
    /// </summary>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithDbContextFactory()
    {
        RequireDbContextFactory();
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
        // Idempotent — calling this twice for the same TInterceptor (directly, or
        // indirectly via WithMultiTenancy()'s own TenantWriteGuardInterceptor registration) must not
        // register it twice, which would run it twice per SaveChanges.
        if (!_additionalInterceptorTypes.Contains(typeof(TInterceptor)))
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
    /// Configures a command timeout applied to every command issued by <typeparamref name="TContext"/>.
    /// </summary>
    /// <param name="commandTimeoutSeconds">The command timeout, in seconds.</param>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithCommandTimeout(int commandTimeoutSeconds)
    {
        _commandTimeoutSeconds = commandTimeoutSeconds;
        return this;
    }

    /// <summary>
    /// Opts in to routing <see cref="Repositories.IReadRepository{TAggregate,TId}"/> reads to a
    /// separate PostgreSQL read-replica connection, distinct from the primary connection writes
    /// always use.
    /// </summary>
    /// <param name="configureReplicaDb">
    /// Action that configures the replica <see cref="DbContextOptionsBuilder"/> (e.g., sets the
    /// replica connection string via <c>options.UseNpgsql(replicaConnectionString)</c>). Mirrors
    /// <see cref="EfCorePersistenceExtensions.AddSharedKernelEfCore{TContext}"/>'s own
    /// <c>configureDb</c> parameter shape — this builder itself never references Npgsql.
    /// </param>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithReadReplica(Action<DbContextOptionsBuilder> configureReplicaDb)
    {
        ArgumentNullException.ThrowIfNull(configureReplicaDb);
        _readReplicaConfigureDb = configureReplicaDb;
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
        EnsureServiceNameInfrastructureRegistered();

        _services.AddOptions<PersistenceServiceOptions>()
            .Configure(o => o.ServiceName = serviceName);

        return this;
    }

    /// <summary>
    /// Configures the unauthenticated audit fallback string, binding
    /// <see cref="PersistenceServiceOptions"/> from <paramref name="configuration"/>'s
    /// <see cref="PersistenceServiceOptions.SectionName"/> section.
    /// </summary>
    /// <param name="configuration">The application's <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.</param>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithServiceName(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        EnsureServiceNameInfrastructureRegistered();

        _services.AddOptions<PersistenceServiceOptions>()
            .Bind(configuration.GetSection(PersistenceServiceOptions.SectionName));

        return this;
    }

    // Idempotent-validation-registration guard.
    private void EnsureServiceNameInfrastructureRegistered()
    {
        if (_serviceNameValidationRegistered)
            return;

        _serviceNameValidationRegistered = true;

        _services.AddSingleton<IValidateOptions<PersistenceServiceOptions>, PersistenceServiceOptionsValidator>();
        _services.AddOptions<PersistenceServiceOptions>().ValidateOnStart();
    }

    /// <summary>
    /// Opts in to applying pending EF Core migrations during host startup via
    /// <c>Database.MigrateAsync</c>.
    /// </summary>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithMigrationsOnStartup()
    {
        _migrationsOnStartup = true;
        return this;
    }

    /// <summary>
    /// Registers a <see cref="TransientFaultRetryOptions"/> singleton for discoverability/observability.
    /// </summary>
    /// <param name="maxRetryCount">The maximum number of retry attempts. Defaults to 6.</param>
    /// <param name="maxRetryDelay">
    /// The maximum delay between retry attempts. Defaults to <see langword="null"/> (provider default,
    /// typically 30 seconds).
    /// </param>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithTransientFaultRetry(
        int maxRetryCount = 6,
        TimeSpan? maxRetryDelay = null)
    {
        _transientFaultRetryOptions = new TransientFaultRetryOptions(maxRetryCount, maxRetryDelay);
        return this;
    }

    /// <summary>
    /// Opts in to a pooled <c>IDbContextFactory&lt;TContext&gt;</c> registration
    /// (<c>AddPooledDbContextFactory</c>) instead of the default always-scoped <c>AddDbContext</c>
    /// registration, for services wanting the reduced per-request allocation/GC overhead of a
    /// pooled <see cref="DbContext"/> at high request throughput.
    /// </summary>
    /// <param name="poolSize">The maximum number of pooled context instances. Defaults to 1024.</param>
    /// <returns>The same builder for further chaining.</returns>
    public EfCorePersistenceBuilder<TContext> WithDbContextPooling(int poolSize = 1024)
    {
        _dbContextPoolingEnabled = true;
        _poolSize = poolSize;
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
    public EfCorePersistenceBuilder<TContext> AddSeeder<TSeeder>()
        where TSeeder : class, IDataSeeder<TContext>
    {
        _services.AddScoped<TSeeder>();
        _seedSteps.Add((
            typeof(TSeeder).Name,
            static (sp, context, ct) => sp.GetRequiredService<TSeeder>().SeedAsync(context, ct)));
        return this;
    }

    /// <summary>
    /// Finalises the DI registration.
    /// </summary>
    /// <returns>The <see cref="IServiceCollection"/> for further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup when <see cref="WithMultiTenancy"/> was called but
    /// <typeparamref name="TContext"/> does not extend <see cref="TenantedDbContext"/>; when
    /// <typeparamref name="TContext"/> extends <see cref="TenantedDbContext"/> but
    /// <see cref="WithMultiTenancy"/> was never called; when this method has already been called for
    /// <typeparamref name="TContext"/> on this <see cref="IServiceCollection"/>; when a prior,
    /// different <c>TContext</c>'s <see cref="Build"/> call already registered the unkeyed
    /// <see cref="Context.SharedKernelDbContext"/>/<see cref="IUnitOfWork"/> services this call would
    /// silently overwrite; or when any action registered via <see cref="AddBuildAction"/> throws its
    /// own guard.
    /// </exception>
    public IServiceCollection Build()
    {
        // Idempotency guard: a second 'Build()' call for the SAME TContext — whether it is a second
        // call on this exact builder instance, or a second, independently-constructed builder for the
        // same TContext sharing this IServiceCollection — leaves a stale keyed registration behind
        // that 'RekeyLastRegistrationAsInner' below would re-key a SECOND time. Re-keying the
        // ALREADY-WRAPPING 'TenantAwareDbContextFactory<TContext>' registration the first call
        // produced turns it into a factory that resolves itself, recursing until
        // StackOverflowException. 'InnerFactoryKey' is a 'static readonly' field on the CLOSED generic
        // 'EfCorePersistenceBuilder<TContext>' type, so it is genuinely shared across every builder
        // instance for the same 'TContext' — checking for an existing keyed registration under it
        // catches both shapes of repeat call, not just the same-instance one.
        if (_services.Any(sd => sd.ServiceType == typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<TContext>) && Equals(sd.ServiceKey, InnerFactoryKey)))
        {
            throw new InvalidOperationException(
                $"'.Build()' was already called for '{typeof(TContext).Name}' on this " +
                $"'IServiceCollection' — either directly (a second '.Build()' call on the same " +
                $"builder) or via a second, independently-constructed " +
                $"'AddSharedKernelEfCore<{typeof(TContext).Name}>(...)' builder. Calling '.Build()' " +
                $"more than once for the same context type corrupts DI resolution: the second call " +
                "re-registers the underlying DbContext factory as its own wrapper, which recurses into " +
                "a StackOverflowException the first time it is resolved. Remove the duplicate call.");
        }

        // Multi-context guard: EfUnitOfWork resolves the unkeyed SharedKernelDbContext/IUnitOfWork
        // services, so two DIFFERENT TContext types sharing this IServiceCollection cannot both
        // register them — the second '.Build()' call would silently win the unkeyed slot, so
        // EfUnitOfWork would bind to whichever context registered last regardless of which
        // repository/context a given consumer actually intended to commit through.
        if (_services.Any(sd => sd.ServiceType == typeof(SharedKernelDbContext) && sd.ServiceKey is null))
        {
            throw new InvalidOperationException(
                $"'.Build()' for '{typeof(TContext).Name}' would re-register the unkeyed " +
                "'SharedKernelDbContext'/'IUnitOfWork' services that a PRIOR " +
                "'AddSharedKernelEfCore<TOther>(...).Build()' call for a DIFFERENT context type " +
                "already registered on this 'IServiceCollection'. Two DbContext types sharing one " +
                "'IServiceCollection' cannot share the same unkeyed 'IUnitOfWork'/'SharedKernelDbContext' " +
                "resolution — the most recently registered context would silently win for every " +
                "consumer, including ones that intended to commit through the other context. Host each " +
                "context's persistence stack in its own DI scope/module, or inject each context's own " +
                $"'{typeof(TContext).Name}'/'IRepository<,>' directly instead of the shared " +
                "'IUnitOfWork'/'SharedKernelDbContext' seam wherever more than one context type is " +
                "registered in this service collection.");
        }

        if (_multiTenancyEnabled && !typeof(TenantedDbContext).IsAssignableFrom(typeof(TContext)))
        {
            throw new InvalidOperationException(
                $"Multi-tenancy was enabled via '.WithMultiTenancy()' but the context type " +
                $"'{typeof(TContext).FullName}' does not extend '{typeof(TenantedDbContext).FullName}'. " +
                $"Either change '{typeof(TContext).Name}' to extend 'TenantedDbContext', " +
                $"or remove the '.WithMultiTenancy()' call from the DI registration.");
        }

        // Symmetric guard (H4): a context that extends TenantedDbContext but never opted into
        // '.WithMultiTenancy()' still gets the tenant global query filter (installed unconditionally by
        // TenantedDbContext.OnModelCreating), but NOT TenantWriteGuardInterceptor — the write-side half
        // of tenant isolation. Silently running with reads filtered and writes unguarded is exactly the
        // "no error, reads still filtered" shape this whole guard family exists to rule out.
        // The one legitimate exception: a registration that binds tenant identity into the database
        // SESSION (an ITenantSessionBinder, which '.WithRowLevelSecurity()' requires) has the database
        // itself rejecting out-of-tenant writes, on every statement, including the raw SQL and bulk
        // paths no SaveChanges interceptor ever observes. That is strictly stronger than the app-level
        // write guard, so demanding '.WithMultiTenancy()' on top of it would reject the safest
        // configuration this domain offers.
        var databaseEnforcesTenantIsolation = _services.Any(sd =>
            sd.ServiceType == typeof(ITenantSessionBinder));

        if (!_multiTenancyEnabled
            && !databaseEnforcesTenantIsolation
            && typeof(TenantedDbContext).IsAssignableFrom(typeof(TContext)))
        {
            throw new InvalidOperationException(
                $"'{typeof(TContext).FullName}' extends '{typeof(TenantedDbContext).FullName}' but " +
                "'.WithMultiTenancy()' was never called. A TenantedDbContext without it gets the " +
                "read-side tenant query filter but NOT TenantWriteGuardInterceptor, the write-side " +
                "half of tenant isolation — silently unprotected writes, with no error anywhere. Call " +
                $"'.WithMultiTenancy()' on this builder, or change '{typeof(TContext).Name}' to extend " +
                "'SharedKernelDbContext' directly if it genuinely does not need tenant isolation. " +
                "Registering an 'ITenantSessionBinder' (as '.WithRowLevelSecurity()' does) also " +
                "satisfies this guard, since the database then enforces isolation on every statement.");
        }

        // The former "'.WithDbContextPooling()' cannot be combined with
        // '.WithMultiTenancy()'" guard is GONE — replaced with a real fix. TenantedDbContext's
        // constructor no longer takes ICurrentTenantContext at all (see its own class remarks); tenant
        // identity is attached per lease by TenantAwareDbContextFactory<TContext>, exactly mirroring
        // how ICurrentActorContext is already attached per lease today. TenantWriteGuardInterceptor
        // likewise no longer captures ICurrentTenantContext in its constructor — it reads it LIVE off
        // the executing context instance, so it is pooling-safe as a SINGLETON like the platform
        // three. The former "'.WithDbContextPooling()' cannot be combined with
        // '.WithDbContextFactory()'" guard is ALSO gone: IDbContextFactory<TContext> is now always
        // registered (see the unified factory registration in this method), so there is no longer a
        // second, conflicting registration for WithDbContextFactory()/RequireDbContextFactory() to
        // trigger.

        // Capability-specific guards (e.g. encryption's pooling incompatibility, or its
        // WithExternalEncryptionKeyProvider-before-WithEncryption ordering requirement) run here, via
        // whatever that capability's own.WithX() extension method registered through AddBuildAction —
        // this core builder no longer knows those capabilities exist.
        foreach (var action in _buildActions)
            action();

        // Register the discoverability singleton when WithTransientFaultRetry() was called.
        if (_transientFaultRetryOptions is not null)
        {
            _services.AddSingleton(_transientFaultRetryOptions);

            // The retry-attempt diagnostic listener subscribes to EF Core's own
            // provider-neutral CoreEventId.ExecutionStrategyRetrying diagnostic event. Registered
            // as a hosted service so its DiagnosticListener.AllListeners subscription is active for
            // the app's lifetime — never registered when WithTransientFaultRetry() was not called.
            _services.AddHostedService<PersistenceRetryDiagnosticListener>();
        }

        // Register PersistenceServiceOptions default if not already configured by WithServiceName().
        // This ensures the default IAuditActorContext (AnonymousActorContext) can always resolve it.
        if (!_services.Any(sd => sd.ServiceType == typeof(IOptions<PersistenceServiceOptions>))
            && !_services.Any(sd => sd.ServiceType == typeof(IConfigureOptions<PersistenceServiceOptions>)))
        {
            _services.AddOptions<PersistenceServiceOptions>();
        }

        if (_dbContextPoolingEnabled)
        {
            // SINGLETON, with a DI-free seed ICurrentActorContext — never resolves
            // ICurrentActorContext from DI at all, so EF's pool-level activator never has anything
            // unsafe to resolve for these three. AuditInterceptor/SoftDeleteInterceptor already read
            // actor identity LIVE off the executing SharedKernelDbContext.CurrentActor (never their
            // own constructor-captured field) for their real per-save logic — this
            // constructor-captured seed is truly a throwaway value, immediately superseded by
            // RefreshActor on every lease.
            _services.AddSingleton(sp => new AuditInterceptor(new PooledSeedActorContext(), sp.GetRequiredService<IClock>()));
            _services.AddSingleton(sp => new SoftDeleteInterceptor(new PooledSeedActorContext(), sp.GetRequiredService<IClock>()));
            _services.AddSingleton<ConcurrencyInterceptor>();

            if (_multiTenancyEnabled)
            {
                // TenantWriteGuardInterceptor no longer captures
                // ICurrentTenantContext in its own constructor (it reads tenant identity LIVE off the
                // executing context instance — see its own class remarks) and its only remaining
                // dependency, ICrossTenantScope, is AsyncLocal-backed and singleton-registered — safe
                // to resolve from the pool-level activator. It is the one AddInterceptor<T>()
                // registration allowed to combine with pooling; any other is rejected below. Also
                // mapped to ISaveChangesInterceptor (same singleton instance) so
                // PersistenceContextDependencies' sp.GetServices<ISaveChangesInterceptor>() resolution
                // below picks it up identically to how the non-pooled branch's own loop already does.
                _services.AddSingleton<TenantWriteGuardInterceptor>();
                _services.AddSingleton<ISaveChangesInterceptor>(sp => sp.GetRequiredService<TenantWriteGuardInterceptor>());
            }

            if (_additionalInterceptorTypes.Any(t => t != typeof(TenantWriteGuardInterceptor)))
            {
                throw new InvalidOperationException(
                    "'.WithDbContextPooling()' cannot be combined with a consumer-supplied " +
                    "AddInterceptor<T>() registration other than the platform's own " +
                    "TenantWriteGuardInterceptor (added automatically by WithMultiTenancy()) — EF " +
                    "Core's pooled-context activator cannot safely resolve an arbitrary interceptor's " +
                    "own scoped dependencies. Remove '.WithDbContextPooling()', or remove the " +
                    "AddInterceptor<T>() call.");
            }

            // SINGLETON — every dependency PersistenceContextDependencies.ApplyTo needs
            // (the three platform interceptors above, TenantWriteGuardInterceptor when multi-tenant,
            // and every registered IPersistenceOptionsExtension) is itself Singleton under pooling, so
            // building this bundle once and sharing the same instance across every pooled slot is
            // safe — see the pooled AddPooledDbContextFactory callback below, which resolves this SAME
            // registration both to construct TContext and to apply its options.
            _services.AddSingleton(sp => new Context.PersistenceContextDependencies(
                sp.GetRequiredService<AuditInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<ConcurrencyInterceptor>(),
                sp.GetServices<ISaveChangesInterceptor>(),
                sp.GetServices<IPersistenceModelConventionFactory>(),
                sp.GetServices<IPersistenceModelConfigurator>(),
                sp.GetServices<IPersistenceOptionsExtension>(),
                sp.GetServices<IDbUpdateExceptionClassifier>()));
        }
        else
        {
            // Register interceptors as scoped so they receive per-request ICurrentActorContext / IClock.
            _services.AddScoped<AuditInterceptor>();
            _services.AddScoped<SoftDeleteInterceptor>();
            _services.AddScoped<ConcurrencyInterceptor>();

            // Register any additional consumer-supplied interceptors as scoped (including
            // TenantWriteGuardInterceptor, when WithMultiTenancy() added it — its own dependencies
            // are pooling-safe now, but that only matters under WithDbContextPooling(); the scoped
            // registration here is correct and unchanged for the non-pooled path).
            foreach (var interceptorType in _additionalInterceptorTypes)
            {
                _services.AddScoped(interceptorType);
                _services.AddScoped(typeof(ISaveChangesInterceptor), sp =>
                    sp.GetRequiredService(interceptorType) as ISaveChangesInterceptor
                        ?? throw new InvalidOperationException(
                            $"Type '{interceptorType.Name}' does not implement ISaveChangesInterceptor."));
            }

            // SCOPED — resolved once per DI scope, from that scope's own AuditInterceptor/
            // SoftDeleteInterceptor/ConcurrencyInterceptor/additional-interceptor registrations above.
            _services.AddScoped(sp => new Context.PersistenceContextDependencies(
                sp.GetRequiredService<AuditInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<ConcurrencyInterceptor>(),
                sp.GetServices<ISaveChangesInterceptor>(),
                sp.GetServices<IPersistenceModelConventionFactory>(),
                sp.GetServices<IPersistenceModelConfigurator>(),
                sp.GetServices<IPersistenceOptionsExtension>(),
                sp.GetServices<IDbUpdateExceptionClassifier>()));
        }

        // Build effective configureDb action — wrap with compiled model and/or command timeout if supplied.
        Action<IServiceProvider, DbContextOptionsBuilder> effectiveConfigureDb = (sp, options) =>
        {
            _configureDb(sp, options);

            if (_compiledModel is not null)
                options.UseModel(_compiledModel);

            if (_commandTimeoutSeconds is not null)
            {
                var relationalExtension = options.Options.Extensions
                    .OfType<RelationalOptionsExtension>()
                        .FirstOrDefault();

                if (relationalExtension is not null)
                {
                    var updatedExtension = relationalExtension.WithCommandTimeout(_commandTimeoutSeconds);
                    ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(updatedExtension);
                }
            }
        };

        // Register the REAL (pooled or
        // non-pooled) low-level factory under a private key, then wrap it with
        // TenantAwareDbContextFactory<TContext> — the PUBLIC, scoped IDbContextFactory<TContext> that
        // attaches the calling scope's actor/tenant identity to every context it hands out, whether
        // freshly constructed or reused from a pool. EVERY way of obtaining a TContext instance
        // (direct injection below, or explicit IDbContextFactory<TContext> injection — the shape a
        // background worker/hosted service's own DI scope uses) funnels through this one decorator,
        // so neither TenantedDbContext's constructor nor TenantWriteGuardInterceptor's constructor
        // ever needs to resolve a genuinely Scoped service from EF's pool-level activator.
        if (_dbContextPoolingEnabled)
        {
            // A pooled DbContext's OPTIONS (including its interceptor list) AND ITS
            // OWN OTHER CONSTRUCTOR PARAMETERS are resolved by EF Core's pooled-context activator,
            // which is bound to the service provider as it existed when the pooled FACTORY ITSELF was
            // constructed (a singleton-level provider) — never the real per-request scope, no matter
            // which scope CreateDbContext() is later called from (confirmed empirically: this is not
            // merely "the options callback runs early", the SAME resolution context is used for every
            // OTHER constructor parameter of TContext). Resolving a genuinely Scoped service anywhere
            // in that path is a captive-dependency violation that throws under
            // ServiceProviderOptions.ValidateScopes = true and, when ValidateScopes is off, silently
            // misattributes identity forever for that pool slot. IClock is the one genuinely shared,
            // cross-request value baked into a pooled interceptor at slot-construction time; it is
            // resolved here (safe only because it is registered Singleton — enforced by this guard).
            if (_services.Any(sd => sd.ServiceType == typeof(IClock) && sd.Lifetime != ServiceLifetime.Singleton))
            {
                throw new InvalidOperationException(
                    "'.WithDbContextPooling()' requires 'IClock' to be registered as a Singleton. A " +
                    "pooled DbContext's interceptors are constructed once per pool slot, sharing one " +
                    "IClock instance across every request that leases that slot — a Scoped or " +
                    "Transient IClock would either be a captive dependency or silently stop updating.");
            }

            _services.AddPooledDbContextFactory<TContext>((sp, options) =>
            {
                effectiveConfigureDb(sp, options);

                // SharedKernelDbContext.OnConfiguring — where PersistenceContextDependencies.ApplyTo
                // normally runs — never runs for a pooled context (Options.IsFrozen is true from the
                // very first construction, per that method's own remarks), so this callback calls the
                // EXACT SAME method here, against the SAME pool-bound provider TContext's own
                // constructor is resolved from (the singleton PersistenceContextDependencies
                // registration above). Calling the identical method from both the pooled and
                // non-pooled paths — rather than maintaining two independent copies of the same
                // interceptor/options-extension wiring — is what makes it structurally impossible for
                // the two paths to silently drift apart the way they previously could (a consumer
                // whose context declared a shorter constructor lost every capability threaded through
                // the collections this method applies, with no error anywhere).
                sp.GetRequiredService<PersistenceContextDependencies>().ApplyTo(options);
            }, _poolSize);
        }
        else
        {
            // Registered SCOPED — not the AddDbContextFactory default of Singleton. Each DI
            // scope gets its OWN DbContextFactory<TContext> instance, constructed against THAT
            // scope's provider, so TContext's other constructor parameters (AuditInterceptor/
            // SoftDeleteInterceptor/ConcurrencyInterceptor, all registered scoped above) resolve
            // correctly per scope — exactly what the former AddDbContext<TContext>(...) registration
            // did, just reached through IDbContextFactory<TContext> instead of DI constructing
            // TContext directly. Never Singleton here — that would reintroduce the exact
            // captive-dependency hazard pooling has, for zero pooling benefit.
            _services.AddDbContextFactory<TContext>(
                (sp, options) => effectiveConfigureDb(sp, options),
                ServiceLifetime.Scoped);
        }

        RekeyLastRegistrationAsInner(_services, typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<TContext>), InnerFactoryKey);

        // The PUBLIC IDbContextFactory<TContext> — scoped, attaches THIS scope's actor/tenant
        // identity to every context it hands out. A background worker/hosted service that wants a
        // specific identity creates its own scope, arranges for that scope's ICurrentActorContext/
        // ICurrentTenantContext to report it, then resolves IDbContextFactory<TContext> from that
        // scope — see TenantAwareDbContextFactory<TContext>'s own remarks.
        _services.AddScoped<Microsoft.EntityFrameworkCore.IDbContextFactory<TContext>>(sp =>
            new TenantAwareDbContextFactory<TContext>(
                sp.GetRequiredKeyedService<Microsoft.EntityFrameworkCore.IDbContextFactory<TContext>>(InnerFactoryKey),
                sp.GetRequiredService<ICurrentActorContext>(),
                sp.GetRequiredService<ICurrentTenantContext>()));

        // TContext direct injection always rides on the same public, tenant/actor-attaching factory —
        // pooled or not, multi-tenant or not.
        _services.AddScoped<TContext>(sp =>
            sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<TContext>>().CreateDbContext());

        // Register TContext also as the base SharedKernelDbContext so EfUnitOfWork resolves it.
        _services.AddScoped<SharedKernelDbContext>(sp => sp.GetRequiredService<TContext>());

        // Read-replica routing — opt-in via.WithReadReplica(...).
        if (_readReplicaConfigureDb is not null)
        {
            var replicaOptionsBuilder = new DbContextOptionsBuilder<TContext>();
            _readReplicaConfigureDb(replicaOptionsBuilder);
            var replicaOptions = replicaOptionsBuilder.Options;

            _services.AddKeyedSingleton(ReadReplicaKeys.ReplicaOptions, replicaOptions);
            _services.AddScoped<IReadReplicaContextAccessor<SharedKernelDbContext>>(sp =>
                new ReadReplicaContextAccessor<TContext>(
                    sp,
                    sp.GetRequiredKeyedService<DbContextOptions<TContext>>(ReadReplicaKeys.ReplicaOptions)));
        }

        if (_transactionalUnitOfWorkEnabled)
        {
            // When transactional UoW is enabled, EfTransactionalUnitOfWork serves as both
            // IUnitOfWork and ITransactionalUnitOfWork — same scoped instance.
            _services.AddScoped<EfTransactionalUnitOfWork>();
            _services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EfTransactionalUnitOfWork>());
            _services.AddScoped<ITransactionalUnitOfWork>(sp => sp.GetRequiredService<EfTransactionalUnitOfWork>());

            // The ambient (connection, transaction) publication seam a Dapper command
            // service resolves (IAmbientDbTransaction) to enlist in this same explicit transaction.
            _services.AddScoped<AmbientDbTransactionAccessor>();
            _services.AddScoped<SharedKernel.Persistence.Abstractions.Coordination.IAmbientDbTransaction>(
                sp => sp.GetRequiredService<AmbientDbTransactionAccessor>());
        }
        else
        {
            // Standard non-transactional path.
            _services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        }

        // ISpecificationEvaluator<T> — singleton because SpecificationEvaluator<T> is stateless.
        _services.AddSingleton(typeof(ISpecificationEvaluator<>), typeof(SpecificationEvaluator<>));

        // Default ICurrentActorContext when none is present. A consuming service's real bridge (e.g.
        // 13.ServiceDefaults.Persistence's SecurityCurrentActorContext) replaces this — the platform's
        // usual "TryAdd wins only if nothing else registered first" placeholder pattern, extended to
        // registration order not mattering because callers use Add, not TryAdd, on their real bridge.
        if (!_services.Any(sd => sd.ServiceType == typeof(ICurrentActorContext)))
        {
            _services.Add(ServiceDescriptor.Scoped<ICurrentActorContext, AnonymousActorContext>());
        }

        // Default ICurrentTenantContext when none is present. Registered unconditionally (not only
        // for multi-tenant services) because opt-in sibling capabilities that are NOT
        // tenancy-specific — e.g. SharedKernel.Persistence.EfCore.Auditing's EfAuditTrailWriter — also
        // resolve this seam, and a single-tenant service enabling one of them must not be forced to
        // also call .WithMultiTenancy() just to satisfy that dependency.
        if (!_services.Any(sd => sd.ServiceType == typeof(ICurrentTenantContext)))
        {
            _services.Add(ServiceDescriptor.Scoped<ICurrentTenantContext, NullCurrentTenantContext>());
        }

        // Default ICrossTenantScope when none is present.
        if (!_services.Any(sd => sd.ServiceType == typeof(ICrossTenantScope)))
        {
            _services.AddSingleton<ICrossTenantScope, CrossTenantScope>();
        }

        // IDomainEventDispatcher is optional — consuming services opt in by registering it.
        // EfUnitOfWork resolves it as a nullable IDomainEventDispatcher? via DI.

        // IClock — registered as singleton only when not already present.
        if (!_services.Any(sd => sd.ServiceType == typeof(IClock)))
        {
            _services.AddSingleton<IClock, SystemClock>();
        }

        // IDbContextFactory<TContext> is now ALWAYS registered above (TContext direct
        // injection itself rides on it), so WithDbContextFactory()/RequireDbContextFactory() no
        // longer trigger a separate registration — they are kept as a no-op public API for source
        // compatibility with sibling capability packages (e.g. the encryption package's rotation
        // registry) that call RequireDbContextFactory() to declare the dependency they need.

        // MigrationAndSeedHostedService<TContext> is registered only when migrations-on-startup
        // was requested, or at least one seeder was registered — fully opt-in, zero overhead
        // otherwise.
        if (_migrationsOnStartup || _seedSteps.Count > 0)
        {
            var migrationsOnStartup = _migrationsOnStartup;
            var seedSteps = _seedSteps.ToArray();

            _services.AddHostedService<MigrationAndSeedHostedService<TContext>>(sp =>
                new MigrationAndSeedHostedService<TContext>(
                    sp,
                    migrationsOnStartup,
                    seedSteps,
                    sp.GetService<ILogger<MigrationAndSeedHostedService<TContext>>>()));
        }

        // Zero-configuration startup check, registered ONLY when an opt-in capability exists that
        // could be silently lost — see PersistenceContextWiringValidator{TContext}'s own remarks for
        // what it catches. A registration with no opt-in capability has nothing to verify, and adding
        // a hosted service for it would break this domain's "a plain EF Core registration schedules
        // no startup work" rule, which MigrationAndSeedHostedServiceTests asserts directly.
        var multiTenancyEnabled = _multiTenancyEnabled;
        var hasCapabilitiesToVerify =
            multiTenancyEnabled
            || _additionalInterceptorTypes.Count > 0
            || _services.Any(sd =>
                sd.ServiceType == typeof(IPersistenceOptionsExtension)
                || sd.ServiceType == typeof(ISaveChangesInterceptor)
                || sd.ServiceType == typeof(IDbUpdateExceptionClassifier));

        if (hasCapabilitiesToVerify)
        {
            _services.AddHostedService<PersistenceContextWiringValidator<TContext>>(sp =>
                new PersistenceContextWiringValidator<TContext>(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    multiTenancyEnabled));
        }

        return _services;
    }

    // Moves the LAST registration matching serviceType from its normal,
    // unkeyed slot to a keyed slot under key, using only the public ServiceDescriptor surface
    // (ImplementationType/ImplementationFactory/ImplementationInstance) — never any EF Core-internal
    // type. This lets TenantAwareDbContextFactory<TContext> be registered as the PUBLIC, unkeyed
    // IDbContextFactory<TContext> while still reaching the REAL (pooled or non-pooled) factory EF
    // Core's own AddPooledDbContextFactory/AddDbContextFactory extension methods just registered —
    // neither of which offers a keyed overload. Generic on purpose: works identically regardless of
    // which of the three ServiceDescriptor shapes the wrapped registration happens to use.
    private static void RekeyLastRegistrationAsInner(IServiceCollection services, Type serviceType, object key)
    {
        var index = -1;
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == serviceType)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"No registration for '{serviceType}' was found to re-key — this is an internal " +
                $"EfCorePersistenceBuilder invariant violation, not a consumer misconfiguration.");
        }

        var original = services[index];
        services.RemoveAt(index);

        ServiceDescriptor keyed;
        if (original.ImplementationType is not null)
        {
            keyed = ServiceDescriptor.DescribeKeyed(serviceType, key, original.ImplementationType, original.Lifetime);
        }
        else if (original.ImplementationFactory is not null)
        {
            var factory = original.ImplementationFactory;
            keyed = ServiceDescriptor.DescribeKeyed(serviceType, key, (sp, _) => factory(sp), original.Lifetime);
        }
        else if (original.ImplementationInstance is not null)
        {
            var instance = original.ImplementationInstance;
            keyed = ServiceDescriptor.DescribeKeyed(serviceType, key, (_, _) => instance, ServiceLifetime.Singleton);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported ServiceDescriptor shape for '{serviceType}' — neither ImplementationType, " +
                $"ImplementationFactory, nor ImplementationInstance is set.");
        }

        services.Add(keyed);
    }

    // A DI-free seed ICurrentActorContext used ONLY to construct pooled
    // interceptors at pool-slot-construction time — never observed by a real request. See the
    // WithDbContextPooling() branch of Build() for the full rationale.
    private sealed class PooledSeedActorContext : ICurrentActorContext
    {
        public string ActorId => string.Empty;
        public ActorKind ActorKind => ActorKind.System;
    }
}