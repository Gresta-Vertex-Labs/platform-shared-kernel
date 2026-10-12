namespace SharedKernel.Presentation.GraphQL;

/// <summary>
/// The error codes this package puts in the <c>errorCode</c> extension of a GraphQL error that carries no error of the
/// service: the counterparts of the HTTP core's <c>PresentationErrorCodes</c> and gRPC's <c>GrpcErrorCodes</c>.
/// </summary>
/// <remarks>
/// Every other code comes from the <c>SharedKernelException</c> or <c>ValidationException</c> the service threw. Like
/// those, this value is a contract: clients branch on it and translations are keyed by it.
/// </remarks>
public static class GraphQLErrorCodes
{
    /// <summary>
    /// <c>graphql.request_invalid</c>: the <c>errorCode</c> of an error HotChocolate itself raised for the request — a
    /// syntax error, an unknown field, a variable of the wrong type, a failed argument binding. Such an error carries no
    /// exception of the service and is a client error (status 400); HotChocolate's own <c>code</c> extension is kept
    /// beside it.
    /// </summary>
    public const string RequestInvalid = "graphql.request_invalid";
}
