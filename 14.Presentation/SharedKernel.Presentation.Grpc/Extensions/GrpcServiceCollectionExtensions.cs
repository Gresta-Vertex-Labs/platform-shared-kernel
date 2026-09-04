using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.Grpc.Interceptors;

namespace SharedKernel.Presentation.Grpc.Extensions;

/// <summary>
/// DI extension for wiring the platform's server-side gRPC conventions.
/// </summary>
public static class GrpcServiceCollectionExtensions
{
    /// <summary>
    /// The conservative default applied to <see cref="GrpcServiceOptions.MaxReceiveMessageSize"/>
    /// (4 MiB) before <paramref name="configure"/> runs — mirrors
    /// <c>PayloadLimitsOptions.MaxRequestBodySizeBytes</c>'s intent (P-411) and
    /// <c>AddSharedKernelSignalR</c>'s <c>MaximumReceiveMessageSize</c> default (P-409).
    /// </summary>
    public const int DefaultMaxReceiveMessageSizeBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Registers gRPC with the platform's global server interceptors
    /// (<see cref="GrpcExceptionInterceptor"/>, <see cref="GrpcCorrelationInterceptor"/>,
    /// <see cref="GrpcTenantContextInterceptor"/>, <see cref="GrpcAuthorizationInterceptor"/>) and
    /// a conservative inbound message-size default.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">
    /// An optional callback to further configure <see cref="GrpcServiceOptions"/> — use this to
    /// opt out of a platform interceptor (clear the relevant <c>Interceptors</c> entry) or raise/
    /// lower <see cref="GrpcServiceOptions.MaxReceiveMessageSize"/>.
    /// </param>
    /// <returns>The stock <see cref="IGrpcServerBuilder"/> returned by <c>Grpc.AspNetCore</c>'s own <c>AddGrpc</c> — no custom wrapper type.</returns>
    /// <remarks>
    /// <para>
    /// Every platform interceptor is registered globally via
    /// <see cref="GrpcServiceOptions.Interceptors"/> — the gRPC-native equivalent of
    /// <c>HubOptions.AddFilter&lt;T&gt;()</c> — so every gRPC service mapped in a consuming service
    /// gets all four by default with zero further per-service wiring. This is a strictly better
    /// story than HTTP's <c>AuthorizationRequirementEndpointFilter</c>, which needs a per-route/
    /// group <c>.AddEndpointFilter&lt;T&gt;()</c> call — <see cref="GrpcServiceOptions.Interceptors"/>
    /// genuinely auto-attaches with no further wiring (D-75).
    /// </para>
    /// <para>
    /// Registration order is deliberate: <see cref="GrpcExceptionInterceptor"/> is registered
    /// first (outermost) so it can catch and safely map an exception thrown by any interceptor
    /// registered after it, then <see cref="GrpcCorrelationInterceptor"/>, then
    /// <see cref="GrpcTenantContextInterceptor"/>, then <see cref="GrpcAuthorizationInterceptor"/>
    /// (innermost, closest to the service method) — mirroring the conceptual ordering of this
    /// domain's HTTP pipeline (exception handling outermost, authorization closest to the
    /// handler).
    /// </para>
    /// <para>
    /// No new <c>13.ServiceDefaults</c> telemetry entry point is needed or requested (D-79): gRPC
    /// calls ride the same Kestrel/HTTP2 pipeline ASP.NET Core's existing server-side
    /// OpenTelemetry instrumentation already traces, the same pipeline HTTP/1.1 Minimal API/MVC
    /// endpoints are already traced through with no <c>14.Presentation</c>-specific
    /// <c>WithXTelemetry</c> entry needed there either.
    /// </para>
    /// </remarks>
    public static IGrpcServerBuilder AddSharedKernelGrpc(
        this IServiceCollection services,
        Action<GrpcServiceOptions>? configure = null)
    {
        services.AddSingleton<GrpcExceptionInterceptor>();
        services.AddSingleton<GrpcCorrelationInterceptor>();
        services.AddSingleton<GrpcTenantContextInterceptor>();
        services.AddSingleton<GrpcAuthorizationInterceptor>();

        return services.AddGrpc(options =>
        {
            // Conservative, explicitly pinned resource-exhaustion default — applied BEFORE
            // configure so every caller override always wins.
            options.MaxReceiveMessageSize = DefaultMaxReceiveMessageSizeBytes;

            options.Interceptors.Add<GrpcExceptionInterceptor>();
            options.Interceptors.Add<GrpcCorrelationInterceptor>();
            options.Interceptors.Add<GrpcTenantContextInterceptor>();
            options.Interceptors.Add<GrpcAuthorizationInterceptor>();

            configure?.Invoke(options);
        });
    }
}
