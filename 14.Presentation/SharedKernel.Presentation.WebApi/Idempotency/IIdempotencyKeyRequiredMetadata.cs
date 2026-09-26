namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Endpoint metadata stating that the endpoint requires an <c>Idempotency-Key</c> request header.
/// <c>UseSharedKernelWebApi()</c> enforces it for every endpoint that carries it (400 before the endpoint runs);
/// tooling such as the OpenAPI add-on reads it to document the header.
/// </summary>
/// <remarks>
/// Added by <see cref="RequireIdempotencyKeyAttribute"/>, the <c>RequireIdempotencyKey()</c> convention and a not-null
/// <see cref="IdempotencyKey"/> parameter. It wins over <see cref="IIdempotencyKeyAcceptedMetadata"/> on the same
/// endpoint.
/// </remarks>
internal interface IIdempotencyKeyRequiredMetadata;
