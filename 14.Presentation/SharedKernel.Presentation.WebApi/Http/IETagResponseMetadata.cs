namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>
/// Endpoint metadata stating that responses of the endpoint carry an <c>ETag</c> header: the version of the resource
/// they describe, as a strong entity tag. Tooling such as the OpenAPI add-on reads it to document the header.
/// </summary>
/// <remarks>
/// Added by <see cref="OkWithETag{TValue}"/> for an endpoint that returns it: for 200, and for 304 when the endpoint
/// answers <c>GET</c> or <c>HEAD</c>.
/// </remarks>
public interface IETagResponseMetadata
{
    /// <summary>Gets the status codes of the responses that carry the <c>ETag</c> header.</summary>
    IReadOnlyList<int> StatusCodes { get; }
}
