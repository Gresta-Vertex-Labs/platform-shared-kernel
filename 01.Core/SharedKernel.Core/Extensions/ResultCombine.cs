using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Folds a batch of independent <see cref="Result"/> / <see cref="Result{T}"/> outcomes into a single
/// aggregate <see cref="ValidationResult"/> / <see cref="ValidationResult{T}"/> — the "produce" side of
/// the multi-error shape <see cref="ValidationResult"/> already models.
/// </summary>
/// <remarks>
/// <para>
/// Every overload evaluates every input — there is no short-circuit on the first failure. When every
/// input succeeds, the aggregate is a successful <see cref="ValidationResult"/> /
/// <see cref="ValidationResult{T}"/>. When one or more inputs fail, the aggregate is a failed
/// <see cref="ValidationResult"/> / <see cref="ValidationResult{T}"/> carrying every failing
/// <see cref="Error"/> — not just the first — in input order.
/// </para>
/// <para>
/// Use this instead of manually appending to a <c>List&lt;Error&gt;</c> across several independent
/// <see cref="Result"/>/<see cref="Result{T}"/>-returning checks (e.g., validating several independent
/// fields of a command, each via its own small check that returns a <see cref="Result"/>).
/// </para>
/// </remarks>
public static class ResultCombine
{
    /// <summary>
    /// Combines a batch of non-generic <see cref="Result"/> outcomes into a single
    /// <see cref="ValidationResult"/>. Every input is evaluated; no short-circuit on the first failure.
    /// </summary>
    /// <param name="results">The results to combine.</param>
    /// <returns>
    /// <see cref="ValidationResult.Success()"/> if every input succeeded; otherwise
    /// <see cref="ValidationResult.Failure(IReadOnlyList{Error})"/> carrying every failing
    /// <see cref="Error"/>, in input order.
    /// </returns>
    public static ValidationResult Combine(params Result[] results)
        => Combine((IEnumerable<Result>)results);

    /// <summary>
    /// Combines a batch of non-generic <see cref="Result"/> outcomes into a single
    /// <see cref="ValidationResult"/>. Every input is evaluated; no short-circuit on the first failure.
    /// </summary>
    /// <param name="results">The results to combine.</param>
    /// <returns>
    /// <see cref="ValidationResult.Success()"/> if every input succeeded; otherwise
    /// <see cref="ValidationResult.Failure(IReadOnlyList{Error})"/> carrying every failing
    /// <see cref="Error"/>, in input order.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="results"/> is <c>null</c>.</exception>
    public static ValidationResult Combine(IEnumerable<Result> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<Error>? errors = null;
        foreach (var result in results)
        {
            if (result.IsFailure)
            {
                errors ??= [];
                errors.Add(result.Error);
            }
        }

        return errors is null
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    /// <summary>
    /// Combines a batch of <see cref="Result{T}"/> outcomes into a single value-collecting
    /// <see cref="ValidationResult{T}"/>. Every input is evaluated; no short-circuit on the first failure.
    /// </summary>
    /// <typeparam name="T">The success value type common to every input.</typeparam>
    /// <param name="results">The results to combine.</param>
    /// <returns>
    /// <see cref="ValidationResult{T}.Success(T)"/> carrying every success value in input order (as
    /// <see cref="IReadOnlyList{T}"/>) if every input succeeded; otherwise
    /// <see cref="ValidationResult{T}.Failure(IReadOnlyList{Error})"/> carrying every failing
    /// <see cref="Error"/>, in input order.
    /// </returns>
    public static ValidationResult<IReadOnlyList<T>> Combine<T>(params Result<T>[] results)
        => Combine((IEnumerable<Result<T>>)results);

    /// <summary>
    /// Combines a batch of <see cref="Result{T}"/> outcomes into a single value-collecting
    /// <see cref="ValidationResult{T}"/>. Every input is evaluated; no short-circuit on the first failure.
    /// </summary>
    /// <typeparam name="T">The success value type common to every input.</typeparam>
    /// <param name="results">The results to combine.</param>
    /// <returns>
    /// <see cref="ValidationResult{T}.Success(T)"/> carrying every success value in input order (as
    /// <see cref="IReadOnlyList{T}"/>) if every input succeeded; otherwise
    /// <see cref="ValidationResult{T}.Failure(IReadOnlyList{Error})"/> carrying every failing
    /// <see cref="Error"/>, in input order.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="results"/> is <c>null</c>.</exception>
    public static ValidationResult<IReadOnlyList<T>> Combine<T>(IEnumerable<Result<T>> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<Error>? errors = null;
        var values = new List<T>();

        foreach (var result in results)
        {
            if (result.IsSuccess)
            {
                values.Add(result.Value);
            }
            else
            {
                errors ??= [];
                errors.Add(result.Error);
            }
        }

        return errors is null
            ? ValidationResult<IReadOnlyList<T>>.Success(values)
            : ValidationResult<IReadOnlyList<T>>.Failure(errors);
    }
}
