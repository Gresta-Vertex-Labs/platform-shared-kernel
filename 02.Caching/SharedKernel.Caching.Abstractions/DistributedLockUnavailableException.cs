namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Thrown when a lock or lease could not be acquired because the lock store could not be reached,
/// as opposed to another holder having the resource.
/// </summary>
public sealed class DistributedLockUnavailableException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public DistributedLockUnavailableException()
        : base("The distributed lock store could not be reached.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public DistributedLockUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying failure.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The failure that made the store unreachable.</param>
    public DistributedLockUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a resource.</summary>
    /// <param name="resource">The resource that could not be locked.</param>
    /// <param name="innerException">The failure that made the store unreachable, if any.</param>
    /// <returns>The exception.</returns>
    public static DistributedLockUnavailableException ForResource(string resource, Exception? innerException = null) =>
        new($"The distributed lock store could not be reached while acquiring '{resource}'.", innerException) { Resource = resource };

    /// <summary>Gets the resource that could not be locked, when known.</summary>
    public string? Resource { get; private init; }
}
