using SharedKernel.Primitives.Errors;

namespace SharedKernel.Caching.Abstractions;

/// <summary>Factory for the <see cref="Error"/> values of this package.</summary>
internal static class CachingErrors
{
    /// <summary>The lock store is unreachable. The message is client-safe and never names the resource.</summary>
    internal static Error LockUnavailable() =>
        Error.Unavailable(CachingErrorCodes.LockUnavailable, "The distributed lock store is unavailable.");
}
