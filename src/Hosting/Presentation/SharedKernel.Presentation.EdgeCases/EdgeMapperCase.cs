using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.EdgeCases;

/// <summary>
/// One row of the edge-mapper conformance catalogue: a <see cref="SharedKernelException"/> another domain throws (or
/// the error it returns) and what every protocol must answer for it.
/// </summary>
/// <param name="Name">A unique, route-safe name of the case.</param>
/// <param name="Create">Creates the exception, through the owner's own public constructor or factory.</param>
/// <param name="ExpectedType">The <see cref="ErrorType"/> the exception must carry.</param>
/// <param name="ExpectedCode">The owner's public error code the exception must carry.</param>
/// <param name="ExpectedHttpStatus">The HTTP status the case is answered with.</param>
/// <param name="ExpectedGrpcStatus">The name of the gRPC <c>StatusCode</c> the case is answered with.</param>
public sealed record EdgeMapperCase(
    string Name,
    Func<SharedKernelException> Create,
    ErrorType ExpectedType,
    string ExpectedCode,
    int ExpectedHttpStatus,
    string ExpectedGrpcStatus)
{
    /// <summary>Gets a value indicating whether the case is of the server category (500, 503 or 504).</summary>
    public bool IsServerCategory => ExpectedHttpStatus >= 500;

    /// <summary>Creates the exception of the case.</summary>
    /// <returns>A new exception.</returns>
    public SharedKernelException CreateException() => Create();

    /// <summary>Creates the exception of the case and returns the error it carries (the form a failed <c>Result</c> has).</summary>
    /// <returns>The error.</returns>
    public Error CreateError() => Create().Error;

    /// <inheritdoc />
    public override string ToString() => Name;
}
