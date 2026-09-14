using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Guards;

/// <summary>
/// Converts the <see cref="Error"/>? returned by functional guards into a <see cref="Result"/> or
/// <see cref="Result{T}"/>, so guard checks join the result railway without an explicit branch.
/// </summary>
/// <example>
/// <code>
/// public static Result&lt;Money&gt; Create(decimal amount, string? currency) =&gt;
///     (Guard.Against.Negative(amount) ?? Guard.Against.NullOrWhiteSpace(currency))
///     .ToResult(() =&gt; new Money(amount, currency!));
/// </code>
/// </example>
public static class GuardErrorExtensions
{
    /// <summary>Converts a guard outcome into a non-generic <see cref="Result"/>.</summary>
    /// <param name="error">The outcome of one guard, or of several chained with <c>??</c>.</param>
    /// <returns>
    /// <see cref="Result.Success()"/> when <paramref name="error"/> is <see langword="null"/>; otherwise a
    /// failure carrying <paramref name="error"/>.
    /// </returns>
    public static Result ToResult(this Error? error)
        => error is null ? Result.Success() : Result.Failure(error);

    /// <summary>Converts a guard outcome into a <see cref="Result{T}"/> carrying an existing value.</summary>
    /// <remarks>
    /// <paramref name="value"/> is evaluated before this method runs. When producing the value is expensive, or
    /// would itself reject invalid input, use the <see cref="ToResult{T}(Error, Func{T})"/> overload.
    /// </remarks>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="error">The outcome of one guard, or of several chained with <c>??</c>.</param>
    /// <param name="value">The value to return when every guard passed.</param>
    /// <returns>
    /// A success carrying <paramref name="value"/> when <paramref name="error"/> is <see langword="null"/>;
    /// otherwise a failure carrying <paramref name="error"/>.
    /// </returns>
    public static Result<T> ToResult<T>(this Error? error, T value)
        => error is null ? Result<T>.Success(value) : Result<T>.Failure(error);

    /// <summary>Converts a guard outcome into a <see cref="Result{T}"/>, creating the value only when every guard passed.</summary>
    /// <remarks>
    /// <paramref name="valueFactory"/> is not called when a guard failed, so it can safely construct an object
    /// whose constructor would throw for the invalid input. An exception thrown by the factory propagates.
    /// </remarks>
    /// <typeparam name="T">The success value type.</typeparam>
    /// <param name="error">The outcome of one guard, or of several chained with <c>??</c>.</param>
    /// <param name="valueFactory">Creates the success value.</param>
    /// <returns>
    /// A success carrying the value from <paramref name="valueFactory"/> when <paramref name="error"/> is
    /// <see langword="null"/>; otherwise a failure carrying <paramref name="error"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="valueFactory"/> is <see langword="null"/>.</exception>
    public static Result<T> ToResult<T>(this Error? error, Func<T> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        return error is null ? Result<T>.Success(valueFactory()) : Result<T>.Failure(error);
    }
}
