using System.Diagnostics;

namespace SharedKernel.Primitives.Results;

/// <summary>
/// Represents the outcome of an operation that may succeed with a value of type <typeparamref name="T"/>
/// or fail with an <see cref="Errors.Error"/>.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
/// <remarks>
/// <para>
/// <c>Result&lt;T&gt;</c> is a sealed class (not a struct) to avoid the zero-value problem
/// that arises with generic struct payloads. Use <see cref="Result"/> (non-generic) for
/// void operations that have no success payload.
/// </para>
/// <para>
/// Accessing <see cref="Value"/> on a failure result or <see cref="Error"/> on a success result
/// throws <see cref="InvalidOperationException"/>. All other members are safe to call regardless
/// of state.
/// </para>
/// <para>
/// Because this is a reference type, <c>default(Result&lt;T&gt;)</c> is <see langword="null"/> and
/// any member access on it fails immediately with a <see cref="NullReferenceException"/>. The
/// subtler uninitialized-value hazard that the non-generic <see cref="Result"/> struct has to
/// guard against therefore cannot arise here — see <see cref="Result.Error"/>'s own remarks.
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Result<T> : IHasSuccessFlag, IResultOfT<T>, IFailureFactory<Result<T>>
{
    private readonly T? _value;
    private readonly Errors.Error _error;

    private Result(T value)
    {
        _value = value;
        _error = Errors.Error.None;
        IsSuccess = true;
    }

    private Result(Errors.Error error)
    {
        _error = error;
        _value = default;
        IsSuccess = false;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the success value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the result represents a failure.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed result. Check IsSuccess before accessing Value.");

    /// <summary>
    /// Gets the error.
    /// </summary>
    /// <remarks>
    /// Never returns <see langword="null"/>: <see cref="Failure(Errors.Error)"/> rejects a
    /// <see langword="null"/> error and the success constructor stores
    /// <see cref="Errors.Error.None"/>, so no reachable state can produce one.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the result represents a success.</exception>
    public Errors.Error Error => IsFailure
        ? _error
        : throw new InvalidOperationException("Cannot access Error on a successful result. Check IsFailure before accessing Error.");

    /// <summary>Creates a successful result containing the specified <paramref name="value"/>.</summary>
    /// <param name="value">The success value.</param>
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result containing the specified <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The error describing the failure. Must not be <see langword="null"/>.</param>
    /// <remarks>
    /// This method also satisfies <see cref="IFailureFactory{TSelf}.Failure(Errors.Error)"/> for
    /// <see cref="IFailureFactory{TSelf}"/> — no separate member was introduced for that interface.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="error"/> is <see langword="null"/>. A failure carrying no error is a
    /// contradiction, and <see cref="Errors.Error"/>'s own contract forbids using
    /// <see langword="null"/> to mean "no error" — use <see cref="Errors.Error.None"/> for that.
    /// </exception>
    public static Result<T> Failure(Errors.Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }

    /// <summary>Implicitly converts a value to a successful <see cref="Result{T}"/>.</summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Implicitly converts an <see cref="Errors.Error"/> to a failed <see cref="Result{T}"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static implicit operator Result<T>(Errors.Error error) => Failure(error);

    // Reads the BACKING FIELDS, never the Value/Error properties, because each of those throws on
    // the opposite state and a DebuggerDisplay expression that throws renders as an evaluation
    // error in the watch window instead of the outcome the developer is trying to read.
    private string DebuggerDisplay =>
        IsSuccess ? $"Success: {_value}" : $"Failure: {_error.Code} ({_error.Type})";
}

/// <summary>
/// Represents the outcome of a void operation — one that succeeds or fails but carries no value payload.
/// </summary>
/// <remarks>
/// <para>
/// <c>Result</c> is a <c>readonly struct</c>. It carries no typed value payload, so the generic
/// zero-value problem that makes <see cref="Result{T}"/> a class does not apply here.
/// </para>
/// <para>
/// It is still a struct, which means the runtime can hand a caller an all-zero instance that never
/// ran either factory: <c>default(Result)</c>, an unassigned field, an element of
/// <c>new Result[n]</c>, or the <c>out</c> value of a failed
/// <see cref="System.Collections.Generic.Dictionary{TKey, TValue}.TryGetValue"/>. Such an instance
/// reports <see cref="IsFailure"/> as <see langword="true"/> while holding no error at all.
/// <see cref="Error"/> detects exactly that state and throws a named
/// <see cref="InvalidOperationException"/> rather than returning <see langword="null"/> — see its
/// own remarks.
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly struct Result : IHasSuccessFlag
{
    private readonly Errors.Error _error;

    private Result(bool isSuccess, Errors.Error error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never returns <see langword="null"/>. Because <see cref="Result"/> is a struct, an instance
    /// that never ran <see cref="Success"/> or <see cref="Failure(Errors.Error)"/> — the all-zero
    /// value described in this type's own remarks — reports <see cref="IsSuccess"/> as
    /// <see langword="false"/> and carries no error. Returning <see langword="null"/> there would
    /// violate <see cref="Errors.Error"/>'s "never null" contract and surface later as a
    /// <see cref="NullReferenceException"/> at an unrelated call site, so this property throws on
    /// that state instead, naming the cause.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The result represents a success, or it is an uninitialized <c>default(Result)</c>.
    /// </exception>
    public Errors.Error Error => IsFailure
        ? (_error ?? throw new InvalidOperationException(
            "This Result is an uninitialized default(Result), which is not a valid result: it "
            + "reports IsFailure but carries no error. Produce results with Result.Success() or "
            + "Result.Failure(error) — never default(Result), an unassigned field, an unpopulated "
            + "array element, or the out-parameter of a failed TryGetValue."))
        : throw new InvalidOperationException("Cannot access Error on a successful result. Check IsFailure before accessing Error.");

    /// <summary>Creates a successful <see cref="Result"/>.</summary>
    public static Result Success() => new(true, Errors.Error.None);

    /// <summary>Creates a failed <see cref="Result"/> containing the specified <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="error"/> is <see langword="null"/>. Use <see cref="Errors.Error.None"/> to
    /// express "no error", never <see langword="null"/>.
    /// </exception>
    public static Result Failure(Errors.Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, error);
    }

    /// <summary>Implicitly converts an <see cref="Errors.Error"/> to a failed <see cref="Result"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static implicit operator Result(Errors.Error error) => Failure(error);

    // Handles the uninitialized default(Result) explicitly rather than letting the null _error
    // surface as an evaluation error: naming that state in the watch window is precisely the
    // shortcut a developer needs when they are looking at one and do not yet know it.
    private string DebuggerDisplay =>
        IsSuccess ? "Success"
        : _error is null ? "INVALID: uninitialized default(Result)"
        : $"Failure: {_error.Code} ({_error.Type})";
}
