namespace SharedKernel.Presentation.WebApi.Idempotency;

/// <summary>
/// Endpoint metadata stating that the endpoint requires an <c>Idempotency-Key</c> request header. Present on every
/// endpoint marked with <c>RequireIdempotencyKey()</c> or <see cref="RequireIdempotencyKeyAttribute"/>, so tooling —
/// such as the OpenAPI add-on — can document the header.
/// </summary>
public interface IIdempotencyKeyRequiredMetadata;
