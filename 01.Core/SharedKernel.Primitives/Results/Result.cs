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
/// </remarks>
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
    /// <param name="error">The error describing the failure.</param>
    /// <remarks>
    /// This method also satisfies <see cref="IFailureFactory{TSelf}.Failure(Errors.Error)"/> for
    /// <see cref="IFailureFactory{TSelf}"/> — no separate member was introduced for that interface.
    /// </remarks>
    public static Result<T> Failure(Errors.Error error) => new(error);

    /// <summary>Implicitly converts a value to a successful <see cref="Result{T}"/>.</summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Implicitly converts an <see cref="Errors.Error"/> to a failed <see cref="Result{T}"/>.</summary>
    public static implicit operator Result<T>(Errors.Error error) => Failure(error);
}

/// <summary>
/// Represents the outcome of a void operation — one that succeeds or fails but carries no value payload.
/// </summary>
/// <remarks>
/// <c>Result</c> is a <c>readonly struct</c>. It carries no typed value payload so the zero-value
/// problem that affects <see cref="Result{T}"/> does not apply. Use this type for operations that
/// have no meaningful return value on success (e.g., command handlers, side-effecting operations).
/// </remarks>
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
    /// <exception cref="InvalidOperationException">Thrown when the result represents a success.</exception>
    public Errors.Error Error => IsFailure
        ? _error
        : throw new InvalidOperationException("Cannot access Error on a successful result. Check IsFailure before accessing Error.");

    /// <summary>Creates a successful <see cref="Result"/>.</summary>
    public static Result Success() => new(true, Errors.Error.None);

    /// <summary>Creates a failed <see cref="Result"/> containing the specified <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    public static Result Failure(Errors.Error error) => new(false, error);

    /// <summary>Implicitly converts an <see cref="Errors.Error"/> to a failed <see cref="Result"/>.</summary>
    public static implicit operator Result(Errors.Error error) => Failure(error);
}
