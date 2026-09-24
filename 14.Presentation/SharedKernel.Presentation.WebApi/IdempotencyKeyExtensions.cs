using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi.Idempotency;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Requires and reads the <c>Idempotency-Key</c> request header.</summary>
public static class IdempotencyKeyExtensions
{
    /// <summary>
    /// Requires an <c>Idempotency-Key</c> header on the endpoints of <paramref name="builder"/>, rejecting a request
    /// without a valid key with 400 before the handler runs. Nothing else needs registering.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">An endpoint, group or controller mapping.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    /// <remarks>
    /// Adds <see cref="RequireIdempotencyKeyAttribute"/> as endpoint metadata (<see cref="IIdempotencyKeyRequiredMetadata"/>),
    /// which <c>UseSharedKernelWebApi()</c> enforces for every kind of endpoint and the OpenAPI add-on documents. A
    /// missing header is answered <c>idempotency.key_required</c>; a key that is not 1 to 256 visible ASCII characters
    /// (after removing one pair of surrounding double quotes) <c>idempotency.key_invalid</c>.
    /// </remarks>
    public static TBuilder RequireIdempotencyKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new RequireIdempotencyKeyAttribute());
    }

    /// <summary>Returns the idempotency key of the request.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>
    /// The <c>Idempotency-Key</c> header without surrounding double quotes when it is 1 to 256 visible ASCII
    /// characters; otherwise <see langword="null"/>.
    /// </returns>
    public static string? GetIdempotencyKey(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return IdempotencyKeyGuard.GetKey(httpContext);
    }
}
