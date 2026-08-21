using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;

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
public sealed partial class IdempotencyKeyRequirementEndpointFilter : IEndpointFilter
{
    private const string MissingIdempotencyKeyErrorCode = "Idempotency.KeyRequired";

    private readonly ILogger<IdempotencyKeyRequirementEndpointFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyKeyRequirementEndpointFilter"/> class.
    /// </summary>
    /// <param name="logger">
    /// The logger used to record rejection-path security audit events. When no
    /// <see cref="ILogger{TCategoryName}"/> is registered in the container, a no-op
    /// <see cref="NullLogger{T}"/> is used instead — this filter never fails to construct merely
    /// because logging was not configured.
    /// </param>
    public IdempotencyKeyRequirementEndpointFilter(ILogger<IdempotencyKeyRequirementEndpointFilter>? logger = null)
    {
        _logger = logger ?? NullLogger<IdempotencyKeyRequirementEndpointFilter>.Instance;
    }

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

            var endpointDisplayName = context.HttpContext.GetEndpoint()?.DisplayName ?? "(unknown endpoint)";
            Log.IdempotencyKeyRequirementRejected(_logger, endpointDisplayName);

            return Microsoft.AspNetCore.Http.Results.Problem(error.ToProblemDetails(context.HttpContext));
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Source-generated log messages for <see cref="IdempotencyKeyRequirementEndpointFilter"/>.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 3,
            Level = LogLevel.Warning,
            Message = "Missing or malformed idempotency key rejected the request to endpoint {EndpointDisplayName}.")]
        public static partial void IdempotencyKeyRequirementRejected(ILogger logger, string endpointDisplayName);
    }
}
