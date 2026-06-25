using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.WebApi.Results;

/// <summary>
/// Converts <see cref="Result"/>/<see cref="Result{T}"/> outcomes into HTTP responses.
/// </summary>
/// <remarks>
/// <para>
/// This is the canonical <c>Result&lt;T&gt;</c>→HTTP mapping — inline
/// <c>if (result.IsSuccess) ... else ...</c> in endpoint or controller code is a platform
/// violation, mirroring the WO-026 P-166/167 precedent for <c>Result&lt;T&gt;</c>→<c>Envelope&lt;T&gt;</c>
/// mapping.
/// </para>
/// <para>
/// Distinct purpose from <c>SharedKernel.Contracts.Mapping.ResultEnvelopeExtensions</c>
/// (<c>04.Contracts</c>): <c>Envelope&lt;T&gt;</c> is a wire DTO for service-to-service payloads;
/// <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/> is the RFC 9457 HTTP <em>error</em>
/// response shape. A REST endpoint may combine both or use <c>ProblemDetails</c> alone — this
/// package does not decide that policy, it only supplies the conversion primitives.
/// </para>
/// <para>
/// The Minimal API overloads return <see cref="IResult"/>; the MVC overloads return
/// <see cref="ActionResult"/>/<see cref="ActionResult{TValue}"/>. There is no third
/// "auto-detect host model" overload — callers pick the form matching their hosting model
/// explicitly.
/// </para>
/// </remarks>
public static class ResultHttpExtensions
{
    /// <summary>
    /// Converts a non-generic <see cref="Result"/> to a Minimal API <see cref="IResult"/>.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>
    /// <see cref="Microsoft.AspNetCore.Http.Results.NoContent"/> (204) on success; otherwise the
    /// failure's <see cref="SharedKernel.Primitives.Errors.Error"/> converted via
    /// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails"/> and returned through
    /// <see cref="Microsoft.AspNetCore.Http.Results.Problem(ProblemDetails)"/>.
    /// </returns>
    public static IResult ToProblemDetailsResult(this Result result)
    {
        if (result.IsSuccess)
            return Microsoft.AspNetCore.Http.Results.NoContent();

        return Microsoft.AspNetCore.Http.Results.Problem(result.Error.ToProblemDetails());
    }

    /// <summary>
    /// Converts a <see cref="Result{T}"/> to a Minimal API <see cref="IResult"/>.
    /// </summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="onSuccess">
    /// An optional projection applied to the success value. Defaults to
    /// <see cref="Microsoft.AspNetCore.Http.Results.Ok{TValue}(TValue)"/> when omitted.
    /// </param>
    /// <returns>
    /// The result of <paramref name="onSuccess"/> on success; otherwise the failure's
    /// <see cref="SharedKernel.Primitives.Errors.Error"/> converted via
    /// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails"/> and returned through
    /// <see cref="Microsoft.AspNetCore.Http.Results.Problem(ProblemDetails)"/>.
    /// </returns>
    public static IResult ToProblemDetailsResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        if (result.IsSuccess)
        {
            return onSuccess is not null
                ? onSuccess(result.Value)
                : Microsoft.AspNetCore.Http.Results.Ok(result.Value);
        }

        return Microsoft.AspNetCore.Http.Results.Problem(result.Error.ToProblemDetails());
    }

    /// <summary>
    /// Converts a non-generic <see cref="Result"/> to an MVC <see cref="ActionResult"/>.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>
    /// A 204 No Content <see cref="ActionResult"/> on success; otherwise an
    /// <see cref="ObjectResult"/> wrapping the failure's
    /// <see cref="SharedKernel.Primitives.Errors.Error"/> converted via
    /// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails"/>.
    /// </returns>
    public static ActionResult ToActionResult(this Result result)
    {
        if (result.IsSuccess)
            return new NoContentResult();

        var problemDetails = result.Error.ToProblemDetails();
        return new ObjectResult(problemDetails) { StatusCode = problemDetails.Status };
    }

    /// <summary>
    /// Converts a <see cref="Result{T}"/> to an MVC <see cref="ActionResult{TValue}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>
    /// A 200 OK <see cref="ActionResult{TValue}"/> wrapping the success value; otherwise an
    /// <see cref="ObjectResult"/> wrapping the failure's
    /// <see cref="SharedKernel.Primitives.Errors.Error"/> converted via
    /// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails"/>.
    /// </returns>
    public static ActionResult<T> ToActionResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return new ActionResult<T>(result.Value);

        var problemDetails = result.Error.ToProblemDetails();
        return new ActionResult<T>(new ObjectResult(problemDetails) { StatusCode = problemDetails.Status });
    }
}
