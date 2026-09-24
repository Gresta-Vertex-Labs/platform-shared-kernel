namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>
/// Endpoint metadata stating that the endpoint requires an <c>If-Match</c> request header naming one strong entity
/// tag. <c>UseSharedKernelWebApi()</c> enforces it for every endpoint that carries it (428, 400 or 412 before the
/// endpoint runs); tooling such as the OpenAPI add-on reads it to document the header and those responses.
/// </summary>
/// <remarks>
/// Added by <see cref="RequireIfMatchAttribute"/>, the <c>RequireIfMatch()</c> convention and a not-null
/// <see cref="IfMatch{TVersion}"/> parameter. It wins over <see cref="IIfMatchAcceptedMetadata"/> on the same endpoint.
/// </remarks>
public interface IIfMatchRequiredMetadata;
