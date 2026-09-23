using Microsoft.AspNetCore.Http;

namespace SharedKernel.Presentation.WebApi.Idempotency;

/// <summary>The endpoint filter <c>RequireIdempotencyKey()</c> adds: rejects a request without a valid key before the handler runs.</summary>
internal sealed class IdempotencyKeyEndpointFilter : IEndpointFilter
{
    public static readonly IdempotencyKeyEndpointFilter Instance = new();

    private IdempotencyKeyEndpointFilter()
    {
    }

    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        IdempotencyKeyGuard.Check(context.HttpContext) is { } rejection
            ? ValueTask.FromResult<object?>(rejection)
            : next(context);
}
