using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.KeyStore;
using SharedKernel.Idempotency.EfCore.MessageStore;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Idempotency.EfCore.Startup;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.TenantContext;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Idempotency.EfCore.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering the EF Core/PostgreSQL-backed
/// idempotency stores.
/// </summary>
public static class EfCoreIdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IdempotencyDbContext"/>, <see cref="EfCoreRequestIdempotencyStore"/> as
    /// <see cref="IRequestIdempotencyStore"/>, and <see cref="EfCoreIdempotencyMessageStore"/> as
    /// <see cref="IIdempotencyStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureDbContext">
    /// Configures the underlying <see cref="DbContextOptionsBuilder"/> — typically
    /// <c>options.UsePostgreSQL(serviceProvider)</c> from <c>SharedKernel.Persistence.EfCore</c>.
    /// </param>
    /// <param name="configureOptions">
    /// Optional delegate to customise <see cref="EfCoreIdempotencyOptions"/>. When
    /// <see langword="null"/> the defaults are used.
    /// </param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Requires an <see cref="IClock"/> to already be registered (e.g. via
    /// <c>01.Core/SharedKernel.Primitives</c>'s <c>services.AddClock()</c>) — every
    /// <c>ReservedAtUtc</c>/<c>ExpiresAtUtc</c> timestamp and expiry comparison is sourced from it,
    /// never <see cref="DateTime.UtcNow"/>.
    /// </para>
    /// <para>
    /// Registers a startup-time <see cref="IHostedService"/>
    /// (<see cref="IdempotencyTenantAccessorStartupValidator"/>) that throws
    /// <see cref="InvalidOperationException"/> at <c>IHost.StartAsync()</c> if no
    /// <see cref="ITenantContextAccessor"/> has been registered — fail-fast, not first-use.
    /// </para>
    /// <para>
    /// This package ships no EF Core migrations. See <c>README.md</c> for the design-time-factory
    /// recipe a consuming service uses to author its own migration for the two tables this context
    /// owns, and the documented cleanup-job recipe for bounding table growth (Domain Invariant 5).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelEfCoreIdempotency(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext,
        Action<EfCoreIdempotencyOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDbContext);

        services.AddDbContext<IdempotencyDbContext>(configureDbContext);

        services
            .AddOptions<EfCoreIdempotencyOptions>()
            .Configure(o => configureOptions?.Invoke(o))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Ensures IOptions<IdempotencyOptions> resolves even when the consuming service never
        // called 07.Messaging's MessagingBusBuilder.WithIdempotency(...) — AddOptions is
        // idempotent, so a prior registration (with the consumer's own configured ExpiryWindow)
        // is left untouched.
        services.AddOptions<IdempotencyOptions>();

        services.AddScoped<IRequestIdempotencyStore, EfCoreRequestIdempotencyStore>();
        services.AddScoped<IIdempotencyStore, EfCoreIdempotencyMessageStore>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, IdempotencyTenantAccessorStartupValidator>());

        return services;
    }
}
