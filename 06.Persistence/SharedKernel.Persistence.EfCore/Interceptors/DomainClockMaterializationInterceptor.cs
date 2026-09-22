using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// Gives every aggregate EF Core materializes the clock of the context that materializes it
/// (<see cref="IHasClock.AttachClock"/>), for tracking and no-tracking queries alike.
/// </summary>
/// <remarks>
/// One shared instance: EF Core treats materialization interceptors as part of its internal service-provider
/// cache key, so a new instance per context would build a new internal provider each time.
/// </remarks>
internal sealed class DomainClockMaterializationInterceptor : IMaterializationInterceptor
{
    private DomainClockMaterializationInterceptor()
    {
    }

    /// <summary>The shared instance every <see cref="SharedKernelDbContext"/> registers.</summary>
    internal static DomainClockMaterializationInterceptor FromContext { get; } = new();

    /// <inheritdoc/>
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (entity is IHasClock { IsClockAttached: false } hasClock
            && materializationData.Context is SharedKernelDbContext context)
        {
            hasClock.AttachClock(context.Clock);
        }

        return entity;
    }
}
