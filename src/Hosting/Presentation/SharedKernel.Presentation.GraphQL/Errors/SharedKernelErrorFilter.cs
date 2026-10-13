using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.GraphQL.Diagnostics;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using Error = SharedKernel.Primitives.Errors.Error;

namespace SharedKernel.Presentation.GraphQL.Errors;

/// <summary>
/// The GraphQL form of the HTTP exception handler: turns every HotChocolate <see cref="IError"/> into an error that
/// carries the members of an RFC 9457 problem, from the same decisions the other protocols use.
/// </summary>
/// <remarks>
/// <para>
/// <b>Classification</b> is the HTTP handler's (<see cref="ExceptionClassification"/>): a
/// <c>ValidationException</c> is a validation error (one error is itself, several are <c>Error.Validation(errors)</c>),
/// any other <c>SharedKernelException</c> is its own <see cref="Error"/>, a <see cref="TimeoutException"/> or an
/// <see cref="OperationCanceledException"/> the client did not cause is <c>timeout.default</c>, and any other
/// exception is the generic <c>unexpected.default</c>. An <see cref="IError"/> with no exception — HotChocolate's own
/// syntax, validation and binding errors — is a client error: status 400, code
/// <see cref="GraphQLErrorCodes.RequestInvalid"/>, HotChocolate's own <c>code</c> extension kept as <c>code</c>.
/// </para>
/// <para>
/// <b>Members</b> written to the error's <c>extensions</c>: <c>status</c> (from <c>ErrorTypeStatusCodeMap</c>, the
/// table HTTP uses), <c>type</c> and <c>title</c> (the framework's RFC 9110 type URI and reason phrase for that status,
/// never the code or the message), <c>detail</c> (the client message: localized, and for a server error outside
/// Development a generic sentence), <c>errorCode</c>, <c>traceId</c>, <c>correlationId</c>, <c>errors</c>/<c>errorCodes</c>
/// for field errors (keyed like HTTP's) and, only in Development and only for an unclassified exception,
/// <c>exception</c> (type, message, stack trace). The error's top-level <c>message</c> is the client message too. There
/// is no <c>IncludeExceptionDetails</c> override: exception text never leaves Development.
/// </para>
/// <para>
/// Outside a request (an in-process executor) the environment is unknown and treated as production, nothing is
/// translated, and the trace id is the current activity's. Each error is logged once — 14400 at Error for a server
/// category, 14401 at Debug for a client category — with the exception, never with a message or a variable. An
/// exception thrown because the client went away is passed through and logged at Debug.
/// </para>
/// <para>Registered automatically by <c>AddSharedKernelGraphQL</c>.</para>
/// </remarks>
internal sealed class SharedKernelErrorFilter : IErrorFilter
{
    private const string StatusKey = "status";
    private const string TitleKey = "title";
    private const string DetailKey = "detail";
    private const string TypeKey = "type";
    private const string ErrorCodeKey = "errorCode";
    private const string TraceIdKey = "traceId";
    private const string CorrelationIdKey = "correlationId";
    private const string ErrorsKey = "errors";
    private const string ErrorCodesKey = "errorCodes";
    private const string ExceptionKey = "exception";

    // Logged only: an exception thrown because the client went away answers nobody.
    private const string RequestAbortedCode = "graphql.request_aborted";

    private readonly Func<HttpContext?> _currentHttpContext;
    private readonly ILogger<SharedKernelErrorFilter> _logger;

    /// <summary>Initializes a new instance of the <see cref="SharedKernelErrorFilter"/> class.</summary>
    /// <param name="currentHttpContext">Gives the current request, when there is one.</param>
    /// <param name="logger">The logger.</param>
    public SharedKernelErrorFilter(Func<HttpContext?> currentHttpContext, ILogger<SharedKernelErrorFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(currentHttpContext);
        ArgumentNullException.ThrowIfNull(logger);

        _currentHttpContext = currentHttpContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public IError OnError(IError error)
    {
        var httpContext = _currentHttpContext();
        var exception = error.Exception;

        if (exception is OperationCanceledException && httpContext?.RequestAborted.IsCancellationRequested == true)
        {
            GraphQLLog.ClientError(_logger, RequestAbortedCode, StatusCodes.Status499ClientClosedRequest, exception);
            return error;
        }

        var classified = exception is null ? null : ExceptionClassification.Classify(exception);
        var presented = classified?.Error
            ?? Error.Validation(GraphQLErrorCodes.RequestInvalid, error.Message);

        var statusCode = classified?.StatusCode ?? ErrorTypeStatusCodeMap.Resolve(presented.Type);
        var clientMessage = ErrorPresentation.GetClientMessage(presented, httpContext);

        Log(presented, statusCode, exception);

        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (error.Extensions is not null)
        {
            foreach (var extension in error.Extensions)
            {
                extensions[extension.Key] = extension.Value;
            }
        }

        extensions[StatusKey] = statusCode;

        var (type, title) = ProblemDefaults.For(statusCode);
        extensions[TypeKey] = type;
        extensions[TitleKey] = title;
        extensions[DetailKey] = clientMessage;
        extensions[ErrorCodeKey] = presented.Code;
        extensions[TraceIdKey] = RequestFacts.GetTraceId(httpContext);

        if (RequestFacts.GetCorrelationId(httpContext) is { } correlationId)
        {
            extensions[CorrelationIdKey] = correlationId;
        }

        if (presented.Details.Count > 0)
        {
            var groups = FieldErrorGroups.Create(presented.Details, httpContext);
            extensions[ErrorsKey] = ToExtensionValue(groups.Errors);
            extensions[ErrorCodesKey] = ToExtensionValue(groups.ErrorCodes);
        }

        if (classified is { Kind: ExceptionKind.Unclassified } && exception is not null && RequestFacts.IsDevelopment(httpContext))
        {
            extensions[ExceptionKey] = ExceptionClassification.Describe(exception)
                .ToDictionary(member => member.Key, member => (object?)member.Value, StringComparer.Ordinal);
        }

        // IError.WithExtensions accepts IReadOnlyDictionary<string, object?>.
        IReadOnlyDictionary<string, object?> readOnly = extensions;
        return error.WithMessage(clientMessage).WithExtensions(readOnly);
    }

    // HotChocolate's result formatter writes extension values of its own shapes (objects, lists, scalars); a typed
    // dictionary of arrays would be written as text.
    private static Dictionary<string, object?> ToExtensionValue(Dictionary<string, string[]> members) =>
        members.ToDictionary(
            member => member.Key,
            member => (object?)member.Value.Select(value => (object?)value).ToList(),
            StringComparer.Ordinal);

    private void Log(Error error, int statusCode, Exception? exception)
    {
        if (ErrorPresentation.IsServerError(error.Type))
        {
            GraphQLLog.ServerError(_logger, error.Code, statusCode, exception);
        }
        else
        {
            GraphQLLog.ClientError(_logger, error.Code, statusCode, exception);
        }
    }
}
