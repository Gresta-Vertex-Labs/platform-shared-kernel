using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Runs an <see cref="IResult"/> as an MVC action result, so MVC failures take the minimal-API path.</summary>
internal sealed class HttpResultActionResult : ActionResult, IStatusCodeActionResult
{
    private readonly IResult _result;

    public HttpResultActionResult(IResult result)
    {
        _result = result;
    }

    /// <inheritdoc />
    public int? StatusCode => (_result as IStatusCodeHttpResult)?.StatusCode;

    /// <inheritdoc />
    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _result.ExecuteAsync(context.HttpContext);
    }
}
