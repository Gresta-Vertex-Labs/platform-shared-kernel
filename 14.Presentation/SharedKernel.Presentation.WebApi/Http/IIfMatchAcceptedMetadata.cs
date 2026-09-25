namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Endpoint metadata stating that the endpoint accepts an optional <c>If-Match</c> request header: a request may leave
/// it out, but one it sends must name one strong entity tag. <c>UseSharedKernelWebApi()</c> enforces it for every
/// endpoint that carries it (400 or 412 before the endpoint runs); tooling such as the OpenAPI add-on reads it to
/// document the header as optional and those responses.
/// </summary>
/// <remarks>
/// Added by <see cref="AcceptIfMatchAttribute"/>, the <c>AcceptIfMatch()</c> convention and a nullable
/// <see cref="IfMatch{TVersion}"/> parameter. On an endpoint that also carries <see cref="IIfMatchRequiredMetadata"/>,
/// the requirement wins.
/// </remarks>
internal interface IIfMatchAcceptedMetadata;
