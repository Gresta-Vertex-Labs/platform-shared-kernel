using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Abstractions.Exceptions;

/// <summary>
/// Thrown from <c>MoveNextAsync</c> when a transport failure interrupts
/// <c>IVectorCollection&lt;TRecord&gt;.ScrollAsync</c> or <c>ISemanticKernel.CompleteStreamingAsync</c>
/// — the only two non-<c>Result</c> surfaces in this domain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Base-type decision:</b> <c>SharedKernel.Primitives</c> ships no exception type at all, and the
/// only <see cref="Error"/>-carrying exception hierarchy in the platform lives in the separate
/// <c>SharedKernel.Core</c> package, which <c>SharedKernel.AI.Abstractions</c> does not and must not
/// reference. This type therefore derives directly from <see cref="Exception"/> and declares its own
/// <see cref="Error"/> property — mirroring <c>09.Search</c>'s identical <c>SearchStreamException</c>
/// base-type finding.
/// </para>
/// <para>
/// Constructed only from a <see cref="SharedKernel.Primitives.Errors.Error"/> — never from a bare
/// string.
/// </para>
/// </remarks>
public sealed class IntelligenceStreamException : Exception
{
    /// <summary>Initializes a new <see cref="IntelligenceStreamException"/> carrying <paramref name="error"/>.</summary>
    public IntelligenceStreamException(Error error)
        : base(error.Message)
    {
        Error = error;
    }

    /// <summary>
    /// Initializes a new <see cref="IntelligenceStreamException"/> carrying <paramref name="error"/>
    /// and wrapping <paramref name="innerException"/>.
    /// </summary>
    public IntelligenceStreamException(Error error, Exception innerException)
        : base(error.Message, innerException)
    {
        Error = error;
    }

    /// <summary>Gets the structured error describing why the stream failed.</summary>
    public Error Error { get; }
}
