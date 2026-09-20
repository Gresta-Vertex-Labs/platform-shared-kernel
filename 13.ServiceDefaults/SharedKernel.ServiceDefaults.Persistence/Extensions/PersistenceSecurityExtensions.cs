using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Security.Abstractions;
using SharedKernel.ServiceDefaults.Persistence.Security;
using SharedKernel.ServiceDefaults.Persistence.UnitOfWork;
using AppBehaviorsIUnitOfWork = SharedKernel.Application.Behaviors.Transaction.IUnitOfWork;

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
    /// Registers <see cref="PersistenceUnitOfWorkAdapter"/> as
    /// <see cref="AppBehaviorsIUnitOfWork"/>, resolving it against whichever
    /// <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c> is registered in the same DI scope —
    /// works correctly for both <c>EfUnitOfWork</c> and <c>EfTransactionalUnitOfWork</c> — see
    /// <see cref="PersistenceUnitOfWorkAdapter"/>'s own remarks for the bug this replaces.
    /// Optional. Omitting this call leaves <see cref="AppBehaviorsIUnitOfWork"/> unregistered.
    /// </remarks>
    public static EfCorePersistenceBuilder<TContext> WithApplicationTransactionBehavior<TContext>(
        this EfCorePersistenceBuilder<TContext> builder)
        where TContext : SharedKernelDbContext
    {
        builder.Services.AddScoped<AppBehaviorsIUnitOfWork, PersistenceUnitOfWorkAdapter>();
        return builder;
    }
}
