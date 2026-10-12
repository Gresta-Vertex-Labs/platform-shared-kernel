using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Search.Abstractions.Exceptions;

/// <summary>
/// Thrown from <c>MoveNextAsync</c> when a transport failure interrupts
/// <c>ISearchIndex&lt;TDocument&gt;.EnumerateAsync</c> or <c>ICursorSearch&lt;TDocument&gt;.StreamAsync</c>
/// — the only two non-<c>Result</c> surfaces in this domain.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SharedKernelException"/> (and therefore <see cref="IHasError"/>), so the HTTP, gRPC and
/// SignalR edges translate it by its <see cref="SharedKernelException.Error"/> type instead of answering 500.
/// </para>
/// <para>
/// Constructed only from an <see cref="Primitives.Errors.Error"/> — never from a bare string. The error text
/// is kernel-authored; engine detail travels in the inner exception.
/// </para>
/// </remarks>
public sealed class SearchStreamException : SharedKernelException
{
    /// <summary>Initializes a new <see cref="SearchStreamException"/> carrying <paramref name="error"/>.</summary>
    /// <param name="error">The structured error describing why the stream failed.</param>
    public SearchStreamException(Error error)
        : base(error)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="SearchStreamException"/> carrying <paramref name="error"/> and
    /// wrapping <paramref name="innerException"/>.
    /// </summary>
    /// <param name="error">The structured error describing why the stream failed.</param>
    /// <param name="innerException">The engine or transport exception that interrupted the stream.</param>
    public SearchStreamException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
