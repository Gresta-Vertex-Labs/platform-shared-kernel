using System.Diagnostics;
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
/// Builds the one <c>google.rpc.Status</c> every gRPC error of this package carries, for the exception interceptor
/// and for <see cref="GrpcResultExtensions"/> alike, so both paths produce the same status for the same error.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>code</c>: <see cref="GrpcStatusCodeMap.Resolve"/>; <c>message</c>:
///   <see cref="ErrorPresentation.GetClientMessage"/> (translated, server errors redacted outside Development) — the
///   same text an HTTP client gets as <c>detail</c>.</item>
///   <item><c>ErrorInfo</c>: <c>reason</c> = the error code, <c>domain</c> = the configured error domain,
///   <c>metadata</c> = <c>traceId</c> and <c>correlationId</c> (the names of the HTTP problem members, with the same
///   values).</item>
///   <item><c>BadRequest</c>, when there are field errors: one violation per error, <c>field</c> = its field path (its
///   code when it names no field, as HTTP's <c>errors</c> keys), <c>description</c> = its client message,
///   <c>reason</c> = its code.</item>
/// </list>
/// The status travels in the <c>grpc-status-details-bin</c> trailer, where a client reads it with
/// <c>RpcException.GetRpcStatus()</c>.
/// </remarks>
internal static class RpcStatusFactory
{
    /// <summary>Builds the status for <paramref name="error"/>.</summary>
    /// <param name="error">The error.</param>
    /// <param name="httpContext">The call's request, or <see langword="null"/> outside a call (no translation, no correlation id, server errors redacted).</param>
    /// <param name="domain">The <c>ErrorInfo</c> domain.</param>
    /// <param name="fieldErrors">The field errors; <see cref="Error.Details"/> when <see langword="null"/>.</param>
    /// <param name="statusCode">The status code; mapped from <see cref="Error.Type"/> when <see langword="null"/>.</param>
    /// <param name="clientMessage">The message; <see cref="ErrorPresentation.GetClientMessage"/> when <see langword="null"/>.</param>
    public static RpcStatus Create(
        Error error,
        HttpContext? httpContext,
        string domain,
        IReadOnlyList<Error>? fieldErrors = null,
        StatusCode? statusCode = null,
        string? clientMessage = null)
    {
        var status = new RpcStatus
        {
            Code = (int)(statusCode ?? GrpcStatusCodeMap.Resolve(error.Type)),
            Message = clientMessage ?? ErrorPresentation.GetClientMessage(error, httpContext),
        };

        status.Details.Add(Any.Pack(CreateErrorInfo(error, httpContext, domain)));

        var fields = fieldErrors ?? error.Details;
        if (fields.Count > 0)
        {
            status.Details.Add(Any.Pack(CreateBadRequest(fields, httpContext)));
        }

        return status;
    }

    /// <summary>Builds the status for <paramref name="error"/> and wraps it in the <see cref="RpcException"/> that carries it.</summary>
    /// <inheritdoc cref="Create" path="/param"/>
    public static RpcException CreateException(
        Error error,
        HttpContext? httpContext,
        string domain,
        IReadOnlyList<Error>? fieldErrors = null,
        StatusCode? statusCode = null,
        string? clientMessage = null) =>
        Create(error, httpContext, domain, fieldErrors, statusCode, clientMessage).ToRpcException();

    private static ErrorInfo CreateErrorInfo(Error error, HttpContext? httpContext, string domain)
    {
        var errorInfo = new ErrorInfo { Reason = error.Code, Domain = domain };

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

        foreach (var fieldError in fieldErrors)
        {
            badRequest.FieldViolations.Add(new BadRequest.Types.FieldViolation
            {
                Field = FieldOf(fieldError),
                Description = ErrorPresentation.GetClientMessage(fieldError, httpContext),
                Reason = fieldError.Code,
            });
        }

        return badRequest;
    }

    private static string FieldOf(Error error) =>
        error.MessageArguments.TryGetValue(ErrorArgumentNames.PropertyPath, out var path) && path is string { Length: > 0 } field
            ? field
            : error.Code;
}
