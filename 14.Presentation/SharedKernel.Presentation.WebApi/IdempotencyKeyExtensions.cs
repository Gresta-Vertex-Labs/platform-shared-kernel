using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi.Idempotency;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Requires or accepts, and reads, the <c>Idempotency-Key</c> request header.</summary>
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

    /// <summary>
    /// Accepts an optional <c>Idempotency-Key</c> header on the endpoints of <paramref name="builder"/>: a request
    /// without it goes on, and one with an invalid key is rejected with 400 before the handler runs. Nothing else needs
    /// registering.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">An endpoint, group or controller mapping.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    /// <remarks>
    /// Adds <see cref="AcceptIdempotencyKeyAttribute"/> as endpoint metadata (<see cref="IIdempotencyKeyAcceptedMetadata"/>),
    /// which <c>UseSharedKernelWebApi()</c> enforces for every kind of endpoint and the OpenAPI add-on documents as an
    /// optional header. A key that is not 1 to 256 visible ASCII characters (after removing one pair of surrounding
    /// double quotes) is answered <c>idempotency.key_invalid</c>. <see cref="RequireIdempotencyKey{TBuilder}"/> on the
    /// same endpoint wins.
    /// </remarks>
    public static TBuilder AcceptIdempotencyKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new AcceptIdempotencyKeyAttribute());
    }

    /// <summary>Returns the idempotency key of the request.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>
    /// The <c>Idempotency-Key</c> header without surrounding double quotes when it is 1 to 256 visible ASCII
    /// characters; otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// On an endpoint that requires or accepts the header — <c>RequireIdempotencyKey()</c>, <c>AcceptIdempotencyKey()</c>,
    /// their attributes or an <see cref="IdempotencyKey"/> parameter — it was validated before the handler ran, so
    /// <see langword="null"/> means the request sent none. Elsewhere <see langword="null"/> also covers an invalid key,
    /// and reading that as "no key" would run a retry twice: declare the header instead of reading it raw.
    /// </remarks>
    public static string? GetIdempotencyKey(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return IdempotencyKeyGuard.GetKey(httpContext);
    }
}
