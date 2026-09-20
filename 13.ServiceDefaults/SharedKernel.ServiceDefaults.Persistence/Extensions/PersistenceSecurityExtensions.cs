using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Security.Abstractions;
using SharedKernel.ServiceDefaults.Persistence.Security;
using SharedKernel.ServiceDefaults.Persistence.UnitOfWork;
using AppBehaviorsIUnitOfWork = SharedKernel.Application.Behaviors.Transaction.IUnitOfWork;
using PersistenceIUnitOfWork = SharedKernel.Persistence.Abstractions.UnitOfWork.IUnitOfWork;
using PersistenceITransactionalUnitOfWork = SharedKernel.Persistence.Abstractions.UnitOfWork.ITransactionalUnitOfWork;

namespace SharedKernel.ServiceDefaults.Persistence.Extensions;

/// <summary>
/// Composition-root extensions bridging <c>12.Security</c>/<c>05.Application.Behaviors</c> into
/// <c>06.Persistence</c>'s local seams.
/// </summary>
/// <remarks>
/// Lives here — not in <c>SharedKernel.Persistence.EfCore</c> — because that package no
/// longer references either <c>12.Security.Abstractions</c> or <c>05.Application.Behaviors</c>;
/// <c>13.ServiceDefaults</c> is the composition-root layer legally positioned to reference both.
/// </remarks>
public static class PersistenceSecurityExtensions
{
    /// <summary>
    /// Registers <see cref="SecurityCurrentActorContext"/> as the default
    /// <see cref="ICurrentActorContext"/> and <see cref="SecurityCurrentTenantContext"/> as the
    /// default <see cref="ICurrentTenantContext"/>, bridging the caller's real
    /// <see cref="IUserContext"/>/<see cref="ITenantProvider"/> identity into every
    /// <c>06.Persistence</c> seam that consumes them (audit columns, the tenant query filter, the
    /// tenant write guard, the audit trail).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Also registers a default <see cref="ITenantProvider"/> (<c>UserContextTenantProvider</c>, from
    /// <c>IUserContext.TenantId</c>) via <c>TryAddScoped</c> — an already-registered
    /// <see cref="ITenantProvider"/> wins. Call this AFTER registering the real <see cref="IUserContext"/>
    /// (e.g. via an <c>AddOidcAuthentication(...)</c>-shaped call) and BEFORE
    /// <c>AddSharedKernelEfCore&lt;TContext&gt;(...).Build()</c>, so these bridges — registered with
    /// plain <c>Add</c>, not <c>TryAdd</c> — win over EfCore's own anonymous defaults
    /// (<c>AnonymousActorContext</c>/<c>NullCurrentTenantContext</c>).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelPersistenceSecurityBridge(this IServiceCollection services)
    {
        services.TryAddScoped<ITenantProvider, UserContextTenantProvider>();
        services.AddScoped<ICurrentActorContext, SecurityCurrentActorContext>();
        services.AddScoped<ICurrentTenantContext, SecurityCurrentTenantContext>();
        return services;
    }

    /// <summary>
    /// Opts in to bridging <c>05.Application.Behaviors</c>' <c>TransactionBehavior</c> to this
    /// builder's registered <c>06.Persistence</c> unit of work (<c>EfUnitOfWork</c> or
    /// <c>EfTransactionalUnitOfWork</c>, whichever <c>.WithTransactionalUnitOfWork()</c> selects).
    /// </summary>
    /// <typeparam name="TContext">The concrete <see cref="SharedKernelDbContext"/> subclass.</typeparam>
    /// <param name="builder">The persistence builder.</param>
    /// <returns>The same builder for further chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registers <see cref="AppBehaviorsIUnitOfWork"/> with a factory that decides, the first time it
    /// is resolved in a given DI scope, whether this scope's <c>06.Persistence</c> registrations also
    /// support <c>05.Application.Behaviors</c>' <c>ITransactionalUnitOfWork</c> capability:
    /// <list type="bullet">
    /// <item><description>
    /// When <c>06.Persistence.Abstractions.UnitOfWork.ITransactionalUnitOfWork</c> resolves (i.e. this
    /// builder's own <c>.WithTransactionalUnitOfWork()</c> was called), the factory returns a
    /// <see cref="TransactionalPersistenceUnitOfWorkAdapter"/> wrapping it — <c>TransactionBehavior</c>'s
    /// runtime <c>is ITransactionalUnitOfWork</c> check then succeeds, and it opens an explicit
    /// transaction before the inner pipeline runs (see that adapter's own remarks for the defect this
    /// fixes: an audit-trail write that requires an already-open ambient transaction previously never
    /// had one).
    /// </description></item>
    /// <item><description>
    /// Otherwise it returns the plain <see cref="PersistenceUnitOfWorkAdapter"/> wrapping
    /// <c>06.Persistence.Abstractions.UnitOfWork.IUnitOfWork</c> — <c>TransactionBehavior</c> behaves
    /// exactly as it did before this capability existed.
    /// </description></item>
    /// </list>
    /// This check is deferred to first resolution (never inspected eagerly against
    /// <see cref="EfCorePersistenceBuilder{TContext}.Services"/> at THIS call site) specifically so
    /// this method's position in the fluent chain relative to <c>.WithTransactionalUnitOfWork()</c> and
    /// <c>.Build()</c> never matters — <c>.WithTransactionalUnitOfWork()</c> only sets a flag; the real
    /// <c>ITransactionalUnitOfWork</c> registration it enables is not added to the service collection
    /// until <see cref="EfCorePersistenceBuilder{TContext}.Build"/> runs, which happens after every
    /// builder method call regardless of call order.
    /// </para>
    /// <para>
    /// Optional. Omitting this call leaves <see cref="AppBehaviorsIUnitOfWork"/> unregistered.
    /// </para>
    /// </remarks>
    public static EfCorePersistenceBuilder<TContext> WithApplicationTransactionBehavior<TContext>(
        this EfCorePersistenceBuilder<TContext> builder)
        where TContext : SharedKernelDbContext
    {
        builder.Services.AddScoped<AppBehaviorsIUnitOfWork>(sp =>
        {
            var transactional = sp.GetService<PersistenceITransactionalUnitOfWork>();

            return transactional is not null
                ? new TransactionalPersistenceUnitOfWorkAdapter(transactional)
                : new PersistenceUnitOfWorkAdapter(sp.GetRequiredService<PersistenceIUnitOfWork>());
        });

        return builder;
    }
}
