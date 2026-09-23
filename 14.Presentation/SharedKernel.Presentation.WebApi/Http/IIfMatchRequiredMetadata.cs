namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>
/// Endpoint metadata stating that the endpoint requires an <c>If-Match</c> request header. Present on every endpoint
/// marked with <c>RequireIfMatch()</c> or <see cref="RequireIfMatchAttribute"/>; tooling such as the OpenAPI add-on
/// reads it to document the header and the 412 and 428 responses.
/// </summary>
public interface IIfMatchRequiredMetadata;
