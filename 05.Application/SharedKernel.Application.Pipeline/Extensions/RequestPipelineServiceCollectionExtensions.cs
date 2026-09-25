using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharedKernel.Application.Pipeline.Extensions;

/// <summary>
/// DI registration for <see cref="RequestPipeline{TRequest,TResponse}"/> and
/// <see cref="StreamRequestPipeline{TRequest,TResponse}"/>.
/// </summary>
public static class RequestPipelineServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RequestPipeline{TRequest,TResponse}"/> and
    /// <see cref="StreamRequestPipeline{TRequest,TResponse}"/> as open-generic transient services.
    /// Idempotent.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <remarks>
    /// <c>ApplicationBehaviorsBuilder.Build()</c> and <c>AddSharedKernelMediatR(...)</c> both call this,
    /// so a host calls it directly only when it resolves a pipeline without either — in a test, for
    /// example. It registers no behavior and no handler.
    /// </remarks>
    public static IServiceCollection AddSharedKernelRequestPipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient(typeof(RequestPipeline<,>));
        services.TryAddTransient(typeof(StreamRequestPipeline<,>));
        return services;
    }
}
