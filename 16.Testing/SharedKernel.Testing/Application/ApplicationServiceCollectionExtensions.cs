using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Transaction;

namespace SharedKernel.Testing.Application;

/// <summary>
/// DI convenience extension registering the <c>Application/</c> local-seam fakes in a single call.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FakeUnitOfWork"/> as <see cref="IUnitOfWork"/>,
    /// <see cref="FakeAuthorizationContext"/> (constructed with <c>defaultResult: true</c>) as
    /// <see cref="IAuthorizationContext"/>, and <see cref="FakeIdempotencyKeyStore"/> (the non-replay
    /// variant — the default production shape absent opt-in) as <see cref="IIdempotencyKeyStore"/>,
    /// all as singletons.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>AddFakeCachingServices()</c>'s one-call bundling pattern. This call satisfies
    /// <c>ApplicationBehaviorsBuilder</c>'s <c>Build()</c>-time missing-dependency guards for
    /// <c>AddTransactionBehavior()</c>/<c>AddAuthorizationBehavior()</c>/<c>AddIdempotencyBehavior()</c>
    /// in one step. For response-replay tests, register <see cref="FakeIdempotencyResponseStore"/>
    /// manually instead:
    /// <c>services.AddSingleton&lt;IIdempotencyKeyStore, FakeIdempotencyResponseStore&gt;();</c>
    /// </remarks>
    /// <remarks>
    /// Local-seam-only scope: every fake registered here implements one of <c>05.Application.Behaviors</c>'
    /// own local seam interfaces exclusively — none references <c>06.Persistence</c>, <c>12.Security</c>,
    /// or <c>07.Messaging</c>. This mirrors the isolation already documented on each individual fake type.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddFakeApplicationBehaviorServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IUnitOfWork, FakeUnitOfWork>();
        services.AddSingleton<IAuthorizationContext>(new FakeAuthorizationContext(defaultResult: true));
        services.AddSingleton<IIdempotencyKeyStore, FakeIdempotencyKeyStore>();

        return services;
    }
}
