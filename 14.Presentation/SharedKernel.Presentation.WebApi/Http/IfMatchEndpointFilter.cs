using Microsoft.AspNetCore.Http;

namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>The endpoint filter <c>RequireIfMatch()</c> adds: answers 428 before the handler runs when <c>If-Match</c> is missing.</summary>
internal sealed class IfMatchEndpointFilter : IEndpointFilter
{
    public static readonly IfMatchEndpointFilter Instance = new();

    private IfMatchEndpointFilter()
    {
    }

    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        EntityTags.CheckIfMatchPresent(context.HttpContext) is { } rejection
            ? ValueTask.FromResult<object?>(rejection)
            : next(context);
}
