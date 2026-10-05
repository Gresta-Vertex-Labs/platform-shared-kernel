using SharedKernel.Primitives.Errors;

namespace SharedKernel.Search.Abstractions.Exceptions;

/// <summary>
/// Thrown from <c>MoveNextAsync</c> when a transport failure interrupts
/// <c>ISearchIndex&lt;TDocument&gt;.EnumerateAsync</c> or <c>ICursorSearch&lt;TDocument&gt;.StreamAsync</c>
/// — the only two non-<c>Result</c> surfaces in this domain.
/// </summary>
/// <remarks>
/// <para>
/// Base-type decision, resolved at Scaffold time (S-12) and not re-litigated here:
/// <c>SharedKernel.Primitives</c> ships no exception type at all, and the only
/// <see cref="Error"/>-carrying exception hierarchy in the platform lives in the separate
/// <c>SharedKernel.Core</c> package, which <c>SharedKernel.Search.Abstractions</c> does not and must
/// not reference. This type therefore derives directly from <see cref="Exception"/> and declares its
/// own <see cref="Error"/> property.
/// </para>
/// <para>
/// Constructed only from a <see cref="SharedKernel.Primitives.Errors.Error"/> — never from a bare
/// string.
/// </para>
/// </remarks>
public sealed class SearchStreamException : Exception
{
    /// <summary>Initializes a new <see cref="SearchStreamException"/> carrying <paramref name="error"/>.</summary>
    public SearchStreamException(Error error)
        : base(error.Message)
    {
        Error = error;
    }

    /// <summary>
    /// Initializes a new <see cref="SearchStreamException"/> carrying <paramref name="error"/> and
    /// wrapping <paramref name="innerException"/>.
    /// </summary>
    public SearchStreamException(Error error, Exception innerException)
        : base(error.Message, innerException)
    {
        Error = error;
    }

    /// <summary>Gets the structured error describing why the stream failed.</summary>
    public Error Error { get; }
}
