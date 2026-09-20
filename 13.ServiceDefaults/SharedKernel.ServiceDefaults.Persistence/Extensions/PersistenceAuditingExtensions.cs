using Microsoft.Extensions.DependencyInjection;
using SharedKernel.ServiceDefaults.Persistence.Auditing;
using AppIAuditTrailWriter = SharedKernel.Application.Behaviors.Auditing.IAuditTrailWriter;

namespace SharedKernel.ServiceDefaults.Persistence.Extensions;

/// <summary>
/// Composition-root extension wiring <c>05.Application.Behaviors</c>'s <c>AuditingBehavior</c> to
/// <c>06.Persistence</c>'s real, hash-chained audit trail.
/// </summary>
public static class PersistenceAuditingExtensions
{
    /// <summary>
    /// Registers <see cref="AuditTrailWriterBridge"/> as <c>05.Application.Behaviors</c>'s
    /// <see cref="AppIAuditTrailWriter"/>, delegating to whichever <c>06.Persistence.Abstractions</c>
    /// <c>IAuditTrailWriter</c> is registered in the same DI scope (typically
    /// <c>SharedKernel.Persistence.EfCore.Auditing</c>'s <c>EfAuditTrailWriter</c>, via
    /// <c>.WithAuditTrail(configuration)</c>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for fluent chaining.</returns>
    /// <remarks>
    /// Call this AFTER <c>AddSharedKernelEfCore&lt;TContext&gt;(...).WithAuditTrail(configuration).Build()</c>
    /// (or any other registration of <c>06.Persistence.Abstractions.Auditing.IAuditTrailWriter</c>) so
    /// this bridge has something real to delegate to. Optional — a service that never opts a command
    /// into <c>IAuditableRequest</c> never needs this call.
    /// </remarks>
    public static IServiceCollection AddSharedKernelAuditTrailBridge(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<AppIAuditTrailWriter, AuditTrailWriterBridge>();
        return services;
    }
}
