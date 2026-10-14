namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Well-known error codes of <c>SharedKernel.Caching</c>. Each code is lowercase <c>{domain}.{snake_reason}</c>.
/// </summary>
public static class CachingErrorCodes
{
    /// <summary>
    /// The distributed lock store could not be reached, so a lock or lease could be neither granted nor refused.
    /// Carried by <see cref="DistributedLockUnavailableException"/> as an
    /// <see cref="SharedKernel.Primitives.Errors.ErrorType.Unavailable"/> error.
    /// </summary>
    public const string LockUnavailable = "caching.lock_unavailable";
}
