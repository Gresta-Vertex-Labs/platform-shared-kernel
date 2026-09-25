using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharedKernel.Execution.Context;
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
    /// <param name="configureRateLimit">
    /// An optional callback declaring per-connection hub-method invocation rate limits and/or
    /// argument-payload shape validation — see <see cref="HubInvocationRateLimitOptions"/>.
    /// <see cref="HubInvocationRateLimitFilter"/> is always registered, but is a genuine no-op
    /// (never rejects an invocation) when this parameter is omitted, carrying zero observable
    /// behavior change for a host that does not opt in.
    /// </param>
    /// <returns>
    /// The stock <see cref="ISignalRServerBuilder"/> returned by
    /// <c>Microsoft.AspNetCore.SignalR</c>'s own <c>AddSignalR</c> — no custom wrapper type.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Every platform filter is registered globally via <see cref="HubOptions"/> extension method
    /// <c>AddFilter&lt;T&gt;()</c> — not via per-hub <c>[HubFilter]</c> attributes — so every hub in
    /// a consuming service gets all three by default.
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
        Action<HubOptions>? configureHubOptions = null,
        Action<HubInvocationRateLimitOptions>? configureRateLimit = null)
    {
        services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();
        services.AddSingleton<TenantContextHubFilter>();
        services.AddSingleton<HubExceptionMappingFilter>();

        var rateLimitOptions = new HubInvocationRateLimitOptions();
        configureRateLimit?.Invoke(rateLimitOptions);
        services.AddSingleton(rateLimitOptions);
        services.AddSingleton<HubInvocationRateLimitFilter>();

        // Startup-time CORS diagnostic (P-418) — flags any mapped SignalR hub endpoint lacking
        // CORS metadata via a one-time Warning log; never rejects a connection or throws.
        services.AddHostedService<SignalRCorsStartupDiagnostic>();

        return services.AddSignalR(options =>
        {
            options.AddFilter<TenantContextHubFilter>();
            options.AddFilter<HubExceptionMappingFilter>();
            options.AddFilter<HubInvocationRateLimitFilter>();

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
