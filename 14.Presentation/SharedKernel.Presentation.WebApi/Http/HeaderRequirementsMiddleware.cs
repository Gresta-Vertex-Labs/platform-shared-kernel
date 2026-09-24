using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi.Idempotency;

namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>
/// Enforces the request headers an endpoint requires through its metadata — <see cref="IIdempotencyKeyRequiredMetadata"/>
/// and <see cref="IIfMatchRequiredMetadata"/> — after authorization and before the endpoint runs.
/// </summary>
/// <remarks>
/// One place for every kind of endpoint: the metadata comes from an attribute on a minimal-API handler or an MVC
/// action or controller, from the <c>RequireIdempotencyKey()</c> and <c>RequireIfMatch()</c> conventions, or from an
/// <see cref="IdempotencyKey"/> or <see cref="IfMatch{TVersion}"/> parameter. Running after authorization means an
/// unauthenticated caller is told to authenticate (401), never which headers it forgot. A gRPC call gets the status
/// without a body.
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

        var rejection = metadata.GetMetadata<IIdempotencyKeyRequiredMetadata>() is null
            ? null
            : IdempotencyKeyGuard.Check(context);

        if (rejection is null && metadata.GetMetadata<IIfMatchRequiredMetadata>() is not null)
        {
            rejection = EntityTags.CheckRequiredIfMatch(context, metadata);
        }

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
}
