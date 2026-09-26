using Microsoft.OpenApi;
using SharedKernel.Presentation.WebApi;

namespace SharedKernel.Presentation.OpenApi.Documents;

/// <summary>
/// The <c>ProblemDetails</c> schema component: the RFC 9457 members and the extension members every error response of
/// a SharedKernel service carries (<see cref="ProblemDetailsExtensionNames"/>).
/// </summary>
/// <remarks>
/// The component replaces one the framework generates from <c>Microsoft.AspNetCore.Mvc.ProblemDetails</c> for an
/// endpoint that declares <c>ProducesProblem(…)</c>, so every reference to <c>ProblemDetails</c> in a document
/// describes the same, complete shape.
/// </remarks>
internal static class ProblemDetailsSchema
{
    /// <summary>The component id, referenced as <c>#/components/schemas/ProblemDetails</c>.</summary>
    public const string Id = "ProblemDetails";

    // RFC 9457 section 3.1 member names.
    private const string TypeMember = "type";
    private const string TitleMember = "title";
    private const string StatusMember = "status";
    private const string DetailMember = "detail";
    private const string InstanceMember = "instance";

    // Members of the exception object, written in Development only.
    private const string ExceptionTypeMember = "type";
    private const string ExceptionMessageMember = "message";
    private const string ExceptionStackTraceMember = "stackTrace";

    private const string Int32Format = "int32";

    /// <summary>Returns a new schema; a document owns and may change its instance, so none is shared.</summary>
    public static OpenApiSchema Create() => new()
    {
        Type = JsonSchemaType.Object,
        Description = "An RFC 9457 problem: the body of every error response, with the media type application/problem+json.",
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            [TypeMember] = Text("A URI reference that identifies the kind of problem."),
            [TitleMember] = Text("A short summary of the kind of problem: the reason phrase of the status."),
            [StatusMember] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = Int32Format,
                Description = "The HTTP status code.",
            },
            [DetailMember] = Text("What went wrong, written for the client. A server error is described generically."),
            [InstanceMember] = Text("The path of the request."),
            [ProblemDetailsExtensionNames.ErrorCode] = Text(
                "The stable code of the error, such as not_found.default; http.{status} when the framework answered without an error."),
            [ProblemDetailsExtensionNames.TraceId] = Text("The W3C trace context of the request, the id to quote to support."),
            [ProblemDetailsExtensionNames.CorrelationId] = Text(
                "The correlation id of the request, also sent in the X-Correlation-Id response header."),
            [ProblemDetailsExtensionNames.Errors] = TextListPerKey(
                "For each invalid field (or error code, when an error names no field), the messages."),
            [ProblemDetailsExtensionNames.ErrorCodes] = TextListPerKey(
                "The same keys as errors, each with the codes of the same errors in the same order."),
            [ProblemDetailsExtensionNames.Exception] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Development only: the unhandled exception.",
                Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                {
                    [ExceptionTypeMember] = Text("The exception type."),
                    [ExceptionMessageMember] = Text("The exception message."),
                    [ExceptionStackTraceMember] = Text("The stack trace."),
                },
            },
        },
        Required = new HashSet<string>(StringComparer.Ordinal)
        {
            StatusMember,
            ProblemDetailsExtensionNames.ErrorCode,
            ProblemDetailsExtensionNames.TraceId,
        },
    };

    private static OpenApiSchema Text(string description) => new() { Type = JsonSchemaType.String, Description = description };

    private static OpenApiSchema TextListPerKey(string description) => new()
    {
        Type = JsonSchemaType.Object,
        Description = description,
        AdditionalProperties = new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            Items = new OpenApiSchema { Type = JsonSchemaType.String },
        },
    };
}
