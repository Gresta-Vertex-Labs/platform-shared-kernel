using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Thrown when a lock or lease could not be acquired because the lock store could not be reached,
/// as opposed to another holder having the resource.
/// </summary>
/// <remarks>
/// It carries an <see cref="ErrorType.Unavailable"/> error with code <see cref="CachingErrorCodes.LockUnavailable"/>,
/// so an edge answers HTTP 503 / gRPC <c>Unavailable</c>. The message is the fixed <see cref="Error"/> message and never
/// contains the resource; <see cref="Resource"/> is diagnostic only and must not be sent to a client.
/// </remarks>
public sealed class DistributedLockUnavailableException : SharedKernelException
{
    /// <summary>Creates the exception from an error.</summary>
    /// <param name="error">The error describing the failure.</param>
    public DistributedLockUnavailableException(Error error)
        : base(error)
    {
    }

    /// <summary>Creates the exception from an error and the underlying failure.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">The failure that made the store unreachable.</param>
    public DistributedLockUnavailableException(Error error, Exception? innerException)
        : base(error, innerException)
    {
    }

    /// <summary>Creates the exception for a resource.</summary>
    /// <param name="resource">The resource that could not be locked.</param>
    /// <param name="innerException">The failure that made the store unreachable, if any.</param>
    /// <returns>The exception.</returns>
    public static DistributedLockUnavailableException ForResource(string resource, Exception? innerException = null) =>
        new(CachingErrors.LockUnavailable(), innerException) { Resource = resource };

    /// <summary>Gets the resource that could not be locked, when known. Diagnostic only.</summary>
    public string? Resource { get; private init; }
}
