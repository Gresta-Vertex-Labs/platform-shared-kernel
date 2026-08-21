using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Presentation.WebApi.Middleware;

/// <summary>
/// DI and pipeline-registration extensions for <see cref="CorrelationIdMiddleware"/>.
/// </summary>
public static class CorrelationIdExtensions
{
    /// <summary>
    /// Registers services required by <see cref="CorrelationIdMiddleware"/>, including its
    /// caller-supplied-value format validation configuration.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">
    /// An optional callback to customise <see cref="CorrelationIdOptions"/>. When omitted, the
    /// documented default <see cref="CorrelationIdOptions.MaxLength"/>/
    /// <see cref="CorrelationIdOptions.AllowedCharacterPattern"/> apply — a well-formed value (GUID,
    /// ULID, or another safe token shape matching the default pattern) continues to be preserved
    /// unchanged, so this is not a breaking change for existing well-behaved callers.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddSharedKernelCorrelationId(
        this IServiceCollection services,
        Action<CorrelationIdOptions>? configure = null)
    {
        var options = new CorrelationIdOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        return services;
    }

    /// <summary>
    /// Adds <see cref="CorrelationIdMiddleware"/> to the request pipeline.
    /// </summary>
    /// <param name="app">The application builder to add the middleware to.</param>
    /// <returns>The same <paramref name="app"/> instance, for chaining.</returns>
    /// <remarks>
    /// Must be the first call in the pipeline — before <c>UseExceptionHandler</c> — so correlation
    /// IDs are present even on error responses.
    /// </remarks>
    public static IApplicationBuilder UseSharedKernelCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}
