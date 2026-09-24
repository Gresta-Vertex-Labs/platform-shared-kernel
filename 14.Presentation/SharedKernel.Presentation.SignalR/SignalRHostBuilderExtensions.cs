using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Presentation.SignalR.Filters;
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
    ///   method returns, becomes a <see cref="HubException"/> with the message <c>"{code}: {message}"</c>: the message
    ///   localized and, for server errors outside Development, replaced by a generic sentence, exactly like an HTTP
    ///   problem response. A <c>TimeoutException</c>, or an <c>OperationCanceledException</c> while the connection is
    ///   open, becomes <c>"timeout.default: The operation did not complete in time."</c>, as HTTP answers it with 504.
    ///   Any other exception becomes <c>"unexpected.exception: An unexpected error occurred."</c> (in
    ///   Development, the exception's message). A <see cref="HubException"/> the hub throws itself passes unchanged. A
    ///   successful <c>Result&lt;T&gt;</c> returns its value to the client. Server errors are logged at Error, client
    ///   errors at Debug. SignalR puts its own sentence in front of the message it sends, so a client receives
    ///   <c>"An unexpected error occurred invoking '{method}' on the server. HubException: {code}: {message}"</c>;
    ///   <see cref="HubErrorMessage.TryParse"/> reads the code and the message back, and <see cref="HubErrorMessage"/>
    ///   lists what a client receives in every case, with the equivalent JavaScript pattern.</item>
    ///   <item>Streams. SignalR streams only a hub method declared to return <c>IAsyncEnumerable&lt;T&gt;</c> or
    ///   <c>ChannelReader&lt;T&gt;</c> (optionally inside <c>Task</c> or <c>ValueTask</c>), so a
    ///   <c>Result&lt;IAsyncEnumerable&lt;T&gt;&gt;</c> or <c>Result&lt;ChannelReader&lt;T&gt;&gt;</c> method is an
    ///   ordinary invocation that cannot stream: its failure is the coded <see cref="HubException"/>, and its success is
    ///   refused as <c>unexpected.exception</c> and logged at Error with the fix, where the stream would otherwise close
    ///   the connection. To stream with a failure that can happen before the first item, declare the stream type and
    ///   throw the failure before returning the stream, with <c>SharedKernel.Core</c>'s <c>GetValueOrThrow()</c>:
    ///   <c>public IAsyncEnumerable&lt;Order&gt; Orders() =&gt; _orders.Stream().GetValueOrThrow();</c>. A
    ///   <c>StreamAsync</c> caller then gets the coded <see cref="HubException"/> or the items. An exception thrown while
    ///   the stream is read, after the hub method returned it, reaches the client without a code, as SignalR's
    ///   <c>"An error occurred on the server while streaming results."</c>: the error mapping wraps the hub method, not
    ///   the reading of its stream. A <c>Result</c> is read only as the hub method's own return value; inside a stream
    ///   item or a collection it cannot be serialized.</item>
    ///   <item>Authorization: <c>AddSharedKernelAuthorization()</c>, which decodes the policies of
    ///   <c>[RequirePermission]</c>, <c>[RequireRole]</c>, <c>[RequireFreshAuthentication]</c> and
    ///   <c>[RequireAuthenticationMethod]</c>. On a hub class and on <c>MapHub&lt;T&gt;().RequirePermission(…)</c> they
    ///   guard the connection, which is refused with 401 or 403. They are <c>[Authorize]</c> attributes, so on a hub
    ///   method SignalR checks them itself, before any hub filter runs: a refused invocation fails with SignalR's own
    ///   <see cref="HubException"/> message, <c>"Failed to invoke '…' because user is unauthorized"</c>, never
    ///   reaches the method, and neither passes through the error mapping nor counts against the rate limit. The host
    ///   still calls <c>UseAuthentication()</c> and <c>UseAuthorization()</c>; <c>UseSharedKernelWebApi()</c> does
    ///   both. A hub method's requirements are checked at every invocation against the principal the connection was
    ///   opened with (<c>Context.User</c>), which is never refreshed while the connection stays open. Step-up
    ///   requirements therefore last only as long as that principal allows: <c>[RequireFreshAuthentication]</c>
    ///   compares its authentication time with the current time, so it lapses on an open connection once the maximum
    ///   age has passed; <c>[RequireAuthenticationMethod]</c> without a maximum age keeps passing for as long as the
    ///   connection stays open, and a maximum age for the method, where the attribute takes one, makes that step-up lapse
    ///   on an open connection as well. SignalR's <c>CloseOnAuthenticationExpiration</c> option on <c>MapHub</c> closes a
    ///   connection when its authentication expires.</item>
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
