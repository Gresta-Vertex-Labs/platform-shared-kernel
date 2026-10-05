using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Transactions;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Persistence.Testing;
using SharedKernel.Testing.Idempotency;

namespace SharedKernel.Testing.Application;

/// <summary>
/// DI convenience extension registering the <c>Application/</c> seam fakes in a single call.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FakeUnitOfWork"/> as <see cref="IUnitOfWork"/>,
    /// <see cref="FakeRequestContext"/> (authenticated by default) as <see cref="IRequestContext"/>,
    /// and <see cref="FakeIdempotencyStore"/> as the <see cref="IIdempotencyStore"/> for
    /// <see cref="IdempotencyPurpose.Request"/>, all as singletons.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>AddFakeCachingServices()</c>'s one-call bundling pattern. This call satisfies the
    /// host-start seam check of <c>AddSharedKernelApplication</c> for <c>WithTransactions()</c>,
    /// <c>WithIdempotency()</c> and <c>[RequirePermission]</c> requests in one step, before or after that call.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddFakeApplicationBehaviorServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IUnitOfWork, FakeUnitOfWork>();
        services.AddSingleton<IRequestContext, FakeRequestContext>();
        services.AddFakeIdempotencyStore(IdempotencyPurpose.Request);

        return services;
    }
}
