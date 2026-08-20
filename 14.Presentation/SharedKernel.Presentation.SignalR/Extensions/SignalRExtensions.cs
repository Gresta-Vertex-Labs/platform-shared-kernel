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
    /// <para>
    /// Both platform filters are registered globally via <see cref="HubOptions"/> extension method
    /// <c>AddFilter&lt;T&gt;()</c> — not via per-hub <c>[HubFilter]</c> attributes — so every hub in
    /// a consuming service gets both by default.
    /// </para>
    /// <para>
    /// Also sets explicit, documented, conservative resource-exhaustion defaults on
    /// <see cref="HubOptions"/> (<see cref="HubOptions.MaximumReceiveMessageSize"/>,
    /// <see cref="HubOptions.MaximumParallelInvocationsPerClient"/>,
    /// <see cref="HubOptions.ClientTimeoutInterval"/>, <see cref="HubOptions.KeepAliveInterval"/>) —
    /// pinned explicitly even where a value matches SignalR's own current framework default, so the
    /// platform's posture is documented and stable across future SignalR version changes rather
    /// than implicit. These defaults are applied BEFORE <paramref name="configureHubOptions"/>
    /// runs, so every default remains fully overridable (raise or lower) with no signature change.
    /// </para>
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

            // Conservative, explicitly pinned resource-exhaustion defaults (WO-062, P-409) —
            // applied BEFORE configureHubOptions so every caller override always wins.
            options.MaximumReceiveMessageSize = 32 * 1024;
            options.MaximumParallelInvocationsPerClient = 1;
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);

            configureHubOptions?.Invoke(options);
        });
    }
}
