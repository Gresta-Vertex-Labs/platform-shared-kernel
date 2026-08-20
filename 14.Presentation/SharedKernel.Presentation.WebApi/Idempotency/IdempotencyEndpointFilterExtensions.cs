using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Presentation.WebApi.Idempotency;

/// <summary>
/// Minimal API sugar for attaching <see cref="RequireIdempotencyKeyAttribute"/> metadata, plus the
/// DI registration for <see cref="IdempotencyKeyRequirementEndpointFilter"/>.
/// </summary>
public static class IdempotencyEndpointFilterExtensions
{
    /// <summary>
    /// Attaches a <see cref="RequireIdempotencyKeyAttribute"/> to the endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route handler builder to attach metadata to.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <remarks>
    /// Purely metadata attachment — this does not itself perform the check or register
    /// <see cref="IdempotencyKeyRequirementEndpointFilter"/> on the pipeline. The consumer must
    /// additionally call
    /// <c>.AddEndpointFilter&lt;IdempotencyKeyRequirementEndpointFilter&gt;()</c> on
    /// <c>MapControllers()</c> and/or each route group — see
    /// <see cref="AddSharedKernelIdempotencyFilters"/>.
    /// </remarks>
    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder)
        => builder.WithMetadata(new RequireIdempotencyKeyAttribute());

    /// <summary>
    /// Attaches a <see cref="RequireIdempotencyKeyAttribute"/> to every endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route group builder to attach metadata to.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static RouteGroupBuilder RequireIdempotencyKey(this RouteGroupBuilder builder)
        => builder.WithMetadata(new RequireIdempotencyKeyAttribute());

    /// <summary>
    /// Registers <see cref="IdempotencyKeyRequirementEndpointFilter"/> as a singleton service.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Mirrors <c>AddSharedKernelAuthorizationFilters</c>'s exact shape.
    /// <b>This registration alone does not attach the filter to any endpoint.</b> The consumer must
    /// additionally call
    /// <c>.AddEndpointFilter&lt;IdempotencyKeyRequirementEndpointFilter&gt;()</c> on
    /// <c>MapControllers()</c> and/or each minimal-API route group.
    /// </remarks>
    public static IServiceCollection AddSharedKernelIdempotencyFilters(this IServiceCollection services)
    {
        services.AddSingleton<IdempotencyKeyRequirementEndpointFilter>();
        return services;
    }
}
