using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.OpenApi.Routing;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.OpenApi.Startup;

/// <summary>
/// Warns, when the host starts, that the documents are served outside the Development environment to anyone who can
/// reach the service: no convention applied to the builder <c>MapSharedKernelOpenApi()</c> returned requires
/// authorization or allows anonymous access on purpose, and no fallback policy applies.
/// </summary>
/// <remarks>
/// An API description maps the attack surface, so serving it is a decision, and so is serving it to everyone:
/// <c>AllowAnonymous()</c> records that decision and silences the warning. Only conventions applied to that builder
/// are seen — not those of a route group the documents are mapped in.
/// </remarks>
internal sealed partial class OpenApiStartupDiagnostics : IHostedLifecycleService
{
    private readonly OpenApiSetupState _state;
    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<OpenApiStartupDiagnostics> _logger;

    public OpenApiStartupDiagnostics(
        OpenApiSetupState state,
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<OpenApiStartupDiagnostics> logger)
    {
        _state = state;
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Checked once every hosted service has started: conventions are applied right after the documents are mapped,
    /// and a <c>Startup</c> class maps them while the web host starts.
    /// </remarks>
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        if (_state.ExposedDocuments.Any(IsOpenToEveryoneByOversight))
        {
            Log.DocumentsServedWithoutAuthorization(_logger, _environment.EnvironmentName);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Neither protected by a convention or the fallback policy, nor made public on purpose with AllowAnonymous().
    private bool IsOpenToEveryoneByOversight(CompositeEndpointConventionBuilder documents) =>
        EndpointAuthorization.GetAccess(documents.GetConventionMetadata(_services)) == EndpointAccess.Unspecified
        && !EndpointAuthorization.HasFallbackPolicy(_services);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 301,
            Level = LogLevel.Warning,
            Message = "The OpenAPI documents and API reference are served in the {EnvironmentName} environment to anyone "
                + "who can reach the service: no authorization convention was applied to MapSharedKernelOpenApi() and no "
                + "fallback authorization policy is set. Protect them, as in "
                + "app.MapSharedKernelOpenApi().RequireEndpointPermission(\"docs.read\"), or call AllowAnonymous() on it if they "
                + "are meant to be public.")]
        public static partial void DocumentsServedWithoutAuthorization(ILogger logger, string environmentName);
    }
}
