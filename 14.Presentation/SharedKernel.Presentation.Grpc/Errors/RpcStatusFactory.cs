using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using RpcStatus = Google.Rpc.Status;

namespace SharedKernel.Presentation.Grpc.Errors;

/// <summary>
/// Builds the one <c>google.rpc.Status</c> every gRPC error of this package carries, so the exception interceptor
/// produces the same status for the same error however it reached it — thrown, or a failed result ended with
/// <c>SharedKernel.Core</c>'s <c>GetValueOrThrow()</c>/<c>ThrowIfFailure()</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>code</c>: <see cref="GrpcStatusCodeMap.Resolve"/>; <c>message</c>:
///   <see cref="ErrorPresentation.GetClientMessage"/> (translated, server errors redacted outside Development) — the
///   same text an HTTP client gets as <c>detail</c>.</item>
///   <item><c>ErrorInfo</c>: <c>reason</c> = the error code, <c>domain</c> = the configured error domain,
///   <c>metadata</c> = <c>traceId</c> and <c>correlationId</c> (the names of the HTTP problem members, with the same
///   values).</item>
///   <item><c>BadRequest</c>, when the error has field errors (<see cref="Error.Details"/>, as HTTP's <c>errors</c>):
///   one violation per field error, <c>field</c> = its field path (its code when it names no field, as HTTP's
///   <c>errors</c> keys), <c>description</c> = its client message, <c>reason</c> = its code. At most
///   <see cref="MaxFieldViolations"/> violations in at most <see cref="MaxFieldViolationBytes"/> bytes; when there
///   are more, one last violation coded <see cref="GrpcErrorCodes.MoreFieldViolations"/> says how many were left
///   out.</item>
/// </list>
/// The status travels in the <c>grpc-status-details-bin</c> trailer, where a client reads it with
/// <c>RpcException.GetRpcStatus()</c>.
/// </remarks>
internal static class RpcStatusFactory
{
    /// <summary>The most field violations one status lists, the summary of the rest not counted.</summary>
    public const int MaxFieldViolations = 50;

    /// <summary>
    /// The most bytes the listed field violations of one status take, encoded, the summary of the rest not counted.
    /// </summary>
    /// <remarks>
    /// The status is base64-encoded into the <c>grpc-status-details-bin</c> trailer. HTTP/2 clients cap the size of a
    /// response's headers and trailers — 8 KB by default for many gRPC clients, 64 KB for <c>Grpc.Net.Client</c> —
    /// and fail the call when a status is larger, so a status stays well under the smaller limit however many field
    /// errors there are: 3 KB of violations become about 4 KB of trailer.
    /// </remarks>
    public const int MaxFieldViolationBytes = 3 * 1024;

    /// <summary>
    /// The description of the violation that stands for the field errors a status leaves out, translatable under
    /// <see cref="GrpcErrorCodes.MoreFieldViolations"/> with a <c>{count}</c> placeholder. (Qualified: Google.Rpc has a
    /// <c>LocalizedMessage</c> detail of its own.)
    /// </summary>
    private static readonly Localization.LocalizedMessage<int> MoreFieldViolations =
        Localization.LocalizedMessage.Define<int>(GrpcErrorCodes.MoreFieldViolations, "… and {count} more.", "count");

    /// <summary>Builds the status for <paramref name="error"/>.</summary>
    /// <param name="error">The error.</param>
    /// <param name="httpContext">The call's request, or <see langword="null"/> outside a call (no translation, no correlation id, server errors redacted).</param>
    /// <param name="domain">The <c>ErrorInfo</c> domain.</param>
    /// <param name="statusCode">The status code; mapped from <see cref="Error.Type"/> when <see langword="null"/>.</param>
    /// <param name="clientMessage">The message; <see cref="ErrorPresentation.GetClientMessage"/> when <see langword="null"/>.</param>
    public static RpcStatus Create(
        Error error,
        HttpContext? httpContext,
        string domain,
        StatusCode? statusCode = null,
        string? clientMessage = null)
    {
        var status = new RpcStatus
        {
            Code = (int)(statusCode ?? GrpcStatusCodeMap.Resolve(error.Type)),
            Message = clientMessage ?? ErrorPresentation.GetClientMessage(error, httpContext),
        };

        status.Details.Add(Any.Pack(CreateErrorInfo(error.Code, httpContext, domain)));

        if (error.Details.Count > 0)
        {
            status.Details.Add(Any.Pack(CreateBadRequest(error.Details, httpContext)));
        }

        return status;
    }

    /// <summary>Builds the status for <paramref name="error"/> and wraps it in the <see cref="RpcException"/> that carries it.</summary>
    /// <inheritdoc cref="Create" path="/param"/>
    public static RpcException CreateException(
        Error error,
        HttpContext? httpContext,
        string domain,
        StatusCode? statusCode = null,
        string? clientMessage = null) =>
        Create(error, httpContext, domain, statusCode, clientMessage).ToRpcException();

    /// <summary>
    /// Builds the status for a status code this service did not build from an error — an <see cref="RpcException"/>
    /// of the service's own code or of a call it made to another service: the code and <paramref name="message"/>,
    /// and an <c>ErrorInfo</c> of this service (<c>reason</c> = <see cref="GrpcErrorCodes.ForStatus"/>, this domain,
    /// this call's trace and correlation ids). Nothing else of the original status is kept.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="message">The message, already redacted where it must be.</param>
    /// <param name="httpContext">The call's request, or <see langword="null"/> outside a call.</param>
    /// <param name="domain">The <c>ErrorInfo</c> domain.</param>
    public static RpcStatus CreateForStatus(StatusCode statusCode, string message, HttpContext? httpContext, string domain)
    {
        var status = new RpcStatus { Code = (int)statusCode, Message = message };
        status.Details.Add(Any.Pack(CreateErrorInfo(GrpcErrorCodes.ForStatus(statusCode), httpContext, domain)));
        return status;
    }

    /// <summary>Builds the status of <see cref="CreateForStatus"/> and wraps it in the <see cref="RpcException"/> that carries it.</summary>
    /// <inheritdoc cref="CreateForStatus" path="/param"/>
    public static RpcException CreateExceptionForStatus(StatusCode statusCode, string message, HttpContext? httpContext, string domain) =>
        CreateForStatus(statusCode, message, httpContext, domain).ToRpcException();

    private static ErrorInfo CreateErrorInfo(string reason, HttpContext? httpContext, string domain)
    {
        var errorInfo = new ErrorInfo { Reason = reason, Domain = domain };

        // The same trace id an HTTP problem carries: the W3C id of the current activity, else the request's own id.
        var traceId = Activity.Current?.Id ?? httpContext?.TraceIdentifier;
        if (!string.IsNullOrEmpty(traceId))
        {
            errorInfo.Metadata[ProblemDetailsExtensionNames.TraceId] = traceId;
        }

        if (httpContext?.GetCorrelationId() is { } correlationId)
        {
            errorInfo.Metadata[ProblemDetailsExtensionNames.CorrelationId] = correlationId;
        }

        return errorInfo;
    }

    private static BadRequest CreateBadRequest(IReadOnlyList<Error> fieldErrors, HttpContext? httpContext)
    {
        var badRequest = new BadRequest();
        var bytes = 0;

        // Only the violations that are sent are built (and translated): a request with thousands of invalid fields
        // costs no more than one with fifty.
        foreach (var fieldError in fieldErrors)
        {
            if (badRequest.FieldViolations.Count == MaxFieldViolations)
            {
                break;
            }

            var violation = CreateFieldViolation(fieldError, httpContext);
            var size = EncodedSize(violation);
            if (bytes + size > MaxFieldViolationBytes)
            {
                break;
            }

            badRequest.FieldViolations.Add(violation);
            bytes += size;
        }

        var omitted = fieldErrors.Count - badRequest.FieldViolations.Count;
        if (omitted > 0)
        {
            badRequest.FieldViolations.Add(CreateFieldViolation(MoreFieldViolations.ToError(ErrorType.Validation, omitted), httpContext));
        }

        return badRequest;
    }

    private static BadRequest.Types.FieldViolation CreateFieldViolation(Error fieldError, HttpContext? httpContext) => new()
    {
        Field = FieldOf(fieldError),
        Description = ErrorPresentation.GetClientMessage(fieldError, httpContext),
        Reason = fieldError.Code,
    };

    // What one violation adds to the encoded BadRequest: the one-byte tag of its repeated field, its length, itself.
    private static int EncodedSize(BadRequest.Types.FieldViolation violation) =>
        1 + CodedOutputStream.ComputeMessageSize(violation);

    private static string FieldOf(Error error) =>
        error.MessageArguments.TryGetValue(ErrorArgumentNames.PropertyPath, out var path) && path is string { Length: > 0 } field
            ? field
            : error.Code;
}
