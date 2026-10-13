using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Pipeline.Shared;

/// <summary>
/// The one place the pipeline behaviors build the <see cref="Error"/> values they return instead of throwing.
/// </summary>
/// <remarks>
/// Application declares no error codes of its own: every code is the kernel-shared <see cref="ErrorCodes"/> constant
/// that <c>14.Presentation</c> also reads. Messages are fixed text; none embeds a request value, an idempotency key or
/// an exception message.
/// </remarks>
internal static class ApplicationErrors
{
    /// <summary>The caller sent a command without an idempotency key.</summary>
    internal static Error IdempotencyKeyRequired()
        => Error.Validation(ErrorCodes.Idempotency.KeyRequired, "An idempotency key is required.");

    /// <summary>Another request holds the reservation of the same idempotency key.</summary>
    internal static Error IdempotencyInProgress()
        => Error.Conflict(ErrorCodes.Idempotency.InProgress, "A request with this idempotency key is still being processed.");

    /// <summary>The idempotency key was already used for a different request.</summary>
    internal static Error IdempotencyKeyReused()
        => Error.Conflict(ErrorCodes.Idempotency.KeyReused, "The idempotency key was already used for a different request.");

    /// <summary>The idempotency store could not be reached, so the handler did not run.</summary>
    internal static Error IdempotencyStoreUnavailable()
        => Error.Unavailable(ErrorCodes.Idempotency.StoreUnavailable, "The idempotency service is temporarily unavailable.");

    /// <summary>The caller of a protected request is not authenticated.</summary>
    internal static Error Unauthenticated()
        => Error.Unauthorized(ErrorCodes.Unauthorized.Default, "Authentication is required.");

    /// <summary>The caller lacks a permission the request requires.</summary>
    internal static Error InsufficientPermission()
        => Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission, "The caller does not have the required permission.");

    /// <summary>Validators rejected the request; the errors carry no rejected value.</summary>
    internal static Error ValidationFailed(IReadOnlyList<Error> errors)
        => Error.Validation(errors);
}
