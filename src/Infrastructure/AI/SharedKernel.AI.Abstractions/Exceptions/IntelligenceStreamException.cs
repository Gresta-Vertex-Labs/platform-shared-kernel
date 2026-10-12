using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Abstractions.Exceptions;

/// <summary>
/// Thrown from <c>MoveNextAsync</c> when a recognised provider failure interrupts
/// <c>IVectorCollection&lt;TRecord&gt;.ScrollAsync</c> or <c>ISemanticKernel.CompleteStreamingAsync</c>
/// — the only two non-<c>Result</c> surfaces in this domain.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SharedKernelException"/>, so the HTTP, gRPC and SignalR edges map it by its
/// <see cref="SharedKernelException.Error"/> (<c>Unavailable</c> answers 503, <c>Timeout</c> 504) instead of
/// answering 500 and losing the error. Its <see cref="Exception.Message"/> is the error's kernel-authored message;
/// the provider's own exception is kept as <see cref="Exception.InnerException"/> and never copied into the message.
/// </para>
/// <para>
/// Only a recognised provider fault is wrapped. The caller's cancellation surfaces as
/// <see cref="OperationCanceledException"/>, and an unrecognised exception (a defect) propagates untouched.
/// </para>
/// </remarks>
public sealed class IntelligenceStreamException : SharedKernelException
{
    /// <summary>Initializes a new <see cref="IntelligenceStreamException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The structured error describing why the stream failed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public IntelligenceStreamException(Error error)
        : base(error)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="IntelligenceStreamException"/> carrying <paramref name="error"/>
    /// and wrapping <paramref name="innerException"/>.
    /// </summary>
    /// <param name="error">The structured error describing why the stream failed.</param>
    /// <param name="innerException">The provider exception that interrupted the stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public IntelligenceStreamException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
