namespace SharedKernel.Presentation.WebApi.Idempotency;

/// <summary>
/// Endpoint metadata stating that the endpoint accepts an optional <c>Idempotency-Key</c> request header: a request
/// may leave it out, but a key it sends must be valid. <c>UseSharedKernelWebApi()</c> enforces it for every endpoint
/// that carries it (400 before the endpoint runs); tooling such as the OpenAPI add-on reads it to document the header
/// as optional.
/// </summary>
/// <remarks>
/// Added by <see cref="AcceptIdempotencyKeyAttribute"/>, the <c>AcceptIdempotencyKey()</c> convention and a nullable
/// <see cref="IdempotencyKey"/> parameter. On an endpoint that also carries
/// <see cref="IIdempotencyKeyRequiredMetadata"/>, the requirement wins.
/// </remarks>
public interface IIdempotencyKeyAcceptedMetadata;
