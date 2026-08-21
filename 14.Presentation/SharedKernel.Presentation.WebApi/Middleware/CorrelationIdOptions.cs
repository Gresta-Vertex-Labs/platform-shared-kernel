namespace SharedKernel.Presentation.WebApi.Middleware;

/// <summary>
/// Configures the shape validation <see cref="CorrelationIdMiddleware"/> applies to a
/// caller-supplied correlation-id header value before it is trusted.
/// </summary>
/// <remarks>
/// A caller-supplied value exceeding <see cref="MaxLength"/> or containing a character outside
/// <see cref="AllowedCharacterPattern"/> is rejected — the middleware falls back to freshly
/// generating a correlation id, exactly as it already does for an absent/whitespace header — before
/// the caller-supplied value ever reaches <c>HttpContext.Items</c>, <see cref="System.Diagnostics.Activity"/>
/// baggage, or the response header. An unvalidated, caller-controlled string flowing directly into
/// OTel baggage and every downstream structured log record is a log-injection/oversized-baggage-
/// propagation vector; this is the platform's defense at the point a raw correlation-id header
/// first enters the system.
/// </remarks>
public sealed class CorrelationIdOptions
{
    /// <summary>
    /// Gets or sets the maximum accepted length, in characters, of a caller-supplied correlation
    /// id. Defaults to <c>128</c>.
    /// </summary>
    public int MaxLength { get; set; } = 128;

    /// <summary>
    /// Gets or sets the regular expression a caller-supplied correlation id must fully match to be
    /// accepted. Defaults to a safe-but-permissive allowlist covering GUID/ULID/general safe-token
    /// shapes: alphanumerics plus <c>-</c>, <c>_</c>, <c>:</c>, and <c>.</c>.
    /// </summary>
    public string AllowedCharacterPattern { get; set; } = "^[A-Za-z0-9\\-_:.]+$";
}
