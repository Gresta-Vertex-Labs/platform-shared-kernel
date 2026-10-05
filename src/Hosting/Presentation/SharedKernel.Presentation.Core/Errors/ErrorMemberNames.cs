namespace SharedKernel.Presentation;

/// <summary>
/// The names under which every protocol carries an error's identifiers: the members of an HTTP problem response
/// (<c>SharedKernel.Presentation.WebApi</c>'s public <c>ProblemDetailsExtensionNames</c>) and the metadata keys of a
/// gRPC <c>ErrorInfo</c> detail, so the two never drift apart.
/// </summary>
internal static class ErrorMemberNames
{
    /// <summary><c>traceId</c>: the W3C trace id of the request.</summary>
    public const string TraceId = "traceId";

    /// <summary><c>correlationId</c>: the request's correlation id.</summary>
    public const string CorrelationId = "correlationId";
}
