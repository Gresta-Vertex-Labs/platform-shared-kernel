using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.EdgeCases;

/// <summary>
/// The defect the negative controls throw: an exception that exposes an <see cref="Error"/> but does not derive
/// <c>SharedKernelException</c>. Every protocol must answer it as an unclassified failure and never show its code, which
/// proves that dispatch is by base type and not by shape.
/// </summary>
internal sealed class ErrorBearingException : Exception
{
    /// <summary>The code the defect carries; no response may contain it.</summary>
    internal const string Code = "defect.error_bearing";

    /// <summary>Gets the error the defect exposes.</summary>
    public Error Error { get; } = Error.Forbidden(Code, "A sentence meant for nobody.");
}
