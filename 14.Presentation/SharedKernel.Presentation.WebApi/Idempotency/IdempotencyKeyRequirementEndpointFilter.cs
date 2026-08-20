using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi.Idempotency;

/// <summary>
/// Global endpoint filter that enforces <see cref="RequireIdempotencyKeyAttribute"/> metadata
/// attached to an endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Safe to register on every route — this filter reads endpoint metadata and no-ops (calls
/// <c>next(context)</c> immediately, resolving nothing) when the attribute is absent. Mirrors
/// <see cref="Authorization.AuthorizationRequirementEndpointFilter"/>'s exact
/// "global registration, no-op when inapplicable" shape, but is a deliberately <b>separate</b>
/// filter type: idempotency-key presence is a request-shape concern, not an authorization
/// concern, and must never be folded into the authorization filter or its attribute set.
/// </para>
/// <para>
/// A missing or malformed key on an annotated endpoint short-circuits — <c>next()</c> is never
/// called — with <see cref="Error.Validation(string, string)"/> converted via
/// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails"/> through
/// <see cref="Microsoft.AspNetCore.Http.Results.Problem(Microsoft.AspNetCore.Mvc.ProblemDetails)"/>
/// (400) — the existing single-<see cref="Error"/> path, since idempotency-key absence is a
/// request-shape validation failure, not a new <see cref="ErrorType"/>.
/// </para>
/// </remarks>
public sealed class IdempotencyKeyRequirementEndpointFilter : IEndpointFilter
{
    private const string MissingIdempotencyKeyErrorCode = "Idempotency.KeyRequired";

    /// <inheritdoc/>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var metadata = context.HttpContext.GetEndpoint()?.Metadata;
        var isRequired = metadata?.GetMetadata<RequireIdempotencyKeyAttribute>() is not null;

        if (!isRequired)
        {
            return await next(context).ConfigureAwait(false);
        }

        if (!context.HttpContext.TryGetIdempotencyKey(out _))
        {
            var error = Error.Validation(
                MissingIdempotencyKeyErrorCode,
                $"The '{HttpContextIdempotencyExtensions.IdempotencyKeyHeader}' header is required and must be "
                    + $"a non-empty value of at most {HttpContextIdempotencyExtensions.MaxIdempotencyKeyLength} characters.");

            return Microsoft.AspNetCore.Http.Results.Problem(error.ToProblemDetails(context.HttpContext));
        }

        return await next(context).ConfigureAwait(false);
    }
}
