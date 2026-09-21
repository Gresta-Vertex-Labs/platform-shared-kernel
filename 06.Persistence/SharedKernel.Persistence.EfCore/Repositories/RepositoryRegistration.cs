using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Registers the open-generic repositories of a context. Called by <c>AddSharedKernelPostgres</c>.
/// </summary>
/// <remarks>Placeholder created by stream E1; stream E2 owns the content.</remarks>
internal static class RepositoryRegistration
{
    /// <summary>Registers <c>IRepository&lt;,&gt;</c>/<c>IReadRepository&lt;,&gt;</c> for <typeparamref name="TContext"/>'s aggregates.</summary>
    /// <typeparam name="TContext">The registered context.</typeparam>
    /// <param name="services">The service collection.</param>
    public static void Register<TContext>(IServiceCollection services)
        where TContext : SharedKernelDbContext
    {
        _ = services;
    }
}
