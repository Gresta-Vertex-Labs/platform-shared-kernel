using Microsoft.AspNetCore.Mvc.Filters;
using SharedKernel.Presentation.WebApi.Errors;

namespace SharedKernel.Presentation.WebApi.Idempotency;

/// <summary>
/// Requires an <c>Idempotency-Key</c> request header on an MVC controller or action. For minimal APIs use
/// <c>RequireIdempotencyKey()</c>.
/// </summary>
/// <remarks>
/// <para>
/// The attribute is its own action filter, so nothing else needs registering. A request without the header is
/// answered 400 <c>idempotency.key_required</c>; a key that is not 1 to 256 visible ASCII characters (after removing
/// one pair of surrounding double quotes) is answered 400 <c>idempotency.key_invalid</c>.
/// </para>
/// <para>
/// This checks the header only. Read the key with <c>HttpContext.GetIdempotencyKey()</c> and pass it to the command
/// (<c>IIdempotentRequest.IdempotencyKey</c>), whose pipeline reserves it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireIdempotencyKeyAttribute : Attribute, IAsyncActionFilter, IIdempotencyKeyRequiredMetadata
{
    /// <inheritdoc />
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (IdempotencyKeyGuard.Check(context.HttpContext) is { } rejection)
        {
            context.Result = new HttpResultActionResult(rejection);
            return Task.CompletedTask;
        }

        return next();
    }
}
