namespace SharedKernel.Communication.Rest.ProblemDetails;

/// <summary>
/// Internal DTO for deserializing RFC 9457 Problem Details JSON responses, including the extension
/// members <c>14.Presentation</c>'s <c>ErrorProblemDetailsExtensions</c>/
/// <c>ValidationProblemDetailsExtensions</c> add: <c>errorCode</c> (a copy of the originating
/// <c>Error.Code</c> — <c>Title</c> already carries the same value, but <c>errorCode</c> is the more
/// explicit source), and, only when the failure aggregates several field errors, <c>errors</c> and
/// <c>errorCodes</c>. Both of those are keyed by field path, or by code for an error that names no
/// field; <c>errors</c> holds the messages and <c>errorCodes</c> the codes of the same errors in the
/// same order. All extension members are serialized as flat top-level JSON properties by ASP.NET
/// Core's <c>ProblemDetails</c> converter, not nested under an <c>"extensions"</c> object.
/// </summary>
internal sealed class ProblemDetailsDto
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public int? Status { get; set; }
    public string? Detail { get; set; }
    public string? Instance { get; set; }
    public string? ErrorCode { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
    public Dictionary<string, string[]>? ErrorCodes { get; set; }
}
