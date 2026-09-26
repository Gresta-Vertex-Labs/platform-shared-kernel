using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Logging;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.Authorization;

/// <summary>
/// Checks, before the host starts serving, that the platform's authorization is still in place, and warns about
/// authentication schemes whose callers it cannot evaluate.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddSharedKernelAuthorization()</c> decorates the <see cref="IAuthorizationPolicyProvider"/> and
/// <see cref="IAuthorizationMiddlewareResultHandler"/> registered before it. One registered after it replaces the
/// decorator, which would silently disable <c>[RequireEndpointPermission]</c> and its siblings or the problem bodies of
/// refusals. This check fails the start with an <see cref="InvalidOperationException"/> instead: a platform policy
/// name must still resolve to the platform's requirement, and the resolved result handler must be the platform's.
/// </para>
/// <para>
/// A caller authenticated by a scheme without an <see cref="IUserContextMapper"/> can never satisfy a requirement and
/// is refused with 403; every such scheme is named in a warning. Remote sign-in schemes (OpenID Connect, OAuth) and
/// policy schemes are skipped: they never authenticate a request themselves.
/// </para>
/// </remarks>
internal sealed partial class SharedKernelAuthorizationStartupCheck : IHostedLifecycleService
{
    /// <summary>A platform policy name used only to probe the registered policy provider.</summary>
    internal static readonly string ProbePolicyName = SharedKernelPolicyNames.ForPermissions(["sharedkernel.startup-probe"]);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SharedKernelAuthorizationStartupCheck> _logger;

    public SharedKernelAuthorizationStartupCheck(IServiceScopeFactory scopes, ILogger<SharedKernelAuthorizationStartupCheck>? logger = null)
    {
        _scopes = scopes;
        _logger = logger ?? NullLogger<SharedKernelAuthorizationStartupCheck>.Instance;
    }

    /// <inheritdoc />
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        await EnsurePolicyProviderAsync(services.GetRequiredService<IAuthorizationPolicyProvider>()).ConfigureAwait(false);
        EnsureResultHandler(services.GetRequiredService<IAuthorizationMiddlewareResultHandler>());
        await WarnAboutUnmappedSchemesAsync(services).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task EnsurePolicyProviderAsync(IAuthorizationPolicyProvider provider)
    {
        var policy = await provider.GetPolicyAsync(ProbePolicyName).ConfigureAwait(false);
        if (policy?.Requirements.OfType<SharedKernelRequirement>().Any() == true)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The IAuthorizationPolicyProvider in use ({provider.GetType().FullName}) does not resolve the policies of "
            + "[RequireEndpointPermission], [RequireRole], [RequireFreshAuthentication] and [RequireAuthenticationMethod]: it was "
            + "registered after AddSharedKernelWebApi() or AddSharedKernelAuthorization() and replaced the platform's. "
            + "Register it before those calls, and the platform decorates it, passing every other policy name to it.");
    }

    internal static void EnsureResultHandler(IAuthorizationMiddlewareResultHandler handler)
    {
        if (handler is SharedKernelAuthorizationResultHandler)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The IAuthorizationMiddlewareResultHandler in use ({handler.GetType().FullName}) replaced the platform's: "
            + "it was registered after AddSharedKernelWebApi() or AddSharedKernelAuthorization(), so refused requests "
            + "lose their problem bodies and step-up challenges. Register it before those calls, and the platform "
            + "decorates it.");
    }

    private async Task WarnAboutUnmappedSchemesAsync(IServiceProvider services)
    {
        if (services.GetService<IAuthenticationSchemeProvider>() is not { } schemeProvider)
        {
            return;
        }

        var mapped = services.GetServices<IUserContextMapper>()
            .Select(mapper => mapper.AuthenticationType)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var scheme in await schemeProvider.GetAllSchemesAsync().ConfigureAwait(false))
        {
            if (!mapped.Contains(scheme.Name) && AuthenticatesRequests(scheme))
            {
                Log.SchemeWithoutMapper(_logger, scheme.Name);
            }
        }
    }

    // A remote scheme (OpenID Connect, OAuth, WS-Federation) signs the user in to another scheme; a policy scheme
    // forwards to another scheme. Neither produces the identity a request is authorized with.
    private static bool AuthenticatesRequests(AuthenticationScheme scheme) =>
        !typeof(IAuthenticationRequestHandler).IsAssignableFrom(scheme.HandlerType)
        && scheme.HandlerType != typeof(PolicySchemeHandler);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 10,
            Level = LogLevel.Warning,
            Message = "Authentication scheme {SchemeName} has no IUserContextMapper: callers it authenticates are refused "
                + "by every [RequireEndpointPermission], [RequireRole], [RequireFreshAuthentication] and "
                + "[RequireAuthenticationMethod] requirement. Register the mapper of the authentication package.")]
        public static partial void SchemeWithoutMapper(ILogger logger, string schemeName);
    }
}
