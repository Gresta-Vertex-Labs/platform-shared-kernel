using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Storage;

/// <summary>
/// Thrown by <see cref="IFileStorage.ListAsync"/> when its prefix is invalid or a page cannot be read — an async
/// stream has no <c>Result</c> to return. Every other storage member returns its errors as values.
/// </summary>
/// <remarks>
/// Catch it around the <c>await foreach</c> and branch on the <c>Code</c> of the inherited
/// <see cref="SharedKernelException.Error"/> property, a <see cref="StorageErrorCodes"/> value, exactly as for a
/// failed <c>Result</c>. The exception message is the error's message. Because it is a
/// <see cref="SharedKernelException"/>, the HTTP, gRPC and SignalR edges map an uncaught instance by
/// <see cref="Error.Type"/> (an outage is <see cref="ErrorType.Unavailable"/>, a denied listing
/// <see cref="ErrorType.Forbidden"/>) instead of answering 500.
/// </remarks>
public sealed class StorageException : SharedKernelException
{
    /// <summary>Initializes a new instance of the <see cref="StorageException"/> class.</summary>
    /// <param name="error">The storage error; its message becomes the exception message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public StorageException(Error error)
        : base(error)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StorageException"/> class with its cause.</summary>
    /// <param name="error">The storage error; its message becomes the exception message.</param>
    /// <param name="innerException">The exception that caused this one, such as the provider exception.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public StorageException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
