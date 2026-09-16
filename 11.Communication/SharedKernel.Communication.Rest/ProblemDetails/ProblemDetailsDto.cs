namespace SharedKernel.Communication.Rest.ProblemDetails;

/// <summary>
/// Internal DTO for deserializing RFC 9457 Problem Details JSON responses, including the two
/// extension members <c>14.Presentation</c>'s <c>ErrorProblemDetailsExtensions</c>/
/// <c>ValidationProblemDetailsExtensions</c> always add: <c>errorCode</c> (a copy of the originating
/// <c>Error.Code</c> — <c>Title</c> already carries the same value, but <c>errorCode</c> is the more
/// explicit source) and <c>errors</c> (present only when the failure aggregates several field errors,
/// grouped by code). Both extension members are serialized as flat top-level JSON properties by
/// ASP.NET Core's <c>ProblemDetails</c> converter, not nested under an <c>"extensions"</c> object.
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
}
