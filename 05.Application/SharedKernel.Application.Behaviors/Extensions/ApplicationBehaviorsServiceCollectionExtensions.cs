using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Application.Behaviors.Extensions;

/// <summary>
/// DI registration entry point for <c>SharedKernel.Application.Behaviors</c>.
/// </summary>
public static class ApplicationBehaviorsServiceCollectionExtensions
{
    /// <summary>
    /// Begins building the opt-in MediatR pipeline behavior registration.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>An <see cref="ApplicationBehaviorsBuilder"/> for chaining <c>.AddXBehavior()</c> calls.</returns>
    /// <remarks>
    /// Does not call <c>services.AddMediatR(...)</c> — the consuming service owns MediatR
    /// registration and assembly scanning. Call <see cref="ApplicationBehaviorsBuilder.Build"/> to
    /// finalize registration.
    /// </remarks>
    public static ApplicationBehaviorsBuilder AddSharedKernelApplicationBehaviors(this IServiceCollection services)
        => new(services);
}
