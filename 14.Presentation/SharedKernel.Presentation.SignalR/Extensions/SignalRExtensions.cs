using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.SignalR.Filters;

namespace SharedKernel.Presentation.SignalR.Extensions;

/// <summary>
/// DI extension for wiring the platform's SignalR conventions.
/// </summary>
public static class SignalRExtensions
{
    /// <summary>
    /// Registers SignalR with the platform's global hub filters
    /// (<see cref="TenantContextHubFilter"/> and <see cref="HubExceptionMappingFilter"/>).
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configureHubOptions">
    /// An optional callback to further configure <see cref="HubOptions"/> — use this to opt out of
    /// either platform filter by clearing the registered <c>HubOptions.HubFilters</c> entries, or
    /// to add service-specific filters.
    /// </param>
    /// <returns>
    /// The stock <see cref="ISignalRServerBuilder"/> returned by
    /// <c>Microsoft.AspNetCore.SignalR</c>'s own <c>AddSignalR</c> — no custom wrapper type.
    /// </returns>
    /// <remarks>
    /// Both platform filters are registered globally via <see cref="HubOptions"/> extension method
    /// <c>AddFilter&lt;T&gt;()</c> — not via per-hub <c>[HubFilter]</c> attributes — so every hub in
    /// a consuming service gets both by default.
    /// </remarks>
    public static ISignalRServerBuilder AddSharedKernelSignalR(
        this IServiceCollection services,
        Action<HubOptions>? configureHubOptions = null)
    {
        services.AddSingleton<TenantContextHubFilter>();
        services.AddSingleton<HubExceptionMappingFilter>();

        return services.AddSignalR(options =>
        {
            options.AddFilter<TenantContextHubFilter>();
            options.AddFilter<HubExceptionMappingFilter>();

            configureHubOptions?.Invoke(options);
        });
    }
}
