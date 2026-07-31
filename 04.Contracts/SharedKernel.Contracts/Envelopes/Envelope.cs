using SharedKernel.Primitives.Errors;

namespace SharedKernel.Contracts.Envelopes;

/// <summary>
/// Cross-service transport envelope for void operations (no typed value payload).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Envelope"/> is the cross-service counterpart to the non-generic <c>Result</c> from
/// <c>SharedKernel.Primitives</c>. Use <see cref="Envelope"/> at service boundaries for operations
/// that return no typed value — HTTP responses, message-bus acknowledgements, or gRPC void results.
/// </para>
/// <para>
/// <strong>Boundary rule:</strong> <see cref="Envelope"/> must only be constructed at service
/// boundaries (presentation layer, HTTP client adapters). The application layer must return
/// <c>Result</c> / <c>Result&lt;T&gt;</c> and have the communication layer map them to envelopes.
/// Never pass an <see cref="Envelope"/> through the application layer.
/// </para>
/// </remarks>
/// <seealso cref="Envelope{T}"/>
/// <seealso cref="SharedKernel.Primitives.Results.Result"/>
public sealed record Envelope
{
    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>
    /// Gets the error associated with a failed operation.
    /// <c>null</c> when <see cref="IsSuccess"/> is <c>true</c>.
    /// </summary>
    public Error? Error { get; private init; }

    [System.Text.Json.Serialization.JsonConstructor]
    internal Envelope(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Creates a successful <see cref="Envelope"/>.</summary>
    /// <returns>An envelope representing a successful void operation.</returns>
    public static Envelope Ok() => new(true, null);

    /// <summary>Creates a failed <see cref="Envelope"/> carrying the specified error.</summary>
    /// <param name="error">The error describing the failure. Must not be <see cref="Primitives.Errors.Error.None"/>.</param>
    /// <returns>An envelope representing a failed operation.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="error"/> is <see cref="Primitives.Errors.Error.None"/>.
    /// Use a descriptive error; <c>Error.None</c> is the "no error" sentinel and must not represent a failure.
    /// </exception>
    public static Envelope Fail(Error error)
    {
        if (error == Primitives.Errors.Error.None)
            throw new ArgumentException("Error.None is not a valid failure error. Supply a descriptive error.", nameof(error));

        return new(false, error);
    }

    /// <summary>
    /// Implicitly converts an <see cref="Primitives.Errors.Error"/> to a failed <see cref="Envelope"/>.
    /// </summary>
    /// <param name="error">The error to wrap.</param>
    public static implicit operator Envelope(Error error) => Fail(error);
}
