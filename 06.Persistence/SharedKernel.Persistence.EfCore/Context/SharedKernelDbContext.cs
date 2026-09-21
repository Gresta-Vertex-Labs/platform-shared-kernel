using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Context;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Context;

/// <summary>
/// Base class of every SharedKernel EF Core context. PostgreSQL-first: registered with
/// <c>AddSharedKernelPostgres&lt;TContext&gt;(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What the base does for every derived context</strong>, with no configuration class and no base
/// entity configuration:
/// </para>
/// <list type="bullet">
/// <item><description>applies every <c>IEntityTypeConfiguration&lt;T&gt;</c> in the context's assembly
/// (<see cref="ShouldApplyConfiguration"/> filters them when several contexts share an assembly);</description></item>
/// <item><description>maps every <c>StronglyTypedId&lt;T&gt;</c> reachable from the context's <c>DbSet</c>s,
/// and <c>Money</c> as a two-column complex type;</description></item>
/// <item><description>configures audit, soft-delete and tenant columns by the interfaces an entity implements
/// (<c>CreatedBy</c>/<c>CreatedOn</c> are written once and never updated afterwards), the named soft-delete
/// query filter and, on PostgreSQL, the <c>xmin</c> concurrency token of every aggregate root;</description></item>
/// <item><description>stamps audit fields, turns deletes of soft-deletable entities into soft deletes, marks an
/// aggregate root modified when a child changed, and (for a <see cref="MultiTenancy.TenantedDbContext"/>)
/// rejects writes outside the current tenant — in one interceptor, with one change-detection pass;</description></item>
/// <item><description>dispatches domain events raised by tracked aggregates before every asynchronous save,
/// whichever code path calls it;</description></item>
/// <item><description>turns every optimistic-concurrency failure into a <c>ConflictException</c> and PostgreSQL
/// constraint errors into typed exceptions.</description></item>
/// </list>
/// <para>
/// A derived context declares exactly one constructor and forwards both parameters:
/// <c>MyContext(DbContextOptions&lt;MyContext&gt; options, PersistenceContextDependencies dependencies) : base(options, dependencies)</c>.
/// A context that overrides <see cref="OnModelCreating"/> or <see cref="ConfigureConventions"/> calls the base first.
/// </para>
/// </remarks>
public abstract class SharedKernelDbContext : DbContext
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.Assembly, bool> AssembliesWithConfigurations = new();

    private readonly PersistenceContextDependencies _dependencies;
    private readonly DbContextOptions _options;
    private readonly ILogger _logger;

    /// <summary>Initialises the context.</summary>
    /// <param name="options">EF Core options supplied by the registration.</param>
    /// <param name="dependencies">Platform dependencies supplied by the registration (or <see cref="PersistenceContextDependencies.Create"/>).</param>
    protected SharedKernelDbContext(DbContextOptions options, PersistenceContextDependencies dependencies)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _dependencies = dependencies;
        _options = options;
        _logger = dependencies.LoggerFactory.CreateLogger(GetType());
        RequestContext = dependencies.InitialRequestContext;
        DomainEventDispatcher = dependencies.DefaultDomainEventDispatcher;
        CrossTenantScope = NewCrossTenantScope(RequestContext);
    }

    /// <summary>
    /// Gets the caller this context attributes changes to and, for a
    /// <see cref="MultiTenancy.TenantedDbContext"/>, filters tenant data by.
    /// </summary>
    /// <remarks>
    /// Attached each time the registration hands the context out (from the resolving scope's
    /// <see cref="IRequestContext"/>, or the caller passed to <c>ICallerDbContextFactory</c>), never through
    /// the constructor, which keeps pooling safe. Reset to <see cref="AnonymousRequestContext"/> on dispose.
    /// </remarks>
    public IRequestContext RequestContext { get; private set; }

    /// <summary>
    /// Gets the cross-tenant bypass this context honours: the resolving scope's <see cref="ICrossTenantScope"/>
    /// or, for a context created through <see cref="ICallerDbContextFactory{TContext}"/>, one of its own attributed
    /// to that caller (<c>using (db.CrossTenantScope.Enter(reason)) { ... }</c>). The tenant write guard and
    /// row-level security read it on every save and command.
    /// </summary>
    public ICrossTenantScope CrossTenantScope { get; private set; }

    /// <summary>The dispatcher that receives domain events before each save, or <see langword="null"/>.</summary>
    internal IDomainEventDispatcher? DomainEventDispatcher { get; private set; }

    /// <summary>The platform dependencies this context was built with.</summary>
    internal PersistenceContextDependencies Dependencies => _dependencies;

    /// <summary>The actor written to audit columns: the caller's user id, else the configured service name.</summary>
    internal string CurrentActorId =>
        RequestContext.UserId is { Length: > 0 } userId ? userId : _dependencies.ServiceName;

    /// <summary>The clock for audit stamps and materialized aggregates.</summary>
    internal IClock Clock => _dependencies.Clock;

    /// <summary>
    /// Attaches the caller, the domain-event dispatcher and the cross-tenant scope for the current lease. Without a
    /// scope the context gets a new, inactive one of its own, attributed to the caller.
    /// </summary>
    internal void AttachLease(
        IRequestContext requestContext, IDomainEventDispatcher? domainEventDispatcher, ICrossTenantScope? crossTenantScope)
    {
        RequestContext = requestContext ?? AnonymousRequestContext.Instance;
        DomainEventDispatcher = domainEventDispatcher;
        CrossTenantScope = crossTenantScope ?? NewCrossTenantScope(RequestContext);
    }

    /// <summary>Replaces the caller and gives the context a new, inactive cross-tenant scope for it (tests).</summary>
    internal void RefreshRequestContext(IRequestContext requestContext)
    {
        RequestContext = requestContext ?? AnonymousRequestContext.Instance;
        CrossTenantScope = NewCrossTenantScope(RequestContext);
    }

    private CrossTenantScope NewCrossTenantScope(IRequestContext requestContext) =>
        new(requestContext, _dependencies.LoggerFactory.CreateLogger<CrossTenantScope>());

    /// <inheritdoc />
    /// <remarks>Resets the caller and dispatcher first, so a pooled instance never carries them into its next lease.</remarks>
    public override void Dispose()
    {
        ResetLease();
        base.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>See <see cref="Dispose"/>.</remarks>
    public override async ValueTask DisposeAsync()
    {
        ResetLease();
        await base.DisposeAsync();
    }

    private void ResetLease()
    {
        RequestContext = AnonymousRequestContext.Instance;
        DomainEventDispatcher = _dependencies.DefaultDomainEventDispatcher;
        CrossTenantScope = NewCrossTenantScope(RequestContext);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Adds the platform interceptors and capability contributions. A pooled context's options are frozen
    /// before this runs, so the pooled factory applies the same contributions itself.
    /// </remarks>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.Options.IsFrozen)
            _dependencies.ApplyTo(optionsBuilder);

        base.OnConfiguring(optionsBuilder);
    }

    /// <summary>
    /// Decides whether an <c>IEntityTypeConfiguration&lt;T&gt;</c> found in this context's assembly is applied.
    /// Returns <see langword="true"/> for every configuration.
    /// </summary>
    /// <param name="configurationType">The configuration class found by the assembly scan.</param>
    /// <returns><see langword="true"/> to apply it.</returns>
    /// <remarks>
    /// Override it when two or more contexts live in the same assembly: applying a configuration adds its
    /// entity type to the model, so without a filter every context would contain every entity. Filter by
    /// namespace or by a marker, not by "types this context already exposes" — that set is incomplete while
    /// the model is being built and silently drops configurations (including <c>.Encrypt()</c>).
    /// </remarks>
    protected virtual bool ShouldApplyConfiguration(Type configurationType) => true;

    /// <inheritdoc />
    /// <remarks>Derived contexts call <c>base.OnModelCreating(modelBuilder)</c> first.</remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Only when the assembly has configurations: EF Core warns (NoEntityTypeConfigurationsWarning) on every model
        // build of a context whose entities are configured in OnModelCreating alone.
        if (HasEntityTypeConfigurations(GetType().Assembly))
            modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly, ShouldApplyConfiguration);

        foreach (var configurator in _dependencies.ModelConfigurators)
            configurator.Configure(modelBuilder);

        MoneyMapping.Apply(modelBuilder);

        if (_dependencies.KeyGenerator is not null)
            ClientKeyGeneration.Apply(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    private static bool HasEntityTypeConfigurations(System.Reflection.Assembly assembly) =>
        AssembliesWithConfigurations.GetOrAdd(assembly, static a =>
        {
            Type?[] types;
            try
            {
                types = a.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            return types.Any(t => t is { IsAbstract: false, IsGenericTypeDefinition: false }
                && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)));
        });

    /// <inheritdoc />
    /// <remarks>
    /// Registers the platform conventions: strongly-typed id and <c>Money</c> mappings, audit/soft-delete/tenant
    /// column rules, the soft-delete query filter, capability conventions, and last the guard that fails the
    /// model build when an <c>.Encrypt()</c> annotation was left unhandled. Derived contexts call the base first.
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        DomainTypeMappings.Apply(configurationBuilder, GetType());

        configurationBuilder.Conventions.Add(_ => new SoftDeleteQueryFilterConvention());
        configurationBuilder.Conventions.Add(_ => new DomainColumnConvention());

        // Created here, not in the constructor: this runs once per model build, the constructor once per instance.
        foreach (var factory in _dependencies.ModelConventionFactories)
        {
            var convention = factory.CreateConvention(this, _options);
            configurationBuilder.Conventions.Add(_ => convention);
        }

        configurationBuilder.Conventions.Add(_ => new EncryptAnnotationRegisteredGuardConvention());

        base.ConfigureConventions(configurationBuilder);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Synchronous saves cannot dispatch domain events (dispatch is asynchronous). When tracked aggregates hold
    /// events and a dispatcher is attached this throws <see cref="InvalidOperationException"/>: use
    /// <c>SaveChangesAsync</c>. Without a dispatcher the events are discarded with a warning.
    /// </para>
    /// <para>
    /// A concurrency failure becomes a <c>ConflictException</c> — also when the row belongs to another tenant, so the
    /// answer never reveals that it exists; other database errors are offered to the registered classifiers.
    /// </para>
    /// </remarks>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (DomainEventDispatcher is not null && DomainEventDispatchLoop.HasPendingEvents(this))
        {
            throw new InvalidOperationException(
                "Tracked aggregates have pending domain events, which can only be dispatched asynchronously. " +
                "Call SaveChangesAsync (or IUnitOfWork.SaveChangesAsync) instead of SaveChanges.");
        }

        DomainEventDispatchLoop.DiscardWithoutDispatcher(this, _logger);

        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw ConcurrencyConflictTranslator.Translate(this, ex, _logger);
        }
        catch (DbUpdateException ex) when (TryClassify(ex) is { } classified)
        {
            throw classified;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Dispatches domain events until none are pending (a handler may raise more, or change more tracked
    /// entities), then saves everything as one unit. Handlers therefore run before the commit: they must only
    /// change data in this context; external effects belong after the commit (<c>ICommandScope.OnCompleted</c>)
    /// or in an outbox. See <see cref="SaveChanges(bool)"/> for exception translation.
    /// <para>
    /// When a domain-event handler throws, nothing is saved and the change tracker is cleared: the dispatched events
    /// are no longer on their aggregates, so saving the remaining changes later would persist them without their
    /// events. Reload and retry the whole operation.
    /// </para>
    /// </remarks>
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await DomainEventDispatchLoop.RunAsync(this, DomainEventDispatcher, _logger, cancellationToken);
        }
        catch
        {
            // The events were taken off their aggregates before dispatch; a later save would persist the changes without
            // them. Abandon the whole save instead: nothing was written, and nothing can be half-applied later.
            ChangeTracker.Clear();
            throw;
        }

        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw await ConcurrencyConflictTranslator.TranslateAsync(this, ex, _logger, cancellationToken);
        }
        catch (DbUpdateException ex) when (TryClassify(ex) is { } classified)
        {
            throw classified;
        }
    }

    private bool IsRetriedByExecutionStrategy(DbUpdateException exception)
    {
        // Configured retry, not CreateExecutionStrategy().RetriesOnFailure: inside a running strategy (the unit of
        // work's transaction) EF suspends nested strategies, which would report no retry here.
        if (this.GetService<IDbContextOptions>()
                .FindExtension<PostgresConventionsOptionsExtension>()?.MaxRetryCount is not > 0)
        {
            return false;
        }

        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is global::Npgsql.NpgsqlException { IsTransient: true })
                return true;
        }

        return false;
    }

    private Exception? TryClassify(DbUpdateException exception)
    {
        if (exception is DbUpdateConcurrencyException)
            return null;

        // A transient failure the execution strategy will retry must reach it unwrapped: the strategy only
        // recognizes the provider exception, so classifying it here would turn every retry into a failure.
        if (IsRetriedByExecutionStrategy(exception))
            return null;

        foreach (var classifier in _dependencies.ExceptionClassifiers)
        {
            if (classifier.TryClassify(exception) is { } classified)
                return classified;
        }

        return null;
    }
}

