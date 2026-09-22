using SharedKernel.Primitives.Errors;

namespace SharedKernel.Storage;

/// <summary>
/// Thrown by <see cref="IFileStorage.ListAsync"/> when its prefix is invalid or a page cannot be read — an async
/// stream has no <c>Result</c> to return. Every other storage member returns its errors as values.
/// </summary>
/// <remarks>
/// Catch it around the <c>await foreach</c> and branch on the <c>Code</c> of the <see cref="Error"/> property, a
/// <see cref="StorageErrorCodes"/> value, exactly as for a failed <c>Result</c>. The exception message is the
/// error's message.
/// </remarks>
public sealed class StorageException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="StorageException"/> class.</summary>
    /// <param name="error">The storage error; its message becomes the exception message.</param>
    /// <param name="innerException">The provider exception, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public StorageException(Error error, Exception? innerException = null)
        : base(error?.Message, innerException)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    /// <summary>
    /// Gets the storage error; its <see cref="Error.Code"/> is a <see cref="StorageErrorCodes"/> value.
    /// </summary>
    public Error Error { get; }
}
