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
    /// <remarks>
    /// The limit is enforced by the server — Kestrel, which also applies each endpoint's own limit — not by this
    /// package, so an in-memory <c>TestServer</c> accepts any body size.
    /// </remarks>
    public long? MaxRequestBodySize { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the deepest JSON nesting read and written, for minimal APIs and MVC alike.
    /// <see langword="null"/> (the default) keeps the framework's own limit (64).
    /// </summary>
    /// <remarks>
    /// The value is the serializer's <c>MaxDepth</c>, which limits responses as well as request bodies: set it no lower
    /// than the deepest response the service returns.
    /// </remarks>
    public int? MaxJsonDepth { get; set; }
}
