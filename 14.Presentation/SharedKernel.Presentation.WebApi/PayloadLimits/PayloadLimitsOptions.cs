namespace SharedKernel.Presentation.WebApi.PayloadLimits;

/// <summary>
/// Configures conservative platform defaults for request payload-size and JSON-depth
/// denial-of-service protection.
/// </summary>
/// <remarks>
/// Both values are independently configurable and fully overridable per service. Wired via
/// <see cref="PayloadLimitsExtensions.AddSharedKernelPayloadLimits"/> (service-registration time,
/// JSON max-depth) and <see cref="PayloadLimitsExtensions.UseSharedKernelPayloadLimits"/>
/// (request-scoped, max request body size).
/// </remarks>
public sealed class PayloadLimitsOptions
{
    /// <summary>
    /// Gets or sets the maximum accepted request body size, in bytes. Defaults to
    /// <c>1_048_576</c> (1 MB).
    /// </summary>
    public long MaxRequestBodySizeBytes { get; set; } = 1_048_576;

    /// <summary>
    /// Gets or sets the maximum accepted JSON nesting depth. Defaults to <c>32</c>.
    /// </summary>
    public int MaxJsonDepth { get; set; } = 32;
}
