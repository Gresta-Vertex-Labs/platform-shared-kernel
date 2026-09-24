using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi.Idempotency;

namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>
/// Enforces the request headers an endpoint requires or accepts through its metadata —
/// <see cref="IIdempotencyKeyRequiredMetadata"/>, <see cref="IIdempotencyKeyAcceptedMetadata"/>,
/// <see cref="IIfMatchRequiredMetadata"/> and <see cref="IIfMatchAcceptedMetadata"/> — after authorization and before
/// the endpoint runs.
/// </summary>
/// <remarks>
/// <para>
/// One place for every kind of endpoint: the metadata comes from an attribute on a minimal-API handler or an MVC
/// action or controller, from the <c>RequireIdempotencyKey()</c>, <c>AcceptIdempotencyKey()</c>,
/// <c>RequireIfMatch()</c> and <c>AcceptIfMatch()</c> conventions, or from an <see cref="IdempotencyKey"/> or
/// <see cref="IfMatch{TVersion}"/> parameter. Running after authorization means an unauthenticated caller is told to
/// authenticate (401), never which headers it forgot. A gRPC call gets the status without a body.
/// </para>
/// <para>
/// A required header must be present and valid. An accepted one may be missing, but one the request sends is validated
/// the same way, so a header the endpoint cannot use is refused rather than read as missing. When an endpoint carries
/// both, the requirement wins.
/// </para>
/// </remarks>
internal sealed class HeaderRequirementsMiddleware
{
    private readonly RequestDelegate _next;

    public HeaderRequirementsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata;
        if (metadata is null)
        {
            return _next(context);
        }

        var rejection = CheckIdempotencyKey(context, metadata) ?? CheckIfMatch(context, metadata);
        if (rejection is null)
        {
            return _next(context);
        }

        if (RequestFacts.IsGrpcRequest(context))
        {
            context.Response.StatusCode = (rejection as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }

        return rejection.ExecuteAsync(context);
    }

    private static IResult? CheckIdempotencyKey(HttpContext context, EndpointMetadataCollection metadata)
    {
        if (metadata.GetMetadata<IIdempotencyKeyRequiredMetadata>() is not null)
        {
            return IdempotencyKeyGuard.Check(context, required: true);
        }

        return metadata.GetMetadata<IIdempotencyKeyAcceptedMetadata>() is not null
            ? IdempotencyKeyGuard.Check(context, required: false)
            : null;
    }

    private static IResult? CheckIfMatch(HttpContext context, EndpointMetadataCollection metadata)
    {
        if (metadata.GetMetadata<IIfMatchRequiredMetadata>() is not null)
        {
            return EntityTags.CheckIfMatch(context, metadata, required: true);
        }

        return metadata.GetMetadata<IIfMatchAcceptedMetadata>() is not null
            ? EntityTags.CheckIfMatch(context, metadata, required: false)
            : null;
    }
}
