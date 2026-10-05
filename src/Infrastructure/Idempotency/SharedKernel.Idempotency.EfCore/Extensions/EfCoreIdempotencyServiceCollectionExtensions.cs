using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Internal;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Idempotency.EfCore.Store;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Idempotency.EfCore.Extensions;

/// <summary><see cref="IServiceCollection"/> extension methods for registering the PostgreSQL idempotency store.</summary>
public static class EfCoreIdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IdempotencyDbContext"/> and <see cref="EfCoreIdempotencyStore"/> as the
    /// <see cref="IIdempotencyStore"/> for every purpose <paramref name="purposes"/> selects, keyed by purpose.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureDbContext">
    /// Configures the context — typically <c>options.UsePostgres(dataSource)</c> from <c>SharedKernel.Persistence.EfCore</c>.
    /// </param>
    /// <param name="purposes">Selects the purposes, for example <c>p =&gt; p.ForRequests().ForMessages()</c>.</param>
    /// <param name="configureOptions">Optional delegate to customise <see cref="EfCoreIdempotencyOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No purpose was selected, or a store is already registered for a selected purpose.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Requires an <see cref="IClock"/> (e.g. <c>services.AddClock()</c>): every reservation and expiry timestamp comes
    /// from it. Registers <see cref="IRequestContextAccessor"/> unless one is already registered: every key is scoped
    /// by the tenant of the ambient request context, which the service's inbound adapters set.
    /// </para>
    /// <para>
    /// This package ships no EF Core migrations. See <c>README.md</c> for the design-time factory recipe and the
    /// cleanup job that bounds table growth.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEfCoreIdempotency(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext,
        Action<IdempotencyPurposeSelection> purposes,
        Action<EfCoreIdempotencyOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDbContext);
        ArgumentNullException.ThrowIfNull(purposes);

        var selected = IdempotencyServiceCollectionExtensions.SelectPurposes(purposes);

        services.AddDbContext<IdempotencyDbContext>(options =>
        {
            configureDbContext(options);
            IdempotencyDbContextOptions.DisableRetry(options);
        });

        services
            .AddOptions<EfCoreIdempotencyOptions>()
            .Configure(o => configureOptions?.Invoke(o));

        foreach (var purpose in selected)
            services.AddIdempotencyStore<EfCoreIdempotencyStore>(purpose);

        services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();

        return services;
    }
}
