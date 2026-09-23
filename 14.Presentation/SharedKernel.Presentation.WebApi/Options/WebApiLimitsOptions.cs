namespace SharedKernel.Presentation.WebApi.Options;

/// <summary>Request limits that protect the service from oversized or deeply nested payloads.</summary>
/// <remarks>
/// An endpoint that accepts larger bodies raises its own limit with <c>WithRequestSizeLimit(bytes)</c> or
/// <c>[RequestSizeLimit]</c>; a request over the limit is answered 413 with the code <c>request.too_large</c>.
/// </remarks>
public sealed class WebApiLimitsOptions
{
    /// <summary>
    /// Gets or sets the largest request body Kestrel accepts, in bytes. Defaults to 4 MiB. <see langword="null"/>
    /// leaves the server's own default in place.
    /// </summary>
    public long? MaxRequestBodySize { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the deepest JSON nesting accepted, for minimal APIs and MVC alike. Defaults to 32.
    /// </summary>
    public int MaxJsonDepth { get; set; } = 32;
}
