using System.Diagnostics.CodeAnalysis;
using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Presentation.Grpc.Interceptors;
using SharedKernel.Presentation.Grpc.Options;
using SharedKernel.Presentation.WebApi;

namespace SharedKernel.Presentation.Grpc;

/// <summary>Registers the gRPC services of a SharedKernel service.</summary>
public static class GrpcHostBuilderExtensions
{
    private const string BindingReason = "Binds SharedKernelGrpcOptions from configuration by reflection.";

    /// <summary>
    /// Registers gRPC with the platform's error contract and authorization, configured from
    /// <c>SharedKernel:Presentation:Grpc</c> (<see cref="SharedKernelGrpcOptions"/>) and validated when the host starts.
    /// </summary>
    /// <param name="builder">The host builder, such as <c>WebApplication.CreateBuilder(args)</c>.</param>
    /// <param name="configure">Adjusts the settings after they are bound from configuration.</param>
    /// <returns>The <see cref="IGrpcServerBuilder"/> of <c>AddGrpc()</c>, for further gRPC configuration.</returns>
    /// <remarks>
    /// <para>Registers:</para>
    /// <list type="bullet">
    ///   <item>gRPC (<c>AddGrpc()</c>) with an exception interceptor on every service. Every exception a service method
    ///   throws ends as a <c>google.rpc.Status</c>: the code from <see cref="Errors.GrpcStatusCodeMap"/>, the message an
    ///   HTTP client would get (translated, server errors redacted outside Development), an <c>ErrorInfo</c> detail
    ///   (<c>reason</c> = the error code, <c>domain</c> = <see cref="SharedKernelGrpcOptions.ErrorDomain"/>,
    ///   <c>metadata</c> = <c>traceId</c> and <c>correlationId</c>) and, for field errors, a <c>BadRequest</c> detail
    ///   with every violation. Clients read it with <c>RpcException.GetRpcStatus()</c>. Server errors are logged at
    ///   Error, client errors at Debug; a call the client cancelled ends as <c>Cancelled</c> and is not logged as an
    ///   error. Map results with <see cref="GrpcResultExtensions"/>.</item>
    ///   <item><see cref="SharedKernelAuthorizationExtensions.AddSharedKernelAuthorization"/>, so
    ///   <c>[RequirePermission]</c>, <c>[RequireRole]</c>, <c>[RequireFreshAuthentication]</c> and
    ///   <c>[RequireAuthenticationMethod]</c> work on a service class or method, and the same requirements as
    ///   conventions on <c>MapGrpcService&lt;T&gt;()</c>. An anonymous caller gets <c>Unauthenticated</c>, a caller
    ///   without the permission <c>PermissionDenied</c>.</item>
    /// </list>
    /// <para>
    /// gRPC calls run through the HTTP pipeline, so <c>app.UseSharedKernelWebApi()</c> (after
    /// <c>builder.AddSharedKernelWebApi()</c>) gives them correlation ids, authentication and authorization; without it,
    /// call <c>UseRouting()</c>, <c>UseAuthentication()</c> and <c>UseAuthorization()</c> before mapping services. In a
    /// service method the correlation id is <c>context.GetHttpContext().GetCorrelationId()</c>; the caller and the
    /// tenant come from an injected <c>IUserContext</c>, <c>ITenantProvider</c> or <c>IRequestContext</c>.
    /// </para>
    /// <para>
    /// Global interceptors run in the order they are added, the first outermost. Call this before adding your own, so
    /// exceptions they throw are mapped too. Safe to call more than once; each <paramref name="configure"/> is applied.
    /// </para>
    /// </remarks>
    /// <exception cref="OptionsValidationException">At host startup, when the settings are invalid.</exception>
    [RequiresUnreferencedCode(BindingReason)]
    [RequiresDynamicCode(BindingReason)]
    public static IGrpcServerBuilder AddSharedKernelGrpc(this IHostApplicationBuilder builder, Action<SharedKernelGrpcOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(GrpcServicesMarker)))
        {
            services.AddSingleton<GrpcServicesMarker>();

            services.AddValidatedOptions<SharedKernelGrpcOptions>(builder.Configuration);

            // A post-configuration, so it sees every other configuration whatever its registration order and only
            // fills a domain nobody set.
            var applicationName = builder.Environment.ApplicationName;
            services.AddOptions<SharedKernelGrpcOptions>().PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ErrorDomain))
                {
                    options.ErrorDomain = applicationName;
                }
            });

            services.AddSingleton<GrpcExceptionInterceptor>();
            services.AddOptions<GrpcServiceOptions>().Configure(options => options.Interceptors.Add<GrpcExceptionInterceptor>());

            services.AddSharedKernelAuthorization();
        }

        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services.AddGrpc();
    }
}

/// <summary>Marks a service collection <c>AddSharedKernelGrpc()</c> has already configured.</summary>
internal sealed class GrpcServicesMarker;
