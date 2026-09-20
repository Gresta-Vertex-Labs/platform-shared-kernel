using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.Abstractions.Context;
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

        // Initialised from AuditInterceptor's own
        // constructor-captured ICurrentActorContext — deliberately NOT a new constructor parameter on
        // this class (it is already reachable through dependencies.AuditInterceptor). Under the
        // default, non-pooled registration this is the correct value for this instance's entire
        // lifetime. Under.WithDbContextPooling(), RefreshActor(...) is called once per lease to
        // replace it with the current scope's real ICurrentActorContext.
        CurrentActor = dependencies.AuditInterceptor.ActorContext;
    }

    /// <summary>
    /// Gets the <see cref="ICurrentActorContext"/> that <see cref="AuditInterceptor"/> and
    /// <see cref="SoftDeleteInterceptor"/> resolve actor identity from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Under the default (non-pooled) registration this is set once at construction — from
    /// <see cref="AuditInterceptor.ActorContext"/> — and never changes for this instance's lifetime,
    /// which is already correct because a fresh <see cref="SharedKernelDbContext"/> instance is
    /// constructed per DI scope.
    /// </para>
    /// <para>
    /// <strong>Pooling:</strong> under
    /// <c>EfCorePersistenceBuilder.WithDbContextPooling()</c>, a pooled instance's constructor runs
    /// ONCE per pooled slot, not once per lease. <see cref="AuditInterceptor"/>/
    /// <see cref="SoftDeleteInterceptor"/> read this property LIVE off
    /// <c>eventData.Context</c> inside <c>SavingChanges</c>/<c>SavingChangesAsync</c> — always the
    /// CURRENT executing instance — instead of their own constructor-captured field, so calling
    /// <see cref="RefreshActor"/> once per lease keeps audit attribution correct across
    /// unrelated requests reusing the same pooled instance.
    /// </para>
    /// </remarks>
    public ICurrentActorContext CurrentActor { get; private set; }

    /// <summary>
    /// Replaces <see cref="CurrentActor"/> with <paramref name="actorContext"/>.
    /// </summary>
    /// <param name="actorContext">The current scope's real <see cref="ICurrentActorContext"/>.</param>
    /// <remarks>
    /// <see langword="internal"/> — called only by
    /// <c>EfCorePersistenceBuilder.WithDbContextPooling()</c>'s own factory delegate, within this
    /// same assembly. Not a public extensibility seam.
    /// </remarks>
    internal void RefreshActor(ICurrentActorContext actorContext) => CurrentActor = actorContext;

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
    /// when <c>T</c> is a type this context exposes — either via one of its own public
    /// <c>DbSet&lt;T&gt;</c> properties, or via <see cref="AdditionalConfiguredEntityTypes"/>. When two
    /// or more <c>SharedKernelDbContext</c> subclasses live in the SAME assembly (a multi-context
    /// service), a configuration for an entity type only the OTHER context exposes is never applied to
    /// this one — closing the bleed the unscoped scan used to allow (every configuration in the
    /// assembly reaching every context, silently inert only because EF Core ignores a configuration for
    /// an entity type never reachable from the model, not because nothing happened). A candidate type
    /// that does not implement <c>IEntityTypeConfiguration&lt;T&gt;</c> at all (an unrelated type the
    /// scan also visits) is let through unfiltered — <c>ApplyConfigurationsFromAssembly</c> already
    /// ignores it internally, so the predicate has nothing meaningful to decide for it.
    /// </para>
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly, IsConfigurationEntityTypeExposed);

        // Opt-in capabilities (e.g. the audit trail) apply extra configurations that live
        // in THEIR OWN assembly, not the downstream concrete context's — the assembly scan above
        // never discovers them. See IPersistenceModelConfigurator.
        foreach (var configurator in _dependencies.ModelConfigurators)
            configurator.Configure(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Entity types this context configures beyond what its own public <c>DbSet&lt;T&gt;</c>
    /// properties already expose — e.g. a child entity type reachable only through a navigation, with
    /// its own dedicated <c>IEntityTypeConfiguration&lt;T&gt;</c> but no <c>DbSet&lt;T&gt;</c> of its
    /// own on this context. Empty by default.
    /// </summary>
    /// <remarks>
    /// The explicit opt-in half of <see cref="OnModelCreating"/>'s scoped assembly scan — a
    /// context that needs a configuration applied for a non-DbSet entity type overrides this property
    /// instead of losing the scan's scoping altogether.
    /// </remarks>
    protected virtual IEnumerable<Type> AdditionalConfiguredEntityTypes => [];

    // Predicate for ApplyConfigurationsFromAssembly: true for any candidate type that either
    // does not implement IEntityTypeConfiguration<T> at all (EF Core ignores it either way — nothing
    // for this predicate to meaningfully decide) or does, and T is exposed by this context's own
    // DbSet<T> properties or AdditionalConfiguredEntityTypes.
    private bool IsConfigurationEntityTypeExposed(Type candidateType)
    {
        var configuredEntityType = candidateType
            .GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
                    .Select(i => i.GetGenericArguments()[0])
                        .FirstOrDefault();

        if (configuredEntityType is null)
            return true;

        return ExposedEntityTypes.Contains(configuredEntityType);
    }

    private HashSet<Type>? _exposedEntityTypesCache;

    private HashSet<Type> ExposedEntityTypes =>
        _exposedEntityTypesCache ??= GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                    .Select(p => p.PropertyType.GetGenericArguments()[0])
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
