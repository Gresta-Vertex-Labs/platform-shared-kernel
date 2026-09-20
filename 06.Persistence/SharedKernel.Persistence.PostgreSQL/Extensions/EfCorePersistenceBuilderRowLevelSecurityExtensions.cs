using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.PostgreSQL.MultiTenancy;

namespace SharedKernel.Persistence.PostgreSQL.Extensions;

/// <summary>
/// Opt-in PostgreSQL row-level-security (RLS) extension method for <see cref="EfCorePersistenceBuilder{TContext}"/>.
/// </summary>
public static class EfCorePersistenceBuilderRowLevelSecurityExtensions
{
    /// <summary>
    /// Opts in to binding the current tenant — and the <see cref="ICrossTenantScope"/> escape clause —
    /// to every connection <typeparamref name="TContext"/> opens, so a matching PostgreSQL row-level
    /// security policy (see <c>RowLevelSecurityMigrationBuilderExtensions.EnableTenantRowLevelSecurity</c>)
    /// enforces tenant isolation at the database level, independently of this platform's own
    /// application-level tenant query filter and write guard.
    /// </summary>
    /// <param name="builder">The persistence builder.</param>
    /// <returns>The same builder for further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at <see cref="EfCorePersistenceBuilder{TContext}.Build"/> time when no
    /// <see cref="ITenantSessionBinder"/> is registered — call <c>services.AddSharedKernelNpgsql(...)</c>
    /// first.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Constrained to <see cref="TenantedDbContext"/> at compile time — row-level security is
    /// inherently a multi-tenant capability, so a single-tenant <c>SharedKernelDbContext</c> cannot opt
    /// in.
    /// </para>
    /// <para>
    /// Deliberately does NOT require <see cref="EfCorePersistenceBuilder{TContext}.WithMultiTenancy"/>
    /// to also have been called. <see cref="TenantedDbContext"/>'s own EF-level tenant query filter is
    /// always installed regardless of that call (only its write guard interceptor is conditional on
    /// it) — a service that opts in to only <c>.WithRowLevelSecurity()</c> still gets EF-level read
    /// filtering plus database-level read <em>and</em> write enforcement (a PostgreSQL policy with no
    /// explicit <c>WITH CHECK</c> clause reuses its <c>USING</c> expression for INSERT/UPDATE too), a
    /// coherent, secure configuration on its own. Calling both is still recommended as defense in depth
    /// — <see cref="EfCorePersistenceBuilder{TContext}.WithMultiTenancy"/>'s application-level write
    /// guard rejects a cross-tenant write with a typed exception before it ever reaches the database;
    /// row-level security alone surfaces the same rejection as a raw provider exception.
    /// </para>
    /// <para>
    /// Pairs with <see cref="RowLevelSecurityConnectionInterceptor"/> (see its own remarks for the
    /// connection-scoped bind/reset design) and requires the RLS migration helper to actually have been
    /// applied to each protected table — this method only wires the application-side half.
    /// </para>
    /// </remarks>
    public static EfCorePersistenceBuilder<TContext> WithRowLevelSecurity<TContext>(
        this EfCorePersistenceBuilder<TContext> builder)
        where TContext : TenantedDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<RowLevelSecurityConnectionInterceptor>();
        builder.Services.AddSingleton<IPersistenceOptionsExtension, RowLevelSecurityOptionsContributor>();

        builder.AddBuildAction(() =>
        {
            if (!builder.Services.Any(sd => sd.ServiceType == typeof(ITenantSessionBinder)))
            {
                throw new InvalidOperationException(
                    "'.WithRowLevelSecurity()' requires an 'ITenantSessionBinder' already registered. " +
                    "Call 'services.AddSharedKernelNpgsql(...)' before " +
                    "'services.AddSharedKernelEfCore<TContext>(...)....WithRowLevelSecurity()'.");
            }
        });

        return builder;
    }
}
