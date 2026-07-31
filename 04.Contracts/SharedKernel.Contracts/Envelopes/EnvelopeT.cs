using SharedKernel.Primitives.Errors;

namespace SharedKernel.Contracts.Envelopes;

/// <summary>
/// Cross-service transport envelope for operations that return a typed value payload.
/// </summary>
/// <typeparam name="T">The type of the value returned on success.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Envelope{T}"/> is the cross-service transport counterpart to <c>Result&lt;T&gt;</c> from
/// <c>SharedKernel.Primitives</c>. <c>Result&lt;T&gt;</c> is used within a single service for
/// railway-oriented programming. <see cref="Envelope{T}"/> is used at service boundaries —
/// HTTP client responses, gRPC payloads, or message-bus acknowledgements.
/// </para>
/// <para>
/// <strong>Boundary rule:</strong> Never return <c>Result&lt;T&gt;</c> across a service boundary.
/// Serialize to <see cref="Envelope{T}"/> at the presentation or communication layer. The application
/// layer must return <c>Result&lt;T&gt;</c>; the boundary layer maps it to <see cref="Envelope{T}"/>.
/// </para>
/// </remarks>
/// <seealso cref="Envelope"/>
/// <seealso cref="SharedKernel.Primitives.Results.Result{T}"/>
public sealed record Envelope<T>
{
    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>
    /// Gets the value returned by a successful operation.
    /// <c>null</c> (or <c>default</c>) when <see cref="IsSuccess"/> is <c>false</c>.
    /// </summary>
    public T? Value { get; private init; }

    /// <summary>
    /// Gets the error associated with a failed operation.
    /// <c>null</c> when <see cref="IsSuccess"/> is <c>true</c>.
    /// </summary>
    public Error? Error { get; private init; }

    [System.Text.Json.Serialization.JsonConstructor]
    internal Envelope(bool isSuccess, T? value, Error? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>Creates a successful <see cref="Envelope{T}"/> carrying the specified value.</summary>
    /// <param name="value">The value to carry. Must not be <c>null</c>.</param>
    /// <returns>An envelope representing a successful operation with a typed result.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is <c>null</c>.
    /// A successful envelope must carry a non-null value.
    /// </exception>
    public static Envelope<T> Ok(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(true, value, null);
    }

    /// <summary>Creates a failed <see cref="Envelope{T}"/> carrying the specified error.</summary>
    /// <param name="error">The error describing the failure. Must not be <see cref="Primitives.Errors.Error.None"/>.</param>
    /// <returns>An envelope representing a failed operation with no value.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="error"/> is <see cref="Primitives.Errors.Error.None"/>.
    /// Use a descriptive error; <c>Error.None</c> is the "no error" sentinel and must not represent a failure.
    /// </exception>
    public static Envelope<T> Fail(Error error)
    {
        if (error == Primitives.Errors.Error.None)
            throw new ArgumentException("Error.None is not a valid failure error. Supply a descriptive error.", nameof(error));

        return new(false, default, error);
    }

    /// <summary>
    /// Implicitly converts a value to a successful <see cref="Envelope{T}"/>.
    /// </summary>
    /// <param name="value">The value to wrap.</param>
    public static implicit operator Envelope<T>(T value) => Ok(value);

    /// <summary>
    /// Implicitly converts an <see cref="Primitives.Errors.Error"/> to a failed <see cref="Envelope{T}"/>.
    /// </summary>
    /// <param name="error">The error to wrap.</param>
    public static implicit operator Envelope<T>(Error error) => Fail(error);
}
