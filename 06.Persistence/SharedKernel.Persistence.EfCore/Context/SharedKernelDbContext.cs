using Microsoft.EntityFrameworkCore;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;

namespace SharedKernel.Persistence.EfCore.Context;

/// <summary>
/// Abstract EF Core DbContext base for all SharedKernel-derived data contexts.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Interceptor registration:</strong> the constructor registers exactly four
/// always-on <c>ISaveChangesInterceptor</c> instances — <see cref="AuditInterceptor"/>,
/// <see cref="SoftDeleteInterceptor"/>, <see cref="ConcurrencyInterceptor"/>, and
/// <see cref="AggregateRootTouchInterceptor"/> — via <c>DbContextOptionsBuilder.AddInterceptors</c>.
/// No <c>OutboxInterceptor</c> is registered here; the outbox infrastructure is MassTransit's
/// concern at the <c>07.Messaging</c> layer. <see cref="Interceptors.TenantWriteGuardInterceptor"/> is
/// NOT unconditional — it is registered as an additional interceptor only for multi-tenant services,
/// by <c>EfCorePersistenceBuilder.WithMultiTenancy()</c>. All of this wiring is reached through the
/// single required <see cref="PersistenceContextDependencies"/> constructor parameter — see that
/// type's own remarks for why it replaced nine individually named constructor parameters.
/// </para>
/// <para>
/// <strong>Extensibility:</strong> this class holds no compile-time reference to any
/// opt-in capability (field-level encryption, the audit trail). Sibling packages contribute to the
/// model and to <c>DbContextOptionsBuilder</c> through collections bundled into
/// <see cref="PersistenceContextDependencies"/>: <see cref="IPersistenceModelConventionFactory"/>
/// (model-finalizing convention contributions), <see cref="IPersistenceModelConfigurator"/> (extra
/// entity configurations, applied in <see cref="OnModelCreating"/>), and
/// <see cref="IPersistenceOptionsExtension"/> (<see cref="DbContextOptionsBuilder"/> mutations,
/// applied in <see cref="OnConfiguring"/> via <see cref="PersistenceContextDependencies.ApplyTo"/>). A
/// capability package registers its own implementation of whichever of these it needs — this class
/// never enumerates capabilities by name.
/// </para>
/// <para>
/// <strong>Model building:</strong> <see cref="OnModelCreating"/> calls
/// <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/> for the calling (concrete) context's
/// assembly, automatically discovering all <c>IEntityTypeConfiguration&lt;T&gt;</c> implementations,
/// then applies every registered <see cref="IPersistenceModelConfigurator"/>.
/// Downstream contexts must call <c>base.OnModelCreating(modelBuilder)</c> first if they override
/// this method.
/// </para>
/// <para>
/// <strong>Save boundary:</strong> <see cref="SaveChangesAsync(CancellationToken)"/> is the
/// delegate used by <c>EfUnitOfWork</c>. Never call it directly from application or domain code —
/// always go through <c>IUnitOfWork.SaveChangesAsync</c>.
/// </para>
/// <para>
/// Concrete downstream contexts extend this base and add their <c>DbSet&lt;T&gt;</c> properties.
/// Multi-tenant contexts extend <see cref="SharedKernel.Persistence.EfCore.MultiTenancy.TenantedDbContext"/>
/// instead.
/// </para>
/// </remarks>
public abstract class SharedKernelDbContext : DbContext
{
    private readonly PersistenceContextDependencies _dependencies;
    private readonly IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.Conventions.IConvention> _additionalModelConventions;

    /// <summary>
    /// Initialises a new <see cref="SharedKernelDbContext"/> and registers the platform's always-on
    /// interceptors plus every capability <paramref name="dependencies"/> carries.
    /// </summary>
    /// <param name="options">EF Core context options supplied by the DI container.</param>
    /// <param name="dependencies">
    /// Every interceptor/convention/configurator/options-extension/exception-classifier dependency this
    /// context needs, bundled into one required parameter. A derived context MUST declare exactly
    /// <c>MyContext(DbContextOptions&lt;MyContext&gt; options, PersistenceContextDependencies dependencies)
    /// : base(options, dependencies)</c> and forward both parameters unchanged — see
    /// <see cref="PersistenceContextDependencies"/>'s own remarks for why a shorter, hand-written
    /// constructor is unsafe.
    /// </param>
    protected SharedKernelDbContext(DbContextOptions options, PersistenceContextDependencies dependencies)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _dependencies = dependencies;
        _additionalModelConventions = dependencies.ModelConventionFactories
            .Select(factory => factory.CreateConvention(this, options))
            .ToList();

        // Starts from the request context AuditInterceptor was constructed with — the current DI
        // scope's IRequestContext under the default (non-pooled) registration, the fail-closed
        // AnonymousRequestContext for a pooled slot. Every supported way of obtaining an instance
        // (TenantAwareDbContextFactory) attaches the current scope's context before handing it out.
        RequestContext = dependencies.AuditInterceptor.RequestContext;
    }

    /// <summary>
    /// Gets the caller this context attributes changes to (audit columns, soft-delete stamps) and, for
    /// a <see cref="MultiTenancy.TenantedDbContext"/>, filters tenant data by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attached per DI-scope lease by <c>TenantAwareDbContextFactory&lt;TContext&gt;</c> — never
    /// resolved through this class's constructor, which is what keeps pooling safe: a pooled
    /// instance's constructor runs once per pool slot, not once per lease. The interceptors read this
    /// property live off the executing context. Reset to <see cref="AnonymousRequestContext"/> on
    /// <see cref="Dispose"/>/<see cref="DisposeAsync"/>.
    /// </para>
    /// </remarks>
    public IRequestContext RequestContext { get; private set; }

    /// <summary>
    /// Gets the identifier written to <c>CreatedBy</c>/<c>ModifiedBy</c>/<c>DeletedBy</c>: the
    /// caller's <see cref="IRequestContext.UserId"/>, or the configured service name
    /// (<c>PersistenceServiceOptions.ServiceName</c>) when there is none.
    /// </summary>
    internal string CurrentActorId =>
        RequestContext.UserId is { Length: > 0 } userId ? userId : _dependencies.AuditInterceptor.ServiceName;

    /// <summary>Replaces <see cref="RequestContext"/> with <paramref name="requestContext"/>.</summary>
    /// <param name="requestContext">The current scope's <see cref="IRequestContext"/>.</param>
    /// <remarks>
    /// <see langword="internal"/> — called only by this assembly's own registrations
    /// (<c>TenantAwareDbContextFactory</c>, the read-replica accessor). Not a public extensibility seam.
    /// </remarks>
    internal void RefreshRequestContext(IRequestContext requestContext) =>
        RequestContext = requestContext ?? AnonymousRequestContext.Instance;

    /// <inheritdoc />
    /// <remarks>
    /// Resets <see cref="RequestContext"/> to the fail-closed <see cref="AnonymousRequestContext"/>
    /// before disposing — the hook a pooled instance passes through when it returns to the pool, so a
    /// later lease can never observe the previous caller.
    /// </remarks>
    public override void Dispose()
    {
        RequestContext = AnonymousRequestContext.Instance;
        base.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>See <see cref="Dispose"/>.</remarks>
    public override async ValueTask DisposeAsync()
    {
        RequestContext = AnonymousRequestContext.Instance;
        await base.DisposeAsync();
    }

    /// <summary>Gets the clock this context's audit interceptor uses, attached to every aggregate it materializes.</summary>
    /// <seealso cref="DomainClockMaterializationInterceptor"/>
    internal SharedKernel.Primitives.Clocks.IClock Clock => _dependencies.AuditInterceptor.Clock;

    /// <inheritdoc />
    /// <remarks>
    /// <strong>Pooling guard:</strong> when <c>optionsBuilder.Options.IsFrozen</c> is
    /// <see langword="true"/> — which EF Core sets for every instance constructed via
    /// <c>EfCorePersistenceBuilder.WithDbContextPooling()</c>'s
    /// <c>AddPooledDbContextFactory&lt;TContext&gt;</c> registration, confirmed empirically to be
    /// <see langword="true"/> even on the very FIRST pool-miss construction — any attempt to mutate
    /// <paramref name="optionsBuilder"/> here throws <see cref="InvalidOperationException"/>
    /// ("'OnConfiguring' cannot be used to modify DbContextOptions when DbContext pooling is
    /// enabled.") the first time the context's internal services are built (e.g., on
    /// <c>SaveChangesAsync</c> or <c>EnsureCreatedAsync</c>), not immediately at the mutation call
    /// site itself. <see cref="EfCorePersistenceBuilder{TContext}.WithDbContextPooling"/>'s pooled
    /// registration therefore calls the SAME <see cref="PersistenceContextDependencies.ApplyTo"/>
    /// this method calls, directly against the pool's own <c>optionsAction</c> — BEFORE freezing —
    /// so this method correctly does nothing extra for a pooled context; for a non-pooled context
    /// (<c>Options.IsFrozen == false</c>), this method performs the wiring exactly as before. Calling
    /// the identical method from both paths (rather than two independently-maintained copies of the
    /// same interceptor/options-extension wiring) is what makes the two paths structurally incapable
    /// of drifting apart.
    /// </remarks>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.Options.IsFrozen)
            _dependencies.ApplyTo(optionsBuilder);

        base.OnConfiguring(optionsBuilder);
    }

    /// <summary>
    /// Applies every <c>IEntityTypeConfiguration&lt;T&gt;</c> in the concrete context's assembly whose
    /// entity type this context actually exposes, then every registered
    /// <see cref="IPersistenceModelConfigurator"/>.
    /// </summary>
    /// <param name="modelBuilder">The builder used to construct the model for this context.</param>
    /// <remarks>
    /// <para>
    /// Downstream contexts that override this method must call
    /// <c>base.OnModelCreating(modelBuilder)</c> first to ensure configurations are applied.
    /// </para>
    /// <para>
    /// <strong>Scoped assembly scan:</strong> <c>ApplyConfigurationsFromAssembly</c> is called
    /// with a predicate that only lets a discovered <c>IEntityTypeConfiguration&lt;T&gt;</c> through
    /// when <c>T</c> is a type this context actually reaches — either one of its own public
    /// <c>DbSet&lt;T&gt;</c> properties, a type EF Core's own navigation-discovery has ALREADY added to
    /// the model by the time this method runs (a child entity type reachable only through a navigation
    /// from a <c>DbSet&lt;T&gt;</c> root — <c>DbSet&lt;T&gt;</c> auto-discovery and the convention
    /// pipeline that follows a root type's own navigations both run before <c>OnModelCreating</c>'s
    /// body starts, so <c>modelBuilder.Model.GetEntityTypes()</c> already lists such a child here), or
    /// <see cref="AdditionalConfiguredEntityTypes"/> (a true manual escape hatch — a type reachable
    /// neither way, e.g. one this context configures via an explicit <c>ComplexProperty</c>/<c>OwnsOne</c>
    /// call inside <c>OnModelCreating</c> itself, after this predicate has already run). When two or
    /// more <c>SharedKernelDbContext</c> subclasses live in the SAME assembly (a multi-context service),
    /// a configuration for an entity type only the OTHER context reaches is never applied to this one —
    /// preserving the bleed fix the scoped scan exists for, while no longer dropping a navigation-only
    /// child's own configuration (including a <c>.Encrypt(...)</c> annotation — silently losing that one
    /// is a silent-plaintext hazard, not merely a missing column length). A candidate type that does not
    /// implement <c>IEntityTypeConfiguration&lt;T&gt;</c> at all (an unrelated type the scan also
    /// visits) is let through unfiltered — <c>ApplyConfigurationsFromAssembly</c> already ignores it
    /// internally, so the predicate has nothing meaningful to decide for it.
    /// </para>
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            GetType().Assembly, candidateType => IsConfigurationEntityTypeExposed(candidateType, modelBuilder));

        // Opt-in capabilities (e.g. the audit trail) apply extra configurations that live
        // in THEIR OWN assembly, not the downstream concrete context's — the assembly scan above
        // never discovers them. See IPersistenceModelConfigurator.
        foreach (var configurator in _dependencies.ModelConfigurators)
            configurator.Configure(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Entity types this context configures beyond what its own public <c>DbSet&lt;T&gt;</c>
    /// properties, and EF Core's own navigation discovery from them, already expose — a TRUE manual
    /// escape hatch for a type reachable neither way (e.g. one this context itself only reaches via an
    /// explicit <c>ComplexProperty</c>/<c>OwnsOne</c> call made later, inside <c>OnModelCreating</c>).
    /// Empty by default.
    /// </summary>
    /// <remarks>
    /// The explicit opt-in half of <see cref="OnModelCreating"/>'s scoped assembly scan — a
    /// context that needs a configuration applied for a type neither a <c>DbSet&lt;T&gt;</c> nor a
    /// navigation from one already reaches overrides this property instead of losing the scan's scoping
    /// altogether. Most navigation-reachable child entity types need NO entry here at all — see
    /// <see cref="OnModelCreating"/>'s own remarks for why.
    /// </remarks>
    protected virtual IEnumerable<Type> AdditionalConfiguredEntityTypes => [];

    // Predicate for ApplyConfigurationsFromAssembly: true for any candidate type that either
    // does not implement IEntityTypeConfiguration<T> at all (EF Core ignores it either way — nothing
    // for this predicate to meaningfully decide) or does, and T is exposed by this context's own
    // DbSet<T> properties, a type EF Core's navigation discovery already reached from one of them, or
    // AdditionalConfiguredEntityTypes.
    private bool IsConfigurationEntityTypeExposed(Type candidateType, ModelBuilder modelBuilder)
    {
        var configuredEntityType = candidateType
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault();

        if (configuredEntityType is null)
            return true;

        return ExposedEntityTypes(modelBuilder).Contains(configuredEntityType);
    }

    private HashSet<Type>? _exposedEntityTypesCache;

    // Reads the model's ALREADY-DISCOVERED entity types directly — EF Core's own authoritative
    // navigation-discovery convention, not a hand-rolled reflection walk over CLR properties that would
    // have to independently reinvent which navigations EF Core itself would traverse (collections vs.
    // references, owned vs. regular, ignored properties, and so on). Cached per context INSTANCE, never
    // per TYPE: unlike the DbSet-only set this replaced, "what the model currently contains" can only
    // be read once conventions have actually run for this specific ModelBuilder.
    private HashSet<Type> ExposedEntityTypes(ModelBuilder modelBuilder) =>
        _exposedEntityTypesCache ??= modelBuilder.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Concat(AdditionalConfiguredEntityTypes)
            .ToHashSet();

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <strong>Concurrency-conflict translation:</strong> wraps the base
    /// save call so <see cref="ConcurrencyInterceptor.TryTranslate"/> can convert a
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> affecting an
    /// <see cref="SharedKernel.Domain.Abstractions.IHasConcurrency"/> entity into a
    /// <see cref="SharedKernel.Core.Exceptions.ConflictException"/>. This is the actual enforcement
    /// point for that translation — EF Core 10 does not allow
    /// <c>ISaveChangesInterceptor.SaveChangesFailed</c>/<c>SaveChangesFailedAsync</c> to replace the
    /// exception propagating from <c>SaveChanges</c> (confirmed empirically; see
    /// <see cref="ConcurrencyInterceptor"/>'s class remarks for the full story). The exception
    /// filter (<c>when (... is { } conflict)</c>) means a non-matching exception is never caught
    /// here at all — it propagates with its original stack trace fully intact, identical to
    /// today's behavior for every exception this translation does not apply to.
    /// </para>
    /// <para>
    /// <strong>DbUpdateException classification:</strong> a non-concurrency
    /// <see cref="DbUpdateException"/> (a unique-constraint or foreign-key violation,...) is offered
    /// to every registered <see cref="IDbUpdateExceptionClassifier"/>, in registration order; the
    /// first non-<see langword="null"/> result replaces it. See
    /// <see cref="IDbUpdateExceptionClassifier"/>.
    /// </para>
    /// </remarks>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException ex) when (_dependencies.ConcurrencyInterceptor.TryTranslate(ex) is { } translated)
        {
            throw translated;
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && TryClassify(ex) is { } classified)
        {
            throw classified;
        }
    }

    /// <inheritdoc />
    /// <remarks>See <see cref="SaveChanges(bool)"/> for the concurrency-conflict and
    /// <see cref="DbUpdateException"/> classification this override performs.</remarks>
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex) when (_dependencies.ConcurrencyInterceptor.TryTranslate(ex) is { } translated)
        {
            throw translated;
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && TryClassify(ex) is { } classified)
        {
            throw classified;
        }
    }

    // Offers a non-concurrency DbUpdateException to every registered IDbUpdateExceptionClassifier,
    // in registration order, returning the first non-null translation.
    private Exception? TryClassify(DbUpdateException exception)
    {
        foreach (var classifier in _dependencies.ExceptionClassifiers)
        {
            if (classifier.TryClassify(exception) is { } classified)
                return classified;
        }

        return null;
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new SoftDeleteQueryFilterConvention());

        foreach (var convention in _additionalModelConventions)
            configurationBuilder.Conventions.Add(_ => convention);

        // Always last: fails the model build loudly if a '.Encrypt(...)' annotation survived every
        // IPersistenceModelConventionFactory contribution above unconsumed — see the convention's own remarks.
        configurationBuilder.Conventions.Add(_ => new EncryptAnnotationRegisteredGuardConvention());

        base.ConfigureConventions(configurationBuilder);
    }
}
