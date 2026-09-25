using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Execution.Context;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Exceptions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Identifiers;

namespace SharedKernel.Persistence.EfCore.Context;

/// <summary>
/// Everything <see cref="SharedKernelDbContext"/> needs, bundled into the one constructor parameter a
/// derived context forwards: <c>MyContext(DbContextOptions&lt;MyContext&gt; o, PersistenceContextDependencies d) : base(o, d)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddSharedKernelPostgres</c> registers one instance as a singleton; nothing in it depends on the
/// caller. The caller (<see cref="IRequestContext"/>) and the domain-event dispatcher are attached to each
/// context instance when it is handed out, which keeps the bundle safe to share between requests and
/// between pooled context instances.
/// </para>
/// <para>
/// A context built by hand — a design-time factory for <c>dotnet ef</c>, a test, a tool — uses
/// <see cref="Create"/>. The constructor is internal on purpose: every platform behavior is reached
/// through this type, so there is nothing a caller could meaningfully assemble differently.
/// </para>
/// </remarks>
public sealed class PersistenceContextDependencies
{
    internal PersistenceContextDependencies(
        IClock clock,
        IRequestContext initialRequestContext,
        string serviceName,
        IDomainEventDispatcher? defaultDomainEventDispatcher,
        IEnumerable<IInterceptor>? additionalInterceptors,
        IEnumerable<IPersistenceModelConventionFactory>? modelConventionFactories,
        IEnumerable<IPersistenceModelConfigurator>? modelConfigurators,
        IEnumerable<IPersistenceOptionsExtension>? optionsExtensions,
        IEnumerable<IDbUpdateExceptionClassifier>? exceptionClassifiers,
        IIdGenerator? keyGenerator,
        ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(initialRequestContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        Clock = clock;
        InitialRequestContext = initialRequestContext;
        ServiceName = serviceName;
        DefaultDomainEventDispatcher = defaultDomainEventDispatcher;
        AdditionalInterceptors = additionalInterceptors?.ToList() ?? [];
        ModelConventionFactories = modelConventionFactories?.ToList() ?? [];
        ModelConfigurators = modelConfigurators?.ToList() ?? [];
        OptionsExtensions = optionsExtensions?.ToList() ?? [];
        ExceptionClassifiers = exceptionClassifiers?.ToList() ?? [];
        KeyGenerator = keyGenerator;
        LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>
    /// Creates the dependencies for a context constructed by hand (a design-time factory, a test, a tool).
    /// </summary>
    /// <param name="requestContext">
    /// The caller changes are attributed to and, for a <see cref="TenantedDbContext"/>, the
    /// tenant data is filtered by. Defaults to the fail-closed <see cref="AnonymousRequestContext"/>.
    /// </param>
    /// <param name="clock">The clock for audit stamps and materialized aggregates. Defaults to the system clock.</param>
    /// <param name="domainEventDispatcher">
    /// Dispatches domain events raised by tracked aggregates before each save. Without one, pending events
    /// are logged as discarded.
    /// </param>
    /// <param name="serviceName">The actor recorded when the caller has no user id. Defaults to <c>"system"</c>.</param>
    /// <param name="additionalInterceptors">Extra interceptors to add to every context built with the result.</param>
    /// <param name="loggerFactory">Optional logger factory for the context's own diagnostics.</param>
    /// <returns>The dependencies to pass to the context's constructor.</returns>
    /// <remarks>
    /// The PostgreSQL SQLSTATE classifier is always included. Capability packages that contribute through
    /// dependency injection (field encryption, the audit trail) are not wired by this method; register the
    /// context with <c>AddSharedKernelPostgres</c> for those.
    /// </remarks>
#pragma warning disable RS0026 // Optional parameters on the one public factory: a single, documented call shape.
    public static PersistenceContextDependencies Create(
        IRequestContext? requestContext = null,
        IClock? clock = null,
        IDomainEventDispatcher? domainEventDispatcher = null,
        string serviceName = PersistenceDefaults.ServiceName,
        IEnumerable<IInterceptor>? additionalInterceptors = null,
        ILoggerFactory? loggerFactory = null)
#pragma warning restore RS0026
        => new(
            clock ?? new SystemClock(),
            requestContext ?? AnonymousRequestContext.Instance,
            serviceName,
            domainEventDispatcher,
            additionalInterceptors,
            modelConventionFactories: null,
            modelConfigurators: null,
            optionsExtensions: null,
            exceptionClassifiers: [new PostgresDbUpdateExceptionClassifier()],
            keyGenerator: null,
            loggerFactory);

    /// <summary>The clock for audit stamps and for aggregates the context materializes.</summary>
    internal IClock Clock { get; }

    /// <summary>The caller a context starts with before one is attached for its lease.</summary>
    internal IRequestContext InitialRequestContext { get; }

    /// <summary>The actor written when the caller has no user id.</summary>
    internal string ServiceName { get; }

    /// <summary>The dispatcher a context starts with before one is attached for its lease.</summary>
    internal IDomainEventDispatcher? DefaultDomainEventDispatcher { get; }

    /// <summary>Consumer- and capability-registered interceptors, added after the platform interceptor.</summary>
    internal IReadOnlyList<IInterceptor> AdditionalInterceptors { get; }

    /// <summary>Model-finalizing convention contributions from capability packages.</summary>
    internal IReadOnlyList<IPersistenceModelConventionFactory> ModelConventionFactories { get; }

    /// <summary>Entity-configuration contributions from capability packages.</summary>
    internal IReadOnlyList<IPersistenceModelConfigurator> ModelConfigurators { get; }

    /// <summary><see cref="DbContextOptionsBuilder"/> contributions from capability packages.</summary>
    internal IReadOnlyList<IPersistenceOptionsExtension> OptionsExtensions { get; }

    /// <summary>Provider-specific <see cref="DbUpdateException"/> classifiers, platform classifier first.</summary>
    internal IReadOnlyList<IDbUpdateExceptionClassifier> ExceptionClassifiers { get; }

    /// <summary>Generates client-side keys when <c>UseUuidV7Keys()</c> is on; otherwise <see langword="null"/>.</summary>
    internal IIdGenerator? KeyGenerator { get; }

    /// <summary>Creates the context's own loggers.</summary>
    internal ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Adds the platform interceptors, every additional interceptor and every
    /// <see cref="IPersistenceOptionsExtension"/> to <paramref name="optionsBuilder"/>. Called by
    /// <see cref="SharedKernelDbContext"/>'s <c>OnConfiguring</c> and, for pooled contexts (whose options
    /// are frozen before <c>OnConfiguring</c> runs), by the pooled factory's options callback.
    /// </summary>
    /// <param name="optionsBuilder">The options builder to mutate.</param>
    internal void ApplyTo(DbContextOptionsBuilder optionsBuilder)
    {
        var interceptors = new List<IInterceptor>(AdditionalInterceptors.Count + 3)
        {
            PersistenceSaveChangesInterceptor.Instance,
            DomainClockMaterializationInterceptor.FromContext,
            ProtectedColumnUpdateGuard.Instance,
        };
        interceptors.AddRange(AdditionalInterceptors);

        optionsBuilder.AddInterceptors(interceptors);
        optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, PersistenceModelCacheKeyFactory>();

        foreach (var extension in OptionsExtensions)
            extension.Apply(optionsBuilder);
    }
}

/// <summary>Default values shared by the registration and the hand-built path.</summary>
internal static class PersistenceDefaults
{
    /// <summary>The actor recorded for writes without a user id, unless a service name is configured.</summary>
    public const string ServiceName = "system";
}
