using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Reporting;

/// <summary>
/// A report definition could not be built: it has no columns, or a column is invalid (<c>reporting.invalid_definition</c>,
/// <see cref="ErrorType.Validation"/>).
/// </summary>
/// <remarks>
/// Thrown by <see cref="ReportDefinitionBuilder{TRow}.Build"/>. It is classified, so an HTTP, gRPC or SignalR edge answers
/// a validation failure rather than a server error; the same condition on an assembled definition returns the same error
/// as a <c>Result</c> from <c>ExportAsync</c>.
/// </remarks>
public sealed class InvalidReportDefinitionException : SharedKernelException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="error">The <c>reporting.invalid_definition</c> error.</param>
    public InvalidReportDefinitionException(Error error)
        : base(error)
    {
    }

    /// <summary>Creates the exception with the exception that caused it.</summary>
    /// <param name="error">The <c>reporting.invalid_definition</c> error.</param>
    /// <param name="innerException">The cause.</param>
    public InvalidReportDefinitionException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
