namespace SharedKernel.ServiceDefaults.Security;

/// <summary>
/// Settings for the HTTP inbound request context set up by
/// <see cref="RequestContextServiceCollectionExtensions.AddSharedKernelRequestContext"/> and
/// <see cref="RequestContextApplicationBuilderExtensions.UseSharedKernelRequestContext"/>.
/// </summary>
public sealed class RequestContextOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether W3C <c>baggage</c> sent by the caller is kept. Defaults to
    /// <see langword="false"/>: hosting takes no baggage from the request, and
    /// <see cref="RequestContextApplicationBuilderExtensions.UseSharedKernelRequestContext"/> removes any inbound item
    /// still on the request's <see cref="System.Diagnostics.Activity"/> before it adds the correlation id.
    /// </summary>
    /// <remarks>
    /// Baggage travels onward with every outgoing call and is copied onto log records, so a caller who can set it
    /// can plant values — a tenant id, a user id — that downstream services and log queries trust. Set this to
    /// <see langword="true"/> only behind a gateway that removes or rewrites caller-supplied baggage. Baggage this
    /// service adds itself, the correlation id included, is never affected. OpenTelemetry's own baggage store is
    /// refused the caller's baggage by <c>SharedKernel.ServiceDefaults</c>' telemetry whatever this setting says.
    /// </remarks>
    public bool TrustInboundBaggage { get; set; }
}
