using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.SignalR.Extensions;

/// <summary>
/// Startup diagnostic that flags any mapped SignalR hub endpoint lacking CORS metadata.
/// </summary>
/// <remarks>
/// <para>
/// <c>SharedKernel.Presentation.SignalR</c> deliberately declines a <c>ProjectReference</c> on
/// <c>SharedKernel.Presentation.WebApi</c>'s <c>CorsPolicyOptions</c> — the two packages remain
/// independent API surfaces (see this domain's <c>CLAUDE.md</c>), so a pure real-time host must not
/// be forced to pull in <c>Asp.Versioning</c>/<c>Microsoft.AspNetCore.OpenApi</c>/
/// <c>Scalar.AspNetCore</c> transitively just to get a CORS integration point. This diagnostic
/// closes that gap without a code dependency: a scan over every mapped endpoint, triggered once by
/// <see cref="IHostApplicationLifetime.ApplicationStarted"/>, logging a <see cref="LogLevel.Warning"/>
/// for any SignalR-hub-shaped endpoint (identified by the public
/// <see cref="Microsoft.AspNetCore.SignalR.HubMetadata"/> marker <c>MapHub&lt;THub&gt;()</c> attaches
/// — confirmed via reflection against the installed <c>Microsoft.AspNetCore.SignalR.Core</c>
/// assembly) that carries no <see cref="ICorsMetadata"/> (confirmed, via a real minimal-host
/// round trip, to be the actual metadata type <c>RequireCors</c>/<c>EnableCorsAttribute</c>/
/// <c>DisableCorsAttribute</c> all implement — the general "a CORS decision was made for this
/// endpoint" marker, covering both an applied policy and an explicit opt-out).
/// </para>
/// <para>
/// Each hub's <c>/negotiate</c> companion endpoint (identified by the presence of
/// <see cref="NegotiateMetadata"/>) is skipped — it always carries the identical CORS metadata as
/// its primary hub endpoint (confirmed via the same round trip), so evaluating it too would only
/// produce a duplicate warning for the same underlying gap.
/// </para>
/// </remarks>
internal sealed partial class SignalRCorsStartupDiagnostic : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly EndpointDataSource _endpointDataSource;
    private readonly ILogger<SignalRCorsStartupDiagnostic> _logger;

    /// <summary>
    /// Initialises a new <see cref="SignalRCorsStartupDiagnostic"/>.
    /// </summary>
    /// <param name="lifetime">Used to defer the scan until the host has fully started.</param>
    /// <param name="endpointDataSource">The application's mapped endpoints.</param>
    /// <param name="logger">
    /// The logger used to record any flagged hub. When no <see cref="ILogger{TCategoryName}"/> is
    /// registered in the container, a no-op <see cref="NullLogger{T}"/> is used instead.
    /// </param>
    public SignalRCorsStartupDiagnostic(
        IHostApplicationLifetime lifetime,
        EndpointDataSource endpointDataSource,
        ILogger<SignalRCorsStartupDiagnostic>? logger = null)
    {
        _lifetime = lifetime;
        _endpointDataSource = endpointDataSource;
        _logger = logger ?? NullLogger<SignalRCorsStartupDiagnostic>.Instance;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime.ApplicationStarted.Register(ScanForMissingCorsMetadata);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ScanForMissingCorsMetadata()
    {
        foreach (var endpoint in _endpointDataSource.Endpoints)
        {
            var hubMetadata = endpoint.Metadata.GetMetadata<HubMetadata>();

            if (hubMetadata is null)
            {
                continue;
            }

            // Skip the /negotiate companion endpoint — it always carries the same CORS metadata as
            // the primary hub endpoint, so evaluating it too would only duplicate the warning.
            if (endpoint.Metadata.GetMetadata<NegotiateMetadata>() is not null)
            {
                continue;
            }

            var hasCorsMetadata = endpoint.Metadata.GetMetadata<ICorsMetadata>() is not null;

            if (!hasCorsMetadata)
            {
                Log.HubMissingCorsPolicy(_logger, hubMetadata.HubType.Name, endpoint.DisplayName ?? "(unnamed endpoint)");
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="SignalRCorsStartupDiagnostic"/>.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 102,
            Level = LogLevel.Warning,
            Message = "SignalR hub {HubTypeName} (endpoint {EndpointDisplayName}) is mapped with no CORS policy attached — cross-origin clients may be unable to connect. Attach a policy via RequireCors(...).")]
        public static partial void HubMissingCorsPolicy(ILogger logger, string hubTypeName, string endpointDisplayName);
    }
}
