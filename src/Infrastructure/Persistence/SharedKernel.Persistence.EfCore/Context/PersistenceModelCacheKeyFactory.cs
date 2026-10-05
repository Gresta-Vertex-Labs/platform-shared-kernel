using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SharedKernel.Persistence.EfCore.Context;

/// <summary>
/// Keys EF Core's model cache by the context type AND the registration settings that change the model (client
/// key generation, capability configurators and conventions), so two registrations of the same context type with
/// different settings in one process never share a model.
/// </summary>
internal sealed class PersistenceModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime)
    {
        if (context is not SharedKernelDbContext sharedKernelContext)
            return (context.GetType(), designTime);

        var dependencies = sharedKernelContext.Dependencies;
        return (
            context.GetType(),
            designTime,
            dependencies.KeyGenerator is not null,
            string.Join('|', dependencies.ModelConfigurators.Select(c => c.GetType().FullName)),
            string.Join('|', dependencies.ModelConventionFactories.Select(f => f.GetType().FullName)));
    }
}
