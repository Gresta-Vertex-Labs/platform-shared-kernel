using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Guards;

/// <summary>
/// Turns the <see cref="Error"/>? returned by a functional guard into a <see cref="Result"/> or
/// <see cref="Result{T}"/>.
/// </summary>
public static class GuardErrorExtensions
{
    /// <summary>
    /// Returns <see cref="Result.Success()"/> when <paramref name="error"/> is <see langword="null"/>, and
    /// a failure carrying it otherwise.
    /// </summary>
    /// <param name="error">The result of one or more chained guards.</param>
    public static Result ToResult(this Error? error)
        => error is null ? Result.Success() : Result.Failure(error);

    /// <summary>
    /// Returns a success carrying <paramref name="value"/> when <paramref name="error"/> is
    /// <see langword="null"/>, and a failure carrying the error otherwise.
    /// </summary>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="error">The result of one or more chained guards.</param>
    /// <param name="value">The value to return when every guard passed.</param>
    public static Result<T> ToResult<T>(this Error? error, T value)
        => error is null ? Result<T>.Success(value) : Result<T>.Failure(error);

    /// <summary>
    /// Returns a success carrying the value produced by <paramref name="valueFactory"/> when
    /// <paramref name="error"/> is <see langword="null"/>, and a failure carrying the error otherwise.
    /// </summary>
    /// <remarks>
    /// <paramref name="valueFactory"/> runs only when every guard passed, so it can safely construct an
    /// object whose constructor would reject the invalid input.
    /// </remarks>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="error">The result of one or more chained guards.</param>
    /// <param name="valueFactory">Creates the value when every guard passed.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="valueFactory"/> is <see langword="null"/>.</exception>
    public static Result<T> ToResult<T>(this Error? error, Func<T> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        return error is null ? Result<T>.Success(valueFactory()) : Result<T>.Failure(error);
    }
}
