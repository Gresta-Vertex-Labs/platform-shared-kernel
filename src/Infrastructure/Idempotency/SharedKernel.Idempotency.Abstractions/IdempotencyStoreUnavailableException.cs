using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Idempotency.Abstractions;

/// <summary>
/// Thrown by a fail-closed <see cref="IIdempotencyStore"/> when the backing store cannot be reached or does not
/// answer in time.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it means.</b> The reservation state is unknown, so the guarded work must not run. A <c>TryBeginAsync</c>
/// that timed out may have left an unconfirmed reservation behind; it expires after its <c>ttl</c>, so retrying the
/// same key is safe.
/// </para>
/// <para>
/// <b>Error.</b> <see cref="SharedKernelException.Error"/> is <see cref="ErrorType.Unavailable"/> with the code
/// <see cref="ErrorCodes.Idempotency.StoreUnavailable"/> and a fixed, provider-neutral message: it names no host, key
/// or provider text. The original provider exception is the <see cref="Exception.InnerException"/>, stack trace
/// intact, for diagnostics only.
/// </para>
/// <para>
/// <b>Not wrapped.</b> A programming error (an undefined purpose, a blank key), a defect in the store (for example a
/// rejected script) and cancellation propagate unchanged.
/// </para>
/// </remarks>
public sealed class IdempotencyStoreUnavailableException : SharedKernelException
{
    private const string CanonicalMessage = "The idempotency store is unavailable.";

    /// <summary>Initialises the exception with <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public IdempotencyStoreUnavailableException(Error error)
        : base(error)
    {
    }

    /// <summary>Initialises the exception with <paramref name="error"/> and the provider exception that caused it.</summary>
    /// <param name="error">The error describing the failure. Its message becomes the exception message.</param>
    /// <param name="innerException">The provider exception that caused the outage.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public IdempotencyStoreUnavailableException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }

    /// <summary>
    /// Initialises the exception with the canonical <see cref="ErrorCodes.Idempotency.StoreUnavailable"/> error and
    /// the provider exception that caused it.
    /// </summary>
    /// <param name="innerException">The provider exception that caused the outage.</param>
    public IdempotencyStoreUnavailableException(Exception innerException)
        : this(CanonicalError(), innerException)
    {
    }

    private static Error CanonicalError() =>
        Error.Unavailable(ErrorCodes.Idempotency.StoreUnavailable, CanonicalMessage);
}
