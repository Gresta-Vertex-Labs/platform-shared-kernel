using Microsoft.AspNetCore.Mvc;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Maps a <see cref="Result"/> or <see cref="Result{T}"/> to an MVC action result. Failures are written exactly
/// like <see cref="ErrorHttpResult"/>: an RFC 9457 <c>application/problem+json</c> response in the request's language.
/// </summary>
public static class ResultActionResultExtensions
{
    /// <summary>Maps success to 204 No Content.</summary>
    /// <param name="result">The result.</param>
    /// <returns>204 No Content, or the error as a problem.</returns>
    public static IActionResult ToActionResult(this Result result) =>
        result.IsSuccess ? new NoContentResult() : new HttpResultActionResult(new ErrorHttpResult(result.Error));

    /// <summary>Maps success to 200 OK with the value as body.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>200 OK with the value, or the error as a problem.</returns>
    public static ActionResult<T> ToActionResult<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? new ActionResult<T>(result.Value)
            : new ActionResult<T>(new HttpResultActionResult(new ErrorHttpResult(result.Error)));
    }

    /// <summary>Maps success to the action result <paramref name="onSuccess"/> builds from the value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="onSuccess">Builds the success result, such as <c>value =&gt; CreatedAtAction(…)</c>.</param>
    /// <returns>The success result, or the error as a problem.</returns>
    public static IActionResult ToActionResult<T>(this Result<T> result, Func<T, IActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess
            ? onSuccess(result.Value)
            : new HttpResultActionResult(new ErrorHttpResult(result.Error));
    }
}
