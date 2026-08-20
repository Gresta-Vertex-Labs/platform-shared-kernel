namespace SharedKernel.Presentation.WebApi.Idempotency;

/// <summary>
/// Declares that an endpoint requires the inbound request to carry a valid
/// <see cref="HttpContextIdempotencyExtensions.IdempotencyKeyHeader"/> header.
/// </summary>
/// <remarks>
/// Usable directly on an MVC controller/action — MVC auto-surfaces attributes as endpoint
/// metadata — or attached to a Minimal API endpoint via
/// <c>RouteHandlerBuilder.WithMetadata(new RequireIdempotencyKeyAttribute())</c>. See
/// <see cref="IdempotencyEndpointFilterExtensions.RequireIdempotencyKey(Microsoft.AspNetCore.Builder.RouteHandlerBuilder)"/>
/// for the equivalent Minimal API sugar. Evaluated by
/// <see cref="IdempotencyKeyRequirementEndpointFilter"/> — a filter deliberately separate from
/// <see cref="Authorization.AuthorizationRequirementEndpointFilter"/>, since idempotency-key
/// presence is a request-shape concern, not an authorization concern.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireIdempotencyKeyAttribute : Attribute;
