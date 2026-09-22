using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Application.Transactions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Repositories;

namespace SharedKernel.Persistence.Testing;

/// <summary>
/// Registers the in-memory persistence fakes, replacing any registration of the same contract — so they also work on
/// top of a service's real composition root (for example in a <c>WebApplicationFactory</c>).
/// </summary>
public static class PersistenceTestingServiceCollectionExtensions
{
    /// <summary>
    /// Registers one <see cref="FakeRepository{TAggregate, TId}"/> as <see cref="IRepository{TAggregate, TId}"/>,
    /// <see cref="IReadRepository{TAggregate, TId}"/> and itself (singleton), so the test can seed and inspect it.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type.</typeparam>
    /// <typeparam name="TId">The identity type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="seed">Optional initial aggregates.</param>
    /// <returns>The registered repository.</returns>
    public static FakeRepository<TAggregate, TId> AddFakeRepository<TAggregate, TId>(
        this IServiceCollection services,
        IEnumerable<TAggregate>? seed = null)
        where TAggregate : IAggregateRoot<TId>
        where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(services);

        var repository = new FakeRepository<TAggregate, TId>(a => a.Id, seed);
        services.RemoveAll<IRepository<TAggregate, TId>>();
        services.RemoveAll<IReadRepository<TAggregate, TId>>();
        services.AddSingleton(repository);
        services.AddSingleton<IRepository<TAggregate, TId>>(repository);
        services.AddSingleton<IReadRepository<TAggregate, TId>>(repository);
        foreach (var unitOfWork in RegisteredInstances<FakeUnitOfWork>(services))
            unitOfWork.Enlist(repository);
        return repository;
    }

    /// <summary>Registers one <see cref="FakeUnitOfWork"/> as <see cref="IUnitOfWork"/> and itself (singleton).</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The registered unit of work.</returns>
    public static FakeUnitOfWork AddFakeUnitOfWork(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var unitOfWork = new FakeUnitOfWork();
        services.RemoveAll<IUnitOfWork>();
        services.AddSingleton(unitOfWork);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        foreach (var participant in RegisteredInstances<IFakeTransactionParticipant>(services))
            unitOfWork.Enlist(participant);
        return unitOfWork;
    }

    /// <summary>Registers <paramref name="context"/> as <see cref="IRequestContext"/> and itself (singleton).</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="context">The caller; defaults to <see cref="TestRequestContext.ForUser"/>.</param>
    /// <returns>The registered context, to change the caller between steps.</returns>
    public static TestRequestContext AddTestRequestContext(this IServiceCollection services, TestRequestContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        context ??= TestRequestContext.ForUser();
        services.RemoveAll<IRequestContext>();
        services.AddSingleton(context);
        services.AddSingleton<IRequestContext>(context);
        return context;
    }

    /// <summary>Registers one <see cref="FakeCrossTenantScope"/> as <see cref="ICrossTenantScope"/> and itself (singleton).</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The registered scope.</returns>
    /// <remarks>For unit tests over the fakes; see <see cref="FakeCrossTenantScope"/>.</remarks>
    public static FakeCrossTenantScope AddFakeCrossTenantScope(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var scope = new FakeCrossTenantScope();
        services.RemoveAll<ICrossTenantScope>();
        services.AddSingleton(scope);
        services.AddSingleton<ICrossTenantScope>(scope);
        return scope;
    }

    /// <summary>Registers one <see cref="FakeAuditTrailWriter"/> as <see cref="IAuditTrailWriter"/> and itself (singleton).</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The registered writer.</returns>
    public static FakeAuditTrailWriter AddFakeAuditTrailWriter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var writer = new FakeAuditTrailWriter();
        services.RemoveAll<IAuditTrailWriter>();
        services.AddSingleton(writer);
        services.AddSingleton<IAuditTrailWriter>(writer);
        return writer;
    }

    // Fakes registered by the Add* helpers are singleton instances; link them whichever is registered first.
    private static IEnumerable<T> RegisteredInstances<T>(IServiceCollection services) =>
        services.Where(d => !d.IsKeyedService && d.ImplementationInstance is T).Select(d => (T)d.ImplementationInstance!).Distinct();
}
