using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Application.Pipeline.Extensions;

/// <summary>
/// DI registration entry point for <c>SharedKernel.Application.Pipeline</c>.
/// </summary>
public static class ApplicationBehaviorsServiceCollectionExtensions
{
    /// <summary>
    /// Begins building the opt-in request pipeline behavior registration.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>An <see cref="ApplicationBehaviorsBuilder"/> for chaining <c>.AddXBehavior()</c> calls.</returns>
    /// <remarks>
    /// Registers no mediator — <c>AddSharedKernelMediatR(...)</c> does that. Call
    /// <see cref="ApplicationBehaviorsBuilder.Build"/> to
    /// finalize registration.
    /// </remarks>
    public static ApplicationBehaviorsBuilder AddSharedKernelApplicationBehaviors(this IServiceCollection services)
        => new(services);
}
