using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Core.Exceptions;

namespace SharedKernel.Presentation.WebApi.Errors;

/// <summary>
/// Converts a <see cref="ValidationException"/> — which carries every failing field's
/// <see cref="SharedKernel.Primitives.Errors.Error"/>, not just one — into an RFC 9457
/// <see cref="ProblemDetails"/> response body that preserves the full multi-field error set.
/// </summary>
/// <remarks>
/// <para>
/// Fixes a confirmed silent-data-loss defect: the single-<c>Error</c>
/// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails(SharedKernel.Primitives.Errors.Error, HttpContext?)"/>
/// path only ever sees <see cref="ValidationException"/>'s inherited <c>Error</c> property — which
/// <see cref="ValidationException"/>'s own constructor sets to <c>Errors[0]</c> — so a request
/// failing validation on three fields previously produced a body naming only one.
/// </para>
/// <para>
/// <c>ValidationException</c> is the one case where a single <c>Error</c> is insufficient. This
/// extension is additive to, never a replacement for, <see cref="ErrorProblemDetailsExtensions"/>:
/// it reuses <see cref="ErrorProblemDetailsExtensions.ToProblemDetails(SharedKernel.Primitives.Errors.Error, HttpContext?)"/>
/// for the base shape (<c>Title</c>/<c>Detail</c>/<c>Status</c>/<c>Type</c>/
/// <c>Extensions["errorCode"]</c>/<c>Extensions["traceId"]</c> — all identical to what the
/// single-<c>Error</c> path would already produce for <see cref="ValidationException"/>'s inherited <c>Error</c> property) and
/// additionally populates <c>Extensions["errors"]</c> with every failing field's messages and
/// <c>Extensions["errorCodes"]</c> with the codes of the same errors, in the same order. Both are
/// keyed by field path (each error's <see cref="SharedKernel.Primitives.Errors.ErrorArgumentNames.PropertyPath"/>
/// argument), or by <see cref="SharedKernel.Primitives.Errors.Error.Code"/> for an error that names
/// no field, so <c>errors</c> keeps ASP.NET Core's own built-in
/// <see cref="ValidationProblemDetails.Errors"/> shape and client tooling that already understands
/// that convention (form-binding libraries, generated SDKs) works unmodified. Built via
/// <see cref="LocalizedDetailResolver.AddErrorsExtensions"/> — the same helper
/// <see cref="ErrorProblemDetailsExtensions"/> uses for a <see cref="SharedKernel.Primitives.Errors.Error"/>
/// whose own <see cref="SharedKernel.Primitives.Errors.Error.Details"/> is non-empty (P-544), so a
/// <c>Result.Failure(Error.Validation(errors))</c> returned from the application layer and a thrown
/// <see cref="ValidationException"/> carrying the same field errors produce identical
/// <c>errors</c> and <c>errorCodes</c> members.
/// </para>
/// <para>
/// Each field's message is independently subject to the same optional localization step as
/// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails(SharedKernel.Primitives.Errors.Error, HttpContext?)"/>
/// (P-484/WO-078, see <see cref="LocalizedDetailResolver"/>) — one field may translate while a
/// sibling field falls back to its raw message in the same response body; this is never an
/// all-or-nothing decision for the whole body.
/// </para>
/// </remarks>
public static class ValidationProblemDetailsExtensions
{
    /// <summary>
    /// Maps the specified <paramref name="exception"/> to a <see cref="ProblemDetails"/> instance
    /// carrying every failing field's error, not just the first.
    /// </summary>
    /// <param name="exception">The validation exception to convert.</param>
    /// <param name="context">
    /// The current <see cref="HttpContext"/>, used to populate <c>Extensions["traceId"]</c> when
    /// <see cref="System.Diagnostics.Activity.Current"/> is unavailable (matches
    /// <c>ErrorProblemDetailsExtensions.ToProblemDetails</c>'s own resolution rule). May be
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// A <see cref="ProblemDetails"/> identical in shape to
    /// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails(SharedKernel.Primitives.Errors.Error, HttpContext?)"/>
    /// applied to <see cref="ValidationException"/>'s inherited <c>Error</c> property, plus
    /// <c>Extensions["errors"]</c> (field path, or code, to the failing messages) and
    /// <c>Extensions["errorCodes"]</c> (the same keys to the codes of those errors, index by
    /// index). Pure mapping — no logging, no I/O.
    /// </returns>
    public static ProblemDetails ToProblemDetails(this ValidationException exception, HttpContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var problemDetails = exception.Error.ToProblemDetails(context);

        // Each field's message is localized/falls back independently (P-484/WO-078, D-82) — one
        // field may translate while a sibling field falls back to its raw message in the same
        // response body; this is never an all-or-nothing decision. Delegates to the same helper
        // ErrorProblemDetailsExtensions uses for Error.Details, so both paths produce
        // identical "errors" and "errorCodes" members for the same input errors (P-544).
        LocalizedDetailResolver.AddErrorsExtensions(problemDetails, exception.Errors, context);

        return problemDetails;
    }
}
