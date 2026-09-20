using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;

namespace SharedKernel.Persistence.EfCore.Context;

/// <summary>
/// Bundles every dependency <see cref="SharedKernelDbContext"/> and
/// <see cref="MultiTenancy.TenantedDbContext"/> need, so a derived context's constructor takes exactly
/// one required parameter to forward to its base constructor instead of nine (four required, five
/// optional) individually named ones.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this type exists:</strong> before it did, a derived context that declared its own,
/// shorter constructor — for example <c>MyContext(DbContextOptions&lt;MyContext&gt; options,
/// AuditInterceptor a, SoftDeleteInterceptor s, ConcurrencyInterceptor c) : base(options, a, s, c)</c>,
/// omitting the five optional trailing collection parameters — compiled cleanly and ran without any
/// error, yet silently lost every opt-in capability threaded through those collections: the tenant
/// write guard (<c>.WithMultiTenancy()</c>), row-level-security connection binding
/// (<c>.WithRowLevelSecurity()</c>), the audit-record mutation guard (<c>.WithAuditTrail()</c>), field
/// encryption (<c>.WithEncryption()</c>), and any registered <see cref="IDbUpdateExceptionClassifier"/>.
/// Reads stayed tenant-filtered (the query filter is installed at model-build time, not through this
/// object), which made the gap easy to miss — only writes and database-level protections were
/// unprotected. A single required parameter of this type structurally forecloses that mistake: nothing
/// about it can be "forgotten" the way one collection among nine optional-looking parameters could be.
/// </para>
/// <para>
/// Constructed and registered internally by <c>EfCorePersistenceExtensions</c> — application code never
/// constructs this type through dependency injection. A hand-built context (e.g. in a unit test) may
/// construct it directly with <see cref="PersistenceContextDependencies(AuditInterceptor,
/// SoftDeleteInterceptor, ConcurrencyInterceptor, IEnumerable{ISaveChangesInterceptor},
/// IEnumerable{IPersistenceModelConventionFactory}, IEnumerable{IPersistenceModelConfigurator},
/// IEnumerable{IPersistenceOptionsExtension}, IEnumerable{IDbUpdateExceptionClassifier})"/>.
/// </para>
/// </remarks>
public sealed class PersistenceContextDependencies
{
    /// <summary>Initialises a new <see cref="PersistenceContextDependencies"/>.</summary>
    /// <param name="auditInterceptor">Scoped interceptor that populates audit fields.</param>
    /// <param name="softDeleteInterceptor">Scoped interceptor that converts deletes to soft-deletes.</param>
    /// <param name="concurrencyInterceptor">Interceptor that wraps concurrency exceptions.</param>
    /// <param name="additionalInterceptors">
    /// Optional consumer-supplied interceptors, including the tenant write guard registered by
    /// <c>.WithMultiTenancy()</c>. Platform interceptors always fire before these.
    /// </param>
    /// <param name="modelConventionFactories">
    /// Optional model-finalizing convention contributions from opt-in capability packages (e.g. field
    /// encryption). See <see cref="IPersistenceModelConventionFactory"/>.
    /// </param>
    /// <param name="modelConfigurators">
    /// Optional extra entity-configuration contributions from opt-in capability packages (e.g. the
    /// audit trail). See <see cref="IPersistenceModelConfigurator"/>.
    /// </param>
    /// <param name="optionsExtensions">
    /// Optional <see cref="DbContextOptionsBuilder"/> mutation contributions from opt-in capability
    /// packages (e.g. row-level security, field encryption). See <see cref="IPersistenceOptionsExtension"/>.
    /// </param>
    /// <param name="exceptionClassifiers">
    /// Optional provider-specific <see cref="DbUpdateException"/> classifiers.
    /// </param>
    public PersistenceContextDependencies(
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor,
        IEnumerable<ISaveChangesInterceptor>? additionalInterceptors = null,
        IEnumerable<IPersistenceModelConventionFactory>? modelConventionFactories = null,
        IEnumerable<IPersistenceModelConfigurator>? modelConfigurators = null,
        IEnumerable<IPersistenceOptionsExtension>? optionsExtensions = null,
        IEnumerable<IDbUpdateExceptionClassifier>? exceptionClassifiers = null)
    {
        ArgumentNullException.ThrowIfNull(auditInterceptor);
        ArgumentNullException.ThrowIfNull(softDeleteInterceptor);
        ArgumentNullException.ThrowIfNull(concurrencyInterceptor);

        AuditInterceptor = auditInterceptor;
        SoftDeleteInterceptor = softDeleteInterceptor;
        ConcurrencyInterceptor = concurrencyInterceptor;
        AdditionalInterceptors = additionalInterceptors?.ToList() ?? [];
        ModelConventionFactories = modelConventionFactories?.ToList() ?? [];
        ModelConfigurators = modelConfigurators?.ToList() ?? [];
        OptionsExtensions = optionsExtensions?.ToList() ?? [];
        ExceptionClassifiers = exceptionClassifiers?.ToList() ?? [];
    }

    /// <summary>Gets the scoped interceptor that populates audit fields.</summary>
    internal AuditInterceptor AuditInterceptor { get; }

    /// <summary>Gets the scoped interceptor that converts deletes to soft-deletes.</summary>
    internal SoftDeleteInterceptor SoftDeleteInterceptor { get; }

    /// <summary>Gets the interceptor that wraps concurrency exceptions.</summary>
    internal ConcurrencyInterceptor ConcurrencyInterceptor { get; }

    /// <summary>Gets the consumer-supplied and capability-registered additional interceptors.</summary>
    internal IReadOnlyList<ISaveChangesInterceptor> AdditionalInterceptors { get; }

    /// <summary>Gets the opt-in-capability model-finalizing convention contributions.</summary>
    internal IReadOnlyList<IPersistenceModelConventionFactory> ModelConventionFactories { get; }

    /// <summary>Gets the opt-in-capability entity-configuration contributions.</summary>
    internal IReadOnlyList<IPersistenceModelConfigurator> ModelConfigurators { get; }

    /// <summary>Gets the opt-in-capability <see cref="DbContextOptionsBuilder"/> mutation contributions.</summary>
    internal IReadOnlyList<IPersistenceOptionsExtension> OptionsExtensions { get; }

    /// <summary>Gets the registered provider-specific <see cref="DbUpdateException"/> classifiers.</summary>
    internal IReadOnlyList<IDbUpdateExceptionClassifier> ExceptionClassifiers { get; }

    /// <summary>
    /// Adds the platform's always-on interceptors, every additional interceptor, and applies every
    /// registered <see cref="IPersistenceOptionsExtension"/> to <paramref name="optionsBuilder"/>.
    /// </summary>
    /// <param name="optionsBuilder">The options builder to mutate.</param>
    /// <remarks>
    /// The single source of truth for "what gets wired onto a <see cref="DbContext"/>'s options" —
    /// called from both <see cref="SharedKernelDbContext.OnConfiguring"/> (the non-pooled path) and
    /// <c>EfCorePersistenceBuilder{TContext}.Build</c>'s pooled-factory callback (which cannot rely on
    /// <see cref="SharedKernelDbContext.OnConfiguring"/> at all, since a pooled context's options are
    /// frozen before that method ever runs). Calling this from one place only, on both paths, makes the
    /// two paths structurally incapable of drifting apart the way they previously could.
    /// </remarks>
    internal void ApplyTo(DbContextOptionsBuilder optionsBuilder)
    {
        // ORDER MATTERS ("soft-delete path stamps ModifiedBy/On"): both
        // SoftDeleteInterceptor (Deleted -> Modified) and AggregateRootTouchInterceptor
        // (Unchanged -> Modified for a touched owning root) FLIP an entry's state; AuditInterceptor only
        // stamps ModifiedBy/ModifiedOn for entries it observes as ALREADY Modified. Both state-flipping
        // interceptors run before Audit so it always observes final state.
        var interceptors = new List<IInterceptor>
        {
            SoftDeleteInterceptor,
            new AggregateRootTouchInterceptor(),
            AuditInterceptor,
            ConcurrencyInterceptor,
            DomainClockMaterializationInterceptor.FromContext,
        };
        interceptors.AddRange(AdditionalInterceptors);

        optionsBuilder.AddInterceptors(interceptors);

        foreach (var extension in OptionsExtensions)
            extension.Apply(optionsBuilder);
    }
}
