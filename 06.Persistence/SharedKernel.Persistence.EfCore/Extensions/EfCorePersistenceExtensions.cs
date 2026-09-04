using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Domain;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.ReadReplica;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions.Abstractions;
using AppBehaviorsIUnitOfWork = SharedKernel.Application.Behaviors.Transaction.IUnitOfWork;
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
    private bool _applicationTransactionBehaviorEnabled;
    private bool _registerFactory;
    private bool _registerEncryption;
    private bool _auditTrailEnabled;
    private bool _serviceNameValidationRegistered;
    private bool _migrationsOnStartup;
    private bool _dbContextPoolingEnabled;
    private int _poolSize = 1024;
    private IModel? _compiledModel;
    private TransientFaultRetryOptions? _transientFaultRetryOptions;
    private int? _commandTimeoutSeconds;
    private Action<DbContextOptionsBuilder>? _readReplicaConfigureDb;
    private readonly List<Type> _additionalInterceptorTypes = [];
    private readonly List<(string SeederTypeName, Func<IServiceProvider, TContext, CancellationToken, Task> Invoke)> _seedSteps = [];

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
    /// Configures a command timeout applied to every command issued by <typeparamref name="TContext"/>.
    /// </summary>
    /// <param name="commandTimeoutSeconds">The command timeout, in seconds.</param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <para>
    /// WO-053/P-337 — wraps the caller-supplied <c>configureDb</c> action, the same wrapping
    /// pattern <see cref="WithCompiledModel"/> already uses for <c>.UseModel(compiledModel)</c>.
    /// <strong>CORRECTED against the originally-planned design</strong>: plain
    /// <see cref="DbContextOptionsBuilder"/> has no provider-neutral <c>CommandTimeout(...)</c>
    /// method of its own — confirmed by direct compilation against the real EF Core 10.0.5
    /// package; that member exists only as an instance method on the provider-specific
    /// <c>RelationalDbContextOptionsBuilder&lt;TBuilder,TExtension&gt;</c> returned from
    /// <c>UseNpgsql(...)</c>'s own configuration callback, unreachable here without an Npgsql
    /// reference. The genuinely provider-neutral mechanism — verified end-to-end against a real
    /// constructed <see cref="DbContext"/>, confirming <c>Database.GetCommandTimeout()</c> reflects
    /// it — locates the already-registered
    /// <see cref="Microsoft.EntityFrameworkCore.Infrastructure.RelationalOptionsExtension"/> (the
    /// base type every provider's own options extension derives from) via a covariant
    /// <c>.OfType&lt;T&gt;()</c> scan of <c>Options.Extensions</c> (unlike
    /// <c>DbContextOptions.FindExtension&lt;T&gt;()</c>, which requires an exact type match and
    /// returns <see langword="null"/> for a base type), calls its immutable-with
    /// <c>WithCommandTimeout(...)</c>, and re-registers the updated extension via
    /// <c>AddOrUpdateExtension</c>. Never references any Npgsql type by name — deliberately NOT
    /// placed on <c>UsePostgreSQL(...)</c> (<c>SharedKernel.Persistence.PostgreSQL</c>), unlike the
    /// genuinely Npgsql-only <c>EnableRetryOnFailure</c> (P-320).
    /// </para>
    /// <para>Optional. Omitting this call preserves today's provider-default command timeout exactly.</para>
    /// </remarks>
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
    /// <remarks>
    /// <para>
    /// WO-053/P-338 — see the "Read-Replica Routing" section of <c>06.Persistence/CLAUDE.md</c> for
    /// the full design. Registers a keyed singleton <c>DbContextOptions&lt;TContext&gt;</c> for the
    /// replica plus a scoped <see cref="ReadReplica.IReadReplicaContextAccessor{TContext}"/> that
    /// lazily constructs the replica <typeparamref name="TContext"/> instance (once per DI scope,
    /// cached thereafter) via <c>ActivatorUtilities.CreateInstance&lt;TContext&gt;</c>, reusing the
    /// same scope-ambient, DI-resolved interceptor instances the primary context already resolved.
    /// </para>
    /// <para>
    /// READ-AFTER-WRITE CONSISTENCY BECOMES THE CALLER'S RESPONSIBILITY ONCE ENABLED — a handler
    /// that writes then immediately reads via <c>IReadRepository</c> in the same logical operation
    /// MAY OBSERVE STALE DATA under replication lag. A read issued inside an active transaction is
    /// NEVER routed to the replica, even when this is configured.
    /// </para>
    /// <para>Optional. Omitting this call leaves every read/write on the single primary connection.</para>
    /// </remarks>
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
    /// Opts in to bridging the 05.Application.Behaviors <c>TransactionBehavior</c> to this
    /// <c>EfUnitOfWork</c> by registering the same scoped instance against
    /// <see cref="AppBehaviorsIUnitOfWork"/>.
    /// </summary>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <para>
    /// After calling this method, both <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c>
    /// and <c>SharedKernel.Application.Behaviors.Transaction.IUnitOfWork</c> resolve the SAME
    /// scoped <see cref="EfUnitOfWork"/> instance per DI scope — not two independent instances.
    /// </para>
    /// <para>
    /// This is opt-in. Omitting this call leaves <c>Build()</c> behavior completely unchanged —
    /// <c>SharedKernel.Application.Behaviors.Transaction.IUnitOfWork</c> remains unregistered.
    /// Services that do not use <c>TransactionBehavior</c>, or that bridge via a hand-written
    /// composition-root adapter, are unaffected.
    /// </para>
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> WithApplicationTransactionBehavior()
    {
        _applicationTransactionBehaviorEnabled = true;
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
        EnsureEncryptionInfrastructureRegistered();

        if (configure is not null)
        {
            _services.AddOptions<EncryptionOptions>().Configure(configure);
        }

        return this;
    }

    /// <summary>
    /// Opts in to field-level AES-256-GCM transparent encryption, binding
    /// <see cref="EncryptionOptions"/> from <paramref name="configuration"/>'s
    /// <see cref="EncryptionOptions.SectionName"/> section.
    /// </summary>
    /// <param name="configuration">The application's <see cref="IConfiguration"/>.</param>
    /// <param name="configure">
    /// Optional additional code-based configuration, layered on top of the bound values under
    /// normal <c>IOptions&lt;T&gt;</c> later-registration-wins semantics.
    /// </param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <para>
    /// WO-053/P-334 — binds via <c>configuration.GetSection(EncryptionOptions.SectionName)</c>,
    /// never a bare <c>"SharedKernel:Encryption"</c> literal. Composes with the pre-existing
    /// <see cref="WithEncryption(Action{EncryptionOptions}?)"/> overload — both may be chained; the
    /// eager startup validation registered by <see cref="EnsureEncryptionInfrastructureRegistered"/>
    /// is idempotent across repeated <c>.WithEncryption(...)</c> calls in the same builder chain.
    /// </para>
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> WithEncryption(
        IConfiguration configuration,
        Action<EncryptionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        EnsureEncryptionInfrastructureRegistered();

        _services.AddOptions<EncryptionOptions>()
            .Bind(configuration.GetSection(EncryptionOptions.SectionName));

        if (configure is not null)
        {
            _services.AddOptions<EncryptionOptions>().Configure(configure);
        }

        return this;
    }

    // Registers the encryption infrastructure exactly once regardless of how many .WithEncryption(...)
    // overloads are chained — idempotent-validation-registration guard (WO-053/P-334).
    private void EnsureEncryptionInfrastructureRegistered()
    {
        if (_registerEncryption)
            return;

        _registerEncryption = true;

        _services.AddOptions<EncryptionOptions>();
        _services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>();
        _services.AddOptions<EncryptionOptions>().ValidateOnStart();

        // Singleton, AsyncLocal-backed rotation-target-version accessor — resolved once by
        // EncryptionModelConvention during model finalization (EF Core caches the compiled model,
        // including converters, across DbContext instances of the same context type, so a scoped
        // registration would only be observed by the very first context's converters). Mutated by
        // EncryptionRotationService<TContext> for the duration of each batch's SaveChangesAsync.
        _services.AddSingleton<IEncryptionVersionOverride, EncryptionVersionOverride>();

        // WO-051/P-323: singleton decode-once-per-config-value cache backing
        // EncryptionOptionsKeyProvider — deliberately NOT scoped, since decoded key bytes vary only
        // with the config VALUE, not per request.
        _services.AddSingleton<EncryptionKeyByteCache>();

        // P-227: Register EncryptionOptionsKeyProvider as scoped IEncryptionKeyProvider.
        // Scoped lifetime matches IEncryptionVersionOverride's existing scoped lifetime.
        // D-108/P-448: EncryptedValueConverter itself no longer resolves IEncryptionKeyProvider
        // directly — this registration is still required because 01.Core's AesGcmEncryptionService
        // (the concrete ISymmetricEncryptionService EncryptedValueConverter calls) resolves
        // IEncryptionKeyProvider from DI internally to bridge its synchronous Encrypt/Decrypt members
        // onto the now-asynchronous IEncryptionKeyProvider contract.
        _services.AddScoped<IEncryptionKeyProvider, EncryptionOptionsKeyProvider>();
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
    /// <param name="configuration">The application's <see cref="IConfiguration"/>.</param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// WO-053/P-334 — binds via <c>configuration.GetSection(PersistenceServiceOptions.SectionName)</c>,
    /// never a bare <c>"SharedKernel:Persistence"</c> literal. Composes with the pre-existing
    /// <see cref="WithServiceName(string)"/> overload under normal <c>IOptions&lt;T&gt;</c>
    /// later-registration-wins semantics.
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> WithServiceName(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        EnsureServiceNameInfrastructureRegistered();

        _services.AddOptions<PersistenceServiceOptions>()
            .Bind(configuration.GetSection(PersistenceServiceOptions.SectionName));

        return this;
    }

    // Idempotent-validation-registration guard mirroring EnsureEncryptionInfrastructureRegistered
    // (WO-053/P-334).
    private void EnsureServiceNameInfrastructureRegistered()
    {
        if (_serviceNameValidationRegistered)
            return;

        _serviceNameValidationRegistered = true;

        _services.AddSingleton<IValidateOptions<PersistenceServiceOptions>, PersistenceServiceOptionsValidator>();
        _services.AddOptions<PersistenceServiceOptions>().ValidateOnStart();
    }

    /// <summary>
    /// Opts in to the append-only, hash-chained audit-trail capability
    /// (<see cref="IAuditTrailWriter"/>/<see cref="IAuditQueryService"/>).
    /// </summary>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <para>
    /// WO-071/P-457/D-125. Registers:
    /// <list type="bullet">
    ///   <item><description><see cref="AuditTrailFeatureMarker"/> (singleton) — see its own remarks for why this is required for <typeparamref name="TContext"/> to pick up <see cref="AuditRecordEntityConfiguration"/>.</description></item>
    ///   <item><description><see cref="AuditRecordImmutabilityInterceptor"/>, via the same <see cref="AddInterceptor{TInterceptor}"/> pipeline any consumer-supplied interceptor uses.</description></item>
    ///   <item><description><see cref="IAuditTrailWriter"/> → <see cref="EfAuditTrailWriter"/> (scoped).</description></item>
    ///   <item><description><see cref="IAuditQueryService"/> → <see cref="EfAuditQueryService"/> (scoped).</description></item>
    ///   <item><description>A DEFAULT <see cref="IAuditActorContext"/> → <see cref="EfCoreAuditActorContext"/>, registered at <see cref="Build"/> time ONLY when the consumer has not already registered their own — mirroring the existing no-op <c>IUserContext</c>/<c>ITenantProvider</c> placeholder pattern.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>REQUIRES two things the consumer must supply themselves:</strong> (1) the downstream
    /// <typeparamref name="TContext"/>'s own constructor must declare an
    /// <see cref="AuditTrailFeatureMarker"/><c>?</c> parameter and forward it to <c>base(...)</c> —
    /// see <see cref="AuditTrailFeatureMarker"/>'s remarks for why a DI-resolved marker type,
    /// mirroring the existing <c>ISymmetricEncryptionService?</c>/<c>IEncryptionKeyProvider?</c>
    /// pattern, is used instead of a raw <see langword="bool"/>; (2) <c>AddSharedKernelCryptography()</c>
    /// (<c>01.Core/SharedKernel.Cryptography</c>) must have been called so
    /// <see cref="SharedKernel.Cryptography.Hashing.IContentHasher"/> resolves — mirroring
    /// <see cref="WithEncryption(Action{EncryptionOptions}?)"/>'s existing requirement for
    /// <c>ISymmetricEncryptionService</c>.
    /// </para>
    /// <para>Optional. Omitting this call leaves all existing behavior unchanged — no <c>AuditRecord</c> table, no audit services registered.</para>
    /// </remarks>
    public EfCorePersistenceBuilder<TContext> WithAuditTrail()
    {
        if (_auditTrailEnabled)
            return this;

        _auditTrailEnabled = true;

        _services.AddSingleton<AuditTrailFeatureMarker>();

        AddInterceptor<AuditRecordImmutabilityInterceptor>();

        _services.AddScoped<IAuditTrailWriter, EfAuditTrailWriter>();
        _services.AddScoped<IAuditQueryService, EfAuditQueryService>();

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
    /// Registers a <see cref="TransientFaultRetryOptions"/> singleton for discoverability/observability.
    /// </summary>
    /// <param name="maxRetryCount">The maximum number of retry attempts. Defaults to 6.</param>
    /// <param name="maxRetryDelay">
    /// The maximum delay between retry attempts. Defaults to <see langword="null"/> (provider default,
    /// typically 30 seconds).
    /// </param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <para>
    /// WO-051/P-320 — CANNOT itself configure Npgsql (this package never references Npgsql). This is
    /// a documented, REQUIRED two-call opt-in PAIR with
    /// <c>UsePostgreSQL(connectionString, maxRetryCount, maxRetryDelay)</c> (the PostgreSQL package):
    /// calling only this method without also passing matching values to <c>UsePostgreSQL(...)</c>
    /// registers the options singleton but enables NO actual retry behavior.
    /// </para>
    /// <para>
    /// The retry-SAFETY correction for explicit transactions
    /// (<c>EfTransactionalUnitOfWork.BeginTransactionAsync</c>'s guard +
    /// <c>ExecuteInTransactionAsync</c>) is UNCONDITIONAL and does NOT depend on this method having
    /// been called — it queries live EF Core execution-strategy state, so it correctly protects a
    /// consumer who enabled retry solely via <c>UsePostgreSQL(...)</c>.
    /// </para>
    /// </remarks>
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
    /// <remarks>
    /// <para>
    /// WO-051/P-322 — see the "DbContext Pooling" section of <c>06.Persistence/CLAUDE.md</c> for the
    /// full pooling-safety story. In short: <see cref="SharedKernelDbContext"/>/
    /// <see cref="TenantedDbContext"/> now expose <c>RefreshUserContext</c>/<c>RefreshRequestContext</c>
    /// specifically so this method can layer a scoped factory delegate over
    /// <c>IDbContextFactory&lt;TContext&gt;.CreateDbContext()</c> that refreshes the leased instance's
    /// user/tenant context to the CURRENT scope's real values on every resolution — regardless of
    /// what (possibly stale, possibly meaningless) values were baked in whenever that pooled slot's
    /// constructor last ran. Existing consumer code that injects <typeparamref name="TContext"/>
    /// directly is completely unaffected — pooling and the per-lease refresh are transparent.
    /// </para>
    /// <para>
    /// Cannot be combined with <see cref="WithDbContextFactory"/> (both would register a conflicting
    /// <c>IDbContextFactory&lt;TContext&gt;</c>) or with <see cref="WithEncryption(Action{EncryptionOptions}?)"/> (its
    /// <c>IEncryptionVersionOverride</c> rotation-scoped seam has the identical constructor-capture
    /// staleness hazard this method's redesign fixes for user/tenant context, and has not yet been
    /// proven safe under pooling) — both combinations throw an actionable
    /// <see cref="InvalidOperationException"/> at <see cref="Build"/> time.
    /// </para>
    /// </remarks>
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

        // WO-051/P-322: .WithDbContextPooling() guards — both combinations are unsupported today.
        if (_dbContextPoolingEnabled && _registerFactory)
        {
            throw new InvalidOperationException(
                "'.WithDbContextPooling()' cannot be combined with '.WithDbContextFactory()' — both " +
                "would register a conflicting IDbContextFactory<TContext> (pooled vs. non-pooled). " +
                "Remove one of the two calls.");
        }

        if (_dbContextPoolingEnabled && _registerEncryption)
        {
            throw new InvalidOperationException(
                "'.WithDbContextPooling()' cannot be combined with '.WithEncryption()' — " +
                "IEncryptionVersionOverride's rotation-scoped seam has the identical constructor-" +
                "capture staleness hazard this method's redesign fixes for user/tenant context, and " +
                "has not yet been proven safe under pooling. Remove one of the two calls.");
        }

        // WO-051/P-320: register the discoverability singleton when WithTransientFaultRetry() was called.
        if (_transientFaultRetryOptions is not null)
        {
            _services.AddSingleton(_transientFaultRetryOptions);

            // WO-053/P-333: the retry-attempt diagnostic listener subscribes to EF Core's own
            // provider-neutral CoreEventId.ExecutionStrategyRetrying diagnostic event. Registered
            // as a hosted service so its DiagnosticListener.AllListeners subscription is active for
            // the app's lifetime — never registered when WithTransientFaultRetry() was not called.
            _services.AddHostedService<PersistenceRetryDiagnosticListener>();
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

        // Build effective configureDb action — wrap with compiled model and/or command timeout if supplied.
        Action<DbContextOptionsBuilder> effectiveConfigureDb = options =>
        {
            _configureDb(options);

            if (_compiledModel is not null)
                options.UseModel(_compiledModel);

            if (_commandTimeoutSeconds is not null)
            {
                // WO-053/P-337: DbContextOptionsBuilder has no provider-neutral CommandTimeout(...)
                // method of its own — CONFIRMED via direct compilation against the real EF Core 10.0.5
                // package (that method exists only as an INSTANCE member on the provider-specific
                // RelationalDbContextOptionsBuilder<TBuilder,TExtension> returned from
                // UseNpgsql(...)'s own configuration callback, unreachable here without an Npgsql
                // reference). The genuinely provider-neutral mechanism — verified end-to-end against a
                // real constructed DbContext, confirming Database.GetCommandTimeout() reflects it — is
                // to locate the already-registered RelationalOptionsExtension (the base type every
                // provider's own options extension derives from) via a covariant .OfType<T>() scan of
                // Options.Extensions (DbContextOptions.FindExtension<T>() requires an EXACT type match
                // and returns null for a base type), call its immutable-with WithCommandTimeout(...),
                // and re-register the updated extension via AddOrUpdateExtension. Never references any
                // Npgsql type by name.
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

        // Register DbContext using the caller-supplied options action.
        // Interceptors are wired via SharedKernelDbContext.OnConfiguring for the non-pooled path.
        if (_dbContextPoolingEnabled)
        {
            // WO-051/P-322: AddPooledDbContextFactory<TContext> registers IDbContextFactory<TContext>
            // as a singleton backed by an ObjectPool<TContext>. EF Core FREEZES the DbContextOptions
            // built here BEFORE any TContext instance is ever constructed from the pool — confirmed
            // empirically that SharedKernelDbContext.OnConfiguring's own interceptor-wiring attempt
            // throws "'OnConfiguring' cannot be used to modify DbContextOptions when DbContext
            // pooling is enabled" the moment the context's internal services are first built (e.g.
            // on EnsureCreatedAsync/SaveChangesAsync), because SharedKernelDbContext.OnConfiguring
            // guards its own mutation on `!optionsBuilder.Options.IsFrozen` (see that method's own
            // remarks) and therefore correctly does nothing further here. The platform three
            // interceptors (plus any additional consumer-supplied ones) MUST therefore be added here
            // instead, via the (IServiceProvider, DbContextOptionsBuilder) overload, BEFORE freezing.
            // Their own constructor-injected IUserContext is a throwaway NoOpUserContext — harmless,
            // because AuditInterceptor/SoftDeleteInterceptor read
            // ((SharedKernelDbContext)eventData.Context).CurrentUserContext LIVE at save time (see
            // those interceptors' own WO-051/P-322 remarks), never their own captured field, so one
            // shared interceptor instance safely serves the ENTIRE pool for its lifetime. IClock and
            // PersistenceServiceOptions ARE resolved for real from sp (the root provider — both are
            // effectively singleton-shared already under this domain's own conventions), so custom
            // registrations of either are honored.
            _services.AddPooledDbContextFactory<TContext>((sp, options) =>
            {
                effectiveConfigureDb(options);

                var clock = sp.GetRequiredService<IClock>();
                var serviceOptions = sp.GetRequiredService<IOptions<PersistenceServiceOptions>>();
                var placeholderUserContext = new NoOpUserContext();

                var interceptors = new List<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>
                {
                    new AuditInterceptor(placeholderUserContext, clock, serviceOptions),
                    new SoftDeleteInterceptor(placeholderUserContext, clock, serviceOptions),
                    new ConcurrencyInterceptor(sp.GetService<ILogger<ConcurrencyInterceptor>>()),
                };
                foreach (var interceptorType in _additionalInterceptorTypes)
                {
                    interceptors.Add((Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor)ActivatorUtilities
                        .CreateInstance(sp, interceptorType));
                }

                options.AddInterceptors(interceptors);
            }, _poolSize);

            // The scoped TContext factory delegate below is what existing consumer code injecting
            // TContext directly transparently rides on — it leases a (possibly reused, possibly
            // freshly-constructed) pooled instance, then refreshes its user/tenant context to THIS
            // scope's real values before returning it, so a reused pooled instance never
            // misattributes audit/tenant data to a prior, unrelated request.
            _services.AddScoped<TContext>(sp =>
            {
                var factory = sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<TContext>>();
                var context = factory.CreateDbContext();
                var userContext = sp.GetRequiredService<IUserContext>();

                if (context is TenantedDbContext tenantedContext)
                {
                    var tenantProvider = sp.GetRequiredService<ITenantProvider>();
                    tenantedContext.RefreshRequestContext(userContext, tenantProvider);
                }
                else
                {
                    context.RefreshUserContext(userContext);
                }

                return context;
            });
        }
        else
        {
            _services.AddDbContext<TContext>(effectiveConfigureDb);
        }

        // Register TContext also as the base SharedKernelDbContext so EfUnitOfWork resolves it.
        _services.AddScoped<SharedKernelDbContext>(sp => sp.GetRequiredService<TContext>());

        // WO-053/P-338: read-replica routing — opt-in via .WithReadReplica(...). Registers the
        // replica's DbContextOptions<TContext> as a keyed singleton, then a scoped
        // IReadReplicaContextAccessor<SharedKernelDbContext> that lazily constructs the replica
        // TContext instance (once per DI scope) via ActivatorUtilities.CreateInstance<TContext>,
        // reusing the current scope's own DI-resolved interceptor instances. Omitted entirely when
        // .WithReadReplica(...) was never called — every EfReadRepository read then targets the
        // single primary connection, provably unchanged.
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
        }
        else
        {
            // Standard non-transactional path.
            _services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        }

        // P-228: Opt-in registration of the same scoped EfUnitOfWork against
        // SharedKernel.Application.Behaviors.Transaction.IUnitOfWork.
        // Both registrations resolve the SAME scoped EfUnitOfWork instance per DI scope — NOT two
        // independent instances. The cast is safe because EfUnitOfWork implements both interfaces.
        if (_applicationTransactionBehaviorEnabled)
        {
            _services.AddScoped<AppBehaviorsIUnitOfWork>(sp =>
                (AppBehaviorsIUnitOfWork)sp.GetRequiredService<IUnitOfWork>());
        }

        // ISpecificationEvaluator<T> — singleton because SpecificationEvaluator<T> is stateless.
        _services.AddSingleton(typeof(ISpecificationEvaluator<>), typeof(SpecificationEvaluator<>));

        // No-op IUserContext placeholder — registered only when no other IUserContext is present.
        if (!_services.Any(sd => sd.ServiceType == typeof(IUserContext)))
        {
            _services.AddScoped<IUserContext, NoOpUserContext>();
        }

        // WO-071/P-457: default IAuditActorContext — registered only when .WithAuditTrail() was
        // called AND the consumer has not already registered their own IAuditActorContext.
        if (_auditTrailEnabled && !_services.Any(sd => sd.ServiceType == typeof(IAuditActorContext)))
        {
            _services.AddScoped<IAuditActorContext, EfCoreAuditActorContext>();
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

            // P-227: Eager startup check — ISymmetricEncryptionService must be resolvable.
            // The consuming service is responsible for calling AddSharedKernelCryptography().
            // This check fires at IServiceProvider build time (via IStartupFilter/BuildServiceProvider)
            // but we defer it to the first resolution via a validation-on-start pattern.
            // Register a startup validator that throws an actionable error if not resolvable.
            _services.AddOptions<EncryptionStartupOptions>().ValidateOnStart();
            _services.AddSingleton<IValidateOptions<EncryptionStartupOptions>>(sp =>
                new EncryptionStartupValidator(sp));

            // EncryptedEntityBatchProcessorRegistry<TContext> and EncryptionRotationService<TContext>
            // both require IDbContextFactory<TContext> — register it if WithDbContextFactory() was
            // not already called.
            if (!_registerFactory)
            {
                _services.AddDbContextFactory<TContext>(effectiveConfigureDb);
                _registerFactory = true;
            }

            // Singleton registry of batch processors, one per encrypted entity type, populated
            // lazily on first use from the model. WO-051/P-324: reworded from "reflection-free" —
            // EncryptedEntityBatchProcessorRegistry<TContext>'s own XML doc is the accurate framing:
            // this is a documented, justified, model-build-time-only exception to the SK0xxx
            // MakeGenericMethod/Invoke reflection-elimination rule (population uses
            // Activator.CreateInstance/MakeGenericType ONCE per entity type at startup), not a
            // claim that zero reflection ever occurs.
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
                new MigrationAndSeedHostedService<TContext>(
                    sp,
                    migrationsOnStartup,
                    seedSteps,
                    sp.GetService<ILogger<MigrationAndSeedHostedService<TContext>>>()));
        }

        return _services;
    }
}
