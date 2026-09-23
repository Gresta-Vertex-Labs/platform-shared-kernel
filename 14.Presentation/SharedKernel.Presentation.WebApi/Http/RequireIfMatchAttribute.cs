using Microsoft.AspNetCore.Mvc.Filters;
using SharedKernel.Presentation.WebApi.Errors;

namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>
/// Requires an <c>If-Match</c> request header on an MVC controller or action, for optimistic concurrency over HTTP.
/// For minimal APIs use <c>RequireIfMatch()</c>.
/// </summary>
/// <remarks>
/// <para>
/// The attribute is its own action filter, so nothing else needs registering. A request without a usable
/// <c>If-Match</c> is answered 428 Precondition Required (<c>precondition.required</c>) before the action runs.
/// </para>
/// <para>
/// Read the version with <c>HttpContext.GetIfMatch()</c> and pass it to the update, for example through
/// <c>EntityVersion.TryParse</c>. On such an endpoint a <see cref="Primitives.Errors.ErrorType.Conflict"/> failure —
/// returned or thrown — is answered 412 Precondition Failed (RFC 9110 section 13.1.1) with its own error code.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireIfMatchAttribute : Attribute, IAsyncActionFilter, IIfMatchRequiredMetadata
{
    /// <inheritdoc />
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (EntityTags.CheckIfMatchPresent(context.HttpContext) is { } rejection)
        {
            context.Result = new HttpResultActionResult(rejection);
            return Task.CompletedTask;
        }

        return next();
    }
}
