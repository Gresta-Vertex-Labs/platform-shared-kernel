using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Presentation.SignalR.Options;
using SharedKernel.Presentation.WebApi;

namespace SharedKernel.Presentation.SignalR;

/// <summary>Registers SignalR with the SharedKernel hub conventions.</summary>
public static class SignalRHostBuilderExtensions
{
    private const string BindingReason = "Binds SharedKernelSignalROptions from configuration by reflection.";

    /// <summary>
    /// Registers SignalR with the platform's error contract, authorization and invocation rate limit, configured from
    /// <c>SharedKernel:Presentation:SignalR</c> (<see cref="SharedKernelSignalROptions"/>) and validated when the host
    /// starts.
    /// </summary>
    /// <param name="builder">The host builder, such as <c>WebApplication.CreateBuilder(args)</c>.</param>
    /// <param name="configure">Adjusts the settings after they are bound from configuration.</param>
    /// <returns>SignalR's own <see cref="ISignalRServerBuilder"/>, for protocols or a scale-out backplane.</returns>
    /// <remarks>
    /// <para>Registers <c>AddSignalR()</c> and, for every hub:</para>
    /// <list type="bullet">
    ///   <item>Error mapping. A <c>SharedKernelException</c>, or a failed <c>Result</c> or <c>Result&lt;T&gt;</c> a hub
    ///   method returns, reaches the client as a <see cref="HubException"/> with the message
    ///   <c>"{code}: {message}"</c>: the message localized and, for server errors outside Development, replaced by a
    ///   generic sentence, exactly like an HTTP problem response. Any other exception becomes
    ///   <c>"unexpected.exception: An unexpected error occurred."</c> (in Development, the exception's message). A
    ///   <see cref="HubException"/> the hub throws itself passes unchanged. A successful <c>Result&lt;T&gt;</c> returns
    ///   its value to the client. Server errors are logged at Error, client errors at Debug.</item>
    ///   <item>Authorization: <c>AddSharedKernelAuthorization()</c>, which decodes the policies of
    ///   <c>[RequirePermission]</c>, <c>[RequireRole]</c>, <c>[RequireFreshAuthentication]</c> and
    ///   <c>[RequireAuthenticationMethod]</c>. On a hub class and on <c>MapHub&lt;T&gt;().RequirePermission(…)</c> they
    ///   guard the connection, which is refused with 401 or 403. They are <c>[Authorize]</c> attributes, so on a hub
    ///   method SignalR checks them itself, before any hub filter runs: a refused invocation fails with SignalR's own
    ///   <see cref="HubException"/> message, <c>"Failed to invoke '…' because user is unauthorized"</c>, never
    ///   reaches the method, and neither passes through the error mapping nor counts against the rate limit. The host
    ///   still calls <c>UseAuthentication()</c> and <c>UseAuthorization()</c>; <c>UseSharedKernelWebApi()</c> does
    ///   both.</item>
    ///   <item>The invocation rate limit of <see cref="SharedKernelSignalROptions.InvocationRateLimit"/>, off unless
    ///   <see cref="SignalRInvocationRateLimitOptions.PermitLimit"/> is set.</item>
    /// </list>
    /// <para>
    /// The hub filters are global, so they wrap filters registered after this call; call it before adding filters
    /// of your own. Safe to call more than once; each <paramref name="configure"/> is applied.
    /// </para>
    /// </remarks>
    /// <exception cref="OptionsValidationException">At host startup, when the settings are invalid.</exception>
    [RequiresUnreferencedCode(BindingReason)]
    [RequiresDynamicCode(BindingReason)]
    public static ISignalRServerBuilder AddSharedKernelSignalR(this IHostApplicationBuilder builder, Action<SharedKernelSignalROptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(SignalRServicesMarker)))
        {
            services.AddSingleton<SignalRServicesMarker>();
            services.AddValidatedOptions<SharedKernelSignalROptions, SharedKernelSignalROptionsValidator>(builder.Configuration);

            services.AddSharedKernelAuthorization();

            services.AddSingleton<HubExceptionMappingFilter>();
            services.AddSingleton<HubInvocationRateLimitFilter>();

            // Order is nesting: the error mapping wraps the rate limit, so it sees what the rate limit refuses as well
            // as what the hub method returns or throws. No filter authorizes: SignalR checks a hub method's
            // [Authorize] attributes, the SharedKernel ones included, before any filter runs.
            services.Configure<HubOptions>(options =>
            {
                options.AddFilter<HubExceptionMappingFilter>();
                options.AddFilter<HubInvocationRateLimitFilter>();
            });
        }

        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services.AddSignalR();
    }
}

/// <summary>Marks a service collection <c>AddSharedKernelSignalR()</c> has already configured.</summary>
internal sealed class SignalRServicesMarker;
