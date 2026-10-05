using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Specifications;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Registers the open-generic repositories (<c>IRepository&lt;,&gt;</c>, <c>IReadRepository&lt;,&gt;</c>,
/// <c>IBulkMutationRepository&lt;,&gt;</c>) for the aggregates of one or more DbContexts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Several contexts.</b> Call once per context type. Each repository resolves the context that maps its
/// aggregate: on first use per aggregate type the registered contexts' models are consulted (models are cached per
/// context type, so this is cheap) and the answer is cached for the process. An aggregate mapped by two contexts
/// is ambiguous and fails with a message naming both; register a closed <c>IRepository&lt;X, XId&gt;</c> yourself
/// to choose.
/// </para>
/// <para>
/// <b>Custom repositories.</b> A closed registration (<c>services.AddScoped&lt;IRepository&lt;Order, OrderId&gt;,
/// OrderRepository&gt;()</c>) always wins over the open generic.
/// </para>
/// </remarks>
internal static class RepositoryRegistration
{
    /// <summary>Registers the repositories for <typeparamref name="TContext"/>'s aggregates. Idempotent.</summary>
    /// <typeparam name="TContext">The context type; it must itself be resolvable from the container.</typeparam>
    /// <param name="services">The service collection.</param>
    public static void Register<TContext>(IServiceCollection services)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var map = services
            .Where(d => d.ServiceType == typeof(RepositoryContextMap) && !d.IsKeyedService)
            .Select(d => d.ImplementationInstance)
            .OfType<RepositoryContextMap>()
            .FirstOrDefault();

        if (map is null)
        {
            map = new RepositoryContextMap();
            services.AddSingleton(map);
        }

        map.Add(typeof(TContext));

        services.TryAddSingleton(typeof(ISpecificationEvaluator<>), typeof(SpecificationEvaluator<>));
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IReadRepository<,>), typeof(ResolvedEfReadRepository<,>)));
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<,>), typeof(ResolvedEfRepository<,>)));
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IBulkMutationRepository<,>), typeof(ResolvedEfRepository<,>)));
    }
}

/// <summary>The context types that own repositories, and which one maps each aggregate type.</summary>
internal sealed class RepositoryContextMap
{
    private readonly List<Type> _contextTypes = [];
    private readonly ConcurrentDictionary<Type, Type> _contextByAggregate = new();

    /// <summary>Gets the registered context types, in registration order.</summary>
    internal IReadOnlyList<Type> ContextTypes => _contextTypes;

    internal void Add(Type contextType)
    {
        if (!_contextTypes.Contains(contextType))
            _contextTypes.Add(contextType);
    }

    /// <summary>Resolves, from the current scope, the context that maps <paramref name="aggregateType"/>.</summary>
    /// <exception cref="InvalidOperationException">No registered context, or more than one, maps it.</exception>
    internal SharedKernelDbContext Resolve(Type aggregateType, IServiceProvider services)
    {
        if (_contextByAggregate.TryGetValue(aggregateType, out var known))
            return (SharedKernelDbContext)services.GetRequiredService(known);

        SharedKernelDbContext? match = null;
        foreach (var contextType in _contextTypes)
        {
            var context = (SharedKernelDbContext)services.GetRequiredService(contextType);
            var entityType = context.Model.FindEntityType(aggregateType);
            if (entityType is null || entityType.IsOwned() || entityType.FindPrimaryKey() is null)
                continue;

            if (match is not null)
            {
                throw new InvalidOperationException(
                    $"Aggregate '{aggregateType.Name}' is mapped by both '{match.GetType().Name}' and "
                    + $"'{contextType.Name}', so its repository cannot choose a context. Register a closed "
                    + $"IRepository<{aggregateType.Name}, …>/IReadRepository<{aggregateType.Name}, …> for the context "
                    + "that owns it.");
            }

            match = context;
        }

        if (match is null)
        {
            throw new InvalidOperationException(
                $"No registered SharedKernelDbContext maps aggregate '{aggregateType.Name}' "
                + $"(registered: {string.Join(", ", _contextTypes.Select(t => t.Name))}). Add it to a context's model.");
        }

        _contextByAggregate.TryAdd(aggregateType, match.GetType());
        return match;
    }
}

/// <summary>The open-generic <see cref="IReadRepository{TAggregate, TId}"/> registration.</summary>
internal sealed class ResolvedEfReadRepository<TAggregate, TId>(
    RepositoryContextMap map,
    IServiceProvider services,
    ISpecificationEvaluator<TAggregate> evaluator)
    : EfReadRepository<TAggregate, TId>(map.Resolve(typeof(TAggregate), services), evaluator)
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull;

/// <summary>The open-generic <see cref="IRepository{TAggregate, TId}"/> and bulk registration.</summary>
internal sealed class ResolvedEfRepository<TAggregate, TId>(
    RepositoryContextMap map,
    IServiceProvider services,
    ISpecificationEvaluator<TAggregate> evaluator)
    : EfRepository<TAggregate, TId>(map.Resolve(typeof(TAggregate), services), evaluator)
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull;
