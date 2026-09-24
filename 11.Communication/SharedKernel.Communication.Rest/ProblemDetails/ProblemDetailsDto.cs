namespace SharedKernel.Communication.Rest.ProblemDetails;

/// <summary>
/// Internal DTO holding the members of an RFC 9457 Problem Details body that
/// <see cref="ProblemDetailsDeserializer"/> reads: <c>detail</c> and the extension members
/// <c>14.Presentation</c> adds. Those are <c>errorCode</c> (the originating <c>Error.Code</c>) and, when the
/// failure lists field errors, <c>errors</c> and <c>errorCodes</c>. Both of those are keyed by field path, or by
/// code for an error that names no field; <c>errors</c> holds the messages and <c>errorCodes</c> the codes of the
/// same errors in the same order. ASP.NET Core's <c>ProblemDetails</c> converter writes extension members as flat
/// top-level JSON properties, not nested under an <c>"extensions"</c> object.
/// </summary>
/// <remarks>
/// The standard <c>type</c>, <c>title</c>, <c>status</c> and <c>instance</c> members are deliberately not bound,
/// so nothing here can read them. <c>type</c> is a URI and <c>title</c> the status reason phrase (P-562), free
/// text from a service outside the platform, so neither is ever the error code. The status comes from the
/// response itself.
/// </remarks>
internal sealed class ProblemDetailsDto
{
    public string? Detail { get; set; }
    public string? ErrorCode { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
    public Dictionary<string, string[]>? ErrorCodes { get; set; }
}
