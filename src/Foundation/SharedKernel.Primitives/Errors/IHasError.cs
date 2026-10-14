namespace SharedKernel.Primitives.Errors;

/// <summary>
/// Implemented by an exception that carries a structured <see cref="Errors.Error"/>, so code that sees only
/// <c>SharedKernel.Primitives</c> (such as the span recorder) can read the error code without referencing the
/// package that defines the exception.
/// </summary>
/// <remarks>
/// <c>SharedKernel.Core</c>'s <c>SharedKernelException</c> implements it. A classified exception that does not
/// derive from that base implements this interface instead. It is deliberately a one-property interface, not a
/// marker: the recorder needs the value, not just the knowledge that one exists.
/// </remarks>
public interface IHasError
{
    /// <summary>Gets the error describing the failure. Never <see langword="null"/>.</summary>
    Error Error { get; }
}
